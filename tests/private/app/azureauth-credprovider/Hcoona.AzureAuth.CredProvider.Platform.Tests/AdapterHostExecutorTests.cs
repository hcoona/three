using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AdapterHostExecutorTests
{
    [Fact]
    public void ExecuteProtocolSuccessWritesOnlyProtocolStdout()
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();

        AdapterHostExecutionOutcome outcome = Execute(
            CreateSuccessOutput("username=user\npassword=secret\n"),
            ["git", "credential-helper", "get"],
            protocolStdout,
            humanStdout,
            stderr
        );

        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.Equal("username=user\npassword=secret\n", protocolStdout.ToString());
        Assert.Equal(string.Empty, humanStdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task ExecuteProtocolCancellationBeforeFirstByteWritesNoCredentialsAndFails()
    {
        var protocolOutput = new StringBuilder();
        var coordinator = new CoordinatedSharedStringWriterCoordinator();
        using var protocolStdout = new CoordinatedSharedStringWriter(
            protocolOutput,
            coordinator
        );
        var stderr = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task<AdapterHostExecutionOutcome> execution = Task.Run(
            () =>
                ExecuteWithCancellation(
                    CreateSuccessOutput("username=user\npassword=secret\n"),
                    ["git", "credential-helper", "get"],
                    protocolStdout,
                    new StringWriter(),
                    stderr,
                    cancellation.Token
                ),
            TestContext.Current.CancellationToken
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            )
        );
        Assert.True(execution.IsFaulted);
        Assert.Equal(string.Empty, protocolOutput.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task ExecuteProtocolCancellationWhileWritePendingAndFailureBeforeFirstByteReturnsFatal()
    {
        var protocolOutput = new StringBuilder();
        var coordinator = new CoordinatedSharedStringWriterCoordinator();
        var protocolStdout = new CoordinatedSharedStringWriter(protocolOutput, coordinator);
        var stderr = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        Task<AdapterHostExecutionOutcome> execution = Task.Run(
            () =>
                ExecuteWithCancellation(
                    CreateSuccessOutput("username=user\npassword=secret\n"),
                    ["git", "credential-helper", "get"],
                    protocolStdout,
                    new StringWriter(),
                    stderr,
                    cancellation.Token
                ),
            TestContext.Current.CancellationToken
        );

        Assert.True(coordinator.WaitForPendingWrite(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        protocolStdout.Dispose();
        Assert.True(coordinator.TryReleaseNextPendingWrite(null, out _));

        AdapterHostExecutionOutcome outcome = await execution.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.False(outcome.Result.WriteProtocolStdout);
        Assert.Equal(string.Empty, protocolOutput.ToString());
        Assert.Contains("Adapter host execution failed.", stderr.ToString());
        Assert.Contains("code=UnhandledHostFailure", stderr.ToString());
    }

    [Fact]
    public async Task ExecuteProtocolCancellationAfterSuccessfulWriteAndFlushReturnsSuccess()
    {
        const string ExpectedCredential = "username=user\npassword=secret\n";
        var protocolOutput = new StringBuilder();
        var coordinator = new CoordinatedSharedStringWriterCoordinator();
        using var protocolStdout = new CoordinatedSharedStringWriter(
            protocolOutput,
            coordinator,
            coordinateAfterFlush: true
        );
        var stderr = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        Task<AdapterHostExecutionOutcome> execution = Task.Run(
            () =>
                ExecuteWithCancellation(
                    CreateSuccessOutput(ExpectedCredential),
                    ["git", "credential-helper", "get"],
                    protocolStdout,
                    new StringWriter(),
                    stderr,
                    cancellation.Token
                ),
            TestContext.Current.CancellationToken
        );

        Assert.True(coordinator.WaitForPendingWrite(TimeSpan.FromSeconds(5)));
        Assert.True(coordinator.TryReleaseNextPendingWrite(null, out _));
        Assert.True(coordinator.WaitForPendingWrite(TimeSpan.FromSeconds(5)));
        Assert.Equal(ExpectedCredential, protocolOutput.ToString());

        cancellation.Cancel();
        Assert.True(coordinator.TryReleaseNextPendingWrite(null, out _));

        AdapterHostExecutionOutcome outcome = await execution.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.True(outcome.Result.WriteProtocolStdout);
        Assert.Equal(ExpectedCredential, protocolOutput.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void ExecuteProtocolNoCredentialIsSilent()
    {
        var protocolStdout = new StringWriter();
        var stderr = new StringWriter();

        AdapterHostExecutionOutcome outcome = Execute(
            new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.NoCredential,
                    DiagnosticsCorrelationId = string.Empty,
                }
            ),
            ["git", "credential-helper", "get"],
            protocolStdout,
            new StringWriter(),
            stderr
        );

        Assert.Equal(AdapterHostExitCode.NoCredential, outcome.Result.ExitCode);
        Assert.Equal(string.Empty, protocolStdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void ExecuteProtocolMissingMappedPayloadReturnsSafeConfigurationError()
    {
        var stderr = new StringWriter();

        AdapterHostExecutionOutcome outcome = Execute(
            CreateSuccessOutput(protocolStdout: null),
            ["git", "credential-helper", "get"],
            new StringWriter(),
            new StringWriter(),
            stderr
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.Contains("Adapter host protocol output was invalid.", stderr.ToString());
        Assert.Contains("code=ProtocolViolation", stderr.ToString());
    }

    [Fact]
    public void ExecuteProtocolFailureWritesSafeDiagnosticAndNoProtocolPayload()
    {
        var protocolStdout = new StringWriter();
        var stderr = new StringWriter();

        AdapterHostExecutionOutcome outcome = Execute(
            new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.Unauthorized,
                    DiagnosticsCorrelationId = string.Empty,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.Unauthorized,
                        Code = "Unauthorized",
                        SafeMessage = "Authentication failed safely.",
                    },
                },
                protocolStdout: "must-not-leak"
            ),
            ["git", "credential-helper", "get"],
            protocolStdout,
            new StringWriter(),
            stderr
        );

        Assert.Equal(AdapterHostExitCode.Unauthorized, outcome.Result.ExitCode);
        Assert.Equal(string.Empty, protocolStdout.ToString());
        Assert.Contains("Authentication failed safely.", stderr.ToString());
        Assert.Contains("code=Unauthorized", stderr.ToString());
    }

    [Fact]
    public void ExecuteHumanCommandWritesHumanOutputAndDiagnostics()
    {
        var protocolStdout = new StringWriter();
        var humanStdout = new StringWriter();
        var stderr = new StringWriter();

        AdapterHostExecutionOutcome outcome = Execute(
            new AdapterHostHandlerOutput(
                humanStdout: "doctor ok",
                protocolStdout: "must-not-leak",
                diagnosticEvents:
                [
                    new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "warning"
                    ),
                    new DiagnosticEvent(
                        DiagnosticSeverity.Error,
                        DiagnosticChannel.ProtocolStdout,
                        "must-not-route"
                    ),
                ]
            ),
            ["doctor"],
            protocolStdout,
            humanStdout,
            stderr
        );

        Assert.Equal(AdapterHostExitCode.Success, outcome.Result.ExitCode);
        Assert.Equal(string.Empty, protocolStdout.ToString());
        Assert.Equal("doctor ok", humanStdout.ToString());
        Assert.Contains("warning", stderr.ToString());
        Assert.DoesNotContain("must-not-route", stderr.ToString());
    }

    [Fact]
    public void ExecuteBoundaryMismatchReturnsSafeConfigurationError()
    {
        var stderr = new StringWriter();
        var router = CreateRouter(stderr);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            CreateDescriptor(),
            "other-tool",
            [],
            _ => throw new InvalidOperationException(),
            new StringWriter(),
            new StringWriter(),
            router
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, outcome.Result.ExitCode);
        Assert.Contains("code=InvocationBoundaryMismatch", stderr.ToString());
    }

    [Fact]
    public void ExecuteUnhandledHandlerFailureReturnsSafeFatalError()
    {
        var stderr = new StringWriter();
        var router = CreateRouter(stderr);

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            CreateDescriptor(),
            "azureauth-credprovider",
            ["git", "credential-helper", "get"],
            _ => throw new IOException("sensitive implementation failure"),
            new StringWriter(),
            new StringWriter(),
            router
        );

        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Contains("Adapter host execution failed.", stderr.ToString());
        Assert.DoesNotContain("sensitive implementation failure", stderr.ToString());
    }

    [Fact]
    public void ExecuteRedactsSafeDiagnosticFields()
    {
        var stderr = new StringWriter();
        var router = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            new SecretRedactor(["secret-value"])
        );

        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            CreateDescriptor(),
            "azureauth-credprovider",
            ["git", "credential-helper", "get"],
            _ => new AdapterHostHandlerOutput(
                credentialResult: new CredentialResult
                {
                    Status = CredentialResultStatus.Fatal,
                    DiagnosticsCorrelationId = string.Empty,
                    Error = new CredentialError
                    {
                        Kind = CredentialErrorKind.Fatal,
                        Code = "Fatal",
                        SafeMessage = "failure secret-value",
                    },
                }
            ),
            new StringWriter(),
            new StringWriter(),
            router
        );

        Assert.Equal(AdapterHostExitCode.Fatal, outcome.Result.ExitCode);
        Assert.Contains(SecretRedactor.DefaultMask, stderr.ToString());
        Assert.DoesNotContain("secret-value", stderr.ToString());
    }

    private static AdapterHostExecutionOutcome Execute(
        AdapterHostHandlerOutput output,
        string[] arguments,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        TextWriter stderr
    )
    {
        return AdapterHostExecutor.Execute(
            CreateDescriptor(),
            "azureauth-credprovider",
            arguments,
            _ => output,
            protocolStdout,
            humanStdout,
            CreateRouter(stderr)
        );
    }

    private static AdapterHostExecutionOutcome ExecuteWithCancellation(
        AdapterHostHandlerOutput output,
        string[] arguments,
        TextWriter protocolStdout,
        TextWriter humanStdout,
        TextWriter stderr,
        CancellationToken cancellationToken
    )
    {
        return AdapterHostExecutor.ExecuteWithCancellation(
            CreateDescriptor(),
            "azureauth-credprovider",
            arguments,
            _ => output,
            protocolStdout,
            humanStdout,
            CreateRouter(stderr),
            cancellationToken
        );
    }

    private static AdapterHostHandlerOutput CreateSuccessOutput(string? protocolStdout)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "user",
                Password = "secret",
                DiagnosticsCorrelationId = string.Empty,
            },
            protocolStdout: protocolStdout,
            humanStdout: "must-not-leak"
        );
    }

    private static DiagnosticRouter CreateRouter(TextWriter stderr) =>
        new([new TextWriterDiagnosticSink(stderr)], SecretRedactor.Empty);

    private static AdapterDescriptor CreateDescriptor() =>
        new(
            "Git",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "Protocol",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix
                ),
                new AdapterEntrypointDescriptor(
                    "Human",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]
                ),
            ]
        );
}
