using System.Security.Cryptography;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record GitPhase8VerticalSliceOptions
{
    public string? StateDirectoryPath { get; init; }

    internal IFileSystem? FileSystem { get; init; }

    public string? UserHomeDirectoryPath { get; init; }

    public string? XdgConfigHomeDirectoryPath { get; init; }

    public string? GitConfigurationProbeWorkingDirectoryPath { get; init; }

    public IProcessRunner? ProcessRunner { get; init; }

    public string? GitExecutablePath { get; init; }

    public string? ProductExecutablePath { get; init; }

    public bool? LocalShellGitDiscoverySupported { get; init; }

    public BoundedCredentialAcquisitionAdapter? CredentialAcquisition { get; init; }

    internal Action? AfterOwnedGitActivationRemoved { get; init; }
}

public sealed record GitPhase8VerticalSliceResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

    public required string UserHomeDirectoryPath { get; init; }

    public required string XdgConfigHomeDirectoryPath { get; init; }

    public required string UserGitConfigPath { get; init; }

    public required string GitConfigPath { get; init; }

    public required string OwnershipManifestPath { get; init; }

    public required string GitHelperDirectoryPath { get; init; }

    public required string GitHelperPath { get; init; }
}

public sealed record GitPhase8ConfigureDryRunResult
{
    public required GitPhase8VerticalSliceResolvedPaths Paths { get; init; }

    public required ConfigurationPlanValidationResult Validation { get; init; }

    public required ConfigurationPlanResult PlanResult { get; init; }
}

public sealed record GitPhase8ConfigureResult
{
    public required GitPhase8VerticalSliceResolvedPaths Paths { get; init; }

    public required ConfigurationPlanResult PlanResult { get; init; }

    public required bool OwnedGitEntriesPresent { get; init; }

    public required bool OwnershipManifestPresent { get; init; }
}

public enum GitEffectiveCredentialHelperState
{
    NotConfigured = 0,
    Active = 1,
    Reset = 2,
    Bypassed = 3,
    Shadowed = 4,
    DiscoveryDeferred = 5,
    DiscoveryFailed = 6,
}

public enum GitEffectiveCredentialHelperEntryKind
{
    Product = 1,
    Other = 2,
}

public enum GitConfigurationScope
{
    Unknown = 0,
    System = 1,
    Global = 2,
    Local = 3,
    Worktree = 4,
    Command = 5,
}

public enum GitCredentialHelperSelectorKind
{
    Unknown = 0,
    Generic = 1,
    UrlSpecific = 2,
}

public enum GitCredentialHelperConflictDirective
{
    Reset = 1,
    OtherHelper = 2,
    ActivationBypassed = 3,
}

public sealed record GitCredentialHelperConflictDescriptor
{
    public required GitConfigurationScope Scope { get; init; }

    public required GitCredentialHelperSelectorKind Selector { get; init; }

    public required GitCredentialHelperConflictDirective Directive { get; init; }
}

public sealed record GitEffectiveCredentialHelperDiagnostics
{
    public required GitEffectiveCredentialHelperState State { get; init; }

    public required IReadOnlyList<GitEffectiveCredentialHelperEntryKind> EffectiveOrder
    {
        get;
        init;
    }

    public required bool ConfigurationTruncated { get; init; }

    public required GitCredentialHelperConflictDescriptor? Conflict { get; init; }
}

public enum GitUseHttpPathInspectionState
{
    Present = 1,
    Absent = 2,
    InspectionIncomplete = 3,
}

public sealed record GitUseHttpPathDiagnostics
{
    public required GitUseHttpPathInspectionState State { get; init; }

    public required bool OutputTruncated { get; init; }
}

public sealed record GitPhase8DoctorResult
{
    public required GitPhase8VerticalSliceResolvedPaths Paths { get; init; }

    public required bool ConfigurationPlanValid { get; init; }

    public required bool OwnedGitEntriesPresent { get; init; }

    public required bool OwnershipManifestPresent { get; init; }

    public required bool CredentialCoreSuccess { get; init; }

    public required bool GitCredentialHelperGetSuccess { get; init; }

    public required bool GitCredentialHelperStoreSuccess { get; init; }

    public required bool GitCredentialHelperEraseSuccess { get; init; }

    public required bool LocalShellHelperShorthandSuccess { get; init; }

    public required bool LocalShellHelperShorthandDeferred { get; init; }

    public required GitEffectiveCredentialHelperDiagnostics EffectiveCredentialHelper
    {
        get;
        init;
    }

    public required GitUseHttpPathDiagnostics DevAzureUseHttpPath { get; init; }

    public required bool ProtocolPayloadCaptured { get; init; }
}

public sealed record GitPhase8UnconfigureResult
{
    public required GitPhase8VerticalSliceResolvedPaths Paths { get; init; }

    public required bool HadOwnedConfiguration { get; init; }

    public ConfigurationPlanResult? PlanResult { get; init; }

    public required bool OwnedGitEntriesPresent { get; init; }

    public required bool OwnershipManifestPresent { get; init; }
}

public sealed class GitPhase8UnrecognizedStateException : InvalidOperationException
{
    public GitPhase8UnrecognizedStateException(string message)
        : base(message) { }

