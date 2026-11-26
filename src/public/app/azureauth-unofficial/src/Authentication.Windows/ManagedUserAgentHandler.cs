using System.Net.Http.Headers;
using Authentication.Core;

namespace Authentication.Windows;

internal sealed class ManagedUserAgentHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const string ProductName = "hcoona-microsoft-authentication-cli";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headers = request.Headers.UserAgent;
        foreach (var prior in headers.Where(value =>
            string.Equals(value.Product?.Name, ProductName, StringComparison.Ordinal)).ToArray())
            headers.Remove(prior);

        headers.Add(new ProductInfoHeaderValue(ProductName, ProductVersion.Semantic));
        return base.SendAsync(request, cancellationToken);
    }
}
