using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        string? invocationPath = GetInvocationPath();
        if (
            NuGetPluginAdapter.TryResolveProtocolInvocation(
                invocationPath ?? "azureauth-credprovider",
                args,
                out _)
        )
        {
            return RunProtocolWithProductionRoot(RunNuGetPlugin);
        }

        if (
            KeyringHelperAdapter.TryResolveProtocolInvocation(
                invocationPath ?? "azureauth-credprovider",
                args,
                out _)
        )
        {
            return RunProtocolWithProductionRoot(
                root => RunKeyringHelper(root, invocationPath, args));
        }

        return CliApplication.Run(
            args,
            StandardConsoleTextWriters.StandardOutput(),
            StandardConsoleTextWriters.StandardError(),
            runtimeOptions: null,
            Console.In,
            invocationPath);
    }

    private static int RunProtocolWithProductionRoot(
        Func<CredentialProviderCompositionRoot, int> dispatch)
    {
        try
        {
            return dispatch(CredentialProviderCompositionRoot.CreateProduction());
        }
        catch (Exception)
        {
            StandardConsoleTextWriters.StandardError()
                .WriteLine("error: credential provider configuration is unavailable.");
            return 70;
        }
    }

    private static int RunNuGetPlugin(CredentialProviderCompositionRoot compositionRoot)
    {
        try
        {
            return compositionRoot.CreateNuGetPluginAdapter()
                .RunPluginAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            StandardConsoleTextWriters.StandardError()
                .WriteLine("error: unexpected fatal failure.");
            return 70;
        }
    }

    private static int RunKeyringHelper(
        CredentialProviderCompositionRoot compositionRoot,
        string? invocationPath,
        string[] args)
    {
        try
        {
            TextWriter protocolStdout = StandardConsoleTextWriters.StandardOutput();
            TextWriter stderr = StandardConsoleTextWriters.StandardError();
            var diagnosticRouter = new DiagnosticRouter(
                [new TextWriterDiagnosticSink(stderr)],
                SecretRedactor.Empty);
            AdapterHostExecutionOutcome outcome = compositionRoot.CreateKeyringHelperAdapter().Execute(
                invocationPath ?? KeyringHelperAdapter.ProductExecutableName,
                args,
                protocolStdout,
                humanStdout: protocolStdout,
                diagnosticRouter);
            return (int)outcome.Result.ExitCode;
        }
        catch (Exception)
        {
            StandardConsoleTextWriters.StandardError()
                .WriteLine("error: unexpected fatal failure.");
            return 70;
        }
    }

    private static string? GetInvocationPath()
    {
        string? nativeArgv0 = TryReadLinuxArgv0();
        if (!IsManagedHostInvocation(nativeArgv0))
        {
            return nativeArgv0;
        }

        string[] commandLineArgs = Environment.GetCommandLineArgs();
        if (commandLineArgs.Length == 0)
        {
            return Environment.ProcessPath;
        }

        string invocationPath = commandLineArgs[0];
        if (IsManagedAssemblyInvocation(invocationPath))
        {
            return Path.GetFileNameWithoutExtension(invocationPath);
        }

        return IsManagedHostInvocation(invocationPath)
            ? Environment.ProcessPath
            : invocationPath;
    }

    private static bool IsManagedHostInvocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string fileName = Path.GetFileName(path);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || IsManagedAssemblyInvocation(fileName);
    }

    private static bool IsManagedAssemblyInvocation(string path) =>
        string.Equals(
            Path.GetExtension(path),
            ".dll",
            StringComparison.OrdinalIgnoreCase);

    private static string? TryReadLinuxArgv0()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            byte[] commandLine = File.ReadAllBytes("/proc/self/cmdline");
            int terminatorIndex = Array.IndexOf(commandLine, (byte)0);
            int length = terminatorIndex < 0 ? commandLine.Length : terminatorIndex;
            return length == 0
                ? null
                : System.Text.Encoding.UTF8.GetString(commandLine, 0, length);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            return null;
        }
    }
}
