using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class GitCredentialHelperAdapter
{
    public const string ProductExecutableName = "azureauth-credprovider";
    public const string HelperExecutableName = "git-credential-azureauth-credprovider";

    private const int MaxCredentialRecordCharacters = 16 * 1024;
    private const string DefaultServiceIdentity = "default";
    private const string ProtocolViolationCode = "ProtocolViolation";
    private const string NoCredentialCode = "NoCredential";

    private static readonly HashSet<string> RecognizedFields =
    [
        "host",
        "password",
        "path",
        "protocol",
        "username",
    ];

    private readonly BoundedCredentialAcquisitionAdapter credentialAcquisition;

    public GitCredentialHelperAdapter()
        : this(CredentialProviderCompositionRoot.CreateProduction().AcquisitionService)
    { }

    public GitCredentialHelperAdapter(CredentialCoreService? credentialCore)
        : this(
            credentialCore is null
                ? CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                : new LegacyV1CredentialAcquisitionService(credentialCore))
    { }

    public GitCredentialHelperAdapter(ICredentialAcquisitionService credentialAcquisition)
        : this(new BoundedCredentialAcquisitionAdapter(credentialAcquisition))
    { }

    public GitCredentialHelperAdapter(BoundedCredentialAcquisitionAdapter credentialAcquisition)
    {
        ArgumentNullException.ThrowIfNull(credentialAcquisition);
        this.credentialAcquisition = credentialAcquisition;
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
        TextReader protocolStdin,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        DiagnosticRouter diagnosticRouter)
    {
        ArgumentNullException.ThrowIfNull(protocolStdin);
        ArgumentNullException.ThrowIfNull(protocolStdout);
        ArgumentNullException.ThrowIfNull(humanStdout);
        ArgumentNullException.ThrowIfNull(diagnosticRouter);

        return AdapterHostExecutor.Execute(
            Descriptor,
            executablePath,
            arguments,
            context => Handle(context, protocolStdin),
            protocolStdout,
            humanStdout,
            diagnosticRouter);
    }

    public static string? CreateProtocolStdout(CredentialResult credentialResult)
    {
        ArgumentNullException.ThrowIfNull(credentialResult);

        return AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
            credentialResult,
            out string? username,
            out string? password)
            ? $"username={username}\npassword={password}\n"
            : null;
    }

    private static AdapterDescriptor CreateDescriptor()
    {
        AdapterEntrypointDescriptor sharedCliEntrypoint = new(
            "GitCredentialHelper",
            AdapterInvocationMode.Protocol,
            executableNames: [ProductExecutableName],
            argumentTokens: ["git", "credential-helper"],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix);
        AdapterEntrypointDescriptor helperExecutableEntrypoint = new(
            "GitCredentialHelperExecutable",
            AdapterInvocationMode.Protocol,
            executableNames: [HelperExecutableName]);
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: [ProductExecutableName]);

        return new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            [sharedCliEntrypoint, helperExecutableEntrypoint, humanEntrypoint]);
    }

    private AdapterHostHandlerOutput Handle(
        AdapterInvocationContext context,
        TextReader protocolStdin)
    {
        if (context.PayloadArguments.Count != 1)
        {
            return CreateProtocolViolationOutput(CredentialOperation.Unspecified);
        }

        if (!TryParseOperation(context.PayloadArguments[0], out CredentialOperation operation))
        {
            _ = TryReadCredentialRecord(protocolStdin, out _);
            return CreateSuccessOutput(CredentialOperation.Store);
        }

        if (
            !TryReadCredentialRecord(
                protocolStdin,
                out IReadOnlyDictionary<string, string>? fields)
        )
        {
            return CreateProtocolViolationOutput(operation);
        }

        return operation switch
        {
            CredentialOperation.Get => HandleGet(fields),
            CredentialOperation.Store or CredentialOperation.Erase
                => CreateSuccessOutput(operation),
            _ => CreateProtocolViolationOutput(operation),
        };
    }

    private AdapterHostHandlerOutput HandleGet(IReadOnlyDictionary<string, string> fields)
    {
        GitResourceParseResult resourceParseResult = TryCreateResource(fields);
        if (resourceParseResult.Status == GitResourceParseStatus.NoCredential)
        {
            return CreateNoCredentialOutput(CredentialOperation.Get);
        }

        if (
            resourceParseResult.Status == GitResourceParseStatus.ProtocolViolation
            || resourceParseResult.Resource is null
        )
        {
            return CreateProtocolViolationOutput(CredentialOperation.Get);
        }

        CredentialRequestV2 request = CreateGetRequest(resourceParseResult.Resource, fields);
        CredentialResult result = credentialAcquisition.Acquire(request);
        return new AdapterHostHandlerOutput(
            credentialResult: result,
            operation: CredentialOperation.Get,
            protocolStdout: CreateProtocolStdout(result));
    }

    private static bool TryParseOperation(
        string payloadArgument,
        out CredentialOperation operation)
    {
        operation = payloadArgument switch
        {
            "get" => CredentialOperation.Get,
            "store" => CredentialOperation.Store,
            "erase" => CredentialOperation.Erase,
            _ => CredentialOperation.Unspecified,
        };
        return operation != CredentialOperation.Unspecified;
    }

    private static bool TryReadCredentialRecord(
        TextReader reader,
        [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? fields)
    {
        var recognizedFields = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalCharacters = 0;
        var terminated = false;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            totalCharacters += line.Length + 1;
            if (totalCharacters > MaxCredentialRecordCharacters)
            {
                fields = null;
                return false;
            }

            if (line.Length == 0)
            {
                terminated = true;
                break;
            }

            if (!TryReadCredentialField(line, recognizedFields))
            {
                fields = null;
                return false;
            }
        }

        if (terminated)
        {
            while ((line = reader.ReadLine()) is not null)
            {
                totalCharacters += line.Length + 1;
                if (totalCharacters > MaxCredentialRecordCharacters || line.Length != 0)
                {
                    fields = null;
                    return false;
                }
            }
        }

        fields = recognizedFields;
        return true;
    }

    private static bool TryReadCredentialField(
        string line,
        IDictionary<string, string> recognizedFields)
    {
        int separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return false;
        }

        string key = line[..separatorIndex];
        string value = line[(separatorIndex + 1)..];
        if (ContainsControlCharacters(key) || ContainsControlCharacters(value))
        {
            return false;
        }

        return !RecognizedFields.Contains(key)
            || recognizedFields.TryAdd(key, value);
    }

    private static GitResourceParseResult TryCreateResource(
        IReadOnlyDictionary<string, string> fields)
    {
        if (
            !fields.TryGetValue("protocol", out string? protocol)
            || !fields.TryGetValue("host", out string? host)
            || string.IsNullOrWhiteSpace(protocol)
            || string.IsNullOrWhiteSpace(host)
        )
        {
            return GitResourceParseResult.ProtocolViolation();
        }

        string rawPath = fields.TryGetValue("path", out string? path) ? path : string.Empty;
        if (!TryCreateServiceEndpoint(protocol, host, rawPath, out Uri? serviceEndpoint))
        {
            return GitResourceParseResult.ProtocolViolation();
        }

        if (!IsAzureReposGitHost(serviceEndpoint.IdnHost))
        {
            return GitResourceParseResult.NoCredential();
        }

        if (!string.Equals(serviceEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return GitResourceParseResult.ProtocolViolation();
        }

        if (
            !TryGetPathSegments(serviceEndpoint, out string[]? segments)
            || !TryParseAzureReposGitResource(
                serviceEndpoint.IdnHost,
                segments,
                out AzureReposGitResourceShape? shape)
        )
        {
            return GitResourceParseResult.ProtocolViolation();
        }

        if (shape is null)
        {
            return GitResourceParseResult.NoCredential();
        }

        try
        {
            return GitResourceParseResult.Success(
                CanonicalResourceIdentity.Create(
                    serviceEndpoint.IdnHost,
                    shape.Organization,
                    serviceEndpoint,
                    shape.Project,
                    repository: shape.Repository));
        }
        catch (ArgumentException)
        {
            return GitResourceParseResult.ProtocolViolation();
        }
    }

    private static bool TryCreateServiceEndpoint(
        string protocol,
        string host,
        string rawPath,
        [NotNullWhen(true)] out Uri? serviceEndpoint)
    {
        serviceEndpoint = null;
        string normalizedPath = rawPath.Length == 0
            ? string.Empty
            : "/" + rawPath.TrimStart('/');
        string serviceEndpointText = string.Concat(protocol, "://", host, normalizedPath);

        return Uri.TryCreate(serviceEndpointText, UriKind.Absolute, out serviceEndpoint);
    }

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

        segments = decodedSegments.ToArray();
        return true;
    }

    private static bool TryParseAzureReposGitResource(
        string host,
        string[] segments,
        out AzureReposGitResourceShape? shape)
    {
        if (string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            shape = ParseModernAzureReposGitResource(segments);
            return true;
        }

        string? legacyOrganization = TryGetLegacyVisualStudioOrganization(host);
        if (legacyOrganization is null)
        {
            shape = null;
            return true;
        }

        shape = ParseLegacyAzureReposGitResource(legacyOrganization, segments);
        return true;
    }

    private static AzureReposGitResourceShape? ParseModernAzureReposGitResource(
        string[] segments)
    {
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return new AzureReposGitResourceShape(segments[0], Project: null, Repository: null);
        }

        if (segments.Length == 4 && IsSegment(segments[2], "_git"))
        {
            return new AzureReposGitResourceShape(
                segments[0],
                Project: segments[1],
                Repository: segments[3]);
        }

        return null;
    }

    private static AzureReposGitResourceShape? ParseLegacyAzureReposGitResource(
        string organization,
        string[] segments)
    {
        string[] resourceSegments =
            segments.Length > 0 && IsSegment(segments[0], "DefaultCollection")
                ? segments[1..]
                : segments;

        if (resourceSegments.Length == 0)
        {
            return new AzureReposGitResourceShape(
                organization,
                Project: null,
                Repository: null);
        }

        if (resourceSegments.Length == 3 && IsSegment(resourceSegments[1], "_git"))
        {
            return new AzureReposGitResourceShape(
                organization,
                Project: resourceSegments[0],
                Repository: resourceSegments[2]);
        }

        return null;
    }

    private static CredentialRequestV2 CreateGetRequest(
        CanonicalResourceIdentity resource,
        IReadOnlyDictionary<string, string> fields)
    {
        return new CredentialRequestV2
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = resource,
            ServiceIdentity = DefaultServiceIdentity,
            AccountHint = fields.TryGetValue("username", out string? username)
                && !string.IsNullOrWhiteSpace(username)
                    ? username
                    : null,
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = InteractivePolicy.Never,
            AcquisitionMode = AcquisitionMode.SilentOnly,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext
            {
                ExplicitCiMode = false,
                AllowsPersistentWrites = false,
            },
        };
    }

    private static AdapterHostHandlerOutput CreateSuccessOutput(CredentialOperation operation)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                DiagnosticsCorrelationId = CorrelationId.New().ToString(),
            },
            operation: operation);
    }

    private static AdapterHostHandlerOutput CreateNoCredentialOutput(CredentialOperation operation)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: new CredentialResult
            {
                Status = CredentialResultStatus.NoCredential,
                DiagnosticsCorrelationId = CorrelationId.New().ToString(),
                Error = new CredentialError
                {
                    Kind = CredentialErrorKind.UnsupportedHost,
                    Code = NoCredentialCode,
                    SafeMessage = "No credential is available for the requested Git host.",
                },
            },
            operation: operation);
    }

    private static AdapterHostHandlerOutput CreateProtocolViolationOutput(
        CredentialOperation operation)
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
                    SafeMessage = "Git credential helper input is invalid.",
                },
            },
            operation: operation);
    }

    private static bool ContainsControlCharacters(string value) => value.Any(char.IsControl);

    private static bool IsSegment(string segment, string expected) =>
        string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsAzureReposGitHost(string host) =>
        string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || (
            host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase)
        );

    private static string? TryGetLegacyVisualStudioOrganization(string host)
    {
        const string packagingSuffix = ".pkgs.visualstudio.com";
        const string suffix = ".visualstudio.com";

        if (
            host.EndsWith(packagingSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > packagingSuffix.Length
        )
        {
            return null;
        }

        if (
            !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || host.Length <= suffix.Length
        )
        {
            return null;
        }

        return host[..^suffix.Length];
    }

    private sealed record AzureReposGitResourceShape(
        string Organization,
        string? Project,
        string? Repository);

    private sealed record GitResourceParseResult(
        GitResourceParseStatus Status,
        CanonicalResourceIdentity? Resource)
    {
        public static GitResourceParseResult Success(CanonicalResourceIdentity resource) =>
            new(GitResourceParseStatus.Success, resource);

        public static GitResourceParseResult NoCredential() =>
            new(GitResourceParseStatus.NoCredential, Resource: null);

        public static GitResourceParseResult ProtocolViolation() =>
            new(GitResourceParseStatus.ProtocolViolation, Resource: null);
    }

    private enum GitResourceParseStatus
    {
        Success,
        NoCredential,
        ProtocolViolation,
    }
}
