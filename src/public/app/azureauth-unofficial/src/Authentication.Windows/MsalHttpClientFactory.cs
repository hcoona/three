using Microsoft.Identity.Client;

namespace Authentication.Windows;

internal sealed class MsalHttpClientFactory : IMsalHttpClientFactory, IDisposable
{
    private readonly HttpClient client;

    // The original request token is the only HTTP bound. HttpClient's default
    // 100-second timeout would add a second, unrelated deadline that could end a
    // request the engine still permits and report it as a provider failure.
    internal MsalHttpClientFactory(HttpMessageHandler transport) =>
        client = new HttpClient(new ManagedUserAgentHandler(transport), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

    public HttpClient GetHttpClient() => client;

    // The process owner disposes only after its request work has drained.
    public void Dispose() => client.Dispose();
}
