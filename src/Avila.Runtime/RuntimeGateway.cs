using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avila.Core;
using Avila.Bridge;
using Avila.Security;

namespace Avila.Runtime;

public sealed class RuntimeGateway : IRuntimeGateway
{
    private readonly AvilaProject _project;
    private readonly RuntimeOptions _options;
    private readonly string _logDirectory;

    public RuntimeGateway(AvilaProject project, RuntimeOptions options, string logDirectory)
    {
        _project = project;
        _options = options;
        _logDirectory = logDirectory;
    }

    public IReadOnlyList<string> GetArgs() => _options.Arguments;

    public string ProtectSecret(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectData(bytes));
    }

    public string UnprotectSecret(string protectedValue)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Secret payload is not valid.");
        }

        return Encoding.UTF8.GetString(UnprotectData(bytes));
    }

    public string GetPath(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var appId = ToSafePathSegment(_project.Manifest.App.Id);
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            EngineIdentity.AppDataRoot,
            appId);
        var roamingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            EngineIdentity.AppDataRoot,
            appId);

        var path = normalized switch
        {
            "app" => _project.RootPath,
            "data" => Path.Combine(localRoot, "data"),
            "config" => Path.Combine(roamingRoot, "config"),
            "cache" => Path.Combine(localRoot, "cache"),
            "downloads" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"),
            "logs" => _logDirectory,
            "temp" => Path.Combine(Path.GetTempPath(), EngineIdentity.AppDataRoot, appId),
            _ => throw new ArgumentOutOfRangeException(nameof(name), "Unknown Avila path.")
        };

        if (normalized is not ("app" or "downloads"))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    public Task QuitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Application.Exit();
        return Task.CompletedTask;
    }

    public Task RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException("Could not resolve the current runtime executable.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
            Arguments = string.Join(" ", _options.Arguments.Select(QuoteArgument))
        });

        Application.Exit();
        return Task.CompletedTask;
    }

    public Task OpenExternalAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    public Task RevealPathAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return Task.CompletedTask;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return Task.CompletedTask;
        }

        throw new FileNotFoundException("Path was not found.", path);
    }

    private static string ToSafePathSegment(string value)
    {
        var chars = Path.GetInvalidFileNameChars();
        var safe = string.Join("_", value.Split(chars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safe) ? "app" : safe;
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private static byte[] ProtectData(byte[] bytes)
    {
        return ProtectOrUnprotect(bytes, protect: true);
    }

    private static byte[] UnprotectData(byte[] bytes)
    {
        return ProtectOrUnprotect(bytes, protect: false);
    }

    private static byte[] ProtectOrUnprotect(byte[] bytes, bool protect)
    {
        var input = new DataBlob();
        var output = new DataBlob();

        try
        {
            input = CreateBlob(bytes);
            var ok = protect
                ? CryptProtectData(ref input, "Avila secret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output);

            if (!ok)
            {
                throw new InvalidOperationException($"DPAPI failed with Win32 error {Marshal.GetLastWin32Error()}.");
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.Data);
            }

            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var blob = new DataBlob
        {
            Size = bytes.Length,
            Data = Marshal.AllocHGlobal(bytes.Length)
        };
        Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
