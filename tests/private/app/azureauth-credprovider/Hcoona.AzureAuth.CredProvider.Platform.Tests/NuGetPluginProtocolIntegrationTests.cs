using System.Diagnostics;
using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using NuGet.Protocol.Plugins;
using NuGet.Versioning;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class NuGetPluginProtocolIntegrationTests
{
    private const string HandshakeRequestId = "11111111-1111-1111-1111-111111111111";
    private const string CloseRequestId = "22222222-2222-2222-2222-222222222222";

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

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the NuGet protocol helper.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string outboundHandshakeLine =
            await process.StandardOutput.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "NuGet plugin closed standard output before handshaking."
            );
        Message outboundHandshake = await DeserializeMessageAsync(outboundHandshakeLine);
        Assert.Equal(MessageType.Request, outboundHandshake.Type);
        Assert.Equal(MessageMethod.Handshake, outboundHandshake.Method);
        HandshakeRequest outboundPayload =
            MessageUtilities.DeserializePayload<HandshakeRequest>(outboundHandshake)
            ?? throw new InvalidDataException(
                "NuGet plugin handshake request did not contain a valid payload."
            );
        Message outboundHandshakeResponse = MessageUtilities.Create(
            outboundHandshake.RequestId,
            MessageType.Response,
            MessageMethod.Handshake,
            new HandshakeResponse(MessageResponseCode.Success, outboundPayload.ProtocolVersion)
        );
        string pipelinedInput = await SerializeMessagesAsync(
            outboundHandshakeResponse,
            handshakeRequest,
            closeRequest
        );

        await process.StandardInput.WriteAsync(pipelinedInput);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        Task<string> remainingOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
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
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
            Assert.Fail(
                "NuGet plugin did not exit after the pipelined Close request."
                    + Environment.NewLine
                    + "stdout: "
                    + await remainingOutput
                    + Environment.NewLine
                    + "stderr: "
                    + await standardError
            );
        }

        Assert.Equal(string.Empty, await standardError);
        Assert.Equal((int)AdapterHostExitCode.Success, process.ExitCode);

        string[] responseLines = ProcessTestApp
            .NormalizeNewlines(await remainingOutput)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Message handshakeResponse = await DeserializeMessageAsync(Assert.Single(responseLines));
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
