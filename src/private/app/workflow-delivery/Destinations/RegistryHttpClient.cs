using System.Net;

namespace WorkflowDelivery.Destinations;

// Registry observation reads follow redirects through the platform handler. Its
// documented contract clears the Authorization header on every automatic redirect
// and refuses HTTPS-to-HTTP redirects; an unfollowed 3xx, including one beyond
// the redirect limit, reaches the adapter as a non-success status.
internal static class RegistryHttpClient
{
    internal const int MaxRedirects = 5;

    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true, MaxAutomaticRedirections = MaxRedirects,
        UseCookies = false, UseProxy = false, UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None,
    };

    internal static HttpClient Create() => new(CreateHandler())
        { Timeout = Timeout.InfiniteTimeSpan };
}
