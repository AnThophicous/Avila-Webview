using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avila.Security;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private static readonly HashSet<string> KnownFileRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "data",
        "config",
        "cache",
        "downloads",
        "temp"
    };

    private void RegisterFileSystemCommands()
    {
        Register("fs.readText", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            await EnsureFileSizeAsync(path, context.Project.Manifest.FileSystem.MaxReadBytes, token).ConfigureAwait(false);
            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8, workerToken).ConfigureAwait(false);
                return new { content };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.writeText", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            var content = GetStringAny(request.Payload, ["content", "text"], CheckedMaxLength(context.Project.Manifest.FileSystem.MaxWriteBytes));
            if (PayloadReader.Utf8Length(content) > context.Project.Manifest.FileSystem.MaxWriteBytes)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Text is larger than fs.maxWriteBytes.");
            }

            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                await WriteBytesAsync(path, Encoding.UTF8.GetBytes(content), context.Project.Manifest.FileSystem.AtomicWrites, workerToken).ConfigureAwait(false);
                return new { written = true };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.readFile", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            await EnsureFileSizeAsync(path, context.Project.Manifest.FileSystem.MaxReadBytes, token).ConfigureAwait(false);
            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8, workerToken).ConfigureAwait(false);
                return new { content };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.writeFile", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            var content = GetStringAny(request.Payload, ["content", "text"], CheckedMaxLength(context.Project.Manifest.FileSystem.MaxWriteBytes));
            if (PayloadReader.Utf8Length(content) > context.Project.Manifest.FileSystem.MaxWriteBytes)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Text is larger than fs.maxWriteBytes.");
            }

            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                await WriteBytesAsync(path, Encoding.UTF8.GetBytes(content), context.Project.Manifest.FileSystem.AtomicWrites, workerToken).ConfigureAwait(false);
                return new { written = true };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.readBytes", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            await EnsureFileSizeAsync(path, context.Project.Manifest.FileSystem.MaxReadBytes, token).ConfigureAwait(false);
            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                var bytes = await File.ReadAllBytesAsync(path, workerToken).ConfigureAwait(false);
                return new
                {
                    base64 = Convert.ToBase64String(bytes),
                    size = bytes.LongLength
                };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.writeBytes", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            var base64 = GetStringAny(request.Payload, ["base64", "content"], CheckedMaxLength(context.Project.Manifest.FileSystem.MaxWriteBytes * 2));
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.base64 must be valid base64.");
            }

            if (bytes.LongLength > context.Project.Manifest.FileSystem.MaxWriteBytes)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Bytes are larger than fs.maxWriteBytes.");
            }

            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                await WriteBytesAsync(path, bytes, context.Project.Manifest.FileSystem.AtomicWrites, workerToken).ConfigureAwait(false);
                return new { written = true, size = bytes.LongLength };
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.exists", (context, request, _) =>
        {
            var path = ResolveFilePath(context, request.Payload, allowMissing: true);
            return Task.FromResult<object?>(new
            {
                exists = File.Exists(path) || Directory.Exists(path)
            });
        });

        Register("fs.stat", (context, request, _) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            return Task.FromResult<object?>(CreateStat(path));
        });

        Register("fs.listDir", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            if (!Directory.Exists(path))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Directory was not found.");
            }

            return await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                var entries = Directory.EnumerateFileSystemEntries(path)
                    .Take(4096)
                    .Select(CreateStat)
                    .ToArray();
                return Task.FromResult<object?>(new { entries });
            }, cancellationToken: token).ConfigureAwait(false);
        });

        Register("fs.createDir", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload, allowMissing: true);
            await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(path);
                return Task.FromResult<object?>(new { created = true });
            }, cancellationToken: token).ConfigureAwait(false);
            return new { created = true };
        });

        Register("fs.removeFile", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                if (!File.Exists(path))
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, "File was not found.");
                }

                File.Delete(path);
                return Task.FromResult<object?>(new { removed = true });
            }, cancellationToken: token).ConfigureAwait(false);
            return new { removed = true };
        });

        Register("fs.removeDir", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            var recursive = PayloadReader.GetOptionalBoolean(request.Payload, "recursive", false);
            await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(path))
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Directory was not found.");
                }

                Directory.Delete(path, recursive);
                return Task.FromResult<object?>(new { removed = true });
            }, cancellationToken: token).ConfigureAwait(false);
            return new { removed = true };
        });

        Register("fs.copy", async (context, request, token) =>
        {
            var from = ResolveFilePath(context, request.Payload, pathProperty: "from");
            var to = ResolveFilePath(context, request.Payload, allowMissing: true, pathProperty: "to", rootProperty: "toRoot");
            var overwrite = PayloadReader.GetOptionalBoolean(request.Payload, "overwrite", false);
            await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                if (File.Exists(from))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(from, to, overwrite);
                }
                else if (Directory.Exists(from))
                {
                    CopyDirectory(from, to, overwrite);
                }
                else
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Source path was not found.");
                }

                return Task.FromResult<object?>(new { copied = true });
            }, cancellationToken: token).ConfigureAwait(false);
            return new { copied = true };
        });

        Register("fs.move", async (context, request, token) =>
        {
            var from = ResolveFilePath(context, request.Payload, pathProperty: "from");
            var to = ResolveFilePath(context, request.Payload, allowMissing: true, pathProperty: "to", rootProperty: "toRoot");
            var overwrite = PayloadReader.GetOptionalBoolean(request.Payload, "overwrite", false);
            await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (File.Exists(from))
                {
                    File.Move(from, to, overwrite);
                }
                else if (Directory.Exists(from))
                {
                    if (Directory.Exists(to) || File.Exists(to))
                    {
                        throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Destination path already exists.");
                    }

                    Directory.Move(from, to);
                }
                else
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Source path was not found.");
                }

                return Task.FromResult<object?>(new { moved = true });
            }, cancellationToken: token).ConfigureAwait(false);
            return new { moved = true };
        });

        Register("fs.rename", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            var name = PayloadReader.GetString(request.Payload, "name", 180);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.name must be a file or folder name, not a path.");
            }

            var parent = Path.GetDirectoryName(path) ?? throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Path has no parent directory.");
            var target = Path.Combine(parent, name);
            await context.Workers.EnqueueAsync<object?>(workerToken =>
            {
                workerToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                {
                    File.Move(path, target);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Move(path, target);
                }
                else
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Path was not found.");
                }

                return Task.FromResult<object?>(new { renamed = true });
            }, cancellationToken: token).ConfigureAwait(false);
            return new { renamed = true };
        });

        Register("fs.hash", async (context, request, token) =>
        {
            var path = ResolveFilePath(context, request.Payload);
            await EnsureFileSizeAsync(path, context.Project.Manifest.FileSystem.MaxReadBytes, token).ConfigureAwait(false);
            var algorithm = PayloadReader.GetString(request.Payload, "algorithm", 16, required: false);
            if (!string.IsNullOrWhiteSpace(algorithm) && !algorithm.Equals("sha256", StringComparison.OrdinalIgnoreCase) && !algorithm.Equals("SHA-256", StringComparison.OrdinalIgnoreCase))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Only SHA-256 is supported.");
            }

            return await context.Workers.EnqueueAsync<object?>(async workerToken =>
            {
                await using var stream = File.OpenRead(path);
                var hash = await SHA256.HashDataAsync(stream, workerToken).ConfigureAwait(false);
                return new { algorithm = "SHA-256", hash = Convert.ToHexString(hash).ToLowerInvariant() };
            }, cancellationToken: token).ConfigureAwait(false);
        });
    }

    private static string ResolveFilePath(
        BridgeCommandContext context,
        JsonElement payload,
        bool allowMissing = false,
        string pathProperty = "path",
        string rootProperty = "root")
    {
        var root = PayloadReader.GetString(payload, rootProperty, 32, required: false);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = "app";
        }

        if (!KnownFileRoots.Contains(root))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"Unknown file root: {root}");
        }

        if (!context.Project.Manifest.FileSystem.AllowedRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"File root is not allowed by fs.allowedRoots: {root}");
        }

        var relativePath = PayloadReader.GetString(payload, pathProperty, 512, required: false);
        var rootPath = context.Runtime.GetPath(root);
        var resolved = ResolveInsideRoot(rootPath, relativePath);
        if (!allowMissing && !File.Exists(resolved) && !Directory.Exists(resolved))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Path was not found.");
        }

        return resolved;
    }

    private static string ResolveInsideRoot(string rootPath, string relativePath)
    {
        var normalized = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        if (normalized is ".")
        {
            return SafePath.ResolveInside(rootPath, ".");
        }

        try
        {
            return SafePath.ResolveInside(rootPath, normalized);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, exception.Message);
        }
    }

    private static async Task EnsureFileSizeAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "File was not found.");
        }

        if (info.Length > maxBytes)
        {
            throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "File is larger than the configured read limit.");
        }

        await Task.CompletedTask;
    }

    private static async Task WriteBytesAsync(string path, byte[] bytes, bool atomic, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!atomic)
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tempPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static object CreateStat(string path)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return new
            {
                name = info.Name,
                path,
                type = "file",
                size = info.Length,
                createdAt = info.CreationTimeUtc,
                modifiedAt = info.LastWriteTimeUtc,
                mime = GuessMimeType(info.Extension)
            };
        }

        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return new
            {
                name = info.Name,
                path,
                type = "directory",
                size = 0L,
                createdAt = info.CreationTimeUtc,
                modifiedAt = info.LastWriteTimeUtc,
                mime = "inode/directory"
            };
        }

        throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Path was not found.");
    }

    private static void CopyDirectory(string source, string target, bool overwrite)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite);
        }
    }

    private static string GetStringAny(JsonElement payload, string[] names, int maxLength)
    {
        foreach (var name in names)
        {
            if (payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a string.");
                }

                var text = value.GetString() ?? "";
                if (text.Length > maxLength)
                {
                    throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is too long.");
                }

                return text;
            }
        }

        throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{names[0]} is required.");
    }

    private static int CheckedMaxLength(long limit)
    {
        return (int)Math.Min(limit, int.MaxValue);
    }

    private static string GuessMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "text/javascript",
            ".json" => "application/json",
            ".txt" or ".md" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
