using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Authentication.Core;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Execute the real adapter and coordinator with a session that cannot reach MSAL
// construction, native libraries, account state, network, windows, or child processes.
[TestClass]
public sealed class MsalCompositionScenarios
{
    private const string Email = "personal@example.test";
    private const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation";
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string MsaTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";
    private const string TransferTenant = "f8cdef31-a31e-4b4a-93e4-5f571e91255a";
    private const string PrivateMarker = "SYNTHETIC-MSAL-COMPOSITION-PRIVATE";
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public Task OrdinaryMultitenantProfileUsesCommon() =>
        VerifyRouteAsync(false, "common", false, "common", "common");

    [TestMethod]
    public Task FixedWorkProfileUsesItsTenant() =>
        VerifyRouteAsync(false, "fixed", false, Tenant, Tenant);

    [TestMethod]
    public Task ExplicitWorkTenantOverridesCommon() =>
        VerifyRouteAsync(false, "explicit", false, Tenant, Tenant);

    [TestMethod]
    public Task LegacyPersonalAccountUsesTheTransferTenant() =>
        VerifyRouteAsync(true, "common", true, "organizations", TransferTenant);

    [TestMethod]
    public Task LegacyWorkAccountRetainsOrganizations() =>
        VerifyRouteAsync(true, "common", false, "organizations", "organizations");

    [TestMethod]
    public Task ExplicitResourceTenantWinsOverLegacyPersonalRouting() =>
        VerifyRouteAsync(true, "explicit", true, Tenant, Tenant);

