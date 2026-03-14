using System.Net;
using System.Text;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class TelegramBotClientTests
{
    [Fact]
    public async Task SendMessagesAsyncUsesTelegramApiBaseAddressWhenBotTokenContainsColon()
    {
        RecordingHttpMessageHandler handler = new();
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org/"),
        };

        TelegramBotClient client = new(httpClient);

        await client.SendMessagesAsync(
            new TelegramCredentials("123456:ABCdef_token", "7713476101", "environment"),
            ["<b>Test message</b>"],
            CancellationToken.None);

        Uri requestUri = Assert.IsType<Uri>(handler.RequestUri);
        Assert.Equal("https", requestUri.Scheme);
        Assert.Equal("api.telegram.org", requestUri.Host);
        Assert.Equal("/bot123456:ABCdef_token/sendMessage", requestUri.AbsolutePath);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ok\":true}",
                    Encoding.UTF8,
                    "application/json"),
            };

            return Task.FromResult(response);
        }
    }
}
