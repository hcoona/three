using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false, true);
        return Run(args, Console.Out, Console.Error);
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args is ["native-message-schemas", var schemaDirectory])
            {
                NativeMessageSchemas.Write(schemaDirectory);
                return 0;
            }
            if (ReleaseCli.Run(args, output) is { } releaseStatus) return releaseStatus;
            if (CiCli.Run(args, output, error) is { } ciStatus) return ciStatus;
            error.WriteLine(
                "Usage: WorkflowDelivery ci "
                    + "caller-<control|dotnet|node|python|ruby|local-python> ..."
                    + " | ci native-preparation-plan <root> <scope.json> <directory>"
                    + " | release job-inputs <ecosystem> <operation>"
                    + " | release prepare-python-index-tools <request.json>"
                    + " | release <prepare|observe|describe|publish|complete>-<target> ..."
                    + " | native-message-schemas <directory>"
            );
            return 2;
        }
        catch (PythonGroupPreparationException exception)
        {
            exception.WriteDiagnostics(error);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            JsonException or ArgumentException or UnauthorizedAccessException or TimeoutException or
            Nerdbank.GitVersioning.GitException or KeyNotFoundException)
        {
            RunnerLog.Write(error, exception.Message, CiCallerTransfer.MessagePrefix);
            return 2;
        }
    }
}
