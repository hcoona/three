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
}
