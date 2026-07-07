using System.Security.Cryptography;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record GitPhase8VerticalSliceOptions
{
    public string? StateDirectoryPath { get; init; }
}

public sealed record GitPhase8VerticalSliceResolvedPaths
{
    public required string StateDirectoryPath { get; init; }

    public required string GitConfigPath { get; init; }

    public required string OwnershipManifestPath { get; init; }
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

    public required bool GitProtocolPathSuccess { get; init; }

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
    internal const string GitCredentialHelperValue = ProductId;
    internal const string GitUseHttpPathKey = "credential.https://dev.azure.com.useHttpPath";
    internal const string GitUseHttpPathValue = "true";

    private static readonly Uri GitServiceEndpoint = new("https://dev.azure.com/org");
    private readonly SystemFileSystem fileSystem;
    private readonly GitPhase8VerticalSliceResolvedPaths paths;

    public GitPhase8VerticalSliceService(GitPhase8VerticalSliceOptions? options = null)
    {
        fileSystem = new SystemFileSystem();
        paths = ResolvePaths(options);
    }

    public GitPhase8VerticalSliceResolvedPaths Paths => paths;

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
            planResult = await CreateManager()
                .ApplyAsync(CreateConfigurePlan(), cancellationToken);
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

        CredentialResult? credentialResult = null;
        var credentialCoreSuccess = false;
        try
        {
            credentialResult = CreateCredentialCoreService().Execute(CreateGitRequest());
            credentialCoreSuccess = credentialResult.Status == CredentialResultStatus.Success;
        }
        catch (Exception exception)
            when (IsExpectedDoctorCheckFailure(exception))
        {
            credentialCoreSuccess = false;
        }

        var gitProtocolPathSuccess = false;
        var protocolPayloadCaptured = false;
        if (credentialResult is not null)
        {
            try
            {
                (
                    gitProtocolPathSuccess,
                    protocolPayloadCaptured
                ) = ExecuteGitProtocolPath(credentialResult);
            }
            catch (Exception exception)
                when (IsExpectedDoctorCheckFailure(exception))
            {
                gitProtocolPathSuccess = false;
                protocolPayloadCaptured = false;
            }
        }

        return new GitPhase8DoctorResult
        {
            Paths = paths,
            ConfigurationPlanValid = configurationPlanValid,
            OwnedGitEntriesPresent = ownedState.OwnedGitEntriesPresent,
            OwnershipManifestPresent = ownedState.OwnershipManifestPresent,
            CredentialCoreSuccess = credentialCoreSuccess,
            GitProtocolPathSuccess = gitProtocolPathSuccess,
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
                    GitCredentialHelperValue,
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

    private static bool ContainsUnexpectedProductOwnedGitConfigState(
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
                && string.Equals(value, ProductId, StringComparison.Ordinal)
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

    private static bool ContainsProductOwnedGitConfigState(string gitConfig)
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
                && string.Equals(value, ProductId, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
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

    private static CredentialCoreService CreateCredentialCoreService() => new();

    private static CredentialRequest CreateGitRequest() =>
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
            InteractivePolicy = InteractivePolicy.UserAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = new CiContext
            {
                ExplicitCiMode = false,
                AllowsPersistentWrites = false,
            },
        };

    private static AdapterDescriptor CreateGitCredentialHelperDescriptor()
    {
        AdapterEntrypointDescriptor protocolEntrypoint = new(
            "GitCredentialHelper",
            AdapterInvocationMode.Protocol,
            executableNames: [ProductId],
            argumentTokens: ["git", "credential-helper"],
            argumentMatchMode: AdapterArgumentMatchMode.Prefix
        );
        AdapterEntrypointDescriptor humanEntrypoint = new(
            "HumanCommand",
            AdapterInvocationMode.HumanCommand,
            executableNames: [ProductId]
        );

        return new AdapterDescriptor(
            "Git Credential Helper",
            AdapterProtocol.GitCredentialHelper,
            [protocolEntrypoint, humanEntrypoint]
        );
    }

    private static (bool Success, bool PayloadCaptured) ExecuteGitProtocolPath(
        CredentialResult credentialResult
    )
    {
        ArgumentNullException.ThrowIfNull(credentialResult);

        string? protocolPayload = CreateGitCredentialHelperProtocolPayload(credentialResult);
        var protocolStdout = new StringWriter();
        AdapterHostExecutionOutcome outcome = AdapterHostExecutor.Execute(
            CreateGitCredentialHelperDescriptor(),
            executablePath: ProductId,
            arguments: ["git", "credential-helper", "get"],
            handler: _ => new AdapterHostHandlerOutput(
                credentialResult: credentialResult,
                protocolStdout: protocolPayload
            ),
            protocolStdout: protocolStdout,
            humanStdout: TextWriter.Null,
            diagnosticRouter: new DiagnosticRouter([], SecretRedactor.Empty)
        );

        string capturedPayload = protocolStdout.ToString();
        bool payloadCaptured = capturedPayload.Length != 0;
        bool success = outcome.Result.ExitCode == AdapterHostExitCode.Success
            && outcome.Result.WriteProtocolStdout
            && payloadCaptured;

        return (success, payloadCaptured);
    }

    private static string? CreateGitCredentialHelperProtocolPayload(
        CredentialResult credentialResult
    )
    {
        return AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
            credentialResult,
            out string? username,
            out string? password
        )
            ? $"username={username}\npassword={password}\n"
            : null;
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
            GitCredentialHelperValue
        )
        && HasExpectedManagedManifestEntry(
            manifest,
            sequence: 2,
            GitUseHttpPathKey,
            GitUseHttpPathValue
        );

    private bool HasExpectedManagedManifestEntry(
        ConfigurationOwnershipManifest manifest,
        int sequence,
        string expectedKey,
        string expectedValue
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedValue);

        ConfigurationOwnershipManifestEntry[] matchingEntries = manifest
            .Entries.Where(entry => MatchesManagedManifestEntry(entry, expectedKey))
            .ToArray();

        return matchingEntries.Length == 1
            && matchingEntries[0].Sequence == sequence
            && matchingEntries[0].Operation == ConfigurationChangeOperation.Set
            && matchingEntries[0].PreserveDeclarationsAndComments
            && matchingEntries[0].HasPlannedValue
            && !matchingEntries[0].IsSecretValue
            && string.Equals(
                matchingEntries[0].PlannedValueSha256,
                ComputeSha256(expectedValue),
                StringComparison.Ordinal
            );
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
        if (IsUnsafeReparsePoint(path))
        {
            return false;
        }

        foreach (string directory in EnumerateParentDirectories(path))
        {
            if (fileSystem.DirectoryExists(directory) && IsUnsafeReparsePoint(directory))
            {
                return false;
            }
        }

        try
        {
            _ = fileSystem.GetFileLength(path);
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }

        return true;
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
            options.StateDirectoryPath
                ?? Path.Combine(Path.GetTempPath(), ProductId, "phase8")
        );
        string gitConfigPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "git", "user.gitconfig")
        );
        string ownershipManifestPath = GetFullPath(
            Path.Combine(stateDirectoryPath, "manifests", "git-ownership-manifest.json")
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
        };
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
            or System.Text.Json.JsonException;

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
