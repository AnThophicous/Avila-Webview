namespace Avila.Security;

public static class SafePath
{
    public static string ResolveInside(string rootPath, string relativePath)
    {
        if (!ManifestLoader.IsSafeRelativePath(relativePath))
        {
            throw new UnauthorizedAccessException("Path must be a safe relative path.");
        }

        var root = Path.GetFullPath(rootPath);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!resolved.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)
            && !resolved.Equals(root, comparison))
        {
            throw new UnauthorizedAccessException("Path escapes the project sandbox.");
        }

        EnsureNoReparsePoints(root, resolved);
        return resolved;
    }

    private static void EnsureNoReparsePoints(string root, string resolved)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.GetFullPath(root).Equals(Path.GetFullPath(resolved), comparison))
        {
            ThrowIfReparsePoint(root);
            return;
        }

        ThrowIfReparsePoint(root);

        var relative = Path.GetRelativePath(root, resolved);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) || File.Exists(current))
            {
                ThrowIfReparsePoint(current);
            }
        }
    }

    private static void ThrowIfReparsePoint(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("Path traverses a reparse point.");
        }
    }
}
