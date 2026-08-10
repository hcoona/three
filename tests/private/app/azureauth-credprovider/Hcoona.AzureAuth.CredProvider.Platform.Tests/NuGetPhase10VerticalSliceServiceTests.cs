using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class NuGetPhase10VerticalSliceServiceTests
{
    [Fact]
    public async Task DryRunConfigurePlansCanonicalNuGetPluginActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);

        NuGetPhase10ConfigureDryRunResult result = await service.DryRunConfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Validation.IsValid);
        Assert.Equal(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
        ConfigurationPlannedChange change = Assert.Single(result.PlanResult.Changes);
        Assert.Equal(ConfigurationChangeOperation.Set, change.Operation);
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, change.TargetKind);
        Assert.Equal(service.Paths.PluginTargetRootPath, change.TargetPathOrName);
        Assert.Equal("physical-target", change.Key);
        Assert.True(change.HasPlannedValue);
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
    }

    [Fact]
    public async Task ConfigureCopiesPayloadAndWritesOwnershipAndDoctorPasses()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);

        NuGetPhase10ConfigureResult configureResult = await service.ConfigureAsync(
            TestContext.Current.CancellationToken
        );
        NuGetPhase10DoctorResult doctorResult = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, configureResult.PlanResult.Operation);
        Assert.True(configureResult.PluginLayoutMarkerPresent);
        Assert.True(configureResult.OwnershipManifestPresent);
        Assert.Equal(
            "fake-assembly",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
        Assert.Contains(
            "azureauth-credprovider-nuget-plugin-activation-v1",
            fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath),
            StringComparison.Ordinal
        );
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.True(doctorResult.ConfigurationPlanValid);
        Assert.True(doctorResult.PluginLayoutMarkerPresent);
        Assert.True(doctorResult.OwnershipManifestPresent);
        Assert.True(doctorResult.NetCorePluginEntrypointPresent);
        Assert.True(doctorResult.PluginModeEntrypointResolvable);
        Assert.True(doctorResult.AzureArtifactsSourceCanonicalizationSuccess);
        Assert.True(doctorResult.InteractivePolicyGuidanceSuccess);
        Assert.True(doctorResult.OptionalEnvironmentOverridesAbsent);
    }

    [Fact]
    public async Task UnconfigureRemovesOnlyOwnedActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.AtomicWriteAllText(
            service.Paths.PluginTargetRootPath + "/preserve.txt",
            "unrelated"
        );

        NuGetPhase10UnconfigureResult result = await service.UnconfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.HadOwnedConfiguration);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult?.Operation);
        Assert.False(result.PluginLayoutMarkerPresent);
        Assert.False(result.OwnershipManifestPresent);
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.False(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.Equal(
            "unrelated",
            fileSystem.ReadAllText(service.Paths.PluginTargetRootPath + "/preserve.txt")
        );

        NuGetPhase10UnconfigureResult repeated = await service.UnconfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(repeated.HadOwnedConfiguration);
        Assert.Null(repeated.PlanResult);
        Assert.False(repeated.PluginLayoutMarkerPresent);
        Assert.False(repeated.OwnershipManifestPresent);
        Assert.Equal(
            "unrelated",
            fileSystem.ReadAllText(service.Paths.PluginTargetRootPath + "/preserve.txt")
        );
    }

    [Fact]
    public async Task UnconfigureRejectsDamagedOwnedActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "damaged");

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.UnconfigureAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal("damaged", fileSystem.ReadAllText(service.Paths.PluginEntrypointPath));
        Assert.True(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
    }

    [Fact]
    public async Task UnconfigureRejectsCanonicalPayloadWithoutOwnershipManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "orphaned-payload");

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.UnconfigureAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(
            "orphaned-payload",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
    }

    [Fact]
    public async Task ReconfigureRefreshesActivationAndUnconfigurePreservesUnrelatedState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        string applicationRoot = service.Paths.ApplicationPayloadRootPath;
        string targetRoot = service.Paths.PluginTargetRootPath;
        fileSystem.AtomicWriteAllText(
            applicationRoot + "/azureauth-credprovider.dll",
            "entrypoint-v1"
        );
        fileSystem.AtomicWriteAllText(applicationRoot + "/old/obsolete.dll", "old-only");

        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.AtomicWriteAllText(targetRoot + "/preserve.txt", "unrelated");
        fileSystem.CreateDirectory(targetRoot + "/unrelated/empty/subtree");

        fileSystem.AtomicWriteAllText(
            applicationRoot + "/azureauth-credprovider.dll",
            "entrypoint-v2"
        );
        fileSystem.DeleteFile(applicationRoot + "/old/obsolete.dll");
        fileSystem.AtomicWriteAllText(applicationRoot + "/new/addition.dll", "new-only");

        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            "entrypoint-v2",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
        Assert.False(fileSystem.FileExists(targetRoot + "/old/obsolete.dll"));
        Assert.False(fileSystem.DirectoryExists(targetRoot + "/old"));
        Assert.Equal("new-only", fileSystem.ReadAllText(targetRoot + "/new/addition.dll"));
        Assert.Equal("unrelated", fileSystem.ReadAllText(targetRoot + "/preserve.txt"));
        Assert.True(fileSystem.DirectoryExists(targetRoot + "/unrelated/empty/subtree"));
        string refreshedInventory = fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath);
        Assert.Contains("new/addition.dll", refreshedInventory, StringComparison.Ordinal);
        Assert.DoesNotContain("old/obsolete.dll", refreshedInventory, StringComparison.Ordinal);

        await service.UnconfigureAsync(TestContext.Current.CancellationToken);

        Assert.False(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.False(fileSystem.FileExists(targetRoot + "/dependency.dll"));
        Assert.False(fileSystem.FileExists(targetRoot + "/new/addition.dll"));
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.Equal("unrelated", fileSystem.ReadAllText(targetRoot + "/preserve.txt"));
        Assert.True(fileSystem.DirectoryExists(targetRoot + "/unrelated/empty/subtree"));
    }

    [Fact]
    public async Task RefreshRejectsNewFileUnderPreExistingUnownedDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        string targetRoot = service.Paths.PluginTargetRootPath;
        string sharedDirectory = targetRoot + "/shared";
        string originalMarker = fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath);
        fileSystem.CreateDirectory(sharedDirectory);
        fileSystem.AtomicWriteAllText(
            service.Paths.ApplicationPayloadRootPath + "/shared/transient.dll",
            "transient"
        );

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.DirectoryExists(sharedDirectory));
        Assert.False(fileSystem.FileExists(sharedDirectory + "/transient.dll"));
        Assert.Equal(
            "fake-assembly",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
        Assert.Equal(originalMarker, fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath));

        await service.UnconfigureAsync(TestContext.Current.CancellationToken);

        Assert.True(fileSystem.DirectoryExists(sharedDirectory));
        Assert.Empty(fileSystem.EnumerateFiles(sharedDirectory));
        Assert.Empty(fileSystem.EnumerateDirectories(sharedDirectory));
    }

    [Fact]
    public async Task UnconfigureOwnershipDeletionFailureLeavesRetryableActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        string unrelatedPath = service.Paths.PluginTargetRootPath + "/preserve.txt";
        fileSystem.AtomicWriteAllText(unrelatedPath, "unrelated");
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.DeleteFile),
            service.Paths.OwnershipManifestPath,
            1,
            new IOException("Injected ownership manifest deletion failure.")
        );

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.UnconfigureAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal("fake-assembly", fileSystem.ReadAllText(service.Paths.PluginEntrypointPath));
        Assert.True(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.Equal("unrelated", fileSystem.ReadAllText(unrelatedPath));

        NuGetPhase10UnconfigureResult retry = await service.UnconfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(retry.HadOwnedConfiguration);
        Assert.False(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.Equal("unrelated", fileSystem.ReadAllText(unrelatedPath));
    }

    [Fact]
    public async Task DoctorReportsEnvironmentOverrideConflict()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(
            fileSystem,
            name =>
                string.Equals(name, "NUGET_NETCORE_PLUGIN_PATHS", StringComparison.Ordinal)
                    ? "/tmp/plugin.dll"
                    : null
        );
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(result.OptionalEnvironmentOverridesAbsent);
        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
    }

    [Fact]
    public async Task DoctorReportsMissingPluginEntrypoint()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.DeleteFile(service.Paths.PluginEntrypointPath);

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
        Assert.False(result.ConfigurationPlanValid);
        Assert.False(result.NetCorePluginEntrypointPresent);
        Assert.False(result.PluginModeEntrypointResolvable);
    }

    [Fact]
    public async Task ConfigureRejectsProductOwnedMarkerWithoutManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(
            service.Paths.PluginLayoutMarkerPath,
            """{"schemaVersion":"azureauth-credprovider-nuget-plugin-activation-v1"}"""
        );

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task ConfigureTreatsMalformedOwnershipManifestAsUnrecognizedState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.OwnershipManifestPath, "{");

        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
    }

    private static NuGetPhase10VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        Func<string, string?>? environmentVariableReader = null
    )
    {
        const string applicationRoot = "/installation/app";
        fileSystem.AtomicWriteAllText(
            applicationRoot + "/azureauth-credprovider.dll",
            "fake-assembly"
        );
        fileSystem.AtomicWriteAllText(applicationRoot + "/dependency.dll", "dependency");
        return new(
            new NuGetPhase10VerticalSliceOptions
            {
                StateDirectoryPath = "/state/azureauth-credprovider/phase10",
                ApplicationPayloadRootPath = applicationRoot,
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader ?? (_ => null),
            }
        );
    }

    [Fact]
    public async Task DoctorValidatesParserWithoutCredentialAcquisition()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        var acquisition = new ThrowingNuGetDoctorCredentialAcquisitionService();
        const string applicationRoot = "/installation/app";
        fileSystem.AtomicWriteAllText(
            applicationRoot + "/azureauth-credprovider.dll",
            "fake-assembly"
        );
        var service = new NuGetPhase10VerticalSliceService(
            new NuGetPhase10VerticalSliceOptions
            {
                StateDirectoryPath = "/state/azureauth-credprovider/phase10",
                ApplicationPayloadRootPath = applicationRoot,
                FileSystem = fileSystem,
                EnvironmentVariableReader = _ => null,
                CredentialAcquisition =
                    new BoundedCredentialAcquisitionAdapter(acquisition),
            }
        );
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, acquisition.InvocationCount);
        Assert.True(result.ConfigurationPlanValid);
        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
        Assert.True(result.NetCorePluginEntrypointPresent);
        Assert.True(result.PluginModeEntrypointResolvable);
        Assert.True(result.AzureArtifactsSourceCanonicalizationSuccess);
        Assert.True(result.InteractivePolicyGuidanceSuccess);
        Assert.True(result.OptionalEnvironmentOverridesAbsent);
    }

    private sealed class ThrowingNuGetDoctorCredentialAcquisitionService
        : Hcoona.AzureAuth.CredProvider.Platform.Composition.ICredentialAcquisitionService
    {
        public int InvocationCount { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            throw new InvalidOperationException(
                "NuGet doctor must classify sources without acquiring credentials."
            );
        }
    }
}
