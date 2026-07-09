using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record YarnPhase13VerticalSliceOptions
{
    public IFileSystem? FileSystem { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public string? WorkspaceDirectoryPath { get; init; }

    public string? UserHomeDirectoryPath { get; init; }

    public string? UserYarnrcPath { get; init; }
}

public sealed record YarnPhase13RegistryDeclaration
{
    public required string SourcePath { get; init; }

    public required string Key { get; init; }

    public string? Scope { get; init; }

    public required Uri RegistryUrl { get; init; }

    public required CanonicalResourceIdentity ResourceIdentity { get; init; }

    public required string NpmRegistriesKey { get; init; }
}

public sealed record YarnPhase13CredentialPlanRequest
{
    public required YarnPhase13RegistryDeclaration Declaration { get; init; }

    public required string AuthToken { get; init; }

    public string? TargetYarnrcPath { get; init; }

    public string? TemporaryHomePath { get; init; }
}

public sealed record YarnPhase13AuthIdentConflict
{
    public required string SourcePath { get; init; }

    public required string RegistryKey { get; init; }

    public required string Key { get; init; }
}

public sealed record YarnPhase13DoctorResult
{
    public required string? WorkspaceYarnrcPath { get; init; }

    public required bool WorkspaceYarnrcExists { get; init; }

    public required string EffectiveUserYarnrcPath { get; init; }

    public required bool EffectiveUserYarnrcExists { get; init; }

    public required string? YarnRcFilenameOverride { get; init; }

    public required IReadOnlyList<YarnPhase13RegistryDeclaration> RegistryDeclarations
    {
        get;
        init;
    }

    public required IReadOnlyList<YarnPhase13AuthIdentConflict> AuthIdentConflicts { get; init; }

    public required bool AzureArtifactsYarnEndpointCanonicalizationSuccess { get; init; }

    public required string WriteGateStatus { get; init; }

    public required string UnsupportedWriteMessage { get; init; }

    public required bool WritesSupported { get; init; }

    public bool RegistryDeclarationDiscovered => RegistryDeclarations.Count > 0;

    public bool YarnRcFilenameOverridePresent => YarnRcFilenameOverride is not null;

    public bool ForbiddenAuthIdentConflictDetected => AuthIdentConflicts.Count > 0;
}

public sealed class YarnPhase13VerticalSliceService
{
    private const string WorkspaceYarnrcFileName = ".yarnrc.yml";
    private const string YarnRcFilenameEnvironmentVariable = "YARN_RC_FILENAME";
    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase13";
    private const string ManifestId = "phase13-yarnrc-credential";
    private const string ConfigurePlanId = "phase13-yarnrc-credential-plan";
    private const string ConfigureChangeSetId = "phase13-yarnrc-credential-changeset";
    private const string WriteGateStatusValue = "phase-1.4-accepted; writes-supported-by-phase-13b";
    private const string UnsupportedWriteMessageValue =
        "Yarn writes are supported by Phase 13B configuration-manager write plans.";

    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly string? workspaceDirectoryPath;
    private readonly string? userHomeDirectoryPath;
    private readonly string? userYarnrcPath;

    public YarnPhase13VerticalSliceService(YarnPhase13VerticalSliceOptions? options = null)
    {
        options ??= new YarnPhase13VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        workspaceDirectoryPath = NormalizeOptionalPath(options.WorkspaceDirectoryPath);
        userHomeDirectoryPath = NormalizeOptionalPath(options.UserHomeDirectoryPath);
        userYarnrcPath = NormalizeOptionalPath(options.UserYarnrcPath);
    }

    public IReadOnlyList<YarnPhase13RegistryDeclaration> DiscoverRegistryDeclarations()
    {
        string? workspaceYarnrcPath = GetWorkspaceYarnrcPath();
        if (workspaceYarnrcPath is not null && fileSystem.FileExists(workspaceYarnrcPath))
        {
            YarnPhase13RegistryDeclaration[] workspaceDeclarations = ReadRegistryDeclarations(
                workspaceYarnrcPath
            );
            if (workspaceDeclarations.Length > 0)
            {
                return workspaceDeclarations;
            }
        }

        string resolvedUserYarnrcPath = ResolveUserYarnrcPath();
        return fileSystem.FileExists(resolvedUserYarnrcPath)
            ? ReadRegistryDeclarations(resolvedUserYarnrcPath)
            : [];
    }

    public ConfigurationChangePlan CreateUserCredentialPlan(
        YarnPhase13CredentialPlanRequest request
    )
    {
        ValidateCredentialPlanRequest(request);
        string targetYarnrcPath = fileSystem.GetFullPath(
            NullIfWhiteSpace(request.TargetYarnrcPath) ?? ResolveUserYarnrcPath()
        );
        ThrowIfForbiddenAuthIdentConflictExists(request.Declaration, targetYarnrcPath);

        return CreateCredentialPlan(
            request,
            targetYarnrcPath,
            ConfigurationScope.User,
            temporaryContainer: null,
            ConfigurationDeclarationPreservation.NotApplicable
        );
    }

    public ConfigurationChangePlan CreateCiTemporaryCredentialPlan(
        YarnPhase13CredentialPlanRequest request
    )
    {
        ValidateCredentialPlanRequest(request);
        ThrowIfCiTemporaryPlanWouldHideRegistryDeclaration(request.Declaration);
        ThrowIfCiTemporaryPlanWouldBeBypassedByYarnRcFilenameOverride();
        string temporaryHomePath =
            NullIfWhiteSpace(request.TemporaryHomePath)
            ?? throw new ArgumentException(
                "CI temporary Yarn plans require a product-owned temporary HOME path.",
                nameof(request)
            );
        temporaryHomePath = fileSystem.GetFullPath(temporaryHomePath);
        string targetYarnrcPath = fileSystem.GetFullPath(
            Path.Combine(temporaryHomePath, WorkspaceYarnrcFileName)
        );
        ThrowIfForbiddenAuthIdentConflictExists(request.Declaration, targetYarnrcPath);
        var temporaryContainer = new ConfigurationTemporaryContainer
        {
            Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
            ProductOwnedPath = temporaryHomePath,
            ActivationEnvironment = CreateTemporaryHomeActivationEnvironment(temporaryHomePath),
        };

        return CreateCredentialPlan(
            request,
            targetYarnrcPath,
            ConfigurationScope.CiTemporary,
            temporaryContainer,
            ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
        );
    }

    public ValueTask<YarnPhase13DoctorResult> RunDoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? workspaceYarnrcPath = GetWorkspaceYarnrcPath();
        string effectiveUserYarnrcPath = ResolveUserYarnrcPath();
        string? yarnRcFilenameOverride = GetYarnRcFilenameOverride();

        return ValueTask.FromResult(
            new YarnPhase13DoctorResult
            {
                WorkspaceYarnrcPath = workspaceYarnrcPath,
                WorkspaceYarnrcExists =
                    workspaceYarnrcPath is not null && fileSystem.FileExists(workspaceYarnrcPath),
                EffectiveUserYarnrcPath = effectiveUserYarnrcPath,
                EffectiveUserYarnrcExists = fileSystem.FileExists(effectiveUserYarnrcPath),
                YarnRcFilenameOverride = yarnRcFilenameOverride,
                RegistryDeclarations = DiscoverRegistryDeclarations(),
                AuthIdentConflicts = DiscoverAuthIdentConflicts(),
                AzureArtifactsYarnEndpointCanonicalizationSuccess =
                    CheckAzureArtifactsYarnEndpointCanonicalization(),
                WriteGateStatus = WriteGateStatusValue,
                UnsupportedWriteMessage = UnsupportedWriteMessageValue,
                WritesSupported = true,
            }
        );
    }

    private YarnPhase13RegistryDeclaration[] ReadRegistryDeclarations(string yarnrcPath)
    {
        string[] lines = SplitLines(fileSystem.ReadAllText(yarnrcPath));
        var declarations = new List<YarnPhase13RegistryDeclaration>();
        bool inNpmScopes = false;
        string? currentScope = null;
        foreach (string rawLine in lines)
        {
            string line = StripYamlComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmed = line.Trim();
            if (indent == 0)
            {
                inNpmScopes = IsYamlMapHeader(trimmed, "npmScopes");
                currentScope = null;
                if (
                    TryParseYamlKeyValue(trimmed, out string? key, out string? value)
                    && string.Equals(key, "npmRegistryServer", StringComparison.Ordinal)
                    && TryCreateRegistryDeclaration(
                        yarnrcPath,
                        key,
                        scope: null,
                        value,
                        out YarnPhase13RegistryDeclaration? declaration
                    )
                )
                {
                    declarations.Add(declaration);
                }

                continue;
            }

            if (!inNpmScopes)
            {
                continue;
            }

            if (indent == 2 && TryParseYamlMapKey(trimmed, out string? scopeName))
            {
                currentScope = NormalizeScopeName(scopeName);
                continue;
            }

            if (
                indent >= 4
                && currentScope is not null
                && TryParseYamlKeyValue(trimmed, out string? scopedKey, out string? scopedValue)
                && string.Equals(scopedKey, "npmRegistryServer", StringComparison.Ordinal)
                && TryCreateRegistryDeclaration(
                    yarnrcPath,
                    "npmScopes." + currentScope + ".npmRegistryServer",
                    currentScope,
                    scopedValue,
                    out YarnPhase13RegistryDeclaration? scopedDeclaration
                )
            )
            {
                declarations.Add(scopedDeclaration);
            }
        }

        return declarations.ToArray();
    }

    private void ThrowIfCiTemporaryPlanWouldBeBypassedByYarnRcFilenameOverride()
    {
        if (GetYarnRcFilenameOverride() is not null)
        {
            throw new InvalidOperationException(
                "CI temporary Yarn plans do not support YARN_RC_FILENAME because Yarn would "
                    + "bypass the product-owned temporary HOME/.yarnrc.yml target."
            );
        }
    }

    private void ThrowIfCiTemporaryPlanWouldHideRegistryDeclaration(
        YarnPhase13RegistryDeclaration declaration
    )
    {
        string userYarnrcPath = ResolveUserYarnrcPath();
        if (PathsEqual(declaration.SourcePath, userYarnrcPath))
        {
            throw new InvalidOperationException(
                "CI temporary Yarn auth-only plans require the registry declaration to remain "
                    + "visible outside the replaced HOME. Copying hidden declarations into "
                    + "temporary Yarnrc files is a separate Phase 13B follow-up."
            );
        }
    }

    private void ThrowIfForbiddenAuthIdentConflictExists(
        YarnPhase13RegistryDeclaration declaration,
        params string[] additionalYarnrcPaths
    )
    {
        foreach (
            YarnPhase13AuthIdentConflict conflict in DiscoverAuthIdentConflicts(
                additionalYarnrcPaths
            )
        )
        {
            if (
                AuthIdentConflictAppliesToDeclaration(conflict, declaration)
            )
            {
                throw new InvalidOperationException(
                    "Yarn npmAuthIdent entries conflict with product-owned npmAuthToken plans."
                );
            }
        }
    }

    private static bool AuthIdentConflictAppliesToDeclaration(
        YarnPhase13AuthIdentConflict conflict,
        YarnPhase13RegistryDeclaration declaration
    )
    {
        if (
            string.Equals(
                NormalizeComparableRegistryKey(conflict.RegistryKey),
                NormalizeComparableRegistryKey(declaration.NpmRegistriesKey),
                StringComparison.Ordinal
            )
        )
        {
            return true;
        }

        if (string.Equals(conflict.RegistryKey, "<global>", StringComparison.Ordinal))
        {
            return declaration.Scope is null;
        }

        if (declaration.Scope is not null)
        {
            return string.Equals(
                conflict.RegistryKey,
                "npmScopes." + declaration.Scope,
                StringComparison.Ordinal
            );
        }

        return false;
    }

    private static ConfigurationChangePlan CreateCredentialPlan(
        YarnPhase13CredentialPlanRequest request,
        string targetYarnrcPath,
        ConfigurationScope scope,
        ConfigurationTemporaryContainer? temporaryContainer,
        ConfigurationDeclarationPreservation declarationPreservation
    )
    {
        string authTokenKey = CreateNpmRegistriesAuthKey(
            request.Declaration.NpmRegistriesKey,
            "npmAuthToken"
        );
        string alwaysAuthKey = CreateNpmRegistriesAuthKey(
            request.Declaration.NpmRegistriesKey,
            "npmAlwaysAuth"
        );
        return ConfigurationChangePlanPolicy.Create(
            ConfigurePlanId,
            ConfigureChangeSetId,
            ProductId,
            scope,
            new ConfigurationManifestMetadata
            {
                ManifestId = ManifestId,
                OwnerProductId = ProductId,
                EntrySelector = authTokenKey,
                ResourceIdentity = request.Declaration.ResourceIdentity,
                ProductVersion = ProductVersion,
                SafeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ecosystem"] = "yarn",
                    ["registry-key"] = request.Declaration.Key,
                },
            },
            [
                CreateYarnrcChange(targetYarnrcPath, alwaysAuthKey, "true", isSecretValue: false),
                CreateYarnrcChange(
                    targetYarnrcPath,
                    authTokenKey,
                    request.AuthToken,
                    isSecretValue: true
                ),
            ],
            temporaryContainer: temporaryContainer,
            declarationPreservation: declarationPreservation,
            containsCredentialMaterial: true
        );
    }

    private static ConfigurationChange CreateYarnrcChange(
        string targetYarnrcPath,
        string key,
        string value,
        bool isSecretValue
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = targetYarnrcPath,
            Key = key,
            Value = value,
            IsSecretValue = isSecretValue,
            RequiresOwnershipRecord = true,
        };

    private static string CreateNpmRegistriesAuthKey(string npmRegistriesKey, string leafKey) =>
        "npmRegistries." + npmRegistriesKey + "." + leafKey;

    private static void ValidateCredentialPlanRequest(YarnPhase13CredentialPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AuthToken);
        if (
            request.AuthToken.Contains('\r', StringComparison.Ordinal)
            || request.AuthToken.Contains('\n', StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                "The Yarn npmAuthToken value must not contain CR or LF.",
                nameof(request)
            );
        }
    }

    private static ConfigurationActivationEnvironment CreateTemporaryHomeActivationEnvironment(
        string temporaryHomePath
    )
    {
        if (IsWindowsDrivePath(temporaryHomePath) || IsWindowsUncPath(temporaryHomePath))
        {
            return new ConfigurationActivationEnvironment
            {
                Platform = "windows",
                SetVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["USERPROFILE"] = temporaryHomePath,
                    ["HOME"] = temporaryHomePath,
                },
                ClearVariables = ["HOMEDRIVE", "HOMEPATH"],
            };
        }

        return new ConfigurationActivationEnvironment
        {
            Platform = "posix",
            SetVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOME"] = temporaryHomePath,
            },
            ClearVariables = [],
        };
    }

    private List<YarnPhase13AuthIdentConflict> DiscoverAuthIdentConflicts(
        params string[] additionalYarnrcPaths
    )
    {
        var conflicts = new List<YarnPhase13AuthIdentConflict>();
        var seenPaths = new List<string>();
        foreach (
            string yarnrcPath in GetReadableYarnrcPaths()
                .Concat(additionalYarnrcPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        )
        {
            string fullPath = fileSystem.GetFullPath(yarnrcPath);
            if (
                !fileSystem.FileExists(fullPath)
                || seenPaths.Any(seenPath => PathsEqual(seenPath, fullPath))
            )
            {
                continue;
            }

            seenPaths.Add(fullPath);
            AddAuthIdentConflicts(fullPath, conflicts);
        }

        return conflicts;
    }

    private void AddAuthIdentConflicts(
        string yarnrcPath,
        List<YarnPhase13AuthIdentConflict> conflicts
    )
    {
        string[] lines = SplitLines(fileSystem.ReadAllText(yarnrcPath));
        YarnAuthIdentContext context = YarnAuthIdentContext.None;
        string? currentRegistryKey = null;
        string? currentScope = null;
        foreach (string rawLine in lines)
        {
            string line = StripYamlComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmed = line.Trim();
            if (indent == 0)
            {
                currentRegistryKey = null;
                currentScope = null;
                if (IsYamlMapHeader(trimmed, "npmRegistries"))
                {
                    context = YarnAuthIdentContext.NpmRegistries;
                    continue;
                }

                if (IsYamlMapHeader(trimmed, "npmScopes"))
                {
                    context = YarnAuthIdentContext.NpmScopes;
                    continue;
                }

                context = YarnAuthIdentContext.None;
                if (
                    TryParseYamlKeyValue(
                        trimmed,
                        out string? topLevelKey,
                        out string? topLevelValue
                    )
                    && string.Equals(topLevelKey, "npmAuthIdent", StringComparison.Ordinal)
                    && !IsYamlNullOrEmpty(topLevelValue)
                )
                {
                    conflicts.Add(CreateAuthIdentConflict(yarnrcPath, "<global>", "npmAuthIdent"));
                }

                continue;
            }

            if (context == YarnAuthIdentContext.NpmRegistries)
            {
                if (indent == 2 && TryParseYamlMapKey(trimmed, out string? registryKey))
                {
                    currentRegistryKey = UnquoteYamlScalar(registryKey) ?? registryKey;
                    continue;
                }

                if (
                    indent >= 4
                    && currentRegistryKey is not null
                    && TryParseYamlKeyValue(
                        trimmed,
                        out string? registryAuthKey,
                        out string? registryAuthValue
                    )
                    && string.Equals(registryAuthKey, "npmAuthIdent", StringComparison.Ordinal)
                    && !IsYamlNullOrEmpty(registryAuthValue)
                )
                {
                    conflicts.Add(
                        CreateAuthIdentConflict(
                            yarnrcPath,
                            currentRegistryKey,
                            "npmRegistries." + currentRegistryKey + ".npmAuthIdent"
                        )
                    );
                }

                continue;
            }

            if (context != YarnAuthIdentContext.NpmScopes)
            {
                continue;
            }

            if (indent == 2 && TryParseYamlMapKey(trimmed, out string? scopeName))
            {
                currentScope = NormalizeScopeName(scopeName);
                continue;
            }

            if (
                indent >= 4
                && currentScope is not null
                && TryParseYamlKeyValue(
                    trimmed,
                    out string? scopeAuthKey,
                    out string? scopeAuthValue
                )
                && string.Equals(scopeAuthKey, "npmAuthIdent", StringComparison.Ordinal)
                && !IsYamlNullOrEmpty(scopeAuthValue)
            )
            {
                conflicts.Add(
                    CreateAuthIdentConflict(
                        yarnrcPath,
                        "npmScopes." + currentScope,
                        "npmScopes." + currentScope + ".npmAuthIdent"
                    )
                );
            }
        }
    }

    private IEnumerable<string> GetReadableYarnrcPaths()
    {
        string? workspaceYarnrcPath = GetWorkspaceYarnrcPath();
        if (workspaceYarnrcPath is not null && fileSystem.FileExists(workspaceYarnrcPath))
        {
            yield return workspaceYarnrcPath;
        }

        string userPath = ResolveUserYarnrcPath();
        if (
            fileSystem.FileExists(userPath)
            && (workspaceYarnrcPath is null || !PathsEqual(workspaceYarnrcPath, userPath))
        )
        {
            yield return userPath;
        }
    }

    private YarnPhase13AuthIdentConflict CreateAuthIdentConflict(
        string yarnrcPath,
        string registryKey,
        string key
    ) =>
        new()
        {
            SourcePath = fileSystem.GetFullPath(yarnrcPath),
            RegistryKey = registryKey,
            Key = key,
        };

    private bool TryCreateRegistryDeclaration(
        string sourcePath,
        string key,
        string? scope,
        string? value,
        [NotNullWhen(true)] out YarnPhase13RegistryDeclaration? declaration
    )
    {
        declaration = null;
        string? scalar = UnquoteYamlScalar(value);
        if (
            scalar is null
            || !Uri.TryCreate(scalar, UriKind.Absolute, out Uri? registryUrl)
            || !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                registryUrl,
                CredentialEcosystem.Yarn
            )
            || !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                registryUrl,
                out CanonicalResourceIdentity? resource
            )
        )
        {
            return false;
        }

        declaration = new YarnPhase13RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(sourcePath),
            Key = key,
            Scope = scope,
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            NpmRegistriesKey = registryUrl.AbsoluteUri,
        };
        return true;
    }

    private string ResolveUserYarnrcPath()
    {
        if (userYarnrcPath is not null)
        {
            return userYarnrcPath;
        }

        string home = userHomeDirectoryPath ?? GetHomeDirectory();
        return ResolveYarnrcPath(home);
    }

    private string? GetWorkspaceYarnrcPath() =>
        workspaceDirectoryPath is null
            ? null
            : ResolveYarnrcPath(workspaceDirectoryPath);

    private string ResolveYarnrcPath(string containingDirectoryPath)
    {
        string rcFilename = GetEffectiveYarnrcFileName();
        return IsRootedPath(rcFilename)
            ? fileSystem.GetFullPath(rcFilename)
            : fileSystem.GetFullPath(Path.Combine(containingDirectoryPath, rcFilename));
    }

    private string GetEffectiveYarnrcFileName() =>
        GetYarnRcFilenameOverride() ?? WorkspaceYarnrcFileName;

    private string? GetYarnRcFilenameOverride()
    {
        return NullIfWhiteSpace(environmentVariableReader(YarnRcFilenameEnvironmentVariable));
    }

    private string GetHomeDirectory()
    {
        string? home = NullIfWhiteSpace(environmentVariableReader("HOME"));
        if (home is not null)
        {
            return fileSystem.GetFullPath(home);
        }

        string? userProfile = NullIfWhiteSpace(environmentVariableReader("USERPROFILE"));
        if (userProfile is not null)
        {
            return fileSystem.GetFullPath(userProfile);
        }

        throw new InvalidOperationException("User profile directory is unavailable.");
    }

    private static bool CheckAzureArtifactsYarnEndpointCanonicalization()
    {
        try
        {
            return EndpointCanonicalizes(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/",
                    "org",
                    project: null,
                    feed: "feed"
                )
                && EndpointCanonicalizes(
                    "https://pkgs.dev.azure.com/org/project/_packaging/feed/npm/registry/",
                    "org",
                    "project",
                    "feed"
                )
                && EndpointCanonicalizes(
                    "https://dev.azure.com/org/project/_packaging/feed/npm/registry/",
                    "org",
                    "project",
                    "feed"
                )
                && !EndpointCanonicalizes(
                    "https://registry.yarnpkg.com/",
                    "org",
                    project: null,
                    feed: "feed"
                );
        }
        catch (Exception exception)
            when (IsExpectedDoctorProbeFailure(exception))
        {
            return false;
        }
    }

    private static bool EndpointCanonicalizes(
        string registryUrl,
        string organization,
        string? project,
        string feed
    )
    {
        if (
            !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                new Uri(registryUrl, UriKind.Absolute),
                out CanonicalResourceIdentity? resource
            )
        )
        {
            return false;
        }

        return string.Equals(resource.Organization, organization, StringComparison.Ordinal)
            && string.Equals(resource.Project, project, StringComparison.Ordinal)
            && string.Equals(resource.Feed, feed, StringComparison.Ordinal);
    }

    private string? NormalizeOptionalPath(string? path) =>
        NullIfWhiteSpace(path) is { } value ? fileSystem.GetFullPath(value) : null;

    private static bool TryParseYamlKeyValue(
        string text,
        [NotNullWhen(true)] out string? key,
        [NotNullWhen(true)] out string? value
    )
    {
        key = null;
        value = null;
        int colonIndex = FindUnquotedColon(text);
        if (colonIndex <= 0 || colonIndex == text.Length - 1)
        {
            return false;
        }

        key = UnquoteYamlScalar(text[..colonIndex].Trim());
        value = text[(colonIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryParseYamlMapKey(
        string text,
        [NotNullWhen(true)] out string? key
    )
    {
        key = null;
        int colonIndex = FindUnquotedColon(text);
        if (colonIndex <= 0 || colonIndex != text.Length - 1)
        {
            return false;
        }

        key = UnquoteYamlScalar(text[..colonIndex].Trim());
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool IsYamlMapHeader(string text, string expectedKey) =>
        TryParseYamlMapKey(text, out string? key)
        && string.Equals(key, expectedKey, StringComparison.Ordinal);

    private static int FindUnquotedColon(string text)
    {
        char? quote = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quote is not null)
            {
                if (current == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == ':')
            {
                return index;
            }
        }

        return -1;
    }

    private static string StripYamlComment(string text)
    {
        char? quote = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (quote is not null)
            {
                if (current == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == '#')
            {
                return text[..index];
            }
        }

        return text;
    }

    private static string? UnquoteYamlScalar(string? value)
    {
        string? trimmed = NullIfWhiteSpace(value);
        if (trimmed is null || IsYamlNull(trimmed))
        {
            return null;
        }

        if (
            trimmed.Length >= 2
            && trimmed[0] == '\''
            && trimmed[^1] == '\''
        )
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (
            trimmed.Length >= 2
            && trimmed[0] == '"'
            && trimmed[^1] == '"'
        )
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static string NormalizeScopeName(string scopeName) =>
        scopeName.StartsWith('@') ? scopeName[1..] : scopeName;

    private static string NormalizeComparableRegistryKey(string registryKey)
    {
        if (
            Uri.TryCreate(registryKey, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return uri.AbsoluteUri;
        }

        if (registryKey.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + registryKey;
        }

        return registryKey;
    }

    private bool PathsEqual(string left, string right)
    {
        string normalizedLeft = fileSystem.GetFullPath(left);
        string normalizedRight = fileSystem.GetFullPath(right);
        return string.Equals(
            normalizedLeft,
            normalizedRight,
            IsWindowsLikePath(normalizedLeft) || IsWindowsLikePath(normalizedRight)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
    }

    private static bool IsRootedPath(string path) =>
        path.StartsWith('/')
        || IsWindowsDrivePath(path)
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsWindowsLikePath(string path) =>
        IsWindowsDrivePath(path)
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 3
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/')
        && char.IsAsciiLetter(path[0]);

    private static bool IsWindowsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsYamlNullOrEmpty(string? value) =>
        UnquoteYamlScalar(value) is not { Length: > 0 };

    private static bool IsYamlNull(string value) =>
        string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "~", StringComparison.Ordinal);

    private static int CountLeadingSpaces(string value)
    {
        int index = 0;
        while (index < value.Length && value[index] == ' ')
        {
            index++;
        }

        return index;
    }

    private static string[] SplitLines(string contents) =>
        contents.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static bool IsExpectedDoctorProbeFailure(Exception exception) =>
        exception is ArgumentException
            or IOException
            or InvalidOperationException
            or NotSupportedException
            or PlatformNotSupportedException
            or UnauthorizedAccessException;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal enum YarnAuthIdentContext
{
    None,
    NpmRegistries,
    NpmScopes,
}
