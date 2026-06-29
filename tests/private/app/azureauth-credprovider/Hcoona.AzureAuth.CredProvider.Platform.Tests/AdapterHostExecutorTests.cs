using System.Collections;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
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

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }

    private sealed class ThrowingDiagnosticSink : IDiagnosticSink
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
    }

    private sealed class PartialThrowingTextWriter : TextWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly int _throwAfterCharacterCount;

        public PartialThrowingTextWriter(
            int throwAfterCharacterCount,
            Exception exceptionToThrow)
        {
            _throwAfterCharacterCount = throwAfterCharacterCount;
            _exceptionToThrow = exceptionToThrow;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Written { get; private set; } = string.Empty;

        public override void Write(string? value)
        {
            string nonNullValue = value ?? string.Empty;
            int writtenCharacterCount = Math.Min(
                _throwAfterCharacterCount,
                nonNullValue.Length);
            Written = nonNullValue[..writtenCharacterCount];
            throw _exceptionToThrow;
        }
    }
}
