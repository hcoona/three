using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class KeyringHelperAdapter
{
    public const string ProductExecutableName = "azureauth-credprovider";

    private const string DefaultServiceIdentity = "default";
    private const string ProtocolViolationCode = "ProtocolViolation";
    private const string UnsupportedHostCode = "UnsupportedHost";

    private readonly CredentialCoreService credentialCore;

    public KeyringHelperAdapter(CredentialCoreService? credentialCore = null)
    {
        this.credentialCore = credentialCore ?? new CredentialCoreService();
    }

    public static AdapterDescriptor Descriptor { get; } = CreateDescriptor();

    public static bool TryResolveProtocolInvocation(
        string? executablePath,
        IEnumerable<string>? arguments,
        out AdapterInvocationContext? context)
    {
        bool resolved = AdapterHostBootstrap.TryResolveInvocation(
            Descriptor,
            executablePath,
            arguments,
            out context);
        if (!resolved || context is null || !context.IsProtocolInvocation)
        {
            context = null;
            return false;
        }

        return true;
    }

    public AdapterHostExecutionOutcome Execute(
        string? executablePath,
        IEnumerable<string>? arguments,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter)
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
            diagnosticRouter);
    }

    private static AdapterDescriptor CreateDescriptor()
    {
        AdapterEntrypointDescriptor sharedCliEntrypoint = new(
            "KeyringHelper",
            AdapterInvocationMode.Protocol,
            executableNames: [ProductExecutableName],
            argumentTokens: [KeyringHelperV2.CommandName],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix);
        AdapterEntrypointDescriptor helperExecutableEntrypoint = new(
            "KeyringHelperExecutable",
            AdapterInvocationMode.Protocol,
            executableNames: [KeyringHelperV2.CommandName]);
        AdapterEntrypointDescriptor helperExecutablePrefixedEntrypoint = new(
            "KeyringHelperExecutablePrefixed",
            AdapterInvocationMode.Protocol,
            executableNames: [KeyringHelperV2.CommandName],
            argumentTokens: [KeyringHelperV2.CommandName],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix);
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: [ProductExecutableName]);

        return new AdapterDescriptor(
            "Keyring Helper v2",
            AdapterProtocol.KeyringHelper,
            [
                sharedCliEntrypoint,
                helperExecutableEntrypoint,
                helperExecutablePrefixedEntrypoint,
                humanEntrypoint,
            ]);
    }

    private AdapterHostHandlerOutput Handle(AdapterInvocationContext context)
    {
        if (!TryParseRequest(context.PayloadArguments, out KeyringHelperRequest? helperRequest))
        {
            return CreateProtocolViolationOutput();
        }

        PythonFeedResourceClassification resourceClassification =
            ClassifyPythonFeedResource(
                helperRequest.Service,
                out CanonicalResourceIdentity? resource);
        if (resourceClassification == PythonFeedResourceClassification.UnsupportedHost)
        {
            return CreateNoCredentialOutput();
        }

        if (
            resourceClassification != PythonFeedResourceClassification.Supported
            || resource is null)
        {
            return CreateProtocolViolationOutput();
        }

        CredentialRequest credentialRequest = CreateCredentialRequest(helperRequest, resource);
        CredentialResult credentialResult = credentialCore.Execute(credentialRequest);
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(
            helperRequest,
            credentialResult);

        return new AdapterHostHandlerOutput(
            credentialResult,
            CredentialOperation.Get,
            protocolStdout: response.Stdout);
    }

    private static bool TryParseRequest(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out KeyringHelperRequest? request)
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
                    System.Globalization.CultureInfo.InvariantCulture))
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
            || !Uri.TryCreate(
                arguments[4],
                UriKind.RelativeOrAbsolute,
                out Uri? service)
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

    private static CredentialRequest CreateCredentialRequest(
        KeyringHelperRequest helperRequest,
        CanonicalResourceIdentity resource)
    {
        return new CredentialRequest
        {
            Ecosystem = CredentialEcosystem.Python,
            Operation = CredentialOperation.Get,
            Resource = resource,
            ServiceIdentity = DefaultServiceIdentity,
            AccountHint = string.IsNullOrWhiteSpace(helperRequest.Username)
                ? null
                : helperRequest.Username.Trim(),
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.DeviceCode,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext
            {
                ExplicitCiMode = false,
                AllowsPersistentWrites = false,
            },
            ExtensionData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["python.keyring.mode"] = helperRequest.Mode == KeyringHelperMode.Credentials
                    ? "creds"
                    : "password",
            },
        };
    }

    private static PythonFeedResourceClassification ClassifyPythonFeedResource(
        Uri service,
        [NotNullWhen(true)] out CanonicalResourceIdentity? resource)
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
            || !TryGetPathSegments(service, out string[]? segments))
        {
            return PythonFeedResourceClassification.Invalid;
        }

        if (
            !TryParseAzureArtifactsPythonResource(
                service.IdnHost,
                segments,
                out AzureArtifactsPythonResourceShape? shape)
            || shape is null
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
                feed: shape.Feed);
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
        [NotNullWhen(true)] out AzureArtifactsPythonResourceShape? shape)
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
        string[] resourceSegments)
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
                Feed: resourceSegments[1]);
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
                Feed: resourceSegments[2]);
        }

        return null;
    }

    private static bool IsPythonFeedEndpointSuffix(string[] segments, int startIndex) =>
        IsSegment(segments[startIndex], "pypi")
        && (IsSegment(segments[startIndex + 1], "simple")
            || IsSegment(segments[startIndex + 1], "upload"));

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

    private static bool TryGetLegacyVisualStudioOrganization(
        string host,
        [NotNullWhen(true)] out string? organization)
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
            operation: CredentialOperation.Get);
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
            operation: CredentialOperation.Get);
    }

    private static bool IsArgument(string value, string expected) =>
        string.Equals(value, expected, StringComparison.Ordinal);

    private static bool ContainsControlCharacters(string value) => value.Any(char.IsControl);

    private static bool IsSegment(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private sealed record AzureArtifactsPythonResourceShape(
        string Organization,
        string? Project,
        string Feed);

    private enum PythonFeedResourceClassification
    {
        Invalid,
        UnsupportedHost,
        Supported,
    }
}
