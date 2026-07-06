using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

internal static class AdapterHostProofProcess
{
    internal const string ForceNonUtf8ConsoleEnvironmentVariable =
        "AZUREAUTH_ADAPTER_HOST_PROOF_FORCE_NON_UTF8_CONSOLE";

    internal const string GitGetSuccessScenario = "git-get-success";
    internal const string GitStoreSuccessScenario = "git-store-success";
    internal const string GitEraseSuccessScenario = "git-erase-success";
    internal const string GitFailureScenario = "git-protocol-failure";
    internal const string GitInvalidUtf16ProtocolStdoutScenario =
        "git-invalid-utf16-protocol-stdout";
    internal const string GitNoCredentialScenario = "git-no-credential";
    internal const string GitUnauthorizedScenario = "git-unauthorized";
    internal const string GitFatalScenario = "git-fatal";
    internal const string NuGetSuccessScenario = "nuget-success";
    internal const string NuGetFailureScenario = "nuget-failure";
    internal const string KeyringSuccessScenario = "keyring-success";
    internal const string KeyringFailureScenario = "keyring-failure";
    internal const string InvocationBoundaryMismatchScenario = "invocation-boundary-mismatch";
    internal const string HumanCommandScenario = "human-command";
    internal const string HumanCommandNonBmpScenario = "human-command-non-bmp";
    internal const string HumanCommandInvalidUtf16StdoutScenario =
        "human-command-invalid-utf16-stdout";

    internal const string GitGetSuccessProtocolPayload =
        "username=AzureDevOps\npassword=git-proof-password\n";
    internal const string GitInvalidUtf16ProtocolPayload =
        "\uD83Dusername=AzureDevOps\npassword=git-proof-password\n";

    internal const string NuGetSuccessProtocolPayload =
        "{\"type\":\"plugin-proof\",\"requestId\":\"proof\",\"payload\":\"opaque\"}";

    internal const string HumanCommandStdout = "doctor ok";
    internal const string HumanCommandNonBmpStdout = "doctor 🚀 ok";
    internal const string HumanCommandNonBmpDiagnosticMessage = "diagnostic 🧪";
    internal const string HumanCommandInvalidUtf16Stdout = "\uD83Ddoctor ok";
    internal const string SuppressedProtocolPayload =
        "suppressed-protocol-payload-should-not-leak";
    internal const string SuppressedHumanStdout =
        "suppressed-human-stdout-should-not-leak";
    internal const string SuppressedDiagnosticMessage =
        "suppressed-diagnostic-message-should-not-leak";
    internal const string ProtocolViolationSafeCode = "ProtocolViolation";
    internal const string ProtocolViolationSafeMessage =
        "Adapter host protocol output was invalid.";
    internal const string InvocationBoundaryMismatchSafeCode = "InvocationBoundaryMismatch";
    internal const string InvocationBoundaryMismatchSafeMessage =
        "Adapter host invocation boundary is unsupported.";
    internal const string NoCredentialSafeCode = "UnsupportedHost";
    internal const string NoCredentialSafeMessage = "No matching credential is available.";
    internal const string UnauthorizedSafeCode = "Unauthorized";
    internal const string UnauthorizedSafeMessage = "Synthetic unauthorized.";
    internal const string FatalSafeCode = "Fatal";
    internal const string FatalSafeMessage = "Synthetic fatal failure.";
    internal const string UnhandledHostFailureSafeCode = "UnhandledHostFailure";
    internal const string UnhandledHostFailureSafeMessage = "Adapter host execution failed.";
    internal const string InvocationBoundaryMismatchDescriptorMarker =
        "bootstrap-proof-descriptor-internal-should-not-leak";
    internal const string InvocationBoundaryMismatchPayloadMarker =
        "bootstrap-proof-sensitive-payload-should-not-leak";
    internal const string GitPassword = "git-proof-password";
    internal const string NuGetPassword = "nuget-proof-password";
    internal const string KeyringPassword = "keyring-proof-password";
    internal const string SharedUsername = "AzureDevOps";

