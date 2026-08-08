using System.Collections.ObjectModel;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class AdapterHostHandlerOutput
{
    public AdapterHostHandlerOutput(
        CredentialResult? credentialResult = null,
        CredentialOperation operation = CredentialOperation.Get,
        AdapterHostExitCode humanCommandExitCode = AdapterHostExitCode.Success,
        string? protocolStdout = null,
        string? humanStdout = null,
        IEnumerable<DiagnosticEvent>? diagnosticEvents = null)
    {
        ValidateOperation(operation, nameof(operation));
        ValidateExitCode(humanCommandExitCode, nameof(humanCommandExitCode));

        CredentialResult = credentialResult;
        Operation = operation;
        HumanCommandExitCode = humanCommandExitCode;
        ProtocolStdout = protocolStdout;
        HumanStdout = humanStdout;
        DiagnosticEvents = CopyDiagnosticEvents(diagnosticEvents);
    }

    public CredentialResult? CredentialResult { get; }

    public CredentialOperation Operation { get; }

    public AdapterHostExitCode HumanCommandExitCode { get; }

    public string? ProtocolStdout { get; }

    public string? HumanStdout { get; }

    public IReadOnlyList<DiagnosticEvent> DiagnosticEvents { get; }

    private static void ValidateOperation(CredentialOperation operation, string paramName)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                operation,
                "Unknown credential operation.");
        }
    }

    private static void ValidateExitCode(AdapterHostExitCode exitCode, string paramName)
    {
        if (!Enum.IsDefined(exitCode))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                exitCode,
                "Unknown adapter-host exit code.");
        }
    }

    private static ReadOnlyCollection<DiagnosticEvent> CopyDiagnosticEvents(
        IEnumerable<DiagnosticEvent>? diagnosticEvents)
    {
        if (diagnosticEvents is null)
        {
            return ReadOnlyCollection<DiagnosticEvent>.Empty;
        }

        DiagnosticEvent[] copiedDiagnosticEvents = diagnosticEvents.ToArray();
        if (Array.Exists(copiedDiagnosticEvents, static diagnosticEvent => diagnosticEvent is null))
        {
            throw new ArgumentException(
                "Diagnostic events must not contain null values.",
                nameof(diagnosticEvents));
        }

        return copiedDiagnosticEvents.Length == 0
            ? ReadOnlyCollection<DiagnosticEvent>.Empty
            : Array.AsReadOnly(copiedDiagnosticEvents);
    }
}
