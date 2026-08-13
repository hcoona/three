using System.Reflection;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol.Plugins;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class NuGetPluginAdapterTests
{
    [Theory]
    [InlineData("-Plugin")]
    [InlineData("-P")]
    public void DescriptorResolvesNuGetPluginEntrypoints(string pluginArgument)
    {
        bool resolved = NuGetPluginAdapter.TryResolveProtocolInvocation(
            "azureauth-credprovider",
            [pluginArgument],
            out AdapterInvocationContext? context
        );

        Assert.True(resolved);
        Assert.NotNull(context);
        Assert.True(context.IsProtocolInvocation);
        Assert.Equal(AdapterProtocol.NuGetPlugin, context.Protocol);
        Assert.Empty(context.PayloadArguments);
    }

    [Fact]
    public void InitializeReturnsSuccess()
    {
        var request = new InitializeRequest("7.6.0", "en-US", TimeSpan.FromSeconds(30));

        InitializeResponse response = NuGetPluginAdapter.HandleInitialize(request);

        Assert.Equal(MessageResponseCode.Success, response.ResponseCode);
    }

    [Fact]
    public void SourceAgnosticOperationClaimsAdvertiseAuthentication()
    {
        var request = new GetOperationClaimsRequest((string)null!, (JObject)null!);

        GetOperationClaimsResponse response = NuGetPluginAdapter.HandleGetOperationClaims(request);

        Assert.Equal([OperationClaim.Authentication], response.Claims);
    }

    [Fact]
    public void SourceSpecificOperationClaimsDoNotAdvertiseAuthentication()
    {
        var request = new GetOperationClaimsRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json",
            new JObject()
        );

        GetOperationClaimsResponse response = NuGetPluginAdapter.HandleGetOperationClaims(request);

        Assert.Empty(response.Claims);
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json")]
    [InlineData("https://dev.azure.com/org/project/_packaging/feed/nuget/v3/index.json")]
    [InlineData("https://org.pkgs.visualstudio.com/_packaging/feed/nuget/v3/index.json")]
    public void AuthenticationRequestReturnsBasicCredentialsForAzureArtifactsNuGetSource(
        string packageSource
    )
    {
        NuGetPluginAdapter adapter = CreateTestAdapter();
        var request = new GetAuthenticationCredentialsRequest(
            new Uri(packageSource),
            isRetry: false,
            isNonInteractive: false,
            canShowDialog: false
        );

        GetAuthenticationCredentialsResponse response = adapter.HandleGetAuthenticationCredentials(
            request
        );

        Assert.Equal(MessageResponseCode.Success, response.ResponseCode);
        Assert.Equal("AzureDevOps", response.Username);
        Assert.StartsWith("fake-secret-", response.Password, StringComparison.Ordinal);
        Assert.Equal(["Basic"], response.AuthenticationTypes);
        Assert.Null(response.Message);
    }

    [Fact]
    public void ExplicitTestScaffoldCanServeNonInteractiveRequest()
    {
        NuGetPluginAdapter adapter = CreateTestAdapter();
        var request = new GetAuthenticationCredentialsRequest(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            isRetry: false,
            isNonInteractive: true,
            canShowDialog: false
        );

        GetAuthenticationCredentialsResponse response = adapter.HandleGetAuthenticationCredentials(
            request
        );

        Assert.Equal(MessageResponseCode.Success, response.ResponseCode);
        Assert.Equal("AzureDevOps", response.Username);
        Assert.StartsWith("fake-secret-", response.Password, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        true,
        false,
        IdentityFlow.InteractiveBrowser,
        InteractivePolicy.Never,
        AcquisitionMode.SilentOnly
    )]
    [InlineData(
        true,
        true,
        IdentityFlow.InteractiveBrowser,
        InteractivePolicy.Never,
        AcquisitionMode.SilentOnly
    )]
    [InlineData(
        false,
        true,
        IdentityFlow.InteractiveBrowser,
        InteractivePolicy.HostToolAllows,
        AcquisitionMode.InteractionAllowed
    )]
    [InlineData(
        false,
        false,
        IdentityFlow.InteractiveBrowser,
        InteractivePolicy.HostToolAllows,
        AcquisitionMode.InteractionAllowed
    )]
    public void AuthenticationRequestMapsNuGetInteractionSignals(
        bool isNonInteractive,
        bool canShowDialog,
        IdentityFlow expectedIdentityFlow,
        InteractivePolicy expectedInteractivePolicy,
        AcquisitionMode expectedAcquisitionMode
    )
    {
        var acquisitionService = new CapturingAcquisitionService();
        var adapter = new NuGetPluginAdapter(acquisitionService);
        var request = new GetAuthenticationCredentialsRequest(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            isRetry: true,
            isNonInteractive,
            canShowDialog
        );

        adapter.HandleGetAuthenticationCredentials(request);

        CredentialRequestV2 capturedRequest = Assert.IsType<CredentialRequestV2>(
            acquisitionService.Request
        );
        Assert.Equal(expectedIdentityFlow, capturedRequest.IdentityFlow);
        Assert.Equal(expectedInteractivePolicy, capturedRequest.InteractivePolicy);
        Assert.Equal(expectedAcquisitionMode, capturedRequest.AcquisitionMode);
        Assert.Equal("true", capturedRequest.ExtensionData["nuget.isRetry"]);
    }

    [Fact]
    public void UnsupportedHostReturnsNotFound()
    {
        NuGetPluginAdapter adapter = CreateTestAdapter();
        var request = new GetAuthenticationCredentialsRequest(
            new Uri("https://api.nuget.org/v3/index.json"),
            isRetry: false,
            isNonInteractive: false,
            canShowDialog: false
        );

        GetAuthenticationCredentialsResponse response = adapter.HandleGetAuthenticationCredentials(
            request
        );

        Assert.Equal(MessageResponseCode.NotFound, response.ResponseCode);
        Assert.Null(response.Username);
        Assert.Null(response.Password);
    }

    [Fact]
    public void AzureArtifactsSourceWithWrongFeedSuffixReturnsSafeError()
    {
        NuGetPluginAdapter adapter = CreateTestAdapter();
        var request = new GetAuthenticationCredentialsRequest(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
            isRetry: false,
            isNonInteractive: false,
            canShowDialog: false
        );

        GetAuthenticationCredentialsResponse response = adapter.HandleGetAuthenticationCredentials(
            request
        );

        Assert.Equal(MessageResponseCode.Error, response.ResponseCode);
        Assert.Null(response.Username);
        Assert.Null(response.Password);
        Assert.Contains("NuGet source URI", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetCredentialsAndSetLogLevelAreNoOpSuccesses()
    {
        NuGetPluginAdapter adapter = CreateTestAdapter();

        SetCredentialsResponse credentialsResponse = NuGetPluginAdapter.HandleSetCredentials(
            new SetCredentialsRequest(
                "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json",
                proxyUsername: null,
                proxyPassword: null,
                username: "unused",
                password: "unused"
            )
        );
        SetLogLevelResponse logLevelResponse = NuGetPluginAdapter.HandleSetLogLevel(
            new SetLogLevelRequest(LogLevel.Information)
        );

        Assert.Equal(MessageResponseCode.Success, credentialsResponse.ResponseCode);
        Assert.Equal(MessageResponseCode.Success, logLevelResponse.ResponseCode);
    }

    private static NuGetPluginAdapter CreateTestAdapter() =>
        new(new SuccessfulTestAcquisitionService());

    private sealed class SuccessfulTestAcquisitionService : ICredentialAcquisitionService
    {
        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "fake-secret-nuget",
                    DiagnosticsCorrelationId = "nuget-adapter-test",
                }
            );
    }

    private sealed class CapturingAcquisitionService : ICredentialAcquisitionService
    {
        public CredentialRequestV2? Request { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.NoCredential,
                    DiagnosticsCorrelationId = "nuget-request-capture",
                }
            );
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void AuthenticationRequestPreservesFixedMetadataAndExactInteractionExtensions(
        bool isNonInteractive,
        bool canShowDialog
    )
    {
        var acquisitionService = new CapturingAcquisitionService();
        var adapter = new NuGetPluginAdapter(acquisitionService);
        var request = new GetAuthenticationCredentialsRequest(
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            isRetry: true,
            isNonInteractive,
            canShowDialog
        );

        adapter.HandleGetAuthenticationCredentials(request);

        CredentialRequestV2 capturedRequest = Assert.IsType<CredentialRequestV2>(
            acquisitionService.Request
        );
        Assert.Equal(CredentialEcosystem.NuGet, capturedRequest.Ecosystem);
        Assert.Equal(CredentialOperation.Get, capturedRequest.Operation);
        Assert.Equal(TokenAudience.AzureArtifacts, capturedRequest.RequestedAudience);
        Assert.Equal(CredentialKind.NuGetPluginCredential, capturedRequest.CredentialKind);
        Assert.Equal(CachePolicyMode.ProductPersistentCacheDisabled, capturedRequest.CachePolicy);
        CiContext ciContext = Assert.IsType<CiContext>(capturedRequest.CiContext);
        Assert.False(ciContext.ExplicitCiMode);
        Assert.False(ciContext.AllowsPersistentWrites);
        Assert.Equal(
            canShowDialog ? "true" : "false",
            capturedRequest.ExtensionData["nuget.canShowDialog"]
        );
        Assert.Equal(
            isNonInteractive ? "true" : "false",
            capturedRequest.ExtensionData["nuget.isNonInteractive"]
        );
        Assert.Equal("true", capturedRequest.ExtensionData["nuget.isRetry"]);
    }

    [Theory]
    [InlineData(false, "false")]
    [InlineData(true, "true")]
    public void AuthenticationRequestPreservesDefaultIdentityCanonicalResourceAndRetryExtension(
        bool isRetry,
        string expectedRetryExtension
    )
    {
        var acquisitionService = new CapturingAcquisitionService();
        var adapter = new NuGetPluginAdapter(acquisitionService);
        var source = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json");
        var request = new GetAuthenticationCredentialsRequest(
            source,
            isRetry,
            isNonInteractive: false,
            canShowDialog: false
        );

        GetAuthenticationCredentialsResponse response = adapter.HandleGetAuthenticationCredentials(
            request
        );

        Assert.Equal(MessageResponseCode.NotFound, response.ResponseCode);
        CredentialRequestV2 capturedRequest = Assert.IsType<CredentialRequestV2>(
            acquisitionService.Request
        );
        Assert.Equal("default", capturedRequest.ServiceIdentity);
        Assert.Null(capturedRequest.AccountHint);
        CanonicalResourceIdentity resource = capturedRequest.Resource;
        Assert.Equal("pkgs.dev.azure.com", resource.AzureDevOpsHost);
        Assert.Equal("org", resource.Organization);
        Assert.Null(resource.Project);
        Assert.Equal("feed", resource.Feed);
        Assert.Null(resource.Repository);
        Assert.Equal(source, resource.ServiceEndpoint);
        Assert.Equal(IdentityFlow.InteractiveBrowser, capturedRequest.IdentityFlow);
        Assert.Equal(InteractivePolicy.HostToolAllows, capturedRequest.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, capturedRequest.AcquisitionMode);
        Assert.Equal(expectedRetryExtension, capturedRequest.ExtensionData["nuget.isRetry"]);
    }

    [Fact]
    public async Task AuthenticationRequestTokenFlowsThroughAcquisitionButNotCommittedEmission()
    {
        var acquisition = new RequestTokenCapturingAcquisitionService();
        var adapter = new NuGetPluginAdapter(acquisition);
        IRequestHandler handler = GetAuthenticationCredentialsHandler(adapter);
        var responseHandler = new CapturingResponseHandler();
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        Message request = CreateAuthenticationCredentialsMessage("token-flow");

        await handler.HandleResponseAsync(
            CreateUnusedConnection(),
            request,
            responseHandler,
            requestCancellation.Token
        );

        Assert.Equal(requestCancellation.Token, acquisition.CancellationToken);
        Assert.Equal(CancellationToken.None, responseHandler.CancellationToken);
        var response = Assert.IsType<GetAuthenticationCredentialsResponse>(responseHandler.Payload);
        Assert.Equal(MessageResponseCode.Success, response.ResponseCode);
        Assert.Equal("AzureDevOps", response.Username);
        Assert.Equal("fake-secret-request-token", response.Password);
        Assert.Equal(1, responseHandler.SendCount);

        requestCancellation.Cancel();
        Assert.True(acquisition.CancellationToken.IsCancellationRequested);
        Assert.False(responseHandler.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task AuthenticationRequestCancellationStopsBeforeResponseSending()
    {
        var acquisition = new CancelableAcquisitionService();
        var adapter = new NuGetPluginAdapter(acquisition);
        IRequestHandler handler = GetAuthenticationCredentialsHandler(adapter);
        var responseHandler = new CapturingResponseHandler();
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        Message request = CreateAuthenticationCredentialsMessage("canceled-flow");

        Task handling = handler.HandleResponseAsync(
            CreateUnusedConnection(),
            request,
            responseHandler,
            requestCancellation.Token
        );
        await acquisition.Entered.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        requestCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
        );
        Assert.Equal(requestCancellation.Token, acquisition.CancellationToken);
        Assert.True(acquisition.CancellationObserved);
        Assert.Equal(0, responseHandler.SendCount);
    }

    [Fact]
    public async Task PendingResponseCancellationAndSendFailurePreservesFailure()
    {
        var adapter = new NuGetPluginAdapter(new RequestTokenCapturingAcquisitionService());
        IRequestHandler handler = GetAuthenticationCredentialsHandler(adapter);
        var responseHandler = new CoordinatedFailingResponseHandler();
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        Message request = CreateAuthenticationCredentialsMessage("failed-emission");

        Task handling = handler.HandleResponseAsync(
            CreateUnusedConnection(),
            request,
            responseHandler,
            requestCancellation.Token
        );
        await responseHandler.Entered.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        requestCancellation.Cancel();
        responseHandler.Release();

        await Assert.ThrowsAsync<IOException>(async () =>
            await handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
        );
        Assert.Equal(CancellationToken.None, responseHandler.CancellationToken);
        Assert.Equal(1, responseHandler.SendCount);
        Assert.Equal(0, responseHandler.CompletedEmissionCount);
    }

    [Fact]
    public async Task AuthenticationRequestCancellationAfterCompleteEmissionReturnsSuccess()
    {
        var adapter = new NuGetPluginAdapter(new RequestTokenCapturingAcquisitionService());
        IRequestHandler handler = GetAuthenticationCredentialsHandler(adapter);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var responseHandler = new CancelAfterCompleteResponseHandler(requestCancellation);
        Message request = CreateAuthenticationCredentialsMessage("completed-emission");

        await handler
            .HandleResponseAsync(
                CreateUnusedConnection(),
                request,
                responseHandler,
                requestCancellation.Token
            )
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(requestCancellation.IsCancellationRequested);
        Assert.Equal(CancellationToken.None, responseHandler.CancellationToken);
        Assert.Equal(1, responseHandler.SendCount);
        Assert.Equal(1, responseHandler.CompletedEmissionCount);
    }

    private static IRequestHandler GetAuthenticationCredentialsHandler(NuGetPluginAdapter adapter)
    {
        RequestHandlers handlers = adapter.CreateRequestHandlers();
        Assert.True(
            handlers.TryGet(
                MessageMethod.GetAuthenticationCredentials,
                out IRequestHandler? handler
            )
        );
        return Assert.IsAssignableFrom<IRequestHandler>(handler);
    }

    private static Message CreateAuthenticationCredentialsMessage(string requestId) =>
        MessageUtilities.Create(
            requestId,
            MessageType.Request,
            MessageMethod.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsRequest(
                new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
                isRetry: false,
                isNonInteractive: true,
                canShowDialog: false
            )
        );

    private static IConnection CreateUnusedConnection() =>
        DispatchProxy.Create<IConnection, UnusedConnectionProxy>();

    public class UnusedConnectionProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("The request handler must not use the connection.");
    }

    private sealed class CapturingResponseHandler : IResponseHandler
    {
        public CancellationToken CancellationToken { get; private set; }

        public object? Payload { get; private set; }

        public int SendCount { get; private set; }

        public Task SendResponseAsync<TPayload>(
            Message request,
            TPayload payload,
            CancellationToken cancellationToken
        )
            where TPayload : class
        {
            CancellationToken = cancellationToken;
            Payload = payload;
            SendCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CoordinatedFailingResponseHandler : IResponseHandler
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public CancellationToken CancellationToken { get; private set; }

        public int CompletedEmissionCount { get; private set; }

        public Task Entered => entered.Task;

        public int SendCount { get; private set; }

        public async Task SendResponseAsync<TPayload>(
            Message request,
            TPayload payload,
            CancellationToken cancellationToken
        )
            where TPayload : class
        {
            CancellationToken = cancellationToken;
            SendCount++;
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Response emission failed before completion.");
        }

        public void Release()
        {
            release.TrySetResult();
        }
    }

    private sealed class CancelAfterCompleteResponseHandler(
        CancellationTokenSource requestCancellation
    ) : IResponseHandler
    {
        public CancellationToken CancellationToken { get; private set; }

        public int CompletedEmissionCount { get; private set; }

        public int SendCount { get; private set; }

        public Task SendResponseAsync<TPayload>(
            Message request,
            TPayload payload,
            CancellationToken cancellationToken
        )
            where TPayload : class
        {
            CancellationToken = cancellationToken;
            SendCount++;
            CompletedEmissionCount++;
            requestCancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RequestTokenCapturingAcquisitionService : ICredentialAcquisitionService
    {
        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            CancellationToken = cancellationToken;
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "fake-secret-request-token",
                    DiagnosticsCorrelationId = "nuget-request-token",
                }
            );
        }
    }

    private sealed class CancelableAcquisitionService : ICredentialAcquisitionService
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public CancellationToken CancellationToken { get; private set; }

        public bool CancellationObserved { get; private set; }

        public Task Entered => entered.Task;

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            CancellationToken = cancellationToken;
            entered.TrySetResult();
            return new ValueTask<CredentialResult>(WaitForCancellationAsync(cancellationToken));
        }

        private async Task<CredentialResult> WaitForCancellationAsync(
            CancellationToken cancellationToken
        )
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The request was expected to be canceled.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The underscores separate the protocol condition from the expected result."
    )]
    public async Task GetCredentials_WhenBrowserIsBlocked_ReportsProgressBeforeFinalResponse()
    {
        const string requestId = "33333333-3333-3333-3333-333333333333";
        var acquisition = new GatedInteractiveBrowserAcquisitionService();
        var adapter = new NuGetPluginAdapter(acquisition);
        IRequestHandler handler = GetAuthenticationCredentialsHandler(adapter);
        (IConnection connection, ProgressRecordingConnectionProxy progressRecorder) =
            CreateProgressRecordingConnection();
        var responseHandler = new OrderedCapturingResponseHandler(progressRecorder);
        Message request = MessageUtilities.Create(
            requestId,
            MessageType.Request,
            MessageMethod.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsRequest(
                new Uri("https://pkgs.dev.azure.com/contoso/_packaging/Feed/nuget/v3/index.json"),
                isRetry: false,
                isNonInteractive: false,
                canShowDialog: true
            )
        );
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(TimeSpan.FromSeconds(30), connection.Options.RequestTimeout);
        Assert.True(PluginConstants.ProgressInterval < connection.Options.RequestTimeout);

        Task handling = handler.HandleResponseAsync(
            connection,
            request,
            responseHandler,
            cancellationToken
        );
        await acquisition.Entered.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        CredentialRequestV2 capturedRequest = Assert.IsType<CredentialRequestV2>(
            acquisition.Request
        );
        Assert.Equal(IdentityFlow.InteractiveBrowser, capturedRequest.IdentityFlow);
        Assert.Equal(InteractivePolicy.HostToolAllows, capturedRequest.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, capturedRequest.AcquisitionMode);
        Assert.Equal("false", capturedRequest.ExtensionData["nuget.isNonInteractive"]);
        Assert.Equal("true", capturedRequest.ExtensionData["nuget.canShowDialog"]);

        await progressRecorder.FirstProgressCompleted.WaitAsync(
            TimeSpan.FromSeconds(20),
            cancellationToken
        );

        Assert.False(acquisition.Completed.IsCompleted);
        Message progress = Assert.Single(progressRecorder.Messages);
        Assert.Equal(MessageType.Progress, progress.Type);
        Assert.Equal(requestId, progress.RequestId);
        Assert.False(responseHandler.Entered.IsCompleted);
        Assert.Equal(0, responseHandler.SendCount);

        acquisition.Release();
        await responseHandler.Entered.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Equal(0, responseHandler.ActiveConnectionSendsAtBegin);
        Assert.Equal(1, responseHandler.ProgressCountAtBegin);
        Assert.True(
            progressRecorder.FirstProgressCompletedSequence < responseHandler.ResponseBeginSequence
        );

        responseHandler.Release();
        await handling.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.True(acquisition.Completed.IsCompletedSuccessfully);
        Assert.Equal(1, responseHandler.SendCount);
        Assert.Equal(
            responseHandler.ProgressCountAtBegin,
            responseHandler.ProgressCountAtCompletion
        );
        Assert.Equal(responseHandler.ProgressCountAtBegin, progressRecorder.ProgressCount);
        Assert.Equal(0, progressRecorder.ActiveSendCount);
        var response = Assert.IsType<GetAuthenticationCredentialsResponse>(responseHandler.Payload);
        Assert.Equal(MessageResponseCode.Success, response.ResponseCode);
        Assert.Equal(
            Hcoona
                .AzureAuth
                .CredProvider
                .Platform
                .TokenMaterialization
                .CredentialFormPolicy
                .NuGetSessionTokenUsername,
            response.Username
        );
        Assert.Equal("opaque-session-token-7c9e2d41", response.Password);
        Assert.Equal(["Basic"], response.AuthenticationTypes);
        Assert.Null(response.Message);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The underscores separate the protocol condition from the expected result."
    )]
    public async Task GetCredentials_WhenDialogIsUnavailable_DoesNotReportProgress()
    {
        var adapter = new NuGetPluginAdapter(new RequestTokenCapturingAcquisitionService());
        IRequestHandler handler = GetAuthenticationCredentialsHandler(adapter);
        (IConnection connection, ProgressRecordingConnectionProxy progressRecorder) =
            CreateProgressRecordingConnection();
        var responseHandler = new CapturingResponseHandler();
        Message request = MessageUtilities.Create(
            "44444444-4444-4444-4444-444444444444",
            MessageType.Request,
            MessageMethod.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsRequest(
                new Uri("https://pkgs.dev.azure.com/contoso/_packaging/Feed/nuget/v3/index.json"),
                isRetry: false,
                isNonInteractive: false,
                canShowDialog: false
            )
        );

        await handler.HandleResponseAsync(
            connection,
            request,
            responseHandler,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(progressRecorder.Messages);
        Assert.Equal(1, responseHandler.SendCount);
    }

    private static (
        IConnection Connection,
        ProgressRecordingConnectionProxy Recorder
    ) CreateProgressRecordingConnection()
    {
        IConnection connection = DispatchProxy.Create<
            IConnection,
            ProgressRecordingConnectionProxy
        >();
        var recorder = (ProgressRecordingConnectionProxy)connection;
        recorder.SetRequestTimeout(TimeSpan.FromSeconds(30));
        return (connection, recorder);
    }

    public class ProgressRecordingConnectionProxy : DispatchProxy
    {
        private readonly object sync = new();
        private readonly List<Message> messages = [];
        private readonly TaskCompletionSource firstProgressCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly ConnectionOptions options = ConnectionOptions.CreateDefault();
        private int activeSendCount;
        private long eventSequence;
        private long firstProgressCompletedSequence;

        public int ActiveSendCount => Volatile.Read(ref activeSendCount);

        public Task FirstProgressCompleted => firstProgressCompleted.Task;

        public long FirstProgressCompletedSequence =>
            Volatile.Read(ref firstProgressCompletedSequence);

        public IReadOnlyList<Message> Messages
        {
            get
            {
                lock (sync)
                {
                    return messages.ToArray();
                }
            }
        }

        public int ProgressCount
        {
            get
            {
                lock (sync)
                {
                    return messages.Count(message => message.Type == MessageType.Progress);
                }
            }
        }

        public long NextEventSequence() => Interlocked.Increment(ref eventSequence);

        public void SetRequestTimeout(TimeSpan requestTimeout)
        {
            options.SetRequestTimeout(requestTimeout);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            return targetMethod.Name switch
            {
                "get_Options" => options,
                "get_ProtocolVersion" => options.ProtocolVersion,
                "SendAsync" => SendAsync(
                    Assert.IsType<Message>(args![0]),
                    Assert.IsType<CancellationToken>(args[1])
                ),
                "add_Faulted"
                or "remove_Faulted"
                or "add_MessageReceived"
                or "remove_MessageReceived"
                or "Close"
                or "Dispose" => null,
                _ => throw new InvalidOperationException(
                    $"Unexpected IConnection member: {targetMethod.Name}."
                ),
            };
        }

        private Task SendAsync(Message message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref activeSendCount);
            try
            {
                NextEventSequence();
                lock (sync)
                {
                    messages.Add(message);
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeSendCount);
            }

            if (message.Type == MessageType.Progress)
            {
                long completionSequence = NextEventSequence();
                Interlocked.CompareExchange(
                    ref firstProgressCompletedSequence,
                    completionSequence,
                    comparand: 0
                );
                firstProgressCompleted.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class GatedInteractiveBrowserAcquisitionService : ICredentialAcquisitionService
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task Completed => completed.Task;

        public Task Entered => entered.Task;

        public CredentialRequestV2? Request { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            entered.TrySetResult();
            return new ValueTask<CredentialResult>(CompleteAcquisitionAsync(cancellationToken));
        }

        public void Release()
        {
            release.TrySetResult();
        }

        private async Task<CredentialResult> CompleteAcquisitionAsync(
            CancellationToken cancellationToken
        )
        {
            try
            {
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = Hcoona
                        .AzureAuth
                        .CredProvider
                        .Platform
                        .TokenMaterialization
                        .CredentialFormPolicy
                        .NuGetSessionTokenUsername,
                    Password = "opaque-session-token-7c9e2d41",
                    DiagnosticsCorrelationId = "nuget-interactive-browser-progress",
                };
            }
            finally
            {
                completed.TrySetResult();
            }
        }
    }

    private sealed class OrderedCapturingResponseHandler(
        ProgressRecordingConnectionProxy progressRecorder
    ) : IResponseHandler
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int ActiveConnectionSendsAtBegin { get; private set; }

        public Task Entered => entered.Task;

        public object? Payload { get; private set; }

        public int ProgressCountAtBegin { get; private set; }

        public int ProgressCountAtCompletion { get; private set; }

        public long ResponseBeginSequence { get; private set; }

        public int SendCount { get; private set; }

        public async Task SendResponseAsync<TPayload>(
            Message request,
            TPayload payload,
            CancellationToken cancellationToken
        )
            where TPayload : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendCount++;
            ResponseBeginSequence = progressRecorder.NextEventSequence();
            ActiveConnectionSendsAtBegin = progressRecorder.ActiveSendCount;
            ProgressCountAtBegin = progressRecorder.ProgressCount;
            entered.TrySetResult();

            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            ProgressCountAtCompletion = progressRecorder.ProgressCount;
            Payload = payload;
        }

        public void Release()
        {
            release.TrySetResult();
        }
    }
}
