using System.Text.RegularExpressions;

namespace Hcoona.DocumentTranslatorCli;

internal static partial class TranslationOptionsValidator
{
    private const long MaximumInputFileLength = 10 * 1024 * 1024;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static TranslationValidationResult Validate(RawTranslationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> errors = [];

        string? inputPath = ValidateInputPath(options.InputPath, errors);
        string? contentType = ValidateInputExtension(inputPath, errors);
        string? outputPath = ValidateOutputPath(options.OutputPath, options.Force, errors);
        ValidateDifferentInputAndOutput(inputPath, outputPath, errors);
        Uri? endpoint = ValidateEndpoint(options.Endpoint, errors);
        AuthMode? authMode = ValidateAuthMode(options.AuthMode, errors);
        ValidateApiKey(options.ApiKey, authMode, errors);
        string? targetLanguage = ValidateTargetLanguage(options.TargetLanguage, errors);

        if (errors.Count > 0)
        {
            return new TranslationValidationResult(null, errors);
        }

        TranslationOptions validatedOptions = new(
            inputPath!,
            outputPath!,
            targetLanguage!,
            endpoint!,
            authMode!.Value,
            authMode.Value == AuthMode.EntraId ? null : options.ApiKey,
            options.Force,
            Path.GetFileName(inputPath!),
            contentType!);

        return new TranslationValidationResult(validatedOptions, errors);
    }

    private static string? ValidateInputPath(string? inputPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            errors.Add("The --input option is required.");
            return null;
        }

        if (!File.Exists(inputPath))
        {
            errors.Add($"Input file does not exist: {inputPath}");
            return inputPath;
        }

        FileInfo fileInfo = new(inputPath);
        if (fileInfo.Length > MaximumInputFileLength)
        {
            errors.Add("Input file must be no larger than 10 MB.");
        }

        return inputPath;
    }

    private static string? ValidateInputExtension(string? inputPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return null;
        }

        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (!DocumentTranslationContentTypes.TryGetContentType(extension, out string contentType))
        {
            errors.Add($"Unsupported input file extension '{extension}'.");
            return null;
        }

        return contentType;
    }

    private static string? ValidateOutputPath(string? outputPath, bool force, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            errors.Add("The --output option is required.");
            return null;
        }

        if (IsDirectoryLikeOutputPath(outputPath))
        {
            errors.Add("Output path must include a file name.");
        }
        else if (Directory.Exists(outputPath))
        {
            errors.Add("Output path must not be an existing directory.");
        }
        else if (File.Exists(outputPath) && !force)
        {
            errors.Add("Output file already exists. Use --force to replace it.");
        }

        return outputPath;
    }

    internal static bool IsDirectoryLikeOutputPath(string outputPath)
    {
        if (outputPath.Length == 0)
        {
            return true;
        }

        char lastCharacter = outputPath[^1];
        return lastCharacter == '/'
            || (OperatingSystem.IsWindows() && lastCharacter == '\\');
    }

    private static void ValidateDifferentInputAndOutput(
        string? inputPath,
        string? outputPath,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            string fullOutputPath = Path.GetFullPath(outputPath);
            if (PathComparer.Equals(fullInputPath, fullOutputPath))
            {
                errors.Add("Output path must not be the same as the input path.");
            }
        }
        catch (Exception ex) when (ex
            is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            errors.Add("Input and output paths must be valid file paths.");
        }
    }

    private static Uri? ValidateEndpoint(string? endpoint, List<string> errors)
    {
        endpoint = endpoint?.Trim();
        if (endpoint is not null && endpoint.Length == 0)
        {
            errors.Add("The --endpoint option must not be blank.");
            return null;
        }

        if (endpoint is null)
        {
            errors.Add(
                "The --endpoint option or AZURE_TRANSLATOR_ENDPOINT "
                + "environment variable is required.");
            return null;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || !StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !HasSupportedHost(uri.Host)
            || !HasSupportedPath(uri.AbsolutePath))
        {
            errors.Add("Endpoint must match https://<resource-name>.cognitiveservices.azure.com.");
            return null;
        }

        return new UriBuilder(uri.Scheme, uri.Host) { Port = -1 }.Uri;
    }

    private static bool HasSupportedHost(string host)
    {
        const string suffix = ".cognitiveservices.azure.com";
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || host.Length <= suffix.Length)
        {
            return false;
        }

        string resourceName = host[..^suffix.Length];
        return ResourceNameRegex().IsMatch(resourceName);
    }

    private static bool HasSupportedPath(string path) =>
        StringComparer.Ordinal.Equals(path, "/")
        || StringComparer.Ordinal.Equals(path, "/translator");

    private static AuthMode? ValidateAuthMode(string? authMode, List<string> errors)
    {
        authMode = NormalizeNonSecretScalar(authMode);
        if (StringComparer.OrdinalIgnoreCase.Equals(authMode, "api-key"))
        {
            return AuthMode.ApiKey;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(authMode, "entra-id"))
        {
            return AuthMode.EntraId;
        }

        errors.Add("Authentication mode must be 'api-key' or 'entra-id'.");
        return null;
    }

    private static void ValidateApiKey(string? apiKey, AuthMode? authMode, List<string> errors)
    {
        if (authMode == AuthMode.ApiKey && string.IsNullOrWhiteSpace(apiKey))
        {
            errors.Add(
                "The --key option or AZURE_TRANSLATOR_KEY environment variable is "
                + "required for api-key authentication.");
        }
    }

    private static string? ValidateTargetLanguage(string? targetLanguage, List<string> errors)
    {
        targetLanguage = NormalizeNonSecretScalar(targetLanguage);
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            errors.Add("The --target-language option is required.");
            return null;
        }

        if (!LanguageTagRegex().IsMatch(targetLanguage))
        {
            errors.Add("Target language must be a syntactically valid language tag.");
            return null;
        }

        return targetLanguage;
    }

    private static string? NormalizeNonSecretScalar(string? value)
    {
        string? trimmedValue = value?.Trim();
        return string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagRegex();

    [GeneratedRegex(
        "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResourceNameRegex();
}
