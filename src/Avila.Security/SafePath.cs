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

        return resolved;
    }
}
