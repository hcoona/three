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
}
