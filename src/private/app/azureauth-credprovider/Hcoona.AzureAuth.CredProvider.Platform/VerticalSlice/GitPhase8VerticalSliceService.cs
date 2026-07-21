using System.Security.Cryptography;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record GitPhase8VerticalSliceOptions
{
    public string? StateDirectoryPath { get; init; }

    public IProcessRunner? ProcessRunner { get; init; }

    public string? GitExecutablePath { get; init; }

    public string? ProductExecutablePath { get; init; }

    public bool? LocalShellGitDiscoverySupported { get; init; }

    public BoundedCredentialAcquisitionAdapter? CredentialAcquisition { get; init; }
}

public sealed record GitPhase8VerticalSliceResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

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

    public required bool DevAzureUseHttpPathPresent { get; init; }

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
    private const string ConfigureChangeSetId = "phase8-git-configure-changeset";
    private const string EntrySelector = "git.config";
    internal const string GitCredentialHelperKey = "credential.helper";
    internal const string GitUseHttpPathKey = "credential.https://dev.azure.com.useHttpPath";
    internal const string GitUseHttpPathValue = "true";
    private const string GitCredentialHelperProtocolInput =
        "protocol=https\n"
        + "host=dev.azure.com\n"
        + "path=org/project/_git/repository\n"
        + "\n";
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode StateFileUnsafeWriteBits =
        UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    private static readonly Uri GitServiceEndpoint = new("https://dev.azure.com/org");
    private readonly SystemFileSystem fileSystem;
    private readonly string gitExecutablePath;
    private readonly bool localShellGitDiscoverySupported;
    private readonly GitPhase8VerticalSliceResolvedPaths paths;
    private readonly IProcessRunner processRunner;
    private readonly ProductExecutableInvocation? productExecutableInvocation;
    private readonly Lazy<BoundedCredentialAcquisitionAdapter>? credentialAcquisition;

    public GitPhase8VerticalSliceService(GitPhase8VerticalSliceOptions? options = null)
        : this(options, configurationOnly: false)
    {
    }

    private GitPhase8VerticalSliceService(
        GitPhase8VerticalSliceOptions? options,
        bool configurationOnly)
    {
        fileSystem = new SystemFileSystem();
        paths = ResolvePaths(options);
        processRunner = options?.ProcessRunner ?? new SystemProcessRunner();
        gitExecutablePath = string.IsNullOrWhiteSpace(options?.GitExecutablePath)
            ? "git"
            : options.GitExecutablePath;
        productExecutableInvocation = ResolveProductExecutableInvocation(
            options?.ProductExecutablePath);
        localShellGitDiscoverySupported =
            options?.LocalShellGitDiscoverySupported ?? !OperatingSystem.IsWindows();
        if (!configurationOnly)
        {
            credentialAcquisition = new Lazy<BoundedCredentialAcquisitionAdapter>(
                () => options?.CredentialAcquisition
                    ?? new BoundedCredentialAcquisitionAdapter(
                        CredentialProviderCompositionRoot.CreateProduction().AcquisitionService),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    public GitPhase8VerticalSliceResolvedPaths Paths => paths;

    public static GitPhase8VerticalSliceService CreateConfigurationOnly(
        GitPhase8VerticalSliceOptions? options = null) =>
        new(options, configurationOnly: true);

    public async ValueTask<GitPhase8ConfigureDryRunResult> DryRunConfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ThrowIfUnrecognizedOwnershipManifestExists();
        ThrowIfMissingManifestLeavesProductOwnedGitConfigState();
        ThrowIfRecognizedManifestHasUnexpectedProductOwnedGitConfigState();
        ConfigurationChangePlan plan = CreateConfigurePlan();
        ConfigurationManager manager = CreateManager();
        ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
        ConfigurationPlanResult planResult;
        try
        {
            planResult = await manager.DryRunAsync(plan, cancellationToken);
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
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

        ThrowIfUnrecognizedOwnershipManifestExists();
        ThrowIfMissingManifestLeavesProductOwnedGitConfigState();
        ThrowIfRecognizedManifestHasUnexpectedProductOwnedGitConfigState();
        ConfigurationPlanResult planResult;
        try
        {
            ConfigurationChangePlan configurePlan = CreateConfigurePlan();
            EnsureTrustedConfigurationStateDirectories();
            EnsureStateGitHelperShim();
            planResult = await CreateManager()
                .ApplyAsync(configurePlan, cancellationToken);
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
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
            ConfigurationChangePlan plan = CreateConfigurePlan();
            ConfigurationManager manager = CreateManager();
            ConfigurationPlanValidationResult validation = manager.ValidatePlan(plan);
            ThrowIfRecognizedManifestHasUnexpectedProductOwnedGitConfigState();
            ConfigurationPlanResult planResult = await manager.DryRunAsync(
                plan,
                cancellationToken
            );
            configurationPlanValid = validation.IsValid
                && planResult.State == ConfigurationPlanState.Planned;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            configurationPlanValid = false;
        }

        var credentialCoreSuccess = false;
        try
        {
            CredentialResult credentialResult = GetCredentialAcquisition().Acquire(
                CreateGitRequest(),
                cancellationToken);
            credentialCoreSuccess = credentialResult.Status == CredentialResultStatus.Success;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            credentialCoreSuccess = false;
        }

        var gitCredentialHelperGetSuccess = false;
        var gitCredentialHelperStoreSuccess = false;
        var gitCredentialHelperEraseSuccess = false;
        var localShellHelperShorthandSuccess = false;
        var localShellHelperShorthandDeferred = false;
        var protocolPayloadCaptured = false;
        bool devAzureUseHttpPathPresent = TryInspectDevAzureUseHttpPathState();
        try
        {
            (
                gitCredentialHelperGetSuccess,
                protocolPayloadCaptured
            ) = ExecuteGitCredentialHelperAdapterPath(
                ProductId,
                ["git", "credential-helper", "get"],
                CredentialOperation.Get);
            gitCredentialHelperStoreSuccess = ExecuteGitCredentialHelperAdapterPath(
                ProductId,
                ["git", "credential-helper", "store"],
                CredentialOperation.Store).Success;
            gitCredentialHelperEraseSuccess = ExecuteGitCredentialHelperAdapterPath(
                ProductId,
                ["git", "credential-helper", "erase"],
                CredentialOperation.Erase).Success;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            gitCredentialHelperGetSuccess = false;
            gitCredentialHelperStoreSuccess = false;
            gitCredentialHelperEraseSuccess = false;
            protocolPayloadCaptured = false;
        }

        if (
            configurationPlanValid
            && ownedState.OwnedGitEntriesPresent
            && ownedState.OwnershipManifestPresent
            && devAzureUseHttpPathPresent
        )
        {
            try
            {
                (
                    localShellHelperShorthandSuccess,
                    localShellHelperShorthandDeferred
                ) = await ExecuteLocalShellGitHelperDiscoveryAsync(cancellationToken);
            }
            catch (Exception exception)
                when (IsExpectedDoctorCheckFailure(exception))
            {
                localShellHelperShorthandSuccess = false;
                localShellHelperShorthandDeferred = false;
            }
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
            DevAzureUseHttpPathPresent = devAzureUseHttpPathPresent,
            ProtocolPayloadCaptured = protocolPayloadCaptured,
        };
    }

    public async ValueTask<GitPhase8UnconfigureResult> UnconfigureAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (
            !TryLoadExpectedOwnershipManifest(
                out ConfigurationOwnershipManifest? manifest,
                out string? manifestJson
            )
        )
        {
            ThrowIfUnrecognizedOwnershipManifestExists();
            ThrowIfMissingManifestLeavesProductOwnedGitConfigState();
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

        ThrowIfRecognizedManifestHasUnexpectedProductOwnedGitConfigState();
        ConfigurationPlanResult planResult;
        try
        {
            planResult = await CreateManager()
                .RemoveAsync(
                    CreateUnconfigurePlan(manifest, manifestJson),
                    cancellationToken
                );
            TryDeleteStateGitHelperShim();
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
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

        if (
            !TryLoadExpectedOwnershipManifest(
                out ConfigurationOwnershipManifest? manifest,
                out string? manifestJson
            )
        )
        {
            ThrowIfUnrecognizedOwnershipManifestExists();
            ThrowIfMissingManifestLeavesProductOwnedGitConfigState();
            return;
        }

        ThrowIfRecognizedManifestHasUnexpectedProductOwnedGitConfigState();
        try
        {
            await CreateManager()
                .DryRunAsync(
                    CreateUnconfigurePlan(manifest, manifestJson),
                    cancellationToken
                );
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
    }

    private ConfigurationManager CreateManager() =>
        new(fileSystem, paths.OwnershipManifestPath, CreatePhysicalTargetWriterDispatcher());

    private ConfigurationPhysicalTargetWriterDispatcher CreatePhysicalTargetWriterDispatcher() =>
        new(fileSystem);

    private ConfigurationChangePlan CreateConfigurePlan()
    {
        string scaffoldMetadata = CreateConfigureScaffoldMetadata(
            out string? previousOwnedEntryHash
        );
        return ConfigurationChangePlanPolicy.Create(
        ConfigurePlanId,
        ConfigureChangeSetId,
            ProductId,
            ConfigurationScope.User,
            new ConfigurationManifestMetadata
            {
                ManifestId = ManifestId,
                OwnerProductId = ProductId,
                EntrySelector = EntrySelector,
                ProductVersion = ProductVersion,
                PreviousOwnedEntryHash = previousOwnedEntryHash,
            },
            [
                CreateGitConfigChange(
                    ConfigurationChangeOperation.Set,
                    GitCredentialHelperKey,
                    CreateGitCredentialHelperValue(),
                    scaffoldMetadata
                ),
                CreateGitConfigChange(
                    ConfigurationChangeOperation.Set,
                    GitUseHttpPathKey,
                    GitUseHttpPathValue,
                    scaffoldMetadata
                ),
            ]
        );
    }

    private ConfigurationChangePlan CreateUnconfigurePlan(
        ConfigurationOwnershipManifest manifest,
        string manifestJson
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);

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
            "phase8-git-unconfigure-changeset",
            ProductId,
            manifest.Scope,
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
            changes
        );
    }

    private ConfigurationChange CreateGitConfigChange(
        ConfigurationChangeOperation operation,
        string key,
        string? value,
        string? previousOwnedEntryMetadata = null
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
            PreviousOwnedEntryMetadata = previousOwnedEntryMetadata,
        };

    private string CreateGitCredentialHelperValue()
    {
        string helperValue = CreateGitCredentialHelperPathValue(paths.GitHelperPath);
        if (!IsSafeRawGitCredentialHelperPath(helperValue))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        return helperValue;
    }

    private static string CreateGitCredentialHelperPathValue(string path)
    {
        return OperatingSystem.IsWindows()
            ? path.Replace('\\', '/')
            : path;
    }

    private static bool IsSafeRawGitCredentialHelperPath(string path)
    {
        return path.Length != 0
            && path.All(IsSafeRawGitCredentialHelperPathCharacter);
    }

    private static bool IsSafeRawGitCredentialHelperPathCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character)
            || character is '/' or ':' or '.' or '_' or '-'
            || (OperatingSystem.IsWindows() && character == '\\');
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
                "The Git credential helper executable could not be resolved.");
        }

        return productExecutableInvocation;
    }

    private ConfigurationChange CreateGitConfigRemoveChange(
        ConfigurationOwnershipManifestEntry entry
    )
    {
        ArgumentNullException.ThrowIfNull(entry);

        return CreateGitConfigChange(
            ConfigurationChangeOperation.Remove,
            entry.Key,
            value: null,
            previousOwnedEntryMetadata: entry.PreviousOwnedEntryMetadata
                ?? entry.PlannedValueSha256
                ?? "owned-git-entry"
        );
    }

    private string CreateConfigureScaffoldMetadata(out string? previousOwnedEntryHash)
    {
        previousOwnedEntryHash = null;
        if (
            TryLoadExpectedOwnershipManifest(
                out ConfigurationOwnershipManifest? manifest,
                out string? manifestJson
            )
            && TryGetExistingScaffoldId(manifest, out string? scaffoldId)
        )
        {
            previousOwnedEntryHash = ComputeSha256Metadata(manifestJson);
            return GitConfigPhysicalTargetWriter.CreateProductOwnedCredentialScaffoldMetadata(
                scaffoldId
            );
        }

        return GitConfigPhysicalTargetWriter.CreateProductOwnedCredentialScaffoldMetadata(
            Guid.NewGuid().ToString("N")
        );
    }

    private void ThrowIfUnrecognizedOwnershipManifestExists()
    {
        if (
            OwnershipManifestPathExists()
            && !TryLoadExpectedOwnershipManifest(out _, out _)
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git ownership manifest is not recognized."
            );
        }
    }

    private void ThrowIfMissingManifestLeavesProductOwnedGitConfigState()
    {
        if (OwnershipManifestPathExists() || !ProductOwnedGitConfigStateExists())
        {
            return;
        }

        throw new GitPhase8UnrecognizedStateException(
            "The Phase 8 Git configuration state is not recognized."
        );
    }

    private void ThrowIfRecognizedManifestHasUnexpectedProductOwnedGitConfigState()
    {
        string? expectedScaffoldId = TryLoadExpectedOwnershipManifest(
                out ConfigurationOwnershipManifest? manifest,
                out _
            )
            && TryGetExistingScaffoldId(manifest, out string? scaffoldId)
            ? scaffoldId
            : null;

        if (UnexpectedProductOwnedGitConfigStateExists(expectedScaffoldId))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized."
            );
        }
    }

    private bool UnexpectedProductOwnedGitConfigStateExists(string? expectedScaffoldId)
    {
        try
        {
            if (!fileSystem.FileExists(paths.GitConfigPath))
            {
                return false;
            }

            if (!CanSafelyReadPath(paths.GitConfigPath))
            {
                throw new GitPhase8UnrecognizedStateException(
                    "The Phase 8 Git configuration state is not recognized."
                );
            }

            string gitConfig = fileSystem.ReadAllText(paths.GitConfigPath);
            return ContainsUnexpectedProductOwnedGitConfigState(gitConfig, expectedScaffoldId);
        }
        catch (GitPhase8UnrecognizedStateException)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
    }

    private bool ContainsUnexpectedProductOwnedGitConfigState(
        string gitConfig,
        string? expectedScaffoldId
    )
    {
        GitConfigCredentialSectionKind credentialSectionKind =
            GitConfigCredentialSectionKind.Other;
        foreach (string rawLine in SplitLines(gitConfig))
        {
            string line = rawLine.Length > 0 && rawLine[^1] == '\r'
                ? rawLine[..^1]
                : rawLine;
            string trimmedStart = line.TrimStart();
            if (TryParseSimpleGitConfigSectionText(trimmedStart, out string sectionText))
            {
                credentialSectionKind = GetCredentialSectionKind(sectionText);
                continue;
            }

            if (
                TryParseSimpleProductScaffoldMarkerId(trimmedStart, out string? markerId)
                && (
                    credentialSectionKind
                        is not GitConfigCredentialSectionKind.Bare
                        and not GitConfigCredentialSectionKind.ManagedDevAzureRoot
                    || !string.Equals(markerId, expectedScaffoldId, StringComparison.Ordinal)
                )
            )
            {
                return true;
            }

            if (
                credentialSectionKind
                    is GitConfigCredentialSectionKind.ManagedDevAzureRoot
                    or GitConfigCredentialSectionKind.DevAzureScoped
                && TryParseSimpleGitConfigAssignment(
                    trimmedStart,
                    out string variableName,
                    out string value
                )
                && string.Equals(variableName, "helper", StringComparison.OrdinalIgnoreCase)
                && IsProductCredentialHelperValue(value)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static GitConfigCredentialSectionKind GetCredentialSectionKind(string sectionText)
    {
        if (string.Equals(sectionText, "credential", StringComparison.OrdinalIgnoreCase))
        {
            return GitConfigCredentialSectionKind.Bare;
        }

        if (!TryParseDevAzureComCredentialSubsection(sectionText, out string subsection))
        {
            return GitConfigCredentialSectionKind.Other;
        }

        return IsRootDevAzureComCredentialSubsection(subsection)
            ? GitConfigCredentialSectionKind.ManagedDevAzureRoot
            : GitConfigCredentialSectionKind.DevAzureScoped;
    }

    private bool OwnershipManifestPathExists()
    {
        try
        {
            return fileSystem.FileExists(paths.OwnershipManifestPath)
                || fileSystem.DirectoryExists(paths.OwnershipManifestPath);
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            return true;
        }
    }

    private bool ProductOwnedGitConfigStateExists()
    {
        try
        {
            if (!fileSystem.FileExists(paths.GitConfigPath))
            {
                return false;
            }

            if (!CanSafelyReadPath(paths.GitConfigPath))
            {
                throw new GitPhase8UnrecognizedStateException(
                    "The Phase 8 Git configuration state is not recognized."
                );
            }

            string gitConfig = fileSystem.ReadAllText(paths.GitConfigPath);
            return ContainsProductOwnedGitConfigState(gitConfig);
        }
        catch (GitPhase8UnrecognizedStateException)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.",
                exception
            );
        }
    }

    private bool ContainsProductOwnedGitConfigState(string gitConfig)
    {
        var inCredentialSection = false;
        foreach (string rawLine in SplitLines(gitConfig))
        {
            string line = rawLine.Length > 0 && rawLine[^1] == '\r'
                ? rawLine[..^1]
                : rawLine;
            string trimmedStart = line.TrimStart();
            if (
                trimmedStart.StartsWith(
                    "# azureauth-credprovider: product-owned credential scaffold;",
                    StringComparison.Ordinal
                )
            )
            {
                return true;
            }

            if (TryParseSimpleGitConfigSection(trimmedStart, out bool isCredentialSection))
            {
                inCredentialSection = isCredentialSection;
                continue;
            }

            if (
                inCredentialSection
                && TryParseSimpleGitConfigAssignment(
                    trimmedStart,
                    out string variableName,
                    out string value
                )
                && string.Equals(variableName, "helper", StringComparison.OrdinalIgnoreCase)
                && IsProductCredentialHelperValue(value)
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool IsProductCredentialHelperValue(string value)
    {
        if (string.Equals(value, ProductId, StringComparison.Ordinal))
        {
            return true;
        }

        return TryCreateGitCredentialHelperValue(out string? helperValue)
            && string.Equals(value, helperValue, GetPathComparison());
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

    private static bool TryParseSimpleProductScaffoldMarkerId(
        string trimmedStart,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? scaffoldId
    )
    {
        scaffoldId = null;
        const string MarkerPrefix =
            "# azureauth-credprovider: product-owned credential scaffold;";
        if (!trimmedStart.StartsWith(MarkerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] tokens = trimmedStart[MarkerPrefix.Length..]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens)
        {
            if (token.StartsWith("id=", StringComparison.Ordinal))
            {
                scaffoldId = token["id=".Length..];
                return true;
            }
        }

        scaffoldId = string.Empty;
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

    private static bool IsDevAzureComCredentialSubsection(string subsection)
    {
        return string.Equals(subsection, "https://dev.azure.com", StringComparison.Ordinal)
            || TryCreateDevAzureComUri(subsection, out _);
    }

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
        if (
            trimmedStart.Length == 0
            || trimmedStart[0] is '#' or ';' or '['
        )
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
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                GitServiceEndpoint
            ),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.DeviceCode,
            InteractivePolicy = InteractivePolicy.Never,
            AcquisitionMode = AcquisitionMode.SilentOnly,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext
            {
                ExplicitCiMode = false,
                AllowsPersistentWrites = false,
            },
        };

    private BoundedCredentialAcquisitionAdapter GetCredentialAcquisition() =>
        credentialAcquisition?.Value
        ?? throw new InvalidOperationException(
            "Credential acquisition is unavailable in a configuration-only service.");

    private (bool Success, bool PayloadCaptured) ExecuteGitCredentialHelperAdapterPath(
        string executablePath,
        string[] arguments,
        CredentialOperation expectedOperation
    )
    {
        var protocolStdout = new StringWriter();
        AdapterHostExecutionOutcome outcome = new GitCredentialHelperAdapter(
            GetCredentialAcquisition()
        ).Execute(
            executablePath,
            arguments,
            new StringReader(GitCredentialHelperProtocolInput),
            protocolStdout,
            TextWriter.Null,
            new DiagnosticRouter([], SecretRedactor.Empty)
        );

        string capturedPayload = protocolStdout.ToString();
        bool expectsPayload = expectedOperation == CredentialOperation.Get;
        bool payloadCaptured = capturedPayload.Length != 0;
        bool success = outcome.Result.ExitCode == AdapterHostExitCode.Success
            && outcome.Result.WriteProtocolStdout == expectsPayload
            && payloadCaptured == expectsPayload
            && !capturedPayload.Contains('\r');

        return (success, payloadCaptured);
    }

    private async ValueTask<(bool Success, bool Deferred)> ExecuteLocalShellGitHelperDiscoveryAsync(
        CancellationToken cancellationToken)
    {
        if (!localShellGitDiscoverySupported)
        {
            return (Success: false, Deferred: true);
        }

        string scratchDirectory = Path.Combine(
            paths.StateDirectoryPath,
            "doctor-git-discovery",
            Guid.NewGuid().ToString("N"));
        string homeDirectory = Path.Combine(scratchDirectory, "home");
        string workDirectory = Path.Combine(scratchDirectory, "work");
        string markerPath = Path.Combine(scratchDirectory, "helper-ran");

        try
        {
            Directory.CreateDirectory(homeDirectory);
            Directory.CreateDirectory(workDirectory);

            if (!CanSafelyExecuteStateGitHelperShim())
            {
                return (Success: false, Deferred: false);
            }

            ProcessResult result = await processRunner
                .RunAsync(
                    CreateLocalShellGitDiscoveryStartSpec(
                        homeDirectory,
                        workDirectory,
                        markerPath),
                    cancellationToken)
                .ConfigureAwait(false);

            bool success = result.ExitCode == 0
                && File.Exists(markerPath)
                && IsExpectedGitCredentialFillOutput(result.StandardOutput)
                && result.StandardError.Length == 0;
            return (success, Deferred: false);
        }
        finally
        {
            TryDeleteDirectory(scratchDirectory);
        }
    }

    private void EnsureTrustedConfigurationStateDirectories()
    {
        EnsureTrustedStateDirectory(GetRequiredDirectoryName(paths.GitConfigPath));
        EnsureTrustedStateDirectory(GetRequiredDirectoryName(paths.OwnershipManifestPath));
    }

    private void EnsureStateGitHelperShim()
    {
        ProductExecutableInvocation invocation = GetRequiredProductExecutableInvocation();
        ThrowIfUnsafeProductExecutableInvocation(invocation);
        ThrowIfUnsafeStateGitHelperPath();
        EnsureTrustedStateDirectory(paths.GitHelperDirectoryPath);
        WriteLocalShellProductShim(paths.GitHelperPath, invocation);
        if (!OperatingSystem.IsWindows())
        {
            fileSystem.SetUnixFileMode(
                paths.GitHelperPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        ThrowIfUnsafeStateFile(paths.GitHelperPath, requireExecutable: true);
    }

    private void ThrowIfUnsafeStateGitHelperPath()
    {
        ThrowIfPathIsOutsideStateDirectory(paths.GitHelperPath);
        ThrowIfUnsafeStateDirectoryAncestors();
        ThrowIfExistingStateDirectoryChainIsUnsafe(paths.GitHelperDirectoryPath);

        if (fileSystem.DirectoryExists(paths.GitHelperPath))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        if (fileSystem.FileExists(paths.GitHelperPath))
        {
            ThrowIfUnsafeStateFile(paths.GitHelperPath, requireExecutable: false);
        }
    }

    private static string GetRequiredDirectoryName(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
    }

    private void EnsureTrustedStateDirectory(string directory)
    {
        ThrowIfPathIsOutsideStateDirectory(directory);
        ThrowIfUnsafeStateDirectoryAncestors();
        foreach (string stateDirectory in EnumerateStateDirectoryChain(directory))
        {
            if (!fileSystem.DirectoryExists(stateDirectory))
            {
                CreateOwnerOnlyDirectory(stateDirectory);
            }

            ThrowIfUnsafeStateDirectory(stateDirectory);
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
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
        }
    }

    private void WriteLocalShellProductShim(
        string helperPath,
        ProductExecutableInvocation invocation)
    {
        fileSystem.AtomicWriteAllText(
            helperPath,
            CreateLocalShellProductShimContent(invocation),
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly);
    }

    private static string CreateLocalShellProductShimContent(
        ProductExecutableInvocation invocation)
    {
        string command = invocation.DotnetExecutablePath is null
            ? QuotePosixShellArgument(invocation.ExecutablePath)
            : string.Concat(
                QuotePosixShellArgument(invocation.DotnetExecutablePath),
                " ",
                QuotePosixShellArgument(invocation.ExecutablePath));
        return string.Concat(
            "#!/bin/sh\n",
            "if [ -n \"${AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER-}\" ]; then\n",
            "  : > \"$AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER\" || exit 70\n",
            "fi\n",
            "exec ",
            command,
            " git credential-helper \"$@\"\n");
    }

    private static string QuotePosixShellArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat("'", value.Replace("'", "'\"'\"'", StringComparison.Ordinal), "'");
    }

    private static bool IsExpectedGitCredentialFillOutput(string standardOutput)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in SplitLines(standardOutput))
        {
            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return false;
            }

            string key = line[..separatorIndex];
            string value = line[(separatorIndex + 1)..];
            if (
                key is not ("protocol" or "host" or "path" or "username" or "password")
                || !fields.TryAdd(key, value)
            )
            {
                return false;
            }
        }

        return fields.Count == 5
            && string.Equals(fields["protocol"], "https", StringComparison.Ordinal)
            && string.Equals(fields["host"], "dev.azure.com", StringComparison.Ordinal)
            && string.Equals(
                fields["path"],
                "org/project/_git/repository",
                StringComparison.Ordinal)
            && string.Equals(fields["username"], "AzureDevOps", StringComparison.Ordinal)
            && fields["password"].StartsWith("fake-secret-", StringComparison.Ordinal);
    }

    private ProcessStartSpec CreateLocalShellGitDiscoveryStartSpec(
        string homeDirectory,
        string workDirectory,
        string markerPath)
    {
        return new ProcessStartSpec(
            gitExecutablePath,
            [
                "credential",
                "fill",
            ],
            workingDirectory: workDirectory,
            environment: CreateLocalShellGitDiscoveryEnvironment(
                homeDirectory,
                markerPath,
                paths.GitConfigPath),
            standardInput: GitCredentialHelperProtocolInput,
            environmentMode: ProcessEnvironmentMode.ExplicitOnly);
    }

    private static Dictionary<string, string?> CreateLocalShellGitDiscoveryEnvironment(
        string homeDirectory,
        string markerPath,
        string gitConfigPath)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER"] = markerPath,
            ["GIT_ASKPASS"] = null,
            ["GIT_CONFIG_GLOBAL"] = gitConfigPath,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_SYSTEM"] = Path.Combine(homeDirectory, "system.gitconfig"),
            ["GIT_SSH_ASKPASS"] = null,
            ["GIT_SSH_COMMAND"] = null,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["HOME"] = homeDirectory,
            ["PATH"] = Environment.GetEnvironmentVariable("PATH"),
            ["SSH_ASKPASS"] = null,
        };
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key || entry.Value is not string value)
            {
                continue;
            }

            if (
                string.Equals(key, "DOTNET_MULTILEVEL_LOOKUP", StringComparison.Ordinal)
                || string.Equals(key, "DOTNET_ROOT", StringComparison.Ordinal)
                || key.StartsWith("DOTNET_ROOT_", StringComparison.Ordinal)
            )
            {
                environment[key] = value;
            }
        }

        return environment;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
        }
    }

    private bool TryInspectDevAzureUseHttpPathState()
    {
        try
        {
            return DevAzureUseHttpPathStateExists();
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool DevAzureUseHttpPathStateExists()
    {
        if (!fileSystem.FileExists(paths.GitConfigPath))
        {
            return false;
        }

        if (!CanSafelyReadPath(paths.GitConfigPath))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized."
            );
        }

        return ContainsDevAzureUseHttpPathState(fileSystem.ReadAllText(paths.GitConfigPath));
    }

    private static bool ContainsDevAzureUseHttpPathState(string gitConfig)
    {
        var inDevAzureCredentialSection = false;
        foreach (string rawLine in SplitLines(gitConfig))
        {
            string line = rawLine.Length > 0 && rawLine[^1] == '\r'
                ? rawLine[..^1]
                : rawLine;
            string trimmedStart = line.TrimStart();
            if (TryParseSimpleGitConfigSection(trimmedStart, out _))
            {
                inDevAzureCredentialSection = TryParseSimpleGitConfigSectionText(
                    trimmedStart,
                    out string sectionText
                )
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

    private (ConfigurationOwnershipManifest Manifest, string Json) LoadOwnershipManifest()
    {
        string manifestJson = fileSystem.ReadAllText(paths.OwnershipManifestPath);
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestJson);
        return (manifest, manifestJson);
    }

    private bool TryLoadExpectedOwnershipManifest(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out ConfigurationOwnershipManifest? manifest,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? manifestJson
    )
    {
        manifest = null;
        manifestJson = null;
        try
        {
            if (!fileSystem.FileExists(paths.OwnershipManifestPath))
            {
                return false;
            }

            if (!CanSafelyReadPath(paths.OwnershipManifestPath))
            {
                return false;
            }

            (manifest, manifestJson) = LoadOwnershipManifest();
            if (
                !HasExpectedManifestMetadata(manifest)
                || !HasExpectedManagedManifestEntries(manifest)
            )
            {
                manifest = null;
                manifestJson = null;
                return false;
            }

            return true;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            manifest = null;
            manifestJson = null;
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
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            ownershipManifestPresent = true;
        }

        if (!ownershipManifestPresent)
        {
            return new GitPhase8OwnedState(
                OwnedGitEntriesPresent: TryInspectProductOwnedGitConfigState(),
                OwnershipManifestPresent: false
            );
        }

        var ownedGitEntriesPresent = false;
        try
        {
            if (
                TryLoadExpectedOwnershipManifest(
                    out ConfigurationOwnershipManifest? manifest,
                    out string? manifestJson
                )
            )
            {
                ownedGitEntriesPresent = await CanDryRunOwnedGitRemovalAsync(
                    manifest,
                    manifestJson,
                    cancellationToken
                );
            }
            else
            {
                ownedGitEntriesPresent = TryInspectProductOwnedGitConfigState();
            }
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            ownedGitEntriesPresent = false;
        }

        return new GitPhase8OwnedState(
            OwnedGitEntriesPresent: ownedGitEntriesPresent,
            OwnershipManifestPresent: ownershipManifestPresent
        );
    }

    private bool TryInspectProductOwnedGitConfigState()
    {
        try
        {
            return ProductOwnedGitConfigStateExists();
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private async ValueTask<bool> CanDryRunOwnedGitRemovalAsync(
        ConfigurationOwnershipManifest manifest,
        string manifestJson,
        CancellationToken cancellationToken
    )
    {
        ConfigurationPlanResult planResult = await CreateManager()
            .DryRunAsync(CreateUnconfigurePlan(manifest, manifestJson), cancellationToken);
        return planResult.State == ConfigurationPlanState.Planned;
    }

    private static bool HasExpectedManifestMetadata(ConfigurationOwnershipManifest manifest) =>
        string.Equals(manifest.ManifestId, ManifestId, StringComparison.Ordinal)
        && string.Equals(manifest.PlanId, ConfigurePlanId, StringComparison.Ordinal)
        && string.Equals(manifest.ChangeSetId, ConfigureChangeSetId, StringComparison.Ordinal)
        && string.Equals(manifest.OwnerProductId, ProductId, StringComparison.Ordinal)
        && manifest.Scope == ConfigurationScope.User
        && string.Equals(manifest.EntrySelector, EntrySelector, StringComparison.Ordinal)
        && string.Equals(manifest.ProductVersion, ProductVersion, StringComparison.Ordinal)
        && manifest.ResourceIdentity is null
        && !manifest.ContainsCredentialMaterial
        && manifest.SafeMetadata.Count == 0;

    private bool HasExpectedManagedManifestEntries(ConfigurationOwnershipManifest manifest) =>
        manifest.Entries.Count == 2
        && TryGetExistingScaffoldId(manifest, out _)
        && HasExpectedManagedManifestEntry(
            manifest,
            sequence: 1,
            GitCredentialHelperKey,
            CreateExpectedGitCredentialHelperManifestValues()
        )
        && HasExpectedManagedManifestEntry(
            manifest,
            sequence: 2,
            GitUseHttpPathKey,
            [GitUseHttpPathValue]
        );

    private bool HasExpectedManagedManifestEntry(
        ConfigurationOwnershipManifest manifest,
        int sequence,
        string expectedKey,
        string[] expectedValues
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKey);
        ArgumentNullException.ThrowIfNull(expectedValues);
        if (expectedValues.Length == 0 || expectedValues.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one expected manifest value is required.",
                nameof(expectedValues));
        }

        ConfigurationOwnershipManifestEntry[] matchingEntries = manifest
            .Entries.Where(entry => MatchesManagedManifestEntry(entry, expectedKey))
            .ToArray();
        string[] expectedValueHashes = expectedValues.Select(ComputeSha256).ToArray();

        return matchingEntries.Length == 1
            && matchingEntries[0].Sequence == sequence
            && matchingEntries[0].Operation == ConfigurationChangeOperation.Set
            && matchingEntries[0].PreserveDeclarationsAndComments
            && matchingEntries[0].HasPlannedValue
            && !matchingEntries[0].IsSecretValue
            && expectedValueHashes.Contains(
                matchingEntries[0].PlannedValueSha256,
                StringComparer.Ordinal
            );
    }

    private string[] CreateExpectedGitCredentialHelperManifestValues()
    {
        List<string> expectedValues = [ProductId];
        if (TryCreateGitCredentialHelperValue(out string? helperValue))
        {
            expectedValues.Add(helperValue);
        }

        return [.. expectedValues];
    }

    private bool TryCreateGitCredentialHelperValue(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? helperValue)
    {
        try
        {
            helperValue = CreateGitCredentialHelperValue();
            return true;
        }
        catch (GitPhase8UnrecognizedStateException)
        {
            helperValue = null;
            return false;
        }
    }

    private bool TryGetExistingScaffoldId(
            ConfigurationOwnershipManifest manifest,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? scaffoldId
    )
    {
        scaffoldId = null;
        ConfigurationOwnershipManifestEntry[] managedEntries = manifest
            .Entries.Where(IsManagedManifestEntry)
            .ToArray();
        var scaffoldIds = new List<string>(managedEntries.Length);
        foreach (ConfigurationOwnershipManifestEntry entry in managedEntries)
        {
            if (
                !GitConfigPhysicalTargetWriter.TryGetProductOwnedCredentialScaffoldId(
                    entry.PreviousOwnedEntryMetadata,
                    out string? id
                )
            )
            {
                return false;
            }

            scaffoldIds.Add(id);
        }

        string[] distinctScaffoldIds = scaffoldIds.Distinct(StringComparer.Ordinal).ToArray();

        if (distinctScaffoldIds.Length != 1)
        {
            return false;
        }

        scaffoldId = distinctScaffoldIds[0];
        return true;
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

    private bool CanSafelyReadPath(string path)
    {
        try
        {
            ThrowIfUnsafeStatePath(path, requireExecutable: false);
            return true;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private bool CanSafelyExecuteStateGitHelperShim()
    {
        try
        {
            ProductExecutableInvocation invocation = GetRequiredProductExecutableInvocation();
            ThrowIfUnsafeProductExecutableInvocation(invocation);
            ThrowIfUnsafeStatePath(paths.GitHelperPath, requireExecutable: true);
            return string.Equals(
                fileSystem.ReadAllText(paths.GitHelperPath),
                CreateLocalShellProductShimContent(invocation),
                StringComparison.Ordinal);
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            return false;
        }
    }

    private void ThrowIfUnsafeProductExecutableInvocation(
        ProductExecutableInvocation invocation)
    {
        ThrowIfProductExecutableCannotHandleSharedCliEntrypoint(invocation);
        ThrowIfUnsafeProductExecutablePath(
            invocation.ExecutablePath,
            requireExecutable: !IsManagedAssemblyInvocation(invocation.ExecutablePath));
        if (invocation.DotnetExecutablePath is not null)
        {
            ThrowIfUnsafeProductExecutablePath(
                invocation.DotnetExecutablePath,
                requireExecutable: true);
        }
    }

    private static void ThrowIfProductExecutableCannotHandleSharedCliEntrypoint(
        ProductExecutableInvocation invocation)
    {
        if (
            !string.Equals(
                Path.GetFileNameWithoutExtension(invocation.ExecutablePath),
                GitCredentialHelperAdapter.ProductExecutableName,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private void ThrowIfUnsafeProductExecutablePath(string path, bool requireExecutable)
    {
        if (!fileSystem.FileExists(path) || IsUnsafeReparsePoint(path))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        try
        {
            _ = fileSystem.GetFileLength(path);
        }
        catch (PlatformNotSupportedException)
        {
        }

        foreach (string directory in EnumerateParentDirectories(path))
        {
            if (fileSystem.DirectoryExists(directory))
            {
                ThrowIfUnsafeProductExecutableDirectory(directory);
            }
        }

        ThrowIfWindowsAclValidationUnsupported();
        ThrowIfUntrustedStateOwner(path, allowRoot: true);
        UnixFileMode mode = fileSystem.GetUnixFileMode(path);
        if (
            (mode & StateFileUnsafeWriteBits) != 0
            || !HasRequiredReadBit(path, mode)
            || (requireExecutable && !HasRequiredExecutableBit(path, mode))
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private void ThrowIfUnsafeProductExecutableDirectory(string directory)
    {
        if (IsUnsafeReparsePoint(directory))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        ThrowIfWindowsAclValidationUnsupported();
        ThrowIfUntrustedStateOwner(directory, allowRoot: true);
        UnixFileMode mode = fileSystem.GetUnixFileMode(directory);
        if ((mode & StateFileUnsafeWriteBits) != 0 && (mode & UnixFileMode.StickyBit) == 0)
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private void ThrowIfUnsafeStatePath(string path, bool requireExecutable)
    {
        ThrowIfPathIsOutsideStateDirectory(path);
        ThrowIfUnsafeStateDirectoryAncestors();
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        ThrowIfStateDirectoryChainIsUnsafe(directory);
        ThrowIfUnsafeStateFile(path, requireExecutable);
    }

    private void ThrowIfExistingStateDirectoryChainIsUnsafe(string directory)
    {
        foreach (string stateDirectory in EnumerateStateDirectoryChain(directory))
        {
            if (fileSystem.DirectoryExists(stateDirectory))
            {
                ThrowIfUnsafeStateDirectory(stateDirectory);
            }
        }
    }

    private void ThrowIfStateDirectoryChainIsUnsafe(string directory)
    {
        foreach (string stateDirectory in EnumerateStateDirectoryChain(directory))
        {
            ThrowIfUnsafeStateDirectory(stateDirectory);
        }
    }

    private void ThrowIfUnsafeStateDirectoryAncestors()
    {
        foreach (string directory in EnumerateParentDirectories(paths.StateDirectoryPath))
        {
            if (fileSystem.DirectoryExists(directory))
            {
                ThrowIfUnsafeStateAncestorDirectory(directory);
            }
        }
    }

    private void ThrowIfUnsafeStateAncestorDirectory(string directory)
    {
        if (IsUnsafeReparsePoint(directory))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        ThrowIfWindowsAclValidationUnsupported();
        ThrowIfUntrustedStateOwner(directory, allowRoot: true);
        UnixFileMode mode = fileSystem.GetUnixFileMode(directory);
        if ((mode & StateFileUnsafeWriteBits) != 0 && (mode & UnixFileMode.StickyBit) == 0)
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private void ThrowIfUnsafeStateDirectory(string directory)
    {
        if (!fileSystem.DirectoryExists(directory) || IsUnsafeReparsePoint(directory))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        ThrowIfWindowsAclValidationUnsupported();
        ThrowIfUntrustedStateOwner(directory, allowRoot: true);
        UnixFileMode mode = fileSystem.GetUnixFileMode(directory);
        if ((mode & StateFileUnsafeWriteBits) != 0)
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private void ThrowIfUnsafeStateFile(string path, bool requireExecutable)
    {
        if (!fileSystem.FileExists(path) || IsUnsafeReparsePoint(path))
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }

        try
        {
            _ = fileSystem.GetFileLength(path);
        }
        catch (PlatformNotSupportedException)
        {
        }

        ThrowIfWindowsAclValidationUnsupported();
        ThrowIfUntrustedStateOwner(path, allowRoot: true);
        UnixFileMode mode = fileSystem.GetUnixFileMode(path);
        if (
            (mode & StateFileUnsafeWriteBits) != 0
            || !HasRequiredReadBit(path, mode)
            || (requireExecutable && !HasRequiredExecutableBit(path, mode))
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private void ThrowIfUntrustedStateOwner(string path, bool allowRoot)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Phase 9 Git helper state owner validation is supported only on Linux.");
        }

        FileSystemOwner owner = fileSystem.GetOwner(path);
        FileSystemOwner currentOwner = fileSystem.GetCurrentOwner();
        if (
            !string.Equals(owner.Id, currentOwner.Id, StringComparison.Ordinal)
            && (!allowRoot || !string.Equals(owner.Id, "unix:0", StringComparison.Ordinal))
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
        }
    }

    private static void ThrowIfWindowsAclValidationUnsupported()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Phase 9 Git helper state ACL validation is not implemented for Windows.");
        }
    }

    private bool HasRequiredReadBit(string path, UnixFileMode mode)
    {
        if (IsCurrentUserOwned(path))
        {
            return (mode & UnixFileMode.UserRead) != 0;
        }

        if (OperatingSystem.IsLinux())
        {
            return (mode & UnixFileMode.OtherRead) != 0;
        }

        return HasAnyReadBit(mode);
    }

    private bool HasRequiredExecutableBit(string path, UnixFileMode mode)
    {
        if (IsCurrentUserOwned(path))
        {
            return (mode & UnixFileMode.UserExecute) != 0;
        }

        if (OperatingSystem.IsLinux())
        {
            return (mode & UnixFileMode.OtherExecute) != 0;
        }

        return HasAnyExecutableBit(mode);
    }

    private static bool HasAnyReadBit(UnixFileMode mode)
    {
        const UnixFileMode readBits =
            UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        return (mode & readBits) != 0;
    }

    private bool IsCurrentUserOwned(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        FileSystemOwner owner = fileSystem.GetOwner(path);
        FileSystemOwner currentOwner = fileSystem.GetCurrentOwner();
        return string.Equals(owner.Id, currentOwner.Id, StringComparison.Ordinal);
    }

    private static bool HasAnyExecutableBit(UnixFileMode mode)
    {
        const UnixFileMode executableBits =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (mode & executableBits) != 0;
    }

    private bool IsUnsafeReparsePoint(string path)
    {
        if (fileSystem.IsSymbolicLink(path))
        {
            return true;
        }

        return fileSystem is IFileSystemReparsePointSafety reparsePointSafety
            && reparsePointSafety.IsReparsePoint(path);
    }

    private void ThrowIfPathIsOutsideStateDirectory(string path)
    {
        string relativePath = Path.GetRelativePath(paths.StateDirectoryPath, path);
        if (
            Path.IsPathRooted(relativePath)
            || string.Equals(relativePath, "..", GetPathComparison())
            || relativePath.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                GetPathComparison())
            || (
                Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar
                && relativePath.StartsWith(
                    ".." + Path.AltDirectorySeparatorChar,
                    GetPathComparison())
            )
        )
        {
            throw new GitPhase8UnrecognizedStateException(
                "The Phase 8 Git configuration state is not recognized.");
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
        foreach (string component in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (component is "." or "..")
            {
                throw new GitPhase8UnrecognizedStateException(
                    "The Phase 8 Git configuration state is not recognized.");
            }

            current = Path.Combine(current, component);
            yield return current;
        }
    }

    private static IEnumerable<string> EnumerateParentDirectories(string path)
    {
        string? current = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(current))
        {
            yield return current;
            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                yield break;
            }

            current = parent;
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
        string gitConfigPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "git", "user.gitconfig")
        );
        string ownershipManifestPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "manifests", "git-ownership-manifest.json")
        );
        string gitHelperDirectoryPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "git-helper")
        );
        string gitHelperPath = GetFullPath(
            Path.Combine(
                gitHelperDirectoryPath,
                GitCredentialHelperAdapter.HelperExecutableName)
        );

        if (string.Equals(gitConfigPath, ownershipManifestPath, GetPathComparison()))
        {
            throw new ArgumentException(
                "The Git config path and ownership manifest path must be different.",
                nameof(options)
            );
        }

        return new GitPhase8VerticalSliceResolvedPaths
        {
            StateDirectoryPath = stateDirectoryPath,
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
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.Combine(localApplicationData, ProductId, "phase8");
        }

        return Path.Combine(Path.GetTempPath(), ProductId, "phase8");
    }

    private static ProductExecutableInvocation? ResolveProductExecutableInvocation(
        string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (IsManagedAssemblyInvocation(configuredPath))
            {
                return new ProductExecutableInvocation(
                    GetFullPath(configuredPath),
                    ResolveDotnetExecutablePath());
            }

            return new ProductExecutableInvocation(
                GetFullPath(configuredPath),
                DotnetExecutablePath: null);
        }

        if (!IsManagedHostInvocation(Environment.ProcessPath))
        {
            return string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? null
                : new ProductExecutableInvocation(
                    GetFullPath(Environment.ProcessPath),
                    DotnetExecutablePath: null);
        }

        string? managedAssemblyPath = TryGetManagedAssemblyPath();
        return string.IsNullOrWhiteSpace(managedAssemblyPath)
            || string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : new ProductExecutableInvocation(
                GetFullPath(managedAssemblyPath),
                GetFullPath(Environment.ProcessPath));
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
        return IsManagedHostInvocation(invocationPath)
            ? Environment.ProcessPath
            : invocationPath;
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
        string.Equals(
            Path.GetExtension(path),
            ".dll",
            StringComparison.OrdinalIgnoreCase);

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
            return length == 0
                ? null
                : System.Text.Encoding.UTF8.GetString(commandLine, 0, length);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
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
            StringComparison.OrdinalIgnoreCase)
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
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return GetFullPath(processPath);
        }

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            string candidate = Path.Combine(
                dotnetRoot,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
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
        string normalized = OperatingSystem.IsWindows()
            ? fullPath.Replace('\\', '/')
            : fullPath;
        return Path.TrimEndingDirectorySeparator(normalized);
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string ComputeSha256Metadata(string value) => "sha256:" + ComputeSha256(value);

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsExpectedDoctorCheckFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception
            or System.Text.Json.JsonException;

    private sealed record ProductExecutableInvocation(
        string ExecutablePath,
        string? DotnetExecutablePath);

    private sealed record GitPhase8OwnedState(
        bool OwnedGitEntriesPresent,
        bool OwnershipManifestPresent
    );

    private enum GitConfigCredentialSectionKind
    {
        Other,
        Bare,
        ManagedDevAzureRoot,
        DevAzureScoped,
    }
}
