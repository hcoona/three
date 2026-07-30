using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class NuGetPhase10VerticalSliceServiceTests
{
    [Fact]
    public async Task DryRunConfigurePlansCanonicalNuGetPluginLayoutMarker()
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
    public async Task ConfigureWritesMarkerAndManifestAndDoctorPassesWhenEntrypointExists()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");

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
            NuGetPhase10VerticalSliceService.MarkerValue,
            fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath)
        );
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.True(doctorResult.ConfigurationPlanValid);
        Assert.True(doctorResult.PluginLayoutMarkerPresent);
        Assert.True(doctorResult.OwnershipManifestPresent);
        Assert.True(doctorResult.NetCorePluginEntrypointPresent);
        Assert.True(doctorResult.PluginModeEntrypointResolvable);
        Assert.False(doctorResult.AzureArtifactsSourceCanonicalizationSuccess);
        Assert.False(doctorResult.InteractivePolicyGuidanceSuccess);
        Assert.True(doctorResult.OptionalEnvironmentOverridesAbsent);
    }

    [Fact]
    public async Task UnconfigureRemovesManifestAndLayoutMarker()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");
        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        NuGetPhase10UnconfigureResult result = await service.UnconfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.HadOwnedConfiguration);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult?.Operation);
        Assert.False(result.PluginLayoutMarkerPresent);
        Assert.False(result.OwnershipManifestPresent);
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
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
        fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "fake-assembly");
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

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
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
            NuGetPhase10VerticalSliceService.MarkerValue
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
    ) =>
        new(
            new NuGetPhase10VerticalSliceOptions
            {
                StateDirectoryPath = "/state/azureauth-credprovider/phase10",
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader,
            }
        );
}
