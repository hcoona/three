using Azure.AI.Translation.Document;
using Xunit;

namespace Hcoona.DocumentTranslatorCli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void ProgramTypeIsAvailableFromApplicationAssembly()
    {
        Type programType = typeof(Program);

        Assert.Equal("Hcoona.DocumentTranslatorCli", programType.Namespace);
        Assert.Equal("document-translator", programType.Assembly.GetName().Name);
    }

    [Theory]
    [InlineData()]
    [InlineData("convert")]
    [InlineData("translate", "--unknown")]
    [InlineData("translate", "--input")]
    public async Task ParseErrorsReturnExitCodeTwoAndDoNotExecute(params string[] args)
    {
        bool executed = false;

        int exitCode = await RunAsync(args, _ => null, _ =>
        {
            executed = true;
            return new ValueTask<int>(Program.SuccessExitCode);
        });

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("translate", "--help")]
    public async Task HelpReturnsSuccess(params string[] args)
    {
        bool executed = false;

        int exitCode = await RunAsync(args, _ => null, _ =>
        {
            executed = true;
            return new ValueTask<int>(Program.SuccessExitCode);
        });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.False(executed);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("translate", "--help")]
    public async Task HelpStdoutDoesNotEndWithExtraBlankLine(params string[] args)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.RunAsync(
            args,
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) => throw new InvalidOperationException("The command should not execute."),
            CancellationToken.None);

        string output = standardOutput.ToString();
        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.EndsWith(Environment.NewLine, output);
        Assert.False(output.EndsWith("\n" + Environment.NewLine, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnvironmentFallbackSuppliesEndpointAuthModeAndApiKey()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.docx", "content");
        string outputPath = directory.GetPath("translated.docx");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            ["translate", "--input", inputPath, "--output", outputPath, "--target-language", "fr"],
            name => name switch
            {
                TranslationOptionResolver.EndpointEnvironmentVariable =>
                    "https://resource.cognitiveservices.azure.com/translator",
                TranslationOptionResolver.AuthModeEnvironmentVariable => "API-KEY",
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "secret",
                _ => null,
            },
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(AuthMode.ApiKey, executedOptions.AuthMode);
        Assert.Equal("secret", executedOptions.ApiKey);
        Assert.Equal(
            "https://resource.cognitiveservices.azure.com",
            executedOptions.Endpoint.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task BlankEnvironmentAuthModeFallsBackToApiKey()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            ["translate", "--input", inputPath, "--output", outputPath, "--target-language", "fr"],
            name => name switch
            {
                TranslationOptionResolver.EndpointEnvironmentVariable =>
                    "https://resource.cognitiveservices.azure.com/translator",
                TranslationOptionResolver.AuthModeEnvironmentVariable => "   ",
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "secret",
                _ => null,
            },
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(AuthMode.ApiKey, executedOptions.AuthMode);
        Assert.Equal("secret", executedOptions.ApiKey);
    }

    [Fact]
    public async Task BlankCommandLineEndpointDoesNotFallBackToEnvironment()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        bool executed = false;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                "fr",
                "--endpoint",
                " ",
                "--key",
                "secret",
            ],
            name => name == TranslationOptionResolver.EndpointEnvironmentVariable
                ? "https://resource.cognitiveservices.azure.com/translator"
                : null,
            _ =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public async Task BlankCommandLineAuthModeDoesNotFallBackToEnvironmentOrDefault()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        bool executed = false;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--auth-mode",
                " ",
                "--key",
                "secret",
            ],
            name => name == TranslationOptionResolver.AuthModeEnvironmentVariable
                ? "entra-id"
                : null,
            _ =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public async Task BlankCommandLineTargetLanguageDoesNotFallBack()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        bool executed = false;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                " ",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
            ],
            _ => null,
            _ =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public async Task AuthModeWhitespaceIsTrimmed()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                "fr",
                "--auth-mode",
                " entra-id ",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
            ],
            name => name == TranslationOptionResolver.ApiKeyEnvironmentVariable
                ? "env-secret"
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(AuthMode.EntraId, executedOptions.AuthMode);
        Assert.Null(executedOptions.ApiKey);
    }

    [Fact]
    public async Task EndpointAndTargetLanguageWhitespaceAreTrimmed()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                " fr ",
                "--key",
                "secret",
            ],
            name => name == TranslationOptionResolver.EndpointEnvironmentVariable
                ? " https://resource.cognitiveservices.azure.com/translator "
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal("fr", executedOptions.TargetLanguage);
        Assert.Equal(
            "https://resource.cognitiveservices.azure.com",
            executedOptions.Endpoint.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task CommandLineValuesOverrideEnvironmentFallback()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                "pt-BR",
                "--auth-mode",
                "entra-id",
                "--endpoint",
                "https://cli.cognitiveservices.azure.com/translator",
                "--key",
                "cli-secret",
            ],
            name => name switch
            {
                TranslationOptionResolver.EndpointEnvironmentVariable =>
                    "https://env.cognitiveservices.azure.com/translator",
                TranslationOptionResolver.AuthModeEnvironmentVariable => "api-key",
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "env-secret",
                _ => null,
            },
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(AuthMode.EntraId, executedOptions.AuthMode);
        Assert.Null(executedOptions.ApiKey);
        Assert.Equal(
            "https://cli.cognitiveservices.azure.com",
            executedOptions.Endpoint.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task ApiKeyModeIsDefaultAndRequiresApiKey()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        bool executed = false;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
            ],
            _ => null,
            _ =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public async Task EntraIdModeDoesNotRequireApiKey()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                inputPath,
                "--output",
                outputPath,
                "--target-language",
                "fr",
                "--auth-mode",
                "ENTRA-ID",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
            ],
            name => name == TranslationOptionResolver.ApiKeyEnvironmentVariable
                ? "env-secret"
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(AuthMode.EntraId, executedOptions.AuthMode);
        Assert.Null(executedOptions.ApiKey);
    }

    [Theory]
    [InlineData("not-a-real-api-key")]
    [InlineData("translate", "--keyy", "not-a-real-api-key")]
    [InlineData("translate", "--keyy=not-a-real-api-key")]
    public async Task ParseErrorsDoNotEchoUnexpectedArgumentValues(params string[] args)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.RunAsync(
            args,
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) => throw new InvalidOperationException("The command should not execute."),
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.DoesNotContain(
            "not-a-real-api-key",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://resource.cognitiveservices.azure.com", true)]
    [InlineData("https://resource.cognitiveservices.azure.com/", true)]
    [InlineData("https://resource.cognitiveservices.azure.com/translator", true)]
    [InlineData("https://resource.cognitiveservices.azure.com:443", true)]
    [InlineData("https://resource.cognitiveservices.azure.com:443/translator", true)]
    [InlineData("http://resource.cognitiveservices.azure.com/translator", false)]
    [InlineData("https://resource.cognitiveservices.azure.com/translator/", false)]
    [InlineData("https://resource.cognitiveservices.azure.com/other", false)]
    [InlineData("https://resource.cognitiveservices.azure.com/translator?api-version=1", false)]
    [InlineData("https://resource.example.com/translator", false)]
    [InlineData("https://cognitiveservices.azure.com/translator", false)]
    [InlineData("https://nested.resource.cognitiveservices.azure.com/translator", false)]
    [InlineData("https://user:password@resource.cognitiveservices.azure.com/translator", false)]
    [InlineData("https://resource.cognitiveservices.azure.com:8443/translator", false)]
    [InlineData("https://-resource.cognitiveservices.azure.com/translator", false)]
    [InlineData("https://resource-.cognitiveservices.azure.com/translator", false)]
    [InlineData("https://resource_name.cognitiveservices.azure.com/translator", false)]
    public void EndpointShapeIsValidated(string endpoint, bool expectedValid)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        RawTranslationOptions options = ValidRawOptions(inputPath, outputPath) with
        {
            Endpoint = endpoint,
        };

        TranslationValidationResult result = TranslationOptionsValidator.Validate(options);

        Assert.Equal(expectedValid, result.Errors.Count == 0);
    }

    [Theory]
    [InlineData("https://resource.cognitiveservices.azure.com")]
    [InlineData("https://resource.cognitiveservices.azure.com/")]
    [InlineData("https://resource.cognitiveservices.azure.com/translator")]
    [InlineData("https://resource.cognitiveservices.azure.com:443")]
    [InlineData("https://resource.cognitiveservices.azure.com:443/translator")]
    public void EndpointIsNormalizedToRoot(string endpoint)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        RawTranslationOptions options = ValidRawOptions(inputPath, outputPath) with
        {
            Endpoint = endpoint,
        };

        TranslationValidationResult result = TranslationOptionsValidator.Validate(options);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Options);
        Assert.Equal(
            "https://resource.cognitiveservices.azure.com",
            result.Options.Endpoint.ToString().TrimEnd('/'));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("fr", true)]
    [InlineData("zh-Hans", true)]
    [InlineData("pt-BR", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("pt BR", false)]
    [InlineData("pt_BR", false)]
    [InlineData("en!", false)]
    public void TargetLanguageSyntaxIsValidated(string targetLanguage, bool expectedValid)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        RawTranslationOptions options = ValidRawOptions(inputPath, outputPath) with
        {
            TargetLanguage = targetLanguage,
        };

        TranslationValidationResult result = TranslationOptionsValidator.Validate(options);

        Assert.Equal(expectedValid, result.Errors.Count == 0);
    }

    [Theory]
    [InlineData("source.TXT", "text/plain")]
    [InlineData("source.tsv", "text/tab-separated-values")]
    [InlineData("source.tab", "text/tab-separated-values")]
    [InlineData("source.csv", "text/csv")]
    [InlineData("source.html", "text/html")]
    [InlineData("source.htm", "text/html")]
    [InlineData("source.mhtml", "message/rfc822")]
    [InlineData("source.mht", "message/rfc822")]
    [InlineData(
        "source.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(
        "source.pptx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("source.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("source.msg", "application/vnd.ms-outlook")]
    [InlineData("source.xlf", "application/xliff+xml")]
    public void SupportedExtensionsResolveContentTypeCaseInsensitively(
        string fileName,
        string expectedContentType)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated" + Path.GetExtension(fileName));

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.Empty(result.Errors);
        Assert.Equal(expectedContentType, result.Options!.ContentType);
        Assert.Equal(fileName, result.Options.OriginalFileName);
    }

    [Theory]
    [InlineData("source.pdf")]
    [InlineData("source.md")]
    [InlineData("source")]
    public void UnsupportedExtensionsFailValidation(string fileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.txt");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ValidationFailuresDoNotExecute()
    {
        bool executed = false;

        int exitCode = await RunAsync(
            [
                "translate",
                "--input",
                "missing.txt",
                "--output",
                "out.txt",
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com",
                "--key",
                "secret",
            ],
            _ => null,
            _ =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
    }

    [Fact]
    public void ExistingOutputRequiresForce()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");

        TranslationValidationResult withoutForce = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));
        TranslationValidationResult withForce = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
            });

        Assert.NotEmpty(withoutForce.Errors);
        Assert.Empty(withForce.Errors);
    }

    [Fact]
    public void OutputPathMustNotBeExistingDirectory()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.CreateSubdirectory("translated");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void InputAndOutputMustNotBeSamePath()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, inputPath) with
            {
                Force = true,
            });

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void FileLargerThanTenMegabytesFailsValidation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.GetPath("source.txt");
        using (FileStream stream = File.Create(inputPath))
        {
            stream.SetLength((10 * 1024 * 1024) + 1);
        }

        string outputPath = directory.GetPath("translated.txt");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task AzureDocumentTranslatorUsesApiKeyClientAndPassesDocumentMetadata()
    {
        using MemoryStream inputStream = new([1, 2, 3]);
        TranslationOptions options = ValidOptions("source.docx") with
        {
            AuthMode = AuthMode.ApiKey,
            ApiKey = "secret",
        };
        CapturingSingleDocumentTranslationClient client = new(BinaryData.FromString("translated"));
        CapturingSingleDocumentTranslationClientFactory factory = new(client);
        AzureDocumentTranslator translator = new(factory);
        using CancellationTokenSource cancellationTokenSource = new();

        BinaryData translatedContent = await translator.TranslateAsync(
            options,
            inputStream,
            cancellationTokenSource.Token);

        Assert.Same(client.TranslatedContent, translatedContent);
        Assert.Equal(options.Endpoint, factory.ApiKeyEndpoint);
        Assert.Equal("secret", factory.ApiKey);
        Assert.False(factory.EntraIdClientCreated);
        Assert.Equal("fr", client.TargetLanguage);
        Assert.Equal(cancellationTokenSource.Token, client.CancellationToken);
        Assert.NotNull(client.Content);
        MultipartFormFileData document = client.Content.MultipartDocument;
        Assert.Equal("source.docx", document.Name);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            document.ContentType);
        Assert.Same(inputStream, document.Content);
    }

    [Fact]
    public async Task AzureDocumentTranslatorUsesEntraIdClientWithoutApiKey()
    {
        using MemoryStream inputStream = new([1, 2, 3]);
        TranslationOptions options = ValidOptions("source.txt") with
        {
            AuthMode = AuthMode.EntraId,
            ApiKey = null,
        };
        CapturingSingleDocumentTranslationClient client = new(BinaryData.FromString("translated"));
        CapturingSingleDocumentTranslationClientFactory factory = new(client);
        AzureDocumentTranslator translator = new(factory);

        await translator.TranslateAsync(options, inputStream, CancellationToken.None);

        Assert.Equal(options.Endpoint, factory.EntraIdEndpoint);
        Assert.True(factory.EntraIdClientCreated);
        Assert.Null(factory.ApiKey);
    }

    [Fact]
    public void AzureClientFactoryCreatesApiKeyAndEntraIdSdkClients()
    {
        AzureSingleDocumentTranslationClientFactory factory = new();
        Uri endpoint = new("https://resource.cognitiveservices.azure.com");

        ISingleDocumentTranslationClient apiKeyClient = factory.CreateApiKeyClient(
            endpoint,
            "secret");
        ISingleDocumentTranslationClient entraIdClient = factory.CreateEntraIdClient(endpoint);

        Assert.NotNull(apiKeyClient);
        Assert.NotNull(entraIdClient);
        Assert.NotSame(apiKeyClient, entraIdClient);
    }

    [Fact]
    public async Task TranslationCommandUsesFakeTranslatorWithValidatedInputMetadata()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.TXT", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using CancellationTokenSource cancellationTokenSource = new();

        BinaryData translatedContent = await TranslationCommand.TranslateValidatedInputAsync(
            options,
            translator,
            cancellationTokenSource.Token);

        Assert.Same(translator.TranslatedContent, translatedContent);
        Assert.Same(options, translator.Options);
        Assert.NotNull(translator.InputStream);
        Assert.True(translator.InputStreamWasReadable);
        TranslationOptions capturedOptions = translator.Options!;
        Assert.Equal("source.TXT", capturedOptions.OriginalFileName);
        Assert.Equal("text/plain", capturedOptions.ContentType);
        Assert.Equal(cancellationTokenSource.Token, translator.CancellationToken);
    }

    [Fact]
    public async Task TranslationCommandHonorsPreCanceledTokenBeforeCallingTranslator()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await TranslationCommand.TranslateValidatedInputAsync(
                options,
                translator,
                cancellationTokenSource.Token));
        Assert.Null(translator.Options);
    }

    private static RawTranslationOptions ValidRawOptions(string inputPath, string outputPath) =>
        new(
            inputPath,
            outputPath,
            "fr",
            "api-key",
            "https://resource.cognitiveservices.azure.com/translator",
            "secret",
            Force: false);

    private static TranslationOptions ValidOptions(string originalFileName) =>
        new(
            "input",
            "output",
            "fr",
            new Uri("https://resource.cognitiveservices.azure.com"),
            AuthMode.ApiKey,
            "secret",
            Force: false,
            originalFileName,
            DocumentTranslationContentTypes.TryGetContentType(
                Path.GetExtension(originalFileName),
                out string contentType)
                ? contentType
                : "text/plain");

    private static async Task<int> RunAsync(
        string[] args,
        Func<string, string?> getEnvironmentVariable,
        Func<TranslationOptions, ValueTask<int>> execute)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        return await Program.RunAsync(
            args,
            standardOutput,
            standardError,
            getEnvironmentVariable,
            (options, _, _) => execute(options),
            CancellationToken.None);
    }

    private sealed class TestDirectory : IDisposable
    {
        private TestDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "document-translator-cli-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TestDirectory(path);
        }

        public string GetPath(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public string CreateSubdirectory(string relativePath)
        {
            string path = GetPath(relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string WriteFile(string relativePath, string content)
        {
            string path = GetPath(relativePath);
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class CapturingSingleDocumentTranslationClient(BinaryData translatedContent)
        : ISingleDocumentTranslationClient
    {
        public BinaryData TranslatedContent { get; } = translatedContent;

        public string? TargetLanguage { get; private set; }

        public DocumentTranslateContent? Content { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<BinaryData> TranslateAsync(
            string targetLanguage,
            DocumentTranslateContent content,
            CancellationToken cancellationToken)
        {
            TargetLanguage = targetLanguage;
            Content = content;
            CancellationToken = cancellationToken;
            return new ValueTask<BinaryData>(TranslatedContent);
        }
    }

    private sealed class CapturingSingleDocumentTranslationClientFactory(
        ISingleDocumentTranslationClient client)
        : ISingleDocumentTranslationClientFactory
    {
        public Uri? ApiKeyEndpoint { get; private set; }

        public string? ApiKey { get; private set; }

        public Uri? EntraIdEndpoint { get; private set; }

        public bool EntraIdClientCreated { get; private set; }

        public ISingleDocumentTranslationClient CreateApiKeyClient(Uri endpoint, string apiKey)
        {
            ApiKeyEndpoint = endpoint;
            ApiKey = apiKey;
            return client;
        }

        public ISingleDocumentTranslationClient CreateEntraIdClient(Uri endpoint)
        {
            EntraIdEndpoint = endpoint;
            EntraIdClientCreated = true;
            return client;
        }
    }

    private sealed class CapturingDocumentTranslator(BinaryData translatedContent)
        : IDocumentTranslator
    {
        public BinaryData TranslatedContent { get; } = translatedContent;

        public TranslationOptions? Options { get; private set; }

        public Stream? InputStream { get; private set; }

        public bool InputStreamWasReadable { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<BinaryData> TranslateAsync(
            TranslationOptions options,
            Stream inputStream,
            CancellationToken cancellationToken)
        {
            Options = options;
            InputStream = inputStream;
            InputStreamWasReadable = inputStream.CanRead;
            CancellationToken = cancellationToken;
            return new ValueTask<BinaryData>(TranslatedContent);
        }
    }
}
