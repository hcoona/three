using System.Collections;
using System.Reflection;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

internal static class ProcessTestApp
{
    internal const string HelperEnabledVariable = "AZUREAUTH_PROCESS_HELPER";
    internal const string HelperEnabledValue = "1";
    internal const string HelperNonceVariable = "AZUREAUTH_PROCESS_HELPER_NONCE";
    internal const string HelperSwitch = "--process-helper";
    internal const string HelperNonceSwitch = "--process-helper-nonce";

    internal static string AppHostPath()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string directory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException(
                $"Test assembly path '{assemblyPath}' does not have a parent directory.");
        string fileName = Path.GetFileNameWithoutExtension(assemblyPath);

        if (OperatingSystem.IsWindows())
        {
            fileName += ".exe";
        }

        string appHostPath = Path.Combine(directory, fileName);
        if (!File.Exists(appHostPath))
        {
            throw new FileNotFoundException(
                $"Sibling test apphost '{appHostPath}' was not found for '{assemblyPath}'.",
                appHostPath);
        }

        return appHostPath;
    }

    internal static string CreateHelperNonce()
    {
        return Guid.NewGuid().ToString("N");
    }

    internal static List<string> CreateHelperArguments(
        string helperNonce,
        string command,
        IReadOnlyList<string>? arguments = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(helperNonce);
        ArgumentException.ThrowIfNullOrEmpty(command);

        var allArguments = new List<string>
        {
            HelperSwitch,
            HelperNonceSwitch,
            helperNonce,
            command,
        };

        if (arguments is not null)
        {
            allArguments.AddRange(arguments);
        }

        return allArguments;
    }

    internal static Dictionary<string, string?> CreateHelperEnvironment(
        string helperNonce,
        IReadOnlyDictionary<string, string?>? environment = null,
        ProcessEnvironmentMode environmentMode = ProcessEnvironmentMode.Inherit
    )
    {
        return CreateHelperEnvironment(
            helperNonce,
            environment,
            environmentMode,
            inheritedEnvironment: null,
            useWindowsEnvironmentVariableSemantics: OperatingSystem.IsWindows()
        );
    }

    internal static Dictionary<string, string?> CreateHelperEnvironment(
        string helperNonce,
        IReadOnlyDictionary<string, string?>? environment,
        ProcessEnvironmentMode environmentMode,
        IReadOnlyDictionary<string, string?>? inheritedEnvironment,
        bool useWindowsEnvironmentVariableSemantics
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(helperNonce);

        var variableNameComparer = GetEnvironmentVariableNameComparer(
            useWindowsEnvironmentVariableSemantics
        );
        var allEnvironment = new Dictionary<string, string?>(variableNameComparer)
        {
            [HelperEnabledVariable] = HelperEnabledValue,
            [HelperNonceVariable] = helperNonce,
        };

        if (environment is not null)
        {
            foreach (var variable in environment)
            {
                allEnvironment.Add(variable.Key, variable.Value);
            }
        }

        if (environmentMode == ProcessEnvironmentMode.ExplicitOnly)
        {
            PreserveDotnetHostBootstrapEnvironment(
                allEnvironment,
                inheritedEnvironment,
                useWindowsEnvironmentVariableSemantics
            );
        }

        return allEnvironment;
    }

    internal static bool TryGetHelperDispatchArguments(
        IReadOnlyList<string> arguments,
        Func<string, string?> getEnvironmentVariable,
        out string[] helperArguments
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        helperArguments = [];

        if (
            !string.Equals(
                getEnvironmentVariable(HelperEnabledVariable),
                HelperEnabledValue,
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        var helperNonce = getEnvironmentVariable(HelperNonceVariable);
        if (
            string.IsNullOrEmpty(helperNonce)
            || arguments.Count < 3
            || !string.Equals(arguments[0], HelperSwitch, StringComparison.Ordinal)
            || !string.Equals(arguments[1], HelperNonceSwitch, StringComparison.Ordinal)
            || !string.Equals(arguments[2], helperNonce, StringComparison.Ordinal)
        )
        {
            return false;
        }

        helperArguments = arguments.Skip(3).ToArray();
        return true;
    }

    internal static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void PreserveDotnetHostBootstrapEnvironment(
        Dictionary<string, string?> environment,
        IReadOnlyDictionary<string, string?>? inheritedEnvironment,
        bool useWindowsEnvironmentVariableSemantics
    )
    {
        var variableNameComparison = GetEnvironmentVariableNameComparison(
            useWindowsEnvironmentVariableSemantics
        );

        if (inheritedEnvironment is not null)
        {
            foreach (var variable in inheritedEnvironment)
            {
                PreserveDotnetHostBootstrapVariable(
                    environment,
                    variable.Key,
                    variable.Value,
                    variableNameComparison
                );
            }

            return;
        }

        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (
                variable.Key is not string name
                || variable.Value is not string value
            )
            {
                continue;
            }

            PreserveDotnetHostBootstrapVariable(
                environment,
                name,
                value,
                variableNameComparison
            );
        }
    }

    private static void PreserveDotnetHostBootstrapVariable(
        Dictionary<string, string?> environment,
        string name,
        string? value,
        StringComparison variableNameComparison
    )
    {
        if (
            value is null
            || environment.ContainsKey(name)
            || !IsDotnetHostBootstrapVariable(name, variableNameComparison)
        )
        {
            return;
        }

        environment.Add(name, value);
    }

    private static bool IsDotnetHostBootstrapVariable(
        string name,
        StringComparison variableNameComparison
    )
    {
        return string.Equals(name, "DOTNET_MULTILEVEL_LOOKUP", variableNameComparison)
            || name.StartsWith("DOTNET_ROOT", variableNameComparison);
    }

    private static StringComparer GetEnvironmentVariableNameComparer(
        bool useWindowsEnvironmentVariableSemantics
    )
    {
        return useWindowsEnvironmentVariableSemantics
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static StringComparison GetEnvironmentVariableNameComparison(
        bool useWindowsEnvironmentVariableSemantics
    )
    {
        return useWindowsEnvironmentVariableSemantics
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
