using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationManagerTests
{
    private const string Owner = "azureauth-credprovider";
    private const string ManifestPath = "/state/npm.json";
    private const string TargetPath = "/home/user/.npmrc";
    private const string Secret = "secret-token-value";

    [Fact]
    public void ValidatePlanRejectsRelativeFilesystemTargets()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan plan = CreateNpmPlan("relative/.npmrc", Secret);

        ConfigurationPlanValidationResult result = manager.ValidatePlan(plan);

        Assert.False(result.IsValid);
        Assert.Contains("fully qualified", result.Violation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRunDoesNotWriteTargetOrManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);

        ConfigurationPlanResult result = await manager.DryRunAsync(
            CreateNpmPlan(TargetPath, Secret),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanOperation.DryRun, result.Operation);
        Assert.False(fileSystem.FileExists(TargetPath));
        Assert.False(fileSystem.FileExists(ManifestPath));
    }

    [Fact]
    public async Task ApplyReconcilesOwnedDriftWithoutPersistingSecretProofs()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan plan = CreateNpmPlan(TargetPath, Secret);
        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
        string key = plan.Changes.Single().Key;
        fileSystem.WriteAllText(TargetPath, $"{key}=changed-by-user\nfund=false\n");

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        string npmrc = fileSystem.ReadAllText(TargetPath);
        string manifest = fileSystem.ReadAllText(ManifestPath);
        Assert.Contains($"{key}={Secret}", npmrc, StringComparison.Ordinal);
        Assert.Contains("fund=false", npmrc, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("plannedValueSha256", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveUsesExactOwnedSelectorAndPreservesUnrelatedConfiguration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan applyPlan = CreateNpmPlan(TargetPath, Secret);
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        fileSystem.WriteAllText(
            TargetPath,
            fileSystem.ReadAllText(TargetPath) + "registry=https://registry.npmjs.org/\n"
        );

        ConfigurationPlanResult result = await manager.RemoveAsync(
            CreateRemovePlan(applyPlan),
            TestContext.Current.CancellationToken
        );

        string remaining = fileSystem.ReadAllText(TargetPath);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.Operation);
        Assert.DoesNotContain("_authToken", remaining, StringComparison.Ordinal);
        Assert.Contains(
            "registry=https://registry.npmjs.org/",
            remaining,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(ManifestPath));
    }

    [Fact]
    public async Task ExistingUnownedSelectorIsRejectedWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationChangePlan plan = CreateNpmPlan(TargetPath, Secret);
        string original = $"{plan.Changes.Single().Key}=existing-token\n";
        fileSystem.AtomicWriteAllText(TargetPath, original);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Equal(original, fileSystem.ReadAllText(TargetPath));
        Assert.False(fileSystem.FileExists(ManifestPath));
    }

    [Fact]
    public async Task MalformedOwnershipManifestIsLeftUntouched()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string malformed = "{ not-json";
        fileSystem.AtomicWriteAllText(ManifestPath, malformed);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await manager.ApplyAsync(
                CreateNpmPlan(TargetPath, Secret),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(malformed, fileSystem.ReadAllText(ManifestPath));
        Assert.False(fileSystem.FileExists(TargetPath));
    }

    [Fact]
    public async Task ApplyManifestIntentFailureLeavesTargetUntouchedAndRetryConverges()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan plan = CreateNpmPlan(TargetPath, Secret);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllText),
            ManifestPath,
            1,
            new IOException("Injected manifest intent failure.")
        );

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(fileSystem.FileExists(TargetPath));
        Assert.False(fileSystem.FileExists(ManifestPath));

        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);

        AssertOwnedSelector(fileSystem, plan.Changes.Single().Key);
        Assert.Contains(Secret, fileSystem.ReadAllText(TargetPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyTargetWriterFailureLeavesOwnershipIntentAndRetryConverges()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan plan = CreateNpmPlan(TargetPath, Secret);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllBytes),
            TargetPath,
            1,
            new IOException("Injected target writer failure.")
        );

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(fileSystem.FileExists(TargetPath));
        AssertOwnedSelector(fileSystem, plan.Changes.Single().Key);

        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);

        AssertOwnedSelector(fileSystem, plan.Changes.Single().Key);
        Assert.Contains(Secret, fileSystem.ReadAllText(TargetPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveTargetWriterFailureRetainsOwnershipAndRetryConverges()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan plan = CreateNpmPlan(TargetPath, Secret);
        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllBytes),
            TargetPath,
            1,
            new IOException("Injected target writer failure.")
        );

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(
                CreateRemovePlan(plan),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(Secret, fileSystem.ReadAllText(TargetPath), StringComparison.Ordinal);
        AssertOwnedSelector(fileSystem, plan.Changes.Single().Key);

        await manager.RemoveAsync(
            CreateRemovePlan(plan),
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(
            plan.Changes.Single().Key,
            fileSystem.ReadAllText(TargetPath),
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(ManifestPath));
    }

    [Fact]
    public async Task RemoveManifestRemovalFailureRetainsOwnershipAndRetryConverges()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan plan = CreateNpmPlan(TargetPath, Secret);
        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.DeleteFile),
            ManifestPath,
            1,
            new IOException("Injected manifest removal failure.")
        );

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(
                CreateRemovePlan(plan),
                TestContext.Current.CancellationToken
            )
        );

        Assert.DoesNotContain(
            plan.Changes.Single().Key,
            fileSystem.ReadAllText(TargetPath),
            StringComparison.Ordinal
        );
        AssertOwnedSelector(fileSystem, plan.Changes.Single().Key);

        await manager.RemoveAsync(
            CreateRemovePlan(plan),
            TestContext.Current.CancellationToken
        );

        Assert.False(fileSystem.FileExists(ManifestPath));
    }

    [Fact]
    public async Task RemoveManifestWriteFailureRetainsOwnershipAndRetryConverges()
    {
        const string retainedSelector =
            "//pkgs.dev.azure.com/org/_packaging/retained-feed/npm/registry/:_authToken";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan removedPlan = CreateNpmPlanForFeed(
            "removed-npm-plan",
            "removed-feed",
            Secret
        );
        ConfigurationChangePlan retainedPlan = CreateNpmPlanForFeed(
            "retained-npm-plan",
            "retained-feed",
            "retained-token-value"
        );
        await manager.ApplyAsync(removedPlan, TestContext.Current.CancellationToken);
        await manager.ApplyAsync(retainedPlan, TestContext.Current.CancellationToken);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllText),
            ManifestPath,
            1,
            new IOException("Injected manifest write failure.")
        );

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(
                CreateRemovePlan(removedPlan),
                TestContext.Current.CancellationToken
            )
        );

        Assert.DoesNotContain(
            removedPlan.Changes.Single().Key,
            fileSystem.ReadAllText(TargetPath),
            StringComparison.Ordinal
        );
        Assert.Equal(
            [removedPlan.Changes.Single().Key, retainedSelector],
            LoadManifest(fileSystem).Entries.Select(entry => entry.Key)
        );

        await manager.RemoveAsync(
            CreateRemovePlan(removedPlan),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(retainedSelector, fileSystem.ReadAllText(TargetPath), StringComparison.Ordinal);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            LoadManifest(fileSystem).Entries
        );
        Assert.Equal(retainedSelector, entry.Key);
    }

    private static ConfigurationChangePlan CreateNpmPlan(string targetPath, string token)
    {
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"),
            feed: "feed"
        );
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        return ConfigurationChangePlanPolicy.Create(
            "npm-plan",
            Owner,
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = "npm-manifest",
                OwnerProductId = Owner,
                EntrySelector = selector,
                ResourceIdentity = resource,
                ProductVersion = "test",
            },
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = targetPath,
                    Key = selector,
                    Value = token,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                    IsSecretValue = true,
                },
            ],
            containsCredentialMaterial: true
        );
    }

    private static ConfigurationChangePlan CreateRemovePlan(ConfigurationChangePlan appliedPlan) =>
        appliedPlan with
        {
            PlanId = "npm-remove-plan",
            ContainsCredentialMaterial = true,
            Changes =
            [
                appliedPlan.Changes.Single() with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                },
            ],
        };

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The exact regression test name is part of the Phase 3 plan."
    )]
    public async Task ExecuteBatchAsync_RemoveThenApply_ReplacesOwnedNpmSelectors()
    {
        const string oldSelector =
            "//pkgs.dev.azure.com/org/_packaging/old-feed/npm/registry/:_authToken";
        const string replacementSelector =
            "//pkgs.dev.azure.com/org/_packaging/replacement-feed/npm/registry/:_authToken";
        const string replacementToken = "replacement-token-value";
        const string unrelatedConfiguration = "registry=https://registry.npmjs.org/";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan oldPlan = CreateNpmPlanForFeed("old-npm-plan", "old-feed", Secret);
        ConfigurationChangePlan replacementPlan = CreateNpmPlanForFeed(
            "replacement-npm-plan",
            "replacement-feed",
            replacementToken
        );
        Assert.Equal(oldSelector, oldPlan.Changes.Single().Key);
        Assert.Equal(replacementSelector, replacementPlan.Changes.Single().Key);
        await manager.ApplyAsync(oldPlan, TestContext.Current.CancellationToken);
        fileSystem.WriteAllText(
            TargetPath,
            fileSystem.ReadAllText(TargetPath) + unrelatedConfiguration + "\n"
        );

        IReadOnlyList<ConfigurationPlanResult> results = await manager.ExecuteBatchAsync(
            [
                (CreateRemovePlan(oldPlan), ConfigurationPlanOperation.Remove),
                (replacementPlan, ConfigurationPlanOperation.Apply),
            ],
            TestContext.Current.CancellationToken
        );

        string npmrc = fileSystem.ReadAllText(TargetPath);
        Assert.Equal(
            unrelatedConfiguration + "\n" + replacementSelector + "=" + replacementToken + "\n",
            npmrc
        );
        Assert.DoesNotContain(oldSelector, npmrc, StringComparison.Ordinal);
        Assert.Collection(
            results,
            result => Assert.Equal(ConfigurationPlanOperation.Remove, result.Operation),
            result => Assert.Equal(ConfigurationPlanOperation.Apply, result.Operation)
        );

        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(ManifestPath)
            );
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(ConfigurationTargetKind.Npmrc, entry.TargetKind);
        Assert.Equal(TargetPath, entry.TargetPathOrName);
        Assert.Equal(replacementSelector, entry.Key);
        Assert.DoesNotContain(
            manifest.Entries,
            candidate => string.Equals(candidate.Key, oldSelector, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(ReplacementFailurePoint.RemoveTarget)]
    [InlineData(ReplacementFailurePoint.OwnershipIntent)]
    [InlineData(ReplacementFailurePoint.ApplyTarget)]
    [InlineData(ReplacementFailurePoint.FinalOwnership)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The test name identifies the remove-then-apply replacement operation."
    )]
    public async Task ExecuteBatchAsync_RemoveThenApply_FailureRetainsCoverageAndRetryConverges(
        ReplacementFailurePoint failurePoint
    )
    {
        const string oldSelector =
            "//pkgs.dev.azure.com/org/_packaging/old-feed/npm/registry/:_authToken";
        const string replacementSelector =
            "//pkgs.dev.azure.com/org/_packaging/replacement-feed/npm/registry/:_authToken";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var manager = new ConfigurationManager(fileSystem, ManifestPath);
        ConfigurationChangePlan oldPlan = CreateNpmPlanForFeed("old-npm-plan", "old-feed", Secret);
        ConfigurationChangePlan replacementPlan = CreateNpmPlanForFeed(
            "replacement-npm-plan",
            "replacement-feed",
            "replacement-token-value"
        );
        await manager.ApplyAsync(oldPlan, TestContext.Current.CancellationToken);
        InjectReplacementFailure(fileSystem, failurePoint);

        await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ExecuteBatchAsync(
                [
                    (CreateRemovePlan(oldPlan), ConfigurationPlanOperation.Remove),
                    (replacementPlan, ConfigurationPlanOperation.Apply),
                ],
                TestContext.Current.CancellationToken
            )
        );

        AssertReplacementFailureState(
            fileSystem,
            failurePoint,
            oldSelector,
            replacementSelector
        );

        await manager.ExecuteBatchAsync(
            [
                (CreateRemovePlan(oldPlan), ConfigurationPlanOperation.Remove),
                (replacementPlan, ConfigurationPlanOperation.Apply),
            ],
            TestContext.Current.CancellationToken
        );

        string npmrc = fileSystem.ReadAllText(TargetPath);
        Assert.DoesNotContain(oldSelector, npmrc, StringComparison.Ordinal);
        Assert.Contains(replacementSelector, npmrc, StringComparison.Ordinal);
        ConfigurationOwnershipManifest manifest = LoadManifest(fileSystem);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(replacementSelector, entry.Key);
        Assert.DoesNotContain(Secret, fileSystem.ReadAllText(ManifestPath), StringComparison.Ordinal);
    }

    private static ConfigurationChangePlan CreateNpmPlanForFeed(
        string planId,
        string feed,
        string token
    )
    {
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri($"https://pkgs.dev.azure.com/org/_packaging/{feed}/npm/registry/"),
            feed: feed
        );
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        return ConfigurationChangePlanPolicy.Create(
            planId,
            Owner,
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = "npm-manifest",
                OwnerProductId = Owner,
                EntrySelector = selector,
                ResourceIdentity = resource,
                ProductVersion = "test",
            },
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = TargetPath,
                    Key = selector,
                    Value = token,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                    IsSecretValue = true,
                },
            ],
            containsCredentialMaterial: true
        );
    }

    private static void InjectReplacementFailure(
        InMemoryFileSystem fileSystem,
        ReplacementFailurePoint failurePoint
    )
    {
        (string operation, string path, int occurrence) = failurePoint switch
        {
            ReplacementFailurePoint.RemoveTarget =>
                (nameof(InMemoryFileSystem.AtomicWriteAllBytes), TargetPath, 1),
            ReplacementFailurePoint.OwnershipIntent =>
                (nameof(InMemoryFileSystem.AtomicWriteAllText), ManifestPath, 1),
            ReplacementFailurePoint.ApplyTarget =>
                (nameof(InMemoryFileSystem.AtomicWriteAllBytes), TargetPath, 2),
            ReplacementFailurePoint.FinalOwnership =>
                (nameof(InMemoryFileSystem.AtomicWriteAllText), ManifestPath, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint)),
        };
        fileSystem.FailMatchingCall(
            operation,
            path,
            occurrence,
            new IOException($"Injected {failurePoint} failure.")
        );
    }

    private static void AssertReplacementFailureState(
        InMemoryFileSystem fileSystem,
        ReplacementFailurePoint failurePoint,
        string oldSelector,
        string replacementSelector
    )
    {
        string target = fileSystem.ReadAllText(TargetPath);
        ConfigurationOwnershipManifest manifest = LoadManifest(fileSystem);
        string[] expectedOwnership = failurePoint switch
        {
            ReplacementFailurePoint.RemoveTarget => [oldSelector],
            ReplacementFailurePoint.OwnershipIntent => [oldSelector],
            ReplacementFailurePoint.ApplyTarget => [oldSelector, replacementSelector],
            ReplacementFailurePoint.FinalOwnership => [oldSelector, replacementSelector],
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint)),
        };
        Assert.Equal(expectedOwnership, manifest.Entries.Select(entry => entry.Key));
        Assert.DoesNotContain(Secret, fileSystem.ReadAllText(ManifestPath), StringComparison.Ordinal);

        if (failurePoint == ReplacementFailurePoint.RemoveTarget)
        {
            Assert.Contains(oldSelector, target, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(oldSelector, target, StringComparison.Ordinal);
        }

        if (failurePoint == ReplacementFailurePoint.FinalOwnership)
        {
            Assert.Contains(replacementSelector, target, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(replacementSelector, target, StringComparison.Ordinal);
        }
    }

    private static void AssertOwnedSelector(InMemoryFileSystem fileSystem, string selector)
    {
        ConfigurationOwnershipManifest manifest = LoadManifest(fileSystem);
        ConfigurationOwnershipManifestEntry entry = Assert.Single(manifest.Entries);
        Assert.Equal(selector, entry.Key);
        Assert.DoesNotContain(Secret, fileSystem.ReadAllText(ManifestPath), StringComparison.Ordinal);
    }

    private static ConfigurationOwnershipManifest LoadManifest(InMemoryFileSystem fileSystem) =>
        ConfigurationOwnershipManifestSerializer.Deserialize(
            fileSystem.ReadAllText(ManifestPath)
        );

    public enum ReplacementFailurePoint
    {
        RemoveTarget,
        OwnershipIntent,
        ApplyTarget,
        FinalOwnership,
    }
}
