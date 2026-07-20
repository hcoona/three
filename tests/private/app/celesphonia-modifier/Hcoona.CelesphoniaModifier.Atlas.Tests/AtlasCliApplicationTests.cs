using System.Text;
using Hcoona.CelesphoniaModifier.Atlas;
using Hcoona.CelesphoniaModifier.Atlas.Cli;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasCliApplicationTests
{
    private static readonly byte[] ExpectedSurvey =
        "{\"schemaVersion\":\"atlas-empty-survey/v1\",\"observations\":[]}\n"u8.ToArray();

    private static readonly byte[] ExpectedEmptySurveyHelp =
    [
        .. "Usage:\n"u8,
        .. "  celesphonia-atlas empty-survey\n"u8,
        .. "\n"u8,
        .. "Commands:\n"u8,
        .. "  empty-survey  Write a deterministic empty Atlas survey.\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    private static readonly byte[] ExpectedCommandHelp =
    [
        .. "Usage: celesphonia-atlas <command> <request-file>\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    private static readonly byte[] ExpectedGlobalHelp =
    [
        .. "Usage:\n"u8,
        .. "  celesphonia-atlas empty-survey\n"u8,
        .. "  celesphonia-atlas intake-discover <request-file>\n"u8,
        .. "  celesphonia-atlas intake-confirm <request-file>\n"u8,
        .. "  celesphonia-atlas intake-copy <request-file>\n"u8,
        .. "  celesphonia-atlas cleanup-preflight <request-file>\n"u8,
        .. "\n"u8,
        .. "Commands:\n"u8,
        .. "  empty-survey       Write a deterministic empty Atlas survey.\n"u8,
        .. "  intake-discover    Discover the approved Atlas intake scope.\n"u8,
        .. "  intake-confirm     Confirm an approved Atlas intake manifest.\n"u8,
        .. "  intake-copy        Create qualified Atlas research snapshots.\n"u8,
        .. "  cleanup-preflight  Report private-artifact cleanup eligibility.\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    public static TheoryData<string[]> GlobalHelpArgs =>
        new()
        {
            { ["-h"] },
            { ["--help"] },
        };

    public static TheoryData<string[], byte[]> CommandHelpArgs =>
        new()
        {
            { ["empty-survey", "-h"], ExpectedEmptySurveyHelp },
            { ["empty-survey", "--help"], ExpectedEmptySurveyHelp },
            { ["intake-discover", "-h"], ExpectedCommandHelp },
            { ["intake-confirm", "--help"], ExpectedCommandHelp },
            { ["intake-copy", "-h"], ExpectedCommandHelp },
            { ["cleanup-preflight", "--help"], ExpectedCommandHelp },
        };

    public static TheoryData<string[]> InvalidArgs =>
        new()
        {
            { [] },
            { ["unknown"] },
            { ["EMPTY-SURVEY"] },
            { ["--version"] },
            { ["[suggest]"] },
            { ["@synthetic-response.rsp"] },
            { ["--"] },
            { ["/h"] },
            { ["intake-discover"] },
            { ["intake-discover", "--help", "extra"] },
            { ["intake-discover", "one", "two"] },
        };

    [Fact]
    public async Task EmptySurveyWritesExactOutput()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal(ExpectedSurvey, standardOutput);
        Assert.Empty(standardError);
    }

    [Theory]
    [MemberData(nameof(GlobalHelpArgs))]
    public async Task GlobalHelpWritesExactOutput(string[] args)
    {
        bool invoked = false;
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            args,
            new DelegatingOperations
            {
                Discover = (_, _) =>
                {
                    invoked = true;
                    return ValueTask.CompletedTask;
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal(ExpectedGlobalHelp, standardOutput);
        Assert.Empty(standardError);
        Assert.False(invoked);
    }

    [Theory]
    [MemberData(nameof(CommandHelpArgs))]
    public async Task CommandHelpWritesExactOutput(string[] args, byte[] expected)
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            args,
            new DelegatingOperations(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal(expected, standardOutput);
        Assert.Empty(standardError);
    }

    [Theory]
    [MemberData(nameof(InvalidArgs))]
    public async Task InvalidArgumentsWriteFixedDiagnostic(string[] args)
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            args,
            new DelegatingOperations(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.UsageErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task WhitespaceRequestPathIsRejectedBeforeDispatch()
    {
        bool invoked = false;
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-discover", " "],
            new DelegatingOperations
            {
                Discover = (_, _) =>
                {
                    invoked = true;
                    return ValueTask.CompletedTask;
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.UsageErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), standardError);
        Assert.False(invoked);
    }

    [Fact]
    public async Task IntakeDiscoverWritesFixedSuccessBytes()
    {
        string? observedPath = null;
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (requestPath, _) =>
                {
                    observedPath = requestPath;
                    return ValueTask.CompletedTask;
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal("Intake discovery completed.\n"u8.ToArray(), standardOutput);
        Assert.Empty(standardError);
        Assert.Equal(@"Q:\private\discover.json", observedPath);
    }

    [Fact]
    public async Task IntakeConfirmWritesFixedSuccessBytes()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-confirm", @"Q:\private\confirm.json"],
            new DelegatingOperations
            {
                Confirm = (_, _) => ValueTask.CompletedTask,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal("Intake confirmation completed.\n"u8.ToArray(), standardOutput);
        Assert.Empty(standardError);
    }

    [Fact]
    public async Task IntakeCopyWritesFixedSuccessBytes()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-copy", @"Q:\private\copy.json"],
            new DelegatingOperations
            {
                Copy = (_, _) => ValueTask.CompletedTask,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal("Intake copy completed.\n"u8.ToArray(), standardOutput);
        Assert.Empty(standardError);
    }

    [Fact]
    public async Task CleanupPreflightWritesFixedSuccessBytes()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["cleanup-preflight", @"Q:\private\cleanup.json"],
            new DelegatingOperations
            {
                CleanupPreflight = (_, _) => ValueTask.CompletedTask,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal("Cleanup preflight completed.\n"u8.ToArray(), standardOutput);
        Assert.Empty(standardError);
    }

    [Fact]
    public async Task RequestFailuresMapToApprovalSafetyIoAndUnexpected()
    {
        string requestPath = @"Q:\private\secret.json";

        (int approvalCode, _, byte[] approvalError) = await RunAsync(
            ["intake-confirm", requestPath],
            new DelegatingOperations
            {
                Confirm = (_, _) => ValueTask.FromException(new AtlasApprovalException("private")),
            },
            TestContext.Current.CancellationToken);
        (int safetyCode, _, byte[] safetyError) = await RunAsync(
            ["intake-copy", requestPath],
            new DelegatingOperations
            {
                Copy = (_, _) => ValueTask.FromException(new AtlasSafetyException("private")),
            },
            TestContext.Current.CancellationToken);
        (int ioCode, _, byte[] ioError) = await RunAsync(
            ["cleanup-preflight", requestPath],
            new DelegatingOperations
            {
                CleanupPreflight = (_, _) => ValueTask.FromException(
                    new IOException("private detail")),
            },
            TestContext.Current.CancellationToken);
        (int unexpectedCode, _, byte[] unexpectedError) = await RunAsync(
            ["intake-discover", requestPath],
            new DelegatingOperations
            {
                Discover = (_, _) => ValueTask.FromException(
                    new InvalidOperationException("private detail")),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.ApprovalRequiredExitCode, approvalCode);
        Assert.Equal("Approval required.\n"u8.ToArray(), approvalError);
        Assert.Equal(AtlasCliApplication.SafetyErrorExitCode, safetyCode);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), safetyError);
        Assert.Equal(AtlasCliApplication.IoErrorExitCode, ioCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), ioError);
        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, unexpectedCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), unexpectedError);

        Assert.DoesNotContain(
            requestPath,
            Encoding.UTF8.GetString(approvalError),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            requestPath,
            Encoding.UTF8.GetString(safetyError),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            requestPath,
            Encoding.UTF8.GetString(ioError),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            requestPath,
            Encoding.UTF8.GetString(unexpectedError),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        AtlasDiscoveryFailureStage.RequestPreflight,
        "Safety check failed: request-preflight.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.WorkspacePreflight,
        "Safety check failed: workspace-preflight.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.ExistingState,
        "Safety check failed: existing-state.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.BaselineInventory,
        "Safety check failed: baseline-inventory.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.LiveSourcePreflight,
        "Safety check failed: live-source-preflight.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.CorpusReconciliation,
        "Safety check failed: corpus-reconciliation.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.Publication,
        "Safety check failed: publication.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.PrivateWorkspacePolicy,
        "Safety check failed: private-workspace-policy.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.DiscoveryCanonicalPaths,
        "Safety check failed: canonical-paths.\n")]
    [InlineData(
        AtlasDiscoveryFailureStage.CommandWorkspaceCensus,
        "Safety check failed: workspace-census.\n")]
    public async Task IntakeDiscoverySafetyFailureWritesFixedStageBytes(
        AtlasDiscoveryFailureStage stage,
        string expectedDiagnostic)
    {
        string requestPath = @"Q:\private\discover.json";
        string privateMessage = $"private failure at {requestPath}";
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-discover", requestPath],
            new DelegatingOperations
            {
                Discover = (_, _) => ValueTask.FromException(
                    new AtlasSafetyException(privateMessage, stage)),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SafetyErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal(Encoding.UTF8.GetBytes(expectedDiagnostic), standardError);
        Assert.DoesNotContain(
            privateMessage,
            Encoding.UTF8.GetString(standardError),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            requestPath,
            Encoding.UTF8.GetString(standardError),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntakeDiscoveryUnspecifiedSafetyFailureUsesGenericDiagnostic()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (_, _) => ValueTask.FromException(
                    new AtlasSafetyException(
                        "private",
                        AtlasDiscoveryFailureStage.Unspecified)),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SafetyErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task IntakeDiscoveryUnknownSafetyFailureUsesGenericDiagnostic()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (_, _) => ValueTask.FromException(
                    new AtlasSafetyException(
                        "private",
                        (AtlasDiscoveryFailureStage)int.MaxValue)),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SafetyErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), standardError);
    }

    [Theory]
    [InlineData("empty-survey")]
    [InlineData("intake-confirm")]
    [InlineData("intake-copy")]
    [InlineData("cleanup-preflight")]
    public async Task NonDiscoverySafetyFailureIgnoresDiscoveryStage(string command)
    {
        string requestPath = @"Q:\private\request.json";
        AtlasSafetyException exception = new(
            $"private failure at {requestPath}",
            AtlasDiscoveryFailureStage.Publication);
        DelegatingOperations operations = new()
        {
            EmptySurvey = (_, _) => ValueTask.FromException(exception),
            Confirm = (_, _) => ValueTask.FromException(exception),
            Copy = (_, _) => ValueTask.FromException(exception),
            CleanupPreflight = (_, _) => ValueTask.FromException(exception),
        };
        string[] args = StringComparer.Ordinal.Equals(command, "empty-survey")
            ? [command]
            : [command, requestPath];

        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            args,
            operations,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.SafetyErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Safety check failed.\n"u8.ToArray(), standardError);
        Assert.DoesNotContain(
            requestPath,
            Encoding.UTF8.GetString(standardError),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationCancellationUsesCallerToken()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["intake-copy", @"Q:\private\copy.json"],
            new DelegatingOperations
            {
                Copy = (_, token) => ValueTask.FromException(new OperationCanceledException(token)),
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyDefaultTokenCancellationUsesCallerCancellation()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations
            {
                EmptySurvey = (_, _) =>
                    ValueTask.FromException(new OperationCanceledException()),
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyForeignTokenCancellationIsUnexpected()
    {
        using CancellationTokenSource source = new();
        using CancellationTokenSource foreignSource = new();
        await source.CancelAsync();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations
            {
                EmptySurvey = (_, _) => ValueTask.FromException(
                    new OperationCanceledException(foreignSource.Token)),
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyUnsolicitedCancellationIsUnexpected()
    {
        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations
            {
                EmptySurvey = (_, _) =>
                    ValueTask.FromException(new OperationCanceledException()),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyUnrequestedCallerTokenCancellationIsUnexpected()
    {
        using CancellationTokenSource source = new();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations
            {
                EmptySurvey = (_, _) => ValueTask.FromException(
                    new OperationCanceledException(source.Token)),
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyReceivesCallerTokenAndMapsPartialCancellation()
    {
        using CancellationTokenSource source = new();
        CancellationToken observedToken = default;
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations
            {
                EmptySurvey = async (output, token) =>
                {
                    observedToken = token;
                    await output.WriteAsync("prefix"u8.ToArray(), token);
                    await source.CancelAsync();
                    throw new OperationCanceledException(token);
                },
            },
            source.Token);

        Assert.Equal(source.Token, observedToken);
        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("prefix"u8.ToArray(), standardOutput);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyOutputFailuresUseIoDiagnostic()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            new DelegatingOperations
            {
                EmptySurvey = async (output, token) =>
                {
                    await output.WriteAsync("prefix"u8.ToArray(), token);
                    throw new IOException("synthetic private detail");
                },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("prefix"u8.ToArray(), standardOutput);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task EmptySurveyNonWritableOutputUsesIoDiagnostic()
    {
        using NonWritableStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["empty-survey"],
            standardOutput,
            standardError,
            AtlasCliOperations.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task EmptySurveyStdoutFailureUsesIoDiagnostic()
    {
        using ThrowingWriteStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["empty-survey"],
            standardOutput,
            standardError,
            AtlasCliOperations.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task A2DefaultTokenCancellationUsesCallerCancellation()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (_, _) =>
                    ValueTask.FromException(new OperationCanceledException()),
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task A2ForeignAndUnsolicitedCancellationAreUnexpected()
    {
        using CancellationTokenSource callerSource = new();
        using CancellationTokenSource foreignSource = new();
        await callerSource.CancelAsync();
        (int foreignCode, _, byte[] foreignError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (_, _) => ValueTask.FromException(
                    new OperationCanceledException(foreignSource.Token)),
            },
            callerSource.Token);
        (int unsolicitedCode, _, byte[] unsolicitedError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (_, _) =>
                    ValueTask.FromException(new OperationCanceledException()),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, foreignCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), foreignError);
        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, unsolicitedCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), unsolicitedError);
    }

    [Fact]
    public async Task A2UnrequestedCallerTokenCancellationIsUnexpected()
    {
        using CancellationTokenSource source = new();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = (_, _) => ValueTask.FromException(
                    new OperationCanceledException(source.Token)),
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task A2CancellationBeforeSuccessWriteDoesNotReportSuccess()
    {
        using CancellationTokenSource source = new();
        (int exitCode, byte[] standardOutput, byte[] standardError) = await RunAsync(
            ["intake-discover", @"Q:\private\discover.json"],
            new DelegatingOperations
            {
                Discover = async (_, token) =>
                {
                    Assert.Equal(source.Token, token);
                    await source.CancelAsync();
                },
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task A2CancellationDuringPartialSuccessWriteDoesNotReportSuccess()
    {
        using CancellationTokenSource source = new();
        using CancelingWriteStream standardOutput = new(source);
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["intake-copy", @"Q:\private\copy.json"],
            standardOutput,
            standardError,
            new DelegatingOperations
            {
                Copy = (_, token) =>
                {
                    Assert.Equal(source.Token, token);
                    return ValueTask.CompletedTask;
                },
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("Intake "u8.ToArray(), standardOutput.ToArray());
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task A2SuccessOutputFailuresUseIoDiagnostic()
    {
        using ThrowingWriteStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["cleanup-preflight", @"Q:\private\cleanup.json"],
            standardOutput,
            standardError,
            new DelegatingOperations(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task A2NonWritableSuccessOutputUsesIoDiagnostic()
    {
        using NonWritableStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["intake-confirm", @"Q:\private\confirm.json"],
            standardOutput,
            standardError,
            new DelegatingOperations(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task HelpAndInvalidInputIgnoreCallerCancellation()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int helpCode, byte[] helpOutput, byte[] helpError) = await RunAsync(
            ["--help"],
            new DelegatingOperations(),
            source.Token);
        (int invalidCode, byte[] invalidOutput, byte[] invalidError) = await RunAsync(
            ["invalid"],
            new DelegatingOperations(),
            source.Token);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, helpCode);
        Assert.Equal(ExpectedGlobalHelp, helpOutput);
        Assert.Empty(helpError);
        Assert.Equal(AtlasCliApplication.UsageErrorExitCode, invalidCode);
        Assert.Empty(invalidOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), invalidError);
    }

    [Fact]
    public async Task HelpOutputFailureUsesIoDiagnostic()
    {
        using ThrowingWriteStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["--help"],
            standardOutput,
            standardError,
            new DelegatingOperations(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task A2DiagnosticFailureTakesIoPrecedence()
    {
        using MemoryStream standardOutput = new();
        using ThrowingWriteStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["intake-copy", @"Q:\private\copy.json"],
            standardOutput,
            standardError,
            new DelegatingOperations
            {
                Copy = (_, _) => ValueTask.FromException(
                    new AtlasSafetyException("synthetic private detail")),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Empty(standardOutput.ToArray());
    }

    [Fact]
    public async Task DiagnosticFailureReturnsIoError()
    {
        using MemoryStream standardOutput = new();
        using ThrowingWriteStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["invalid"],
            standardOutput,
            standardError,
            AtlasCliOperations.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
    }

    [Fact]
    public async Task StreamsRemainCallerOwned()
    {
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["--help"],
            standardOutput,
            standardError,
            AtlasCliOperations.Default,
            TestContext.Current.CancellationToken);

        standardOutput.WriteByte(0);
        standardError.WriteByte(0);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
    }

    [Fact]
    public async Task NullArgumentsThrow()
    {
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AtlasCliApplication.RunAsync(
                null!,
                standardOutput,
                standardError,
                AtlasCliOperations.Default,
                TestContext.Current.CancellationToken).AsTask());
    }

    private static async Task<(int ExitCode, byte[] StandardOutput, byte[] StandardError)> RunAsync(
        string[] args,
        AtlasCliOperations operations,
        CancellationToken cancellationToken)
    {
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();
        int exitCode = await AtlasCliApplication.RunAsync(
            args,
            standardOutput,
            standardError,
            operations,
            cancellationToken);
        return (exitCode, standardOutput.ToArray(), standardError.ToArray());
    }

    private sealed class DelegatingOperations : AtlasCliOperations
    {
        public Func<string, CancellationToken, ValueTask>? CleanupPreflight { get; init; }

        public Func<string, CancellationToken, ValueTask>? Confirm { get; init; }

        public Func<string, CancellationToken, ValueTask>? Copy { get; init; }

        public Func<string, CancellationToken, ValueTask>? Discover { get; init; }

        public Func<Stream, CancellationToken, ValueTask>? EmptySurvey { get; init; }

        public override ValueTask WriteEmptySurveyAsync(
            Stream standardOutput,
            CancellationToken cancellationToken) =>
            EmptySurvey?.Invoke(standardOutput, cancellationToken)
            ?? base.WriteEmptySurveyAsync(standardOutput, cancellationToken);

        public override ValueTask RunCleanupPreflightAsync(
            string requestFilePath,
            CancellationToken cancellationToken) =>
            CleanupPreflight?.Invoke(requestFilePath, cancellationToken)
            ?? ValueTask.CompletedTask;

        public override ValueTask RunIntakeConfirmAsync(
            string requestFilePath,
            CancellationToken cancellationToken) =>
            Confirm?.Invoke(requestFilePath, cancellationToken)
            ?? ValueTask.CompletedTask;

        public override ValueTask RunIntakeCopyAsync(
            string requestFilePath,
            CancellationToken cancellationToken) =>
            Copy?.Invoke(requestFilePath, cancellationToken)
            ?? ValueTask.CompletedTask;

        public override ValueTask RunIntakeDiscoverAsync(
            string requestFilePath,
            CancellationToken cancellationToken) =>
            Discover?.Invoke(requestFilePath, cancellationToken)
            ?? ValueTask.CompletedTask;
    }

    private sealed class ThrowingWriteStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("synthetic stream failure");

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("synthetic stream failure"));
    }

    private sealed class NonWritableStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class CancelingWriteStream(CancellationTokenSource source) : Stream
    {
        private readonly MemoryStream inner = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => inner.ToArray();

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer[..7], cancellationToken);
            await source.CancelAsync();
            throw new OperationCanceledException(cancellationToken);
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
}
