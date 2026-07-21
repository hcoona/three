namespace Hcoona.AzureAuth.CredProvider.Contracts;

public enum CredentialEcosystem
{
    Unspecified = 0,
    Git = 1,
    NuGet = 2,
    Python = 3,
    Npm = 4,
    Pnpm = 5,
    Yarn = 6,
}

public enum CredentialOperation
{
    Unspecified = 0,
    Get = 1,
    Store = 2,
    Erase = 3,
    Refresh = 4,
    Configure = 5,
    Doctor = 6,
}

public enum TokenAudience
{
    Unspecified = 0,
    AzureDevOps = 1,
    AzureArtifacts = 2,
}

public enum CredentialKind
{
    Unspecified = 0,
    BasicPassword = 1,
    BearerToken = 2,
    NpmAuthToken = 3,
    NuGetPluginCredential = 4,
    PatCompatibility = 5,
}

public enum IdentityFlow
{
    Unspecified = 0,
    InteractiveBrowser = 1,
    DeviceCode = 2,
    PatCompatibility = 3,
    AzurePipelinesSystemAccessToken = 4,
    ServicePrincipal = 5,
    ManagedIdentity = 6,
    WorkloadIdentityFederation = 7,
}

public enum IdentityFlowState
{
    Unspecified = 0,
    AcceptedMvp = 1,
    Deferred = 2,
    Disabled = 3,
    Unsupported = 4,
}

public enum InteractivePolicy
{
    Unspecified = 0,
    Never = 1,
    HostToolAllows = 2,
    UserAllowed = 3,
}

/// <summary>
/// Separates credential acquisition interaction mode from identity flow selection.
/// Introduced in v2 of the credential acquisition contract.
/// </summary>
public enum AcquisitionMode
{
    /// <summary>
    /// Explicit v2 bridge value that preserves v1 acquisition semantics:
    /// acquisition remains governed entirely by IdentityFlow and InteractivePolicy.
    /// V1 itself does not carry an AcquisitionMode field.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Categorically forbids any browser launch, device-code prompt, broker UI,
    /// account-selection UI, IWA round-trip, or any other form of active
    /// authentication that requires human attention or network interaction
    /// beyond a cache or broker lookup.
    /// A cache miss will map to InteractionRequired rather than falling back.
    /// Valid only for operation Get with InteractivePolicy Never and no explicit
    /// CI or opaque-token context. Current providers fail closed until a
    /// source-proved cache or broker lookup path exists.
    /// </summary>
    SilentOnly = 1,

    /// <summary>
    /// Permits explicit accepted human-facing flows (interactive browser,
    /// device code) when InteractivePolicy also allows them.
    /// Valid only for operation Get when InteractivePolicy is HostToolAllows
    /// or UserAllowed.
    /// Not valid in CI-mode or non-interactive routing.
    /// </summary>
    InteractionAllowed = 2,
}

public enum CachePolicyMode
{
    Unspecified = 0,
    NoCache = 1,
    ProductPersistentCacheDisabled = 2,
    NonPersistentCi = 3,
    FuturePersistentCacheRequested = 4,
}

public enum CredentialResultStatus
{
    Unspecified = 0,
    Success = 1,
    NoCredential = 2,
    InteractionRequired = 3,
    InteractionBlocked = 4,
    Unauthorized = 5,
    CredentialUnavailable = 6,
    FlowDeferred = 7,
    FlowDisabled = 8,
    UnsupportedFlow = 9,
    CacheUnavailable = 10,
    Fatal = 11,
    IntegrityFailure = 12,
    ProtocolViolation = 13,
}

public enum CredentialErrorKind
{
    Unspecified = 0,
    UnsupportedHost = 1,
    UnsupportedFlow = 2,
    FlowDeferred = 3,
    FlowDisabled = 4,
    InteractionRequired = 5,
    InteractionBlocked = 6,
    CredentialUnavailable = 7,
    Unauthorized = 8,
    CacheUnavailable = 9,
    PolicyViolation = 10,
    IntegrityFailure = 11,
    ProtocolViolation = 12,
    Fatal = 13,
}