    public GitPhase8UnrecognizedStateException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class GitPhase8VerticalSliceService
{
    private const string ProductId = "azureauth-credprovider";
    private const string ProductVersion = "phase8";
    private const string ManifestId = "phase8-git-configuration";
    private const string ConfigurePlanId = "phase8-git-configure-plan";
    private const string EntrySelector = "git.config";
    private const int GitDiscoveryMaximumHelperEntries = 128;
    private const int GitDiscoveryOutputByteLimit = 64 * 1024;
    private const string GitUseHttpPathProbeUsernamePresent =
        "azureauth-use-http-path-present";
    private const string GitUseHttpPathProbeUsernameAbsent =
        "azureauth-use-http-path-absent";
    private const string GitUseHttpPathProbeHelper =
        "!f() { p=absent; while IFS= read -r line; do case \"$line\" in path=*) "
        + "p=present;; esac; done; echo username=azureauth-use-http-path-$p; "
        + "echo password=azureauth-use-http-path-probe; }; f";
    private const string GitUseHttpPathProbeInput =
        "protocol=https\n"
        + "host=dev.azure.com\n"
        + "path=org/project/_git/repository\n"
        + "\n";
    private static readonly TimeSpan GitDiscoveryTimeout = TimeSpan.FromSeconds(10);
    internal const string GitCredentialHelperKey = "credential.helper";
    internal const string GitUseHttpPathKey = "credential.https://dev.azure.com.useHttpPath";
    internal const string GitUseHttpPathValue = "true";
    private const string GitCredentialHelperProtocolInput =
        "protocol=https\n" + "host=dev.azure.com\n" + "path=org/project/_git/repository\n" + "\n";
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly Uri GitServiceEndpoint = new("https://dev.azure.com/org");
    private static readonly Uri GitConfigurationProbeUrl =
        new("https://dev.azure.com/org/project/_git/repository");
    private readonly IFileSystem fileSystem;
    private readonly GitUserGlobalConfigActivation gitActivation;
    private readonly string gitConfigurationProbeWorkingDirectoryPath;
    private readonly string gitExecutablePath;
    private readonly bool localShellGitDiscoverySupported;
    private readonly GitPhase8VerticalSliceResolvedPaths paths;
    private readonly IProcessRunner processRunner;
    private readonly ProductExecutableInvocation? productExecutableInvocation;
    private readonly Lazy<BoundedCredentialAcquisitionAdapter>? credentialAcquisition;
    private readonly Action? afterOwnedGitActivationRemoved;

    public GitPhase8VerticalSliceService(GitPhase8VerticalSliceOptions? options = null)
        : this(options, configurationOnly: false) { }

    private GitPhase8VerticalSliceService(
        GitPhase8VerticalSliceOptions? options,
        bool configurationOnly
    )
    {
        fileSystem = options?.FileSystem ?? new SystemFileSystem();
        gitActivation = new GitUserGlobalConfigActivation(fileSystem);
        paths = ResolvePaths(options);
        gitConfigurationProbeWorkingDirectoryPath = GetFullPath(
            options?.GitConfigurationProbeWorkingDirectoryPath
                ?? Environment.CurrentDirectory
        );
        processRunner = options?.ProcessRunner ?? new SystemProcessRunner();
        gitExecutablePath = string.IsNullOrWhiteSpace(options?.GitExecutablePath)
            ? "git"
            : options.GitExecutablePath;
        productExecutableInvocation = ResolveProductExecutableInvocation(
            options?.ProductExecutablePath
        );
        afterOwnedGitActivationRemoved = options?.AfterOwnedGitActivationRemoved;
        localShellGitDiscoverySupported =
            options?.LocalShellGitDiscoverySupported ?? true;
        if (!configurationOnly)
        {
            credentialAcquisition = new Lazy<BoundedCredentialAcquisitionAdapter>(
                () =>
                    options?.CredentialAcquisition
                    ?? new BoundedCredentialAcquisitionAdapter(
                        CredentialProviderCompositionRoot.CreateProduction().AcquisitionService
                    ),
                LazyThreadSafetyMode.ExecutionAndPublication
            );
        }
    }

    public GitPhase8VerticalSliceResolvedPaths Paths => paths;

    public static GitPhase8VerticalSliceService CreateConfigurationOnly(
        GitPhase8VerticalSliceOptions? options = null
    ) => new(options, configurationOnly: true);

    public async ValueTask<GitPhase8ConfigureDryRunResult> DryRunConfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ThrowIfUnrecognizedOwnershipManifestExists();
        ConfigurationChangePlan plan = CreateConfigurePlan();
        ConfigurationManager manager = CreateManager();
        ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
        ConfigurationPlanResult planResult;
        try
        {
            _ = gitActivation.Inspect(paths.UserGitConfigPath, paths.GitConfigPath);
            planResult = await manager.DryRunAsync(plan, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }

        return new GitPhase8ConfigureDryRunResult
        {
            Paths = paths,
            Validation = validation,
            PlanResult = planResult,
        };
    }

    public async ValueTask<GitPhase8ConfigureResult> ConfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable lifecycleLock = AcquireLifecycleLock();

        ThrowIfUnrecognizedOwnershipManifestExists();
        ConfigurationPlanResult planResult;
        try
        {
            ConfigurationChangePlan configurePlan = CreateConfigurePlan();
            GitUserGlobalConfigActivationState activationState = gitActivation.Inspect(
                paths.UserGitConfigPath,
                paths.GitConfigPath
            );
            ConfigurationManager manager = CreateManager();
            bool privateStateCurrent = manager.IsAppliedStateCurrent(
                configurePlan,
                cancellationToken
            );
            if (
                activationState == GitUserGlobalConfigActivationState.Present
                && !privateStateCurrent
            )
            {
                throw new InvalidOperationException(
                    "The product-owned Git include block does not reference recognized state."
                );
            }

            EnsureConfigurationStateDirectories();
            EnsureStateGitHelperShim();
            if (privateStateCurrent)
            {
                planResult = (
                    await manager.DryRunAsync(configurePlan, cancellationToken)
                ) with
                {
                    Operation = ConfigurationPlanOperation.Apply,
                };
            }
            else
            {
                planResult = await manager.ApplyAsync(configurePlan, cancellationToken);
            }

            EnsureUserGitConfigDirectory();
            gitActivation.EnsurePresent(paths.UserGitConfigPath, paths.GitConfigPath);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
        GitPhase8OwnedState ownedState = await InspectOwnedStateAsync(cancellationToken);

        return new GitPhase8ConfigureResult
        {
            Paths = paths,
            PlanResult = planResult,
            OwnedGitEntriesPresent = ownedState.OwnedGitEntriesPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
        };
    }

    public async ValueTask<GitPhase8DoctorResult> DoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        GitPhase8OwnedState ownedState = await InspectOwnedStateAsync(cancellationToken);
        bool configurationPlanValid = false;
        try
        {
            _ = gitActivation.Inspect(paths.UserGitConfigPath, paths.GitConfigPath);
            ConfigurationChangePlan plan = CreateConfigurePlan();
            ConfigurationManager manager = CreateManager();
            ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
            ConfigurationPlanResult planResult = await manager.DryRunAsync(plan, cancellationToken);
            configurationPlanValid =
                validation.IsValid && planResult.Operation == ConfigurationPlanOperation.DryRun;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            configurationPlanValid = false;
        }

        var credentialCoreSuccess = false;
        try
        {
            CredentialResult credentialResult = GetCredentialAcquisition()
                .Acquire(CreateGitRequest(), cancellationToken);
            credentialCoreSuccess = credentialResult.Status == CredentialResultStatus.Success;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            credentialCoreSuccess = false;
        }

        var gitCredentialHelperGetSuccess = false;
        var gitCredentialHelperStoreSuccess = false;
        var gitCredentialHelperEraseSuccess = false;
        var localShellHelperShorthandSuccess = false;
        var localShellHelperShorthandDeferred = false;
        var protocolPayloadCaptured = false;
        GitUseHttpPathDiagnostics devAzureUseHttpPath =
            CreateUseHttpPathDiagnostics(
                GitUseHttpPathInspectionState.InspectionIncomplete
            );
        GitEffectiveCredentialHelperDiagnostics effectiveCredentialHelper =
            CreateEffectiveCredentialHelperDiagnostics(
                GitEffectiveCredentialHelperState.NotConfigured
            );
        try
        {
            (gitCredentialHelperGetSuccess, protocolPayloadCaptured) =
                ExecuteGitCredentialHelperAdapterPath(
                    ProductId,
                    ["git", "credential-helper", "get"],
                    CredentialOperation.Get,
                    cancellationToken
                );
            gitCredentialHelperStoreSuccess = ExecuteGitCredentialHelperAdapterPath(
                ProductId,
                ["git", "credential-helper", "store"],
                CredentialOperation.Store,
                cancellationToken
            ).Success;
            gitCredentialHelperEraseSuccess = ExecuteGitCredentialHelperAdapterPath(
                ProductId,
                ["git", "credential-helper", "erase"],
                CredentialOperation.Erase,
                cancellationToken
            ).Success;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            gitCredentialHelperGetSuccess = false;
            gitCredentialHelperStoreSuccess = false;
            gitCredentialHelperEraseSuccess = false;
            protocolPayloadCaptured = false;
        }

        bool ownedGitActivationPresent = TryInspectOwnedGitActivation();
        bool productGitConfigurationPresent =
            ownedGitActivationPresent
            || ownedState.OwnedGitEntriesPresent
            || ownedState.OwnershipManifestPresent;
        bool productHelperExecutable = CanExecuteStateGitHelperShim();
        try
        {
            GitEffectiveConfigurationInspection inspection =
                await InspectEffectiveGitConfigurationAsync(
                    productGitConfigurationPresent,
                    cancellationToken
                );
            devAzureUseHttpPath = inspection.UseHttpPath;
            effectiveCredentialHelper = inspection.CredentialHelper;
            if (
                configurationPlanValid
                && ownedState.OwnedGitEntriesPresent
                && ownedState.OwnershipManifestPresent
            )
            {
                localShellHelperShorthandSuccess =
                    productHelperExecutable
                    && inspection.CredentialHelper.State
                        == GitEffectiveCredentialHelperState.Active;
                localShellHelperShorthandDeferred =
                    inspection.CredentialHelper.State
                    == GitEffectiveCredentialHelperState.DiscoveryDeferred;
            }
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            localShellHelperShorthandSuccess = false;
            localShellHelperShorthandDeferred = false;
            effectiveCredentialHelper = CreateEffectiveCredentialHelperDiagnostics(
                GitEffectiveCredentialHelperState.DiscoveryFailed
            );
            devAzureUseHttpPath = CreateUseHttpPathDiagnostics(
                GitUseHttpPathInspectionState.InspectionIncomplete
            );
        }

        return new GitPhase8DoctorResult
        {
            Paths = paths,
            ConfigurationPlanValid = configurationPlanValid,
            OwnedGitEntriesPresent = ownedState.OwnedGitEntriesPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
            CredentialCoreSuccess = credentialCoreSuccess,
            GitCredentialHelperGetSuccess = gitCredentialHelperGetSuccess,
            GitCredentialHelperStoreSuccess = gitCredentialHelperStoreSuccess,
            GitCredentialHelperEraseSuccess = gitCredentialHelperEraseSuccess,
            LocalShellHelperShorthandSuccess = localShellHelperShorthandSuccess,
            LocalShellHelperShorthandDeferred = localShellHelperShorthandDeferred,
            EffectiveCredentialHelper = effectiveCredentialHelper,
            DevAzureUseHttpPath = devAzureUseHttpPath,
            ProtocolPayloadCaptured = protocolPayloadCaptured,
        };
    }

    public async ValueTask<GitPhase8UnconfigureResult> UnconfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable lifecycleLock = AcquireLifecycleLock();

