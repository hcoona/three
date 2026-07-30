using System.Reflection;
using System.Text;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasGoldFileApplicationTests
{
    private static readonly AtlasSaveReaderLimits Limits = AtlasSaveReaderLimits.Default;

    public static TheoryData<AtlasGoldFileApplicationFailure, string> FixedFailures =>
        new()
        {
            {
                AtlasGoldFileApplicationFailure.UnsupportedPlatform,
                "Gold file application is supported only on Windows."
            },
            {
                AtlasGoldFileApplicationFailure.UnsupportedSlotPath,
                "The slot path is not a supported canonical save-slot path."
            },
            {
                AtlasGoldFileApplicationFailure.BackupConflict,
                "The fixed backup artifacts conflict with this operation."
            },
            {
                AtlasGoldFileApplicationFailure.StagingConflict,
                "The fixed candidate stage conflicts with this operation."
            },
            {
                AtlasGoldFileApplicationFailure.SourceChanged,
                "The source slot changed before replacement."
            },
            {
                AtlasGoldFileApplicationFailure.ReplacementFailed,
                "The source slot replacement failed without changing the classified files."
            },
            {
                AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown,
                "The source slot replacement outcome is unknown."
            },
            {
                AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed,
                "The replaced slot failed verification."
            },
        };

    public static TheoryData<string> UnsupportedLeaves =>
        new()
        {
            "global.rpgsave",
            "config.rpgsave",
            "file0.rpgsave",
            "file00.rpgsave",
            "file01.rpgsave",
            "file21.rpgsave",
            "FILE1.RPGSAVE",
            "File1.rpgsave",
            "file1.RPGSAVE",
            "file1.rpgsave.bak",
            "file1",
            "file1.rpgsave ",
        };

    public static TheoryData<string> SourceChangeCases =>
        new()
        {
            "replace",
            "remove",
            "directory",
        };

    public static TheoryData<string> UnknownReplacementStates =>
        new()
        {
            "candidate-live-stage-present",
            "source-live-stage-absent",
            "third-live",
            "missing-live",
            "directory-live",
            "changed-backup",
            "invalid-backup",
            "unreadable-live",
        };

    public static TheoryData<string> ReturnedPostconditionFailures =>
        new()
        {
            "live-bytes",
            "stage-present",
            "backup-bytes",
            "backup-invalid",
        };

    [Fact]
    public void PublicSurfaceIsClosedAndExactlyShaped()
    {
        Assert.Equal(
            [
                nameof(AtlasGoldFileApplicationDisposition.Unchanged),
                nameof(AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated),
                nameof(AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved),
            ],
            Enum.GetNames<AtlasGoldFileApplicationDisposition>());
        Assert.Equal(
            [
                nameof(AtlasGoldFileApplicationFailure.UnsupportedPlatform),
                nameof(AtlasGoldFileApplicationFailure.UnsupportedSlotPath),
                nameof(AtlasGoldFileApplicationFailure.BackupConflict),
                nameof(AtlasGoldFileApplicationFailure.StagingConflict),
                nameof(AtlasGoldFileApplicationFailure.SourceChanged),
                nameof(AtlasGoldFileApplicationFailure.ReplacementFailed),
                nameof(AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown),
                nameof(AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed),
            ],
            Enum.GetNames<AtlasGoldFileApplicationFailure>());

        MethodInfo apply = Assert.Single(
            typeof(AtlasGoldFileApplication).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldFileApplication.ApplyAsync), apply.Name);
        Assert.Equal(
            typeof(ValueTask<AtlasGoldFileApplicationDisposition>),
            apply.ReturnType);
        ParameterInfo[] parameters = apply.GetParameters();
        Assert.Equal(
            [
                typeof(string),
                typeof(long),
                typeof(AtlasSaveReaderLimits),
                typeof(CancellationToken),
            ],
            parameters.Select(static parameter => parameter.ParameterType));
        Assert.Equal(
            ["slotPath", "value", "limits", "cancellationToken"],
            parameters.Select(static parameter => parameter.Name));
        Assert.True(parameters[^1].HasDefaultValue);
        Assert.Null(parameters[^1].DefaultValue);

        Type exceptionType = typeof(AtlasGoldFileApplicationException);
        Assert.True(exceptionType.IsSealed);
        Assert.Empty(exceptionType.GetConstructors());
        PropertyInfo failure = Assert.Single(
            exceptionType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldFileApplicationException.Failure), failure.Name);
        Assert.Equal(typeof(AtlasGoldFileApplicationFailure), failure.PropertyType);
        Assert.Null(failure.SetMethod);
    }

    [Theory]
    [MemberData(nameof(FixedFailures))]
    public void EveryFailureUsesFixedValueFreeTextAndRetainsInternalDiagnostics(
        AtlasGoldFileApplicationFailure failure,
        string expectedMessage)
    {
        const string privatePath = @"C:\private\file7.rpgsave";
        const string privatePayload = "synthetic-private-payload";
        const string value = "9223372036854775807";
        IOException diagnostic = new(
            $"Local diagnostic for {privatePath}, {privatePayload}, and {value}.");

        AtlasGoldFileApplicationException exception = new(failure, diagnostic);

        Assert.Equal(failure, exception.Failure);
        Assert.Equal(expectedMessage, exception.Message);
        Assert.DoesNotContain(privatePath, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(privatePayload, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
        Assert.Same(diagnostic, exception.InnerException);
    }

    [Fact]
    public async Task EstablishedArgumentReaderMutationIoAndCancellationFailuresAreRetained()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(7);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await ApplyAsync(null!, 8, workspace));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await ApplyAsync(" ", 8, workspace));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await AtlasGoldFileApplication.ApplyAsync(
                workspace.SlotPath,
                8,
                null!,
                AtlasIoSeams.Default,
                true,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await AtlasGoldFileApplication.ApplyAsync(
                workspace.SlotPath,
                8,
                new AtlasSaveReaderLimits { MaximumEncodedBytes = 0 },
                AtlasIoSeams.Default,
                true,
                TestContext.Current.CancellationToken));
        AtlasSaveReadException encodedLimit =
            await Assert.ThrowsAsync<AtlasSaveReadException>(
                async () => await AtlasGoldFileApplication.ApplyAsync(
                    workspace.SlotPath,
                    8,
                    new AtlasSaveReaderLimits { MaximumEncodedBytes = 1 },
                    AtlasIoSeams.Default,
                    true,
                    TestContext.Current.CancellationToken));
        Assert.Equal(AtlasSaveReadFailure.EncodedInputLimit, encodedLimit.Failure);

        await File.WriteAllBytesAsync(
            workspace.SlotPath,
            "not-a-save"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasSaveReadException>(
            async () => await ApplyAsync(workspace.SlotPath, 8));

        await File.WriteAllBytesAsync(
            workspace.SlotPath,
            ApplicationWorkspace.CreateIncompleteSaveBytes(),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasGoldMutationException>(
            async () => await ApplyAsync(workspace.SlotPath, 8));

        await File.WriteAllBytesAsync(
            workspace.SlotPath,
            ApplicationWorkspace.CreateSaveBytes(7),
            TestContext.Current.CancellationToken);
        IOException ioFailure = new("Synthetic candidate create failure.");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                StringComparer.OrdinalIgnoreCase.Equals(path, workspace.CandidateStagePath)
                && mode == FileMode.CreateNew
                    ? throw ioFailure
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));
        IOException actual = await Assert.ThrowsAsync<IOException>(
            async () => await ApplyAsync(workspace.SlotPath, 8, io));
        Assert.Same(ioFailure, actual);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await ApplyWithCancellationAsync(
                workspace.SlotPath,
                8,
                null,
                cancellation.Token));
    }

    [Fact]
    public async Task EveryExactCanonicalSlotNameIsAccepted()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(17);
        foreach (int index in Enumerable.Range(1, 20))
        {
            string path = workspace.GetSlotPath(index);
            await File.WriteAllBytesAsync(
                path,
                ApplicationWorkspace.CreateSaveBytes(17),
                TestContext.Current.CancellationToken);

            AtlasGoldFileApplicationDisposition result = await ApplyAsync(path, 17);

            Assert.Equal(AtlasGoldFileApplicationDisposition.Unchanged, result);
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedLeaves))]
    public async Task NoncanonicalLeavesAreRefused(string leaf)
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(3);
        string path = Path.Combine(workspace.RootPath, leaf);

        AtlasGoldFileApplicationException exception =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await ApplyAsync(path, 3));

        Assert.Equal(
            AtlasGoldFileApplicationFailure.UnsupportedSlotPath,
            exception.Failure);
    }

    [Fact]
    public async Task RelativeAndAliasedPathsAreRefused()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(3);
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "alias"));
        string alias = Path.Combine(
            workspace.RootPath,
            "alias",
            "..",
            Path.GetFileName(workspace.SlotPath));

        foreach (string path in new[] { "file1.rpgsave", alias })
        {
            AtlasGoldFileApplicationException exception =
                await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                    async () => await ApplyAsync(path, 3));
            Assert.Equal(
                AtlasGoldFileApplicationFailure.UnsupportedSlotPath,
                exception.Failure);
        }
    }

    [Fact]
    public async Task PlatformQualificationIsDeterministicAndPubliclyBound()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(4);

        AtlasGoldFileApplicationException forced =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await AtlasGoldFileApplication.ApplyAsync(
                    workspace.SlotPath,
                    4,
                    Limits,
                    AtlasIoSeams.Default,
                    false,
                    TestContext.Current.CancellationToken));
        Assert.Equal(AtlasGoldFileApplicationFailure.UnsupportedPlatform, forced.Failure);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(
                AtlasGoldFileApplicationDisposition.Unchanged,
                await AtlasGoldFileApplication.ApplyAsync(
                    workspace.SlotPath,
                    4,
                    Limits,
                    TestContext.Current.CancellationToken));
        }
        else
        {
            AtlasGoldFileApplicationException actual =
                await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                    async () => await AtlasGoldFileApplication.ApplyAsync(
                        workspace.SlotPath,
                        4,
                        Limits,
                        TestContext.Current.CancellationToken));
            Assert.Equal(
                AtlasGoldFileApplicationFailure.UnsupportedPlatform,
                actual.Failure);
        }
    }

    [Fact]
    public async Task ExistingOrdinaryValidationRetainsItsEstablishedFailures()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(4);
        File.Delete(workspace.SlotPath);
        Directory.CreateDirectory(workspace.SlotPath);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            async () => await ApplyAsync(workspace.SlotPath, 4));

        Directory.Delete(workspace.SlotPath);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            async () => await ApplyAsync(workspace.SlotPath, 4));

        await File.WriteAllBytesAsync(
            workspace.SlotPath,
            ApplicationWorkspace.CreateSaveBytes(4),
            TestContext.Current.CancellationToken);
        AtlasIoSeams reparseFile = AtlasTestSupport.CreateIo(
            getAttributes: path =>
            {
                FileAttributes attributes = AtlasIoSeams.Default.GetAttributes(path);
                return StringComparer.OrdinalIgnoreCase.Equals(path, workspace.SlotPath)
                    ? attributes | FileAttributes.ReparsePoint
                    : attributes;
            });
        await Assert.ThrowsAsync<AtlasSafetyException>(
            async () => await ApplyAsync(workspace.SlotPath, 4, reparseFile));

        AtlasIoSeams reparseParent = AtlasTestSupport.CreateIo(
            getAttributes: path =>
            {
                FileAttributes attributes = AtlasIoSeams.Default.GetAttributes(path);
                return StringComparer.OrdinalIgnoreCase.Equals(path, workspace.RootPath)
                    ? attributes | FileAttributes.ReparsePoint
                    : attributes;
            });
        await Assert.ThrowsAsync<AtlasSafetyException>(
            async () => await ApplyAsync(workspace.SlotPath, 4, reparseParent));
    }

    [Fact]
    public async Task SemanticNoOpPerformsNoArtifactOperationAndUsesRequiredSourceSharing()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(9);
        List<string> artifactCalls = [];
        (FileMode Mode, FileAccess Access, FileShare Share, FileOptions Options)? sourceOpen = null;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            readAllBytesAsync: (path, cancellationToken) =>
            {
                RecordArtifact(path, "read-all-bytes");
                return AtlasIoSeams.Default.ReadAllBytesAsync(path, cancellationToken);
            },
            fileExists: path =>
            {
                RecordArtifact(path, "file-exists");
                return AtlasIoSeams.Default.FileExists(path);
            },
            directoryExists: path =>
            {
                RecordArtifact(path, "directory-exists");
                return AtlasIoSeams.Default.DirectoryExists(path);
            },
            getAttributes: path =>
            {
                RecordArtifact(path, "attributes");
                return AtlasIoSeams.Default.GetAttributes(path);
            },
            openFile: (path, mode, access, share, options) =>
            {
                RecordArtifact(path, "open");
                if (StringComparer.OrdinalIgnoreCase.Equals(path, workspace.SlotPath))
                {
                    sourceOpen = (mode, access, share, options);
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            moveFile: (source, destination) =>
            {
                RecordArtifact(source, "move-source");
                RecordArtifact(destination, "move-destination");
                AtlasIoSeams.Default.MoveFile(source, destination);
            },
            replaceFile: (source, destination, backup) =>
            {
                RecordArtifact(source, "replace-source");
                AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
            },
            createDirectory: path =>
            {
                RecordArtifact(path, "create-directory");
                AtlasIoSeams.Default.CreateDirectory(path);
            },
            deleteDirectory: (path, recursive) =>
            {
                RecordArtifact(path, "delete-directory");
                AtlasIoSeams.Default.DeleteDirectory(path, recursive);
            },
            deleteFile: path =>
            {
                RecordArtifact(path, "delete");
                AtlasIoSeams.Default.DeleteFile(path);
            },
            setAttributes: (path, attributes) =>
            {
                RecordArtifact(path, "set-attributes");
                AtlasIoSeams.Default.SetAttributes(path, attributes);
            },
            getLength: path =>
            {
                RecordArtifact(path, "get-length");
                return AtlasIoSeams.Default.GetLength(path);
            },
            getLastWriteTimeUtc: path =>
            {
                RecordArtifact(path, "get-last-write");
                return AtlasIoSeams.Default.GetLastWriteTimeUtc(path);
            });

        AtlasGoldFileApplicationDisposition result =
            await ApplyAsync(workspace.SlotPath, 9, io);

        Assert.Equal(AtlasGoldFileApplicationDisposition.Unchanged, result);
        Assert.Empty(artifactCalls);
        Assert.Equal(
            (
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                FileOptions.Asynchronous | FileOptions.SequentialScan),
            sourceOpen);

        void RecordArtifact(string path, string operation)
        {
            if (workspace.IsArtifactPath(path))
            {
                artifactCalls.Add($"{operation}:{path}");
            }
        }
    }

    [Fact]
    public async Task FirstRepeatedAndBackupResetEditsPreserveTheSpecifiedBaselines()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        byte[] original = await File.ReadAllBytesAsync(
            workspace.SlotPath,
            TestContext.Current.CancellationToken);

        AtlasGoldFileApplicationDisposition first =
            await ApplyAsync(workspace.SlotPath, 20);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            first);
        Assert.Equal(20, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(
                workspace.BackupPath,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(workspace.CandidateStagePath));
        Assert.False(File.Exists(workspace.BackupStagingPath));

        AtlasGoldFileApplicationDisposition repeated =
            await ApplyAsync(workspace.SlotPath, 30);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved,
            repeated);
        Assert.Equal(30, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(
                workspace.BackupPath,
                TestContext.Current.CancellationToken));

        byte[] resetBaseline = await File.ReadAllBytesAsync(
            workspace.SlotPath,
            TestContext.Current.CancellationToken);
        File.Delete(workspace.BackupPath);
        AtlasGoldFileApplicationDisposition reset =
            await ApplyAsync(workspace.SlotPath, 40);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            reset);
        Assert.Equal(40, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        byte[] newBackup = await File.ReadAllBytesAsync(
            workspace.BackupPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(resetBaseline, newBackup);
        Assert.NotEqual(original, newBackup);
    }

    [Fact]
    public async Task BackupArtifactCollisionsAndInvalidCompletedBackupsAreRefused()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        await File.WriteAllBytesAsync(
            workspace.BackupStagingPath,
            "occupied"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(workspace.SlotPath, 20));

        File.Delete(workspace.BackupStagingPath);
        await File.WriteAllBytesAsync(
            workspace.BackupPath,
            ApplicationWorkspace.CreateSaveBytes(1),
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            workspace.BackupStagingPath,
            "occupied"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(workspace.SlotPath, 20));

        File.Delete(workspace.BackupStagingPath);
        await File.WriteAllBytesAsync(
            workspace.BackupPath,
            "invalid-save"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(workspace.SlotPath, 20));

        await File.WriteAllBytesAsync(
            workspace.BackupPath,
            ApplicationWorkspace.CreateDisagreeingSaveBytes(),
            TestContext.Current.CancellationToken);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(workspace.SlotPath, 20));

        File.Delete(workspace.BackupPath);
        Directory.CreateDirectory(workspace.BackupPath);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(workspace.SlotPath, 20));
    }

    [Fact]
    public async Task PreExistingBackupReadIsBoundedAndClassifiedAsAConflict()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        byte[] source = await File.ReadAllBytesAsync(
            workspace.SlotPath,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            workspace.BackupPath,
            [.. source, (byte)'A'],
            TestContext.Current.CancellationToken);
        AtlasSaveReaderLimits exactSourceLimit = new()
        {
            MaximumEncodedBytes = source.Length,
        };

        AtlasGoldFileApplicationException exception =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await AtlasGoldFileApplication.ApplyAsync(
                    workspace.SlotPath,
                    20,
                    exactSourceLimit,
                    AtlasIoSeams.Default,
                    true,
                    TestContext.Current.CancellationToken));

        Assert.Equal(AtlasGoldFileApplicationFailure.BackupConflict, exception.Failure);
    }

    [Fact]
    public async Task BackupAndCandidateReparseStatesAreRefused()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        await File.WriteAllBytesAsync(
            workspace.BackupPath,
            ApplicationWorkspace.CreateSaveBytes(1),
            TestContext.Current.CancellationToken);
        AtlasIoSeams backupReparse = AtlasTestSupport.CreateIo(
            getAttributes: path =>
            {
                FileAttributes attributes = AtlasIoSeams.Default.GetAttributes(path);
                return StringComparer.OrdinalIgnoreCase.Equals(path, workspace.BackupPath)
                    ? attributes | FileAttributes.ReparsePoint
                    : attributes;
            });
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(workspace.SlotPath, 20, backupReparse));

        File.Delete(workspace.BackupPath);
        byte[] candidate = workspace.CreateCandidateBytes(20);
        await File.WriteAllBytesAsync(
            workspace.CandidateStagePath,
            candidate,
            TestContext.Current.CancellationToken);
        AtlasIoSeams candidateReparse = AtlasTestSupport.CreateIo(
            getAttributes: path =>
            {
                FileAttributes attributes = AtlasIoSeams.Default.GetAttributes(path);
                return StringComparer.OrdinalIgnoreCase.Equals(
                    path,
                    workspace.CandidateStagePath)
                    ? attributes | FileAttributes.ReparsePoint
                    : attributes;
            });
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.StagingConflict,
            () => ApplyAsync(workspace.SlotPath, 20, candidateReparse));
    }

    [Fact]
    public async Task ExactCandidateStageIsReusedAfterProvenFailureAndDifferentCandidateConflicts()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        AtlasIoSeams failing = AtlasTestSupport.CreateIo(
            replaceFile: (_, _, _) => throw new IOException("Synthetic pre-mutation failure."));

        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.ReplacementFailed,
            () => ApplyAsync(workspace.SlotPath, 20, failing));
        byte[] retained = await File.ReadAllBytesAsync(
            workspace.CandidateStagePath,
            TestContext.Current.CancellationToken);
        Assert.Equal(workspace.CreateCandidateBytes(20), retained);

        AtlasGoldFileApplicationDisposition retry =
            await ApplyAsync(workspace.SlotPath, 20);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupPreserved,
            retry);
        Assert.False(File.Exists(workspace.CandidateStagePath));

        await File.WriteAllBytesAsync(
            workspace.CandidateStagePath,
            retained,
            TestContext.Current.CancellationToken);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.StagingConflict,
            () => ApplyAsync(workspace.SlotPath, 30));
    }

    [Fact]
    public async Task CandidateByteAndNonordinaryConflictsAreRefused()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        await File.WriteAllBytesAsync(
            workspace.CandidateStagePath,
            "different"u8.ToArray(),
            TestContext.Current.CancellationToken);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.StagingConflict,
            () => ApplyAsync(workspace.SlotPath, 20));

        File.Delete(workspace.CandidateStagePath);
        Directory.CreateDirectory(workspace.CandidateStagePath);
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.StagingConflict,
            () => ApplyAsync(workspace.SlotPath, 20));
    }

    [Fact]
    public async Task ArtifactWriteFailuresRetainTheirIoTypeAndPartialStage()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        IOException writeFailure = new("Synthetic artifact write failure.");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    path,
                    workspace.BackupStagingPath)
                && mode == FileMode.CreateNew
                    ? new ThrowingWriteStream(
                        AtlasIoSeams.Default.OpenFile(path, mode, access, share, options),
                        writeFailure)
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));

        IOException actual = await Assert.ThrowsAsync<IOException>(
            async () => await ApplyAsync(workspace.SlotPath, 20, io));

        Assert.Same(writeFailure, actual);
        Assert.True(File.Exists(workspace.BackupStagingPath));
        Assert.False(File.Exists(workspace.BackupPath));
        Assert.False(File.Exists(workspace.CandidateStagePath));
    }

    [Fact]
    public async Task ArtifactWritesAreCreateNewWriteThroughFlushedClosedAndRereadInOrder()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        List<string> events = [];
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                string? role = workspace.GetArtifactRole(path);
                if (role is not null && mode == FileMode.CreateNew)
                {
                    events.Add($"{role}:open:{mode}:{access}:{share}:{options}");
                    return new TrackingFileStream(
                        path,
                        mode,
                        access,
                        share,
                        options,
                        role,
                        events);
                }

                if (role is not null && mode == FileMode.Open)
                {
                    events.Add($"{role}:reread");
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            moveFile: (source, destination) =>
            {
                events.Add("backup:move");
                AtlasIoSeams.Default.MoveFile(source, destination);
            },
            replaceFile: (_, _, _) =>
            {
                events.Add("candidate:replace");
                throw new IOException("Synthetic classified failure.");
            });

        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.ReplacementFailed,
            () => ApplyAsync(workspace.SlotPath, 20, io));

        AssertSubsequence(
            events,
            [
                $"backup:open:{FileMode.CreateNew}:{FileAccess.Write}:{FileShare.None}:"
                    + $"{FileOptions.Asynchronous | FileOptions.WriteThrough}",
                "backup:write",
                "backup:flush-async",
                "backup:flush-disk:True",
                "backup:close",
                "backup:reread",
                "backup:move",
                "backup:reread",
            ]);
        AssertSubsequence(
            events,
            [
                $"candidate:open:{FileMode.CreateNew}:{FileAccess.Write}:{FileShare.None}:"
                    + $"{FileOptions.Asynchronous | FileOptions.WriteThrough}",
                "candidate:write",
                "candidate:flush-async",
                "candidate:flush-disk:True",
                "candidate:close",
                "candidate:reread",
                "candidate:replace",
            ]);
    }

    [Fact]
    public async Task HeldSourceDeniesWritersAndRemainsCompatibleWithReplacement()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        bool writerDenied = false;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            replaceFile: (source, destination, backup) =>
            {
                writerDenied = Assert.Throws<IOException>(
                    () =>
                    {
                        using FileStream _ = new(
                            destination,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.Read | FileShare.Write | FileShare.Delete);
                    }) is not null;
                AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
            });

        AtlasGoldFileApplicationDisposition result =
            await ApplyAsync(workspace.SlotPath, 20, io);

        Assert.True(writerDenied);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            result);
        Assert.Equal(20, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
    }

    [Theory]
    [MemberData(nameof(SourceChangeCases))]
    public async Task ObservableLivePathChangesAreRefusedBeforeApplicationReplace(string change)
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        int candidateReadCount = 0;
        int applicationReplaceCount = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(
                        path,
                        workspace.CandidateStagePath)
                    && mode == FileMode.Open
                    && ++candidateReadCount == 1)
                {
                    switch (change)
                    {
                        case "replace":
                            workspace.ReplaceLiveWith(
                                ApplicationWorkspace.CreateSaveBytes(11));
                            break;
                        case "remove":
                            File.Delete(workspace.SlotPath);
                            break;
                        case "directory":
                            File.Delete(workspace.SlotPath);
                            Directory.CreateDirectory(workspace.SlotPath);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            replaceFile: (_, _, _) => applicationReplaceCount++);

        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.SourceChanged,
            () => ApplyAsync(workspace.SlotPath, 20, io));

        Assert.Equal(0, applicationReplaceCount);
    }

    [Fact]
    public async Task BackupAndCandidateDriftAreRefusedBeforeReplacement()
    {
        await using ApplicationWorkspace backupWorkspace =
            await ApplicationWorkspace.CreateAsync(10);
        await File.WriteAllBytesAsync(
            backupWorkspace.BackupPath,
            ApplicationWorkspace.CreateSaveBytes(1),
            TestContext.Current.CancellationToken);
        int sourceReads = 0;
        AtlasIoSeams backupDrift = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(
                        path,
                        backupWorkspace.SlotPath)
                    && mode == FileMode.Open
                    && ++sourceReads == 2)
                {
                    File.WriteAllBytes(
                        backupWorkspace.BackupPath,
                        ApplicationWorkspace.CreateSaveBytes(2));
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            replaceFile: (_, _, _) => throw new InvalidOperationException("Not expected."));
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.BackupConflict,
            () => ApplyAsync(backupWorkspace.SlotPath, 20, backupDrift));

        await using ApplicationWorkspace stageWorkspace =
            await ApplicationWorkspace.CreateAsync(10);
        int stageSourceReads = 0;
        AtlasIoSeams stageDrift = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(
                        path,
                        stageWorkspace.SlotPath)
                    && mode == FileMode.Open
                    && ++stageSourceReads == 2)
                {
                    File.WriteAllBytes(
                        stageWorkspace.CandidateStagePath,
                        "changed"u8.ToArray());
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            replaceFile: (_, _, _) => throw new InvalidOperationException("Not expected."));
        await AssertFailureAsync(
            AtlasGoldFileApplicationFailure.StagingConflict,
            () => ApplyAsync(stageWorkspace.SlotPath, 20, stageDrift));
    }

    [Fact]
    public async Task CancellationBeforeSourceAndBeforeArtifactsPreservesState()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        using CancellationTokenSource beforeSource = new();
        beforeSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await ApplyWithCancellationAsync(
                workspace.SlotPath,
                20,
                null,
                beforeSource.Token));
        Assert.False(File.Exists(workspace.BackupPath));
        Assert.False(File.Exists(workspace.CandidateStagePath));

        using CancellationTokenSource beforeArtifacts = new();
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(path, workspace.BackupPath))
                {
                    beforeArtifacts.Cancel();
                    throw new OperationCanceledException(beforeArtifacts.Token);
                }

                return AtlasIoSeams.Default.GetAttributes(path);
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await ApplyWithCancellationAsync(
                workspace.SlotPath,
                20,
                io,
                beforeArtifacts.Token));
        Assert.False(File.Exists(workspace.BackupPath));
        Assert.False(File.Exists(workspace.CandidateStagePath));
    }

    [Fact]
    public async Task FinalCancellationBoundaryPreservesCompletedArtifacts()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        using CancellationTokenSource cancellation = new();
        int candidateReads = 0;
        int replacements = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                Stream stream = AtlasIoSeams.Default.OpenFile(
                    path,
                    mode,
                    access,
                    share,
                    options);
                if (StringComparer.OrdinalIgnoreCase.Equals(
                        path,
                        workspace.CandidateStagePath)
                    && mode == FileMode.Open
                    && ++candidateReads == 2)
                {
                    return new CancelOnDisposeStream(stream, cancellation);
                }

                return stream;
            },
            replaceFile: (_, _, _) => replacements++);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await ApplyWithCancellationAsync(
                workspace.SlotPath,
                20,
                io,
                cancellation.Token));

        Assert.Equal(0, replacements);
        Assert.True(File.Exists(workspace.BackupPath));
        Assert.True(File.Exists(workspace.CandidateStagePath));
        Assert.False(File.Exists(workspace.BackupStagingPath));
    }

    [Fact]
    public async Task CancellationRaisedInsideReplacementNeverInterruptsClassification()
    {
        await using ApplicationWorkspace failedWorkspace =
            await ApplicationWorkspace.CreateAsync(10);
        using CancellationTokenSource failedCancellation = new();
        IOException failure = new("Synthetic failure before mutation.");
        AtlasIoSeams failedIo = AtlasTestSupport.CreateIo(
            replaceFile: (_, _, _) =>
            {
                failedCancellation.Cancel();
                throw failure;
            });
        AtlasGoldFileApplicationException failed =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await ApplyWithCancellationAsync(
                    failedWorkspace.SlotPath,
                    20,
                    failedIo,
                    failedCancellation.Token));
        Assert.Equal(AtlasGoldFileApplicationFailure.ReplacementFailed, failed.Failure);
        Assert.Same(failure, failed.InnerException);

        await using ApplicationWorkspace appliedWorkspace =
            await ApplicationWorkspace.CreateAsync(10);
        using CancellationTokenSource appliedCancellation = new();
        AtlasIoSeams appliedIo = AtlasTestSupport.CreateIo(
            replaceFile: (source, destination, backup) =>
            {
                AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
                appliedCancellation.Cancel();
            });
        AtlasGoldFileApplicationDisposition applied = await ApplyWithCancellationAsync(
            appliedWorkspace.SlotPath,
            20,
            appliedIo,
            appliedCancellation.Token);
        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpectedReplacementExceptionsClassifyProvenFailure(bool unauthorized)
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        Exception replacement = unauthorized
            ? new UnauthorizedAccessException("Synthetic denied replacement.")
            : new IOException("Synthetic failed replacement.");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            replaceFile: (_, _, _) => throw replacement);

        AtlasGoldFileApplicationException exception =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await ApplyAsync(workspace.SlotPath, 20, io));

        Assert.Equal(AtlasGoldFileApplicationFailure.ReplacementFailed, exception.Failure);
        Assert.Same(replacement, exception.InnerException);
        Assert.Equal(10, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        Assert.Equal(
            ApplicationWorkspace.CreateSaveBytes(10),
            await File.ReadAllBytesAsync(
                workspace.BackupPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            workspace.CreateCandidateBytes(20),
            await File.ReadAllBytesAsync(
                workspace.CandidateStagePath,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpectedReplacementExceptionsRecognizeEffectiveSuccess(bool unauthorized)
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        Exception replacement = unauthorized
            ? new UnauthorizedAccessException("Synthetic denied after mutation.")
            : new IOException("Synthetic failed after mutation.");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            replaceFile: (source, destination, backup) =>
            {
                AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
                throw replacement;
            });

        AtlasGoldFileApplicationDisposition result =
            await ApplyAsync(workspace.SlotPath, 20, io);

        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            result);
        Assert.Equal(20, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        Assert.False(File.Exists(workspace.CandidateStagePath));
    }

    [Fact]
    public async Task UnexpectedReplacementExceptionRetainsItsRuntimeType()
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        InvalidOperationException replacement = new("Synthetic unexpected replacement failure.");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            replaceFile: (_, _, _) => throw replacement);

        InvalidOperationException actual =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ApplyAsync(workspace.SlotPath, 20, io));

        Assert.Same(replacement, actual);
        Assert.True(File.Exists(workspace.CandidateStagePath));
        Assert.Equal(10, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
    }

    [Theory]
    [MemberData(nameof(UnknownReplacementStates))]
    public async Task ExpectedReplacementExceptionsPreserveUnknownPartialStates(string state)
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        IOException replacement = new($"Synthetic unknown state: {state}.");
        bool replacementStarted = false;
        int deleteCalls = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                replacementStarted
                && state == "unreadable-live"
                && StringComparer.OrdinalIgnoreCase.Equals(path, workspace.SlotPath)
                    ? throw new IOException("Synthetic unreadable classification.")
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options),
            replaceFile: (_, _, _) =>
            {
                replacementStarted = true;
                switch (state)
                {
                    case "candidate-live-stage-present":
                        workspace.ReplaceLiveWith(
                            File.ReadAllBytes(workspace.CandidateStagePath));
                        break;
                    case "source-live-stage-absent":
                        File.Delete(workspace.CandidateStagePath);
                        break;
                    case "third-live":
                        workspace.ReplaceLiveWith(
                            ApplicationWorkspace.CreateSaveBytes(999));
                        break;
                    case "missing-live":
                        File.Delete(workspace.SlotPath);
                        break;
                    case "directory-live":
                        File.Delete(workspace.SlotPath);
                        Directory.CreateDirectory(workspace.SlotPath);
                        break;
                    case "changed-backup":
                        File.WriteAllBytes(
                            workspace.BackupPath,
                            ApplicationWorkspace.CreateSaveBytes(999));
                        break;
                    case "invalid-backup":
                        File.Delete(workspace.BackupPath);
                        Directory.CreateDirectory(workspace.BackupPath);
                        break;
                    case "unreadable-live":
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                throw replacement;
            },
            deleteFile: path =>
            {
                deleteCalls++;
                AtlasIoSeams.Default.DeleteFile(path);
            });

        AtlasGoldFileApplicationException exception =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await ApplyAsync(workspace.SlotPath, 20, io));

        Assert.Equal(
            AtlasGoldFileApplicationFailure.ReplacementOutcomeUnknown,
            exception.Failure);
        Assert.Same(replacement, exception.InnerException);
        Assert.Equal(0, deleteCalls);
    }

    [Theory]
    [MemberData(nameof(ReturnedPostconditionFailures))]
    public async Task ReturnedReplacementPostconditionFailuresDoNotRollBack(string failure)
    {
        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        int deleteCalls = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            replaceFile: (source, destination, backup) =>
            {
                switch (failure)
                {
                    case "live-bytes":
                        AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
                        workspace.ReplaceLiveWith(
                            ApplicationWorkspace.CreateSaveBytes(999));
                        break;
                    case "stage-present":
                        workspace.ReplaceLiveWith(File.ReadAllBytes(source));
                        break;
                    case "backup-bytes":
                        AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
                        File.WriteAllBytes(
                            workspace.BackupPath,
                            ApplicationWorkspace.CreateSaveBytes(999));
                        break;
                    case "backup-invalid":
                        AtlasIoSeams.Default.ReplaceFile(source, destination, backup);
                        File.Delete(workspace.BackupPath);
                        Directory.CreateDirectory(workspace.BackupPath);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            },
            deleteFile: path =>
            {
                deleteCalls++;
                AtlasIoSeams.Default.DeleteFile(path);
            });

        AtlasGoldFileApplicationException exception =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await ApplyAsync(workspace.SlotPath, 20, io));

        Assert.Equal(
            AtlasGoldFileApplicationFailure.PostReplaceVerificationFailed,
            exception.Failure);
        Assert.Equal(0, deleteCalls);
        if (failure == "stage-present")
        {
            Assert.True(File.Exists(workspace.CandidateStagePath));
        }

        if (failure == "live-bytes")
        {
            Assert.Equal(999, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        }
    }

    [Fact]
    public async Task RealWindowsFileReplaceUsesOnlyTheCandidateAndPreservesTheArchive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using ApplicationWorkspace workspace = await ApplicationWorkspace.CreateAsync(10);
        byte[] original = await File.ReadAllBytesAsync(
            workspace.SlotPath,
            TestContext.Current.CancellationToken);

        AtlasGoldFileApplicationDisposition result =
            await AtlasGoldFileApplication.ApplyAsync(
                workspace.SlotPath,
                20,
                Limits,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            AtlasGoldFileApplicationDisposition.AppliedWithBackupCreated,
            result);
        Assert.Equal(20, await ApplicationWorkspace.ReadGoldAsync(workspace.SlotPath));
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(
                workspace.BackupPath,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(workspace.CandidateStagePath));
        Assert.False(File.Exists(workspace.BackupStagingPath));
        Assert.False(File.Exists(workspace.SlotPath + ".replacement-backup"));
    }

    private static ValueTask<AtlasGoldFileApplicationDisposition> ApplyAsync(
        string slotPath,
        long value,
        AtlasIoSeams? io = null) =>
        AtlasGoldFileApplication.ApplyAsync(
            slotPath,
            value,
            Limits,
            io ?? AtlasIoSeams.Default,
            true,
            TestContext.Current.CancellationToken);

    private static ValueTask<AtlasGoldFileApplicationDisposition>
        ApplyWithCancellationAsync(
            string slotPath,
            long value,
            AtlasIoSeams? io,
            CancellationToken cancellationToken) =>
        AtlasGoldFileApplication.ApplyAsync(
            slotPath,
            value,
            Limits,
            io ?? AtlasIoSeams.Default,
            true,
            cancellationToken);

    private static ValueTask<AtlasGoldFileApplicationDisposition> ApplyAsync(
        string? slotPath,
        long value,
        ApplicationWorkspace workspace) =>
        AtlasGoldFileApplication.ApplyAsync(
            slotPath!,
            value,
            Limits,
            AtlasIoSeams.Default,
            true,
            TestContext.Current.CancellationToken);

    private static async Task AssertFailureAsync(
        AtlasGoldFileApplicationFailure expected,
        Func<ValueTask<AtlasGoldFileApplicationDisposition>> action)
    {
        AtlasGoldFileApplicationException exception =
            await Assert.ThrowsAsync<AtlasGoldFileApplicationException>(
                async () => await action());
        Assert.Equal(expected, exception.Failure);
    }

    private static void AssertSubsequence(
        List<string> actual,
        IReadOnlyList<string> expected)
    {
        int actualIndex = 0;
        foreach (string item in expected)
        {
            while (actualIndex < actual.Count
                   && !StringComparer.Ordinal.Equals(actual[actualIndex], item))
            {
                actualIndex++;
            }

            Assert.True(
                actualIndex < actual.Count,
                $"Expected event '{item}' after index {actualIndex}. Actual: "
                    + string.Join(", ", actual));
            actualIndex++;
        }
    }

    private sealed class TrackingFileStream : FileStream
    {
        private readonly string role;
        private readonly List<string> events;
        private bool closeRecorded;

        public TrackingFileStream(
            string path,
            FileMode mode,
            FileAccess access,
            FileShare share,
            FileOptions options,
            string role,
            List<string> events)
            : base(
                path,
                new FileStreamOptions
                {
                    Mode = mode,
                    Access = access,
                    Share = share,
                    Options = options,
                })
        {
            this.role = role;
            this.events = events;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            events.Add($"{role}:write");
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            events.Add($"{role}:flush-async");
            return base.FlushAsync(cancellationToken);
        }

        public override void Flush(bool flushToDisk)
        {
            events.Add($"{role}:flush-disk:{flushToDisk}");
            base.Flush(flushToDisk);
        }

        public override async ValueTask DisposeAsync()
        {
            RecordClose();
            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            RecordClose();
            base.Dispose(disposing);
        }

        private void RecordClose()
        {
            if (!closeRecorded)
            {
                closeRecorded = true;
                events.Add($"{role}:close");
            }
        }
    }

    private sealed class CancelOnDisposeStream(
        Stream inner,
        CancellationTokenSource cancellation)
        : Stream
    {
        private bool disposed;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);

        public override async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                cancellation.Cancel();
                await inner.DisposeAsync();
            }

            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                cancellation.Cancel();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingWriteStream(Stream inner, IOException failure) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            throw failure;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(failure);

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ApplicationWorkspace : IAsyncDisposable
    {
        private ApplicationWorkspace(string rootPath, string slotPath)
        {
            RootPath = rootPath;
            SlotPath = slotPath;
        }

        public string RootPath { get; }

        public string SlotPath { get; }

        public string BackupPath => SlotPath + ".celesphonia-original.bak";

        public string BackupStagingPath =>
            SlotPath + ".celesphonia-original.bak.staging";

        public string CandidateStagePath => SlotPath + ".celesphonia-stage.tmp";

        public static async Task<ApplicationWorkspace> CreateAsync(long gold)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "atlas-gold-file-application-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string slot = Path.Combine(root, "file1.rpgsave");
            await File.WriteAllBytesAsync(
                slot,
                CreateSaveBytes(gold),
                TestContext.Current.CancellationToken);
            return new ApplicationWorkspace(root, slot);
        }

        public static byte[] CreateSaveBytes(long gold) =>
            AtlasLzStringCodec.CompressToBase64(
                CreateGoldJson(gold, gold),
                cancellationToken: TestContext.Current.CancellationToken);

        public static byte[] CreateDisagreeingSaveBytes() =>
            AtlasLzStringCodec.CompressToBase64(
                CreateGoldJson(1, 2),
                cancellationToken: TestContext.Current.CancellationToken);

        public static byte[] CreateIncompleteSaveBytes() =>
            AtlasLzStringCodec.CompressToBase64(
                "{\"party\":{\"_gold\":1}}",
                cancellationToken: TestContext.Current.CancellationToken);

        public byte[] CreateCandidateBytes(long value)
        {
            byte[] sourceBytes = File.ReadAllBytes(SlotPath);
            AtlasSaveReadResult source = AtlasSaveReader.Read(
                sourceBytes,
                Limits,
                TestContext.Current.CancellationToken);
            return AtlasGoldMutationKernel.CreateCandidate(
                    source,
                    value,
                    Limits,
                    TestContext.Current.CancellationToken)
                .GetCompressedBytes(TestContext.Current.CancellationToken);
        }

        public string GetSlotPath(int index) =>
            Path.Combine(RootPath, $"file{index}.rpgsave");

        public bool IsArtifactPath(string path) =>
            StringComparer.OrdinalIgnoreCase.Equals(path, BackupPath)
            || StringComparer.OrdinalIgnoreCase.Equals(path, BackupStagingPath)
            || StringComparer.OrdinalIgnoreCase.Equals(path, CandidateStagePath);

        public string? GetArtifactRole(string path)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(path, BackupStagingPath)
                || StringComparer.OrdinalIgnoreCase.Equals(path, BackupPath))
            {
                return "backup";
            }

            return StringComparer.OrdinalIgnoreCase.Equals(path, CandidateStagePath)
                ? "candidate"
                : null;
        }

        public void ReplaceLiveWith(byte[] bytes)
        {
            string replacement = Path.Combine(
                RootPath,
                $"external-replacement-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(replacement, bytes);
            File.Replace(replacement, SlotPath, null);
        }

        public static async Task<long> ReadGoldAsync(string path)
        {
            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);
            AtlasGoldReadModelResult result = AtlasGoldReadModel.Read(
                AtlasSaveReader.Read(
                    bytes,
                    Limits,
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
            Assert.Equal(AtlasGoldAggregateState.Consistent, result.Aggregate);
            Assert.Equal(result.PartyGold.Value, result.VariableGold.Value);
            return result.PartyGold.Value!.Value;
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static string CreateGoldJson(long partyGold, long variableGold)
        {
            StringBuilder data = new();
            data.Append('[');
            for (int index = 0; index < 216; index++)
            {
                if (index > 0)
                {
                    data.Append(',');
                }

                data.Append(index == 215 ? variableGold : 0);
            }

            data.Append(']');
            return $"{{\"party\":{{\"_gold\":{partyGold}}},"
                + $"\"variables\":{{\"_data\":{data}}}}}";
        }
    }
}
