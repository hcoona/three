using System.Collections.ObjectModel;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class AdapterEntrypointDescriptor
{
    public AdapterEntrypointDescriptor(
        string name,
        AdapterInvocationMode mode,
        IEnumerable<string>? executableNames = null,
        IEnumerable<string>? argumentTokens = null,
        AdapterArgumentMatchMode argumentMatchMode = AdapterArgumentMatchMode.Any,
        bool stripMatchedArguments = true,
        string? description = null,
        AdapterProtocol protocol = AdapterProtocol.Unspecified
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown invocation mode.");
        }

        if (!Enum.IsDefined(argumentMatchMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(argumentMatchMode),
                argumentMatchMode,
                "Unknown argument match mode."
            );
        }

        if (!Enum.IsDefined(protocol))
        {
            throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unknown protocol.");
        }

        if (mode == AdapterInvocationMode.HumanCommand && protocol != AdapterProtocol.Unspecified)
        {
            throw new ArgumentException(
                "Human command entry points cannot declare a protocol.",
                nameof(protocol)
            );
        }

        ExecutableNames = CopyValues(executableNames, nameof(executableNames));
        ArgumentTokens = CopyValues(argumentTokens, nameof(argumentTokens));
        if (argumentMatchMode == AdapterArgumentMatchMode.Any && ArgumentTokens.Count != 0)
        {
            throw new ArgumentException(
                "Argument tokens require Prefix or Exact matching.",
                nameof(argumentTokens)
            );
        }

        if (argumentMatchMode != AdapterArgumentMatchMode.Any && ArgumentTokens.Count == 0)
        {
            throw new ArgumentException(
                "Prefix and Exact matching require argument tokens.",
                nameof(argumentTokens)
            );
        }

        if (ExecutableNames.Count == 0 && argumentMatchMode == AdapterArgumentMatchMode.Any)
        {
            throw new ArgumentException(
                "Entry points must constrain the executable or arguments.",
                nameof(executableNames)
            );
        }

        Name = name;
        Mode = mode;
        Protocol = protocol;
        ArgumentMatchMode = argumentMatchMode;
        StripMatchedArguments = stripMatchedArguments;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public string Name { get; }

    public AdapterInvocationMode Mode { get; }

    public AdapterProtocol Protocol { get; }

    public IReadOnlyList<string> ExecutableNames { get; }

    public IReadOnlyList<string> ArgumentTokens { get; }

    public AdapterArgumentMatchMode ArgumentMatchMode { get; }

    public bool StripMatchedArguments { get; }

    public string? Description { get; }

    internal bool TryMatch(
        string? executableName,
        IReadOnlyList<string> arguments,
        out IReadOnlyList<string> matchedArguments,
        out IReadOnlyList<string> payloadArguments
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!MatchesExecutable(executableName))
        {
            matchedArguments = ReadOnlyCollection<string>.Empty;
            payloadArguments = ReadOnlyCollection<string>.Empty;
            return false;
        }

        bool argumentsMatch = ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Any => true,
            AdapterArgumentMatchMode.Prefix => StartsWith(arguments, ArgumentTokens),
            AdapterArgumentMatchMode.Exact => SequenceEqual(arguments, ArgumentTokens),
            _ => false,
        };
        if (!argumentsMatch)
        {
            matchedArguments = ReadOnlyCollection<string>.Empty;
            payloadArguments = ReadOnlyCollection<string>.Empty;
            return false;
        }

        matchedArguments =
            ArgumentMatchMode == AdapterArgumentMatchMode.Any
                ? ReadOnlyCollection<string>.Empty
                : ArgumentTokens;
        payloadArguments =
            StripMatchedArguments && ArgumentMatchMode != AdapterArgumentMatchMode.Any
                ? ToReadOnly(arguments.Skip(ArgumentTokens.Count))
                : arguments;
        return true;
    }

    internal AdapterProtocol ResolveProtocol(AdapterProtocol descriptorProtocol)
    {
        if (Mode != AdapterInvocationMode.Protocol)
        {
            return AdapterProtocol.Unspecified;
        }

        return Protocol == AdapterProtocol.Unspecified ? descriptorProtocol : Protocol;
    }

    private bool MatchesExecutable(string? executableName)
    {
        if (ExecutableNames.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(executableName))
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalizedActual = NormalizeExecutableName(executableName);
        return ExecutableNames.Any(expected =>
            string.Equals(NormalizeExecutableName(expected), normalizedActual, comparison)
        );
    }

    private static string NormalizeExecutableName(string executableName)
    {
        return executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName[..^4]
            : executableName;
    }

    private static bool StartsWith(IReadOnlyList<string> arguments, IReadOnlyList<string> expected)
    {
        if (arguments.Count < expected.Count)
        {
            return false;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(arguments[index], expected[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> expected
    )
    {
        return arguments.Count == expected.Count && StartsWith(arguments, expected);
    }

    private static ReadOnlyCollection<string> CopyValues(
        IEnumerable<string>? values,
        string parameterName
    )
    {
        if (values is null)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        string[] copied = values.ToArray();
        if (copied.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Entry point values must not be null or whitespace.",
                parameterName
            );
        }

        return Array.AsReadOnly(copied);
    }

    private static ReadOnlyCollection<string> ToReadOnly(IEnumerable<string> values)
    {
        string[] copied = values.ToArray();
        return copied.Length == 0 ? ReadOnlyCollection<string>.Empty : Array.AsReadOnly(copied);
    }
}
