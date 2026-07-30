using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzurePipelines;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record ConfigurationPhase14VerticalSliceOptions
{
    public string? AzurePipelinesJobScopeId { get; init; }

    public string? CiTemporaryProductRootPath { get; init; }

    public string? StateDirectoryPath { get; init; }

    public IFileSystem? FileSystem { get; init; }

    public CredentialCoreService? CredentialCoreService { get; init; }

    public BoundedCredentialAcquisitionAdapter? CredentialAcquisition { get; init; }

    public Func<ICredentialAcquisitionService>? CredentialAcquisitionFactory { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public IReadOnlyDictionary<CredentialEcosystem, Uri>? RegistryUrls { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    public RegistryCredentialExpiryPolicyOptions? ExpiryPolicy { get; init; }
}

public sealed record ConfigurationPhase14ResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

    public required string ManifestDirectoryPath { get; init; }

    public required string CiTemporaryRootPath { get; init; }

    public required string CiTemporaryManifestDirectoryPath { get; init; }

    public required string OwnershipManifestPath { get; init; }

    public required string NpmUserNpmrcPath { get; init; }

    public required string PnpmUserNpmrcPath { get; init; }

    public required string NpmCiTemporaryNpmrcPath { get; init; }

    public required string PnpmCiTemporaryNpmrcPath { get; init; }

    public required string YarnUserYarnrcPath { get; init; }

    public required string YarnCiTemporaryHomePath { get; init; }
}

public sealed record ConfigurationPhase14PlanResult
{
    public required ConfigurationPhase14ResolvedPaths Paths { get; init; }

    public required IReadOnlyList<ConfigurationPlanResult> PlanResults { get; init; }

    public required bool OwnershipManifestPresent { get; init; }

    public bool OwnershipManifestCleanupIncomplete { get; init; }

    public bool LifecycleStateMutated { get; init; }

    public ConfigurationPlanResult PlanResult => PlanResults[^1];

    public int ChangeCount => PlanResults.Sum(static result => result.Changes.Count);

    public int AppliedChangeCount =>
        PlanResults
            .Where(static result => result.State == ConfigurationPlanState.Applied)
            .Sum(static result => result.Changes.Count);
}

public sealed record ConfigurationPhase14DoctorResult
{
    public required ConfigurationPhase14ResolvedPaths Paths { get; init; }

    public required IReadOnlyList<ConfigurationPhase14EcosystemDoctorResult> Ecosystems { get; init; }

    public required bool AzurePipelinesSystemAccessTokenPresent { get; init; }

    public required bool PersistentDerivedCredentialCacheEnabled { get; init; }
}

public sealed record ConfigurationPhase14EcosystemDoctorResult
{
    public required CredentialEcosystem Ecosystem { get; init; }

    public required ConfigurationPhase14Scope Scope { get; init; }

    public required bool ConfigurationPlanValid { get; init; }

    public required bool OwnershipManifestPresent { get; init; }

    public required bool OwnedTargetPresent { get; init; }

    public required bool TemporaryContainerPresent { get; init; }

    public RegistryCredentialLifecycleState LifecycleState { get; init; } =
        RegistryCredentialLifecycleState.Missing;

    public DateTimeOffset? CredentialExpiresAt { get; init; }

    public Uri? RegistryUrl { get; init; }
}

public sealed record ConfigurationPhase14CleanupResult
{
    public required ConfigurationPhase14ResolvedPaths Paths { get; init; }

    public required ConfigurationPhase14Scope Scope { get; init; }

    public required IReadOnlyList<ConfigurationPhase14CleanupEcosystemResult> Ecosystems { get; init; }

    public required bool PersistentDerivedCredentialsRemoved { get; init; }

    public int ChangeCount => Ecosystems.Sum(static result => result.ChangeCount);

    public int AppliedChangeCount => Ecosystems.Sum(static result => result.AppliedChangeCount);
}

public sealed record ConfigurationPhase14CleanupEcosystemResult
{
    public required CredentialEcosystem Ecosystem { get; init; }

    public required ConfigurationPhase14Scope Scope { get; init; }

    public required string State { get; init; }

    public required int ChangeCount { get; init; }

    public required int AppliedChangeCount { get; init; }

    public required bool OwnershipManifestPresent { get; init; }

    public required bool TemporaryContainerPresent { get; init; }
}

public sealed class ConfigurationPhase14VerticalSliceService
{
    private sealed record ExistingOwnershipManifest(
        ConfigurationOwnershipManifest Manifest,
        string Json
    );

    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase14.2";
    private const string PythonPlanId = "phase14-python-keyring-configure-plan";
    private const string PythonChangeSetId = "phase14-python-keyring-configure-changeset";
    private const string PythonManifestId = "phase14-python-keyring";
    private const string NpmCredentialManifestId = "phase12-npmrc-credential";
    private const string YarnCredentialManifestId = "phase13-yarnrc-credential";
    private const string PhysicalTargetKey = "physical-target";
    private const string AzurePipelinesSystemAccessTokenVariable = "SYSTEM_ACCESSTOKEN";
    private const string AzurePipelinesJobScopeIdVariable = "SYSTEM_JOBID";
    private const int MaximumJobScopeIdLength = 128;
    private const string NpmUserConfigEnvironmentVariable = "NPM_CONFIG_USERCONFIG";
    private const string LowercaseNpmUserConfigEnvironmentVariable = "npm_config_userconfig";
    private const string YarnRcFilenameEnvironmentVariable = "YARN_RC_FILENAME";
    private const string CleanupStateNotNeeded = "not-needed";
    private const string CleanupStateRemoved = "removed";
    private const string CleanupStateIncomplete = "incomplete";

    private static readonly Uri PythonServiceEndpoint = new("https://dev.azure.com/org");

    private readonly Lazy<BoundedCredentialAcquisitionAdapter> credentialAcquisition;
    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly string? jobScopeId;
    private readonly string? rawJobScopeId;
    private readonly ConfigurationPhase14ResolvedPaths paths;
    private readonly Dictionary<CredentialEcosystem, Uri> registryUrls;
    private readonly RegistryCredentialExpiryPolicy expiryPolicy;
    private readonly TimeProvider timeProvider;

    public ConfigurationPhase14VerticalSliceService(
        ConfigurationPhase14VerticalSliceOptions? options = null
    )
    {
        options ??= new ConfigurationPhase14VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        credentialAcquisition = new Lazy<BoundedCredentialAcquisitionAdapter>(
            () =>
                options.CredentialAcquisition
                ?? new BoundedCredentialAcquisitionAdapter(
                    options.CredentialAcquisitionFactory?.Invoke()
                        ?? (
                            options.CredentialCoreService is null
                                ? CredentialProviderCompositionRoot
                                    .CreateProduction()
                                    .AcquisitionService
                                : new LegacyV1CredentialAcquisitionService(
                                    options.CredentialCoreService
                                )
                        )
                ),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        rawJobScopeId =
            options.AzurePipelinesJobScopeId
            ?? environmentVariableReader(AzurePipelinesJobScopeIdVariable);
        jobScopeId = IsValidJobScopeId(rawJobScopeId) ? rawJobScopeId : null;
        paths = ResolvePaths(options, fileSystem, jobScopeId, environmentVariableReader);
        registryUrls = ValidateRegistryUrls(options.RegistryUrls);
        timeProvider = options.TimeProvider ?? TimeProvider.System;
        expiryPolicy = new RegistryCredentialExpiryPolicy(timeProvider, options.ExpiryPolicy);
    }

    public ConfigurationPhase14ResolvedPaths Paths => paths;

    public void ValidateConfigureRequest(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        EnsureCiJobScope(scope);
        _ = GetOwnershipManifestPath(ecosystem, scope);
        switch (ecosystem)
        {
            case CredentialEcosystem.Npm:
            case CredentialEcosystem.Pnpm:
                _ = CreateNpmDeclaration(ecosystem);
                break;
            case CredentialEcosystem.Yarn:
                _ = CreateYarnDeclaration();
                break;
            case CredentialEcosystem.Python:
                break;
            default:
                throw new NotSupportedException("Unsupported Phase 14 configuration ecosystem.");
        }
    }

    public void ValidateUnconfigureRequest(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        EnsureCiJobScope(scope);
        _ = GetOwnershipManifestPath(ecosystem, scope);
    }

    public async ValueTask<ConfigurationPhase14PlanResult> ConfigureAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) =>
        await ConfigureCoreAsync(
            ecosystem,
            scope,
            execute: true,
            forceRefresh: false,
            cancellationToken
        );

    public async ValueTask<ConfigurationPhase14PlanResult> DryRunConfigureAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) =>
        await ConfigureCoreAsync(
            ecosystem,
            scope,
            execute: false,
            forceRefresh: false,
            cancellationToken
        );

    public async ValueTask<ConfigurationPhase14PlanResult> RefreshAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) =>
        await ConfigureCoreAsync(
            ecosystem,
            scope,
            execute: true,
            forceRefresh: true,
            cancellationToken
        );

    public async ValueTask<ConfigurationPhase14PlanResult> DryRunRefreshAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) =>
        await ConfigureCoreAsync(
            ecosystem,
            scope,
            execute: false,
            forceRefresh: true,
            cancellationToken
        );

    private async ValueTask<ConfigurationPhase14PlanResult> ConfigureCoreAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        bool execute,
        bool forceRefresh,
        CancellationToken cancellationToken
    )
    {
        EnsureCiJobScope(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            forceRefresh
            && ecosystem
                is not CredentialEcosystem.Npm
                    and not CredentialEcosystem.Pnpm
                    and not CredentialEcosystem.Yarn
        )
        {
            throw new NotSupportedException(
                "Refresh supports only npm, pnpm, and Yarn registry credentials."
            );
        }

        if (IsPackageEcosystem(ecosystem))
        {
            return await ConfigurePackageCoreAsync(
                ecosystem,
                scope,
                execute,
                forceRefresh,
                cancellationToken
            );
        }

        if (ecosystem != CredentialEcosystem.Python)
        {
            throw new NotSupportedException(
                "Phase 14 configuration supports Python, npm, pnpm, and Yarn. "
                    + "Git and NuGet use their dedicated configuration services."
            );
        }

        string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
        IReadOnlyList<ConfigurationChangePlan> plans = CreatePythonPlans(scope);
        List<ConfigurationPlanResult> previewResults = [];
        foreach (ConfigurationChangePlan plan in plans)
        {
            ConfigurationChangePlan preparedPlan = AttachPreviousOwnershipManifestHashIfPresent(
                plan,
                ownershipManifestPath
            );
            previewResults.Add(
                await CreateManager(ownershipManifestPath)
                    .DryRunAsync(preparedPlan, cancellationToken)
            );
        }

        if (!execute)
        {
            return CreateResult(previewResults, ownershipManifestPath);
        }

        List<ConfigurationPlanResult> appliedResults = [];
        for (var index = 0; index < plans.Count; index++)
        {
            ConfigurationChangePlan preparedPlan = AttachPreviousOwnershipManifestHashIfPresent(
                plans[index],
                ownershipManifestPath
            );
            ConfigurationPlanResult applied = await CreateManager(ownershipManifestPath)
                .ApplyAsync(preparedPlan, cancellationToken);
            appliedResults.Add(applied with { Plan = previewResults[index].Plan });
        }

        return CreateResult(appliedResults, ownershipManifestPath);
    }

    public Uri ResolvePersistedRegistryUrl(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        EnsureCiJobScope(scope);
        if (!IsPackageEcosystem(ecosystem))
        {
            throw new NotSupportedException(
                "Registry URL inference supports only npm, pnpm, and Yarn."
            );
        }

        try
        {
            string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
            if (
                !TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out _
                )
                || !OwnershipManifestMatchesExpectedBaseState(manifest, ecosystem, scope)
                || manifest.ResourceIdentity?.ServiceEndpoint is not { } registryUrl
            )
            {
                throw CreateRegistryUrlInferenceException();
            }

            return registryUrl;
        }
        catch (Exception exception) when (IsExpectedOwnershipManifestReadOrParseFailure(exception))
        {
            throw CreateRegistryUrlInferenceException(exception);
        }
    }

    private async ValueTask<ConfigurationPhase14PlanResult> ConfigurePackageCoreAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        bool execute,
        bool forceRefresh,
        CancellationToken cancellationToken
    )
    {
        string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
        ConfigurationChangePlan requestedDryRunPlan = CreateApplyPlans(
                ecosystem,
                scope,
                CreateDryRunCredential(scope)
            )
            .Single();
        ExistingOwnershipManifest? existing = LoadRecognizedPackageManifest(
            ecosystem,
            scope,
            ownershipManifestPath
        );
        if (existing is not null && !TryValidateLifecycleManifest(existing.Manifest, scope, out _))
        {
            throw new InvalidOperationException(
                "The existing registry credential lifecycle metadata is invalid. Run "
                    + "unconfigure before configuring it again."
            );
        }

        if (
            !forceRefresh
            && existing is not null
            && await IsCurrentConfigurationFreshAsync(
                existing.Manifest,
                ecosystem,
                scope,
                requestedDryRunPlan,
                ownershipManifestPath,
                cancellationToken
            )
        )
        {
            return CreateResult(
                [
                    CreatePersistedNoOpPlanResult(
                        execute
                            ? ConfigurationPlanOperation.Apply
                            : ConfigurationPlanOperation.DryRun,
                        applied: execute,
                        requestedDryRunPlan,
                        ownershipManifestPath
                    ),
                ],
                ownershipManifestPath
            );
        }

        CredentialResult credential = execute
            ? GetPackageCredential(ecosystem, scope, cancellationToken)
            : CreateDryRunCredential(scope);
        ConfigurationChangePlan applyPlan = CreateApplyPlans(ecosystem, scope, credential).Single();
        bool replaceExisting =
            existing is not null
            && !ManifestMatchesRequestedConfiguration(existing.Manifest, applyPlan);
        ConfigurationChangePlan[] removalPlans = replaceExisting
            ? CreateRemovePlans(ecosystem, scope, existing!.Manifest, existing.Json)
            : [];

        List<ConfigurationPlanResult> previewResults = [];
        foreach (ConfigurationChangePlan removalPlan in removalPlans)
        {
            previewResults.Add(
                await CreateManager(ownershipManifestPath)
                    .DryRunAsync(
                        AttachPreviousOwnershipManifestHashIfPresent(
                            removalPlan,
                            ownershipManifestPath
                        ),
                        cancellationToken
                    )
            );
        }

        ConfigurationChangePlan preparedApplyPlan = replaceExisting
            ? applyPlan
            : AttachPreviousOwnershipManifestHashIfPresent(applyPlan, ownershipManifestPath);
        if (replaceExisting)
        {
            ConfigurationChangePlan destinationValidationPlan =
                CreateReplacementDestinationValidationPlan(applyPlan, removalPlans);
            if (destinationValidationPlan.Changes.Count > 0)
            {
                string previewManifestPath = CreateReplacementPreviewManifestPath(
                    ownershipManifestPath,
                    removalPlans.Append(destinationValidationPlan)
                );
                _ = await CreateManager(previewManifestPath)
                    .DryRunAsync(destinationValidationPlan, cancellationToken);
            }

            previewResults.Add(
                await new ConfigurationManager().DryRunAsync(preparedApplyPlan, cancellationToken)
            );
        }
        else
        {
            previewResults.Add(
                await CreateManager(ownershipManifestPath)
                    .DryRunAsync(preparedApplyPlan, cancellationToken)
            );
        }

        if (!execute)
        {
            return CreateResult(previewResults, ownershipManifestPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Keep the short replacement commit noninterruptible once removal starts.
        CancellationToken commitToken = replaceExisting
            ? CancellationToken.None
            : cancellationToken;
        List<ConfigurationPlanResult> appliedResults = [];
        foreach (ConfigurationChangePlan removalPlan in removalPlans)
        {
            ConfigurationPlanResult removed = await CreateManager(ownershipManifestPath)
                .RemoveAsync(
                    AttachPreviousOwnershipManifestHashIfPresent(
                        removalPlan,
                        ownershipManifestPath
                    ),
                    commitToken
                );
            appliedResults.Add(removed with { Plan = previewResults[appliedResults.Count].Plan });
        }

        ConfigurationPlanResult applied = await CreateManager(ownershipManifestPath)
            .ApplyAsync(
                AttachPreviousOwnershipManifestHashIfPresent(applyPlan, ownershipManifestPath),
                commitToken
            );
        appliedResults.Add(applied with { Plan = previewResults[^1].Plan });
        SetSecretFileModes(ownershipManifestPath, [applyPlan]);
        return CreateResult(appliedResults, ownershipManifestPath);
    }

    private ConfigurationChangePlan CreateReplacementDestinationValidationPlan(
        ConfigurationChangePlan applyPlan,
        IReadOnlyList<ConfigurationChangePlan> removalPlans
    )
    {
        ConfigurationChange[] removalChanges = removalPlans
            .SelectMany(static plan => plan.Changes)
            .ToArray();
        return applyPlan with
        {
            Changes = applyPlan
                .Changes.Where(applyChange =>
                    !removalChanges.Any(removalChange =>
                        applyChange.TargetKind == removalChange.TargetKind
                        && PathEquals(applyChange.TargetPathOrName, removalChange.TargetPathOrName)
                        && string.Equals(
                            applyChange.Key,
                            removalChange.Key,
                            StringComparison.Ordinal
                        )
                    )
                )
                .ToArray(),
        };
    }

    private string CreateReplacementPreviewManifestPath(
        string ownershipManifestPath,
        IEnumerable<ConfigurationChangePlan> plans
    )
    {
        string[] targetPaths = plans
            .SelectMany(static plan => plan.Changes)
            .Select(static change => change.TargetPathOrName)
            .ToArray();
        string basePath = ownershipManifestPath + ".replacement-preview";
        string candidate = basePath;
        for (var suffix = 1; ; suffix++)
        {
            if (
                !PathEquals(candidate, ownershipManifestPath)
                && !targetPaths.Any(targetPath => PathEquals(candidate, targetPath))
                && !fileSystem.FileExists(candidate)
                && !fileSystem.DirectoryExists(candidate)
            )
            {
                return candidate;
            }

            candidate = basePath + "." + suffix.ToString(CultureInfo.InvariantCulture);
        }
    }

    private ExistingOwnershipManifest? LoadRecognizedPackageManifest(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        string ownershipManifestPath
    )
    {
        try
        {
            if (
                !TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out string? json
                )
            )
            {
                return null;
            }

            if (!OwnershipManifestMatchesExpectedBaseState(manifest, ecosystem, scope))
            {
                throw new InvalidOperationException(
                    "The existing registry credential ownership manifest is not recognized."
                );
            }

            return new ExistingOwnershipManifest(manifest, json);
        }
        catch (Exception exception) when (IsExpectedOwnershipManifestReadOrParseFailure(exception))
        {
            throw new InvalidOperationException(
                "The existing registry credential ownership manifest is invalid.",
                exception
            );
        }
    }

    public async ValueTask<ConfigurationPhase14PlanResult> UnconfigureAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) => await UnconfigureCoreAsync(ecosystem, scope, execute: true, cancellationToken);

    public async ValueTask<ConfigurationPhase14PlanResult> DryRunUnconfigureAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) => await UnconfigureCoreAsync(ecosystem, scope, execute: false, cancellationToken);

    private async ValueTask<ConfigurationPhase14PlanResult> UnconfigureCoreAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        bool execute,
        CancellationToken cancellationToken
    )
    {
        EnsureCiJobScope(scope);
        cancellationToken.ThrowIfCancellationRequested();
        string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
        ConfigurationOwnershipManifest? manifest;
        string? manifestJson;
        try
        {
            if (!TryLoadOwnershipManifest(ownershipManifestPath, out manifest, out manifestJson))
            {
                return CreateResult(
                    [
                        CreateNoOpPlanResult(
                            execute
                                ? ConfigurationPlanOperation.Remove
                                : ConfigurationPlanOperation.DryRun,
                            execute
                        ),
                    ],
                    ownershipManifestPath
                );
            }
        }
        catch (Exception exception) when (IsExpectedOwnershipManifestReadOrParseFailure(exception))
        {
            return CreateResult(
                [
                    CreateNoOpPlanResult(
                        execute
                            ? ConfigurationPlanOperation.Remove
                            : ConfigurationPlanOperation.DryRun,
                        execute
                    ),
                ],
                ownershipManifestPath,
                ownershipManifestCleanupIncomplete: true
            );
        }

        bool recognized =
            ecosystem == CredentialEcosystem.Python
                ? OwnershipManifestMatchesExpectedState(manifest, ecosystem, scope)
                : OwnershipManifestMatchesExpectedBaseState(manifest, ecosystem, scope);
        if (!recognized)
        {
            return CreateResult(
                [
                    CreateNoOpPlanResult(
                        execute
                            ? ConfigurationPlanOperation.Remove
                            : ConfigurationPlanOperation.DryRun,
                        execute
                    ),
                ],
                ownershipManifestPath,
                ownershipManifestCleanupIncomplete: true
            );
        }

        if (
            IsPackageEcosystem(ecosystem)
            && manifest
                .Entries.Where(entry => EntryMatchesEcosystem(entry, ecosystem))
                .All(entry => !ConfigurationTargetExists(entry.TargetPathOrName))
        )
        {
            if (execute)
            {
                fileSystem.DeleteFile(ownershipManifestPath);
                if (scope == ConfigurationPhase14Scope.CiTemporary)
                {
                    DeleteKnownCiTemporaryContainerIfEmpty(ecosystem);
                }
            }

            return CreateResult(
                [
                    CreateNoOpPlanResult(
                        execute
                            ? ConfigurationPlanOperation.Remove
                            : ConfigurationPlanOperation.DryRun,
                        execute
                    ),
                ],
                ownershipManifestPath,
                lifecycleStateMutated: execute
            );
        }

        ConfigurationChangePlan[] plans = CreateRemovePlans(
            ecosystem,
            scope,
            manifest,
            manifestJson
        );
        List<ConfigurationPlanResult> previewResults = [];
        foreach (ConfigurationChangePlan plan in plans)
        {
            ConfigurationChangePlan preparedPlan = AttachPreviousOwnershipManifestHashIfPresent(
                plan,
                ownershipManifestPath
            );
            previewResults.Add(
                await CreateManager(ownershipManifestPath)
                    .DryRunAsync(preparedPlan, cancellationToken)
            );
        }

        if (!execute)
        {
            return previewResults.Count == 0
                ? CreateResult(
                    [CreateNoOpPlanResult(ConfigurationPlanOperation.DryRun, applied: false)],
                    ownershipManifestPath
                )
                : CreateResult(previewResults, ownershipManifestPath);
        }

        List<ConfigurationPlanResult> removedResults = [];
        for (var index = 0; index < plans.Length; index++)
        {
            ConfigurationChangePlan preparedPlan = AttachPreviousOwnershipManifestHashIfPresent(
                plans[index],
                ownershipManifestPath
            );
            ConfigurationPlanResult removed = await CreateManager(ownershipManifestPath)
                .RemoveAsync(preparedPlan, cancellationToken);
            removedResults.Add(removed with { Plan = previewResults[index].Plan });
        }

        if (
            scope == ConfigurationPhase14Scope.CiTemporary
            && !SafeFileExists(ownershipManifestPath)
        )
        {
            DeleteKnownCiTemporaryContainerIfEmpty(ecosystem);
        }

        return removedResults.Count == 0
            ? CreateResult(
                [CreateNoOpPlanResult(ConfigurationPlanOperation.Remove, applied: true)],
                ownershipManifestPath,
                lifecycleStateMutated: true
            )
            : CreateResult(removedResults, ownershipManifestPath, lifecycleStateMutated: true);
    }

    public async ValueTask<ConfigurationPhase14DoctorResult> DoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<ConfigurationPhase14EcosystemDoctorResult> ecosystemResults = [];
        foreach (CredentialEcosystem ecosystem in GetSupportedUserEcosystems())
        {
            ecosystemResults.Add(
                await InspectDoctorAsync(
                    ecosystem,
                    ConfigurationPhase14Scope.User,
                    cancellationToken
                )
            );
        }

        foreach (CredentialEcosystem ecosystem in GetSupportedCiTemporaryEcosystems())
        {
            if (TemporaryStateExists(ecosystem, ConfigurationPhase14Scope.CiTemporary))
            {
                ecosystemResults.Add(
                    await InspectDoctorAsync(
                        ecosystem,
                        ConfigurationPhase14Scope.CiTemporary,
                        cancellationToken
                    )
                );
            }
        }

        return new ConfigurationPhase14DoctorResult
        {
            Paths = paths,
            Ecosystems = ecosystemResults,
            AzurePipelinesSystemAccessTokenPresent = !string.IsNullOrWhiteSpace(
                environmentVariableReader(AzurePipelinesSystemAccessTokenVariable)
            ),
            PersistentDerivedCredentialCacheEnabled = false,
        };
    }

    public async ValueTask<ConfigurationPhase14CleanupResult> CleanupAsync(
        CredentialEcosystem? ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) => await CleanupCoreAsync(ecosystem, scope, execute: true, cancellationToken);

    public async ValueTask<ConfigurationPhase14CleanupResult> DryRunCleanupAsync(
        CredentialEcosystem? ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken = default
    ) => await CleanupCoreAsync(ecosystem, scope, execute: false, cancellationToken);

    private async ValueTask<ConfigurationPhase14CleanupResult> CleanupCoreAsync(
        CredentialEcosystem? ecosystem,
        ConfigurationPhase14Scope scope,
        bool execute,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scope != ConfigurationPhase14Scope.CiTemporary)
        {
            return new ConfigurationPhase14CleanupResult
            {
                Paths = paths,
                Scope = scope,
                Ecosystems = [],
                PersistentDerivedCredentialsRemoved = false,
            };
        }

        List<ConfigurationPhase14CleanupEcosystemResult> cleanupResults = [];
        foreach (CredentialEcosystem targetEcosystem in GetCleanupEcosystems(ecosystem))
        {
            if (ecosystem is null && targetEcosystem == CredentialEcosystem.Pnpm)
            {
                cleanupResults.Add(CreateSharedPnpmCleanupAliasResult());
                continue;
            }

            try
            {
                EnsureCiJobScope(scope);
                ConfigurationPhase14CleanupEcosystemResult cleanupResult =
                    await CleanupCiTemporaryEcosystemAsync(
                        targetEcosystem,
                        execute,
                        cancellationToken
                    );
                cleanupResults.Add(cleanupResult);
            }
            catch (Exception exception) when (IsExpectedCleanupFailure(exception))
            {
                cleanupResults.Add(
                    CreateIncompleteCleanupResult(
                        targetEcosystem,
                        ConfigurationPhase14Scope.CiTemporary
                    )
                );
            }
        }

        return new ConfigurationPhase14CleanupResult
        {
            Paths = paths,
            Scope = scope,
            Ecosystems = cleanupResults,
            PersistentDerivedCredentialsRemoved = false,
        };
    }

    public async ValueTask<ConfigurationPhase14CleanupResult> LogoutAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ConfigurationPhase14CleanupEcosystemResult> results = [];
        foreach (CredentialEcosystem ecosystem in GetSupportedUserPackageEcosystems())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ConfigurationPhase14PlanResult removed = await UnconfigureAsync(
                    ecosystem,
                    ConfigurationPhase14Scope.User,
                    cancellationToken
                );
                results.Add(
                    CreateCleanupResult(ecosystem, ConfigurationPhase14Scope.User, removed)
                );
            }
            catch (Exception exception) when (IsExpectedCleanupFailure(exception))
            {
                results.Add(
                    CreateIncompleteCleanupResult(ecosystem, ConfigurationPhase14Scope.User)
                );
            }
        }

        if (rawJobScopeId is not null)
        {
            try
            {
                ConfigurationPhase14CleanupResult ci = await CleanupAsync(
                    ecosystem: null,
                    ConfigurationPhase14Scope.CiTemporary,
                    cancellationToken
                );
                results.AddRange(ci.Ecosystems);
            }
            catch (Exception exception) when (IsExpectedCleanupFailure(exception))
            {
                foreach (CredentialEcosystem ecosystem in GetSupportedCiTemporaryEcosystems())
                {
                    results.Add(
                        CreateIncompleteCleanupResult(
                            ecosystem,
                            ConfigurationPhase14Scope.CiTemporary
                        )
                    );
                }
            }
        }

        return new ConfigurationPhase14CleanupResult
        {
            Paths = paths,
            Scope = ConfigurationPhase14Scope.User,
            Ecosystems = results,
            PersistentDerivedCredentialsRemoved = false,
        };
    }

    private IReadOnlyList<ConfigurationChangePlan> CreateApplyPlans(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CredentialResult? credential
    ) =>
        ecosystem switch
        {
            CredentialEcosystem.Python => CreatePythonPlans(scope),
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm =>
            [
                CreateNpmCompatiblePlan(
                    ecosystem,
                    scope,
                    credential
                        ?? throw new InvalidOperationException("Package credential is required.")
                ),
            ],
            CredentialEcosystem.Yarn =>
            [
                CreateYarnPlan(
                    scope,
                    credential
                        ?? throw new InvalidOperationException("Package credential is required.")
                ),
            ],
            _ => throw new NotSupportedException(
                "Phase 14.2 configuration orchestration supports Python, npm, pnpm, and Yarn."
            ),
        };

    private IReadOnlyList<ConfigurationChangePlan> CreatePythonPlans(
        ConfigurationPhase14Scope scope
    )
    {
        if (scope != ConfigurationPhase14Scope.User)
        {
            throw new NotSupportedException(
                "Phase 14.2 Python configuration supports only user scope."
            );
        }

        ConfigurationTargetLayoutProjection backendProjection =
            ConfigurationLayoutProjector.ProjectPythonKeyringBackend(
                CreateCurrentLayoutProjectionContext()
            );
        ConfigurationTargetLayoutProjection shimProjection =
            ConfigurationLayoutProjector.ProjectKeyringShim(CreateCurrentLayoutProjectionContext());
        return
        [
            CreatePythonPlan(
                "backend",
                ConfigurationTargetKind.PythonKeyringBackend,
                backendProjection.TargetPath,
                CreatePythonBackendManifestValue(shimProjection.TargetPath)
            ),
            CreatePythonPlan(
                "shim",
                ConfigurationTargetKind.KeyringShim,
                shimProjection.TargetPath,
                CreateKeyringShimValue()
            ),
        ];
    }

    private static ConfigurationChangePlan CreatePythonPlan(
        string suffix,
        ConfigurationTargetKind targetKind,
        string targetPath,
        string value
    )
    {
        return ConfigurationChangePlanPolicy.Create(
            PythonPlanId + "-" + suffix,
            PythonChangeSetId + "-" + suffix,
            ProductId,
            ConfigurationScope.User,
            CreatePythonManifestMetadata(),
            [CreatePythonPhysicalTargetChange(targetKind, targetPath, value)],
            containsCredentialMaterial: false
        );
    }

    private ConfigurationChangePlan CreateNpmCompatiblePlan(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CredentialResult credential,
        CanonicalResourceIdentity? resourceOverride = null,
        string? targetPathOverride = null
    )
    {
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader,
                UserNpmrcPath =
                    ecosystem == CredentialEcosystem.Pnpm
                        ? paths.PnpmUserNpmrcPath
                        : paths.NpmUserNpmrcPath,
            }
        );
        NpmPhase12RegistryDeclaration declaration = resourceOverride is null
            ? CreateNpmDeclaration(ecosystem)
            : CreateNpmDeclaration(ecosystem, resourceOverride);
        var request = new NpmPhase12CredentialPlanRequest
        {
            Declaration = declaration,
            AuthToken = GetRequiredBearerToken(credential),
            Ecosystem = ecosystem,
            TargetNpmrcPath = targetPathOverride ?? GetNpmTargetPath(ecosystem, scope),
        };
        ConfigurationChangePlan plan =
            scope == ConfigurationPhase14Scope.CiTemporary
                ? service.CreateCiTemporaryCredentialPlan(request)
                : service.CreateUserCredentialPlan(request);
        if (scope == ConfigurationPhase14Scope.CiTemporary)
        {
            plan = plan with
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        TargetKind = ConfigurationTargetKind.Npmrc,
                        TargetPathOrName = request.TargetNpmrcPath!,
                        Key = declaration.Key,
                        Value = declaration.RegistryUrl.AbsoluteUri,
                        IsSecretValue = false,
                        RequiresOwnershipRecord = true,
                    },
                    .. plan.Changes,
                ],
                DeclarationPreservation =
                    ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            };
        }

        return AttachCredentialLifecycle(plan, scope, credential);
    }

    private ConfigurationChangePlan CreateYarnPlan(
        ConfigurationPhase14Scope scope,
        CredentialResult credential,
        CanonicalResourceIdentity? resourceOverride = null
    )
    {
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environmentVariableReader,
                UserYarnrcPath = paths.YarnUserYarnrcPath,
            }
        );
        YarnPhase13RegistryDeclaration declaration = resourceOverride is null
            ? CreateYarnDeclaration()
            : CreateYarnDeclaration(resourceOverride);
        var request = new YarnPhase13CredentialPlanRequest
        {
            Declaration = declaration,
            AuthToken = GetRequiredBearerToken(credential),
            TargetYarnrcPath = paths.YarnUserYarnrcPath,
            TemporaryHomePath = paths.YarnCiTemporaryHomePath,
        };
        ConfigurationChangePlan plan =
            scope == ConfigurationPhase14Scope.CiTemporary
                ? service.CreateCiTemporaryCredentialPlan(request)
                : service.CreateUserCredentialPlan(request);
        if (scope == ConfigurationPhase14Scope.CiTemporary)
        {
            plan = plan with
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        TargetKind = ConfigurationTargetKind.Yarnrc,
                        TargetPathOrName = Path.Combine(
                            paths.YarnCiTemporaryHomePath,
                            ".yarnrc.yml"
                        ),
                        Key = declaration.Key,
                        Value = declaration.RegistryUrl.AbsoluteUri,
                        IsSecretValue = false,
                        RequiresOwnershipRecord = true,
                    },
                    .. plan.Changes,
                ],
                DeclarationPreservation =
                    ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            };
        }

        return AttachCredentialLifecycle(plan, scope, credential);
    }

    private CredentialResult GetPackageCredential(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken
    )
    {
        CanonicalResourceIdentity resource =
            ecosystem == CredentialEcosystem.Yarn
                ? CreateYarnDeclaration().ResourceIdentity
                : CreateNpmDeclaration(ecosystem).ResourceIdentity;
        bool ciTemporary = scope == ConfigurationPhase14Scope.CiTemporary;
        var request = new CredentialRequestV2
        {
            Ecosystem = ecosystem,
            Operation = CredentialOperation.Get,
            Resource = resource,
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = CredentialKind.NpmAuthToken,
            IdentityFlow = ciTemporary
                ? IdentityFlow.AzurePipelinesSystemAccessToken
                : IdentityFlow.InteractiveBrowser,
            InteractivePolicy = ciTemporary
                ? InteractivePolicy.Never
                : InteractivePolicy.UserAllowed,
            AcquisitionMode = ciTemporary
                ? AcquisitionMode.Unspecified
                : AcquisitionMode.InteractionAllowed,
            CachePolicy = ciTemporary
                ? CachePolicyMode.NonPersistentCi
                : CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = ciTemporary
                ? new CiContext
                {
                    ExplicitCiMode = true,
                    Provider = CiProviderNames.AzurePipelines,
                    HasAzurePipelinesSystemAccessToken = true,
                    AllowsPersistentWrites = false,
                }
                : null,
        };
        CredentialResult result = ciTemporary
            ? AzurePipelinesSystemAccessTokenService
                .Handle(
                    ToV1CiRequest(request),
                    environmentVariableReader(AzurePipelinesSystemAccessTokenVariable)
                )
                .CreateProtocolResult("wp5-ci-temporary-configuration")
            : credentialAcquisition.Value.Acquire(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            result.Status != CredentialResultStatus.Success
            || string.IsNullOrWhiteSpace(result.BearerToken)
        )
        {
            throw new InvalidOperationException(
                result.Error?.SafeMessage ?? "Failed to acquire a package authentication token."
            );
        }

        if (!ciTemporary && result.ExpiresAt is null)
        {
            throw new InvalidOperationException(
                "The acquired user registry credential did not include an expiry."
            );
        }

        return result;
    }

    private static CredentialRequest ToV1CiRequest(CredentialRequestV2 request) =>
        new()
        {
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource,
            ServiceIdentity = request.ServiceIdentity,
            AccountHint = request.AccountHint,
            TenantHint = request.TenantHint,
            RequestedAudience = request.RequestedAudience,
            CredentialKind = request.CredentialKind,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = request.InteractivePolicy,
            CachePolicy = request.CachePolicy,
            CiContext = request.CiContext,
            ExtensionData = request.ExtensionData,
        };

    private CredentialResult CreateDryRunCredential(ConfigurationPhase14Scope scope) =>
        new()
        {
            Status = CredentialResultStatus.Success,
            BearerToken = "dry-run-redacted-placeholder",
            ExpiresAt =
                scope == ConfigurationPhase14Scope.User
                    ? timeProvider.GetUtcNow().AddHours(1)
                    : null,
            DiagnosticsCorrelationId = "dry-run",
        };

    private ConfigurationChangePlan AttachCredentialLifecycle(
        ConfigurationChangePlan plan,
        ConfigurationPhase14Scope scope,
        CredentialResult credential
    )
    {
        RegistryCredentialLifecycleMetadata metadata = expiryPolicy.Create(
            scope == ConfigurationPhase14Scope.CiTemporary
                ? ConfigurationScope.CiTemporary
                : ConfigurationScope.User,
            credential.ExpiresAt
        );
        return plan with
        {
            Manifest = plan.Manifest with
            {
                SafeMetadata = RegistryCredentialLifecycleMetadataCodec.Write(
                    plan.Manifest.SafeMetadata,
                    metadata
                ),
            },
        };
    }

    private static string GetRequiredBearerToken(CredentialResult credential) =>
        !string.IsNullOrWhiteSpace(credential.BearerToken)
            ? credential.BearerToken
            : throw new InvalidOperationException("Package credential material is missing.");

    private void SetSecretFileModes(
        string ownershipManifestPath,
        IReadOnlyList<ConfigurationChangePlan> plans
    )
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode secretMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        foreach (
            string path in plans
                .SelectMany(plan => plan.Changes)
                .Where(change => change.IsSecretValue)
                .Select(change => change.TargetPathOrName)
                .Append(ownershipManifestPath)
                .Select(NormalizePath)
                .Distinct(GetPathComparerForPaths())
        )
        {
            if (fileSystem.FileExists(path))
            {
                fileSystem.SetUnixFileMode(path, secretMode);
            }
        }
    }

    private string GetNpmTargetPath(CredentialEcosystem ecosystem, ConfigurationPhase14Scope scope)
    {
        return (ecosystem, scope) switch
        {
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.User) => paths.NpmUserNpmrcPath,
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.User) => paths.PnpmUserNpmrcPath,
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.CiTemporary) =>
                paths.NpmCiTemporaryNpmrcPath,
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.CiTemporary) =>
                paths.PnpmCiTemporaryNpmrcPath,
            _ => throw new NotSupportedException("Unsupported npm-compatible configuration scope."),
        };
    }

    private async ValueTask<ConfigurationPhase14EcosystemDoctorResult> InspectDoctorAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string ownershipManifestPath = GetOwnershipManifestPath(ecosystem, scope);
        bool ownershipManifestPresent = SafeFileExists(ownershipManifestPath);
        bool ownedTargetPresent = TryInspectOwnedTargetPresence(
            ecosystem,
            scope,
            ownershipManifestPath
        );
        bool temporaryContainerPresent = TemporaryContainerExists(ecosystem, scope);
        bool configurationPlanValid =
            ownershipManifestPresent
                ? await TryValidateRemovalPlanAsync(
                    ecosystem,
                    scope,
                    ownershipManifestPath,
                    cancellationToken
                )
            : scope == ConfigurationPhase14Scope.CiTemporary && temporaryContainerPresent ? false
            : true;
        RegistryCredentialLifecycleMetadata? lifecycle = null;
        RegistryCredentialLifecycleState lifecycleState = RegistryCredentialLifecycleState.Missing;
        Uri? registryUrl = null;
        if (IsPackageEcosystem(ecosystem) && ownershipManifestPresent)
        {
            bool lifecycleMetadataPresent = OwnershipManifestContainsLifecycleMetadata(
                ownershipManifestPath
            );
            bool lifecycleStructurallyValid = TryGetCurrentLifecycle(
                scope,
                ownershipManifestPath,
                out lifecycle
            );
            lifecycleState =
                lifecycleStructurallyValid && ownedTargetPresent
                    ? expiryPolicy.Evaluate(
                        lifecycle,
                        scope == ConfigurationPhase14Scope.CiTemporary
                            ? ConfigurationScope.CiTemporary
                            : ConfigurationScope.User
                    )
                : lifecycleStructurallyValid && !ownedTargetPresent
                    ? RegistryCredentialLifecycleState.Missing
                : lifecycleMetadataPresent ? RegistryCredentialLifecycleState.Invalid
                : RegistryCredentialLifecycleState.Missing;
            configurationPlanValid &=
                (lifecycleStructurallyValid && ownedTargetPresent)
                || (lifecycleStructurallyValid && !ownedTargetPresent)
                || !lifecycleMetadataPresent;
            registryUrl = TryGetManifestRegistryUrl(ownershipManifestPath);
        }

        return new ConfigurationPhase14EcosystemDoctorResult
        {
            Ecosystem = ecosystem,
            Scope = scope,
            ConfigurationPlanValid = configurationPlanValid,
            OwnershipManifestPresent = ownershipManifestPresent,
            OwnedTargetPresent = ownedTargetPresent,
            TemporaryContainerPresent = temporaryContainerPresent,
            LifecycleState = lifecycleState,
            CredentialExpiresAt = lifecycle?.ExpiresAt,
            RegistryUrl = registryUrl,
        };
    }

    private async ValueTask<ConfigurationPhase14CleanupEcosystemResult> CleanupCiTemporaryEcosystemAsync(
        CredentialEcosystem ecosystem,
        bool execute,
        CancellationToken cancellationToken
    )
    {
        if (!execute)
        {
            return await InspectCiTemporaryCleanupAsync(ecosystem, cancellationToken);
        }

        bool temporaryContainerBefore = TemporaryContainerExists(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary
        );
        string ownershipManifestPath = GetOwnershipManifestPath(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary
        );
        bool ownershipManifestBefore = SafeFileExists(ownershipManifestPath);
        ConfigurationPhase14PlanResult result = await UnconfigureCoreAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            execute: true,
            cancellationToken
        );
        if (!SafeFileExists(ownershipManifestPath) && IsKnownCiTemporaryContainerEmpty(ecosystem))
        {
            DeleteKnownCiTemporaryContainer(ecosystem);
        }

        bool temporaryContainerAfter = TemporaryContainerExists(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary
        );
        bool ownershipManifestAfter = SafeFileExists(ownershipManifestPath);
        string state =
            temporaryContainerAfter || ownershipManifestAfter ? CleanupStateIncomplete
            : result.ChangeCount > 0 || temporaryContainerBefore || ownershipManifestBefore
                ? CleanupStateRemoved
            : CleanupStateNotNeeded;

        return new ConfigurationPhase14CleanupEcosystemResult
        {
            Ecosystem = ecosystem,
            Scope = ConfigurationPhase14Scope.CiTemporary,
            State = state,
            ChangeCount = result.ChangeCount,
            AppliedChangeCount = result.AppliedChangeCount,
            OwnershipManifestPresent = ownershipManifestAfter,
            TemporaryContainerPresent = temporaryContainerAfter,
        };
    }

    private async ValueTask<ConfigurationPhase14CleanupEcosystemResult> InspectCiTemporaryCleanupAsync(
        CredentialEcosystem ecosystem,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool temporaryContainerPresent = TemporaryContainerExists(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary
        );
        string ownershipManifestPath = GetOwnershipManifestPath(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary
        );
        bool ownershipManifestPresent = SafeFileExists(ownershipManifestPath);
        ConfigurationPhase14PlanResult result = await UnconfigureCoreAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            execute: false,
            cancellationToken
        );

        bool cleanupIncomplete =
            result.OwnershipManifestCleanupIncomplete
            || (
                !ownershipManifestPresent
                && temporaryContainerPresent
                && !IsKnownCiTemporaryContainerEmpty(ecosystem)
            );
        string state =
            cleanupIncomplete ? CleanupStateIncomplete
            : ownershipManifestPresent || temporaryContainerPresent ? CleanupStateRemoved
            : CleanupStateNotNeeded;
        return new ConfigurationPhase14CleanupEcosystemResult
        {
            Ecosystem = ecosystem,
            Scope = ConfigurationPhase14Scope.CiTemporary,
            State = state,
            ChangeCount = result.ChangeCount,
            AppliedChangeCount = 0,
            OwnershipManifestPresent = ownershipManifestPresent,
            TemporaryContainerPresent = temporaryContainerPresent,
        };
    }

    private bool IsKnownCiTemporaryContainerEmpty(CredentialEcosystem ecosystem)
    {
        try
        {
            return ecosystem switch
            {
                CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => !SafeFileExists(
                    GetNpmTargetPath(ecosystem, ConfigurationPhase14Scope.CiTemporary)
                )
                    || fileSystem
                        .ReadAllBytes(
                            GetNpmTargetPath(ecosystem, ConfigurationPhase14Scope.CiTemporary)
                        )
                        .Length == 0,
                CredentialEcosystem.Yarn => !fileSystem
                    .EnumerateFiles(paths.YarnCiTemporaryHomePath, "*", SearchOption.AllDirectories)
                    .Any()
                    && !fileSystem
                        .EnumerateDirectories(
                            paths.YarnCiTemporaryHomePath,
                            "*",
                            SearchOption.AllDirectories
                        )
                        .Any(),
                _ => false,
            };
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool OwnershipManifestMatchesExpectedState(
        ConfigurationOwnershipManifest manifest,
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    ) =>
        ecosystem == CredentialEcosystem.Python
        || OwnershipManifestMatchesExpectedBaseState(manifest, ecosystem, scope);

    private bool OwnershipManifestMatchesExpectedBaseState(
        ConfigurationOwnershipManifest manifest,
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        if (
            !ManifestMatchesExpectedOwnership(manifest, scope)
            || !PackageManifestEntriesMatchExpectedState(manifest, ecosystem, scope)
            || manifest.ResourceIdentity?.ServiceEndpoint is not { } registryUrl
            || !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                registryUrl,
                out CanonicalResourceIdentity? expectedResource
            )
            || !Equals(manifest.ResourceIdentity, expectedResource)
        )
        {
            return false;
        }

        string expectedSelector = ecosystem is CredentialEcosystem.Npm or CredentialEcosystem.Pnpm
            ? NpmCompatibleAuthSelectorPolicy.Create(expectedResource).NpmAuthTokenKey
            : "npmRegistries." + registryUrl.AbsoluteUri + ".npmAuthToken";
        return string.Equals(manifest.EntrySelector, expectedSelector, StringComparison.Ordinal);
    }

    private bool PackageManifestEntriesMatchExpectedState(
        ConfigurationOwnershipManifest manifest,
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    ) =>
        ecosystem switch
        {
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => string.Equals(
                manifest.ManifestId,
                NpmCredentialManifestId,
                StringComparison.Ordinal
            ) && ManifestEntriesMatchNpmCompatibleState(manifest, scope),
            CredentialEcosystem.Yarn => string.Equals(
                manifest.ManifestId,
                YarnCredentialManifestId,
                StringComparison.Ordinal
            ) && ManifestEntriesMatchYarnState(manifest, scope),
            _ => false,
        };

    private static bool ManifestEntriesMatchNpmCompatibleState(
        ConfigurationOwnershipManifest manifest,
        ConfigurationPhase14Scope scope
    )
    {
        ConfigurationOwnershipManifestEntry? authEntry = manifest.Entries.SingleOrDefault(entry =>
            string.Equals(entry.Key, manifest.EntrySelector, StringComparison.Ordinal)
        );
        if (
            authEntry is null
            || authEntry.Operation != ConfigurationChangeOperation.Set
            || authEntry.TargetKind != ConfigurationTargetKind.Npmrc
            || !authEntry.HasPlannedValue
            || !authEntry.IsSecretValue
        )
        {
            return false;
        }

        if (scope == ConfigurationPhase14Scope.User || manifest.Entries.Count == 1)
        {
            return manifest.Entries.Count == 1;
        }

        ConfigurationOwnershipManifestEntry? registryEntry = manifest.Entries.SingleOrDefault(
            static entry => string.Equals(entry.Key, "registry", StringComparison.Ordinal)
        );
        return manifest.Entries.Count == 2
            && registryEntry is not null
            && registryEntry.Operation == ConfigurationChangeOperation.Set
            && registryEntry.TargetKind == ConfigurationTargetKind.Npmrc
            && registryEntry.HasPlannedValue
            && !registryEntry.IsSecretValue
            && string.Equals(
                registryEntry.TargetPathOrName,
                authEntry.TargetPathOrName,
                StringComparison.Ordinal
            );
    }

    private bool ManifestEntriesMatchYarnState(
        ConfigurationOwnershipManifest manifest,
        ConfigurationPhase14Scope scope
    )
    {
        string? expectedAlwaysAuthSelector = manifest.ResourceIdentity?.ServiceEndpoint
            is { } registryUrl
            ? "npmRegistries." + registryUrl.AbsoluteUri + ".npmAlwaysAuth"
            : null;
        string? targetPath = GetSingleNormalizedPathOrDefault(
            manifest.Entries.Select(static entry => entry.TargetPathOrName)
        );
        int expectedEntryCount = scope == ConfigurationPhase14Scope.CiTemporary ? 3 : 2;
        return manifest.Entries.Count == expectedEntryCount
            && targetPath is not null
            && manifest.Entries.All(entry =>
                entry.Operation == ConfigurationChangeOperation.Set
                && entry.TargetKind == ConfigurationTargetKind.Yarnrc
                && PathEquals(entry.TargetPathOrName, targetPath)
                && entry.HasPlannedValue
            )
            && manifest.Entries.Count(entry =>
                entry.IsSecretValue
                && string.Equals(entry.Key, manifest.EntrySelector, StringComparison.Ordinal)
            ) == 1
            && manifest.Entries.Count(entry =>
                !entry.IsSecretValue
                && string.Equals(entry.Key, expectedAlwaysAuthSelector, StringComparison.Ordinal)
            ) == 1
            && (
                scope != ConfigurationPhase14Scope.CiTemporary
                || manifest.Entries.Count(entry =>
                    !entry.IsSecretValue
                    && string.Equals(entry.Key, "npmRegistryServer", StringComparison.Ordinal)
                ) == 1
            );
    }

    private static bool TryValidateLifecycleManifest(
        ConfigurationOwnershipManifest manifest,
        ConfigurationPhase14Scope scope,
        [NotNullWhen(true)] out RegistryCredentialLifecycleMetadata? lifecycle
    )
    {
        lifecycle = null;
        if (
            !ManifestMatchesExpectedOwnership(manifest, scope)
            || !RegistryCredentialLifecycleMetadataCodec.TryRead(
                manifest.SafeMetadata,
                out RegistryCredentialLifecycleMetadata? parsedLifecycle
            )
            || parsedLifecycle is null
        )
        {
            return false;
        }

        lifecycle = parsedLifecycle;
        return true;
    }

    private static bool ManifestMatchesExpectedOwnership(
        ConfigurationOwnershipManifest manifest,
        ConfigurationPhase14Scope scope
    ) =>
        manifest.SchemaVersion == ConfigurationOwnershipManifest.CurrentSchemaVersion
        && string.Equals(manifest.OwnerProductId, ProductId, StringComparison.Ordinal)
        && manifest.Scope
            == (
                scope == ConfigurationPhase14Scope.CiTemporary
                    ? ConfigurationScope.CiTemporary
                    : ConfigurationScope.User
            )
        && manifest.ResourceIdentity is not null;

    private async ValueTask<bool> IsCurrentConfigurationFreshAsync(
        ConfigurationOwnershipManifest manifest,
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        ConfigurationChangePlan requestedPlan,
        string ownershipManifestPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (
                !OwnershipManifestMatchesExpectedBaseState(manifest, ecosystem, scope)
                || !ManifestMatchesRequestedConfiguration(manifest, requestedPlan)
                || !TryValidateLifecycleManifest(
                    manifest,
                    scope,
                    out RegistryCredentialLifecycleMetadata? lifecycle
                )
            )
            {
                return false;
            }

            return expiryPolicy.Evaluate(
                    lifecycle,
                    scope == ConfigurationPhase14Scope.CiTemporary
                        ? ConfigurationScope.CiTemporary
                        : ConfigurationScope.User
                ) == RegistryCredentialLifecycleState.Fresh
                && await TryValidateRemovalPlanAsync(
                    ecosystem,
                    scope,
                    ownershipManifestPath,
                    cancellationToken
                );
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool ManifestMatchesRequestedConfiguration(
        ConfigurationOwnershipManifest manifest,
        ConfigurationChangePlan requestedPlan
    )
    {
        string existingTargetPath = GetSingleNormalizedPath(
            manifest.Entries.Select(static entry => entry.TargetPathOrName)
        );
        string requestedTargetPath = GetSingleNormalizedPath(
            requestedPlan.Changes.Select(static change => change.TargetPathOrName)
        );
        return Equals(manifest.ResourceIdentity, requestedPlan.Manifest.ResourceIdentity)
            && string.Equals(
                manifest.EntrySelector,
                requestedPlan.Manifest.EntrySelector,
                StringComparison.Ordinal
            )
            && PathEquals(existingTargetPath, requestedTargetPath)
            && manifest.Entries.Count == requestedPlan.Changes.Count
            && manifest.Entries.All(entry =>
                requestedPlan.Changes.Any(change =>
                    change.Operation == ConfigurationChangeOperation.Set
                    && change.TargetKind == entry.TargetKind
                    && PathEquals(change.TargetPathOrName, entry.TargetPathOrName)
                    && string.Equals(change.Key, entry.Key, StringComparison.Ordinal)
                )
            );
    }

    private static InvalidOperationException CreateRegistryUrlInferenceException(
        Exception? innerException = null
    ) =>
        new(
            "The registry URL was omitted and could not be inferred from the canonical ownership "
                + "manifest. Specify --registry-url <url>; run status or doctor, then "
                + "unconfigure the package ecosystem to remediate invalid state.",
            innerException
        );

    private bool TryGetCurrentLifecycle(
        ConfigurationPhase14Scope scope,
        string ownershipManifestPath,
        [NotNullWhen(true)] out RegistryCredentialLifecycleMetadata? lifecycle
    )
    {
        lifecycle = null;
        try
        {
            if (
                !TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out _
                )
            )
            {
                return false;
            }

            return TryValidateLifecycleManifest(manifest, scope, out lifecycle);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private Uri? TryGetManifestRegistryUrl(string ownershipManifestPath)
    {
        try
        {
            if (
                !TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out _
                ) || manifest.ResourceIdentity is null
            )
            {
                return null;
            }

            return manifest.ResourceIdentity.ServiceEndpoint;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return null;
        }
    }

    private bool OwnershipManifestContainsLifecycleMetadata(string ownershipManifestPath)
    {
        try
        {
            return TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out _
                )
                && RegistryCredentialLifecycleMetadataCodec.ContainsLifecycleMetadata(
                    manifest.SafeMetadata
                );
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return true;
        }
    }

    private static bool IsExpectedOwnershipManifestReadOrParseFailure(Exception exception) =>
        exception
            is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or System.Text.Json.JsonException;

    private void DeleteKnownCiTemporaryContainer(CredentialEcosystem ecosystem)
    {
        switch (ecosystem)
        {
            case CredentialEcosystem.Npm:
            case CredentialEcosystem.Pnpm:
                string path = GetNpmTargetPath(ecosystem, ConfigurationPhase14Scope.CiTemporary);
                if (SafeFileExists(path))
                {
                    fileSystem.DeleteFile(path);
                }

                break;
            case CredentialEcosystem.Yarn:
                fileSystem.DeleteDirectory(paths.YarnCiTemporaryHomePath, recursive: true);
                break;
            default:
                throw new NotSupportedException(
                    "Phase 14.3 cleanup supports npm, pnpm, and Yarn CI temporary state."
                );
        }
    }

    private void DeleteKnownCiTemporaryContainerIfEmpty(CredentialEcosystem ecosystem)
    {
        if (IsKnownCiTemporaryContainerEmpty(ecosystem))
        {
            DeleteKnownCiTemporaryContainer(ecosystem);
        }
    }

    private async ValueTask<bool> TryValidateRemovalPlanAsync(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        string ownershipManifestPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (
                !TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out string? manifestJson
                )
            )
            {
                return false;
            }

            bool recognized =
                ecosystem == CredentialEcosystem.Python
                    ? OwnershipManifestMatchesExpectedState(manifest, ecosystem, scope)
                    : OwnershipManifestMatchesExpectedBaseState(manifest, ecosystem, scope);
            if (!recognized)
            {
                return false;
            }

            foreach (
                ConfigurationChangePlan plan in CreateRemovePlans(
                    ecosystem,
                    scope,
                    manifest,
                    manifestJson
                )
            )
            {
                ConfigurationPlanResult result = await CreateManager(ownershipManifestPath)
                    .DryRunAsync(
                        AttachPreviousOwnershipManifestHashIfPresent(plan, ownershipManifestPath),
                        cancellationToken
                    );
                if (result.State != ConfigurationPlanState.Planned)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool TryInspectOwnedTargetPresence(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        string ownershipManifestPath
    )
    {
        try
        {
            if (
                !TryLoadOwnershipManifest(
                    ownershipManifestPath,
                    out ConfigurationOwnershipManifest? manifest,
                    out _
                )
            )
            {
                return false;
            }

            ConfigurationOwnershipManifestEntry[] entries = manifest
                .Entries.Where(entry => EntryMatchesEcosystem(entry, ecosystem))
                .ToArray();
            return entries.Length > 0
                && entries.All(entry => ConfigurationTargetExists(entry.TargetPathOrName));
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool ConfigurationTargetExists(string targetPath)
    {
        try
        {
            return fileSystem.FileExists(targetPath) || fileSystem.DirectoryExists(targetPath);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool TemporaryStateExists(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        return SafeFileExists(GetOwnershipManifestPath(ecosystem, scope))
            || TemporaryContainerExists(ecosystem, scope);
    }

    private bool TemporaryContainerExists(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        if (scope != ConfigurationPhase14Scope.CiTemporary)
        {
            return false;
        }

        return ecosystem switch
        {
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => SafeFileExists(
                GetNpmTargetPath(ecosystem, scope)
            ),
            CredentialEcosystem.Yarn => SafeDirectoryExists(paths.YarnCiTemporaryHomePath),
            _ => false,
        };
    }

    private bool SafeFileExists(string path)
    {
        try
        {
            return fileSystem.FileExists(path);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool SafeDirectoryExists(string path)
    {
        try
        {
            return fileSystem.DirectoryExists(path);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private static IReadOnlyList<CredentialEcosystem> GetSupportedUserEcosystems() =>
        [
            CredentialEcosystem.Python,
            CredentialEcosystem.Npm,
            CredentialEcosystem.Pnpm,
            CredentialEcosystem.Yarn,
        ];

    private static IReadOnlyList<CredentialEcosystem> GetSupportedCiTemporaryEcosystems() =>
        [CredentialEcosystem.Npm, CredentialEcosystem.Pnpm, CredentialEcosystem.Yarn];

    private static IReadOnlyList<CredentialEcosystem> GetSupportedUserPackageEcosystems() =>
        [CredentialEcosystem.Npm, CredentialEcosystem.Pnpm, CredentialEcosystem.Yarn];

    private static bool IsPackageEcosystem(CredentialEcosystem ecosystem) =>
        ecosystem
            is CredentialEcosystem.Npm
                or CredentialEcosystem.Pnpm
                or CredentialEcosystem.Yarn;

    private ConfigurationPhase14CleanupEcosystemResult CreateCleanupResult(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        ConfigurationPhase14PlanResult result
    ) =>
        new()
        {
            Ecosystem = ecosystem,
            Scope = scope,
            State =
                result.OwnershipManifestCleanupIncomplete ? CleanupStateIncomplete
                : result.OwnershipManifestPresent ? CleanupStateIncomplete
                : result.AppliedChangeCount > 0 || result.LifecycleStateMutated
                    ? CleanupStateRemoved
                : CleanupStateNotNeeded,
            ChangeCount = result.ChangeCount,
            AppliedChangeCount = result.AppliedChangeCount,
            OwnershipManifestPresent = result.OwnershipManifestPresent,
            TemporaryContainerPresent = TemporaryContainerExists(ecosystem, scope),
        };

    private ConfigurationPhase14CleanupEcosystemResult CreateIncompleteCleanupResult(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    ) =>
        new()
        {
            Ecosystem = ecosystem,
            Scope = scope,
            State = CleanupStateIncomplete,
            ChangeCount = 0,
            AppliedChangeCount = 0,
            OwnershipManifestPresent = SafeFileExists(GetOwnershipManifestPath(ecosystem, scope)),
            TemporaryContainerPresent = TemporaryContainerExists(ecosystem, scope),
        };

    private static ConfigurationPhase14CleanupEcosystemResult CreateSharedPnpmCleanupAliasResult() =>
        new()
        {
            Ecosystem = CredentialEcosystem.Pnpm,
            Scope = ConfigurationPhase14Scope.CiTemporary,
            State = CleanupStateNotNeeded,
            ChangeCount = 0,
            AppliedChangeCount = 0,
            OwnershipManifestPresent = false,
            TemporaryContainerPresent = false,
        };

    private static bool IsExpectedCleanupFailure(Exception exception) =>
        exception
            is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or System.Text.Json.JsonException;

    private static IReadOnlyList<CredentialEcosystem> GetCleanupEcosystems(
        CredentialEcosystem? ecosystem
    )
    {
        if (ecosystem is null)
        {
            return GetSupportedCiTemporaryEcosystems();
        }

        return
            ecosystem.Value
                is CredentialEcosystem.Npm
                    or CredentialEcosystem.Pnpm
                    or CredentialEcosystem.Yarn
            ? [ecosystem.Value]
            : throw new NotSupportedException(
                "Phase 14.3 cleanup supports npm, pnpm, and Yarn CI temporary state."
            );
    }

    private static bool IsExpectedDoctorCheckFailure(Exception exception) =>
        exception
            is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or System.Text.Json.JsonException;

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 3
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/')
        && char.IsAsciiLetter(path[0]);

    private static bool IsWindowsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private ConfigurationChangePlan[] CreateRemovePlans(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        ConfigurationOwnershipManifest manifest,
        string manifestJson
    )
    {
        string ecosystemName = ToContractEcosystemName(ecosystem);
        ConfigurationOwnershipManifestEntry[] entries = manifest
            .Entries.Where(entry => EntryMatchesEcosystem(entry, ecosystem))
            .OrderBy(entry => entry.Sequence)
            .ToArray();

        return entries
            .GroupBy(static entry => entry.TargetKind)
            .Select(group =>
                CreateRemovePlan(
                    ecosystem,
                    ecosystemName,
                    scope,
                    manifest,
                    manifestJson,
                    group.ToArray()
                )
            )
            .ToArray();
    }

    private ConfigurationChangePlan CreateRemovePlan(
        CredentialEcosystem ecosystem,
        string ecosystemName,
        ConfigurationPhase14Scope scope,
        ConfigurationOwnershipManifest manifest,
        string manifestJson,
        IReadOnlyList<ConfigurationOwnershipManifestEntry> entries
    )
    {
        return ConfigurationChangePlanPolicy.Create(
            "phase14-" + ecosystemName + "-unconfigure-plan",
            "phase14-" + ecosystemName + "-unconfigure-changeset",
            ProductId,
            scope == ConfigurationPhase14Scope.CiTemporary
                ? ConfigurationScope.CiTemporary
                : ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = manifest.ManifestId,
                OwnerProductId = manifest.OwnerProductId,
                EntrySelector = manifest.EntrySelector,
                ResourceIdentity = manifest.ResourceIdentity,
                ProductVersion = manifest.ProductVersion,
                PreviousOwnedEntryHash = ComputeSha256Metadata(manifestJson),
                SafeMetadata = manifest.SafeMetadata,
            },
            entries.Select(CreateRemoveChange).ToArray(),
            temporaryContainer: CreateRemoveTemporaryContainer(ecosystem, scope, manifest),
            declarationPreservation: scope == ConfigurationPhase14Scope.CiTemporary
                ? ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible
                : ConfigurationDeclarationPreservation.NotApplicable,
            containsCredentialMaterial: manifest.ContainsCredentialMaterial
        );
    }

    private ConfigurationTemporaryContainer? CreateRemoveTemporaryContainer(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope,
        ConfigurationOwnershipManifest manifest
    )
    {
        if (scope != ConfigurationPhase14Scope.CiTemporary)
        {
            return null;
        }

        CanonicalResourceIdentity resource =
            manifest.ResourceIdentity
            ?? throw new InvalidOperationException(
                "The package ownership manifest does not identify its registry resource."
            );
        CredentialResult credential = CreateDryRunCredential(scope);
        return ecosystem switch
        {
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => CreateNpmCompatiblePlan(
                ecosystem,
                scope,
                credential,
                resource,
                GetSingleNormalizedPath(
                    manifest.Entries.Select(static entry => entry.TargetPathOrName)
                )
            ).TemporaryContainer,
            CredentialEcosystem.Yarn => CreateYarnPlan(
                scope,
                credential,
                resource
            ).TemporaryContainer,
            _ => null,
        };
    }

    private static ConfigurationChange CreateRemoveChange(
        ConfigurationOwnershipManifestEntry entry
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Remove,
            TargetKind = entry.TargetKind,
            TargetPathOrName = entry.TargetPathOrName,
            Key = entry.Key,
            Value = null,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = entry.PreserveDeclarationsAndComments,
            PreviousOwnedEntryMetadata =
                entry.PreviousOwnedEntryMetadata
                ?? entry.PlannedValueSha256
                ?? "previous-secret-owned-entry",
        };

    private static bool EntryMatchesEcosystem(
        ConfigurationOwnershipManifestEntry entry,
        CredentialEcosystem ecosystem
    )
    {
        return ecosystem switch
        {
            CredentialEcosystem.Python => entry.TargetKind
                is ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim,
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm => entry.TargetKind
                == ConfigurationTargetKind.Npmrc,
            CredentialEcosystem.Yarn => entry.TargetKind == ConfigurationTargetKind.Yarnrc,
            _ => false,
        };
    }

    private ConfigurationPhase14PlanResult CreateResult(
        IReadOnlyList<ConfigurationPlanResult> planResults,
        string ownershipManifestPath,
        bool ownershipManifestCleanupIncomplete = false,
        bool? ownershipManifestPresent = null,
        bool lifecycleStateMutated = false
    )
    {
        return new()
        {
            Paths = paths,
            PlanResults = planResults,
            OwnershipManifestPresent =
                ownershipManifestPresent ?? fileSystem.FileExists(ownershipManifestPath),
            OwnershipManifestCleanupIncomplete = ownershipManifestCleanupIncomplete,
            LifecycleStateMutated = lifecycleStateMutated,
        };
    }

    private static ConfigurationPlanResult CreateNoOpPlanResult(
        ConfigurationPlanOperation operation,
        bool applied = true,
        ConfigurationChangePlan? sourcePlan = null
    ) =>
        new()
        {
            Plan = new ConfigurationDryRunPlan
            {
                ContractMajor = ContractVersions.ConfigurationChangePlanMajor,
                PlanId = "phase14-configuration-noop",
                ChangeSetId = "phase14-configuration-noop",
                OwnerProductId = ProductId,
                Scope = sourcePlan?.Scope ?? ConfigurationScope.User,
                AtomicityPolicy = ConfigurationAtomicityPolicy.AtomicChangeSetRequired,
                RollbackPolicy = ConfigurationRollbackPolicy.Required,
                State = ConfigurationPlanState.Planned,
                ManifestCommitPolicy = ConfigurationManifestCommitPolicy.CommitAfterDurableChanges,
                Manifest = sourcePlan?.Manifest ?? CreateNeutralNoOpManifestMetadata(),
                DeclarationPreservation =
                    sourcePlan?.DeclarationPreservation
                    ?? ConfigurationDeclarationPreservation.NotApplicable,
                ContainsCredentialMaterial = false,
                TemporaryContainer = sourcePlan?.TemporaryContainer,
            },
            Operation = operation,
            State = applied ? ConfigurationPlanState.Applied : ConfigurationPlanState.Planned,
        };

    private ConfigurationPlanResult CreatePersistedNoOpPlanResult(
        ConfigurationPlanOperation operation,
        bool applied,
        ConfigurationChangePlan sourcePlan,
        string ownershipManifestPath
    )
    {
        string manifestJson = ReadBoundedText(ownershipManifestPath);
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestJson);
        ConfigurationChangePlan persistedSourcePlan = sourcePlan with
        {
            Manifest = new ConfigurationManifestMetadata
            {
                ManifestId = manifest.ManifestId,
                OwnerProductId = manifest.OwnerProductId,
                EntrySelector = manifest.EntrySelector,
                ResourceIdentity = manifest.ResourceIdentity,
                ProductVersion = manifest.ProductVersion,
                PreviousOwnedEntryHash = manifest.PreviousOwnedEntryHash,
                SafeMetadata = manifest.SafeMetadata,
            },
        };
        return CreateNoOpPlanResult(operation, applied, persistedSourcePlan);
    }

    private static ConfigurationManifestMetadata CreateNeutralNoOpManifestMetadata() =>
        new()
        {
            ManifestId = "phase14-configuration-noop",
            OwnerProductId = ProductId,
            EntrySelector = string.Empty,
            ProductVersion = ProductVersion,
        };

    private ConfigurationManager CreateManager(string ownershipManifestPath) =>
        new(
            fileSystem,
            ownershipManifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

    private ConfigurationChangePlan AttachPreviousOwnershipManifestHashIfPresent(
        ConfigurationChangePlan plan,
        string ownershipManifestPath
    )
    {
        if (!fileSystem.FileExists(ownershipManifestPath))
        {
            return plan;
        }

        string manifestJson = fileSystem.ReadAllText(ownershipManifestPath);
        return plan with
        {
            Manifest = plan.Manifest with
            {
                PreviousOwnedEntryHash = ComputeSha256Metadata(manifestJson),
            },
        };
    }

    private ConfigurationLayoutProjectionContext CreateCurrentLayoutProjectionContext() =>
        new()
        {
            Platform =
                OperatingSystem.IsWindows() ? ConfigurationLayoutPlatform.Windows
                : OperatingSystem.IsMacOS() ? ConfigurationLayoutPlatform.MacOs
                : ConfigurationLayoutPlatform.Linux,
            HomeDirectory = GetHomeDirectory(),
            LocalAppDataDirectory = GetLocalAppDataDirectory(),
            XdgDataHomeDirectory = environmentVariableReader("XDG_DATA_HOME"),
            XdgConfigHomeDirectory = environmentVariableReader("XDG_CONFIG_HOME"),
            FileExists = fileSystem.FileExists,
        };

    private static ConfigurationChange CreatePythonPhysicalTargetChange(
        ConfigurationTargetKind targetKind,
        string targetPath,
        string value
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = targetKind,
            TargetPathOrName = targetPath,
            Key = PhysicalTargetKey,
            Value = value,
            IsSecretValue = false,
            RequiresOwnershipRecord = true,
        };

    private static ConfigurationManifestMetadata CreatePythonManifestMetadata() =>
        new()
        {
            ManifestId = PythonManifestId,
            OwnerProductId = ProductId,
            EntrySelector = "python.keyring",
            ResourceIdentity = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                PythonServiceEndpoint
            ),
            ProductVersion = ProductVersion,
            SafeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ecosystem"] = "python",
            },
        };

    private static string CreatePythonBackendManifestValue(string shimPath) =>
        "azureauth-credprovider python-keyring-backend\n"
        + "phase=14.2\n"
        + "helper="
        + shimPath
        + "\n";

    private static string CreateKeyringShimValue() =>
        OperatingSystem.IsWindows()
            ? "azureauth-credprovider keyring shim phase=14.2\r\n"
            : "#!/usr/bin/env sh\nexec azureauth-credprovider keyring-helper-v2 \"$@\"\n";

    private NpmPhase12RegistryDeclaration CreateNpmDeclaration(CredentialEcosystem ecosystem)
    {
        Uri registryUrl = GetRequiredRegistryUrl(ecosystem);
        if (
            !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                registryUrl,
                out CanonicalResourceIdentity? resource
            )
        )
        {
            throw new InvalidOperationException(
                "The configured package registry URL is not a canonical Azure Artifacts npm URL."
            );
        }

        return CreateNpmDeclaration(ecosystem, resource);
    }

    private NpmPhase12RegistryDeclaration CreateNpmDeclaration(
        CredentialEcosystem ecosystem,
        CanonicalResourceIdentity resource
    )
    {
        Uri registryUrl = resource.ServiceEndpoint;
        return new NpmPhase12RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(
                Path.Combine(paths.StateDirectoryPath, "npm", "explicit-registry-declaration.npmrc")
            ),
            Key = "registry",
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            AuthSelectors = NpmCompatibleAuthSelectorPolicy.Create(resource),
        };
    }

    private YarnPhase13RegistryDeclaration CreateYarnDeclaration()
    {
        Uri registryUrl = GetRequiredRegistryUrl(CredentialEcosystem.Yarn);
        if (
            !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                registryUrl,
                out CanonicalResourceIdentity? resource
            )
        )
        {
            throw new InvalidOperationException(
                "The configured package registry URL is not a canonical Azure Artifacts npm URL."
            );
        }

        return CreateYarnDeclaration(resource);
    }

    private YarnPhase13RegistryDeclaration CreateYarnDeclaration(CanonicalResourceIdentity resource)
    {
        Uri registryUrl = resource.ServiceEndpoint;
        return new YarnPhase13RegistryDeclaration
        {
            SourcePath = fileSystem.GetFullPath(
                Path.Combine(
                    paths.StateDirectoryPath,
                    "yarn",
                    "explicit-registry-declaration.yarnrc.yml"
                )
            ),
            Key = "npmRegistryServer",
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            NpmRegistriesKey = registryUrl.AbsoluteUri,
        };
    }

    private Uri GetRequiredRegistryUrl(CredentialEcosystem ecosystem)
    {
        if (registryUrls.TryGetValue(ecosystem, out Uri? registryUrl))
        {
            return registryUrl;
        }

        throw new InvalidOperationException(
            "Package registry configuration is required. Run azureauth-credprovider configure "
                + $"{GetEcosystemName(ecosystem)} --registry-url "
                + "<azure-artifacts-npm-url>."
        );
    }

    private static Dictionary<CredentialEcosystem, Uri> ValidateRegistryUrls(
        IReadOnlyDictionary<CredentialEcosystem, Uri>? registryUrls
    )
    {
        if (registryUrls is null)
        {
            return new Dictionary<CredentialEcosystem, Uri>();
        }

        var validated = new Dictionary<CredentialEcosystem, Uri>();
        foreach ((CredentialEcosystem ecosystem, Uri registryUrl) in registryUrls)
        {
            if (
                ecosystem
                is not CredentialEcosystem.Npm
                    and not CredentialEcosystem.Pnpm
                    and not CredentialEcosystem.Yarn
            )
            {
                throw new ArgumentException(
                    "Registry declarations are supported only for npm, pnpm, and Yarn.",
                    nameof(registryUrls)
                );
            }

            ArgumentNullException.ThrowIfNull(registryUrl);
            if (
                !registryUrl.IsAbsoluteUri
                || !NpmPhase12VerticalSliceService.TryCreateAzureArtifactsNpmResourceIdentity(
                    registryUrl,
                    out _
                )
            )
            {
                throw new ArgumentException(
                    "Registry declarations must use canonical Azure Artifacts npm registry URLs.",
                    nameof(registryUrls)
                );
            }

            validated.Add(ecosystem, registryUrl);
        }

        return validated;
    }

    private static string GetEcosystemName(CredentialEcosystem ecosystem) =>
        ecosystem switch
        {
            CredentialEcosystem.Npm => "npm",
            CredentialEcosystem.Pnpm => "pnpm",
            CredentialEcosystem.Yarn => "yarn",
            _ => throw new ArgumentException(
                "A package registry declaration was requested for an unsupported ecosystem.",
                nameof(ecosystem)
            ),
        };

    private static ConfigurationPhase14ResolvedPaths ResolvePaths(
        ConfigurationPhase14VerticalSliceOptions options,
        IFileSystem fileSystem,
        string? jobScopeId,
        Func<string, string?> environmentVariableReader
    )
    {
        string stateDirectoryPath = fileSystem.GetFullPath(
            options.StateDirectoryPath ?? GetDefaultStateDirectoryPath()
        );
        string ciTemporaryProductRootPath = fileSystem.GetFullPath(
            options.CiTemporaryProductRootPath
                ?? (
                    options.StateDirectoryPath is null
                        ? GetDefaultCiTemporaryProductRootPath()
                        : Path.Combine(stateDirectoryPath, "ci-jobs")
                )
        );
        string ciTemporaryRootPath = fileSystem.GetFullPath(
            jobScopeId is null
                ? ciTemporaryProductRootPath
                : Path.Combine(ciTemporaryProductRootPath, jobScopeId)
        );
        string npmUserConfigPath = ResolveNpmUserConfigPath(fileSystem, environmentVariableReader);
        return new ConfigurationPhase14ResolvedPaths
        {
            StateDirectoryPath = stateDirectoryPath,
            ManifestDirectoryPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "manifests")
            ),
            CiTemporaryRootPath = ciTemporaryRootPath,
            CiTemporaryManifestDirectoryPath = fileSystem.GetFullPath(
                Path.Combine(ciTemporaryRootPath, "manifests")
            ),
            OwnershipManifestPath = fileSystem.GetFullPath(
                Path.Combine(stateDirectoryPath, "manifests", "python-user-ownership-manifest.json")
            ),
            NpmUserNpmrcPath = npmUserConfigPath,
            PnpmUserNpmrcPath = npmUserConfigPath,
            NpmCiTemporaryNpmrcPath = fileSystem.GetFullPath(
                Path.Combine(ciTemporaryRootPath, "npm", "userconfig.npmrc")
            ),
            PnpmCiTemporaryNpmrcPath = fileSystem.GetFullPath(
                Path.Combine(ciTemporaryRootPath, "npm", "userconfig.npmrc")
            ),
            YarnUserYarnrcPath = ResolveYarnUserConfigPath(fileSystem, environmentVariableReader),
            YarnCiTemporaryHomePath = fileSystem.GetFullPath(
                Path.Combine(ciTemporaryRootPath, "yarn", "home")
            ),
        };
    }

    private static string GetDefaultStateDirectoryPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "." + ProductId, "phase14.2");
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.Combine(localApplicationData, ProductId, "phase14.2");
        }

        return Path.Combine(Path.GetTempPath(), ProductId, "phase14.2");
    }

    private static string GetDefaultCiTemporaryProductRootPath() =>
        Path.Combine(Path.GetTempPath(), ProductId, "phase14.2", "ci-jobs");

    private static string GetHomeDirectory()
    {
        string profileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            return profileDirectory;
        }

        string environmentHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        return string.IsNullOrWhiteSpace(environmentHome) ? Path.GetTempPath() : environmentHome;
    }

    private static string? GetLocalAppDataDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        return string.IsNullOrWhiteSpace(localApplicationData) ? null : localApplicationData;
    }

    private static string ResolveNpmUserConfigPath(
        IFileSystem fileSystem,
        Func<string, string?> environmentVariableReader
    )
    {
        string? uppercase = NullIfWhiteSpace(
            environmentVariableReader(NpmUserConfigEnvironmentVariable)
        );
        string? lowercase = NullIfWhiteSpace(
            environmentVariableReader(LowercaseNpmUserConfigEnvironmentVariable)
        );
        string? uppercasePath = uppercase is null ? null : fileSystem.GetFullPath(uppercase);
        string? lowercasePath = lowercase is null ? null : fileSystem.GetFullPath(lowercase);
        if (
            uppercasePath is not null
            && lowercasePath is not null
            && !string.Equals(
                uppercasePath,
                lowercasePath,
                UsesWindowsPathSemantics(uppercasePath) || UsesWindowsPathSemantics(lowercasePath)
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "NPM_CONFIG_USERCONFIG and npm_config_userconfig resolve to different user "
                    + "configuration paths."
            );
        }

        return uppercasePath
            ?? lowercasePath
            ?? fileSystem.GetFullPath(
                Path.Combine(
                    ResolveUserHomeDirectory(
                        environmentVariableReader,
                        UsesWindowsPathSemantics(fileSystem.GetFullPath("."))
                    ),
                    ".npmrc"
                )
            );
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string ResolveUserHomeDirectory(
        Func<string, string?> environmentVariableReader,
        bool useWindowsConventions
    )
    {
        string? home = useWindowsConventions
            ? environmentVariableReader("USERPROFILE")
            : environmentVariableReader("HOME");
        if (useWindowsConventions && string.IsNullOrWhiteSpace(home))
        {
            home = environmentVariableReader("HOME");
        }

        return string.IsNullOrWhiteSpace(home) ? GetHomeDirectory() : home;
    }

    private static string ResolveYarnUserConfigPath(
        IFileSystem fileSystem,
        Func<string, string?> environmentVariableReader
    )
    {
        string home = ResolveUserHomeDirectory(
            environmentVariableReader,
            UsesWindowsPathSemantics(fileSystem.GetFullPath("."))
        );
        string? configuredFilename = environmentVariableReader(YarnRcFilenameEnvironmentVariable);
        string filename = IsValidConfigurationFilename(configuredFilename)
            ? configuredFilename!
            : ".yarnrc.yml";
        return fileSystem.GetFullPath(Path.Combine(home, filename));
    }

    private static bool IsValidConfigurationFilename(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && !Path.IsPathRooted(value)
        && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private string GetOwnershipManifestPath(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14Scope scope
    )
    {
        string fileName = (ecosystem, scope) switch
        {
            (CredentialEcosystem.Python, ConfigurationPhase14Scope.User) =>
                "python-user-ownership-manifest.json",
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.User) =>
                "npm-compatible-user-ownership-manifest.json",
            (CredentialEcosystem.Npm, ConfigurationPhase14Scope.CiTemporary) =>
                "npm-compatible-ci-temporary-ownership-manifest.json",
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.User) =>
                "npm-compatible-user-ownership-manifest.json",
            (CredentialEcosystem.Pnpm, ConfigurationPhase14Scope.CiTemporary) =>
                "npm-compatible-ci-temporary-ownership-manifest.json",
            (CredentialEcosystem.Yarn, ConfigurationPhase14Scope.User) =>
                "yarn-user-ownership-manifest.json",
            (CredentialEcosystem.Yarn, ConfigurationPhase14Scope.CiTemporary) =>
                "yarn-ci-temporary-ownership-manifest.json",
            _ => throw new NotSupportedException(
                "Phase 14.2 configuration orchestration supports Python user scope and "
                    + "npm, pnpm, and Yarn user or CI temporary scopes."
            ),
        };

        string manifestDirectory =
            scope == ConfigurationPhase14Scope.CiTemporary
                ? paths.CiTemporaryManifestDirectoryPath
                : paths.ManifestDirectoryPath;
        return fileSystem.GetFullPath(Path.Combine(manifestDirectory, fileName));
    }

    private void EnsureCiJobScope(ConfigurationPhase14Scope scope)
    {
        if (scope != ConfigurationPhase14Scope.CiTemporary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawJobScopeId))
        {
            throw new InvalidOperationException(
                "Azure Pipelines CI temporary configuration requires SYSTEM_JOBID."
            );
        }

        if (jobScopeId is null)
        {
            throw new InvalidOperationException(
                "Azure Pipelines SYSTEM_JOBID is invalid for temporary configuration."
            );
        }
    }

    private static bool IsValidJobScopeId(string? value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumJobScopeIdLength
            || value is "." or ".."
        )
        {
            return false;
        }

        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
        );
    }

    private bool TryLoadOwnershipManifest(
        string ownershipManifestPath,
        [NotNullWhen(true)] out ConfigurationOwnershipManifest? manifest,
        [NotNullWhen(true)] out string? manifestJson
    )
    {
        manifest = null;
        manifestJson = null;
        if (!fileSystem.FileExists(ownershipManifestPath))
        {
            return false;
        }

        manifestJson = ReadBoundedText(ownershipManifestPath);
        manifest = ConfigurationOwnershipManifestSerializer.Deserialize(manifestJson);
        return true;
    }

    private string ReadBoundedText(string path)
    {
        const int maximumCharacters = 1024 * 1024;
        if (fileSystem is SystemFileSystem)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maximumCharacters)
            {
                throw new InvalidOperationException("Configuration file is too large.");
            }

            using StreamReader reader = new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true
            );
            char[] buffer = new char[maximumCharacters + 1];
            int count = reader.ReadBlock(buffer, 0, buffer.Length);
            if (count > maximumCharacters)
            {
                throw new InvalidOperationException("Configuration file is too large.");
            }

            return new string(buffer, 0, count);
        }

        string contents = fileSystem.ReadAllText(path);
        return contents.Length <= maximumCharacters
            ? contents
            : throw new InvalidOperationException("Configuration file is too large.");
    }

    private static string ToContractEcosystemName(CredentialEcosystem ecosystem) =>
        ecosystem switch
        {
            CredentialEcosystem.Python => "python",
            CredentialEcosystem.Npm => "npm",
            CredentialEcosystem.Pnpm => "pnpm",
            CredentialEcosystem.Yarn => "yarn",
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null),
        };

    private static string ComputeSha256Metadata(string value) => "sha256:" + ComputeSha256(value);

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetSingleNormalizedPath(IEnumerable<string> values) =>
        GetSingleNormalizedPathOrDefault(values)
        ?? throw new InvalidOperationException("A single physical target path is required.");

    private string? GetSingleNormalizedPathOrDefault(IEnumerable<string> values)
    {
        string[] paths = values.Select(NormalizePath).ToArray();
        return paths.Distinct(GetPathComparerForPaths(paths)).SingleOrDefault();
    }

    private string NormalizePath(string path) => fileSystem.GetFullPath(path);

    private bool PathEquals(string left, string right) =>
        string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            UsesWindowsPathSemantics(left) || UsesWindowsPathSemantics(right)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private static StringComparer GetPathComparerForPaths(IEnumerable<string>? paths = null) =>
        OperatingSystem.IsWindows() || (paths?.Any(UsesWindowsPathSemantics) ?? false)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static bool UsesWindowsPathSemantics(string path) =>
        OperatingSystem.IsWindows() || IsWindowsDrivePath(path) || IsWindowsUncPath(path);
}

public enum ConfigurationPhase14Scope
{
    User,
    CiTemporary,
}
