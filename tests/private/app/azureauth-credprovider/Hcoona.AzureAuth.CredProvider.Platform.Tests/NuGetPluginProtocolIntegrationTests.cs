using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using NuGet.Protocol.Plugins;
using NuGet.Versioning;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class NuGetPluginProtocolIntegrationTests
{
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(5);

    private const string HandshakeRequestId = "11111111-1111-1111-1111-111111111111";
    private const string CloseRequestId = "22222222-2222-2222-2222-222222222222";
    private const string OperationClaimsRequestId = "33333333-3333-3333-3333-333333333333";
    private const string OperationClaimsRequestJson =
        "{\"RequestId\":\""
        + OperationClaimsRequestId
        + "\",\"Type\":\"Request\",\"Method\":\"GetOperationClaims\","
        + "\"Payload\":{\"PackageSourceRepository\":null,\"ServiceIndex\":{}}}";

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The underscores separate the protocol condition from the expected result."
    )]
    public async Task RunPluginAsync_WhenHandshakeAndCloseArePipelined_ExitsSuccessfully()
    {
        var minimumProtocolVersion = new SemanticVersion(1, 0, 0);
        var currentProtocolVersion = new SemanticVersion(2, 0, 0);
        Message handshakeRequest = MessageUtilities.Create(
            HandshakeRequestId,
            MessageType.Request,
            MessageMethod.Handshake,
            new HandshakeRequest(currentProtocolVersion, minimumProtocolVersion)
        );
        Message closeRequest = MessageUtilities.Create(
            CloseRequestId,
            MessageType.Request,
            MessageMethod.Close
        );
        using Process process = StartPluginProcess();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        bool completedSuccessfully = false;
        try
        {
            Message outboundHandshakeResponse = await CreateOutboundHandshakeResponseAsync(
                process,
                cancellationToken
            );
            string pipelinedInput = await SerializeMessagesAsync(
                outboundHandshakeResponse,
                handshakeRequest,
                closeRequest
            );

            await process.StandardInput.WriteAsync(pipelinedInput);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            string remainingOutput = await WaitForSuccessfulExitAsync(
                process,
                "NuGet plugin did not exit after the pipelined Close request.",
                cancellationToken
            );

            string[] responseLines = ProcessTestApp
                .NormalizeNewlines(remainingOutput)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            await AssertHandshakeResponseAsync(
                Assert.Single(responseLines),
                currentProtocolVersion
            );
            completedSuccessfully = true;
        }
        finally
        {
            await CompleteProcessCleanupAsync(process, completedSuccessfully);
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The underscores separate the protocol condition from the expected result."
    )]
    public async Task RunPluginAsync_WhenOperationClaimsUsesNuGet79WirePayload_ReturnsEmptyClaims()
    {
        var minimumProtocolVersion = new SemanticVersion(1, 0, 0);
        var currentProtocolVersion = new SemanticVersion(2, 0, 0);
        Message handshakeRequest = MessageUtilities.Create(
            HandshakeRequestId,
            MessageType.Request,
            MessageMethod.Handshake,
            new HandshakeRequest(currentProtocolVersion, minimumProtocolVersion)
        );
        Message closeRequest = MessageUtilities.Create(
            CloseRequestId,
            MessageType.Request,
            MessageMethod.Close
        );
        using Process process = StartPluginProcess();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        bool completedSuccessfully = false;
        try
        {
            Message outboundHandshakeResponse = await CreateOutboundHandshakeResponseAsync(
                process,
                cancellationToken
            );
            string handshakeInput = await SerializeMessagesAsync(
                outboundHandshakeResponse,
                handshakeRequest
            );
            await process.StandardInput.WriteAsync(handshakeInput);
            await process.StandardInput.FlushAsync(cancellationToken);

            string handshakeResponseLine = await ReadRequiredOutputLineAsync(
                process,
                "NuGet plugin closed standard output before responding to the handshake.",
                "NuGet plugin did not respond to the handshake within the timeout.",
                cancellationToken
            );
            await AssertHandshakeResponseAsync(handshakeResponseLine, currentProtocolVersion);

            await process.StandardInput.WriteAsync(OperationClaimsRequestJson + "\n");
            await process.StandardInput.FlushAsync(cancellationToken);
            string operationClaimsResponseLine = await ReadRequiredOutputLineAsync(
                process,
                "NuGet plugin closed standard output before responding to operation claims.",
                "NuGet plugin did not respond to operation claims within the timeout.",
                cancellationToken
            );

            Message operationClaimsResponse = await DeserializeMessageAsync(
                operationClaimsResponseLine
            );
            Assert.Equal(OperationClaimsRequestId, operationClaimsResponse.RequestId);
            Assert.Equal(MessageType.Response, operationClaimsResponse.Type);
            Assert.Equal(MessageMethod.GetOperationClaims, operationClaimsResponse.Method);
            GetOperationClaimsResponse operationClaimsPayload =
                MessageUtilities.DeserializePayload<GetOperationClaimsResponse>(
                    operationClaimsResponse
                )
                ?? throw new InvalidDataException(
                    "NuGet plugin operation-claims response did not contain a valid payload."
                );
            Assert.Empty(operationClaimsPayload.Claims);

            await process.StandardInput.WriteAsync(await SerializeMessagesAsync(closeRequest));
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            string remainingOutput = await WaitForSuccessfulExitAsync(
                process,
                "NuGet plugin did not exit after the operation-claims response.",
                cancellationToken
            );
            Assert.Equal(string.Empty, remainingOutput);
            completedSuccessfully = true;
        }
        finally
        {
            await CompleteProcessCleanupAsync(process, completedSuccessfully);
        }
    }

    private static Process StartPluginProcess()
    {
        var helperNonce = ProcessTestApp.CreateHelperNonce();
        var startInfo = new ProcessStartInfo
        {
            FileName = ProcessTestApp.AppHostPath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (
            string argument in ProcessTestApp.CreateHelperArguments(
                helperNonce,
                NuGetPluginProtocolTestProcess.Command
            )
        )
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (
            KeyValuePair<string, string?> variable in ProcessTestApp.CreateHelperEnvironment(
                helperNonce
            )
        )
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the NuGet protocol helper.");
    }

    private static async Task<string> ReadRequiredOutputLineAsync(
        Process process,
        string closedMessage,
        string timeoutMessage,
        CancellationToken cancellationToken
    )
    {
        using var responseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var responseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            responseTimeout.Token
        );
        try
        {
            return await process.StandardOutput.ReadLineAsync(responseCancellation.Token)
                ?? throw new InvalidOperationException(closedMessage);
        }
        catch (OperationCanceledException) when (responseTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }

    private static async Task CompleteProcessCleanupAsync(
        Process process,
        bool completedSuccessfully
    )
    {
        Exception? cleanupFailure = await TryTerminateProcessAsync(process);
        if (cleanupFailure is null)
        {
            return;
        }

        const string message = "NuGet protocol helper cleanup failed.";
        if (completedSuccessfully)
        {
            throw new InvalidOperationException(message, cleanupFailure);
        }

        TestContext.Current.AddWarning(
            message + Environment.NewLine + cleanupFailure
        );
    }

    private static async Task<Exception?> TryTerminateProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return null;
        }

        Exception? killFailure = null;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return null;
        }
        catch (Exception exception)
            when (exception
                is InvalidOperationException
                    or NotSupportedException
                    or Win32Exception)
        {
            killFailure = exception;
        }

        using var cleanupCancellation = new CancellationTokenSource(
            ProcessCleanupTimeout
        );
        try
        {
            await process.WaitForExitAsync(cleanupCancellation.Token);
            return null;
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return null;
        }
        catch (OperationCanceledException)
            when (cleanupCancellation.IsCancellationRequested)
        {
            return new TimeoutException(
                "NuGet protocol helper did not exit within "
                    + $"{ProcessCleanupTimeout} after termination.",
                killFailure
            );
        }
        catch (Exception exception)
            when (exception
                is InvalidOperationException
                    or NotSupportedException
                    or Win32Exception)
        {
            return killFailure is null
                ? exception
                : new AggregateException(
                    "NuGet protocol helper kill and exit wait both failed.",
                    killFailure,
                    exception
                );
        }
    }

    private static async Task<string> ReadProcessStreamAsync(
        StreamReader reader,
        string streamName
    )
    {
        using var streamCancellation = new CancellationTokenSource(
            ProcessCleanupTimeout
        );
        try
        {
            return await reader.ReadToEndAsync(streamCancellation.Token);
        }
        catch (OperationCanceledException)
            when (streamCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"NuGet protocol helper {streamName} did not close within {ProcessCleanupTimeout}."
            );
        }
    }

    private static async Task<string> ReadProcessStreamForDiagnosticsAsync(
        StreamReader reader,
        string streamName
    )
    {
        try
        {
            return await ReadProcessStreamAsync(reader, streamName);
        }
        catch (TimeoutException exception)
        {
            return $"<{exception.Message}>";
        }
    }

    private static async Task<string> WaitForSuccessfulExitAsync(
        Process process,
        string timeoutMessage,
        CancellationToken cancellationToken
    )
    {
        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var exitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            exitTimeout.Token
        );
        try
        {
            await process.WaitForExitAsync(exitCancellation.Token);
        }
        catch (OperationCanceledException) when (exitTimeout.IsCancellationRequested)
        {
            Exception? cleanupFailure = await TryTerminateProcessAsync(process);
            string remainingOutput = await ReadProcessStreamForDiagnosticsAsync(
                process.StandardOutput,
                "standard output"
            );
            string standardError = await ReadProcessStreamForDiagnosticsAsync(
                process.StandardError,
                "standard error"
            );
            Assert.Fail(
                timeoutMessage
                    + Environment.NewLine
                    + "stdout: "
                    + remainingOutput
                    + Environment.NewLine
                    + "stderr: "
                    + standardError
                    + (cleanupFailure is null
                        ? string.Empty
                        : Environment.NewLine
                            + "cleanup: "
                            + cleanupFailure)
            );
        }

        string output = await ReadProcessStreamAsync(
            process.StandardOutput,
            "standard output"
        );
        string error = await ReadProcessStreamAsync(
            process.StandardError,
            "standard error"
        );
        Assert.Equal(string.Empty, error);
        Assert.Equal((int)AdapterHostExitCode.Success, process.ExitCode);
        return output;
    }

    private static async Task<Message> CreateOutboundHandshakeResponseAsync(
        Process process,
        CancellationToken cancellationToken
    )
    {
        string outboundHandshakeLine = await ReadRequiredOutputLineAsync(
            process,
            "NuGet plugin closed standard output before handshaking.",
            "NuGet plugin did not initiate the handshake within the timeout.",
            cancellationToken
        );
        Message outboundHandshake = await DeserializeMessageAsync(outboundHandshakeLine);
        Assert.Equal(MessageType.Request, outboundHandshake.Type);
        Assert.Equal(MessageMethod.Handshake, outboundHandshake.Method);
        HandshakeRequest outboundPayload =
            MessageUtilities.DeserializePayload<HandshakeRequest>(outboundHandshake)
            ?? throw new InvalidDataException(
                "NuGet plugin handshake request did not contain a valid payload."
            );
        return MessageUtilities.Create(
            outboundHandshake.RequestId,
            MessageType.Response,
            MessageMethod.Handshake,
            new HandshakeResponse(MessageResponseCode.Success, outboundPayload.ProtocolVersion)
        );
    }

    private static async Task AssertHandshakeResponseAsync(
        string responseLine,
        SemanticVersion currentProtocolVersion
    )
    {
        Message handshakeResponse = await DeserializeMessageAsync(responseLine);
        Assert.Equal(HandshakeRequestId, handshakeResponse.RequestId);
        Assert.Equal(MessageType.Response, handshakeResponse.Type);
        Assert.Equal(MessageMethod.Handshake, handshakeResponse.Method);
        HandshakeResponse handshakePayload =
            MessageUtilities.DeserializePayload<HandshakeResponse>(handshakeResponse)
            ?? throw new InvalidDataException(
                "NuGet plugin handshake response did not contain a valid payload."
            );
        Assert.Equal(MessageResponseCode.Success, handshakePayload.ResponseCode);
        Assert.Equal(currentProtocolVersion, handshakePayload.ProtocolVersion);
    }

    private static async Task<string> SerializeMessagesAsync(params Message[] messages)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var sender = new Sender(writer);
        sender.Connect();
        foreach (Message message in messages)
        {
            await sender.SendAsync(message, TestContext.Current.CancellationToken);
        }

        sender.Close();
        return writer.ToString();
    }

    private static async Task<Message> DeserializeMessageAsync(string line)
    {
        using var reader = new StringReader(line + "\n");
        using var receiver = new StandardInputReceiver(reader);
        var received = new TaskCompletionSource<Message>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        receiver.MessageReceived += (_, args) => received.TrySetResult(args.Message);
        receiver.Faulted += (_, args) => received.TrySetException(args.Exception);
        receiver.Connect();
        return await received.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
    }
}
