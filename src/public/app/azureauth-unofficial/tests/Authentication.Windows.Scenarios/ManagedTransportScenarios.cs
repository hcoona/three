using System.Net;
using Authentication.Core;
using Authentication.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

[TestClass]
public sealed class ManagedTransportScenarios
{
    [TestMethod]
    public async Task ManagedUserAgentIsSingleStableAndForwardsCancellation()
    {
        var product = "hcoona-microsoft-authentication-cli/" + ProductVersion.Semantic;
        using var original = new CancellationTokenSource();
        using var terminal = new CaptureHandler();
        using var handler = new ManagedUserAgentHandler(terminal);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://resource.example.test/first");
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://resource.example.test/second");
        first.Headers.UserAgent.ParseAdd("SyntheticDependency/1.0");
        second.Headers.UserAgent.ParseAdd("SyntheticDependency/1.0 " + product);
        using var firstResponse = await invoker.SendAsync(first, original.Token);
        using var secondResponse = await invoker.SendAsync(second, original.Token);

        Assert.AreEqual(2, terminal.Observations.Count);
        foreach (var observation in terminal.Observations)
        {
            Assert.AreEqual(1, observation.Headers.Count(value => value == product));
            CollectionAssert.AreEquivalent(new[] { "SyntheticDependency/1.0", product }, observation.Headers);
            Assert.AreEqual(original.Token, observation.Token);
        }
        original.Cancel();
        Assert.IsTrue(terminal.Observations.All(observation => observation.Token.IsCancellationRequested));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<(string[] Headers, CancellationToken Token)> Observations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Observations.Add((request.Headers.UserAgent.Select(value => value.ToString()).ToArray(), cancellationToken));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
