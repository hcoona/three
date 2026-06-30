using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
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
                        FileExists = files.Contains,
                        DirectoryExists = directories.Contains,
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
                .ProbePlacements(
                    new FakeAdapterDiscoveryContext
                    {
                        Layout = layout,
                        FileExists = fileSystem.FileExists,
                        DirectoryExists = fileSystem.DirectoryExists,
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
            FakeAdapterSurface.PythonKeyringBackend,
            FakeAdapterProbeStatus.WrongKind,
            FakeAdapterArtifactKind.Directory
        );
        Assert.NotEmpty(fileSystem.Calls);
        Assert.All(
            fileSystem.Calls,
            call =>
                Assert.True(
                    call.Operation is nameof(InMemoryFileSystem.FileExists)
                        or nameof(InMemoryFileSystem.DirectoryExists)
                )
        );
    }

    private static Dictionary<FakeAdapterSurface, FakeAdapterPlacement> GetPlacementsBySurface(
        ConfigurationLayoutProjectionContext context
    ) =>
        FakeAdapterDiscoveryScaffold.ProjectPlacements(context).ToDictionary(
            placement => placement.Surface
        );

    private static void AssertExpectedSurfaceSet(IEnumerable<FakeAdapterSurface> surfaces)
    {
        FakeAdapterSurface[] actualSurfaces = surfaces.OrderBy(static surface => surface).ToArray();
        Assert.Equal(ExpectedSurfaces, actualSurfaces);
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
}
