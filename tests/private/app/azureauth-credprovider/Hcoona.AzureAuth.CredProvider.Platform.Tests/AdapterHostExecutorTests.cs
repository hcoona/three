using System.Collections;
using System.Reflection;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AdapterHostExecutorTests
{
    private const string DiagnosticsCorrelationId = "9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2";
    private const string GitProtocolPayload = "username=fake\npassword=fake\n";
    private const string SuppressedProtocolPayload =
        "username=should-not-leak\npassword=should-not-leak\n";

    [Fact]
    public void ExecuteProtocolSuccessWritesOnlyProtocolStdout()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        AdapterInvocationContext? handledContext = null;
        const string humanMessage = "success for humans only";
        const string diagnosticMessage = "diagnostic for stderr only";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: context =>
            {
                handledContext = context;
                return new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: GitProtocolPayload,
                    humanStdout: humanMessage,
                    diagnosticEvents:
                    [
                        new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            diagnosticMessage)
                    ]);
            },
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(handledContext);
        Assert.NotNull(outcome.Invocation);
        Assert.Same(handledContext, outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal("GitCredentialHelper", outcome.Invocation.Entrypoint.Name);
        Assert.Equal(["get"], outcome.Invocation.PayloadArguments);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        string protocolStdoutText = capture.ProtocolStdout.ToString();

        Assert.Equal(GitProtocolPayload, protocolStdoutText);
        Assert.DoesNotContain(humanMessage, protocolStdoutText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            diagnosticMessage,
            protocolStdoutText,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteProtocolSuccessWritesProtocolStdoutToStringOnlyWriter()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new StringOnlyTextWriter();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "username=🚀\npassword=🧪\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(protocolPayload, protocolStdout.Written);
        AssertWritesContainNoIsolatedSurrogates(protocolStdout.Writes);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteProtocolSuccessWritesProtocolStdoutToExactStreamWriter()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var protocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "username=🚀\npassword=🧪\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        protocolStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(protocolPayload, encoding.GetString(stream.ToArray()));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteProtocolSuccessFlushesBufferedSharedStreamWriterBeforeReturning()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var firstProtocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        using var secondProtocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        const string firstPayload = "username=alpha\npassword=one\n";
        const string secondPayload = "username=beta\npassword=two\n";

        AdapterHostExecutionOutcome firstOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: firstPayload),
            protocolStdout: firstProtocolStdout,
            humanStdout: new StringWriter(),
            diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty));
        AdapterHostExecutionOutcome secondOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: secondPayload),
            protocolStdout: secondProtocolStdout,
            humanStdout: new StringWriter(),
            diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty));

        Assert.Equal(AdapterHostExitCode.Success, firstOutcome.Result.ExitCode);
        Assert.True(firstOutcome.Result.WriteProtocolStdout);
        Assert.False(firstOutcome.Result.WriteDiagnosticStderr);
        Assert.Equal(AdapterHostExitCode.Success, secondOutcome.Result.ExitCode);
        Assert.True(secondOutcome.Result.WriteProtocolStdout);
        Assert.False(secondOutcome.Result.WriteDiagnosticStderr);
        Assert.Equal(firstPayload + secondPayload, encoding.GetString(stream.ToArray()));

        secondProtocolStdout.Flush();
        firstProtocolStdout.Flush();

        Assert.Equal(firstPayload + secondPayload, encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void ExecuteProtocolSuccessFlushesBufferedStandardConsoleStdoutBeforeReturning()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        using var downstreamStream = new MemoryStream();
        using var bufferedStream = new BufferedStream(downstreamStream, bufferSize: 1024);
        TextWriter protocolStdout = new StandardConsoleTextWriter(
            bufferedStream,
            encoding,
            Environment.NewLine);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "username=alpha\npassword=one\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(protocolPayload, encoding.GetString(downstreamStream.ToArray()));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);

        protocolStdout.Flush();

        Assert.Equal(protocolPayload, encoding.GetString(downstreamStream.ToArray()));
    }

    [Fact]
    public void
        ExecuteProtocolPreambleEncodingSharedExactStreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        Assert.NotEmpty(encoding.GetPreamble());
        using var stream = new MemoryStream();
        using var firstProtocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        using var secondProtocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string firstPayload = "username=alpha\npassword=one\n";
        const string secondPayload = "username=beta\npassword=two\n";

        AdapterHostExecutionOutcome firstOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: firstPayload),
            protocolStdout: firstProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);
        AdapterHostExecutionOutcome secondOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: secondPayload),
            protocolStdout: secondProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.Equal(AdapterHostExitCode.Fatal, firstOutcome.Result.ExitCode);
        Assert.False(firstOutcome.Result.WriteProtocolStdout);
        Assert.True(firstOutcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", firstOutcome.Result.SafeDiagnosticCode);
        Assert.Equal(AdapterHostExitCode.Fatal, secondOutcome.Result.ExitCode);
        Assert.False(secondOutcome.Result.WriteProtocolStdout);
        Assert.True(secondOutcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", secondOutcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, stream.Length);
        Assert.Empty(stream.ToArray());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(2, diagnosticText.Split('\n').Length - 1);
        Assert.DoesNotContain(firstPayload, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(secondPayload, diagnosticText, StringComparison.Ordinal);

        Assert.Equal(2, capture.RecordingSink.Events.Count);
        Assert.All(capture.RecordingSink.Events, diagnosticEvent =>
        {
            Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
            Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
            Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
        });
    }

    [Fact]
    public void ExecuteProtocolUnencodableUnicodeExactStreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = Encoding.Latin1;
        using var stream = new MemoryStream();
        using var protocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "username=🚀\npassword=🧪\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        protocolStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolCustomEncodingExactStreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var stream = new MemoryStream();
        using var protocolStdout = new StreamWriter(
            stream,
            new CloneBypassingReplacementEncoding(
                Encoding.Latin1,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "username=\u0100\npassword=safe\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        protocolStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.Latin1.GetString(stream.ToArray()));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolLeadingInvalidUtf16StreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var stream = new MemoryStream();
        using var protocolStdout = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "\uD83Dusername=fake\npassword=fake\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        protocolStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolLeadingInvalidUtf16DefaultStreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var protocolStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "\uD83Dusername=fake\npassword=fake\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        protocolStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolLeadingInvalidUtf16StringWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "\uD83Dusername=fake\npassword=fake\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolTrailingInvalidUtf16StandardConsoleStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var stream = new MemoryStream();
        TextWriter protocolStdout = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = GitProtocolPayload + "\uD83D";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        protocolStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.UTF8.GetString(stream.ToArray()));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolNonBmpStdoutRejectsCharOnlyWriterBeforeCommit()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new CharOnlyTextWriter();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "username=🚀\npassword=🧪\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, protocolStdout.Written);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteHumanCommandSuccessAllowsHumanStdoutWithoutProtocolLeak()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: "doctor ok",
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterProtocol.Unspecified, outcome.Invocation.Protocol);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal("doctor ok", capture.HumanStdout.ToString());
        Assert.DoesNotContain(
            "should-not-leak",
            capture.HumanStdout.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteHumanCommandSuccessWritesUnicodeScalarsToStringOnlyHumanStdout()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var humanStdout = new StringOnlyTextWriter();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "doctor 🚀 ok 🧪";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(humanMessage, humanStdout.Written);
        AssertWritesContainNoIsolatedSurrogates(humanStdout.Writes);
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteHumanCommandSuccessWritesHumanStdoutToExactStreamWriter()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var humanStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "doctor 🚀 ok 🧪";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        humanStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(humanMessage, encoding.GetString(stream.ToArray()));
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void
        ExecuteHumanCommandSuccessFlushesAutoFlushStatefulExactStreamWriterBeforeReturning()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding? maybeEncoding = TryCreateUtf7Encoding();
        Assert.SkipWhen(
            maybeEncoding is null,
            "UTF-7 encoding is unavailable or disabled on this target framework.");
        Encoding encoding = maybeEncoding!;
        Assert.Empty(encoding.GetPreamble());
        using var stream = new MemoryStream();
        using var humanStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "tail \u0100";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        byte[] expectedBytes = encoding.GetBytes(humanMessage);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(expectedBytes, stream.ToArray());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);

        humanStdout.Flush();

        Assert.Equal(expectedBytes, stream.ToArray());
    }

    [Fact]
    public void ExecuteHumanCommandLeadingInvalidUtf16StreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var stream = new MemoryStream();
        using var humanStdout = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "\uD83Ddoctor ok";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        humanStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(0, stream.Length);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void
        ExecuteHumanCommandUnencodableUnicodeExactStreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = Encoding.Latin1;
        using var stream = new MemoryStream();
        using var humanStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "doctor 🚀 ok 🧪";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        humanStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void
        ExecuteHumanCommandLeadingInvalidUtf16DefaultStreamWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var humanStdout = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "\uD83Ddoctor ok";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        humanStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteHumanCommandLeadingInvalidUtf16StringWriterStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "\uD83Ddoctor ok";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void
        ExecuteHumanCommandTrailingInvalidUtf16StandardConsoleStdoutBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var stream = new MemoryStream();
        TextWriter humanStdout = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "doctor ok\uD83D";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        humanStdout.Flush();

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.UTF8.GetString(stream.ToArray()));

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EncoderFallbackException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteHumanCommandNonBmpStdoutRejectsCharOnlyWriterBeforeCommit()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var humanStdout = new CharOnlyTextWriter();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "doctor 🚀 ok 🧪";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: humanMessage,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, humanStdout.Written);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolSuccessPreservesUnpairedSurrogatesInStringOnlyWriter()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new StringOnlyTextWriter();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "A🚀B\uD83DC\uDE80D";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(protocolPayload, protocolStdout.Written);
        Assert.Equal(["A", "🚀", "B", "\uD83D", "C", "\uDE80", "D"], protocolStdout.Writes);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteInvocationBoundaryMismatchBecomesSafeConfigurationFailure()
    {
        var descriptor = new AdapterDescriptor(
            "Shared Host",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "ProtocolAny",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["tool"]),
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        var handlerCalled = false;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: null,
            arguments: ["doctor"],
            handler: _ =>
            {
                handlerCalled = true;
                return new AdapterHostHandlerOutput(
                    humanStdout: "should-not-run",
                    protocolStdout: SuppressedProtocolPayload);
            },
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.False(handlerCalled);
        Assert.Null(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("InvocationBoundaryMismatch", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Adapter host invocation boundary is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains(
            "code=InvocationBoundaryMismatch",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "does not match the current invocation boundary",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Shared Host", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal(
            "Adapter host invocation boundary is unsupported.",
            diagnosticEvent.Message);
        Assert.Equal(
            "InvocationBoundaryMismatch",
            diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
    }

    [Fact]
    public void ExecuteInvalidExecutablePathBecomesSafeConfigurationFailure()
    {
        AdapterDescriptor descriptor = new(
            "Human Only",
            AdapterProtocol.Unspecified,
            [
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string invalidExecutablePath = "..";
        var handlerCalled = false;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: invalidExecutablePath,
            arguments: ["doctor"],
            handler: _ =>
            {
                handlerCalled = true;
                return new AdapterHostHandlerOutput(
                    humanStdout: "should-not-run",
                    protocolStdout: SuppressedProtocolPayload);
            },
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.False(handlerCalled);
        Assert.Null(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("InvocationBoundaryMismatch", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Adapter host invocation boundary is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains(
            "code=InvocationBoundaryMismatch",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "does not match the current invocation boundary",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            invalidExecutablePath,
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Human Only", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal(
            "Adapter host invocation boundary is unsupported.",
            diagnosticEvent.Message);
        Assert.Equal(
            "InvocationBoundaryMismatch",
            diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
    }

    [Theory]
    [InlineData(DiagnosticChannel.Diagnostic)]
    [InlineData(DiagnosticChannel.HumanStdout)]
    public void ExecuteHumanCommandTreatsHandlerSuppliedSafeEnvelopesAsUntrusted(
        DiagnosticChannel sourceChannel)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: new SecretRedactor(["ED"]));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: _ => new AdapterHostHandlerOutput(
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        sourceChannel,
                        "coEDde=Spoofed",
                        properties: new Dictionary<string, string?>
                        {
                            ["detail"] = "coEDde=Spoofed",
                            ["detail-coEDde"] = "kept",
                            ["code"] = "coEDde=Visible",
                        },
                        isSafeDiagnosticEnvelope: true),
                ]),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("code=Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("detail=code=Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("detail-code=kept", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=code=Visible", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code:Spoofed", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.False(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal(DiagnosticChannel.Diagnostic, diagnosticEvent.Channel);
        Assert.Equal("code=Spoofed", diagnosticEvent.Message);
        Assert.Equal("code=Spoofed", diagnosticEvent.Properties["detail"]);
        Assert.Equal("kept", diagnosticEvent.Properties["detail-code"]);
        Assert.Equal("code=Visible", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolNoCredentialSuppressesProtocolStdoutAndStderr()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string humanMessage = "no credential human detail";
        const string diagnosticMessage = "No matching credential is available.";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateNoCredentialResult(),
                protocolStdout: SuppressedProtocolPayload,
                humanStdout: humanMessage,
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        diagnosticMessage)
                ]),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.NoCredential, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedHost", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        string diagnosticText = capture.DiagnosticText.ToString();

        Assert.Equal(string.Empty, diagnosticText);
        Assert.DoesNotContain(
            "should-not-leak",
            capture.ProtocolStdout.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(humanMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            diagnosticMessage,
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    // editorconfig-checker-disable
    public async Task ExecuteProtocolNoCredentialSuppressesLateFlowedCredentialCoreSafeDiagnosticFromDisposedProtocolScope()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);
        using var releaseLateCredentialCoreExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? lateCredentialCoreExecutionTask = null;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                lateCredentialCoreExecutionTask = Task.Run(
                    () =>
                    {
                        if (!releaseLateCredentialCoreExecution.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the late credential-core "
                                    + "execution.");
                        }

                        return service.Execute(
                            CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));
                    },
                    TestContext.Current.CancellationToken);
                return new AdapterHostHandlerOutput(
                    credentialResult: CreateNoCredentialResult(),
                    protocolStdout: SuppressedProtocolPayload);
            },
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        releaseLateCredentialCoreExecution.Set();

        CredentialResult lateCredentialCoreResult = await Assert
            .IsType<Task<CredentialResult>>(lateCredentialCoreExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.NoCredential, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedHost", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteProtocolFailureSuppressesProtocolStdoutAndWritesSafeDiagnostic()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateUnauthorizedCredentialResult(),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("Unauthorized", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Contains(
            "Authorization is required.",
            capture.DiagnosticText.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "code=Unauthorized",
            capture.DiagnosticText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "should-not-leak",
            capture.DiagnosticText.ToString(),
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Authorization is required.", diagnosticEvent.Message);
        Assert.Equal("Unauthorized", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolFatalSuppressesProtocolStdoutAndWritesSanitizedSafeDiagnostic()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string longSafeValue =
            "value with spaces\r\nand control\u001b[31m\u202E "
            + new string('x', 400);
        IReadOnlyDictionary<string, string> safeDetails = new Dictionary<string, string>
        {
            ["code"] = "Spoofed",
            ["detail key=\r\n"] = longSafeValue,
        };

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    "Credential acquisition failed safely.\r\nSecond line "
                    + "\u001b[31mred\u001b[0m\u202E",
                    safeDetails),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely. Second line",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-leak", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);
        Assert.DoesNotContain(new string('x', 300), diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Contains(
            "Credential acquisition failed safely. Second line",
            diagnosticEvent.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", diagnosticEvent.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", diagnosticEvent.Message, StringComparison.Ordinal);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.DoesNotContain(
            "Spoofed",
            diagnosticEvent.Properties["code"],
            StringComparison.Ordinal);
        Assert.True(diagnosticEvent.Properties.ContainsKey("detail_key"));

        string detailValue = Assert.IsType<string>(diagnosticEvent.Properties["detail_key"]);
        Assert.DoesNotContain("\n", detailValue, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", detailValue, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", detailValue, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", detailValue, StringComparison.Ordinal);
        Assert.True(detailValue.Length <= 256);
    }

    [Fact]
    public void ExecuteProtocolFatalEscapesProducerCodeTokensInSafeMessageAndDetailValues()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    "Credential acquisition failed safely. code=Spoofed",
                    new Dictionary<string, string>
                    {
                        ["detail"] = "code=Spoofed",
                    }),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely. code:Spoofed",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("detail=code:Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=Spoofed", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(
            "Credential acquisition failed safely. code:Spoofed",
            diagnosticEvent.Message);
        Assert.Equal("code:Spoofed", diagnosticEvent.Properties["detail"]);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolFatalEscapesRedactionSynthesizedProducerCodeTokens()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: new SecretRedactor(["ED"]));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    "Credential acquisition failed safely. coEDde=Spoofed",
                    new Dictionary<string, string>
                    {
                        ["detail"] = "coEDde=Spoofed",
                        ["detail-coEDde"] = "should-not-surface",
                    }),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely. code:Spoofed",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("detail=code:Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("detail-code", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(
            "Credential acquisition failed safely. code:Spoofed",
            diagnosticEvent.Message);
        Assert.Equal("code:Spoofed", diagnosticEvent.Properties["detail"]);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("detail-code"));
    }

    [Fact]
    public void ExecuteProtocolFailureDropsRedactionSynthesizedSpoofedCanonicalCodeKey()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: new SecretRedactor(["ED"]));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.ProtocolViolation,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.ProtocolViolation,
                        Code = " \r\n\t ",
                        SafeMessage = "Protocol output failed safely.",
                        SafeDetails = new Dictionary<string, string>
                        {
                            ["coEDde"] = "UnhandledHostFailure",
                        },
                    },
                },
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(" \r\n\t ", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Protocol output failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=UnhandledHostFailure",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Protocol output failed safely.", diagnosticEvent.Message);
        Assert.Empty(diagnosticEvent.Properties);
    }

    [Fact]
    public void ExecuteProtocolFatalRejectsSafeDetailKeysEndingWithCode()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    "Credential acquisition failed safely.",
                    new Dictionary<string, string>
                    {
                        ["xcode"] = "Spoofed",
                        ["detail CODE"] = "Spoofed",
                        ["detail"] = "available",
                    }),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("detail=available", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("xcode=Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("detail_CODE=Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=Spoofed", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential acquisition failed safely.", diagnosticEvent.Message);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.Equal("available", diagnosticEvent.Properties["detail"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("xcode"));
        Assert.False(diagnosticEvent.Properties.ContainsKey("detail_CODE"));
    }

    [Fact]
    public void ExecuteProtocolFatalSanitizesProducerControlledDiagnosticCodeToken()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage: "Credential acquisition failed safely.",
                    code: "code=Spoofed"),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("code=Spoofed", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("code=code=Spoofed", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        string diagnosticCode = Assert.IsType<string>(diagnosticEvent.Properties["code"]);
        Assert.NotEmpty(diagnosticCode);
        Assert.Contains($"code={diagnosticCode}", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=", diagnosticCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("=", diagnosticCode, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", diagnosticCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", diagnosticCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", diagnosticCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteProtocolFatalPreservesSafeDiagnosticsAtExactMaxLength()
    {
        const int maxSafeDiagnosticCodeLength = 64;
        const int maxSafeDiagnosticTextLength = 256;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string safeCode = new('c', maxSafeDiagnosticCodeLength);
        string safeMessage = new('m', maxSafeDiagnosticTextLength);
        string safeDetail = new('d', maxSafeDiagnosticTextLength);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage,
                    new Dictionary<string, string>
                    {
                        ["detail"] = safeDetail,
                    },
                    safeCode),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        string diagnosticCode = Assert.IsType<string>(diagnosticEvent.Properties["code"]);
        string detailValue = Assert.IsType<string>(diagnosticEvent.Properties["detail"]);

        Assert.Equal(safeMessage, diagnosticEvent.Message);
        Assert.Equal(safeCode, diagnosticCode);
        Assert.Equal(safeDetail, detailValue);
        Assert.Equal(maxSafeDiagnosticTextLength, diagnosticEvent.Message.Length);
        Assert.Equal(maxSafeDiagnosticCodeLength, diagnosticCode.Length);
        Assert.Equal(maxSafeDiagnosticTextLength, detailValue.Length);
        Assert.False(diagnosticEvent.Message.EndsWith("...", StringComparison.Ordinal));
        Assert.False(diagnosticCode.EndsWith("...", StringComparison.Ordinal));
        Assert.False(detailValue.EndsWith("...", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteProtocolFatalTruncatesSafeDiagnosticsOnlyWhenExceedingMaxLength()
    {
        const int maxSafeDiagnosticCodeLength = 64;
        const int maxSafeDiagnosticTextLength = 256;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string safeCode = new('c', maxSafeDiagnosticCodeLength + 1);
        string safeMessage = new('m', maxSafeDiagnosticTextLength + 1);
        string safeDetail = new('d', maxSafeDiagnosticTextLength + 1);
        string expectedCode = new string('c', maxSafeDiagnosticCodeLength - 3) + "...";
        string expectedMessage = new string('m', maxSafeDiagnosticTextLength - 3) + "...";
        string expectedDetail = new string('d', maxSafeDiagnosticTextLength - 3) + "...";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage,
                    new Dictionary<string, string>
                    {
                        ["detail"] = safeDetail,
                    },
                    safeCode),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        string diagnosticCode = Assert.IsType<string>(diagnosticEvent.Properties["code"]);
        string detailValue = Assert.IsType<string>(diagnosticEvent.Properties["detail"]);

        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.Equal(expectedCode, diagnosticCode);
        Assert.Equal(expectedDetail, detailValue);
        Assert.Equal(maxSafeDiagnosticTextLength, diagnosticEvent.Message.Length);
        Assert.Equal(maxSafeDiagnosticCodeLength, diagnosticCode.Length);
        Assert.Equal(maxSafeDiagnosticTextLength, detailValue.Length);
    }

    [Fact]
    public void ExecuteProtocolFatalCapsVeryLargeSafeDiagnosticsWithoutRemappingEnvelope()
    {
        const int maxSafeDiagnosticCodeLength = 64;
        const int maxSafeDiagnosticTextLength = 256;
        const int veryLargeLength = 100_000;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string safeCode = new('c', veryLargeLength);
        string safeMessage = new('m', veryLargeLength);
        string safeDetail = new('d', veryLargeLength);
        string expectedCode = new string('c', maxSafeDiagnosticCodeLength - 3) + "...";
        string expectedMessage = new string('m', maxSafeDiagnosticTextLength - 3) + "...";
        string expectedDetail = new string('d', maxSafeDiagnosticTextLength - 3) + "...";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage,
                    new Dictionary<string, string>
                    {
                        ["detail"] = safeDetail,
                    },
                    safeCode),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal(safeCode, outcome.Result.SafeDiagnosticCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        string diagnosticCode = Assert.IsType<string>(diagnosticEvent.Properties["code"]);
        string detailValue = Assert.IsType<string>(diagnosticEvent.Properties["detail"]);

        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.Equal(expectedCode, diagnosticCode);
        Assert.Equal(expectedDetail, detailValue);
        Assert.Equal(maxSafeDiagnosticTextLength, diagnosticEvent.Message.Length);
        Assert.Equal(maxSafeDiagnosticCodeLength, diagnosticCode.Length);
        Assert.Equal(maxSafeDiagnosticTextLength, detailValue.Length);
    }

    [Fact]
    public void ExecuteProtocolFatalBoundsVeryLargeWhitespacePrefixedSafeDiagnostics()
    {
        const int veryLargePrefixLength = 100_000;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string safePrefix = new string(' ', veryLargePrefixLength) + "\r\n\t\u001b\u202E";
        string safeMessage = safePrefix + "late-message-suffix";
        string safeDetail = safePrefix + "late-detail-suffix";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage,
                    new Dictionary<string, string>
                    {
                        ["detail"] = safeDetail,
                    }),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "late-message-suffix",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "late-detail-suffix",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UnhandledHostFailure",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.True(diagnosticEvent.Properties.ContainsKey("detail"));
        string detailValue = Assert.IsType<string>(diagnosticEvent.Properties["detail"]);
        Assert.DoesNotContain("late-detail-suffix", detailValue, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteProtocolUnauthorizedBoundsLateWhitespacePrefixedSafeCode()
    {
        const int veryLargePrefixLength = 100_000;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string safePrefix = new string(' ', veryLargePrefixLength) + "\r\n\t\u001b\u202E";
        const string lateSafeCode = "late-unauthorized-suffix";
        string safeCode = safePrefix + lateSafeCode;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.Unauthorized,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.Unauthorized,
                        Code = safeCode,
                        SafeMessage = "Authorization is required.",
                        SafeDetails = new Dictionary<string, string>
                        {
                            ["detail"] = "producer-detail",
                        },
                    },
                },
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.Equal(safeCode, outcome.Result.SafeDiagnosticCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Authorization is required.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("detail=producer-detail", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=", diagnosticText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lateSafeCode, diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Authorization is required.", diagnosticEvent.Message);
        Assert.Equal("producer-detail", diagnosticEvent.Properties["detail"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("code"));
        Assert.Single(diagnosticEvent.Properties);
    }

    [Fact]
    public void ExecuteProtocolUnauthorizedBoundsLateWhitespacePrefixedSafeDetailKey()
    {
        const int veryLargePrefixLength = 100_000;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        string safePrefix = new string(' ', veryLargePrefixLength) + "\r\n\t\u001b\u202E";
        string lateSafeDetailKey = safePrefix + "detail";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.Unauthorized,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.Unauthorized,
                        Code = "Unauthorized",
                        SafeMessage = "Authorization is required.",
                        SafeDetails = new Dictionary<string, string>
                        {
                            [lateSafeDetailKey] = "late-detail-value",
                        },
                    },
                },
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.Equal("Unauthorized", outcome.Result.SafeDiagnosticCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Authorization is required.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=Unauthorized", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("detail=late-detail-value", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("late-detail-value", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Authorization is required.", diagnosticEvent.Message);
        Assert.Equal("Unauthorized", diagnosticEvent.Properties["code"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("detail"));
        Assert.Single(diagnosticEvent.Properties);
    }

    [Fact]
    public void ExecuteProtocolFatalSkipsNullSafeDetailValuesWithoutRemappingEnvelope()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage: "Credential acquisition failed safely.",
                    safeDetails: new UnsafeSafeDetailsDictionary(
                        new Dictionary<string, string?>
                        {
                            ["detail"] = "available",
                            ["null-detail"] = null,
                        })),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("detail=available", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("null-detail", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.Equal("available", diagnosticEvent.Properties["detail"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("null-detail"));
    }

    [Fact]
    public void ExecuteProtocolFatalKeepsMappedFailureWhenSafeDetailEnumerationThrows()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage: "Credential acquisition failed safely.",
                    safeDetails: new ThrowingSafeDetailsDictionary(
                        [
                            new KeyValuePair<string, string>("detail", "available"),
                            new KeyValuePair<string, string>(
                                "dropped",
                                "should-not-appear"),
                        ],
                        throwAfterYieldCount: 1)),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("detail=available", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=UnhandledHostFailure",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential acquisition failed safely.", diagnosticEvent.Message);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.Equal("available", diagnosticEvent.Properties["detail"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("dropped"));
    }

    [Fact]
    public void ExecuteProtocolFatalStopsSafeDetailInspectionAfterManySkippedEntries()
    {
        const int skippedEntryCount = 4096;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        List<KeyValuePair<string, string>> values = [];
        for (int index = 0; index < skippedEntryCount; index++)
        {
            values.Add(
                index % 2 == 0
                    ? new KeyValuePair<string, string>(string.Empty, "ignored")
                    : new KeyValuePair<string, string>("code", "Spoofed"));
        }

        values.Add(new KeyValuePair<string, string>("late", "should-not-surface"));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage: "Credential acquisition failed safely.",
                    safeDetails: new ThrowingSafeDetailsDictionary(
                        values,
                        throwAfterYieldCount: int.MaxValue)),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "late=should-not-surface",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("code=Spoofed", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential acquisition failed safely.", diagnosticEvent.Message);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.False(diagnosticEvent.Properties.ContainsKey("late"));
        Assert.Single(diagnosticEvent.Properties);
    }

    [Fact]
    public void ExecuteProtocolFatalStopsSafeDetailInspectionAfterManyCollidingEntries()
    {
        const int collidingEntryCount = 4096;
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        List<KeyValuePair<string, string>> values =
        [
            new KeyValuePair<string, string>("detail", "initial"),
        ];

        for (int index = 0; index < collidingEntryCount; index++)
        {
            values.Add(new KeyValuePair<string, string>("detail", $"collision-{index}"));
        }

        values.Add(new KeyValuePair<string, string>("late", "should-not-surface"));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage: "Credential acquisition failed safely.",
                    safeDetails: new ThrowingSafeDetailsDictionary(
                        values,
                        throwAfterYieldCount: int.MaxValue)),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal("Fatal", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential acquisition failed safely.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=Fatal", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "late=should-not-surface",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential acquisition failed safely.", diagnosticEvent.Message);
        Assert.Equal("Fatal", diagnosticEvent.Properties["code"]);
        Assert.True(diagnosticEvent.Properties.ContainsKey("detail"));
        Assert.False(diagnosticEvent.Properties.ContainsKey("late"));
    }

    [Fact]
    public void
        ExecuteProtocolProducerOwnedUnsupportedAdapterProtocolCodeKeepsProducerSafeEnvelope()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateFatalCredentialResult(
                    safeMessage: "Producer controlled unsupported-protocol failure.",
                    safeDetails: new Dictionary<string, string>
                    {
                        ["detail"] = "producer-detail",
                    },
                    code: "UnsupportedAdapterProtocol"),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Equal(
            "UnsupportedAdapterProtocol",
            outcome.Result.SafeDiagnosticCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Producer controlled unsupported-protocol failure.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains(
            "detail=producer-detail",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains(
            "code=UnsupportedAdapterProtocol",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host protocol is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(
            "Producer controlled unsupported-protocol failure.",
            diagnosticEvent.Message);
        Assert.Equal(
            "UnsupportedAdapterProtocol",
            diagnosticEvent.Properties["code"]);
        Assert.Equal("producer-detail", diagnosticEvent.Properties["detail"]);
    }

    [Fact]
    public void ExecuteProtocolProducerOwnedReservedValidationCodeUsesGenericFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.Unauthorized,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.Unauthorized,
                        Code = "UnsupportedContractMajor",
                        SafeMessage = "\r\n\u001b\u202E\t ",
                        SafeDetails = new Dictionary<string, string>
                        {
                            ["detail"] = "producer-detail",
                        },
                    },
                },
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.Equal("UnsupportedContractMajor", outcome.Result.SafeDiagnosticCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains(
            "code=UnsupportedContractMajor",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("detail=producer-detail", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential result contract major is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnsupportedContractMajor", diagnosticEvent.Properties["code"]);
        Assert.Equal("producer-detail", diagnosticEvent.Properties["detail"]);
    }

    [Fact]
    public void
        ExecuteProtocolProducerOwnedReservedValidationCodeStaysGenericAfterRedactionBlanksMessage()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: new SecretRedactor(["producer-secret", "ED"]));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.Unauthorized,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.Unauthorized,
                        Code = "UnsupportedContractMajor",
                        SafeMessage = "producer-secret",
                    },
                },
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.Equal("UnsupportedContractMajor", outcome.Result.SafeDiagnosticCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential result contract major is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnsupportedContractMajor", diagnosticEvent.Properties["code"]);
        Assert.False(diagnosticEvent.AllowCodeSpecificFallback);
    }

    [Fact]
    public void ExecuteProtocolFailureKeepsSafeDiagnosticHostOwned()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string handlerWarning =
            "handler warning\r\ncode=Spoofed\u001b[31m "
            + "must not replace the safe envelope";
        const string handlerError =
            "handler error\ncode=Spoofed\u202E must not add extra lines";
        const string humanStdout =
            "human detail\r\ncode=Spoofed\u001b[31m "
            + "must not appear on protocol stderr";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateUnauthorizedCredentialResult(),
                humanStdout: humanStdout,
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        handlerWarning,
                        properties: new Dictionary<string, string?>
                        {
                            ["code"] = "Spoofed",
                        }),
                    new DiagnosticEvent(
                        DiagnosticSeverity.Error,
                        DiagnosticChannel.Diagnostic,
                        handlerError),
                ]),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("Unauthorized", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Authorization is required.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=Unauthorized", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("Spoofed", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("handler warning", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("handler error", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("human detail", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Authorization is required.", diagnosticEvent.Message);
        Assert.Equal("Unauthorized", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void
        ExecuteProtocolCredentialCoreSuccessKeepsStderrEmptyAndStandaloneCoreStillEmitsDiagnostics()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);
        CredentialRequest request = CreateCredentialCoreGitRequest();
        string? expectedProtocolStdout = null;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                CredentialResult credentialResult = service.Execute(request);
                expectedProtocolStdout = CreateGitCredentialHelperProtocolPayload(credentialResult);
                return new AdapterHostHandlerOutput(
                    credentialResult: credentialResult,
                    protocolStdout: expectedProtocolStdout);
            },
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(
            Assert.IsType<string>(expectedProtocolStdout),
            capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);

        CredentialResult standaloneResult = service.Execute(request);

        Assert.Equal(CredentialResultStatus.Success, standaloneResult.Status);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Credential request succeeded.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=CredentialIssued", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential request succeeded.", diagnosticEvent.Message);
        Assert.Equal("CredentialIssued", diagnosticEvent.Properties["code"]);
    }

    [Theory]
    [InlineData(
        "ProtocolViolationSuccessResult",
        CredentialOperation.Get,
        "ProtocolViolation",
        "Adapter host protocol output was invalid.")]
    [InlineData(
        "UnsupportedContractMajor",
        CredentialOperation.Get,
        "UnsupportedContractMajor",
        "Credential result contract major is unsupported.")]
    [InlineData(
        "UnsupportedCacheKeySchemaMajor",
        CredentialOperation.Get,
        "UnsupportedCacheKeySchemaMajor",
        "Credential cache-key schema is unsupported.")]
    [InlineData(
        "ProtocolViolationUnsupportedGitOperation",
        CredentialOperation.Refresh,
        "ProtocolViolation",
        "Adapter host protocol output was invalid.")]
    public void ExecuteProtocolMapperOwnedValidationDiagnosticsUseCanonicalHostEnvelope(
        string scenario,
        CredentialOperation operation,
        string expectedCode,
        string expectedMessage)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        CredentialResult credentialResult = scenario switch
        {
            "ProtocolViolationSuccessResult" => new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "AzureDevOps",
                Password = "password",
                DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                Error = CreateProducerControlledCredentialError(
                    CredentialErrorKind.CredentialUnavailable),
            },
            "UnsupportedContractMajor" => new CredentialResult
            {
                ContractMajor = 0,
                Status = CredentialResultStatus.Fatal,
                DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                Error = CreateProducerControlledCredentialError(CredentialErrorKind.Fatal),
            },
            "UnsupportedCacheKeySchemaMajor" => new CredentialResult
            {
                Status = CredentialResultStatus.Fatal,
                DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                CacheKey = new CacheKey
                {
                    SchemaMajor = 0,
                    Value = "unsupported-cache-key",
                },
                Error = CreateProducerControlledCredentialError(CredentialErrorKind.Fatal),
            },
            "ProtocolViolationUnsupportedGitOperation" => new CredentialResult
            {
                Status = CredentialResultStatus.Unauthorized,
                DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                Error = CreateProducerControlledCredentialError(
                    CredentialErrorKind.Unauthorized),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                operation: operation),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(expectedCode, outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(expectedMessage, diagnosticText, StringComparison.Ordinal);
        Assert.Contains($"code={expectedCode}", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("producer message", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("producer detail", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("code=Spoofed", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.Equal(expectedCode, diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.NotNull(diagnosticEvent.CorrelationId);
    }

    [Fact]
    public void
        ExecuteProtocolCredentialCoreFlowDeferredEmitsOneHostOwnedStderrLine()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: service.Execute(
                    CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal)),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("FlowDeferred", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Requested identity flow is deferred by the MVP scaffold.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=FlowDeferred", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal(
            "Requested identity flow is deferred by the MVP scaffold.",
            diagnosticEvent.Message);
        Assert.Equal("FlowDeferred", diagnosticEvent.Properties["code"]);
    }

    [Theory]
    [InlineData("UnsupportedContractMajor")]
    [InlineData("UnsupportedCacheKeySchemaMajor")]
    public void
        ExecuteProtocolMapperOwnedValidationDiagnosticsRestoreCanonicalFallbackAfterRedaction(
        string scenario)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        (CredentialResult Result, string Message) validationScenario = scenario switch
        {
            "UnsupportedContractMajor" => (
                new CredentialResult
                {
                    ContractMajor = 0,
                    Status = CredentialResultStatus.Fatal,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    Error = CreateProducerControlledCredentialError(CredentialErrorKind.Fatal),
                },
                "Credential result contract major is unsupported."),
            "UnsupportedCacheKeySchemaMajor" => (
                new CredentialResult
                {
                    Status = CredentialResultStatus.Fatal,
                    DiagnosticsCorrelationId = DiagnosticsCorrelationId,
                    CacheKey = new CacheKey
                    {
                        SchemaMajor = 0,
                        Value = "unsupported-cache-key",
                    },
                    Error = CreateProducerControlledCredentialError(CredentialErrorKind.Fatal),
                },
                "Credential cache-key schema is unsupported."),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: new SecretRedactor([validationScenario.Message, "ED"]));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: validationScenario.Result),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(validationScenario.Message, diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(validationScenario.Message, diagnosticEvent.Message);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
    }

    [Theory]
    [InlineData("FlowDisabled", false)]
    [InlineData("FlowDisabled", true)]
    [InlineData("OperationNotSupported", false)]
    [InlineData("OperationNotSupported", true)]
    [InlineData("UnsupportedFlow", true)]
    public void
        ExecuteProtocolTrustedCredentialCoreFailuresRestoreCredentialCoreFallbackAfterRedaction(
        string scenario,
        bool redactCode)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        (CredentialResult Result, string Code, string Message) credentialCoreScenario = scenario
            switch
        {
            "FlowDisabled" => (
                CreateTrustedCredentialCoreFailureResult(
                    CredentialResultStatus.FlowDisabled,
                    CredentialErrorKind.FlowDisabled,
                    "FlowDisabled",
                    "Credential request is disabled by the current MVP policy."),
                "FlowDisabled",
                "Credential request is disabled by the current MVP policy."),
            "OperationNotSupported" => (
                CreateTrustedCredentialCoreFailureResult(
                    CredentialResultStatus.CredentialUnavailable,
                    CredentialErrorKind.CredentialUnavailable,
                    "OperationNotSupported",
                    "Credential core scaffold only supports get operations."),
                "OperationNotSupported",
                "Credential core scaffold only supports get operations."),
            "UnsupportedFlow" => (
                CreateTrustedCredentialCoreFailureResult(
                    CredentialResultStatus.UnsupportedFlow,
                    CredentialErrorKind.UnsupportedFlow,
                    "UnsupportedFlow",
                    "Requested identity flow is not supported by the current MVP policy."),
                "UnsupportedFlow",
                "Requested identity flow is not supported by the current MVP policy."),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: CreateBlankingRedactor(
                redactCode
                    ? new[] { credentialCoreScenario.Message, credentialCoreScenario.Code }
                    : new[] { credentialCoreScenario.Message }));
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialCoreScenario.Result,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(credentialCoreScenario.Code, outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(credentialCoreScenario.Message, diagnosticText, StringComparison.Ordinal);
        if (redactCode)
        {
            Assert.DoesNotContain(
                $"code={credentialCoreScenario.Code}",
                diagnosticText,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(
                $"code={credentialCoreScenario.Code}",
                diagnosticText,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential core diagnostic details are unavailable.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(credentialCoreScenario.Message, diagnosticEvent.Message);
        if (redactCode)
        {
            Assert.False(diagnosticEvent.Properties.ContainsKey("code"));
        }
        else
        {
            Assert.Equal(credentialCoreScenario.Code, diagnosticEvent.Properties["code"]);
        }

        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Theory]
    [InlineData(
        "FlowDisabled",
        "Requested identity flow is disabled by the MVP scaffold.",
        "Credential request is disabled by the current MVP policy.")]
    [InlineData(
        "UnsupportedFlow",
        "Requested identity flow is not supported by the MVP scaffold.",
        "Requested identity flow is not supported by the current MVP policy.")]
    // editorconfig-checker-disable
    public void ExecuteProtocolTrustedCredentialCoreLegacyFlowMessagesRestoreCredentialCoreFallbackAfterRedaction(
    // editorconfig-checker-enable
        string scenario,
        string legacySafeMessage,
        string expectedFallbackMessage)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        (CredentialResult Result, string Code) credentialCoreScenario = scenario switch
        {
            "FlowDisabled" => (
                CreateTrustedCredentialCoreFailureResult(
                    CredentialResultStatus.FlowDisabled,
                    CredentialErrorKind.FlowDisabled,
                    "FlowDisabled",
                    legacySafeMessage),
                "FlowDisabled"),
            "UnsupportedFlow" => (
                CreateTrustedCredentialCoreFailureResult(
                    CredentialResultStatus.UnsupportedFlow,
                    CredentialErrorKind.UnsupportedFlow,
                    "UnsupportedFlow",
                    legacySafeMessage),
                "UnsupportedFlow"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: CreateBlankingRedactor(legacySafeMessage));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialCoreScenario.Result,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(credentialCoreScenario.Code, outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(expectedFallbackMessage, diagnosticText, StringComparison.Ordinal);
        Assert.Contains(
            $"code={credentialCoreScenario.Code}",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(legacySafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential core diagnostic details are unavailable.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(expectedFallbackMessage, diagnosticEvent.Message);
        Assert.Equal(credentialCoreScenario.Code, diagnosticEvent.Properties["code"]);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Fact]
    public void ExecuteProtocolShapeMatchingTrustedCredentialCoreFailureStripsProducerSafeDetails()
    {
        const string producerSafeDetail = "producer detail should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialResult credentialResult = CreateTrustedCredentialCoreFailureResult(
            CredentialResultStatus.FlowDisabled,
            CredentialErrorKind.FlowDisabled,
            "FlowDisabled",
            "Credential request is disabled by the current MVP policy.");
        CredentialError error = Assert.IsType<CredentialError>(credentialResult.Error);
        credentialResult = credentialResult with
        {
            Error = error with
            {
                SafeDetails = new Dictionary<string, string>
                {
                    ["producerDetail"] = producerSafeDetail,
                },
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("FlowDisabled", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential request is disabled by the current MVP policy.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=FlowDisabled", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeDetail, diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(
            "Credential request is disabled by the current MVP policy.",
            diagnosticEvent.Message);
        Assert.Equal("FlowDisabled", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Theory]
    [InlineData(
        CredentialResultStatus.CredentialUnavailable,
        CredentialErrorKind.CredentialUnavailable,
        "OperationNotSupported",
        "Credential core scaffold only supports get operations.",
        null)]
    [InlineData(
        CredentialResultStatus.ProtocolViolation,
        CredentialErrorKind.ProtocolViolation,
        "ProtocolViolation",
        "Credential request was invalid.",
        "Adapter host protocol output was invalid.")]
    // editorconfig-checker-disable
    public void ExecuteProtocolNullSafeMessageForTrustedCredentialCoreCodesFailsClosedToGenericMappedDiagnostic(
    // editorconfig-checker-enable
        CredentialResultStatus status,
        CredentialErrorKind errorKind,
        string safeCode,
        string rejectedCredentialCoreMessage,
        string? rejectedAdapterHostMessage)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialResult credentialResult = CreateTrustedCredentialCoreFailureResult(
            status,
            errorKind,
            safeCode,
            safeMessage: null!);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(safeCode, outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains($"code={safeCode}", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            rejectedCredentialCoreMessage,
            diagnosticText,
            StringComparison.Ordinal);
        if (rejectedAdapterHostMessage is not null)
        {
            Assert.DoesNotContain(
                rejectedAdapterHostMessage,
                diagnosticText,
                StringComparison.Ordinal);
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal(safeCode, diagnosticEvent.Properties["code"]);
        Assert.False(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.AdapterHost, diagnosticEvent.FallbackScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
        ExecuteProtocolTrustedCoreProtocolViolationRestoresCoreFallbackAfterRedaction(
        bool redactCode)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialRequest request = CreateCredentialCoreGitRequest() with
        {
            ServiceIdentity = "Default",
        };
        CredentialResult standaloneResult = new CredentialCoreService(
            new DeterministicFakeIdentityProvider()).Execute(request);
        CredentialError error = Assert.IsType<CredentialError>(standaloneResult.Error);
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: CreateBlankingRedactor(
                redactCode
                    ? [error.SafeMessage, error.Code]
                    : [error.SafeMessage]));
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: service.Execute(request),
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(error.SafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host protocol output was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        if (redactCode)
        {
            Assert.DoesNotContain(
                "code=ProtocolViolation",
                diagnosticText,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Credential request was invalid.", diagnosticEvent.Message);
        if (redactCode)
        {
            Assert.False(diagnosticEvent.Properties.ContainsKey("code"));
        }
        else
        {
            Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        }

        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Fact]
    public void
        ExecuteProtocolShapeMatchingTrustedCoreProtocolViolationStripsProducerMessageAndDetails()
    {
        const string producerSafeMessage =
            "Protocol violation: producer suffix should not leak.";
        const string producerSafeDetail = "producer detail should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialResult credentialResult = new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = producerSafeMessage,
                SafeDetails = new Dictionary<string, string>
                {
                    ["status"] = CredentialResultStatus.ProtocolViolation.ToString(),
                    ["operation"] = "producer operation should not leak",
                    ["ecosystem"] = "producer ecosystem should not leak",
                    ["credentialKind"] = "producer credential kind should not leak",
                    ["identityFlow"] = "producer identity flow should not leak",
                    ["producerDetail"] = producerSafeDetail,
                },
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeDetail, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "producer operation should not leak",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host protocol output was invalid.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential request was invalid.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteProtocolCaseInsensitiveNonCanonicalProtocolViolationSafeDetailsFallsBackToGenericMappedDiagnostic()
    // editorconfig-checker-enable
    {
        const string producerSafeMessage =
            "Protocol violation: producer suffix should not leak.";
        const string producerSafeDetail = "producer detail should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialResult credentialResult = new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = producerSafeMessage,
                SafeDetails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Status"] = CredentialResultStatus.ProtocolViolation.ToString(),
                    ["Operation"] = "producer operation should not leak",
                    ["Ecosystem"] = "producer ecosystem should not leak",
                    ["CredentialKind"] = "producer credential kind should not leak",
                    ["IdentityFlow"] = "producer identity flow should not leak",
                    ["producerDetail"] = producerSafeDetail,
                },
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeDetail, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "producer operation should not leak",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.False(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.AdapterHost, diagnosticEvent.FallbackScope);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteProtocolCaseInsensitiveCanonicalProtocolViolationSafeDetailsPreservesCredentialCoreFallback()
    // editorconfig-checker-enable
    {
        const string producerSafeMessage =
            "Protocol violation: producer suffix should not leak.";
        const string producerSafeDetail = "producer detail should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialResult credentialResult = new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = producerSafeMessage,
                SafeDetails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = CredentialResultStatus.ProtocolViolation.ToString(),
                    ["operation"] = "producer operation should not leak",
                    ["ecosystem"] = "producer ecosystem should not leak",
                    ["credentialKind"] = "producer credential kind should not leak",
                    ["identityFlow"] = "producer identity flow should not leak",
                    ["producerDetail"] = producerSafeDetail,
                },
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeDetail, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "producer operation should not leak",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential request was invalid.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Fact]
    public void
        ExecuteProtocolCanonicalProtocolViolationSafeDetailsStopsAfterRequiredTrustedKeys()
    {
        const string producerSafeMessage =
            "Protocol violation: producer suffix should not leak.";
        const string producerSafeDetail = "producer detail should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialResult credentialResult = new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = producerSafeMessage,
                SafeDetails = new ThrowingSafeDetailsDictionary(
                    [
                        new KeyValuePair<string, string>(
                            "status",
                            CredentialResultStatus.ProtocolViolation.ToString()),
                        new KeyValuePair<string, string>(
                            "operation",
                            "producer operation should not leak"),
                        new KeyValuePair<string, string>(
                            "ecosystem",
                            "producer ecosystem should not leak"),
                        new KeyValuePair<string, string>(
                            "credentialKind",
                            "producer credential kind should not leak"),
                        new KeyValuePair<string, string>(
                            "identityFlow",
                            "producer identity flow should not leak"),
                        new KeyValuePair<string, string>("producerDetail", producerSafeDetail),
                    ],
                    throwAfterYieldCount: 5),
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeDetail, diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Credential request was invalid.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteProtocolCanonicalProtocolViolationSafeDetailsAfterInspectionCapFallsBackToGenericMappedDiagnostic()
    // editorconfig-checker-enable
    {
        const string producerSafeMessage =
            "Protocol violation: producer suffix should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        int inspectionCap = GetProtocolViolationSafeDetailsInspectionCap();
        List<KeyValuePair<string, string>> safeDetailPairs = [];
        for (var index = 0; index < inspectionCap; index++)
        {
            safeDetailPairs.Add(new KeyValuePair<string, string>(
                $"ignored{index}",
                $"producer ignored detail {index}"));
        }

        safeDetailPairs.Add(new KeyValuePair<string, string>(
            "status",
            CredentialResultStatus.ProtocolViolation.ToString()));
        safeDetailPairs.Add(new KeyValuePair<string, string>(
            "operation",
            "producer operation should not leak"));
        safeDetailPairs.Add(new KeyValuePair<string, string>(
            "ecosystem",
            "producer ecosystem should not leak"));
        safeDetailPairs.Add(new KeyValuePair<string, string>(
            "credentialKind",
            "producer credential kind should not leak"));
        safeDetailPairs.Add(new KeyValuePair<string, string>(
            "identityFlow",
            "producer identity flow should not leak"));
        CountingSafeDetailsDictionary safeDetails = new(safeDetailPairs);
        CredentialResult credentialResult = new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = producerSafeMessage,
                SafeDetails = safeDetails,
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.Equal(inspectionCap, safeDetails.EnumeratedCount);
        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "producer operation should not leak",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.False(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.AdapterHost, diagnosticEvent.FallbackScope);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteProtocolMissingCanonicalProtocolViolationSafeDetailStopsAtInspectionCapAndFallsBackToGenericMappedDiagnostic()
    // editorconfig-checker-enable
    {
        const string producerSafeMessage =
            "Protocol violation: producer suffix should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        int inspectionCap = GetProtocolViolationSafeDetailsInspectionCap();
        List<KeyValuePair<string, string>> safeDetailPairs =
        [
            new(
                "status",
                CredentialResultStatus.ProtocolViolation.ToString()),
            new(
                "operation",
                "producer operation should not leak"),
            new(
                "ecosystem",
                "producer ecosystem should not leak"),
            new(
                "credentialKind",
                "producer credential kind should not leak"),
        ];
        for (var index = 0; index < inspectionCap; index++)
        {
            safeDetailPairs.Add(new KeyValuePair<string, string>(
                $"ignored{index}",
                $"producer ignored detail {index}"));
        }

        CountingSafeDetailsDictionary safeDetails = new(safeDetailPairs);
        CredentialResult credentialResult = new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = producerSafeMessage,
                SafeDetails = safeDetails,
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.Equal(inspectionCap, safeDetails.EnumeratedCount);
        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "producer operation should not leak",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.False(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.AdapterHost, diagnosticEvent.FallbackScope);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteProtocolTrustedCoreProtocolViolationSafeDetailsLookupFailurePreservesCredentialCoreFallback()
    // editorconfig-checker-enable
    {
        const string producerSafeMessage =
            "Protocol violation: producer safe message should not leak.";
        const string producerSafeDetail = "producer detail should not leak.";
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        CredentialRequest request = CreateCredentialCoreGitRequest() with
        {
            ServiceIdentity = "Default",
        };
        CredentialResult credentialResult = new CredentialCoreService(
            new DeterministicFakeIdentityProvider()).Execute(request);
        CredentialError error = Assert.IsType<CredentialError>(credentialResult.Error);
        Assert.StartsWith("Protocol violation: ", error.SafeMessage);
        IReadOnlyDictionary<string, string> safeDetails =
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(error.SafeDetails);
        credentialResult = credentialResult with
        {
            Error = error with
            {
                SafeMessage = producerSafeMessage,
                SafeDetails = new LookupThrowingSafeDetailsDictionary(
                    new Dictionary<string, string>(safeDetails, StringComparer.Ordinal)
                    {
                        ["producerDetail"] = producerSafeDetail,
                    }),
            },
        };
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(producerSafeDetail, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host protocol output was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Credential request was invalid.", diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        Assert.Single(diagnosticEvent.Properties);
        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.CredentialCore, diagnosticEvent.FallbackScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
        ExecuteProtocolMapperOwnedProtocolViolationPreservesAdapterHostFallbackAfterRedaction(
        bool redactCode)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        const string expectedMessage = "Adapter host protocol output was invalid.";
        var capture = new OutputCapture(
            includeDiagnosticTextWriter: true,
            redactor: CreateBlankingRedactor(
                redactCode
                    ? [expectedMessage, "ProtocolViolation"]
                    : [expectedMessage]));

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: null),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(expectedMessage, diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential request was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        if (redactCode)
        {
            Assert.DoesNotContain(
                "code=ProtocolViolation",
                diagnosticText,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("code=ProtocolViolation", diagnosticText, StringComparison.Ordinal);
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        if (redactCode)
        {
            Assert.False(diagnosticEvent.Properties.ContainsKey("code"));
        }
        else
        {
            Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
        }

        Assert.True(diagnosticEvent.AllowCodeSpecificFallback);
        Assert.Equal(SafeDiagnosticFallbackScope.AdapterHost, diagnosticEvent.FallbackScope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExecuteProtocolMissingMappedStdoutBecomesProtocolViolation(
        string? protocolStdout)
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolStdout),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains(
            "Adapter host protocol output was invalid.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains(
            "code=ProtocolViolation",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("username=fake", diagnosticText, StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal(
            "Adapter host protocol output was invalid.",
            diagnosticEvent.Message);
        Assert.Equal("ProtocolViolation", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolUnhandledExceptionBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => throw new InvalidOperationException(
                "top-secret-token should never be exposed"),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Contains(
            "Adapter host execution failed.",
            capture.DiagnosticText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "top-secret-token",
            capture.DiagnosticText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            capture.DiagnosticText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "top-secret-token",
            capture.ProtocolStdout.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteProtocolDiagnosticZeroByteFailureBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var diagnosticRouter = new DiagnosticRouter(
            [
                new ThrowingDiagnosticSink(new IOException("diagnostic stderr write failed")),
            ],
            SecretRedactor.Empty);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateUnauthorizedCredentialResult()),
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: diagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
    }

    [Fact]
    public void ExecuteProtocolDiagnosticCommitFailurePropagatesWithoutSafeFatalRemap()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var diagnosticText = new StringWriter();
        var diagnosticRouter = new DiagnosticRouter(
            [
                new TextWriterDiagnosticSink(diagnosticText),
                new ThrowingDiagnosticSink(new IOException("diagnostic stderr write failed")),
            ],
            SecretRedactor.Empty);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateUnauthorizedCredentialResult()),
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: diagnosticRouter));

        Assert.Equal("diagnostic stderr write failed", exception.Message);

        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Contains(
            "Authorization is required.",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.Contains("code=Unauthorized", diagnosticTextValue, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=UnhandledHostFailure",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticTextValue.Split('\n').Length - 1);
    }

    [Fact]
    public void ExecuteFallbackSafeDiagnosticCommitFailurePropagatesWriteException()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var diagnosticWriter = new PartialThrowingTextWriter(
            throwAfterCharacterCount: 1,
            exceptionToThrow: new IOException("fallback diagnostic write failed"));
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(diagnosticWriter)],
            SecretRedactor.Empty);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => throw new InvalidOperationException("should not be remapped"),
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: diagnosticRouter));

        Assert.Equal("fallback diagnostic write failed", exception.Message);
        Assert.NotEmpty(diagnosticWriter.Written);
    }

    [Fact]
    public void ExecuteProtocolZeroByteStdoutFailureBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new PartialThrowingStringOnlyTextWriter(
            throwAfterCharacterCount: 0,
            exceptionToThrow: new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: GitProtocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, protocolStdout.Written);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "protocol stdout write failed",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal(
            "Adapter host execution failed.",
            diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolStdoutBmpAppendThenThrowFailurePropagatesWriteException()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new AppendThenThrowStringTextWriter(
            new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: GitProtocolPayload,
                humanStdout: "human text that must not leak",
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "diagnostic text that must not leak")
                ]),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("protocol stdout write failed", exception.Message);
        Assert.Equal("u", protocolStdout.Written);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteProtocolStdoutStringWriterSubclassFailurePropagatesWriteException()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new ExternallyWritingThrowingStringWriter(
            new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: GitProtocolPayload,
                humanStdout: "human text that must not leak",
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "diagnostic text that must not leak")
                ]),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("protocol stdout write failed", exception.Message);
        Assert.Equal("u", protocolStdout.Written);
        Assert.Equal(string.Empty, protocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteProtocolLeadingNonBmpZeroByteStdoutFailureBecomesSafeFatalFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new PartialThrowingStringOnlyTextWriter(
            throwAfterCharacterCount: 0,
            exceptionToThrow: new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "🚀username=fake\npassword=fake\n";

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.Protocol, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Equal(string.Empty, protocolStdout.Written);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("code=UnhandledHostFailure", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "protocol stdout write failed",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.True(diagnosticEvent.IsSafeDiagnosticEnvelope);
        Assert.Equal("Adapter host execution failed.", diagnosticEvent.Message);
        Assert.Equal("UnhandledHostFailure", diagnosticEvent.Properties["code"]);
    }

    [Fact]
    public void ExecuteProtocolLeadingNonBmpPartialWriteCountsAsCommitted()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new PartialThrowingStringOnlyTextWriter(
            throwAfterCharacterCount: 1,
            exceptionToThrow: new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        const string protocolPayload = "🚀username=fake\npassword=fake\n";

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: protocolPayload),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("protocol stdout write failed", exception.Message);
        Assert.Equal(1, protocolStdout.Written.Length);
        Assert.True(char.IsHighSurrogate(protocolStdout.Written[0]));
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteHandlerDiagnosticCommitPreventsSecondEnvelopeOnZeroByteStdoutFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new PartialThrowingTextWriter(
            throwAfterCharacterCount: 0,
            exceptionToThrow: new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                capture.DiagnosticRouter.Route(new DiagnosticEvent(
                    DiagnosticSeverity.Warning,
                    DiagnosticChannel.Diagnostic,
                    "handler diagnostic"));

                return new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: GitProtocolPayload);
            },
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("protocol stdout write failed", exception.Message);
        Assert.Equal(string.Empty, protocolStdout.Written);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("handler diagnostic", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=UnhandledHostFailure",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("handler diagnostic", diagnosticEvent.Message);
        Assert.False(diagnosticEvent.IsSafeDiagnosticEnvelope);
    }

    [Fact]
    public void ExecuteNestedChildProtocolStdoutCommitSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    AdapterHostExecutionOutcome childOutcome = AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["git", "credential-helper", "get"],
                        handler: static _ => new AdapterHostHandlerOutput(
                            credentialResult: CreateSuccessCredentialResult(),
                            protocolStdout: GitProtocolPayload),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);
                    Assert.Equal(AdapterHostExitCode.Success, childOutcome.Result.ExitCode);
                    throw new InvalidOperationException(
                        "outer execution failed after child protocol stdout");
                },
                protocolStdout: capture.ProtocolStdout,
                humanStdout: capture.HumanStdout,
                diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal(
            "outer execution failed after child protocol stdout",
            exception.Message);
        Assert.Equal(GitProtocolPayload, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteNestedChildHumanStdoutCommitSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: _ =>
                {
                    AdapterHostExecutionOutcome childOutcome = AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["doctor", "--json"],
                        handler: static _ => new AdapterHostHandlerOutput(
                            humanStdout: "doctor ok",
                            protocolStdout: SuppressedProtocolPayload),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);
                    Assert.Equal(AdapterHostExitCode.Success, childOutcome.Result.ExitCode);
                    throw new InvalidOperationException(
                        "outer execution failed after child human stdout");
                },
                protocolStdout: capture.ProtocolStdout,
                humanStdout: capture.HumanStdout,
                diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal(
            "outer execution failed after child human stdout",
            exception.Message);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal("doctor ok", capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void
        ExecuteSequentialNestedChildProtocolStdoutCommitSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: _ =>
                {
                    AdapterHostExecutionOutcome firstChildOutcome = AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["git", "credential-helper", "get"],
                        handler: static _ => new AdapterHostHandlerOutput(
                            credentialResult: CreateSuccessCredentialResult(),
                            protocolStdout: GitProtocolPayload),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);
                    Assert.Equal(AdapterHostExitCode.Success, firstChildOutcome.Result.ExitCode);

                    AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["git", "credential-helper", "get"],
                        handler: static _ => throw new InvalidOperationException(
                            "second nested child failed after protocol stdout"),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);

                    return new AdapterHostHandlerOutput();
                },
                protocolStdout: capture.ProtocolStdout,
                humanStdout: capture.HumanStdout,
                diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("second nested child failed after protocol stdout", exception.Message);
        Assert.Equal(GitProtocolPayload, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void
        ExecuteSequentialNestedChildHumanStdoutCommitSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: _ =>
                {
                    AdapterHostExecutionOutcome firstChildOutcome = AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["doctor", "--json"],
                        handler: static _ => new AdapterHostHandlerOutput(
                            humanStdout: "doctor ok",
                            protocolStdout: SuppressedProtocolPayload),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);
                    Assert.Equal(AdapterHostExitCode.Success, firstChildOutcome.Result.ExitCode);

                    AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["doctor", "--json"],
                        handler: static _ => throw new InvalidOperationException(
                            "second nested child failed after human stdout"),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);

                    return new AdapterHostHandlerOutput();
                },
                protocolStdout: capture.ProtocolStdout,
                humanStdout: capture.HumanStdout,
                diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("second nested child failed after human stdout", exception.Message);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal("doctor ok", capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteSequentialNestedChildDiagnosticCommitSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: _ =>
                {
                    AdapterHostExecutionOutcome firstChildOutcome = AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["doctor", "--json"],
                        handler: _ =>
                        {
                            capture.DiagnosticRouter.Route(new DiagnosticEvent(
                                DiagnosticSeverity.Warning,
                                DiagnosticChannel.Diagnostic,
                                "first nested child diagnostic"));
                            return new AdapterHostHandlerOutput();
                        },
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);
                    Assert.Equal(AdapterHostExitCode.Success, firstChildOutcome.Result.ExitCode);

                    AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "/usr/local/bin/azureauth-credprovider",
                        arguments: ["doctor", "--json"],
                        handler: static _ => throw new InvalidOperationException(
                            "second nested child failed after diagnostic output"),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter);

                    return new AdapterHostHandlerOutput();
                },
                protocolStdout: capture.ProtocolStdout,
                humanStdout: capture.HumanStdout,
                diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("second nested child failed after diagnostic output", exception.Message);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("first nested child diagnostic", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host execution failed.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=UnhandledHostFailure",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("first nested child diagnostic", diagnosticEvent.Message);
        Assert.False(diagnosticEvent.IsSafeDiagnosticEnvelope);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteNestedBootstrapFailureAfterAncestorDiagnosticCommitSuppressesInvocationBoundaryFallback()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: _ =>
            {
                capture.DiagnosticRouter.Route(
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "ancestor diagnostic"));

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    AdapterHostExecutor.Execute(
                        descriptor,
                        executablePath: "..",
                        arguments: ["doctor", "--json"],
                        handler: static _ => new AdapterHostHandlerOutput(
                            humanStdout: "should-not-run",
                            protocolStdout: SuppressedProtocolPayload),
                        protocolStdout: capture.ProtocolStdout,
                        humanStdout: capture.HumanStdout,
                        diagnosticRouter: capture.DiagnosticRouter));
                Assert.Contains(
                    "does not match the current invocation boundary",
                    exception.Message,
                    StringComparison.Ordinal);

                return new AdapterHostHandlerOutput();
            },
            protocolStdout: capture.ProtocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter);

        Assert.NotNull(outcome.Invocation);
        Assert.Equal(AdapterInvocationMode.HumanCommand, outcome.Invocation.Mode);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("ancestor diagnostic", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Adapter host invocation boundary is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=InvocationBoundaryMismatch",
            diagnosticText,
            StringComparison.Ordinal);

        DiagnosticEvent diagnosticEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("ancestor diagnostic", diagnosticEvent.Message);
        Assert.False(diagnosticEvent.IsSafeDiagnosticEnvelope);
    }

    [Fact]
    public async Task
        ExecuteInFlightNestedChildProtocolStdoutSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var protocolStdout = new BlockingStringTextWriter();
        var humanStdout = new StringWriter();
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [recordingSink, new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        Task<AdapterHostExecutionOutcome>? childExecutionTask = null;

        InvalidOperationException exception;
        try
        {
            exception = Assert.Throws<InvalidOperationException>(() =>
                AdapterHostExecutor.Execute(
                    descriptor,
                    executablePath: "/usr/local/bin/azureauth-credprovider",
                    arguments: ["git", "credential-helper", "get"],
                    handler: _ =>
                    {
                        childExecutionTask = Task.Run(
                            () => AdapterHostExecutor.Execute(
                                descriptor,
                                executablePath: "/usr/local/bin/azureauth-credprovider",
                                arguments: ["git", "credential-helper", "get"],
                                handler: static _ => new AdapterHostHandlerOutput(
                                    credentialResult: CreateSuccessCredentialResult(),
                                    protocolStdout: GitProtocolPayload),
                                protocolStdout: protocolStdout,
                                humanStdout: humanStdout,
                                diagnosticRouter: diagnosticRouter),
                            TestContext.Current.CancellationToken);
                        protocolStdout.WaitForBlockedWriteEntered();
                        throw new InvalidOperationException(
                            "outer execution failed after in-flight child protocol stdout");
                    },
                    protocolStdout: protocolStdout,
                    humanStdout: humanStdout,
                    diagnosticRouter: diagnosticRouter));
        }
        finally
        {
            protocolStdout.ReleaseBlockedWrite();

            if (childExecutionTask is not null)
            {
                AdapterHostExecutionOutcome childOutcome = await childExecutionTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
                Assert.Equal(AdapterHostExitCode.Success, childOutcome.Result.ExitCode);
            }
        }

        Assert.Equal(
            "outer execution failed after in-flight child protocol stdout",
            exception.Message);
        Assert.Equal(GitProtocolPayload, protocolStdout.Written);
        Assert.Equal(string.Empty, humanStdout.ToString());
        Assert.Equal(string.Empty, diagnosticText.ToString());
        Assert.Empty(recordingSink.Events);
    }

    [Fact]
    public async Task
        ExecuteInFlightNestedChildHumanStdoutSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new StringWriter();
        using var humanStdout = new BlockingStringTextWriter();
        var diagnosticText = new StringWriter();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [recordingSink, new TextWriterDiagnosticSink(diagnosticText)],
            SecretRedactor.Empty);
        Task<AdapterHostExecutionOutcome>? childExecutionTask = null;

        InvalidOperationException exception;
        try
        {
            exception = Assert.Throws<InvalidOperationException>(() =>
                AdapterHostExecutor.Execute(
                    descriptor,
                    executablePath: "/usr/local/bin/azureauth-credprovider",
                    arguments: ["doctor", "--json"],
                    handler: _ =>
                    {
                        childExecutionTask = Task.Run(
                            () => AdapterHostExecutor.Execute(
                                descriptor,
                                executablePath: "/usr/local/bin/azureauth-credprovider",
                                arguments: ["doctor", "--json"],
                                handler: static _ => new AdapterHostHandlerOutput(
                                    humanStdout: "doctor ok",
                                    protocolStdout: SuppressedProtocolPayload),
                                protocolStdout: protocolStdout,
                                humanStdout: humanStdout,
                                diagnosticRouter: diagnosticRouter),
                            TestContext.Current.CancellationToken);
                        humanStdout.WaitForBlockedWriteEntered();
                        throw new InvalidOperationException(
                            "outer execution failed after in-flight child human stdout");
                    },
                    protocolStdout: protocolStdout,
                    humanStdout: humanStdout,
                    diagnosticRouter: diagnosticRouter));
        }
        finally
        {
            humanStdout.ReleaseBlockedWrite();

            if (childExecutionTask is not null)
            {
                AdapterHostExecutionOutcome childOutcome = await childExecutionTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
                Assert.Equal(AdapterHostExitCode.Success, childOutcome.Result.ExitCode);
            }
        }

        Assert.Equal(
            "outer execution failed after in-flight child human stdout",
            exception.Message);
        Assert.Equal(string.Empty, protocolStdout.ToString());
        Assert.Equal("doctor ok", humanStdout.Written);
        Assert.Equal(string.Empty, diagnosticText.ToString());
        Assert.Empty(recordingSink.Events);
    }

    [Fact]
    public async Task ExecuteLateNestedChildHumanStdoutDoesNotWriteAfterFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        using var childHandlerEntered = new ManualResetEventSlim(false);
        using var releaseChildExecution = new ManualResetEventSlim(false);
        Task<AdapterHostExecutionOutcome>? childExecutionTask = null;
        AdapterHostExecutionOutcome? outcome = null;
        AdapterHostExecutionOutcome? childOutcome = null;
        Exception? exception = null;

        try
        {
            exception = Record.Exception(() => outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: _ =>
                {
                    childExecutionTask = Task.Run(
                        () => AdapterHostExecutor.Execute(
                            descriptor,
                            executablePath: "/usr/local/bin/azureauth-credprovider",
                            arguments: ["doctor", "--json"],
                            handler: _ =>
                            {
                                childHandlerEntered.Set();
                                if (!releaseChildExecution.Wait(TimeSpan.FromSeconds(10)))
                                {
                                    throw new TimeoutException(
                                        "Timed out waiting to release the child " +
                                        "human stdout execution.");
                                }

                                return new AdapterHostHandlerOutput(
                                    humanStdout: "doctor ok",
                                    protocolStdout: SuppressedProtocolPayload);
                            },
                            protocolStdout: capture.ProtocolStdout,
                            humanStdout: capture.HumanStdout,
                            diagnosticRouter: capture.DiagnosticRouter),
                        TestContext.Current.CancellationToken);
                    if (!childHandlerEntered.Wait(
                        TimeSpan.FromSeconds(10),
                        TestContext.Current.CancellationToken))
                    {
                        throw new TimeoutException(
                            "Timed out waiting for the child human stdout execution " +
                            "to enter the handler.");
                    }

                    throw new InvalidOperationException(
                        "outer execution failed before late child human stdout");
                },
                protocolStdout: capture.ProtocolStdout,
                humanStdout: capture.HumanStdout,
                diagnosticRouter: capture.DiagnosticRouter));
        }
        finally
        {
            releaseChildExecution.Set();

            if (childExecutionTask is not null)
            {
                childOutcome = await childExecutionTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            }
        }

        Assert.Null(exception);

        AdapterHostExecutionOutcome parentOutcome = Assert.IsType<AdapterHostExecutionOutcome>(
            outcome);
        AdapterHostExecutionOutcome lateChildOutcome =
            Assert.IsType<AdapterHostExecutionOutcome>(childOutcome);

        Assert.Equal(AdapterHostExitCode.Fatal, parentOutcome.Result.ExitCode);
        Assert.False(parentOutcome.Result.WriteProtocolStdout);
        Assert.True(parentOutcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", parentOutcome.Result.SafeDiagnosticCode);

        Assert.Equal(AdapterHostExitCode.Success, lateChildOutcome.Result.ExitCode);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("doctor ok", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);

        DiagnosticEvent fallbackEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);
    }

    [Fact]
    public void ExecuteProtocolStdoutCommitFailurePropagatesWriteException()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new PartialThrowingTextWriter(
            throwAfterCharacterCount: "username=".Length,
            exceptionToThrow: new IOException("protocol stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateSuccessCredentialResult(),
                protocolStdout: GitProtocolPayload,
                humanStdout: "human text that must not leak",
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "diagnostic text that must not leak")
                ]),
            protocolStdout: protocolStdout,
            humanStdout: capture.HumanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("protocol stdout write failed", exception.Message);
        Assert.Equal("username=", protocolStdout.Written);
        Assert.Equal(string.Empty, capture.HumanStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void
        ExecuteConcurrentChildTaskDiagnosticCommitsPreventUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        const int attemptCount = 64;
        const int concurrentChildTaskCount = 8;

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            using var sink = new CoordinatedMixedCommitTrackingDiagnosticSink(
                concurrentChildTaskCount);
            var diagnosticRouter = new DiagnosticRouter([sink], SecretRedactor.Empty);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                AdapterHostExecutor.Execute(
                    descriptor,
                    executablePath: "/usr/local/bin/azureauth-credprovider",
                    arguments: ["git", "credential-helper", "get"],
                    handler: _ =>
                    {
                        Task[] childTasks = sink.StartConcurrentRoutes(diagnosticRouter);
                        sink.WaitForPendingWrites();
                        sink.ReleasePendingWrites();
                        Task.WaitAll(childTasks);
                        throw new InvalidOperationException(
                            "handler failed after child diagnostics");
                    },
                    protocolStdout: new StringWriter(),
                    humanStdout: new StringWriter(),
                    diagnosticRouter: diagnosticRouter));

            Assert.Equal("handler failed after child diagnostics", exception.Message);
            Assert.Equal(concurrentChildTaskCount, sink.AttemptedEvents.Length);
            Assert.Contains(
                sink.AttemptedEvents,
                diagnosticEvent => string.Equals(
                    diagnosticEvent.Message,
                    CoordinatedMixedCommitTrackingDiagnosticSink.CommittedMessage,
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                sink.AttemptedEvents,
                diagnosticEvent => string.Equals(
                    diagnosticEvent.Message,
                    "Adapter host execution failed.",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ExecuteLateFireAndForgetChildDiagnosticDoesNotWriteAfterFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        using var releaseLateDiagnostic = new ManualResetEventSlim(false);
        Task? lateDiagnosticTask = null;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                lateDiagnosticTask = Task.Run(() =>
                {
                    if (!releaseLateDiagnostic.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the late child diagnostic.");
                    }

                    capture.DiagnosticRouter.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "late child diagnostic"));
                });

                throw new InvalidOperationException(
                    "handler failed before the late child diagnostic ran");
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: capture.DiagnosticRouter);

        releaseLateDiagnostic.Set();
        await Assert
            .IsType<Task>(lateDiagnosticTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

        DiagnosticEvent fallbackEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("late child diagnostic", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);
    }

    [Fact]
    public async Task ExecuteLateCredentialCoreSafeDiagnosticDoesNotWriteAfterHostFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);
        using var releaseLateCredentialCoreExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? lateCredentialCoreExecutionTask = null;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                lateCredentialCoreExecutionTask = Task.Run(
                    () =>
                    {
                        if (!releaseLateCredentialCoreExecution.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the late credential-core "
                                    + "execution.");
                        }

                        return service.Execute(
                            CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));
                    },
                    TestContext.Current.CancellationToken);

                throw new InvalidOperationException(
                    "handler failed before the late credential-core execution ran");
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: capture.DiagnosticRouter);

        releaseLateCredentialCoreExecution.Set();

        CredentialResult lateCredentialCoreResult = await Assert
            .IsType<Task<CredentialResult>>(lateCredentialCoreExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

        DiagnosticEvent fallbackEvent = Assert.Single(capture.RecordingSink.Events);
        Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Requested identity flow is deferred by the MVP scaffold.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("code=FlowDeferred", diagnosticText, StringComparison.Ordinal);
        Assert.Equal(1, diagnosticText.Split('\n').Length - 1);
    }

    [Fact]
    public async Task
        ExecuteLateCredentialCoreSafeDiagnosticDoesNotWriteAfterZeroByteFallbackDiagnosticFailure()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [
                recordingSink,
                new ThrowingDiagnosticSink(new IOException("fallback diagnostic write failed")),
            ],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            diagnosticRouter);
        using var releaseLateCredentialCoreExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? lateCredentialCoreExecutionTask = null;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                lateCredentialCoreExecutionTask = Task.Run(
                    () =>
                    {
                        if (!releaseLateCredentialCoreExecution.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the late credential-core "
                                    + "execution.");
                        }

                        return service.Execute(
                            CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));
                    },
                    TestContext.Current.CancellationToken);

                throw new InvalidOperationException(
                    "handler failed before the late credential-core execution ran");
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: diagnosticRouter);

        releaseLateCredentialCoreExecution.Set();

        CredentialResult lateCredentialCoreResult = await Assert
            .IsType<Task<CredentialResult>>(lateCredentialCoreExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

        DiagnosticEvent fallbackEvent = Assert.Single(recordingSink.Events);
        Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);
        Assert.Equal("UnhandledHostFailure", fallbackEvent.Properties["code"]);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteCurrentThreadCredentialCoreSafeDiagnosticReopensAfterZeroByteFallbackDiagnosticFailureWithoutAmbientScope()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [
                recordingSink,
                new ThrowingDiagnosticSink(new IOException("fallback diagnostic write failed")),
            ],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            diagnosticRouter);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => throw new InvalidOperationException(
                "handler failed before the follow-up credential-core execution ran"),
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: diagnosticRouter);

        CredentialResult lateCredentialCoreResult = service.Execute(
            CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));

        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
        Assert.Collection(
            recordingSink.Events,
            fallbackEvent =>
            {
                Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);
                Assert.Equal("UnhandledHostFailure", fallbackEvent.Properties["code"]);
            },
            credentialCoreEvent =>
            {
                Assert.Equal(
                    "Requested identity flow is deferred by the MVP scaffold.",
                    credentialCoreEvent.Message);
                Assert.Equal("FlowDeferred", credentialCoreEvent.Properties["code"]);
            });
    }

    [Fact]
    public async Task
        ExecuteLateCredentialCoreSafeDiagnosticDoesNotWriteAfterHostFallbackWithAmbientOuterScope()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            capture.DiagnosticRouter);
        using var releaseLateCredentialCoreExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? lateCredentialCoreExecutionTask = null;
        AdapterHostExecutionOutcome outcome;

        using (capture.DiagnosticRouter.BeginUserVisibleCommitTracking())
        {
            outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    lateCredentialCoreExecutionTask = Task.Run(
                        () =>
                        {
                            if (!releaseLateCredentialCoreExecution.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException(
                                    "Timed out waiting to release the late credential-core "
                                        + "execution.");
                            }

                            return service.Execute(
                                CreateCredentialCoreGitRequest(
                                    flow: IdentityFlow.ServicePrincipal));
                        },
                        TestContext.Current.CancellationToken);

                    throw new InvalidOperationException(
                        "handler failed before the late credential-core execution ran");
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: capture.DiagnosticRouter);

            releaseLateCredentialCoreExecution.Set();

            CredentialResult lateCredentialCoreResult = await Assert
                .IsType<Task<CredentialResult>>(lateCredentialCoreExecutionTask)
                .WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);

            Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
            Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
            Assert.False(outcome.Result.WriteProtocolStdout);
            Assert.True(outcome.Result.WriteDiagnosticStderr);
            Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

            DiagnosticEvent fallbackEvent = Assert.Single(capture.RecordingSink.Events);
            Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);

            string diagnosticText = capture.DiagnosticText.ToString();
            Assert.Contains(
                "Adapter host execution failed.",
                diagnosticText,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Requested identity flow is deferred by the MVP scaffold.",
                diagnosticText,
                StringComparison.Ordinal);
            Assert.DoesNotContain("code=FlowDeferred", diagnosticText, StringComparison.Ordinal);
            Assert.Equal(1, diagnosticText.Split('\n').Length - 1);
        }
    }

    [Fact]
    // editorconfig-checker-disable
    public async Task ExecuteLateCredentialCoreSafeDiagnosticDoesNotWriteAfterZeroByteFallbackDiagnosticFailureWithAmbientOuterScope()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [
                recordingSink,
                new ThrowingDiagnosticSink(new IOException("fallback diagnostic write failed")),
            ],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            diagnosticRouter);
        using var releaseLateCredentialCoreExecution = new ManualResetEventSlim(false);
        Task<CredentialResult>? lateCredentialCoreExecutionTask = null;
        AdapterHostExecutionOutcome outcome;

        using (diagnosticRouter.BeginUserVisibleCommitTracking())
        {
            outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    lateCredentialCoreExecutionTask = Task.Run(
                        () =>
                        {
                            if (!releaseLateCredentialCoreExecution.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException(
                                    "Timed out waiting to release the late credential-core "
                                        + "execution.");
                            }

                            return service.Execute(
                                CreateCredentialCoreGitRequest(
                                    flow: IdentityFlow.ServicePrincipal
                                ));
                        },
                        TestContext.Current.CancellationToken);

                    throw new InvalidOperationException(
                        "handler failed before the late credential-core execution ran");
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter);

            releaseLateCredentialCoreExecution.Set();

            CredentialResult lateCredentialCoreResult = await Assert
                .IsType<Task<CredentialResult>>(lateCredentialCoreExecutionTask)
                .WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);

            Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
            Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
            Assert.False(outcome.Result.WriteProtocolStdout);
            Assert.True(outcome.Result.WriteDiagnosticStderr);
            Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

            DiagnosticEvent fallbackEvent = Assert.Single(recordingSink.Events);
            Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);
            Assert.Equal("UnhandledHostFailure", fallbackEvent.Properties["code"]);
        }
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteCurrentThreadCredentialCoreSafeDiagnosticDoesNotWriteAfterZeroByteFallbackDiagnosticFailureWithAmbientOuterScope()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [
                recordingSink,
                new ThrowingDiagnosticSink(new IOException("fallback diagnostic write failed")),
            ],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            diagnosticRouter);
        AdapterHostExecutionOutcome outcome;
        CredentialResult lateCredentialCoreResult;

        using (diagnosticRouter.BeginUserVisibleCommitTracking())
        {
            outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => throw new InvalidOperationException(
                    "handler failed before the follow-up credential-core execution ran"),
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter);

            lateCredentialCoreResult = service.Execute(
                CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));
        }

        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

        DiagnosticEvent fallbackEvent = Assert.Single(recordingSink.Events);
        Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);
        Assert.Equal("UnhandledHostFailure", fallbackEvent.Properties["code"]);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteCurrentThreadCredentialCoreSafeDiagnosticDoesNotWriteAfterBootstrapFallbackZeroByteDiagnosticFailureWithAmbientOuterScope()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = new(
            "Human Only",
            AdapterProtocol.Unspecified,
            [
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [
                recordingSink,
                new ThrowingDiagnosticSink(new IOException("fallback diagnostic write failed")),
            ],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            diagnosticRouter);
        AdapterHostExecutionOutcome outcome;
        CredentialResult lateCredentialCoreResult;
        var handlerCalled = false;

        using (diagnosticRouter.BeginUserVisibleCommitTracking())
        {
            outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "..",
                arguments: ["doctor"],
                handler: _ =>
                {
                    handlerCalled = true;
                    return new AdapterHostHandlerOutput(
                        humanStdout: "should-not-run",
                        protocolStdout: SuppressedProtocolPayload);
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter);

            lateCredentialCoreResult = service.Execute(
                CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));
        }

        Assert.False(handlerCalled);
        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("InvocationBoundaryMismatch", outcome.Result.SafeDiagnosticCode);

        DiagnosticEvent fallbackEvent = Assert.Single(recordingSink.Events);
        Assert.Equal("Adapter host invocation boundary is unsupported.", fallbackEvent.Message);
        Assert.Equal("InvocationBoundaryMismatch", fallbackEvent.Properties["code"]);
    }

    [Fact]
    // editorconfig-checker-disable
    public void ExecuteSecondChildCredentialCoreSafeDiagnosticDoesNotWriteAfterZeroByteFallbackDiagnosticFailureWithAmbientOuterScope()
    // editorconfig-checker-enable
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var recordingSink = new RecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [
                recordingSink,
                new ThrowingDiagnosticSink(new IOException("fallback diagnostic write failed")),
            ],
            SecretRedactor.Empty);
        var service = new CredentialCoreService(
            new DeterministicFakeIdentityProvider(),
            diagnosticRouter);
        AdapterHostExecutionOutcome outcome;
        CredentialResult lateCredentialCoreResult;

        using (diagnosticRouter.BeginUserVisibleCommitTracking())
        {
            outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => throw new InvalidOperationException(
                    "handler failed before the follow-up credential-core execution ran"),
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter);

            using (diagnosticRouter.BeginUserVisibleCommitTracking())
            {
                lateCredentialCoreResult = service.Execute(
                    CreateCredentialCoreGitRequest(flow: IdentityFlow.ServicePrincipal));
            }
        }

        Assert.Equal(CredentialResultStatus.FlowDeferred, lateCredentialCoreResult.Status);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);

        DiagnosticEvent fallbackEvent = Assert.Single(recordingSink.Events);
        Assert.Equal("Adapter host execution failed.", fallbackEvent.Message);
        Assert.Equal("UnhandledHostFailure", fallbackEvent.Properties["code"]);
    }

    [Fact]
    public async Task
        ExecuteLateDiagnosticFromDisposedScopeDoesNotContaminateLaterSharedRouterExecution()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        using var releaseLateDiagnostic = new ManualResetEventSlim(false);
        Task? lateDiagnosticTask = null;

        AdapterHostExecutionOutcome firstOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                lateDiagnosticTask = Task.Run(() =>
                {
                    if (!releaseLateDiagnostic.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the prior execution diagnostic.");
                    }

                    capture.DiagnosticRouter.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "late prior execution diagnostic"));
                });

                throw new InvalidOperationException("first execution failed");
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: capture.DiagnosticRouter);

        AdapterHostExecutionOutcome secondOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: static _ => new AdapterHostHandlerOutput(
                credentialResult: CreateUnauthorizedCredentialResult()),
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: capture.DiagnosticRouter);

        releaseLateDiagnostic.Set();
        await Assert
            .IsType<Task>(lateDiagnosticTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(AdapterHostExitCode.Fatal, firstOutcome.Result.ExitCode);
        Assert.Equal("UnhandledHostFailure", firstOutcome.Result.SafeDiagnosticCode);
        Assert.Equal(AdapterHostExitCode.Unauthorized, secondOutcome.Result.ExitCode);
        Assert.Equal("Unauthorized", secondOutcome.Result.SafeDiagnosticCode);

        Assert.Collection(
            capture.RecordingSink.Events,
            firstEvent => Assert.Equal("Adapter host execution failed.", firstEvent.Message),
            secondEvent => Assert.Equal("Authorization is required.", secondEvent.Message));

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Contains("Adapter host execution failed.", diagnosticText, StringComparison.Ordinal);
        Assert.Contains("Authorization is required.", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "late prior execution diagnostic",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Equal(2, diagnosticText.Split('\n').Length - 1);
    }

    [Fact]
    public async Task
        ExecuteFlowedTaskInheritedDisposedScopeStillEmitsBootstrapFallbackAndRestoresPlainRoutes()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        AdapterDescriptor bootstrapFailureDescriptor = new(
            "Human Only",
            AdapterProtocol.Unspecified,
            [
                new AdapterEntrypointDescriptor(
                    "HumanDoctor",
                    AdapterInvocationMode.HumanCommand,
                    argumentTokens: ["doctor"],
                    argumentMatchMode: AdapterArgumentMatchMode.Exact),
            ]);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        using var releaseFlowedTask = new ManualResetEventSlim(false);
        Task<AdapterHostExecutionOutcome>? flowedExecutionTask = null;

        AdapterHostExecutionOutcome firstOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                flowedExecutionTask = Task.Run(
                    () =>
                    {
                        if (!releaseFlowedTask.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the flowed execution.");
                        }

                        var flowedHandlerCalled = false;
                        AdapterHostExecutionOutcome flowedOutcome = AdapterHostExecutor.Execute(
                            bootstrapFailureDescriptor,
                            executablePath: "..",
                            arguments: ["doctor"],
                            handler: _ =>
                            {
                                flowedHandlerCalled = true;
                                return new AdapterHostHandlerOutput(
                                    humanStdout: "should-not-run",
                                    protocolStdout: SuppressedProtocolPayload);
                            },
                            protocolStdout: new StringWriter(),
                            humanStdout: new StringWriter(),
                            diagnosticRouter: capture.DiagnosticRouter);
                        Assert.False(flowedHandlerCalled);
                        capture.DiagnosticRouter.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "flowed plain diagnostic"));
                        return flowedOutcome;
                    },
                    TestContext.Current.CancellationToken);

                throw new InvalidOperationException("first execution failed");
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: capture.DiagnosticRouter);

        releaseFlowedTask.Set();

        AdapterHostExecutionOutcome flowedOutcome = await Assert
            .IsType<Task<AdapterHostExecutionOutcome>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(AdapterHostExitCode.Fatal, firstOutcome.Result.ExitCode);
        Assert.Equal("UnhandledHostFailure", firstOutcome.Result.SafeDiagnosticCode);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, flowedOutcome.Result.ExitCode);
        Assert.Equal("InvocationBoundaryMismatch", flowedOutcome.Result.SafeDiagnosticCode);

        Assert.Collection(
            capture.RecordingSink.Events,
            firstEvent =>
            {
                Assert.Equal("Adapter host execution failed.", firstEvent.Message);
                Assert.Equal("UnhandledHostFailure", firstEvent.Properties["code"]);
            },
            secondEvent =>
            {
                Assert.Equal(
                    "Adapter host invocation boundary is unsupported.",
                    secondEvent.Message);
                Assert.Equal("InvocationBoundaryMismatch", secondEvent.Properties["code"]);
            },
            thirdEvent =>
            {
                Assert.Equal("flowed plain diagnostic", thirdEvent.Message);
                Assert.False(thirdEvent.IsSafeDiagnosticEnvelope);
            });

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Equal(3, diagnosticText.Split('\n').Length - 1);
        Assert.Contains(
            "Adapter host invocation boundary is unsupported.",
            diagnosticText,
            StringComparison.Ordinal);
        Assert.Contains("flowed plain diagnostic", diagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        ExecuteFlowedTaskInheritedDisposedScopeStillEmitsFallbackAndRestoresPlainRoutes()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);
        using var releaseFlowedTask = new ManualResetEventSlim(false);
        Task<AdapterHostExecutionOutcome>? flowedExecutionTask = null;

        AdapterHostExecutionOutcome firstOutcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                flowedExecutionTask = Task.Run(
                    () =>
                    {
                        if (!releaseFlowedTask.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the flowed execution.");
                        }

                        AdapterHostExecutionOutcome flowedOutcome = AdapterHostExecutor.Execute(
                            descriptor,
                            executablePath: "/usr/local/bin/azureauth-credprovider",
                            arguments: ["git", "credential-helper", "get"],
                            handler: static _ => throw new InvalidOperationException(
                                "flowed execution failed"),
                            protocolStdout: new StringWriter(),
                            humanStdout: new StringWriter(),
                            diagnosticRouter: capture.DiagnosticRouter);
                        capture.DiagnosticRouter.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "flowed plain diagnostic"));
                        return flowedOutcome;
                    },
                    TestContext.Current.CancellationToken);

                throw new InvalidOperationException("first execution failed");
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: capture.DiagnosticRouter);

        releaseFlowedTask.Set();

        AdapterHostExecutionOutcome flowedOutcome = await Assert
            .IsType<Task<AdapterHostExecutionOutcome>>(flowedExecutionTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(AdapterHostExitCode.Fatal, firstOutcome.Result.ExitCode);
        Assert.Equal("UnhandledHostFailure", firstOutcome.Result.SafeDiagnosticCode);
        Assert.Equal(AdapterHostExitCode.Fatal, flowedOutcome.Result.ExitCode);
        Assert.Equal("UnhandledHostFailure", flowedOutcome.Result.SafeDiagnosticCode);

        Assert.Collection(
            capture.RecordingSink.Events,
            firstEvent =>
            {
                Assert.Equal("Adapter host execution failed.", firstEvent.Message);
                Assert.Equal("UnhandledHostFailure", firstEvent.Properties["code"]);
            },
            secondEvent =>
            {
                Assert.Equal("Adapter host execution failed.", secondEvent.Message);
                Assert.Equal("UnhandledHostFailure", secondEvent.Properties["code"]);
            },
            thirdEvent =>
            {
                Assert.Equal("flowed plain diagnostic", thirdEvent.Message);
                Assert.False(thirdEvent.IsSafeDiagnosticEnvelope);
            });

        string diagnosticText = capture.DiagnosticText.ToString();
        Assert.Equal(3, diagnosticText.Split('\n').Length - 1);
        Assert.Contains("flowed plain diagnostic", diagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteCommittedNestedChildDiagnosticSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var sink = new CommittedRecordingDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter([sink], SecretRedactor.Empty);
        using var childDiagnosticCommitted = new ManualResetEventSlim(false);
        using var releaseChildExecution = new ManualResetEventSlim(false);
        Task<AdapterHostExecutionOutcome>? childExecutionTask = null;

        InvalidOperationException exception;
        try
        {
            exception = Assert.Throws<InvalidOperationException>(() =>
                AdapterHostExecutor.Execute(
                    descriptor,
                    executablePath: "/usr/local/bin/azureauth-credprovider",
                    arguments: ["doctor", "--json"],
                    handler: _ =>
                    {
                        childExecutionTask = Task.Run(
                            () => AdapterHostExecutor.Execute(
                                descriptor,
                                executablePath: "/usr/local/bin/azureauth-credprovider",
                                arguments: ["doctor", "--json"],
                                handler: _ =>
                                {
                                    diagnosticRouter.Route(new DiagnosticEvent(
                                        DiagnosticSeverity.Warning,
                                        DiagnosticChannel.Diagnostic,
                                        "committed nested child diagnostic"));
                                    childDiagnosticCommitted.Set();
                                    if (!releaseChildExecution.Wait(TimeSpan.FromSeconds(10)))
                                    {
                                        throw new TimeoutException(
                                            "Timed out waiting to release the committed " +
                                            "nested child execution.");
                                    }

                                    return new AdapterHostHandlerOutput();
                                },
                                protocolStdout: new StringWriter(),
                                humanStdout: new StringWriter(),
                                diagnosticRouter: diagnosticRouter),
                            TestContext.Current.CancellationToken);
                        if (!childDiagnosticCommitted.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting for the committed nested child diagnostic.");
                        }

                        throw new InvalidOperationException(
                            "outer execution failed after committed nested child diagnostic");
                    },
                    protocolStdout: new StringWriter(),
                    humanStdout: new StringWriter(),
                    diagnosticRouter: diagnosticRouter));
        }
        finally
        {
            releaseChildExecution.Set();

            if (childExecutionTask is not null)
            {
                AdapterHostExecutionOutcome childOutcome = await childExecutionTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
                Assert.Equal(AdapterHostExitCode.Success, childOutcome.Result.ExitCode);
            }
        }

        Assert.Equal(
            "outer execution failed after committed nested child diagnostic",
            exception.Message);
        DiagnosticEvent diagnosticEvent = Assert.Single(sink.AttemptedEvents);
        Assert.Equal("committed nested child diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task ExecuteInFlightNestedChildDiagnosticSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var sink = new BlockingCommitTrackingDiagnosticSink(
            "in-flight nested child diagnostic");
        var diagnosticRouter = new DiagnosticRouter([sink], SecretRedactor.Empty);
        Task<AdapterHostExecutionOutcome>? childExecutionTask = null;

        InvalidOperationException exception;
        try
        {
            exception = Assert.Throws<InvalidOperationException>(() =>
                AdapterHostExecutor.Execute(
                    descriptor,
                    executablePath: "/usr/local/bin/azureauth-credprovider",
                    arguments: ["doctor", "--json"],
                    handler: _ =>
                    {
                        childExecutionTask = Task.Run(
                            () => AdapterHostExecutor.Execute(
                                descriptor,
                                executablePath: "/usr/local/bin/azureauth-credprovider",
                                arguments: ["doctor", "--json"],
                                handler: _ =>
                                {
                                    diagnosticRouter.Route(new DiagnosticEvent(
                                        DiagnosticSeverity.Warning,
                                        DiagnosticChannel.Diagnostic,
                                        "in-flight nested child diagnostic"));
                                    return new AdapterHostHandlerOutput();
                                },
                                protocolStdout: new StringWriter(),
                                humanStdout: new StringWriter(),
                                diagnosticRouter: diagnosticRouter),
                            TestContext.Current.CancellationToken);
                        sink.WaitForBlockedWriteEntered();
                        throw new InvalidOperationException(
                            "outer execution failed after in-flight nested child diagnostic");
                    },
                    protocolStdout: new StringWriter(),
                    humanStdout: new StringWriter(),
                    diagnosticRouter: diagnosticRouter));
        }
        finally
        {
            sink.ReleaseBlockedWrite();

            if (childExecutionTask is not null)
            {
                AdapterHostExecutionOutcome childOutcome = await childExecutionTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
                Assert.Equal(AdapterHostExitCode.Success, childOutcome.Result.ExitCode);
            }
        }

        Assert.Equal(
            "outer execution failed after in-flight nested child diagnostic",
            exception.Message);
        DiagnosticEvent diagnosticEvent = Assert.Single(sink.AttemptedEvents);
        Assert.Equal("in-flight nested child diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task
        ExecuteInFlightSingleSinkChildRouteSuppressesUnhandledHostFailureFallbackAndLateRoute()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var sink = new BlockingCommitTrackingDiagnosticSink("in-flight child diagnostic");
        var diagnosticRouter = new DiagnosticRouter([sink], SecretRedactor.Empty);
        using var releaseLateRoute = new ManualResetEventSlim(false);
        Task? inFlightChildTask = null;
        Task? lateChildTask = null;
        AdapterHostExecutionOutcome? outcome = null;
        Exception? exception = null;

        try
        {
            exception = Record.Exception(() => outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    inFlightChildTask = Task.Run(
                        () => diagnosticRouter.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "in-flight child diagnostic")),
                        TestContext.Current.CancellationToken);
                    lateChildTask = Task.Run(
                        () =>
                        {
                            if (!releaseLateRoute.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException(
                                    "Timed out waiting to release the late child diagnostic.");
                            }

                            diagnosticRouter.Route(new DiagnosticEvent(
                                DiagnosticSeverity.Warning,
                                DiagnosticChannel.Diagnostic,
                                "late child diagnostic"));
                        },
                        TestContext.Current.CancellationToken);
                    sink.WaitForBlockedWriteEntered();
                    throw new InvalidOperationException(
                        "handler failed after in-flight child route entered");
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter));
        }
        finally
        {
            releaseLateRoute.Set();
            sink.ReleaseBlockedWrite();

            if (inFlightChildTask is not null)
            {
                await inFlightChildTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            }

            if (lateChildTask is not null)
            {
                await lateChildTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            }
        }

        InvalidOperationException invalidOperationException =
            Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            "handler failed after in-flight child route entered",
            invalidOperationException.Message);
        Assert.Null(outcome);

        DiagnosticEvent diagnosticEvent = Assert.Single(sink.AttemptedEvents);
        Assert.Equal("in-flight child diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task ExecuteInFlightMultiSinkChildRouteSuppressesUnhandledHostFailureFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var firstSink = new CommittedRecordingDiagnosticSink();
        using var secondSink = new BlockingCommitTrackingDiagnosticSink(
            "multi-sink child diagnostic");
        var diagnosticRouter = new DiagnosticRouter(
            [firstSink, secondSink],
            SecretRedactor.Empty);
        Task? childTask = null;
        AdapterHostExecutionOutcome? outcome = null;
        Exception? exception = null;

        try
        {
            exception = Record.Exception(() => outcome = AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    childTask = Task.Run(
                        () => diagnosticRouter.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "multi-sink child diagnostic")),
                        TestContext.Current.CancellationToken);
                    secondSink.WaitForBlockedWriteEntered();
                    throw new InvalidOperationException(
                        "handler failed after multi-sink child route entered");
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter));
        }
        finally
        {
            secondSink.ReleaseBlockedWrite();

            if (childTask is not null)
            {
                await childTask.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            }
        }

        InvalidOperationException invalidOperationException =
            Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            "handler failed after multi-sink child route entered",
            invalidOperationException.Message);
        Assert.Null(outcome);

        DiagnosticEvent committedEvent = Assert.Single(firstSink.AttemptedEvents);
        Assert.Equal("multi-sink child diagnostic", committedEvent.Message);

        DiagnosticEvent blockedEvent = Assert.Single(secondSink.AttemptedEvents);
        Assert.Equal("multi-sink child diagnostic", blockedEvent.Message);
    }

    [Fact]
    public void ExecuteProtocolSkipsNonTrackableHumanStdoutSinkValidation()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var diagnosticSink = new RecordingDiagnosticSink();
        var humanStdoutSink = new NonTrackableDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [diagnosticSink],
            SecretRedactor.Empty,
            [humanStdoutSink]);
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var handlerCalled = false;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                handlerCalled = true;
                return new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: GitProtocolPayload,
                    humanStdout: "should-not-write-human-output");
            },
            protocolStdout: protocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: diagnosticRouter);

        Assert.True(handlerCalled);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(GitProtocolPayload, protocolStdout.ToString());
        Assert.Equal(string.Empty, humanStdout.ToString());
        Assert.Equal(0, humanStdoutSink.WriteCallCount);
        Assert.Empty(diagnosticSink.Events);
    }

    [Fact]
    public void ExecuteHumanCommandSkipsNonTrackableHumanStdoutSinkValidation()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var diagnosticSink = new RecordingDiagnosticSink();
        var humanStdoutSink = new NonTrackableDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter(
            [diagnosticSink],
            SecretRedactor.Empty,
            [humanStdoutSink]);
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var handlerCalled = false;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: _ =>
            {
                handlerCalled = true;
                return new AdapterHostHandlerOutput(
                    humanStdout: "doctor ok",
                    protocolStdout: SuppressedProtocolPayload);
            },
            protocolStdout: protocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: diagnosticRouter);

        Assert.True(handlerCalled);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal(string.Empty, protocolStdout.ToString());
        Assert.Equal("doctor ok", humanStdout.ToString());
        Assert.Equal(0, humanStdoutSink.WriteCallCount);
        Assert.Empty(diagnosticSink.Events);
    }

    [Fact]
    public void ExecuteRejectsNonTrackableDiagnosticSinkBeforeHandlerExecution()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var sink = new NonTrackableDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter([sink], SecretRedactor.Empty);
        var handlerCalled = false;

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["git", "credential-helper", "get"],
            handler: _ =>
            {
                handlerCalled = true;
                return new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: GitProtocolPayload);
            },
            protocolStdout: new StringWriter(),
            humanStdout: new StringWriter(),
            diagnosticRouter: diagnosticRouter);

        Assert.False(handlerCalled);
        Assert.Equal(0, sink.WriteCallCount);
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.True(outcome.Result.WriteDiagnosticStderr);
        Assert.Equal("UnhandledHostFailure", outcome.Result.SafeDiagnosticCode);
    }

    [Fact]
    public async Task ExecuteOverlappingSharedRouterScopesDoNotCrossContaminateCommittedOutput()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var sink = new OverlappingExecutionDiagnosticSink();
        var diagnosticRouter = new DiagnosticRouter([sink], SecretRedactor.Empty);

        Task<AdapterHostExecutionOutcome> firstExecutionTask = Task.Run(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    sink.SignalFirstHandlerEntered();
                    sink.WaitForSecondHandlerEntered();
                    sink.WaitForFirstHandlerRelease();
                    return new AdapterHostHandlerOutput(
                        credentialResult: CreateUnauthorizedCredentialResult());
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter));

        sink.WaitForFirstHandlerEntered();

        Task<AdapterHostExecutionOutcome> secondExecutionTask = Task.Run(() =>
            AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: _ =>
                {
                    sink.SignalSecondHandlerEntered();
                    sink.WaitForSecondHandlerRelease();
                    return new AdapterHostHandlerOutput(
                        credentialResult: CreateUnauthorizedCredentialResult());
                },
                protocolStdout: new StringWriter(),
                humanStdout: new StringWriter(),
                diagnosticRouter: diagnosticRouter));

        sink.WaitForSecondHandlerEntered();
        sink.ReleaseFirstHandler();
        AdapterHostExecutionOutcome firstOutcome = await firstExecutionTask;
        sink.ReleaseSecondHandler();
        AdapterHostExecutionOutcome secondOutcome = await secondExecutionTask;

        Assert.Equal(AdapterHostExitCode.Unauthorized, firstOutcome.Result.ExitCode);
        Assert.Equal("Unauthorized", firstOutcome.Result.SafeDiagnosticCode);
        Assert.Equal(AdapterHostExitCode.Fatal, secondOutcome.Result.ExitCode);
        Assert.Equal("UnhandledHostFailure", secondOutcome.Result.SafeDiagnosticCode);

        DiagnosticEvent[] attemptedEvents = sink.AttemptedEvents;
        Assert.Equal(3, attemptedEvents.Length);
        Assert.Equal("Authorization is required.", attemptedEvents[0].Message);
        Assert.Equal("Authorization is required.", attemptedEvents[1].Message);
        Assert.Equal("Adapter host execution failed.", attemptedEvents[2].Message);
        Assert.Equal("UnhandledHostFailure", attemptedEvents[2].Properties["code"]);
    }

    [Fact]
    public void ExecuteHumanStdoutCommitFailurePropagatesWriteException()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var humanStdout = new PartialThrowingTextWriter(
            throwAfterCharacterCount: "doctor".Length,
            exceptionToThrow: new IOException("human stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: "doctor ok",
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("human stdout write failed", exception.Message);
        Assert.Equal("doctor", humanStdout.Written);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteHumanStdoutBmpAppendThenThrowFailurePropagatesWriteException()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var humanStdout = new AppendThenThrowStringTextWriter(
            new IOException("human stdout write failed"));
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: "doctor ok",
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("human stdout write failed", exception.Message);
        Assert.Equal("d", humanStdout.Written);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public void ExecuteHumanStdoutStandardConsolePartialWriteDoesNotEmitFallback()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        using var stream = new PartiallyWritingThrowingStream(
            bytesToWriteBeforeThrow: 1,
            new IOException("human stdout write failed"));
        TextWriter humanStdout = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        var capture = new OutputCapture(includeDiagnosticTextWriter: true);

        IOException exception = Assert.Throws<IOException>(() => AdapterHostExecutor.Execute(
            descriptor,
            executablePath: "/usr/local/bin/azureauth-credprovider",
            arguments: ["doctor", "--json"],
            handler: static _ => new AdapterHostHandlerOutput(
                humanStdout: "doctor ok",
                protocolStdout: SuppressedProtocolPayload),
            protocolStdout: capture.ProtocolStdout,
            humanStdout: humanStdout,
            diagnosticRouter: capture.DiagnosticRouter));

        Assert.Equal("human stdout write failed", exception.Message);
        Assert.Equal(1, stream.WrittenByteCount);
        Assert.Equal(string.Empty, capture.ProtocolStdout.ToString());
        Assert.Equal(string.Empty, capture.DiagnosticText.ToString());
        Assert.Empty(capture.RecordingSink.Events);
    }

    [Fact]
    public async Task ExecuteConcurrentHumanCommandsSharingWriterProduceOnlyWholePayloadOrderings()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var humanStdout = new CoordinatedProgressAwareTextWriter();
        const string firstPayload = "doctor alpha 🚀\n";
        const string secondPayload = "doctor beta 🛰️\n";
        using var executionsReady = new CountdownEvent(2);
        using var releaseExecutions = new ManualResetEventSlim(false);

        Task<AdapterHostExecutionOutcome> firstExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: static _ => new AdapterHostHandlerOutput(
                    humanStdout: firstPayload,
                    protocolStdout: SuppressedProtocolPayload),
                protocolStdout: new StringWriter(),
                humanStdout: humanStdout,
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));
        Task<AdapterHostExecutionOutcome> secondExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: static _ => new AdapterHostHandlerOutput(
                    humanStdout: secondPayload,
                    protocolStdout: SuppressedProtocolPayload),
                protocolStdout: new StringWriter(),
                humanStdout: humanStdout,
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));

        Assert.True(
            executionsReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseExecutions.Set();
        ReleasePendingWritesUntilCompleted(
            humanStdout,
            firstExecutionTask,
            secondExecutionTask);

        AdapterHostExecutionOutcome[] outcomes = await Task.WhenAll(
            firstExecutionTask,
            secondExecutionTask);
        Assert.All(
            outcomes,
            static outcome =>
            {
                Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
                Assert.False(outcome.Result.WriteProtocolStdout);
                Assert.False(outcome.Result.WriteDiagnosticStderr);
            });
        AssertSerializedPayloadOrdering(humanStdout.Written, firstPayload, secondPayload);
        AssertWritesContainNoIsolatedSurrogates(humanStdout.Writes);
    }

    [Fact]
    public async Task
        ExecuteConcurrentProtocolInvocationsSharingWriterProduceOnlyWholePayloadOrderings()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var protocolStdout = new CoordinatedProgressAwareTextWriter();
        const string firstPayload = "username=alpha🚀\npassword=one🧪\n";
        const string secondPayload = "username=beta🛰️\npassword=two🪐\n";
        using var executionsReady = new CountdownEvent(2);
        using var releaseExecutions = new ManualResetEventSlim(false);

        Task<AdapterHostExecutionOutcome> firstExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: firstPayload),
                protocolStdout: protocolStdout,
                humanStdout: new StringWriter(),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));
        Task<AdapterHostExecutionOutcome> secondExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: secondPayload),
                protocolStdout: protocolStdout,
                humanStdout: new StringWriter(),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));

        Assert.True(
            executionsReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseExecutions.Set();
        ReleasePendingWritesUntilCompleted(
            protocolStdout,
            firstExecutionTask,
            secondExecutionTask);

        AdapterHostExecutionOutcome[] outcomes = await Task.WhenAll(
            firstExecutionTask,
            secondExecutionTask);
        Assert.All(
            outcomes,
            static outcome =>
            {
                Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
                Assert.True(outcome.Result.WriteProtocolStdout);
                Assert.False(outcome.Result.WriteDiagnosticStderr);
            });
        AssertSerializedPayloadOrdering(protocolStdout.Written, firstPayload, secondPayload);
        AssertWritesContainNoIsolatedSurrogates(protocolStdout.Writes);
    }

    [Fact]
    public async Task
        ExecuteConcurrentHumanCommandsAcrossDistinctStringBuilderWritersSerializePayloads()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var sharedBuilder = new StringBuilder();
        var writeCoordinator = new CoordinatedSharedStringWriterCoordinator();
        const string firstPayload = "doctor alpha\n";
        const string secondPayload = "doctor beta\n";
        using var executionsReady = new CountdownEvent(2);
        using var releaseExecutions = new ManualResetEventSlim(false);

        Task<AdapterHostExecutionOutcome> firstExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: static _ => new AdapterHostHandlerOutput(
                    humanStdout: firstPayload,
                    protocolStdout: SuppressedProtocolPayload),
                protocolStdout: new StringWriter(),
                humanStdout: new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));
        Task<AdapterHostExecutionOutcome> secondExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["doctor", "--json"],
                handler: static _ => new AdapterHostHandlerOutput(
                    humanStdout: secondPayload,
                    protocolStdout: SuppressedProtocolPayload),
                protocolStdout: new StringWriter(),
                humanStdout: new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));

        Assert.True(
            executionsReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseExecutions.Set();
        Assert.True(writeCoordinator.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = writeCoordinator.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));
        ReleasePendingWritesUntilCompleted(
            writeCoordinator,
            firstExecutionTask,
            secondExecutionTask);

        AdapterHostExecutionOutcome[] outcomes = await Task.WhenAll(
            firstExecutionTask,
            secondExecutionTask);
        Assert.All(
            outcomes,
            static outcome =>
            {
                Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
                Assert.False(outcome.Result.WriteProtocolStdout);
                Assert.False(outcome.Result.WriteDiagnosticStderr);
            });
        Assert.False(
            observedTwoPendingThreads,
            "Distinct StringWriter wrappers sharing one StringBuilder should share one "
                + "builder-scoped lock.");
        AssertSerializedPayloadOrdering(sharedBuilder.ToString(), firstPayload, secondPayload);
    }

    [Fact]
    public async Task
        ExecuteConcurrentProtocolInvocationsAcrossDistinctStringBuilderWritersSerializePayloads()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var sharedBuilder = new StringBuilder();
        var writeCoordinator = new CoordinatedSharedStringWriterCoordinator();
        const string firstPayload = "username=alpha\npassword=one\n";
        const string secondPayload = "username=beta\npassword=two\n";
        using var executionsReady = new CountdownEvent(2);
        using var releaseExecutions = new ManualResetEventSlim(false);

        Task<AdapterHostExecutionOutcome> firstExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: firstPayload),
                protocolStdout: new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator),
                humanStdout: new StringWriter(),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));
        Task<AdapterHostExecutionOutcome> secondExecutionTask = StartCoordinatedExecution(
            executionsReady,
            releaseExecutions,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: secondPayload),
                protocolStdout: new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator),
                humanStdout: new StringWriter(),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));

        Assert.True(
            executionsReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseExecutions.Set();
        Assert.True(writeCoordinator.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = writeCoordinator.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));
        ReleasePendingWritesUntilCompleted(
            writeCoordinator,
            firstExecutionTask,
            secondExecutionTask);

        AdapterHostExecutionOutcome[] outcomes = await Task.WhenAll(
            firstExecutionTask,
            secondExecutionTask);
        Assert.All(
            outcomes,
            static outcome =>
            {
                Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
                Assert.True(outcome.Result.WriteProtocolStdout);
                Assert.False(outcome.Result.WriteDiagnosticStderr);
            });
        Assert.False(
            observedTwoPendingThreads,
            "Distinct StringWriter wrappers sharing one StringBuilder should share one "
                + "builder-scoped lock.");
        AssertSerializedPayloadOrdering(sharedBuilder.ToString(), firstPayload, secondPayload);
    }

    [Fact]
    public async Task
        ExecuteProtocolInvocationSerializesWithExternalWritesThroughSameSynchronizedStringWriter()
    {
        AdapterDescriptor descriptor = CreateSharedGitDescriptor();
        var sharedBuilder = new StringBuilder();
        var writeCoordinator = new CoordinatedSharedStringWriterCoordinator();
        TextWriter protocolStdout = TextWriter.Synchronized(
            new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator));
        const string protocolPayload = "username=alpha\npassword=one\n";
        const string externalPayload = "external note\n";
        using var operationsReady = new CountdownEvent(2);
        using var releaseOperations = new ManualResetEventSlim(false);

        Task<AdapterHostExecutionOutcome> executionTask = StartCoordinatedExecution(
            operationsReady,
            releaseOperations,
            () => AdapterHostExecutor.Execute(
                descriptor,
                executablePath: "/usr/local/bin/azureauth-credprovider",
                arguments: ["git", "credential-helper", "get"],
                handler: static _ => new AdapterHostHandlerOutput(
                    credentialResult: CreateSuccessCredentialResult(),
                    protocolStdout: protocolPayload),
                protocolStdout: protocolStdout,
                humanStdout: new StringWriter(),
                diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)));
        Task externalWriteTask = Task.Factory.StartNew(
            () =>
            {
                operationsReady.Signal();
                if (!releaseOperations.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release the same-wrapper external write.");
                }

                protocolStdout.Write(externalPayload);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Assert.True(
            operationsReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseOperations.Set();
        Assert.True(writeCoordinator.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = writeCoordinator.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));
        ReleasePendingWritesUntilCompleted(writeCoordinator, executionTask, externalWriteTask);

        AdapterHostExecutionOutcome outcome = await executionTask;
        await externalWriteTask;

        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.False(outcome.Result.WriteDiagnosticStderr);
        Assert.False(
            observedTwoPendingThreads,
            "The same synchronized StringWriter instance should keep its wrapper monitor while "
                + "adapter host output bypasses the wrapper.");
        AssertSerializedPayloadOrdering(sharedBuilder.ToString(), protocolPayload, externalPayload);
    }

    private static AdapterDescriptor CreateSharedGitDescriptor()
    {
        AdapterEntrypointDescriptor protocolEntrypoint = new(
            "GitCredentialHelper",
            AdapterInvocationMode.Protocol,
            executableNames: ["azureauth-credprovider"],
            argumentTokens: ["git", "credential-helper"],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix);
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: ["azureauth-credprovider"]);

        return new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            [protocolEntrypoint, humanEntrypoint]);
    }

    private static CredentialRequest CreateCredentialCoreGitRequest(
        IdentityFlow flow = IdentityFlow.DeviceCode,
        CredentialKind kind = CredentialKind.BasicPassword,
        InteractivePolicy interactivePolicy = InteractivePolicy.UserAllowed,
        CachePolicyMode cachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        CiContext? ciContext = null)
    {
        return new CredentialRequest
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org")),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = kind,
            IdentityFlow = flow,
            InteractivePolicy = interactivePolicy,
            CachePolicy = cachePolicy,
            CiContext = ciContext
                ?? new CiContext
                {
                    ExplicitCiMode = false,
                    AllowsPersistentWrites = false,
                },
        };
    }

    private static CredentialRequest CreateCredentialCoreAzurePipelinesSystemAccessTokenRequest(
        CachePolicyMode cachePolicy,
        CiContext? ciContext)
    {
        CredentialRequest request = CreateCredentialCoreGitRequest(
            flow: IdentityFlow.AzurePipelinesSystemAccessToken,
            kind: CredentialKind.BearerToken,
            interactivePolicy: InteractivePolicy.Never,
            cachePolicy: cachePolicy,
            ciContext: ciContext);
        return ciContext is null ? request with { CiContext = null } : request;
    }

    private static string CreateGitCredentialHelperProtocolPayload(CredentialResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Assert.True(
            AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
                result,
                out string? username,
                out string? password));
        return $"username={username}\npassword={password}\n";
    }

    private static SecretRedactor CreateBlankingRedactor(params string[] valuesToBlank)
    {
        var secrets = new List<string?> { SecretRedactor.DefaultMask };
        foreach (string value in valuesToBlank)
        {
            secrets.Add(value);
        }

        return new SecretRedactor(secrets);
    }

    private static CredentialResult CreateSuccessCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "password",
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
        };
    }

    private static CredentialResult CreateTrustedCredentialCoreFailureResult(
        CredentialResultStatus status,
        CredentialErrorKind errorKind,
        string code,
        string safeMessage)
    {
        return new CredentialResult
        {
            Status = status,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = errorKind,
                Code = code,
                SafeMessage = safeMessage,
            },
        };
    }

    private static CredentialResult CreateNoCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.NoCredential,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.UnsupportedHost,
                Code = "UnsupportedHost",
                SafeMessage = "No matching credential is available.",
            },
        };
    }

    private static CredentialResult CreateUnauthorizedCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Unauthorized,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.Unauthorized,
                Code = "Unauthorized",
                SafeMessage = "Authorization is required.",
            },
        };
    }

    private static CredentialError CreateProducerControlledCredentialError(
        CredentialErrorKind kind,
        string code = "ProducerControlledCode")
    {
        return new CredentialError
        {
            Kind = kind,
            Code = code,
            SafeMessage = "producer message code=Spoofed",
            SafeDetails = new Dictionary<string, string>
            {
                ["detail"] = "producer detail code=Spoofed",
            },
        };
    }

    private static CredentialResult CreateFatalCredentialResult(
        string safeMessage = "Credential acquisition failed safely.",
        IReadOnlyDictionary<string, string>? safeDetails = null,
        string code = "Fatal")
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Fatal,
            DiagnosticsCorrelationId = DiagnosticsCorrelationId,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.Fatal,
                Code = code,
                SafeMessage = safeMessage,
                SafeDetails = safeDetails ?? new Dictionary<string, string>(),
            },
        };
    }

    private static int GetProtocolViolationSafeDetailsInspectionCap()
    {
        FieldInfo? field = typeof(AdapterHostExecutor).GetField(
            "MaxSafeDiagnosticPropertyInspectionCount",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<int>(field!.GetRawConstantValue());
    }

    private sealed class UnsafeSafeDetailsDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly IReadOnlyDictionary<string, string?> _values;

        public UnsafeSafeDetailsDictionary(IReadOnlyDictionary<string, string?> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _values = values;
        }

        public string this[string key] => _values[key]!;

        public IEnumerable<string> Keys => _values.Keys;

        public IEnumerable<string> Values => GetValues();

        public int Count => _values.Count;

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            foreach (KeyValuePair<string, string?> pair in _values)
            {
                yield return new KeyValuePair<string, string>(pair.Key, pair.Value!);
            }
        }

        public bool TryGetValue(string key, out string value)
        {
            bool found = _values.TryGetValue(key, out string? rawValue);
            value = rawValue!;
            return found;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<string> GetValues()
        {
            foreach (string? value in _values.Values)
            {
                yield return value!;
            }
        }
    }

    private sealed class CountingSafeDetailsDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly KeyValuePair<string, string>[] _values;

        public CountingSafeDetailsDictionary(IEnumerable<KeyValuePair<string, string>> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            _values = [.. values];
        }

        public int EnumeratedCount { get; private set; }

        public string this[string key] =>
            TryGetValue(key, out string value) ? value : throw new KeyNotFoundException(key);

        public IEnumerable<string> Keys => GetKeys();

        public IEnumerable<string> Values => GetValues();

        public int Count => _values.Length;

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                EnumeratedCount++;
                yield return pair;
            }
        }

        public bool TryGetValue(string key, out string value)
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<string> GetKeys()
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                yield return pair.Key;
            }
        }

        private IEnumerable<string> GetValues()
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                yield return pair.Value;
            }
        }
    }

    private sealed class ThrowingSafeDetailsDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly Exception _exceptionToThrow;
        private readonly KeyValuePair<string, string>[] _values;
        private readonly int _throwAfterYieldCount;

        public ThrowingSafeDetailsDictionary(
            IEnumerable<KeyValuePair<string, string>> values,
            int throwAfterYieldCount,
            Exception? exceptionToThrow = null)
        {
            ArgumentNullException.ThrowIfNull(values);
            ArgumentOutOfRangeException.ThrowIfNegative(throwAfterYieldCount);

            _exceptionToThrow = exceptionToThrow ?? new InvalidOperationException(
                "Safe details enumeration failed.");
            _throwAfterYieldCount = throwAfterYieldCount;
            var copiedValues = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> pair in values)
            {
                copiedValues.Add(pair);
            }

            _values = copiedValues.ToArray();
        }

        public string this[string key] =>
            TryGetValue(key, out string value) ? value : throw new KeyNotFoundException(key);

        public IEnumerable<string> Keys => GetKeys();

        public IEnumerable<string> Values => GetValues();

        public int Count => _values.Length;

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            for (int index = 0; index < _values.Length; index++)
            {
                if (index == _throwAfterYieldCount)
                {
                    throw _exceptionToThrow;
                }

                yield return _values[index];
            }
        }

        public bool TryGetValue(string key, out string value)
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<string> GetKeys()
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                yield return pair.Key;
            }
        }

        private IEnumerable<string> GetValues()
        {
            foreach (KeyValuePair<string, string> pair in _values)
            {
                yield return pair.Value;
            }
        }
    }

    private sealed class LookupThrowingSafeDetailsDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly Exception _exceptionToThrow;
        private readonly IReadOnlyDictionary<string, string> _values;

        public LookupThrowingSafeDetailsDictionary(
            IReadOnlyDictionary<string, string> values,
            Exception? exceptionToThrow = null)
        {
            ArgumentNullException.ThrowIfNull(values);

            _values = values;
            _exceptionToThrow = exceptionToThrow ?? new InvalidOperationException(
                "Safe details lookup failed.");
        }

        public string this[string key] => throw _exceptionToThrow;

        public IEnumerable<string> Keys => _values.Keys;

        public IEnumerable<string> Values => _values.Values;

        public int Count => _values.Count;

        public bool ContainsKey(string key) => throw _exceptionToThrow;

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

        public bool TryGetValue(string key, out string value)
        {
            value = string.Empty;
            throw _exceptionToThrow;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class OutputCapture
    {
        public OutputCapture(
            bool includeDiagnosticTextWriter,
            SecretRedactor? redactor = null)
        {
            var sinks = new List<IDiagnosticSink> { RecordingSink };
            if (includeDiagnosticTextWriter)
            {
                sinks.Add(new TextWriterDiagnosticSink(DiagnosticText));
            }

            DiagnosticRouter = new DiagnosticRouter(sinks, redactor ?? SecretRedactor.Empty);
        }

        public StringWriter ProtocolStdout { get; } = new();

        public StringWriter HumanStdout { get; } = new();

        public StringWriter DiagnosticText { get; } = new();

        public RecordingDiagnosticSink RecordingSink { get; } = new();

        public DiagnosticRouter DiagnosticRouter { get; }
    }

    private sealed class RecordingDiagnosticSink : ICommitTrackingDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            Write(diagnosticEvent);
            return false;
        }
    }

    private sealed class CommittedRecordingDiagnosticSink : ICommitTrackingDiagnosticSink
    {
        private readonly List<DiagnosticEvent> _attemptedEvents = [];

        public DiagnosticEvent[] AttemptedEvents
        {
            get
            {
                lock (_attemptedEvents)
                {
                    return _attemptedEvents.ToArray();
                }
            }
        }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw new NotSupportedException("Commit tracking is required.");
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            lock (_attemptedEvents)
            {
                _attemptedEvents.Add(diagnosticEvent);
            }

            return true;
        }
    }

    private sealed class BlockingCommitTrackingDiagnosticSink
        : ICommitTrackingDiagnosticSink, IDisposable
    {
        private readonly string _blockedMessage;
        private readonly List<DiagnosticEvent> _attemptedEvents = [];
        private readonly ManualResetEventSlim _blockedWriteEntered = new(false);
        private readonly ManualResetEventSlim _releaseBlockedWrite = new(false);

        public BlockingCommitTrackingDiagnosticSink(string blockedMessage)
        {
            _blockedMessage = blockedMessage;
        }

        public DiagnosticEvent[] AttemptedEvents
        {
            get
            {
                lock (_attemptedEvents)
                {
                    return _attemptedEvents.ToArray();
                }
            }
        }

        public void WaitForBlockedWriteEntered()
        {
            if (!_blockedWriteEntered.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out waiting for the in-flight child diagnostic route.");
            }
        }

        public void ReleaseBlockedWrite()
        {
            _releaseBlockedWrite.Set();
        }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw new NotSupportedException("Commit tracking is required.");
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            lock (_attemptedEvents)
            {
                _attemptedEvents.Add(diagnosticEvent);
            }

            if (string.Equals(
                diagnosticEvent.Message,
                _blockedMessage,
                StringComparison.Ordinal))
            {
                _blockedWriteEntered.Set();
                if (!_releaseBlockedWrite.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out releasing the in-flight child diagnostic route.");
                }
            }

            return false;
        }

        public void Dispose()
        {
            _blockedWriteEntered.Dispose();
            _releaseBlockedWrite.Dispose();
        }
    }

    private sealed class ThrowingDiagnosticSink : ICommitTrackingDiagnosticSink
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingDiagnosticSink(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw _exceptionToThrow;
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            throw _exceptionToThrow;
        }
    }

    private sealed class OverlappingExecutionDiagnosticSink
        : ICommitTrackingDiagnosticSink, IDisposable
    {
        private readonly ManualResetEventSlim _firstHandlerEntered = new(false);
        private readonly ManualResetEventSlim _secondHandlerEntered = new(false);
        private readonly ManualResetEventSlim _releaseFirstHandler = new(false);
        private readonly ManualResetEventSlim _releaseSecondHandler = new(false);
        private readonly List<DiagnosticEvent> _attemptedEvents = [];
        private int _writeCallCount;

        public DiagnosticEvent[] AttemptedEvents
        {
            get
            {
                lock (_attemptedEvents)
                {
                    return _attemptedEvents.ToArray();
                }
            }
        }

        public void SignalFirstHandlerEntered()
        {
            _firstHandlerEntered.Set();
        }

        public void WaitForFirstHandlerEntered()
        {
            Wait(_firstHandlerEntered);
        }

        public void SignalSecondHandlerEntered()
        {
            _secondHandlerEntered.Set();
        }

        public void WaitForSecondHandlerEntered()
        {
            Wait(_secondHandlerEntered);
        }

        public void ReleaseFirstHandler()
        {
            _releaseFirstHandler.Set();
        }

        public void WaitForFirstHandlerRelease()
        {
            Wait(_releaseFirstHandler);
        }

        public void ReleaseSecondHandler()
        {
            _releaseSecondHandler.Set();
        }

        public void WaitForSecondHandlerRelease()
        {
            Wait(_releaseSecondHandler);
        }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw new NotSupportedException("Commit tracking is required.");
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            lock (_attemptedEvents)
            {
                _attemptedEvents.Add(diagnosticEvent);
            }

            return Interlocked.Increment(ref _writeCallCount) switch
            {
                1 => true,
                2 => throw new IOException("diagnostic stderr write failed"),
                3 => true,
                _ => throw new InvalidOperationException("Unexpected diagnostic write."),
            };
        }

        public void Dispose()
        {
            _firstHandlerEntered.Dispose();
            _secondHandlerEntered.Dispose();
            _releaseFirstHandler.Dispose();
            _releaseSecondHandler.Dispose();
        }

        private static void Wait(ManualResetEventSlim gate)
        {
            if (!gate.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out waiting for overlapping execution test coordination.");
            }
        }
    }

    private sealed class CoordinatedMixedCommitTrackingDiagnosticSink
        : ICommitTrackingDiagnosticSink, IDisposable
    {
        internal const string CommittedMessage = "committed child diagnostic";

        private readonly List<DiagnosticEvent> _attemptedEvents = [];
        private readonly CountdownEvent _pendingWrites;
        private readonly int _expectedWriteCount;
        private readonly ManualResetEventSlim _releasePendingWrites = new(false);
        private int _writeCount;

        public CoordinatedMixedCommitTrackingDiagnosticSink(int expectedWriteCount)
        {
            _expectedWriteCount = expectedWriteCount;
            _pendingWrites = new CountdownEvent(expectedWriteCount);
        }

        public DiagnosticEvent[] AttemptedEvents
        {
            get
            {
                lock (_attemptedEvents)
                {
                    return _attemptedEvents.ToArray();
                }
            }
        }

        public Task[] StartConcurrentRoutes(DiagnosticRouter diagnosticRouter)
        {
            ArgumentNullException.ThrowIfNull(diagnosticRouter);

            var routeTasks = new Task[_expectedWriteCount];
            routeTasks[0] = StartRouteTask(
                diagnosticRouter,
                CreateDiagnosticEvent(CommittedMessage));
            for (var index = 1; index < routeTasks.Length; index++)
            {
                routeTasks[index] = StartRouteTask(
                    diagnosticRouter,
                    CreateDiagnosticEvent($"zero-byte child diagnostic {index}"));
            }

            return routeTasks;
        }

        public void WaitForPendingWrites()
        {
            if (!_pendingWrites.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out waiting for concurrent child-task diagnostics.");
            }
        }

        public void ReleasePendingWrites()
        {
            _releasePendingWrites.Set();
        }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw new NotSupportedException("Commit tracking is required.");
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            lock (_attemptedEvents)
            {
                _attemptedEvents.Add(diagnosticEvent);
            }

            int callIndex = Interlocked.Increment(ref _writeCount);
            if (callIndex <= _expectedWriteCount)
            {
                _pendingWrites.Signal();
                if (!_releasePendingWrites.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out releasing concurrent child-task diagnostics.");
                }
            }

            return string.Equals(
                diagnosticEvent.Message,
                CommittedMessage,
                StringComparison.Ordinal);
        }

        public void Dispose()
        {
            _pendingWrites.Dispose();
            _releasePendingWrites.Dispose();
        }

        private static DiagnosticEvent CreateDiagnosticEvent(string message)
        {
            return new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.Diagnostic,
                message);
        }

        private static Task StartRouteTask(
            DiagnosticRouter diagnosticRouter,
            DiagnosticEvent diagnosticEvent)
        {
            return Task.Factory.StartNew(
                () => diagnosticRouter.Route(diagnosticEvent),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    private sealed class NonTrackableDiagnosticSink : IDiagnosticSink
    {
        public int WriteCallCount { get; private set; }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            WriteCallCount++;
        }
    }

    private static void AssertWritesContainNoIsolatedSurrogates(IEnumerable<string> writes)
    {
        foreach (string write in writes)
        {
            for (var index = 0; index < write.Length; index++)
            {
                if (char.IsHighSurrogate(write[index]))
                {
                    Assert.True(
                        index + 1 < write.Length && char.IsLowSurrogate(write[index + 1]),
                        $"Write chunk contained an isolated high surrogate: '{write}'.");
                    index++;
                    continue;
                }

                Assert.False(
                    char.IsLowSurrogate(write[index]),
                    $"Write chunk contained an isolated low surrogate: '{write}'.");
            }
        }
    }

    private static Encoding? TryCreateUtf7Encoding()
    {
        Type? utf7EncodingType = typeof(Encoding).Assembly.GetType(
            "System.Text.UTF7Encoding",
            throwOnError: false);
        if (utf7EncodingType is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(utf7EncodingType) as Encoding;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (System.Reflection.TargetInvocationException ex)
            when (ex.InnerException is NotSupportedException)
        {
            return null;
        }
    }

    private static void AssertSerializedPayloadOrdering(
        string written,
        string firstPayload,
        string secondPayload)
    {
        Assert.True(
            string.Equals(written, firstPayload + secondPayload, StringComparison.Ordinal)
                || string.Equals(written, secondPayload + firstPayload, StringComparison.Ordinal),
            $"Unexpected output ordering: '{written}'.");
    }

    private static Task<AdapterHostExecutionOutcome> StartCoordinatedExecution(
        CountdownEvent executionsReady,
        ManualResetEventSlim releaseExecutions,
        Func<AdapterHostExecutionOutcome> execute)
    {
        ArgumentNullException.ThrowIfNull(executionsReady);
        ArgumentNullException.ThrowIfNull(releaseExecutions);
        ArgumentNullException.ThrowIfNull(execute);

        return Task.Factory.StartNew(
            () =>
            {
                executionsReady.Signal();
                if (!releaseExecutions.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release the coordinated adapter host execution.");
                }

                return execute();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void ReleasePendingWritesUntilCompleted(
        ICoordinatedPendingWriteSource writeSource,
        params Task[] tasks)
    {
        ArgumentNullException.ThrowIfNull(writeSource);
        ArgumentNullException.ThrowIfNull(tasks);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (tasks.Any(static task => !task.IsCompleted))
        {
            if (!writeSource.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    tasks.All(static task => task.IsCompleted) || DateTime.UtcNow < deadline,
                    "Timed out waiting for the next coordinated adapter host output chunk.");
                continue;
            }

            _ = writeSource.WaitForDistinctPendingThreadCount(
                minimumDistinctPendingThreadCount: 2,
                TimeSpan.FromMilliseconds(100));
            if (!writeSource.TryReleaseNextPendingWrite(lastReleasedThreadId, out int threadId))
            {
                continue;
            }

            lastReleasedThreadId = threadId;
        }
    }

    private static void ReleasePendingWritesUntilCompleted(
        CoordinatedSharedStringWriterCoordinator writeCoordinator,
        params Task[] tasks)
    {
        ArgumentNullException.ThrowIfNull(writeCoordinator);
        ArgumentNullException.ThrowIfNull(tasks);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (tasks.Any(static task => !task.IsCompleted))
        {
            if (!writeCoordinator.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    tasks.All(static task => task.IsCompleted) || DateTime.UtcNow < deadline,
                    "Timed out waiting for the next coordinated adapter host output chunk.");
                continue;
            }

            _ = writeCoordinator.WaitForDistinctPendingThreadCount(
                minimumDistinctPendingThreadCount: 2,
                TimeSpan.FromMilliseconds(100));
            if (
                !writeCoordinator.TryReleaseNextPendingWrite(
                    lastReleasedThreadId,
                    out int threadId)
            )
            {
                continue;
            }

            lastReleasedThreadId = threadId;
        }
    }

    private interface ICoordinatedPendingWriteSource
    {
        bool WaitForPendingWrite(TimeSpan timeout);

        bool WaitForDistinctPendingThreadCount(
            int minimumDistinctPendingThreadCount,
            TimeSpan timeout);

        bool TryReleaseNextPendingWrite(
            int? preferredDifferentThreadId,
            out int releasedThreadId);
    }

    private sealed class StringOnlyTextWriter : TextWriter, IProgressAwareTextWriter
    {
        private readonly List<string> _writes = [];
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public IReadOnlyList<string> Writes => _writes;

        public override void Write(string? value)
        {
            if (value is not null)
            {
                _writes.Add(value);
                _written.Append(value);
            }
        }

        public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
        {
            string chunk = new(value);
            Write(chunk);
            charsWritten += chunk.Length;
        }
    }

    private sealed class CharOnlyTextWriter : TextWriter
    {
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(char value)
        {
            _written.Append(value);
        }
    }

    private sealed class PartialThrowingStringOnlyTextWriter : TextWriter, IProgressAwareTextWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly int _throwAfterCharacterCount;
        private readonly StringBuilder _written = new();

        public PartialThrowingStringOnlyTextWriter(
            int throwAfterCharacterCount,
            Exception exceptionToThrow)
        {
            _throwAfterCharacterCount = throwAfterCharacterCount;
            _exceptionToThrow = exceptionToThrow;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (_written.Length >= _throwAfterCharacterCount)
            {
                throw _exceptionToThrow;
            }

            int remainingCharacterCount = _throwAfterCharacterCount - _written.Length;
            int charactersToWrite = Math.Min(value.Length, remainingCharacterCount);
            _written.Append(value.AsSpan(0, charactersToWrite));
            if (charactersToWrite < value.Length)
            {
                throw _exceptionToThrow;
            }
        }

        public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
        {
            string chunk = new(value);
            int initialLength = _written.Length;
            try
            {
                Write(chunk);
            }
            catch
            {
                charsWritten += _written.Length - initialLength;
                throw;
            }

            charsWritten += chunk.Length;
        }
    }

    private sealed class AppendThenThrowStringTextWriter : TextWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly StringBuilder _written = new();

        public AppendThenThrowStringTextWriter(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _written.Append(value);
            throw _exceptionToThrow;
        }
    }

    private sealed class ExternallyWritingThrowingStringWriter : StringWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly StringBuilder _written = new();

        public ExternallyWritingThrowingStringWriter(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public string Written => _written.ToString();

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _written.Append(value);
            throw _exceptionToThrow;
        }
    }

    private sealed class CloneBypassingReplacementEncoding : Encoding
    {
        private readonly Encoding _strictCloneEncoding;
        private readonly Encoding _writeEncoding;

        public CloneBypassingReplacementEncoding(
            Encoding writeEncoding,
            Encoding strictCloneEncoding)
        {
            _writeEncoding = writeEncoding
                ?? throw new ArgumentNullException(nameof(writeEncoding));
            _strictCloneEncoding = strictCloneEncoding
                ?? throw new ArgumentNullException(nameof(strictCloneEncoding));
        }

        public override object Clone()
        {
            return _strictCloneEncoding.Clone();
        }

        public override int GetByteCount(char[] chars, int index, int count)
        {
            return _writeEncoding.GetByteCount(chars, index, count);
        }

        public override int GetBytes(
            char[] chars,
            int charIndex,
            int charCount,
            byte[] bytes,
            int byteIndex)
        {
            return _writeEncoding.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            return _writeEncoding.GetCharCount(bytes, index, count);
        }

        public override int GetChars(
            byte[] bytes,
            int byteIndex,
            int byteCount,
            char[] chars,
            int charIndex)
        {
            return _writeEncoding.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
        }

        public override int GetMaxByteCount(int charCount)
        {
            return _writeEncoding.GetMaxByteCount(charCount);
        }

        public override int GetMaxCharCount(int byteCount)
        {
            return _writeEncoding.GetMaxCharCount(byteCount);
        }

        public override byte[] GetPreamble()
        {
            return _writeEncoding.GetPreamble();
        }

        public override Decoder GetDecoder()
        {
            return _writeEncoding.GetDecoder();
        }

        public override Encoder GetEncoder()
        {
            return _writeEncoding.GetEncoder();
        }
    }

    private sealed class PartialThrowingTextWriter : TextWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly int _throwAfterCharacterCount;
        private readonly StringBuilder _written = new();

        public PartialThrowingTextWriter(
            int throwAfterCharacterCount,
            Exception exceptionToThrow)
        {
            _throwAfterCharacterCount = throwAfterCharacterCount;
            _exceptionToThrow = exceptionToThrow;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(char value)
        {
            if (_written.Length >= _throwAfterCharacterCount)
            {
                throw _exceptionToThrow;
            }

            _written.Append(value);
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (char character in value)
            {
                Write(character);
            }
        }
    }

    private sealed class BlockingStringTextWriter : TextWriter
    {
        private readonly ManualResetEventSlim _blockedWriteEntered = new(false);
        private readonly ManualResetEventSlim _releaseBlockedWrite = new(false);
        private readonly StringBuilder _written = new();
        private bool _blockedFirstWrite;

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public void WaitForBlockedWriteEntered()
        {
            if (!_blockedWriteEntered.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out waiting for the in-flight child stdout write.");
            }
        }

        public void ReleaseBlockedWrite()
        {
            _releaseBlockedWrite.Set();
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _written.Append(value);
            if (_blockedFirstWrite)
            {
                return;
            }

            _blockedFirstWrite = true;
            _blockedWriteEntered.Set();
            if (!_releaseBlockedWrite.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out releasing the in-flight child stdout write.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _blockedWriteEntered.Dispose();
                _releaseBlockedWrite.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class PartiallyWritingThrowingStream : Stream
    {
        private readonly int _bytesToWriteBeforeThrow;
        private readonly Exception _exceptionToThrow;
        private readonly MemoryStream _written = new();

        public PartiallyWritingThrowingStream(
            int bytesToWriteBeforeThrow,
            Exception exceptionToThrow)
        {
            _bytesToWriteBeforeThrow = bytesToWriteBeforeThrow;
            _exceptionToThrow = exceptionToThrow;
        }

        public int WrittenByteCount => checked((int)_written.Length);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _written.Length;

        public override long Position
        {
            get => _written.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int bytesToWrite = Math.Min(count, _bytesToWriteBeforeThrow);
            if (bytesToWrite != 0)
            {
                _written.Write(buffer, offset, bytesToWrite);
            }

            throw _exceptionToThrow;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _written.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CoordinatedProgressAwareTextWriter
        : TextWriter, IProgressAwareTextWriter, ICoordinatedPendingWriteSource
    {
        private readonly List<PendingWrite> _pendingWrites = [];
        private readonly object _syncRoot = new();
        private readonly List<string> _writes = [];
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written
        {
            get
            {
                lock (_syncRoot)
                {
                    return _written.ToString();
                }
            }
        }

        public IReadOnlyList<string> Writes
        {
            get
            {
                lock (_syncRoot)
                {
                    return _writes.ToArray();
                }
            }
        }

        public bool WaitForPendingWrite(TimeSpan timeout)
        {
            return WaitUntil(HasUnreleasedPendingWrite, timeout);
        }

        public bool WaitForDistinctPendingThreadCount(
            int minimumDistinctPendingThreadCount,
            TimeSpan timeout)
        {
            return WaitUntil(
                () => GetDistinctPendingThreadCount() >= minimumDistinctPendingThreadCount,
                timeout);
        }

        public bool TryReleaseNextPendingWrite(
            int? preferredDifferentThreadId,
            out int releasedThreadId)
        {
            lock (_syncRoot)
            {
                PendingWrite? pendingWrite =
                    FindPendingWrite(preferredDifferentThreadId, requireDifferentThreadId: true)
                    ?? FindPendingWrite(
                        preferredDifferentThreadId,
                        requireDifferentThreadId: false);
                if (pendingWrite is null)
                {
                    releasedThreadId = 0;
                    return false;
                }

                pendingWrite.IsReleased = true;
                releasedThreadId = pendingWrite.ThreadId;
                pendingWrite.Release.Set();
                return true;
            }
        }

        public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
        {
            string chunk = new(value);
            var pendingWrite = new PendingWrite(Environment.CurrentManagedThreadId);
            lock (_syncRoot)
            {
                _pendingWrites.Add(pendingWrite);
                Monitor.PulseAll(_syncRoot);
            }

            try
            {
                if (!pendingWrite.Release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release a coordinated adapter host output chunk.");
                }

                lock (_syncRoot)
                {
                    _writes.Add(chunk);
                    _written.Append(chunk);
                }

                charsWritten += chunk.Length;
            }
            finally
            {
                lock (_syncRoot)
                {
                    _pendingWrites.Remove(pendingWrite);
                    Monitor.PulseAll(_syncRoot);
                }

                pendingWrite.Release.Dispose();
            }
        }

        private PendingWrite? FindPendingWrite(
            int? preferredDifferentThreadId,
            bool requireDifferentThreadId)
        {
            foreach (PendingWrite pendingWrite in _pendingWrites)
            {
                if (pendingWrite.IsReleased)
                {
                    continue;
                }

                if (requireDifferentThreadId
                    && preferredDifferentThreadId.HasValue
                    && pendingWrite.ThreadId == preferredDifferentThreadId.Value)
                {
                    continue;
                }

                return pendingWrite;
            }

            return null;
        }

        private int GetDistinctPendingThreadCount()
        {
            int? firstThreadId = null;
            var distinctThreadCount = 0;
            foreach (PendingWrite pendingWrite in _pendingWrites)
            {
                if (pendingWrite.IsReleased)
                {
                    continue;
                }

                if (firstThreadId is null)
                {
                    firstThreadId = pendingWrite.ThreadId;
                    distinctThreadCount = 1;
                    continue;
                }

                if (pendingWrite.ThreadId != firstThreadId.Value)
                {
                    return 2;
                }
            }

            return distinctThreadCount;
        }

        private bool HasUnreleasedPendingWrite()
        {
            foreach (PendingWrite pendingWrite in _pendingWrites)
            {
                if (!pendingWrite.IsReleased)
                {
                    return true;
                }
            }

            return false;
        }

        private bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            lock (_syncRoot)
            {
                while (!predicate())
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    Monitor.Wait(_syncRoot, remaining);
                }

                return true;
            }
        }

        private sealed class PendingWrite
        {
            public PendingWrite(int threadId)
            {
                ThreadId = threadId;
            }

            public bool IsReleased { get; set; }

            public ManualResetEventSlim Release { get; } = new(false);

            public int ThreadId { get; }
        }
    }
}
