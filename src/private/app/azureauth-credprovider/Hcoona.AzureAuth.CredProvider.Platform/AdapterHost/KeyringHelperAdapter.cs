using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class KeyringHelperAdapter
{
    public const string ProductExecutableName = "azureauth-credprovider";

    private const string ArtifactsKeyringNonInteractiveEnvironmentVariable =
        "ARTIFACTS_KEYRING_NONINTERACTIVE_MODE";
    private const string DefaultServiceIdentity = "default";
    private const string ProtocolViolationCode = "ProtocolViolation";
    private const string ProductNoUserEnvironmentVariable = "AZUREAUTH_NO_USER";
    private const string UnsupportedHostCode = "UnsupportedHost";

    private readonly BoundedCredentialAcquisitionAdapter credentialAcquisition;
    private readonly Func<string, string?> environmentVariableReader;

    public KeyringHelperAdapter()
        : this(
            CredentialProviderCompositionRoot.CreateProduction().AcquisitionService,
            Environment.GetEnvironmentVariable
        )
    { }

    public KeyringHelperAdapter(
        CredentialCoreService? credentialCore,
        Func<string, string?>? environmentVariableReader = null
    )
        : this(
            credentialCore is null
                ? CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                : new LegacyV1CredentialAcquisitionService(credentialCore),
            environmentVariableReader
        )
    { }

    public KeyringHelperAdapter(
        ICredentialAcquisitionService credentialAcquisition,
        Func<string, string?>? environmentVariableReader = null
    )
        : this(
            new BoundedCredentialAcquisitionAdapter(credentialAcquisition),
            environmentVariableReader
        )
    { }

    public KeyringHelperAdapter(
        BoundedCredentialAcquisitionAdapter credentialAcquisition,
        Func<string, string?>? environmentVariableReader = null
    )
    {
        ArgumentNullException.ThrowIfNull(credentialAcquisition);
        this.credentialAcquisition = credentialAcquisition;
        this.environmentVariableReader =
            environmentVariableReader ?? Environment.GetEnvironmentVariable;
    }

    public static AdapterDescriptor Descriptor { get; } = CreateDescriptor();

    public static bool TryResolveProtocolInvocation(
        string? executablePath,
        IEnumerable<string>? arguments,
        out AdapterInvocationContext? context
    )
    {
        bool resolved = AdapterHostBootstrap.TryResolveInvocation(
            Descriptor,
            executablePath,
            arguments,
            out context
        );
        if (!resolved || context is null || !context.IsProtocolInvocation)
        {
            context = null;
            return false;
        }

        return true;
    }

    internal static bool TryNormalizeServiceForKeyringCli(
        string serviceText,
        [NotNullWhen(true)] out string? normalizedServiceText
    )
    {
        normalizedServiceText = serviceText;
        if (!Uri.TryCreate(serviceText, UriKind.Absolute, out Uri? service))
        {
            return true;
        }

        if (!IsAzureArtifactsHost(service.IdnHost))
        {
            return true;
        }

        if (
            !IsValidAzureKeyringServiceSyntax(service)
            || !TryGetRawPathSegments(
                serviceText,
                out string[]? segments,
                out bool hasTrailingSlash
            )
        )
        {
            normalizedServiceText = null;
            return false;
        }

        const int PythonDownloadTailSegmentCount = 5;
        int pypiIndex = segments.Length - PythonDownloadTailSegmentCount;
        if (
            pypiIndex < 2
            || !IsSegment(segments[pypiIndex - 2], "_packaging")
            || !IsSegment(segments[pypiIndex], "pypi")
            || !IsSegment(segments[pypiIndex + 1], "download")
        )
        {
            return true;
        }

        if (
            hasTrailingSlash
            || segments[(pypiIndex + 2)..].Any(string.IsNullOrWhiteSpace)
        )
        {
            normalizedServiceText = null;
            return false;
        }

        var normalizedServiceBuilder = new UriBuilder(service)
        {
            Path =
                "/"
                + string.Join(
                    "/",
                    segments[..(pypiIndex + 1)].Select(Uri.EscapeDataString)
                )
                + "/simple/",
        };
        Uri normalizedService = normalizedServiceBuilder.Uri;
        if (
            ClassifyPythonFeedResource(normalizedService, out _)
            != PythonFeedResourceClassification.Supported
        )
        {
            normalizedServiceText = null;
            return false;
        }

        normalizedServiceText = normalizedService.AbsoluteUri;
        return true;
    }

    public AdapterHostExecutionOutcome Execute(
        string? executablePath,
        IEnumerable<string>? arguments,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter
    )
    {
        ArgumentNullException.ThrowIfNull(protocolStdout);
        ArgumentNullException.ThrowIfNull(humanStdout);
        ArgumentNullException.ThrowIfNull(diagnosticRouter);

        return AdapterHostExecutor.Execute(
            Descriptor,
            executablePath,
            arguments,
            Handle,
            protocolStdout,
            humanStdout,
            diagnosticRouter
        );
    }

    private static AdapterDescriptor CreateDescriptor()
    {
        AdapterEntrypointDescriptor sharedCliEntrypoint = new(
            "KeyringHelper",
            AdapterInvocationMode.Protocol,
            executableNames: [ProductExecutableName],
            argumentTokens: [KeyringHelperV2.CommandName],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix
        );
        AdapterEntrypointDescriptor helperExecutableEntrypoint = new(
            "KeyringHelperExecutable",
            AdapterInvocationMode.Protocol,
            executableNames: [KeyringHelperV2.CommandName]
        );
        AdapterEntrypointDescriptor helperExecutablePrefixedEntrypoint = new(
            "KeyringHelperExecutablePrefixed",
            AdapterInvocationMode.Protocol,
            executableNames: [KeyringHelperV2.CommandName],
            argumentTokens: [KeyringHelperV2.CommandName],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix
        );
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: [ProductExecutableName]
        );

        return new AdapterDescriptor(
            "Keyring Helper v2",
            AdapterProtocol.KeyringHelper,
            [
                sharedCliEntrypoint,
                helperExecutablePrefixedEntrypoint,
                helperExecutableEntrypoint,
                humanEntrypoint,
            ]
        );
    }

    private AdapterHostHandlerOutput Handle(AdapterInvocationContext context)
    {
        if (!TryParseRequest(context.PayloadArguments, out KeyringHelperRequest? helperRequest))
        {
            return CreateProtocolViolationOutput();
        }

        PythonFeedResourceClassification resourceClassification = ClassifyPythonFeedResource(
            helperRequest.Service,
            out CanonicalResourceIdentity? resource
        );
        if (resourceClassification == PythonFeedResourceClassification.UnsupportedHost)
        {
            return CreateNoCredentialOutput();
        }

        if (
            resourceClassification != PythonFeedResourceClassification.Supported
            || resource is null
        )
        {
            return CreateProtocolViolationOutput();
        }

        CredentialRequestV2 credentialRequest = CreateCredentialRequest(
            helperRequest,
            resource,
            IsNonInteractiveRequest()
        );
        CredentialResult credentialResult = credentialAcquisition.Acquire(credentialRequest);
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(
            helperRequest,
            credentialResult
        );

        return new AdapterHostHandlerOutput(
            credentialResult,
            CredentialOperation.Get,
            protocolStdout: response.Stdout
        );
    }

    private static bool TryParseRequest(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out KeyringHelperRequest? request
    )
    {
        request = null;
        if (arguments.Count is not 7 and not 9)
        {
            return false;
        }

        if (
            !IsArgument(arguments[0], KeyringHelperV2.GetVerb)
            || !IsArgument(arguments[1], "--protocol-version")
            || !IsArgument(
                arguments[2],
                ContractVersions.KeyringHelperMajor.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
            || !IsArgument(arguments[3], "--service")
        )
        {
            return false;
        }

        string? username = null;
        int modeOptionIndex;
        if (arguments.Count == 7)
        {
            modeOptionIndex = 5;
        }
        else
        {
            if (
                !IsArgument(arguments[5], "--username")
                || string.IsNullOrWhiteSpace(arguments[6])
                || ContainsControlCharacters(arguments[6])
            )
            {
                return false;
            }

            username = arguments[6];
            modeOptionIndex = 7;
        }

        if (
            !IsArgument(arguments[modeOptionIndex], "--mode")
            || !TryParseMode(arguments[modeOptionIndex + 1], out KeyringHelperMode mode)
            || string.IsNullOrWhiteSpace(arguments[4])
            || ContainsControlCharacters(arguments[4])
            || !Uri.TryCreate(arguments[4], UriKind.RelativeOrAbsolute, out Uri? service)
        )
        {
            return false;
        }

        request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = service,
            Username = username,
            Mode = mode,
        };

        return true;
    }

    private static CredentialRequestV2 CreateCredentialRequest(
        KeyringHelperRequest helperRequest,
        CanonicalResourceIdentity resource,
        bool isNonInteractive
    )
    {
        return new CredentialRequestV2
        {
            Ecosystem = CredentialEcosystem.Python,
            Operation = CredentialOperation.Get,
            Resource = resource,
            ServiceIdentity = DefaultServiceIdentity,
            AccountHint = null,
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = isNonInteractive
                ? InteractivePolicy.Never
                : InteractivePolicy.UserAllowed,
            AcquisitionMode = isNonInteractive
                ? AcquisitionMode.SilentOnly
                : AcquisitionMode.InteractionAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
            ExtensionData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["python.keyring.mode"] =
                    helperRequest.Mode == KeyringHelperMode.Credentials ? "creds" : "password",
            },
        };
    }

    private bool IsNonInteractiveRequest() =>
        IsArtifactsKeyringNonInteractiveModeEnabled(
            environmentVariableReader(ArtifactsKeyringNonInteractiveEnvironmentVariable)
        )
        || IsAzureAuthNoUserEnabled(
            environmentVariableReader(ProductNoUserEnvironmentVariable)
        );

    private static bool IsArtifactsKeyringNonInteractiveModeEnabled(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsAzureAuthNoUserEnabled(string? value) =>
        !string.IsNullOrEmpty(value);

    private static PythonFeedResourceClassification ClassifyPythonFeedResource(
        Uri service,
        [NotNullWhen(true)] out CanonicalResourceIdentity? resource
    )
    {
        resource = null;
        if (!service.IsAbsoluteUri)
        {
            return PythonFeedResourceClassification.Invalid;
        }

        string host = service.IdnHost;
        if (!IsAzureArtifactsHost(host))
        {
            return PythonFeedResourceClassification.UnsupportedHost;
        }

        if (
            !IsValidAzureKeyringServiceSyntax(service)
            || !TryGetPathSegments(service, out string[]? segments)
        )
        {
            return PythonFeedResourceClassification.Invalid;
        }

        if (
            !TryParseAzureArtifactsPythonResource(
                service.IdnHost,
                segments,
                out AzureArtifactsPythonResourceShape? shape
            ) || shape is null
        )
        {
            return PythonFeedResourceClassification.Invalid;
        }

        try
        {
            resource = CanonicalResourceIdentity.Create(
                service.IdnHost,
                shape.Organization,
                service,
                shape.Project,
                feed: shape.Feed
            );
            return PythonFeedResourceClassification.Supported;
        }
        catch (ArgumentException)
        {
            return PythonFeedResourceClassification.Invalid;
        }
    }

    private static bool IsValidAzureKeyringServiceSyntax(Uri service)
    {
        return string.Equals(service.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && service.IsDefaultPort
            && !HasUserInfoDelimiter(service)
            && string.IsNullOrEmpty(service.Query)
            && string.IsNullOrEmpty(service.Fragment)
            && !service.AbsoluteUri.Contains('?', StringComparison.Ordinal)
            && !service.AbsoluteUri.Contains('#', StringComparison.Ordinal);
    }

    private static bool IsAzureArtifactsHost(string host) =>
        string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || TryGetLegacyVisualStudioOrganization(host, out _);

    private static bool HasUserInfoDelimiter(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        string absoluteUri = uri.AbsoluteUri;
        int authorityStart = absoluteUri.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += "://".Length;
        int authorityEnd = absoluteUri.IndexOfAny(['/', '?', '#'], authorityStart);
        ReadOnlySpan<char> authority =
            authorityEnd < 0
                ? absoluteUri.AsSpan(authorityStart)
                : absoluteUri.AsSpan(authorityStart, authorityEnd - authorityStart);
        return authority.Contains('@');
    }

    private static bool TryParseAzureArtifactsPythonResource(
        string host,
        string[] segments,
        [NotNullWhen(true)] out AzureArtifactsPythonResourceShape? shape
    )
    {
        if (
            string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (segments.Length == 0 || string.IsNullOrWhiteSpace(segments[0]))
            {
                shape = null;
                return false;
            }

            shape = ParsePythonResourceSegments(segments[0], segments[1..]);
            return shape is not null;
        }

        if (!TryGetLegacyVisualStudioOrganization(host, out string? organization))
        {
            shape = null;
            return false;
        }

        string[] resourceSegments =
            segments.Length > 0 && IsSegment(segments[0], "DefaultCollection")
                ? segments[1..]
                : segments;
        shape = ParsePythonResourceSegments(organization, resourceSegments);
        return shape is not null;
    }

    private static AzureArtifactsPythonResourceShape? ParsePythonResourceSegments(
        string organization,
        string[] resourceSegments
    )
    {
        if (
            resourceSegments.Length == 4
            && IsSegment(resourceSegments[0], "_packaging")
            && IsPythonFeedEndpointSuffix(resourceSegments, 2)
        )
        {
            return new AzureArtifactsPythonResourceShape(
                organization,
                Project: null,
                Feed: resourceSegments[1]
            );
        }

        if (
            resourceSegments.Length == 5
            && IsSegment(resourceSegments[1], "_packaging")
            && IsPythonFeedEndpointSuffix(resourceSegments, 3)
        )
        {
            return new AzureArtifactsPythonResourceShape(
                organization,
                Project: resourceSegments[0],
                Feed: resourceSegments[2]
            );
        }

        return null;
    }

    private static bool IsPythonFeedEndpointSuffix(string[] segments, int startIndex) =>
        IsSegment(segments[startIndex], "pypi")
        && (
            IsSegment(segments[startIndex + 1], "simple")
            || IsSegment(segments[startIndex + 1], "upload")
        );

    private static bool TryGetPathSegments(Uri uri, [NotNullWhen(true)] out string[]? segments)
    {
        string path = uri.AbsolutePath.StartsWith('/') ? uri.AbsolutePath[1..] : uri.AbsolutePath;
        if (path.Length == 0)
        {
            segments = [];
            return true;
        }

        var decodedSegments = new List<string>();
        foreach (string segment in path.Split('/', StringSplitOptions.None))
        {
            string decodedSegment;
            try
            {
                decodedSegment = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                segments = null;
                return false;
            }

            if (
                ContainsControlCharacters(decodedSegment)
                || decodedSegment.Contains('/', StringComparison.Ordinal)
                || decodedSegment.Contains('\\', StringComparison.Ordinal)
            )
            {
                segments = null;
                return false;
            }

            decodedSegments.Add(decodedSegment);
        }

        if (decodedSegments.Count > 0 && decodedSegments[^1].Length == 0)
        {
            decodedSegments.RemoveAt(decodedSegments.Count - 1);
        }

        segments = decodedSegments.ToArray();
        return true;
    }

    private static bool TryGetRawPathSegments(
        string serviceText,
        [NotNullWhen(true)] out string[]? segments,
        out bool hasTrailingSlash
    )
    {
        segments = null;
        hasTrailingSlash = false;
        int authorityStart = serviceText.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        int pathStart = serviceText.IndexOf('/', authorityStart + "://".Length);
        if (pathStart < 0)
        {
            segments = [];
            return true;
        }

        int pathEnd = serviceText.IndexOfAny(['?', '#'], pathStart);
        string rawPath =
            pathEnd < 0 ? serviceText[(pathStart + 1)..] : serviceText[(pathStart + 1)..pathEnd];
        string[] rawSegments = rawPath.Split('/', StringSplitOptions.None);
        if (rawSegments.Length > 0 && rawSegments[^1].Length == 0)
        {
            hasTrailingSlash = true;
            rawSegments = rawSegments[..^1];
        }

        if (rawSegments.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var decodedSegments = new string[rawSegments.Length];
        for (int index = 0; index < rawSegments.Length; index++)
        {
            if (
                !TryDecodeRawPathSegment(
                    rawSegments[index],
                    out string? decodedSegment
                )
            )
            {
                return false;
            }

            decodedSegments[index] = decodedSegment;
        }

        segments = decodedSegments;
        return true;
    }

    private static bool TryDecodeRawPathSegment(
        string rawSegment,
        [NotNullWhen(true)] out string? decodedSegment
    )
    {
        decodedSegment = null;
        for (int index = 0; index < rawSegment.Length; index++)
        {
            char rawCharacter = rawSegment[index];
            if (char.IsWhiteSpace(rawCharacter))
            {
                return false;
            }

            if (rawCharacter != '%')
            {
                continue;
            }

            if (
                index + 2 >= rawSegment.Length
                || !Uri.IsHexDigit(rawSegment[index + 1])
                || !Uri.IsHexDigit(rawSegment[index + 2])
            )
            {
                return false;
            }

            index += 2;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(rawSegment);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (!IsSafeDecodedPathSegment(decoded))
        {
            return false;
        }

        string safetyProbe = decoded;
        const int MaxNestedDecodingPasses = 8;
        for (int pass = 0; pass < MaxNestedDecodingPasses; pass++)
        {
            string nextSafetyProbe;
            try
            {
                nextSafetyProbe = Uri.UnescapeDataString(safetyProbe);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (string.Equals(nextSafetyProbe, safetyProbe, StringComparison.Ordinal))
            {
                decodedSegment = decoded;
                return true;
            }

            if (!IsSafeDecodedPathSegment(nextSafetyProbe))
            {
                return false;
            }

            safetyProbe = nextSafetyProbe;
        }

        return false;
    }

    private static bool IsSafeDecodedPathSegment(string segment) =>
        !string.IsNullOrWhiteSpace(segment)
        && segment is not "." and not ".."
        && !ContainsControlCharacters(segment)
        && !segment.Contains('/', StringComparison.Ordinal)
        && !segment.Contains('\\', StringComparison.Ordinal);

    private static bool TryGetLegacyVisualStudioOrganization(
        string host,
        [NotNullWhen(true)] out string? organization
    )
    {
        const string pkgsSuffix = ".pkgs.visualstudio.com";
        const string suffix = ".visualstudio.com";
        if (
            host.EndsWith(pkgsSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > pkgsSuffix.Length
        )
        {
            organization = host[..^pkgsSuffix.Length];
            return IsValidLegacyOrganization(organization);
        }

        if (
            host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > suffix.Length
        )
        {
            organization = host[..^suffix.Length];
            return IsValidLegacyOrganization(organization);
        }

        organization = null;
        return false;
    }

    private static bool IsValidLegacyOrganization(string organization) =>
        !string.IsNullOrWhiteSpace(organization)
        && !organization.Contains('.', StringComparison.Ordinal);

    private static bool TryParseMode(string value, out KeyringHelperMode mode)
    {
        mode = value switch
        {
            "password" => KeyringHelperMode.Password,
            "creds" => KeyringHelperMode.Credentials,
            _ => KeyringHelperMode.Unspecified,
        };

        return mode != KeyringHelperMode.Unspecified;
    }

    private static AdapterHostHandlerOutput CreateProtocolViolationOutput()
    {
        return new AdapterHostHandlerOutput(
            credentialResult: new CredentialResult
            {
                Status = CredentialResultStatus.ProtocolViolation,
                DiagnosticsCorrelationId = CorrelationId.New().ToString(),
                Error = new CredentialError
                {
                    Kind = CredentialErrorKind.ProtocolViolation,
                    Code = ProtocolViolationCode,
                    SafeMessage = "Keyring helper input is invalid.",
                },
            },
            operation: CredentialOperation.Get
        );
    }

    private static AdapterHostHandlerOutput CreateNoCredentialOutput()
    {
        return new AdapterHostHandlerOutput(
            credentialResult: new CredentialResult
            {
                Status = CredentialResultStatus.NoCredential,
                DiagnosticsCorrelationId = CorrelationId.New().ToString(),
                Error = new CredentialError
                {
                    Kind = CredentialErrorKind.UnsupportedHost,
                    Code = UnsupportedHostCode,
                    SafeMessage = "Keyring helper service host is unsupported.",
                },
            },
            operation: CredentialOperation.Get
        );
    }

    private static bool IsArgument(string value, string expected) =>
        string.Equals(value, expected, StringComparison.Ordinal);

    private static bool ContainsControlCharacters(string value) => value.Any(char.IsControl);

    private static bool IsSegment(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private sealed record AzureArtifactsPythonResourceShape(
        string Organization,
        string? Project,
        string Feed
    );

    private enum PythonFeedResourceClassification
    {
        Invalid,
        UnsupportedHost,
        Supported,
    }
}
