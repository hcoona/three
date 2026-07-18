using Hcoona.CelesphoniaModifier.Atlas;

namespace Hcoona.CelesphoniaModifier.Atlas.Cli;

internal static class AtlasCliApplication
{
    internal const int SuccessExitCode = 0;
    internal const int UnexpectedErrorExitCode = 1;
    internal const int UsageErrorExitCode = 2;
    internal const int CanceledExitCode = 3;
    internal const int IoErrorExitCode = 4;

    private static readonly byte[] HelpBytes =
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

    private static readonly byte[] InvalidArgumentsBytes = "Invalid arguments.\n"u8.ToArray();
    private static readonly byte[] CanceledBytes = "Operation canceled.\n"u8.ToArray();
    private static readonly byte[] IoFailureBytes = "I/O failure.\n"u8.ToArray();
    private static readonly byte[] UnexpectedFailureBytes = "Unexpected failure.\n"u8.ToArray();

    internal static ValueTask<int> RunAsync(
        string[] args,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken) =>
        RunAsync(
            args,
            standardOutput,
            standardError,
            EmptyAtlasSurvey.WriteAsync,
            cancellationToken);

    internal static async ValueTask<int> RunAsync(
        string[] args,
        Stream standardOutput,
        Stream standardError,
        Func<Stream, CancellationToken, ValueTask> writeEmptySurvey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(writeEmptySurvey);

        if (IsHelp(args))
        {
            return await WriteHelpAsync(standardOutput, standardError).ConfigureAwait(false);
        }

        if (!IsEmptySurvey(args))
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    InvalidArgumentsBytes,
                    UsageErrorExitCode)
                .ConfigureAwait(false);
        }

        try
        {
            await writeEmptySurvey(standardOutput, cancellationToken).ConfigureAwait(false);
            return SuccessExitCode;
        }
        catch (OperationCanceledException exception)
            when (IsCallerCancellation(exception, cancellationToken))
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    CanceledBytes,
                    CanceledExitCode)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsStreamOutputFailure(exception))
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    IoFailureBytes,
                    IoErrorExitCode)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    UnexpectedFailureBytes,
                    UnexpectedErrorExitCode)
                .ConfigureAwait(false);
        }
    }

    private static bool IsEmptySurvey(string[] args) =>
        args.Length == 1
        && StringComparer.Ordinal.Equals(args[0], "empty-survey");

    private static bool IsHelp(string[] args) =>
        (args.Length == 1 && IsHelpToken(args[0]))
        || (args.Length == 2
            && StringComparer.Ordinal.Equals(args[0], "empty-survey")
            && IsHelpToken(args[1]));

    private static bool IsHelpToken(string argument) =>
        StringComparer.Ordinal.Equals(argument, "-h")
        || StringComparer.Ordinal.Equals(argument, "--help");

    private static bool IsCallerCancellation(
        OperationCanceledException exception,
        CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && (exception.CancellationToken == cancellationToken
            || exception.CancellationToken == default);

    private static bool IsStreamOutputFailure(Exception exception) =>
        exception is IOException
            or ObjectDisposedException
            or NotSupportedException;

    private static async ValueTask<int> WriteHelpAsync(
        Stream standardOutput,
        Stream standardError)
    {
        try
        {
            await WriteBytesAsync(standardOutput, HelpBytes).ConfigureAwait(false);
            return SuccessExitCode;
        }
        catch (Exception)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    IoFailureBytes,
                    IoErrorExitCode)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask<int> WriteDiagnosticAsync(
        Stream standardError,
        ReadOnlyMemory<byte> diagnostic,
        int intendedExitCode)
    {
        try
        {
            await WriteBytesAsync(standardError, diagnostic).ConfigureAwait(false);
            return intendedExitCode;
        }
        catch (Exception)
        {
            return IoErrorExitCode;
        }
    }

    private static async ValueTask WriteBytesAsync(
        Stream destination,
        ReadOnlyMemory<byte> bytes)
    {
        if (!destination.CanWrite)
        {
            throw new NotSupportedException("The destination stream does not support writing.");
        }

        await destination.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }
}
