using Avila.Core;
using Xunit;

namespace Avila.Tests;

public sealed class VersionateTests
{
    [Fact]
    public void VersionateFileMatchesReleaseMarker()
    {
        var versionPath = FindVersionatePath();
        var versionText = File.ReadAllText(versionPath).Trim();

        Assert.Equal(Versionate.CurrentRelease, versionText);
    }

    private static string FindVersionatePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, Versionate.FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate Versionate.txt.");
    }
}
