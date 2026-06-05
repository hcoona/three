namespace Hcoona.DocumentTranslatorCli;

internal static class Program
{
    internal const int SuccessExitCode = 0;
    internal const int UnexpectedErrorExitCode = 1;
    internal const int ValidationErrorExitCode = 2;

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return RunAsync(
                args,
                Console.Out,
                Console.Error,
                Environment.GetEnvironmentVariable,
                (_, _, _) =>
                {
                    Console.Error.WriteLine("Translation execution is not implemented yet.");
                    return new ValueTask<int>(UnexpectedErrorExitCode);
                },
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    internal static async ValueTask<int> RunAsync(
        string[] args,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<string, string?> getEnvironmentVariable,
        Func<
            TranslationOptions,
            TextWriter,
            CancellationToken,
            ValueTask<int>> executeValidatedCommand,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(executeValidatedCommand);

        CommandLineParseResult parseResult = DocumentTranslatorCommandLineParser.Parse(args);
        if (parseResult.ShowHelp)
        {
            await standardOutput
                .WriteLineAsync(parseResult.HelpText.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            return SuccessExitCode;
        }

        if (parseResult.Errors.Count > 0)
        {
            await WriteErrorsAsync(standardError, parseResult.Errors, cancellationToken)
                .ConfigureAwait(false);
            return ValidationErrorExitCode;
        }

        RawTranslationOptions resolvedOptions = TranslationOptionResolver.Resolve(
            parseResult.Options,
            getEnvironmentVariable);
        TranslationValidationResult validationResult =
            TranslationOptionsValidator.Validate(resolvedOptions);
        if (validationResult.Errors.Count > 0)
        {
            await WriteErrorsAsync(standardError, validationResult.Errors, cancellationToken)
                .ConfigureAwait(false);
            return ValidationErrorExitCode;
        }

        return await executeValidatedCommand(
                validationResult.Options!,
                standardOutput,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask WriteErrorsAsync(
        TextWriter standardError,
        IReadOnlyCollection<string> errors,
        CancellationToken cancellationToken)
    {
        foreach (string error in errors)
        {
            await standardError
                .WriteLineAsync($"Error: {error}".AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
