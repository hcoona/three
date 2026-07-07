using Azure;
using Azure.Identity;

namespace Hcoona.DocumentTranslatorCli;

internal static class Program
{
    internal const int SuccessExitCode = 0;
    internal const int UnexpectedErrorExitCode = 1;
    internal const int ValidationErrorExitCode = 2;
    internal const int ServiceErrorExitCode = 3;
    internal const int FileIoErrorExitCode = 4;

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using CancellationTokenSource cancellationTokenSource = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;
        try
        {
            IDocumentTranslator translator = new AzureDocumentTranslator();
            TextWriter standardOutput = Console.Out;
            TextWriter standardError = Console.Error;
            return RunAsync(
                    args,
                    standardOutput,
                    standardError,
                    Environment.GetEnvironmentVariable,
                    (options, output, token) => ExecuteValidatedCommandAsync(
                        options,
                        output,
                        standardError,
                        translator,
                        AtomicOutputWriter.WriteAsync,
                        token),
                    cancellationTokenSource.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
        }
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
        CancellationToken cancellationToken) =>
        await RunAsync(
                args,
                standardOutput,
                standardError,
                getEnvironmentVariable,
                executeValidatedCommand,
                TranslationOptionsValidator.Validate,
                cancellationToken)
            .ConfigureAwait(false);

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
        Func<RawTranslationOptions, TranslationValidationResult> validateOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(executeValidatedCommand);
        ArgumentNullException.ThrowIfNull(validateOptions);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommandLineParseResult parseResult = DocumentTranslatorCommandLineParser.Parse(args);
            if (parseResult.ShowHelp)
            {
                await WriteOutputLineBestEffortAsync(
                        standardOutput,
                        parseResult.HelpText,
                        cancellationToken)
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
            TranslationValidationResult validationResult;
            try
            {
                validationResult = validateOptions(resolvedOptions);
            }
            catch (Exception ex) when (IsFileIoException(ex))
            {
                await WriteFileIoErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
                return FileIoErrorExitCode;
            }

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
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError)
                .ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex)
        {
            await WriteUnexpectedErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
    }

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        CancellationToken cancellationToken)
    {
        using AzureTextSegmentTranslator textSegmentTranslator = new();
        return await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                textSegmentTranslator,
                AtomicOutputWriter.WriteAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        ITextSegmentTranslator textSegmentTranslator,
        CancellationToken cancellationToken) =>
        await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                textSegmentTranslator,
                AtomicOutputWriter.WriteAsync,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        OutputWriter outputWriter,
        CancellationToken cancellationToken)
    {
        if (options.TranslationRoute == TranslationRoute.MarkdownAware)
        {
            using AzureTextSegmentTranslator textSegmentTranslator = new();
            return await ExecuteMarkdownAwareValidatedCommandAsync(
                    options,
                    standardOutput,
                    standardError,
                    textSegmentTranslator,
                    outputWriter,
                    CreatePreflightTempFile,
                    OpenExistingOutputForReplaceability,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                NoOpTextSegmentTranslator.Instance,
                outputWriter,
                CreatePreflightTempFile,
                OpenExistingOutputForReplaceability,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        ITextSegmentTranslator textSegmentTranslator,
        OutputWriter outputWriter,
        CancellationToken cancellationToken) =>
        await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                textSegmentTranslator,
                outputWriter,
                CreatePreflightTempFile,
                OpenExistingOutputForReplaceability,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        OutputWriter outputWriter,
        Func<string, FileStream> createPreflightTempFile,
        CancellationToken cancellationToken)
    {
        return await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                NoOpTextSegmentTranslator.Instance,
                outputWriter,
                createPreflightTempFile,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        ITextSegmentTranslator textSegmentTranslator,
        OutputWriter outputWriter,
        Func<string, FileStream> createPreflightTempFile,
        CancellationToken cancellationToken) =>
        await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                textSegmentTranslator,
                outputWriter,
                createPreflightTempFile,
                OpenExistingOutputForReplaceability,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        OutputWriter outputWriter,
        Func<string, FileStream> createPreflightTempFile,
        Func<string, FileStream> openExistingOutputForReplaceability,
        CancellationToken cancellationToken)
    {
        if (options.TranslationRoute == TranslationRoute.MarkdownAware)
        {
            using AzureTextSegmentTranslator textSegmentTranslator = new();
            return await ExecuteMarkdownAwareValidatedCommandAsync(
                    options,
                    standardOutput,
                    standardError,
                    textSegmentTranslator,
                    outputWriter,
                    createPreflightTempFile,
                    openExistingOutputForReplaceability,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                NoOpTextSegmentTranslator.Instance,
                outputWriter,
                createPreflightTempFile,
                openExistingOutputForReplaceability,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<int> ExecuteValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        IDocumentTranslator translator,
        ITextSegmentTranslator textSegmentTranslator,
        OutputWriter outputWriter,
        Func<string, FileStream> createPreflightTempFile,
        Func<string, FileStream> openExistingOutputForReplaceability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(textSegmentTranslator);
        ArgumentNullException.ThrowIfNull(outputWriter);
        ArgumentNullException.ThrowIfNull(createPreflightTempFile);
        ArgumentNullException.ThrowIfNull(openExistingOutputForReplaceability);

        if (options.TranslationRoute == TranslationRoute.MarkdownAware)
        {
            return await ExecuteMarkdownAwareValidatedCommandAsync(
                    options,
                    standardOutput,
                    standardError,
                    textSegmentTranslator,
                    outputWriter,
                    createPreflightTempFile,
                    openExistingOutputForReplaceability,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightOutputPath(
                options.OutputPath,
                options.Force,
                createPreflightTempFile,
                openExistingOutputForReplaceability);
        }
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex) when (IsFileIoException(ex))
        {
            await WriteFileIoErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return FileIoErrorExitCode;
        }

        FileStream inputStream;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputStream = OpenInputStream(options.InputPath);
        }
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex) when (IsFileIoException(ex))
        {
            await WriteFileIoErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return FileIoErrorExitCode;
        }

        BinaryData translatedContent;
        try
        {
            await using (inputStream.ConfigureAwait(false))
            {
                translatedContent = await translator
                    .TranslateAsync(options, inputStream, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (RequestFailedException ex)
        {
            await WriteErrorLineBestEffortAsync(
                    standardError,
                    $"Error: Azure service error ({ex.Status}, {ex.ErrorCode}): {ex.Message}")
                .ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (CredentialUnavailableException)
        {
            await WriteCredentialErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (AuthenticationFailedException)
        {
            await WriteCredentialErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex)
        {
            await WriteUnexpectedErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }

        try
        {
            await outputWriter(
                    options.OutputPath,
                    translatedContent,
                    options.Force,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex) when (IsFileIoException(ex))
        {
            await WriteFileIoErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return FileIoErrorExitCode;
        }
        catch (Exception ex)
        {
            await WriteUnexpectedErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }

        try
        {
            await standardOutput
                .WriteLineAsync("Translation completed.".AsMemory(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        return SuccessExitCode;
    }

    private static async ValueTask<int> ExecuteMarkdownAwareValidatedCommandAsync(
        TranslationOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        ITextSegmentTranslator textSegmentTranslator,
        OutputWriter outputWriter,
        Func<string, FileStream> createPreflightTempFile,
        Func<string, FileStream> openExistingOutputForReplaceability,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightOutputPath(
                options.OutputPath,
                options.Force,
                createPreflightTempFile,
                openExistingOutputForReplaceability);
        }
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex) when (IsFileIoException(ex))
        {
            await WriteFileIoErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return FileIoErrorExitCode;
        }

        MarkdownTranslationCommand command = new(
            MarkdownDocumentParser.CreateV1(),
            textSegmentTranslator,
            outputWriter);

        MarkdownTranslationCommandResult result;
        try
        {
            result = await command.ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            await WriteErrorLineBestEffortAsync(
                    standardError,
                    $"Error: Azure service error ({ex.Status}, {ex.ErrorCode}).")
                .ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (CredentialUnavailableException)
        {
            await WriteCredentialErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (AuthenticationFailedException)
        {
            await WriteCredentialErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (TextTranslationServiceException ex)
        {
            await WriteErrorLineBestEffortAsync(
                    standardError,
                    $"Error: Azure Text Translation service error: {ex.Message}")
                .ConfigureAwait(false);
            return ServiceErrorExitCode;
        }
        catch (MarkdownTranslationFileIoException ex)
        {
            await WriteFileIoErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return FileIoErrorExitCode;
        }
        catch (OperationCanceledException)
        {
            await WriteOperationCanceledErrorBestEffortAsync(standardError).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }
        catch (Exception ex)
        {
            await WriteUnexpectedErrorBestEffortAsync(standardError, ex).ConfigureAwait(false);
            return UnexpectedErrorExitCode;
        }

        if (!result.Success)
        {
            await WriteMarkdownDiagnosticsAsync(standardError, result.Diagnostics)
                .ConfigureAwait(false);
            return ValidationErrorExitCode;
        }

        try
        {
            await standardOutput
                .WriteLineAsync("Translation completed.".AsMemory(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        return SuccessExitCode;
    }

    private static FileStream OpenInputStream(string inputPath) =>
        new(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

    private static FileStream CreatePreflightTempFile(string tempPath) =>
        new(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            useAsync: false);

    private static FileStream OpenExistingOutputForReplaceability(string outputPath) =>
        new(
            outputPath,
            FileMode.Open,
            OperatingSystem.IsWindows() ? FileAccess.ReadWrite : FileAccess.Read,
            FileShare.None,
            bufferSize: 1,
            useAsync: false);

    internal static void PreflightOutputPath(
        string outputPath,
        Func<string, FileStream> createTempFile) =>
        PreflightOutputPath(
            outputPath,
            force: false,
            createTempFile,
            OpenExistingOutputForReplaceability);

    internal static void PreflightOutputPath(
        string outputPath,
        bool force,
        Func<string, FileStream> createTempFile) =>
        PreflightOutputPath(
            outputPath,
            force,
            createTempFile,
            OpenExistingOutputForReplaceability);

    internal static void PreflightOutputPath(
        string outputPath,
        bool force,
        Func<string, FileStream> createTempFile,
        Func<string, FileStream> openExistingOutputForReplaceability)
    {
        ArgumentNullException.ThrowIfNull(createTempFile);
        ArgumentNullException.ThrowIfNull(openExistingOutputForReplaceability);

        if (TranslationOptionsValidator.IsDirectoryLikeOutputPath(outputPath))
        {
            throw new ArgumentException(
                "Output path must include a file name.",
                nameof(outputPath));
        }

        string fullOutputPath = Path.GetFullPath(outputPath);
        if (Directory.Exists(fullOutputPath))
        {
            throw new IOException($"Output path is an existing directory: '{fullOutputPath}'.");
        }

        if (!force && File.Exists(fullOutputPath))
        {
            throw new IOException(
                $"Output file already exists. Use --force to replace it: '{fullOutputPath}'.");
        }

        string outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);
        string tempPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using FileStream _ = createTempFile(tempPath);
        }
        finally
        {
            TryDelete(tempPath);
        }

        if (force && File.Exists(fullOutputPath) && OperatingSystem.IsWindows())
        {
            if ((File.GetAttributes(fullOutputPath) & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException(
                    $"Output file is read-only: '{fullOutputPath}'.");
            }

            using FileStream _ = openExistingOutputForReplaceability(fullOutputPath);
        }
    }

    private static async ValueTask WriteCredentialErrorBestEffortAsync(TextWriter standardError)
    {
        await WriteErrorLineBestEffortAsync(
                standardError,
                "Error: Azure credential acquisition failed.")
            .ConfigureAwait(false);
    }

    private static async ValueTask WriteFileIoErrorBestEffortAsync(
        TextWriter standardError,
        Exception exception)
    {
        await WriteErrorLineBestEffortAsync(
                standardError,
                $"Error: File I/O error: {exception.Message}")
            .ConfigureAwait(false);
    }

    private static async ValueTask WriteOperationCanceledErrorBestEffortAsync(
        TextWriter standardError)
    {
        await WriteErrorLineBestEffortAsync(standardError, "Error: Operation canceled.")
            .ConfigureAwait(false);
    }

    private static async ValueTask WriteMarkdownDiagnosticsAsync(
        TextWriter standardError,
        IReadOnlyCollection<MarkdownDiagnostic> diagnostics)
    {
        foreach (MarkdownDiagnostic diagnostic in diagnostics)
        {
            string location = diagnostic.Line is null ? string.Empty : $" line {diagnostic.Line}:";
            await WriteErrorLineBestEffortAsync(
                    standardError,
                    $"Error: Markdown-aware {diagnostic.Kind}{location} {diagnostic.Message}")
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask WriteUnexpectedErrorBestEffortAsync(
        TextWriter standardError,
        Exception exception)
    {
        await WriteErrorLineBestEffortAsync(
                standardError,
                $"Error: Unexpected error: {exception.Message}")
            .ConfigureAwait(false);
    }

    private static async ValueTask WriteErrorLineBestEffortAsync(
        TextWriter standardError,
        string message)
    {
        try
        {
            await standardError
                .WriteLineAsync(message.AsMemory(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsOutputChannelException(ex))
        {
        }
    }

    private static async ValueTask WriteOutputLineBestEffortAsync(
        TextWriter standardOutput,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await standardOutput
                .WriteLineAsync(message.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsOutputChannelException(ex))
        {
        }
    }

    private static bool IsFileIoException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or PathTooLongException
            or NotSupportedException
            or ArgumentException;

    private static bool IsOutputChannelException(Exception exception) =>
        exception is IOException or ObjectDisposedException or InvalidOperationException;

    private sealed class NoOpTextSegmentTranslator : ITextSegmentTranslator
    {
        public static readonly NoOpTextSegmentTranslator Instance = new();

        private NoOpTextSegmentTranslator()
        {
        }

        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(segments);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async ValueTask WriteErrorsAsync(
        TextWriter standardError,
        IReadOnlyCollection<string> errors,
        CancellationToken cancellationToken)
    {
        foreach (string error in errors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteErrorLineBestEffortAsync(standardError, $"Error: {error}")
                .ConfigureAwait(false);
        }
    }
}
