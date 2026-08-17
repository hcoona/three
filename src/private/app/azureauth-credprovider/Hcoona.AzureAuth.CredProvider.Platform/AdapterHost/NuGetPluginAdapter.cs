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
        using var lifetime = new PluginLifetimeCoordinator();
        AddRequestHandler(requestHandlers, MessageMethod.Close, lifetime.CloseRequestHandler);
        AddRequestHandler(
            requestHandlers,
            MessageMethod.MonitorNuGetProcessExit,
            lifetime.MonitorNuGetProcessExitRequestHandler
        );
        using IPlugin plugin = await PluginFactory
            .CreateFromCurrentProcessAsync(
                requestHandlers,
                ConnectionOptions.CreateDefault(),
                cancellationToken
            )
            .ConfigureAwait(false);

        lifetime.Attach(plugin);
        await lifetime.WaitForCloseAsync(cancellationToken).ConfigureAwait(false);
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

    internal async ValueTask<GetAuthenticationCredentialsResponse>
        HandleGetAuthenticationCredentialsAsync(
        GetAuthenticationCredentialsRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        NuGetResourceParseResult parseResult = NuGetResourceSourceParser.Parse(request.Uri);
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

    private sealed class PluginLifetimeCoordinator : IDisposable
    {
        private readonly TaskCompletionSource beginClose = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource endClose = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<IPlugin> pluginReady = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly CloseHandler closeRequestHandler;
        private readonly MonitorHandler monitorNuGetProcessExitRequestHandler;
        private IPlugin? plugin;

        public PluginLifetimeCoordinator()
        {
            closeRequestHandler = new CloseHandler(pluginReady.Task);
            monitorNuGetProcessExitRequestHandler = new MonitorHandler(pluginReady.Task);
        }

        public IRequestHandler CloseRequestHandler => closeRequestHandler;

        public IRequestHandler MonitorNuGetProcessExitRequestHandler =>
            monitorNuGetProcessExitRequestHandler;

        public void Attach(IPlugin value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Interlocked.CompareExchange(ref plugin, value, comparand: null) is not null)
            {
                throw new InvalidOperationException("A NuGet plugin is already attached.");
            }

            value.BeforeClose += OnBeforeClose;
            value.Closed += OnClosed;

            if (
                value.Connection is Connection connection
                && connection.State is ConnectionState.Closing or ConnectionState.Closed
            )
            {
                beginClose.TrySetResult();
                if (connection.State == ConnectionState.Closed)
                {
                    endClose.TrySetResult();
                }
            }

            pluginReady.TrySetResult(value);
        }

        public async Task WaitForCloseAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(() =>
                {
                    beginClose.TrySetCanceled(cancellationToken);
                    endClose.TrySetCanceled(cancellationToken);
                });

            await beginClose.Task.ConfigureAwait(false);
            using var shutdownTimeout = new CancellationTokenSource(PluginShutdownTimeout);
            using CancellationTokenRegistration shutdownRegistration =
                shutdownTimeout.Token.Register(() =>
                    endClose.TrySetCanceled(shutdownTimeout.Token)
                );
            await endClose.Task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            pluginReady.TrySetCanceled();
            IPlugin? attachedPlugin = plugin;
            if (attachedPlugin is not null)
            {
                attachedPlugin.BeforeClose -= OnBeforeClose;
                attachedPlugin.Closed -= OnClosed;
            }

            monitorNuGetProcessExitRequestHandler.Dispose();
        }

        private void OnBeforeClose(object? sender, EventArgs eventArgs) =>
            beginClose.TrySetResult();

        private void OnClosed(object? sender, EventArgs eventArgs)
        {
            beginClose.TrySetResult();
            endClose.TrySetResult();
        }

        private sealed class CloseHandler(Task<IPlugin> pluginReady) : IRequestHandler
        {
            public CancellationToken CancellationToken => CancellationToken.None;

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

                IPlugin plugin = await pluginReady
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                plugin.Close();
            }
        }

        private sealed class MonitorHandler(Task<IPlugin> pluginReady)
            : IRequestHandler,
                IDisposable
        {
            private readonly object sync = new();
            private MonitorNuGetProcessExitRequestHandler? handler;

            public CancellationToken CancellationToken => CancellationToken.None;

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

                IPlugin plugin = await pluginReady
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                MonitorNuGetProcessExitRequestHandler resolvedHandler;
                lock (sync)
                {
                    resolvedHandler = handler ??= new MonitorNuGetProcessExitRequestHandler(plugin);
                }

                await resolvedHandler
                    .HandleResponseAsync(connection, request, responseHandler, cancellationToken)
                    .ConfigureAwait(false);
            }

            public void Dispose()
            {
                lock (sync)
                {
                    handler?.Dispose();
                    handler = null;
                }
            }
        }
    }

    internal static CredentialRequestV2 CreateCredentialRequest(
        CanonicalResourceIdentity resource,
        GetAuthenticationCredentialsRequest request
    )
    {
        bool interactionAllowed = !request.IsNonInteractive;

        return new CredentialRequestV2
        {
            Ecosystem = CredentialEcosystem.NuGet,
            Operation = CredentialOperation.Get,
            Resource = resource,
            ServiceIdentity = DefaultServiceIdentity,
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = CredentialKind.NuGetPluginCredential,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
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

        protected override bool ShouldReportProgress(GetAuthenticationCredentialsRequest request) =>
            !request.IsNonInteractive;
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
            TResponse response;
            if (ShouldReportProgress(payload))
            {
                using AutomaticProgressReporter progressReporter = AutomaticProgressReporter.Create(
                    connection,
                    request,
                    PluginConstants.ProgressInterval,
                    cancellationToken
                );
                response = await HandleRequestAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                response = await HandleRequestAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await responseHandler
                .SendResponseAsync(request, response, CancellationToken.None)
                .ConfigureAwait(false);
        }

        protected virtual bool ShouldReportProgress(TRequest request) => false;

        protected abstract ValueTask<TResponse> HandleRequestAsync(
            TRequest request,
            CancellationToken cancellationToken
        );
    }

}
