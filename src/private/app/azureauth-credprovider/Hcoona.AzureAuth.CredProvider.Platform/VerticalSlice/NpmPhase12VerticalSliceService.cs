using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record NpmPhase12VerticalSliceOptions
{
    public IFileSystem? FileSystem { get; init; }

    public IProcessRunner? ProcessRunner { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public string? WorkspaceDirectoryPath { get; init; }

    public string? UserHomeDirectoryPath { get; init; }

    public string? UserNpmrcPath { get; init; }

    public string? CiTemporaryNpmrcPath { get; init; }

    public string? NpmExecutablePath { get; init; }
}

public enum NpmWorkspaceResolutionStatus
{
    Succeeded = 0,
    NotRequired = 1,
    LaunchFailure = 2,
    TimedOut = 3,
    NonZeroExit = 4,
    OutputTooLarge = 5,
    InvalidOutput = 6,
}

public sealed record NpmWorkspaceResolutionResult
{
    public required NpmWorkspaceResolutionStatus Status { get; init; }

    public string? WorkspaceRootPath { get; init; }

    public string? FailureDetail { get; init; }

    public bool Succeeded =>
        Status
        is NpmWorkspaceResolutionStatus.Succeeded
            or NpmWorkspaceResolutionStatus.NotRequired;
}

public sealed class NpmWorkspaceResolutionException : InvalidOperationException
{
    public NpmWorkspaceResolutionException(NpmWorkspaceResolutionResult resolution)
        : base(
            resolution?.FailureDetail
                ?? throw new ArgumentNullException(nameof(resolution))
        )
    {
        Resolution = resolution;
    }

    public NpmWorkspaceResolutionResult Resolution { get; }

    public NpmWorkspaceResolutionStatus Status => Resolution.Status;
}

internal enum NpmExecutableResolutionStatus
{
    Succeeded = 0,
    MissingCandidate = 1,
    InvalidCandidate = 2,
}

internal sealed record NpmExecutableResolutionResult
{
    public required NpmExecutableResolutionStatus Status { get; init; }

    public string? FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? FailureDetail { get; init; }
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

    public bool IncludeRegistryDeclarationInTarget { get; init; }

    public IReadOnlyList<NpmPhase12RegistryDeclaration> RegistryDeclarationsToInclude
    {
        get;
        init;
    } = [];
}

public sealed record NpmPhase12DoctorResult
{
    public required NpmWorkspaceResolutionStatus WorkspaceResolutionStatus { get; init; }

    public required bool WorkspaceResolutionSucceeded { get; init; }

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

    public bool CiTemporaryAuthOnlyPlanSupported =>
        CiTemporaryCredentialPlanValid
        && RegistryDeclarations.Count > 0
        && !string.Equals(
            RegistryDeclarations[0].SourcePath,
            EffectiveUserNpmrcPath,
            StringComparison.Ordinal
        );
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
    private const int NpmPrefixOutputByteLimit = 4096;
    private static readonly TimeSpan NpmPrefixTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly IProcessRunner processRunner;
    private readonly string? workspaceDirectoryPath;
    private readonly string? userHomeDirectoryPath;
    private readonly string? userNpmrcPath;
    private readonly string? ciTemporaryNpmrcPath;
    private readonly string? npmExecutablePath;

    public NpmPhase12VerticalSliceService(NpmPhase12VerticalSliceOptions? options = null)
    {
        options ??= new NpmPhase12VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        processRunner = options.ProcessRunner ?? new SystemProcessRunner();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        workspaceDirectoryPath = NormalizeOptionalPath(options.WorkspaceDirectoryPath);
        userHomeDirectoryPath = NormalizeOptionalPath(options.UserHomeDirectoryPath);
        userNpmrcPath = NormalizeOptionalPath(options.UserNpmrcPath);
        ciTemporaryNpmrcPath = NormalizeOptionalPath(options.CiTemporaryNpmrcPath);
        npmExecutablePath = NormalizeOptionalPath(options.NpmExecutablePath);
    }

    public IReadOnlyList<NpmPhase12RegistryDeclaration> DiscoverRegistryDeclarations(
        CredentialEcosystem ecosystem = CredentialEcosystem.Npm
    )
    {
        NpmWorkspaceResolutionResult resolution =
            ResolveWorkspaceForSynchronousOperation(ecosystem);
        return DiscoverRegistryDeclarations(ecosystem, resolution);
    }

    private NpmPhase12RegistryDeclaration[] DiscoverRegistryDeclarations(
        CredentialEcosystem ecosystem,
        NpmWorkspaceResolutionResult resolution
    )
    {
        var effectiveSettings = new Dictionary<
            string,
            (string SourcePath, string Value)
        >(StringComparer.Ordinal);
        string resolvedUserNpmrcPath = ResolveUserNpmrcPath();
        if (fileSystem.FileExists(resolvedUserNpmrcPath))
        {
            MergeRegistrySettings(resolvedUserNpmrcPath, effectiveSettings);
        }

        string? workspaceNpmrcPath = GetWorkspaceNpmrcPath(ecosystem, resolution);
        if (workspaceNpmrcPath is not null && fileSystem.FileExists(workspaceNpmrcPath))
        {
            MergeRegistrySettings(workspaceNpmrcPath, effectiveSettings);
        }

        var declarations = new List<NpmPhase12RegistryDeclaration>();
        foreach (
            KeyValuePair<string, (string SourcePath, string Value)> setting in effectiveSettings
        )
        {
            if (
                TryCreateRegistryDeclaration(
                    setting.Value.SourcePath,
                    setting.Key,
                    setting.Value.Value,
                    out NpmPhase12RegistryDeclaration? declaration
                )
            )
            {
                declarations.Add(declaration);
            }
        }

        return declarations.ToArray();
    }

    public async ValueTask<NpmPhase12DoctorResult> RunDoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        NpmWorkspaceResolutionResult resolution = await ResolveWorkspaceAsync(
                CredentialEcosystem.Npm,
                cancellationToken
            )
            .ConfigureAwait(false);
        bool workspaceResolutionSucceeded = resolution.Succeeded;
        string? workspaceNpmrcPath = workspaceResolutionSucceeded
            ? GetWorkspaceNpmrcPath(CredentialEcosystem.Npm, resolution)
            : null;
        string effectiveUserNpmrcPath = ResolveUserNpmrcPath();
        string resolvedCiTemporaryNpmrcPath = ResolveCiTemporaryNpmrcPath();
        NpmPhase12RegistryDeclaration[] declarations =
            workspaceResolutionSucceeded
                ? DiscoverRegistryDeclarations(CredentialEcosystem.Npm, resolution)
                : [];
        IGrouping<string, NpmPhase12RegistryDeclaration>[] declarationGroups = declarations
            .GroupBy(
                static declaration => declaration.AuthSelectors.NpmAuthTokenKey,
                StringComparer.Ordinal
            )
            .ToArray();
        NpmPhase12RegistryDeclaration[] selectedDeclarations =
            declarationGroups.Length == 1 ? declarationGroups[0].ToArray() : [];
        NpmPhase12RegistryDeclaration? selectedDeclaration =
            selectedDeclarations.Length == 0 ? null : selectedDeclarations[0];

        return new NpmPhase12DoctorResult
        {
            WorkspaceResolutionStatus = resolution.Status,
            WorkspaceResolutionSucceeded = workspaceResolutionSucceeded,
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
            NpmUserCredentialPlanValid =
                workspaceResolutionSucceeded
                && TryValidateUserCredentialPlan(
                    selectedDeclaration,
                    CredentialEcosystem.Npm,
                    resolution
                ),
            PnpmUserCredentialPlanValid =
                workspaceResolutionSucceeded
                && TryValidateUserCredentialPlan(
                    selectedDeclaration,
                    CredentialEcosystem.Pnpm,
                    resolution
                ),
            CiTemporaryCredentialPlanValid =
                workspaceResolutionSucceeded
                && TryValidateCiTemporaryCredentialPlan(
                    selectedDeclarations,
                    resolvedCiTemporaryNpmrcPath,
                    resolution
                ),
        };
    }

    public ConfigurationChangePlan CreateUserCredentialPlan(NpmPhase12CredentialPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        NpmWorkspaceResolutionResult resolution =
            ResolveWorkspaceForSynchronousOperation(request.Ecosystem);
        return CreateUserCredentialPlan(request, resolution);
    }

    private ConfigurationChangePlan CreateUserCredentialPlan(
        NpmPhase12CredentialPlanRequest request,
        NpmWorkspaceResolutionResult resolution
    )
    {
        ValidateCredentialPlanRequest(request);
        string targetNpmrcPath = fileSystem.GetFullPath(
            NullIfWhiteSpace(request.TargetNpmrcPath) ?? ResolveUserNpmrcPath()
        );
        ThrowIfProjectAuthWouldShadowPlan(
            request.Declaration,
            request.Ecosystem,
            targetNpmrcPath,
            resolution
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
        ArgumentNullException.ThrowIfNull(request);
        NpmWorkspaceResolutionResult resolution =
            ResolveWorkspaceForSynchronousOperation(request.Ecosystem);
        return CreateCiTemporaryCredentialPlan(request, resolution);
    }

    private ConfigurationChangePlan CreateCiTemporaryCredentialPlan(
        NpmPhase12CredentialPlanRequest request,
        NpmWorkspaceResolutionResult resolution
    )
    {
        ValidateCredentialPlanRequest(request);
        if (!request.IncludeRegistryDeclarationInTarget)
        {
            ThrowIfCiTemporaryPlanWouldHideRegistryDeclaration(request.Declaration);
        }

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
            targetNpmrcPath,
            resolution
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
        IReadOnlyList<NpmPhase12RegistryDeclaration> registryDeclarations =
            request.IncludeRegistryDeclarationInTarget
                ? GetRegistryDeclarationsToInclude(request)
                : [];

        ConfigurationChangePlan plan = CreateCredentialPlan(
            request,
            targetNpmrcPath,
            ConfigurationScope.CiTemporary,
            temporaryContainer,
            request.IncludeRegistryDeclarationInTarget
                ? ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig
                : ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
        );
        return request.IncludeRegistryDeclarationInTarget
            ? plan with
            {
                Changes =
                [
                    .. registryDeclarations.Select(declaration =>
                        CreateRegistryDeclarationChange(declaration, targetNpmrcPath)
                    ),
                    .. plan.Changes,
                ],
            }
            : plan;
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
        string targetNpmrcPath,
        NpmWorkspaceResolutionResult resolution
    )
    {
        string? workspaceNpmrcPath = GetWorkspaceNpmrcPath(ecosystem, resolution);
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

    private static ConfigurationChange CreateRegistryDeclarationChange(
        NpmPhase12RegistryDeclaration declaration,
        string targetNpmrcPath
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = targetNpmrcPath,
            Key = declaration.Key,
            Value = declaration.RegistryUrl.AbsoluteUri,
            IsSecretValue = false,
            RequiresOwnershipRecord = true,
        };

    private static IReadOnlyList<NpmPhase12RegistryDeclaration> GetRegistryDeclarationsToInclude(
        NpmPhase12CredentialPlanRequest request
    )
    {
        IReadOnlyList<NpmPhase12RegistryDeclaration> declarations =
            request.RegistryDeclarationsToInclude.Count == 0
                ? [request.Declaration]
                : request.RegistryDeclarationsToInclude;
        string expectedAuthTokenKey = request.Declaration.AuthSelectors.NpmAuthTokenKey;
        if (
            declarations.Count == 0
            || declarations.Any(declaration =>
                !string.Equals(
                    declaration.AuthSelectors.NpmAuthTokenKey,
                    expectedAuthTokenKey,
                    StringComparison.Ordinal
                )
            )
            || declarations.Select(static declaration => declaration.Key).Distinct().Count()
                != declarations.Count
        )
        {
            throw new ArgumentException(
                "Included registry declarations must have unique keys for the requested registry.",
                nameof(request)
            );
        }

        return declarations;
    }

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

    private void MergeRegistrySettings(
        string npmrcPath,
        Dictionary<string, (string SourcePath, string Value)> effectiveSettings
    )
    {
        string contents = fileSystem.ReadAllText(npmrcPath);
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

            effectiveSettings[key] = (npmrcPath, value);
        }
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
        CredentialEcosystem ecosystem,
        NpmWorkspaceResolutionResult resolution
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
        bool npmWorkspaceDeclarationFound = false;
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

            string packageJsonPath = FileSystemPathSemantics.Combine(
                fileSystem,
                directory,
                "package.json"
            );
            bool packageJsonExists = fileSystem.FileExists(packageJsonPath);
            if (
                nearestProjectNpmrcPath is null
                && (
                    packageJsonExists
                    || fileSystem.DirectoryExists(
                        FileSystemPathSemantics.Combine(fileSystem, directory, "node_modules")
                    )
                )
            )
            {
                nearestProjectNpmrcPath = npmrcPath;
            }

            if (
                ecosystem == CredentialEcosystem.Npm
                && packageJsonExists
                && DeclaresNpmWorkspaces(packageJsonPath)
            )
            {
                npmWorkspaceDeclarationFound = true;
            }
        }

        if (ecosystem == CredentialEcosystem.Npm && npmWorkspaceDeclarationFound)
        {
            string npmWorkspaceRoot =
                resolution.Status == NpmWorkspaceResolutionStatus.Succeeded
                    ? resolution.WorkspaceRootPath!
                    : throw new NpmWorkspaceResolutionException(resolution);
            return FileSystemPathSemantics.Combine(
                fileSystem,
                npmWorkspaceRoot,
                WorkspaceNpmrcFileName
            );
        }

        return nearestProjectNpmrcPath ?? invocationNpmrcPath;
    }

    private bool DeclaresNpmWorkspaces(string packageJsonPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                fileSystem.ReadAllText(packageJsonPath)
            );
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("workspaces", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async ValueTask<NpmWorkspaceResolutionResult> ResolveWorkspaceAsync(
        CredentialEcosystem ecosystem = CredentialEcosystem.Npm,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            ecosystem != CredentialEcosystem.Npm
            || workspaceDirectoryPath is null
            || !RequiresNpmWorkspaceResolution()
        )
        {
            return new NpmWorkspaceResolutionResult
            {
                Status = NpmWorkspaceResolutionStatus.NotRequired,
            };
        }

        NpmExecutableResolutionResult executable = ResolveNpmExecutable();
        if (executable.Status != NpmExecutableResolutionStatus.Succeeded)
        {
            return CreateWorkspaceResolutionFailure(
                NpmWorkspaceResolutionStatus.LaunchFailure,
                executable.FailureDetail
                    ?? "npm could not be resolved. Install npm and ensure it is available on PATH."
            );
        }

        ProcessResult processResult;
        try
        {
            processResult = await processRunner
                .RunAsync(
                    new ProcessStartSpec(
                        executable.FileName!,
                        executable.Arguments,
                        workspaceDirectoryPath,
                        timeout: NpmPrefixTimeout,
                        outputCaptureOptions: new ProcessOutputCaptureOptions
                        {
                            StandardOutputByteLimit = NpmPrefixOutputByteLimit,
                            StandardErrorByteLimit = NpmPrefixOutputByteLimit,
                        }
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedNpmLaunchException(exception))
        {
            return CreateWorkspaceResolutionFailure(
                NpmWorkspaceResolutionStatus.LaunchFailure,
                "npm could not be started. Install npm and ensure it is available on PATH."
            );
        }

        if (!processResult.Succeeded)
        {
            return processResult.Status switch
            {
                ProcessExecutionStatus.LaunchFailure => CreateWorkspaceResolutionFailure(
                    NpmWorkspaceResolutionStatus.LaunchFailure,
                    "npm could not be started. Install npm and ensure it is available on PATH."
                ),
                ProcessExecutionStatus.TimedOut => CreateWorkspaceResolutionFailure(
                    NpmWorkspaceResolutionStatus.TimedOut,
                    "npm workspace discovery timed out. Run `npm prefix` from the "
                        + "workspace directory and resolve the delay."
                ),
                ProcessExecutionStatus.OutputTooLarge => CreateWorkspaceResolutionFailure(
                    NpmWorkspaceResolutionStatus.OutputTooLarge,
                    "npm workspace discovery produced too much output. Run `npm prefix` "
                        + "and resolve the npm configuration."
                ),
                ProcessExecutionStatus.InvalidOutput => CreateWorkspaceResolutionFailure(
                    NpmWorkspaceResolutionStatus.InvalidOutput,
                    "npm workspace discovery returned invalid process output. Run "
                        + "`npm prefix` and verify npm succeeds."
                ),
                _ => CreateWorkspaceResolutionFailure(
                    NpmWorkspaceResolutionStatus.NonZeroExit,
                    processResult.HasExitCode
                        ? $"npm workspace discovery failed with exit code "
                            + $"{processResult.ExitCode}. Run `npm prefix` and resolve "
                            + "the npm error."
                        : "npm workspace discovery failed. Run `npm prefix` and resolve "
                            + "the npm error."
                ),
            };
        }

        string output = processResult.StandardOutput.Trim();
        if (
            output.Length == 0
            || output.Contains('\r')
            || output.Contains('\n')
            || !IsAbsolutePath(output)
        )
        {
            return CreateInvalidWorkspaceResolution();
        }

        try
        {
            string root = fileSystem.GetFullPath(output);
            if (
                fileSystem.DirectoryExists(root)
                && FileSystemPathSemantics.IsSameOrDescendant(
                    fileSystem,
                    workspaceDirectoryPath,
                    root
                )
            )
            {
                return new NpmWorkspaceResolutionResult
                {
                    Status = NpmWorkspaceResolutionStatus.Succeeded,
                    WorkspaceRootPath = root,
                };
            }
        }
        catch (Exception exception) when (IsExpectedPathResolutionException(exception))
        {
            return CreateInvalidWorkspaceResolution();
        }

        return CreateInvalidWorkspaceResolution();
    }

    private bool RequiresNpmWorkspaceResolution()
    {
        for (
            string? directory = workspaceDirectoryPath;
            directory is not null;
            directory = FileSystemPathSemantics.GetParentDirectory(fileSystem, directory)
        )
        {
            string packageJsonPath = FileSystemPathSemantics.Combine(
                fileSystem,
                directory,
                "package.json"
            );
            if (
                fileSystem.FileExists(packageJsonPath)
                && DeclaresNpmWorkspaces(packageJsonPath)
            )
            {
                return true;
            }
        }

        return false;
    }

    private NpmWorkspaceResolutionResult ResolveWorkspaceForSynchronousOperation(
        CredentialEcosystem ecosystem
    )
    {
        NpmWorkspaceResolutionResult resolution = ResolveWorkspaceAsync(ecosystem)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return resolution.Succeeded
            ? resolution
            : throw new NpmWorkspaceResolutionException(resolution);
    }

    private NpmExecutableResolutionResult ResolveNpmExecutable()
    {
        bool useWindowsLaunch =
            FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
            || (
                workspaceDirectoryPath is not null
                && IsWindowsLikePath(workspaceDirectoryPath)
            )
            || (npmExecutablePath is not null && IsWindowsLikePath(npmExecutablePath));
        if (!useWindowsLaunch)
        {
            return new NpmExecutableResolutionResult
            {
                Status = NpmExecutableResolutionStatus.Succeeded,
                FileName = "npm",
                Arguments = ["prefix"],
            };
        }

        if (npmExecutablePath is not null)
        {
            return ResolveWindowsNpmCandidate(npmExecutablePath);
        }

        string? pathValue =
            NullIfWhiteSpace(environmentVariableReader("PATH"))
            ?? NullIfWhiteSpace(environmentVariableReader("Path"));
        if (pathValue is null)
        {
            return CreateMissingNpmExecutable(
                "npm was not found because PATH is unavailable."
            );
        }

        string[] directories = pathValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim().Trim('"'))
            .Where(static value => value.Length > 0)
            .ToArray();
        IReadOnlyList<string> supportedExtensions = GetSupportedWindowsNpmExtensions();
        foreach (string directory in directories)
        {
            foreach (string extension in supportedExtensions)
            {
                string candidate = FileSystemPathSemantics.Combine(
                    fileSystem,
                    directory,
                    "npm" + extension
                );
                if (!fileSystem.FileExists(candidate))
                {
                    continue;
                }

                return ResolveWindowsNpmCandidate(candidate);
            }
        }

        return CreateMissingNpmExecutable(
            "npm was not found on PATH. Install Node.js with npm or add npm.exe or npm.cmd to PATH."
        );
    }

    private List<string> GetSupportedWindowsNpmExtensions()
    {
        string? pathExtValue = NullIfWhiteSpace(environmentVariableReader("PATHEXT"));
        if (pathExtValue is null)
        {
            return [".exe", ".cmd"];
        }

        var supportedExtensions = new List<string>(capacity: 2);
        foreach (string configuredExtension in pathExtValue.Split(';'))
        {
            string normalizedExtension = configuredExtension.Trim().Trim('"');
            string? supportedExtension =
                normalizedExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? ".exe"
                : normalizedExtension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                    ? ".cmd"
                    : null;
            if (
                supportedExtension is not null
                && !supportedExtensions.Contains(
                    supportedExtension,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                supportedExtensions.Add(supportedExtension);
            }
        }

        return supportedExtensions;
    }

    private NpmExecutableResolutionResult ResolveWindowsNpmCandidate(string candidatePath)
    {
        string candidate;
        try
        {
            candidate = fileSystem.GetFullPath(candidatePath);
        }
        catch (Exception exception) when (IsExpectedPathResolutionException(exception))
        {
            return new NpmExecutableResolutionResult
            {
                Status = NpmExecutableResolutionStatus.InvalidCandidate,
                FailureDetail = "The configured npm path is invalid.",
            };
        }

        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return fileSystem.IsExecutableFile(candidate)
                ? new NpmExecutableResolutionResult
                {
                    Status = NpmExecutableResolutionStatus.Succeeded,
                    FileName = candidate,
                    Arguments = ["prefix"],
                }
                : CreateMissingNpmExecutable(
                    "The configured npm executable does not exist or is not launchable."
                );
        }

        if (!candidate.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new NpmExecutableResolutionResult
            {
                Status = NpmExecutableResolutionStatus.InvalidCandidate,
                FailureDetail =
                    "The configured npm path must identify npm.exe or a standard npm.cmd shim.",
            };
        }

        if (!fileSystem.FileExists(candidate))
        {
            return CreateMissingNpmExecutable(
                "The configured npm.cmd shim does not exist."
            );
        }

        string? directory = FileSystemPathSemantics.GetParentDirectory(
            fileSystem,
            candidate
        );
        if (directory is null)
        {
            return new NpmExecutableResolutionResult
            {
                Status = NpmExecutableResolutionStatus.InvalidCandidate,
                FailureDetail = "The configured npm.cmd shim has no parent directory.",
            };
        }

        string npmCliScript = FileSystemPathSemantics.Combine(
            fileSystem,
            directory,
            "node_modules",
            "npm",
            "bin",
            "npm-cli.js"
        );
        string siblingNodeExecutable = FileSystemPathSemantics.Combine(
            fileSystem,
            directory,
            "node.exe"
        );
        bool npmCliExists = fileSystem.FileExists(npmCliScript);
        string? nodeExecutable = fileSystem.IsExecutableFile(siblingNodeExecutable)
            ? fileSystem.GetFullPath(siblingNodeExecutable)
            : ResolveWindowsNodeExecutableFromPath();
        if (!npmCliExists && nodeExecutable is null)
        {
            return new NpmExecutableResolutionResult
            {
                Status = NpmExecutableResolutionStatus.InvalidCandidate,
                FailureDetail =
                    "The npm.cmd shim is not in a standard Node.js npm layout.",
            };
        }

        if (!npmCliExists)
        {
            return CreateMissingNpmExecutable(
                "The npm CLI script required by npm.cmd is unavailable."
            );
        }

        if (nodeExecutable is null)
        {
            return CreateMissingNpmExecutable(
                "No launchable node.exe was found beside npm.cmd or on PATH."
            );
        }

        return new NpmExecutableResolutionResult
        {
            Status = NpmExecutableResolutionStatus.Succeeded,
            FileName = nodeExecutable,
            Arguments = [fileSystem.GetFullPath(npmCliScript), "prefix"],
        };
    }

    private string? ResolveWindowsNodeExecutableFromPath()
    {
        string? pathValue =
            NullIfWhiteSpace(environmentVariableReader("PATH"))
            ?? NullIfWhiteSpace(environmentVariableReader("Path"));
        if (pathValue is null || !WindowsPathExtContainsExecutableExtension())
        {
            return null;
        }

        foreach (
            string directory in pathValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim().Trim('"'))
                .Where(static value => value.Length > 0)
        )
        {
            string? normalizedDirectory = NormalizeWindowsPathDirectory(directory);
            if (normalizedDirectory is null)
            {
                continue;
            }

            string candidate = FileSystemPathSemantics.Combine(
                fileSystem,
                normalizedDirectory,
                "node.exe"
            );
            if (fileSystem.IsExecutableFile(candidate))
            {
                return fileSystem.GetFullPath(candidate);
            }
        }

        return null;
    }

    private string? NormalizeWindowsPathDirectory(string directory)
    {
        if (IsWindowsDriveRelativePath(directory))
        {
            return null;
        }

        try
        {
            string path = directory;
            if (IsWindowsRootRelativePath(path))
            {
                if (
                    workspaceDirectoryPath is null
                    || IsWindowsUncPath(workspaceDirectoryPath)
                    || !IsWindowsDrivePath(workspaceDirectoryPath)
                )
                {
                    return null;
                }

                string workspaceVolumeRoot = workspaceDirectoryPath[..3];
                string relativeToVolumeRoot = path.TrimStart('/', '\\');
                path =
                    relativeToVolumeRoot.Length == 0
                        ? workspaceVolumeRoot
                        : FileSystemPathSemantics.Combine(
                            fileSystem,
                            workspaceVolumeRoot,
                            relativeToVolumeRoot
                        );
            }
            else if (!IsAbsolutePath(path))
            {
                if (workspaceDirectoryPath is null)
                {
                    return null;
                }

                path = FileSystemPathSemantics.Combine(
                    fileSystem,
                    workspaceDirectoryPath,
                    path
                );
            }

            return fileSystem.GetFullPath(path);
        }
        catch (Exception exception) when (IsExpectedPathResolutionException(exception))
        {
            return null;
        }
    }

    private bool WindowsPathExtContainsExecutableExtension()
    {
        string? pathExtValue = NullIfWhiteSpace(environmentVariableReader("PATHEXT"));
        return pathExtValue is null
            || pathExtValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim().Trim('"'))
                .Any(static value =>
                    value.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                );
    }

    private static NpmExecutableResolutionResult CreateMissingNpmExecutable(string detail) =>
        new()
        {
            Status = NpmExecutableResolutionStatus.MissingCandidate,
            FailureDetail = detail,
        };

    private static NpmWorkspaceResolutionResult CreateInvalidWorkspaceResolution() =>
        CreateWorkspaceResolutionFailure(
            NpmWorkspaceResolutionStatus.InvalidOutput,
            "npm workspace discovery returned an invalid root. Run `npm prefix` and "
                + "verify it returns one existing absolute ancestor path."
        );

    private static NpmWorkspaceResolutionResult CreateWorkspaceResolutionFailure(
        NpmWorkspaceResolutionStatus status,
        string detail
    ) =>
        new()
        {
            Status = status,
            FailureDetail = detail,
        };

    private static bool IsExpectedNpmLaunchException(Exception exception) =>
        exception
            is Win32Exception
                or IOException
                or NotSupportedException
                or PlatformNotSupportedException
                or UnauthorizedAccessException;

    private static bool IsExpectedPathResolutionException(Exception exception) =>
        exception
            is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException;

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
        CredentialEcosystem ecosystem,
        NpmWorkspaceResolutionResult resolution
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
                },
                resolution
            );
            return ConfigurationChangePlanPolicy.IsValid(plan);
        }
        catch (Exception exception) when (IsExpectedDoctorProbeFailure(exception))
        {
            return false;
        }
    }

    private bool TryValidateCiTemporaryCredentialPlan(
        NpmPhase12RegistryDeclaration[] declarations,
        string targetNpmrcPath,
        NpmWorkspaceResolutionResult resolution
    )
    {
        if (declarations.Length == 0)
        {
            return false;
        }

        try
        {
            ConfigurationChangePlan plan = CreateCiTemporaryCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = declarations[0],
                    AuthToken = "doctor-token",
                    TargetNpmrcPath = targetNpmrcPath,
                    IncludeRegistryDeclarationInTarget = true,
                    RegistryDeclarationsToInclude = declarations,
                },
                resolution
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

    private static bool IsWindowsDriveRelativePath(string path) =>
        path.Length >= 2
        && path[1] == ':'
        && char.IsAsciiLetter(path[0])
        && (path.Length == 2 || (path[2] != '\\' && path[2] != '/'));

    private static bool IsWindowsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsWindowsRootRelativePath(string path) =>
        path.Length > 0
        && path[0] is '/' or '\\'
        && (path.Length == 1 || path[1] is not ('/' or '\\'));

    private static bool IsAbsolutePath(string path) =>
        path.StartsWith('/') || IsWindowsDrivePath(path) || IsWindowsUncPath(path);

    private static string[] SplitLines(string contents) =>
        contents.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string[] GetDecodedPathSegments(Uri uri)
    {
        string path = uri.AbsolutePath.Trim('/');
        return path.Length == 0 ? [] : path.Split('/').Select(Uri.UnescapeDataString).ToArray();
    }

    internal static bool IsRegistryDeclarationKey(string key) =>
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
