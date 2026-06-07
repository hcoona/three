namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public interface IDiagnosticSink
{
    void Write(DiagnosticEvent diagnosticEvent);
}
