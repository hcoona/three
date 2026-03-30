using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void BuildDefaultOutputPathSanitizesMetadata()
    {
        string outputPath = AppPaths.BuildDefaultOutputPath(
            Path.Combine(Path.GetTempPath(), "output-root"),
            "1045928363",
            "Title:Name",
            "Author/Name");

        Assert.EndsWith("1045928363_Title_Name_Author_Name.md", outputPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveTranslatesChromiumProfileSubdirectoryOverride()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string userDataDir = Path.Combine(root, "Edge", "User Data");
            string profileDir = Path.Combine(userDataDir, "Default");
            Directory.CreateDirectory(profileDir);
            File.WriteAllText(Path.Combine(userDataDir, "Local State"), "{}");
            File.WriteAllText(Path.Combine(profileDir, "Preferences"), "{}");

            AppStoragePaths paths = AppPaths.Resolve(
                new AppSettings
                {
                    BrowserProfileDir = profileDir,
                });

            Assert.Equal(Path.GetFullPath(userDataDir), paths.BrowserProfileRoot);
            Assert.Equal("Default", paths.BrowserProfileDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
