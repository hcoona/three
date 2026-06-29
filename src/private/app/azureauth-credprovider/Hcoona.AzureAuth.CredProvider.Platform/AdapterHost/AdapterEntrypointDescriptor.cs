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
        AdapterProtocol protocol = AdapterProtocol.Unspecified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ReadOnlyCollection<string> copiedExecutableNames = CopyNames(
            executableNames,
            nameof(executableNames));
        ReadOnlyCollection<string> copiedArgumentTokens = CopyNames(
            argumentTokens,
            nameof(argumentTokens));
        ValidateMode(mode, nameof(mode));
        ValidateArgumentMatchMode(argumentMatchMode, nameof(argumentMatchMode));
        ValidateProtocol(protocol, nameof(protocol));

        Name = name;
        Mode = mode;
        Protocol = protocol;
        ExecutableNames = copiedExecutableNames;
        ArgumentTokens = copiedArgumentTokens;
        ArgumentMatchMode = argumentMatchMode;
        StripMatchedArguments = stripMatchedArguments;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;

        ValidateProtocolConfiguration(nameof(protocol));
        ValidateArgumentMatchConfiguration();
        ValidateBoundaryConfiguration(nameof(executableNames));
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
        out IReadOnlyList<string> payloadArguments,
        bool useWindowsExecutableSemantics = false)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!MatchesExecutable(executableName, useWindowsExecutableSemantics))
        {
            matchedArguments = ReadOnlyCollection<string>.Empty;
            payloadArguments = ReadOnlyCollection<string>.Empty;
            return false;
        }

        if (!TryMatchArguments(arguments, out matchedArguments, out payloadArguments))
        {
            matchedArguments = ReadOnlyCollection<string>.Empty;
            payloadArguments = ReadOnlyCollection<string>.Empty;
            return false;
        }

        return true;
    }

    internal bool HasExecutableConstraint => ExecutableNames.Count != 0;

    internal bool IsExecutableOnlyBoundary =>
        ArgumentMatchMode == AdapterArgumentMatchMode.Any &&
        ArgumentTokens.Count == 0;

    internal bool MatchesExecutableBoundary(
        string? executableName,
        bool useWindowsExecutableSemantics = false)
    {
        return MatchesExecutable(executableName, useWindowsExecutableSemantics);
    }

    internal bool MatchesArgumentBoundary(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (ArgumentTokens.Count == 0)
        {
            return false;
        }

        return TryMatchArguments(arguments, out _, out _);
    }

    internal bool HasConfusableProtocolArgumentBoundary(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 ||
            ArgumentTokens.Count == 0)
        {
            return false;
        }

        return ArgumentMatchMode switch
        {
            AdapterArgumentMatchMode.Prefix => arguments.Count < ArgumentTokens.Count &&
                                               HasLeadingArgumentTokenMatch(arguments),
            AdapterArgumentMatchMode.Exact => arguments.Count != ArgumentTokens.Count &&
                                              HasLeadingArgumentTokenMatch(arguments),
            AdapterArgumentMatchMode.ContainsAll =>
                HasPartialContainsAllArgumentTokenMatch(arguments),
            _ => false,
        };
    }

    internal AdapterProtocol ResolveProtocol(AdapterProtocol descriptorProtocol)
    {
        if (Mode != AdapterInvocationMode.Protocol)
        {
            return AdapterProtocol.Unspecified;
        }

        return Protocol != AdapterProtocol.Unspecified
            ? Protocol
            : descriptorProtocol;
    }

    private static void ValidateMode(AdapterInvocationMode mode, string paramName)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                mode,
                "Unknown adapter invocation mode.");
        }
    }

    private static void ValidateArgumentMatchMode(
        AdapterArgumentMatchMode argumentMatchMode,
        string paramName)
    {
        if (!Enum.IsDefined(argumentMatchMode))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                argumentMatchMode,
                "Unknown adapter argument match mode.");
        }
    }

    private static void ValidateProtocol(AdapterProtocol protocol, string paramName)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                protocol,
                "Unknown adapter protocol.");
        }
    }

    private void ValidateProtocolConfiguration(string paramName)
    {
        if (Mode == AdapterInvocationMode.HumanCommand &&
            Protocol != AdapterProtocol.Unspecified)
        {
            throw new ArgumentException(
                "Human command entry points must not declare a concrete adapter protocol.",
                paramName);
        }
    }

    private void ValidateArgumentMatchConfiguration()
    {
        if (ArgumentMatchMode == AdapterArgumentMatchMode.Any)
        {
            if (ArgumentTokens.Count != 0)
            {
                throw new ArgumentException(
                    "Argument tokens are supported only when the argument match mode " +
                    "requires them.",
                    nameof(ArgumentTokens));
            }

            return;
        }

        if (ArgumentTokens.Count == 0)
        {
            throw new ArgumentException(
                "Argument match modes other than Any require at least one argument token.",
                nameof(ArgumentTokens));
        }
    }

    private void ValidateBoundaryConfiguration(string paramName)
    {
        if (ExecutableNames.Count == 0 &&
            ArgumentMatchMode == AdapterArgumentMatchMode.Any &&
            ArgumentTokens.Count == 0)
        {
            throw new ArgumentException(
                "Adapter entry points must constrain executable names or argument tokens; " +
                "fully unconstrained entry points are not allowed.",
                paramName);
        }
    }

    private bool MatchesExecutable(
        string? executableName,
        bool useWindowsExecutableSemantics)
    {
        if (ExecutableNames.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(executableName))
        {
            return false;
        }

        foreach (string expectedExecutableName in ExecutableNames)
        {
            if (ExecutableNamesMatch(
                    expectedExecutableName,
                    executableName,
                    useWindowsExecutableSemantics))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExecutableNamesMatch(
        string expectedExecutableName,
        string actualExecutableName,
        bool useWindowsExecutableSemantics)
    {
        StringComparison comparison = useWindowsExecutableSemantics
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(expectedExecutableName, actualExecutableName, comparison))
        {
            return true;
        }

        if (!useWindowsExecutableSemantics)
        {
            return false;
        }

        return string.Equals(
            NormalizeExecutableName(expectedExecutableName, useWindowsExecutableSemantics),
            NormalizeExecutableName(actualExecutableName, useWindowsExecutableSemantics),
            comparison);
    }

    private static bool HasWindowsExeSuffix(
        string executableName,
        bool useWindowsExecutableSemantics)
    {
        return executableName.EndsWith(
            ".exe",
            useWindowsExecutableSemantics
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    internal static string NormalizeExecutableName(
        string executableName,
        bool useWindowsExecutableSemantics)
    {
        return useWindowsExecutableSemantics &&
               HasWindowsExeSuffix(executableName, useWindowsExecutableSemantics)
            ? executableName[..^4]
            : executableName;
    }

    internal HashSet<string>? GetMatchedExecutableNameSet(
        string? executableName,
        bool useWindowsExecutableSemantics)
    {
        if (!HasExecutableConstraint)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrEmpty(executableName);

        StringComparer comparer = useWindowsExecutableSemantics
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var matchedExecutableNames = new HashSet<string>(comparer);
        foreach (string expectedExecutableName in ExecutableNames)
        {
            if (ExecutableNamesMatch(
                    expectedExecutableName,
                    executableName,
                    useWindowsExecutableSemantics))
            {
                matchedExecutableNames.Add(NormalizeExecutableName(
                    expectedExecutableName,
                    useWindowsExecutableSemantics));
            }
        }

        return matchedExecutableNames;
    }

    private bool TryMatchArguments(
        IReadOnlyList<string> arguments,
        out IReadOnlyList<string> matchedArguments,
        out IReadOnlyList<string> payloadArguments)
    {
        switch (ArgumentMatchMode)
        {
            case AdapterArgumentMatchMode.Any:
                matchedArguments = ReadOnlyCollection<string>.Empty;
                payloadArguments = arguments;
                return true;

            case AdapterArgumentMatchMode.Prefix:
                return TryMatchPrefix(arguments, out matchedArguments, out payloadArguments);

            case AdapterArgumentMatchMode.Exact:
                return TryMatchExact(arguments, out matchedArguments, out payloadArguments);

            case AdapterArgumentMatchMode.ContainsAll:
                return TryMatchContainsAll(arguments, out matchedArguments, out payloadArguments);

            default:
                throw new InvalidOperationException("Unknown adapter argument match mode.");
        }
    }

    private bool TryMatchPrefix(
        IReadOnlyList<string> arguments,
        out IReadOnlyList<string> matchedArguments,
        out IReadOnlyList<string> payloadArguments)
    {
        if (arguments.Count < ArgumentTokens.Count)
        {
            matchedArguments = ReadOnlyCollection<string>.Empty;
            payloadArguments = ReadOnlyCollection<string>.Empty;
            return false;
        }

        for (var index = 0; index < ArgumentTokens.Count; index++)
        {
            if (!string.Equals(arguments[index], ArgumentTokens[index], StringComparison.Ordinal))
            {
                matchedArguments = ReadOnlyCollection<string>.Empty;
                payloadArguments = ReadOnlyCollection<string>.Empty;
                return false;
            }
        }

        matchedArguments = ArgumentTokens;
        payloadArguments = StripMatchedArguments
            ? ToReadOnly(arguments.Skip(ArgumentTokens.Count))
            : arguments;
        return true;
    }

    private bool TryMatchExact(
        IReadOnlyList<string> arguments,
        out IReadOnlyList<string> matchedArguments,
        out IReadOnlyList<string> payloadArguments)
    {
        if (arguments.Count != ArgumentTokens.Count)
        {
            matchedArguments = ReadOnlyCollection<string>.Empty;
            payloadArguments = ReadOnlyCollection<string>.Empty;
            return false;
        }

        for (var index = 0; index < ArgumentTokens.Count; index++)
        {
            if (!string.Equals(arguments[index], ArgumentTokens[index], StringComparison.Ordinal))
            {
                matchedArguments = ReadOnlyCollection<string>.Empty;
                payloadArguments = ReadOnlyCollection<string>.Empty;
                return false;
            }
        }

        matchedArguments = ArgumentTokens;
        payloadArguments = StripMatchedArguments
            ? ReadOnlyCollection<string>.Empty
            : arguments;
        return true;
    }

    private bool TryMatchContainsAll(
        IReadOnlyList<string> arguments,
        out IReadOnlyList<string> matchedArguments,
        out IReadOnlyList<string> payloadArguments)
    {
        var matchedIndexes = new HashSet<int>();
        foreach (string token in ArgumentTokens)
        {
            var found = false;
            for (var index = 0; index < arguments.Count; index++)
            {
                if (matchedIndexes.Contains(index) ||
                    !string.Equals(arguments[index], token, StringComparison.Ordinal))
                {
                    continue;
                }

                matchedIndexes.Add(index);
                found = true;
                break;
            }

            if (!found)
            {
                matchedArguments = ReadOnlyCollection<string>.Empty;
                payloadArguments = ReadOnlyCollection<string>.Empty;
                return false;
            }
        }

        matchedArguments = ArgumentTokens;
        if (!StripMatchedArguments)
        {
            payloadArguments = arguments;
            return true;
        }

        var payload = new List<string>(Math.Max(0, arguments.Count - matchedIndexes.Count));
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!matchedIndexes.Contains(index))
            {
                payload.Add(arguments[index]);
            }
        }

        payloadArguments = ToReadOnly(payload);
        return true;
    }

    private static ReadOnlyCollection<string> CopyNames(
        IEnumerable<string>? values,
        string paramName)
    {
        if (values is null)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        var copiedValues = values.ToArray();
        if (Array.Exists(copiedValues, static value => string.IsNullOrWhiteSpace(value)))
        {
            throw new ArgumentException(
                "Adapter entry point names and argument tokens must not contain null or " +
                "whitespace values.",
                paramName);
        }

        return Array.AsReadOnly(copiedValues);
    }

    private static ReadOnlyCollection<string> ToReadOnly(IEnumerable<string> values)
    {
        var copiedValues = values.ToArray();
        return copiedValues.Length == 0
            ? ReadOnlyCollection<string>.Empty
            : Array.AsReadOnly(copiedValues);
    }

    private bool HasPartialContainsAllArgumentTokenMatch(IReadOnlyList<string> arguments)
    {
        Dictionary<string, int> availableTokenCounts = CountTokens(arguments);
        Dictionary<string, int> requiredTokenCounts = CountTokens(ArgumentTokens);
        var matchedTokenCount = 0;

        foreach (KeyValuePair<string, int> requiredTokenCount in requiredTokenCounts)
        {
            if (!availableTokenCounts.TryGetValue(requiredTokenCount.Key, out int availableCount))
            {
                continue;
            }

            matchedTokenCount += Math.Min(availableCount, requiredTokenCount.Value);
        }

        return matchedTokenCount != 0 &&
               matchedTokenCount < ArgumentTokens.Count;
    }

    private static Dictionary<string, int> CountTokens(IReadOnlyList<string> tokens)
    {
        var tokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            tokenCounts[token] = tokenCounts.TryGetValue(token, out int count)
                ? count + 1
                : 1;
        }

        return tokenCounts;
    }

    private bool HasLeadingArgumentTokenMatch(IReadOnlyList<string> arguments)
    {
        int compareCount = Math.Min(arguments.Count, ArgumentTokens.Count);
        for (var index = 0; index < compareCount; index++)
        {
            if (!string.Equals(arguments[index], ArgumentTokens[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return compareCount != 0;
    }
}
