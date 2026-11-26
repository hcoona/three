using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Exercise the production construction phase without availability or account calls.
[TestClass]
public sealed class MsalConstructionScenarios
{
    private static readonly Guid ClientId = new("22222222-3333-4444-5555-666666666666");
    private const string ExactTenant = "11111111-2222-3333-4444-555555555555";

    [TestMethod]
    public void OrdinaryProfileConstructsCommonApplication() => VerifyConstruction("common", false);

    [TestMethod]
    public void LegacyProfileConstructsOrganizationsApplication() => VerifyConstruction("organizations", true);

    [TestMethod]
    public void ExactTenantProfileConstructsRestrictedApplication() => VerifyConstruction(ExactTenant, false);

    private static void VerifyConstruction(string tenant, bool legacy)
    {
        using var http = new ConstructionHttp();
        var settings = Settings(tenant, legacy);
        var application = MsalSessionFactory.BuildApplication(settings, http, CancellationToken.None);
        Assert.IsNotNull(application, "The admitted settings must construct the real public-client application.");
        var config = application.AppConfig;
        Assert.AreEqual(ClientId.ToString("D"), config.ClientId);
        Assert.AreEqual(settings.Authority + "/", application.Authority);
        Assert.AreEqual(settings.RedirectUri, config.RedirectUri);
        Assert.AreSame(http, config.HttpClientFactory);
        Assert.IsTrue(config.IsBrokerEnabled);
        Assert.IsFalse(config.EnablePiiLogging);
        Assert.IsFalse(config.IsDefaultPlatformLoggingEnabled);
        Assert.IsNull(config.LoggingCallback);
        Assert.IsNull(config.ClientCapabilities);
        var broker = MsalSessionFactory.Broker(settings);
        Assert.AreEqual("Unofficial hcoona/microsoft-authentication-cli", broker.Title);
        Assert.AreEqual(settings.ListOperatingSystemAccounts, broker.ListOperatingSystemAccounts);
        Assert.AreEqual(settings.MsaPassthrough, broker.MsaPassthrough);
        Assert.AreEqual(0, http.ClientRequests, "Construction must not request an HTTP client.");
        Assert.AreEqual(0, http.Transport.Sends, "Construction must not send an HTTP request.");
    }

    [TestMethod]
    public void OriginalCancellationPreventsApplicationConstruction()
    {
        using var original = new CancellationTokenSource();
        original.Cancel();
        using var http = new ConstructionHttp();
        try
        {
            _ = MsalSessionFactory.BuildApplication(Settings("common", false), http, original.Token);
            Assert.Fail("The original canceled token must stop construction.");
        }
        catch (OperationCanceledException exception)
        {
            Assert.AreEqual(original.Token, exception.CancellationToken);
        }
        Assert.AreEqual(0, http.ClientRequests);
        Assert.AreEqual(0, http.Transport.Sends);
    }

    private static MsalClientSettings Settings(string tenant, bool legacy) =>
        new(ClientId, "https://login.microsoftonline.com/" + tenant,
            "ms-appx-web://microsoft.aad.brokerplugin/" + ClientId,
            ListOperatingSystemAccounts: true, MsaPassthrough: legacy);

    [TestMethod]
    public void UnavailableBrokerStopsSessionCreationWithItsFixedCategory()
    {
        // Only the availability answer is substituted; the real application is built
        // from the admitted settings and handed to the probe before the decision.
        using var http = new ConstructionHttp();
        var trace = new WindowsMechanismTrace();
        PublicClientApplication? probed = null;
        var factory = new MsalSessionFactory(trace, application => { probed = application; return false; });

        var failure = Assert.ThrowsExactly<Authentication.Core.ProviderFailureException>(() =>
            factory.Create(Settings("common", false), http, CancellationToken.None));

        Assert.AreEqual(Authentication.Core.AuthenticationFailure.MechanismUnavailable, failure.Failure);
        Assert.AreEqual(WindowsMechanismFailure.BrokerUnavailable, trace.Failure);
        Assert.IsNotNull(probed);
        Assert.IsTrue(probed.AppConfig.IsBrokerEnabled);
        Assert.AreEqual(0, http.Transport.Sends, "An unavailable broker must not lead to any HTTP request.");
    }

    [TestMethod]
    public void CancellationDuringTheAvailabilityProbeWinsOverItsAnswer()
    {
        using var http = new ConstructionHttp();
        using var original = new CancellationTokenSource();
        var trace = new WindowsMechanismTrace();
        var factory = new MsalSessionFactory(trace, _ => { original.Cancel(); return false; });

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            factory.Create(Settings("common", false), http, original.Token));
        Assert.AreEqual(WindowsMechanismFailure.Unavailable, trace.Failure);
    }

    private sealed class ConstructionHttp : IMsalHttpClientFactory, IDisposable
    {
        private readonly MsalHttpClientFactory owner;
        internal RejectingTransport Transport { get; } = new();
        internal int ClientRequests { get; private set; }

        internal ConstructionHttp() => owner = new(Transport);

        public HttpClient GetHttpClient()
        {
            ClientRequests++;
            return owner.GetHttpClient();
        }

        public void Dispose() => owner.Dispose();
    }

    private sealed class RejectingTransport : HttpMessageHandler
    {
        internal int Sends { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Sends++;
            throw new InvalidOperationException("The construction fixture forbids HTTP requests.");
        }
    }
}
