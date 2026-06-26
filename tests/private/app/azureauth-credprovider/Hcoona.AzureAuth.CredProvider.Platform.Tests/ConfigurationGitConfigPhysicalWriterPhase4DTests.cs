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
public sealed class ConfigurationGitConfigPhysicalWriterPhase4DTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task ApplyWritesGitConfigBatchAndPreservesUnrelatedContent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-apply-manifest.json";
        const string targetPath = "/config/sub/../user.gitconfig";
        const string normalizedTargetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            # keep top comment
            [user]
                name = Existing User
            [credential "https://example.com"]
                helper = foreign-helper
            """;
        fileSystem.AtomicWriteAllText(normalizedTargetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                normalizedTargetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        string gitConfig = fileSystem.ReadAllText(normalizedTargetPath);
        string expectedGitConfig = string.Join(
            '\n',
            "# keep top comment",
            "[user]",
            "    name = Existing User",
            "[credential \"https://example.com\"]",
            "    helper = foreign-helper",
            string.Empty,
            "[credential]",
            "\thelper = \"hcoona-azureauth\"",
            string.Empty,
            "[credential \"https://dev.azure.com\"]",
            "\tuseHttpPath = \"true\"",
            string.Empty
        );
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal(expectedGitConfig, gitConfig);
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.Collection(
            result.OwnershipManifest!.Entries,
            entry => Assert.Equal("credential.helper", entry.Key),
            entry => Assert.Equal("credential.https://dev.azure.com.useHttpPath", entry.Key)
        );
    }

    [Fact]
    public async Task DryRunDoesNotMutateGitConfigFileOrManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            # keep dry-run comment
            [credential "https://example.com"]
                helper = foreign-helper
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Planned, result.State);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
        );
    }

    [Fact]
    public async Task ApplyPersistsCanonicalFullGitConfigTargetPathInManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-canonical-path-manifest.json";
        const string relativeTargetPath = "config/sub/../user.gitconfig";
        const string canonicalTargetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                relativeTargetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.True(fileSystem.FileExists(canonicalTargetPath));
        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal(canonicalTargetPath, entry.TargetPathOrName);
        Assert.Contains(canonicalTargetPath, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(relativeTargetPath, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task DryRunRejectsDanglingGitConfigTargetSymlinkWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-dangling-symlink-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        fileSystem.CreateDirectory("/config");
        fileSystem.AddSymbolicLink(targetPath, "/missing/user.gitconfig");
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.True(fileSystem.IsSymbolicLink(targetPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DryRunRejectsGitConfigTargetParentSymlinkOrReparsePointWithoutMutation(
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-parent-link-manifest.json";
        const string parentPath = "/config/link-parent";
        string targetPath = parentPath + "/user.gitconfig";
        fileSystem.CreateDirectory("/config");
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
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("parent path", exception.Message, StringComparison.Ordinal);
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
        Assert.False(fileSystem.FileExists(manifestPath));
        if (useSymbolicLink)
        {
            Assert.True(fileSystem.IsSymbolicLink(parentPath));
        }
        else
        {
            Assert.True(
                ((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint(parentPath)
            );
        }
    }

    [Fact]
    public async Task DryRunRejectsGitConfigTargetNonDirectoryParentWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-file-parent-manifest.json";
        const string parentPath = "/config/file-parent";
        string targetPath = parentPath + "/user.gitconfig";
        fileSystem.AtomicWriteAllText(parentPath, "not a directory");
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-directory", exception.Message, StringComparison.Ordinal);
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
        Assert.Equal("not a directory", fileSystem.ReadAllText(parentPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyQuotesGitConfigValuesSoCommentsWhitespaceAndQuotesRemainLiteral()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-quoted-values-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "helper with spaces # not-a-comment \\ \"quoted\""
            )
        );

        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);

        Assert.Contains(
            "helper = \"helper with spaces # not-a-comment \\\\ \\\"quoted\\\"\"",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [MemberData(nameof(UnsupportedGitConfigSyntaxCases))]
    public async Task ApplyRejectsUnsupportedGitConfigSyntaxWithoutMutation(
        byte[] existingGitConfigBytes,
        string expectedMessage
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-syntax-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        fileSystem.AtomicWriteAllBytes(targetPath, existingGitConfigBytes);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfigBytes, fileSystem.ReadAllBytes(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    public static TheoryData<byte[], string> UnsupportedGitConfigSyntaxCases =>
        new()
        {
            {
                Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes("[credential]\n\thelper = manager-core\n"))
                    .ToArray(),
                "BOM-prefixed"
            },
            {
                Encoding.UTF8.GetBytes("[credential]\n\thelper = manager-core\\\n"),
                "line continuations"
            },
            {
                Encoding.UTF8.GetBytes("credential.helper = manager-core\n"),
                "top-level Git config content"
            },
            {
                Encoding.UTF8.GetBytes("[credential]\n\thelper manager-core\n"),
                "variable syntax"
            },
            {
                Encoding.UTF8.GetBytes(
                    "[credential \"https://dev.azure.com\\n\"]\n\tuseHttpPath = true\n"),
                "section syntax"
            },
            {
                Encoding.UTF8.GetBytes("[ credential ]\n\thelper = manager-core\n"),
                "section syntax"
            },
            {
                Encoding.UTF8.GetBytes("[credential ]\n\thelper = manager-core\n"),
                "section syntax"
            },
            {
                Encoding.UTF8.GetBytes(
                    "[credential \"https://dev.azure.com\" ]\n\tuseHttpPath = true\n"),
                "section syntax"
            },
            {
                Encoding.UTF8.GetBytes(
                    "[credential \"https://dev.azure.com\" \"extra\"]\n\tuseHttpPath = true\n"),
                "section syntax"
            },
            {
                Encoding.UTF8.GetBytes("[credential]\r\n\thelper = manager-core\n"),
                "mixed Git config newline"
            },
            {
                Encoding.UTF8.GetBytes("[include]\n\tpath = /etc/gitconfig\n"),
                "include/includeIf directives"
            },
            {
                Encoding.UTF8.GetBytes("[includeIf \"gitdir:/work/\"]\n\tpath = /etc/gitconfig\n"),
                "include/includeIf directives"
            },
        };

    public static TheoryData<string> UnsupportedGitConfigSectionHeaderSyntaxCases =>
        new()
        {
            "[ credential ]\n\thelper = manager-core\n",
            "[credential ]\n\thelper = manager-core\n",
            "[credential \"https://dev.azure.com\" ]\n\tuseHttpPath = true\n",
            "[credential \"https://dev.azure.com\" \"extra\"]\n\tuseHttpPath = true\n",
        };

    [Theory]
    [InlineData("[include]\n\tpath = /etc/gitconfig\n")]
    [InlineData("[includeIf \"gitdir:/work/\"]\n\tpath = /etc/gitconfig\n")]
    public async Task DryRunRejectsGitConfigIncludesWithoutMutation(string existingGitConfig)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-include-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("include/includeIf directives", exception.Message);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task ValidatePlanDryRunAndApplyRejectUnsupportedGitConfigKey()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-unsupported-key-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.username",
                "hcoona"
            )
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        var dryRunException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        var applyException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains("supports only credential.helper", validationResult.Violation);
        Assert.Contains("supports only credential.helper", dryRunException.Message);
        Assert.Contains("supports only credential.helper", applyException.Message);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task DryRunRejectsUnownedExistingGitConfigKeyWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-unowned-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("not proven to be owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
        );
    }

    [Fact]
    public async Task DryRunRejectsUnsupportedGitConfigTargetSyntaxWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-syntax-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[credential]\n\thelper manager-core\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("variable syntax", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [MemberData(nameof(UnsupportedGitConfigSectionHeaderSyntaxCases))]
    public async Task DryRunRejectsUnsupportedGitConfigSectionHeaderSyntaxWithoutMutation(
        string existingGitConfig
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-dry-run-section-syntax-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("section syntax", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ValidatePlanDryRunAndApplyRejectUseHttpPathValuesOtherThanTrue()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-use-http-path-false-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "false"
            )
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        var dryRunException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        var applyException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains("canonical value true", validationResult.Violation);
        Assert.Contains("canonical value true", dryRunException.Message);
        Assert.Contains("canonical value true", applyException.Message);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ValidatePlanDryRunAndApplyRejectShellSnippetCredentialHelperValues()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-helper-shell-snippet-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = manager-core
            """;
        const string shellSnippetHelperValue = "!echo hcoona-azureauth";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                shellSnippetHelperValue
            )
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        var dryRunException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
        fileSystem.Calls.Clear();
        var applyException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains("shell snippet", validationResult.Violation, StringComparison.Ordinal);
        Assert.Contains("shell snippet", dryRunException.Message, StringComparison.Ordinal);
        Assert.Contains("shell snippet", applyException.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyAllowsHelperWriteWhenLaterFalseShadowsEarlierTruthyUseHttpPath()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-use-http-path-shadowed-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential "https://dev.azure.com"]
            useHttpPath = true
            useHttpPath = false
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        string expectedGitConfig = string.Join(
            '\n',
            "[credential \"https://dev.azure.com\"]",
            "useHttpPath = true",
            "useHttpPath = false",
            string.Empty,
            "[credential]",
            "\thelper = \"hcoona-azureauth\"",
            string.Empty
        );
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set, "secret-helper", true, "non-secret values")]
    [InlineData(ConfigurationChangeOperation.Set, "bad\u0001value", false, "printable values")]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter, null, false, "remove-adapter")]
    public async Task ValidatePlanAndNoFilesystemDryRunRejectUnsupportedStaticGitConfigInputs(
        ConfigurationChangeOperation operation,
        string? value,
        bool isSecretValue,
        string expectedMessage
    )
    {
        var manager = new ConfigurationManager();
        ConfigurationChange change = CreateGitConfigChange(
            operation,
            "user.gitconfig",
            "credential.helper",
            value,
            previousOwnedEntryMetadata: operation == ConfigurationChangeOperation.RemoveAdapter
                ? "owned-helper"
                : null
        ) with
        {
            IsSecretValue = isSecretValue,
        };
        ConfigurationChangePlan plan = CreateGitConfigPlan(change) with
        {
            ContainsCredentialMaterial = isSecretValue,
        };

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        var dryRunException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains(expectedMessage, validationResult.Violation);
        Assert.Contains(expectedMessage, dryRunException.Message);
    }

    [Fact]
    public async Task NoFilesystemDryRunCanonicalizesGitConfigKeyBeforeReturningManifest()
    {
        var manager = new ConfigurationManager();
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                "user.gitconfig",
                "credential \"https://dev.azure.com\".useHttpPath",
                "true"
            )
        );

        ConfigurationPlanResult result = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        ConfigurationOwnershipManifestEntry entry = Assert.Single(
            result.OwnershipManifest!.Entries
        );
        Assert.Equal("credential.https://dev.azure.com.useHttpPath", entry.Key);
        ConfigurationPlannedOperation operation = Assert.Single(result.PlannedOperations);
        Assert.Equal("credential.https://dev.azure.com.useHttpPath", operation.Change.Key);
    }

    [Fact]
    public async Task ApplyRejectsUnownedUrlmatchEquivalentUseHttpPathAliasWithoutDuplicate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-urlmatch-alias-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential "HTTPS://DEV.AZURE.COM/"]
                useHttpPath = false
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("not proven to be owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyFailsClosedForUnsafeEffectiveUseHttpPathAliasWithoutDuplicate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-urlmatch-unsafe-alias-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential "https://dev.azure.com/org"]
                useHttpPath = false
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("cannot be canonicalized safely", exception.Message);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyFailsClosedForTrailingDotEffectiveUseHttpPathAliasWithoutDuplicate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-urlmatch-trailing-dot-use-http-path-alias-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential "https://dev.azure.com."]
                useHttpPath = false
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("cannot be canonicalized safely", exception.Message);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter, null, false, "remove-adapter")]
    [InlineData(ConfigurationChangeOperation.Set, "secret-helper", true, "non-secret values")]
    [InlineData(ConfigurationChangeOperation.Set, "bad\u0001value", false, "printable values")]
    public async Task ApplyOrRemoveRejectsUnsupportedStaticGitConfigWriterInputsBeforePreclaim(
        ConfigurationChangeOperation operation,
        string? value,
        bool isSecretValue,
        string expectedMessage
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-static-validation-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChange change = CreateGitConfigChange(
            operation,
            targetPath,
            "credential.helper",
            value,
            previousOwnedEntryMetadata: operation == ConfigurationChangeOperation.Set
                ? null
                : "owned-helper"
        ) with
        {
            IsSecretValue = isSecretValue,
        };
        ConfigurationChangePlan plan = CreateGitConfigPlan(change) with
        {
            ContainsCredentialMaterial = isSecretValue,
        };
        fileSystem.Calls.Clear();

        var exception = operation == ConfigurationChangeOperation.Set
            ? await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            )
            : await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.RemoveAsync(plan, TestContext.Current.CancellationToken)
            );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ApplyRejectsSecretGitConfigValueWhenDispatcherLacksPreclaimPolicy()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-policyless-secret-manifest.json";
        const string targetPath = "/config/policyless-secret.gitconfig";
        var dispatchCalled = false;
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new PolicylessCallbackGitConfigDispatcher((_, _) =>
            {
                dispatchCalled = true;
                return ValueTask.CompletedTask;
            })
        );
        ConfigurationChangePlan plan = CreateCredentialMaterialPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ) with
            {
                IsSecretValue = true,
            }
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-secret values", exception.Message, StringComparison.Ordinal);
        Assert.False(dispatchCalled);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ValidatePlanDryRunAndApplyRespectCustomGitConfigDispatcherPolicyAndValidator()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-permissive-dispatcher-manifest.json";
        const string targetPath = "/config/permissive-dispatcher.gitconfig";
        const string existingGitConfig = """
            [credential]
            helper = "manager-core"
            """;
        const string updatedGitConfig = """
            [credential]
            helper = "hcoona-azureauth"
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new PermissiveValidatedGitConfigDispatcher(fileSystem, targetPath, updatedGitConfig)
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth",
                previousOwnedEntryMetadata: HashMetadata("manager-core")
            )
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.True(validationResult.IsValid);
        Assert.Null(validationResult.Violation);
        Assert.Equal(ConfigurationPlanState.Planned, dryRunResult.State);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));

        ConfigurationPlanResult applyResult = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, applyResult.State);
        Assert.Equal(updatedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        ValidatePlanDryRunAndApplyRejectSecretGitConfigChangesWhenDispatcherLacksValidator()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-relaxed-policy-secret-manifest.json";
        const string targetPath = "/config/relaxed-policy-secret.gitconfig";
        var dispatchCalled = false;
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((_, _) =>
            {
                dispatchCalled = true;
                return ValueTask.CompletedTask;
            })
        );
        ConfigurationChangePlan plan = CreateCredentialMaterialPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
            with
            {
                IsSecretValue = true,
            }
        );
        fileSystem.Calls.Clear();

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        var dryRunException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );
        var applyException = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Violation);
        Assert.Contains("non-secret values", validationResult.Violation, StringComparison.Ordinal);
        Assert.Contains("non-secret values", dryRunException.Message, StringComparison.Ordinal);
        Assert.Contains("non-secret values", applyException.Message, StringComparison.Ordinal);
        Assert.False(dispatchCalled);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task ApplyRejectsUnsupportedProjectionOnlyTargetKindBeforePreclaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-unsupported-kind-manifest.json";
        const string targetPath = "/config/nuget-plugin-layout";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = ConfigurationChangePlanPolicy.Create(
            "plan-unsupported-kind-before-preclaim",
            "changeset-unsupported-kind-before-preclaim",
            "azureauth-credprovider",
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-gitconfig-physical-writer",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "git.config",
                ProductVersion = "0.0.0-test",
            },
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
                    TargetPathOrName = targetPath,
                    Key = "install",
                    Value = "planned-value",
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                },
            ]
        );
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("no registered writer", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(ConfigurationTargetKind.NuGetPluginLayout)]
    [InlineData(ConfigurationTargetKind.PythonKeyringBackend)]
    [InlineData(ConfigurationTargetKind.KeyringShim)]
    public async Task
        ValidateDryRunApplyAndRemoveRejectUnsupportedProjectionOnlyKindsBeforePreclaim(
            ConfigurationTargetKind targetKind
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-unsupported-kind-all-ops-manifest.json";
        string targetPath =
            "/config/unsupported-kind-all-ops-"
            + targetKind.ToString().ToLower(CultureInfo.InvariantCulture);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateProjectionOnlyPhysicalTargetPlan(
            targetKind,
            ConfigurationChangeOperation.Set,
            targetPath,
            "unsupported.physical.target",
            "planned-value"
        );
        ConfigurationChangePlan removePlan = CreateProjectionOnlyPhysicalTargetPlan(
            targetKind,
            ConfigurationChangeOperation.Remove,
            targetPath,
            "unsupported.physical.target",
            value: null,
            previousOwnedEntryMetadata: "previous-unsupported-entry"
        );
        fileSystem.Calls.Clear();

        ConfigurationPlanValidationResult applyValidation = manager.ValidatePlan(applyPlan);
        var dryRunException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(applyPlan, TestContext.Current.CancellationToken)
        );
        var applyException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken)
        );
        ConfigurationPlanValidationResult removeValidation = manager.ValidatePlan(removePlan);
        var removeException = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.False(applyValidation.IsValid);
        Assert.NotNull(applyValidation.Violation);
        Assert.Contains("no registered writer", applyValidation.Violation);
        Assert.Contains("no registered writer", dryRunException.Message);
        Assert.Contains("no registered writer", applyException.Message);
        Assert.False(removeValidation.IsValid);
        Assert.NotNull(removeValidation.Violation);
        Assert.Contains("no registered writer", removeValidation.Violation);
        Assert.Contains("no registered writer", removeException.Message);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task DryRunRejectsRemoveAdapterBeforeValidatorOnValidatorOnlyGitConfigDispatcher()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-validator-only-remove-manifest.json";
        const string targetPath = "/config/validator-only-remove.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        ConfigurationChange change = CreateGitConfigChange(
            ConfigurationChangeOperation.RemoveAdapter,
            targetPath,
            "credential.helper",
            null,
            previousOwnedEntryMetadata: HashMetadata("manager-core")
        );
        ConfigurationChangePlan seededPlan = CreateGitConfigPlan(change);
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            new ConfigurationOwnershipManifest
            {
                ManifestId = seededPlan.Manifest.ManifestId,
                PlanId = "existing-gitconfig-validator-only-remove-plan",
                ChangeSetId = "existing-gitconfig-validator-only-remove-changeset",
                OwnerProductId = seededPlan.OwnerProductId,
                Scope = seededPlan.Scope,
                EntrySelector = seededPlan.Manifest.EntrySelector,
                ProductVersion = seededPlan.Manifest.ProductVersion,
                SafeMetadata = new Dictionary<string, string>(),
                ContainsCredentialMaterial = false,
                Entries =
                [
                    new ConfigurationOwnershipManifestEntry
                    {
                        Sequence = 1,
                        Operation = ConfigurationChangeOperation.Set,
                        TargetKind = ConfigurationTargetKind.GitConfig,
                        TargetPathOrName = targetPath,
                        Key = "credential.helper",
                        PreserveDeclarationsAndComments = true,
                        HasPlannedValue = true,
                        IsSecretValue = false,
                        PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("manager-core")),
                    },
                ],
            }
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            HashMetadata(existingManifestJson),
            change
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new ValidatorOnlyGitConfigDispatcher()
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("remove-adapter", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task ApplyRejectsUnownedGitConfigKeyWithoutCommittingManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-unowned-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("not proven to be owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyGlobalHelperRejectsUnownedDevAzureSpecificHelper()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-devazure-helper-conflict-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential "https://dev.azure.com"]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "effective Azure DevOps Git credential helper",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyUseHttpPathRejectsUnownedGlobalHelper()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-use-http-path-global-helper-conflict.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("not proven to be owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("1")]
    public async Task DryRunRejectsUnownedDevAzureUseHttpPathWhenWritingGlobalHelper(
        string existingUseHttpPathValue
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-use-http-path-unowned-helper-dry-run.json";
        const string targetPath = "/config/user.gitconfig";
        string existingGitConfig = string.Join(
            '\n',
            "[credential \"https://dev.azure.com\"]",
            $"    useHttpPath = {existingUseHttpPathValue}",
            string.Empty
        );
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("useHttpPath=true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "CreateDirectory"
                        or "DeleteDirectory"
        );
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("1")]
    public async Task ApplyRejectsUnownedDevAzureUseHttpPathWhenWritingGlobalHelper(
        string existingUseHttpPathValue
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-use-http-path-unowned-helper-apply.json";
        const string targetPath = "/config/user.gitconfig";
        string existingGitConfig = string.Join(
            '\n',
            "[credential \"https://dev.azure.com\"]",
            $"    useHttpPath = {existingUseHttpPathValue}",
            string.Empty
        );
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("useHttpPath=true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "CreateDirectory"
                        or "DeleteDirectory"
        );
    }

    [Fact]
    public async Task ApplyUseHttpPathAcceptsOwnedGlobalHelper()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-use-http-path-owned-helper.json";
        const string targetPath = "/config/user.gitconfig";
        string expectedGitConfig = string.Join(
            '\n',
            "[credential]",
            "\thelper = \"hcoona-azureauth\"",
            string.Empty,
            "[credential \"https://dev.azure.com\"]",
            "\tuseHttpPath = \"true\"",
            string.Empty
        );
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan helperPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(helperPlan, TestContext.Current.CancellationToken);
        string helperManifestJson = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan useHttpPathPlan = CreateGitConfigPlan(
            HashMetadata(helperManifestJson),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            useHttpPathPlan,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Collection(
            result.OwnershipManifest!.Entries,
            entry => Assert.Equal("credential.helper", entry.Key),
            entry => Assert.Equal("credential.https://dev.azure.com.useHttpPath", entry.Key)
        );
    }

    [Theory]
    [InlineData("https://dev.azure.com/org")]
    [InlineData("https://dev.azure.com.")]
    public async Task ApplyUseHttpPathFailsClosedForUnsafeEffectiveHelperAlias(
        string subsection
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-helper-unsafe-alias-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        string existingGitConfig = $"""
            [credential "{subsection}"]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("cannot be canonicalized safely", exception.Message);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsSpoofedSetOwnershipMetadataForUnownedGitConfigKey()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-spoofed-set-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = manager-core
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth",
                previousOwnedEntryMetadata: "bogus-owned-entry"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("not proven to be owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackGitConfigAndDoesNotCommitManifestWhenWriterFailsAfterMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-writer-failure-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [user]
                name = Keep Me
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new MutatingThenFailingGitConfigDispatcher(
                fileSystem,
                targetPath,
                "[credential]\n\thelper = \"partial\"\n"
            )
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "simulated Git config writer failure",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsMissingCompletedGitConfigMutationCoverageBeforeFinalManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-missing-mutation-coverage-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "every Git config target path",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackRequiresRollbackFalseGitConfigMutationWithoutNoOpProof()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-forced-rollback-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string targetContents = "[credential]\n\thelper = \"hcoona-azureauth\"\n";
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileSystem.AtomicWriteAllText(targetPath, targetContents);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: null,
                        ExpectedCurrentSha256Hash: Sha256Hex(
                            Encoding.UTF8.GetBytes(targetContents)
                        ),
                        RequiresRollback: false
                    )
                );
                throw new InvalidOperationException("simulated post-target failure");
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "simulated post-target failure",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackExistingGitConfigNoOpReportWhenCurrentHashDiffers()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-existing-noop-negative-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string originalContents = "[credential]\n\thelper = \"original-helper\"\n";
        const string mutatedContents = "[credential]\n\thelper = \"mutated-helper\"\n";
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalContents);
        byte[] mutatedBytes = Encoding.UTF8.GetBytes(mutatedContents);
        fileSystem.AtomicWriteAllText(targetPath, originalContents);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-gitconfig-noop-plan",
            ChangeSetId = "existing-gitconfig-noop-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = targetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(originalBytes),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: true,
                        PreviousContentsBytes: originalBytes,
                        ExpectedCurrentSha256Hash: Sha256Hex(mutatedBytes),
                        RequiresRollback: false
                    )
                );
                throw new InvalidOperationException("simulated post-target failure");
            })
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "current value hash does not match",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(originalContents, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        ApplyRejectsInconsistentCompletedMutationSnapshotWithoutDeletingExistingGitConfig()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-inconsistent-mutation-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        byte[] existingGitConfigBytes = Encoding.UTF8.GetBytes(existingGitConfig);
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: existingGitConfigBytes,
                        ExpectedCurrentSha256Hash: Sha256Hex(existingGitConfigBytes)
                    )
                );
                throw new InvalidOperationException("simulated post-target failure");
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "invalid completed file mutation",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        ApplyRejectsDuplicateInconsistentCompletedMutationWithoutDeletingExistingGitConfig()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-duplicate-inconsistent-mutation-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        byte[] existingGitConfigBytes = Encoding.UTF8.GetBytes(existingGitConfig);
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: null,
                        ExpectedCurrentSha256Hash: Sha256Hex(existingGitConfigBytes)
                    )
                );
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: existingGitConfigBytes,
                        ExpectedCurrentSha256Hash: Sha256Hex(existingGitConfigBytes)
                    )
                );
                throw new InvalidOperationException("simulated post-target failure");
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("duplicate completed file", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackValidDuplicateMutationBeforeRejectingInconsistentDuplicate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-valid-duplicate-inconsistent-mutation-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        const string mutatedGitConfig = "[credential]\n\thelper = \"partial\"\n";
        byte[] existingGitConfigBytes = Encoding.UTF8.GetBytes(existingGitConfig);
        byte[] mutatedGitConfigBytes = Encoding.UTF8.GetBytes(mutatedGitConfig);
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileSystem.AtomicWriteAllText(targetPath, mutatedGitConfig);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: true,
                        PreviousContentsBytes: existingGitConfigBytes,
                        ExpectedCurrentSha256Hash: Sha256Hex(mutatedGitConfigBytes)
                    )
                );
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: existingGitConfigBytes,
                        ExpectedCurrentSha256Hash: Sha256Hex(mutatedGitConfigBytes)
                    )
                );
                throw new InvalidOperationException("simulated post-target failure");
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("duplicate completed file", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Theory]
    [InlineData("", "empty path")]
    [InlineData("/config/unrelated.gitconfig", "unrelated Git config target path")]
    public async Task ApplyRejectsInvalidCompletedMutationPathBeforeFinalManifestAndRollback(
        string reportedPath,
        string expectedMessage
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-invalid-mutation-path-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string unrelatedContents = "[credential]\n\thelper = \"unrelated\"\n";
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(reportedPath))
                {
                    fileSystem.AtomicWriteAllText(reportedPath, unrelatedContents);
                }

                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        reportedPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: null,
                        ExpectedCurrentSha256Hash: Sha256Hex(
                            Encoding.UTF8.GetBytes(unrelatedContents)
                        )
                    )
                );
                return ValueTask.CompletedTask;
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (!string.IsNullOrEmpty(reportedPath))
        {
            Assert.Equal(unrelatedContents, fileSystem.ReadAllText(reportedPath));
        }
    }

    [Theory]
    [InlineData("", "empty path")]
    [InlineData("/config/unrelated.gitconfig", "unrelated Git config target path")]
    public async Task ApplyRollsBackValidGitConfigMutationBeforeRejectingExtraCompletedMutationPath(
        string reportedPath,
        string expectedMessage
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-valid-plus-invalid-mutation-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        const string targetContents = "[credential]\n\thelper = \"hcoona-azureauth\"\n";
        const string unrelatedContents = "[credential]\n\thelper = \"unrelated\"\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] previousContents = fileSystem.ReadAllBytes(targetPath);
                fileSystem.AtomicWriteAllText(targetPath, targetContents);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        PreviouslyExisted: true,
                        PreviousContentsBytes: previousContents,
                        ExpectedCurrentSha256Hash: Sha256Hex(
                            Encoding.UTF8.GetBytes(targetContents)
                        )
                    )
                );
                if (!string.IsNullOrEmpty(reportedPath))
                {
                    fileSystem.AtomicWriteAllText(reportedPath, unrelatedContents);
                }

                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        reportedPath,
                        PreviouslyExisted: false,
                        PreviousContentsBytes: null,
                        ExpectedCurrentSha256Hash: Sha256Hex(
                            Encoding.UTF8.GetBytes(unrelatedContents)
                        )
                    )
                );
                return ValueTask.CompletedTask;
            })
        );
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
        if (!string.IsNullOrEmpty(reportedPath))
        {
            Assert.Equal(unrelatedContents, fileSystem.ReadAllText(reportedPath));
        }
    }

    [Fact]
    public async Task
        CredentialApplyRethrowsOperationCanceledWhenCancellationOccursAfterPreclaimRollback()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cancel-after-preclaim-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var dispatchCalled = false;
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((_, _) =>
            {
                dispatchCalled = true;
                return ValueTask.CompletedTask;
            })
        );
        ConfigurationChangePlan plan = CreateCredentialMaterialPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        using CancellationTokenSource cancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var preclaimWritten = false;
        fileSystem.AfterRecord = (call, _) =>
        {
            if (
                !preclaimWritten
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                preclaimWritten = true;
                cancellationSource.Cancel();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await manager.ApplyAsync(plan, cancellationSource.Token)
        );
        fileSystem.AfterRecord = null;

        Assert.True(preclaimWritten);
        Assert.False(dispatchCalled);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        CredentialApplyRestoresRetainedGitConfigManifestWhenCanceledAfterPreclaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cancel-retained-after-preclaim.json";
        const string helperTargetPath = "/config/cancel-retained-helper.gitconfig";
        const string retainedTargetPath = "/config/cancel-retained-usehttppath.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string retainedGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(retainedTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                helperTargetPath,
                "credential.helper",
                "hcoona-azureauth-updated",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-cancel-retained-after-preclaim-plan",
            ChangeSetId = "existing-cancel-retained-after-preclaim-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = retainedTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        using CancellationTokenSource cancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var dispatchCalled = false;
        var preclaimWritten = false;
        fileSystem.AfterRecord = (call, _) =>
        {
            if (
                preclaimWritten
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                || call.Value?.Contains(
                    "hcoona.azureAuthCredProvider.physicalTargetManifestState",
                    StringComparison.Ordinal
                ) != true
            )
            {
                return;
            }

            preclaimWritten = true;
            cancellationSource.Cancel();
        };
        var dispatcher = new RetainedValidatingCallbackGitConfigDispatcher(
            fileSystem,
            (_, _) =>
            {
                dispatchCalled = true;
                return ValueTask.CompletedTask;
            }
        );
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await manager.ApplyAsync(plan, cancellationSource.Token)
        );
        fileSystem.AfterRecord = null;

        Assert.True(preclaimWritten);
        Assert.False(dispatchCalled);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(retainedTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        CredentialApplyRethrowsOperationCanceledWhenDispatcherCancelsAfterMutationRollback()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cancel-inside-dispatcher-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        const string partialGitConfig = "[credential]\n\thelper = \"hcoona-partial\"\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new CallbackGitConfigDispatcher((request, cancellationToken) =>
            {
                byte[] previousContents = fileSystem.ReadAllBytes(targetPath);
                fileSystem.AtomicWriteAllText(targetPath, partialGitConfig);
                request.RegisterCompletedFileMutation(
                    new ConfigurationPhysicalTargetFileMutation(
                        targetPath,
                        true,
                        previousContents,
                        Sha256Hex(Encoding.UTF8.GetBytes(partialGitConfig))
                    )
                );
                throw new OperationCanceledException(
                    "dispatcher cancellation",
                    cancellationToken
                );
            })
        );
        ConfigurationChangePlan plan = CreateCredentialMaterialPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );

        OperationCanceledException exception =
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
            );

        Assert.Contains("dispatcher cancellation", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task CredentialApplyUsesSanitizedRollbackFailedErrorWhenRollbackFails()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-credential-rollback-failed-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new MutatingThenFailingGitConfigDispatcher(
                fileSystem,
                targetPath,
                "[credential]\n\thelper = \"hcoona-partial\"\n"
            )
        );
        ConfigurationChangePlan plan = CreateCredentialMaterialPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                fs.FailNextCall(new IOException("secret rollback failure"));
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("rollback failed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hcoona-azureauth", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret rollback failure",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CredentialApplyUsesSanitizedIndeterminateErrorWhenFinalCommitIsIndeterminate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-credential-indeterminate-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new MutatingGitConfigDispatcher(
                fileSystem,
                targetPath,
                "[credential]\n\thelper = \"hcoona-azureauth\"\n"
            )
        );
        ConfigurationChangePlan plan = CreateCredentialMaterialPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var manifestWriteCount = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                manifestWriteCount++;
                if (manifestWriteCount == 2)
                {
                    fs.FailNextCall(
                        new FileMutationException(
                            "secret final manifest failure",
                            mutationMayHaveReachedDurableState: true,
                            new IOException("secret durability failure")
                        )
                    );
                }
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("indeterminate", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hcoona-azureauth", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret final manifest failure",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ApplyRollsBackRealGitConfigWriterAfterDurableTargetMutationFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-real-writer-durable-failure-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var injectedFailure = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                injectedFailure
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                || call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) != true
            )
            {
                return;
            }

            injectedFailure = true;
            fs.AtomicWriteAllText(targetPath, call.Value);
            fs.FailNextCall(
                new FileMutationException(
                    "injected durable Git config write failure",
                    mutationMayHaveReachedDurableState: true,
                    new IOException("durable target failure")
                )
            );
        };

        var exception = await Assert.ThrowsAsync<FileMutationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("durable Git config write failure", exception.Message);
        Assert.True(injectedFailure);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRollsBackRealGitConfigWriterAfterDurableTargetMutationFailure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-real-writer-remove-durable-failure-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        byte[] targetBeforeRemove = fileSystem.ReadAllBytes(targetPath);
        byte[] manifestBeforeRemove = fileSystem.ReadAllBytes(manifestPath);
        string manifestBeforeRemoveText = Encoding.UTF8.GetString(manifestBeforeRemove);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemoveText),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                null,
                previousOwnedEntryMetadata: "owned-use-http-path"
            )
        );
        var injectedFailure = false;
        string? durableRemoveContents = null;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                injectedFailure
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                || call.Value is null
            )
            {
                return;
            }

            injectedFailure = true;
            durableRemoveContents = call.Value;
            fs.AtomicWriteAllText(targetPath, call.Value);
            fs.FailNextCall(
                new FileMutationException(
                    "injected durable Git config remove failure",
                    mutationMayHaveReachedDurableState: true,
                    new IOException("durable remove target failure")
                )
            );
        };

        var exception = await Assert.ThrowsAsync<FileMutationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("durable Git config remove failure", exception.Message);
        Assert.True(injectedFailure);
        Assert.NotNull(durableRemoveContents);
        string durableRemoveContentsText = durableRemoveContents!;
        Assert.DoesNotContain(
            "hcoona-azureauth",
            durableRemoveContentsText,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("useHttpPath", durableRemoveContentsText, StringComparison.Ordinal);
        Assert.Equal(targetBeforeRemove, fileSystem.ReadAllBytes(targetPath));
        Assert.Equal(manifestBeforeRemove, fileSystem.ReadAllBytes(manifestPath));
        Assert.DoesNotContain(
            "hcoona.azureAuthCredProvider.physicalTargetManifestState",
            fileSystem.ReadAllText(manifestPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ApplyRejectsConcurrentGitConfigEditBetweenReadAndWriteWithoutStaleManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-concurrent-before-write-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        const string concurrentGitConfig = "[user]\n\tname = Concurrent\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var injectedConcurrentEdit = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                injectedConcurrentEdit
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                || call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) != true
            )
            {
                return;
            }

            injectedConcurrentEdit = true;
            fs.AtomicWriteAllText(targetPath, concurrentGitConfig);
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("before-state hash", exception.Message, StringComparison.Ordinal);
        Assert.True(injectedConcurrentEdit);
        Assert.Equal(concurrentGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task
        ApplyRejectsConcurrentGitConfigEditDuringFinalManifestCommitWithoutStaleManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-concurrent-during-final-commit-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = "[user]\n\tname = Keep Me\n";
        const string concurrentGitConfig = "[user]\n\tname = Concurrent\n";
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var injectedConcurrentEdit = false;
        var manifestWriteCount = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                return;
            }

            manifestWriteCount++;
            if (injectedConcurrentEdit || manifestWriteCount != 2)
            {
                return;
            }

            injectedConcurrentEdit = true;
            fs.AtomicWriteAllText(targetPath, concurrentGitConfig);
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "completed physical target mutation current hash",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(injectedConcurrentEdit);
        Assert.Equal(concurrentGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveDeletesOwnedGitConfigKeysAndPreservesUnrelatedContent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-remove-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            # keep remove comment
            [user]
                email = user@example.com
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                null,
                previousOwnedEntryMetadata: "owned-use-http-path"
            )
        );

        ConfigurationPlanResult result = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        string gitConfig = fileSystem.ReadAllText(targetPath);
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Null(result.OwnershipManifest);
        Assert.Contains("# keep remove comment", gitConfig, StringComparison.Ordinal);
        Assert.Contains("[user]", gitConfig, StringComparison.Ordinal);
        Assert.Contains("email = user@example.com", gitConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("hcoona-azureauth", gitConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("useHttpPath", gitConfig, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task UpdateRewritesOwnedGitConfigKeyAndPreservesUnrelatedContent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-update-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [user]
                name = Keep Me
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeUpdate = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan updatePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeUpdate),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                targetPath,
                "credential.helper",
                "hcoona-azureauth-updated",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            updatePlan,
            TestContext.Current.CancellationToken
        );

        string gitConfig = fileSystem.ReadAllText(targetPath);
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Contains("[user]", gitConfig, StringComparison.Ordinal);
        Assert.Contains("name = Keep Me", gitConfig, StringComparison.Ordinal);
        Assert.Contains(
            "helper = \"hcoona-azureauth-updated\"",
            gitConfig,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "helper = \"hcoona-azureauth\"\n",
            gitConfig,
            StringComparison.Ordinal
        );
        Assert.True(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task UpdateRejectsStaleOwnedGitConfigPhysicalValueWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-stale-physical-update-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string staleGitConfig = "[credential]\n\thelper = \"out-of-band\"\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeUpdate = fileSystem.ReadAllText(manifestPath);
        fileSystem.AtomicWriteAllText(targetPath, staleGitConfig);
        ConfigurationChangePlan updatePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeUpdate),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                targetPath,
                "credential.helper",
                "hcoona-azureauth-updated",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(updatePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("current value hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal(staleGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBeforeUpdate, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsStaleOwnedGitConfigPhysicalValueWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-stale-physical-remove-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string staleGitConfig = "[credential]\n\thelper = \"out-of-band\"\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        fileSystem.AtomicWriteAllText(targetPath, staleGitConfig);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("current value hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal(staleGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBeforeRemove, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        UpdateOrRemoveHelperRejectsRetainedUseHttpPathFalseManifestAndPhysicalValueWithoutMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-retained-false-use-http-path-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                operation == ConfigurationPlanOperation.Apply
                    ? ConfigurationChangeOperation.Update
                    : ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                operation == ConfigurationPlanOperation.Apply
                    ? "hcoona-azureauth-updated"
                    : null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-retained-false-use-http-path-plan",
            ChangeSetId = "existing-retained-false-use-http-path-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = targetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = targetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("false")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);

        var exception =
            operation == ConfigurationPlanOperation.Apply
                ? await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.ApplyAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                )
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.RemoveAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                );

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        UpdateOrRemoveHelperRejectsCrossFileRetainedUseHttpPathFalsePhysicalValueWithoutMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cross-file-use-http-path-manifest.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                operation == ConfigurationPlanOperation.Apply
                    ? ConfigurationChangeOperation.Update
                    : ConfigurationChangeOperation.Remove,
                helperTargetPath,
                "credential.helper",
                operation == ConfigurationPlanOperation.Apply
                    ? "hcoona-azureauth-updated"
                    : null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-cross-file-use-http-path-plan",
            ChangeSetId = "existing-cross-file-use-http-path-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);

        var exception =
            operation == ConfigurationPlanOperation.Apply
                ? await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.ApplyAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                )
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.RemoveAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                );

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(useHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        UpdateOrRemoveHelperRejectsCrossFileRetainedSecretGitConfigManifestEntryWithoutMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cross-file-secret-manifest-entry.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string secretTargetPath = "/config/secret.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string secretGitConfig = """
            [credential]
                helper = "secret-helper"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(secretTargetPath, secretGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                operation == ConfigurationPlanOperation.Apply
                    ? ConfigurationChangeOperation.Update
                    : ConfigurationChangeOperation.Remove,
                helperTargetPath,
                "credential.helper",
                operation == ConfigurationPlanOperation.Apply
                    ? "hcoona-azureauth-updated"
                    : null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-cross-file-secret-entry-plan",
            ChangeSetId = "existing-cross-file-secret-entry-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            ContainsCredentialMaterial = true,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = secretTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = true,
                    PlannedValueSha256 = null,
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);

        var exception =
            operation == ConfigurationPlanOperation.Apply
                ? await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.ApplyAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                )
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.RemoveAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                );

        Assert.Contains("non-secret value-writing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(secretGitConfig, fileSystem.ReadAllText(secretTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        UpdateOrRemoveHelperRejectsCrossFileRetainedNonValueGitConfigManifestEntryWithoutMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cross-file-non-value-manifest-entry.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string retainedTargetPath = "/config/retained.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string retainedGitConfig = """
            [credential]
                helper = "retained-helper"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(retainedTargetPath, retainedGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                operation == ConfigurationPlanOperation.Apply
                    ? ConfigurationChangeOperation.Update
                    : ConfigurationChangeOperation.Remove,
                helperTargetPath,
                "credential.helper",
                operation == ConfigurationPlanOperation.Apply
                    ? "hcoona-azureauth-updated"
                    : null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-cross-file-non-value-entry-plan",
            ChangeSetId = "existing-cross-file-non-value-entry-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Remove,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = retainedTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = false,
                    IsSecretValue = false,
                    PlannedValueSha256 = null,
                    PreviousOwnedEntryMetadata = "owned-retained-helper",
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = CreateManager(fileSystem, manifestPath);

        var exception =
            operation == ConfigurationPlanOperation.Apply
                ? await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.ApplyAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                )
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.RemoveAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                );

        Assert.Contains("non-secret value-writing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(retainedTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
    }

    [Fact]
    public async Task
        ApplyLeavesPreclaimWhenCrossFileRetainedUseHttpPathDriftsBeforeCommitAndFollowUpsReject()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-cross-file-use-http-path-drift-manifest.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string updatedHelperValue = "hcoona-azureauth-updated";
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        const string driftedUseHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                helperTargetPath,
                "credential.helper",
                updatedHelperValue,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-cross-file-use-http-path-drift-plan",
            ChangeSetId = "existing-cross-file-use-http-path-drift-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var driftInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                driftInjected
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, helperTargetPath, StringComparison.Ordinal)
                || call.Value?.Contains(updatedHelperValue, StringComparison.Ordinal) != true
            )
            {
                return;
            }

            driftInjected = true;
            fs.AtomicWriteAllText(useHttpPathTargetPath, driftedUseHttpPathGitConfig);
        };
        var manager = CreateManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(targetedPlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.True(driftInjected);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(driftedUseHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        string preclaimManifestJson = fileSystem.ReadAllText(manifestPath);
        Assert.NotEqual(existingManifestJson, preclaimManifestJson);
        Assert.Contains(
            "hcoona.azureAuthCredProvider.physicalTargetManifestState",
            preclaimManifestJson,
            StringComparison.Ordinal
        );
        Assert.Contains("prepared", preclaimManifestJson, StringComparison.Ordinal);

        await AssertFollowUpGitConfigOperationsRejectReservedPreclaimManifestAsync(
            fileSystem,
            manifestPath,
            preclaimManifestJson,
            helperTargetPath,
            helperGitConfig,
            useHttpPathTargetPath,
            driftedUseHttpPathGitConfig
        );
    }

    [Fact]
    public async Task
        ApplyLeavesPreclaimManifestWhenCrossFileRetainedUseHttpPathDriftsAfterFinalManifestWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-cross-file-use-http-path-post-commit-drift-manifest.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string updatedHelperValue = "hcoona-azureauth-updated";
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        const string driftedUseHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                helperTargetPath,
                "credential.helper",
                updatedHelperValue,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-cross-file-use-http-path-post-commit-drift-plan",
            ChangeSetId = "existing-cross-file-use-http-path-post-commit-drift-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var manifestWriteCount = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                return;
            }

            manifestWriteCount++;
            if (manifestWriteCount == 2)
            {
                fs.AtomicWriteAllText(useHttpPathTargetPath, driftedUseHttpPathGitConfig);
            }
        };
        var manager = CreateManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(targetedPlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.True(manifestWriteCount >= 2);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(driftedUseHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        string preclaimManifestJson = fileSystem.ReadAllText(manifestPath);
        Assert.NotEqual(existingManifestJson, preclaimManifestJson);
        Assert.Contains(
            "hcoona.azureAuthCredProvider.physicalTargetManifestState",
            preclaimManifestJson,
            StringComparison.Ordinal
        );
        Assert.Contains("prepared", preclaimManifestJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        ApplyLeavesPreclaimWhenDispatchFailsOrCancelsAfterPreclaimAndRetainedGitConfigDrifts(
            bool cancelAfterPreclaim
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-post-preclaim-dispatch-failure-retained-drift-manifest.json";
        const string helperTargetPath = "/config/post-preclaim-helper.gitconfig";
        const string retainedTargetPath = "/config/post-preclaim-retained.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string updatedHelperValue = "hcoona-azureauth-updated";
        const string retainedGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        const string driftedRetainedGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(retainedTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                helperTargetPath,
                "credential.helper",
                updatedHelperValue,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-post-preclaim-dispatch-failure-retained-drift-plan",
            ChangeSetId = "existing-post-preclaim-dispatch-failure-retained-drift-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = retainedTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        using var cancellation = new CancellationTokenSource();
        var driftInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                driftInjected
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                || call.Value?.Contains(
                    "hcoona.azureAuthCredProvider.physicalTargetManifestState",
                    StringComparison.Ordinal
                ) != true
            )
            {
                return;
            }

            driftInjected = true;
            fs.AtomicWriteAllText(retainedTargetPath, driftedRetainedGitConfig);
            if (cancelAfterPreclaim)
            {
                cancellation.Cancel();
            }
        };
        var dispatcher = new RetainedValidatingCallbackGitConfigDispatcher(
            fileSystem,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "simulated dispatch failure after manifest preclaim"
                );
            }
        );
        var manager = new ConfigurationManager(fileSystem, manifestPath, dispatcher);

        if (cancelAfterPreclaim)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await manager.ApplyAsync(plan, cancellation.Token)
            );
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ApplyAsync(plan, cancellation.Token)
            );
            Assert.Contains(
                "simulated dispatch failure",
                exception.Message,
                StringComparison.Ordinal
            );
        }

        fileSystem.AfterRecord = null;

        Assert.True(driftInjected);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(driftedRetainedGitConfig, fileSystem.ReadAllText(retainedTargetPath));
        string preclaimManifestJson = fileSystem.ReadAllText(manifestPath);
        Assert.NotEqual(existingManifestJson, preclaimManifestJson);
        Assert.Contains(
            "hcoona.azureAuthCredProvider.physicalTargetManifestState",
            preclaimManifestJson,
            StringComparison.Ordinal
        );
        Assert.Contains("prepared", preclaimManifestJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        UpdateOrRemoveGenericFileRejectsRetainedUseHttpPathFalsePhysicalValueWithoutMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/generic-retained-use-http-path-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string genericValue = "owned-value";
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGenericFilePlan(
            operation == ConfigurationPlanOperation.Apply
                ? ConfigurationChangeOperation.Update
                : ConfigurationChangeOperation.Remove,
            genericTargetPath,
            operation == ConfigurationPlanOperation.Apply ? "updated-value" : null,
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-generic-retained-use-http-path-plan",
            ChangeSetId = "existing-generic-retained-use-http-path-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = genericTargetPath,
                    Key = "file",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes(genericValue)),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception =
            operation == ConfigurationPlanOperation.Apply
                ? await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.ApplyAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                )
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.RemoveAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                );

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(useHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
    }

    [Fact]
    public async Task
        ApplyGenericFileRejectsRetainedSecretGitConfigManifestEntryWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/generic-retained-secret-gitconfig-entry.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string gitConfigTargetPath = "/config/secret.gitconfig";
        const string genericValue = "owned-value";
        const string gitConfigValue = """
            [credential]
                helper = "secret-helper"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(gitConfigTargetPath, gitConfigValue);
        ConfigurationChangePlan targetedPlan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-generic-retained-secret-gitconfig-plan",
            ChangeSetId = "existing-generic-retained-secret-gitconfig-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            ContainsCredentialMaterial = true,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = genericTargetPath,
                    Key = "file",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes(genericValue)),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = gitConfigTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = true,
                    PlannedValueSha256 = null,
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(targetedPlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("non-secret value-writing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(gitConfigValue, fileSystem.ReadAllText(gitConfigTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
    }

    [Fact]
    public async Task DryRunGenericFileRejectsRetainedUseHttpPathFalsePhysicalValueWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/generic-dry-run-retained-use-http-path-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string genericValue = "owned-value";
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-generic-dry-run-retained-use-http-path-plan",
            ChangeSetId = "existing-generic-dry-run-retained-use-http-path-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = genericTargetPath,
                    Key = "file",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes(genericValue)),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("useHttpPath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(useHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "DeleteDirectory"
        );
    }

    [Fact]
    public async Task ApplyGenericFileRejectsRetainedHelperDriftInAnotherGitConfigFile()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/generic-retained-helper-drift-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string helperTargetPath = "/config/retained-helper.gitconfig";
        const string genericValue = "owned-value";
        const string retainedHelperValue = "hcoona-azureauth";
        const string driftedHelperGitConfig = """
            [credential]
                helper = "out-of-band-helper"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(helperTargetPath, driftedHelperGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-generic-retained-helper-drift-plan",
            ChangeSetId = "existing-generic-retained-helper-drift-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = genericTargetPath,
                    Key = "file",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes(genericValue)),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes(retainedHelperValue)
                    ),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("current value hash", exception.Message, StringComparison.Ordinal);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(driftedHelperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "DeleteDirectory"
        );
    }

    [Fact]
    public async Task
        ApplyGenericFileRejectsRetainedUseHttpPathWhenUnownedGlobalHelperAppearsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/generic-retained-use-http-path-unowned-helper-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string gitConfigTargetPath = "/config/retained-use-http-path.gitconfig";
        const string genericValue = "owned-value";
        const string retainedUseHttpPathValue = "true";
        const string retainedGitConfig = """
            [credential]
                helper = "foreign-helper"
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(gitConfigTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateGenericFileAndRetainedGitConfigManifest(
                plan,
                genericTargetPath,
                genericValue,
                gitConfigTargetPath,
                "credential.https://dev.azure.com.useHttpPath",
                retainedUseHttpPathValue
            );
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("not proven to be owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "DeleteDirectory"
        );
    }

    [Fact]
    public async Task
        ApplyGenericFileRejectsRetainedHelperWhenUrlSpecificHelperAppearsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/generic-retained-helper-url-specific-helper-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string gitConfigTargetPath = "/config/retained-helper.gitconfig";
        const string genericValue = "owned-value";
        const string retainedHelperValue = "hcoona-azureauth";
        const string retainedGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            [credential "https://dev.azure.com"]
                helper = "foreign-helper"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(gitConfigTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateGenericFileAndRetainedGitConfigManifest(
                plan,
                genericTargetPath,
                genericValue,
                gitConfigTargetPath,
                "credential.helper",
                retainedHelperValue
            );
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "effective Azure DevOps Git credential helper",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "DeleteDirectory"
        );
    }

    [Fact]
    public async Task
        ApplyGenericFileRejectsRetainedHelperWhenUnsafeEffectiveAliasAppearsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/generic-retained-helper-unsafe-alias-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string gitConfigTargetPath = "/config/retained-helper-unsafe-alias.gitconfig";
        const string genericValue = "owned-value";
        const string retainedHelperValue = "hcoona-azureauth";
        const string retainedGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            [credential "https://dev.azure.com/org"]
                helper = "foreign-helper"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(gitConfigTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateGenericFileAndRetainedGitConfigManifest(
                plan,
                genericTargetPath,
                genericValue,
                gitConfigTargetPath,
                "credential.helper",
                retainedHelperValue
            );
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "cannot be canonicalized safely",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation
                    is "WriteAllText"
                        or "AtomicWriteAllText"
                        or "AtomicWriteAllBytes"
                        or "DeleteFile"
                        or "DeleteDirectory"
        );
    }

    [Fact]
    public async Task ApplyGenericFileRejectsStaleFinalManifestAfterRetainedGitConfigValidation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/generic-retained-final-manifest-stale.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string gitConfigTargetPath = "/config/retained-helper-final-check.gitconfig";
        const string genericValue = "owned-value";
        const string retainedHelperValue = "hcoona-azureauth";
        const string retainedGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(gitConfigTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            "updated-value",
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateGenericFileAndRetainedGitConfigManifest(
                plan,
                genericTargetPath,
                genericValue,
                gitConfigTargetPath,
                "credential.helper",
                retainedHelperValue
            );
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var finalManifestWritten = false;
        var staleManifestInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !finalManifestWritten
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                finalManifestWritten = true;
                return;
            }

            if (
                finalManifestWritten
                && !staleManifestInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                staleManifestInjected = true;
                fs.AtomicWriteAllText(manifestPath, existingManifestJson);
            }
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(staleManifestInjected);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(retainedGitConfig, fileSystem.ReadAllText(gitConfigTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        ApplyGenericFileKeepsManifestWhenRetainedGitConfigDriftsBeforeFinalManifestWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/generic-retained-post-mutation-drift-manifest.json";
        const string genericTargetPath = "/config/generic-owned.txt";
        const string gitConfigTargetPath = "/config/retained-helper-post-mutation.gitconfig";
        const string genericValue = "owned-value";
        const string updatedGenericValue = "updated-value";
        const string retainedHelperValue = "hcoona-azureauth";
        const string retainedGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string driftedGitConfig = """
            [credential]
                helper = "out-of-band-helper"
            """;
        fileSystem.AtomicWriteAllText(genericTargetPath, genericValue);
        fileSystem.AtomicWriteAllText(gitConfigTargetPath, retainedGitConfig);
        ConfigurationChangePlan plan = CreateGenericFilePlan(
            ConfigurationChangeOperation.Update,
            genericTargetPath,
            updatedGenericValue,
            previousOwnedEntryMetadata: HashMetadata(genericValue)
        );
        ConfigurationOwnershipManifest existingManifest =
            CreateGenericFileAndRetainedGitConfigManifest(
                plan,
                genericTargetPath,
                genericValue,
                gitConfigTargetPath,
                "credential.helper",
                retainedHelperValue
            );
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var driftInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                driftInjected
                || !string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                || !string.Equals(call.Path, genericTargetPath, StringComparison.Ordinal)
                || !string.Equals(call.Value, updatedGenericValue, StringComparison.Ordinal)
            )
            {
                return;
            }

            driftInjected = true;
            fs.AtomicWriteAllText(gitConfigTargetPath, driftedGitConfig);
        };
        var manager = new ConfigurationManager(fileSystem, manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("current value hash", exception.Message, StringComparison.Ordinal);
        Assert.False(exception.Data.Contains("ConfigurationRollbackFailure"));
        Assert.True(driftInjected);
        Assert.Equal(genericValue, fileSystem.ReadAllText(genericTargetPath));
        Assert.Equal(driftedGitConfig, fileSystem.ReadAllText(gitConfigTargetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(ConfigurationPlanOperation.Apply)]
    [InlineData(ConfigurationPlanOperation.Remove)]
    public async Task
        UpdateOrRemoveHelperRejectsStaleRetainedUseHttpPathHashWithoutMutation(
            ConfigurationPlanOperation operation
        )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-retained-stale-hash-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyBothPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );
        await manager.ApplyAsync(applyBothPlan, TestContext.Current.CancellationToken);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        string gitConfigBefore = fileSystem.ReadAllText(targetPath);
        string staleGitConfig = gitConfigBefore.Replace(
            "useHttpPath = \"true\"",
            "useHttpPath = \"false\"",
            StringComparison.Ordinal
        );
        Assert.NotEqual(gitConfigBefore, staleGitConfig);
        fileSystem.AtomicWriteAllText(targetPath, staleGitConfig);
        fileSystem.Calls.Clear();
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            HashMetadata(manifestBefore),
            CreateGitConfigChange(
                operation == ConfigurationPlanOperation.Apply
                    ? ConfigurationChangeOperation.Update
                    : ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                operation == ConfigurationPlanOperation.Apply
                    ? "hcoona-azureauth-updated"
                    : null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );

        var exception =
            operation == ConfigurationPlanOperation.Apply
                ? await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.ApplyAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                )
                : await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.RemoveAsync(
                        targetedPlan,
                        TestContext.Current.CancellationToken
                    )
                );

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(staleGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                call.Operation is "AtomicWriteAllText" or "AtomicWriteAllBytes" or "DeleteFile"
        );
        Assert.Contains(
            "helper = \"hcoona-azureauth\"",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "hcoona-azureauth-updated",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AliasGitConfigKeyIsStoredCanonicallyAndCanBeRemovedByEitherSpelling()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-alias-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential \"https://dev.azure.com\".useHttpPath",
                "true"
            )
        );
        ConfigurationPlanResult applyResult = await manager.ApplyAsync(
            applyPlan,
            TestContext.Current.CancellationToken
        );
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                null,
                previousOwnedEntryMetadata: "owned-use-http-path"
            )
        );

        ConfigurationPlanResult removeResult = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.Collection(
            applyResult.OwnershipManifest!.Entries,
            entry => Assert.Equal("credential.https://dev.azure.com.useHttpPath", entry.Key)
        );
        Assert.Single(applyResult.OwnershipManifest.Entries);
        Assert.Contains(
            "[credential \"https://dev.azure.com\"]",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "credential \"https://dev.azure.com\".useHttpPath",
            manifestBeforeRemove,
            StringComparison.Ordinal
        );
        Assert.Null(removeResult.OwnershipManifest);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            "useHttpPath",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RemoveSingleGitConfigKeyGoldenRetainsRemainingCanonicalManifestEntry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-remove-single-golden-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                null,
                previousOwnedEntryMetadata: "owned-use-http-path"
            )
        );

        ConfigurationPlanResult removeResult = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        string expectedGitConfig = string.Join(
            '\n',
            "[credential]",
            "\thelper = \"hcoona-azureauth\"",
            string.Empty,
            "[credential \"https://dev.azure.com\"]",
            string.Empty
        );
        string finalManifestJson = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest finalManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(finalManifestJson);
        ConfigurationOwnershipManifestEntry remainingEntry = Assert.Single(
            finalManifest.Entries
        );
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(ConfigurationPlanState.Applied, removeResult.State);
        Assert.Equal("credential.helper", remainingEntry.Key);
        Assert.Equal(targetPath, remainingEntry.TargetPathOrName);
        Assert.Equal(
            Sha256Hex(Encoding.UTF8.GetBytes("hcoona-azureauth")),
            remainingEntry.PlannedValueSha256
        );
        Assert.DoesNotContain(
            "credential.https://dev.azure.com.useHttpPath",
            finalManifestJson,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RemoveFullGitConfigBatchGoldenDeletesManifestAndKeepsExactBytes()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-remove-full-batch-golden-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                null,
                previousOwnedEntryMetadata: "owned-use-http-path"
            )
        );

        ConfigurationPlanResult removeResult = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        string expectedGitConfig = string.Join(
            '\n',
            "[credential]",
            string.Empty,
            "[credential \"https://dev.azure.com\"]",
            string.Empty
        );
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Null(removeResult.OwnershipManifest);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyCanonicalizesRetainedGitConfigManifestEntriesBeforeFinalWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-retained-canonical-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string retainedManifestTargetPath = "/config/sub/../user.gitconfig";
        const string aliasKey = "credential \"https://dev.azure.com\".useHttpPath";
        const string canonicalKey = "credential.https://dev.azure.com.useHttpPath";
        const string existingGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-retained-canonical-plan",
            ChangeSetId = "existing-retained-canonical-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = retainedManifestTargetPath,
                    Key = aliasKey,
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        string finalManifestJson = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest finalManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(finalManifestJson);
        Assert.Equal(ConfigurationPlanState.Applied, result.State);
        Assert.Collection(
            finalManifest.Entries,
            entry =>
            {
                Assert.Equal(targetPath, entry.TargetPathOrName);
                Assert.Equal(canonicalKey, entry.Key);
            },
            entry =>
            {
                Assert.Equal(targetPath, entry.TargetPathOrName);
                Assert.Equal("credential.helper", entry.Key);
            }
        );
        Assert.DoesNotContain(aliasKey, finalManifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            retainedManifestTargetPath,
            finalManifestJson,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ApplyRejectsUnsupportedRetainedGitConfigManifestEntryBeforePreclaim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-retained-unsupported-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string existingGitConfig = """
            [credential]
                username = "hcoona"
            """;
        fileSystem.AtomicWriteAllText(targetPath, existingGitConfig);
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-retained-unsupported-plan",
            ChangeSetId = "existing-retained-unsupported-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = targetPath,
                    Key = "credential.username",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("hcoona")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        plan = plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        fileSystem.Calls.Clear();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("supports only credential.helper", exception.Message);
        Assert.Equal(existingGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(existingManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task DryRunRejectsDuplicateAcceptedGitConfigAliasesWithoutWritingManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-duplicate-alias-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential \"https://dev.azure.com\".useHttpPath",
                "true"
            )
        );

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.Contains("same canonical physical key", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsStaleManifestHashBeforeGitConfigMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-stale-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string gitConfigBefore = fileSystem.ReadAllText(targetPath);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan staleUpdatePlan = CreateGitConfigPlan(
            HashMetadata("stale-manifest"),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                targetPath,
                "credential.helper",
                "hcoona-azureauth-updated",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(staleUpdatePlan, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "before-state hash does not match",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(gitConfigBefore, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyAcquiresLifecycleLockForGitConfigBeforePhysicalWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-lifecycle-lock-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        fileSystem.Calls.Clear();

        await manager.ApplyAsync(plan, TestContext.Current.CancellationToken);

        int lockCallIndex = fileSystem.Calls.FindIndex(call =>
            string.Equals(
                call.Operation,
                nameof(IFileSystemMutationLock.AcquireMutationLock),
                StringComparison.Ordinal
            )
        );
        int targetWriteCallIndex = fileSystem.Calls.FindIndex(call =>
            string.Equals(
                call.Operation,
                nameof(IFileSystem.AtomicWriteAllText),
                StringComparison.Ordinal
            )
            && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
        int manifestWriteCallIndex = fileSystem.Calls.FindIndex(call =>
            string.Equals(
                call.Operation,
                nameof(IFileSystem.AtomicWriteAllText),
                StringComparison.Ordinal
            )
            && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
        );
        Assert.InRange(lockCallIndex, 0, targetWriteCallIndex - 1);
        Assert.InRange(lockCallIndex, 0, manifestWriteCallIndex - 1);
        Assert.Contains(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Path,
                    "/state/.azureauth-credprovider.lifecycle-locks",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public async Task ApplyRejectsConcurrentGitConfigEditBeforeManifestCommit()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-physical-hash-revalidation-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string concurrentGitConfig = "[credential]\n\thelper = \"concurrent\"\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var sawTargetWrite = false;
        var injectedConcurrentEdit = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) == true
            )
            {
                sawTargetWrite = true;
                return;
            }

            if (
                sawTargetWrite
                && !injectedConcurrentEdit
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                injectedConcurrentEdit = true;
                fs.AtomicWriteAllText(targetPath, concurrentGitConfig);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "completed physical target mutation current hash",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(injectedConcurrentEdit);
        Assert.Equal(concurrentGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackGitConfigWhenConcurrentManifestDriftDoesNotAdoptPhysicalBytes()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-concurrent-unrelated-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        string unrelatedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest! with { Entries = [] }
        );
        fileSystem.Calls.Clear();
        var wroteUnrelatedManifest = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !wroteUnrelatedManifest
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) == true
            )
            {
                wroteUnrelatedManifest = true;
                fs.AtomicWriteAllText(manifestPath, unrelatedManifestJson);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(wroteUnrelatedManifest);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal(unrelatedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyDoesNotRegisterNoOpGitConfigObservationForRollbackWrites()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-noop-rollback-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string expectedGitConfig = "[credential]\n\thelper = \"hcoona-azureauth\"\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeNoOp = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan noOpUpdatePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeNoOp),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                targetPath,
                "credential.helper",
                "hcoona-azureauth",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        fileSystem.Calls.Clear();
        var manifestWriteCount = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                manifestWriteCount++;
                if (manifestWriteCount == 2)
                {
                    fs.FailNextCall(new IOException("Injected final manifest commit failure."));
                }
            }
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.ApplyAsync(noOpUpdatePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest commit failure", exception.Message);
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ApplyDoesNotRollBackGitConfigWhenConcurrentManifestAdoptsPhysicalBytes()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-concurrent-adoption-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string expectedGitConfig = "[credential]\n\thelper = \"hcoona-azureauth\"\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        string adoptedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest!
        );
        fileSystem.Calls.Clear();
        var adoptedManifest = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !adoptedManifest
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) == true
            )
            {
                adoptedManifest = true;
                fs.AtomicWriteAllText(manifestPath, adoptedManifestJson);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(adoptedManifest);
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(adoptedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        ApplyRejectsConcurrentFinalManifestAdoptionWhenCrossFileRetainedUseHttpPathDriftsFalse()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-adoption-retained-use-http-path-drift-manifest.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string updatedHelperValue = "hcoona-azureauth-updated";
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        const string driftedUseHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                helperTargetPath,
                "credential.helper",
                updatedHelperValue,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-adoption-retained-use-http-path-drift-plan",
            ChangeSetId = "existing-adoption-retained-use-http-path-drift-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            targetedPlan,
            TestContext.Current.CancellationToken
        );
        string adoptedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest!
        );
        fileSystem.Calls.Clear();
        var helperWriteSeen = false;
        var adoptedManifest = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (adoptedManifest)
            {
                return;
            }

            if (
                !helperWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, helperTargetPath, StringComparison.Ordinal)
                && call.Value?.Contains(updatedHelperValue, StringComparison.Ordinal) == true
            )
            {
                helperWriteSeen = true;
                return;
            }

            if (
                helperWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, helperTargetPath, StringComparison.Ordinal)
            )
            {
                adoptedManifest = true;
                fs.AtomicWriteAllText(manifestPath, adoptedManifestJson);
                fs.AtomicWriteAllText(useHttpPathTargetPath, driftedUseHttpPathGitConfig);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(targetedPlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.True(adoptedManifest);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(driftedUseHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        Assert.Equal(adoptedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyFailsClosedWhenRetainedGitConfigEntryDriftsWithoutRetainedValidator()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-retained-drift-manifest.json";
        const string helperTargetPath = "/config/user.gitconfig";
        const string useHttpPathTargetPath = "/config/azure-devops.gitconfig";
        const string helperGitConfig = """
            [credential]
                helper = "hcoona-azureauth"
            """;
        const string updatedHelperValue = "hcoona-azureauth-updated";
        const string useHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "true"
            """;
        const string driftedUseHttpPathGitConfig = """
            [credential "https://dev.azure.com"]
                useHttpPath = "false"
            """;
        fileSystem.AtomicWriteAllText(helperTargetPath, helperGitConfig);
        fileSystem.AtomicWriteAllText(useHttpPathTargetPath, useHttpPathGitConfig);
        ConfigurationChangePlan targetedPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                helperTargetPath,
                "credential.helper",
                updatedHelperValue,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var existingManifest = new ConfigurationOwnershipManifest
        {
            ManifestId = targetedPlan.Manifest.ManifestId,
            PlanId = "existing-retained-drift-plan",
            ChangeSetId = "existing-retained-drift-changeset",
            OwnerProductId = targetedPlan.OwnerProductId,
            Scope = targetedPlan.Scope,
            EntrySelector = targetedPlan.Manifest.EntrySelector,
            ProductVersion = targetedPlan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = helperTargetPath,
                    Key = "credential.helper",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(
                        Encoding.UTF8.GetBytes("hcoona-azureauth")
                    ),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = useHttpPathTargetPath,
                    Key = "credential.https://dev.azure.com.useHttpPath",
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("true")),
                },
            ],
        };
        string existingManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest
        );
        fileSystem.AtomicWriteAllText(manifestPath, existingManifestJson);
        targetedPlan = targetedPlan with
        {
            Manifest = targetedPlan.Manifest with
            {
                PreviousOwnedEntryHash = HashMetadata(existingManifestJson),
            },
        };
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new MutatingGitConfigDispatcher(fileSystem, helperTargetPath, string.Join(
                '\n',
                "[credential]",
                "\thelper = \"hcoona-azureauth-updated\"",
                string.Empty
            ))
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            targetedPlan,
            TestContext.Current.CancellationToken
        );
        string adoptedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest!
        );
        fileSystem.Calls.Clear();
        var helperWriteSeen = false;
        var adoptedManifest = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (adoptedManifest)
            {
                return;
            }

            if (
                !helperWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, helperTargetPath, StringComparison.Ordinal)
                && call.Value?.Contains(updatedHelperValue, StringComparison.Ordinal) == true
            )
            {
                helperWriteSeen = true;
                return;
            }

            if (
                helperWriteSeen
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, helperTargetPath, StringComparison.Ordinal)
            )
            {
                adoptedManifest = true;
                fs.AtomicWriteAllText(manifestPath, adoptedManifestJson);
                fs.AtomicWriteAllText(useHttpPathTargetPath, driftedUseHttpPathGitConfig);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(targetedPlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("canonical value true", exception.Message, StringComparison.Ordinal);
        Assert.True(adoptedManifest);
        Assert.Equal(helperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(driftedUseHttpPathGitConfig, fileSystem.ReadAllText(useHttpPathTargetPath));
        Assert.Equal(adoptedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackGitConfigWhenFinalAdoptionManifestCollidesWithManifestPath()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-adoption-manifest-collision.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifestEntry preparedEntry = Assert.Single(
            dryRunResult.OwnershipManifest!.Entries
        );
        string invalidAdoptedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest with
            {
                Entries =
                [
                    preparedEntry,
                    preparedEntry with
                    {
                        Sequence = 2,
                        TargetPathOrName = manifestPath,
                        PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes("collision")),
                    },
                ],
            }
        );
        var adoptedManifest = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !adoptedManifest
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) == true
            )
            {
                adoptedManifest = true;
                fs.AtomicWriteAllText(manifestPath, invalidAdoptedManifestJson);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(adoptedManifest);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal(invalidAdoptedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRollsBackGitConfigWhenAdoptionManifestParentBecomesReparsePoint()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-adoption-parent-reparse-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        string adoptedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest!
        );
        var injectedUnsafeParent = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !injectedUnsafeParent
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && call.Value?.Contains("hcoona-azureauth", StringComparison.Ordinal) == true
            )
            {
                injectedUnsafeParent = true;
                fs.AtomicWriteAllText(manifestPath, adoptedManifestJson);
                fs.MarkAsNonSymbolicReparsePoint("/state");
            }
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "ownership manifest parent paths",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(injectedUnsafeParent);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.True(((IFileSystemReparsePointSafety)fileSystem).IsReparsePoint("/state"));
    }

    [Fact]
    public async Task RemoveDoesNotRollBackGitConfigWhenCurrentFinalManifestAdoptsAllRemovals()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-remove-final-adoption-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string expectedGitConfig = "[credential]\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest existingManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestBeforeRemove);
        string adoptedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            existingManifest with
            {
                PlanId = "external-remove-adoption-plan",
                ChangeSetId = "external-remove-adoption-changeset",
                Entries = [],
            }
        );
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var adoptedManifest = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !adoptedManifest
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
                && call.Value is not null
                && !call.Value.Contains("hcoona-azureauth", StringComparison.Ordinal)
            )
            {
                adoptedManifest = true;
                fs.AtomicWriteAllText(manifestPath, adoptedManifestJson);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(adoptedManifest);
        Assert.Equal(expectedGitConfig, fileSystem.ReadAllText(targetPath));
        Assert.Equal(adoptedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsFinalManifestEditAfterCommitBeforeReturn()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-final-post-commit-edit-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        string editedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest! with
            {
                PlanId = "concurrent-final-edit-plan",
                ChangeSetId = "concurrent-final-edit-changeset",
                Entries = [],
            }
        );
        fileSystem.Calls.Clear();
        var manifestWriteCount = 0;
        var awaitingPostCommitVerification = false;
        var editInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                manifestWriteCount++;
                if (manifestWriteCount == 2)
                {
                    awaitingPostCommitVerification = true;
                }

                return;
            }

            if (
                awaitingPostCommitVerification
                && !editInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                awaitingPostCommitVerification = false;
                editInjected = true;
                fs.AtomicWriteAllText(manifestPath, editedManifestJson);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(editInjected);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal(editedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsFinalManifestDeleteAfterCommitBeforeReturn()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-final-post-commit-delete-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        var manifestWriteCount = 0;
        var awaitingPostCommitVerification = false;
        var deleteInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                manifestWriteCount++;
                if (manifestWriteCount == 2)
                {
                    awaitingPostCommitVerification = true;
                }

                return;
            }

            if (
                awaitingPostCommitVerification
                && !deleteInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                awaitingPostCommitVerification = false;
                deleteInjected = true;
                fs.DeleteFile(manifestPath);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(deleteInjected);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task RemoveRejectsFinalManifestRecreateAfterDeleteBeforeReturn()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-final-post-delete-recreate-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string targetBeforeRemove = fileSystem.ReadAllText(targetPath);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var awaitingPostDeleteVerification = false;
        var recreateInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !awaitingPostDeleteVerification
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                awaitingPostDeleteVerification = true;
                return;
            }

            if (
                awaitingPostDeleteVerification
                && !recreateInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                awaitingPostDeleteVerification = false;
                recreateInjected = true;
                fs.AtomicWriteAllText(manifestPath, manifestBeforeRemove);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(recreateInjected);
        Assert.Equal(targetBeforeRemove, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBeforeRemove, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task ApplyRejectsFinalManifestEditDuringPostFinalValidationBeforeReturn()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-final-post-validation-edit-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        ConfigurationPlanResult dryRunResult = await manager.DryRunAsync(
            plan,
            TestContext.Current.CancellationToken
        );
        string editedManifestJson = ConfigurationOwnershipManifestSerializer.Serialize(
            dryRunResult.OwnershipManifest! with
            {
                PlanId = "concurrent-post-validation-edit-plan",
                ChangeSetId = "concurrent-post-validation-edit-changeset",
                Entries = [],
            }
        );
        fileSystem.Calls.Clear();
        var manifestWriteCount = 0;
        var finalManifestVerificationObserved = false;
        var editInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !editInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                manifestWriteCount++;
                return;
            }

            if (
                manifestWriteCount == 2
                && !finalManifestVerificationObserved
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.ReadAllBytes),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                finalManifestVerificationObserved = true;
                return;
            }

            if (
                finalManifestVerificationObserved
                && !editInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                editInjected = true;
                fs.AtomicWriteAllText(manifestPath, editedManifestJson);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(plan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(finalManifestVerificationObserved);
        Assert.True(editInjected);
        Assert.False(fileSystem.FileExists(targetPath));
        Assert.Equal(editedManifestJson, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        ApplyPhase4DDoesNotRestoreStaleOwnedManifestWhenPostFinalGitConfigRollbackFails()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-post-final-target-rollback-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        const string concurrentGitConfig = "[credential]\n\thelper = \"concurrent-owner\"\n";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string manifestBeforeUpdate = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest staleOwnershipManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestBeforeUpdate);
        ConfigurationOwnershipManifestEntry staleOwnershipEntry = Assert.Single(
            staleOwnershipManifest.Entries
        );
        ConfigurationChangePlan updatePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeUpdate),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Update,
                targetPath,
                "credential.helper",
                "hcoona-azureauth-updated",
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var manifestWriteCount = 0;
        var awaitingPostFinalTargetRevalidation = false;
        var concurrentEditInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                manifestWriteCount++;
                if (manifestWriteCount == 2)
                {
                    awaitingPostFinalTargetRevalidation = true;
                }

                return;
            }

            if (
                awaitingPostFinalTargetRevalidation
                && !concurrentEditInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                awaitingPostFinalTargetRevalidation = false;
                concurrentEditInjected = true;
                fs.AtomicWriteAllText(targetPath, concurrentGitConfig);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ApplyAsync(updatePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains(
            "completed physical target mutation current hash",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(concurrentEditInjected);
        Assert.True(exception.Data.Contains("ConfigurationRollbackFailure"));
        Assert.Equal(concurrentGitConfig, fileSystem.ReadAllText(targetPath));
        if (fileSystem.FileExists(manifestPath))
        {
            string manifestAfterRollbackFailure = fileSystem.ReadAllText(manifestPath);
            Assert.NotEqual(manifestBeforeUpdate, manifestAfterRollbackFailure);
            Assert.Contains(
                "hcoona.azureAuthCredProvider.physicalTargetManifestState",
                manifestAfterRollbackFailure,
                StringComparison.Ordinal
            );
            ConfigurationOwnershipManifest manifestAfterRollbackFailureModel =
                ConfigurationOwnershipManifestSerializer.Deserialize(
                    manifestAfterRollbackFailure
                );
            Assert.DoesNotContain(
                manifestAfterRollbackFailureModel.Entries,
                entry =>
                    entry.TargetKind == staleOwnershipEntry.TargetKind
                    && string.Equals(
                        entry.TargetPathOrName,
                        staleOwnershipEntry.TargetPathOrName,
                        StringComparison.Ordinal
                    )
                    && string.Equals(entry.Key, staleOwnershipEntry.Key, StringComparison.Ordinal)
                    && string.Equals(
                        entry.PlannedValueSha256,
                        staleOwnershipEntry.PlannedValueSha256,
                        StringComparison.Ordinal
                    )
            );
        }
    }

    [Fact]
    public async Task RemoveRejectsFinalManifestRecreateDuringPostFinalValidationBeforeReturn()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath =
            "/state/gitconfig-final-post-validation-recreate-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string targetBeforeRemove = fileSystem.ReadAllText(targetPath);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );
        var finalManifestDeleteObserved = false;
        var finalMissingManifestVerificationObserved = false;
        var recreateInjected = false;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                !recreateInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.DeleteFile),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                finalManifestDeleteObserved = true;
                return;
            }

            if (
                finalManifestDeleteObserved
                && !finalMissingManifestVerificationObserved
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
            )
            {
                finalMissingManifestVerificationObserved = true;
                return;
            }

            if (
                finalMissingManifestVerificationObserved
                && !recreateInjected
                && string.Equals(
                    call.Operation,
                    nameof(IFileSystem.FileExists),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
            )
            {
                recreateInjected = true;
                fs.AtomicWriteAllText(manifestPath, manifestBeforeRemove);
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest changed", exception.Message, StringComparison.Ordinal);
        Assert.True(finalMissingManifestVerificationObserved);
        Assert.True(recreateInjected);
        Assert.Equal(targetBeforeRemove, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBeforeRemove, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task
        RemoveRollsBackRealGitConfigWriterTargetAndManifestWhenFinalManifestWriteFails()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-remove-final-write-failure-manifest.json";
        const string targetPath = "/config/user.gitconfig";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan applyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );
        await manager.ApplyAsync(applyPlan, TestContext.Current.CancellationToken);
        string targetBeforeRemove = fileSystem.ReadAllText(targetPath);
        string manifestBeforeRemove = fileSystem.ReadAllText(manifestPath);
        ConfigurationChangePlan removePlan = CreateGitConfigPlan(
            HashMetadata(manifestBeforeRemove),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                targetPath,
                "credential.https://dev.azure.com.useHttpPath",
                null,
                previousOwnedEntryMetadata: "owned-use-http-path"
            )
        );
        var manifestWriteCount = 0;
        fileSystem.AfterRecord = (call, fs) =>
        {
            if (
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, manifestPath, StringComparison.Ordinal)
                && ++manifestWriteCount == 2
            )
            {
                fs.FailNextCall(new IOException("Injected remove final manifest write failure."));
            }
        };

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await manager.RemoveAsync(removePlan, TestContext.Current.CancellationToken)
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("final manifest write failure", exception.Message);
        Assert.Equal(targetBeforeRemove, fileSystem.ReadAllText(targetPath));
        Assert.Equal(manifestBeforeRemove, fileSystem.ReadAllText(manifestPath));
    }

    [Fact(
        Skip = "Windows filesystem identity is case-insensitive.",
        SkipWhen = nameof(IsWindows)
    )]
    public async Task DryRunRejectsGitConfigBatchPathsThatDifferOnlyByCaseOnNonWindows()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string manifestPath = "/state/gitconfig-path-case-manifest.json";
        var manager = CreateManager(fileSystem, manifestPath);
        ConfigurationChangePlan plan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                "/config/User.gitconfig",
                "credential.helper",
                "hcoona-azureauth"
            ),
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                "/config/user.gitconfig",
                "credential.https://dev.azure.com.useHttpPath",
                "true"
            )
        );

        ConfigurationPlanValidationResult validationResult = manager.ValidatePlan(plan);
        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.DryRunAsync(plan, TestContext.Current.CancellationToken)
        );

        Assert.False(validationResult.IsValid);
        Assert.Contains("same normalized physical path", validationResult.Violation);
        Assert.Contains("same normalized physical path", exception.Message);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    private static async Task AssertFollowUpGitConfigOperationsRejectReservedPreclaimManifestAsync(
        InMemoryFileSystem fileSystem,
        string manifestPath,
        string preclaimManifestJson,
        string helperTargetPath,
        string expectedHelperGitConfig,
        string retainedTargetPath,
        string expectedRetainedGitConfig
    )
    {
        const string followUpTargetPath = "/config/follow-up-after-reserved-preclaim.gitconfig";
        ConfigurationChangePlan followUpApplyPlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Set,
                followUpTargetPath,
                "credential.helper",
                "follow-up-helper"
            )
        );
        ConfigurationChangePlan followUpRemovePlan = CreateGitConfigPlan(
            CreateGitConfigChange(
                ConfigurationChangeOperation.Remove,
                helperTargetPath,
                "credential.helper",
                null,
                previousOwnedEntryMetadata: "owned-helper"
            )
        );

        fileSystem.Calls.Clear();
        InvalidOperationException dryRunException =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CreateManager(fileSystem, manifestPath)
                    .DryRunAsync(followUpApplyPlan, TestContext.Current.CancellationToken)
            );
        AssertReservedPreclaimFollowUpRejection(
            dryRunException,
            fileSystem,
            manifestPath,
            preclaimManifestJson,
            helperTargetPath,
            expectedHelperGitConfig,
            retainedTargetPath,
            expectedRetainedGitConfig,
            followUpTargetPath
        );

        var dispatchCallCount = 0;
        var rejectingDispatcher = new CallbackGitConfigDispatcher((_, _) =>
        {
            dispatchCallCount++;
            throw new InvalidOperationException("Follow-up dispatcher should not be invoked.");
        });
        var followUpManager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            rejectingDispatcher
        );

        fileSystem.Calls.Clear();
        InvalidOperationException applyException =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await followUpManager.ApplyAsync(
                    followUpApplyPlan,
                    TestContext.Current.CancellationToken
                )
            );
        Assert.Equal(0, dispatchCallCount);
        AssertReservedPreclaimFollowUpRejection(
            applyException,
            fileSystem,
            manifestPath,
            preclaimManifestJson,
            helperTargetPath,
            expectedHelperGitConfig,
            retainedTargetPath,
            expectedRetainedGitConfig,
            followUpTargetPath
        );

        fileSystem.Calls.Clear();
        InvalidOperationException removeException =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await followUpManager.RemoveAsync(
                    followUpRemovePlan,
                    TestContext.Current.CancellationToken
                )
            );
        Assert.Equal(0, dispatchCallCount);
        AssertReservedPreclaimFollowUpRejection(
            removeException,
            fileSystem,
            manifestPath,
            preclaimManifestJson,
            helperTargetPath,
            expectedHelperGitConfig,
            retainedTargetPath,
            expectedRetainedGitConfig,
            followUpTargetPath
        );
    }

    private static void AssertReservedPreclaimFollowUpRejection(
        InvalidOperationException exception,
        InMemoryFileSystem fileSystem,
        string manifestPath,
        string preclaimManifestJson,
        string helperTargetPath,
        string expectedHelperGitConfig,
        string retainedTargetPath,
        string expectedRetainedGitConfig,
        string followUpTargetPath
    )
    {
        Assert.Contains("reserved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("preclaim metadata", exception.Message, StringComparison.Ordinal);
        Assert.Equal(preclaimManifestJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(expectedHelperGitConfig, fileSystem.ReadAllText(helperTargetPath));
        Assert.Equal(expectedRetainedGitConfig, fileSystem.ReadAllText(retainedTargetPath));
        Assert.False(fileSystem.FileExists(followUpTargetPath));
        AssertNoGitConfigPhysicalMutationCalls(fileSystem.Calls);
    }

    private static ConfigurationManager CreateManager(
        InMemoryFileSystem fileSystem,
        string manifestPath
    ) =>
        new(
            fileSystem,
            manifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

    private static ConfigurationChangePlan CreateGitConfigPlan(
        params ConfigurationChange[] changes
    ) => CreateGitConfigPlan(null, changes);

    private static ConfigurationChangePlan CreateGitConfigPlan(
        string? previousManifestHash,
        params ConfigurationChange[] changes
    ) =>
        ConfigurationChangePlanPolicy.Create(
            "plan-gitconfig-physical-writer",
            "changeset-gitconfig-physical-writer",
            "azureauth-credprovider",
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-gitconfig-physical-writer",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "git.config",
                ProductVersion = "0.0.0-test",
                PreviousOwnedEntryHash = previousManifestHash,
            },
            changes
        );

    private static ConfigurationChangePlan CreateCredentialMaterialPlan(
        params ConfigurationChange[] changes
    ) =>
        CreateGitConfigPlan(changes) with
        {
            ContainsCredentialMaterial = true,
        };

    private static ConfigurationChangePlan CreateProjectionOnlyPhysicalTargetPlan(
        ConfigurationTargetKind targetKind,
        ConfigurationChangeOperation operation,
        string targetPath,
        string key,
        string? value,
        string? previousOwnedEntryMetadata = null
    ) =>
        ConfigurationChangePlanPolicy.Create(
            "plan-unsupported-projection-only-kind",
            "changeset-unsupported-projection-only-kind",
            "azureauth-credprovider",
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-gitconfig-physical-writer",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "git.config",
                ProductVersion = "0.0.0-test",
            },
            [
                new ConfigurationChange
                {
                    Operation = operation,
                    TargetKind = targetKind,
                    TargetPathOrName = targetPath,
                    Key = key,
                    Value = value,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = true,
                    PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
                },
            ]
        );

    private static ConfigurationChangePlan CreateGenericFilePlan(
        ConfigurationChangeOperation operation,
        string targetPath,
        string? value,
        string? previousOwnedEntryMetadata = null
    ) =>
        ConfigurationChangePlanPolicy.Create(
            $"plan-generic-file-{operation}",
            $"changeset-generic-file-{operation}",
            "azureauth-credprovider",
            ConfigurationScope.CiTemporary,
            new ConfigurationManifestMetadata
            {
                ManifestId = "manifest-generic-file",
                OwnerProductId = "azureauth-credprovider",
                EntrySelector = "generic.file",
                ProductVersion = "0.0.0-test",
            },
            [
                new ConfigurationChange
                {
                    Operation = operation,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = targetPath,
                    Key = "file",
                    Value = value,
                    RequiresOwnershipRecord = true,
                    PreserveDeclarationsAndComments = false,
                    PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
                },
            ],
            temporaryContainer: CreateTemporaryHomeContainer(
                GetParentConfigurationPath(targetPath)
            ),
            declarationPreservation:
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig
        );

    private static ConfigurationTemporaryContainer CreateTemporaryHomeContainer(
        string productOwnedPath
    ) =>
        new()
        {
            Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
            ProductOwnedPath = productOwnedPath,
            ActivationEnvironment = new ConfigurationActivationEnvironment
            {
                Platform = "posix",
                SetVariables = new Dictionary<string, string>
                {
                    ["HOME"] = productOwnedPath,
                },
                ClearVariables = [],
            },
        };

    private static string GetParentConfigurationPath(string path)
    {
        int separatorIndex = path.LastIndexOf('/');
        if (separatorIndex <= 0)
        {
            return "/";
        }

        return path[..separatorIndex];
    }

    private static ConfigurationChange CreateGitConfigChange(
        ConfigurationChangeOperation operation,
        string targetPath,
        string key,
        string? value,
        string? previousOwnedEntryMetadata = null
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.GitConfig,
            TargetPathOrName = targetPath,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
            PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
        };

    private static ConfigurationOwnershipManifest CreateGenericFileAndRetainedGitConfigManifest(
        ConfigurationChangePlan plan,
        string genericTargetPath,
        string genericValue,
        string gitConfigTargetPath,
        string gitConfigKey,
        string gitConfigValue
    ) =>
        new()
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = "existing-generic-retained-gitconfig-plan",
            ChangeSetId = "existing-generic-retained-gitconfig-changeset",
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = new Dictionary<string, string>(),
            Entries =
            [
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 1,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = genericTargetPath,
                    Key = "file",
                    PreserveDeclarationsAndComments = false,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes(genericValue)),
                },
                new ConfigurationOwnershipManifestEntry
                {
                    Sequence = 2,
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = gitConfigTargetPath,
                    Key = gitConfigKey,
                    PreserveDeclarationsAndComments = true,
                    HasPlannedValue = true,
                    IsSecretValue = false,
                    PlannedValueSha256 = Sha256Hex(Encoding.UTF8.GetBytes(gitConfigValue)),
                },
            ],
        };

    private static string HashMetadata(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static void AssertNoGitConfigPhysicalMutationCalls(
        IEnumerable<FileSystemCall> calls
    )
    {
        string[] forbiddenOperations =
        [
            nameof(IFileSystem.WriteAllText),
            nameof(IFileSystem.AtomicWriteAllText),
            nameof(IFileSystem.AtomicWriteAllBytes),
            nameof(IFileSystem.CreateDirectory),
            nameof(IFileSystem.DeleteFile),
            nameof(IFileSystem.DeleteDirectory),
            nameof(IFileSystemMutationLock.AcquireMutationLock),
        ];

        Assert.DoesNotContain(
            calls,
            call => forbiddenOperations.Contains(call.Operation, StringComparer.Ordinal)
        );
    }

    private static void AssertNoFilesystemStateReadCallsBeforeLockAcquisition(
        IReadOnlyList<FileSystemCall> calls
    )
    {
        int lockCallIndex = calls
            .Select((call, index) => (call, index))
            .Single(tuple =>
                string.Equals(
                    tuple.call.Operation,
                    nameof(IFileSystemMutationLock.AcquireMutationLock),
                    StringComparison.Ordinal
                )
            )
            .index;

        Assert.DoesNotContain(calls.Take(lockCallIndex), IsFilesystemStateReadCall);
    }

    private static bool IsFilesystemStateReadCall(FileSystemCall call)
    {
        string[] readOperations =
        [
            nameof(IFileSystem.FileExists),
            nameof(IFileSystem.DirectoryExists),
            nameof(IFileSystem.IsSymbolicLink),
            nameof(IFileSystem.ReadAllText),
            nameof(IFileSystem.ReadAllBytes),
            nameof(IFileSystemReparsePointSafety.IsReparsePoint),
        ];

        return readOperations.Contains(call.Operation, StringComparer.Ordinal);
    }

    private sealed class MutatingThenFailingGitConfigDispatcher(
        InMemoryFileSystem fileSystem,
        string targetPath,
        string mutatedContents
    ) : IConfigurationPhysicalTargetWriterDispatcher,
        IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy
    {
        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => false;

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? previousContents = fileSystem.FileExists(targetPath)
                ? fileSystem.ReadAllBytes(targetPath)
                : null;
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    previousContents is not null,
                    previousContents,
                    Sha256Hex(Encoding.UTF8.GetBytes(mutatedContents))
                )
            );
            throw new InvalidOperationException("simulated Git config writer failure");
        }
    }

    private sealed class MutatingGitConfigDispatcher(
        InMemoryFileSystem fileSystem,
        string targetPath,
        string mutatedContents
    ) : IConfigurationPhysicalTargetWriterDispatcher,
        IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy
    {
        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => false;

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? previousContents = fileSystem.FileExists(targetPath)
                ? fileSystem.ReadAllBytes(targetPath)
                : null;
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    previousContents is not null,
                    previousContents,
                    Sha256Hex(Encoding.UTF8.GetBytes(mutatedContents))
                )
            );
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PermissiveValidatedGitConfigDispatcher(
        InMemoryFileSystem fileSystem,
        string targetPath,
        string mutatedContents
    ) : IConfigurationPhysicalTargetWriterDispatcher,
        IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy,
        IConfigurationPhysicalTargetWriterDispatcherValidator
    {
        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => false;

        public void Validate(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? previousContents = fileSystem.FileExists(targetPath)
                ? fileSystem.ReadAllBytes(targetPath)
                : null;
            fileSystem.AtomicWriteAllText(targetPath, mutatedContents);
            request.RegisterCompletedFileMutation(
                new ConfigurationPhysicalTargetFileMutation(
                    targetPath,
                    previousContents is not null,
                    previousContents,
                    Sha256Hex(Encoding.UTF8.GetBytes(mutatedContents))
                )
            );
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallbackGitConfigDispatcher(
        Func<
            ConfigurationPhysicalTargetWriterRequest,
            CancellationToken,
            ValueTask
        > callback
    ) : IConfigurationPhysicalTargetWriterDispatcher,
        IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy
    {
        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => false;

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        ) => callback(request, cancellationToken);
    }

    private sealed class RetainedValidatingCallbackGitConfigDispatcher(
        IFileSystem fileSystem,
        Func<
            ConfigurationPhysicalTargetWriterRequest,
            CancellationToken,
            ValueTask
        > callback
    ) : IConfigurationPhysicalTargetWriterDispatcher,
        IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy,
        IConfigurationPhysicalTargetRetainedOwnershipProofValidator
    {
        private readonly ConfigurationPhysicalTargetWriterDispatcher retainedProofValidator =
            new(fileSystem);

        public bool RejectSecretGitConfigValueWritesBeforeManifestPreclaim => false;

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        ) => callback(request, cancellationToken);

        public void ValidateRetainedOwnershipProofs(
            IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
            CancellationToken cancellationToken
        ) =>
            retainedProofValidator.ValidateRetainedOwnershipProofs(
                ownershipProofs,
                cancellationToken
            );
    }

    private sealed class PolicylessCallbackGitConfigDispatcher(
        Func<
            ConfigurationPhysicalTargetWriterRequest,
            CancellationToken,
            ValueTask
        > callback
    ) : IConfigurationPhysicalTargetWriterDispatcher
    {
        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        ) => callback(request, cancellationToken);
    }

    private sealed class ValidatorOnlyGitConfigDispatcher
        : IConfigurationPhysicalTargetWriterDispatcher,
            IConfigurationPhysicalTargetWriterDispatcherValidator
    {
        public void Validate(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public ValueTask Dispatch(
            ConfigurationPhysicalTargetWriterRequest request,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private static string Sha256Hex(byte[] value)
    {
        byte[] hash = SHA256.HashData(value);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
