using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CredentialCoreServiceTests
{
    private static readonly DateTimeOffset CustomProviderExpiresAt = new(
        2030,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void ExecuteAcceptedMvpRequestReturnsSuccessAndValidCacheKey()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest();

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal("AzureDevOps", result.Username);
        Assert.NotNull(result.Password);
        Assert.Null(result.BearerToken);
        Assert.NotNull(result.ExpiresAt);
        Assert.Null(result.Error);
        Assert.True(CorrelationId.TryParse(result.DiagnosticsCorrelationId, out _));

        string account = Assert.IsType<string>(result.Account);
        string tenant = Assert.IsType<string>(result.Tenant);
        CacheKey cacheKey = Assert.IsType<CacheKey>(result.CacheKey);
        Assert.True(CacheKeySchema.IsValid(cacheKey));
        Assert.Equal(CacheKeySchema.Create(request, account, tenant).Value, cacheKey.Value);
    }

    [Fact]
    public void ExecutePatCompatibilityRequestReturnsSuccessAndValidCacheKey()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest(
            flow: IdentityFlow.PatCompatibility,
            kind: CredentialKind.PatCompatibility);
        IdentityMaterial expectedIdentity = new DeterministicFakeIdentityProvider()
            .GetIdentity(request);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal("AzureDevOps", result.Username);
        Assert.Equal(expectedIdentity.Account, result.Account);
        Assert.Equal(expectedIdentity.Tenant, result.Tenant);
        Assert.Equal(expectedIdentity.ExpiresAt, result.ExpiresAt);

        string password = Assert.IsType<string>(result.Password);
        Assert.Equal(expectedIdentity.Secret, password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.Error);
        Assert.True(CorrelationId.TryParse(result.DiagnosticsCorrelationId, out _));

        CacheKey cacheKey = Assert.IsType<CacheKey>(result.CacheKey);
        Assert.True(CacheKeySchema.IsValid(cacheKey));
        Assert.Equal(
            CacheKeySchema.Create(request, expectedIdentity.Account, expectedIdentity.Tenant).Value,
            cacheKey.Value);
    }

    [Fact]
    public void ExecuteCanonicalizesProviderAccountAndTenantBeforeReturningSuccess()
    {
        const string rawAccount = " User@Example.COM ";
        const string rawTenant = "\tTenant-ONE ";
        var service = new CredentialCoreService(
            new StaticIdentityProvider(
                CreateIdentityMaterial(account: rawAccount, tenant: rawTenant)));
        CredentialRequest request = CreateGitRequest();

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal("user@example.com", result.Account);
        Assert.Equal("tenant-one", result.Tenant);
        Assert.NotEqual(rawAccount, result.Account);
        Assert.NotEqual(rawTenant, result.Tenant);

        CacheKey cacheKey = Assert.IsType<CacheKey>(result.CacheKey);
        Assert.Equal(
            CacheKeySchema
                .Create(
                    request,
                    Assert.IsType<string>(result.Account),
                    Assert.IsType<string>(result.Tenant))
                .Value,
            cacheKey.Value);
    }

    [Theory]
    [MemberData(nameof(UnsafeProviderMaterialScenarios))]
    public void ExecuteRejectsProviderMaterialWithProtocolLineBreaksWithoutLeakingMaterial(
        CredentialRequest request,
        IdentityMaterial identity)
    {
        string[] rawProviderValues =
        [
            identity.Account,
            identity.Tenant,
            identity.Secret,
            identity.AccessToken,
        ];
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText), recordingSink],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(new StaticIdentityProvider(identity), router);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Fatal, result.Status);
        Assert.Null(result.Account);
        Assert.Null(result.Tenant);
        Assert.Null(result.CacheKey);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.Fatal, error.Kind);
        Assert.Equal("CredentialCoreFailure", error.Code);
        Assert.Equal("Credential core execution failed.", error.SafeMessage);

        foreach (string rawProviderValue in rawProviderValues)
        {
            Assert.DoesNotContain(rawProviderValue, error.SafeMessage, StringComparison.Ordinal);
        }

        foreach ((string key, string value) in error.SafeDetails)
        {
            foreach (string rawProviderValue in rawProviderValues)
            {
                Assert.DoesNotContain(rawProviderValue, key, StringComparison.Ordinal);
                Assert.DoesNotContain(rawProviderValue, value, StringComparison.Ordinal);
            }
        }

        string emittedText = diagnosticText.ToString();

        foreach (string rawProviderValue in rawProviderValues)
        {
            Assert.DoesNotContain(rawProviderValue, emittedText, StringComparison.Ordinal);
        }

        Assert.NotEmpty(recordingSink.Events);

        foreach (DiagnosticEvent diagnosticEvent in recordingSink.Events)
        {
            foreach (string rawProviderValue in rawProviderValues)
            {
                Assert.DoesNotContain(
                    rawProviderValue,
                    diagnosticEvent.Message,
                    StringComparison.Ordinal);
            }

            foreach ((string key, string? value) in diagnosticEvent.Properties)
            {
                foreach (string rawProviderValue in rawProviderValues)
                {
                    Assert.DoesNotContain(rawProviderValue, key, StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        rawProviderValue,
                        value ?? string.Empty,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void
        ExecutePythonBasicPasswordRoundTripsThroughKeyringCredentialsModeWithoutRequestUsername()
    {
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider());
        CredentialRequest credentialRequest = CreatePythonRequest(feed: "feed");

        CredentialResult result = service.Execute(credentialRequest);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal("AzureDevOps", result.Username);
        string password = Assert.IsType<string>(result.Password);

        var keyringRequest = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = credentialRequest.Resource.ServiceEndpoint,
            Mode = KeyringHelperMode.Credentials,
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(keyringRequest, result);

        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal($"AzureDevOps\n{password}\n", response.Stdout);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Fact]
    public void
        ExecuteProjectScopedDevAzurePythonBasicPasswordKeyringCredentialsOmitsRequestUsername()
    {
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider());
        CredentialRequest credentialRequest = CreateProjectScopedPythonRequest(feed: "feed");

        CredentialResult result = service.Execute(credentialRequest);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal("AzureDevOps", result.Username);
        string password = Assert.IsType<string>(result.Password);

        var keyringRequest = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = credentialRequest.Resource.ServiceEndpoint,
            Mode = KeyringHelperMode.Credentials,
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(keyringRequest, result);

        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal($"AzureDevOps\n{password}\n", response.Stdout);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Fact]
    public void
        ExecuteLegacyVisualStudioPythonBasicPasswordKeyringCredentialsOmitsRequestUsername()
    {
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider());
        CredentialRequest credentialRequest = CreateLegacyVisualStudioProjectScopedPythonRequest(
            feed: "feed"
        );

        CredentialResult result = service.Execute(credentialRequest);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal("AzureDevOps", result.Username);
        string password = Assert.IsType<string>(result.Password);

        var keyringRequest = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = credentialRequest.Resource.ServiceEndpoint,
            Mode = KeyringHelperMode.Credentials,
        };

        IReadOnlyList<string> arguments = KeyringHelperV2.BuildArguments(keyringRequest);
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(keyringRequest, result);

        Assert.Contains("--service", arguments);
        Assert.Contains(credentialRequest.Resource.ServiceEndpoint.AbsoluteUri, arguments);
        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal($"AzureDevOps\n{password}\n", response.Stdout);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Theory]
    [MemberData(nameof(RejectedFlowRequests))]
    public void ExecuteDeferredOrDisallowedRequestFailsClosedWithoutInvokingProvider(
        CredentialRequest request,
        CredentialResultStatus expectedStatus,
        CredentialErrorKind expectedErrorKind)
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);

        CredentialResult result = service.Execute(request);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(expectedErrorKind, error.Kind);
    }

    [Fact]
    public void ExecuteAllowsOnlyExplicitAzurePipelinesSystemAccessTokenWithNonPersistentCi()
    {
        var acceptedProvider = new DeterministicFakeIdentityProvider();
        var acceptedService = new CredentialCoreService(acceptedProvider);
        CredentialRequest acceptedRequest = CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            });

        CredentialResult acceptedResult = acceptedService.Execute(acceptedRequest);

        Assert.Equal(CredentialResultStatus.Success, acceptedResult.Status);
        Assert.Equal(1, acceptedProvider.InvocationCount);
        Assert.NotNull(acceptedResult.BearerToken);

        foreach (CredentialRequest rejectedRequest in RejectedAzurePipelinesRequests())
        {
            var rejectedProvider = new DeterministicFakeIdentityProvider();
            var rejectedService = new CredentialCoreService(rejectedProvider);

            CredentialResult rejectedResult = rejectedService.Execute(rejectedRequest);

            Assert.Equal(CredentialResultStatus.FlowDisabled, rejectedResult.Status);
            Assert.Equal(0, rejectedProvider.InvocationCount);
        }
    }

    [Fact]
    public void ExecuteRejectsAzurePipelinesSystemAccessTokenWhenCiContextIsAbsent()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            ciContext: null);

        Assert.Null(request.CiContext);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.FlowDisabled, result.Status);
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public void ExecuteUsesCachePartitioningAcrossSupportedDimensions()
    {
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider());

        (string CacheKey, string Material) gitBasic = GetIssuedMaterial(
            service,
            CreateGitRequest()
        );
        (string CacheKey, string Material) gitBearer = GetIssuedMaterial(
            service,
            CreateGitRequest(flow: IdentityFlow.DeviceCode, kind: CredentialKind.BearerToken));
        (string CacheKey, string Material) gitAccountA = GetIssuedMaterial(
            service,
            CreateGitRequest(accountHint: "user-a@example.com"));
        (string CacheKey, string Material) gitAccountB = GetIssuedMaterial(
            service,
            CreateGitRequest(accountHint: "user-b@example.com"));
        (string CacheKey, string Material) gitTenantA = GetIssuedMaterial(
            service,
            CreateGitRequest(tenantHint: "tenant-a"));
        (string CacheKey, string Material) gitTenantB = GetIssuedMaterial(
            service,
            CreateGitRequest(tenantHint: "tenant-b"));
        (string CacheKey, string Material) pythonFeedA = GetIssuedMaterial(
            service,
            CreatePythonRequest(feed: "feed-a"));
        (string CacheKey, string Material) pythonFeedB = GetIssuedMaterial(
            service,
            CreatePythonRequest(feed: "feed-b"));

        Assert.NotEqual(gitBasic.CacheKey, pythonFeedA.CacheKey);
        Assert.NotEqual(gitBasic.Material, pythonFeedA.Material);
        Assert.NotEqual(gitBasic.CacheKey, gitBearer.CacheKey);
        Assert.NotEqual(gitBasic.Material, gitBearer.Material);
        Assert.NotEqual(gitAccountA.CacheKey, gitAccountB.CacheKey);
        Assert.NotEqual(gitAccountA.Material, gitAccountB.Material);
        Assert.NotEqual(gitTenantA.CacheKey, gitTenantB.CacheKey);
        Assert.NotEqual(gitTenantA.Material, gitTenantB.Material);
        Assert.NotEqual(pythonFeedA.CacheKey, pythonFeedB.CacheKey);
        Assert.NotEqual(pythonFeedA.Material, pythonFeedB.Material);
    }

    [Fact]
    public void ExecuteReturnsSamePasswordWhenGitRequestsShareFrozenCacheKey()
    {
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider());
        CredentialRequest orgOnlyRequest = CreateGitRequest(
            accountHint: "user@example.com",
            tenantHint: "tenant-1");
        CredentialRequest repoScopedRequest = orgOnlyRequest with
        {
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org/project/_git/repo"),
                project: "project",
                repository: "repo"),
        };

        CredentialResult orgOnlyResult = service.Execute(orgOnlyRequest);
        CredentialResult repoScopedResult = service.Execute(repoScopedRequest);

        Assert.Equal(CredentialResultStatus.Success, orgOnlyResult.Status);
        Assert.Equal(CredentialResultStatus.Success, repoScopedResult.Status);
        Assert.Equal(
            Assert.IsType<CacheKey>(orgOnlyResult.CacheKey).Value,
            Assert.IsType<CacheKey>(repoScopedResult.CacheKey).Value);
        Assert.Equal(
            Assert.IsType<string>(orgOnlyResult.Password),
            Assert.IsType<string>(repoScopedResult.Password));
    }

    [Fact]
    public void ExecuteReturnsSameBearerTokenWhenAcceptedFlowsShareFrozenCacheKey()
    {
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider());
        CredentialRequest interactiveRequest = CreateGitRequest(
            flow: IdentityFlow.InteractiveBrowser,
            kind: CredentialKind.BearerToken,
            accountHint: "user@example.com",
            tenantHint: "tenant-1");
        CredentialRequest deviceCodeRequest = interactiveRequest with
        {
            IdentityFlow = IdentityFlow.DeviceCode,
        };

        CredentialResult interactiveResult = service.Execute(interactiveRequest);
        CredentialResult deviceCodeResult = service.Execute(deviceCodeRequest);

        Assert.Equal(CredentialResultStatus.Success, interactiveResult.Status);
        Assert.Equal(CredentialResultStatus.Success, deviceCodeResult.Status);
        Assert.Equal(
            Assert.IsType<CacheKey>(interactiveResult.CacheKey).Value,
            Assert.IsType<CacheKey>(deviceCodeResult.CacheKey).Value);
        Assert.Equal(
            Assert.IsType<string>(interactiveResult.BearerToken),
            Assert.IsType<string>(deviceCodeResult.BearerToken));
    }

    [Fact]
    public void ExecuteDiagnosticsDoNotEmitFakeSecretOrTokenMaterial()
    {
        CredentialRequest request = CreateGitRequest(
            flow: IdentityFlow.DeviceCode,
            kind: CredentialKind.BearerToken,
            accountHint: "user@example.com",
            tenantHint: "tenant-1");
        var probeProvider = new DeterministicFakeIdentityProvider();
        IdentityMaterial probeIdentity = probeProvider.GetIdentity(request);

        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText), recordingSink],
            new SecretRedactor([probeIdentity.Secret, probeIdentity.AccessToken]));
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);

        CredentialResult result = service.Execute(request);

        string emittedText = diagnosticText.ToString();
        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.NotEmpty(recordingSink.Events);
        Assert.Equal(probeIdentity.AccessToken, result.BearerToken);
        Assert.DoesNotContain(probeIdentity.Secret, emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(probeIdentity.AccessToken, emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretRedactor.DefaultMask, emittedText, StringComparison.Ordinal);

        foreach (DiagnosticEvent diagnosticEvent in recordingSink.Events)
        {
            Assert.DoesNotContain(
                probeIdentity.Secret,
                diagnosticEvent.Message,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                probeIdentity.AccessToken,
                diagnosticEvent.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                SecretRedactor.DefaultMask,
                diagnosticEvent.Message,
                StringComparison.Ordinal);

            foreach ((string key, string? value) in diagnosticEvent.Properties)
            {
                Assert.DoesNotContain(probeIdentity.Secret, key, StringComparison.Ordinal);
                Assert.DoesNotContain(probeIdentity.AccessToken, key, StringComparison.Ordinal);
                Assert.DoesNotContain(SecretRedactor.DefaultMask, key, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    probeIdentity.Secret,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    probeIdentity.AccessToken,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    SecretRedactor.DefaultMask,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ExecuteRejectedRequestIgnoresDiagnosticSinkFailures()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider, CreateThrowingDiagnosticRouter());
        CredentialRequest request = CreateGitRequest(
            flow: IdentityFlow.InteractiveBrowser,
            kind: CredentialKind.BasicPassword,
            interactivePolicy: InteractivePolicy.Never);

        CredentialResult? result = null;
        Exception? exception = Record.Exception(() => result = service.Execute(request));

        Assert.Null(exception);
        result = Assert.IsType<CredentialResult>(result);
        Assert.Equal(CredentialResultStatus.InteractionBlocked, result.Status);
        Assert.Equal(0, provider.InvocationCount);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.InteractionBlocked, error.Kind);
        Assert.Equal("InteractionBlocked", error.Code);
    }

    [Fact]
    public void ExecuteSuccessIgnoresDiagnosticSinkFailures()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider, CreateThrowingDiagnosticRouter());

        CredentialResult? result = null;
        Exception? exception = Record.Exception(() => result = service.Execute(CreateGitRequest()));

        Assert.Null(exception);
        result = Assert.IsType<CredentialResult>(result);
        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.NotNull(result.Password);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExecuteMalformedAcceptedFlowRequestReturnsProtocolViolation()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest() with
        {
            ServiceIdentity = "Default",
        };

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, error.Kind);
        Assert.Equal("ProtocolViolation", error.Code);
    }

    [Theory]
    [InlineData("default\u001B")]
    [InlineData("default\u009F")]
    public void ExecuteRequestWithControlCharacterInServiceIdentityReturnsProtocolViolation(
        string serviceIdentity)
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest() with
        {
            ServiceIdentity = serviceIdentity,
        };

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, error.Kind);
        Assert.Equal("ProtocolViolation", error.Code);
    }

    [Theory]
    [MemberData(nameof(RequestsWithControlCharactersInAccountOrTenantHints))]
    public void ExecuteRequestWithControlCharacterInAccountOrTenantHintReturnsProtocolViolation(
        CredentialRequest request)
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, error.Kind);
        Assert.Equal("ProtocolViolation", error.Code);
    }

    [Theory]
    [MemberData(nameof(PythonRequestsWithEncodedControlCharactersInResourceIdentity))]
    public void
        ExecutePythonRequestsWithEncodedControlCharactersInResourceIdentityReturnsProtocolViolation(
            CredentialRequest request
        )
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, error.Kind);
        Assert.Equal("ProtocolViolation", error.Code);
    }

    [Fact]
    public void
        ExecuteGitRequestWithDecodedControlCharacterInRepositoryPathReturnsProtocolViolation()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest() with
        {
            Resource = new CanonicalResourceIdentity
            {
                AzureDevOpsHost = "dev.azure.com",
                Organization = "org",
                Project = "project",
                Repository = "repo\nother",
                ServiceEndpoint = new Uri("https://dev.azure.com/org/project/_git/repo%0Aother"),
            },
        };

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, error.Kind);
        Assert.Equal("ProtocolViolation", error.Code);
    }

    [Fact]
    public void ExecuteInteractivePolicyNeverReturnsInteractionBlockedAndMapsToInteractionRequired()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest(
            flow: IdentityFlow.InteractiveBrowser,
            kind: CredentialKind.BasicPassword,
            interactivePolicy: InteractivePolicy.Never);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.InteractionBlocked, result.Status);
        Assert.Equal(0, provider.InvocationCount);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.InteractionBlocked, error.Kind);
        Assert.Equal("InteractionBlocked", error.Code);
        Assert.Equal(
            "Credential request requires interaction, but interaction is blocked by policy.",
            error.SafeMessage);

        AdapterHostResult adapterResult = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result);
        Assert.Equal(AdapterHostExitCode.InteractionRequired, adapterResult.ExitCode);
        Assert.False(adapterResult.WriteProtocolStdout);
        Assert.True(adapterResult.WriteDiagnosticStderr);
        Assert.Equal("InteractionBlocked", adapterResult.SafeDiagnosticCode);
    }

    public static TheoryData<CredentialRequest, CredentialResultStatus, CredentialErrorKind>
        RejectedFlowRequests() =>
            new()
            {
                {
                    CreateGitRequest(
                        flow: IdentityFlow.ServicePrincipal,
                        kind: CredentialKind.BasicPassword),
                    CredentialResultStatus.FlowDeferred,
                    CredentialErrorKind.FlowDeferred
                },
                {
                    CreateGitRequest(
                        flow: IdentityFlow.ManagedIdentity,
                        kind: CredentialKind.BasicPassword),
                    CredentialResultStatus.FlowDeferred,
                    CredentialErrorKind.FlowDeferred
                },
                {
                    CreateGitRequest(
                        flow: IdentityFlow.WorkloadIdentityFederation,
                        kind: CredentialKind.BasicPassword),
                    CredentialResultStatus.FlowDeferred,
                    CredentialErrorKind.FlowDeferred
                },
                {
                    CreateGitRequest(
                        flow: IdentityFlow.DeviceCode,
                        kind: CredentialKind.BasicPassword,
                        interactivePolicy: InteractivePolicy.Never),
                    CredentialResultStatus.InteractionBlocked,
                    CredentialErrorKind.InteractionBlocked
                },
            };

    public static TheoryData<CredentialRequest, IdentityMaterial>
        UnsafeProviderMaterialScenarios() =>
            new()
            {
            {
                CreateGitRequest(),
                CreateIdentityMaterial(accessToken: "unsafe\r\ntoken")
            },
            {
                CreateGitRequest(kind: CredentialKind.BearerToken),
                CreateIdentityMaterial(secret: "unsafe\r\nsecret")
            },
            {
                CreateGitRequest(),
                CreateIdentityMaterial(account: "unsafe\r\naccount")
            },
            {
                CreateGitRequest(),
                CreateIdentityMaterial(tenant: "unsafe\r\ntenant")
            },
        };

    public static TheoryData<CredentialRequest>
        RequestsWithControlCharactersInAccountOrTenantHints() =>
            new()
            {
                {
                    CreateGitRequest(accountHint: "user\u001B@example.com")
                },
                {
                    CreateGitRequest(accountHint: "user\u009F@example.com")
                },
                {
                    CreateGitRequest(tenantHint: "tenant\u001Bone")
                },
                {
                    CreateGitRequest(tenantHint: "tenant\u009Fone")
                },
            };

    public static TheoryData<CredentialRequest>
        PythonRequestsWithEncodedControlCharactersInResourceIdentity() =>
            new()
            {
                {
                    CreatePythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "pkgs.dev.azure.com",
                            Organization = "org\nother",
                            Feed = "feed",
                            ServiceEndpoint = new Uri(
                                "https://pkgs.dev.azure.com/org%0Aother/_packaging/feed/pypi/simple"
                            ),
                        },
                    }
                },
                {
                    CreatePythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "pkgs.dev.azure.com",
                            Organization = "org\u001Bother",
                            Feed = "feed",
                            ServiceEndpoint = new Uri(
                                "https://pkgs.dev.azure.com/org%1Bother/_packaging/feed/pypi/simple"
                            ),
                        },
                    }
                },
                {
                    CreateProjectScopedPythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "dev.azure.com",
                            Organization = "org",
                            Project = "project\rother",
                            Feed = "feed",
                            ServiceEndpoint = new Uri(
                                "https://dev.azure.com/org/project%0Dother/_packaging/feed/"
                                    + "pypi/simple"
                            ),
                        },
                    }
                },
                {
                    CreateProjectScopedPythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "dev.azure.com",
                            Organization = "org",
                            Project = "project\u009Fother",
                            Feed = "feed",
                            ServiceEndpoint = new Uri(
                                "https://dev.azure.com/org/project%C2%9Fother/_packaging/feed/"
                                    + "pypi/simple"
                            ),
                        },
                    }
                },
                {
                    CreatePythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "pkgs.dev.azure.com",
                            Organization = "org",
                            Feed = "feed\tother",
                            ServiceEndpoint = new Uri(
                                "https://pkgs.dev.azure.com/org/_packaging/feed%09other/pypi/simple"
                            ),
                        },
                    }
                },
                {
                    CreatePythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "pkgs.dev.azure.com",
                            Organization = "org",
                            Feed = "feed\u007Fother",
                            ServiceEndpoint = new Uri(
                                "https://pkgs.dev.azure.com/org/_packaging/feed%7Fother/pypi/simple"
                            ),
                        },
                    }
                },
                {
                    CreatePythonRequest(feed: "feed") with
                    {
                        Resource = new CanonicalResourceIdentity
                        {
                            AzureDevOpsHost = "pkgs.dev.azure.com",
                            Organization = "org",
                            Feed = "feed\u0085other",
                            ServiceEndpoint = new Uri(
                                "https://pkgs.dev.azure.com/org/_packaging/feed%C2%85other/"
                                    + "pypi/simple"
                            ),
                        },
                    }
                },
            };

    private static IEnumerable<CredentialRequest> RejectedAzurePipelinesRequests()
    {
        yield return CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            ciContext: null);
        yield return CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            new CiContext
            {
                ExplicitCiMode = false,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            });
        yield return CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            new CiContext
            {
                ExplicitCiMode = true,
                Provider = "GitHubActions",
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            });
        yield return CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = false,
                AllowsPersistentWrites = false,
            });
        yield return CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.ProductPersistentCacheDisabled,
            new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            });
        yield return CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = true,
            });
    }

    private static (string CacheKey, string Material) GetIssuedMaterial(
        CredentialCoreService service,
        CredentialRequest request)
    {
        CredentialResult result = service.Execute(request);
        Assert.Equal(CredentialResultStatus.Success, result.Status);
        return (
            Assert.IsType<CacheKey>(result.CacheKey).Value,
            result.Password ?? Assert.IsType<string>(result.BearerToken)
        );
    }

    private static CredentialRequest CreateGitRequest(
        IdentityFlow flow = IdentityFlow.DeviceCode,
        CredentialKind kind = CredentialKind.BasicPassword,
        InteractivePolicy interactivePolicy = InteractivePolicy.UserAllowed,
        CachePolicyMode cachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        CiContext? ciContext = null,
        string? accountHint = null,
        string? tenantHint = null) =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org")),
            ServiceIdentity = "default",
            AccountHint = accountHint,
            TenantHint = tenantHint,
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = kind,
            IdentityFlow = flow,
            InteractivePolicy = interactivePolicy,
            CachePolicy = cachePolicy,
            CiContext = ciContext
                ?? new CiContext
                {
                    ExplicitCiMode = false,
                    AllowsPersistentWrites = false,
                },
        };

    private static CredentialRequest CreateAzurePipelinesSystemAccessTokenRequest(
        CachePolicyMode cachePolicy,
        CiContext? ciContext)
    {
        CredentialRequest request = CreateGitRequest(
            flow: IdentityFlow.AzurePipelinesSystemAccessToken,
            kind: CredentialKind.BearerToken,
            interactivePolicy: InteractivePolicy.Never,
            cachePolicy: cachePolicy,
            ciContext: ciContext);
        return ciContext is null ? request with { CiContext = null } : request;
    }

    private static CredentialRequest CreatePythonRequest(
        string feed,
        string? accountHint = null,
        string? tenantHint = null) =>
        new()
        {
            Ecosystem = CredentialEcosystem.Python,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "pkgs.dev.azure.com",
                "org",
                new Uri($"https://pkgs.dev.azure.com/org/_packaging/{feed}/pypi/simple"),
                feed: feed),
            ServiceIdentity = "default",
            AccountHint = accountHint,
            TenantHint = tenantHint,
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
        };

    private static CredentialRequest CreateProjectScopedPythonRequest(
        string feed,
        string project = "project",
        string? accountHint = null,
        string? tenantHint = null) =>
        CreatePythonRequest(feed, accountHint, tenantHint) with
        {
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri($"https://dev.azure.com/org/{project}/_packaging/{feed}/pypi/simple"),
                project: project,
                feed: feed),
        };

    private static CredentialRequest CreateLegacyVisualStudioProjectScopedPythonRequest(
        string feed,
        string project = "project",
        string? accountHint = null,
        string? tenantHint = null) =>
        CreatePythonRequest(feed, accountHint, tenantHint) with
        {
            Resource = CanonicalResourceIdentity.Create(
                "org.visualstudio.com",
                "org",
                new Uri(
                    $"https://org.visualstudio.com/DefaultCollection/{project}/_packaging/"
                        + $"{feed}/pypi/simple/"
                ),
                project: project,
                feed: feed),
        };

    private static IdentityMaterial CreateIdentityMaterial(
        string account = "user@example.com",
        string tenant = "tenant-1",
        string secret = "safe-secret",
        string accessToken = "safe-token") =>
        new()
        {
            Account = account,
            Tenant = tenant,
            Secret = secret,
            AccessToken = accessToken,
            ExpiresAt = CustomProviderExpiresAt,
        };

    private static DiagnosticRouter CreateThrowingDiagnosticRouter() =>
        new(
            [new ThrowingDiagnosticSink(new IOException("diagnostic sink failed"))],
            SecretRedactor.Empty
        );

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }

    private sealed class ThrowingDiagnosticSink(Exception exceptionToThrow) : IDiagnosticSink
    {
        private readonly Exception _exceptionToThrow = exceptionToThrow;

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw _exceptionToThrow;
        }
    }

    private sealed class StaticIdentityProvider(IdentityMaterial identity) : IIdentityProvider
    {
        private readonly IdentityMaterial _identity = identity;

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _identity;
        }
    }
}
