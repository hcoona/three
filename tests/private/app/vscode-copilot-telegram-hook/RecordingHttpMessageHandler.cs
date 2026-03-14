using System.Net;
using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

internal sealed class RecordingHttpMessageHandler(
    IEnumerable<HttpResponseMessage>? responses = null) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new(
        responses ?? [CreateJsonResponse(HttpStatusCode.OK, """{"ok":true}""")]);

    public List<CapturedHttpRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(
            new CapturedHttpRequest(
                request.Method,
                request.RequestUri,
                request.Content?.Headers.ContentType?.MediaType,
                body));

        return responses.Count > 0
            ? responses.Dequeue()
            : CreateJsonResponse(HttpStatusCode.OK, """{"ok":true}""");
    }

    public static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    string? MediaType,
    string Body);