    private static async Task VerifyRouteAsync(bool legacy, string tenantPolicy,
        bool personalAccount, string initialTenant, string silentTenant)
    {
        using var scene = new Scene(legacy, tenantPolicy, personalAccount);
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success, "The admitted selected-account journey must succeed.");
        Assert.IsNotNull(scene.Settings);
        Assert.AreEqual(scene.Profile.ClientId, scene.Settings.ClientId);
        Assert.AreEqual("https://login.microsoftonline.com/" + initialTenant, scene.Settings.Authority);
        Assert.AreEqual("ms-appx-web://microsoft.aad.brokerplugin/" + scene.Profile.ClientId,
            scene.Settings.RedirectUri);
        Assert.IsTrue(scene.Settings.ListOperatingSystemAccounts);
        Assert.AreEqual(legacy, scene.Settings.MsaPassthrough);
        Assert.AreSame(scene, scene.HttpFactory);
        // Discovery and the silent call use the session created for this request.
        foreach (var name in new[] { "session", "discover", "silent" })
            CollectionAssert.Contains(scene.Events, name, $"The {name} event was not observed.");
        Assert.IsTrue(scene.Events.IndexOf("session") < scene.Events.IndexOf("discover"));
        Assert.IsTrue(scene.Events.IndexOf("discover") < scene.Events.IndexOf("silent"));
        Assert.AreSame(scene.Selected, scene.Silent[0].Account, "The nonmatching first account must not be selected.");
        Assert.AreEqual(silentTenant, scene.Silent[0].Tenant);
        CollectionAssert.AreEqual(new[] { Scope }, scene.Silent[0].Scopes.ToArray());
        Assert.AreNotEqual(Guid.Empty, scene.Silent[0].CorrelationId);
        Assert.AreEqual(scene.Silent[0].CorrelationId, outcome.Success.CorrelationId);
        Assert.AreEqual(scene.Silent[0].CorrelationId, outcome.Success.OperationId);
        Assert.AreEqual(Email, outcome.Success.Email);
        Assert.AreEqual(Guid.Parse(Tenant), outcome.Success.Tenant);
        Assert.AreEqual(0, scene.Interactive.Count);
        Assert.AreEqual(0, scene.Opened);
        Assert.IsTrue(scene.Tokens.All(token => token == scene.Original.Token));
        foreach (var name in new[] { "admitted", "loader" })
            CollectionAssert.Contains(scene.Events, name, $"The {name} event was not observed.");
        Assert.IsTrue(scene.Events.IndexOf("admitted") < scene.Events.IndexOf("loader"));
        Assert.IsTrue(scene.Events.IndexOf("loader") < scene.Events.IndexOf("session"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ForcedOperationCarriesNeitherAccountNorLoginHint(bool accountVisible)
    {
        // Forced interaction never binds the discovered account, even when one exact
        // match is visible, and carries no login hint (see the builder scenarios
        // below). Final identity validation then rejects any other returned account.
        using var scene = new Scene(forceInteractive: true) { IncludeSelectedAccount = accountVisible };
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success);
        Assert.IsTrue(outcome.Interactive);
        Assert.AreEqual(1, scene.Discoveries);
        Assert.AreEqual(0, scene.Silent.Count);
        Assert.AreEqual(1, scene.Interactive.Count);
        var operation = scene.Interactive[0];
        Assert.IsTrue(operation.ForceInteractive);
        Assert.IsNull(operation.Account);
        Assert.IsNull(operation.LoginHint);
        Assert.IsNull(operation.Claims);
        Assert.AreEqual(Tenant, operation.Tenant);
        Assert.AreEqual((nint)42, operation.ParentWindow);
        CollectionAssert.AreEqual(new[] { Scope }, operation.Scopes.ToArray());
        Assert.AreEqual(operation.CorrelationId, outcome.Success.OperationId);
        Assert.AreEqual(1, scene.Opened);
        Assert.AreEqual(1, scene.Closed);
        Assert.IsTrue(scene.Tokens.All(token => token == scene.Original.Token));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void RealInteractiveBuilderBindsAnIdentityOnlyForOrdinaryRequests(bool forced, bool accountVisible)
    {
        using var scene = new Scene();
        // Construction only: no broker is registered and this builder is never executed.
        var application = (PublicClientApplication)PublicClientApplicationBuilder
            .Create(scene.Profile.ClientId.ToString("D"))
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithRedirectUri("http://localhost")
            .WithHttpClientFactory(scene)
            .Build();
        // The adapter projects neither an account nor a hint into a forced operation.
        var account = accountVisible && !forced ? scene.Selected : null;
        var hint = account is null && !forced ? Email : null;
        const string claims = "{\"access_token\":{\"synthetic_claim\":null}}";
        var correlation = Guid.NewGuid();
        var operation = new MsalInteractiveOperation([Scope], account, hint, Tenant,
            claims, (nint)42, correlation, forced);
        var builder = MsalSessionFactory.BuildInteractiveRequest(application, operation,
            null, scene.Original.Token);

        // Pinned-dependency contract probe: reflection reads non-public members of
        // the pinned MSAL 4.83.1 builder, so a dependency update that renames them
        // fails here rather than silently skipping the check. It inspects the real
        // request boundary, not the session fake.
        var interactive = BuilderMember(builder, "Parameters")!;
        var common = BuilderMember(builder, "CommonParameters")!;
        Assert.AreSame(account, BuilderMember(interactive, "Account"));
        Assert.AreEqual(hint, BuilderMember(interactive, "LoginHint"));
        Assert.IsNull(BuilderMember(common, "ExtraQueryParameters"));
        Assert.IsNull(BuilderMember(common, "CacheKeyComponents"));
        CollectionAssert.AreEqual(new[] { Scope }, ((IEnumerable<string>)BuilderMember(common, "Scopes")!).ToArray());
        Assert.AreEqual(claims, BuilderMember(common, "Claims"));
        Assert.AreEqual(correlation, BuilderMember(common, "UserProvidedCorrelationId"));
        var parent = BuilderMember(interactive, "UiParent")!;
        Assert.AreEqual((nint)42, BuilderMember(parent, "OwnerWindow"));
        Assert.IsInstanceOfType<RejectingWebUi>(BuilderMember(interactive, "CustomWebUi"));
        Assert.AreEqual(0, scene.Discoveries);
        Assert.IsEmpty(scene.Silent);
        Assert.IsEmpty(scene.Interactive);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void RealInteractiveBuilderRejectsAForcedIdentity(bool withAccount, bool withHint)
    {
        using var scene = new Scene();
        // Construction only: no broker is registered and this builder is never executed.
        var application = (PublicClientApplication)PublicClientApplicationBuilder
            .Create(scene.Profile.ClientId.ToString("D"))
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithRedirectUri("http://localhost")
            .WithHttpClientFactory(scene)
            .Build();
        var operation = new MsalInteractiveOperation([Scope], withAccount ? scene.Selected : null,
            withHint ? Email : null, Tenant, null, (nint)42, Guid.NewGuid(), true);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            MsalSessionFactory.BuildInteractiveRequest(application, operation, null, scene.Original.Token));
    }

    [TestMethod]
    public void RealInteractiveBuilderRejectsAnOrdinaryRequestWithoutIdentity()
    {
        using var scene = new Scene();
        // Construction only: no broker is registered and this builder is never executed.
        var application = (PublicClientApplication)PublicClientApplicationBuilder
            .Create(scene.Profile.ClientId.ToString("D"))
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithRedirectUri("http://localhost")
            .WithHttpClientFactory(scene)
            .Build();
        var operation = new MsalInteractiveOperation([Scope], null, null, Tenant, null, (nint)42, Guid.NewGuid(), false);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            MsalSessionFactory.BuildInteractiveRequest(application, operation, null, scene.Original.Token));
    }

    internal static object? BuilderMember(object instance, string name)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null) return property.GetValue(instance);
        }
        throw new AssertFailedException($"The pinned MSAL request member {name} is missing.");
    }

    [TestMethod]
    public async Task SilentClaimsContinueWithTheSameAccountAndNoCompetingHint()
    {
        using var scene = new Scene();
        const string claims = "{\"access_token\":{\"synthetic_claim\":null}}";
        scene.SilentFailure = Challenge(claims);
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success, "A permitted silent challenge must reach one successful continuation.");
        Assert.IsTrue(outcome.Interactive);
        Assert.AreEqual(1, scene.Silent.Count);
        Assert.AreEqual(1, scene.Interactive.Count);
        var operation = scene.Interactive[0];
        Assert.IsFalse(operation.ForceInteractive);
        Assert.AreSame(scene.Selected, operation.Account);
        Assert.IsNull(operation.LoginHint);
        Assert.AreEqual(claims, operation.Claims);
        Assert.AreEqual(Tenant, operation.Tenant);
        Assert.AreEqual((nint)42, operation.ParentWindow);
        CollectionAssert.AreEqual(new[] { Scope }, operation.Scopes.ToArray());
        Assert.AreEqual(operation.CorrelationId, outcome.Success.OperationId);
        Assert.AreNotEqual(scene.Silent[0].CorrelationId, operation.CorrelationId);
        Assert.AreEqual(1, scene.Opened);
        Assert.AreEqual(1, scene.Closed);
        Assert.IsTrue(scene.Tokens.All(token => token == scene.Original.Token));
    }

    [TestMethod]
    public async Task NoVisibleMatchUsesOnlyTheRequestedLoginHint()
    {
        using var scene = new Scene { IncludeSelectedAccount = false };
        var outcome = await scene.RunAsync();
        Assert.IsNotNull(outcome.Success, "No match with permission must use the requested login hint.");
        Assert.AreEqual(0, scene.Silent.Count);
        Assert.AreEqual(1, scene.Interactive.Count);
        Assert.IsNull(scene.Interactive[0].Account);
        Assert.IsFalse(scene.Interactive[0].ForceInteractive);
        Assert.AreEqual(Email, scene.Interactive[0].LoginHint);
        Assert.IsNull(scene.Interactive[0].Claims);
        Assert.AreEqual(Tenant, scene.Interactive[0].Tenant);
        Assert.AreEqual(1, scene.Closed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResultFromAnotherCorrelationCannotBecomeSuccess(bool interactive)
    {
        // The candidate binds to the provider-returned correlation ID; the ID the
        // adapter sent is never copied into the candidate as proof.
        using var scene = new Scene { IncludeSelectedAccount = !interactive, ReturnedCorrelation = Guid.NewGuid() };
        Failure(AuthenticationFailure.IdentityValidationFailed, await scene.RunAsync());
        Assert.AreEqual(interactive ? 0 : 1, scene.Silent.Count);
        Assert.AreEqual(interactive ? 1 : 0, scene.Interactive.Count);
    }

    [TestMethod]
    public async Task ResultWithoutCorrelationCannotBecomeSuccess()
    {
        using var scene = new Scene { ReturnedCorrelation = Guid.Empty };
        Failure(AuthenticationFailure.IdentityValidationFailed, await scene.RunAsync());
        Assert.AreEqual(1, scene.Silent.Count);
    }

    [TestMethod]
    public async Task ASecondChallengeStopsAndDoesNotExposeProviderDetails()
    {
        using var scene = new Scene
        {
            SilentFailure = Challenge("{\"synthetic\":true}"),
            InteractiveFailure = Challenge("{\"second\":true}"),
        };
        Failure(AuthenticationFailure.InteractionRequired, await scene.RunAsync());
        Assert.AreEqual(1, scene.Silent.Count);
        Assert.AreEqual(1, scene.Interactive.Count);
        Assert.AreEqual(1, scene.Closed);
    }

    [TestMethod]
    public async Task DiscoveryFailureUsesTheSameSafeProviderClassification()
    {
        using var scene = new Scene { DiscoveryFailure = new MsalClientException("network_not_available", PrivateMarker) };
        var outcome = await scene.RunAsync();
        Failure(AuthenticationFailure.TemporarilyUnavailable, outcome);
        Assert.AreEqual(AuthenticationReason.NetworkTransient, outcome.Reason);
        Assert.AreEqual(1, scene.Discoveries);
        Assert.AreEqual(0, scene.Silent.Count);
        Assert.AreEqual(0, scene.Opened);
    }

    [TestMethod]
    public async Task ProviderInitializationFailureUsesTheSameSafeClassification()
    {
        using var scene = new Scene { InitializationFailure = new MsalClientException("network_not_available", PrivateMarker) };
        Failure(AuthenticationFailure.TemporarilyUnavailable, await scene.RunAsync());
        Assert.AreEqual(1, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
    }

    [TestMethod]
    public async Task CancellationDuringLoaderSetupPreventsSessionConstruction()
    {
        using var scene = new Scene();
        scene.DuringLoader = () => scene.Original.Cancel();
        Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
        Assert.IsTrue(scene.Events.Contains("loader"), "The controlled loader boundary must be reached.");
        Assert.AreEqual(0, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
    }

    [TestMethod]
    public async Task FailedLoaderSetupPreventsSessionConstruction()
    {
        using var scene = new Scene();
        scene.DuringLoader = () => throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable);
        Failure(AuthenticationFailure.MechanismUnavailable, await scene.RunAsync());
        Assert.IsTrue(scene.Events.Contains("loader"), "Unavailability must come from the attempted loader restriction.");
        Assert.AreEqual(0, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
    }

    [TestMethod]
    public async Task CancellationDuringSessionConstructionPreventsDiscovery()
    {
        using var scene = new Scene();
        scene.DuringInitialization = () => scene.Original.Cancel();
        Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
        Assert.AreEqual(1, scene.Initializations);
        Assert.AreEqual(0, scene.Discoveries);
    }

    [TestMethod]
    public async Task OriginalCancellationWinsOverADiscoveryFailure()
    {
        using var scene = new Scene();
        scene.DuringDiscovery = () => scene.Original.Cancel();
        scene.DiscoveryFailure = new MsalClientException("access_denied", PrivateMarker);
        Failure(AuthenticationFailure.Cancelled, await scene.RunAsync());
        Assert.AreEqual(1, scene.Discoveries);
        Assert.AreEqual(0, scene.Silent.Count);
        Assert.AreEqual(0, scene.Opened);
    }

    private static MsalUiRequiredException Challenge(string claims)
    {
        var value = JsonNode.Parse(new MsalUiRequiredException("invalid_grant", PrivateMarker).ToJsonString())!;
        value["claims"] = claims;
        return (MsalUiRequiredException)MsalException.FromJsonString(value.ToJsonString());
    }

    private static void Failure(AuthenticationFailure expected, AuthenticationOutcome outcome)
    {
        Assert.AreEqual(expected, outcome.Failure);
        Assert.IsNull(outcome.Success);
        var result = ResultProjection.Serialize(outcome);
        Assert.AreEqual(1, result.ExitCode);
        var text = Encoding.UTF8.GetString(result.Utf8Json);
        Assert.IsFalse(text.Contains(PrivateMarker, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("accessToken", StringComparison.Ordinal));
    }

    private sealed class Scene : IMsalSessionFactory, IMsalSession, IMsalHttpClientFactory,
        IWindowsHostAdmission, IRequestHost, IDisposable
    {
        internal Scene(bool legacy = false, string tenantPolicy = "explicit", bool personalAccount = false,
            bool forceInteractive = false)
        {
            Profile = new("Synthetic", Guid.Parse(legacy
                ? "872cd9fa-d31f-45e0-9eab-6e460a02d1f1" : "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                tenantPolicy == "fixed" ? Guid.Parse(Tenant) : null,
                legacy ? "visual-studio-legacy-wam" : "windows-wam",
                legacy ? "Microsoft" : "Synthetic fixture owner", "Synthetic fixture");
            var selector = tenantPolicy == "explicit" ? Tenant : null;
            Assert.IsTrue(ProfileSyntax.TryResolveTenant(Profile, selector, out var exactTenant));
            Request = new(Email, [Scope], true, exactTenant, forceInteractive);
            Selected = new("PERSONAL@example.test", personalAccount ? MsaTenant : Tenant);
        }

        internal ClientProfile Profile { get; }
        internal AuthenticationRequest Request { get; }
        internal SyntheticAccount Selected { get; }
        internal CancellationTokenSource Original { get; } = new();
        internal MsalClientSettings? Settings;
        internal IMsalHttpClientFactory? HttpFactory;
        internal bool IncludeSelectedAccount { get; init; } = true;
        // Null echoes the operation's correlation ID as pinned MSAL does.
        internal Guid? ReturnedCorrelation { get; init; }
        internal Exception? InitializationFailure, DiscoveryFailure, SilentFailure, InteractiveFailure;
        internal Action? DuringLoader, DuringInitialization, DuringDiscovery;
        internal int Initializations, Discoveries, Opened, Closed;
        internal List<string> Events { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];
        internal List<MsalSilentOperation> Silent { get; } = [];
        internal List<MsalInteractiveOperation> Interactive { get; } = [];
        public TimeProvider Clock { get; } = new FixedClock();

        internal Task<AuthenticationOutcome> RunAsync()
        {
            var provider = new LocalWindowsProvider(this, token =>
                MsalAuthenticationProvider.Initialize(Profile, Request, this, this, RestrictLoader, token));
            return new RequestCoordinator(provider, this).AuthenticateAsync(Request, Original.Token);
        }

        public void Admit(CancellationToken token) { Events.Add("admitted"); Tokens.Add(token); }
        public void Recheck(CancellationToken token) { Events.Add("rechecked"); Tokens.Add(token); }
        private void RestrictLoader(CancellationToken token)
        {
            Events.Add("loader"); Tokens.Add(token); DuringLoader?.Invoke();
        }

        public IMsalSession Create(MsalClientSettings settings, IMsalHttpClientFactory http, CancellationToken token)
        {
            Initializations++; Events.Add("session"); Tokens.Add(token);
            Settings = settings; HttpFactory = http;
            DuringInitialization?.Invoke();
            if (InitializationFailure is { } failure) throw failure;
            return this;
        }

        public Task<IReadOnlyList<IAccount>> GetAccountsAsync(CancellationToken token)
        {
            Discoveries++; Events.Add("discover"); Tokens.Add(token); DuringDiscovery?.Invoke();
            if (DiscoveryFailure is { } failure) return Task.FromException<IReadOnlyList<IAccount>>(failure);
            IReadOnlyList<IAccount> accounts = IncludeSelectedAccount
                ? [new SyntheticAccount("other@example.test", Tenant), Selected]
                : [new SyntheticAccount("other@example.test", Tenant)];
            return Task.FromResult(accounts);
        }

        public Task<AuthenticationResult> AcquireSilentAsync(MsalSilentOperation operation, CancellationToken token)
        {
            Silent.Add(operation); Events.Add("silent"); Tokens.Add(token);
            return SilentFailure is { } failure ? Task.FromException<AuthenticationResult>(failure)
                : Task.FromResult(Result(operation.CorrelationId));
        }

        public Task<AuthenticationResult> AcquireInteractiveAsync(MsalInteractiveOperation operation, CancellationToken token)
        {
            Interactive.Add(operation); Tokens.Add(token);
            return InteractiveFailure is { } failure ? Task.FromException<AuthenticationResult>(failure)
                : Task.FromResult(Result(operation.CorrelationId));
        }

        public HttpClient GetHttpClient() => throw new InvalidOperationException("Synthetic MSAL sessions do not use HTTP.");
        public Task<nint> OpenInteractionAsync(CancellationToken token)
        {
            Opened++; Tokens.Add(token); return Task.FromResult<nint>(42);
        }
        public Task CloseInteractionAsync() { Closed++; return Task.CompletedTask; }
        public void Dispose() => Original.Dispose();
        private AuthenticationResult Result(Guid sent) => new("synthetic-access", false, PrivateMarker,
            Now.AddHours(1), Now.AddHours(1), Tenant, new SyntheticAccount(Email, Tenant), PrivateMarker,
            [Scope], ReturnedCorrelation ?? sent, tokenType: "Bearer");
    }

    private sealed class SyntheticAccount(string username, string homeTenant) : IAccount
    {
        public string Username => username;
        public string Environment => "login.microsoftonline.com";
        public AccountId HomeAccountId { get; } = new(
            "55555555-6666-7777-8888-999999999999." + homeTenant,
            "55555555-6666-7777-8888-999999999999", homeTenant);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
