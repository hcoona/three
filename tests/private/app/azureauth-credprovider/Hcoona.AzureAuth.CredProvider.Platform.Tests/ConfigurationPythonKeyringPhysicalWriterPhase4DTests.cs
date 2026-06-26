using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationPythonKeyringPhysicalWriterPhase4DTests
{
    public static TheoryData<ConfigurationTargetKind> PythonPhysicalTargetKinds =>
        new()
        {
            ConfigurationTargetKind.PythonKeyringBackend,
            ConfigurationTargetKind.KeyringShim,
        };

    public static TheoryData<ConfigurationTargetKind, bool> PythonLinkSafetyCases
    {
        get
        {
            var cases = new TheoryData<ConfigurationTargetKind, bool>();
            foreach (ConfigurationTargetKind targetKind in PythonPhysicalTargetKinds)
            {
                cases.Add(targetKind, true);
                cases.Add(targetKind, false);
            }

            return cases;
        }
    }

    public static TheoryData<ConfigurationTargetKind, bool> PythonManifestCollisionCases
    {
        get
        {
            var cases = new TheoryData<ConfigurationTargetKind, bool>();
            foreach (ConfigurationTargetKind targetKind in PythonPhysicalTargetKinds)
            {
                cases.Add(targetKind, false);
                cases.Add(targetKind, true);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task DryRunDoesNotMutatePythonKeyringTargetOrManifest(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-dry-run-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath);

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(targetKind, entry.TargetKind);
        Assert.Equal(targetPath, entry.TargetPathOrName);
        Assert.Equal("physical-target", entry.Key);
        Assert.Equal(HashMetadata("planned-value"), entry.PlannedValueSha256);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task ApplyWritesPythonKeyringTargetAndManifestEntries(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-apply-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath);

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
        UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (!OperatingSystem.IsWindows() && targetKind == ConfigurationTargetKind.KeyringShim)
        {
            expectedMode |= UnixFileMode.UserExecute;
        }

        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(targetKind, entry.TargetKind);
        Assert.Equal(targetPath, entry.TargetPathOrName);
        Assert.Equal("physical-target", entry.Key);
        Assert.Equal(HashMetadata("planned-value"), entry.PlannedValueSha256);
    }

    [Fact]
    public async Task ValidatePlanDryRunApplyAndReapplyKeyringShimUnderCustomXdgDataHome()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? originalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string customXdgDataHome = Path.Combine(
            GetCurrentUserProfileDirectory(),
            ".azureauth-credprovider-custom-xdg-data-home"
        );
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", customXdgDataHome);

        try
        {
            var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
            string targetPath = GetCanonicalPythonTargetPath(ConfigurationTargetKind.KeyringShim);
            Assert.Equal(
                Path.Combine(
                    customXdgDataHome,
                    "azureauth-credprovider",
                    "keyring-shim",
                    "keyring"
                ),
                targetPath
            );

            string manifestPath = CreateStatePath("python-keyring-shim-custom-xdg.json");
            var manager = CreateManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreatePythonPlan(
                ConfigurationTargetKind.KeyringShim,
                targetPath
            );

            ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

            Assert.True(validationResult.IsValid);
            Assert.Null(validationResult.Violation);

            ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
                plan,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(ConfigurationPlanState.Planned, dryRunResult.State);
            Assert.False(fileSystem.FileExists(manifestPath));
            Assert.False(fileSystem.FileExists(targetPath));
            AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);

            ConfigurationPlanResult applyResult = await manager.ApplyAsync(
                plan,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(ConfigurationPlanState.Applied, applyResult.State);
            Assert.True(fileSystem.FileExists(manifestPath));
            Assert.True(fileSystem.FileExists(targetPath));
            Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
            UnixFileMode executableMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.Equal(executableMode, fileSystem.GetUnixFileMode(targetPath));
            string existingManifestJson = fileSystem.ReadAllText(manifestPath);
            UnixFileMode driftedMode =
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute;
            fileSystem.SetUnixFileMode(targetPath, driftedMode);
            Assert.Equal(driftedMode, fileSystem.GetUnixFileMode(targetPath));

            fileSystem.Calls.Clear();
            ConfigurationChangePlan reapplyPlan = plan with
            {
                Manifest = plan.Manifest with
                {
                    PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
                },
            };

            ConfigurationPlanResult reapplyResult = await manager.ApplyAsync(
                reapplyPlan,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(ConfigurationPlanState.Applied, reapplyResult.State);
            Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
            Assert.Equal(executableMode, fileSystem.GetUnixFileMode(targetPath));
            Assert.True(fileSystem.FileExists(manifestPath));
            Assert.DoesNotContain(
                fileSystem.Calls,
                call =>
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.AtomicWriteAllText),
                        StringComparison.Ordinal
                    )
                    && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            );
            Assert.Contains(
                fileSystem.Calls,
                call =>
                    string.Equals(
                        call.Operation,
                        nameof(IFileSystem.SetUnixFileMode),
                        StringComparison.Ordinal
                    )
                    && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalXdgDataHome);
        }
    }

    [Theory]
    [MemberData(nameof(PythonManifestCollisionCases))]
    public async Task
        ValidatePlanAndDryRunRejectPythonKeyringOwnershipManifestCollisions(
            ConfigurationTargetKind targetKind,
            bool manifestPathNestedUnderTargetPath
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = manifestPathNestedUnderTargetPath
            ? Path.Combine(targetPath, "ownership-manifest.json")
            : targetPath;
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath);

        await AssertPythonKeyringValidationAndDryRunRejectedAsync(
            manager,
            plan,
            "ownership manifest path"
        );
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task ValidatePlanAndDryRunRejectPythonKeyringSamePathConflicts(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        var manager = CreateManager(
            fileSystem,
            CreateStatePath($"python-same-path-conflict-{targetKind}.json")
        );
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath) with
        {
            Changes =
            [
                CreatePythonChange(targetKind, targetPath),
                CreateGitConfigPhysicalTargetChange(targetPath),
            ],
        };

        await AssertPythonKeyringValidationAndDryRunRejectedAsync(
            manager,
            plan,
            "same physical target path"
        );
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task ValidatePlanDryRunAndApplyRejectPythonKeyringReservedInternalPaths(
        ConfigurationTargetKind targetKind
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? originalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string? originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string reservedRoot = Path.Combine(
            GetCurrentUserProfileDirectory(),
            ".azureauth-credprovider.fs.lock"
        );

        try
        {
            if (targetKind == ConfigurationTargetKind.KeyringShim)
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", reservedRoot);
            }
            else
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", reservedRoot);
            }

            var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
            string targetPath = GetCanonicalPythonTargetPath(targetKind);
            Assert.StartsWith(reservedRoot, targetPath, StringComparison.Ordinal);
            var manager = CreateManager(
                fileSystem,
                CreateStatePath($"python-reserved-{targetKind}.json")
            );
            ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath);

            await AssertPythonKeyringValidationAndDryRunRejectedAsync(
                manager,
                plan,
                "reserved internal filesystem artifact"
            );
            AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);

            fileSystem.Calls.Clear();
            var applyException = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

            Assert.Contains(
                "reserved internal filesystem artifact",
                applyException.Message,
                StringComparison.Ordinal
            );
            AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalXdgDataHome);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", originalXdgConfigHome);
        }
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task DryRunRejectsPythonKeyringEmptyValues(ConfigurationTargetKind targetKind)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-empty-value-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath) with
        {
            Changes =
            [
                CreatePythonChange(targetKind, targetPath) with
                {
                    Value = string.Empty,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-empty", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task DryRunRejectsPythonKeyringSecretValues(ConfigurationTargetKind targetKind)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-secret-value-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath) with
        {
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreatePythonChange(targetKind, targetPath) with
                {
                    IsSecretValue = true,
                },
            ],
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("secret value-writing", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(targetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task RemovePreservesUnrelatedFilesAndClearsOwnedPythonKeyringTarget(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-remove-{targetKind}.json");
        string unrelatedFile = CreateStatePath($"python-unrelated-{targetKind}.txt");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreatePythonPlan(targetKind, targetPath);

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
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal(string.Empty, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task RemoveThenApplyReinstallsPythonKeyringTarget(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-reapply-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreatePythonPlan(targetKind, targetPath);

        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string existingManifestJson = fileSystem.ReadAllText(manifestPath);
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

        await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken);
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal(string.Empty, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        fileSystem.Calls.Clear();

        ConfigurationPlanResult reinstallResult = await manager.ApplyAsync(
            applyPlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, reinstallResult.State);
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
        UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (!OperatingSystem.IsWindows() && targetKind == ConfigurationTargetKind.KeyringShim)
        {
            expectedMode |= UnixFileMode.UserExecute;
        }

        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task ApplyRejectsPythonKeyringStaleManifestHashWithoutMutation(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-stale-manifest-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreatePythonPlan(targetKind, targetPath);

        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        fileSystem.Calls.Clear();

        ConfigurationChangePlan stalePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata("stale-manifest"),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(stalePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "before-state hash does not match",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task ApplyRejectsPythonKeyringForeignManifestIdentityWithoutMutation(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-foreign-manifest-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreatePythonPlan(targetKind, targetPath);

        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string existingManifestJson = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest foreignManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(existingManifestJson) with
            {
                OwnerProductId = "foreign-product",
            };
        string foreignManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            foreignManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, foreignManifestJson);
        fileSystem.Calls.Clear();

        ConfigurationChangePlan foreignPlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(foreignManifestJson),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(foreignPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "existing manifest identity does not match",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(foreignManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public async Task DryRunRemoveDoesNotMutatePythonKeyringTargetOrManifest(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-dry-run-remove-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreatePythonPlan(targetKind, targetPath);

        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string existingManifestJson = fileSystem.ReadAllText(manifestPath);
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

        ConfigurationPlanResult result = await manager.DryRunAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        Assert.Null(result.OwnershipManifest);
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal("planned-value", fileSystem.ReadAllText(targetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task ApplyRollsBackPythonKeyringTargetWhenManifestWriteFails()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        const ConfigurationTargetKind targetKind = ConfigurationTargetKind.PythonKeyringBackend;
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath("python-rollback.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan createPlan = CreatePythonPlan(targetKind, targetPath);

        await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
        string targetBeforeUpdate = fileSystem.ReadAllText(targetPath);
        string manifestBeforeUpdate = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan updatePlan = createPlan with
        {
            Manifest = createPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(manifestBeforeUpdate),
            },
            Changes =
            [
                createPlan.Changes[0] with
                {
                    Value = "updated-value",
                },
            ],
        };

        var manifestWriteCount = 0;
        fileSystem.AfterRecord = (call, _) =>
        {
            if (
                string.Equals(call.Operation, nameof(InMemoryFileSystem.AtomicWriteAllText))
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && ++manifestWriteCount == 2
            )
            {
                throw new IOException("simulated final manifest write failure");
            }
        };

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await manager.ApplyAsync(updatePlan, TestContext.Current.CancellationToken)
        );

        Assert.True(fileSystem.FileExists(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.Equal(targetBeforeUpdate, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBeforeUpdate, fileSystem.ReadAllText(manifestPath));

        fileSystem.AfterRecord = null;
        ConfigurationPlanResult retryResult = await manager.ApplyAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, retryResult.State);
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal("updated-value", fileSystem.ReadAllText(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackPythonKeyringShimTargetWhenManifestWriteFails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? originalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string customXdgDataHome = Path.Combine(
            GetCurrentUserProfileDirectory(),
            ".azureauth-credprovider-custom-xdg-data-home"
        );
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", customXdgDataHome);

        try
        {
            var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
            const ConfigurationTargetKind targetKind = ConfigurationTargetKind.KeyringShim;
            string targetPath = GetCanonicalPythonTargetPath(targetKind);
            string manifestPath = CreateStatePath("python-keyring-shim-rollback.json");
            var manager = CreateManager(fileSystem, manifestPath);
            ConfigurationChangePlan createPlan = CreatePythonPlan(targetKind, targetPath);

            await manager.ApplyAsync(createPlan, TestContext.Current.CancellationToken);
            string targetBeforeUpdate = fileSystem.ReadAllText(targetPath);
            string manifestBeforeUpdate = fileSystem.ReadAllText(manifestPath);
            UnixFileMode executableMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.Equal(
                executableMode,
                fileSystem.GetUnixFileMode(targetPath)
            );
            UnixFileMode driftedMode =
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute;
            fileSystem.SetUnixFileMode(targetPath, driftedMode);
            UnixFileMode targetBeforeUpdateMode = fileSystem.GetUnixFileMode(targetPath);
            Assert.Equal(driftedMode, targetBeforeUpdateMode);

            ConfigurationChangePlan updatePlan = createPlan with
            {
                Manifest = createPlan.Manifest with
                {
                    PreviousOwnedEntryHash = HashMetadata(manifestBeforeUpdate),
                },
                Changes =
                [
                    createPlan.Changes[0] with
                    {
                        Value = "updated-value",
                    },
                ],
            };

            var manifestWriteCount = 0;
            fileSystem.AfterRecord = (call, _) =>
            {
                if (
                    string.Equals(call.Operation, nameof(InMemoryFileSystem.AtomicWriteAllText))
                    && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                    && ++manifestWriteCount == 2
                )
                {
                    throw new IOException("simulated final manifest write failure");
                }
            };

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await manager.ApplyAsync(updatePlan, TestContext.Current.CancellationToken)
            );

            Assert.True(fileSystem.FileExists(targetPath));
            Assert.True(fileSystem.FileExists(manifestPath));
            Assert.Equal(targetBeforeUpdate, fileSystem.ReadAllText(targetPath));
            Assert.Equal(targetBeforeUpdateMode, fileSystem.GetUnixFileMode(targetPath));
            Assert.Equal(manifestBeforeUpdate, fileSystem.ReadAllText(manifestPath));

            fileSystem.AfterRecord = null;
            ConfigurationPlanResult retryResult = await manager.ApplyAsync(
                updatePlan,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(ConfigurationPlanState.Applied, retryResult.State);
            Assert.True(fileSystem.FileExists(targetPath));
            Assert.Equal("updated-value", fileSystem.ReadAllText(targetPath));
            Assert.Equal(executableMode, fileSystem.GetUnixFileMode(targetPath));
            Assert.True(fileSystem.FileExists(manifestPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalXdgDataHome);
        }
    }

    [Fact]
    public async Task ApplyRollsBackPythonKeyringShimNoOpReapplyWhenModeIsDriftedWide()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? originalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string customXdgDataHome = Path.Combine(
            GetCurrentUserProfileDirectory(),
            ".azureauth-credprovider-custom-xdg-data-home"
        );
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", customXdgDataHome);

        try
        {
            var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
            string targetPath = GetCanonicalPythonTargetPath(ConfigurationTargetKind.KeyringShim);
            string manifestPath = CreateStatePath("python-keyring-shim-noop-rollback.json");
            var manager = CreateManager(fileSystem, manifestPath);
            ConfigurationChangePlan plan = CreatePythonPlan(
                ConfigurationTargetKind.KeyringShim,
                targetPath
            );

            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
            string targetBeforeUpdate = fileSystem.ReadAllText(targetPath);
            string manifestBeforeUpdate = fileSystem.ReadAllText(manifestPath);
            UnixFileMode executableMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            UnixFileMode driftedMode =
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute;
            fileSystem.SetUnixFileMode(targetPath, driftedMode);
            UnixFileMode targetBeforeUpdateMode = fileSystem.GetUnixFileMode(targetPath);
            Assert.Equal(driftedMode, targetBeforeUpdateMode);

            ConfigurationChangePlan reapplyPlan = plan with
            {
                Manifest = plan.Manifest with
                {
                    PreviousOwnedEntryHash = HashMetadata(manifestBeforeUpdate),
                },
            };

            var manifestWriteCount = 0;
            fileSystem.AfterRecord = (call, _) =>
            {
                if (
                    string.Equals(call.Operation, nameof(InMemoryFileSystem.AtomicWriteAllText))
                    && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                    && ++manifestWriteCount == 2
                )
                {
                    throw new IOException("simulated final manifest write failure");
                }
            };

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await manager.ApplyAsync(reapplyPlan, TestContext.Current.CancellationToken)
            );

            Assert.True(fileSystem.FileExists(targetPath));
            Assert.True(fileSystem.FileExists(manifestPath));
            Assert.Equal(targetBeforeUpdate, fileSystem.ReadAllText(targetPath));
            Assert.Equal(targetBeforeUpdateMode, fileSystem.GetUnixFileMode(targetPath));
            Assert.Equal(manifestBeforeUpdate, fileSystem.ReadAllText(manifestPath));

            fileSystem.AfterRecord = null;
            ConfigurationPlanResult retryResult = await manager.ApplyAsync(
                reapplyPlan,
                TestContext.Current.CancellationToken
            );

            Assert.Equal(ConfigurationPlanState.Applied, retryResult.State);
            Assert.True(fileSystem.FileExists(targetPath));
            Assert.Equal(targetBeforeUpdate, fileSystem.ReadAllText(targetPath));
            Assert.Equal(executableMode, fileSystem.GetUnixFileMode(targetPath));
            Assert.True(fileSystem.FileExists(manifestPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalXdgDataHome);
        }
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public void ValidatePlanRejectsInvalidPythonKeyringTargetPath(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string invalidTargetPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            "not-the-official-target"
        );
        string manifestPath = CreateStatePath($"python-invalid-path-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, invalidTargetPath);

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            targetKind == ConfigurationTargetKind.PythonKeyringBackend
                ? "official per-user backend manifest file"
                : "official per-user keyring shim path",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(fileSystem.FileExists(invalidTargetPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonLinkSafetyCases))]
    public async Task DryRunRejectsDanglingPythonKeyringTargetLinksWithoutMutation(
        ConfigurationTargetKind targetKind,
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-link-{targetKind}-{useSymbolicLink}.json");
        string? parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            fileSystem.CreateDirectory(parentDirectory);
        }

        if (useSymbolicLink)
        {
            fileSystem.AddSymbolicLink(targetPath, Path.Combine(parentDirectory!, "missing"));
        }
        else
        {
            fileSystem.AtomicWriteAllText(targetPath, "existing");
            fileSystem.MarkAsNonSymbolicReparsePoint(targetPath);
        }

        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    [Theory]
    [MemberData(nameof(PythonPhysicalTargetKinds))]
    public void ValidatePlanRejectsUnsupportedPythonKeyringTargetShape(
        ConfigurationTargetKind targetKind
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = GetCanonicalPythonTargetPath(targetKind);
        string manifestPath = CreateStatePath($"python-invalid-shape-{targetKind}.json");
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreatePythonPlan(targetKind, targetPath) with
        {
            Changes =
            [
                CreatePythonChange(targetKind, targetPath) with
                {
                    Key = "not-physical-target",
                },
            ],
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(
            "canonical physical target key",
            validationResult.Violation,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoPythonPhysicalMutationCalls(fileSystem.Calls);
    }

    private static ConfigurationManager CreateManager(
        IFileSystem fileSystem,
        string manifestPath
    ) =>
        new(
            fileSystem,
            manifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

    private static ConfigurationChangePlan CreatePythonPlan(
        ConfigurationTargetKind targetKind,
        string targetPath
    ) =>
        ConfigurationChangePlanPolicy.Create(
            $"plan-{targetKind}-python-physical-target",
            $"changeset-{targetKind}-python-physical-target",
            "azureauth-credprovider",
            ConfigurationScope.User,
            CreateManifest(targetKind),
            [CreatePythonChange(targetKind, targetPath)]
        );

    private static ConfigurationManifestMetadata CreateManifest(
        ConfigurationTargetKind targetKind
    ) =>
        new()
        {
            ManifestId = $"manifest-{targetKind}-python-physical-target",
            OwnerProductId = "azureauth-credprovider",
            EntrySelector = $"{targetKind}.physical-target",
            ProductVersion = "0.0.0-test",
        };

    private static ConfigurationChange CreatePythonChange(
        ConfigurationTargetKind targetKind,
        string targetPath
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = targetKind,
            TargetPathOrName = targetPath,
            Key = "physical-target",
            Value = "planned-value",
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
        };

    private static ConfigurationChange CreateGitConfigPhysicalTargetChange(string targetPath) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.GitConfig,
            TargetPathOrName = targetPath,
            Key = "credential.helper",
            Value = "planned-value",
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
        };

    private static async Task AssertPythonKeyringValidationAndDryRunRejectedAsync(
        ConfigurationManager manager,
        ConfigurationChangePlan plan,
        string expectedViolation
    )
    {
        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(expectedViolation, validationResult.Violation, StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        Assert.Contains(expectedViolation, exception.Message, StringComparison.Ordinal);
    }

    private static string GetCanonicalPythonTargetPath(ConfigurationTargetKind targetKind)
    {
        ConfigurationLayoutProjectionContext context = CreateLayoutProjectionContext();
        return targetKind switch
        {
            ConfigurationTargetKind.PythonKeyringBackend =>
                ConfigurationLayoutProjector.ProjectPythonKeyringBackend(context).TargetPath,
            ConfigurationTargetKind.KeyringShim =>
                ConfigurationLayoutProjector.ProjectKeyringShim(context).TargetPath,
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null),
        };
    }

    private static ConfigurationLayoutProjectionContext CreateLayoutProjectionContext() =>
        new()
        {
            Platform = GetCurrentLayoutPlatform(),
            HomeDirectory = GetCurrentUserProfileDirectory(),
            LocalAppDataDirectory = GetLocalAppDataDirectory(),
            XdgDataHomeDirectory = Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            XdgConfigHomeDirectory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        };

    private static ConfigurationLayoutPlatform GetCurrentLayoutPlatform() =>
        OperatingSystem.IsWindows()
            ? ConfigurationLayoutPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? ConfigurationLayoutPlatform.MacOs
                : ConfigurationLayoutPlatform.Linux;

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

    private static string? GetLocalAppDataDirectory()
    {
        string? localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.TrimEndingDirectorySeparator(localAppData);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(windowsLocalAppData))
            {
                return Path.TrimEndingDirectorySeparator(windowsLocalAppData);
            }

            string? userProfile = GetCurrentUserProfileDirectory();
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(userProfile, "AppData", "Local");
            }
        }

        return null;
    }

    private static string CreateStatePath(string fileName) =>
        Path.Combine(GetCurrentUserProfileDirectory(), ".azureauth-credprovider-tests", fileName);

    private static void AssertNoPythonPhysicalMutationCalls(
        IEnumerable<FileSystemCall> calls
    ) =>
        Assert.DoesNotContain(
            calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or nameof(IFileSystem.SetUnixFileMode)
                        or nameof(IFileSystem.CreateDirectory)
                        or nameof(IFileSystem.DeleteDirectory)
                        or nameof(IFileSystemMutationLock.AcquireMutationLock)
                        or nameof(InMemoryFileSystem.AddSymbolicLink)
        );

    private static string HashMetadata(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