    private const string SharedExecutablePath = "/usr/local/bin/azureauth-credprovider";
    private const string NuGetExecutablePath = "/usr/local/bin/CredentialProvider.AzureAuth";
    private const string KeyringExecutablePath = "/usr/local/bin/python-keyring";

    internal static void Run(string[] args)
    {
        ConfigureAmbientConsoleForProof();

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            ExitConfiguration("Adapter host proof process requires exactly one scenario.");
            return;
        }

        AdapterHostExecutionOutcome outcome = ExecuteScenario(args[0]);
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit((int)outcome.Result.ExitCode);
    }

    internal static string CreateKeyringSuccessProtocolPayload()
    {
        return KeyringHelperV2.ToResponse(
            CreateKeyringRequest(),
            CreateKeyringSuccessCredentialResult()).Stdout;
    }

    private static AdapterHostExecutionOutcome ExecuteScenario(string scenario)
    {
        return scenario switch
        {
            GitGetSuccessScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "get"],
                CreateProtocolSuccessOutput(
                    CreateGitGetSuccessCredentialResult(),
                    GitGetSuccessProtocolPayload)),
            GitStoreSuccessScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "store"],
                CreateQuietGitMutationSuccessOutput(CredentialOperation.Store)),
            GitEraseSuccessScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "erase"],
                CreateQuietGitMutationSuccessOutput(CredentialOperation.Erase)),
            GitFailureScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "get"],
                CreateProtocolViolationOutput(CreateGitGetSuccessCredentialResult())),
            GitInvalidUtf16ProtocolStdoutScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "get"],
                CreateProtocolSuccessOutput(
                    CreateGitGetSuccessCredentialResult(),
                    GitInvalidUtf16ProtocolPayload)),
            GitNoCredentialScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "get"],
                CreateProtocolFailureOutput(CreateNoCredentialResult())),
            GitUnauthorizedScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "get"],
                CreateProtocolFailureOutput(CreateUnauthorizedCredentialResult())),
            GitFatalScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["git", "credential-helper", "get"],
                CreateProtocolFailureOutput(CreateFatalCredentialResult())),
            NuGetSuccessScenario => Execute(
                CreateNuGetDescriptor(),
                NuGetExecutablePath,
                CreateNuGetArguments(),
                CreateProtocolSuccessOutput(
                    CreateNuGetSuccessCredentialResult(),
                    NuGetSuccessProtocolPayload)),
            NuGetFailureScenario => Execute(
                CreateNuGetDescriptor(),
                NuGetExecutablePath,
                CreateNuGetArguments(),
                CreateProtocolViolationOutput(CreateNuGetSuccessCredentialResult())),
            KeyringSuccessScenario => Execute(
                CreateKeyringDescriptor(),
                KeyringExecutablePath,
                CreateKeyringArguments(),
                CreateProtocolSuccessOutput(
                    CreateKeyringSuccessCredentialResult(),
                    CreateKeyringSuccessProtocolPayload())),
            KeyringFailureScenario => Execute(
                CreateKeyringDescriptor(),
                KeyringExecutablePath,
                CreateKeyringArguments(),
                CreateProtocolViolationOutput(CreateKeyringSuccessCredentialResult())),
            InvocationBoundaryMismatchScenario => Execute(
                CreateInvocationBoundaryMismatchDescriptor(),
                CreateInvocationBoundaryMismatchExecutablePath(),
                CreateInvocationBoundaryMismatchArguments(),
                CreateProtocolSuccessOutput(
                    CreateGitGetSuccessCredentialResult(),
                    GitGetSuccessProtocolPayload)),
            HumanCommandScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["doctor", "--json"],
                new AdapterHostHandlerOutput(
                    humanStdout: HumanCommandStdout,
                    protocolStdout: SuppressedProtocolPayload)),
            HumanCommandInvalidUtf16StdoutScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["doctor", "--invalid-utf16"],
                new AdapterHostHandlerOutput(
                    humanStdout: HumanCommandInvalidUtf16Stdout,
                    protocolStdout: SuppressedProtocolPayload)),
            HumanCommandNonBmpScenario => Execute(
                CreateSharedGitDescriptor(),
                SharedExecutablePath,
                ["doctor", "--unicode"],
                new AdapterHostHandlerOutput(
                    humanStdout: HumanCommandNonBmpStdout,
                    protocolStdout: SuppressedProtocolPayload,
                    diagnosticEvents:
                    [
                        new DiagnosticEvent(
                            DiagnosticSeverity.Error,
                            DiagnosticChannel.Diagnostic,
                            HumanCommandNonBmpDiagnosticMessage,
                            timestamp: DateTimeOffset.Parse(
                                "2025-01-02T03:04:05.0000000+00:00"))
                    ])),
            _ => UnknownScenario(scenario),
        };
    }

    private static AdapterHostExecutionOutcome Execute(
        AdapterDescriptor descriptor,
        string executablePath,
        IReadOnlyList<string> arguments,
        AdapterHostHandlerOutput handlerOutput)
    {
        var standardOutput = StandardConsoleTextWriter.StandardOutput();
        var standardError = StandardConsoleTextWriter.StandardError();
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(standardError)],
            SecretRedactor.Empty);
        try
        {
            return AdapterHostExecutor.Execute(
                descriptor,
                executablePath,
                arguments,
                _ => handlerOutput,
                protocolStdout: standardOutput,
                humanStdout: standardOutput,
                diagnosticRouter);
        }
        finally
        {
            standardOutput.Flush();
            standardError.Flush();
        }
    }

    private static AdapterHostHandlerOutput CreateProtocolSuccessOutput(
        CredentialResult credentialResult,
        string protocolStdout)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: credentialResult,
            protocolStdout: protocolStdout,
            humanStdout: SuppressedHumanStdout,
            diagnosticEvents: CreateSuppressedDiagnosticEvents());
    }

    private static AdapterHostHandlerOutput CreateQuietGitMutationSuccessOutput(
        CredentialOperation operation)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                DiagnosticsCorrelationId = string.Empty,
            },
            operation: operation,
            protocolStdout: SuppressedProtocolPayload,
            humanStdout: SuppressedHumanStdout,
            diagnosticEvents: CreateSuppressedDiagnosticEvents());
    }

    private static AdapterHostHandlerOutput CreateProtocolViolationOutput(
        CredentialResult credentialResult)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: credentialResult,
            protocolStdout: null,
            humanStdout: SuppressedHumanStdout,
            diagnosticEvents: CreateSuppressedDiagnosticEvents());
    }

    private static AdapterHostHandlerOutput CreateProtocolFailureOutput(
        CredentialResult credentialResult)
    {
        return new AdapterHostHandlerOutput(
            credentialResult: credentialResult,
            protocolStdout: SuppressedProtocolPayload,
            humanStdout: SuppressedHumanStdout,
            diagnosticEvents: CreateSuppressedDiagnosticEvents());
    }

    private static DiagnosticEvent[] CreateSuppressedDiagnosticEvents()
    {
        return
        [
            new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticChannel.Diagnostic,
                SuppressedDiagnosticMessage),
        ];
    }

    private static AdapterDescriptor CreateSharedGitDescriptor()
    {
        return new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitCredentialHelper",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);
    }

    private static AdapterDescriptor CreateNuGetDescriptor()
    {
        return new AdapterDescriptor(
            "NuGet Plugin",
            AdapterProtocol.NuGetPlugin,
            [
                new AdapterEntrypointDescriptor(
                    "NuGetPlugin",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["CredentialProvider.AzureAuth"],
                    argumentTokens: ["-Plugin", "-Uri"],
                    argumentMatchMode: AdapterArgumentMatchMode.ContainsAll,
                    stripMatchedArguments: false),
            ]);
    }

    private static AdapterDescriptor CreateKeyringDescriptor()
    {
        return new AdapterDescriptor(
            "Keyring Helper v2",
            AdapterProtocol.KeyringHelper,
            [
                new AdapterEntrypointDescriptor(
                    "KeyringHelperV2",
                    AdapterInvocationMode.Protocol,
                    executableNames: [KeyringHelperV2.CommandName],
                    argumentTokens: [KeyringHelperV2.GetVerb],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
            ]);
    }

    private static AdapterDescriptor CreateInvocationBoundaryMismatchDescriptor()
    {
        return new AdapterDescriptor(
            InvocationBoundaryMismatchDescriptorMarker,
            AdapterProtocol.GitCredentialHelper,
            [
                new AdapterEntrypointDescriptor(
                    "GitCredentialHelper",
                    AdapterInvocationMode.Protocol,
                    executableNames: ["azureauth-credprovider"],
                    argumentTokens: ["git", "credential-helper"],
                    argumentMatchMode: AdapterArgumentMatchMode.Prefix),
                new AdapterEntrypointDescriptor(
                    "HumanCommand",
                    AdapterInvocationMode.HumanCommand,
                    executableNames: ["azureauth-credprovider"]),
            ]);
    }

    private static string[] CreateNuGetArguments()
    {
        return
        [
            "-Uri",
            "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json",
            "-NonInteractive",
            "-Plugin",
        ];
    }

    private static string[] CreateKeyringArguments()
    {
        return KeyringHelperV2.BuildArguments(CreateKeyringRequest()).Skip(1).ToArray();
    }

    private static string CreateInvocationBoundaryMismatchExecutablePath()
    {
        return $"/usr/local/bin/{InvocationBoundaryMismatchPayloadMarker}/.";
    }

    private static string[] CreateInvocationBoundaryMismatchArguments()
    {
        return
        [
            "git",
            "credential-helper",
            InvocationBoundaryMismatchPayloadMarker,
        ];
    }

    private static KeyringHelperRequest CreateKeyringRequest()
    {
        return new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Credentials,
        };
    }

    private static CredentialResult CreateGitGetSuccessCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = SharedUsername,
            Password = GitPassword,
            DiagnosticsCorrelationId = string.Empty,
        };
    }

    private static CredentialResult CreateNuGetSuccessCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = SharedUsername,
            Password = NuGetPassword,
            DiagnosticsCorrelationId = string.Empty,
        };
    }

    private static CredentialResult CreateKeyringSuccessCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = SharedUsername,
            Password = KeyringPassword,
            DiagnosticsCorrelationId = string.Empty,
        };
    }

    private static CredentialResult CreateUnauthorizedCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Unauthorized,
            DiagnosticsCorrelationId = string.Empty,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.Unauthorized,
                Code = UnauthorizedSafeCode,
                SafeMessage = UnauthorizedSafeMessage,
            },
        };
    }

    private static CredentialResult CreateNoCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.NoCredential,
            DiagnosticsCorrelationId = string.Empty,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.UnsupportedHost,
                Code = NoCredentialSafeCode,
                SafeMessage = NoCredentialSafeMessage,
            },
        };
    }

    private static CredentialResult CreateFatalCredentialResult()
    {
        return new CredentialResult
        {
            Status = CredentialResultStatus.Fatal,
            DiagnosticsCorrelationId = string.Empty,
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.Fatal,
                Code = FatalSafeCode,
                SafeMessage = FatalSafeMessage,
            },
        };
    }

    private static AdapterHostExecutionOutcome UnknownScenario(string scenario)
    {
        ExitConfiguration($"Unknown adapter host proof scenario '{scenario}'.");
        throw new InvalidOperationException("Unreachable.");
    }

    private static void ConfigureAmbientConsoleForProof()
    {
        if (string.Equals(
            Environment.GetEnvironmentVariable(ForceNonUtf8ConsoleEnvironmentVariable),
            "1",
            StringComparison.Ordinal))
        {
            Console.OutputEncoding = Encoding.Latin1;
        }
    }

    private static void ExitConfiguration(string message)
    {
        StandardConsoleTextWriter standardError = StandardConsoleTextWriter.StandardError();
        standardError.Write(message);
        standardError.Flush();
        Environment.Exit((int)AdapterHostExitCode.ConfigurationError);
    }
}
