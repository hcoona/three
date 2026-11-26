using WorkflowDelivery.CI.Callers;

namespace WorkflowDelivery.CI;

internal static class CiCli
{
    internal static int? Run(string[] args, TextWriter output, TextWriter error) =>
        NativePreparationQualification.Run(args, output) ??
        ControlCiCaller.Run(args, output) ??
        LocalPythonCiCaller.Run(args, output, error) ??
        CiCallerCli.Run(args, output, error);
}
