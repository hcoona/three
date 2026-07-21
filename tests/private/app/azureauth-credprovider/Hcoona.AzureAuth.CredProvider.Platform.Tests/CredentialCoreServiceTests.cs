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
    public void ExecuteDefaultServiceUsesDeterministicFakeProvider()
    {
        CredentialRequest request = CreateGitRequest();
        IdentityMaterial expectedIdentity = new DeterministicFakeIdentityProvider().GetIdentity(
            request
        );
        var service = new CredentialCoreService();

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal("AzureDevOps", result.Username);
        Assert.Equal(expectedIdentity.Account, result.Account);
        Assert.Equal(expectedIdentity.Tenant, result.Tenant);
        Assert.Equal(expectedIdentity.ExpiresAt, result.ExpiresAt);
        Assert.Equal(
            Assert.IsType<string>(expectedIdentity.Secret),
            Assert.IsType<string>(result.Password));
        Assert.Null(result.BearerToken);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExecutePatCompatibilityFailsClosedBeforeProviderCacheOrExchange()
    {
        IdentityMaterial identity = CreateIdentityMaterial();
        var provider = new StaticIdentityProvider(identity);
        var cache = new ReportingDerivedCredentialCache(
            DerivedCredentialCacheAvailability.Available);
        var exchange = new CountingTokenExchange((_, _, _) =>
            throw new InvalidOperationException("must not exchange"));
        var service = new CredentialCoreService(provider, null, cache, exchange);
        CredentialRequest request = CreateGitRequest(
            flow: IdentityFlow.PatCompatibility,
            kind: CredentialKind.PatCompatibility,
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.FlowDeferred, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, cache.PersistentReadCount);
        Assert.Equal(0, cache.PersistentWriteCount);
        Assert.Equal(0, exchange.InvocationCount);
        AssertClosedFailureResult(result, identity);
        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal("PatCompatibilityDeferred", error.Code);
        Assert.DoesNotContain("token", error.SafeMessage, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void ExecuteBearerTokenRequestAllowsCustomBearerOnlyProviderMaterial()
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            secret: null,
            accessToken: "custom-bearer-token");
        var service = new CredentialCoreService(new StaticIdentityProvider(identity));
        CredentialRequest request = CreateGitRequest(kind: CredentialKind.BearerToken);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(identity.Account, result.Account);
        Assert.Equal(identity.Tenant, result.Tenant);
        Assert.Equal(identity.ExpiresAt, result.ExpiresAt);
        Assert.Equal(
            Assert.IsType<string>(identity.AccessToken),
            Assert.IsType<string>(result.BearerToken));
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExecuteBasicPasswordRequestAllowsCustomPasswordOnlyProviderMaterial()
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            secret: "custom-password-secret",
            accessToken: null);
        var service = new CredentialCoreService(new StaticIdentityProvider(identity));
        CredentialRequest request = CreateGitRequest();

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(identity.Account, result.Account);
        Assert.Equal(identity.Tenant, result.Tenant);
        Assert.Equal(identity.ExpiresAt, result.ExpiresAt);
        Assert.Equal("AzureDevOps", result.Username);
        Assert.Equal(
            Assert.IsType<string>(identity.Secret),
            Assert.IsType<string>(result.Password));
        Assert.Null(result.BearerToken);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExecuteAcceptedMvpRequestWithInjectedDirectMsalProviderReturnsSuccess()
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            account: "direct-msal@example.com",
            tenant: "direct-tenant",
            secret: "direct-secret",
            accessToken: "direct-access-token");
        var provider = new CountingDirectMsalIdentityProvider(
            _ => DirectMsalIdentityResult.Success(identity));
        var service = new CredentialCoreService(new DirectMsalIdentityProvider(provider));

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(identity.Account, result.Account);
        Assert.Equal(identity.Tenant, result.Tenant);
        Assert.Equal(identity.ExpiresAt, result.ExpiresAt);
        Assert.Equal("AzureDevOps", result.Username);
        Assert.Equal(
            Assert.IsType<string>(identity.Secret),
            Assert.IsType<string>(result.Password));
        Assert.Null(result.BearerToken);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ExecuteDirectMsalProviderWithoutInjectedImplementationFailsClosed()
    {
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var service = new CredentialCoreService(
            new DirectMsalIdentityProvider(),
            diagnosticRouter: null,
            derivedCredentialCache: null,
            tokenExchange: tokenExchange);

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal(0, tokenExchange.InvocationCount);
        Assert.Null(result.Account);
        Assert.Null(result.Tenant);
        Assert.Null(result.CacheKey);
        Assert.Null(result.ExpiresAt);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.CredentialUnavailable, error.Kind);
        Assert.Equal("DirectMsalNotImplemented", error.Code);
        Assert.Equal(
            "Direct MSAL identity provider is not implemented.",
            error.SafeMessage);
    }

    [Fact]
    public void ExecuteDirectMsalUnavailableFailsClosedWithoutLeakingDiagnostics()
    {
        const string rawUnavailableReason = "direct-msal-secret-should-not-leak";
        var provider = new CountingDirectMsalIdentityProvider(
            _ => throw new PlatformNotSupportedException(rawUnavailableReason));
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText), recordingSink],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DirectMsalIdentityProvider(provider),
            router,
            derivedCredentialCache: null,
            tokenExchange: tokenExchange);

        CredentialResult result = service.Execute(
            CreateGitRequest(kind: CredentialKind.BearerToken));

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
        Assert.Null(result.Account);
        Assert.Null(result.Tenant);
        Assert.Null(result.CacheKey);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.CredentialUnavailable, error.Kind);
        Assert.Equal("DirectMsalUnavailable", error.Code);
        Assert.Equal("Direct MSAL identity provider is unavailable.", error.SafeMessage);
        Assert.DoesNotContain(rawUnavailableReason, error.SafeMessage, StringComparison.Ordinal);

        foreach ((string key, string value) in error.SafeDetails)
        {
            Assert.DoesNotContain(rawUnavailableReason, key, StringComparison.Ordinal);
            Assert.DoesNotContain(rawUnavailableReason, value, StringComparison.Ordinal);
        }

        string emittedText = diagnosticText.ToString();
        Assert.DoesNotContain(rawUnavailableReason, emittedText, StringComparison.Ordinal);

        foreach (DiagnosticEvent diagnosticEvent in recordingSink.Events)
        {
            Assert.DoesNotContain(
                rawUnavailableReason,
                diagnosticEvent.Message,
                StringComparison.Ordinal);

            foreach ((string key, string? value) in diagnosticEvent.Properties)
            {
                Assert.DoesNotContain(rawUnavailableReason, key, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    rawUnavailableReason,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ExecuteSuccessDiagnosticsUseCredentialCoreFallbackWhenRedactionBlanksMessage()
    {
        const string expectedMessage = "Credential request succeeded.";
        const string expectedCode = "CredentialIssued";
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        DiagnosticRouter router = CreateRecordingDiagnosticRouter(
            diagnosticText,
            recordingSink,
            CreateBlankingRedactor(expectedMessage));
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.Success, result.Status);

        DiagnosticEvent diagnosticEvent = Assert.Single(recordingSink.Events);
        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.Equal(expectedCode, diagnosticEvent.Properties["code"]);

        string emittedText = diagnosticText.ToString();
        Assert.Contains(expectedMessage, emittedText, StringComparison.Ordinal);
        Assert.Contains($"code={expectedCode}", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            emittedText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ExecuteCacheUnavailableDiagnosticsUseCredentialCoreFallbackWhenRedactionBlanksMessage()
    {
        const string expectedMessage = "Persistent derived credential cache is unavailable.";
        const string expectedCode = "CacheUnavailable";
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        DiagnosticRouter router = CreateRecordingDiagnosticRouter(
            diagnosticText,
            recordingSink,
            CreateBlankingRedactor(expectedMessage));
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            router,
            new ReportingDerivedCredentialCache(
                new DerivedCredentialCacheAvailability(
                    DerivedCredentialCacheAvailabilityStatus.Unavailable)));

        CredentialResult result = service.Execute(
            CreateGitRequest(cachePolicy: CachePolicyMode.FuturePersistentCacheRequested));

        Assert.Equal(CredentialResultStatus.CacheUnavailable, result.Status);

        DiagnosticEvent diagnosticEvent = Assert.Single(recordingSink.Events);
        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.Equal(expectedCode, diagnosticEvent.Properties["code"]);

        string emittedText = diagnosticText.ToString();
        Assert.Contains(expectedMessage, emittedText, StringComparison.Ordinal);
        Assert.Contains($"code={expectedCode}", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            emittedText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteFlowDisabledDiagnosticsUseCredentialCoreFallbackWhenRedactionBlanksMessage()
    {
        const string expectedMessage = "Credential request is disabled by the current MVP policy.";
        const string expectedCode = "FlowDisabled";
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        DiagnosticRouter router = CreateRecordingDiagnosticRouter(
            diagnosticText,
            recordingSink,
            CreateBlankingRedactor(expectedMessage));
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);

        CredentialResult result = service.Execute(
            CreateGitRequest() with
            {
                CiContext = new CiContext
                {
                    ExplicitCiMode = false,
                    AllowsPersistentWrites = true,
                },
            });

        Assert.Equal(CredentialResultStatus.FlowDisabled, result.Status);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedMessage, error.SafeMessage);

        DiagnosticEvent diagnosticEvent = Assert.Single(recordingSink.Events);
        Assert.Equal(error.SafeMessage, diagnosticEvent.Message);
        Assert.Equal(expectedCode, diagnosticEvent.Properties["code"]);

        string emittedText = diagnosticText.ToString();
        Assert.Contains(error.SafeMessage, emittedText, StringComparison.Ordinal);
        Assert.Contains($"code={expectedCode}", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            emittedText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ExecuteDirectMsalUnavailableDiagnosticsUseCredentialCoreFallbackWhenRedactionDropsCode()
    {
        const string expectedMessage = "Direct MSAL identity provider is unavailable.";
        const string expectedCode = "DirectMsalUnavailable";
        var provider = new CountingDirectMsalIdentityProvider(
            _ => throw new PlatformNotSupportedException("direct-msal should stay internal"));
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        DiagnosticRouter router = CreateRecordingDiagnosticRouter(
            diagnosticText,
            recordingSink,
            CreateBlankingRedactor(expectedMessage, expectedCode));
        var service = new CredentialCoreService(
            new DirectMsalIdentityProvider(provider),
            router,
            derivedCredentialCache: null,
            tokenExchange: tokenExchange);

        CredentialResult result = service.Execute(
            CreateGitRequest(kind: CredentialKind.BearerToken));

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);

        DiagnosticEvent diagnosticEvent = Assert.Single(recordingSink.Events);
        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.False(diagnosticEvent.Properties.ContainsKey("code"));

        string emittedText = diagnosticText.ToString();
        Assert.Contains(expectedMessage, emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"code={expectedCode}",
            emittedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            emittedText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        ExecuteRecoversSafeDiagnosticFromInheritedDisposedCommitTrackingScopeWithoutLateRoutes()
    {
        var diagnosticText = new StringWriter();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);
        using var releaseFlowedTasks = new ManualResetEventSlim(false);
        Task<CredentialResult>? flowedExecutionTask = null;
        Task? lateSiblingRouteTask = null;

        using (router.BeginUserVisibleCommitTracking())
        {
            flowedExecutionTask = Task.Run(
                () =>
                {
                    if (!releaseFlowedTasks.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the flowed credential-core execution.");
                    }

                    CredentialResult result = service.Execute(CreateGitRequest());
                    router.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "late raw diagnostic"));
                    return result;
                },
                TestContext.Current.CancellationToken);
            lateSiblingRouteTask = Task.Run(
                () =>
                {
                    if (!releaseFlowedTasks.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the late flowed diagnostic route.");
                    }

                    router.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "late sibling diagnostic"));
                },
                TestContext.Current.CancellationToken);
        }

        releaseFlowedTasks.Set();

        CredentialResult result = await Assert
            .IsType<Task<CredentialResult>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        await Assert
            .IsType<Task>(lateSiblingRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.Success, result.Status);

        string emittedText = diagnosticText.ToString();
        Assert.Contains("Credential request succeeded.", emittedText, StringComparison.Ordinal);
        Assert.Contains("code=CredentialIssued", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain("late raw diagnostic", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain("late sibling diagnostic", emittedText, StringComparison.Ordinal);
        Assert.Equal(1, emittedText.Split('\n').Length - 1);
    }

    [Fact]
    public async Task
        ExecuteSuppressesSafeDiagnosticRecoveryFromInheritedDisposedCommittedCommitTrackingScope()
    {
        var diagnosticText = new StringWriter();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);
        using var releaseFlowedExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? flowedExecutionTask = null;

        using (router.BeginUserVisibleCommitTracking())
        {
            router.Route(new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticChannel.Diagnostic,
                "committed boundary diagnostic"));

            flowedExecutionTask = Task.Run(
                () =>
                {
                    if (!releaseFlowedExecution.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the flowed credential-core execution.");
                    }

                    return service.Execute(CreateGitRequest());
                },
                TestContext.Current.CancellationToken);
        }

        releaseFlowedExecution.Set();

        CredentialResult result = await Assert
            .IsType<Task<CredentialResult>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.Success, result.Status);

        string emittedText = diagnosticText.ToString();
        Assert.Contains("committed boundary diagnostic", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential request succeeded.",
            emittedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("code=CredentialIssued", emittedText, StringComparison.Ordinal);
        Assert.Equal(1, emittedText.Split('\n').Length - 1);
    }

    [Fact]
    public async Task
        ExecuteSuppressesSafeDiagnosticRecoveryFromInheritedDisposedDirectSuppressionScope()
    {
        var diagnosticText = new StringWriter();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);
        using var releaseFlowedExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? flowedExecutionTask = null;

        using (router.BeginUserVisibleCommitTracking(
            suppressDirectCredentialCoreSafeDiagnosticRoutes: true))
        {
            flowedExecutionTask = Task.Run(
                () =>
                {
                    if (!releaseFlowedExecution.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the flowed credential-core execution.");
                    }

                    return service.Execute(CreateGitRequest(flow: IdentityFlow.ServicePrincipal));
                },
                TestContext.Current.CancellationToken);
        }

        releaseFlowedExecution.Set();

        CredentialResult result = await Assert
            .IsType<Task<CredentialResult>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.FlowDeferred, result.Status);
        Assert.Equal(string.Empty, diagnosticText.ToString());
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("tokenExchange")]
    public async Task ExecuteSuppressesLateDescendantRoutesSpawnedInsideProviderAndTokenExchange(
        string descendantOwner)
    {
        var diagnosticText = new StringWriter();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        using var seamEntered = new ManualResetEventSlim(false);
        using var releaseDescendantRoute = new ManualResetEventSlim(false);
        Task? descendantRouteTask = null;
        const string lateDescendantDiagnostic = "late descendant diagnostic";

        Task CreateDescendantRouteTask()
        {
            return Task.Run(
                () =>
                {
                    if (!releaseDescendantRoute.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the late flowed descendant "
                                + "diagnostic route.");
                    }

                    router.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        lateDescendantDiagnostic));
                },
                TestContext.Current.CancellationToken);
        }

        IIdentityProvider identityProvider;
        ITokenExchange tokenExchange;
        switch (descendantOwner)
        {
            case "provider":
                var deterministicIdentityProvider = new DeterministicFakeIdentityProvider();
                identityProvider = new CallbackIdentityProvider(
                    request =>
                    {
                        seamEntered.Set();
                        descendantRouteTask = CreateDescendantRouteTask();
                        if (!releaseDescendantRoute.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the flowed credential-core "
                                    + "identity-provider descendant route.");
                        }

                        Assert
                            .IsType<Task>(descendantRouteTask)
                            .WaitAsync(
                                TimeSpan.FromSeconds(10),
                                TestContext.Current.CancellationToken)
                            .GetAwaiter()
                            .GetResult();
                        return deterministicIdentityProvider.GetIdentity(request);
                    });
                tokenExchange = new DeterministicLocalTokenExchange();
                break;
            case "tokenExchange":
                identityProvider = new DeterministicFakeIdentityProvider();
                tokenExchange = new CountingTokenExchange(
                    (request, identity, cacheKey) =>
                    {
                        seamEntered.Set();
                        descendantRouteTask = CreateDescendantRouteTask();
                        if (!releaseDescendantRoute.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the flowed credential-core "
                                    + "token-exchange descendant route.");
                        }

                        Assert
                            .IsType<Task>(descendantRouteTask)
                            .WaitAsync(
                                TimeSpan.FromSeconds(10),
                                TestContext.Current.CancellationToken)
                            .GetAwaiter()
                            .GetResult();
                        return new DeterministicLocalTokenExchange().Exchange(
                            request,
                            identity,
                            cacheKey);
                    });
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(descendantOwner),
                    descendantOwner,
                    null);
        }

        var service = new CredentialCoreService(
            identityProvider,
            router,
            derivedCredentialCache: null,
            tokenExchange: tokenExchange);
        Task<CredentialResult>? flowedExecutionTask = null;

        try
        {
            using (router.BeginUserVisibleCommitTracking())
            {
                flowedExecutionTask = Task.Run(
                    () => service.Execute(CreateGitRequest()),
                    TestContext.Current.CancellationToken);

                if (!seamEntered.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException(
                        $"Timed out waiting for the flowed credential-core {descendantOwner} "
                            + "seam.");
                }
            }
        }
        finally
        {
            releaseDescendantRoute.Set();
        }

        CredentialResult result = await Assert
            .IsType<Task<CredentialResult>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        await Assert
            .IsType<Task>(descendantRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.Success, result.Status);

        string emittedText = diagnosticText.ToString();
        Assert.Contains("Credential request succeeded.", emittedText, StringComparison.Ordinal);
        Assert.Contains("code=CredentialIssued", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(lateDescendantDiagnostic, emittedText, StringComparison.Ordinal);
        Assert.Equal(1, emittedText.Split('\n').Length - 1);
    }

    [Fact]
    public async Task
        ExecuteRecoversSafeDiagnosticAfterMidExecutionScopeClosureWithoutRevivingLateRoutes()
    {
        var diagnosticText = new StringWriter();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        using var tokenExchangeEntered = new ManualResetEventSlim(false);
        using var releaseTokenExchange = new ManualResetEventSlim(false);
        var tokenExchange = new CountingTokenExchange(
            (request, identity, cacheKey) =>
            {
                tokenExchangeEntered.Set();
                if (!releaseTokenExchange.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release the flowed credential-core token exchange.");
                }

                return new DeterministicLocalTokenExchange().Exchange(
                    request,
                    identity,
                    cacheKey);
            });
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            router,
            derivedCredentialCache: null,
            tokenExchange: tokenExchange);
        Task<CredentialResult>? flowedExecutionTask = null;

        try
        {
            using (router.BeginUserVisibleCommitTracking())
            {
                flowedExecutionTask = Task.Run(
                    () =>
                    {
                        CredentialResult result = service.Execute(CreateGitRequest());
                        router.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "late raw diagnostic"));
                        return result;
                    },
                    TestContext.Current.CancellationToken);

                if (!tokenExchangeEntered.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException(
                        "Timed out waiting for the flowed credential-core token exchange.");
                }
            }
        }
        finally
        {
            releaseTokenExchange.Set();
        }

        CredentialResult result = await Assert
            .IsType<Task<CredentialResult>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, tokenExchange.InvocationCount);

        string emittedText = diagnosticText.ToString();
        Assert.Contains("Credential request succeeded.", emittedText, StringComparison.Ordinal);
        Assert.Contains("code=CredentialIssued", emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain("late raw diagnostic", emittedText, StringComparison.Ordinal);
        Assert.Equal(1, emittedText.Split('\n').Length - 1);
    }

    [Theory]
    [MemberData(nameof(AcceptedTokenExchangeRequests))]
    public void ExecuteAcceptedRequestInvokesTokenExchangeExactlyOnceAndPreservesResultShape(
        CredentialRequest request)
    {
        var provider = new DeterministicFakeIdentityProvider();
        IdentityMaterial expectedIdentity = new DeterministicFakeIdentityProvider().GetIdentity(
            request
        );
        var tokenExchange = new CountingTokenExchange(
            (exchangeRequest, identity, cacheKey) =>
                new DeterministicLocalTokenExchange().Exchange(
                    exchangeRequest,
                    identity,
                    cacheKey
                ));
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, tokenExchange.InvocationCount);
        Assert.Equal(expectedIdentity.Account, result.Account);
        Assert.Equal(expectedIdentity.Tenant, result.Tenant);
        Assert.Equal(expectedIdentity.ExpiresAt, result.ExpiresAt);
        Assert.Equal(
            CacheKeySchema.Create(request, expectedIdentity.Account, expectedIdentity.Tenant).Value,
            Assert.IsType<CacheKey>(result.CacheKey).Value);
        Assert.Null(result.Error);

        if (
            request.CredentialKind
            is CredentialKind.BearerToken or CredentialKind.NpmAuthToken
        )
        {
            Assert.Equal(
                Assert.IsType<string>(expectedIdentity.AccessToken),
                Assert.IsType<string>(result.BearerToken));
            Assert.Null(result.Username);
            Assert.Null(result.Password);
            return;
        }

        Assert.Equal("AzureDevOps", result.Username);
        Assert.Equal(
            Assert.IsType<string>(expectedIdentity.Secret),
            Assert.IsType<string>(result.Password));
        Assert.Null(result.BearerToken);
    }

    [Theory]
    [MemberData(nameof(PasswordShapedTokenExchangeRequests))]
    public void ExecutePasswordShapedRequestCanonicalizesInjectedTokenExchangeUsernameInvariant(
        CredentialRequest request)
    {
        const string injectedUsername = "NotAzureDevOps";
        const string exchangedPassword = "custom-exchanged-password";
        IdentityMaterial identity = CreateIdentityMaterial(
            secret: "provider-secret",
            accessToken: "unused-provider-token");
        var provider = new StaticIdentityProvider(identity);
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) =>
                TokenExchangeResult.Success(
                    new TokenExchangeMaterial
                    {
                        Username = injectedUsername,
                        Password = exchangedPassword,
                    }));
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, tokenExchange.InvocationCount);
        Assert.Equal(identity.Account, result.Account);
        Assert.Equal(identity.Tenant, result.Tenant);
        Assert.Equal(identity.ExpiresAt, result.ExpiresAt);
        Assert.Equal("AzureDevOps", result.Username);
        Assert.NotEqual(injectedUsername, result.Username);
        Assert.Equal(exchangedPassword, Assert.IsType<string>(result.Password));
        Assert.Null(result.BearerToken);
        Assert.Null(result.Error);
    }

    [Theory]
    [MemberData(nameof(BlockedTokenExchangeRequests))]
    public void ExecuteBlockedRequestDoesNotInvokeTokenExchange(CredentialRequest request)
    {
        var provider = new DeterministicFakeIdentityProvider();
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(request);

        Assert.NotEqual(CredentialResultStatus.Success, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
    }

    [Theory]
    [MemberData(nameof(BlockedDirectMsalRequests))]
    public void ExecuteBlockedRequestDoesNotInvokeDirectMsalProvider(
        CredentialRequest request,
        CredentialResultStatus expectedStatus)
    {
        var provider = new CountingDirectMsalIdentityProvider(
            _ => throw new InvalidOperationException("direct msal provider should not run"));
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var service = new CredentialCoreService(
            new DirectMsalIdentityProvider(provider),
            diagnosticRouter: null,
            derivedCredentialCache: null,
            tokenExchange: tokenExchange);

        CredentialResult result = service.Execute(request);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
    }

    [Fact]
    public void ExecuteInvalidProviderMaterialDoesNotInvokeTokenExchange()
    {
        var provider = new StaticIdentityProvider(CreateIdentityMaterial(secret: null));
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.Fatal, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
    }

    [Fact]
    public void ExecuteTokenExchangeUnavailableFailsClosedWithoutLeakingIdentityMaterial()
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            account: "safe-account@example.com",
            tenant: "safe-tenant",
            secret: "safe-secret-value",
            accessToken: "safe-bearer-value");
        var provider = new StaticIdentityProvider(identity);
        var tokenExchange = new CountingTokenExchange((_, _, _) => TokenExchangeResult.Unavailable);
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, tokenExchange.InvocationCount);
        AssertClosedFailureResult(result, identity);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.CredentialUnavailable, error.Kind);
        Assert.Equal("TokenExchangeUnavailable", error.Code);
        Assert.Equal("Credential token exchange is unavailable.", error.SafeMessage);
    }

    [Fact]
    public void ExecuteTokenExchangeFailureFailsClosedWithoutLeakingIdentityMaterial()
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            account: "safe-account@example.com",
            tenant: "safe-tenant",
            secret: "safe-secret-value",
            accessToken: "safe-bearer-value");
        var provider = new StaticIdentityProvider(identity);
        var tokenExchange = new CountingTokenExchange((_, _, _) => TokenExchangeResult.Failed);
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.Fatal, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, tokenExchange.InvocationCount);
        AssertClosedFailureResult(result, identity);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.Fatal, error.Kind);
        Assert.Equal("TokenExchangeFailed", error.Code);
        Assert.Equal("Credential token exchange failed.", error.SafeMessage);
    }

    [Fact]
    public void ExecuteTokenExchangeExceptionFailsClosedWithoutLeakingIdentityMaterial()
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            account: "safe-account@example.com",
            tenant: "safe-tenant",
            secret: "safe-secret-value",
            accessToken: "safe-bearer-value");
        var provider = new StaticIdentityProvider(identity);
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("safe-secret-value"));
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(CreateGitRequest());

        Assert.Equal(CredentialResultStatus.Fatal, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, tokenExchange.InvocationCount);
        AssertClosedFailureResult(result, identity);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.Fatal, error.Kind);
        Assert.Equal("CredentialCoreFailure", error.Code);
        Assert.Equal("Credential core execution failed.", error.SafeMessage);
    }

    [Theory]
    [MemberData(nameof(MalformedTokenExchangeSuccessOutputs))]
    public void ExecuteMalformedTokenExchangeSuccessOutputFailsClosedWithoutLeakingMaterial(
        CredentialRequest request,
        bool returnSuccessWithNullMaterial,
        string? username,
        string? password,
        string? bearerToken,
        string[] rawExchangeValues)
    {
        IdentityMaterial identity = CreateIdentityMaterial(
            account: "safe-account@example.com",
            tenant: "safe-tenant",
            secret: "safe-secret-value",
            accessToken: "safe-bearer-value");
        var provider = new StaticIdentityProvider(identity);
        TokenExchangeResult exchangeResult = returnSuccessWithNullMaterial
            ? new TokenExchangeResult(TokenExchangeStatus.Success, null)
            : TokenExchangeResult.Success(
                new TokenExchangeMaterial
                {
                    Username = username,
                    Password = password,
                    BearerToken = bearerToken,
                });
        var tokenExchange = new CountingTokenExchange((_, _, _) => exchangeResult);
        var service = new CredentialCoreService(provider, null, null, tokenExchange);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Fatal, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, tokenExchange.InvocationCount);
        AssertClosedFailureResult(result, identity, rawExchangeValues);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.Fatal, error.Kind);
    }

    [Theory]
    [MemberData(nameof(ProviderMaterialWithUnusedProtocolLineBreaksScenarios))]
    public void ExecuteIgnoresUnusedProviderMaterialWithProtocolLineBreaks(
        CredentialRequest request,
        IdentityMaterial identity)
    {
        var service = new CredentialCoreService(new StaticIdentityProvider(identity));

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(identity.Account, result.Account);
        Assert.Equal(identity.Tenant, result.Tenant);
        Assert.Equal(identity.ExpiresAt, result.ExpiresAt);
        Assert.Null(result.Error);

        if (request.CredentialKind == CredentialKind.BearerToken)
        {
            Assert.Equal(
                Assert.IsType<string>(identity.AccessToken),
                Assert.IsType<string>(result.BearerToken));
            Assert.Null(result.Username);
            Assert.Null(result.Password);
            return;
        }

        Assert.Equal("AzureDevOps", result.Username);
        Assert.Equal(
            Assert.IsType<string>(identity.Secret),
            Assert.IsType<string>(result.Password));
        Assert.Null(result.BearerToken);
    }

    [Theory]
    [MemberData(nameof(UnsafeProviderMaterialScenarios))]
    public void ExecuteRejectsProviderMaterialWithProtocolLineBreaksWithoutLeakingMaterial(
        CredentialRequest request,
        IdentityMaterial identity)
    {
        List<string> rawProviderValues =
        [
            identity.Account,
            identity.Tenant,
        ];

        if (identity.Secret is not null)
        {
            rawProviderValues.Add(identity.Secret);
        }

        if (identity.AccessToken is not null)
        {
            rawProviderValues.Add(identity.AccessToken);
        }

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

    [Theory]
    [MemberData(nameof(IncompleteProviderMaterialScenarios))]
    public void ExecuteRejectsProviderMaterialWithMissingEmptyOrWhitespaceRequiredMaterial(
        CredentialRequest request,
        IdentityMaterial identity)
    {
        var service = new CredentialCoreService(new StaticIdentityProvider(identity));

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
    public void ExecuteNonGetRequestFailsClosedWithoutInvokingProviderOrTokenExchange()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run"));
        var service = new CredentialCoreService(provider, null, null, tokenExchange);
        CredentialRequest request = CreateGitRequest() with
        {
            Operation = CredentialOperation.Store,
        };

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.Null(result.CacheKey);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.CredentialUnavailable, error.Kind);
        Assert.Equal("OperationNotSupported", error.Code);
        Assert.Equal(
            "Credential core scaffold only supports get operations.",
            error.SafeMessage);
    }

    [Fact]
    public void ExecuteAzurePipelinesSystemAccessTokenRequiresDedicatedOpaqueService()
    {
        IdentityMaterial identity = CreateIdentityMaterial();
        var provider = new StaticIdentityProvider(identity);
        var cache = new ReportingDerivedCredentialCache(
            DerivedCredentialCacheAvailability.Available);
        var exchange = new CountingTokenExchange((_, _, _) =>
            throw new InvalidOperationException("must not exchange"));
        var service = new CredentialCoreService(provider, null, cache, exchange);
        CredentialRequest request = CreateAzurePipelinesSystemAccessTokenRequest(
            CachePolicyMode.NonPersistentCi,
            new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            });

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, cache.PersistentReadCount);
        Assert.Equal(0, cache.PersistentWriteCount);
        Assert.Equal(0, exchange.InvocationCount);
        AssertClosedFailureResult(result, identity);
        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(
            "AzurePipelinesSystemAccessTokenDedicatedServiceRequired",
            error.Code);
        Assert.DoesNotContain("SYSTEM_ACCESSTOKEN", error.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedWp5FlowRequests))]
    public void ExecuteMalformedWp5FlowRequestReturnsProtocolViolationBeforeFlowPolicy(
        CredentialRequest request)
    {
        IdentityMaterial identity = CreateIdentityMaterial();
        var provider = new StaticIdentityProvider(identity);
        var cache = new ReportingDerivedCredentialCache(
            DerivedCredentialCacheAvailability.Available);
        var exchange = new CountingTokenExchange((_, _, _) =>
            throw new InvalidOperationException("must not exchange"));
        var service = new CredentialCoreService(provider, null, cache, exchange);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, cache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, cache.PersistentReadCount);
        Assert.Equal(0, cache.PersistentWriteCount);
        Assert.Equal(0, exchange.InvocationCount);
        AssertClosedFailureResult(result, identity);
        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.ProtocolViolation, error.Kind);
        Assert.Equal("ProtocolViolation", error.Code);
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

        Assert.Equal(CredentialResultStatus.CredentialUnavailable, result.Status);
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
            new SecretRedactor(
                [
                    Assert.IsType<string>(probeIdentity.Secret),
                    Assert.IsType<string>(probeIdentity.AccessToken),
                ]));
        var service = new CredentialCoreService(new DeterministicFakeIdentityProvider(), router);

        CredentialResult result = service.Execute(request);

        string probeSecret = Assert.IsType<string>(probeIdentity.Secret);
        string probeAccessToken = Assert.IsType<string>(probeIdentity.AccessToken);
        string emittedText = diagnosticText.ToString();
        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.NotEmpty(recordingSink.Events);
        Assert.Equal(probeAccessToken, Assert.IsType<string>(result.BearerToken));
        Assert.DoesNotContain(probeSecret, emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(probeAccessToken, emittedText, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretRedactor.DefaultMask, emittedText, StringComparison.Ordinal);

        foreach (DiagnosticEvent diagnosticEvent in recordingSink.Events)
        {
            Assert.DoesNotContain(
                probeSecret,
                diagnosticEvent.Message,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                probeAccessToken,
                diagnosticEvent.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                SecretRedactor.DefaultMask,
                diagnosticEvent.Message,
                StringComparison.Ordinal);

            foreach ((string key, string? value) in diagnosticEvent.Properties)
            {
                Assert.DoesNotContain(probeSecret, key, StringComparison.Ordinal);
                Assert.DoesNotContain(probeAccessToken, key, StringComparison.Ordinal);
                Assert.DoesNotContain(SecretRedactor.DefaultMask, key, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    probeSecret,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    probeAccessToken,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    SecretRedactor.DefaultMask,
                    value ?? string.Empty,
                    StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(CachePolicyMode.NoCache)]
    [InlineData(CachePolicyMode.ProductPersistentCacheDisabled)]
    [InlineData(CachePolicyMode.NonPersistentCi)]
    public void ExecuteAcceptedMvpRequestDoesNotTouchPersistentDerivedCredentialCacheByDefault(
        CachePolicyMode cachePolicy)
    {
        var provider = new DeterministicFakeIdentityProvider();
        var derivedCredentialCache = new NoPersistentDerivedCredentialCache();
        var service = new CredentialCoreService(
            provider,
            derivedCredentialCache: derivedCredentialCache);
        CredentialRequest request = CreateGitRequest(cachePolicy: cachePolicy);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, result.Status);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(0, derivedCredentialCache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, derivedCredentialCache.PersistentReadCount);
        Assert.Equal(0, derivedCredentialCache.PersistentWriteCount);
    }

    [Fact]
    public void ExecutePersistentCacheRequestUsesDefaultNoPersistentCacheAndFailsClosed()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var service = new CredentialCoreService(provider);
        CredentialRequest request = CreateGitRequest(
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.CacheUnavailable, result.Status);
        Assert.Equal(0, provider.InvocationCount);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.CacheUnavailable, error.Kind);
        Assert.Equal("CacheUnavailable", error.Code);
    }

    [Fact]
    public void ExecutePersistentCacheRequestWithAvailableCacheStillFailsClosedByCurrentMvpPolicy()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var derivedCredentialCache = new ReportingDerivedCredentialCache(
            new DerivedCredentialCacheAvailability(
                DerivedCredentialCacheAvailabilityStatus.Available)
        );
        var tokenExchange = new CountingTokenExchange(
            static (_, _, _) =>
                throw new InvalidOperationException("Token exchange should not run.")
        );
        var service = new CredentialCoreService(
            provider,
            diagnosticRouter: null,
            derivedCredentialCache: derivedCredentialCache,
            tokenExchange: tokenExchange
        );
        CredentialRequest request = CreateGitRequest(
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.FlowDisabled, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
        Assert.Equal(1, derivedCredentialCache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, derivedCredentialCache.PersistentReadCount);
        Assert.Equal(0, derivedCredentialCache.PersistentWriteCount);
        Assert.Null(result.Account);
        Assert.Null(result.Tenant);
        Assert.Null(result.CacheKey);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.FlowDisabled, error.Kind);
        Assert.Equal("FlowDisabled", error.Code);
        Assert.Equal(
            "Credential request is disabled by the current MVP policy.",
            error.SafeMessage
        );
    }

    [Theory]
    [InlineData(DerivedCredentialCacheAvailabilityStatus.Unavailable)]
    [InlineData(DerivedCredentialCacheAvailabilityStatus.Denied)]
    [InlineData(DerivedCredentialCacheAvailabilityStatus.Unsupported)]
    [InlineData(DerivedCredentialCacheAvailabilityStatus.VerificationFailed)]
    public void ExecutePersistentCacheRequestFailsClosedWithoutLeakingCredentialMaterial(
        DerivedCredentialCacheAvailabilityStatus availabilityStatus)
    {
        const string fakeSecret = "fake-secret-should-not-leak";
        const string fakeToken = "fake-token-should-not-leak";
        var provider = new DeterministicFakeIdentityProvider();
        var derivedCredentialCache = new ReportingDerivedCredentialCache(
            new DerivedCredentialCacheAvailability(availabilityStatus));
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText), recordingSink],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            provider,
            router,
            derivedCredentialCache);
        CredentialRequest request = CreateGitRequest(
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested);

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.CacheUnavailable, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(1, derivedCredentialCache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, derivedCredentialCache.PersistentReadCount);
        Assert.Equal(0, derivedCredentialCache.PersistentWriteCount);
        Assert.Null(result.Account);
        Assert.Null(result.Tenant);
        Assert.Null(result.CacheKey);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        Assert.Equal(CredentialErrorKind.CacheUnavailable, error.Kind);
        Assert.Equal("CacheUnavailable", error.Code);
        Assert.Equal(
            "Persistent derived credential cache is unavailable.",
            error.SafeMessage);
        Assert.Equal(
            CachePolicyMode.FuturePersistentCacheRequested.ToString(),
            error.SafeDetails["cachePolicy"]);
        Assert.Equal(availabilityStatus.ToString(), error.SafeDetails["cacheAvailability"]);

        foreach (string fakeCredentialMaterial in new[] { fakeSecret, fakeToken })
        {
            Assert.DoesNotContain(
                fakeCredentialMaterial,
                error.SafeMessage,
                StringComparison.Ordinal);

            foreach ((string key, string value) in error.SafeDetails)
            {
                Assert.DoesNotContain(fakeCredentialMaterial, key, StringComparison.Ordinal);
                Assert.DoesNotContain(fakeCredentialMaterial, value, StringComparison.Ordinal);
            }
        }

        string emittedText = diagnosticText.ToString();

        foreach (string fakeCredentialMaterial in new[] { fakeSecret, fakeToken })
        {
            Assert.DoesNotContain(
                fakeCredentialMaterial,
                emittedText,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain(SecretRedactor.DefaultMask, emittedText, StringComparison.Ordinal);

        Assert.NotEmpty(recordingSink.Events);

        foreach (DiagnosticEvent diagnosticEvent in recordingSink.Events)
        {
            Assert.DoesNotContain(
                SecretRedactor.DefaultMask,
                diagnosticEvent.Message,
                StringComparison.Ordinal);

            foreach (string fakeCredentialMaterial in new[] { fakeSecret, fakeToken })
            {
                Assert.DoesNotContain(
                    fakeCredentialMaterial,
                    diagnosticEvent.Message,
                    StringComparison.Ordinal);

                foreach ((string key, string? value) in diagnosticEvent.Properties)
                {
                    Assert.DoesNotContain(fakeCredentialMaterial, key, StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        fakeCredentialMaterial,
                        value ?? string.Empty,
                        StringComparison.Ordinal);
                }
            }

            foreach ((string key, string? value) in diagnosticEvent.Properties)
            {
                Assert.DoesNotContain(SecretRedactor.DefaultMask, key, StringComparison.Ordinal);
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

    [Fact]
    public void ExecuteContractV2MajorFailsClosedWithoutInvokingProviderCacheOrTokenExchange()
    {
        var provider = new DeterministicFakeIdentityProvider();
        var derivedCredentialCache = new ReportingDerivedCredentialCache(
            new DerivedCredentialCacheAvailability(
                DerivedCredentialCacheAvailabilityStatus.Available
            )
        );
        var tokenExchange = new CountingTokenExchange(
            (_, _, _) => throw new InvalidOperationException("token exchange should not run")
        );
        var service = new CredentialCoreService(
            provider,
            diagnosticRouter: null,
            derivedCredentialCache: derivedCredentialCache,
            tokenExchange: tokenExchange
        );
        CredentialRequest request = CreateGitRequest() with
        {
            ContractMajor = ContractVersions.CredentialContractV2Major,
        };

        CredentialResult result = service.Execute(request);

        Assert.Equal(CredentialResultStatus.ProtocolViolation, result.Status);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Equal(0, derivedCredentialCache.PersistentAvailabilityCheckCount);
        Assert.Equal(0, derivedCredentialCache.PersistentReadCount);
        Assert.Equal(0, derivedCredentialCache.PersistentWriteCount);
        Assert.Equal(0, tokenExchange.InvocationCount);
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

    public static TheoryData<CredentialRequest> AcceptedTokenExchangeRequests() =>
        new()
        {
            { CreateGitRequest() },
            { CreateGitRequest(kind: CredentialKind.BearerToken) },
            {
                CreatePackageRequest(CredentialEcosystem.Npm, CredentialKind.NpmAuthToken)
            },
            {
                CreatePackageRequest(
                    CredentialEcosystem.NuGet,
                    CredentialKind.NuGetPluginCredential)
            },
        };

    public static TheoryData<CredentialRequest> PasswordShapedTokenExchangeRequests() =>
        new()
        {
            { CreateGitRequest() },
            {
                    CreatePackageRequest(
                        CredentialEcosystem.NuGet,
                        CredentialKind.NuGetPluginCredential)
            },
        };

    public static TheoryData<CredentialRequest, bool, string?, string?, string?, string[]>
        MalformedTokenExchangeSuccessOutputs() =>
            new()
            {
                    {
                        CreateGitRequest(),
                        true,
                        null,
                        null,
                        null,
                        []
                    },
                    {
                        CreateGitRequest(),
                        false,
                        null,
                        "malformed-exchange-password",
                        null,
                        ["malformed-exchange-password"]
                    },
                    {
                        CreateGitRequest(kind: CredentialKind.BearerToken),
                        false,
                        null,
                        null,
                        null,
                        []
                    },
                    {
                        CreateGitRequest(),
                        false,
                        "AzureDevOps",
                        "malformed-exchange-password",
                        "forbidden-bearer-token",
                        ["malformed-exchange-password", "forbidden-bearer-token"]
                    },
                    {
                        CreateGitRequest(kind: CredentialKind.BearerToken),
                        false,
                        "AzureDevOps",
                        "forbidden-password",
                        "valid-bearer-token",
                        ["forbidden-password", "valid-bearer-token"]
                    },
                    {
                        CreateGitRequest(),
                        false,
                        "Azure\r\nDevOps",
                        "safe-password",
                        null,
                        ["Azure\r\nDevOps", "safe-password"]
                    },
                    {
                        CreateGitRequest(kind: CredentialKind.BearerToken),
                        false,
                        null,
                        null,
                        "unsafe\r\ntoken",
                        ["unsafe\r\ntoken"]
                    },
                    {
                        CreateGitRequest(),
                        false,
                        "AzureDevOps",
                        "unsafe\0password",
                        null,
                        ["unsafe\0password"]
                    },
                    {
                        CreateGitRequest(),
                        false,
                        "AzureDevOps",
                        "unsafe\u001Bpassword",
                        null,
                        ["unsafe\u001Bpassword"]
                    },
                    {
                        CreateGitRequest(),
                        false,
                        "AzureDevOps",
                        "unsafe\u009Fpassword",
                        null,
                        ["unsafe\u009Fpassword"]
                    },
                    {
                        CreateGitRequest(kind: CredentialKind.BearerToken),
                        false,
                        null,
                        null,
                        "unsafe\0token",
                        ["unsafe\0token"]
                    },
                    {
                        CreateGitRequest(kind: CredentialKind.BearerToken),
                        false,
                        null,
                        null,
                        "unsafe\u001Btoken",
                        ["unsafe\u001Btoken"]
                    },
                    {
                        CreateGitRequest(kind: CredentialKind.BearerToken),
                        false,
                        null,
                        null,
                        "unsafe\u009Ftoken",
                        ["unsafe\u009Ftoken"]
                    },
            };

    public static TheoryData<CredentialRequest> BlockedTokenExchangeRequests() =>
        new()
        {
            {
                CreateGitRequest() with
                {
                    ServiceIdentity = "Default",
                }
            },
            {
                CreateGitRequest(
                    flow: IdentityFlow.DeviceCode,
                    kind: CredentialKind.BasicPassword,
                    interactivePolicy: InteractivePolicy.Never)
            },
            {
                CreateGitRequest(
                    cachePolicy: CachePolicyMode.FuturePersistentCacheRequested)
            },
        };

    public static TheoryData<CredentialRequest> MalformedWp5FlowRequests()
    {
        CredentialRequest pat = CreateGitRequest(
            flow: IdentityFlow.PatCompatibility,
            kind: CredentialKind.PatCompatibility,
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested);
        CredentialRequest systemAccessToken =
            CreateAzurePipelinesSystemAccessTokenRequest(
                CachePolicyMode.NonPersistentCi,
                new CiContext
                {
                    ExplicitCiMode = true,
                    Provider = CiProviderNames.AzurePipelines,
                    HasAzurePipelinesSystemAccessToken = true,
                    AllowsPersistentWrites = false,
                });

        return new TheoryData<CredentialRequest>
        {
            pat with { ContractMajor = ContractVersions.CredentialContractV2Major },
            systemAccessToken with
            {
                ContractMajor = ContractVersions.CredentialContractV2Major,
            },
            pat with { Operation = (CredentialOperation)int.MaxValue },
            systemAccessToken with { Operation = CredentialOperation.Unspecified },
            pat with { Resource = null! },
            systemAccessToken with { Resource = null! },
            pat with { AccountHint = "account\u001B" },
            pat with { TenantHint = "tenant\u009F" },
            systemAccessToken with { AccountHint = "account\u001B" },
            systemAccessToken with { TenantHint = "tenant\u009F" },
            pat with { Ecosystem = (CredentialEcosystem)int.MaxValue },
            systemAccessToken with { CachePolicy = (CachePolicyMode)int.MaxValue },
            pat with { CredentialKind = CredentialKind.BearerToken },
            systemAccessToken with { CredentialKind = CredentialKind.BasicPassword },
        };
    }

    public static TheoryData<CredentialRequest, CredentialResultStatus>
        BlockedDirectMsalRequests() =>
        new()
        {
            {
                CreateGitRequest(
                    flow: IdentityFlow.ServicePrincipal,
                    kind: CredentialKind.BasicPassword),
                CredentialResultStatus.FlowDeferred
            },
            {
                CreateAzurePipelinesSystemAccessTokenRequest(
                    CachePolicyMode.NonPersistentCi,
                    ciContext: null),
                CredentialResultStatus.CredentialUnavailable
            },
            {
                CreateGitRequest(
                    cachePolicy: CachePolicyMode.FuturePersistentCacheRequested),
                CredentialResultStatus.CacheUnavailable
            },
        };

    public static TheoryData<CredentialRequest, IdentityMaterial>
        ProviderMaterialWithUnusedProtocolLineBreaksScenarios() =>
            new()
            {
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(accessToken: "unused\r\ntoken")
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(secret: "unused\r\nsecret")
                },
            };

    public static TheoryData<CredentialRequest, IdentityMaterial>
        UnsafeProviderMaterialScenarios() =>
            new()
            {
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: "unsafe\r\nsecret")
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: "unsafe\r\ntoken")
                },
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: "unsafe\0secret")
                },
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: "unsafe\u001Bsecret")
                },
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: "unsafe\u009Fsecret")
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: "unsafe\0token")
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: "unsafe\u001Btoken")
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: "unsafe\u009Ftoken")
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

    public static TheoryData<CredentialRequest, IdentityMaterial>
        IncompleteProviderMaterialScenarios() =>
            new()
            {
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: null)
                },
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: string.Empty)
                },
                {
                    CreateGitRequest(),
                    CreateIdentityMaterial(secret: " \t ")
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: null)
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: string.Empty)
                },
                {
                    CreateGitRequest(kind: CredentialKind.BearerToken),
                    CreateIdentityMaterial(accessToken: " \t ")
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

    private static CredentialRequest CreatePackageRequest(
        CredentialEcosystem ecosystem,
        CredentialKind kind) =>
        new()
        {
            Ecosystem = ecosystem,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "pkgs.dev.azure.com",
                "org",
                ecosystem switch
                {
                    CredentialEcosystem.NuGet => new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
                    ),
                    CredentialEcosystem.Python => new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple"
                    ),
                    _ => new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
                },
                feed: "feed"),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = kind,
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
        string? secret = "safe-secret",
        string? accessToken = "safe-token") =>
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

    private static SecretRedactor CreateBlankingRedactor(params string[] valuesToBlank)
    {
        var secrets = new List<string?> { SecretRedactor.DefaultMask };
        foreach (string value in valuesToBlank)
        {
            secrets.Add(value);
        }

        return new SecretRedactor(secrets);
    }

    private static DiagnosticRouter CreateRecordingDiagnosticRouter(
        StringWriter diagnosticText,
        RecordingDiagnosticSink recordingSink,
        SecretRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(diagnosticText);
        ArgumentNullException.ThrowIfNull(recordingSink);
        ArgumentNullException.ThrowIfNull(redactor);

        return new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticText), recordingSink],
            redactor);
    }

    private static void AssertClosedFailureResult(
        CredentialResult result,
        IdentityMaterial identity,
        params string?[] additionalRawValues)
    {
        Assert.Null(result.Account);
        Assert.Null(result.Tenant);
        Assert.Null(result.CacheKey);
        Assert.Null(result.Username);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);

        CredentialError error = Assert.IsType<CredentialError>(result.Error);
        List<string> rawIdentityValues =
        [
            identity.Account,
            identity.Tenant,
            Assert.IsType<string>(identity.Secret),
            Assert.IsType<string>(identity.AccessToken),
        ];

        foreach (string? rawValue in additionalRawValues)
        {
            if (!string.IsNullOrEmpty(rawValue))
            {
                rawIdentityValues.Add(rawValue);
            }
        }

        foreach (string rawIdentityValue in rawIdentityValues)
        {
            Assert.DoesNotContain(rawIdentityValue, error.SafeMessage, StringComparison.Ordinal);

            foreach ((string key, string value) in error.SafeDetails)
            {
                Assert.DoesNotContain(rawIdentityValue, key, StringComparison.Ordinal);
                Assert.DoesNotContain(rawIdentityValue, value, StringComparison.Ordinal);
            }
        }
    }

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

        public int InvocationCount { get; private set; }

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            InvocationCount++;
            return _identity;
        }
    }

    private sealed class CallbackIdentityProvider(
        Func<CredentialRequest, IdentityMaterial> getIdentity)
        : IIdentityProvider
    {
        private readonly Func<CredentialRequest, IdentityMaterial> _getIdentity = getIdentity;

        public int InvocationCount { get; private set; }

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            InvocationCount++;
            return _getIdentity(request);
        }
    }

    private sealed class CountingDirectMsalIdentityProvider(
        Func<CredentialRequest, DirectMsalIdentityResult> acquireIdentity)
        : IDirectMsalIdentityProvider
    {
        private readonly Func<CredentialRequest, DirectMsalIdentityResult> _acquireIdentity =
            acquireIdentity;

        public int InvocationCount { get; private set; }

        public DirectMsalIdentityResult AcquireIdentity(CredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            InvocationCount++;
            return _acquireIdentity(request);
        }
    }

    private sealed class CountingTokenExchange(
        Func<CredentialRequest, IdentityMaterial, CacheKey, TokenExchangeResult> exchange)
        : ITokenExchange
    {
        private readonly Func<CredentialRequest, IdentityMaterial, CacheKey, TokenExchangeResult>
            _exchange = exchange;

        public int InvocationCount { get; private set; }

        public TokenExchangeResult Exchange(
            CredentialRequest request,
            IdentityMaterial identity,
            CacheKey cacheKey)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(identity);
            ArgumentNullException.ThrowIfNull(cacheKey);
            InvocationCount++;
            return _exchange(request, identity, cacheKey);
        }
    }

    private sealed class ReportingDerivedCredentialCache(
        DerivedCredentialCacheAvailability availability) : IDerivedCredentialCache
    {
        private readonly DerivedCredentialCacheAvailability _availability = availability;

        public int PersistentAvailabilityCheckCount { get; private set; }

        public int PersistentReadCount { get; private set; }

        public int PersistentWriteCount { get; private set; }

        public DerivedCredentialCacheAvailability GetPersistentAvailability(
            CredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            PersistentAvailabilityCheckCount++;
            return _availability;
        }

        public DerivedCredentialCacheReadResult TryReadPersistent(
            CredentialRequest request,
            CacheKey cacheKey)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(cacheKey);
            PersistentReadCount++;
            return DerivedCredentialCacheReadResult.Miss;
        }

        public DerivedCredentialCacheWriteResult TryWritePersistent(
            CredentialRequest request,
            CacheKey cacheKey,
            IdentityMaterial identity)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(cacheKey);
            ArgumentNullException.ThrowIfNull(identity);
            PersistentWriteCount++;
            return DerivedCredentialCacheWriteResult.Written;
        }
    }
}
