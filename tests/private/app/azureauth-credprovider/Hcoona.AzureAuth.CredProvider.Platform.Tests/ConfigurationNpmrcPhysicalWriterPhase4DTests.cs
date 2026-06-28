using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationNpmrcPhysicalWriterPhase4DTests
{
    public static TheoryData<ConfigurationPlanOperation, ConfigurationChange, string>
        InvalidChangeCases
    {
        get
        {
            string targetPath = CreateValidationTargetPath();
            return new TheoryData<ConfigurationPlanOperation, ConfigurationChange, string>
            {
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(targetPath, ConfigurationChangeOperation.Set, "", "value"),
                    "requires a non-empty key"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry ",
                        "value"
                    ),
                    "surrounding whitespace"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(targetPath, ConfigurationChangeOperation.Set, "bad\rkey", "value"),
                    "supports keys without CR or LF"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry\tname",
                        "value"
                    ),
                    "control characters"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "reg=istry",
                        "value"
                    ),
                    "supports keys without '='"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "#registry",
                        "value"
                    ),
                    "comment markers"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "reg#istry",
                        "value"
                    ),
                    "comment markers"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "reg;istry",
                        "value"
                    ),
                    "comment markers"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        "bad\nvalue"
                    ),
                    "supports values without CR or LF"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        "value\u0001"
                    ),
                    "control characters"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        " value"
                    ),
                    "surrounding whitespace"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        "https://registry.npmjs.org/;comment"
                    ),
                    "comment markers"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "\"registry\"",
                        "value"
                    ),
                    "quoted"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        "'planned-value'"
                    ),
                    "quoted"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "_authToken",
                        "secret"
                    ),
                    "requires auth token values to be marked as secret"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        "secret",
                        isSecretValue: true
                    ),
                    "secret values to use auth token keys"
                },
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        " _authToken",
                        "secret",
                        isSecretValue: true
                    ),
                    "surrounding whitespace"
                },
                {
                    ConfigurationPlanOperation.Remove,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Remove,
                        "registry",
                        "unexpected"
                    ),
                    "supports remove changes without a value"
                },
                {
                    ConfigurationPlanOperation.DryRun,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.RemoveAdapter,
                        "registry",
                        null
                    ),
                    "remove-adapter changes"
                },
                {
                    ConfigurationPlanOperation.DryRun,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.EnsureFile,
                        "registry",
                        null
                    ),
                    "ensure-file changes"
                },
                {
                    ConfigurationPlanOperation.DryRun,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.InstallAdapter,
                        "registry",
                        null
                    ),
                    "install-adapter changes"
                },
            };
        }
    }

    public static TheoryData<ConfigurationPlanOperation, ConfigurationChange, string>
        InvalidRequestShapeCases
    {
        get
        {
            string targetPath = CreateValidationTargetPath();
            return new TheoryData<ConfigurationPlanOperation, ConfigurationChange, string>
            {
                {
                    ConfigurationPlanOperation.Apply,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Remove,
                        "registry",
                        null
                    ),
                    "value-writing changes only for apply"
                },
                {
                    ConfigurationPlanOperation.Remove,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Set,
                        "registry",
                        "https://registry.npmjs.org/"
                    ),
                    "ownership-removing changes only for remove"
                },
                {
                    ConfigurationPlanOperation.Remove,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Update,
                        "registry",
                        "https://new.example/"
                    ),
                    "ownership-removing changes only for remove"
                },
                {
                    ConfigurationPlanOperation.Remove,
                    CreateChange(
                        targetPath,
                        ConfigurationChangeOperation.Refresh,
                        "registry",
                        "https://new.example/"
                    ),
                    "ownership-removing changes only for remove"
                },
            };
        }
    }

    public static TheoryData<string, string> ExistingIniCompatibleValueParsingCases
    {
        get
        {
            return new TheoryData<string, string>
            {
                {
                    "registry=https://registry.npmjs.org/ # comment\n",
                    "https://registry.npmjs.org/"
                },
                {
                    "registry=https://registry.npmjs.org/\\#fragment\n",
                    "https://registry.npmjs.org/#fragment"
                },
                {
                    "registry=https://registry.npmjs.org/\\;fragment\n",
                    "https://registry.npmjs.org/;fragment"
                },
                {
                    "registry='https://registry.npmjs.org/#single;quoted'\n",
                    "https://registry.npmjs.org/#single;quoted"
                },
                {
                    "registry=\"https://registry.npmjs.org/#double;quoted\"\n",
                    "https://registry.npmjs.org/#double;quoted"
                },
                {
                    CreateText(@"registry=foo\\\\bar"),
                    @"foo\\bar"
                },
                {
                    CreateText(@"registry=""https://registry.npmjs.org/"" # comment"),
                    "https://registry.npmjs.org/"
                },
                {
                    CreateText(@"registry=""foo\\bar"""),
                    @"foo\bar"
                },
                {
                    CreateText(@"registry=""foo\qbar"""),
                    @"foo\qbar"
                },
            };
        }
    }

    public static TheoryData<string, string> UnsupportedNpmrcNewlineStyleCases
    {
        get
        {
            return new TheoryData<string, string>
            {
                {
                    "# leading comment\r\nregistry=https://registry.npmjs.org/\n",
                    "mixed or bare-CR Npmrc newline styles"
                },
                {
                    "# leading comment\rregistry=https://registry.npmjs.org/\r",
                    "mixed or bare-CR Npmrc newline styles"
                },
            };
        }
    }

    public static TheoryData<ConfigurationChangeOperation, string?, string>
        SupportedValueWritingOperationCases
    {
        get
        {
            string currentValue = "https://old.example/";
            string plannedValue = "https://new.example/";
            return new TheoryData<ConfigurationChangeOperation, string?, string>
            {
                {
                    ConfigurationChangeOperation.Create,
                    null,
                    CreateText($"registry={plannedValue}")
                },
                {
                    ConfigurationChangeOperation.Create,
                    CreateText("# keep comment", "other=value"),
                    CreateText("# keep comment", "other=value", $"registry={plannedValue}")
                },
                {
                    ConfigurationChangeOperation.Update,
                    CreateText($"registry={currentValue}"),
                    CreateText($"registry={plannedValue}")
                },
                {
                    ConfigurationChangeOperation.Refresh,
                    CreateText($"registry={currentValue}"),
                    CreateText($"registry={currentValue}")
                },
            };
        }
    }

    public static TheoryData<ConfigurationChangeOperation, string, string>
        SupportedNoOpValueWritingOperationCasesWithInlineComments
    {
        get
        {
            return new TheoryData<ConfigurationChangeOperation, string, string>
            {
                {
                    ConfigurationChangeOperation.Update,
                    "https://registry.npmjs.org/",
                    CreateText("registry=https://registry.npmjs.org/ # comment")
                },
                {
                    ConfigurationChangeOperation.Refresh,
                    "https://registry.npmjs.org/",
                    CreateText("registry=https://registry.npmjs.org/ ; comment")
                },
                {
                    ConfigurationChangeOperation.Update,
                    @"foo\bar",
                    CreateText(@"registry=""foo\\bar"" # comment")
                },
                {
                    ConfigurationChangeOperation.Refresh,
                    @"foo\qbar",
                    CreateText(@"registry=""foo\qbar"" # comment")
                },
            };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidChangeCases))]
    public void ValidateRejectsInvalidNpmrcChangeShapes(
        ConfigurationPlanOperation planOperation,
        ConfigurationChange change,
        string expectedMessage
    )
    {
        var writer = new NpmrcPhysicalTargetWriter(
            new InMemoryFileSystem(InMemoryPathSemantics.Host)
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            planOperation,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidRequestShapeCases))]
    public void ValidateRejectsInvalidNpmrcRequestOperationShapes(
        ConfigurationPlanOperation planOperation,
        ConfigurationChange change,
        string expectedMessage
    )
    {
        var writer = new NpmrcPhysicalTargetWriter(
            new InMemoryFileSystem(InMemoryPathSemantics.Host)
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            planOperation,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("_authToken")]
    [InlineData("//evil.example/org/_packaging/feed/npm/registry/:_authToken")]
    [InlineData("//pkgs.dev.azure.com/org/_packaging/feed/npm/:_authToken")]
    public void ValidateRejectsNpmrcSecretAuthTokenSelectorsThatDoNotMatchCanonicalRegistryIdentity(
        string key
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            key,
            "secret-token",
            isSecretValue: true
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        )
        {
            ResourceIdentity = CreateNpmResourceIdentity(),
        };

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("canonical registry identity", exception.Message, StringComparison.Ordinal);
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void ValidateAcceptsCanonicalNpmrcSecretAuthTokenSelector()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        const string canonicalSelector =
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken";
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            canonicalSelector,
            "secret-token",
            isSecretValue: true
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        )
        {
            ResourceIdentity = CreateNpmResourceIdentity(),
        };

        writer.Validate(request, TestContext.Current.CancellationToken);
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void ValidateRejectsDuplicateNpmrcCanonicalKeysWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Set,
                    "registry",
                    "https://registry.npmjs.org/"
                ),
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Set,
                    "registry",
                    "https://registry.npmjs.org/"
                ),
            ],
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "only one change per canonical key",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(request.CompletedFileMutations);
    }

    [Theory]
    [MemberData(nameof(ExistingIniCompatibleValueParsingCases))]
    public void ValidateRetainedOwnershipProofsAcceptsExistingIniCompatibleValues(
        string existingContents,
        string parsedValue
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetOwnershipProof proof = new(
            ConfigurationTargetKind.Npmrc,
            targetPath,
            "registry",
            HashString(parsedValue)
        );

        writer.ValidateRetainedOwnershipProofs(
            [proof],
            TestContext.Current.CancellationToken
        );
    }

    [Theory]
    [MemberData(nameof(UnsupportedNpmrcNewlineStyleCases))]
    public void WriteRejectsUnsupportedNpmrcNewlineStylesWithoutMutation(
        string existingContents,
        string expectedMessage
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        const string currentValue = "https://registry.npmjs.org/";
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Refresh,
                    "registry",
                    currentValue
                ),
            ],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "registry",
                    HashString(currentValue)
                ),
            ]
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Empty(request.CompletedFileMutations);
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
        );
    }

    [Theory]
    [MemberData(nameof(SupportedValueWritingOperationCases))]
    public void ValidateAcceptsSupportedNpmrcValueWritingOperationsWithoutMutation(
        ConfigurationChangeOperation operation,
        string? existingContents,
        string expectedFinalContents
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        if (existingContents is not null)
        {
            fileSystem.AtomicWriteAllText(targetPath, existingContents);
        }

        fileSystem.Calls.Clear();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        const string currentValue = "https://old.example/";
        const string updatedValue = "https://new.example/";
        string plannedValue =
            operation == ConfigurationChangeOperation.Refresh ? currentValue : updatedValue;
        ConfigurationChange change = CreateChange(
            targetPath,
            operation,
            "registry",
            plannedValue
        );
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs =
            operation is ConfigurationChangeOperation.Update or ConfigurationChangeOperation.Refresh
                ? [
                    new ConfigurationPhysicalTargetOwnershipProof(
                        ConfigurationTargetKind.Npmrc,
                        targetPath,
                        "registry",
                        HashString(currentValue)
                    ),
                ]
                : [];
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            ownershipProofs
        );

        writer.Validate(request, TestContext.Current.CancellationToken);

        if (existingContents is null)
        {
            Assert.False(fileSystem.FileExists(targetPath));
        }
        else
        {
            Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        }

        Assert.False(string.IsNullOrWhiteSpace(expectedFinalContents));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Theory]
    [MemberData(nameof(SupportedValueWritingOperationCases))]
    public void WriteAppliesSupportedNpmrcValueWritingOperations(
        ConfigurationChangeOperation operation,
        string? existingContents,
        string expectedFinalContents
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        if (existingContents is not null)
        {
            fileSystem.AtomicWriteAllText(targetPath, existingContents);
        }

        fileSystem.Calls.Clear();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        const string currentValue = "https://old.example/";
        const string updatedValue = "https://new.example/";
        string plannedValue =
            operation == ConfigurationChangeOperation.Refresh ? currentValue : updatedValue;
        ConfigurationChange change = CreateChange(
            targetPath,
            operation,
            "registry",
            plannedValue
        );
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs =
            operation is ConfigurationChangeOperation.Update or ConfigurationChangeOperation.Refresh
                ? [
                    new ConfigurationPhysicalTargetOwnershipProof(
                        ConfigurationTargetKind.Npmrc,
                        targetPath,
                        "registry",
                        HashString(currentValue)
                    ),
                ]
                : [];
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [change],
            ownershipProofs
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(expectedFinalContents, fileSystem.ReadAllText(targetPath));
        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Equal(targetPath, mutation.Path);
        Assert.Equal(existingContents is not null, mutation.PreviouslyExisted);
        if (existingContents is null)
        {
            Assert.Null(mutation.PreviousContentsBytes);
        }
        else
        {
            Assert.Equal(Encoding.UTF8.GetBytes(existingContents), mutation.PreviousContentsBytes);
        }

        Assert.Equal(HashString(expectedFinalContents), mutation.ExpectedCurrentSha256Hash);
    }

    [Fact]
    public void WriteEscapesBackslashesInNpmrcEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        string key = @"registry\\name";
        string value = @"foo\\bar";
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            key,
            value
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(
            CreateText(@"registry\\\\name=foo\\\\bar"),
            fileSystem.ReadAllText(targetPath)
        );
        writer.ValidateRetainedOwnershipProofs(
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    key,
                    HashString(value)
                ),
            ],
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public void WriteRejectsMissingRemoveTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        string targetPath = CreateValidationTargetPath();
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Remove,
            "registry",
            null
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Remove,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "missing from the physical configuration file",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WriteRejectsProoflessExistingEntryBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = "registry=https://registry.npmjs.org/\n";
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Set,
            "registry",
            "https://registry.npmjs.org/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "not proven to be owned",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void WriteRejectsCreateOnExistingSameKeyWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = "registry=https://registry.npmjs.org/\n";
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            "registry",
            "https://new.example/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "create target already exists",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void ValidateRejectsDuplicateExistingDeclarationsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = CreateText(
            "registry=https://first.example/",
            "registry=https://second.example/"
        );
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Update,
            "registry",
            "https://new.example/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "multiple existing declarations",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void ValidateRejectsDirectoryTargetsWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        fileSystem.CreateDirectory(targetPath);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            "registry",
            "https://new.example/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("exists as a directory", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.DirectoryExists(targetPath));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void ValidateRejectsTargetSymbolicLinksWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateLinkSafetyTargetPath("target-link");
        fileSystem.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string linkTargetPath = Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider",
            "npmrc-link-safety",
            "missing",
            ".npmrc"
        );
        fileSystem.AddSymbolicLink(targetPath, linkTargetPath);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            "registry",
            "https://new.example/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "target path is a symbolic-link or reparse-point",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.True(fileSystem.IsSymbolicLink(targetPath));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidateRejectsParentSymbolicLinksOrReparsePointsWithoutMutation(
        bool useSymbolicLink
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateLinkSafetyTargetPath("parent-link");
        string parentPath = Path.GetDirectoryName(targetPath)!;
        string ancestorPath = Path.GetDirectoryName(parentPath)!;
        fileSystem.CreateDirectory(ancestorPath);
        if (useSymbolicLink)
        {
            string outsideDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "azureauth-credprovider",
                "npmrc-link-safety",
                "outside"
            );
            fileSystem.CreateDirectory(outsideDirectoryPath);
            fileSystem.AddSymbolicLink(parentPath, outsideDirectoryPath);
        }
        else
        {
            fileSystem.CreateDirectory(parentPath);
            fileSystem.MarkAsNonSymbolicReparsePoint(parentPath);
        }

        fileSystem.Calls.Clear();
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            "registry",
            "https://new.example/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("target parent path", exception.Message, StringComparison.Ordinal);
        Assert.Empty(request.CompletedFileMutations);
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
    public void ValidateRejectsUtf8BomFilesWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        byte[] bomPrefixedContents = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(CreateText("registry=https://registry.npmjs.org/")))
            .ToArray();
        fileSystem.AtomicWriteAllBytes(targetPath, bomPrefixedContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Create,
            "registry",
            "https://new.example/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.DryRun,
            ConfigurationTargetKind.Npmrc,
            [change],
            []
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            writer.Validate(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains("UTF-8 BOM", exception.Message, StringComparison.Ordinal);
        Assert.Equal(bomPrefixedContents, fileSystem.ReadAllBytes(targetPath));
        Assert.Empty(request.CompletedFileMutations);
    }

    [Fact]
    public void WriteRegistersCompletedMutationForCanonicalPathAndRetainedProofs()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string rawTargetPath = CreateTraversalTargetPath();
        string canonicalTargetPath = fileSystem.GetFullPath(rawTargetPath);
        string existingContents = "registry=https://registry.npmjs.org/\n";
        fileSystem.AtomicWriteAllText(canonicalTargetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        string plannedValue = "https://registry.npmjs.org/";
        ConfigurationChange change = CreateChange(
            rawTargetPath,
            ConfigurationChangeOperation.Set,
            "registry",
            plannedValue
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [change],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    rawTargetPath,
                    "registry",
                    HashString(plannedValue)
                ),
            ]
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(existingContents, fileSystem.ReadAllText(canonicalTargetPath));
        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Equal(canonicalTargetPath, mutation.Path);
        Assert.True(mutation.PreviouslyExisted);
        Assert.Equal(Encoding.UTF8.GetBytes(existingContents), mutation.PreviousContentsBytes);
        Assert.False(mutation.RequiresRollback);
        Assert.Equal(HashString(existingContents), mutation.ExpectedCurrentSha256Hash);
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, canonicalTargetPath, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void RemoveDeletesOwnedNpmrcEntryAndRegistersCompletedMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingValue = "https://registry.npmjs.org/";
        string existingContents = CreateText($"registry=\"{existingValue}\" # comment");
        const UnixFileMode permissiveMode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite;
        const UnixFileMode ownerOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.SetUnixFileMode(targetPath, permissiveMode);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Remove,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Remove,
                    "registry",
                    null
                ),
            ],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "registry",
                    HashString(existingValue)
                ),
            ]
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal(string.Empty, fileSystem.ReadAllText(targetPath));
        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Equal(targetPath, mutation.Path);
        Assert.True(mutation.PreviouslyExisted);
        Assert.Equal(Encoding.UTF8.GetBytes(existingContents), mutation.PreviousContentsBytes);
        Assert.Equal(HashString(string.Empty), mutation.ExpectedCurrentSha256Hash);
        Assert.True(mutation.RequiresRollback);
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(mutation.PreviousUnixFileMode);
        }
        else
        {
            Assert.Equal(permissiveMode, mutation.PreviousUnixFileMode);
        }

        Assert.Equal(ownerOnlyMode, fileSystem.GetUnixFileMode(targetPath));
    }

    [Fact]
    public void WriteAppliesSamePathMultiChangeNpmrcValueWritingBatchAndRegistersCompletedMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string currentRegistryValue = "https://old.example/";
        string currentAlwaysAuthValue = "false";
        string existingContents = CreateText(
            $"registry={currentRegistryValue}",
            $"always-auth={currentAlwaysAuthValue}"
        );
        string updatedRegistryValue = "https://new.example/";
        string updatedAlwaysAuthValue = "true";
        string expectedFinalContents = CreateText(
            $"registry={updatedRegistryValue}",
            $"always-auth={updatedAlwaysAuthValue}"
        );
        const UnixFileMode permissiveMode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite;
        const UnixFileMode ownerOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.SetUnixFileMode(targetPath, permissiveMode);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Update,
                    "registry",
                    updatedRegistryValue
                ),
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Update,
                    "always-auth",
                    updatedAlwaysAuthValue
                ),
            ],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "registry",
                    HashString(currentRegistryValue)
                ),
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "always-auth",
                    HashString(currentAlwaysAuthValue)
                ),
            ]
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal(expectedFinalContents, fileSystem.ReadAllText(targetPath));
        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Equal(targetPath, mutation.Path);
        Assert.True(mutation.PreviouslyExisted);
        Assert.Equal(Encoding.UTF8.GetBytes(existingContents), mutation.PreviousContentsBytes);
        Assert.Equal(HashString(expectedFinalContents), mutation.ExpectedCurrentSha256Hash);
        Assert.True(mutation.RequiresRollback);
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(mutation.PreviousUnixFileMode);
        }
        else
        {
            Assert.Equal(permissiveMode, mutation.PreviousUnixFileMode);
        }

        Assert.Equal(ownerOnlyMode, fileSystem.GetUnixFileMode(targetPath));
    }

    [Fact]
    public void WriteRejectsRetainedOwnershipProofHashMismatchBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = "registry=https://registry.npmjs.org/\n";
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Set,
            "registry",
            "https://registry.npmjs.org/"
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [change],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                ConfigurationTargetKind.Npmrc,
                targetPath,
                "registry",
                HashString("https://example.invalid/")
                ),
            ]
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "does not match the current file contents",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Empty(request.CompletedFileMutations);
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
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.SetUnixFileMode),
                    StringComparison.Ordinal
                )
                && string.Equals(call.Path, targetPath, StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
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

    [Fact]
    public void ValidateRetainedOwnershipProofsRejectsDuplicateProofsForSameCanonicalPathAndKey()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = "registry=https://registry.npmjs.org/\n";
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetOwnershipProof proof = new(
            ConfigurationTargetKind.Npmrc,
            targetPath,
            "registry",
            HashString("https://registry.npmjs.org/")
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.ValidateRetainedOwnershipProofs(
                [proof, proof],
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(
            "unique per canonical physical key",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RemoveRejectsRetainedOwnershipProofKeyMismatchBeforeMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = "registry=https://registry.npmjs.org/\n";
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            targetPath,
            ConfigurationChangeOperation.Remove,
            "registry",
            null
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Remove,
            ConfigurationTargetKind.Npmrc,
            [change],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                ConfigurationTargetKind.Npmrc,
                targetPath,
                "different-registry",
                null
                ),
            ]
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(request, TestContext.Current.CancellationToken)
        );

        Assert.Contains(
            "retained ownership proof does not match any existing file",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Empty(request.CompletedFileMutations);
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
        Assert.DoesNotContain(
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

    [Fact]
    public void WritePreservesCommentsAndBlankLinesOnRealApplyUpdate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateCommentsPreservationTargetPath();
        string existingContents = string.Join(
            "\r\n",
            "# leading comment",
            string.Empty,
            "registry=https://old.example/",
            string.Empty,
            "; trailing comment",
            string.Empty
        );
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        string updatedContents = string.Join(
            "\r\n",
            "# leading comment",
            string.Empty,
            "registry=https://new.example/",
            string.Empty,
            "; trailing comment",
            string.Empty
        );
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Set,
                    "registry",
                    "https://new.example/"
                ),
            ],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "registry",
                    HashString("https://old.example/")
                ),
            ]
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(updatedContents, fileSystem.ReadAllText(targetPath));
        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Equal(targetPath, mutation.Path);
        Assert.True(mutation.PreviouslyExisted);
        Assert.Equal(Encoding.UTF8.GetBytes(existingContents), mutation.PreviousContentsBytes);
        Assert.Equal(HashString(updatedContents), mutation.ExpectedCurrentSha256Hash);
        Assert.True(mutation.RequiresRollback);
    }

    [Fact]
    public void WritePreservesCommentsAndBlankLinesOnNoOpApplyAndReapply()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateCommentsPreservationTargetPath();
        string existingContents = string.Join(
            "\n",
            "# leading comment",
            string.Empty,
            "registry=https://registry.npmjs.org/",
            string.Empty,
            "; trailing comment",
            string.Empty
        );
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        const UnixFileMode permissiveMode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite;
        const UnixFileMode ownerOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        fileSystem.SetUnixFileMode(targetPath, permissiveMode);
        Assert.Equal(permissiveMode, fileSystem.GetUnixFileMode(targetPath));
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        ConfigurationPhysicalTargetWriterRequest CreateRequest() =>
            new(
                ConfigurationPlanOperation.Apply,
                ConfigurationTargetKind.Npmrc,
                [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Set,
                    "registry",
                    "https://registry.npmjs.org/"
                ),
                ],
                [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "registry",
                    HashString("https://registry.npmjs.org/")
                ),
                ]
            );

        ConfigurationPhysicalTargetWriterRequest firstRequest = CreateRequest();
        writer.Write(firstRequest, TestContext.Current.CancellationToken);
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Equal(ownerOnlyMode, fileSystem.GetUnixFileMode(targetPath));
        ConfigurationPhysicalTargetFileMutation firstMutation = Assert.Single(
            firstRequest.CompletedFileMutations
        );
        Assert.Equal(permissiveMode, firstMutation.PreviousUnixFileMode);
        Assert.True(firstMutation.RequiresRollback);
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

        fileSystem.Calls.Clear();
        ConfigurationPhysicalTargetWriterRequest secondRequest = CreateRequest();
        writer.Write(secondRequest, TestContext.Current.CancellationToken);
        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        Assert.Equal(ownerOnlyMode, fileSystem.GetUnixFileMode(targetPath));
        ConfigurationPhysicalTargetFileMutation secondMutation = Assert.Single(
            secondRequest.CompletedFileMutations
        );
        Assert.Equal(ownerOnlyMode, secondMutation.PreviousUnixFileMode);
        Assert.False(secondMutation.RequiresRollback);
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
        Assert.DoesNotContain(
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

    [Theory]
    [MemberData(nameof(SupportedNoOpValueWritingOperationCasesWithInlineComments))]
    public void WritePreservesInlineCommentsWhenNpmrcValueIsUnchanged(
        ConfigurationChangeOperation operation,
        string currentValue,
        string existingContents
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateCommentsPreservationTargetPath();
        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    operation,
                    "registry",
                    currentValue
                ),
            ],
            [
                new ConfigurationPhysicalTargetOwnershipProof(
                    ConfigurationTargetKind.Npmrc,
                    targetPath,
                    "registry",
                    HashString(currentValue)
                ),
            ]
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(existingContents, fileSystem.ReadAllText(targetPath));
        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Equal(HashString(existingContents), mutation.ExpectedCurrentSha256Hash);
        Assert.DoesNotContain(
            fileSystem.Calls,
            call =>
                string.Equals(
                    call.Operation,
                    nameof(IFileSystem.AtomicWriteAllText),
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void WriteRestrictsUnixFileModeWhenReplacingExistingFileWithSecretAuthToken()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Host);
        string targetPath = CreateValidationTargetPath();
        string existingContents = "registry=https://old.example/\n";
        const UnixFileMode permissiveMode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite;
        const UnixFileMode ownerOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const string authTokenKey =
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken";
        const string secret = "azdops_pat_secret_for_npmrc_apply_tests";

        fileSystem.AtomicWriteAllText(targetPath, existingContents);
        fileSystem.SetUnixFileMode(targetPath, permissiveMode);
        Assert.Equal(permissiveMode, fileSystem.GetUnixFileMode(targetPath));
        fileSystem.Calls.Clear();

        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = new(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.Npmrc,
            [
                CreateChange(
                    targetPath,
                    ConfigurationChangeOperation.Set,
                    authTokenKey,
                    secret,
                    isSecretValue: true
                ),
            ],
            []
        )
        {
            ResourceIdentity = CreateNpmResourceIdentity(),
        };

        writer.Write(request, TestContext.Current.CancellationToken);

        ConfigurationPhysicalTargetFileMutation mutation = Assert.Single(
            request.CompletedFileMutations
        );
        Assert.Contains(
            $"{authTokenKey}={secret}",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );
        Assert.Equal(ownerOnlyMode, fileSystem.GetUnixFileMode(targetPath));
        Assert.Equal(permissiveMode, mutation.PreviousUnixFileMode);
        Assert.True(mutation.RequiresRollback);
    }

    private static ConfigurationChange CreateChange(
        string targetPath,
        ConfigurationChangeOperation operation,
        string key,
        string? value,
        bool isSecretValue = false
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = targetPath,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            IsSecretValue = isSecretValue,
            PreserveDeclarationsAndComments = true,
        };

    private static CanonicalResourceIdentity CreateNpmResourceIdentity() =>
        CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"),
            feed: "feed"
        );

    private static string CreateValidationTargetPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider",
            "npmrc-validation",
            ".npmrc"
        );

    private static string CreateTraversalTargetPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider",
            "npmrc-canonical",
            "nested",
            "..",
            ".npmrc"
        );

    private static string CreateCommentsPreservationTargetPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider",
            "npmrc-comments",
            ".npmrc"
        );

    private static string CreateLinkSafetyTargetPath(string leafDirectory) =>
        Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider",
            "npmrc-link-safety",
            leafDirectory,
            ".npmrc"
        );

    private static string CreateText(params string[] lines) =>
        lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static string HashString(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
