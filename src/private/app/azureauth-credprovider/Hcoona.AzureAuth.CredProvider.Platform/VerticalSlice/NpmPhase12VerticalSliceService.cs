using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record NpmPhase12VerticalSliceOptions
{
    public IFileSystem? FileSystem { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public string? WorkspaceDirectoryPath { get; init; }

    public string? UserHomeDirectoryPath { get; init; }

    public string? UserNpmrcPath { get; init; }

    public string? CiTemporaryNpmrcPath { get; init; }
}

public sealed record NpmPhase12RegistryDeclaration
{
    public required string SourcePath { get; init; }

    public required string Key { get; init; }

    public required Uri RegistryUrl { get; init; }

    public required CanonicalResourceIdentity ResourceIdentity { get; init; }

    public required NpmCompatibleAuthSelectors AuthSelectors { get; init; }
}

public sealed record NpmPhase12CredentialPlanRequest
{
    public required NpmPhase12RegistryDeclaration Declaration { get; init; }

    public required string AuthToken { get; init; }

    public CredentialEcosystem Ecosystem { get; init; } = CredentialEcosystem.Npm;

    public string? TargetNpmrcPath { get; init; }
}

public sealed record NpmPhase12DoctorResult
{
    public required string? WorkspaceNpmrcPath { get; init; }

    public required bool WorkspaceNpmrcExists { get; init; }

    public required string EffectiveUserNpmrcPath { get; init; }

    public required bool EffectiveUserNpmrcExists { get; init; }

    public required string CiTemporaryNpmrcPath { get; init; }

    public required bool UppercaseUserConfigEnvironmentOverridePresent { get; init; }

    public required bool LowercaseUserConfigEnvironmentOverridePresent { get; init; }

    public required IReadOnlyList<NpmPhase12RegistryDeclaration> RegistryDeclarations { get; init; }

    public required bool AzureArtifactsNpmEndpointCanonicalizationSuccess { get; init; }

    public required bool NpmUserCredentialPlanValid { get; init; }

    public required bool PnpmUserCredentialPlanValid { get; init; }

    public required bool CiTemporaryCredentialPlanValid { get; init; }

    public bool RegistryDeclarationDiscovered => RegistryDeclarations.Count > 0;

    public bool EffectiveUserConfigEnvironmentOverridePresent =>
        UppercaseUserConfigEnvironmentOverridePresent
        || LowercaseUserConfigEnvironmentOverridePresent;

    public bool CiTemporaryAuthOnlyPlanSupported => CiTemporaryCredentialPlanValid;
}

public sealed class NpmPhase12VerticalSliceService
{
    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase12";
    private const string ManifestId = "phase12-npmrc-credential";
    private const string ConfigurePlanId = "phase12-npmrc-credential-plan";
    private const string NpmUserConfigEnvironmentVariable = "NPM_CONFIG_USERCONFIG";
    private const string LowercaseNpmUserConfigEnvironmentVariable = "npm_config_userconfig";
    private const string WorkspaceNpmrcFileName = ".npmrc";

    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly string? workspaceDirectoryPath;
    private readonly string? userHomeDirectoryPath;
    private readonly string? userNpmrcPath;
    private readonly string? ciTemporaryNpmrcPath;

    public NpmPhase12VerticalSliceService(NpmPhase12VerticalSliceOptions? options = null)
    {
        options ??= new NpmPhase12VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        workspaceDirectoryPath = NormalizeOptionalPath(options.WorkspaceDirectoryPath);
        userHomeDirectoryPath = NormalizeOptionalPath(options.UserHomeDirectoryPath);
        userNpmrcPath = NormalizeOptionalPath(options.UserNpmrcPath);
        ciTemporaryNpmrcPath = NormalizeOptionalPath(options.CiTemporaryNpmrcPath);
    }

    public IReadOnlyList<NpmPhase12RegistryDeclaration> DiscoverRegistryDeclarations()
    {
        string? workspaceNpmrcPath = GetWorkspaceNpmrcPath();
        if (workspaceNpmrcPath is not null && fileSystem.FileExists(workspaceNpmrcPath))
        {
            NpmPhase12RegistryDeclaration[] workspaceDeclarations = ReadRegistryDeclarations(
                workspaceNpmrcPath
            );
            if (workspaceDeclarations.Length > 0)
            {
                return workspaceDeclarations;
            }
        }

        string resolvedUserNpmrcPath = ResolveUserNpmrcPath();
        return fileSystem.FileExists(resolvedUserNpmrcPath)
            ? ReadRegistryDeclarations(resolvedUserNpmrcPath)
            : [];
    }

    public ValueTask<NpmPhase12DoctorResult> RunDoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? workspaceNpmrcPath = GetWorkspaceNpmrcPath();
        string effectiveUserNpmrcPath = ResolveUserNpmrcPath();
        string resolvedCiTemporaryNpmrcPath = ResolveCiTemporaryNpmrcPath();
        IReadOnlyList<NpmPhase12RegistryDeclaration> declarations = DiscoverRegistryDeclarations();
        NpmPhase12RegistryDeclaration? firstDeclaration =
            declarations.Count == 0 ? null : declarations[0];

        return ValueTask.FromResult(
            new NpmPhase12DoctorResult
            {
                WorkspaceNpmrcPath = workspaceNpmrcPath,
                WorkspaceNpmrcExists =
                    workspaceNpmrcPath is not null && fileSystem.FileExists(workspaceNpmrcPath),
                EffectiveUserNpmrcPath = effectiveUserNpmrcPath,
                EffectiveUserNpmrcExists = fileSystem.FileExists(effectiveUserNpmrcPath),
                CiTemporaryNpmrcPath = resolvedCiTemporaryNpmrcPath,
                UppercaseUserConfigEnvironmentOverridePresent =
                    NullIfWhiteSpace(environmentVariableReader(NpmUserConfigEnvironmentVariable))
                        is not null,
                LowercaseUserConfigEnvironmentOverridePresent =
                    NullIfWhiteSpace(
                        environmentVariableReader(LowercaseNpmUserConfigEnvironmentVariable)
                    )
                        is not null,
                RegistryDeclarations = declarations,
                AzureArtifactsNpmEndpointCanonicalizationSuccess =
                    CheckAzureArtifactsNpmEndpointCanonicalization(),
                NpmUserCredentialPlanValid = TryValidateUserCredentialPlan(
                    firstDeclaration,
                    CredentialEcosystem.Npm
                ),
                PnpmUserCredentialPlanValid = TryValidateUserCredentialPlan(
                    firstDeclaration,
                    CredentialEcosystem.Pnpm
                ),
                CiTemporaryCredentialPlanValid = TryValidateCiTemporaryCredentialPlan(
                    firstDeclaration,
                    resolvedCiTemporaryNpmrcPath
                ),
            }
        );
    }

    public ConfigurationChangePlan CreateUserCredentialPlan(NpmPhase12CredentialPlanRequest request)
    {
        ValidateCredentialPlanRequest(request);
        string targetNpmrcPath = fileSystem.GetFullPath(
            NullIfWhiteSpace(request.TargetNpmrcPath) ?? ResolveUserNpmrcPath()
        );
        ThrowIfProjectAuthWouldShadowPlan(
            request.Declaration,
            request.Ecosystem,
            targetNpmrcPath
        );

        return CreateCredentialPlan(
            request,
            targetNpmrcPath,
            ConfigurationScope.User,
            temporaryContainer: null,
            ConfigurationDeclarationPreservation.NotApplicable
        );
    }

    public ConfigurationChangePlan CreateCiTemporaryCredentialPlan(
        NpmPhase12CredentialPlanRequest request
    )
    {
        ValidateCredentialPlanRequest(request);
        ThrowIfCiTemporaryPlanWouldHideRegistryDeclaration(request.Declaration);
        string targetNpmrcPath =
            NullIfWhiteSpace(request.TargetNpmrcPath)
            ?? throw new ArgumentException(
                "CI temporary npmrc plans require a product-owned target npmrc path.",
                nameof(request)
            );
        targetNpmrcPath = fileSystem.GetFullPath(targetNpmrcPath);
        ThrowIfProjectAuthWouldShadowPlan(
            request.Declaration,
            request.Ecosystem,
            targetNpmrcPath
        );
        string platform = InferActivationPlatform(targetNpmrcPath);
        var activationEnvironment = new ConfigurationActivationEnvironment
        {
            Platform = platform,
            SetVariables = CreateNpmrcActivationSetVariables(platform, targetNpmrcPath),
            ClearVariables = [],
        };
        var temporaryContainer = new ConfigurationTemporaryContainer
        {
            Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
            ProductOwnedPath = targetNpmrcPath,
            ActivationEnvironment = activationEnvironment,
        };

        return CreateCredentialPlan(
            request,
            targetNpmrcPath,
            ConfigurationScope.CiTemporary,
            temporaryContainer,
            ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
        );
    }

    private void ThrowIfCiTemporaryPlanWouldHideRegistryDeclaration(
        NpmPhase12RegistryDeclaration declaration
    )
    {
        string userNpmrcPath = ResolveUserNpmrcPath();
        if (PathsEqual(declaration.SourcePath, userNpmrcPath))
        {
            throw new InvalidOperationException(
                "CI temporary npmrc auth-only plans require the registry declaration to remain "
                    + "visible outside the replaced user npmrc. Copying hidden declarations into "
                    + "temporary npmrc files is a separate Phase 12 follow-up."
            );
        }
    }

    private void ThrowIfProjectAuthWouldShadowPlan(
        NpmPhase12RegistryDeclaration declaration,
        CredentialEcosystem ecosystem,
        string targetNpmrcPath
    )
    {
        string? workspaceNpmrcPath = GetWorkspaceNpmrcPath(ecosystem);
        if (
            workspaceNpmrcPath is null
            || !fileSystem.FileExists(workspaceNpmrcPath)
            || PathsEqual(workspaceNpmrcPath, targetNpmrcPath)
        )
        {
            return;
        }

        Uri plannedRegistry = declaration.RegistryUrl;
        foreach (string rawLine in SplitLines(fileSystem.ReadAllText(workspaceNpmrcPath)))
        {
            string trimmedLine = rawLine.Trim();
            if (
                trimmedLine.Length == 0
                || trimmedLine.StartsWith('#')
                || trimmedLine.StartsWith(';')
            )
            {
                continue;
            }

            int separatorIndex = rawLine.IndexOf('=', StringComparison.Ordinal);
            string key =
                separatorIndex < 0 ? trimmedLine : rawLine[..separatorIndex].Trim();
            if (
                TryParseRegistryAuthSelector(key, out Uri? registry)
                && IsSameOrDescendantRegistry(plannedRegistry, registry)
            )
            {
                throw new InvalidOperationException(
                    "Project-local npm authentication would shadow the planned user or CI "
                        + "credential."
                );
            }
        }
    }

    private static bool TryParseRegistryAuthSelector(
        string key,
        [NotNullWhen(true)] out Uri? registry
    )
    {
        registry = null;
        string? registryText = null;
        foreach (string leaf in new[] { "_authToken", "_auth", "username", "_password" })
        {
            string suffix = ":" + leaf;
            if (key.EndsWith(suffix, StringComparison.Ordinal))
            {
                registryText = key[..^suffix.Length];
                break;
            }
        }

        if (
            string.IsNullOrEmpty(registryText)
            || !registryText.StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate("https:" + registryText, UriKind.Absolute, out registry)
        )
        {
            registry = null;
            return false;
        }

        return true;
    }

    private static bool IsSameOrDescendantRegistry(Uri plannedRegistry, Uri candidateRegistry)
    {
        string planned = NormalizeRegistryUrl(plannedRegistry);
        string candidate = NormalizeRegistryUrl(candidateRegistry);
        return string.Equals(candidate, planned, StringComparison.Ordinal)
            || candidate.StartsWith(planned + "/", StringComparison.Ordinal);
    }

    private static string NormalizeRegistryUrl(Uri registryUrl) =>
        registryUrl.AbsoluteUri.TrimEnd('/');

    private static ConfigurationChangePlan CreateCredentialPlan(
        NpmPhase12CredentialPlanRequest request,
        string targetNpmrcPath,
        ConfigurationScope scope,
        ConfigurationTemporaryContainer? temporaryContainer,
        ConfigurationDeclarationPreservation declarationPreservation
    )
    {
        string authTokenKey = request.Declaration.AuthSelectors.NpmAuthTokenKey;
        ConfigurationChange change = CreateAuthTokenChange(request, targetNpmrcPath, authTokenKey);
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
                    ["ecosystem"] = ToContractEcosystemName(request.Ecosystem),
                    ["registry-key"] = request.Declaration.Key,
                },
            },
            [change],
            temporaryContainer: temporaryContainer,
            declarationPreservation: declarationPreservation,
            containsCredentialMaterial: true
        );
    }

    private static void ValidateCredentialPlanRequest(NpmPhase12CredentialPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AuthToken);
        if (
            request.AuthToken.Contains('\r', StringComparison.Ordinal)
            || request.AuthToken.Contains('\n', StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                "The npm auth token must not contain CR or LF.",
                nameof(request)
            );
        }

        if (request.Ecosystem is not CredentialEcosystem.Npm and not CredentialEcosystem.Pnpm)
        {
            throw new ArgumentException(
                "Phase 12 npmrc plans support only npm and pnpm ecosystems.",
                nameof(request)
            );
        }

        ArgumentNullException.ThrowIfNull(request.Declaration);
    }

    private static ConfigurationChange CreateAuthTokenChange(
        NpmPhase12CredentialPlanRequest request,
        string targetNpmrcPath,
        string authTokenKey
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = targetNpmrcPath,
            Key = authTokenKey,
            Value = request.AuthToken,
            IsSecretValue = true,
            RequiresOwnershipRecord = true,
        };

    private static Dictionary<string, string> CreateNpmrcActivationSetVariables(
        string platform,
        string targetNpmrcPath
    ) =>
        string.Equals(platform, "windows", StringComparison.Ordinal)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NpmUserConfigEnvironmentVariable] = targetNpmrcPath,
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NpmUserConfigEnvironmentVariable] = targetNpmrcPath,
                [LowercaseNpmUserConfigEnvironmentVariable] = targetNpmrcPath,
            };

    private NpmPhase12RegistryDeclaration[] ReadRegistryDeclarations(string npmrcPath)
    {
        string contents = fileSystem.ReadAllText(npmrcPath);
        var declarations = new List<NpmPhase12RegistryDeclaration>();
        foreach (string rawLine in SplitLines(contents))
        {
            string trimmedLine = rawLine.Trim();
            if (
                trimmedLine.Length == 0
                || trimmedLine.StartsWith('#')
                || trimmedLine.StartsWith(';')
            )
            {
                continue;
            }

            int separatorIndex = rawLine.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = rawLine[..separatorIndex].Trim();
            string value = rawLine[(separatorIndex + 1)..].Trim();
            if (!IsRegistryDeclarationKey(key))
            {
                continue;
            }

            if (TryCreateRegistryDeclaration(npmrcPath, key, value, out var declaration))
            {
                declarations.Add(declaration);
            }
        }

        return declarations.ToArray();
    }

    private bool TryCreateRegistryDeclaration(
        string sourcePath,
        string key,
        string value,
        [NotNullWhen(true)] out NpmPhase12RegistryDeclaration? declaration
    )
    {
        declaration = null;
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? registryUrl)
            || !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                registryUrl,
                CredentialEcosystem.Npm
            )
            || !TryCreateAzureArtifactsNpmResourceIdentity(
                registryUrl,
                out CanonicalResourceIdentity? resource
            )
        )
        {
            return false;
        }

        declaration = new NpmPhase12RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(sourcePath),
            Key = key,
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            AuthSelectors = NpmCompatibleAuthSelectorPolicy.Create(resource),
        };
        return true;
    }

    internal static bool TryCreateAzureArtifactsNpmResourceIdentity(
        Uri registryUrl,
        [NotNullWhen(true)] out CanonicalResourceIdentity? resource
    )
    {
        resource = null;
        string host = registryUrl.IdnHost;
        string[] segments = GetDecodedPathSegments(registryUrl);
        string? organization = null;
        string? project = null;
        string? feed = null;

        if (string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments is [var org, "_packaging", var feedName, "npm", "registry"])
            {
                organization = org;
                feed = feedName;
            }

            if (
                segments
                is [
                    var projectOrg,
                    var projectName,
                    "_packaging",
                    var projectFeedName,
                    "npm",
                    "registry",
                ]
            )
            {
                organization = projectOrg;
                project = projectName;
                feed = projectFeedName;
            }
        }
        else if (TryGetLegacyVisualStudioOrganization(host, out string? legacyOrganization))
        {
            organization = legacyOrganization;
            TryParseLegacyNpmSegments(host, segments, out project, out feed);
        }

        if (organization is null || feed is null)
        {
            return false;
        }

        try
        {
            resource = CanonicalResourceIdentity.Create(
                host,
                organization,
                registryUrl,
                project: project,
                feed: feed
            );
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseLegacyNpmSegments(
        string host,
        string[] segments,
        out string? project,
        out string? feed
    )
    {
        project = null;
        feed = null;
        if (!host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] resourceSegments =
            segments is ["DefaultCollection", .. var remaining] ? remaining : segments;
        if (resourceSegments is ["_packaging", var feedName, "npm", "registry"])
        {
            feed = feedName;
            return true;
        }

        if (
            resourceSegments
            is [var projectName, "_packaging", var projectFeedName, "npm", "registry"]
        )
        {
            project = projectName;
            feed = projectFeedName;
            return true;
        }

        return false;
    }

    private string ResolveUserNpmrcPath()
    {
        string? configuredUserNpmrcPath =
            NullIfWhiteSpace(environmentVariableReader(NpmUserConfigEnvironmentVariable))
            ?? NullIfWhiteSpace(
                environmentVariableReader(LowercaseNpmUserConfigEnvironmentVariable)
            )
            ?? userNpmrcPath;
        if (configuredUserNpmrcPath is not null)
        {
            return fileSystem.GetFullPath(configuredUserNpmrcPath);
        }

        string home = userHomeDirectoryPath ?? GetHomeDirectory();
        return fileSystem.GetFullPath(Path.Combine(home, WorkspaceNpmrcFileName));
    }

    private string? GetWorkspaceNpmrcPath(
        CredentialEcosystem ecosystem = CredentialEcosystem.Npm
    )
    {
        if (workspaceDirectoryPath is null)
        {
            return null;
        }

        string invocationNpmrcPath = FileSystemPathSemantics.Combine(
            fileSystem,
            workspaceDirectoryPath,
            WorkspaceNpmrcFileName
        );
        string? nearestProjectNpmrcPath = null;
        for (
            string? directory = workspaceDirectoryPath;
            directory is not null;
            directory = FileSystemPathSemantics.GetParentDirectory(fileSystem, directory)
        )
        {
            string npmrcPath = FileSystemPathSemantics.Combine(
                fileSystem,
                directory,
                WorkspaceNpmrcFileName
            );
            if (
                ecosystem == CredentialEcosystem.Pnpm
                && fileSystem.FileExists(
                    FileSystemPathSemantics.Combine(
                        fileSystem,
                        directory,
                        "pnpm-workspace.yaml"
                    )
                )
            )
            {
                return npmrcPath;
            }

            if (
                nearestProjectNpmrcPath is null
                && (
                    fileSystem.FileExists(
                        FileSystemPathSemantics.Combine(fileSystem, directory, "package.json")
                    )
                    || fileSystem.DirectoryExists(
                        FileSystemPathSemantics.Combine(fileSystem, directory, "node_modules")
                    )
                )
            )
            {
                nearestProjectNpmrcPath = npmrcPath;
                if (ecosystem == CredentialEcosystem.Npm)
                {
                    return npmrcPath;
                }
            }
        }

        return nearestProjectNpmrcPath ?? invocationNpmrcPath;
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

    private string ResolveCiTemporaryNpmrcPath() =>
        ciTemporaryNpmrcPath
        ?? fileSystem.GetFullPath(
            Path.Combine(Path.GetTempPath(), ProductId, "phase12-ci", ".npmrc")
        );

    private bool TryValidateUserCredentialPlan(
        NpmPhase12RegistryDeclaration? declaration,
        CredentialEcosystem ecosystem
    )
    {
        if (declaration is null)
        {
            return false;
        }

        try
        {
            ConfigurationChangePlan plan = CreateUserCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = declaration,
                    AuthToken = "doctor-token",
                    Ecosystem = ecosystem,
                }
            );
            return ConfigurationChangePlanPolicy.IsValid(plan);
        }
        catch (Exception exception) when (IsExpectedDoctorProbeFailure(exception))
        {
            return false;
        }
    }

    private bool TryValidateCiTemporaryCredentialPlan(
        NpmPhase12RegistryDeclaration? declaration,
        string targetNpmrcPath
    )
    {
        if (declaration is null)
        {
            return false;
        }

        try
        {
            ConfigurationChangePlan plan = CreateCiTemporaryCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = declaration,
                    AuthToken = "doctor-token",
                    TargetNpmrcPath = targetNpmrcPath,
                }
            );
            return ConfigurationChangePlanPolicy.IsValid(plan);
        }
        catch (Exception exception) when (IsExpectedDoctorProbeFailure(exception))
        {
            return false;
        }
    }

    private static bool CheckAzureArtifactsNpmEndpointCanonicalization()
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
                    "https://org.pkgs.visualstudio.com/_packaging/feed/npm/registry/",
                    "org",
                    project: null,
                    feed: "feed"
                )
                && EndpointCanonicalizes(
                    "https://org.pkgs.visualstudio.com/DefaultCollection/project/"
                        + "_packaging/feed/npm/registry/",
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
                    "https://org.visualstudio.com/DefaultCollection/project/"
                        + "_packaging/feed/npm/registry/",
                    "org",
                    "project",
                    "feed"
                )
                && !EndpointCanonicalizes(
                    "https://registry.npmjs.org/",
                    "org",
                    project: null,
                    feed: "feed"
                )
                && !EndpointCanonicalizes(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm",
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
            !TryCreateAzureArtifactsNpmResourceIdentity(
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

    private static string InferActivationPlatform(string targetNpmrcPath)
    {
        if (targetNpmrcPath.StartsWith('/'))
        {
            return "posix";
        }

        if (IsWindowsDrivePath(targetNpmrcPath) || IsWindowsUncPath(targetNpmrcPath))
        {
            return "windows";
        }

        return OperatingSystem.IsWindows() ? "windows" : "posix";
    }

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 3
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/')
        && char.IsAsciiLetter(path[0]);

    private static bool IsWindowsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static string[] SplitLines(string contents) =>
        contents.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string[] GetDecodedPathSegments(Uri uri)
    {
        string path = uri.AbsolutePath.Trim('/');
        return path.Length == 0 ? [] : path.Split('/').Select(Uri.UnescapeDataString).ToArray();
    }

    private static bool IsRegistryDeclarationKey(string key) =>
        string.Equals(key, "registry", StringComparison.Ordinal)
        || key.EndsWith(":registry", StringComparison.Ordinal);

    private static bool TryGetLegacyVisualStudioOrganization(string host, out string? organization)
    {
        organization = null;
        const string packagingSuffix = ".pkgs.visualstudio.com";
        const string suffix = ".visualstudio.com";
        if (host.EndsWith(packagingSuffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = host[..^packagingSuffix.Length];
            return organization.Length > 0 && !organization.Contains('.', StringComparison.Ordinal);
        }

        if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            organization = host[..^suffix.Length];
            return organization.Length > 0 && !organization.Contains('.', StringComparison.Ordinal);
        }

        return false;
    }

    private string? NormalizeOptionalPath(string? path) =>
        NullIfWhiteSpace(path) is { } value ? fileSystem.GetFullPath(value) : null;

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

    private static bool IsWindowsLikePath(string path) =>
        IsWindowsDrivePath(path) || IsWindowsUncPath(path);

    private static string ToContractEcosystemName(CredentialEcosystem ecosystem) =>
        ecosystem switch
        {
            CredentialEcosystem.Npm => "npm",
            CredentialEcosystem.Pnpm => "pnpm",
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null),
        };

    private static bool IsExpectedDoctorProbeFailure(Exception exception) =>
        exception
            is ArgumentException
                or IOException
                or InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException
                or UnauthorizedAccessException;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
