using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class KeyringHelperAdapterTests
{
    private static readonly DateTimeOffset ExpiresAt = new(
        2030,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CredentialsModeForModernFeedWritesKeyringCredentialPairOnly()
    {
        var provider = new CapturingIdentityProvider();
        KeyringHelperRequest request = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: "User@Example.com",
            KeyringHelperMode.Credentials);

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.True(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal("AzureDevOps\nphase11-secret\n", result.ProtocolStdout);
        Assert.Empty(result.HumanStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(1, provider.InvocationCount);

        CredentialRequest credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal(CredentialEcosystem.Python, credentialRequest.Ecosystem);
        Assert.Equal(CredentialKind.BasicPassword, credentialRequest.CredentialKind);
        Assert.Equal(TokenAudience.AzureArtifacts, credentialRequest.RequestedAudience);
        Assert.Equal("User@Example.com", credentialRequest.AccountHint);
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Null(credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
    }

    [Fact]
    public void PasswordModeForLegacyFeedWritesOnlyPassword()
    {
        var provider = new CapturingIdentityProvider();
        KeyringHelperRequest request = CreateRequest(
            "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password);

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);

        CredentialRequest credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Equal("project", credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload/", null)]
    [InlineData("https://dev.azure.com/org/project/_packaging/feed/pypi/upload/", "project")]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/upload/",
        "project")]
    public void UploadEndpointFeedsAreAcceptedForPublishing(string service, string? project)
    {
        var provider = new CapturingIdentityProvider();
        KeyringHelperRequest request = CreateRequest(
            service,
            username: null,
            KeyringHelperMode.Credentials);

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).Skip(1).ToArray(),
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("AzureDevOps\nphase11-secret\n", result.ProtocolStdout);

        CredentialRequest credentialRequest = Assert.Single(provider.Requests);
        Assert.Equal("org", credentialRequest.Resource.Organization);
        Assert.Equal(project, credentialRequest.Resource.Project);
        Assert.Equal("feed", credentialRequest.Resource.Feed);
    }

    [Fact]
    public void SharedHostEntrypointAcceptsFullHelperCommand()
    {
        var provider = new CapturingIdentityProvider();
        KeyringHelperRequest request = CreateRequest(
            "https://dev.azure.com/org/project/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Credentials);

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).ToArray(),
            executablePath: "/usr/local/bin/azureauth-credprovider",
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("AzureDevOps\nphase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void DedicatedShimEntrypointAcceptsPythonBuiltFullHelperCommand()
    {
        var provider = new CapturingIdentityProvider();
        KeyringHelperRequest request = CreateRequest(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            username: null,
            KeyringHelperMode.Password);

        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(request).ToArray(),
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.Success, result.Outcome.Result.ExitCode);
        Assert.Equal("phase11-secret\n", result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(1, provider.InvocationCount);
    }

    [Fact]
    public void DedicatedShimEntrypointRoutesMalformedCommandToProtocolFailure()
    {
        AdapterRunResult result = Execute(
            [
                "set",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "--mode",
                "creds",
            ]);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedServiceHostReturnsNoCredentialWithoutInvokingCredentialCore()
    {
        var provider = new CapturingIdentityProvider();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://example.com/org/_packaging/feed/pypi/simple/",
                "--mode",
                "creds",
            ],
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.NoCredential, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void MalformedAzureServicePathFailsClosedWithoutInvokingCredentialCore()
    {
        var provider = new CapturingIdentityProvider();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/npm",
                "--mode",
                "creds",
            ],
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("pkgs.dev.azure.com", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void OldProtocolMajorFailsClosedWithoutInvokingCredentialCore()
    {
        var provider = new CapturingIdentityProvider();
        AdapterRunResult result = Execute(
            [
                "get",
                "--protocol-version",
                "1",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "--mode",
                "password",
            ],
            credentialCore: new CredentialCoreService(provider));

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=ProtocolViolation", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void CredentialUnavailableSuppressesStdoutAndWritesSafeDiagnostic()
    {
        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(
                CreateRequest(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                    username: null,
                    KeyringHelperMode.Credentials)).Skip(1).ToArray(),
            credentialCore: CreateCredentialCore(TokenExchangeResult.Unavailable));

        Assert.Equal(AdapterHostExitCode.ConfigurationError, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=TokenExchangeUnavailable", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalCredentialFailureSuppressesStdoutAndWritesSafeDiagnostic()
    {
        AdapterRunResult result = Execute(
            KeyringHelperV2.BuildArguments(
                CreateRequest(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                    username: null,
                    KeyringHelperMode.Password)).Skip(1).ToArray(),
            credentialCore: CreateCredentialCore(TokenExchangeResult.Failed));

        Assert.Equal(AdapterHostExitCode.Fatal, result.Outcome.Result.ExitCode);
        Assert.False(result.Outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, result.ProtocolStdout);
        Assert.Contains("code=TokenExchangeFailed", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveProtocolInvocationRecognizesDedicatedShimEvenWithBadArgs()
    {
        bool resolved = KeyringHelperAdapter.TryResolveProtocolInvocation(
            "/usr/local/bin/python-keyring",
            ["set"],
            out AdapterInvocationContext? context);

        Assert.True(resolved);
        Assert.NotNull(context);
        Assert.Equal(AdapterProtocol.KeyringHelper, context.Protocol);
        Assert.Equal(["set"], context.PayloadArguments);
    }

    private static AdapterRunResult Execute(
        string[] args,
        string executablePath = "/usr/local/bin/python-keyring",
        CredentialCoreService? credentialCore = null)
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            SecretRedactor.Empty);
        AdapterHostExecutionOutcome outcome = new KeyringHelperAdapter(
            credentialCore).Execute(
                executablePath,
                args,
                protocolStdout,
                humanStdout,
                diagnosticRouter);

        return new AdapterRunResult(
            outcome,
            protocolStdout.ToString(),
            humanStdout.ToString(),
            stderr.ToString());
    }

    private static KeyringHelperRequest CreateRequest(
        string service,
        string? username,
        KeyringHelperMode mode)
    {
        return new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Username = username,
            Mode = mode,
        };
    }

    private static CredentialCoreService CreateCredentialCore(TokenExchangeResult exchangeResult)
    {
        return new CredentialCoreService(
            new CapturingIdentityProvider(),
            diagnosticRouter: null,
            derivedCredentialCache: null,
            tokenExchange: new FixedTokenExchange(exchangeResult));
    }

    private sealed class CapturingIdentityProvider : IIdentityProvider
    {
        public int InvocationCount { get; private set; }

        public List<CredentialRequest> Requests { get; } = [];

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            InvocationCount++;
            Requests.Add(request);
            return new IdentityMaterial
            {
                Account = request.AccountHint ?? "default@org.example",
                Tenant = "tenant",
                Secret = "phase11-secret",
                AccessToken = "phase11-token",
                ExpiresAt = ExpiresAt,
            };
        }
    }

    private sealed class FixedTokenExchange(TokenExchangeResult exchangeResult) : ITokenExchange
    {
        public TokenExchangeResult Exchange(
            CredentialRequest request,
            IdentityMaterial identity,
            CacheKey cacheKey)
        {
            return exchangeResult;
        }
    }

    private sealed record AdapterRunResult(
        AdapterHostExecutionOutcome Outcome,
        string ProtocolStdout,
        string HumanStdout,
        string Stderr);
}
