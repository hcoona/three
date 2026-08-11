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
    private const string F1LegacyMarker =
        "azureauth-credprovider nuget-plugin-layout\n"
        + "phase=10\n"
        + "runtime=netcore\n"
        + "entrypoint=azureauth-credprovider.dll\n";

    [Fact]
    public async Task ValidConfigureDryRunPlansActivationWithoutFilesystemMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.Calls.Clear();

        NuGetPhase10ConfigureDryRunResult result = await service.DryRunConfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(fileSystem.Calls, call => IsMutation(call.Operation));
        Assert.True(result.Validation.IsValid);
        Assert.Equal(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
        ConfigurationPlannedChange change = Assert.Single(result.PlanResult.Changes);
        Assert.Equal(ConfigurationChangeOperation.Set, change.Operation);
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, change.TargetKind);
        Assert.Equal(service.Paths.PluginTargetRootPath, change.TargetPathOrName);
        Assert.Equal("physical-target", change.Key);
        Assert.True(change.HasPlannedValue);
        Assert.False(fileSystem.DirectoryExists(service.Paths.PluginTargetRootPath));
        Assert.False(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.False(
            fileSystem.FileExists(service.Paths.PluginTargetRootPath + "/dependency.dll")
        );
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
    }

    [Fact]
    public async Task ConfiguredUnconfigureDryRunPreservesOwnedFilesWithoutFilesystemMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        string[] ownedPaths =
        [
            service.Paths.PluginEntrypointPath,
            service.Paths.PluginTargetRootPath + "/dependency.dll",
            service.Paths.PluginLayoutMarkerPath,
            service.Paths.OwnershipManifestPath,
        ];
        Dictionary<string, OwnedFileSnapshot> snapshots = ownedPaths.ToDictionary(
            path => path,
            path =>
                new OwnedFileSnapshot(
                    fileSystem.ReadAllBytes(path),
                    fileSystem.GetUnixFileMode(path)
                ),
            StringComparer.Ordinal
        );
        fileSystem.Calls.Clear();

        await service.ValidateUnconfigureDryRunAsync(
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(fileSystem.Calls, call => IsMutation(call.Operation));
        foreach ((string path, OwnedFileSnapshot snapshot) in snapshots)
        {
            Assert.True(fileSystem.FileExists(path));
            Assert.Equal(snapshot.Contents, fileSystem.ReadAllBytes(path));
            Assert.Equal(snapshot.Mode, fileSystem.GetUnixFileMode(path));
        }
        Assert.DoesNotContain(fileSystem.Calls, call => IsMutation(call.Operation));
    }

    [Fact]
    public async Task ConfigureMigratesExactF1LegacyActivationToJsonInventory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        string sourceRoot = service.Paths.ApplicationPayloadRootPath;
        string targetRoot = service.Paths.PluginTargetRootPath;
        fileSystem.AtomicWriteAllText(sourceRoot + "/shared/legacy.dll", "legacy-only");
        string ownershipBefore = await SeedExactF1LegacyActivationAsync(fileSystem, service);
        fileSystem.DeleteFile(sourceRoot + "/shared/legacy.dll");
        fileSystem.AtomicWriteAllText(sourceRoot + "/shared/current.dll", "current-only");

        NuGetPhase10ConfigureResult result = await service.ConfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Single(result.PlanResult.Changes);
        Assert.Equal(
            "fake-assembly",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
        Assert.Equal(
            "dependency",
            fileSystem.ReadAllText(service.Paths.PluginTargetRootPath + "/dependency.dll")
        );
        string marker = fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath);
        Assert.StartsWith("{", marker, StringComparison.Ordinal);
        Assert.Contains(
            "\"schemaVersion\": \"azureauth-credprovider-nuget-plugin-activation-v1\"",
            marker,
            StringComparison.Ordinal
        );
        Assert.Contains("\"dependency.dll\"", marker, StringComparison.Ordinal);
        Assert.Contains("\"shared/current.dll\"", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("\"shared/legacy.dll\"", marker, StringComparison.Ordinal);
        Assert.Equal(
            "legacy-only",
            fileSystem.ReadAllText(targetRoot + "/shared/legacy.dll")
        );
        Assert.Equal(
            "current-only",
            fileSystem.ReadAllText(targetRoot + "/shared/current.dll")
        );
        Assert.Equal(
            ownershipBefore,
            fileSystem.ReadAllText(service.Paths.OwnershipManifestPath)
        );
    }

    [Fact]
    public async Task UnconfigureRemovesOnlyExactF1LegacyEntrypointAndMarker()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await SeedExactF1LegacyActivationAsync(fileSystem, service);
        string unrelatedPath = service.Paths.PluginTargetRootPath + "/preserve.txt";
        fileSystem.AtomicWriteAllText(unrelatedPath, "unrelated");

        NuGetPhase10UnconfigureResult result = await service.UnconfigureAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.HadOwnedConfiguration);
        Assert.False(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.Equal(
            "dependency",
            fileSystem.ReadAllText(service.Paths.PluginTargetRootPath + "/dependency.dll")
        );
        Assert.Equal("unrelated", fileSystem.ReadAllText(unrelatedPath));
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("manifest")]
    public async Task DriftedF1LegacyStateRemainsUnrecognized(string scenario)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        string ownershipBefore = await SeedExactF1LegacyActivationAsync(fileSystem, service);
        string markerBefore = fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath);
        if (scenario == "marker")
        {
            markerBefore += " ";
            fileSystem.AtomicWriteAllText(service.Paths.PluginLayoutMarkerPath, markerBefore);
        }
        else
        {
            ownershipBefore += " ";
            fileSystem.AtomicWriteAllText(service.Paths.OwnershipManifestPath, ownershipBefore);
        }

        NuGetPhase10DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );
        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.UnconfigureAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal(NuGetConfigurationState.Unrecognized, doctor.ConfigurationState);
        Assert.Equal(
            "fake-assembly",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
        Assert.Equal(markerBefore, fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath));
        Assert.Equal(
            ownershipBefore,
            fileSystem.ReadAllText(service.Paths.OwnershipManifestPath)
        );
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

        Assert.Equal(ConfigurationPlanOperation.Apply, configureResult.PlanResult.Operation);
        Assert.Single(configureResult.PlanResult.Plan.Changes);
        Assert.Single(configureResult.PlanResult.Changes);
        Assert.Single(configureResult.PlanResult.PlannedOperations);
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
        Assert.Equal(NuGetConfigurationState.Valid, doctorResult.ConfigurationState);
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

    [Theory]
    [InlineData("missing-root")]
    [InlineData("missing-entrypoint")]
    [InlineData("source-contains-target")]
    [InlineData("target-contains-source")]
    [InlineData("unowned-file")]
    [InlineData("unowned-directory")]
    public async Task DryRunConfigureRejectsInvalidPayloadBeforeMutation(string scenario)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        InvalidNuGetPayloadFixture fixture = await CreateInvalidPayloadFixtureAsync(
            fileSystem,
            scenario
        );
        fileSystem.Calls.Clear();

        NuGetPhase10UnrecognizedStateException exception =
            await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
                await fixture.Service.DryRunConfigureAsync(
                    TestContext.Current.CancellationToken
                )
            );

        Assert.NotNull(exception.InnerException);
        Assert.Contains(
            fixture.ExpectedDiagnostic,
            exception.InnerException.Message,
            StringComparison.Ordinal
        );
        AssertRejectedConfigureWasNonMutating(fileSystem, fixture);
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("missing-entrypoint")]
    [InlineData("source-contains-target")]
    [InlineData("target-contains-source")]
    [InlineData("unowned-file")]
    [InlineData("unowned-directory")]
    public async Task ConfigureRejectsInvalidPayloadBeforePersistingOwnership(string scenario)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        InvalidNuGetPayloadFixture fixture = await CreateInvalidPayloadFixtureAsync(
            fileSystem,
            scenario
        );
        fileSystem.Calls.Clear();

        NuGetPhase10UnrecognizedStateException exception =
            await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
                await fixture.Service.ConfigureAsync(TestContext.Current.CancellationToken)
            );

        Assert.NotNull(exception.InnerException);
        Assert.Contains(
            fixture.ExpectedDiagnostic,
            exception.InnerException.Message,
            StringComparison.Ordinal
        );
        AssertRejectedConfigureWasNonMutating(fileSystem, fixture);
    }

    [Fact]
    public async Task ConfigureWhenAppliedStateIsCurrentPerformsNoWrites()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        string entrypointBefore = fileSystem.ReadAllText(service.Paths.PluginEntrypointPath);
        string markerBefore = fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath);
        string ownershipBefore = fileSystem.ReadAllText(service.Paths.OwnershipManifestPath);
        fileSystem.Calls.Clear();

        NuGetPhase10ConfigureResult result = await service.ConfigureAsync(
            TestContext.Current.CancellationToken
        );

        FileSystemCall[] ownedMutations = GetOwnedMutationCalls(fileSystem, service);
        Assert.Empty(ownedMutations);
        Assert.Equal(ConfigurationPlanOperation.Apply, result.PlanResult.Operation);
        Assert.Empty(result.PlanResult.Plan.Changes);
        Assert.Empty(result.PlanResult.Changes);
        Assert.Empty(result.PlanResult.PlannedOperations);
        Assert.Equal(entrypointBefore, fileSystem.ReadAllText(service.Paths.PluginEntrypointPath));
        Assert.Equal(markerBefore, fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath));
        Assert.Equal(ownershipBefore, fileSystem.ReadAllText(service.Paths.OwnershipManifestPath));
        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
    }

    [Fact]
    public async Task ConfigureWhenSourcePayloadChangesRefreshesActivationWithWrites()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        string targetRoot = service.Paths.PluginTargetRootPath;
        string sourceRoot = service.Paths.ApplicationPayloadRootPath;
        string unrelatedPath = targetRoot + "/preserve.txt";
        fileSystem.AtomicWriteAllText(unrelatedPath, "unrelated");
        fileSystem.AtomicWriteAllText(
            sourceRoot + "/azureauth-credprovider.dll",
            "source-b"
        );
        fileSystem.DeleteFile(sourceRoot + "/dependency.dll");
        fileSystem.AtomicWriteAllText(sourceRoot + "/new/addition.dll", "new-b");
        fileSystem.Calls.Clear();

        NuGetPhase10ConfigureResult result = await service.ConfigureAsync(
            TestContext.Current.CancellationToken
        );

        FileSystemCall[] mutationCalls = GetOwnedMutationCalls(fileSystem, service);
        Assert.Single(result.PlanResult.Plan.Changes);
        Assert.Single(result.PlanResult.Changes);
        Assert.Single(result.PlanResult.PlannedOperations);
        Assert.Equal(
            2,
            mutationCalls.Count(call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllBytes)
                && IsSameOrDescendant(call.Path, targetRoot)
            )
        );
        Assert.Single(
            mutationCalls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllText)
                && call.Path == service.Paths.PluginLayoutMarkerPath
        );
        Assert.Single(
            mutationCalls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.DeleteFile)
                && call.Path == targetRoot + "/dependency.dll"
        );
        Assert.Equal("source-b", fileSystem.ReadAllText(service.Paths.PluginEntrypointPath));
        Assert.Equal("new-b", fileSystem.ReadAllText(targetRoot + "/new/addition.dll"));
        Assert.False(fileSystem.FileExists(targetRoot + "/dependency.dll"));
        Assert.Equal("unrelated", fileSystem.ReadAllText(unrelatedPath));
        string marker = fileSystem.ReadAllText(service.Paths.PluginLayoutMarkerPath);
        Assert.Contains("new/addition.dll", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("dependency.dll", marker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureWhenTargetUnixModeDriftsRepairsModeWithWrites()
    {
        const UnixFileMode sourceMode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute;
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        string sourceEntrypoint =
            service.Paths.ApplicationPayloadRootPath + "/azureauth-credprovider.dll";
        fileSystem.SetUnixFileMode(sourceEntrypoint, sourceMode);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.SetUnixFileMode(
            service.Paths.PluginEntrypointPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite
        );
        fileSystem.Calls.Clear();

        await service.ConfigureAsync(TestContext.Current.CancellationToken);

        FileSystemCall[] mutationCalls = GetOwnedMutationCalls(fileSystem, service);
        Assert.Single(
            mutationCalls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.AtomicWriteAllBytes)
                && call.Path == service.Paths.PluginEntrypointPath
        );
        Assert.Single(
            mutationCalls,
            call =>
                call.Operation == nameof(InMemoryFileSystem.SetUnixFileMode)
                && call.Path == service.Paths.PluginEntrypointPath
        );
        Assert.Equal(sourceMode, fileSystem.GetUnixFileMode(service.Paths.PluginEntrypointPath));
        Assert.Equal(
            "fake-assembly",
            fileSystem.ReadAllText(service.Paths.PluginEntrypointPath)
        );
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

    [Theory]
    [InlineData("""{"schemaVersion":"azureauth-credprovider-nuget-plugin-activation-v1"}""")]
    [InlineData(F1LegacyMarker)]
    public async Task ConfigureRejectsProductOwnedMarkerWithoutManifest(string marker)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(service.Paths.PluginLayoutMarkerPath, marker);

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

        NuGetPhase10DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(NuGetConfigurationState.Unrecognized, doctor.ConfigurationState);
        Assert.False(doctor.ConfigurationPlanValid);
        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
    }

    private static NuGetPhase10VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        Func<string, string?>? environmentVariableReader = null,
        string? applicationRootPath = null,
        bool seedPayload = true
    )
    {
        string applicationRoot = applicationRootPath ?? "/installation/app";
        if (seedPayload)
        {
            fileSystem.AtomicWriteAllText(
                applicationRoot + "/azureauth-credprovider.dll",
                "fake-assembly"
            );
            fileSystem.AtomicWriteAllText(applicationRoot + "/dependency.dll", "dependency");
        }
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

    private static async Task<InvalidNuGetPayloadFixture> CreateInvalidPayloadFixtureAsync(
        InMemoryFileSystem fileSystem,
        string scenario
    )
    {
        NuGetPhase10VerticalSliceService service;
        string? foreignPath = null;
        const string foreignContents = "foreign";
        string expectedDiagnostic;
        switch (scenario)
        {
            case "missing-root":
                service = CreateService(
                    fileSystem,
                    applicationRootPath: "/missing/application",
                    seedPayload: false
                );
                expectedDiagnostic = "source application root is unavailable";
                break;
            case "missing-entrypoint":
                service = CreateService(fileSystem, seedPayload: false);
                fileSystem.AtomicWriteAllText(
                    service.Paths.ApplicationPayloadRootPath + "/dependency.dll",
                    "dependency"
                );
                expectedDiagnostic = "source application payload is incomplete";
                break;
            case "source-contains-target":
                service = CreateService(
                    fileSystem,
                    applicationRootPath: "/"
                );
                expectedDiagnostic = "source and activation roots must be disjoint";
                break;
            case "target-contains-source":
                NuGetPhase10VerticalSliceService layout = CreateService(
                    fileSystem,
                    seedPayload: false
                );
                service = CreateService(
                    fileSystem,
                    applicationRootPath: layout.Paths.PluginTargetRootPath + "/application"
                );
                expectedDiagnostic = "source and activation roots must be disjoint";
                break;
            case "unowned-file":
                service = CreateService(fileSystem);
                await service.ConfigureAsync(TestContext.Current.CancellationToken);
                fileSystem.AtomicWriteAllText(
                    service.Paths.ApplicationPayloadRootPath + "/new/conflict.dll",
                    "product"
                );
                foreignPath = service.Paths.PluginTargetRootPath + "/new/conflict.dll";
                fileSystem.AtomicWriteAllText(foreignPath, foreignContents);
                expectedDiagnostic = "overwrite an unowned path";
                break;
            case "unowned-directory":
                service = CreateService(fileSystem);
                await service.ConfigureAsync(TestContext.Current.CancellationToken);
                fileSystem.AtomicWriteAllText(
                    service.Paths.ApplicationPayloadRootPath + "/shared/addition.dll",
                    "product"
                );
                foreignPath = service.Paths.PluginTargetRootPath + "/shared";
                fileSystem.CreateDirectory(foreignPath);
                expectedDiagnostic = "use an unowned existing directory";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        return new InvalidNuGetPayloadFixture(
            service,
            ReadTextIfPresent(fileSystem, service.Paths.PluginLayoutMarkerPath),
            ReadTextIfPresent(fileSystem, service.Paths.OwnershipManifestPath),
            foreignPath,
            foreignContents,
            expectedDiagnostic
        );
    }

    private static async Task<string> SeedExactF1LegacyActivationAsync(
        InMemoryFileSystem fileSystem,
        NuGetPhase10VerticalSliceService service
    )
    {
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        string ownership = fileSystem.ReadAllText(service.Paths.OwnershipManifestPath);
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(ownership);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("phase10-nuget-plugin-layout", manifest.ManifestId);
        Assert.Equal("azureauth-credprovider", manifest.OwnerProductId);
        Assert.Equal(ConfigurationScope.User, manifest.Scope);
        Assert.Equal("nuget.plugin-layout", manifest.EntrySelector);
        Assert.Equal("phase10", manifest.ProductVersion);
        Assert.Empty(manifest.SafeMetadata);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, entry.TargetKind);
        Assert.Equal(service.Paths.PluginTargetRootPath, entry.TargetPathOrName);
        Assert.Equal("physical-target", entry.Key);
        Assert.Equal(
            ConfigurationOwnershipManifestSerializer.Serialize(manifest),
            ownership
        );
        fileSystem.AtomicWriteAllText(service.Paths.PluginLayoutMarkerPath, F1LegacyMarker);
        return ownership;
    }

    private static void AssertRejectedConfigureWasNonMutating(
        InMemoryFileSystem fileSystem,
        InvalidNuGetPayloadFixture fixture
    )
    {
        FileSystemCall[] mutationCalls = GetOwnedMutationCalls(fileSystem, fixture.Service);
        Assert.Empty(mutationCalls);
        AssertOptionalTextEquals(
            fileSystem,
            fixture.Service.Paths.PluginLayoutMarkerPath,
            fixture.MarkerBefore
        );
        AssertOptionalTextEquals(
            fileSystem,
            fixture.Service.Paths.OwnershipManifestPath,
            fixture.OwnershipBefore
        );
        if (fixture.ForeignPath is not null)
        {
            if (fileSystem.FileExists(fixture.ForeignPath))
            {
                Assert.Equal(
                    fixture.ForeignContents,
                    fileSystem.ReadAllText(fixture.ForeignPath)
                );
            }
            else
            {
                Assert.True(fileSystem.DirectoryExists(fixture.ForeignPath));
                Assert.Empty(fileSystem.EnumerateFiles(fixture.ForeignPath));
                Assert.Empty(fileSystem.EnumerateDirectories(fixture.ForeignPath));
            }
        }
    }

    private static FileSystemCall[] GetOwnedMutationCalls(
        InMemoryFileSystem fileSystem,
        NuGetPhase10VerticalSliceService service
    ) =>
        fileSystem
            .Calls.Where(call =>
                IsMutation(call.Operation)
                && (
                    IsSameOrDescendant(call.Path, service.Paths.PluginTargetRootPath)
                    || IsSameOrDescendant(call.Path, service.Paths.StateDirectoryPath)
                )
            )
            .ToArray();

    private static bool IsMutation(string operation) =>
        operation
            is nameof(InMemoryFileSystem.AtomicWriteAllBytes)
                or nameof(InMemoryFileSystem.AtomicWriteAllText)
                or nameof(InMemoryFileSystem.WriteAllText)
                or nameof(InMemoryFileSystem.SetUnixFileMode)
                or nameof(InMemoryFileSystem.CreateDirectory)
                or nameof(InMemoryFileSystem.DeleteFile)
                or nameof(InMemoryFileSystem.DeleteDirectory);

    private static bool IsSameOrDescendant(string path, string root) =>
        string.Equals(path, root, StringComparison.Ordinal)
        || path.StartsWith(root + "/", StringComparison.Ordinal);

    private static string? ReadTextIfPresent(InMemoryFileSystem fileSystem, string path) =>
        fileSystem.FileExists(path) ? fileSystem.ReadAllText(path) : null;

    private static void AssertOptionalTextEquals(
        InMemoryFileSystem fileSystem,
        string path,
        string? expected
    )
    {
        if (expected is null)
        {
            Assert.False(fileSystem.FileExists(path));
        }
        else
        {
            Assert.Equal(expected, fileSystem.ReadAllText(path));
        }
    }

    private sealed record InvalidNuGetPayloadFixture(
        NuGetPhase10VerticalSliceService Service,
        string? MarkerBefore,
        string? OwnershipBefore,
        string? ForeignPath,
        string ForeignContents,
        string ExpectedDiagnostic
    );

    private sealed record OwnedFileSnapshot(byte[] Contents, UnixFileMode Mode);

    private static string GetIsolatedHomePath() =>
        Path.Combine(
            Path.GetPathRoot(Environment.CurrentDirectory)
                ?? Path.DirectorySeparatorChar.ToString(),
            "azureauth-credprovider-tests",
            "user-home"
        );

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

    [Fact]
    public async Task DoctorClassifiesStaleSourceAsRefreshable()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.AtomicWriteAllText(
            service.Paths.ApplicationPayloadRootPath + "/azureauth-credprovider.dll",
            "source-b"
        );

        NuGetPhase10DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(NuGetConfigurationState.Refreshable, result.ConfigurationState);
        Assert.False(result.ConfigurationPlanValid);
        Assert.Equal("fake-assembly", fileSystem.ReadAllText(service.Paths.PluginEntrypointPath));
        Assert.True(result.PluginLayoutMarkerPresent);
        Assert.True(result.OwnershipManifestPresent);
    }

    [Theory]
    [InlineData("target-root-file")]
    [InlineData("orphan-entrypoint-file")]
    [InlineData("entrypoint-directory")]
    [InlineData("ownership-manifest-directory")]
    [InlineData("marker-directory")]
    public async Task DoctorAndConfigureRejectOwnershiplessCollision(string scenario)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        ApplyOwnershiplessCollision(fileSystem, service, scenario);

        NuGetPhase10DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(NuGetConfigurationState.Unrecognized, doctor.ConfigurationState);
        Assert.False(doctor.ConfigurationPlanValid);
        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task ConfigureAndDoctorPreserveMarkerlessDirectoryWithOnlyUnrelatedFiles()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        fileSystem.AtomicWriteAllText(
            service.Paths.PluginTargetRootPath + "/unrelated.txt",
            "unrelated"
        );

        NuGetPhase10ConfigureResult configure = await service.ConfigureAsync(
            TestContext.Current.CancellationToken
        );
        NuGetPhase10DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Single(configure.PlanResult.Changes);
        Assert.Equal(NuGetConfigurationState.Valid, doctor.ConfigurationState);
        Assert.True(doctor.ConfigurationPlanValid);
        Assert.Equal(
            "unrelated",
            fileSystem.ReadAllText(service.Paths.PluginTargetRootPath + "/unrelated.txt")
        );
        Assert.True(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.True(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.PluginLayoutMarkerPath));
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.OwnershipManifestPath));
    }

    [Fact]
    public async Task ConfigureAndDoctorRejectOwnershipManifestClaimWithoutMarker()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        NuGetPhase10VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(TestContext.Current.CancellationToken);
        fileSystem.DeleteFile(service.Paths.PluginLayoutMarkerPath);

        NuGetPhase10DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(NuGetConfigurationState.Unrecognized, doctor.ConfigurationState);
        Assert.False(doctor.ConfigurationPlanValid);
        await Assert.ThrowsAsync<NuGetPhase10UnrecognizedStateException>(async () =>
            await service.ConfigureAsync(TestContext.Current.CancellationToken)
        );
        Assert.True(fileSystem.FileExists(service.Paths.OwnershipManifestPath));
        Assert.True(fileSystem.FileExists(service.Paths.PluginEntrypointPath));
        Assert.False(fileSystem.FileExists(service.Paths.PluginLayoutMarkerPath));
    }

    private static void ApplyOwnershiplessCollision(
        InMemoryFileSystem fileSystem,
        NuGetPhase10VerticalSliceService service,
        string scenario
    )
    {
        switch (scenario)
        {
            case "target-root-file":
                fileSystem.AtomicWriteAllText(service.Paths.PluginTargetRootPath, "foreign");
                break;
            case "orphan-entrypoint-file":
                fileSystem.AtomicWriteAllText(service.Paths.PluginEntrypointPath, "foreign");
                break;
            case "entrypoint-directory":
                fileSystem.CreateDirectory(service.Paths.PluginEntrypointPath);
                break;
            case "ownership-manifest-directory":
                fileSystem.CreateDirectory(service.Paths.OwnershipManifestPath);
                break;
            case "marker-directory":
                fileSystem.CreateDirectory(service.Paths.PluginLayoutMarkerPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
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
