using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationNuGetPluginLayoutPhysicalWriterPhase4DTests
{
    private const string MarkerFileName = ".azureauth-credprovider.nuget-plugin-layout";

    private static string CreateCanonicalNuGetPluginLayoutTargetRoot(string? userName = null)
    {
        string homeDirectory = GetCurrentUserProfileDirectory();
        if (userName is null)
        {
            return Path.Combine(
                homeDirectory,
                ".nuget",
                "plugins",
                "netcore",
                "azureauth-credprovider"
            );
        }

        string? parentDirectory = Path.GetDirectoryName(homeDirectory);
        string userProfileDirectory = string.IsNullOrEmpty(parentDirectory)
            ? Path.Combine(homeDirectory, userName)
            : Path.Combine(parentDirectory, userName);
        return Path.Combine(
            userProfileDirectory,
            ".nuget",
            "plugins",
            "netcore",
            "azureauth-credprovider"
        );
    }

    private static string GetCurrentUserProfileDirectory()
    {
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.TrimEndingDirectorySeparator(userProfile);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(windowsUserProfile))
            {
                return Path.TrimEndingDirectorySeparator(windowsUserProfile);
            }

            string? homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            string? homePath = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            {
                return Path.TrimEndingDirectorySeparator(homeDrive + homePath);
            }
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.TrimEndingDirectorySeparator(home);
            }
        }

        throw new InvalidOperationException("User profile directory is unavailable.");
    }

    private static string CreateTraversalNuGetPluginLayoutTargetRoot()
    {
        string homeDirectory = GetCurrentUserProfileDirectory();
        string? parentDirectory = Path.GetDirectoryName(homeDirectory);
        string homeLeaf = Path.GetFileName(homeDirectory);
        return Path.Combine(
            string.IsNullOrWhiteSpace(parentDirectory) ? homeDirectory : parentDirectory,
            homeLeaf,
            "..",
            homeLeaf,
            ".nuget",
            "plugins",
            "netcore",
            "azureauth-credprovider"
        );
    }

    [Fact]
    public async Task DryRunDoesNotMutateNuGetPluginLayoutMarkerOrManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-dry-run-manifest.json";
        string rawTargetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string canonicalTargetPath = fileSystem.GetFullPath(rawTargetPath);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(rawTargetPath);

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, entry.TargetKind);
        Assert.Equal(canonicalTargetPath, entry.TargetPathOrName);
        Assert.Equal("physical-target", entry.Key);
        Assert.Equal(HashMetadata("planned-value"), entry.PlannedValueSha256);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(Path.Combine(canonicalTargetPath, MarkerFileName)));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task ApplyWritesNuGetPluginLayoutMarkerAndManifestEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-apply-manifest.json";
        string rawTargetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string canonicalTargetPath = fileSystem.GetFullPath(rawTargetPath);
        string markerPath = Path.Combine(canonicalTargetPath, MarkerFileName);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(rawTargetPath);

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal("planned-value", fileSystem.ReadAllText(markerPath));
        Assert.True(fileSystem.FileExists(manifestPath));
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(ConfigurationTargetKind.NuGetPluginLayout, entry.TargetKind);
        Assert.Equal(canonicalTargetPath, entry.TargetPathOrName);
        Assert.Equal("physical-target", entry.Key);
        Assert.Equal(HashMetadata("planned-value"), entry.PlannedValueSha256);
        Assert.True(fileSystem.DirectoryExists(canonicalTargetPath));
    }

    [Fact]
    public void ValidateRejectsDanglingNuGetPluginLayoutMarkerSymlinkWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        fileSystem.CreateDirectory(targetPath);
        fileSystem.AddSymbolicLink(markerPath, "/missing/nuget-plugin-layout-marker");
        fileSystem.Calls.Clear();
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.NuGetPluginLayout,
            plan.Changes,
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(fileSystem.IsSymbolicLink(markerPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DryRunRejectsDanglingNuGetPluginLayoutMarkerSymlinkWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-dangling-marker-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        fileSystem.CreateDirectory(targetPath);
        fileSystem.AddSymbolicLink(markerPath, "/missing/nuget-plugin-layout-marker");
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.True(fileSystem.IsSymbolicLink(markerPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task RemoveLeavesEmptyNuGetPluginLayoutMarkerAndPreservesUnrelatedFiles()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-remove-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        const string unrelatedFile = "/config/nuget-plugin-layout/other.txt";
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateNuGetPluginLayoutPlan(targetPath);

        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string existingManifestJson = fileSystem.ReadAllText(manifestPath);
        fileSystem.AtomicWriteAllText(unrelatedFile, "keep-me");
        fileSystem.Calls.Clear();

        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = HashMetadata("planned-value"),
                },
            ],
        };

        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Null(result.OwnershipManifest);
        Assert.True(fileSystem.FileExists(unrelatedFile));
        Assert.Equal("keep-me", fileSystem.ReadAllText(unrelatedFile));
        Assert.True(fileSystem.FileExists(markerPath));
        Assert.Equal(string.Empty, fileSystem.ReadAllText(markerPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveThenApplyReinstallsNuGetPluginLayoutMarker()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-reinstall-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateNuGetPluginLayoutPlan(targetPath);

        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string existingManifestJson = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                    PreviousOwnedEntryMetadata = HashMetadata("planned-value"),
                },
            ],
        };

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);
        Assert.True(fileSystem.FileExists(markerPath));
        Assert.Equal(string.Empty, fileSystem.ReadAllText(markerPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        fileSystem.Calls.Clear();

        ConfigurationPlanResult reinstallResult = await manager.ApplyAsync(
            applyPlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, reinstallResult.State);
        Assert.Equal("planned-value", fileSystem.ReadAllText(markerPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public void WriteRejectsMarkerAppearingAfterValidationWithoutRetainedOwnershipProof()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.NuGetPluginLayout,
            plan.Changes,
            []
        );

        writer.Validate(request, TestContext.Current.CancellationToken);
        fileSystem.AtomicWriteAllText(markerPath, "intruder-value");
        fileSystem.Calls.Clear();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.Equal("intruder-value", fileSystem.ReadAllText(markerPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DryRunRejectsUnsupportedNuGetPluginLayoutKeyWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-unsupported-key-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(
            targetPath,
            key: "unexpected-key"
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "canonical physical target key",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("/var/cache/.nuget/plugins/netcore/azureauth-credprovider")]
    public async Task DryRunRejectsOffTreeNuGetPluginLayoutTargetRootWithoutMutation(
        string targetPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-off-tree-manifest.json";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(null)]
    public async Task DryRunRejectsDotSegmentTraversalNuGetPluginLayoutTargetRootWithoutMutation(
        string? targetPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-traversal-manifest.json";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(
            targetPath ?? CreateTraversalNuGetPluginLayoutTargetRoot()
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        DryRunRejectsNuGetPluginLayoutTargetParentSymlinkOrReparsePointWithoutMutation(
            bool useSymbolicLink
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-parent-link-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string parentPath = GetCurrentUserProfileDirectory();
        fileSystem.CreateDirectory(Path.GetDirectoryName(parentPath)!);
        if (useSymbolicLink)
        {
            fileSystem.CreateDirectory("/outside");
            fileSystem.AddSymbolicLink(parentPath, "/outside");
        }
        else
        {
            fileSystem.CreateDirectory(parentPath);
            fileSystem.MarkAsNonSymbolicReparsePoint(parentPath);
        }

        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("parent path", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DryRunRejectsNuGetPluginLayoutTargetSymlinkWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-target-link-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string targetParentPath = Path.GetDirectoryName(targetPath)!;
        fileSystem.CreateDirectory(targetParentPath);
        fileSystem.AddSymbolicLink(targetPath, "/outside/nuget-plugin-layout");
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public void
        ValidateRetainedOwnershipProofsRejectsOffTreeNuGetPluginLayoutTargetRootWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot("bob");
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        fileSystem.AtomicWriteAllText(markerPath, "planned-value");
        fileSystem.Calls.Clear();
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetOwnershipProof proof = new(
            ConfigurationTargetKind.NuGetPluginLayout,
            targetPath,
            "physical-target",
            HashMetadata("planned-value")
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.ValidateRetainedOwnershipProofs([proof], TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "official per-user plugin convention root",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(fileSystem.Calls);
    }

    [Fact]
    public async Task ApplyRollsBackNuGetPluginLayoutMarkerOnDispatchFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/nuget-rollback-manifest.json";
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        string markerPath = Path.Combine(targetPath, MarkerFileName);
        fileSystem.AtomicWriteAllText(markerPath, "existing-value");
        ConfigurationChangePlan existingPlan = CreateNuGetPluginLayoutPlan(
            targetPath,
            value: "existing-value"
        );
        ConfigurationOwnershipManifest existingManifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(
                existingPlan,
                ConfigurationPlanProjector.CreatePlannedOperations(existingPlan)
            );
        string existingManifestJson =
            ConfigurationOwnershipManifestSerializer.Serialize(existingManifest);
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        var manager = CreateManager(
            fileSystem,
            manifestPath,
            new ThrowingAfterDispatchDispatcher(fileSystem)
        );
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("Injected dispatch failure", exception.Message, StringComparison.Ordinal);
        Assert.Equal("existing-value", fileSystem.ReadAllText(markerPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public void ValidateRejectsWhitespaceNuGetPluginLayoutValueWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(targetPath, value: "   ");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.NuGetPluginLayout,
            plan.Changes,
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-empty", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fileSystem.FileExists(Path.Combine(targetPath, MarkerFileName)));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public void ValidateRejectsSecretNuGetPluginLayoutValueWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        string targetPath = CreateCanonicalNuGetPluginLayoutTargetRoot();
        ConfigurationChangePlan plan = CreateNuGetPluginLayoutPlan(
            targetPath,
            isSecretValue: true
        );
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.NuGetPluginLayout,
            plan.Changes,
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fileSystem.FileExists(Path.Combine(targetPath, MarkerFileName)));
        AssertNoNuGetPluginLayoutPhysicalMutationCalls(fileSystem.Calls);
    }

    private static ConfigurationManager CreateManager(
        IFileSystem fileSystem,
        string manifestPath,
        IConfigurationPhysicalTargetWriterDispatcher? dispatcher = null
    ) =>
        dispatcher is null
            ? new ConfigurationManager(
                fileSystem,
                manifestPath,
                new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
            )
            : new ConfigurationManager(fileSystem, manifestPath, dispatcher);

    private static ConfigurationChangePlan CreateNuGetPluginLayoutPlan(
        string targetPath,
        string key = "physical-target",
        string? value = "planned-value",
        bool isSecretValue = false
    ) =>
        ConfigurationChangePlanPolicy.Create(
            "plan-nuget-plugin-layout-physical-target",
            "changeset-nuget-plugin-layout-physical-target",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest() with
            {
                ManifestId = "manifest-nuget-plugin-layout-physical-target",
                EntrySelector = "nuget.physical-target",
            },
            [
                new ConfigurationChange
                {
                    Operation = value is null
                        ? ConfigurationChangeOperation.Remove
                        : ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
                    TargetPathOrName = targetPath,
                    Key = key,
                    Value = value,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                    IsSecretValue = isSecretValue,
                },
            ]
        );

    private static ConfigurationManifestMetadata CreateManifest() =>
        new()
        {
            ManifestId = "manifest-nuget-plugin-layout",
            OwnerProductId = "azureauth-credprovider",
            EntrySelector = "nuget.physical-target",
            ProductVersion = "0.0.0-test",
        };

    private static string HashMetadata(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AssertNoNuGetPluginLayoutPhysicalMutationCalls(
        IReadOnlyCollection<FileSystemCall> calls
    )
    {
        Assert.DoesNotContain(
            calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
        );
    }

    private sealed class ThrowingAfterDispatchDispatcher(IFileSystem fileSystem)
        : IConfigurationPhysicalTargetWriterDispatcher,
            IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy,
            IConfigurationPhysicalTargetWriterDispatcherValidator,
            IConfigurationPhysicalTargetRetainedOwnershipProofValidator
    {
        private readonly ConfigurationPhysicalTargetWriterDispatcher builtIn = new(fileSystem);

        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => true;

        public void Validate(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        ) => builtIn.Validate(request, cancellationToken);

        public void ValidateRetainedOwnershipProofs(
            IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
            CancellationToken cancellationToken
        ) => builtIn.ValidateRetainedOwnershipProofs(ownershipProofs, cancellationToken);

        public async ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            await builtIn.Dispatch(request, cancellationToken);
            throw new InvalidOperationException("Injected dispatch failure.");
        }
    }
}
