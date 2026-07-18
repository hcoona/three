using Hcoona.CelesphoniaModifier.Atlas.Cli;
using System.Text;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasCliApplicationTests
{
    private static readonly byte[] ExpectedSurvey =
        "{\"schemaVersion\":\"atlas-empty-survey/v1\",\"observations\":[]}\n"u8.ToArray();

    private static readonly byte[] ExpectedHelp =
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

    public static TheoryData<string> HelpArgumentLines =>
        new()
        {
            "-h",
            "--help",
            "empty-survey -h",
            "empty-survey --help",
        };

    public static TheoryData<string> InvalidArgumentLines =>
        new()
        {
            string.Empty,
            "unknown",
            "EMPTY-SURVEY",
            "--HELP",
            "--version",
            "[suggest]",
            "@synthetic-response.rsp",
            "--",
            "/h",
            "-?",
            "/?",
            "empty-survey --unknown",
            "empty-survey extra",
        };

    [Fact]
    public async Task EmptySurveyWritesExactOutput()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) =
            await RunWithTestCancellationAsync(["empty-survey"]);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal(ExpectedSurvey, standardOutput);
        Assert.Empty(standardError);
    }

    [Theory]
    [MemberData(nameof(HelpArgumentLines))]
    public async Task HelpWritesExactOutputWithoutInvokingOperation(string argumentLine)
    {
        string[] args = argumentLine.Split(' ', StringSplitOptions.None);
        bool invoked = false;
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, byte[] standardOutput, byte[] standardError) =
            await RunAsync(
                args,
                (_, _) =>
                {
                    invoked = true;
                    return ValueTask.CompletedTask;
                },
                source.Token);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal(ExpectedHelp, standardOutput);
        Assert.Empty(standardError);
        Assert.False(invoked);
    }

    [Theory]
    [MemberData(nameof(InvalidArgumentLines))]
    public async Task InvalidArgumentsWriteFixedDiagnostic(string argumentLine)
    {
        string[] args = argumentLine.Length == 0
            ? []
            : argumentLine.Split(' ', StringSplitOptions.None);
        bool invoked = false;
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, byte[] standardOutput, byte[] standardError) =
            await RunAsync(
                args,
                (_, _) =>
                {
                    invoked = true;
                    return ValueTask.CompletedTask;
                },
                source.Token);

        Assert.Equal(AtlasCliApplication.UsageErrorExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Invalid arguments.\n"u8.ToArray(), standardError);
        Assert.False(invoked);
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
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task NullStandardOutputThrows()
    {
        using MemoryStream standardError = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AtlasCliApplication.RunAsync(
                ["empty-survey"],
                null!,
                standardError,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task NullStandardErrorThrows()
    {
        using MemoryStream standardOutput = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AtlasCliApplication.RunAsync(
                ["empty-survey"],
                standardOutput,
                null!,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task NullOperationThrows()
    {
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AtlasCliApplication.RunAsync(
                ["empty-survey"],
                standardOutput,
                standardError,
                null!,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task PreCanceledOperationReturnsCanceled()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, byte[] standardOutput, byte[] standardError) =
            await RunAsync(["empty-survey"], null, source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Empty(standardOutput);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task DefaultTokenCancellationMapsToCanceledWhenCallerCanceled()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            (_, _) => ValueTask.FromException(new OperationCanceledException()),
            source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task OperationReceivesCallerToken()
    {
        using CancellationTokenSource source = new();
        CancellationToken observedToken = default;

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            (_, token) =>
            {
                observedToken = token;
                return ValueTask.CompletedTask;
            },
            source.Token);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
        Assert.Equal(source.Token, observedToken);
        Assert.Empty(standardError);
    }

    [Fact]
    public async Task UnrequestedCallerTokenCancellationMapsToUnexpected()
    {
        using CancellationTokenSource source = new();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            (_, _) => ValueTask.FromException(
                new OperationCanceledException(source.Token)),
            source.Token);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task ForeignTokenCancellationMapsToUnexpected()
    {
        using CancellationTokenSource source = new();
        using CancellationTokenSource foreignSource = new();
        await source.CancelAsync();

        (int exitCode, _, byte[] standardError) = await RunAsync(
            ["empty-survey"],
            (_, _) => ValueTask.FromException(
                new OperationCanceledException(foreignSource.Token)),
            source.Token);

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task UnsolicitedCancellationMapsToUnexpected()
    {
        (int exitCode, _, byte[] standardError) = await RunWithTestCancellationAsync(
            ["empty-survey"],
            (_, _) => ValueTask.FromException(new OperationCanceledException()));

        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task PartialWriteCancellationReturnsCanceled()
    {
        using CancellationTokenSource source = new();

        (int exitCode, byte[] standardOutput, byte[] standardError) =
            await RunAsync(
                ["empty-survey"],
                async (output, token) =>
                {
                    await output.WriteAsync("prefix"u8.ToArray(), token);
                    await source.CancelAsync();
                    throw new OperationCanceledException(token);
                },
                source.Token);

        Assert.Equal(AtlasCliApplication.CanceledExitCode, exitCode);
        Assert.Equal("prefix"u8.ToArray(), standardOutput);
        Assert.Equal("Operation canceled.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task OutputFailureReturnsIoError()
    {
        (int exitCode, _, byte[] standardError) = await RunWithTestCancellationAsync(
            ["empty-survey"],
            (_, _) => ValueTask.FromException(
                new IOException("synthetic private detail")));

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError);
        Assert.DoesNotContain(
            "synthetic",
            Encoding.UTF8.GetString(standardError),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialWriteFailureReturnsIoError()
    {
        (int exitCode, byte[] standardOutput, byte[] standardError) =
            await RunWithTestCancellationAsync(
                ["empty-survey"],
                async (output, token) =>
                {
                    await output.WriteAsync("prefix"u8.ToArray(), token);
                    throw new IOException("synthetic write failure");
                });

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("prefix"u8.ToArray(), standardOutput);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError);
    }

    [Fact]
    public async Task NonWritableOutputReturnsIoError()
    {
        using NonWritableStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["empty-survey"],
            standardOutput,
            standardError,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
    }

    [Fact]
    public async Task UnexpectedFailureDoesNotExposeException()
    {
        (int exitCode, _, byte[] standardError) = await RunWithTestCancellationAsync(
            ["empty-survey"],
            (_, _) => ValueTask.FromException(
                new InvalidOperationException("synthetic private detail")));

        string diagnostic = Encoding.UTF8.GetString(standardError);
        Assert.Equal(AtlasCliApplication.UnexpectedErrorExitCode, exitCode);
        Assert.Equal("Unexpected failure.\n", diagnostic);
        Assert.DoesNotContain("synthetic", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpOutputFailureWritesIoDiagnostic()
    {
        using ThrowingWriteStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["--help"],
            standardOutput,
            standardError,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Equal("I/O failure.\n"u8.ToArray(), standardError.ToArray());
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
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasCliApplication.IoErrorExitCode, exitCode);
        Assert.Empty(standardOutput.ToArray());
    }

    [Fact]
    public async Task StreamsRemainCallerOwned()
    {
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();

        int exitCode = await AtlasCliApplication.RunAsync(
            ["empty-survey"],
            standardOutput,
            standardError,
            TestContext.Current.CancellationToken);

        standardOutput.WriteByte(0);
        standardError.WriteByte(0);

        Assert.Equal(AtlasCliApplication.SuccessExitCode, exitCode);
    }

    private static Task<(int ExitCode, byte[] StandardOutput, byte[] StandardError)>
        RunWithTestCancellationAsync(
        string[] args,
        Func<Stream, CancellationToken, ValueTask>? writeEmptySurvey = null) =>
        RunAsync(
            args,
            writeEmptySurvey,
            TestContext.Current.CancellationToken);

    private static async Task<(int ExitCode, byte[] StandardOutput, byte[] StandardError)> RunAsync(
        string[] args,
        Func<Stream, CancellationToken, ValueTask>? writeEmptySurvey,
        CancellationToken cancellationToken)
    {
        using MemoryStream standardOutput = new();
        using MemoryStream standardError = new();
        int exitCode = writeEmptySurvey is null
            ? await AtlasCliApplication.RunAsync(
                args,
                standardOutput,
                standardError,
                cancellationToken)
            : await AtlasCliApplication.RunAsync(
                args,
                standardOutput,
                standardError,
                writeEmptySurvey,
                cancellationToken);
        return (exitCode, standardOutput.ToArray(), standardError.ToArray());
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