        if (!TryLoadExpectedOwnershipManifest(out ConfigurationOwnershipManifest? manifest))
        {
            ThrowIfUnrecognizedOwnershipManifestExists();
            ThrowIfActivationExistsWithoutManifest();
            GitPhase8OwnedState absentState = await InspectOwnedStateAsync(cancellationToken);
            return new GitPhase8UnconfigureResult
            {
                Paths = paths,
                HadOwnedConfiguration = false,
                PlanResult = null,
                OwnedGitEntriesPresent = absentState.OwnedGitEntriesPresent,
                OwnershipManifestPresent = absentState.OwnershipManifestPresent,
            };
        }

        ConfigurationPlanResult planResult;
        try
        {
            ConfigurationManager manager = CreateManager();
            ThrowIfAppliedGitConfigurationIsNotCurrent(
                manager,
                manifest,
                cancellationToken
            );
            GitUserGlobalConfigActivationState activationState = gitActivation.Inspect(
                paths.UserGitConfigPath,
                paths.GitConfigPath
            );
            if (activationState == GitUserGlobalConfigActivationState.Present)
            {
                gitActivation.Remove(paths.UserGitConfigPath, paths.GitConfigPath);
                afterOwnedGitActivationRemoved?.Invoke();
            }
            planResult = await manager.RemoveAsync(
                CreateUnconfigurePlan(manifest),
                cancellationToken
            );
            TryDeleteStateGitHelperShim();
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
        GitPhase8OwnedState ownedState = await InspectOwnedStateAsync(cancellationToken);

        return new GitPhase8UnconfigureResult
        {
            Paths = paths,
            HadOwnedConfiguration = true,
            PlanResult = planResult,
            OwnedGitEntriesPresent = ownedState.OwnedGitEntriesPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
        };
    }

    public async ValueTask ValidateUnconfigureDryRunAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryLoadExpectedOwnershipManifest(out ConfigurationOwnershipManifest? manifest))
        {
            ThrowIfUnrecognizedOwnershipManifestExists();
            ThrowIfActivationExistsWithoutManifest();
            return;
        }

        try
        {
            ConfigurationManager manager = CreateManager();
            ThrowIfAppliedGitConfigurationIsNotCurrent(
                manager,
                manifest,
                cancellationToken
            );
            _ = gitActivation.Inspect(paths.UserGitConfigPath, paths.GitConfigPath);
            await manager.DryRunAsync(CreateUnconfigurePlan(manifest), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
    }

    private ConfigurationManager CreateManager() =>
        new(fileSystem, paths.OwnershipManifestPath, CreatePhysicalTargetWriterDispatcher());

    private IDisposable AcquireLifecycleLock()
    {
        if (fileSystem is not IFileSystemMutationLock mutationLock)
        {
            throw new InvalidOperationException(
                "The Phase 8 Git lifecycle requires cross-process mutation locking."
            );
        }

        return mutationLock.AcquireMutationLock(
            Path.Combine(paths.StateDirectoryPath, ".locks", "git-phase8-lifecycle")
        );
    }

    private ConfigurationPhysicalTargetWriterDispatcher CreatePhysicalTargetWriterDispatcher() =>
        new(fileSystem);

    private ConfigurationChangePlan CreateConfigurePlan()
    {
        return ConfigurationChangePlanPolicy.Create(
            ConfigurePlanId,
            ProductId,
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = ManifestId,
                OwnerProductId = ProductId,
                EntrySelector = EntrySelector,
                ProductVersion = ProductVersion,
            },
            [
                CreateGitConfigChange(
                    ConfigurationChangeOperation.Set,
                    GitCredentialHelperKey,
                    CreateGitCredentialHelperValue()
                ),
                CreateGitConfigChange(
                    ConfigurationChangeOperation.Set,
                    GitUseHttpPathKey,
                    GitUseHttpPathValue
                ),
            ]
        );
    }

    private ConfigurationChangePlan CreateUnconfigurePlan(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        ConfigurationChange[] changes = manifest
            .Entries.Where(IsManagedManifestEntry)
            .OrderBy(entry => entry.Sequence)
            .Select(CreateGitConfigRemoveChange)
            .ToArray();
        if (changes.Length == 0)
        {
            throw new InvalidOperationException(
                "Owned Git configuration manifest does not contain removable Phase 8 entries."
            );
        }

        return ConfigurationChangePlanPolicy.Create(
            "phase8-git-unconfigure-plan",
            ProductId,
            manifest.Scope,
            new ConfigurationManifestMetadata
            {
                ManifestId = manifest.ManifestId,
                OwnerProductId = manifest.OwnerProductId,
                EntrySelector = manifest.EntrySelector,
                ResourceIdentity = manifest.ResourceIdentity,
                ProductVersion = manifest.ProductVersion,
                SafeMetadata = manifest.SafeMetadata,
            },
            changes
        );
    }

    private ConfigurationChange CreateGitConfigChange(
        ConfigurationChangeOperation operation,
        string key,
        string? value
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.GitConfig,
            TargetPathOrName = paths.GitConfigPath,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
        };

    private string CreateGitCredentialHelperValue()
    {
        return CreateGitCredentialHelperPathValue(paths.GitHelperPath);
    }

    private static string CreateGitCredentialHelperPathValue(string path)
    {
        return OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
    }

    private ProductExecutableInvocation GetRequiredProductExecutableInvocation()
    {
        if (
            productExecutableInvocation is null
            || !File.Exists(productExecutableInvocation.ExecutablePath)
            || (
                IsManagedAssemblyInvocation(productExecutableInvocation.ExecutablePath)
                && productExecutableInvocation.DotnetExecutablePath is null
            )
            || (
                productExecutableInvocation.DotnetExecutablePath is not null
                && !File.Exists(productExecutableInvocation.DotnetExecutablePath)
            )
        )
        {
            throw new InvalidOperationException(
                "The Git credential helper executable could not be resolved."
            );
        }

        return productExecutableInvocation;
    }

    private ConfigurationChange CreateGitConfigRemoveChange(
        ConfigurationOwnershipManifestEntry entry
    )
    {
        ArgumentNullException.ThrowIfNull(entry);

        return CreateGitConfigChange(ConfigurationChangeOperation.Remove, entry.Key, value: null);
    }

    private void ThrowIfUnrecognizedOwnershipManifestExists()
    {
        if (OwnershipManifestPathExists() && !TryLoadExpectedOwnershipManifest(out _))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git ownership manifest is not recognized."
            );
        }
    }

    private bool OwnershipManifestPathExists()
    {
        try
        {
            return fileSystem.FileExists(paths.OwnershipManifestPath)
                || fileSystem.DirectoryExists(paths.OwnershipManifestPath);
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return true;
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        string[] rawLines = text.Split('\n');
        int lineCount = text.EndsWith('\n') ? rawLines.Length - 1 : rawLines.Length;
        for (var index = 0; index < lineCount; index++)
        {
            yield return rawLines[index];
        }
    }

    private static bool TryParseSimpleGitConfigSection(
        string trimmedStart,
        out bool isCredentialSection
    )
    {
        isCredentialSection = false;
        if (!TryParseSimpleGitConfigSectionText(trimmedStart, out string sectionText))
        {
            return false;
        }

        if (
            string.Equals(sectionText, "credential", StringComparison.OrdinalIgnoreCase)
            || IsDevAzureComCredentialSection(sectionText)
        )
        {
            isCredentialSection = true;
        }

        return true;
    }

    private static bool TryParseSimpleGitConfigSectionText(
        string trimmedStart,
        out string sectionText
    )
    {
        sectionText = string.Empty;
        if (trimmedStart.Length == 0 || trimmedStart[0] != '[')
        {
            return false;
        }

        int closingIndex = trimmedStart.IndexOf(']');
        if (closingIndex < 0)
        {
            return false;
        }

        sectionText = trimmedStart[1..closingIndex].Trim();
        return true;
    }

    private static bool IsDevAzureComCredentialSection(string sectionText)
    {
        const string CredentialPrefix = "credential";
        if (
            !sectionText.StartsWith(CredentialPrefix, StringComparison.OrdinalIgnoreCase)
            || sectionText.Length == CredentialPrefix.Length
            || !char.IsWhiteSpace(sectionText[CredentialPrefix.Length])
        )
        {
            return false;
        }

        return TryParseDevAzureComCredentialSubsection(sectionText, out _);
    }

    private static bool TryParseDevAzureComCredentialSubsection(
        string sectionText,
        out string subsection
    )
    {
        subsection = string.Empty;
        const string CredentialPrefix = "credential";
        if (
            !sectionText.StartsWith(CredentialPrefix, StringComparison.OrdinalIgnoreCase)
            || sectionText.Length == CredentialPrefix.Length
            || !char.IsWhiteSpace(sectionText[CredentialPrefix.Length])
        )
        {
            return false;
        }

        string subsectionText = sectionText[CredentialPrefix.Length..].TrimStart();
        return TryParseSimpleQuotedGitConfigValue(subsectionText, out subsection)
            && IsDevAzureComCredentialSubsection(subsection);
    }

    private static bool IsRootDevAzureComCredentialSubsection(string subsection)
    {
        if (string.Equals(subsection, "https://dev.azure.com", StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryCreateDevAzureComUri(subsection, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.IdnHost, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && uri.AbsolutePath is "" or "/";
    }

    private static bool IsDevAzureComCredentialSubsection(string subsection) =>
        string.Equals(subsection, "https://dev.azure.com", StringComparison.Ordinal)
        || TryCreateDevAzureComUri(subsection, out _);

    private static bool TryCreateDevAzureComUri(
        string subsection,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri
    )
    {
        uri = null;
        if (
            !Uri.TryCreate(subsection, UriKind.Absolute, out Uri? parsedUri)
            || !string.Equals(
                parsedUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase
            )
            || !IsDevAzureComHostOrEffectiveAlias(parsedUri.IdnHost)
            || !IsDevAzureComHostOrEffectiveAlias(parsedUri.Host)
        )
        {
            return false;
        }

        uri = parsedUri;
        return true;
    }

    private static bool IsDevAzureComHostOrEffectiveAlias(string host)
    {
        if (string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string trimmedHost = host.TrimEnd('.');
        return trimmedHost.Length != host.Length
            && string.Equals(trimmedHost, "dev.azure.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSimpleGitConfigAssignment(
        string trimmedStart,
        out string variableName,
        out string value
    )
    {
        variableName = string.Empty;
        value = string.Empty;
        if (trimmedStart.Length == 0 || trimmedStart[0] is '#' or ';' or '[')
        {
            return false;
        }

        int equalsIndex = trimmedStart.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return false;
        }

        variableName = trimmedStart[..equalsIndex].Trim();
        string valueText = trimmedStart[(equalsIndex + 1)..].Trim();
        if (valueText.Length > 0 && valueText[0] == '"')
        {
            return TryParseSimpleQuotedGitConfigValue(valueText, out value);
        }

        value = TrimSimpleUnquotedGitConfigValue(valueText);
        return true;
    }

    private static string TrimSimpleUnquotedGitConfigValue(string valueText)
    {
        for (var index = 0; index < valueText.Length; index++)
        {
            if (valueText[index] is '#' or ';')
            {
                return valueText[..index].TrimEnd();
            }
        }

        return valueText;
    }

    private static bool TryParseSimpleQuotedGitConfigValue(string valueText, out string value)
    {
        value = string.Empty;
        var builder = new System.Text.StringBuilder(valueText.Length);
        var escaping = false;
        for (var index = 1; index < valueText.Length; index++)
        {
            char character = valueText[index];
            if (escaping)
            {
                if (character is not ('\\' or '"'))
                {
                    return false;
                }

                builder.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\')
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                string trailingText = valueText[(index + 1)..].TrimStart();
                if (trailingText.Length > 0 && trailingText[0] is not ('#' or ';'))
                {
                    return false;
                }

                value = builder.ToString();
                return true;
            }

            builder.Append(character);
        }

        return false;
    }

    private static CredentialRequestV2 CreateGitRequest() =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create("dev.azure.com", "org", GitServiceEndpoint),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = InteractivePolicy.Never,
            AcquisitionMode = AcquisitionMode.SilentOnly,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };

    private BoundedCredentialAcquisitionAdapter GetCredentialAcquisition() =>
        credentialAcquisition?.Value
        ?? throw new InvalidOperationException(
            "Credential acquisition is unavailable in a configuration-only service."
        );

    private (bool Success, bool PayloadCaptured) ExecuteGitCredentialHelperAdapterPath(
        string executablePath,
        string[] arguments,
        CredentialOperation expectedOperation,
        CancellationToken cancellationToken
    )
    {
        var protocolStdout = new StringWriter();
        AdapterHostExecutionOutcome outcome = new GitCredentialHelperAdapter(
            GetCredentialAcquisition(),
            static name =>
                string.Equals(name, "GIT_TERMINAL_PROMPT", StringComparison.Ordinal) ? "0" : null
        ).Execute(
            executablePath,
            arguments,
            new StringReader(GitCredentialHelperProtocolInput),
            protocolStdout,
            TextWriter.Null,
            new DiagnosticRouter([], SecretRedactor.Empty),
            cancellationToken
        );

        string capturedPayload = protocolStdout.ToString();
        bool expectsPayload = expectedOperation == CredentialOperation.Get;
        bool payloadCaptured = capturedPayload.Length != 0;
        bool success =
            outcome.Result.ExitCode == AdapterHostExitCode.Success
            && outcome.Result.WriteProtocolStdout == expectsPayload
            && payloadCaptured == expectsPayload
            && !capturedPayload.Contains('\r');

        return (success, payloadCaptured);
    }

    private async ValueTask<GitEffectiveConfigurationInspection>
        InspectEffectiveGitConfigurationAsync(
            bool productGitConfigurationPresent,
            CancellationToken cancellationToken
        )
    {
        if (!localShellGitDiscoverySupported)
        {
            return new GitEffectiveConfigurationInspection(
                CreateEffectiveCredentialHelperDiagnostics(
                    GitEffectiveCredentialHelperState.DiscoveryDeferred
                ),
                CreateUseHttpPathDiagnostics(
                    GitUseHttpPathInspectionState.InspectionIncomplete
                )
            );
        }

        ProcessResult helperResult = await processRunner
            .RunAsync(CreateEffectiveHelperInspectionStartSpec(), cancellationToken)
            .ConfigureAwait(false);
        GitEffectiveCredentialHelperDiagnostics credentialHelper =
            await InspectEffectiveCredentialHelpersAsync(
                    helperResult,
                    productGitConfigurationPresent,
                    cancellationToken
                )
                .ConfigureAwait(false);
        ProcessResult useHttpPathResult = await processRunner
            .RunAsync(CreateEffectiveUseHttpPathInspectionStartSpec(), cancellationToken)
            .ConfigureAwait(false);

        GitUseHttpPathDiagnostics useHttpPath =
            InspectEffectiveUseHttpPath(useHttpPathResult);
        return new GitEffectiveConfigurationInspection(
            credentialHelper,
            useHttpPath
        );
    }

    private void EnsureConfigurationStateDirectories()
    {
        EnsureStateDirectory(GetRequiredDirectoryName(paths.GitConfigPath));
        EnsureStateDirectory(GetRequiredDirectoryName(paths.OwnershipManifestPath));
    }

    private void EnsureUserGitConfigDirectory()
    {
        string directory = GetRequiredDirectoryName(paths.UserGitConfigPath);
        Directory.CreateDirectory(directory);
    }

    private void EnsureStateGitHelperShim()
    {
        ProductExecutableInvocation invocation = GetRequiredProductExecutableInvocation();
        ThrowIfInvalidProductExecutableInvocation(invocation);
        ThrowIfInvalidStateGitHelperPath();
        EnsureStateDirectory(paths.GitHelperDirectoryPath);
        string expectedContent = CreateLocalShellProductShimContent(invocation);
        if (
            !fileSystem.FileExists(paths.GitHelperPath)
            || !string.Equals(
                fileSystem.ReadAllText(paths.GitHelperPath),
                expectedContent,
                StringComparison.Ordinal
            )
        )
        {
            WriteLocalShellProductShim(paths.GitHelperPath, expectedContent);
        }
        if (!OperatingSystem.IsWindows())
        {
            fileSystem.SetUnixFileMode(
                paths.GitHelperPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        ThrowIfMissingRegularFile(paths.GitHelperPath);
    }

    private void ThrowIfInvalidStateGitHelperPath()
    {
        ThrowIfPathIsOutsideStateDirectory(paths.GitHelperPath);

        if (fileSystem.DirectoryExists(paths.GitHelperPath))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized."
            );
        }
    }

    private static string GetRequiredDirectoryName(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
    }

    private void EnsureStateDirectory(string directory)
    {
        ThrowIfPathIsOutsideStateDirectory(directory);
        foreach (string stateDirectory in EnumerateStateDirectoryChain(directory))
        {
            if (!fileSystem.DirectoryExists(stateDirectory))
            {
                CreateOwnerOnlyDirectory(stateDirectory);
            }
        }
    }

    private static void CreateOwnerOnlyDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, OwnerOnlyDirectoryMode);
    }

    private void TryDeleteStateGitHelperShim()
    {
        try
        {
            if (File.Exists(paths.GitHelperPath))
            {
                File.Delete(paths.GitHelperPath);
            }

            if (
                Directory.Exists(paths.GitHelperDirectoryPath)
                && Directory.GetFileSystemEntries(paths.GitHelperDirectoryPath).Length == 0
            )
            {
                Directory.Delete(paths.GitHelperDirectoryPath);
            }
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception)) { }
    }

    private void WriteLocalShellProductShim(string helperPath, string content)
    {
        fileSystem.AtomicWriteAllText(
            helperPath,
            content,
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
        );
    }

    private static string CreateLocalShellProductShimContent(ProductExecutableInvocation invocation)
    {
        string command = invocation.DotnetExecutablePath is null
            ? QuotePosixShellArgument(invocation.ExecutablePath)
            : string.Concat(
                QuotePosixShellArgument(invocation.DotnetExecutablePath),
                " ",
                QuotePosixShellArgument(invocation.ExecutablePath)
            );
        return string.Concat("#!/bin/sh\n", "exec ", command, " git credential-helper \"$@\"\n");
    }

    private static string QuotePosixShellArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat("'", value.Replace("'", "'\"'\"'", StringComparison.Ordinal), "'");
    }

    private async ValueTask<GitEffectiveCredentialHelperDiagnostics>
        InspectEffectiveCredentialHelpersAsync(
        ProcessResult result,
        bool productGitConfigurationPresent,
        CancellationToken cancellationToken
    )
    {
        if (!IsNormalGitConfigQueryResult(result))
        {
            return new GitEffectiveCredentialHelperDiagnostics
            {
                State = GitEffectiveCredentialHelperState.DiscoveryFailed,
                EffectiveOrder = Array.Empty<GitEffectiveCredentialHelperEntryKind>(),
                ConfigurationTruncated =
                    result.Status == ProcessExecutionStatus.OutputTooLarge,
                Conflict = null,
            };
        }

        if (!TryParseCredentialHelperRecords(result.StandardOutput, out var records))
        {
            return CreateEffectiveCredentialHelperDiagnostics(
                GitEffectiveCredentialHelperState.DiscoveryFailed
            );
        }

        if (records.Count > GitDiscoveryMaximumHelperEntries)
        {
            return new GitEffectiveCredentialHelperDiagnostics
            {
                State = GitEffectiveCredentialHelperState.DiscoveryFailed,
                EffectiveOrder = Array.Empty<GitEffectiveCredentialHelperEntryKind>(),
                ConfigurationTruncated = true,
                Conflict = null,
            };
        }

        IReadOnlySet<string> applicableUrlPatterns;
        string[] urlPatterns = records
            .Where(record => record.UrlPattern is not null)
            .Select(record => record.UrlPattern!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (urlPatterns.Length == 0)
        {
            applicableUrlPatterns = new HashSet<string>(StringComparer.Ordinal);
        }
        else
        {
            if (
                !TryCreateCredentialUrlApplicabilityStartSpec(
                    urlPatterns,
                    out ProcessStartSpec? applicabilityStartSpec
                )
            )
            {
                return CreateEffectiveCredentialHelperDiagnostics(
                    GitEffectiveCredentialHelperState.DiscoveryFailed
                );
            }
            ProcessResult applicabilityResult = await processRunner
                .RunAsync(applicabilityStartSpec, cancellationToken)
                .ConfigureAwait(false);
            if (
                !TryInspectApplicableCredentialUrlPatterns(
                    applicabilityResult,
                    urlPatterns,
                    out applicableUrlPatterns
                )
            )
            {
                return new GitEffectiveCredentialHelperDiagnostics
                {
                    State = GitEffectiveCredentialHelperState.DiscoveryFailed,
                    EffectiveOrder =
                        Array.Empty<GitEffectiveCredentialHelperEntryKind>(),
                    ConfigurationTruncated =
                        applicabilityResult.Status
                        == ProcessExecutionStatus.OutputTooLarge,
                    Conflict = null,
                };
            }
        }

        string expected = GitConfigPhysicalTargetWriter.RenderCredentialHelperCommandValue(
            CreateGitCredentialHelperValue()
        );
        var effectiveHelpers = new List<GitEffectiveCredentialHelperEntry>();
        var expectedHelperObserved = false;
        GitCredentialHelperConflictDescriptor? lastReset = null;
        foreach (GitCredentialHelperRecord record in records)
        {
            if (
                record.UrlPattern is not null
                && !applicableUrlPatterns.Contains(record.UrlPattern)
            )
            {
                continue;
            }

            if (record.Value.Length == 0)
            {
                effectiveHelpers.Clear();
                lastReset = CreateConflictDescriptor(
                    record,
                    GitCredentialHelperConflictDirective.Reset
                );
            }
            else
            {
                bool isExpected = string.Equals(
                    record.Value,
                    expected,
                    StringComparison.Ordinal
                );
                expectedHelperObserved |= isExpected;
                effectiveHelpers.Add(
                    new GitEffectiveCredentialHelperEntry(
                        isExpected
                            ? GitEffectiveCredentialHelperEntryKind.Product
                            : GitEffectiveCredentialHelperEntryKind.Other,
                        record
                    )
                );
            }
        }

        GitEffectiveCredentialHelperState state =
            effectiveHelpers.Any(entry =>
                entry.Kind == GitEffectiveCredentialHelperEntryKind.Product
            )
                ? GitEffectiveCredentialHelperState.Active
            : expectedHelperObserved
                ? effectiveHelpers.Count == 0
                    ? GitEffectiveCredentialHelperState.Reset
                    : GitEffectiveCredentialHelperState.Shadowed
            : productGitConfigurationPresent
                ? GitEffectiveCredentialHelperState.Bypassed
                : GitEffectiveCredentialHelperState.NotConfigured;
        GitCredentialHelperConflictDescriptor? conflict = state switch
        {
            GitEffectiveCredentialHelperState.Reset => lastReset,
            GitEffectiveCredentialHelperState.Shadowed => lastReset,
            GitEffectiveCredentialHelperState.Bypassed =>
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Unknown,
                    Selector = GitCredentialHelperSelectorKind.Unknown,
                    Directive = GitCredentialHelperConflictDirective.ActivationBypassed,
                },
            _ => null,
        };
        return new GitEffectiveCredentialHelperDiagnostics
        {
            State = state,
            EffectiveOrder = effectiveHelpers.Select(entry => entry.Kind).ToArray(),
            ConfigurationTruncated = false,
            Conflict = conflict,
        };
    }

    private ProcessStartSpec CreateEffectiveHelperInspectionStartSpec() =>
        new(
            gitExecutablePath,
            [
                "config",
                "--includes",
                "--show-scope",
                "--null",
                "--get-regexp",
                @"^credential(\..*)?\.helper$",
            ],
            workingDirectory: gitConfigurationProbeWorkingDirectoryPath,
            environment: CreateGitInspectionEnvironment(),
            timeout: GitDiscoveryTimeout,
            outputCaptureOptions: new ProcessOutputCaptureOptions
            {
                StandardOutputByteLimit = GitDiscoveryOutputByteLimit,
                StandardErrorByteLimit = GitDiscoveryOutputByteLimit,
            }
        );

    private bool TryCreateCredentialUrlApplicabilityStartSpec(
        string[] urlPatterns,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProcessStartSpec? startSpec
    )
    {
        startSpec = null;
        if (!TryGetInheritedGitConfigCount(out int inheritedCount))
        {
            return false;
        }

        var environment = CreateGitInspectionEnvironment();
        for (var index = 0; index < urlPatterns.Length; index++)
        {
            int configIndex = inheritedCount + index;
            environment["GIT_CONFIG_KEY_" + configIndex] =
                "credential."
                    + urlPatterns[index]
                    + "."
                    + CreateCredentialUrlProbeVariable(urlPatterns[index], index);
            environment["GIT_CONFIG_VALUE_" + configIndex] = "true";
        }
        environment["GIT_CONFIG_COUNT"] = (inheritedCount + urlPatterns.Length).ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
        startSpec = new ProcessStartSpec(
            gitExecutablePath,
            [
                "config",
                "--includes",
                "--null",
                "--get-urlmatch",
                "credential",
                GitConfigurationProbeUrl.AbsoluteUri,
            ],
            workingDirectory: gitConfigurationProbeWorkingDirectoryPath,
            environment: environment,
            timeout: GitDiscoveryTimeout,
            outputCaptureOptions: CreateGitDiscoveryOutputCaptureOptions()
        );
        return true;
    }

    private static bool TryGetInheritedGitConfigCount(out int count)
    {
        string? value = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
        if (value is null)
        {
            count = 0;
            return true;
        }

        return int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out count
            )
            && count >= 0
            && count <= GitDiscoveryMaximumHelperEntries;
    }

    private ProcessStartSpec CreateEffectiveUseHttpPathInspectionStartSpec() =>
        new(
            gitExecutablePath,
            [
                "-c",
                "credential.helper=",
                "-c",
                "credential.helper=" + GitUseHttpPathProbeHelper,
                "credential",
                "fill",
            ],
            workingDirectory: gitConfigurationProbeWorkingDirectoryPath,
            environment: CreateGitInspectionEnvironment(),
            standardInput: GitUseHttpPathProbeInput,
            timeout: GitDiscoveryTimeout,
            outputCaptureOptions: CreateGitDiscoveryOutputCaptureOptions()
        );

    private static ProcessOutputCaptureOptions CreateGitDiscoveryOutputCaptureOptions() =>
        new()
        {
            StandardOutputByteLimit = GitDiscoveryOutputByteLimit,
            StandardErrorByteLimit = GitDiscoveryOutputByteLimit,
        };

    private static GitEffectiveCredentialHelperDiagnostics
        CreateEffectiveCredentialHelperDiagnostics(
            GitEffectiveCredentialHelperState state
        ) =>
        new()
        {
            State = state,
            EffectiveOrder = Array.Empty<GitEffectiveCredentialHelperEntryKind>(),
            ConfigurationTruncated = false,
            Conflict = null,
        };

    private static GitUseHttpPathDiagnostics CreateUseHttpPathDiagnostics(
        GitUseHttpPathInspectionState state,
        bool outputTruncated = false
    ) =>
        new()
        {
            State = state,
            OutputTruncated = outputTruncated,
        };

    private Dictionary<string, string?> CreateGitInspectionEnvironment()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = paths.UserHomeDirectoryPath,
            ["XDG_CONFIG_HOME"] = paths.XdgConfigHomeDirectoryPath,
        };
    }

    private static bool IsNormalGitConfigQueryResult(ProcessResult result)
    {
        if (result.StandardError.Length != 0 || !result.HasExitCode)
        {
            return false;
        }

        return result.Status switch
        {
            ProcessExecutionStatus.Success => result.ExitCode == 0,
            ProcessExecutionStatus.NonZeroExit =>
                result.ExitCode == 1 && result.StandardOutput.Length == 0,
            _ => false,
        };
    }

    private static bool TryParseCredentialHelperRecords(
        string output,
        out IReadOnlyList<GitCredentialHelperRecord> records
    )
    {
        records = Array.Empty<GitCredentialHelperRecord>();
        if (output.Length == 0)
        {
            return true;
        }

        string[] parts = output.Split('\0', StringSplitOptions.None);
        if (parts[^1].Length != 0 || (parts.Length - 1) % 2 != 0)
        {
            return false;
        }

        var parsed = new List<GitCredentialHelperRecord>((parts.Length - 1) / 2);
        for (var index = 0; index < parts.Length - 1; index += 2)
        {
            if (!TryParseGitConfigurationScope(parts[index], out GitConfigurationScope scope))
            {
                return false;
            }

            string record = parts[index + 1];
            int separator = record.IndexOf('\n');
            if (separator < 0)
            {
                return false;
            }

            string key = record[..separator];
            string value = record[(separator + 1)..];
            if (!TryParseCredentialHelperKey(key, out string? urlPattern))
            {
                return false;
            }

            parsed.Add(
                new GitCredentialHelperRecord(
                    scope,
                    urlPattern is null
                        ? GitCredentialHelperSelectorKind.Generic
                        : GitCredentialHelperSelectorKind.UrlSpecific,
                    urlPattern,
                    value
                )
            );
        }

        records = parsed;
        return true;
    }

    private static bool TryParseGitConfigurationScope(
        string token,
        out GitConfigurationScope scope
    )
    {
        scope = token switch
        {
            "system" => GitConfigurationScope.System,
            "global" => GitConfigurationScope.Global,
            "local" => GitConfigurationScope.Local,
            "worktree" => GitConfigurationScope.Worktree,
            "command" => GitConfigurationScope.Command,
            _ => GitConfigurationScope.Unknown,
        };
        return token.Length is > 0 and <= 32
            && token.All(character =>
                character is >= 'a' and <= 'z' || character == '-'
            );
    }

    private static bool TryParseCredentialHelperKey(
        string key,
        out string? urlPattern
    )
    {
        const string Prefix = "credential.";
        const string Suffix = ".helper";
        urlPattern = null;
        if (string.Equals(key, GitCredentialHelperKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (
            !key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !key.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
            || key.Length <= Prefix.Length + Suffix.Length
        )
        {
            return false;
        }

        urlPattern = key[Prefix.Length..^Suffix.Length];
        return urlPattern.Length <= GitDiscoveryOutputByteLimit
            && !urlPattern.Contains('\0')
            && !urlPattern.Contains('\n')
            && !urlPattern.Contains('\r');
    }

    private static string CreateCredentialUrlProbeVariable(string pattern, int index)
    {
        byte[] digest = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(index.ToString() + "\0" + pattern)
        );
        return "azureauthdiscovery" + Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static bool TryInspectApplicableCredentialUrlPatterns(
        ProcessResult result,
        string[] urlPatterns,
        out IReadOnlySet<string> applicablePatterns
    )
    {
        applicablePatterns = new HashSet<string>(StringComparer.Ordinal);
        if (!IsNormalGitConfigQueryResult(result))
        {
            return false;
        }

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < urlPatterns.Length; index++)
        {
            variables.Add(
                "credential." + CreateCredentialUrlProbeVariable(urlPatterns[index], index),
                urlPatterns[index]
            );
        }

        if (result.StandardOutput.Length == 0)
        {
            return true;
        }

        string[] records = result.StandardOutput.Split('\0', StringSplitOptions.None);
        if (records[^1].Length != 0)
        {
            return false;
        }

        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (string record in records[..^1])
        {
            int separator = record.IndexOf('\n');
            if (separator < 0)
            {
                return false;
            }

            string key = record[..separator];
            string value = record[(separator + 1)..];
            if (!variables.TryGetValue(key, out string? pattern))
            {
                continue;
            }
            if (
                !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || !matched.Add(pattern)
            )
            {
                return false;
            }
        }

        applicablePatterns = matched;
        return true;
    }

    private static GitCredentialHelperConflictDescriptor CreateConflictDescriptor(
        GitCredentialHelperRecord record,
        GitCredentialHelperConflictDirective directive
    ) =>
        new()
        {
            Scope = record.Scope,
            Selector = record.Selector,
            Directive = directive,
        };

    private static GitUseHttpPathDiagnostics InspectEffectiveUseHttpPath(
        ProcessResult result
    )
    {
        if (
            result.Status != ProcessExecutionStatus.Success
            || !result.HasExitCode
            || result.ExitCode != 0
            || result.StandardError.Length != 0
        )
        {
            return CreateUseHttpPathDiagnostics(
                GitUseHttpPathInspectionState.InspectionIncomplete,
                outputTruncated:
                    result.Status == ProcessExecutionStatus.OutputTooLarge
            );
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in SplitLines(result.StandardOutput))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (
                separator <= 0
                || !fields.TryAdd(line[..separator], line[(separator + 1)..])
            )
            {
                return CreateUseHttpPathDiagnostics(
                    GitUseHttpPathInspectionState.InspectionIncomplete
                );
            }
        }

        string? username = null;
        bool valid =
            fields.TryGetValue("protocol", out string? protocol)
            && string.Equals(protocol, "https", StringComparison.Ordinal)
            && fields.TryGetValue("host", out string? host)
            && string.Equals(host, "dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && fields.TryGetValue("username", out username)
            && fields.TryGetValue("password", out string? password)
            && password.Length != 0
            && (
                string.Equals(
                    username,
                    GitUseHttpPathProbeUsernamePresent,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    username,
                    GitUseHttpPathProbeUsernameAbsent,
                    StringComparison.Ordinal
                )
            );
        if (!valid)
        {
            return CreateUseHttpPathDiagnostics(
                GitUseHttpPathInspectionState.InspectionIncomplete
            );
        }

        return CreateUseHttpPathDiagnostics(
            string.Equals(
                username,
                GitUseHttpPathProbeUsernamePresent,
                StringComparison.Ordinal
            )
                ? GitUseHttpPathInspectionState.Present
                : GitUseHttpPathInspectionState.Absent
        );
    }

    private static bool ContainsDevAzureUseHttpPathState(string gitConfig)
    {
        var inDevAzureCredentialSection = false;
        foreach (string rawLine in SplitLines(gitConfig))
        {
            string line = rawLine.Length > 0 && rawLine[^1] == '\r' ? rawLine[..^1] : rawLine;
            string trimmedStart = line.TrimStart();
            if (TryParseSimpleGitConfigSection(trimmedStart, out _))
            {
                inDevAzureCredentialSection =
                    TryParseSimpleGitConfigSectionText(trimmedStart, out string sectionText)
                    && TryParseDevAzureComCredentialSubsection(sectionText, out string subsection)
                    && IsRootDevAzureComCredentialSubsection(subsection);
                continue;
            }

            if (
                inDevAzureCredentialSection
                && TryParseSimpleGitConfigAssignment(
                    trimmedStart,
                    out string variableName,
                    out string value
                )
                && string.Equals(variableName, "useHttpPath", StringComparison.OrdinalIgnoreCase)
                && string.Equals(value, GitUseHttpPathValue, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool TryLoadExpectedOwnershipManifest(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out ConfigurationOwnershipManifest? manifest
    )
    {
        manifest = null;
        try
        {
            if (!fileSystem.FileExists(paths.OwnershipManifestPath))
            {
                return false;
            }

            if (!CanReadRegularFile(paths.OwnershipManifestPath))
            {
                return false;
            }

            manifest = ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(paths.OwnershipManifestPath)
            );
            if (
                !HasExpectedManifestMetadata(manifest)
                || !HasExpectedManagedManifestEntries(manifest)
            )
            {
                manifest = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            manifest = null;
            return false;
        }
    }

    private async ValueTask<GitPhase8OwnedState> InspectOwnedStateAsync(
        CancellationToken cancellationToken
    )
    {
        var ownershipManifestPresent = false;
        try
        {
            ownershipManifestPresent = OwnershipManifestPathExists();
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            ownershipManifestPresent = true;
        }

        if (!ownershipManifestPresent)
        {
            _ = TryInspectOwnedGitActivation();
            return new GitPhase8OwnedState(
                OwnedGitEntriesPresent: false,
                OwnershipManifestPresent: false
            );
        }

        var ownedGitEntriesPresent = false;
        try
        {
            if (TryLoadExpectedOwnershipManifest(out ConfigurationOwnershipManifest? manifest))
            {
                ownedGitEntriesPresent =
                    await CanDryRunOwnedGitRemovalAsync(manifest, cancellationToken)
                    && TryInspectOwnedGitActivation();
            }
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            ownedGitEntriesPresent = false;
        }

        return new GitPhase8OwnedState(
            OwnedGitEntriesPresent: ownedGitEntriesPresent,
            OwnershipManifestPresent: ownershipManifestPresent
        );
    }

    private async ValueTask<bool> CanDryRunOwnedGitRemovalAsync(
        ConfigurationOwnershipManifest manifest,
        CancellationToken cancellationToken
    )
    {
        ConfigurationPlanResult planResult = await CreateManager()
            .DryRunAsync(CreateUnconfigurePlan(manifest), cancellationToken);
        return planResult.Operation == ConfigurationPlanOperation.DryRun;
    }

    private static bool HasExpectedManifestMetadata(ConfigurationOwnershipManifest manifest) =>
        string.Equals(manifest.ManifestId, ManifestId, StringComparison.Ordinal)
        && string.Equals(manifest.OwnerProductId, ProductId, StringComparison.Ordinal)
        && manifest.Scope == ConfigurationScope.User
        && string.Equals(manifest.EntrySelector, EntrySelector, StringComparison.Ordinal)
        && string.Equals(manifest.ProductVersion, ProductVersion, StringComparison.Ordinal)
        && manifest.ResourceIdentity is null
        && manifest.SafeMetadata.Count == 0;

    private bool HasExpectedManagedManifestEntries(ConfigurationOwnershipManifest manifest) =>
        manifest.Entries.Count == 2
        && HasExpectedManagedManifestEntry(manifest, sequence: 1, GitCredentialHelperKey)
        && HasExpectedManagedManifestEntry(manifest, sequence: 2, GitUseHttpPathKey);

    private bool HasExpectedManagedManifestEntry(
        ConfigurationOwnershipManifest manifest,
        int sequence,
        string expectedKey
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKey);

        ConfigurationOwnershipManifestEntry[] matchingEntries = manifest
            .Entries.Where(entry => MatchesManagedManifestEntry(entry, expectedKey))
            .ToArray();
        return matchingEntries.Length == 1 && matchingEntries[0].Sequence == sequence;
    }

    private bool MatchesManagedManifestEntry(
        ConfigurationOwnershipManifestEntry entry,
        string expectedKey
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKey);

        return entry.TargetKind == ConfigurationTargetKind.GitConfig
            && string.Equals(entry.Key, expectedKey, StringComparison.Ordinal)
            && string.Equals(
                NormalizeComparablePath(entry.TargetPathOrName),
                NormalizeComparablePath(paths.GitConfigPath),
                GetPathComparison()
            );
    }

    private bool IsManagedManifestEntry(ConfigurationOwnershipManifestEntry entry)
    {
        return MatchesManagedManifestEntry(entry, GitCredentialHelperKey)
            || MatchesManagedManifestEntry(entry, GitUseHttpPathKey);
    }

    private bool CanReadRegularFile(string path)
    {
        try
        {
            ThrowIfPathIsOutsideStateDirectory(path);
            ThrowIfMissingRegularFile(path);
            return true;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool CanExecuteStateGitHelperShim()
    {
        try
        {
            ThrowIfPathIsOutsideStateDirectory(paths.GitHelperPath);
            ThrowIfMissingRegularFile(paths.GitHelperPath);
            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            UnixFileMode mode = fileSystem.GetUnixFileMode(paths.GitHelperPath);
            return (
                    mode
                    & (
                        UnixFileMode.UserExecute
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherExecute
                    )
                ) != 0;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private void ThrowIfAppliedGitConfigurationIsNotCurrent(
        ConfigurationManager manager,
        ConfigurationOwnershipManifest manifest,
        CancellationToken cancellationToken
    )
    {
        if (manager.IsAppliedStateCurrent(CreateConfigurePlan(), cancellationToken))
        {
            return;
        }

        GitUserGlobalConfigActivationState activationState = gitActivation.Inspect(
            paths.UserGitConfigPath,
            paths.GitConfigPath
        );
        if (
            activationState == GitUserGlobalConfigActivationState.Absent
            && manager.IsAppliedStateCurrent(CreateUnconfigurePlan(manifest), cancellationToken)
        )
        {
            return;
        }

        throw new GitPhase8UnrecognizedStateException(
            "The Phase 8 Git configuration state is not recognized."
        );
    }

    private bool TryInspectOwnedGitActivation()
    {
        try
        {
            return gitActivation.Inspect(paths.UserGitConfigPath, paths.GitConfigPath)
                == GitUserGlobalConfigActivationState.Present;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private void ThrowIfActivationExistsWithoutManifest()
    {
        try
        {
            if (
                gitActivation.Inspect(paths.UserGitConfigPath, paths.GitConfigPath)
                == GitUserGlobalConfigActivationState.Present
            )
            {
                throw new GitPhase8UnrecognizedStateException(
                    "The Phase 8 Git configuration state is not recognized."
                );
            }
        }
        catch (GitPhase8UnrecognizedStateException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
    }

    private void ThrowIfInvalidProductExecutableInvocation(ProductExecutableInvocation invocation)
    {
        ThrowIfProductExecutableCannotHandleSharedCliEntrypoint(invocation);
        ThrowIfMissingRegularFile(invocation.ExecutablePath);
        if (invocation.DotnetExecutablePath is not null)
        {
            ThrowIfMissingRegularFile(invocation.DotnetExecutablePath);
        }
    }

    private static void ThrowIfProductExecutableCannotHandleSharedCliEntrypoint(
        ProductExecutableInvocation invocation
    )
    {
        if (
            !string.Equals(
                Path.GetFileNameWithoutExtension(invocation.ExecutablePath),
                GitCredentialHelperAdapter.ProductExecutableName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized."
            );
        }
    }

    private void ThrowIfMissingRegularFile(string path)
    {
        if (
            !fileSystem.IsPathFullyQualified(path)
            || !fileSystem.FileExists(path)
            || fileSystem.DirectoryExists(path)
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized."
            );
        }

        try
        {
            _ = fileSystem.GetFileLength(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
    }

    private void ThrowIfPathIsOutsideStateDirectory(string path)
    {
        string relativePath = Path.GetRelativePath(paths.StateDirectoryPath, path);
        if (
            Path.IsPathRooted(relativePath)
            || string.Equals(relativePath, "..", GetPathComparison())
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, GetPathComparison())
            || (
                Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar
                && relativePath.StartsWith(
                    ".." + Path.AltDirectorySeparatorChar,
                    GetPathComparison()
                )
            )
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized."
            );
        }
    }

    private IEnumerable<string> EnumerateStateDirectoryChain(string directory)
    {
        ThrowIfPathIsOutsideStateDirectory(directory);
        yield return paths.StateDirectoryPath;

        string relativePath = Path.GetRelativePath(paths.StateDirectoryPath, directory);
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            yield break;
        }

        string current = paths.StateDirectoryPath;
        foreach (
            string component in relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            if (component is "." or "..")
            {
                throw new GitPhase8UnrecognizedStateException(
                    "The Phase 8 Git configuration state is not recognized."
                );
            }

            current = Path.Combine(current, component);
            yield return current;
        }
    }

    private static GitPhase8VerticalSliceResolvedPaths ResolvePaths(
        GitPhase8VerticalSliceOptions? options
    )
    {
        options ??= new GitPhase8VerticalSliceOptions();

        string stateDirectoryPath = GetFullPath(
            options.StateDirectoryPath ?? GetDefaultStateDirectoryPath()
        );
        string userHomeDirectoryPath = GetFullPath(
            options.UserHomeDirectoryPath
                ?? (
                    options.StateDirectoryPath is null
                        ? GetDefaultUserHomeDirectoryPath()
                        : Path.Combine(stateDirectoryPath, "isolated-home")
                )
        );
        string xdgConfigHomeDirectoryPath = GetFullPath(
            options.XdgConfigHomeDirectoryPath
                ?? (
                    options.UserHomeDirectoryPath is null
                        && options.StateDirectoryPath is null
                        && !string.IsNullOrWhiteSpace(
                            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                        )
                            ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")!
                            : Path.Combine(userHomeDirectoryPath, ".config")
                )
        );
        string homeGitConfigPath = GetFullPath(
            Path.Combine(userHomeDirectoryPath, ".gitconfig")
        );
        string xdgGitConfigPath = GetFullPath(
            Path.Combine(xdgConfigHomeDirectoryPath, "git", "config")
        );
        string userGitConfigPath =
            File.Exists(homeGitConfigPath) || !File.Exists(xdgGitConfigPath)
                ? homeGitConfigPath
                : xdgGitConfigPath;
        string gitConfigPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "git", "user.gitconfig")
        );
        string ownershipManifestPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "manifests", "git-ownership-manifest.json")
        );
        string gitHelperDirectoryPath = GetFullPath(Path.Combine(stateDirectoryPath, "git-helper"));
        string gitHelperPath = GetFullPath(
            Path.Combine(gitHelperDirectoryPath, GitCredentialHelperAdapter.HelperExecutableName)
        );

        if (
            string.Equals(gitConfigPath, ownershipManifestPath, GetPathComparison())
            || string.Equals(gitConfigPath, userGitConfigPath, GetPathComparison())
            || string.Equals(ownershipManifestPath, userGitConfigPath, GetPathComparison())
        )
        {
            throw new ArgumentException(
                "The user, product, and ownership Git configuration paths must be different.",
                nameof(options)
            );
        }

        return new GitPhase8VerticalSliceResolvedPaths
        {
            StateDirectoryPath = stateDirectoryPath,
            UserHomeDirectoryPath = userHomeDirectoryPath,
            XdgConfigHomeDirectoryPath = xdgConfigHomeDirectoryPath,
            UserGitConfigPath = userGitConfigPath,
            GitConfigPath = gitConfigPath,
            OwnershipManifestPath = ownershipManifestPath,
            GitHelperDirectoryPath = gitHelperDirectoryPath,
            GitHelperPath = gitHelperPath,
        };
    }

    private static string GetDefaultStateDirectoryPath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "." + ProductId, "phase8");
        }

        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.Combine(localApplicationData, ProductId, "phase8");
        }

        return Path.Combine(Path.GetTempPath(), ProductId, "phase8");
    }

    private static string GetDefaultUserHomeDirectoryPath()
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return userProfile;
        }

        throw new InvalidOperationException("The user home directory could not be resolved.");
    }

    private static ProductExecutableInvocation? ResolveProductExecutableInvocation(
        string? configuredPath
    )
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                throw new ArgumentException(
                    "The configured product executable path must be fully qualified.",
                    nameof(configuredPath)
                );
            }

            if (IsManagedAssemblyInvocation(configuredPath))
            {
                return new ProductExecutableInvocation(
                    GetFullPath(configuredPath),
                    ResolveDotnetExecutablePath()
                );
            }

            return new ProductExecutableInvocation(
                GetFullPath(configuredPath),
                DotnetExecutablePath: null
            );
        }

        if (!IsManagedHostInvocation(Environment.ProcessPath))
        {
            return string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? null
                : new ProductExecutableInvocation(
                    GetFullPath(Environment.ProcessPath),
                    DotnetExecutablePath: null
                );
        }

        string? managedAssemblyPath = TryGetManagedAssemblyPath();
        return
            string.IsNullOrWhiteSpace(managedAssemblyPath)
            || string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : new ProductExecutableInvocation(
                GetFullPath(managedAssemblyPath),
                GetFullPath(Environment.ProcessPath)
            );
    }

    private static string? GetCurrentProcessInvocationPath()
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
        return IsManagedHostInvocation(invocationPath) ? Environment.ProcessPath : invocationPath;
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
        string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);

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
            return length == 0 ? null : System.Text.Encoding.UTF8.GetString(commandLine, 0, length);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or System.Security.SecurityException
            )
        {
            return null;
        }
    }

    private static string? TryGetManagedAssemblyPath()
    {
        string[] commandLineArgs = Environment.GetCommandLineArgs();
        if (commandLineArgs.Length == 0)
        {
            return null;
        }

        string invocationPath = commandLineArgs[0];
        return string.Equals(
            Path.GetExtension(invocationPath),
            ".dll",
            StringComparison.OrdinalIgnoreCase
        )
            ? invocationPath
            : null;
    }

    private static string? ResolveDotnetExecutablePath()
    {
        string? processPath = Environment.ProcessPath;
        if (
            !string.IsNullOrWhiteSpace(processPath)
            && string.Equals(
                Path.GetFileName(processPath),
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return GetFullPath(processPath);
        }

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            string candidate = Path.Combine(
                dotnetRoot,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"
            );
            if (File.Exists(candidate))
            {
                return GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static string NormalizeComparablePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string normalized = OperatingSystem.IsWindows() ? fullPath.Replace('\\', '/') : fullPath;
        return Path.TrimEndingDirectorySeparator(normalized);
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsExpectedDoctorCheckFailure(Exception exception) =>
        exception
            is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException
                or System.ComponentModel.Win32Exception
                or System.Text.Json.JsonException;

    private sealed record ProductExecutableInvocation(
        string ExecutablePath,
        string? DotnetExecutablePath
    );

    private sealed record GitPhase8OwnedState(
        bool OwnedGitEntriesPresent,
        bool OwnershipManifestPresent
    );

    private sealed record GitEffectiveConfigurationInspection(
        GitEffectiveCredentialHelperDiagnostics CredentialHelper,
        GitUseHttpPathDiagnostics UseHttpPath
    );

    private sealed record GitCredentialHelperRecord(
        GitConfigurationScope Scope,
        GitCredentialHelperSelectorKind Selector,
        string? UrlPattern,
        string Value
    );

    private sealed record GitEffectiveCredentialHelperEntry(
        GitEffectiveCredentialHelperEntryKind Kind,
        GitCredentialHelperRecord Record
    );
}
