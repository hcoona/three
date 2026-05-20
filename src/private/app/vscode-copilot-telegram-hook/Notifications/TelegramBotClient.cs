using System.Net.Http.Json;
using System.Text.Json;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Notifications;

internal sealed class TelegramBotClient(
    HttpClient httpClient,
    ILogger<TelegramBotClient> logger)
{
    public async Task<int> SendMessagesAsync(
        TelegramCredentials credentials,
        IReadOnlyList<string> htmlMessages,
        CancellationToken cancellationToken)
    {
        int successfulMessageCount = 0;
        foreach (string htmlMessage in htmlMessages)
        {
            try
            {
                await SendMessageAsync(credentials, htmlMessage, cancellationToken);
                successfulMessageCount++;
            }
            catch (Exception ex) when (
                ex is not TelegramSendMessagesException
                && (ex is not OperationCanceledException
                    || !cancellationToken.IsCancellationRequested))
            {
                if (successfulMessageCount == 0)
                {
                    throw;
                }

                throw new TelegramSendMessagesException(
                    ex.Message,
                    successfulMessageCount,
                    ex);
            }
        }

        return successfulMessageCount;
    }

    private async Task SendMessageAsync(
        TelegramCredentials credentials,
        string htmlMessage,
        CancellationToken cancellationToken)
    {
        AppLog.SendingTelegramAttempt(logger, 1, credentials.Source, htmlMessage.Length);
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

        using HttpResponseMessage response = await httpClient.SendAsync(
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
            AppLog.SentTelegramAttempt(logger, 1);
            return;
        }

        string description = apiResponse?.Description
            ?? response.ReasonPhrase
            ?? $"Telegram delivery failed with HTTP {(int)response.StatusCode}.";
        AppLog.TelegramDeliveryFailed(
            logger,
            1,
            (int)response.StatusCode,
            apiResponse?.ErrorCode,
            description);

        throw new InvalidOperationException(description);
    }
}

internal sealed class TelegramSendMessagesException(
    string message,
    int successfulMessageCount,
    Exception innerException) : InvalidOperationException(message, innerException)
{
    public int SuccessfulMessageCount { get; } = successfulMessageCount;
}
