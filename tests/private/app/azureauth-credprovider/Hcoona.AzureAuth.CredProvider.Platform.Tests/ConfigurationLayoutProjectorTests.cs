using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationLayoutProjectorTests
{
    [Fact]
    public void ProjectsPlatformProductRootsUsingGoldenConventions()
    {
        ConfigurationTargetLayoutProjection windows =
            ConfigurationLayoutProjector.ProjectKeyringShim(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Windows,
                    HomeDirectory = @"C:\Users\alice",
                    LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
                }
            );
        ConfigurationTargetLayoutProjection linux = ConfigurationLayoutProjector.ProjectKeyringShim(
            new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.Linux,
                HomeDirectory = "/home/alice",
                XdgDataHomeDirectory = "/home/alice/.local/share",
                XdgConfigHomeDirectory = "/home/alice/.config",
            }
        );
        ConfigurationTargetLayoutProjection macOs = ConfigurationLayoutProjector.ProjectKeyringShim(
            new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.MacOs,
                HomeDirectory = "/Users/alice",
            }
        );

        Assert.Equal(
            @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider",
            windows.ProductDataRoot
        );
        Assert.Equal(
            @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider",
            windows.ProductConfigRoot
        );
        Assert.Equal("/home/alice/.local/share/azureauth-credprovider", linux.ProductDataRoot);
        Assert.Equal("/home/alice/.config/azureauth-credprovider", linux.ProductConfigRoot);
        Assert.Equal(
            "/Users/alice/Library/Application Support/AzureAuth/CredProvider",
            macOs.ProductDataRoot
        );
        Assert.Equal(
            "/Users/alice/Library/Application Support/AzureAuth/CredProvider",
            macOs.ProductConfigRoot
        );
        Assert.Equal(
            "/home/alice/.local/share/azureauth-credprovider/keyring-shim/keyring",
            linux.TargetPath
        );
        Assert.Equal(
            "/Users/alice/Library/Application Support/AzureAuth/CredProvider/keyring-shim/keyring",
            macOs.TargetPath
        );
        Assert.False(linux.TargetPath.EndsWith(".exe", StringComparison.Ordinal));
        Assert.False(macOs.TargetPath.EndsWith(".exe", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        (int)ConfigurationLayoutPlatform.Linux,
        "/home/alice",
        null,
        "/home/alice/.config/git/config",
        "/home/alice/.gitconfig"
    )]
    [InlineData(
        (int)ConfigurationLayoutPlatform.MacOs,
        "/Users/alice",
        null,
        "/Users/alice/.config/git/config",
        "/Users/alice/.gitconfig"
    )]
    [InlineData(
        (int)ConfigurationLayoutPlatform.Windows,
        @"C:\Users\alice",
        @"C:\Users\alice\AppData\Local",
        @"C:\Users\alice\.config\git\config",
        @"C:\Users\alice\.gitconfig"
    )]
    public void GitConfigSelectsHomeGitconfigBeforeExistingXdgConfig(
        int platformValue,
        string homeDirectory,
        string? localAppDataDirectory,
        string xdgGitConfig,
        string homeGitConfig
    )
    {
        var platform = (ConfigurationLayoutPlatform)platformValue;
        ConfigurationTargetLayoutProjection existingXdgOnly =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = platform,
                    HomeDirectory = homeDirectory,
                    LocalAppDataDirectory = localAppDataDirectory,
                    FileExists = path =>
                        string.Equals(path, xdgGitConfig, StringComparison.Ordinal),
                }
            );
        ConfigurationTargetLayoutProjection existingHomeAndXdg =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = platform,
                    HomeDirectory = homeDirectory,
                    LocalAppDataDirectory = localAppDataDirectory,
                    FileExists = path =>
                        string.Equals(path, xdgGitConfig, StringComparison.Ordinal)
                        || string.Equals(path, homeGitConfig, StringComparison.Ordinal),
                }
            );
        ConfigurationTargetLayoutProjection missingXdg =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = platform,
                    HomeDirectory = homeDirectory,
                    LocalAppDataDirectory = localAppDataDirectory,
                    FileExists = _ => false,
                }
            );

        Assert.Equal(xdgGitConfig, existingXdgOnly.TargetPath);
        Assert.Equal(homeGitConfig, existingHomeAndXdg.TargetPath);
        Assert.Equal(homeGitConfig, missingXdg.TargetPath);
        Assert.Contains(
            existingHomeAndXdg.ActivationGuidance,
            guidance => guidance.Contains("file-level", StringComparison.OrdinalIgnoreCase)
                && guidance.Contains("do not invoke the git config CLI", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void MacOsGitConfigSelectsHomeGitconfigOverCustomXdgConfigHome()
    {
        const string xdgGitConfig = "/custom/xdg/git/config";
        ConfigurationTargetLayoutProjection projection =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.MacOs,
                    HomeDirectory = "/Users/alice",
                    XdgConfigHomeDirectory = "/custom/xdg",
                    FileExists = path =>
                        string.Equals(path, xdgGitConfig, StringComparison.Ordinal)
                        || string.Equals(path, "/Users/alice/.gitconfig", StringComparison.Ordinal),
                }
            );

        Assert.Equal("/Users/alice/.gitconfig", projection.TargetPath);
    }

    [Theory]
    [InlineData(null, "/.config/git/config")]
    [InlineData("/.config", "/.config/git/config")]
    [InlineData("/", "/git/config")]
    public void PosixRootGitConfigProjectionUsesSingleLeadingSlashPaths(
        string? xdgConfigHomeDirectory,
        string expectedXdgGitConfig
    )
    {
        ConfigurationTargetLayoutProjection existingXdgOnly =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Linux,
                    HomeDirectory = "/",
                    XdgConfigHomeDirectory = xdgConfigHomeDirectory,
                    FileExists = path =>
                        string.Equals(path, expectedXdgGitConfig, StringComparison.Ordinal),
                }
            );
        ConfigurationTargetLayoutProjection missingXdg =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Linux,
                    HomeDirectory = "/",
                    XdgConfigHomeDirectory = xdgConfigHomeDirectory,
                    FileExists = _ => false,
                }
            );

        Assert.Equal(expectedXdgGitConfig, existingXdgOnly.TargetPath);
        Assert.Equal("/.gitconfig", missingXdg.TargetPath);
        Assert.DoesNotContain(
            existingXdgOnly.ProjectedPaths,
            path => path.StartsWith("//", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            missingXdg.ProjectedPaths,
            path => path.StartsWith("//", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void WindowsGitConfigHonorsCustomXdgConfigHome()
    {
        const string xdgGitConfig = @"D:\git-xdg\git\config";
        ConfigurationTargetLayoutProjection projection =
            ConfigurationLayoutProjector.ProjectGitConfig(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Windows,
                    HomeDirectory = @"C:\Users\alice",
                    LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
                    XdgConfigHomeDirectory = @"D:\git-xdg",
                    FileExists = path =>
                        string.Equals(path, xdgGitConfig, StringComparison.Ordinal),
                }
            );

        Assert.Equal(xdgGitConfig, projection.TargetPath);
    }

    [Fact]
    public void ProjectionRejectsEmptyHomeDirectory()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Linux,
                    HomeDirectory = " ",
                }
            )
        );

        Assert.Equal("HomeDirectory", exception.ParamName);
    }

    [Theory]
    [InlineData(
        (int)ConfigurationLayoutPlatform.Windows,
        @"C:\Users\alice",
        @"C:\Users\alice\AppData\Local",
        null,
        null,
        @"C:\Users\alice\.nuget\plugins\netcore\azureauth-credprovider",
        @"C:\Users\alice\.nuget\plugins\netcore\azureauth-credprovider\azureauth-credprovider.dll",
        @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider"
    )]
    [InlineData(
        (int)ConfigurationLayoutPlatform.Linux,
        "/home/alice",
        null,
        "/home/alice/.local/share",
        "/home/alice/.config",
        "/home/alice/.nuget/plugins/netcore/azureauth-credprovider",
        "/home/alice/.nuget/plugins/netcore/azureauth-credprovider/azureauth-credprovider.dll",
        "/home/alice/.config/azureauth-credprovider"
    )]
    [InlineData(
        (int)ConfigurationLayoutPlatform.MacOs,
        "/Users/alice",
        null,
        null,
        null,
        "/Users/alice/.nuget/plugins/netcore/azureauth-credprovider",
        "/Users/alice/.nuget/plugins/netcore/azureauth-credprovider/azureauth-credprovider.dll",
        "/Users/alice/Library/Application Support/AzureAuth/CredProvider"
    )]
    public void NuGetProjectionUsesOfficialConventionWithoutPersistentPluginPathMutation(
        int platformValue,
        string homeDirectory,
        string? localAppDataDirectory,
        string? xdgDataHomeDirectory,
        string? xdgConfigHomeDirectory,
        string expectedTargetPath,
        string expectedEntrypointPath,
        string expectedProductConfigRoot
    )
    {
        var platform = (ConfigurationLayoutPlatform)platformValue;
        ConfigurationTargetLayoutProjection projection =
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = platform,
                    HomeDirectory = homeDirectory,
                    LocalAppDataDirectory = localAppDataDirectory,
                    XdgDataHomeDirectory = xdgDataHomeDirectory,
                    XdgConfigHomeDirectory = xdgConfigHomeDirectory,
                }
            );

        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, projection.TargetKind);
        Assert.Equal(expectedTargetPath, projection.TargetPath);
        Assert.Equal(expectedProductConfigRoot, projection.ProductConfigRoot);
        Assert.Contains(expectedTargetPath, projection.ProjectedPaths, StringComparer.Ordinal);
        Assert.Contains(expectedEntrypointPath, projection.ProjectedPaths, StringComparer.Ordinal);
        Assert.Contains(
            GetParentPath(GetParentPath(expectedTargetPath)),
            projection.ProjectedPaths,
            StringComparer.Ordinal
        );
        Assert.Contains(
            Combine(platform, expectedProductConfigRoot, "nuget-plugin", "manifest.json"),
            projection.ProjectedPaths,
            StringComparer.Ordinal
        );
        Assert.Contains(
            "NUGET_PLUGIN_PATHS",
            projection.OptionalProcessEnvironmentVariables,
            StringComparer.Ordinal
        );
        Assert.Contains(
            "NUGET_NETCORE_PLUGIN_PATHS",
            projection.OptionalProcessEnvironmentVariables,
            StringComparer.Ordinal
        );
        Assert.Contains(
            projection.ActivationGuidance,
            guidance =>
                guidance.Contains("official per-user plugin convention", StringComparison.Ordinal)
                && guidance.Contains(expectedEntrypointPath, StringComparison.Ordinal)
        );
        Assert.Empty(projection.PersistentEnvironmentMutations);
        Assert.DoesNotContain(
            projection.PersistentEnvironmentMutations.Keys,
            variable =>
                string.Equals(variable, "NUGET_PLUGIN_PATHS", StringComparison.Ordinal)
                || string.Equals(variable, "NUGET_NETCORE_PLUGIN_PATHS", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(
        (int)ConfigurationLayoutPlatform.Windows,
        @"C:\Users\alice",
        @"C:\Users\alice\AppData\Local",
        null,
        null,
        @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider",
        @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider\python-keyring\backend-manifest.json"
    )]
    [InlineData(
        (int)ConfigurationLayoutPlatform.Linux,
        "/home/alice",
        null,
        "/home/alice/.local/share",
        "/home/alice/.config",
        "/home/alice/.config/azureauth-credprovider",
        "/home/alice/.config/azureauth-credprovider/python-keyring/backend-manifest.json"
    )]
    [InlineData(
        (int)ConfigurationLayoutPlatform.MacOs,
        "/Users/alice",
        null,
        null,
        null,
        "/Users/alice/Library/Application Support/AzureAuth/CredProvider",
        "/Users/alice/Library/Application Support/AzureAuth/CredProvider/"
            + "python-keyring/backend-manifest.json"
    )]
    public void PythonKeyringProjectionWritesOnlyProductOwnedManifest(
        int platformValue,
        string homeDirectory,
        string? localAppDataDirectory,
        string? xdgDataHomeDirectory,
        string? xdgConfigHomeDirectory,
        string expectedProductConfigRoot,
        string expectedTargetPath
    )
    {
        var platform = (ConfigurationLayoutPlatform)platformValue;
        ConfigurationTargetLayoutProjection projection =
            ConfigurationLayoutProjector.ProjectPythonKeyringBackend(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = platform,
                    HomeDirectory = homeDirectory,
                    LocalAppDataDirectory = localAppDataDirectory,
                    XdgDataHomeDirectory = xdgDataHomeDirectory,
                    XdgConfigHomeDirectory = xdgConfigHomeDirectory,
                }
            );

        Assert.Equal(ConfigurationTargetKind.PythonKeyringBackend, projection.TargetKind);
        Assert.Equal(expectedProductConfigRoot, projection.ProductConfigRoot);
        Assert.Equal(expectedTargetPath, projection.TargetPath);
        Assert.Equal([expectedTargetPath], projection.ProjectedPaths);
        Assert.DoesNotContain(
            projection.ProjectedPaths,
            path => path.EndsWith("keyringrc.cfg", StringComparison.Ordinal)
        );
        Assert.Contains(
            projection.ActivationGuidance,
            guidance =>
                guidance.Contains("Do not write the user's keyringrc.cfg", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            projection.ActivationGuidance,
            guidance => guidance.Contains("by default", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void RootPosixInputsProjectSingleLeadingSlashPaths()
    {
        var context = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = "/",
            XdgDataHomeDirectory = "/",
            XdgConfigHomeDirectory = "/",
        };

        ConfigurationTargetLayoutProjection nuget =
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(context);
        ConfigurationTargetLayoutProjection python =
            ConfigurationLayoutProjector.ProjectPythonKeyringBackend(context);
        ConfigurationTargetLayoutProjection keyring =
            ConfigurationLayoutProjector.ProjectKeyringShim(context);
        ConfigurationTargetLayoutProjection gitConfig =
            ConfigurationLayoutProjector.ProjectGitConfig(context);

        Assert.Equal("/.nuget/plugins/netcore/azureauth-credprovider", nuget.TargetPath);
        Assert.Equal(
            "/azureauth-credprovider/python-keyring/backend-manifest.json",
            python.TargetPath
        );
        Assert.Equal("/azureauth-credprovider/keyring-shim/keyring", keyring.TargetPath);
        Assert.Equal("/.gitconfig", gitConfig.TargetPath);

        Assert.False(nuget.TargetPath.StartsWith("//", StringComparison.Ordinal));
        Assert.False(python.TargetPath.StartsWith("//", StringComparison.Ordinal));
        Assert.False(keyring.TargetPath.StartsWith("//", StringComparison.Ordinal));
        Assert.False(gitConfig.TargetPath.StartsWith("//", StringComparison.Ordinal));
        Assert.DoesNotContain(
            nuget.ProjectedPaths,
            path => path.StartsWith("//", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            python.ProjectedPaths,
            path => path.StartsWith("//", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            keyring.ProjectedPaths,
            path => path.StartsWith("//", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            gitConfig.ProjectedPaths,
            path => path.StartsWith("//", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void KeyringShimProjectionInstallsWindowsKeyringExeWithoutGlobalPathMutation()
    {
        ConfigurationTargetLayoutProjection projection =
            ConfigurationLayoutProjector.ProjectKeyringShim(
                new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Windows,
                    HomeDirectory = @"C:\Users\alice",
                    LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
                }
            );

        Assert.Equal(ConfigurationTargetKind.KeyringShim, projection.TargetKind);
        Assert.Equal(
            @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider\keyring-shim\keyring.exe",
            projection.TargetPath
        );
        Assert.Empty(projection.PersistentEnvironmentMutations);
        Assert.Empty(projection.PersistentProfileOrRegistryMutations);
        Assert.Contains(
            projection.ActivationGuidance,
            guidance => guidance.Contains("PATH", StringComparison.Ordinal)
        );
        Assert.Contains(
            projection.ActivationGuidance,
            guidance => guidance.Contains("Do not mutate", StringComparison.Ordinal)
                && guidance.Contains("shell profiles", StringComparison.Ordinal)
                && guidance.Contains("registry", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            projection.ActivationGuidance,
            guidance => guidance.Contains("mutate", StringComparison.OrdinalIgnoreCase)
                && !guidance.Contains("Do not", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static string GetParentPath(string path)
    {
        int separatorIndex = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return separatorIndex < 0 ? path
            : separatorIndex == 0 ? "/"
            : path[..separatorIndex];
    }

    private static string Combine(
        ConfigurationLayoutPlatform platform,
        string firstSegment,
        params string[] additionalSegments
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string result = firstSegment.TrimEnd(separator, AlternateSeparator(separator));
        foreach (string segment in additionalSegments)
        {
            result = string.Concat(
                result,
                separator,
                segment.Trim(separator, AlternateSeparator(separator))
            );
        }

        return result;
    }

    private static char AlternateSeparator(char separator) => separator == '\\' ? '/' : '\\';
}
