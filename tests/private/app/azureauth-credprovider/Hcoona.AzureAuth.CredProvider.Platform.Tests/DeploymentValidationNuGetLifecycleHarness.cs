using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

internal static class DeploymentValidationNuGetLifecycleHarness
{
    internal const string Command = "deployment-validation-nuget-lifecycle";

    internal static void Run(string[] args)
    {
        if (
            args.Length != 4
            || !TryResolveExplicitPath(args[1], out string stateDirectoryPath)
            || !TryResolveExplicitPath(args[2], out string applicationPayloadRootPath)
            || !TryResolveExplicitPath(args[3], out string userHomeDirectoryPath)
        )
        {
            Environment.Exit(64);
            return;
        }

        try
        {
            var service = new NuGetPhase10VerticalSliceService(
                new NuGetPhase10VerticalSliceOptions
                {
                    StateDirectoryPath = stateDirectoryPath,
                    ApplicationPayloadRootPath = applicationPayloadRootPath,
                    UserHomeDirectoryPath = userHomeDirectoryPath,
                    EnvironmentVariableReader = static _ => null,
                }
            );

            if (string.Equals(args[0], "configure", StringComparison.Ordinal))
            {
                service.ConfigureAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (string.Equals(args[0], "unconfigure", StringComparison.Ordinal))
            {
                service.UnconfigureAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                Environment.Exit(64);
                return;
            }

            Console.WriteLine(args[0]);
            Environment.Exit(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NuGet lifecycle harness failed: {exception.Message}");
            Console.Error.Flush();
            Environment.Exit(1);
        }
    }

    private static bool TryResolveExplicitPath(string value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            return false;
        }

        try
        {
            path = Path.GetFullPath(value);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
