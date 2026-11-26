using System.Net;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

[TestClass]
public sealed class MsalHttpOwnershipScenarios
{
    [TestMethod]
    public async Task OneOwnedClientSurvivesOperationsUntilCancellationAndDrain()
    {
        var transport = new TerminalHandler();
        using (var factory = new MsalHttpClientFactory(transport))
        {
            var first = factory.GetHttpClient();
            var second = factory.GetHttpClient();
            Assert.AreSame(first, second, "MSAL operations must share the owner's HTTP client.");
            Assert.AreEqual(Timeout.InfiniteTimeSpan, first.Timeout,
                "Only the original request token may bound managed HTTP.");
            for (var index = 0; index < 2; index++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://authority.example.test/synthetic");
                request.Headers.UserAgent.ParseAdd("synthetic-msal/4.83.1");
                using var response = await first.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.IsFalse(transport.Disposed);
            }
            Assert.AreEqual(2, transport.UserAgents.Count);
            foreach (var observed in transport.UserAgents)
                CollectionAssert.AreEquivalent(new[]
                {
                    "synthetic-msal/4.83.1", "hcoona-microsoft-authentication-cli/" + ProductVersion.Semantic,
                }, observed);

            transport.Hold = true;
            using var original = new CancellationTokenSource();
            using var pendingRequest = new HttpRequestMessage(HttpMethod.Get, "https://authority.example.test/synthetic");
            var pending = first.SendAsync(pendingRequest, original.Token);
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(transport.Disposed);
            Assert.IsFalse(pending.IsCompleted);
            original.Cancel();
            try
            {
                using var unexpected = await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Fail("Cancellation must end the pending managed request.");
            }
            catch (OperationCanceledException) { }
            Assert.IsTrue(transport.CancellationObserved);
            Assert.IsFalse(transport.Disposed, "Cancellation must not end the shared client's ownership.");
        }
        Assert.IsTrue(transport.Disposed, "The owner must dispose its client and transport after drain.");
    }

    // There is no inner network handler and no socket can be opened by this fixture.
    private sealed class TerminalHandler : HttpMessageHandler
    {
        internal bool Hold, Disposed, CancellationObserved;
        internal List<string[]> UserAgents { get; } = [];
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.UserAgent.Select(value => value.ToString()).ToArray());
            if (Hold)
            {
                Started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }
            return new(HttpStatusCode.OK);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
