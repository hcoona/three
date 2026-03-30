using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class ChromiumProfilePathResolverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveUsesDedicatedProfileByDefault()
    {
        string defaultUserDataDir = Path.Combine(root, "browser-profile");

        ChromiumProfilePaths resolved = ChromiumProfilePathResolver.Resolve(
            defaultUserDataDir,
            configuredPath: null);

        Assert.Equal(Path.GetFullPath(defaultUserDataDir), resolved.UserDataDir);
        Assert.Null(resolved.ProfileDirectory);
        Assert.False(resolved.IsOverride);
        Assert.Equal(Path.GetFullPath(defaultUserDataDir), resolved.EffectiveProfilePath);
    }

    [Fact]
    public void ResolveTranslatesExistingChromiumProfileDirectoryToUserDataRoot()
    {
        string userDataDir = Path.Combine(root, "Edge", "User Data");
        string profileDir = Path.Combine(userDataDir, "Profile 7");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(userDataDir, "Local State"), "{}");
        File.WriteAllText(Path.Combine(profileDir, "Preferences"), "{}");

        ChromiumProfilePaths resolved = ChromiumProfilePathResolver.Resolve(
            Path.Combine(root, "browser-profile"),
            profileDir);

        Assert.Equal(Path.GetFullPath(userDataDir), resolved.UserDataDir);
        Assert.Equal("Profile 7", resolved.ProfileDirectory);
        Assert.True(resolved.IsOverride);
        Assert.Equal(Path.GetFullPath(profileDir), resolved.EffectiveProfilePath);
    }

    [Fact]
    public void ResolveKeepsUserDataRootOverrideWhenPathIsNotProfileSubdirectory()
    {
        string userDataDir = Path.Combine(root, "Chrome", "User Data");
        Directory.CreateDirectory(userDataDir);
        File.WriteAllText(Path.Combine(userDataDir, "Local State"), "{}");

        ChromiumProfilePaths resolved = ChromiumProfilePathResolver.Resolve(
            Path.Combine(root, "browser-profile"),
            userDataDir);

        Assert.Equal(Path.GetFullPath(userDataDir), resolved.UserDataDir);
        Assert.Null(resolved.ProfileDirectory);
        Assert.True(resolved.IsOverride);
        Assert.Equal(Path.GetFullPath(userDataDir), resolved.EffectiveProfilePath);
    }

    [Fact]
    public void BuildLaunchArgumentsAddsProfileDirectoryWhenProvided()
    {
        string[] arguments = ChromiumProfilePathResolver.BuildLaunchArguments(
            ["--disable-blink-features=AutomationControlled"],
            "Default");

        Assert.Equal(
            [
                "--disable-blink-features=AutomationControlled",
                "--profile-directory=Default",
            ],
            arguments);
    }

    [Theory]
    [InlineData("Failed to create a ProcessSingleton for your profile directory.")]
    [InlineData("Opening in existing browser session.")]
    [InlineData("Browser closed unexpectedly (exit code: 21)")]
    public void IsLikelyLockConflictRecognizesChromiumProfileLockFailures(string message)
    {
        Assert.True(ChromiumProfilePathResolver.IsLikelyLockConflict(new InvalidOperationException(message)));
    }

    [Fact]
    public void IsLikelyLockConflictIgnoresUnrelatedFailures()
    {
        Assert.False(ChromiumProfilePathResolver.IsLikelyLockConflict(new InvalidOperationException("Browser executable not found.")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
