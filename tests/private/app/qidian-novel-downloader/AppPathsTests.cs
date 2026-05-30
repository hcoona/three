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

        Assert.EndsWith(
            "1045928363_Title_Name_Author_Name.md",
            outputPath,
            StringComparison.Ordinal);
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

    [Fact]
    public void EnsureStorageRejectsBrowserProfileRootUnderReparseAncestor()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outsideRoot = Path.Combine(root, "outside");
        string linkRoot = Path.Combine(root, "linked-profile-root");
        try
        {
            Directory.CreateDirectory(outsideRoot);
            if (!CanCreateDirectorySymbolicLink(root))
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    "Symbolic link creation is unavailable; reparse-point coverage skipped.");
            }

            Directory.CreateSymbolicLink(linkRoot, outsideRoot);
            AppStorageService storageService = new();

            Assert.Throws<IOException>(
                () => storageService.EnsureStorage(
                    new AppSettings
                    {
                        BrowserProfileDir = Path.Combine(linkRoot, "profile"),
                        OutputDir = Path.Combine(root, "output"),
                    }));

            Assert.False(Directory.Exists(Path.Combine(outsideRoot, "profile")));
        }
        finally
        {
            if (Directory.Exists(linkRoot)
                && (File.GetAttributes(linkRoot) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(linkRoot);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool CanCreateDirectorySymbolicLink(string root)
    {
        string target = Path.Combine(root, "symlink-target");
        string link = Path.Combine(root, "symlink-link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Directory.Delete(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }
    }
}
