using System.Net;
using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Hcoona.VsCodeCopilotTelegramHook.Tests;

public sealed class TelegramBotClientTests
{
    [Fact]
    public async Task SendMessagesAsyncUsesTelegramApiBaseAddressWhenBotTokenContainsColon()
    {
        RecordingHttpMessageHandler handler = new();
        using ServiceProvider services = CreateServices(handler);
        TelegramBotClient client = services.GetRequiredService<TelegramBotClient>();

        await client.SendMessagesAsync(
            new TelegramCredentials("123456:ABCdef_token", "7713476101", "environment"),
            ["<b>Test message</b>"],
            CancellationToken.None);

        Uri requestUri = Assert.IsType<Uri>(Assert.Single(handler.Requests).RequestUri);
        Assert.Equal("https", requestUri.Scheme);
        Assert.Equal("api.telegram.org", requestUri.Host);
        Assert.Equal("/bot123456:ABCdef_token/sendMessage", requestUri.AbsolutePath);
    }

    [Fact]
    public async Task SendMessagesAsyncRetriesTooManyRequestsViaResilienceAndUsesHtmlPayload()
    {
        RecordingHttpMessageHandler handler = new(
        [
            RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.TooManyRequests,
                """
                {"ok":false,"error_code":429,"description":"Too Many Requests"}
                """),
            RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.OK,
                """{"ok":true}"""),
        ]);
        using ServiceProvider services = CreateServices(handler);
        TelegramBotClient client = services.GetRequiredService<TelegramBotClient>();

        await client.SendMessagesAsync(
            new TelegramCredentials("123456:ABCdef_token", "7713476101", "environment"),
            ["<b>Hello</b>"],
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        TelegramSendMessageRequest requestPayload = DeserializePayload(handler.Requests[0]);
        Assert.Equal("7713476101", requestPayload.ChatId);
        Assert.Equal("<b>Hello</b>", requestPayload.Text);
        Assert.Equal("HTML", requestPayload.ParseMode);
        Assert.Equal("application/json", handler.Requests[0].MediaType);
    }

    [Fact]
    public async Task SendMessagesAsyncRetriesServerErrorsViaResilienceUntilSuccess()
    {
        RecordingHttpMessageHandler handler = new(
        [
            RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.BadGateway,
                """
                {"ok":false,"error_code":502,"description":"Bad Gateway"}
                """),
            RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.OK,
                """{"ok":true}"""),
        ]);
        using ServiceProvider services = CreateServices(handler);
        TelegramBotClient client = services.GetRequiredService<TelegramBotClient>();

        await client.SendMessagesAsync(
            new TelegramCredentials("123456:ABCdef_token", "7713476101", "environment"),
            ["<b>Hello</b>"],
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendMessagesAsyncDoesNotRetryClientErrorsAndThrowsTelegramDescription()
    {
        RecordingHttpMessageHandler handler = new(
        [
            RecordingHttpMessageHandler.CreateJsonResponse(
                HttpStatusCode.BadRequest,
                """
                {"ok":false,"error_code":400,"description":"chat not found"}
                """),
        ]);
        using ServiceProvider services = CreateServices(handler);
        TelegramBotClient client = services.GetRequiredService<TelegramBotClient>();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendMessagesAsync(
                new TelegramCredentials("123456:ABCdef_token", "7713476101", "environment"),
                ["<b>Hello</b>"],
                CancellationToken.None));

        Assert.Equal("chat not found", exception.Message);
        Assert.Single(handler.Requests);
    }

    private static ServiceProvider CreateServices(HttpMessageHandler handler)
    {
        ServiceCollection services = [];
        services.AddLogging();
        services
            .AddHttpClient<TelegramBotClient>(static client =>
            {
                client.BaseAddress = new Uri("https://api.telegram.org/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(static options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            });

        return services.BuildServiceProvider();
    }

    private static TelegramSendMessageRequest DeserializePayload(CapturedHttpRequest request)
    {
        return JsonSerializer.Deserialize(
                request.Body,
                AppJsonSerializerContext.Default.TelegramSendMessageRequest)
            ?? throw new InvalidOperationException("Expected a valid Telegram request payload.");
    }
}