public enum AdapterProtocol
{
    Unspecified = 0,
    GitCredentialHelper = 1,
    NuGetPlugin = 2,
    PythonKeyringBackend = 3,
    KeyringHelper = 4,
    NpmConfiguration = 5,
}

public enum AdapterHostExitCode
{
    Success = 0,
    NoCredential = 1,
    InteractionRequired = 2,
    Unauthorized = 3,
    ConfigurationError = 64,
    IntegrityFailure = 65,
    CacheUnavailable = 69,
    Fatal = 70,
}

public enum ConfigurationChangeOperation
{
    Unspecified = 0,
    Set = 1,
    Remove = 2,
    EnsureFile = 3,
    InstallAdapter = 4,
    RemoveAdapter = 5,
    Create = 6,
    Update = 7,
    Refresh = 8,
}

public enum ConfigurationTargetKind
{
    Unspecified = 0,
    GitConfig = 1,
    NuGetPluginLayout = 2,
    PythonKeyringBackend = 3,
    KeyringShim = 4,
    Npmrc = 5,
    Yarnrc = 6,
    CiTemporaryFile = 7,
}

public enum ConfigurationScope
{
    Unspecified = 0,
    User = 1,
    WorkspaceReadOnly = 2,
    ExplicitPath = 3,
    CiTemporary = 4,
    Global = 5,
}

public enum ConfigurationAtomicityPolicy
{
    Unspecified = 0,
    AtomicChangeSetRequired = 1,
}

public enum ConfigurationRollbackPolicy
{
    Unspecified = 0,
    Required = 1,
}

public enum ConfigurationPlanState
{
    Unspecified = 0,
    Planned = 1,
    Applied = 2,
    RolledBack = 3,
    Failed = 4,
}

public enum ConfigurationManifestCommitPolicy
{
    Unspecified = 0,
    CommitAfterDurableChanges = 1,
}

public enum ConfigurationDeclarationPreservation
{
    Unspecified = 0,
    NotApplicable = 1,
    AuthOnlyWhenDeclarationsRemainVisible = 2,
    CopyHiddenDeclarationsToTemporaryConfig = 3,
    CompleteMergedTemporaryConfig = 4,
}

public enum ConfigurationTemporaryContainerKind
{
    Unspecified = 0,
    None = 1,
    NpmrcFile = 2,
    TemporaryHome = 3,
    YarnRcFile = 4,
}

public enum DoctorCheckStatus
{
    Unspecified = 0,
    Pass = 1,
    Warning = 2,
    Fail = 3,
    Skipped = 4,
    Unsupported = 5,
    Deferred = 6,
    NotApplicable = 7,
}

public enum DoctorCheckSeverity
{
    Unspecified = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

public enum KeyringHelperMode
{
    Unspecified = 0,
    Password = 1,
    Credentials = 2,
}

public enum KeyringHelperIntegrityPlatform
{
    Unspecified = 0,
    Linux = 1,
    Windows = 2,
    MacOs = 3,
}

public enum KeyringOwnerValidationRequirement
{
    Unspecified = 0,
    Required = 1,
    DeferredNotAvailable = 2,
}

public enum KeyringSymlinkPolicy
{
    Unspecified = 0,
    RejectSymlinks = 1,
    BestEffortRejectSymlinks = 2,
}

public enum KeyringDigestPolicy
{
    Unspecified = 0,
    Sha256Required = 1,
    Sha256RequiredWeakPath = 2,
}

public enum ContractBreakingChangeKind
{
    Unspecified = 0,
    RemoveField = 1,
    RenameField = 2,
    ChangeFieldType = 3,
    ChangeFieldRequiredness = 4,
    ChangeFieldMeaning = 5,
    ChangeEnumRepresentation = 6,
    ChangeProtocolStdout = 7,
    ChangeProtocolStderr = 8,
    ChangeProtocolExitCode = 9,
    WeakenSecurityPolicy = 10,
    WeakenCachePartitioning = 11,
    AllowPlaintextSecretDiagnostics = 12,
    AddSilentPatFallback = 13,
    MakeIntegrityCheckOptional = 14,
}
