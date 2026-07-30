using Azure;
using Azure.AI.Translation.Document;
using Azure.Identity;
using System.Text;
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
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ParseErrorsReturnExitCodeTwoWhenStderrFails(
        Exception standardErrorException)
    {
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);
        bool executed = false;

        int exitCode = await Program.RunAsync(
            ["translate", "--unknown"],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
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

    [Fact]
    public async Task RootHelpReturnsSuccessWhenStdoutFails()
    {
        using ThrowingStringWriter standardOutput = new(new IOException("stdout failed"));
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
            ["--help"],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task TranslateHelpReturnsSuccessWhenStdoutFails()
    {
        using ThrowingStringWriter standardOutput = new(new IOException("stdout failed"));
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
            ["translate", "--help"],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardError.ToString());
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
    public async Task RunAsyncMapsValidatedCommandCancellationToUnexpectedExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        using CancellationTokenSource cancellationTokenSource = new();

        int exitCode = await Program.RunAsync(
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
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, token) =>
            {
                Assert.Equal(cancellationTokenSource.Token, token);
                cancellationTokenSource.Cancel();
                throw new OperationCanceledException(token);
            },
            cancellationTokenSource.Token);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Operation canceled.", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncMarkdownAwarePreCanceledTokenWithExistingOutputReturnsCancellation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.WriteFile("translated.md", "old content");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();
        bool executed = false;

        int exitCode = await Program.RunAsync(
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
                "--key",
                "secret",
                "--markdown-mode",
                "aware",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            cancellationTokenSource.Token);

        string error = standardError.ToString();
        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Operation canceled.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("already exists", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncMapsUnexpectedValidationSeamExceptionToUnexpectedExitCode()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
            [
                "translate",
                "--input",
                "source.txt",
                "--output",
                "translated.txt",
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            _ => throw new InvalidOperationException("validation seam failed"),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Unexpected error", standardError.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "validation seam failed",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncReturnsUnexpectedExitCodeWhenUnexpectedErrorReportingFails()
    {
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(new IOException("stderr failed"));
        bool executed = false;

        int exitCode = await Program.RunAsync(
            [
                "translate",
                "--input",
                "source.txt",
                "--output",
                "translated.txt",
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            _ => throw new InvalidOperationException("validation seam failed"),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task
        EnvironmentFallbackSuppliesEndpointAuthModeApiKeyAndRegionForMarkdownAwareRoute()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "content");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions? executedOptions = null;

        int exitCode = await RunAsync(
            ["translate", "--input", inputPath, "--output", outputPath, "--target-language", "fr"],
            name => name switch
            {
                TranslationOptionResolver.EndpointEnvironmentVariable =>
                    "https://resource.cognitiveservices.azure.com/translator",
                TranslationOptionResolver.AuthModeEnvironmentVariable => "API-KEY",
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "secret",
                TranslationOptionResolver.RegionEnvironmentVariable => " eastus ",
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
        Assert.Equal("eastus", executedOptions.Region);
        Assert.Equal(
            "https://resource.cognitiveservices.azure.com",
            executedOptions.Endpoint.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task CommandLineRegionOverridesEnvironmentForMarkdownAwareApiKeyRoute()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "content");
        string outputPath = directory.GetPath("translated.md");
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
                "--region",
                "westus2",
            ],
            name => name == TranslationOptionResolver.RegionEnvironmentVariable
                ? "eastus"
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(AuthMode.ApiKey, executedOptions.AuthMode);
        Assert.Equal(TranslationRoute.MarkdownAware, executedOptions.TranslationRoute);
        Assert.Equal("westus2", executedOptions.Region);
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
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
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
            standardOutput,
            standardError,
            name => name == TranslationOptionResolver.EndpointEnvironmentVariable
                ? "https://resource.cognitiveservices.azure.com/translator"
                : null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains(
            "The --endpoint option must not be blank.",
            standardError.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TranslationOptionResolver.EndpointEnvironmentVariable,
            standardError.ToString(),
            StringComparison.Ordinal);
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
                "--region",
                "westus2",
            ],
            name => name switch
            {
                TranslationOptionResolver.EndpointEnvironmentVariable =>
                    "https://env.cognitiveservices.azure.com/translator",
                TranslationOptionResolver.AuthModeEnvironmentVariable => "api-key",
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "env-secret",
                TranslationOptionResolver.RegionEnvironmentVariable => "eastus",
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
        Assert.Null(executedOptions.Region);
        Assert.Equal(
            "https://cli.cognitiveservices.azure.com",
            executedOptions.Endpoint.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task BlankCommandLineRegionDoesNotFallBackToEnvironment()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "content");
        string outputPath = directory.GetPath("translated.md");
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
                "--region",
                " ",
            ],
            name => name == TranslationOptionResolver.RegionEnvironmentVariable
                ? "eastus"
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Null(executedOptions.Region);
    }

    [Fact]
    public async Task CommandLineMarkdownModeOverridesEnvironmentFallback()
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
                "--markdown-mode",
                "legacy",
            ],
            name => name == TranslationOptionResolver.MarkdownModeEnvironmentVariable
                ? "aware"
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(MarkdownMode.Legacy, executedOptions.MarkdownMode);
        Assert.Equal(TranslationRoute.LegacyDocument, executedOptions.TranslationRoute);
    }

    [Fact]
    public async Task EnvironmentMarkdownModeOverridesDefault()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "invalid {{ markdown fixture");
        string outputPath = directory.GetPath("translated.md");
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
            ],
            name => name == TranslationOptionResolver.MarkdownModeEnvironmentVariable
                ? " legacy "
                : null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(MarkdownMode.Legacy, executedOptions.MarkdownMode);
        Assert.Equal(TranslationRoute.LegacyDocument, executedOptions.TranslationRoute);
        Assert.Equal("text/plain", executedOptions.LegacyDocumentContentType);
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
    [InlineData(null, true)]
    [InlineData("eastus", true)]
    [InlineData("westus2", true)]
    [InlineData(" eastus ", true)]
    [InlineData("east us", false)]
    [InlineData("-eastus", false)]
    [InlineData("eastus-", false)]
    public void RegionSyntaxIsValidated(string? region, bool expectedValid)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "content");
        string outputPath = directory.GetPath("translated.md");
        RawTranslationOptions options = ValidRawOptions(inputPath, outputPath) with
        {
            Region = region,
        };

        TranslationValidationResult result = TranslationOptionsValidator.Validate(options);

        Assert.Equal(expectedValid, result.Errors.Count == 0);
        if (expectedValid)
        {
            Assert.Equal(region?.Trim(), result.Options!.Region);
        }
    }

    [Fact]
    public async Task LegacyDocumentRouteIgnoresInvalidEnvironmentRegion()
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
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "secret",
                TranslationOptionResolver.RegionEnvironmentVariable => "east us",
                _ => null,
            },
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(TranslationRoute.LegacyDocument, executedOptions.TranslationRoute);
        Assert.Null(executedOptions.Region);
    }

    [Fact]
    public async Task MarkdownAwareApiKeyRouteRejectsInvalidEnvironmentRegion()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "content");
        string outputPath = directory.GetPath("translated.md");
        bool executed = false;

        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        int exitCode = await Program.RunAsync(
            ["translate", "--input", inputPath, "--output", outputPath, "--target-language", "fr"],
            standardOutput,
            standardError,
            name => name switch
            {
                TranslationOptionResolver.EndpointEnvironmentVariable =>
                    "https://resource.cognitiveservices.azure.com/translator",
                TranslationOptionResolver.ApiKeyEnvironmentVariable => "secret",
                TranslationOptionResolver.RegionEnvironmentVariable => "east us",
                _ => null,
            },
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains(
            "Azure Translator region must be a syntactically valid Azure region name.",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EntraIdMarkdownAwareRouteIgnoresRegion()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "content");
        string outputPath = directory.GetPath("translated.md");
        RawTranslationOptions options = ValidRawOptions(inputPath, outputPath) with
        {
            AuthMode = "entra-id",
            Region = "east us",
        };

        TranslationValidationResult result = TranslationOptionsValidator.Validate(options);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Options);
        Assert.Equal(AuthMode.EntraId, result.Options.AuthMode);
        Assert.Equal(TranslationRoute.MarkdownAware, result.Options.TranslationRoute);
        Assert.Null(result.Options.Region);
    }

    [Fact]
    public void LegacyDocumentRouteClearsValidRegion()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.docx", "content");
        string outputPath = directory.GetPath("translated.docx");
        RawTranslationOptions options = ValidRawOptions(inputPath, outputPath) with
        {
            Region = "eastus",
        };

        TranslationValidationResult result = TranslationOptionsValidator.Validate(options);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Options);
        Assert.Equal(TranslationRoute.LegacyDocument, result.Options.TranslationRoute);
        Assert.Null(result.Options.Region);
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
    public void SupportedExtensionsResolveLegacyDocumentContentTypeCaseInsensitively(
        string fileName,
        string expectedLegacyDocumentContentType)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated" + Path.GetExtension(fileName));

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.Empty(result.Errors);
        Assert.Equal(expectedLegacyDocumentContentType, result.Options!.LegacyDocumentContentType);
        Assert.Equal(fileName, result.Options.OriginalFileName);
    }

    [Fact]
    public async Task DefaultMarkdownModeIsAuto()
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
            ],
            _ => null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(MarkdownMode.Auto, executedOptions.MarkdownMode);
        Assert.False(executedOptions.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.LegacyDocument, executedOptions.TranslationRoute);
    }

    [Theory]
    [InlineData("source.md")]
    [InlineData("source.MD")]
    [InlineData("source.MarkDown")]
    [InlineData("archive.tar.markdown")]
    public void MarkdownExtensionsRouteToMarkdownAwareInAuto(string fileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.md");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.Empty(result.Errors);
        Assert.Equal(MarkdownMode.Auto, result.Options!.MarkdownMode);
        Assert.True(result.Options.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.MarkdownAware, result.Options.TranslationRoute);
        Assert.Null(result.Options.LegacyDocumentContentType);
    }

    [Theory]
    [InlineData("source.md")]
    [InlineData("source.markdown")]
    public void AwareMarkdownModeRoutesMarkdownExtensionsToMarkdownAware(string fileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.md");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            });

        Assert.Empty(result.Errors);
        Assert.Equal(MarkdownMode.Aware, result.Options!.MarkdownMode);
        Assert.True(result.Options.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.MarkdownAware, result.Options.TranslationRoute);
        Assert.Null(result.Options.LegacyDocumentContentType);
    }

    [Theory]
    [InlineData("source.txt")]
    [InlineData("source.docx")]
    public void NonMarkdownSupportedFormatsRouteToLegacyDocumentInAuto(string fileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.out");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.Empty(result.Errors);
        Assert.False(result.Options!.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.LegacyDocument, result.Options.TranslationRoute);
    }

    [Fact]
    public void MarkdownParentDirectoryDoesNotAffectAutoRouteForNonMarkdownLeafFile()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(Path.Combine("folder.md", "source.txt"), "content");
        string outputPath = directory.GetPath("translated.txt");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.Empty(result.Errors);
        Assert.False(result.Options!.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.LegacyDocument, result.Options.TranslationRoute);
        Assert.Equal("text/plain", result.Options.LegacyDocumentContentType);
    }

    [Fact]
    public void AwareMarkdownModeRejectsNonMarkdownExtensions()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = " aware ",
            });

        Assert.NotEmpty(result.Errors);
        Assert.Contains(
            "Markdown-aware translation requires a .md or .markdown input file.",
            result.Errors);
    }

    [Theory]
    [InlineData("source.md")]
    [InlineData("source.MARKDOWN")]
    public void LegacyMarkdownModeRoutesMarkdownExtensionsAsTextPlain(string fileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "invalid {{ markdown fixture");
        string outputPath = directory.GetPath("translated.md");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "LEGACY",
            });

        Assert.Empty(result.Errors);
        Assert.Equal(MarkdownMode.Legacy, result.Options!.MarkdownMode);
        Assert.True(result.Options.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.LegacyDocument, result.Options.TranslationRoute);
        Assert.Equal("text/plain", result.Options.LegacyDocumentContentType);
        Assert.Equal(fileName, result.Options.OriginalFileName);
    }

    [Theory]
    [InlineData("source.txt", "text/plain")]
    [InlineData(
        "source.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void LegacyMarkdownModeRoutesNonMarkdownSupportedFormatsToLegacyDocument(
        string fileName,
        string expectedLegacyDocumentContentType)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.out");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "legacy",
            });

        Assert.Empty(result.Errors);
        Assert.Equal(MarkdownMode.Legacy, result.Options!.MarkdownMode);
        Assert.False(result.Options.IsMarkdownExtension);
        Assert.Equal(TranslationRoute.LegacyDocument, result.Options.TranslationRoute);
        Assert.Equal(expectedLegacyDocumentContentType, result.Options.LegacyDocumentContentType);
        Assert.Equal(fileName, result.Options.OriginalFileName);
    }

    [Theory]
    [InlineData("source.pdf")]
    [InlineData("source")]
    public void LegacyMarkdownModeRejectsUnsupportedExtensions(string fileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.txt");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "legacy",
            });

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task InvalidMarkdownModeFailsBeforeTranslation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
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
                "--key",
                "secret",
                "--markdown-mode",
                "sometimes",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains(
            "Markdown mode must be 'auto', 'aware', or 'legacy'.",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyMarkdownModeBypassesMarkdownAwareValidationAndCallsLegacyTranslator()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "invalid {{ markdown fixture");
        string outputPath = directory.GetPath("translated.md");
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
                "--markdown-mode",
                "legacy",
            ],
            _ => null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(TranslationRoute.LegacyDocument, executedOptions.TranslationRoute);
        Assert.Equal("text/plain", executedOptions.LegacyDocumentContentType);
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
    public async Task RunAsyncExecutesValidatedCommandForSupportedExtensions(
        string fileName,
        string expectedLegacyDocumentContentType)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.out");
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
            ],
            _ => null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.Equal(inputPath, executedOptions.InputPath);
        Assert.Equal(outputPath, executedOptions.OutputPath);
        Assert.Equal(fileName, executedOptions.OriginalFileName);
        Assert.Equal(expectedLegacyDocumentContentType, executedOptions.LegacyDocumentContentType);
    }

    [Theory]
    [InlineData("source.pdf")]
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

    [Theory]
    [InlineData("source.pdf", ".pdf")]
    [InlineData("source", "")]
    public async Task RunAsyncRejectsUnsupportedExtensionsBeforeExecution(
        string fileName,
        string expectedExtension)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(fileName, "content");
        string outputPath = directory.GetPath("translated.txt");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
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
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains(
            $"Unsupported input file extension '{expectedExtension}'.",
            standardError.ToString(),
            StringComparison.Ordinal);
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

    [Theory]
    [InlineData(DocumentedValidationCase.MissingInput, "The --input option is required.")]
    [InlineData(DocumentedValidationCase.MissingOutput, "The --output option is required.")]
    [InlineData(DocumentedValidationCase.MissingEndpoint, "AZURE_TRANSLATOR_ENDPOINT")]
    [InlineData(DocumentedValidationCase.MalformedEndpoint, "Endpoint must match")]
    [InlineData(DocumentedValidationCase.InvalidAuthMode, "Authentication mode must be")]
    [InlineData(
        DocumentedValidationCase.MissingTargetLanguage,
        "The --target-language option is required.")]
    [InlineData(
        DocumentedValidationCase.InvalidTargetLanguage,
        "Target language must be a syntactically valid language tag.")]
    [InlineData(DocumentedValidationCase.MissingApiKey, "AZURE_TRANSLATOR_KEY")]
    public async Task DocumentedValidationFailuresReturnExitCodeTwoAndDoNotExecute(
        DocumentedValidationCase validationCase,
        string expectedErrorFragment)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
            BuildDocumentedValidationFailureArgs(validationCase, inputPath, outputPath),
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains(expectedErrorFragment, standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret",
            standardError.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> ValidationFileIoExceptions()
    {
        yield return [new IOException("input metadata failed")];
        yield return [new UnauthorizedAccessException("input metadata denied")];
        yield return [new PathTooLongException("input path too long")];
        yield return [new NotSupportedException("input path format is not supported")];
        yield return [new ArgumentException("input path is invalid")];
    }

    [Theory]
    [MemberData(nameof(ValidationFileIoExceptions))]
    public async Task ValidationFileIoFailuresReturnFileIoExitCode(Exception exception)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool executed = false;

        int exitCode = await Program.RunAsync(
            [
                "translate",
                "--input",
                "source.txt",
                "--output",
                "translated.txt",
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com",
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            _ => throw exception,
            CancellationToken.None);

        string error = standardError.ToString();
        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ValidationFileIoFailuresReturnFileIoExitCodeWhenStderrFails(
        Exception standardErrorException)
    {
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);
        bool executed = false;

        int exitCode = await Program.RunAsync(
            [
                "translate",
                "--input",
                "source.txt",
                "--output",
                "translated.txt",
                "--target-language",
                "fr",
                "--endpoint",
                "https://resource.cognitiveservices.azure.com",
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            _ => throw new IOException("input metadata failed"),
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Theory]
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ValidationFailuresReturnExitCodeTwoWhenStderrFails(
        Exception standardErrorException)
    {
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);
        bool executed = false;

        int exitCode = await Program.RunAsync(
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
            standardOutput,
            standardError,
            _ => null,
            (_, _, _) =>
            {
                executed = true;
                return new ValueTask<int>(Program.SuccessExitCode);
            },
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(executed);
        Assert.Equal(string.Empty, standardOutput.ToString());
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
    public async Task RunAsyncPropagatesForceWhenReplacingExistingOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");
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
                "--endpoint",
                "https://resource.cognitiveservices.azure.com/translator",
                "--key",
                "secret",
                "--force",
            ],
            _ => null,
            options =>
            {
                executedOptions = options;
                return new ValueTask<int>(Program.SuccessExitCode);
            });

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(executedOptions);
        Assert.True(executedOptions.Force);
        Assert.Equal(outputPath, executedOptions.OutputPath);
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
    public async Task DirectoryLikeSlashOutputPathFailsValidationAndDoesNotExecute()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated") + '/';
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
    public async Task DirectoryLikeBackslashOutputPathFailsValidationAndDoesNotExecuteOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated") + '\\';
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
    public void OutputPathWithTrailingBackslashPassesValidationOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated\\");

        TranslationValidationResult result = TranslationOptionsValidator.Validate(
            ValidRawOptions(inputPath, outputPath));

        Assert.Empty(result.Errors);
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
        Assert.Equal("text/plain", capturedOptions.LegacyDocumentContentType);
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

    [Fact]
    public async Task ExecuteValidatedCommandWritesTranslatedOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("translated")),
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Equal("translated", File.ReadAllText(outputPath));
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsTranslatorFailureToServiceExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new RequestFailedException(401, "Denied", "Auth", null)),
            CancellationToken.None);

        Assert.Equal(Program.ServiceErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains(
            "Azure service error (401, Auth)",
            standardError.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret", standardError.ToString(), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> AzureIdentityCredentialFailures()
    {
        yield return
        [
            new AuthenticationFailedException(
                "EnvironmentCredential failed with AZURE_CLIENT_SECRET=secret")
        ];
        yield return
        [
            new CredentialUnavailableException(
                "DefaultAzureCredential unavailable; checked environment secret")
        ];
    }

    [Fact]
    public async Task
        ExecuteValidatedCommandMapsUnexpectedTextTranslationServiceFailureToUnexpectedExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "sensitive segment");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new TextTranslationServiceException(
                "Azure Text Translation service returned malformed JSON.")),
            CancellationToken.None);

        string error = standardError.ToString();
        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Unexpected error", error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Azure Text Translation service error",
            error,
            StringComparison.Ordinal);
        Assert.Contains("malformed JSON", error, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive segment", error, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> StandardErrorOutputChannelFailures()
    {
        yield return [new IOException("stderr failed")];
        yield return [new ObjectDisposedException("stderr")];
        yield return [new InvalidOperationException("stderr failed")];
    }

    [Theory]
    [MemberData(nameof(AzureIdentityCredentialFailures))]
    public async Task ExecuteValidatedCommandMapsAzureIdentityFailuresToServiceExitCode(
        Exception exception)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                AuthMode = "entra-id",
                ApiKey = null,
            })
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(exception),
            CancellationToken.None);

        string error = standardError.ToString();
        Assert.Equal(Program.ServiceErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Azure credential acquisition failed.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("AZURE_CLIENT_SECRET", error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(exception.Message, error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ExecuteValidatedCommandPreservesServiceExitCodeWhenStderrFails(
        Exception standardErrorException)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new RequestFailedException(401, "Denied", "Auth", null)),
            CancellationToken.None);

        Assert.Equal(Program.ServiceErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsInputOpenFailureToFileIoExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.GetPath(Path.Combine("missing", "source.txt"));
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ExecuteValidatedCommandPreservesFileIoExitCodeWhenStderrFails(
        Exception standardErrorException)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.GetPath(Path.Combine("missing", "source.txt"));
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandDoesNotMapTranslatorIoFailureToFileIoExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new IOException("network stream failed")),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Unexpected error", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ExecuteValidatedCommandPreservesUnexpectedExitCodeWhenStderrFails(
        Exception standardErrorException)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("translator failed")),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsOutputIoFailureToFileIoExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string blockingPath = directory.WriteFile("blocking", "not a directory");
        string outputPath = Path.Combine(blockingPath, "translated.txt");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsPreflightTempCreateFailureBeforeTranslation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            _ => throw new IOException("preflight temp create failed"),
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandRejectsExistingOutputDirectoryBeforeTranslation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.CreateSubdirectory("translated.txt");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
        Assert.Contains("existing directory", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandRejectsExistingOutputFileWithoutForce()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            Force = false,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
        Assert.Contains("already exists", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandRejectsExistingOutputBeforeOpeningMissingInput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.GetPath("missing-source.txt");
        string outputPath = directory.WriteFile("translated.txt", "old content");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            Force = false,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            CancellationToken.None);

        string error = standardError.ToString();
        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", error, StringComparison.Ordinal);
        Assert.Contains("already exists", error, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-source.txt", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsSlashDirectoryLikeOutputPathBeforeTranslation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated") + '/';
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsBackslashDirectoryLikeOutputPathOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated") + '\\';
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandAcceptsTrailingBackslashOutputPathOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated\\");
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(translator.Options);
        Assert.True(outputWriterCalled);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsForceReplaceabilityFailureOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
            })
            .Options!;
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;
        string? checkedPath = null;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            (_, _, _, _) =>
            {
                outputWriterCalled = true;
                return ValueTask.CompletedTask;
            },
            CreatePreflightTempFile,
            path =>
            {
                checkedPath = path;
                throw new UnauthorizedAccessException("existing output cannot be replaced");
            },
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.False(outputWriterCalled);
        Assert.Equal(Path.GetFullPath(outputPath), checkedPath);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PreflightOutputPathCreatesAndDeletesTempFileInOutputDirectory()
    {
        using TestDirectory directory = TestDirectory.Create();
        string outputPath = directory.GetPath(Path.Combine("nested", "translated.txt"));
        string expectedOutputDirectory = System.IO.Path.GetDirectoryName(outputPath)!;
        string? preflightTempPath = null;

        Program.PreflightOutputPath(
            outputPath,
            tempPath =>
            {
                preflightTempPath = tempPath;
                Assert.Equal(expectedOutputDirectory, System.IO.Path.GetDirectoryName(tempPath));
                return new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
            });

        Assert.NotNull(preflightTempPath);
        Assert.False(File.Exists(preflightTempPath));
        Assert.False(File.Exists(outputPath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(expectedOutputDirectory),
            path => System.IO.Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void PreflightOutputPathChecksExistingOutputReplaceabilityOnlyWhenForcedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string outputPath = directory.WriteFile("translated.txt", "old content");
        bool checkedWithoutForce = false;

        IOException existingOutputException = Assert.Throws<IOException>(
            () => Program.PreflightOutputPath(
                outputPath,
                force: false,
                CreatePreflightTempFile,
                _ =>
                {
                    checkedWithoutForce = true;
                    throw new IOException("should not be checked");
                }));

        Assert.Contains(
            "already exists",
            existingOutputException.Message,
            StringComparison.Ordinal);
        Assert.False(checkedWithoutForce);
        Assert.Equal("old content", File.ReadAllText(outputPath));

        UnauthorizedAccessException exception = Assert.Throws<UnauthorizedAccessException>(
            () => Program.PreflightOutputPath(
                outputPath,
                force: true,
                CreatePreflightTempFile,
                _ => throw new UnauthorizedAccessException("locked")));
        Assert.Equal("locked", exception.Message);
        Assert.Equal("old content", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task ExecuteValidatedCommandDoesNotReadUnreadableExistingOutputOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
            })
            .Options!;
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool outputWriterCalled = false;

        try
        {
            File.SetUnixFileMode(outputPath, UnixFileMode.None);

            int exitCode = await Program.ExecuteValidatedCommandAsync(
                options,
                standardOutput,
                standardError,
                translator,
                (_, _, _, _) =>
                {
                    outputWriterCalled = true;
                    return ValueTask.CompletedTask;
                },
                CreatePreflightTempFile,
                _ => throw new UnauthorizedAccessException("should not read existing output"),
                CancellationToken.None);

            Assert.Equal(Program.SuccessExitCode, exitCode);
            Assert.NotNull(translator.Options);
            Assert.True(outputWriterCalled);
            Assert.Equal(string.Empty, standardError.ToString());
        }
        finally
        {
            File.SetUnixFileMode(
                outputPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void PreflightOutputPathReplaceabilityCheckDoesNotModifyExistingOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string outputPath = directory.WriteFile("translated.txt", "old content");

        Program.PreflightOutputPath(
            outputPath,
            force: true,
            CreatePreflightTempFile,
            path => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));

        Assert.Equal("old content", File.ReadAllText(outputPath));
    }

    [Fact]
    public void PreflightOutputPathRejectsReadOnlyExistingOutputOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string outputPath = directory.WriteFile("translated.txt", "old content");

        try
        {
            File.SetAttributes(
                outputPath,
                File.GetAttributes(outputPath) | FileAttributes.ReadOnly);

            UnauthorizedAccessException exception =
                Assert.Throws<UnauthorizedAccessException>(
                    () => Program.PreflightOutputPath(
                        outputPath,
                        force: true,
                        CreatePreflightTempFile));

            Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("old content", File.ReadAllText(outputPath));
        }
        finally
        {
            File.SetAttributes(
                outputPath,
                File.GetAttributes(outputPath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void PreflightOutputPathAllowsReadOnlyExistingOutputOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = TestDirectory.Create();
        string outputPath = directory.WriteFile("translated.txt", "old content");

        try
        {
            File.SetAttributes(
                outputPath,
                File.GetAttributes(outputPath) | FileAttributes.ReadOnly);

            Program.PreflightOutputPath(outputPath, force: true, CreatePreflightTempFile);

            Assert.Equal("old content", File.ReadAllText(outputPath));
        }
        finally
        {
            File.SetAttributes(
                outputPath,
                File.GetAttributes(outputPath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsInvalidOutputPathBeforeTranslation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath =
            directory.Path + Path.DirectorySeparatorChar + "invalid\0translated.txt";
        TranslationOptions options = ValidOptions("source.txt") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        CapturingDocumentTranslator translator = new(BinaryData.FromString("translated"));
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            translator,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Null(translator.Options);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", standardError.ToString(), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> OutputFileIoExceptions()
    {
        yield return [new IOException("disk failed")];
        yield return [new DriveNotFoundException("drive missing")];
        yield return [new UnauthorizedAccessException("access denied")];
        yield return [new PathTooLongException("path too long")];
        yield return [new NotSupportedException("path format is not supported")];
    }

    [Theory]
    [MemberData(nameof(OutputFileIoExceptions))]
    public async Task ExecuteValidatedCommandMapsInjectedOutputFileIoFailuresToFileIoExitCode(
        Exception exception)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("translated")),
            (_, _, _, _) => throw exception,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsOutputCancellationToUnexpectedExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("translated")),
            (_, _, _, _) => throw new OperationCanceledException(),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Operation canceled.", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsUnexpectedOutputFailureToUnexpectedExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("translated")),
            (_, _, _, _) => throw new InvalidOperationException("writer failed"),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Unexpected error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandReportsSuccessAfterCommitDespiteCallerCancellation()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using CancellationTokenSource cancellationTokenSource = new();
        using CancelSensitiveStringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("translated")),
            (_, _, _, _) =>
            {
                cancellationTokenSource.Cancel();
                return ValueTask.CompletedTask;
            },
            cancellationTokenSource.Token);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandIgnoresStatusMessageIoFailureAfterOutputCommit()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using ThrowingStringWriter standardOutput = new(new IOException("stdout failed"));
        using StringWriter standardError = new();
        bool outputCommitted = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("translated")),
            (_, _, _, _) =>
            {
                outputCommitted = true;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.True(outputCommitted);
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.DoesNotContain(
            "File I/O error",
            standardError.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteValidatedCommandMapsCancellationAndLeavesNoOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new OperationCanceledException()),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Operation canceled.", standardError.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StandardErrorOutputChannelFailures))]
    public async Task ExecuteValidatedCommandPreservesCancellationExitCodeWhenStderrFails(
        Exception standardErrorException)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.GetPath("translated.txt");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath))
            .Options!;
        using StringWriter standardOutput = new();
        using ThrowingStringWriter standardError = new(standardErrorException);

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new OperationCanceledException()),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task ExecuteValidatedCommandPreservesExistingOutputWhenTranslationFails()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
            })
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new RequestFailedException(503, "Unavailable")),
            CancellationToken.None);

        Assert.Equal(Program.ServiceErrorExitCode, exitCode);
        Assert.Equal("old content", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task ExecuteValidatedCommandOverwritesExistingOutputOnlyAfterSuccess()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.txt", "content");
        string outputPath = directory.WriteFile("translated.txt", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
            })
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new CapturingDocumentTranslator(BinaryData.FromString("new content")),
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Equal("new content", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task AtomicOutputWriterUsesSameDirectoryTempFileAndCleansItOnMoveFailure()
    {
        using TestDirectory directory = TestDirectory.Create();
        string outputPath = directory.WriteFile("translated.txt", "old content");

        await Assert.ThrowsAsync<IOException>(
            async () => await AtomicOutputWriter.WriteAsync(
                outputPath,
                BinaryData.FromString("new content"),
                overwrite: false,
                CancellationToken.None));

        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.Path),
            path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkdownAwareRouteRunsPipelineAndWritesTranslatedMarkdown()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "# Hello\r\n\r\nWorld\r\n");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using CancellationTokenSource cancellationTokenSource = new();
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            cancellationTokenSource.Token);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Equal(2, textTranslator.Requests.Count);
        Assert.Equal(cancellationTokenSource.Token, textTranslator.CancellationToken);
        Assert.Equal("# fr:Hello\r\n\r\nfr:World\r\n", File.ReadAllText(outputPath));
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Theory]
    [InlineData("source.md")]
    [InlineData("source.markdown")]
    public async Task AutoMarkdownExtensionDispatchesToMarkdownAwarePipeline(
        string inputFileName)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile(inputFileName, "# Hello\r\n\r\nWorld\r\n");
        string outputPath = directory.GetPath("translated.md");
        CapturingDocumentTranslator documentTranslator = new(
            BinaryData.FromString("legacy translated"));
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        TranslationOptions? capturedOptions = null;

        int exitCode = await Program.RunAsync(
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
                "--key",
                "secret",
            ],
            standardOutput,
            standardError,
            _ => null,
            async (options, output, token) =>
            {
                capturedOptions = options;
                return await Program.ExecuteValidatedCommandAsync(
                        options,
                        output,
                        standardError,
                        documentTranslator,
                        textTranslator,
                        token)
                    .ConfigureAwait(false);
            },
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(capturedOptions);
        Assert.Equal(MarkdownMode.Auto, capturedOptions.MarkdownMode);
        Assert.Equal(TranslationRoute.MarkdownAware, capturedOptions.TranslationRoute);
        Assert.Equal(2, textTranslator.Requests.Count);
        Assert.Null(documentTranslator.Options);
        Assert.Equal("# fr:Hello\r\n\r\nfr:World\r\n", File.ReadAllText(outputPath));
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task LegacyMarkdownModeDispatchesToDocumentTranslator()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "# Hello\r\n\r\nWorld\r\n");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "legacy",
            })
            .Options!;
        CapturingDocumentTranslator documentTranslator = new(
            BinaryData.FromString("legacy translated"));
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            documentTranslator,
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Equal(MarkdownMode.Legacy, options.MarkdownMode);
        Assert.Equal(TranslationRoute.LegacyDocument, options.TranslationRoute);
        Assert.NotNull(documentTranslator.Options);
        Assert.Empty(textTranslator.Requests);
        Assert.Equal("legacy translated", File.ReadAllText(outputPath));
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task MarkdownAwareOutputWriterReceivesCallerCancellationToken()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using CancellationTokenSource cancellationTokenSource = new();
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        CancellationToken capturedToken = default;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            async (path, content, overwrite, token) =>
            {
                Assert.False(overwrite);
                capturedToken = token;
                await File.WriteAllBytesAsync(path, content.ToArray(), token).ConfigureAwait(false);
            },
            cancellationTokenSource.Token);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Equal(cancellationTokenSource.Token, capturedToken);
        Assert.Equal("fr:Hello", File.ReadAllText(outputPath));
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task MarkdownAwareIgnoresStatusMessageIoFailureAfterOutputCommit()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using ThrowingStringWriter standardOutput = new(new IOException("stdout failed"));
        using StringWriter standardError = new();
        bool outputCommitted = false;

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            async (path, content, overwrite, token) =>
            {
                Assert.False(overwrite);
                outputCommitted = true;
                await File.WriteAllBytesAsync(path, content.ToArray(), token).ConfigureAwait(false);
            },
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.True(outputCommitted);
        Assert.Equal("fr:Hello", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task RunAsyncMarkdownAwareForcePassesOverwriteTrueToOutputWriterForExistingOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.WriteFile("translated.md", "old content");
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        bool? capturedOverwrite = null;
        TranslationOptions? capturedOptions = null;

        int exitCode = await Program.RunAsync(
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
                "--key",
                "secret",
                "--markdown-mode",
                "aware",
                "--force",
            ],
            standardOutput,
            standardError,
            _ => null,
            async (options, output, token) =>
            {
                capturedOptions = options;
                return await Program.ExecuteValidatedCommandAsync(
                        options,
                        output,
                        standardError,
                        new ThrowingDocumentTranslator(
                            new InvalidOperationException("legacy called")),
                        textTranslator,
                        async (path, content, overwrite, writerToken) =>
                        {
                            capturedOverwrite = overwrite;
                            if (!overwrite)
                            {
                                throw new IOException("Overwrite was not requested.");
                            }

                            await File
                                .WriteAllBytesAsync(path, content.ToArray(), writerToken)
                                .ConfigureAwait(false);
                        },
                        token)
                    .ConfigureAwait(false);
            },
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.Force);
        Assert.True(capturedOverwrite);
        Assert.Equal("fr:Hello", File.ReadAllText(outputPath));
        Assert.Contains(
            "Translation completed.",
            standardOutput.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardError.ToString());
    }

    [Fact]
    public async Task MarkdownAwareZeroSegmentsWritesOriginalWithoutCallingBackend()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.GetPath("source.md");
        string outputPath = directory.GetPath("translated.md");
        byte[] inputBytes =
        [
            0xEF,
            0xBB,
            0xBF,
            .. Encoding.UTF8.GetBytes("```text\r\nunchanged\r\n```\r\n"),
        ];
        File.WriteAllBytes(inputPath, inputBytes);
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.SuccessExitCode, exitCode);
        Assert.Empty(textTranslator.Requests);
        Assert.Equal(inputBytes, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public async Task MarkdownAwareValidationFailureDoesNotWriteOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        ReplacingTextSegmentTranslator textTranslator = new("# Broken");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Contains("Markdown-aware", standardError.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task MarkdownAwareTranslatorCancellationWinsOverValidationFailure()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        using CancellationTokenSource cancellationTokenSource = new();
        CancelingReplacingTextSegmentTranslator textTranslator = new(
            "# Broken",
            cancellationTokenSource);
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            cancellationTokenSource.Token);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Contains("Operation canceled.", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Markdown-aware", standardError.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, standardOutput.ToString());
    }

    [Fact]
    public async Task MarkdownAwareExistingOutputPreflightFailsBeforeReadingInputOrCallingBackend()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.CreateSubdirectory("source.md");
        string outputPath = directory.WriteFile("translated.md", "old content");
        TranslationOptions options = ValidOptions("source.md") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            MarkdownMode = MarkdownMode.Aware,
            TranslationRoute = TranslationRoute.MarkdownAware,
        };
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Empty(textTranslator.Requests);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("already exists", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("source.md", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.Path),
            path => System.IO.Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkdownAwarePatchFailurePreservesExistingOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.WriteFile("translated.md", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
                MarkdownMode = "aware",
            })
            .Options!;
        NullTextSegmentTranslator textTranslator = new();
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.Equal("old content", File.ReadAllText(outputPath));
    }

    [Theory]
    [MemberData(nameof(MarkdownAwareServiceFailures))]
    public async Task MarkdownAwareServiceFailuresReturnServiceExitCodeWithoutLeakingDetails(
        Exception exception,
        string expectedError)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.WriteFile("translated.md", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
                MarkdownMode = "aware",
            })
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            new ThrowingTextSegmentTranslator(exception),
            CancellationToken.None);

        Assert.Equal(Program.ServiceErrorExitCode, exitCode);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Equal(expectedError + Environment.NewLine, standardError.ToString());
        if (exception is TextTranslationServiceException)
        {
            Assert.Contains(
                exception.Message,
                standardError.ToString(),
                StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(
                exception.Message,
                standardError.ToString(),
                StringComparison.Ordinal);
        }
        Assert.DoesNotContain(
            "secret",
            standardError.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(MarkdownAwareCredentialFailures))]
    public async Task
        MarkdownAwareCredentialFailuresReturnFixedCredentialErrorWithoutLeakingDetails(
        Exception exception)
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.WriteFile("translated.md", "old content");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                Force = true,
                MarkdownMode = "aware",
                AuthMode = "entra-id",
                ApiKey = null,
            })
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            new ThrowingTextSegmentTranslator(exception),
            CancellationToken.None);

        string error = standardError.ToString();
        Assert.Equal(Program.ServiceErrorExitCode, exitCode);
        Assert.Equal("old content", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Equal(
            "Error: Azure credential acquisition failed." + Environment.NewLine,
            error);
        Assert.DoesNotContain(exception.Message, error, StringComparison.Ordinal);
        Assert.DoesNotContain("AZURE_CLIENT_SECRET", error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarkdownAwareParseFailureDoesNotCallBackend()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "{\"title\":\"unsupported\"}");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.ValidationErrorExitCode, exitCode);
        Assert.Empty(textTranslator.Requests);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task
        MarkdownAwareInputReadFileIoFailureReturnsFileIoExitCodeWithoutCallingBackend()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.CreateSubdirectory("source.md");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = ValidOptions("source.md") with
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            MarkdownMode = MarkdownMode.Aware,
            TranslationRoute = TranslationRoute.MarkdownAware,
        };
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.Empty(textTranslator.Requests);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownAwareOutputWriterIoFailureReturnsFileIoExitCodeWithoutFinalOutput()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        PrefixingTextSegmentTranslator textTranslator = new("fr:");
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            (_, _, _, _) => throw new IOException("writer failed"),
            CancellationToken.None);

        Assert.Equal(Program.FileIoErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownAwareTextSegmentTranslatorIoFailureReturnsUnexpectedExitCode()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            new ThrowingTextSegmentTranslator(new IOException("network stream failed")),
            CancellationToken.None);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Unexpected error", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("File I/O error", standardError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        MarkdownAwarePostTranslationCancellationReturnsUnexpectedExitCodeWithoutOutputOrTempFile()
    {
        using TestDirectory directory = TestDirectory.Create();
        string inputPath = directory.WriteFile("source.md", "Hello");
        string outputPath = directory.GetPath("translated.md");
        TranslationOptions options = TranslationOptionsValidator
            .Validate(ValidRawOptions(inputPath, outputPath) with
            {
                MarkdownMode = "aware",
            })
            .Options!;
        using CancellationTokenSource cancellationTokenSource = new();
        CancelingPrefixingTextSegmentTranslator textTranslator = new(
            "fr:",
            cancellationTokenSource);
        bool outputWriterCalled = false;
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await Program.ExecuteValidatedCommandAsync(
            options,
            standardOutput,
            standardError,
            new ThrowingDocumentTranslator(new InvalidOperationException("legacy called")),
            textTranslator,
            async (path, content, _, token) =>
            {
                outputWriterCalled = true;
                token.ThrowIfCancellationRequested();
                await File.WriteAllBytesAsync(path, content.ToArray(), token).ConfigureAwait(false);
            },
            cancellationTokenSource.Token);

        Assert.Equal(Program.UnexpectedErrorExitCode, exitCode);
        Assert.Equal(cancellationTokenSource.Token, textTranslator.CancellationToken);
        Assert.True(textTranslator.CancellationToken.IsCancellationRequested);
        Assert.False(outputWriterCalled);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("Operation canceled.", standardError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.Path),
            path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
    }

    private static RawTranslationOptions ValidRawOptions(string inputPath, string outputPath) =>
        new(
            inputPath,
            outputPath,
            "fr",
            "api-key",
            "https://resource.cognitiveservices.azure.com/translator",
            "secret",
            "auto",
            Force: false);

    private static TranslationOptions ValidOptions(string originalFileName) =>
        new(
            "input",
            "output",
            "fr",
            new Uri("https://resource.cognitiveservices.azure.com"),
            AuthMode.ApiKey,
            "secret",
            MarkdownMode.Auto,
            TranslationRoute.LegacyDocument,
            TranslationOptionsValidator.IsMarkdownExtension(originalFileName),
            Force: false,
            originalFileName,
            LegacyDocumentContentTypes.TryGetContentType(
                Path.GetExtension(originalFileName),
                out string contentType)
                ? contentType
                : "text/plain");

    private static string[] BuildDocumentedValidationFailureArgs(
        DocumentedValidationCase validationCase,
        string inputPath,
        string outputPath)
    {
        string? effectiveInputPath = validationCase == DocumentedValidationCase.MissingInput
            ? null
            : inputPath;
        string? effectiveOutputPath = validationCase == DocumentedValidationCase.MissingOutput
            ? null
            : outputPath;
        string? effectiveTargetLanguage = validationCase switch
        {
            DocumentedValidationCase.MissingTargetLanguage => null,
            DocumentedValidationCase.InvalidTargetLanguage => "pt_BR",
            _ => "fr",
        };
        string? effectiveEndpoint = validationCase switch
        {
            DocumentedValidationCase.MissingEndpoint => null,
            DocumentedValidationCase.MalformedEndpoint => "https://example.com",
            _ => "https://resource.cognitiveservices.azure.com",
        };
        string effectiveAuthMode = validationCase == DocumentedValidationCase.InvalidAuthMode
            ? "managed-identity"
            : "api-key";
        string? effectiveApiKey = validationCase == DocumentedValidationCase.MissingApiKey
            ? null
            : "secret";

        List<string> args = ["translate"];
        AddOption(args, "--input", effectiveInputPath);
        AddOption(args, "--output", effectiveOutputPath);
        AddOption(args, "--target-language", effectiveTargetLanguage);
        AddOption(args, "--endpoint", effectiveEndpoint);
        AddOption(args, "--auth-mode", effectiveAuthMode);
        AddOption(args, "--key", effectiveApiKey);
        return [.. args];
    }

    public static IEnumerable<object[]> MarkdownAwareServiceFailures()
    {
        yield return
        [
            new TextTranslationServiceException(
                "Azure Text Translation service returned malformed JSON."),
            "Error: Azure Text Translation service error: "
                + "Azure Text Translation service returned malformed JSON.",
        ];
        yield return
        [
            new RequestFailedException(
                503,
                "service unavailable with secret-token",
                "Unavailable",
                null),
            "Error: Azure service error (503, Unavailable).",
        ];
    }

    public static IEnumerable<object[]> MarkdownAwareCredentialFailures()
    {
        yield return
        [
            new CredentialUnavailableException(
                "DefaultAzureCredential unavailable; checked AZURE_CLIENT_SECRET=secret-token")
        ];
        yield return
        [
            new AuthenticationFailedException(
                "EnvironmentCredential failed with AZURE_CLIENT_SECRET=secret-token")
        ];
    }

    private static void AddOption(List<string> args, string optionName, string? value)
    {
        if (value is null)
        {
            return;
        }

        args.Add(optionName);
        args.Add(value);
    }

    public enum DocumentedValidationCase
    {
        MissingInput,
        MissingOutput,
        MissingEndpoint,
        MalformedEndpoint,
        InvalidAuthMode,
        MissingTargetLanguage,
        InvalidTargetLanguage,
        MissingApiKey,
    }

    private static FileStream CreatePreflightTempFile(string tempPath) =>
        new(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

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

    private sealed class CancelSensitiveStringWriter : StringWriter
    {
        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return base.WriteLineAsync(buffer, cancellationToken);
        }
    }

    private sealed class ThrowingStringWriter(Exception exception) : StringWriter
    {
        public override Task WriteLineAsync(
            ReadOnlyMemory<char> buffer,
            CancellationToken cancellationToken = default) =>
            Task.FromException(exception);
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

    private sealed class ThrowingDocumentTranslator(Exception exception)
        : IDocumentTranslator
    {
        public ValueTask<BinaryData> TranslateAsync(
            TranslationOptions options,
            Stream inputStream,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }

    private sealed class PrefixingTextSegmentTranslator(string prefix) : ITextSegmentTranslator
    {
        public List<TextSegmentTranslationRequest> Requests { get; } = [];

        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Requests.AddRange(segments);
            IReadOnlyList<string> translations = segments
                .OrderBy(static segment => segment.SegmentIndex)
                .Select(segment => prefix + segment.Text)
                .ToArray();
            return new ValueTask<IReadOnlyList<string>>(translations);
        }
    }

    private sealed class CancelingPrefixingTextSegmentTranslator(
        string prefix,
        CancellationTokenSource cancellationTokenSource)
        : ITextSegmentTranslator
    {
        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            cancellationTokenSource.Cancel();
            IReadOnlyList<string> translations = segments
                .OrderBy(static segment => segment.SegmentIndex)
                .Select(segment => prefix + segment.Text)
                .ToArray();
            return new ValueTask<IReadOnlyList<string>>(translations);
        }
    }

    private sealed class ReplacingTextSegmentTranslator(string replacement) : ITextSegmentTranslator
    {
        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> translations = segments.Select(_ => replacement).ToArray();
            return new ValueTask<IReadOnlyList<string>>(translations);
        }
    }

    private sealed class CancelingReplacingTextSegmentTranslator(
        string replacement,
        CancellationTokenSource cancellationTokenSource)
        : ITextSegmentTranslator
    {
        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            cancellationTokenSource.Cancel();
            IReadOnlyList<string> translations = segments.Select(_ => replacement).ToArray();
            return new ValueTask<IReadOnlyList<string>>(translations);
        }
    }

    private sealed class NullTextSegmentTranslator : ITextSegmentTranslator
    {
        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> translations = [null!];
            return new ValueTask<IReadOnlyList<string>>(translations);
        }
    }

    private sealed class ThrowingTextSegmentTranslator(Exception exception) : ITextSegmentTranslator
    {
        public ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }
}
