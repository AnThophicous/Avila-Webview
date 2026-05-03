namespace Avila.Core;

public static class Versionate
{
    public const string FileName = "Versionate.txt";
    public const string CurrentRelease = "26.0 Release";

    public static string ResolveText(params string?[] roots)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var directory = Path.GetFullPath(root);
            if (!Directory.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory) ?? directory;
            }

            var found = FindFileUpwards(directory, FileName);
            if (found is not null)
            {
                var text = File.ReadAllText(found).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return CurrentRelease;
    }

    private static string? FindFileUpwards(string startDirectory, string fileName)
    {
        var directory = new DirectoryInfo(startDirectory);
        if (!directory.Exists)
        {
            directory = directory.Parent ?? directory;
        }

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
