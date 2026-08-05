using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol.Plugins;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class NuGetPluginAdapter
{
    public const string ProductExecutableName = "azureauth-credprovider";

    private const string DefaultServiceIdentity = "default";
    private const string NoCredentialMessage = "Credentials were not found for this NuGet source.";
    private static readonly TimeSpan PluginShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly BoundedCredentialAcquisitionAdapter credentialAcquisition;

    public NuGetPluginAdapter()
        : this(CredentialProviderCompositionRoot.CreateProduction().AcquisitionService) { }

    public NuGetPluginAdapter(CredentialCoreService? credentialCore)
        : this(
            credentialCore is null
                ? CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                : new LegacyV1CredentialAcquisitionService(credentialCore)
        )
    { }

    public NuGetPluginAdapter(ICredentialAcquisitionService credentialAcquisition)
        : this(new BoundedCredentialAcquisitionAdapter(credentialAcquisition)) { }

    public NuGetPluginAdapter(BoundedCredentialAcquisitionAdapter credentialAcquisition)
    {
        ArgumentNullException.ThrowIfNull(credentialAcquisition);
        this.credentialAcquisition = credentialAcquisition;
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

    public async Task<int> RunPluginAsync(CancellationToken cancellationToken = default)
    {
        RequestHandlers requestHandlers = CreateRequestHandlers();
        using IPlugin plugin = await PluginFactory
            .CreateFromCurrentProcessAsync(
                requestHandlers,
                ConnectionOptions.CreateDefault(),
                cancellationToken
            )
            .ConfigureAwait(false);

        await WaitForPluginCloseAsync(plugin, cancellationToken).ConfigureAwait(false);
        return (int)AdapterHostExitCode.Success;
    }

    public static InitializeResponse HandleInitialize(InitializeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new InitializeResponse(MessageResponseCode.Success);
    }

    public static GetOperationClaimsResponse HandleGetOperationClaims(
        GetOperationClaimsRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.PackageSourceRepository is null && request.ServiceIndex is null
            ? new GetOperationClaimsResponse([OperationClaim.Authentication])
            : new GetOperationClaimsResponse([]);
    }

    public GetAuthenticationCredentialsResponse HandleGetAuthenticationCredentials(
        GetAuthenticationCredentialsRequest request
    ) =>
        HandleGetAuthenticationCredentialsAsync(request, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    internal async ValueTask<GetAuthenticationCredentialsResponse> HandleGetAuthenticationCredentialsAsync(
        GetAuthenticationCredentialsRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        NuGetResourceParseResult parseResult = TryCreateResource(request.Uri);
        if (parseResult.Status == NuGetResourceParseStatus.NoCredential)
        {
            return CreateNotFoundResponse();
        }

        if (
            parseResult.Status == NuGetResourceParseStatus.ProtocolViolation
            || parseResult.Resource is null
        )
        {
            return CreateErrorResponse("Protocol violation: NuGet source URI is not supported.");
        }

        CredentialRequestV2 credentialRequest = CreateCredentialRequest(
            parseResult.Resource,
            request
        );
        CredentialResult credentialResult = await credentialAcquisition
            .AcquireAsync(credentialRequest, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return CreateAuthenticationCredentialsResponse(credentialResult);
    }

    public static SetCredentialsResponse HandleSetCredentials(SetCredentialsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SetCredentialsResponse(MessageResponseCode.Success);
    }

    public static SetLogLevelResponse HandleSetLogLevel(SetLogLevelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = request.LogLevel;
        return new SetLogLevelResponse(MessageResponseCode.Success);
    }

    internal RequestHandlers CreateRequestHandlers()
    {
        var requestHandlers = new RequestHandlers();
        AddRequestHandler(requestHandlers, MessageMethod.Initialize, new InitializeHandler());
        AddRequestHandler(
            requestHandlers,
            MessageMethod.GetOperationClaims,
            new GetOperationClaimsHandler()
        );
        AddRequestHandler(
            requestHandlers,
            MessageMethod.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsHandler(this)
        );
        AddRequestHandler(
            requestHandlers,
            MessageMethod.SetCredentials,
            new SetCredentialsHandler()
        );
        AddRequestHandler(requestHandlers, MessageMethod.SetLogLevel, new SetLogLevelHandler());
        return requestHandlers;
    }

    private static void AddRequestHandler(
        RequestHandlers requestHandlers,
        MessageMethod method,
        IRequestHandler handler
    )
    {
        if (!requestHandlers.TryAdd(method, handler))
        {
            throw new InvalidOperationException(
                "NuGet plugin request handler registration failed."
            );
        }
    }

    private static AdapterDescriptor CreateDescriptor()
    {
        AdapterEntrypointDescriptor pluginEntrypoint = new(
            "NuGetPlugin",
            AdapterInvocationMode.Protocol,
            executableNames: [ProductExecutableName],
            argumentTokens: ["-Plugin"],
            argumentMatchMode: AdapterArgumentMatchMode.Exact,
            protocol: AdapterProtocol.NuGetPlugin
        );
        AdapterEntrypointDescriptor shortPluginEntrypoint = new(
            "NuGetPluginShort",
            AdapterInvocationMode.Protocol,
            executableNames: [ProductExecutableName],
            argumentTokens: ["-P"],
            argumentMatchMode: AdapterArgumentMatchMode.Exact,
            protocol: AdapterProtocol.NuGetPlugin
        );
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: [ProductExecutableName]
        );

        return new AdapterDescriptor(
            "NuGet Plugin",
            AdapterProtocol.NuGetPlugin,
            [pluginEntrypoint, shortPluginEntrypoint, humanEntrypoint]
        );
    }

    private static async Task WaitForPluginCloseAsync(
        IPlugin plugin,
        CancellationToken cancellationToken
    )
    {
        var beginClose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var endClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            () =>
            {
                beginClose.TrySetCanceled(cancellationToken);
                endClose.TrySetCanceled(cancellationToken);
            }
        );

        plugin.BeforeClose += (_, _) => beginClose.TrySetResult();
        plugin.Closed += (_, _) =>
        {
            beginClose.TrySetResult();
            endClose.TrySetResult();
        };

        await beginClose.Task.ConfigureAwait(false);
        using var shutdownTimeout = new CancellationTokenSource(PluginShutdownTimeout);
        using CancellationTokenRegistration shutdownRegistration = shutdownTimeout.Token.Register(
            () =>
                endClose.TrySetCanceled(shutdownTimeout.Token)
        );
        await endClose.Task.ConfigureAwait(false);
    }

    private static CredentialRequestV2 CreateCredentialRequest(
        CanonicalResourceIdentity resource,
        GetAuthenticationCredentialsRequest request
    )
    {
        bool interactionAllowed = !request.IsNonInteractive;
        IdentityFlow identityFlow =
            interactionAllowed && !request.CanShowDialog
                ? IdentityFlow.DeviceCode
                : IdentityFlow.InteractiveBrowser;

        return new CredentialRequestV2
        {
            Ecosystem = CredentialEcosystem.NuGet,
            Operation = CredentialOperation.Get,
            Resource = resource,
            ServiceIdentity = DefaultServiceIdentity,
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = CredentialKind.NuGetPluginCredential,
            IdentityFlow = identityFlow,
            InteractivePolicy = interactionAllowed
                ? InteractivePolicy.HostToolAllows
                : InteractivePolicy.Never,
            AcquisitionMode = interactionAllowed
                ? AcquisitionMode.InteractionAllowed
                : AcquisitionMode.SilentOnly,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
            ExtensionData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nuget.canShowDialog"] = request.CanShowDialog ? "true" : "false",
                ["nuget.isNonInteractive"] = request.IsNonInteractive ? "true" : "false",
                ["nuget.isRetry"] = request.IsRetry ? "true" : "false",
            },
        };
    }

    private static GetAuthenticationCredentialsResponse CreateAuthenticationCredentialsResponse(
        CredentialResult credentialResult
    )
    {
        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.NuGetPlugin,
            credentialResult
        );

        if (mapped.ExitCode == AdapterHostExitCode.NoCredential)
        {
            return CreateNotFoundResponse();
        }

        if (
            mapped.ExitCode == AdapterHostExitCode.Success
            && !string.IsNullOrEmpty(credentialResult.Username)
            && !string.IsNullOrEmpty(credentialResult.Password)
        )
        {
            return new GetAuthenticationCredentialsResponse(
                credentialResult.Username,
                credentialResult.Password,
                message: null,
                authenticationTypes: ["Basic"],
                MessageResponseCode.Success
            );
        }

        return CreateErrorResponse(
            credentialResult.Error?.SafeMessage ?? "NuGet credential request failed."
        );
    }

    private static GetAuthenticationCredentialsResponse CreateNotFoundResponse() =>
        new(
            username: null,
            password: null,
            NoCredentialMessage,
            authenticationTypes: null,
            MessageResponseCode.NotFound
        );

    private static GetAuthenticationCredentialsResponse CreateErrorResponse(string message) =>
        new(
            username: null,
            password: null,
            message,
            authenticationTypes: null,
            MessageResponseCode.Error
        );

    private static NuGetResourceParseResult TryCreateResource(Uri uri)
    {
        if (uri is null)
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        if (!IsAzureArtifactsHost(uri.IdnHost))
        {
            return NuGetResourceParseResult.NoCredential();
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        if (!TryGetPathSegments(uri, out string[]? segments))
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        if (
            !TryParseAzureArtifactsNuGetResource(
                uri.IdnHost,
                segments,
                out AzureArtifactsNuGetResourceShape? shape
            ) || shape is null
        )
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }

        try
        {
            return NuGetResourceParseResult.Success(
                CanonicalResourceIdentity.Create(
                    uri.IdnHost,
                    shape.Organization,
                    uri,
                    shape.Project,
                    feed: shape.Feed
                )
            );
        }
        catch (ArgumentException)
        {
            return NuGetResourceParseResult.ProtocolViolation();
        }
    }

    private static bool IsAzureArtifactsHost(string host) =>
        string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
        || TryGetLegacyVisualStudioOrganization(host, out _);

    private static bool TryParseAzureArtifactsNuGetResource(
        string host,
        string[] segments,
        [NotNullWhen(true)] out AzureArtifactsNuGetResourceShape? shape
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

            shape = ParseNuGetResourceSegments(segments[0], segments.Skip(1).ToArray());
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
        shape = ParseNuGetResourceSegments(organization, resourceSegments);
        return shape is not null;
    }

    private static AzureArtifactsNuGetResourceShape? ParseNuGetResourceSegments(
        string organization,
        string[] resourceSegments
    )
    {
        if (
            resourceSegments.Length == 5
            && IsSegment(resourceSegments[0], "_packaging")
            && IsNuGetV3IndexSuffix(resourceSegments, 2)
        )
        {
            return new AzureArtifactsNuGetResourceShape(
                organization,
                Project: null,
                Feed: resourceSegments[1]
            );
        }

        if (
            resourceSegments.Length == 6
            && IsSegment(resourceSegments[1], "_packaging")
            && IsNuGetV3IndexSuffix(resourceSegments, 3)
        )
        {
            return new AzureArtifactsNuGetResourceShape(
                organization,
                Project: resourceSegments[0],
                Feed: resourceSegments[2]
            );
        }

        return null;
    }

    private static bool IsNuGetV3IndexSuffix(string[] segments, int startIndex) =>
        IsSegment(segments[startIndex], "nuget")
        && IsSegment(segments[startIndex + 1], "v3")
        && IsSegment(segments[startIndex + 2], "index.json");

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

    private static bool TryGetLegacyVisualStudioOrganization(
        string host,
        [NotNullWhen(true)] out string? organization
    )
    {
        const string suffix = ".pkgs.visualstudio.com";
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = null;
            return false;
        }

        organization = host[..^suffix.Length];
        return !string.IsNullOrWhiteSpace(organization);
    }

    private static bool IsSegment(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsControlCharacters(string? value) =>
        value is not null && value.Any(char.IsControl);

    private sealed class InitializeHandler
        : NuGetRequestHandler<InitializeRequest, InitializeResponse>
    {
        protected override ValueTask<InitializeResponse> HandleRequestAsync(
            InitializeRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(NuGetPluginAdapter.HandleInitialize(request));
    }

    private sealed class GetOperationClaimsHandler
        : NuGetRequestHandler<GetOperationClaimsRequest, GetOperationClaimsResponse>
    {
        protected override ValueTask<GetOperationClaimsResponse> HandleRequestAsync(
            GetOperationClaimsRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(NuGetPluginAdapter.HandleGetOperationClaims(request));
    }

    private sealed class GetAuthenticationCredentialsHandler(NuGetPluginAdapter adapter)
        : NuGetRequestHandler<
            GetAuthenticationCredentialsRequest,
            GetAuthenticationCredentialsResponse
        >
    {
        protected override ValueTask<GetAuthenticationCredentialsResponse> HandleRequestAsync(
            GetAuthenticationCredentialsRequest request,
            CancellationToken cancellationToken
        ) => adapter.HandleGetAuthenticationCredentialsAsync(request, cancellationToken);
    }

    private sealed class SetCredentialsHandler
        : NuGetRequestHandler<SetCredentialsRequest, SetCredentialsResponse>
    {
        protected override ValueTask<SetCredentialsResponse> HandleRequestAsync(
            SetCredentialsRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(NuGetPluginAdapter.HandleSetCredentials(request));
    }

    private sealed class SetLogLevelHandler
        : NuGetRequestHandler<SetLogLevelRequest, SetLogLevelResponse>
    {
        protected override ValueTask<SetLogLevelResponse> HandleRequestAsync(
            SetLogLevelRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(NuGetPluginAdapter.HandleSetLogLevel(request));
    }

    private abstract class NuGetRequestHandler<TRequest, TResponse> : IRequestHandler
        where TResponse : class
    {
        public CancellationToken CancellationToken { get; } = CancellationToken.None;

        public async Task HandleResponseAsync(
            IConnection connection,
            Message request,
            IResponseHandler responseHandler,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(responseHandler);
            cancellationToken.ThrowIfCancellationRequested();

            TRequest payload = MessageUtilities.DeserializePayload<TRequest>(request);
            TResponse response = await HandleRequestAsync(payload, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await responseHandler
                .SendResponseAsync(request, response, CancellationToken.None)
                .ConfigureAwait(false);
        }

        protected abstract ValueTask<TResponse> HandleRequestAsync(
            TRequest request,
            CancellationToken cancellationToken
        );
    }

    private sealed record NuGetResourceParseResult(
        NuGetResourceParseStatus Status,
        CanonicalResourceIdentity? Resource
    )
    {
        public static NuGetResourceParseResult Success(CanonicalResourceIdentity resource) =>
            new(NuGetResourceParseStatus.Success, resource);

        public static NuGetResourceParseResult NoCredential() =>
            new(NuGetResourceParseStatus.NoCredential, Resource: null);

        public static NuGetResourceParseResult ProtocolViolation() =>
            new(NuGetResourceParseStatus.ProtocolViolation, Resource: null);
    }

    private sealed record AzureArtifactsNuGetResourceShape(
        string Organization,
        string? Project,
        string Feed
    );

    private enum NuGetResourceParseStatus
    {
        Success,
        NoCredential,
        ProtocolViolation,
    }
}
