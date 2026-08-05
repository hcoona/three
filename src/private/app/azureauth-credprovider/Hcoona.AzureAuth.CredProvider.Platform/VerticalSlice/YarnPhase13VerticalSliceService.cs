using System.Diagnostics.CodeAnalysis;
using System.Text;
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

    // editorconfig-checker-disable
    public required IReadOnlyList<YarnPhase13RegistryDeclaration> RegistryDeclarations { get; init; }

    // editorconfig-checker-enable

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
        foreach (string workspaceYarnrcPath in GetReadableWorkspaceYarnrcPaths())
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
        ThrowIfRepositoryLocalCredentialTarget(targetYarnrcPath);
        ThrowIfProjectAuthWouldShadowPlan(request.Declaration, targetYarnrcPath);
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
        ThrowIfProjectAuthWouldShadowPlan(request.Declaration, targetYarnrcPath);
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
        int? scopeIndent = null;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = StripYamlComment(lines[lineIndex]);
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
                scopeIndent = null;
                if (
                    TryParseYamlKeyValue(trimmed, out string? key, out string? value)
                )
                {
                    if (
                        string.Equals(key, "npmRegistryServer", StringComparison.Ordinal)
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
                    else if (
                        string.Equals(key, "npmScopes", StringComparison.Ordinal)
                        && TryCollectFlowMappingValue(
                            lines,
                            ref lineIndex,
                            value,
                            out string? flowValue
                        )
                        && TryCreateFlowProjectAuthBlocks(
                            YarnAuthIdentContext.NpmScopes,
                            flowValue!,
                            out List<YarnProjectAuthBlock>? flowBlocks
                        )
                    )
                    {
                        foreach (
                            YarnProjectAuthBlock block in flowBlocks!.Where(static block =>
                                block.Scope is not null && block.RegistryKey is not null
                            )
                        )
                        {
                            if (
                                TryCreateRegistryDeclaration(
                                    yarnrcPath,
                                    "npmScopes." + block.Scope + ".npmRegistryServer",
                                    block.Scope,
                                    block.RegistryKey!,
                                    out YarnPhase13RegistryDeclaration? flowScopedDeclaration
                                )
                            )
                            {
                                declarations.Add(flowScopedDeclaration);
                            }
                        }
                    }
                }

                continue;
            }

            if (!inNpmScopes)
            {
                continue;
            }

            scopeIndent ??= indent;
            if (indent == scopeIndent && TryParseYamlMapKey(trimmed, out string? scopeName))
            {
                currentScope = NormalizeScopeName(scopeName);
                continue;
            }

            if (
                indent > scopeIndent.Value
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
            if (AuthIdentConflictAppliesToDeclaration(conflict, declaration))
            {
                throw new InvalidOperationException(
                    "Yarn npmAuthIdent entries conflict with product-owned npmAuthToken plans."
                );
            }
        }
    }

    private void ThrowIfProjectAuthWouldShadowPlan(
        YarnPhase13RegistryDeclaration declaration,
        string targetYarnrcPath
    )
    {
        string plannedRegistry = NormalizeComparableRegistryKey(declaration.NpmRegistriesKey);
        YarnProjectAuthBlock? shadow = GetReadableWorkspaceYarnrcPaths()
            .Where(path => !PathsEqual(path, targetYarnrcPath))
            .SelectMany(ReadProjectAuthBlocks)
            .FirstOrDefault(block =>
                block.ShadowingSelector is not null
                && ResolveEffectiveProjectAuthRegistry(block) is { } effectiveRegistry
                && string.Equals(
                    NormalizeComparableRegistryKey(effectiveRegistry),
                    plannedRegistry,
                    StringComparison.Ordinal
                )
            );
        if (shadow is not null)
        {
            string shadowingSelector =
                shadow.RegistryKey is null
                    ? shadow.AuthenticationSelector!
                    : shadow.ShadowingSelector!;
            throw new InvalidOperationException(
                "Project-local Yarn selector "
                    + shadowingSelector
                    + " would shadow the planned user or CI credential."
            );
        }
    }

    private void ThrowIfRepositoryLocalCredentialTarget(string targetYarnrcPath)
    {
        if (
            workspaceDirectoryPath is null
            || PathsEqual(targetYarnrcPath, ResolveDefaultUserYarnrcPath())
        )
        {
            return;
        }

        string? repositoryRootPath = GetRepositoryRootPath();
        if (repositoryRootPath is not null)
        {
            if (
                FileSystemPathSemantics.IsSameOrDescendant(
                    fileSystem,
                    targetYarnrcPath,
                    repositoryRootPath
                )
            )
            {
                throw new InvalidOperationException(
                    "Repository-local Yarn configuration cannot store credential material."
                );
            }

            return;
        }

        string? targetDirectory = FileSystemPathSemantics.GetParentDirectory(
            fileSystem,
            targetYarnrcPath
        );
        for (
            string? directory = workspaceDirectoryPath;
            directory is not null;
            directory = FileSystemPathSemantics.GetParentDirectory(fileSystem, directory)
        )
        {
            if (targetDirectory is not null && PathsEqual(targetDirectory, directory))
            {
                throw new InvalidOperationException(
                    "Repository-local Yarn configuration cannot store credential material."
                );
            }
        }
    }

    private string? GetRepositoryRootPath()
    {
        string? repositoryRootPath = null;
        for (
            string? directory = workspaceDirectoryPath;
            directory is not null;
            directory = FileSystemPathSemantics.GetParentDirectory(fileSystem, directory)
        )
        {
            if (
                fileSystem.DirectoryExists(
                    FileSystemPathSemantics.Combine(fileSystem, directory, ".git")
                )
            )
            {
                return directory;
            }

            if (
                fileSystem.FileExists(
                    FileSystemPathSemantics.Combine(fileSystem, directory, "package.json")
                )
                || fileSystem.FileExists(
                    FileSystemPathSemantics.Combine(fileSystem, directory, "yarn.lock")
                )
            )
            {
                repositoryRootPath = directory;
            }
        }

        return repositoryRootPath;
    }

    private string? ResolveEffectiveProjectAuthRegistry(YarnProjectAuthBlock block)
    {
        if (block.RegistryKey is not null)
        {
            return block.RegistryKey;
        }

        if (block.Scope is null || block.AuthenticationSelector is null)
        {
            return null;
        }

        foreach (string yarnrcPath in GetReadableYarnrcPaths())
        {
            YarnPhase13RegistryDeclaration? scopedDeclaration = ReadRegistryDeclarations(
                yarnrcPath
            )
                .FirstOrDefault(declaration =>
                    declaration.Scope is not null
                    && string.Equals(
                        NormalizeScopeName(declaration.Scope),
                        block.Scope,
                        StringComparison.Ordinal
                    )
                );
            if (scopedDeclaration is not null)
            {
                return scopedDeclaration.NpmRegistriesKey;
            }
        }

        return null;
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
        NpmCompatibleAuthSelectors authSelectors = NpmCompatibleAuthSelectorPolicy.Create(
            request.Declaration.ResourceIdentity
        );
        string authTokenKey = authSelectors.YarnAuthTokenKey;
        string alwaysAuthKey = authSelectors.YarnAlwaysAuthKey;
        return ConfigurationChangePlanPolicy.Create(
            ConfigurePlanId,
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
                ClearVariables = ["HOMEDRIVE", "HOMEPATH", "YARN_RC_FILENAME"],
            };
        }

        return new ConfigurationActivationEnvironment
        {
            Platform = "posix",
            SetVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOME"] = temporaryHomePath,
            },
            ClearVariables = ["YARN_RC_FILENAME"],
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
        foreach (YarnProjectAuthBlock block in ReadFlowProjectAuthBlocks(yarnrcPath))
        {
            if (block.AuthIdentSelector is null)
            {
                continue;
            }

            conflicts.Add(
                CreateAuthIdentConflict(
                    yarnrcPath,
                    block.Scope is null
                        ? block.RegistryKey!
                        : "npmScopes." + block.Scope,
                    block.AuthIdentSelector
                )
            );
        }

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

    private List<YarnProjectAuthBlock> ReadFlowProjectAuthBlocks(string yarnrcPath)
    {
        var blocks = new List<YarnProjectAuthBlock>();
        string[] lines = SplitLines(fileSystem.ReadAllText(yarnrcPath));
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = StripYamlComment(lines[lineIndex]);
            if (string.IsNullOrWhiteSpace(line) || CountLeadingSpaces(line) != 0)
            {
                continue;
            }

            string trimmed = line.Trim();
            if (
                !TryParseYamlKeyValue(
                    trimmed,
                    out string? topLevelKey,
                    out string? topLevelValue
                )
                || !IsManagedProjectAuthMapKey(topLevelKey)
                || !TryCollectFlowMappingValue(
                    lines,
                    ref lineIndex,
                    topLevelValue,
                    out string? flowValue
                )
                || !TryCreateFlowProjectAuthBlocks(
                    string.Equals(topLevelKey, "npmRegistries", StringComparison.Ordinal)
                        ? YarnAuthIdentContext.NpmRegistries
                        : YarnAuthIdentContext.NpmScopes,
                    flowValue!,
                    out List<YarnProjectAuthBlock>? flowBlocks
                )
            )
            {
                continue;
            }

            blocks.AddRange(flowBlocks!);
        }

        return blocks;
    }

    private List<YarnProjectAuthBlock> ReadProjectAuthBlocks(string yarnrcPath)
    {
        var blocks = new List<YarnProjectAuthBlock>();
        YarnAuthIdentContext context = YarnAuthIdentContext.None;
        YarnProjectAuthBlock? currentBlock = null;
        int? blockIndent = null;
        int? settingIndent = null;
        bool settingAllowsNestedContent = false;
        string[] lines = SplitLines(fileSystem.ReadAllText(yarnrcPath));
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = StripYamlComment(lines[lineIndex]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmed = line.Trim();
            if (indent == 0)
            {
                currentBlock = null;
                blockIndent = null;
                settingIndent = null;
                settingAllowsNestedContent = false;
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

                if (
                    TryParseYamlKeyValue(
                        trimmed,
                        out string? topLevelKey,
                        out string? topLevelValue
                    )
                    && IsManagedProjectAuthMapKey(topLevelKey)
                )
                {
                    if (
                        TryCollectFlowMappingValue(
                            lines,
                            ref lineIndex,
                            topLevelValue,
                            out string? flowValue
                        )
                        && TryCreateFlowProjectAuthBlocks(
                            string.Equals(
                                topLevelKey,
                                "npmRegistries",
                                StringComparison.Ordinal
                            )
                                ? YarnAuthIdentContext.NpmRegistries
                                : YarnAuthIdentContext.NpmScopes,
                            flowValue!,
                            out List<YarnProjectAuthBlock>? flowBlocks
                        )
                    )
                    {
                        blocks.AddRange(flowBlocks!);
                        context = YarnAuthIdentContext.None;
                        continue;
                    }

                    if (!IsYamlNullOrEmpty(topLevelValue))
                    {
                        throw CreateMalformedProjectAuthStructureException(yarnrcPath);
                    }
                }

                context = YarnAuthIdentContext.None;
                continue;
            }

            if (context == YarnAuthIdentContext.None)
            {
                continue;
            }

            if (HasTabInLeadingWhitespace(line))
            {
                throw CreateMalformedProjectAuthStructureException(yarnrcPath);
            }

            blockIndent ??= indent;
            if (indent < blockIndent)
            {
                throw CreateMalformedProjectAuthStructureException(yarnrcPath);
            }

            if (indent == blockIndent)
            {
                if (!TryParseYamlMapKey(trimmed, out string? mapKey))
                {
                    throw CreateMalformedProjectAuthStructureException(yarnrcPath);
                }

                currentBlock = new YarnProjectAuthBlock(
                    context == YarnAuthIdentContext.NpmRegistries
                        ? UnquoteYamlScalar(mapKey) ?? mapKey
                        : null,
                    context == YarnAuthIdentContext.NpmScopes
                        ? NormalizeScopeName(mapKey)
                        : null
                );
                if (context != YarnAuthIdentContext.None)
                {
                    blocks.Add(currentBlock);
                }
                settingIndent = null;
                settingAllowsNestedContent = false;
                continue;
            }

            if (currentBlock is null)
            {
                throw CreateMalformedProjectAuthStructureException(yarnrcPath);
            }

            settingIndent ??= indent;
            if (indent < settingIndent || (indent > settingIndent && !settingAllowsNestedContent))
            {
                throw CreateMalformedProjectAuthStructureException(yarnrcPath);
            }

            if (indent > settingIndent)
            {
                continue;
            }

            if (TryParseYamlMapKey(trimmed, out _))
            {
                settingAllowsNestedContent = true;
                continue;
            }

            if (
                !TryParseYamlKeyValue(
                    trimmed,
                    out string? settingKey,
                    out string? settingValue
                )
            )
            {
                throw CreateMalformedProjectAuthStructureException(yarnrcPath);
            }

            settingAllowsNestedContent = AllowsNestedYamlContent(settingValue);
            if (
                context == YarnAuthIdentContext.NpmScopes
                && string.Equals(settingKey, "npmRegistryServer", StringComparison.Ordinal)
            )
            {
                currentBlock.RegistryKey = UnquoteYamlScalar(settingValue);
                continue;
            }

            currentBlock.RecordAuthSetting(settingKey, settingValue);
        }

        return blocks;
    }

    private static bool TryCollectFlowMappingValue(
        string[] lines,
        ref int lineIndex,
        string initialValue,
        [NotNullWhen(true)] out string? flowValue
    )
    {
        flowValue = null;
        string value = initialValue.Trim();
        if (!value.StartsWith('{'))
        {
            return false;
        }

        var builder = new StringBuilder(value);
        int balance = GetFlowCollectionBalance(value);
        while (balance > 0 && lineIndex + 1 < lines.Length)
        {
            lineIndex++;
            string continuation = StripYamlComment(lines[lineIndex]).Trim();
            if (continuation.Length == 0)
            {
                continue;
            }

            builder.Append(' ').Append(continuation);
            balance = GetFlowCollectionBalance(builder.ToString());
        }

        if (balance != 0)
        {
            return false;
        }

        flowValue = builder.ToString();
        return true;
    }

    private static int GetFlowCollectionBalance(string value)
    {
        int balance = 0;
        char? quote = null;
        bool escaped = false;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (quote is not null)
            {
                if (quote == '"' && current == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (current == quote && !escaped)
                {
                    if (
                        quote == '\''
                        && index + 1 < value.Length
                        && value[index + 1] == '\''
                    )
                    {
                        index++;
                        continue;
                    }

                    quote = null;
                }

                escaped = false;
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current is '{' or '[')
            {
                balance++;
            }
            else if (current is '}' or ']')
            {
                balance--;
                if (balance < 0)
                {
                    return -1;
                }
            }
        }

        return quote is null ? balance : -1;
    }

    private static bool TryCreateFlowProjectAuthBlocks(
        YarnAuthIdentContext context,
        string flowValue,
        [NotNullWhen(true)] out List<YarnProjectAuthBlock>? blocks
    )
    {
        blocks = null;
        if (
            !YamlFlowMappingParser.TryParse(flowValue, out List<YamlFlowEntry>? outerEntries)
        )
        {
            return false;
        }

        var parsedBlocks = new List<YarnProjectAuthBlock>();
        foreach (YamlFlowEntry outerEntry in outerEntries)
        {
            if (outerEntry.Value.Mapping is null)
            {
                return false;
            }

            var block = new YarnProjectAuthBlock(
                context == YarnAuthIdentContext.NpmRegistries ? outerEntry.Key : null,
                context == YarnAuthIdentContext.NpmScopes
                    ? NormalizeScopeName(outerEntry.Key)
                    : null
            );
            foreach (YamlFlowEntry setting in outerEntry.Value.Mapping)
            {
                if (
                    string.Equals(setting.Key, "npmRegistryServer", StringComparison.Ordinal)
                )
                {
                    if (setting.Value.Mapping is not null)
                    {
                        return false;
                    }

                    block.RegistryKey = UnquoteYamlScalar(setting.Value.Scalar);
                    continue;
                }

                if (
                    string.Equals(setting.Key, "npmAuthToken", StringComparison.Ordinal)
                    || string.Equals(setting.Key, "npmAuthIdent", StringComparison.Ordinal)
                    || string.Equals(setting.Key, "npmAlwaysAuth", StringComparison.Ordinal)
                )
                {
                    if (setting.Value.Mapping is not null)
                    {
                        return false;
                    }

                    block.RecordAuthSetting(setting.Key, setting.Value.Scalar);
                }
            }

            parsedBlocks.Add(block);
        }

        blocks = parsedBlocks;
        return true;
    }

    private static InvalidOperationException CreateMalformedProjectAuthStructureException(
        string yarnrcPath
    ) =>
        new(
            "Project-local Yarn npmRegistries or npmScopes structure is malformed; refusing "
                + "to create a credential plan for "
                + yarnrcPath
                + "."
        );

    private static bool IsManagedProjectAuthMapKey(string key) =>
        string.Equals(key, "npmRegistries", StringComparison.Ordinal)
        || string.Equals(key, "npmScopes", StringComparison.Ordinal);

    private static bool AllowsNestedYamlContent(string value) =>
        value.StartsWith('|') || value.StartsWith('>');

    private IEnumerable<string> GetReadableYarnrcPaths()
    {
        var seenPaths = new List<string>();
        foreach (string workspaceYarnrcPath in GetReadableWorkspaceYarnrcPaths())
        {
            seenPaths.Add(workspaceYarnrcPath);
            yield return workspaceYarnrcPath;
        }

        string userPath = ResolveUserYarnrcPath();
        if (
            fileSystem.FileExists(userPath)
            && !seenPaths.Any(workspacePath => PathsEqual(workspacePath, userPath))
        )
        {
            yield return userPath;
        }
    }

    private IEnumerable<string> GetReadableWorkspaceYarnrcPaths()
    {
        var seenPaths = new List<string>();
        foreach (string yarnrcPath in GetWorkspaceYarnrcPaths())
        {
            if (
                !seenPaths.Any(seenPath => PathsEqual(seenPath, yarnrcPath))
                && fileSystem.FileExists(yarnrcPath)
            )
            {
                seenPaths.Add(yarnrcPath);
                yield return yarnrcPath;
            }
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

        string? overridePath = GetYarnRcFilenameOverride();
        return overridePath is not null && IsRootedPath(overridePath)
            ? fileSystem.GetFullPath(overridePath)
            : ResolveDefaultUserYarnrcPath();
    }

    private string ResolveDefaultUserYarnrcPath()
    {
        string home = userHomeDirectoryPath ?? GetHomeDirectory();
        return FileSystemPathSemantics.Combine(fileSystem, home, WorkspaceYarnrcFileName);
    }

    private string? GetWorkspaceYarnrcPath()
    {
        if (workspaceDirectoryPath is null)
        {
            return null;
        }

        return GetWorkspaceYarnrcPaths().FirstOrDefault(fileSystem.FileExists)
            ?? ResolveYarnrcPath(workspaceDirectoryPath);
    }

    private IEnumerable<string> GetWorkspaceYarnrcPaths()
    {
        if (workspaceDirectoryPath is null)
        {
            yield break;
        }

        string rcFilename = GetEffectiveYarnrcFileName();
        if (IsRootedPath(rcFilename))
        {
            yield return fileSystem.GetFullPath(rcFilename);
            yield break;
        }

        for (
            string? directory = workspaceDirectoryPath;
            directory is not null;
            directory = FileSystemPathSemantics.GetParentDirectory(fileSystem, directory)
        )
        {
            yield return ResolveYarnrcPath(directory);
        }
    }

    private string ResolveYarnrcPath(string containingDirectoryPath)
    {
        string rcFilename = GetEffectiveYarnrcFileName();
        return IsRootedPath(rcFilename)
            ? fileSystem.GetFullPath(rcFilename)
            : FileSystemPathSemantics.Combine(fileSystem, containingDirectoryPath, rcFilename);
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
                && !EndpointCanonicalizes(
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
        catch (Exception exception) when (IsExpectedDoctorProbeFailure(exception))
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

    private static bool TryParseYamlMapKey(string text, [NotNullWhen(true)] out string? key)
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

            if (
                current == ':'
                && (index == text.Length - 1 || char.IsWhiteSpace(text[index + 1]))
            )
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

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
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
            return uri.AbsoluteUri.TrimEnd('/');
        }

        if (registryKey.StartsWith("//", StringComparison.Ordinal))
        {
            return ("https:" + registryKey).TrimEnd('/');
        }

        return registryKey.TrimEnd('/');
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

    private static bool HasTabInLeadingWhitespace(string value)
    {
        foreach (char character in value)
        {
            if (character == '\t')
            {
                return true;
            }

            if (character != ' ')
            {
                return false;
            }
        }

        return false;
    }

    private static string[] SplitLines(string contents) =>
        contents.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static bool IsExpectedDoctorProbeFailure(Exception exception) =>
        exception
            is ArgumentException
                or IOException
                or InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException
                or UnauthorizedAccessException;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record YamlFlowEntry(string Key, YamlFlowValue Value);

    private sealed record YamlFlowValue(string? Scalar, List<YamlFlowEntry>? Mapping);

    private sealed class YamlFlowMappingParser
    {
        private readonly string text;
        private int index;

        private YamlFlowMappingParser(string text) => this.text = text;

        public static bool TryParse(
            string text,
            [NotNullWhen(true)] out List<YamlFlowEntry>? entries
        )
        {
            var parser = new YamlFlowMappingParser(text);
            if (!parser.TryParseMapping(out entries))
            {
                entries = null;
                return false;
            }

            parser.SkipWhitespace();
            return parser.index == text.Length;
        }

        private bool TryParseMapping([NotNullWhen(true)] out List<YamlFlowEntry>? entries)
        {
            entries = null;
            SkipWhitespace();
            if (!TryConsume('{'))
            {
                return false;
            }

            var parsedEntries = new List<YamlFlowEntry>();
            SkipWhitespace();
            if (TryConsume('}'))
            {
                entries = parsedEntries;
                return true;
            }

            while (index < text.Length)
            {
                if (!TryReadKey(out string? key) || !TryConsume(':'))
                {
                    return false;
                }

                SkipWhitespace();
                YamlFlowValue value;
                if (index < text.Length && text[index] == '{')
                {
                    if (!TryParseMapping(out List<YamlFlowEntry>? nestedEntries))
                    {
                        return false;
                    }

                    value = new YamlFlowValue(null, nestedEntries);
                }
                else
                {
                    if (!TryReadScalar(out string? scalar))
                    {
                        return false;
                    }

                    value = new YamlFlowValue(scalar, null);
                }

                parsedEntries.Add(new YamlFlowEntry(key, value));
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    entries = parsedEntries;
                    return true;
                }

                if (!TryConsume(','))
                {
                    return false;
                }

                SkipWhitespace();
                if (TryConsume('}'))
                {
                    entries = parsedEntries;
                    return true;
                }
            }

            return false;
        }

        private bool TryReadKey([NotNullWhen(true)] out string? key)
        {
            key = null;
            SkipWhitespace();
            int start = index;
            char? quote = null;
            bool escaped = false;
            while (index < text.Length)
            {
                char current = text[index];
                if (quote is not null)
                {
                    index++;
                    if (quote == '"' && current == '\\' && !escaped)
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == quote && !escaped)
                    {
                        if (
                            quote == '\''
                            && index < text.Length
                            && text[index] == '\''
                        )
                        {
                            index++;
                            continue;
                        }

                        quote = null;
                    }

                    escaped = false;
                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    index++;
                    continue;
                }

                if (
                    current == ':'
                    && !(
                        index + 2 < text.Length
                        && text[index + 1] == '/'
                        && text[index + 2] == '/'
                    )
                )
                {
                    string rawKey = text[start..index].Trim();
                    key = UnquoteYamlScalar(rawKey);
                    return key is not null;
                }

                if (current is ',' or '}' or '{' or '[' or ']')
                {
                    return false;
                }

                index++;
            }

            return false;
        }

        private bool TryReadScalar([NotNullWhen(true)] out string? scalar)
        {
            scalar = null;
            SkipWhitespace();
            int start = index;
            char? quote = null;
            bool escaped = false;
            int sequenceDepth = 0;
            while (index < text.Length)
            {
                char current = text[index];
                if (quote is not null)
                {
                    index++;
                    if (quote == '"' && current == '\\' && !escaped)
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == quote && !escaped)
                    {
                        if (
                            quote == '\''
                            && index < text.Length
                            && text[index] == '\''
                        )
                        {
                            index++;
                            continue;
                        }

                        quote = null;
                    }

                    escaped = false;
                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    index++;
                    continue;
                }

                if (current == '[')
                {
                    sequenceDepth++;
                    index++;
                    continue;
                }

                if (current == ']')
                {
                    if (sequenceDepth == 0)
                    {
                        return false;
                    }

                    sequenceDepth--;
                    index++;
                    continue;
                }

                if (sequenceDepth == 0 && (current is ',' or '}'))
                {
                    break;
                }

                if (sequenceDepth == 0 && current == '{')
                {
                    return false;
                }

                index++;
            }

            if (quote is not null || sequenceDepth != 0)
            {
                return false;
            }

            scalar = text[start..index].Trim();
            return true;
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (index >= text.Length || text[index] != expected)
            {
                return false;
            }

            index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
    }

    private sealed class YarnProjectAuthBlock(string? registryKey, string? scope)
    {
        public string? RegistryKey { get; set; } = registryKey;

        public string? Scope { get; } = scope;

        public string? AuthenticationSelector { get; private set; }

        public string? AuthIdentSelector { get; private set; }

        public string? ShadowingSelector { get; private set; }

        public void RecordAuthSetting(string key, string? value)
        {
            bool authValuePresent =
                (
                    string.Equals(key, "npmAuthToken", StringComparison.Ordinal)
                    || string.Equals(key, "npmAuthIdent", StringComparison.Ordinal)
                ) && !IsYamlNullOrEmpty(value);
            bool alwaysAuthDisabled =
                string.Equals(key, "npmAlwaysAuth", StringComparison.Ordinal)
                && string.Equals(
                    UnquoteYamlScalar(value),
                    "false",
                    StringComparison.OrdinalIgnoreCase
                );
            string selector =
                (Scope is null ? "npmRegistries[registry]" : "npmScopes." + Scope) + "." + key;
            if (authValuePresent && AuthenticationSelector is null)
            {
                AuthenticationSelector = selector;
            }

            if (
                AuthIdentSelector is null
                && string.Equals(key, "npmAuthIdent", StringComparison.Ordinal)
                && authValuePresent
            )
            {
                AuthIdentSelector = selector;
            }

            if (ShadowingSelector is null && (authValuePresent || alwaysAuthDisabled))
            {
                ShadowingSelector = selector;
            }
        }
    }
}

internal enum YarnAuthIdentContext
{
    None,
    NpmRegistries,
    NpmScopes,
}
