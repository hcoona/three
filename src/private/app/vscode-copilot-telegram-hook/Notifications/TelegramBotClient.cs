using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Notifications;

internal sealed class TelegramBotClient(
    HttpClient httpClient,
    ILogger<TelegramBotClient> logger)
{
    public async Task SendMessagesAsync(
        TelegramCredentials credentials,
        IReadOnlyList<string> htmlMessages,
        CancellationToken cancellationToken)
    {
        foreach (string htmlMessage in htmlMessages)
        {
            await SendMessageAsync(credentials, htmlMessage, cancellationToken);
        }
    }

    private async Task SendMessageAsync(
        TelegramCredentials credentials,
        string htmlMessage,
        CancellationToken cancellationToken)
    {
        TimeSpan fallbackDelay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            AppLog.SendingTelegramAttempt(logger, attempt, credentials.Source, htmlMessage.Length);
            Uri requestUri = new($"/bot{credentials.BotToken}/sendMessage", UriKind.Relative);

            using HttpRequestMessage request = new(
                HttpMethod.Post,
                requestUri)
            {
                Content = JsonContent.Create(
                    new TelegramSendMessageRequest
                    {
                        ChatId = credentials.ChatId,
                        Text = htmlMessage,
                    },
                    AppJsonSerializerContext.Default.TelegramSendMessageRequest),
            };

            HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            TelegramApiResponse? apiResponse = string.IsNullOrWhiteSpace(responseContent)
                ? null
                : JsonSerializer.Deserialize(
                    responseContent,
                    AppJsonSerializerContext.Default.TelegramApiResponse);

            if (response.IsSuccessStatusCode && apiResponse is { Ok: true })
            {
                AppLog.SentTelegramAttempt(logger, attempt);
                return;
            }

            if (attempt < 3 && ShouldRetry(response.StatusCode, apiResponse))
            {
                TimeSpan retryDelay = GetRetryDelay(apiResponse, fallbackDelay, attempt);
                AppLog.RetryingTelegramSend(
                    logger,
                    attempt,
                    (int)response.StatusCode,
                    apiResponse?.ErrorCode,
                    retryDelay.TotalMilliseconds);
                await Task.Delay(retryDelay, cancellationToken);
                fallbackDelay = TimeSpan.FromMilliseconds(
                    Math.Min(fallbackDelay.TotalMilliseconds * 2, 2_000));
                continue;
            }

            string description = apiResponse?.Description
                ?? response.ReasonPhrase
                ?? $"Telegram delivery failed with HTTP {(int)response.StatusCode}.";
            AppLog.TelegramDeliveryFailed(
                logger,
                attempt,
                (int)response.StatusCode,
                apiResponse?.ErrorCode,
                description);

            throw new InvalidOperationException(description);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, TelegramApiResponse? apiResponse)
    {
        if (apiResponse?.ErrorCode == 429)
        {
            return true;
        }

        if (statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if ((int)statusCode >= 500)
        {
            return true;
        }

        return apiResponse is { Ok: false, ErrorCode: >= 500 };
    }

    private static TimeSpan GetRetryDelay(
        TelegramApiResponse? apiResponse,
        TimeSpan fallbackDelay,
        int attempt)
    {
        if (apiResponse?.Parameters?.RetryAfterSeconds is int retryAfterSeconds
            && retryAfterSeconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds, 10));
        }

        return attempt switch
        {
            1 => TimeSpan.FromMilliseconds(500),
            2 => TimeSpan.FromSeconds(1.5),
            _ => fallbackDelay,
        };
    }
}
