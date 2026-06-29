using System.Collections.ObjectModel;
using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Platform.Packaging;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public static class AdapterHostBootstrap
{
    public static AdapterInvocationContext ResolveInvocation(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        InvocationResolutionResult resolution = ResolveInvocationCore(
            descriptor,
            executablePath,
            arguments);
        if (resolution.Status == InvocationResolutionStatus.Success)
        {
            return resolution.Context!;
        }

        if (resolution.Status == InvocationResolutionStatus.Ambiguous)
        {
            throw CreateAmbiguousMatchException(descriptor, resolution.AmbiguousMatches!);
        }

        throw new InvalidOperationException(
            $"Adapter descriptor '{descriptor.Name}' does not match the current " +
            "invocation boundary.");
    }

    public static bool TryResolveInvocation(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments,
        out AdapterInvocationContext? context)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        InvocationResolutionResult resolution = ResolveInvocationCore(
            descriptor,
            executablePath,
            arguments);
        context = resolution.Context;
        return resolution.Status == InvocationResolutionStatus.Success;
    }

    private static InvocationResolutionResult ResolveInvocationCore(
        AdapterDescriptor descriptor,
        string? executablePath,
        IEnumerable<string>? arguments)
    {
        ReadOnlyCollection<string> copiedArguments = CopyArguments(arguments);
        bool useWindowsExecutableSemantics = UsesWindowsExecutableSemantics(executablePath);
        ExecutablePathParseResult executablePathParseResult = ParseExecutablePath(
            executablePath,
            useWindowsExecutableSemantics);
        if (executablePathParseResult.Status == ExecutablePathParseStatus.Invalid)
        {
            return InvocationResolutionResult.NoMatch;
        }

        string? executableName = executablePathParseResult.ExecutableName;
        var protocolCandidates = new List<MatchCandidate>();
        var humanCandidates = new List<MatchCandidate>();

        foreach (AdapterEntrypointDescriptor entrypoint in descriptor.Entrypoints)
        {
            if (!entrypoint.TryMatch(
                    executableName,
                    copiedArguments,
                    out IReadOnlyList<string> matchedArguments,
                    out IReadOnlyList<string> payloadArguments,
                    useWindowsExecutableSemantics))
            {
                continue;
            }

            var candidate = new MatchCandidate(
                entrypoint,
                matchedArguments,
                payloadArguments,
                entrypoint.GetMatchedExecutableNameSet(
                    executableName,
                    useWindowsExecutableSemantics));
            if (entrypoint.Mode == AdapterInvocationMode.Protocol)
            {
                protocolCandidates.Add(candidate);
            }
            else
            {
                humanCandidates.Add(candidate);
            }
        }

        if (protocolCandidates.Count != 0)
        {
            return ResolveMatchedCandidates(
                descriptor,
                executablePath,
                executableName,
                copiedArguments,
                protocolCandidates,
                useWindowsExecutableSemantics);
        }

        if (HasProtocolBoundaryConflict(
                descriptor,
                executableName,
                copiedArguments,
                useWindowsExecutableSemantics,
                executablePathParseResult.Status == ExecutablePathParseStatus.Missing &&
                humanCandidates.Count != 0))
        {
            return InvocationResolutionResult.NoMatch;
        }

        if (humanCandidates.Count == 0)
        {
            return InvocationResolutionResult.NoMatch;
        }

        return ResolveMatchedCandidates(
            descriptor,
            executablePath,
            executableName,
            copiedArguments,
            humanCandidates,
            useWindowsExecutableSemantics);
    }

    private static ReadOnlyCollection<string> CopyArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        var copiedArguments = arguments.ToArray();
        if (Array.Exists(copiedArguments, static argument => argument is null))
        {
            throw new ArgumentException(
                "Adapter arguments must not contain null values.",
                nameof(arguments));
        }

        return copiedArguments.Length == 0
            ? ReadOnlyCollection<string>.Empty
            : Array.AsReadOnly(copiedArguments);
    }

    internal static string? GetExecutableName(
        string? executablePath,
        bool useWindowsExecutableSemantics)
    {
        return ParseExecutablePath(
            executablePath,
            useWindowsExecutableSemantics).ExecutableName;
    }

    private static ExecutablePathParseResult ParseExecutablePath(
        string? executablePath,
        bool useWindowsExecutableSemantics)
    {
        if (executablePath is null)
        {
            return ExecutablePathParseResult.Missing;
        }

        if (executablePath.Length == 0)
        {
            return ExecutablePathParseResult.Invalid;
        }

        string normalizedExecutablePath = executablePath;
        if (useWindowsExecutableSemantics)
        {
            string? normalizedWindowsExecutablePath = NormalizeWindowsExecutablePath(
                normalizedExecutablePath);
            if (normalizedWindowsExecutablePath is null)
            {
                return ExecutablePathParseResult.Invalid;
            }

            normalizedExecutablePath = normalizedWindowsExecutablePath;
            if (IsMalformedWindowsExecutablePath(normalizedExecutablePath))
            {
                return ExecutablePathParseResult.Invalid;
            }

            if (HasWindowsDevicePrefix(normalizedExecutablePath))
            {
                if (!IsWindowsDeviceFileBackedChildPath(normalizedExecutablePath))
                {
                    return ExecutablePathParseResult.Invalid;
                }
            }
            else if (HasWindowsUncPrefix(normalizedExecutablePath))
            {
                if (!IsWindowsUncChildPath(normalizedExecutablePath))
                {
                    return ExecutablePathParseResult.Invalid;
                }
            }
            if (HasInvalidWindowsPathSegment(normalizedExecutablePath))
            {
                return ExecutablePathParseResult.Invalid;
            }
        }
        else if (HasInvalidPosixPathCharacter(normalizedExecutablePath))
        {
            return ExecutablePathParseResult.Invalid;
        }

        int fileNameStartIndex = useWindowsExecutableSemantics &&
                                 IsWindowsDrivePath(normalizedExecutablePath)
            ? 2
            : 0;
        int separatorIndex = useWindowsExecutableSemantics
            ? Math.Max(
                normalizedExecutablePath.LastIndexOf('/'),
                normalizedExecutablePath.LastIndexOf('\\'))
            : normalizedExecutablePath.LastIndexOf('/');
        string fileName = normalizedExecutablePath[
            Math.Max(separatorIndex + 1, fileNameStartIndex)..];
        return IsInvalidExecutableBasename(fileName)
            ? ExecutablePathParseResult.Invalid
            : ExecutablePathParseResult.Success(fileName);
    }

    private static string? NormalizeWindowsExecutablePath(string path)
    {
        if (TryCanonicalizeRawWindowsNtPath(path, out string canonicalPath))
        {
            return canonicalPath;
        }

        return HasRawWindowsNtNamespacePrefix(path) ||
               HasNonFileBackedRawWindowsObjectManagerRoot(path)
            ? null
            : path;
    }

    private static bool IsMalformedWindowsExecutablePath(string path)
    {
        return HasRepeatedLeadingDirectorySeparators(path) ||
               HasSingleLeadingWindowsPseudoDevicePrefix(path) ||
               HasDoubleLeadingRawWindowsNtDosDevicesAliasLookalikePrefix(path) ||
               HasMalformedWindowsDriveDesignator(path);
    }

    private static bool HasInvalidWindowsPathSegment(string path)
    {
        int componentStartIndex = TryGetWindowsUncHostAndShareComponentBounds(
            path,
            out _,
            out _,
            out _,
            out int shareEndIndex)
            ? shareEndIndex < path.Length &&
              IsDirectorySeparator(path[shareEndIndex])
                ? shareEndIndex + 1
                : shareEndIndex
            : GetWindowsPathSegmentStartIndex(path);
        for (var index = componentStartIndex; index <= path.Length; index++)
        {
            if (index < path.Length &&
                !IsDirectorySeparator(path[index]))
            {
                continue;
            }

            int componentLength = index - componentStartIndex;
            if (componentLength == 0)
            {
                if (index < path.Length)
                {
                    return true;
                }
            }
            else if (!FoundationArtifactPath.IsSafeWindowsPathSegment(
                         path.Substring(componentStartIndex, componentLength)))
            {
                return true;
            }

            componentStartIndex = index + 1;
        }

        return false;
    }

    private static int GetWindowsPathSegmentStartIndex(string path)
    {
        if (HasWindowsDevicePrefix(path))
        {
            if (HasWindowsDeviceUncPrefix(path))
            {
                return 8;
            }

            if (TryGetWindowsDeviceVolumeComponentEndIndex(path, out int componentEndIndex))
            {
                return componentEndIndex < path.Length &&
                       IsDirectorySeparator(path[componentEndIndex])
                    ? componentEndIndex + 1
                    : componentEndIndex;
            }

            if (path.Length >= 6 &&
                IsAsciiWindowsDriveLetter(path[4]) &&
                path[5] == ':')
            {
                return path.Length >= 7 &&
                       IsDirectorySeparator(path[6])
                    ? 7
                    : 6;
            }
        }

        if (HasWindowsUncPrefix(path))
        {
            return 2;
        }

        if (IsWindowsDrivePath(path))
        {
            return path.Length >= 3 &&
                   IsDirectorySeparator(path[2])
                ? 3
                : 2;
        }

        return HasSingleLeadingDirectorySeparator(path)
            ? 1
            : 0;
    }

    private static bool HasInvalidPosixPathCharacter(string path)
    {
        foreach (char value in path)
        {
            if (char.IsControl(value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesWindowsExecutableSemantics(string? executablePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(executablePath) ||
               !IsExplicitUnixPathShape(executablePath);
    }

    private static bool IsExplicitUnixPathShape(string path)
    {
        // Preserve explicit POSIX-style paths only for single-leading-slash paths like
        // "/usr/local/bin/tool" and "/server/share/tool". UNC-like or repeated-leading-separator
        // paths (for example "//server/share/tool.exe" or "///server/share/tool.exe") opt into
        // Windows executable semantics so they cannot bypass Windows path validation.
        return path.Length != 0 &&
               path[0] == '/' &&
               (path.Length == 1 || path[1] != '/');
    }

    private static bool HasWindowsUncPrefix(string path)
    {
        return path.Length >= 2 &&
               IsDirectorySeparator(path[0]) &&
               IsDirectorySeparator(path[1]) &&
               (path.Length == 2 || !IsDirectorySeparator(path[2]));
    }

    private static bool IsWindowsDeviceFileBackedChildPath(string path)
    {
        return HasWindowsDevicePrefix(path) &&
               (IsWindowsDeviceDriveChildPath(path) ||
                IsWindowsDeviceVolumeChildPath(path) ||
                IsWindowsDeviceUncChildPath(path));
    }

    private static bool IsWindowsDeviceDriveChildPath(string path)
    {
        return path.Length >= 7 &&
               HasWindowsDevicePrefix(path) &&
               IsAsciiWindowsDriveLetter(path[4]) &&
               path[5] == ':' &&
               IsDirectorySeparator(path[6]) &&
               !ContainsOnlyDirectorySeparators(path, 6);
    }

    private static bool IsWindowsDeviceVolumeChildPath(string path)
    {
        return TryGetWindowsDeviceVolumeComponentEndIndex(path, out int componentEndIndex) &&
               componentEndIndex < path.Length &&
               IsDirectorySeparator(path[componentEndIndex]) &&
               !ContainsOnlyDirectorySeparators(path, componentEndIndex);
    }

    private static bool TryGetWindowsDeviceVolumeComponentEndIndex(
        string path,
        out int componentEndIndex)
    {
        const int volumeComponentStartIndex = 4;
        const int volumeComponentLength = 44;

        componentEndIndex = 0;
        if (!HasWindowsDevicePrefix(path) ||
            path.Length < volumeComponentStartIndex + volumeComponentLength ||
            !IsCanonicalWindowsVolumeComponent(
                path.AsSpan(volumeComponentStartIndex, volumeComponentLength)))
        {
            return false;
        }

        componentEndIndex = volumeComponentStartIndex + volumeComponentLength;
        return true;
    }

    private static bool IsWindowsDeviceUncChildPath(string path)
    {
        return HasWindowsDeviceUncPrefix(path) &&
               IsWindowsUncChildPath(path);
    }

    private static bool IsWindowsUncChildPath(string path)
    {
        if (!TryGetWindowsUncHostAndShareComponentBounds(
                path,
                out int hostStartIndex,
                out int serverSeparatorIndex,
                out int shareStartIndex,
                out int shareEndIndex) ||
            !IsSafeWindowsUncAuthorityComponent(
                path.Substring(hostStartIndex, serverSeparatorIndex - hostStartIndex)))
        {
            return false;
        }

        ReadOnlySpan<char> shareComponent =
            path.AsSpan(shareStartIndex, shareEndIndex - shareStartIndex);
        if (!IsSafeWindowsUncShareComponent(shareComponent) ||
            IsWindowsUncIpcShare(shareComponent))
        {
            return false;
        }

        return shareEndIndex < path.Length &&
               !ContainsOnlyDirectorySeparators(path, shareEndIndex);
    }

    private static bool TryGetWindowsUncHostStartIndex(
        string path,
        out int hostStartIndex)
    {
        hostStartIndex = 0;
        if (!HasWindowsUncPrefix(path))
        {
            return false;
        }

        if (HasWindowsDevicePrefix(path))
        {
            if (!HasWindowsDeviceUncPrefix(path))
            {
                return false;
            }

            hostStartIndex = 8;
        }
        else
        {
            hostStartIndex = 2;
        }

        return hostStartIndex < path.Length &&
               !IsDirectorySeparator(path[hostStartIndex]);
    }

    private static bool TryGetWindowsUncHostAndShareComponentBounds(
        string path,
        out int hostStartIndex,
        out int serverSeparatorIndex,
        out int shareStartIndex,
        out int shareEndIndex)
    {
        hostStartIndex = 0;
        serverSeparatorIndex = 0;
        shareStartIndex = 0;
        shareEndIndex = 0;

        if (!TryGetWindowsUncHostStartIndex(path, out hostStartIndex))
        {
            return false;
        }

        serverSeparatorIndex = FindDirectorySeparator(path, hostStartIndex);
        if (serverSeparatorIndex < 0 ||
            serverSeparatorIndex == hostStartIndex ||
            serverSeparatorIndex == path.Length - 1 ||
            IsDirectorySeparator(path[serverSeparatorIndex + 1]))
        {
            return false;
        }

        shareStartIndex = serverSeparatorIndex + 1;
        shareEndIndex = FindDirectorySeparator(path, shareStartIndex);
        if (shareEndIndex < 0)
        {
            shareEndIndex = path.Length;
        }

        return shareEndIndex > shareStartIndex;
    }

    private static bool HasWindowsDevicePrefix(string path)
    {
        return path.Length >= 4 &&
               IsDirectorySeparator(path[0]) &&
               IsDirectorySeparator(path[1]) &&
               path[2] is '?' or '.' &&
               IsDirectorySeparator(path[3]);
    }

    private static bool HasWindowsDeviceUncPrefix(string path)
    {
        return path.Length >= 8 &&
               HasWindowsDevicePrefix(path) &&
               path[4] is 'U' or 'u' &&
               path[5] is 'N' or 'n' &&
               path[6] is 'C' or 'c' &&
               IsDirectorySeparator(path[7]);
    }

    private static bool TryCanonicalizeRawWindowsNtPath(
        string path,
        out string canonicalPath)
    {
        canonicalPath = path;
        if (!TryGetRawWindowsNtDosDevicesAliasEndIndex(path, out int namespaceComponentEndIndex) ||
            namespaceComponentEndIndex == path.Length)
        {
            return false;
        }

        canonicalPath = $"{path[0]}{path[0]}?{path[namespaceComponentEndIndex..]}";
        return true;
    }

    private static bool HasRawWindowsNtNamespacePrefix(string path)
    {
        return HasRawWindowsNtDosDevicesPrefix(path) ||
               HasRawWindowsNtDevicePrefix(path);
    }

    private static bool HasRawWindowsNtDosDevicesPrefix(string path)
    {
        return TryGetRawWindowsNtDosDevicesAliasEndIndex(path, out _);
    }

    private static bool TryGetRawWindowsNtDosDevicesAliasEndIndex(
        string path,
        out int namespaceComponentEndIndex)
    {
        namespaceComponentEndIndex = 0;
        if (!HasSingleLeadingDirectorySeparator(path))
        {
            return false;
        }

        int firstSeparatorIndex = FindDirectorySeparator(path, 1);
        namespaceComponentEndIndex = firstSeparatorIndex >= 0
            ? firstSeparatorIndex
            : path.Length;
        return IsRawWindowsNtDosDevicesAlias(
            path.AsSpan(1, namespaceComponentEndIndex - 1));
    }

    private static bool HasRawWindowsNtDevicePrefix(string path)
    {
        return HasSingleLeadingDirectorySeparator(path) &&
               path.Length >= 7 &&
               path[1] is 'D' or 'd' &&
               path[2] is 'E' or 'e' &&
               path[3] is 'V' or 'v' &&
               path[4] is 'I' or 'i' &&
               path[5] is 'C' or 'c' &&
               path[6] is 'E' or 'e' &&
               (path.Length == 7 || IsDirectorySeparator(path[7]));
    }

    private static bool HasDoubleLeadingRawWindowsNtDosDevicesAliasLookalikePrefix(string path)
    {
        if (!TryGetWindowsUncHostAndShareComponentBounds(
                path,
                out int hostStartIndex,
                out int serverSeparatorIndex,
                out int shareStartIndex,
                out int shareEndIndex) ||
            !IsRawWindowsNtDosDevicesAlias(
                path.AsSpan(hostStartIndex, serverSeparatorIndex - hostStartIndex)))
        {
            return false;
        }

        ReadOnlySpan<char> shareComponent =
            path.AsSpan(shareStartIndex, shareEndIndex - shareStartIndex);
        return IsWindowsDriveDesignatorComponent(shareComponent) ||
               shareComponent.Equals("UNC".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               IsCanonicalWindowsVolumeComponent(shareComponent);
    }

    private static bool HasNonFileBackedRawWindowsObjectManagerRoot(string path)
    {
        if (!HasSingleLeadingDirectorySeparator(path) ||
            TryGetRawWindowsNtDosDevicesAliasEndIndex(path, out _) ||
            HasRawWindowsNtDevicePrefix(path))
        {
            return false;
        }

        int rootComponentEndIndex = FindDirectorySeparator(path, 1);
        if (rootComponentEndIndex < 0)
        {
            rootComponentEndIndex = path.Length;
        }

        return IsNonFileBackedRawWindowsObjectManagerRoot(
            path.AsSpan(1, rootComponentEndIndex - 1));
    }

    private static bool IsRawWindowsNtDosDevicesAlias(ReadOnlySpan<char> namespaceAlias)
    {
        return namespaceAlias.Equals("??".AsSpan(), StringComparison.Ordinal) ||
               namespaceAlias.Equals("DosDevices".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               namespaceAlias.Equals("Global??".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNonFileBackedRawWindowsObjectManagerRoot(
        ReadOnlySpan<char> rootComponent)
    {
        return rootComponent.Equals("ArcName".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals(
                   "BaseNamedObjects".AsSpan(),
                   StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("Callback".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("Driver".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("FileSystem".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("GLOBALROOT".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("KernelObjects".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("KnownDlls".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("KnownDlls32".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("NLS".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("ObjectTypes".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("Registry".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("RPC Control".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("SECURITY".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               rootComponent.Equals("Sessions".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static int FindDirectorySeparator(string path, int startIndex)
    {
        for (var index = startIndex; index < path.Length; index++)
        {
            if (IsDirectorySeparator(path[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool ContainsOnlyDirectorySeparators(string path, int startIndex)
    {
        for (var index = startIndex; index < path.Length; index++)
        {
            if (!IsDirectorySeparator(path[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value is '/' or '\\';
    }

    private static bool HasRepeatedLeadingDirectorySeparators(string path)
    {
        return path.Length >= 3 &&
               IsDirectorySeparator(path[0]) &&
               IsDirectorySeparator(path[1]) &&
               IsDirectorySeparator(path[2]);
    }

    private static bool HasSingleLeadingWindowsPseudoDevicePrefix(string path)
    {
        return HasSingleLeadingDirectorySeparator(path) &&
               path.Length >= 3 &&
               path[1] is '?' or '.' &&
               IsDirectorySeparator(path[2]);
    }

    private static bool HasSingleLeadingDirectorySeparator(string path)
    {
        return path.Length != 0 &&
               IsDirectorySeparator(path[0]) &&
               (path.Length == 1 || !IsDirectorySeparator(path[1]));
    }

    private static bool HasMalformedWindowsDriveDesignator(string path)
    {
        return HasWindowsDriveDesignator(path) &&
               (!IsAsciiWindowsDriveLetter(path[0]) ||
                (path.Length >= 3 && path[2] == ':'));
    }

    private static bool HasWindowsDriveDesignator(string path)
    {
        return path.Length >= 2 &&
               path[1] == ':';
    }

    private static bool IsWindowsDrivePath(string path)
    {
        return HasWindowsDriveDesignator(path) &&
               IsAsciiWindowsDriveLetter(path[0]);
    }

    private static bool IsWindowsDriveDesignatorComponent(ReadOnlySpan<char> component)
    {
        return component.Length == 2 &&
               IsAsciiWindowsDriveLetter(component[0]) &&
               component[1] == ':';
    }

    private static bool IsCanonicalWindowsVolumeComponent(ReadOnlySpan<char> component)
    {
        const int volumeGuidStartIndex = 7;
        const int volumeGuidLength = 36;
        const int volumeGuidEndBraceIndex = volumeGuidStartIndex + volumeGuidLength;

        return component.Length == volumeGuidEndBraceIndex + 1 &&
               (component[0] is 'V' or 'v') &&
               (component[1] is 'O' or 'o') &&
               (component[2] is 'L' or 'l') &&
               (component[3] is 'U' or 'u') &&
               (component[4] is 'M' or 'm') &&
               (component[5] is 'E' or 'e') &&
               component[6] == '{' &&
               component[volumeGuidEndBraceIndex] == '}' &&
               IsCanonicalWindowsVolumeGuid(
                   component.Slice(volumeGuidStartIndex, volumeGuidLength));
    }

    private static bool IsInvalidExecutableBasename(string fileName)
    {
        return string.IsNullOrWhiteSpace(fileName) ||
               fileName is "." or "..";
    }

    private static bool IsAsciiWindowsDriveLetter(char value)
    {
        return value is >= 'A' and <= 'Z' ||
               value is >= 'a' and <= 'z';
    }

    private static bool IsSafeWindowsUncAuthorityComponent(string authorityComponent)
    {
        if (authorityComponent.Length == 0 ||
            authorityComponent is "." or "..")
        {
            return false;
        }

        for (var index = 0; index < authorityComponent.Length; index++)
        {
            if (char.IsControl(authorityComponent[index]) ||
                char.IsWhiteSpace(authorityComponent[index]) ||
                authorityComponent[index] == '\0' ||
                authorityComponent[index] is '<' or '>' or '"' or '[' or ']' or ':' or
                    '|' or '?' or '*' ||
                CharUnicodeInfo.GetUnicodeCategory(
                    authorityComponent,
                    index) == UnicodeCategory.Format)
            {
                return false;
            }

            if (char.IsHighSurrogate(authorityComponent[index]) &&
                index + 1 < authorityComponent.Length &&
                char.IsLowSurrogate(authorityComponent[index + 1]))
            {
                index++;
            }
        }

        return true;
    }

    private static bool IsSafeWindowsUncShareComponent(ReadOnlySpan<char> shareComponent)
    {
        if (shareComponent.Length == 0 ||
            shareComponent.Length > 80 ||
            shareComponent[^1] is '.' or ' ')
        {
            return false;
        }

        foreach (char value in shareComponent)
        {
            if (char.IsControl(value) ||
                value is '\0' or '"' or '\\' or '/' or '[' or ']' or ':' or '|' or '<' or
                    '>' or '+' or '=' or ';' or ',' or '*' or '?')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWindowsUncIpcShare(ReadOnlySpan<char> shareComponent)
    {
        return shareComponent.Equals("pipe".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               shareComponent.Equals("mailslot".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               shareComponent.Equals("IPC$".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCanonicalWindowsVolumeGuid(ReadOnlySpan<char> value)
    {
        if (value.Length != 36)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (index is 8 or 13 or 18 or 23)
            {
                if (value[index] != '-')
                {
                    return false;
                }

                continue;
            }

            if (!IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexDigit(char value)
    {
        return value is >= '0' and <= '9' ||
               value is >= 'A' and <= 'F' ||
               value is >= 'a' and <= 'f';
    }

    private static bool IsDominated(
        IReadOnlyList<MatchCandidate> matches,
        int candidateIndex,
        bool useWindowsExecutableSemantics)
    {
        for (var index = 0; index < matches.Count; index++)
        {
            if (index == candidateIndex)
            {
                continue;
            }

            if (StrictlyDominates(
                    matches[index],
                    matches[candidateIndex],
                    useWindowsExecutableSemantics))
            {
                return true;
            }
        }

        return false;
    }

    private static InvocationResolutionResult ResolveMatchedCandidates(
        AdapterDescriptor descriptor,
        string? executablePath,
        string? executableName,
        ReadOnlyCollection<string> copiedArguments,
        IReadOnlyList<MatchCandidate> matchedCandidates,
        bool useWindowsExecutableSemantics)
    {
        MatchCandidate[] bestMatches = matchedCandidates
            .Where((candidate, index) => !IsDominated(
                matchedCandidates,
                index,
                useWindowsExecutableSemantics))
            .ToArray();
        if (bestMatches.Length != 1)
        {
            return InvocationResolutionResult.Ambiguous(bestMatches);
        }

        return InvocationResolutionResult.Success(
            CreateInvocationContext(
                descriptor,
                executablePath,
                executableName,
                copiedArguments,
                bestMatches[0]));
    }

    private static AdapterInvocationContext CreateInvocationContext(
        AdapterDescriptor descriptor,
        string? executablePath,
        string? executableName,
        ReadOnlyCollection<string> copiedArguments,
        MatchCandidate candidate)
    {
        return new AdapterInvocationContext(
            descriptor,
            candidate.Entrypoint,
            executablePath,
            executableName,
            copiedArguments,
            candidate.MatchedArguments,
            candidate.PayloadArguments);
    }

    private static bool StrictlyDominates(
        MatchCandidate left,
        MatchCandidate right,
        bool useWindowsExecutableSemantics)
    {
        ConstraintRelation executableRelation = CompareMatchedExecutableConstraints(
            left,
            right);
        if (executableRelation == ConstraintRelation.Superset ||
            executableRelation == ConstraintRelation.Incomparable)
        {
            return false;
        }

        ConstraintRelation argumentRelation = CompareArgumentConstraints(
            left.Entrypoint,
            right.Entrypoint);
        if (argumentRelation == ConstraintRelation.Superset ||
            argumentRelation == ConstraintRelation.Incomparable)
        {
            return false;
        }

        return executableRelation == ConstraintRelation.Subset ||
               argumentRelation == ConstraintRelation.Subset ||
               (CompareDeclaredExecutableConstraints(
                    left.Entrypoint,
                    right.Entrypoint,
                    useWindowsExecutableSemantics) == ConstraintRelation.Subset &&
                executableRelation == ConstraintRelation.Equal &&
                argumentRelation == ConstraintRelation.Equal);
    }

    private static ConstraintRelation CompareMatchedExecutableConstraints(
        MatchCandidate left,
        MatchCandidate right)
    {
        if (left.MatchedExecutableNames is null &&
            right.MatchedExecutableNames is null)
        {
            return ConstraintRelation.Equal;
        }

        if (left.MatchedExecutableNames is null)
        {
            return ConstraintRelation.Superset;
        }

        if (right.MatchedExecutableNames is null)
        {
            return ConstraintRelation.Subset;
        }

        bool leftSubsetOfRight =
            left.MatchedExecutableNames.IsSubsetOf(right.MatchedExecutableNames);
        bool rightSubsetOfLeft =
            right.MatchedExecutableNames.IsSubsetOf(left.MatchedExecutableNames);
        if (leftSubsetOfRight &&
            rightSubsetOfLeft)
        {
            return ConstraintRelation.Equal;
        }

        if (leftSubsetOfRight)
        {
            return ConstraintRelation.Subset;
        }

        if (rightSubsetOfLeft)
        {
            return ConstraintRelation.Superset;
        }

        return ConstraintRelation.Incomparable;
    }

    private static ConstraintRelation CompareDeclaredExecutableConstraints(
        AdapterEntrypointDescriptor left,
        AdapterEntrypointDescriptor right,
        bool useWindowsExecutableSemantics)
    {
        return AdapterEntrypointBoundaryRelations.CompareDeclaredExecutableConstraints(
            left,
            right,
            useWindowsExecutableSemantics);
    }

    private static ConstraintRelation CompareArgumentConstraints(
        AdapterEntrypointDescriptor left,
        AdapterEntrypointDescriptor right)
    {
        return AdapterEntrypointBoundaryRelations.CompareArgumentConstraints(left, right);
    }

    private static bool HasProtocolBoundaryConflict(
        AdapterDescriptor descriptor,
        string? executableName,
        ReadOnlyCollection<string> arguments,
        bool useWindowsExecutableSemantics,
        bool ignoreExecutableBoundary)
    {
        if (arguments.Count == 0)
        {
            return false;
        }

        return descriptor.Entrypoints.Any(
            entrypoint => entrypoint.Mode == AdapterInvocationMode.Protocol &&
                          AdapterEntrypointBoundaryRelations.HasProtocolBoundaryConflict(
                              entrypoint,
                              executableName,
                              arguments,
                              useWindowsExecutableSemantics,
                              ignoreExecutableBoundary));
    }

    private static InvalidOperationException CreateAmbiguousMatchException(
        AdapterDescriptor descriptor,
        IReadOnlyCollection<MatchCandidate> matches)
    {
        string[] entrypointNames = matches
            .Select(static match => match.Entrypoint.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        string joinedEntrypointNames = string.Join(", ", entrypointNames);
        return new InvalidOperationException(
            $"Adapter descriptor '{descriptor.Name}' has ambiguous entrypoint matches " +
            $"for the current invocation boundary: {joinedEntrypointNames}.");
    }

    private readonly record struct InvocationResolutionResult(
        InvocationResolutionStatus Status,
        AdapterInvocationContext? Context,
        IReadOnlyCollection<MatchCandidate>? AmbiguousMatches)
    {
        public static InvocationResolutionResult NoMatch { get; } = new(
            InvocationResolutionStatus.NoMatch,
            null,
            null);

        public static InvocationResolutionResult Success(AdapterInvocationContext context)
        {
            return new InvocationResolutionResult(
                InvocationResolutionStatus.Success,
                context,
                null);
        }

        public static InvocationResolutionResult Ambiguous(
            IReadOnlyCollection<MatchCandidate> matches)
        {
            return new InvocationResolutionResult(
                InvocationResolutionStatus.Ambiguous,
                null,
                matches);
        }
    }

    private enum InvocationResolutionStatus
    {
        Success = 0,
        NoMatch = 1,
        Ambiguous = 2,
    }

    private readonly record struct MatchCandidate(
        AdapterEntrypointDescriptor Entrypoint,
        IReadOnlyList<string> MatchedArguments,
        IReadOnlyList<string> PayloadArguments,
        HashSet<string>? MatchedExecutableNames);

    private readonly record struct ExecutablePathParseResult(
        ExecutablePathParseStatus Status,
        string? ExecutableName)
    {
        public static ExecutablePathParseResult Missing { get; } = new(
            ExecutablePathParseStatus.Missing,
            null);

        public static ExecutablePathParseResult Invalid { get; } = new(
            ExecutablePathParseStatus.Invalid,
            null);

        public static ExecutablePathParseResult Success(string executableName)
        {
            return new ExecutablePathParseResult(
                ExecutablePathParseStatus.Success,
                executableName);
        }
    }

    private enum ExecutablePathParseStatus
    {
        Missing = 0,
        Success = 1,
        Invalid = 2,
    }

}
