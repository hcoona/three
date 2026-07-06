using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Installer;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class InstallerDiscoveryScaffoldTests
{
    private static readonly FakeAdapterSurface[] ExpectedSurfaces =
    [
        FakeAdapterSurface.GitHelper,
        FakeAdapterSurface.NuGetNetCorePlugin,
        FakeAdapterSurface.PythonKeyringBackend,
        FakeAdapterSurface.PythonKeyringHelper,
        FakeAdapterSurface.KeyringShim,
    ];
    private static readonly FakeAdapterSurface[] PosixExecutableSurfaces =
    [
        FakeAdapterSurface.GitHelper,
        FakeAdapterSurface.PythonKeyringHelper,
        FakeAdapterSurface.KeyringShim,
    ];
    private static readonly UnixFileMode ExpectedPosixExecutableFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode TrustedPosixDirectoryMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherExecute;
    private const UnixFileMode PermissivePosixDirectoryMode =
        TrustedPosixDirectoryMode | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    [Fact]
    public void FakeAdapterSurfaceEnumContainsOnlyScaffoldedSurfaces()
    {
        AssertExpectedSurfaceSet(Enum.GetValues<FakeAdapterSurface>());
        Assert.DoesNotContain(
            Enum.GetNames<FakeAdapterSurface>(),
            static name =>
                name.Contains("Npm", StringComparison.Ordinal)
                || name.Contains("Yarn", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ProjectPlacementsUsesDeterministicWindowsConventions()
    {
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.Windows,
                HomeDirectory = @"C:\Users\alice",
                LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
            }
        );
        const string productDataRoot = @"C:\Users\alice\AppData\Local\AzureAuth\CredProvider";
        const string nuGetPluginRoot =
            @"C:\Users\alice\.nuget\plugins\netcore\azureauth-credprovider";
        string pythonBackendPlacementRoot = productDataRoot
            + @"\python-environments\fake-environment\Lib\site-packages"
            + @"\azureauth_keyring_backend-1.0.dist-info";

        AssertExpectedSurfaceSet(placements.Keys);
        AssertPlacement(
            placements,
            FakeAdapterSurface.GitHelper,
            productDataRoot + @"\git-helper",
            productDataRoot + @"\git-helper\git-credential-azureauth-credprovider.exe"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.NuGetNetCorePlugin,
            nuGetPluginRoot,
            nuGetPluginRoot + @"\azureauth-credprovider.dll"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.PythonKeyringBackend,
            pythonBackendPlacementRoot,
            pythonBackendPlacementRoot + @"\entry_points.txt"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.PythonKeyringHelper,
            productDataRoot + @"\python-keyring",
            productDataRoot
                + @$"\python-keyring\{KeyringHelperV2.CommandName}.exe"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.KeyringShim,
            productDataRoot + @"\keyring-shim",
            productDataRoot + @"\keyring-shim\keyring.exe"
        );
    }

    [Fact]
    public void ProjectPlacementsUsesDeterministicLinuxConventions()
    {
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.Linux,
                HomeDirectory = "/home/alice",
                XdgDataHomeDirectory = "/home/alice/.local/share",
                XdgConfigHomeDirectory = "/home/alice/.config",
            }
        );
        const string productDataRoot = "/home/alice/.local/share/azureauth-credprovider";
        const string nuGetPluginRoot = "/home/alice/.nuget/plugins/netcore/azureauth-credprovider";
        string pythonBackendPlacementRoot = productDataRoot
            + "/python-environments/fake-environment/lib/site-packages"
            + "/azureauth_keyring_backend-1.0.dist-info";

        AssertExpectedSurfaceSet(placements.Keys);
        AssertPlacement(
            placements,
            FakeAdapterSurface.GitHelper,
            productDataRoot + "/git-helper",
            productDataRoot + "/git-helper/git-credential-azureauth-credprovider"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.NuGetNetCorePlugin,
            nuGetPluginRoot,
            nuGetPluginRoot + "/azureauth-credprovider.dll"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.PythonKeyringBackend,
            pythonBackendPlacementRoot,
            pythonBackendPlacementRoot + "/entry_points.txt"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.PythonKeyringHelper,
            productDataRoot + "/python-keyring",
            productDataRoot + $"/python-keyring/{KeyringHelperV2.CommandName}"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.KeyringShim,
            productDataRoot + "/keyring-shim",
            productDataRoot + "/keyring-shim/keyring"
        );
        Assert.False(
            placements[FakeAdapterSurface.GitHelper].ArtifactPath.EndsWith(
                ".exe",
                StringComparison.Ordinal
            )
        );
        Assert.False(
            placements[FakeAdapterSurface.PythonKeyringHelper].ArtifactPath.EndsWith(
                ".exe",
                StringComparison.Ordinal
            )
        );
        Assert.False(
            placements[FakeAdapterSurface.KeyringShim].ArtifactPath.EndsWith(
                ".exe",
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public void ProjectPlacementsUsesDeterministicMacOsConventions()
    {
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.MacOs,
                HomeDirectory = "/Users/alice",
            }
        );
        const string productDataRoot =
            "/Users/alice/Library/Application Support/AzureAuth/CredProvider";
        const string nuGetPluginRoot = "/Users/alice/.nuget/plugins/netcore/azureauth-credprovider";
        string pythonBackendPlacementRoot = productDataRoot
            + "/python-environments/fake-environment/lib/site-packages"
            + "/azureauth_keyring_backend-1.0.dist-info";

        AssertExpectedSurfaceSet(placements.Keys);
        AssertPlacement(
            placements,
            FakeAdapterSurface.GitHelper,
            productDataRoot + "/git-helper",
            productDataRoot + "/git-helper/git-credential-azureauth-credprovider"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.NuGetNetCorePlugin,
            nuGetPluginRoot,
            nuGetPluginRoot + "/azureauth-credprovider.dll"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.PythonKeyringBackend,
            pythonBackendPlacementRoot,
            pythonBackendPlacementRoot + "/entry_points.txt"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.PythonKeyringHelper,
            productDataRoot + "/python-keyring",
            productDataRoot + $"/python-keyring/{KeyringHelperV2.CommandName}"
        );
        AssertPlacement(
            placements,
            FakeAdapterSurface.KeyringShim,
            productDataRoot + "/keyring-shim",
            productDataRoot + "/keyring-shim/keyring"
        );
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.MacOs))]
    public void MaterializePlacementsThenProbeReturnsFoundForAllSurfaces(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(CreateTopologyAwareDiscoveryContext(layout, fileSystem))
                .ToDictionary(result => result.Surface);

        AssertExpectedSurfaceSet(placements.Keys);
        AssertExpectedSurfaceSet(results.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement placement = placements[surface];
            Assert.True(fileSystem.DirectoryExists(placement.PlacementRoot));
            Assert.True(fileSystem.FileExists(placement.ArtifactPath));
            Assert.Equal(
                GetExpectedMaterializedContents(placement, platform),
                fileSystem.ReadAllText(placement.ArtifactPath)
            );
            AssertResult(
                results,
                surface,
                FakeAdapterProbeStatus.Found,
                FakeAdapterArtifactKind.File
            );
        }

        AssertPosixExecutableModes(fileSystem, placements, platform);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.MacOs))]
    public void MaterializePlacementsUsesDeterministicPathsAndContents(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var firstFileSystem = CreateMaterializationFileSystem(platform);
        var secondFileSystem = CreateMaterializationFileSystem(platform);

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> firstPlacements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = firstFileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> secondPlacements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = secondFileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        AssertExpectedSurfaceSet(firstPlacements.Keys);
        AssertExpectedSurfaceSet(secondPlacements.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement firstPlacement = firstPlacements[surface];
            FakeAdapterPlacement secondPlacement = secondPlacements[surface];

            Assert.Equal(firstPlacement, secondPlacement);
            Assert.Equal(
                firstFileSystem.ReadAllText(firstPlacement.ArtifactPath),
                secondFileSystem.ReadAllText(secondPlacement.ArtifactPath)
            );
            Assert.Equal(
                GetExpectedMaterializedContents(firstPlacement, platform),
                firstFileSystem.ReadAllText(firstPlacement.ArtifactPath)
            );
            Assert.Equal(
                GetExpectedMaterializedContents(secondPlacement, platform),
                secondFileSystem.ReadAllText(secondPlacement.ArtifactPath)
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // editorconfig-checker-disable
    public void MaterializePlacementRejectsMissingArtifactsWhenConditionalFileMutationsUnsupportedBeforeFilesystemMutation(
    // editorconfig-checker-enable
        bool usePlacementOverload
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        fileSystem.SupportsConditionalFileMutations = false;
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            MaterializeSinglePlacement(
                FakeAdapterSurface.GitHelper,
                layout,
                fileSystem,
                usePlacementOverload
            )
        );

        Assert.Contains(
            "conditional file mutation support",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Empty(fileSystem.Files);
        Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementsRejectsMissingArtifactsWhenConditionalFileMutationsUnsupportedBeforeFilesystemMutation()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Windows);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        fileSystem.SupportsConditionalFileMutations = false;
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "conditional file mutation support",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Empty(fileSystem.Files);
        foreach (FakeAdapterPlacement placement in placements.Values)
        {
            Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
    }

    [Fact]
    public void MaterializePlacementsAllowsExistingDeterministicFakeFilesWithoutRewritingArtifacts()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> firstPlacements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);
        fileSystem.Calls.Clear();

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> secondPlacements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        AssertExpectedSurfaceSet(firstPlacements.Keys);
        AssertExpectedSurfaceSet(secondPlacements.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            Assert.Equal(firstPlacements[surface], secondPlacements[surface]);
            Assert.Equal(
                GetExpectedMaterializedContents(
                    secondPlacements[surface],
                    ConfigurationLayoutPlatform.Linux
                ),
                fileSystem.ReadAllText(secondPlacements[surface].ArtifactPath)
            );
        }

        AssertPosixExecutableModes(
            fileSystem,
            secondPlacements,
            ConfigurationLayoutPlatform.Linux
        );
        AssertNoArtifactRewriteCalls(
            fileSystem,
            secondPlacements.Values,
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    public void
    MaterializePlacementsAllowsExistingDeterministicArtifactsWhenConditionalFileMutationsUnsupported
    ()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Windows);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> firstPlacements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);
        fileSystem.Calls.Clear();
        fileSystem.SupportsConditionalFileMutations = false;

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> secondPlacements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        AssertExpectedSurfaceSet(firstPlacements.Keys);
        AssertExpectedSurfaceSet(secondPlacements.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            Assert.Equal(firstPlacements[surface], secondPlacements[surface]);
            Assert.Equal(
                GetExpectedMaterializedContents(
                    secondPlacements[surface],
                    ConfigurationLayoutPlatform.Windows
                ),
                fileSystem.ReadAllText(secondPlacements[surface].ArtifactPath)
            );
        }

        AssertNoMaterializationMutationCalls(fileSystem);
        AssertNoArtifactRewriteCalls(
            fileSystem,
            secondPlacements.Values,
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // editorconfig-checker-disable
    public void MaterializePlacementAllowsExistingDeterministicPosixExecutableWhenConditionalFileMutationsUnsupported(
    // editorconfig-checker-enable
        bool usePlacementOverload
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement firstPlacement = MaterializeSinglePlacement(
            FakeAdapterSurface.GitHelper,
            layout,
            fileSystem,
            usePlacementOverload
        );
        fileSystem.Calls.Clear();
        fileSystem.SupportsConditionalFileMutations = false;

        FakeAdapterPlacement secondPlacement = MaterializeSinglePlacement(
            FakeAdapterSurface.GitHelper,
            layout,
            fileSystem,
            usePlacementOverload
        );

        Assert.Equal(firstPlacement, secondPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(secondPlacement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(secondPlacement.ArtifactPath)
        );
        Assert.Equal(
            ExpectedPosixExecutableFileMode,
            fileSystem.GetUnixFileMode(secondPlacement.ArtifactPath)
        );
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.CaptureTrustedParentDirectorySnapshots)
                && PathsEqual(
                    ConfigurationLayoutPlatform.Linux,
                    call.Path,
                    secondPlacement.ArtifactPath
                )
        );
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(
                    ConfigurationLayoutPlatform.Linux,
                    call.Path,
                    secondPlacement.PlacementRoot
                )
        );
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [secondPlacement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementsRejectsExistingDeterministicPosixExecutableFakeFileWithUntrustedOwnerBeforeMutation()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);
        FakeAdapterPlacement gitHelperPlacement = placements[FakeAdapterSurface.GitHelper];
        fileSystem.SetOwner(
            gitHelperPlacement.ArtifactPath,
            new FileSystemOwner("fake:other-user")
        );
        fileSystem.Calls.Clear();

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("trusted owner", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal(
            GetExpectedMaterializedContents(gitHelperPlacement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(gitHelperPlacement.ArtifactPath)
        );
        Assert.Equal(
            new FileSystemOwner("fake:other-user"),
            fileSystem.GetOwner(gitHelperPlacement.ArtifactPath)
        );
    }

    [Fact]
    public void MaterializePlacementsAllowsEquivalentWindowsPathSpellingWithoutArtifactRewrite()
    {
        ConfigurationLayoutProjectionContext initialLayout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        ) with
        {
            HomeDirectory = @"c:/Users/ALICE/",
            LocalAppDataDirectory = @"c:/Users/ALICE/AppData/LOCAL/",
        };
        ConfigurationLayoutProjectionContext rematerializedLayout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        );
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);

        _ = FakeAdapterDiscoveryScaffold.MaterializePlacements(
            new FakeAdapterMaterializationContext
            {
                Layout = initialLayout,
                FileSystem = fileSystem,
            }
        );
        fileSystem.Calls.Clear();

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = rematerializedLayout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        AssertExpectedSurfaceSet(placements.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement placement = placements[surface];
            Assert.Equal(
                GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Windows),
                fileSystem.ReadAllText(placement.ArtifactPath)
            );
        }

        AssertNoArtifactRewriteCalls(
            fileSystem,
            placements.Values,
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Fact]
    public void
    MaterializePlacementsAllowsEquivalentWindowsRepeatedSeparatorSpellingWithoutArtifactRewrite()
    {
        ConfigurationLayoutProjectionContext initialLayout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        ) with
        {
            HomeDirectory = "c:/Users//ALICE",
            LocalAppDataDirectory = "c:/Users//ALICE/AppData//LOCAL/",
        };
        ConfigurationLayoutProjectionContext rematerializedLayout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        );
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);

        _ = FakeAdapterDiscoveryScaffold.MaterializePlacements(
            new FakeAdapterMaterializationContext
            {
                Layout = initialLayout,
                FileSystem = fileSystem,
            }
        );
        fileSystem.Calls.Clear();

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = rematerializedLayout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        AssertExpectedSurfaceSet(placements.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement placement = placements[surface];
            Assert.Equal(
                GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Windows),
                fileSystem.ReadAllText(placement.ArtifactPath)
            );
        }

        AssertNoArtifactRewriteCalls(
            fileSystem,
            placements.Values,
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Fact]
    public void
    MaterializePlacementsAllowsEquivalentPosixRepeatedSeparatorSpellingWithoutArtifactRewrite()
    {
        ConfigurationLayoutProjectionContext initialLayout = CreateLayout(
            ConfigurationLayoutPlatform.Linux
        ) with
        {
            HomeDirectory = "/home//alice/",
            XdgDataHomeDirectory = "/home//alice/.local//share/",
            XdgConfigHomeDirectory = "/home//alice/.config/",
        };
        ConfigurationLayoutProjectionContext rematerializedLayout = CreateLayout(
            ConfigurationLayoutPlatform.Linux
        );
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);

        _ = FakeAdapterDiscoveryScaffold.MaterializePlacements(
            new FakeAdapterMaterializationContext
            {
                Layout = initialLayout,
                FileSystem = fileSystem,
            }
        );
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> probeResults =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(
                    CreateTopologyAwareDiscoveryContext(rematerializedLayout, fileSystem)
                )
                .ToDictionary(result => result.Surface);
        fileSystem.Calls.Clear();

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = rematerializedLayout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);

        AssertExpectedSurfaceSet(placements.Keys);
        AssertExpectedSurfaceSet(probeResults.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement placement = placements[surface];
            AssertResult(
                probeResults,
                surface,
                FakeAdapterProbeStatus.Found,
                FakeAdapterArtifactKind.File,
                placement
            );
            Assert.Equal(
                GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
                fileSystem.ReadAllText(placement.ArtifactPath)
            );
        }

        AssertPosixExecutableModes(fileSystem, placements, ConfigurationLayoutPlatform.Linux);
        AssertNoArtifactRewriteCalls(
            fileSystem,
            placements.Values,
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    public void MaterializePlacementAllowsEquivalentWindowsPathSpellingWithoutArtifactRewrite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Windows);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        FakeAdapterPlacement canonicalPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        _ = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            canonicalPlacement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        fileSystem.Calls.Clear();

        FakeAdapterPlacement equivalentPlacement = canonicalPlacement with
        {
            PlacementRoot = @"c:/Users/ALICE/AppData/LOCAL/AzureAuth/CredProvider/git-helper/",
            ArtifactPath =
                @"c:/USERS/alice/AppData/Local/AzureAuth/CredProvider\git-helper/"
                + "git-credential-azureauth-credprovider.exe",
        };

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            equivalentPlacement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(canonicalPlacement, materializedPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Windows
            ),
            fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
        );
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [canonicalPlacement],
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Fact]
    public void
    MaterializePlacementAllowsEquivalentWindowsRepeatedSeparatorSpellingWithoutArtifactRewrite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Windows);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        FakeAdapterPlacement canonicalPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        _ = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            canonicalPlacement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        fileSystem.Calls.Clear();

        FakeAdapterPlacement equivalentPlacement = canonicalPlacement with
        {
            PlacementRoot =
                @"c:/Users//ALICE/AppData//LOCAL/AzureAuth//CredProvider///git-helper//",
            ArtifactPath =
                @"c:/USERS//alice/AppData//Local/AzureAuth//CredProvider///git-helper//"
                + "git-credential-azureauth-credprovider.exe",
        };

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            equivalentPlacement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(canonicalPlacement, materializedPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Windows
            ),
            fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
        );
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [canonicalPlacement],
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Fact]
    public void
    MaterializePlacementNuGetNetCorePluginAllowsUnrelatedInvalidWindowsLocalAppDataDirectory()
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"C:\Users\alice",
            LocalAppDataDirectory = @"\\server\share\alice",
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        FakeAdapterPlacement canonicalPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            CreateLayout(ConfigurationLayoutPlatform.Windows)
        );

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(canonicalPlacement, placement);
        Assert.True(fileSystem.DirectoryExists(canonicalPlacement.PlacementRoot));
        Assert.True(fileSystem.FileExists(canonicalPlacement.ArtifactPath));
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Windows
            ),
            fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
        );
        AssertOnlyExpectedPlacementPathsAccessed(
            fileSystem,
            [canonicalPlacement],
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
    MaterializePlacementNuGetNetCorePluginAllowsBlankUnrelatedWindowsLocalAppDataDirectory(
        bool usePlacementOverload
    )
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"C:\Users\alice",
            LocalAppDataDirectory = "",
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        FakeAdapterPlacement canonicalPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            CreateLayout(ConfigurationLayoutPlatform.Windows)
        );

        FakeAdapterPlacement placement = MaterializeSinglePlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout,
            fileSystem,
            usePlacementOverload
        );

        Assert.Equal(canonicalPlacement, placement);
        Assert.True(fileSystem.DirectoryExists(canonicalPlacement.PlacementRoot));
        Assert.True(fileSystem.FileExists(canonicalPlacement.ArtifactPath));
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Windows
            ),
            fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
        );
        AssertOnlyExpectedPlacementPathsAccessed(
            fileSystem,
            [canonicalPlacement],
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
    MaterializePlacementGitHelperAllowsBlankUnrelatedLinuxHomeDirectoryWhenXdgDataHomeProvided(
        bool usePlacementOverload
    )
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = "",
            XdgDataHomeDirectory = "/home/alice/.local/share",
            XdgConfigHomeDirectory = "",
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement canonicalPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            CreateLayout(ConfigurationLayoutPlatform.Linux)
        );

        FakeAdapterPlacement placement = MaterializeSinglePlacement(
            FakeAdapterSurface.GitHelper,
            layout,
            fileSystem,
            usePlacementOverload
        );

        Assert.Equal(canonicalPlacement, placement);
        Assert.True(fileSystem.DirectoryExists(canonicalPlacement.PlacementRoot));
        Assert.True(fileSystem.FileExists(canonicalPlacement.ArtifactPath));
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Linux
            ),
            fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
        );
        Assert.Equal(
            ExpectedPosixExecutableFileMode,
            fileSystem.GetUnixFileMode(canonicalPlacement.ArtifactPath)
        );
        AssertOnlyExpectedPlacementPathsAccessed(
            fileSystem,
            [canonicalPlacement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementGitHelperThenProbePlacementsUsesSharedSurfaceScopedLayoutOnPartialLinuxLayout()
    // editorconfig-checker-enable
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = "",
            XdgDataHomeDirectory = "/home/alice/.local/share",
            XdgConfigHomeDirectory = "",
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> canonicalPlacements =
            GetPlacementsBySurface(CreateLayout(ConfigurationLayoutPlatform.Linux));

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.GitHelper,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(CreateTopologyAwareDiscoveryContext(layout, fileSystem))
                .ToDictionary(result => result.Surface);

        Assert.Equal(canonicalPlacements[FakeAdapterSurface.GitHelper], placement);
        Assert.True(
            fileSystem.FileExists(canonicalPlacements[FakeAdapterSurface.GitHelper].ArtifactPath)
        );
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacements[FakeAdapterSurface.GitHelper],
                ConfigurationLayoutPlatform.Linux
            ),
            fileSystem.ReadAllText(canonicalPlacements[FakeAdapterSurface.GitHelper].ArtifactPath)
        );
        AssertSurfaceSet(
            [
                FakeAdapterSurface.GitHelper,
                FakeAdapterSurface.PythonKeyringBackend,
                FakeAdapterSurface.PythonKeyringHelper,
                FakeAdapterSurface.KeyringShim,
            ],
            results.Keys
        );
        Assert.DoesNotContain(FakeAdapterSurface.NuGetNetCorePlugin, results.Keys);
        AssertResult(
            results,
            FakeAdapterSurface.GitHelper,
            FakeAdapterProbeStatus.Found,
            FakeAdapterArtifactKind.File,
            canonicalPlacements[FakeAdapterSurface.GitHelper]
        );
        AssertResult(
            results,
            FakeAdapterSurface.PythonKeyringBackend,
            FakeAdapterProbeStatus.Missing,
            FakeAdapterArtifactKind.Missing,
            canonicalPlacements[FakeAdapterSurface.PythonKeyringBackend]
        );
        AssertResult(
            results,
            FakeAdapterSurface.PythonKeyringHelper,
            FakeAdapterProbeStatus.Missing,
            FakeAdapterArtifactKind.Missing,
            canonicalPlacements[FakeAdapterSurface.PythonKeyringHelper]
        );
        AssertResult(
            results,
            FakeAdapterSurface.KeyringShim,
            FakeAdapterProbeStatus.Missing,
            FakeAdapterArtifactKind.Missing,
            canonicalPlacements[FakeAdapterSurface.KeyringShim]
        );
        AssertOnlyExpectedPlacementPathsAccessed(
            fileSystem,
            [
                canonicalPlacements[FakeAdapterSurface.GitHelper],
                canonicalPlacements[FakeAdapterSurface.PythonKeyringBackend],
                canonicalPlacements[FakeAdapterSurface.PythonKeyringHelper],
                canonicalPlacements[FakeAdapterSurface.KeyringShim],
            ],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\\server\share\alice")]
    // editorconfig-checker-disable
    public void MaterializePlacementNuGetNetCorePluginThenProbePlacementsUsesSurfaceScopedLayoutOnPartialWindowsLayout(
    // editorconfig-checker-enable
        string localAppDataDirectory
    )
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"C:\Users\alice",
            LocalAppDataDirectory = localAppDataDirectory,
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        FakeAdapterPlacement canonicalPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            CreateLayout(ConfigurationLayoutPlatform.Windows)
        );

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(CreateTopologyAwareDiscoveryContext(layout, fileSystem))
                .ToDictionary(result => result.Surface);

        Assert.Equal(canonicalPlacement, placement);
        Assert.True(fileSystem.FileExists(canonicalPlacement.ArtifactPath));
        Assert.Equal(
            GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Windows
            ),
            fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
        );
        AssertSurfaceSet([FakeAdapterSurface.NuGetNetCorePlugin], results.Keys);
        AssertResult(
            results,
            FakeAdapterSurface.NuGetNetCorePlugin,
            FakeAdapterProbeStatus.Found,
            FakeAdapterArtifactKind.File,
            canonicalPlacement
        );
        AssertOnlyExpectedPlacementPathsAccessed(
            fileSystem,
            [canonicalPlacement],
            ConfigurationLayoutPlatform.Windows
        );
    }

    [Theory]
    [InlineData(@"C:", "bare-drive roots")]
    [InlineData(@"C:relative", "drive-relative")]
    // editorconfig-checker-disable
    public void MaterializePlacementNuGetNetCorePluginRejectsRelevantUnsafeWindowsHomeDirectoryBeforeFilesystemMutation(
    // editorconfig-checker-enable
        string homeDirectory,
        string expectedMessageFragment
    )
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        ) with
        {
            HomeDirectory = homeDirectory,
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                FakeAdapterSurface.NuGetNetCorePlugin,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux), @"/home/alice\")]
    [InlineData(nameof(ConfigurationLayoutPlatform.MacOs), @"/Users/alice\")]
    // editorconfig-checker-disable
    public void MaterializePlacementNuGetNetCorePluginRejectsRelevantPosixHomeDirectoryContainingTrailingBackslashBeforeFilesystemMutation(
    // editorconfig-checker-enable
        string platformName,
        string homeDirectory
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform) with
        {
            HomeDirectory = homeDirectory,
        };
        var fileSystem = CreateMaterializationFileSystem(platform);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                FakeAdapterSurface.NuGetNetCorePlugin,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("backslashes", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Theory]
    [InlineData(@"D:", "bare-drive roots")]
    [InlineData(@"D:relative", "drive-relative")]
    // editorconfig-checker-disable
    public void MaterializePlacementGitHelperRejectsRelevantUnsafeWindowsLocalAppDataDirectoryInPlacementOverloadBeforeFilesystemMutation(
    // editorconfig-checker-enable
        string localAppDataDirectory,
        string expectedMessageFragment
    )
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        ) with
        {
            LocalAppDataDirectory = localAppDataDirectory,
        };

        NotSupportedException exception =
            AssertPlacementMaterializationRejectsUnsafeRelevantLayoutRootBeforeFilesystemMutation(
                FakeAdapterSurface.GitHelper,
                layout
            );

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux), @"/home/alice\", null)]
    [InlineData(
        nameof(ConfigurationLayoutPlatform.Linux),
        "/home/alice",
        @"/home/alice/.local/share\"
    )]
    [InlineData(nameof(ConfigurationLayoutPlatform.MacOs), @"/Users/alice\", null)]
    // editorconfig-checker-disable
    public void MaterializePlacementGitHelperRejectsRelevantPosixTrailingBackslashLayoutRootsInPlacementOverloadBeforeFilesystemMutation(
    // editorconfig-checker-enable
        string platformName,
        string homeDirectory,
        string? xdgDataHomeDirectory
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform) with
        {
            HomeDirectory = homeDirectory,
            XdgDataHomeDirectory = xdgDataHomeDirectory,
        };

        NotSupportedException exception =
            AssertPlacementMaterializationRejectsUnsafeRelevantLayoutRootBeforeFilesystemMutation(
                FakeAdapterSurface.GitHelper,
                layout
            );

        Assert.Contains("backslashes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializePlacementHardensPosixExecutableParentDirectoriesForIntegrityValidation()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };
        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local/share");

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.GitHelper,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        var snapshot = fileSystem.CaptureFileIntegritySnapshot(placement.ArtifactPath);

        Assert.Equal(ExpectedPosixExecutableFileMode, snapshot.UnixFileMode);
        Assert.Equal(
            OwnerOnlyDirectoryMode,
            fileSystem.GetUnixFileMode("/home/alice/.local/share/azureauth-credprovider")
        );
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode(placement.PlacementRoot));
        Assert.Equal(
            TrustedPosixDirectoryMode,
            fileSystem.GetUnixFileMode("/home/alice/.local/share")
        );
        Assert.True(fileSystem.FileMatchesIntegritySnapshot(placement.ArtifactPath, snapshot));
    }

    [Fact]
    public void
    MaterializePlacementKeepsSharedPosixParentsTrustedWhenNonExecutableSurfaceMaterializesFirst()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };
        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");

        FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.PythonKeyringBackend,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(
            OwnerOnlyDirectoryMode,
            fileSystem.GetUnixFileMode("/home/alice/.local/share")
        );
        Assert.Equal(
            OwnerOnlyDirectoryMode,
            fileSystem.GetUnixFileMode("/home/alice/.local/share/azureauth-credprovider")
        );

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.GitHelper,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        var snapshot = fileSystem.CaptureFileIntegritySnapshot(placement.ArtifactPath);

        Assert.Equal(ExpectedPosixExecutableFileMode, snapshot.UnixFileMode);
        Assert.True(fileSystem.FileMatchesIntegritySnapshot(placement.ArtifactPath, snapshot));
    }

    [Fact]
    public void
    MaterializePlacementRejectsPosixExecutableWhenPreexistingTrustedParentRemainsUnsafe()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );

        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        fileSystem.CreateDirectory("/home/alice/.local/share");
        fileSystem.SetUnixFileMode("/home/alice/.local/share", PermissivePosixDirectoryMode);
        fileSystem.Calls.Clear();

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                FakeAdapterSurface.GitHelper,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "/home/alice/.local/share",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.False(fileSystem.DirectoryExists(productDataRoot));
        Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementRejectsPosixExecutableWhenPreexistingManagedParentHasUntrustedOwnerBeforeMutation()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );

        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local/share");
        fileSystem.CreateDirectory(productDataRoot);
        fileSystem.SetUnixFileMode(productDataRoot, OwnerOnlyDirectoryMode);
        fileSystem.SetOwner(productDataRoot, new FileSystemOwner("fake:other-user"));
        fileSystem.Calls.Clear();

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                FakeAdapterSurface.GitHelper,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("trusted owner", exception.Message, StringComparison.Ordinal);
        Assert.Contains(productDataRoot, exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.True(fileSystem.DirectoryExists(productDataRoot));
        Assert.Equal(
            new FileSystemOwner("fake:other-user"),
            fileSystem.GetOwner(productDataRoot)
        );
        Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
    }

    [Fact]
    public void
    MaterializePlacementDoesNotHardenPreexistingTrustedNonCurrentOwnerManagedPosixParents()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );
        FileSystemOwner trustedNonCurrentOwner = new("fake:root");

        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local/share");
        fileSystem.CreateDirectory(productDataRoot);
        fileSystem.SetUnixFileMode(productDataRoot, TrustedPosixDirectoryMode);
        fileSystem.SetOwner(productDataRoot, trustedNonCurrentOwner);
        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.SetUnixFileMode(placement.PlacementRoot, TrustedPosixDirectoryMode);
        fileSystem.SetOwner(placement.PlacementRoot, trustedNonCurrentOwner);
        fileSystem.Calls.Clear();

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            placement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(placement, materializedPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
        Assert.Equal(TrustedPosixDirectoryMode, fileSystem.GetUnixFileMode(productDataRoot));
        Assert.Equal(
            TrustedPosixDirectoryMode,
            fileSystem.GetUnixFileMode(placement.PlacementRoot)
        );
        Assert.Equal(trustedNonCurrentOwner, fileSystem.GetOwner(productDataRoot));
        Assert.Equal(trustedNonCurrentOwner, fileSystem.GetOwner(placement.PlacementRoot));
        Assert.Equal(
            ExpectedPosixExecutableFileMode,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && (
                    PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, productDataRoot)
                    || PathsEqual(
                        ConfigurationLayoutPlatform.Linux,
                        call.Path,
                        placement.PlacementRoot
                    )
                )
        );
    }

    [Fact]
    public void
    MaterializePlacementHardensBlankPosixTrustedParentsWhenNonExecutableSurfaceMaterializesFirst()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };

        FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.PythonKeyringBackend,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode("/home"));
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode("/home/alice"));
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode("/home/alice/.local"));

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.GitHelper,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        var snapshot = fileSystem.CaptureFileIntegritySnapshot(placement.ArtifactPath);

        Assert.Equal(ExpectedPosixExecutableFileMode, snapshot.UnixFileMode);
        Assert.True(fileSystem.FileMatchesIntegritySnapshot(placement.ArtifactPath, snapshot));
    }

    [Fact]
    public void MaterializePlacementNuGetThenGitHelperDoesNotPoisonLaterExecutablePosixSurface()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = PermissivePosixDirectoryMode,
        };

        FakeAdapterPlacement nuGetPlacement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        FakeAdapterPlacement gitHelperPlacement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.GitHelper,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        FileIntegritySnapshot snapshot = fileSystem.CaptureFileIntegritySnapshot(
            gitHelperPlacement.ArtifactPath
        );

        Assert.Equal(
            GetExpectedMaterializedContents(nuGetPlacement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(nuGetPlacement.ArtifactPath)
        );
        Assert.Equal(
            GetExpectedMaterializedContents(gitHelperPlacement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(gitHelperPlacement.ArtifactPath)
        );
        Assert.Equal(ExpectedPosixExecutableFileMode, snapshot.UnixFileMode);
        Assert.True(
            fileSystem.FileMatchesIntegritySnapshot(gitHelperPlacement.ArtifactPath, snapshot)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializePlacementRejectsUnsafeCallerConstructedPlacementBeforeFilesystemMutation(
        bool useInvalidArtifactKind
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement projectedPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        FakeAdapterPlacement unsafePlacement = useInvalidArtifactKind
            ? projectedPlacement with { ArtifactKind = FakeAdapterArtifactKind.Missing }
            : projectedPlacement with { ArtifactPath = "/tmp/fake-adapter-escape.txt" };

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                unsafePlacement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("projected placement", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementsRejectsTraversalBearingProjectedPathsBeforeFilesystemMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout =
            platform == ConfigurationLayoutPlatform.Windows
                ? new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Windows,
                    HomeDirectory = @"C:\Users\alice\..\escape",
                    LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
                }
                : new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Linux,
                    HomeDirectory = "/home/alice/../escape",
                    XdgDataHomeDirectory = "/home/alice/.local/share",
                    XdgConfigHomeDirectory = "/home/alice/.config",
                };
        var fileSystem = CreateMaterializationFileSystem(platform);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("'.' or '..'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public void MaterializePlacementsRejectsWindowsUncProjectedPathsBeforeFilesystemMutation()
    {
        ConfigurationLayoutProjectionContext layout = new()
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"\\server\share\alice",
            LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("UNC", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Theory]
    [InlineData(@"C:", @"C:\Users\alice\AppData\Local")]
    [InlineData(@"C:\Users\alice", @"D:")]
    public void MaterializePlacementsRejectsWindowsBareDriveRawLayoutRootsBeforeFilesystemMutation(
        string homeDirectory,
        string localAppDataDirectory
    )
    {
        NotSupportedException exception =
            AssertWindowsMaterializationRejectsUnsafeLayoutRootBeforeFilesystemMutation(
                homeDirectory,
                localAppDataDirectory
            );

        Assert.Contains("bare-drive roots", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:relative", @"C:\Users\alice\AppData\Local")]
    [InlineData(@"C:\Users\alice", @"D:relative")]
    public void
    MaterializePlacementsRejectsWindowsDriveRelativeRawLayoutRootsBeforeFilesystemMutation(
        string homeDirectory,
        string localAppDataDirectory
    )
    {
        NotSupportedException exception =
            AssertWindowsMaterializationRejectsUnsafeLayoutRootBeforeFilesystemMutation(
                homeDirectory,
                localAppDataDirectory
            );

        Assert.Contains("drive-relative", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        @"C:\con",
        @"C:\con\AppData\Local",
        "reserved DOS device names"
    )]
    [InlineData(
        @"C:\Users\alice\foo:bar",
        @"C:\Users\alice\foo:bar\AppData\Local",
        "colons outside the drive specifier"
    )]
    public void
    MaterializePlacementsRejectsWindowsReservedDosOrAdsRawLayoutRootsBeforeFilesystemMutation(
        string homeDirectory,
        string localAppDataDirectory,
        string expectedMessageFragment
    )
    {
        NotSupportedException exception =
            AssertWindowsMaterializationRejectsUnsafeLayoutRootBeforeFilesystemMutation(
                homeDirectory,
                localAppDataDirectory
            );

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.MacOs))]
    public void MaterializePlacementsRejectsPosixBackslashProjectedPathsBeforeFilesystemMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout =
            platform == ConfigurationLayoutPlatform.Linux
                ? new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.Linux,
                    HomeDirectory = @"/home/alice\escape",
                    XdgDataHomeDirectory = @"/home/alice\escape/.local/share",
                    XdgConfigHomeDirectory = "/home/alice/.config",
                }
                : new ConfigurationLayoutProjectionContext
                {
                    Platform = ConfigurationLayoutPlatform.MacOs,
                    HomeDirectory = @"/Users/alice\escape",
                };
        var fileSystem = CreateMaterializationFileSystem(platform);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("backslashes", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Theory]
    [InlineData(
        nameof(ConfigurationLayoutPlatform.Linux),
        @"/home/alice\",
        "/home/alice/.local/share",
        "/home/alice/.config"
    )]
    [InlineData(
        nameof(ConfigurationLayoutPlatform.Linux),
        "/home/alice",
        @"/home/alice/.local/share\",
        "/home/alice/.config"
    )]
    [InlineData(
        nameof(ConfigurationLayoutPlatform.MacOs),
        @"/Users/alice\",
        null,
        null
    )]
    // editorconfig-checker-disable
    public void MaterializePlacementsRejectsPosixRawLayoutRootsContainingTrailingBackslashesBeforeFilesystemMutation(
    // editorconfig-checker-enable
        string platformName,
        string homeDirectory,
        string? xdgDataHomeDirectory,
        string? xdgConfigHomeDirectory
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext baseLayout = CreateLayout(platform);
        ConfigurationLayoutProjectionContext layout = baseLayout with
        {
            HomeDirectory = homeDirectory,
            XdgDataHomeDirectory = xdgDataHomeDirectory ?? baseLayout.XdgDataHomeDirectory,
            XdgConfigHomeDirectory = xdgConfigHomeDirectory ?? baseLayout.XdgConfigHomeDirectory,
        };
        var fileSystem = CreateMaterializationFileSystem(platform);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("backslashes", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public void MaterializePlacementsAllowsMalformedUnusedLinuxXdgConfigHomeDirectory()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Linux
        ) with
        {
            XdgConfigHomeDirectory = @"/home/alice/.config\",
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> canonicalPlacements =
            GetPlacementsBySurface(CreateLayout(ConfigurationLayoutPlatform.Linux));

        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements =
            FakeAdapterDiscoveryScaffold
                .MaterializePlacements(
                    new FakeAdapterMaterializationContext
                    {
                        Layout = layout,
                        FileSystem = fileSystem,
                    }
                )
                .ToDictionary(placement => placement.Surface);
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(CreateTopologyAwareDiscoveryContext(layout, fileSystem))
                .ToDictionary(result => result.Surface);

        AssertExpectedSurfaceSet(placements.Keys);
        AssertExpectedSurfaceSet(results.Keys);
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement canonicalPlacement = canonicalPlacements[surface];
            Assert.Equal(canonicalPlacement, placements[surface]);
            Assert.True(fileSystem.FileExists(canonicalPlacement.ArtifactPath));
            Assert.Equal(
                GetExpectedMaterializedContents(
                canonicalPlacement,
                ConfigurationLayoutPlatform.Linux
            ),
                fileSystem.ReadAllText(canonicalPlacement.ArtifactPath)
            );
            AssertResult(
                results,
                surface,
                FakeAdapterProbeStatus.Found,
                FakeAdapterArtifactKind.File,
                canonicalPlacement
            );
        }
        AssertOnlyExpectedPlacementPathsAccessed(
            fileSystem,
            canonicalPlacements.Values,
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    public void MaterializePlacementRejectsPosixBackslashCallerPlacementBeforeFilesystemMutation()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement projectedPlacement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        FakeAdapterPlacement unsafePlacement = projectedPlacement with
        {
            ArtifactPath = projectedPlacement.ArtifactPath.Replace(
                "/git-helper/",
                @"\git-helper\",
                StringComparison.Ordinal
            ),
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                unsafePlacement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("backslashes", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public void
    MaterializePlacementsRejectsHostNativePathSemanticsMismatchBeforeFilesystemMutation()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            GetLayoutPlatformWithHostPathSemanticsMismatch()
        );
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("path semantics", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public void MaterializePlacementRejectsHostNativePathSemanticsMismatchBeforeFilesystemMutation()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            GetLayoutPlatformWithHostPathSemanticsMismatch()
        );
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("path semantics", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public void MaterializePlacementRejectsRealSystemFileSystemMaterializationBeforeMutation()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider-tests",
            Guid.NewGuid().ToString("N")
        );
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = Path.Combine(root, "home"),
            XdgDataHomeDirectory = Path.Combine(root, "xdg-data"),
            XdgConfigHomeDirectory = Path.Combine(root, "xdg-config"),
        };
        var fileSystem = new SystemFileSystem();
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("SystemFileSystem", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(placement.PlacementRoot));
        Assert.False(File.Exists(placement.ArtifactPath));
    }

    [Fact]
    public void
    MaterializePlacementRejectsDelegatingRealSystemFileSystemMaterializationBeforeMutation()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider-tests",
            Guid.NewGuid().ToString("N")
        );
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = Path.Combine(root, "home"),
            XdgDataHomeDirectory = Path.Combine(root, "xdg-data"),
            XdgConfigHomeDirectory = Path.Combine(root, "xdg-config"),
        };
        IFileSystem fileSystem = new DelegatingFileSystem(new SystemFileSystem());
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(nameof(DelegatingFileSystem), exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(placement.PlacementRoot));
        Assert.False(File.Exists(placement.ArtifactPath));
    }

    [Fact]
    public void MaterializePlacementRejectsWindowsOptInFakeWithoutReparsePointSafetyBeforeMutation()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Windows);
        var innerFileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        IFileSystem fileSystem = new DelegatingScaffoldMaterializationFileSystem(innerFileSystem);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "reparse-point safety support",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(innerFileSystem);
        Assert.False(innerFileSystem.DirectoryExists(placement.PlacementRoot));
        Assert.False(innerFileSystem.FileExists(placement.ArtifactPath));
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementRejectsSymbolicLinkPlacementRootBeforeMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string placementRootParent = placement.PlacementRoot[
            ..placement.PlacementRoot.LastIndexOf(separator)
        ];
        string outsideParent = platform == ConfigurationLayoutPlatform.Windows
            ? @"C:\outside"
            : "/outside";
        string outsideRoot = platform == ConfigurationLayoutPlatform.Windows
            ? @"C:\outside\git-helper"
            : "/outside/git-helper";

        fileSystem.CreateDirectory(placementRootParent);
        fileSystem.CreateDirectory(outsideParent);
        fileSystem.AddSymbolicLink(placement.PlacementRoot, outsideRoot);
        fileSystem.Calls.Clear();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.False(fileSystem.DirectoryExists(outsideRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementRejectsNonSymbolicReparsePointArtifactTargetBeforeMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, "original-contents");
        fileSystem.MarkAsNonSymbolicReparsePoint(placement.ArtifactPath);
        fileSystem.Calls.Clear();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal("original-contents", fileSystem.ReadAllText(placement.ArtifactPath));
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementRejectsWrongKindPlacementRootBeforeMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string placementRootParent = placement.PlacementRoot[
            ..placement.PlacementRoot.LastIndexOf(separator)
        ];

        fileSystem.CreateDirectory(placementRootParent);
        fileSystem.WriteAllText(placement.PlacementRoot, "occupied-as-file");
        fileSystem.Calls.Clear();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("wrong kind", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.True(fileSystem.FileExists(placement.PlacementRoot));
        Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal("occupied-as-file", fileSystem.ReadAllText(placement.PlacementRoot));
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementRejectsExistingUnknownSameKindFileBeforeMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, "real-existing-shim");
        fileSystem.Calls.Clear();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("non-scaffold contents", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal("real-existing-shim", fileSystem.ReadAllText(placement.ArtifactPath));
    }

    [Fact]
    public void MaterializePlacementRejectsExistingDeterministicPosixExecutableFileWithWrongMode()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(
            placement.ArtifactPath,
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux)
        );
        fileSystem.SetUnixFileMode(
            placement.ArtifactPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite
        );
        fileSystem.Calls.Clear();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("executable mode", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
    }

    [Fact]
    public void MaterializePlacementAllowsExistingDeterministicFakeFileWithoutRewritingArtifact()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(
            placement.ArtifactPath,
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux)
        );
        fileSystem.SetUnixFileMode(placement.ArtifactPath, ExpectedPosixExecutableFileMode);
        fileSystem.Calls.Clear();

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            placement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(placement, materializedPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
        Assert.Equal(
            ExpectedPosixExecutableFileMode,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
        AssertNoArtifactRewriteCalls(
            fileSystem,
            new[] { placement },
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementAllowsExistingDeterministicPosixExecutableUnderWritableScaffoldOwnedParentsByHardeningThem()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );

        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local/share");
        fileSystem.CreateDirectory(productDataRoot);
        fileSystem.SetUnixFileMode(productDataRoot, PermissivePosixDirectoryMode);
        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.SetUnixFileMode(placement.PlacementRoot, PermissivePosixDirectoryMode);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.SetUnixFileMode(placement.ArtifactPath, ExpectedPosixExecutableFileMode);
        fileSystem.Calls.Clear();

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            placement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        var snapshot = fileSystem.CaptureFileIntegritySnapshot(placement.ArtifactPath);

        Assert.Equal(placement, materializedPlacement);
        Assert.Equal(deterministicContents, fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode(productDataRoot));
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode(placement.PlacementRoot));
        Assert.Equal(ExpectedPosixExecutableFileMode, snapshot.UnixFileMode);
        Assert.True(fileSystem.FileMatchesIntegritySnapshot(placement.ArtifactPath, snapshot));
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, productDataRoot)
        );
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.PlacementRoot)
        );
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [placement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Theory]
    [InlineData(nameof(FakeAdapterSurface.GitHelper))]
    [InlineData(nameof(FakeAdapterSurface.PythonKeyringHelper))]
    [InlineData(nameof(FakeAdapterSurface.KeyringShim))]
    public void
    MaterializePlacementRejectsExistingDeterministicPosixExecutableFakeFileWithUntrustedOwner(
        string surfaceName
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterSurface surface = Enum.Parse<FakeAdapterSurface>(surfaceName);
        FakeAdapterPlacement placement =
            FakeAdapterDiscoveryScaffold.ProjectPlacement(surface, layout);

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(
            placement.ArtifactPath,
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux)
        );
        fileSystem.SetUnixFileMode(placement.ArtifactPath, ExpectedPosixExecutableFileMode);
        fileSystem.SetOwner(placement.ArtifactPath, new FileSystemOwner("fake:other-user"));
        fileSystem.Calls.Clear();

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("trusted owner", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
        Assert.Equal(
            new FileSystemOwner("fake:other-user"),
            fileSystem.GetOwner(placement.ArtifactPath)
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementRejectsExistingDeterministicPosixExecutableWhenOutsideManagedAncestorRemainsUnsafe()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );

        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        fileSystem.CreateDirectory("/home/alice/.local/share");
        fileSystem.SetUnixFileMode("/home/alice/.local/share", PermissivePosixDirectoryMode);
        fileSystem.CreateDirectory(productDataRoot);
        fileSystem.SetUnixFileMode(productDataRoot, OwnerOnlyDirectoryMode);
        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.SetUnixFileMode(placement.PlacementRoot, OwnerOnlyDirectoryMode);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.SetUnixFileMode(placement.ArtifactPath, ExpectedPosixExecutableFileMode);
        fileSystem.Calls.Clear();

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "/home/alice/.local/share",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal(deterministicContents, fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.Equal(
            PermissivePosixDirectoryMode,
            fileSystem.GetUnixFileMode("/home/alice/.local/share")
        );
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode(productDataRoot));
        Assert.Equal(OwnerOnlyDirectoryMode, fileSystem.GetUnixFileMode(placement.PlacementRoot));
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [placement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    public void MaterializePlacementRejectsLateReparsePointInjectedBeforeIdempotentEarlyReturn()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(
            placement.ArtifactPath,
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux)
        );
        fileSystem.Calls.Clear();

        int artifactReadCount = 0;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.ReadAllBytes)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            artifactReadCount++;
            if (artifactReadCount != 2)
            {
                return;
            }

            raceInjected = true;
            system.MarkAsNonSymbolicReparsePoint(placement.ArtifactPath);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(2, artifactReadCount);
        Assert.True(raceInjected);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.True(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementRejectsLateSymbolicLinkInjectedBeforeIdempotentEarlyReturnWithoutRewritingArtifact()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );
        const string outsideParent = "/outside";
        const string outsideTarget = "/outside/same-contents.dll";

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.Calls.Clear();

        int artifactReadCount = 0;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.ReadAllBytes)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            artifactReadCount++;
            if (artifactReadCount != 2)
            {
                return;
            }

            raceInjected = true;
            system.CreateDirectory(outsideParent);
            system.WriteAllText(outsideTarget, deterministicContents);
            system.DeleteFile(placement.ArtifactPath);
            system.AddSymbolicLink(placement.ArtifactPath, outsideTarget);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(2, artifactReadCount);
        Assert.True(raceInjected);
        Assert.True(fileSystem.IsSymbolicLink(placement.ArtifactPath));
        Assert.Equal(deterministicContents, fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Fact]
    public void MaterializePlacementRejectsLateDeleteBeforeIdempotentEarlyReturn()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.Calls.Clear();

        int artifactReadCount = 0;
        bool readyToInject = false;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                call.Operation == nameof(InMemoryFileSystem.ReadAllBytes)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                artifactReadCount++;
                readyToInject |= artifactReadCount == 2;
                return;
            }

            if (
                raceInjected
                || !readyToInject
                || call.Operation != nameof(InMemoryFileSystem.IsSymbolicLink)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            system.DeleteFile(placement.ArtifactPath);
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("no longer exists", exception.Message, StringComparison.Ordinal);
        Assert.True(readyToInject);
        Assert.True(raceInjected);
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Theory]
    [InlineData(true, "expected file kind")]
    [InlineData(false, "non-scaffold contents")]
    public void MaterializePlacementRejectsLateReplacementBeforeIdempotentEarlyReturn(
        bool replaceWithDirectory,
        string expectedMessageFragment
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );
        const string replacementContents = "foreign-same-kind-contents";

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.Calls.Clear();

        int artifactReadCount = 0;
        bool readyToInject = false;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                call.Operation == nameof(InMemoryFileSystem.ReadAllBytes)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                artifactReadCount++;
                readyToInject |= artifactReadCount == 2;
                return;
            }

            if (
                raceInjected
                || !readyToInject
                || call.Operation != nameof(InMemoryFileSystem.IsSymbolicLink)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            if (replaceWithDirectory)
            {
                system.DeleteFile(placement.ArtifactPath);
                system.CreateDirectory(placement.ArtifactPath);
            }
            else
            {
                system.WriteAllText(placement.ArtifactPath, replacementContents);
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.True(readyToInject);
        Assert.True(raceInjected);
        if (replaceWithDirectory)
        {
            Assert.True(fileSystem.DirectoryExists(placement.ArtifactPath));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
        else
        {
            Assert.True(fileSystem.FileExists(placement.ArtifactPath));
            Assert.Equal(replacementContents, fileSystem.ReadAllText(placement.ArtifactPath));
        }

        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementRejectsLateSymbolicLinkInjectedAfterFinalNonSnapshotIdempotentReadWindowWithoutArtifactClobber()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );
        const string outsideParent = "/outside";
        const string outsideTarget = "/outside/final-window-swap.dll";
        const string outsideTargetContents = "outside-target-contents";

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.Calls.Clear();

        int artifactReadCount = 0;
        bool readyToInject = false;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                call.Operation == nameof(InMemoryFileSystem.ReadAllBytes)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                artifactReadCount++;
                readyToInject |= artifactReadCount == 4;
                return;
            }

            if (
                raceInjected
                || !readyToInject
                || call.Operation != nameof(InMemoryFileSystem.IsSymbolicLink)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            system.CreateDirectory(outsideParent);
            system.WriteAllText(outsideTarget, outsideTargetContents);
            system.DeleteFile(placement.ArtifactPath);
            system.AddSymbolicLink(placement.ArtifactPath, outsideTarget);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(4, artifactReadCount);
        Assert.True(readyToInject);
        Assert.True(raceInjected);
        Assert.True(fileSystem.IsSymbolicLink(placement.ArtifactPath));
        Assert.Equal(outsideTargetContents, fileSystem.ReadAllText(outsideTarget));
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [placement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Theory]
    [InlineData(true, "expected file kind")]
    [InlineData(false, "non-scaffold contents")]
    public void
    MaterializePlacementRejectsLateRegularReplacementInjectedDuringFinalNonSnapshotSafetyWindow(
        bool replaceWithDirectory,
        string expectedMessageFragment
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );
        const string replacementContents = "foreign-final-window-contents";

        fileSystem.CreateDirectory(placement.PlacementRoot);
        fileSystem.WriteAllText(placement.ArtifactPath, deterministicContents);
        fileSystem.Calls.Clear();

        int artifactReadCount = 0;
        bool readyToInject = false;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                call.Operation == nameof(InMemoryFileSystem.ReadAllBytes)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                artifactReadCount++;
                readyToInject |= artifactReadCount == 4;
                return;
            }

            if (
                raceInjected
                || !readyToInject
                || call.Operation != nameof(InMemoryFileSystem.IsSymbolicLink)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            if (replaceWithDirectory)
            {
                system.DeleteFile(placement.ArtifactPath);
                system.CreateDirectory(placement.ArtifactPath);
            }
            else
            {
                system.WriteAllText(placement.ArtifactPath, replacementContents);
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.True(readyToInject);
        Assert.True(raceInjected);
        if (replaceWithDirectory)
        {
            Assert.True(fileSystem.DirectoryExists(placement.ArtifactPath));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
        else
        {
            Assert.True(fileSystem.FileExists(placement.ArtifactPath));
            Assert.Equal(replacementContents, fileSystem.ReadAllText(placement.ArtifactPath));
        }

        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Theory]
    [InlineData(true, "non-scaffold contents")]
    [InlineData(false, "expected scaffold executable mode")]
    public void MaterializePlacementRejectsCaptureSnapshotReplacementForExistingPosixExecutable(
        bool replaceContents,
        string expectedMessageFragment
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.MaterializePlacement(
            FakeAdapterSurface.GitHelper,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );
        string replacementContents = replaceContents
            ? "foreign-same-kind-contents"
            : deterministicContents;
        UnixFileMode replacementMode = replaceContents
            ? ExpectedPosixExecutableFileMode
            : UnixFileMode.UserRead | UnixFileMode.UserExecute;

        fileSystem.Calls.Clear();

        bool snapshotReplacementInjected = false;
        bool restorePending = false;
        bool restoredAfterSnapshot = false;
        bool mutatingFileWithinCallback = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (mutatingFileWithinCallback)
            {
                return;
            }

            if (
                !snapshotReplacementInjected
                && call.Operation == nameof(InMemoryFileSystem.CaptureFileIntegritySnapshot)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                snapshotReplacementInjected = true;
                restorePending = true;
                mutatingFileWithinCallback = true;
                try
                {
                    ReplaceExistingPosixFile(
                        system,
                        placement.ArtifactPath,
                        replacementContents,
                        replacementMode
                    );
                }
                finally
                {
                    mutatingFileWithinCallback = false;
                }

                return;
            }

            if (!restorePending || restoredAfterSnapshot)
            {
                return;
            }

            restoredAfterSnapshot = true;
            restorePending = false;
            mutatingFileWithinCallback = true;
            try
            {
                ReplaceExistingPosixFile(
                    system,
                    placement.ArtifactPath,
                    deterministicContents,
                    ExpectedPosixExecutableFileMode
                );
            }
            finally
            {
                mutatingFileWithinCallback = false;
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.True(snapshotReplacementInjected);
        Assert.False(restoredAfterSnapshot);
    }

    [Fact]
    public void MaterializePlacementRejectsLateSymbolicLinkInjectedAtDirectoryCreationCallSite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        const string outsideParent = "/outside";
        const string outsideRoot = "/outside/git-helper";
        string placementRootParent = placement.PlacementRoot[
            ..placement.PlacementRoot.LastIndexOf('/')
        ];
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.CreateDirectoryNoFollow)
                || !PathsEqual(
                    ConfigurationLayoutPlatform.Linux,
                    call.Path,
                    placement.PlacementRoot
                )
            )
            {
                return;
            }

            raceInjected = true;
            system.CreateDirectory(placementRootParent);
            system.CreateDirectory(outsideParent);
            system.AddSymbolicLink(placement.PlacementRoot, outsideRoot);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(raceInjected);
        Assert.True(fileSystem.IsSymbolicLink(placement.PlacementRoot));
        Assert.False(fileSystem.DirectoryExists(outsideRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Empty(fileSystem.Files);
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Fact]
    public void
    MaterializePlacementRejectsLateAncestorSymbolicLinkInjectedAtDirectoryCreationCallSite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );
        string productDataRootParent = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            productDataRoot
        );
        const string outsideParent = "/outside";
        const string outsideProductDataRoot = "/outside/product-data";
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.CreateDirectoryNoFollow)
                || !PathsEqual(
                    ConfigurationLayoutPlatform.Linux,
                    call.Path,
                    placement.PlacementRoot
                )
            )
            {
                return;
            }

            raceInjected = true;
            system.CreateDirectory(productDataRootParent);
            system.CreateDirectory(outsideParent);
            system.AddSymbolicLink(productDataRoot, outsideProductDataRoot);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(raceInjected);
        Assert.True(fileSystem.IsSymbolicLink(productDataRoot));
        Assert.False(fileSystem.DirectoryExists(outsideProductDataRoot));
        Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Empty(fileSystem.Files);
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [placement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Fact]
    public void MaterializePlacementRejectsFileAppearingBeforeConditionalCreateWithoutClobberingIt()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        fileSystem.CreateDirectory(placement.PlacementRoot);
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.AtomicWriteAllText)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            system.WriteAllText(placement.ArtifactPath, "race-foreign-contents");
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "expected mutation target to be absent",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(raceInjected);
        Assert.Equal("race-foreign-contents", fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.DeleteFile)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Fact]
    public void MaterializePlacementRejectsLateNonExecutableReplacementBeforeFinalCreateValidation()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.NuGetNetCorePlugin,
            layout
        );
        const string replacementContents = "foreign-same-kind-contents";
        bool artifactCreated = false;
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                !artifactCreated
                && call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                artifactCreated = true;
                return;
            }

            if (
                raceInjected
                || !artifactCreated
                || call.Operation != nameof(InMemoryFileSystem.ReadAllBytes)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            system.WriteAllText(placement.ArtifactPath, replacementContents);
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("non-scaffold contents", exception.Message, StringComparison.Ordinal);
        Assert.True(artifactCreated);
        Assert.True(raceInjected);
        Assert.True(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal(replacementContents, fileSystem.ReadAllText(placement.ArtifactPath));
    }

    [Theory]
    [InlineData(true, "expected file kind")]
    [InlineData(false, "non-scaffold contents")]
    public void MaterializePlacementRejectsLateExecutableReplacementBeforeFinalCreateValidation(
        bool replaceWithDirectory,
        string expectedMessageFragment
    )
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        const string replacementContents = "foreign-same-kind-contents";
        bool raceInjected = false;
        bool mutatingWithinCallback = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                mutatingWithinCallback
                || raceInjected
                || call.Operation != nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            mutatingWithinCallback = true;
            try
            {
                if (replaceWithDirectory)
                {
                    system.DeleteFile(placement.ArtifactPath);
                    system.CreateDirectory(placement.ArtifactPath);
                }
                else
                {
                    ReplaceExistingPosixFile(
                        system,
                        placement.ArtifactPath,
                        replacementContents,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite
                    );
                }
            }
            finally
            {
                mutatingWithinCallback = false;
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.True(raceInjected);
        if (replaceWithDirectory)
        {
            Assert.True(fileSystem.DirectoryExists(placement.ArtifactPath));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
        else
        {
            Assert.True(fileSystem.FileExists(placement.ArtifactPath));
            Assert.Equal(replacementContents, fileSystem.ReadAllText(placement.ArtifactPath));
            Assert.Equal(
                ExpectedPosixExecutableFileMode,
                fileSystem.GetUnixFileMode(placement.ArtifactPath)
            );
        }
    }

    [Fact]
    public void
    MaterializePlacementDoesNotLeavePoisonedExecutableScaffoldFileWhenPostWriteFailureOccurs()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        bool postWriteFailureInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                postWriteFailureInjected
                || call.Operation
                    != nameof(InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            postWriteFailureInjected = true;
            system.FailNextCall(new IOException("post-write failure"));
        };

        IOException exception = Assert.Throws<IOException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("post-write failure", exception.Message, StringComparison.Ordinal);
        Assert.True(postWriteFailureInjected);
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation
                    == nameof(
                        InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow
                    )
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );

        fileSystem.Calls.Clear();

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            placement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(placement, materializedPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
        Assert.Equal(
            ExpectedPosixExecutableFileMode,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
    }

    [Fact]
    public void
    MaterializePlacementRollsBackNewlyCreatedExecutableScaffoldFileWhenModeSettingFails()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );

        bool modeFailureInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                modeFailureInjected
                || call.Operation != nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            modeFailureInjected = true;
            system.FailNextCall(new IOException("chmod failed"));
        };

        IOException exception = Assert.Throws<IOException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("chmod failed", exception.Message, StringComparison.Ordinal);
        Assert.True(modeFailureInjected);
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.DeleteFile)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );

        fileSystem.AfterRecord = null;
        fileSystem.Calls.Clear();

        FakeAdapterPlacement materializedPlacement =
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
            placement,
            new FakeAdapterMaterializationContext
            {
                Layout = layout,
                FileSystem = fileSystem,
            }
        );

        Assert.Equal(placement, materializedPlacement);
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
        Assert.Equal(
            ExpectedPosixExecutableFileMode,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
    }

    [Fact]
    // editorconfig-checker-disable
    public void MaterializePlacementPreservesRacedInExecutableReplacementBeforeNoFollowSnapshotWhenModeSettingFails()
    // editorconfig-checker-enable
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        const string replacementContents = "race-foreign-contents";

        bool snapshotRaceInjected = false;
        bool modeFailureInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                !snapshotRaceInjected
                && call.Operation
                    == nameof(InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                snapshotRaceInjected = true;
                ReplaceExistingPosixFile(
                    system,
                    placement.ArtifactPath,
                    replacementContents,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                );
                return;
            }

            if (
                modeFailureInjected
                || call.Operation != nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            modeFailureInjected = true;
            system.FailNextCall(new IOException("chmod failed"));
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "rollback of the newly created scaffold file failed",
            exception.Message,
            StringComparison.Ordinal
        );
        AggregateException aggregateException = Assert.IsType<AggregateException>(
            exception.InnerException
        );
        Assert.Collection(
            aggregateException.InnerExceptions,
            innerException =>
            {
                IOException modeException = Assert.IsType<IOException>(innerException);
                Assert.Contains("chmod failed", modeException.Message, StringComparison.Ordinal);
            },
            innerException =>
            {
                InvalidOperationException rollbackException =
                    Assert.IsType<InvalidOperationException>(
                    innerException
                );
                Assert.Contains(
                    "snapshot does not match",
                    rollbackException.Message,
                    StringComparison.Ordinal
                );
            }
        );
        Assert.True(snapshotRaceInjected);
        Assert.True(modeFailureInjected);
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation
                    == nameof(
                        InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow
                    )
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
        Assert.True(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal(replacementContents, fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
    }

    [Fact]
    public void
    MaterializePlacementWrapsExecutableModeFailureWhenRollbackDeleteRejectsRacedInReplacement()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        const string replacementContents = "race-foreign-contents";

        bool modeFailureInjected = false;
        bool rollbackReplacementInjected = false;
        bool mutatingWithinCallback = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (mutatingWithinCallback)
            {
                return;
            }

            if (
                !modeFailureInjected
                && call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                modeFailureInjected = true;
                system.FailNextCall(new IOException("chmod failed"));
                return;
            }

            if (
                rollbackReplacementInjected
                || !modeFailureInjected
                || call.Operation != nameof(InMemoryFileSystem.DeleteFile)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            rollbackReplacementInjected = true;
            mutatingWithinCallback = true;
            try
            {
                ReplaceExistingPosixFile(
                    system,
                    placement.ArtifactPath,
                    replacementContents,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                );
            }
            finally
            {
                mutatingWithinCallback = false;
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "rollback of the newly created scaffold file failed",
            exception.Message,
            StringComparison.Ordinal
        );
        AggregateException aggregateException = Assert.IsType<AggregateException>(
            exception.InnerException
        );
        Assert.Collection(
            aggregateException.InnerExceptions,
            innerException =>
            {
                IOException modeException = Assert.IsType<IOException>(innerException);
                Assert.Contains("chmod failed", modeException.Message, StringComparison.Ordinal);
            },
            innerException =>
            {
                InvalidOperationException rollbackException =
                    Assert.IsType<InvalidOperationException>(
                    innerException
                );
                Assert.Contains(
                    "snapshot does not match",
                    rollbackException.Message,
                    StringComparison.Ordinal
                );
            }
        );
        Assert.True(modeFailureInjected);
        Assert.True(rollbackReplacementInjected);
        Assert.True(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal(replacementContents, fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
    }

    [Fact]
    public void
    MaterializePlacementDoesNotDeleteRacedInSameContentsReplacementWhenExecutableModeRollbackRuns()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string deterministicContents = GetExpectedMaterializedContents(
            placement,
            ConfigurationLayoutPlatform.Linux
        );

        bool modeFailureInjected = false;
        bool rollbackReplacementInjected = false;
        bool mutatingWithinCallback = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (mutatingWithinCallback)
            {
                return;
            }

            if (
                !modeFailureInjected
                && call.Operation == nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                modeFailureInjected = true;
                system.FailNextCall(new IOException("chmod failed"));
                return;
            }

            if (
                rollbackReplacementInjected
                || !modeFailureInjected
                || call.Operation != nameof(InMemoryFileSystem.DeleteFile)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            rollbackReplacementInjected = true;
            mutatingWithinCallback = true;
            try
            {
                ReplaceExistingPosixFile(
                    system,
                    placement.ArtifactPath,
                    deterministicContents,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                );
            }
            finally
            {
                mutatingWithinCallback = false;
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "rollback of the newly created scaffold file failed",
            exception.Message,
            StringComparison.Ordinal
        );
        AggregateException aggregateException = Assert.IsType<AggregateException>(
            exception.InnerException
        );
        Assert.Collection(
            aggregateException.InnerExceptions,
            innerException =>
            {
                IOException modeException = Assert.IsType<IOException>(innerException);
                Assert.Contains("chmod failed", modeException.Message, StringComparison.Ordinal);
            },
            innerException =>
            {
                InvalidOperationException rollbackException =
                    Assert.IsType<InvalidOperationException>(
                    innerException
                );
                Assert.Contains(
                    "snapshot does not match",
                    rollbackException.Message,
                    StringComparison.Ordinal
                );
            }
        );
        Assert.True(modeFailureInjected);
        Assert.True(rollbackReplacementInjected);
        Assert.True(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal(deterministicContents, fileSystem.ReadAllText(placement.ArtifactPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
    }

    [Fact]
    public void MaterializePlacementRejectsLateReparsePointInjectedAtExecutableModeSettingCallSite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            system.MarkAsNonSymbolicReparsePoint(placement.ArtifactPath);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(raceInjected);
        Assert.True(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Equal(
            GetExpectedMaterializedContents(placement, ConfigurationLayoutPlatform.Linux),
            fileSystem.ReadAllText(placement.ArtifactPath)
        );
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(placement.ArtifactPath)
        );
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.DeleteFile)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
        );
    }

    [Fact]
    public void MaterializePlacementRejectsLateSymbolicLinkInjectedAtExecutableModeSettingCallSite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        const string outsideParent = "/outside";
        const string outsideTarget = "/outside/late-chmod-swap";
        const string outsideTargetContents = "outside-target-contents";
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                || !PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
            {
                return;
            }

            raceInjected = true;
            system.CreateDirectory(outsideParent);
            system.WriteAllText(outsideTarget, outsideTargetContents);
            system.DeleteFile(placement.ArtifactPath);
            system.AddSymbolicLink(placement.ArtifactPath, outsideTarget);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(raceInjected);
        Assert.True(fileSystem.IsSymbolicLink(placement.ArtifactPath));
        Assert.Equal(outsideTargetContents, fileSystem.ReadAllText(outsideTarget));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(outsideTarget)
        );
        Assert.Single(
            fileSystem.Calls.FindAll(call =>
                call.Operation == nameof(InMemoryFileSystem.DeleteFile)
                && PathsEqual(ConfigurationLayoutPlatform.Linux, call.Path, placement.ArtifactPath)
            )
        );
    }

    [Fact]
    public void
    MaterializePlacementRejectsLateAncestorReparsePointInjectedAtParentHardeningCallSite()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Linux);
        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.GitHelper,
            layout
        );
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            placement.PlacementRoot
        );
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation != nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                || !PathsEqual(
                    ConfigurationLayoutPlatform.Linux,
                    call.Path,
                    placement.PlacementRoot
                )
            )
            {
                return;
            }

            raceInjected = true;
            system.MarkAsNonSymbolicReparsePoint(productDataRoot);
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(raceInjected);
        Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(productDataRoot));
        Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        Assert.Empty(fileSystem.Files);
        AssertNoArtifactRewriteCalls(
            fileSystem,
            [placement],
            ConfigurationLayoutPlatform.Linux
        );
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementsRejectsLaterExistingUnknownSameKindFileBeforeAnyMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        FakeAdapterPlacement unsafePlacement = placements[FakeAdapterSurface.KeyringShim];

        fileSystem.CreateDirectory(unsafePlacement.PlacementRoot);
        fileSystem.WriteAllText(unsafePlacement.ArtifactPath, "foreign-same-kind-contents");
        fileSystem.Calls.Clear();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("non-scaffold contents", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal(
            "foreign-same-kind-contents",
            fileSystem.ReadAllText(unsafePlacement.ArtifactPath)
        );
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            if (surface == FakeAdapterSurface.KeyringShim)
            {
                continue;
            }

            FakeAdapterPlacement placement = placements[surface];
            Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementsRejectsLaterWrongKindProjectedPlacementBeforeAnyMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        FakeAdapterPlacement unsafePlacement = placements[FakeAdapterSurface.PythonKeyringHelper];

        fileSystem.CreateDirectory(unsafePlacement.PlacementRoot);
        fileSystem.CreateDirectory(unsafePlacement.ArtifactPath);
        fileSystem.Calls.Clear();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("wrong kind", exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Empty(fileSystem.Files);
        Assert.True(fileSystem.DirectoryExists(unsafePlacement.PlacementRoot));
        Assert.True(fileSystem.DirectoryExists(unsafePlacement.ArtifactPath));
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            if (surface == FakeAdapterSurface.PythonKeyringHelper)
            {
                continue;
            }

            FakeAdapterPlacement placement = placements[surface];
            Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void MaterializePlacementsRejectsLaterUnsafeProjectedPlacementBeforeAnyMutation(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        FakeAdapterPlacement unsafePlacement = placements[FakeAdapterSurface.PythonKeyringHelper];
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string placementRootParent = unsafePlacement.PlacementRoot[
            ..unsafePlacement.PlacementRoot.LastIndexOf(separator)
        ];
        string outsideParent = platform == ConfigurationLayoutPlatform.Windows
            ? @"C:\outside"
            : "/outside";
        string outsideRoot = platform == ConfigurationLayoutPlatform.Windows
            ? @"C:\outside\python-keyring"
            : "/outside/python-keyring";

        fileSystem.CreateDirectory(placementRootParent);
        fileSystem.CreateDirectory(outsideParent);
        fileSystem.AddSymbolicLink(unsafePlacement.PlacementRoot, outsideRoot);
        fileSystem.Calls.Clear();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Empty(fileSystem.Files);
        Assert.True(fileSystem.IsSymbolicLink(unsafePlacement.PlacementRoot));
        Assert.False(fileSystem.DirectoryExists(outsideRoot));
        Assert.False(fileSystem.FileExists(unsafePlacement.ArtifactPath));
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            if (surface == FakeAdapterSurface.PythonKeyringHelper)
            {
                continue;
            }

            FakeAdapterPlacement placement = placements[surface];
            Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
        }
    }

    [Fact]
    public void MaterializePlacementsRejectsLaterExecutableTrustedParentFailureBeforeAnyMutation()
    {
        ConfigurationLayoutProjectionContext layout =
            CreateLayout(ConfigurationLayoutPlatform.Linux);
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        FakeAdapterPlacement unsafePlacement = placements[FakeAdapterSurface.PythonKeyringHelper];
        string productDataRoot = GetContainingDirectory(
            ConfigurationLayoutPlatform.Linux,
            unsafePlacement.PlacementRoot
        );

        SeedTrustedPosixDirectory(fileSystem, "/home");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local");
        SeedTrustedPosixDirectory(fileSystem, "/home/alice/.local/share");
        fileSystem.CreateDirectory(productDataRoot);
        fileSystem.SetUnixFileMode(productDataRoot, OwnerOnlyDirectoryMode);
        fileSystem.CreateDirectory(unsafePlacement.PlacementRoot);
        fileSystem.SetUnixFileMode(unsafePlacement.PlacementRoot, OwnerOnlyDirectoryMode);
        fileSystem.SetOwner(unsafePlacement.PlacementRoot, new FileSystemOwner("fake:other-user"));
        fileSystem.Calls.Clear();

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Contains("trusted owner", exception.Message, StringComparison.Ordinal);
        Assert.Contains(unsafePlacement.PlacementRoot, exception.Message, StringComparison.Ordinal);
        AssertNoMaterializationMutationCalls(fileSystem);
        Assert.Equal(
            new FileSystemOwner("fake:other-user"),
            fileSystem.GetOwner(unsafePlacement.PlacementRoot)
        );
        foreach (FakeAdapterSurface surface in ExpectedSurfaces)
        {
            FakeAdapterPlacement placement = placements[surface];
            Assert.False(fileSystem.FileExists(placement.ArtifactPath));
            if (surface != FakeAdapterSurface.PythonKeyringHelper)
            {
                Assert.False(fileSystem.DirectoryExists(placement.PlacementRoot));
            }
        }
    }

    [Theory]
    [InlineData("home/alice", "home/alice/.local/share", "rooted placement paths")]
    [InlineData("/home/alice/../alice", "/home/alice/.local/share/../share", "'.' or '..'")]
    public void ProbeSkipsAndRejectsUnsafePosixProjectedLayouts(
        string homeDirectory,
        string xdgDataHomeDirectory,
        string expectedMessageFragment
    )
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = homeDirectory,
            XdgDataHomeDirectory = xdgDataHomeDirectory,
            XdgConfigHomeDirectory = "/home/alice/.config",
        };
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = path =>
            {
                fileExistsCalls++;
                throw new InvalidOperationException(
                    $"Unsafe probe unexpectedly called FileExists('{path}')."
                );
            },
            DirectoryExists = path =>
            {
                directoryExistsCalls++;
                throw new InvalidOperationException(
                    $"Unsafe probe unexpectedly called DirectoryExists('{path}')."
                );
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
    }

    [Fact]
    public void ProbeSkipsAndRejectsUnsafeWindowsProjectedLayouts()
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"C:\Users\alice.",
            LocalAppDataDirectory = @"C:\Users\alice.\AppData\Local ",
        };
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = path =>
            {
                fileExistsCalls++;
                throw new InvalidOperationException(
                    $"Unsafe probe unexpectedly called FileExists('{path}')."
                );
            },
            DirectoryExists = path =>
            {
                directoryExistsCalls++;
                throw new InvalidOperationException(
                    $"Unsafe probe unexpectedly called DirectoryExists('{path}')."
                );
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains("trailing spaces or periods", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
    }

    [Theory]
    [InlineData(
        @"C:\con",
        @"C:\con\AppData\Local",
        "reserved DOS device names"
    )]
    [InlineData(
        @"C:\Users\alice\foo:bar",
        @"C:\Users\alice\foo:bar\AppData\Local",
        "colons outside the drive specifier"
    )]
    public void ProbeSkipsAndRejectsUnsafeWindowsReservedDosOrAdsProjectedLayouts(
        string homeDirectory,
        string localAppDataDirectory,
        string expectedMessageFragment
    )
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = homeDirectory,
            LocalAppDataDirectory = localAppDataDirectory,
        };
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = path =>
            {
                fileExistsCalls++;
                throw new InvalidOperationException(
                    $"Unsafe probe unexpectedly called FileExists('{path}')."
                );
            },
            DirectoryExists = path =>
            {
                directoryExistsCalls++;
                throw new InvalidOperationException(
                    $"Unsafe probe unexpectedly called DirectoryExists('{path}')."
                );
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void ProbePlacementsSkipAndSurfaceProbeRejectContextsWithoutTopologyCallbacks(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        var files = new HashSet<string>(StringComparer.Ordinal)
        {
            placements[FakeAdapterSurface.GitHelper].ArtifactPath,
        };
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = path =>
            {
                fileExistsCalls++;
                return files.Contains(path);
            },
            DirectoryExists = path =>
            {
                directoryExistsCalls++;
                return false;
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains(
            "symbolic-link topology probe support",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
    }

    [Fact]
    public void ProbePlacementsSkipAndSurfaceProbeRejectWindowsContextsWithoutReparsePointCallback()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        );
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        var files = new HashSet<string>(StringComparer.Ordinal)
        {
            placements[FakeAdapterSurface.GitHelper].ArtifactPath,
        };
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        int symbolicLinkCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = path =>
            {
                fileExistsCalls++;
                return files.Contains(path);
            },
            DirectoryExists = path =>
            {
                directoryExistsCalls++;
                return false;
            },
            IsSymbolicLink = path =>
            {
                symbolicLinkCalls++;
                return false;
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains(
            "reparse-point topology probe support",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
        Assert.Equal(0, symbolicLinkCalls);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux), true)]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux), false)]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows), true)]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows), false)]
    public void ProbePlacementsSkipAndSurfaceProbeRejectWhenSymbolicLinkCallbackThrows(
        string platformName,
        bool throwUnauthorizedAccessException
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        int symbolicLinkCalls = 0;
        int reparsePointCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = _ =>
            {
                fileExistsCalls++;
                return false;
            },
            DirectoryExists = _ =>
            {
                directoryExistsCalls++;
                return false;
            },
            IsSymbolicLink = path =>
            {
                symbolicLinkCalls++;
                throw CreateTopologyProbeCallbackException(
                    throwUnauthorizedAccessException,
                    path
                );
            },
            IsReparsePoint = _ =>
            {
                reparsePointCalls++;
                return false;
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        if (throwUnauthorizedAccessException)
        {
            Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
        }
        else
        {
            Assert.IsType<IOException>(exception.InnerException);
        }

        Assert.True(symbolicLinkCalls > 0);
        Assert.Equal(0, reparsePointCalls);
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
    }

    [Fact]
    public void ProbePlacementsSkipAndSurfaceProbeRejectPathSemanticsMismatch()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        );
        var pathSemantics = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        int symbolicLinkCalls = 0;
        int reparsePointCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = pathSemantics.IsPathFullyQualified,
            FileExists = _ =>
            {
                fileExistsCalls++;
                return false;
            },
            DirectoryExists = _ =>
            {
                directoryExistsCalls++;
                return false;
            },
            IsSymbolicLink = _ =>
            {
                symbolicLinkCalls++;
                return false;
            },
            IsReparsePoint = _ =>
            {
                reparsePointCalls++;
                return false;
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains("path semantics", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
        Assert.Equal(0, symbolicLinkCalls);
        Assert.Equal(0, reparsePointCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProbePlacementsSkipAndSurfaceProbeRejectWhenReparsePointCallbackThrows(
        bool throwUnauthorizedAccessException
    )
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        );
        int fileExistsCalls = 0;
        int directoryExistsCalls = 0;
        int symbolicLinkCalls = 0;
        int reparsePointCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = _ =>
            {
                fileExistsCalls++;
                return false;
            },
            DirectoryExists = _ =>
            {
                directoryExistsCalls++;
                return false;
            },
            IsSymbolicLink = _ =>
            {
                symbolicLinkCalls++;
                return false;
            },
            IsReparsePoint = path =>
            {
                reparsePointCalls++;
                throw CreateTopologyProbeCallbackException(
                    throwUnauthorizedAccessException,
                    path
                );
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        if (throwUnauthorizedAccessException)
        {
            Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
        }
        else
        {
            Assert.IsType<IOException>(exception.InnerException);
        }

        Assert.True(symbolicLinkCalls > 0);
        Assert.True(reparsePointCalls > 0);
        Assert.Equal(0, fileExistsCalls);
        Assert.Equal(0, directoryExistsCalls);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void ProbePlacementsSkipAndSurfaceProbeRejectContextsWithoutFileExistsCallback(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        int directoryExistsCalls = 0;
        FakeAdapterDiscoveryContext context = CreateTopologyAwareDiscoveryContext(
            layout,
            CreateMaterializationFileSystem(platform)
        ) with
        {
            FileExists = null,
            DirectoryExists = _ =>
            {
                directoryExistsCalls++;
                return false;
            },
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains(
            "file-existence probe support",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(0, directoryExistsCalls);
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void ProbePlacementsSkipAndSurfaceProbeRejectContextsWithoutDirectoryExistsCallback(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        int fileExistsCalls = 0;
        FakeAdapterDiscoveryContext context = CreateTopologyAwareDiscoveryContext(
            layout,
            CreateMaterializationFileSystem(platform)
        ) with
        {
            FileExists = _ =>
            {
                fileExistsCalls++;
                return false;
            },
            DirectoryExists = null,
        };

        Assert.Empty(FakeAdapterDiscoveryScaffold.ProbePlacements(context));
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(FakeAdapterSurface.GitHelper, context)
        );
        Assert.Contains(
            "directory-existence probe support",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(0, fileExistsCalls);
    }

    [Fact]
    public void ProbePlacementsSkipStatefullyUnsafeRepeatObservationsInsteadOfThrowing()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Linux
        );
        var observedPaths = new HashSet<string>(StringComparer.Ordinal);
        int symbolicLinkCalls = 0;
        FakeAdapterDiscoveryContext context = new()
        {
            Layout = layout,
            IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
            FileExists = static _ => false,
            DirectoryExists = static _ => false,
            IsSymbolicLink = path =>
            {
                symbolicLinkCalls++;
                return !observedPaths.Add(path);
            },
        };

        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(context)
                .ToDictionary(result => result.Surface);

        AssertSurfaceSet([FakeAdapterSurface.GitHelper], results.Keys);
        AssertResult(
            results,
            FakeAdapterSurface.GitHelper,
            FakeAdapterProbeStatus.Missing,
            FakeAdapterArtifactKind.Missing
        );
        Assert.True(symbolicLinkCalls > 1);
    }

    [Fact]
    public void ProbePlacementsReturnsFoundMissingAndWrongKindResults()
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"C:\Users\alice",
            LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
        };
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );

        var files = new HashSet<string>(StringComparer.Ordinal)
        {
            placements[FakeAdapterSurface.GitHelper].ArtifactPath,
            placements[FakeAdapterSurface.PythonKeyringHelper].ArtifactPath,
        };
        var directories = new HashSet<string>(StringComparer.Ordinal)
        {
            placements[FakeAdapterSurface.PythonKeyringBackend].ArtifactPath,
        };

        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(
                    new FakeAdapterDiscoveryContext
                    {
                        Layout = layout,
                        IsPathFullyQualified = CreatePathSemanticsProbe(layout.Platform),
                        FileExists = files.Contains,
                        DirectoryExists = directories.Contains,
                        IsSymbolicLink = static _ => false,
                        IsReparsePoint = static _ => false,
                    }
                )
                .ToDictionary(result => result.Surface);

        AssertExpectedSurfaceSet(results.Keys);
        AssertResult(
            results,
            FakeAdapterSurface.GitHelper,
            FakeAdapterProbeStatus.Found,
            FakeAdapterArtifactKind.File
        );
        AssertResult(
            results,
            FakeAdapterSurface.NuGetNetCorePlugin,
            FakeAdapterProbeStatus.Missing,
            FakeAdapterArtifactKind.Missing
        );
        AssertResult(
            results,
            FakeAdapterSurface.PythonKeyringBackend,
            FakeAdapterProbeStatus.WrongKind,
            FakeAdapterArtifactKind.Directory
        );
        AssertResult(
            results,
            FakeAdapterSurface.PythonKeyringHelper,
            FakeAdapterProbeStatus.Found,
            FakeAdapterArtifactKind.File
        );
        AssertResult(
            results,
            FakeAdapterSurface.KeyringShim,
            FakeAdapterProbeStatus.Missing,
            FakeAdapterArtifactKind.Missing
        );
    }

    [Theory]
    [InlineData(nameof(ConfigurationLayoutPlatform.Linux))]
    [InlineData(nameof(ConfigurationLayoutPlatform.Windows))]
    public void ProbePlacementsSkipAndSurfaceProbeRejectsProjectedSymbolicLinkPlacementRoot(
        string platformName
    )
    {
        ConfigurationLayoutPlatform platform =
            Enum.Parse<ConfigurationLayoutPlatform>(platformName);
        ConfigurationLayoutProjectionContext layout = CreateLayout(platform);
        var fileSystem = CreateMaterializationFileSystem(platform);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        FakeAdapterPlacement unsafePlacement = placements[FakeAdapterSurface.PythonKeyringHelper];
        string placementRootParent = GetContainingDirectory(
            platform,
            unsafePlacement.PlacementRoot
        );
        string outsideRoot = platform == ConfigurationLayoutPlatform.Windows
            ? @"C:\outside\python-keyring"
            : "/outside/python-keyring";
        FakeAdapterDiscoveryContext context = CreateTopologyAwareDiscoveryContext(
            layout,
            fileSystem
        );

        fileSystem.CreateDirectory(placementRootParent);
        fileSystem.CreateDirectory(GetContainingDirectory(platform, outsideRoot));
        fileSystem.CreateDirectory(outsideRoot);
        fileSystem.AddSymbolicLink(unsafePlacement.PlacementRoot, outsideRoot);
        fileSystem.CreateDirectory(unsafePlacement.ArtifactPath);

        FakeAdapterProbeResult rawResult = FakeAdapterDiscoveryScaffold.ProbePlacement(
            unsafePlacement,
            context
        );
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(context)
                .ToDictionary(result => result.Surface);

        Assert.Equal(FakeAdapterProbeStatus.WrongKind, rawResult.Status);
        Assert.Equal(FakeAdapterArtifactKind.Directory, rawResult.ActualKind);
        AssertSurfaceSet(
            ExpectedSurfaces.Where(surface => surface != unsafePlacement.Surface),
            results.Keys
        );
        Assert.DoesNotContain(unsafePlacement.Surface, results.Keys);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(unsafePlacement.Surface, context)
        );
        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ProbePlacementsSkipAndSurfaceProbeRejectsProjectedReparseArtifactPath()
    {
        ConfigurationLayoutProjectionContext layout = CreateLayout(
            ConfigurationLayoutPlatform.Windows
        );
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        FakeAdapterPlacement unsafePlacement = placements[FakeAdapterSurface.GitHelper];
        FakeAdapterDiscoveryContext context = CreateTopologyAwareDiscoveryContext(
            layout,
            fileSystem
        );

        fileSystem.CreateDirectory(unsafePlacement.PlacementRoot);
        fileSystem.WriteAllText(unsafePlacement.ArtifactPath, "helper");
        fileSystem.MarkAsNonSymbolicReparsePoint(unsafePlacement.ArtifactPath);

        FakeAdapterProbeResult rawResult = FakeAdapterDiscoveryScaffold.ProbePlacement(
            unsafePlacement,
            context
        );
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(context)
                .ToDictionary(result => result.Surface);

        Assert.Equal(FakeAdapterProbeStatus.Found, rawResult.Status);
        Assert.Equal(FakeAdapterArtifactKind.File, rawResult.ActualKind);
        AssertSurfaceSet(
            ExpectedSurfaces.Where(surface => surface != unsafePlacement.Surface),
            results.Keys
        );
        Assert.DoesNotContain(unsafePlacement.Surface, results.Keys);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.ProbePlacement(unsafePlacement.Surface, context)
        );
        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void PythonKeyringBackendPlacementDoesNotReuseConfigurationManifestTarget()
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = @"C:\Users\alice",
            LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
        };

        FakeAdapterPlacement placement = FakeAdapterDiscoveryScaffold.ProjectPlacement(
            FakeAdapterSurface.PythonKeyringBackend,
            layout
        );
        string configurationManifestPath = ConfigurationLayoutProjector
            .ProjectPythonKeyringBackend(layout)
            .TargetPath;

        Assert.NotEqual(configurationManifestPath, placement.ArtifactPath);
    }

    [Fact]
    public void ProbePlacementsIsSideEffectFree()
    {
        var layout = new ConfigurationLayoutProjectionContext
        {
            Platform = ConfigurationLayoutPlatform.Linux,
            HomeDirectory = "/home/alice",
            XdgDataHomeDirectory = "/home/alice/.local/share",
            XdgConfigHomeDirectory = "/home/alice/.config",
        };
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements = GetPlacementsBySurface(
            layout
        );
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);

        fileSystem.CreateDirectory(placements[FakeAdapterSurface.GitHelper].PlacementRoot);
        fileSystem.WriteAllText(placements[FakeAdapterSurface.GitHelper].ArtifactPath, "helper");
        fileSystem.CreateDirectory(
            placements[FakeAdapterSurface.PythonKeyringBackend].ArtifactPath
        );
        fileSystem.Calls.Clear();

        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results =
            FakeAdapterDiscoveryScaffold
                .ProbePlacements(CreateTopologyAwareDiscoveryContext(layout, fileSystem))
                .ToDictionary(result => result.Surface);

        AssertExpectedSurfaceSet(results.Keys);
        AssertResult(
            results,
            FakeAdapterSurface.GitHelper,
            FakeAdapterProbeStatus.Found,
            FakeAdapterArtifactKind.File
        );
        AssertResult(
            results,
            FakeAdapterSurface.PythonKeyringBackend,
            FakeAdapterProbeStatus.WrongKind,
            FakeAdapterArtifactKind.Directory
        );
        Assert.NotEmpty(fileSystem.Calls);
        Assert.All(
            fileSystem.Calls,
            call =>
                Assert.True(
                    call.Operation is nameof(InMemoryFileSystem.IsPathFullyQualified)
                        or nameof(InMemoryFileSystem.FileExists)
                        or nameof(InMemoryFileSystem.DirectoryExists)
                        or nameof(InMemoryFileSystem.IsSymbolicLink)
                        or nameof(IFileSystemReparsePointSafety.IsReparsePoint)
                )
        );
    }

    private static Dictionary<FakeAdapterSurface, FakeAdapterPlacement> GetPlacementsBySurface(
        ConfigurationLayoutProjectionContext context
    ) =>
        FakeAdapterDiscoveryScaffold.ProjectPlacements(context).ToDictionary(
            placement => placement.Surface
        );

    private static void AssertPosixExecutableModes(
        InMemoryFileSystem fileSystem,
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements,
        ConfigurationLayoutPlatform platform
    )
    {
        if (platform == ConfigurationLayoutPlatform.Windows)
        {
            return;
        }

        foreach (FakeAdapterSurface surface in PosixExecutableSurfaces)
        {
            Assert.Equal(
                ExpectedPosixExecutableFileMode,
                fileSystem.GetUnixFileMode(placements[surface].ArtifactPath)
            );
        }
    }

    private static ConfigurationLayoutProjectionContext CreateLayout(
        ConfigurationLayoutPlatform platform
    ) =>
        platform switch
        {
            ConfigurationLayoutPlatform.Windows => new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.Windows,
                HomeDirectory = @"C:\Users\alice",
                LocalAppDataDirectory = @"C:\Users\alice\AppData\Local",
            },
            ConfigurationLayoutPlatform.Linux => new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.Linux,
                HomeDirectory = "/home/alice",
                XdgDataHomeDirectory = "/home/alice/.local/share",
                XdgConfigHomeDirectory = "/home/alice/.config",
            },
            ConfigurationLayoutPlatform.MacOs => new ConfigurationLayoutProjectionContext
            {
                Platform = ConfigurationLayoutPlatform.MacOs,
                HomeDirectory = "/Users/alice",
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(platform),
                platform,
                "Unsupported platform."
            ),
        };

    private static ConfigurationLayoutPlatform GetLayoutPlatformWithHostPathSemanticsMismatch()
    {
        if (OperatingSystem.IsWindows())
        {
            return ConfigurationLayoutPlatform.Linux;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return ConfigurationLayoutPlatform.Windows;
        }

        throw new PlatformNotSupportedException("Unsupported host platform.");
    }

    private static InMemoryFileSystem CreateMaterializationFileSystem(
        ConfigurationLayoutPlatform platform
    ) =>
        new(
            platform == ConfigurationLayoutPlatform.Windows
                ? InMemoryPathSemantics.Windows
                : InMemoryPathSemantics.Posix
        );

    private static Func<string, bool> CreatePathSemanticsProbe(
        ConfigurationLayoutPlatform platform
    ) => CreateMaterializationFileSystem(platform).IsPathFullyQualified;

    private static FakeAdapterDiscoveryContext CreateTopologyAwareDiscoveryContext(
        ConfigurationLayoutProjectionContext layout,
        InMemoryFileSystem fileSystem
    ) =>
        new()
        {
            Layout = layout,
            IsPathFullyQualified = fileSystem.IsPathFullyQualified,
            FileExists = fileSystem.FileExists,
            DirectoryExists = fileSystem.DirectoryExists,
            IsSymbolicLink = fileSystem.IsSymbolicLink,
            IsReparsePoint = ((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint,
        };

    private static Exception CreateTopologyProbeCallbackException(
        bool throwUnauthorizedAccessException,
        string path
    ) =>
        throwUnauthorizedAccessException
            ? new UnauthorizedAccessException(
                $"Fake topology probe denied access to '{path}'."
            )
            : new IOException($"Fake topology probe failed for '{path}'.");

    private static FakeAdapterPlacement MaterializeSinglePlacement(
        FakeAdapterSurface surface,
        ConfigurationLayoutProjectionContext layout,
        InMemoryFileSystem fileSystem,
        bool usePlacementOverload
    )
    {
        FakeAdapterMaterializationContext context = new()
        {
            Layout = layout,
            FileSystem = fileSystem,
        };

        return usePlacementOverload
            ? FakeAdapterDiscoveryScaffold.MaterializePlacement(
                FakeAdapterDiscoveryScaffold.ProjectPlacement(surface, layout),
                context
            )
            : FakeAdapterDiscoveryScaffold.MaterializePlacement(surface, context);
    }

    private static string GetExpectedMaterializedContents(
        FakeAdapterPlacement placement,
        ConfigurationLayoutPlatform platform
    ) =>
        string.Join(
            '\n',
            [
                "fake-adapter-scaffold-version=1",
                $"surface={placement.Surface}",
                $"platform={platform}",
                $"placement-root={
                    NormalizeExpectedMaterializedPayloadPath(
                        platform,
                        placement.PlacementRoot
                    )
                }",
                $"artifact-path={
                    NormalizeExpectedMaterializedPayloadPath(
                        platform,
                        placement.ArtifactPath
                    )
                }",
                $"artifact-kind={placement.ArtifactKind}",
                $"unix-executable-intent={
                    platform != ConfigurationLayoutPlatform.Windows
                    && PosixExecutableSurfaces.Contains(placement.Surface)
                }",
            ]
        ) + "\n";

    private static string NormalizeExpectedMaterializedPayloadPath(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        string normalizedPath = NormalizeExpectedMaterializedPath(platform, path);
        return platform == ConfigurationLayoutPlatform.Windows && normalizedPath.Length > 0
            ? string.Concat(normalizedPath[0], normalizedPath[1..].ToLowerInvariant())
            : normalizedPath;
    }

    private static void AssertNoArtifactRewriteCalls(
        InMemoryFileSystem fileSystem,
        IEnumerable<FakeAdapterPlacement> placements,
        ConfigurationLayoutPlatform platform
    )
    {
        foreach (FakeAdapterPlacement placement in placements)
        {
            Assert.DoesNotContain(
                fileSystem.Calls,
                call =>
                    call.Operation
                        is nameof(InMemoryFileSystem.WriteAllText)
                            or nameof(
                                InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow
                            )
                            or nameof(InMemoryFileSystem.AtomicWriteAllText)
                    && PathsEqual(platform, call.Path, placement.ArtifactPath)
            );
            Assert.DoesNotContain(
                fileSystem.Calls,
                call =>
                    call.Operation
                        is nameof(InMemoryFileSystem.SetUnixFileMode)
                            or nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
                    && PathsEqual(platform, call.Path, placement.ArtifactPath)
            );
        }
    }

    private static bool PathsEqual(
        ConfigurationLayoutPlatform platform,
        string left,
        string right
    ) =>
        string.Equals(
            NormalizeExpectedMaterializedPath(platform, left),
            NormalizeExpectedMaterializedPath(platform, right),
            platform == ConfigurationLayoutPlatform.Windows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private static string NormalizeExpectedMaterializedPath(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string normalizedPath = path.Replace(AlternateSeparator(separator), separator);
        if (
            platform == ConfigurationLayoutPlatform.Windows
            && normalizedPath.Length >= 2
            && IsWindowsDriveLetter(normalizedPath[0])
            && normalizedPath[1] == ':'
        )
        {
            normalizedPath = CollapseRepeatedWindowsSeparatorsAfterDriveSpecifier(normalizedPath);
        }
        else if (platform != ConfigurationLayoutPlatform.Windows)
        {
            normalizedPath = CollapseRepeatedPosixSeparators(normalizedPath);
        }

        return IsRootPath(platform, normalizedPath)
            ? normalizedPath
            : TrimTrailingSeparators(normalizedPath, separator);
    }

    private static string CollapseRepeatedWindowsSeparatorsAfterDriveSpecifier(string path)
    {
        var normalizedPath = new StringBuilder(path.Length);
        normalizedPath.Append(char.ToUpperInvariant(path[0]));
        normalizedPath.Append(':');

        bool previousWasSeparator = false;
        for (int index = 2; index < path.Length; index++)
        {
            char currentCharacter = path[index];
            if (currentCharacter == '\\')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
                normalizedPath.Append(currentCharacter);
                continue;
            }

            previousWasSeparator = false;
            normalizedPath.Append(currentCharacter);
        }

        return normalizedPath.ToString();
    }

    private static string CollapseRepeatedPosixSeparators(string path)
    {
        var normalizedPath = new StringBuilder(path.Length);
        bool previousWasSeparator = false;
        foreach (char currentCharacter in path)
        {
            if (currentCharacter == '/')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
                normalizedPath.Append(currentCharacter);
                continue;
            }

            previousWasSeparator = false;
            normalizedPath.Append(currentCharacter);
        }

        return normalizedPath.ToString();
    }

    private static bool IsRootPath(ConfigurationLayoutPlatform platform, string path) =>
        platform == ConfigurationLayoutPlatform.Windows
            ? path.Length == 3
                && IsWindowsDriveLetter(path[0])
                && path[1] == ':'
                && (path[2] == '\\' || path[2] == '/')
            : path == "/";

    private static bool IsWindowsDriveLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static string TrimTrailingSeparators(string value, char separator)
    {
        string trimmed = value.TrimEnd(separator, AlternateSeparator(separator));
        return trimmed.Length == 0 ? value : trimmed;
    }

    private static char AlternateSeparator(char separator) => separator == '\\' ? '/' : '\\';

    private static void AssertExpectedSurfaceSet(IEnumerable<FakeAdapterSurface> surfaces)
    {
        FakeAdapterSurface[] actualSurfaces = surfaces.OrderBy(static surface => surface).ToArray();
        Assert.Equal(ExpectedSurfaces, actualSurfaces);
    }

    private static void AssertSurfaceSet(
        IEnumerable<FakeAdapterSurface> expectedSurfaces,
        IEnumerable<FakeAdapterSurface> actualSurfaces
    )
    {
        Assert.Equal(
            expectedSurfaces.OrderBy(static surface => surface).ToArray(),
            actualSurfaces.OrderBy(static surface => surface).ToArray()
        );
    }

    private static NotSupportedException
    AssertWindowsMaterializationRejectsUnsafeLayoutRootBeforeFilesystemMutation(
        string homeDirectory,
        string localAppDataDirectory
    )
    {
        ConfigurationLayoutProjectionContext layout = new()
        {
            Platform = ConfigurationLayoutPlatform.Windows,
            HomeDirectory = homeDirectory,
            LocalAppDataDirectory = localAppDataDirectory,
        };
        var fileSystem = CreateMaterializationFileSystem(ConfigurationLayoutPlatform.Windows);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacements(
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
        return exception;
    }

    private static NotSupportedException
    AssertPlacementMaterializationRejectsUnsafeRelevantLayoutRootBeforeFilesystemMutation(
        FakeAdapterSurface surface,
        ConfigurationLayoutProjectionContext layout
    )
    {
        var fileSystem = CreateMaterializationFileSystem(layout.Platform);
        FakeAdapterPlacement placement =
            FakeAdapterDiscoveryScaffold.ProjectPlacement(surface, layout);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            FakeAdapterDiscoveryScaffold.MaterializePlacement(
                placement,
                new FakeAdapterMaterializationContext
                {
                    Layout = layout,
                    FileSystem = fileSystem,
                }
            )
        );

        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
        return exception;
    }

    private static void SeedTrustedPosixDirectory(InMemoryFileSystem fileSystem, string path)
    {
        fileSystem.CreateDirectory(path);
        fileSystem.SetUnixFileMode(path, TrustedPosixDirectoryMode);
    }

    private static void ReplaceExistingPosixFile(
        InMemoryFileSystem fileSystem,
        string path,
        string contents,
        UnixFileMode mode
    )
    {
        fileSystem.DeleteFile(path);
        fileSystem.WriteAllText(path, contents);
        fileSystem.SetUnixFileMode(path, mode);
    }

    private static void AssertNoMaterializationMutationCalls(InMemoryFileSystem fileSystem)
    {
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                is nameof(InMemoryFileSystem.CreateDirectory)
                    or nameof(InMemoryFileSystem.CreateDirectoryNoFollow)
                or nameof(InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow)
                or nameof(InMemoryFileSystem.AtomicWriteAllText)
                or nameof(InMemoryFileSystem.WriteAllText)
                or nameof(InMemoryFileSystem.DeleteFile)
                or nameof(InMemoryFileSystem.SetUnixFileMode)
                or nameof(InMemoryFileSystem.SetUnixFileModeNoFollow)
        );
    }

    private static void AssertOnlyExpectedPlacementPathsAccessed(
        InMemoryFileSystem fileSystem,
        IEnumerable<FakeAdapterPlacement> placements,
        ConfigurationLayoutPlatform platform
    )
    {
        HashSet<string> expectedPaths = GetExpectedPlacementPaths(placements, platform);
        foreach (FileSystemCall call in fileSystem.Calls)
        {
            Assert.True(
                expectedPaths.Contains(NormalizeExpectedMaterializedPath(platform, call.Path)),
                $"Unexpected filesystem path was accessed: {call.Path}"
            );
        }

        foreach (string path in fileSystem.Files.Keys)
        {
            Assert.True(
                expectedPaths.Contains(NormalizeExpectedMaterializedPath(platform, path)),
                $"Unexpected scaffold file was materialized: {path}"
            );
        }

        foreach (string path in fileSystem.Directories)
        {
            Assert.True(
                expectedPaths.Contains(NormalizeExpectedMaterializedPath(platform, path)),
                $"Unexpected scaffold directory was materialized: {path}"
            );
        }
    }

    private static HashSet<string> GetExpectedPlacementPaths(
        IEnumerable<FakeAdapterPlacement> placements,
        ConfigurationLayoutPlatform platform
    )
    {
        var expectedPaths = new HashSet<string>(
            platform == ConfigurationLayoutPlatform.Windows
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal
        );
        foreach (FakeAdapterPlacement placement in placements)
        {
            AppendExpectedPathChain(expectedPaths, platform, placement.PlacementRoot);
            switch (placement.ArtifactKind)
            {
                case FakeAdapterArtifactKind.File:
                    AppendExpectedPathChain(
                        expectedPaths,
                        platform,
                        GetContainingDirectory(platform, placement.ArtifactPath)
                    );
                    expectedPaths.Add(
                        NormalizeExpectedMaterializedPath(platform, placement.ArtifactPath)
                    );
                    break;
                case FakeAdapterArtifactKind.Directory:
                    AppendExpectedPathChain(expectedPaths, platform, placement.ArtifactPath);
                    break;
            }
        }

        return expectedPaths;
    }

    private static void AppendExpectedPathChain(
        HashSet<string> expectedPaths,
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        foreach (string candidatePath in EnumerateExpectedPathChain(platform, path))
        {
            expectedPaths.Add(candidatePath);
        }
    }

    private static IEnumerable<string> EnumerateExpectedPathChain(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        string normalizedPath = NormalizeExpectedMaterializedPath(platform, path);
        string root = platform == ConfigurationLayoutPlatform.Windows
            ? normalizedPath.Length >= 3
                && IsWindowsDriveLetter(normalizedPath[0])
                && normalizedPath[1] == ':'
                ? normalizedPath[..3]
                : string.Empty
            : normalizedPath.Length > 0 && normalizedPath[0] == '/' ? "/" : string.Empty;
        if (root.Length == 0)
        {
            yield break;
        }

        yield return root;
        if (PathsEqual(platform, normalizedPath, root))
        {
            yield break;
        }

        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        int componentStart = root.Length;
        while (componentStart < normalizedPath.Length)
        {
            int separatorIndex = normalizedPath.IndexOf(separator, componentStart);
            if (separatorIndex < 0)
            {
                yield return normalizedPath;
                yield break;
            }

            yield return normalizedPath[..separatorIndex];
            componentStart = separatorIndex + 1;
        }
    }

    private static string GetContainingDirectory(
        ConfigurationLayoutPlatform platform,
        string path
    )
    {
        char separator = platform == ConfigurationLayoutPlatform.Windows ? '\\' : '/';
        string normalizedPath = NormalizeExpectedMaterializedPath(platform, path);
        int separatorIndex = normalizedPath.LastIndexOf(separator);
        return separatorIndex < 0
            ? normalizedPath
            : separatorIndex == 0
                ? normalizedPath[..1]
                : separatorIndex == 2 && platform == ConfigurationLayoutPlatform.Windows
                    ? normalizedPath[..3]
                    : normalizedPath[..separatorIndex];
    }

    private static void AssertPlacement(
        Dictionary<FakeAdapterSurface, FakeAdapterPlacement> placements,
        FakeAdapterSurface surface,
        string expectedPlacementRoot,
        string expectedArtifactPath
    )
    {
        FakeAdapterPlacement placement = placements[surface];
        Assert.Equal(expectedPlacementRoot, placement.PlacementRoot);
        Assert.Equal(expectedArtifactPath, placement.ArtifactPath);
        Assert.Equal(FakeAdapterArtifactKind.File, placement.ArtifactKind);
    }

    private static void AssertResult(
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results,
        FakeAdapterSurface surface,
        FakeAdapterProbeStatus expectedStatus,
        FakeAdapterArtifactKind expectedActualKind
    )
    {
        FakeAdapterProbeResult result = results[surface];
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(FakeAdapterArtifactKind.File, result.ExpectedKind);
        Assert.Equal(expectedActualKind, result.ActualKind);
    }

    private static void AssertResult(
        Dictionary<FakeAdapterSurface, FakeAdapterProbeResult> results,
        FakeAdapterSurface surface,
        FakeAdapterProbeStatus expectedStatus,
        FakeAdapterArtifactKind expectedActualKind,
        FakeAdapterPlacement expectedPlacement
    )
    {
        AssertResult(results, surface, expectedStatus, expectedActualKind);
        FakeAdapterProbeResult result = results[surface];
        Assert.Equal(expectedPlacement.PlacementRoot, result.PlacementRoot);
        Assert.Equal(expectedPlacement.ArtifactPath, result.ArtifactPath);
    }

    private class DelegatingFileSystem : IFileSystem
    {
        protected readonly IFileSystem inner;

        public DelegatingFileSystem(IFileSystem inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool SupportsConditionalFileMutations => inner.SupportsConditionalFileMutations;

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string GetFullPath(string path) => inner.GetFullPath(path);

        public bool IsPathFullyQualified(string path) => inner.IsPathFullyQualified(path);

        public bool IsSymbolicLink(string path) => inner.IsSymbolicLink(path);

        public byte[] ComputeSha256Hash(string path) => inner.ComputeSha256Hash(path);

        public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path) =>
            inner.CaptureFileIntegritySnapshot(path);

        public bool FileMatchesIntegritySnapshot(string path, FileIntegritySnapshot snapshot) =>
            inner.FileMatchesIntegritySnapshot(path, snapshot);

        public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
            string path
        ) => inner.CaptureTrustedParentDirectorySnapshots(path);

        public FileSystemOwner GetCurrentOwner() => inner.GetCurrentOwner();

        public FileSystemOwner GetOwner(string path) => inner.GetOwner(path);

        public string ReadAllText(string path, Encoding? encoding = null) =>
            inner.ReadAllText(path, encoding);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            inner.WriteAllText(path, contents, encoding);

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => inner.AtomicWriteAllText(path, contents, encoding, options, expectation);

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => inner.AtomicWriteAllBytes(path, contents, options, expectation);

        public UnixFileMode GetUnixFileMode(string path) => inner.GetUnixFileMode(path);

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            inner.SetUnixFileMode(path, mode);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteFile(string path, FileMutationExpectation? expectation = null) =>
            inner.DeleteFile(path, expectation);

        public void DeleteDirectory(string path, bool recursive = false) =>
            inner.DeleteDirectory(path, recursive);

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateFiles(path, searchPattern, searchOption);

        public IEnumerable<string> EnumerateDirectories(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateDirectories(path, searchPattern, searchOption);
    }

    private sealed class DelegatingScaffoldMaterializationFileSystem
        : DelegatingFileSystem, IFakeAdapterScaffoldMaterializationFileSystem
    {
        private readonly InMemoryFileSystem scaffoldInner;

        public DelegatingScaffoldMaterializationFileSystem(InMemoryFileSystem inner)
            : base(inner)
        {
            scaffoldInner = inner;
        }

        public FileIntegritySnapshot AtomicWriteAllTextAndCaptureSnapshotNoFollow(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => scaffoldInner.AtomicWriteAllTextAndCaptureSnapshotNoFollow(
            path,
            contents,
            encoding,
            options,
            expectation
        );

        public FileIntegritySnapshot
        CaptureFileIntegritySnapshotWithoutTrustedParents(string path) =>
            scaffoldInner.CaptureFileIntegritySnapshotWithoutTrustedParents(path);

        public void CreateDirectoryNoFollow(string path) =>
            scaffoldInner.CreateDirectoryNoFollow(path);

        public void DeleteFileIfMatchesSnapshotNoFollow(
            string path,
            FileIntegritySnapshot snapshot
        ) => scaffoldInner.DeleteFileIfMatchesSnapshotNoFollow(path, snapshot);

        public void SetUnixFileModeNoFollow(string path, UnixFileMode mode) =>
            scaffoldInner.SetUnixFileModeNoFollow(path, mode);
    }
}
