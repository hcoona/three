using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol.Plugins;
using System.Reflection;
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
        IdentityFlow.DeviceCode,
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
        Assert.Equal(IdentityFlow.DeviceCode, capturedRequest.IdentityFlow);
        Assert.Equal(InteractivePolicy.HostToolAllows, capturedRequest.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, capturedRequest.AcquisitionMode);
        Assert.Equal(expectedRetryExtension, capturedRequest.ExtensionData["nuget.isRetry"]);
    }

    [Fact]
    public async Task AuthenticationRequestTokenFlowsThroughAcquisitionAndResponseSending()
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
        Assert.Equal(requestCancellation.Token, responseHandler.CancellationToken);
        var response = Assert.IsType<GetAuthenticationCredentialsResponse>(
            responseHandler.Payload
        );
        Assert.Equal(MessageResponseCode.Success, response.ResponseCode);
        Assert.Equal("AzureDevOps", response.Username);
        Assert.Equal("fake-secret-request-token", response.Password);
        Assert.Equal(1, responseHandler.SendCount);

        requestCancellation.Cancel();
        Assert.True(acquisition.CancellationToken.IsCancellationRequested);
        Assert.True(responseHandler.CancellationToken.IsCancellationRequested);
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
            await handling.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(requestCancellation.Token, acquisition.CancellationToken);
        Assert.True(acquisition.CancellationObserved);
        Assert.Equal(0, responseHandler.SendCount);
    }

    private static IRequestHandler GetAuthenticationCredentialsHandler(
        NuGetPluginAdapter adapter
    )
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
                new Uri(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
                ),
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

    private sealed class RequestTokenCapturingAcquisitionService
        : ICredentialAcquisitionService
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
}
