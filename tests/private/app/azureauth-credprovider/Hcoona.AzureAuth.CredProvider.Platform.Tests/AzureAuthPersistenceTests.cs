using System.Text;
using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthPersistenceTests
{
    [Fact]
    public void ProviderConfigPersistenceSupportsCreateReplaceReadAndRepair()
    {
        const string recordName = "azureauth/provider-config.json";
        var store = new InMemorySecureRecordStore();
        var persistence = new AzureAuthProviderConfigPersistence(store);
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDefault();
        AzureAuthProviderConfig azureAuth = CreateAzureAuthConfig();

        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> created = persistence.Create(
            recordName,
            directMsal
        );
        AzureAuthPersistedRecord<AzureAuthProviderConfig> read = persistence.Read(recordName);
        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> replaced = persistence.Replace(
            created.Record!,
            azureAuth
        );

        store.PutText(recordName, "r99", "{");
        AzureAuthPersistedRecord<AzureAuthProviderConfig> malformed = persistence.Read(recordName);
        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> repaired = persistence.Repair(
            malformed,
            directMsal
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, created.Status);
        Assert.Equal(AzureAuthPersistedRecordStatus.Present, read.Status);
        Assert.Equal(directMsal, read.Value);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, replaced.Status);
        Assert.Equal(azureAuth, replaced.Record!.Value);
        Assert.Equal(AzureAuthPersistedRecordStatus.Malformed, malformed.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, repaired.Status);
        Assert.Equal(directMsal, repaired.Record!.Value);
    }

    [Fact]
    public void BindingPersistenceSupportsBindRebindUnbindAndDeterministicConflicts()
    {
        const string recordName = "azureauth/binding.json";
        var store = new InMemorySecureRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDefault();
        AzureAuthProviderConfig azureAuth = CreateAzureAuthConfig();
        AzureAuthTrustResult trust = CreateTrustedResult(azureAuth.DeploymentConfig!);

        AzureAuthPersistedRecord<AzureAuthBinding> missing = persistence.Read(recordName);
        AzureAuthPersistedWriteResult<AzureAuthBinding> bound = persistence.Bind(
            missing,
            directMsal,
            "User@Example.com",
            "Tenant-One",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );
        AzureAuthPersistedWriteResult<AzureAuthBinding> idempotent = persistence.Bind(
            bound.Record!,
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero)
        );
        AzureAuthPersistedWriteResult<AzureAuthBinding> stale = persistence.Bind(
            missing,
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero)
        );
        AzureAuthPersistedWriteResult<AzureAuthBinding> rebound = persistence.Rebind(
            idempotent.Record!,
            azureAuth,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 3, 0, 0, TimeSpan.Zero),
            trust
        );
        AzureAuthPersistedWriteResult<AzureAuthBinding> unbound = persistence.Unbind(
            rebound.Record!,
            new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, bound.Status);
        Assert.Equal("user@example.com", bound.Record!.Value!.AccountId);
        Assert.Equal("tenant-one", bound.Record.Value.TenantId);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, idempotent.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, stale.Status);
        Assert.Equal(AzureAuthProviderSelection.AzureAuth, rebound.Record!.Value!.ProviderSelection);
        Assert.Equal(trust.DeploymentKey, rebound.Record.Value.DeploymentKey);
        Assert.Equal(AzureAuthBindingState.Unbound, unbound.Record!.Value!.State);
    }

    [Fact]
    public void BindingPersistenceSkipsWriteChurnForNoOpsButKeepsCasForRealChanges()
    {
        const string recordName = "azureauth/binding.json";
        var store = new InMemorySecureRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDefault();
        AzureAuthProviderConfig azureAuth = CreateAzureAuthConfig();
        AzureAuthTrustResult trust = CreateTrustedResult(azureAuth.DeploymentConfig!);

        AzureAuthPersistedWriteResult<AzureAuthBinding> bound = persistence.Bind(
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing(recordName),
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );
        int writeCountAfterBound = store.WriteCount;
        AzureAuthPersistedRecord<AzureAuthBinding> boundRecord = bound.Record!;
        string boundRevision = boundRecord.Revision!;
        DateTimeOffset boundRecordedAt = boundRecord.Value!.RecordedAtUtc;
        AzureAuthPersistedRecord<AzureAuthBinding> staleBoundRecord = boundRecord with { };

        AzureAuthPersistedWriteResult<AzureAuthBinding> noOpBind = persistence.Bind(
            boundRecord,
            directMsal,
            "USER@EXAMPLE.COM",
            "TENANT-ONE",
            boundRecordedAt.AddHours(1)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, noOpBind.Status);
        Assert.Same(boundRecord, noOpBind.Record);
        Assert.Equal(writeCountAfterBound, store.WriteCount);
        Assert.Equal(boundRevision, noOpBind.Record!.Revision);
        Assert.Equal(boundRecordedAt, noOpBind.Record.Value!.RecordedAtUtc);

        AzureAuthPersistedRecord<AzureAuthBinding> currentBoundRecord = noOpBind.Record!;
        AzureAuthPersistedWriteResult<AzureAuthBinding> rebound = persistence.Rebind(
            currentBoundRecord,
            azureAuth,
            "user@example.com",
            "tenant-one",
            boundRecordedAt.AddHours(2),
            trust
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, rebound.Status);
        Assert.True(store.WriteCount > writeCountAfterBound);
        Assert.NotEqual(boundRevision, rebound.Record!.Revision);

        AzureAuthPersistedWriteResult<AzureAuthBinding> staleActualChange = persistence.Rebind(
            staleBoundRecord,
            directMsal,
            "other@example.com",
            "tenant-two",
            boundRecordedAt.AddHours(3)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, staleActualChange.Status);

        AzureAuthPersistedRecord<AzureAuthBinding> reboundRecord = rebound.Record!;
        AzureAuthPersistedWriteResult<AzureAuthBinding> unbound = persistence.Unbind(
            reboundRecord,
            boundRecordedAt.AddHours(4)
        );
        int writeCountAfterUnbind = store.WriteCount;
        AzureAuthPersistedRecord<AzureAuthBinding> unboundRecord = unbound.Record!;
        string unboundRevision = unboundRecord.Revision!;
        DateTimeOffset unboundRecordedAt = unboundRecord.Value!.RecordedAtUtc;
        AzureAuthPersistedWriteResult<AzureAuthBinding> noOpUnbind = persistence.Unbind(
            unboundRecord,
            boundRecordedAt.AddHours(5)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, noOpUnbind.Status);
        Assert.Same(unboundRecord, noOpUnbind.Record);
        Assert.Equal(writeCountAfterUnbind, store.WriteCount);
        Assert.Equal(unboundRevision, noOpUnbind.Record!.Revision);
        Assert.Equal(unboundRecordedAt, noOpUnbind.Record.Value!.RecordedAtUtc);
    }

    [Fact]
    public void BindingPersistenceNoOpBindConflictsWhenSnapshotRevisionIsStale()
    {
        const string recordName = "azureauth/binding.json";
        var store = new InMemorySecureRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDefault();
        AzureAuthProviderConfig azureAuth = CreateAzureAuthConfig();
        AzureAuthTrustResult trust = CreateTrustedResult(azureAuth.DeploymentConfig!);

        AzureAuthPersistedWriteResult<AzureAuthBinding> bound = persistence.Bind(
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing(recordName),
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );
        AzureAuthPersistedRecord<AzureAuthBinding> staleBoundRecord = bound.Record! with { };
        AzureAuthPersistedWriteResult<AzureAuthBinding> rebound = persistence.Rebind(
            bound.Record!,
            azureAuth,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero),
            trust
        );
        int writeCountAfterRebind = store.WriteCount;

        AzureAuthPersistedWriteResult<AzureAuthBinding> staleNoOp = persistence.Bind(
            staleBoundRecord,
            directMsal,
            "USER@EXAMPLE.COM",
            "TENANT-ONE",
            new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, rebound.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, staleNoOp.Status);
        Assert.Equal(writeCountAfterRebind, store.WriteCount);
    }

    [Fact]
    public void BindingPersistenceNoOpUnbindConflictsWhenSnapshotRevisionIsStale()
    {
        const string recordName = "azureauth/binding.json";
        var store = new InMemorySecureRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDefault();

        AzureAuthPersistedWriteResult<AzureAuthBinding> bound = persistence.Bind(
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing(recordName),
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );
        AzureAuthPersistedWriteResult<AzureAuthBinding> unbound = persistence.Unbind(
            bound.Record!,
            new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero)
        );
        AzureAuthPersistedRecord<AzureAuthBinding> staleUnboundRecord = unbound.Record! with { };
        AzureAuthPersistedWriteResult<AzureAuthBinding> rebound = persistence.Rebind(
            unbound.Record!,
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero)
        );
        int writeCountAfterRebind = store.WriteCount;

        AzureAuthPersistedWriteResult<AzureAuthBinding> staleNoOp = persistence.Unbind(
            staleUnboundRecord,
            new DateTimeOffset(2026, 7, 20, 3, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, rebound.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, staleNoOp.Status);
        Assert.Equal(writeCountAfterRebind, store.WriteCount);
    }

    [Fact]
    public void BindingPersistenceRepairsMalformedBytesOnlyThroughRebindOrUnbind()
    {
        const string recordName = "azureauth/binding.json";
        var store = new InMemorySecureRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDefault();

        store.PutText(recordName, "r1", "{");
        AzureAuthPersistedRecord<AzureAuthBinding> malformed = persistence.Read(recordName);

        Assert.Equal(AzureAuthPersistedRecordStatus.Malformed, malformed.Status);
        Assert.Throws<InvalidOperationException>(() =>
            persistence.Bind(
                malformed,
                directMsal,
                "user@example.com",
                "tenant-one",
                new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
            )
        );

        AzureAuthPersistedWriteResult<AzureAuthBinding> rebound = persistence.Rebind(
            malformed,
            directMsal,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero)
        );

        store.PutText(recordName, rebound.Record!.Revision!, "{");
        AzureAuthPersistedRecord<AzureAuthBinding> malformedAgain = persistence.Read(recordName);
        AzureAuthPersistedWriteResult<AzureAuthBinding> unbound = persistence.Unbind(
            malformedAgain,
            new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, rebound.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, unbound.Status);
        Assert.Equal(AzureAuthBindingState.Unbound, unbound.Record!.Value!.State);
    }

    [Fact]
    public void PersistedContractsContainNoTokenFieldsAndBlockDirectSerializerUse()
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateDefault(),
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );
        string json = AzureAuthBindingJson.Serialize(binding);

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize(binding, PlatformPersistedJsonContext.Default.AzureAuthBinding)
        );
        Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Deserialize(json, PlatformPersistedJsonContext.Default.AzureAuthBinding)
        );
    }

    [Fact]
    public void PersistenceValidatesRecordNamesAndSurfacesUnsupportedAndUnsafeStores()
    {
        var providerPersistence = new AzureAuthProviderConfigPersistence();
        var unsafeBindingPersistence = new AzureAuthBindingPersistence(new UnsafeSecureRecordStore());
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateDefault();

        Assert.Throws<ArgumentException>(() => providerPersistence.Read("../provider.json"));
        Assert.Throws<ArgumentException>(() => providerPersistence.Read("AzureAuth/provider.json"));

        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> unsupportedCreate =
            providerPersistence.Create("azureauth/provider-config.json", config);
        AzureAuthPersistedRecord<AzureAuthProviderConfig> unsupportedRead = providerPersistence.Read(
            "azureauth/provider-config.json"
        );
        AzureAuthPersistedRecord<AzureAuthBinding> unsafeRead = unsafeBindingPersistence.Read(
            "azureauth/binding.json"
        );
        AzureAuthPersistedWriteResult<AzureAuthBinding> unsafeWrite = unsafeBindingPersistence.Unbind(
            unsafeRead,
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Unsupported, unsupportedCreate.Status);
        Assert.Equal(AzureAuthPersistedRecordStatus.Unsupported, unsupportedRead.Status);
        Assert.Equal(AzureAuthPersistedRecordStatus.Unsafe, unsafeRead.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Unsafe, unsafeWrite.Status);
    }

    [Fact]
    public void PersistenceWriteRejectsSuccessfulMutationsThatReuseExpectedRevision()
    {
        const string recordName = "azureauth/provider-config.json";
        var persistence = new AzureAuthProviderConfigPersistence(
            new SameRevisionOnSuccessSecureRecordStore()
        );
        AzureAuthProviderConfig current = AzureAuthProviderConfig.CreateDefault();
        AzureAuthPersistedRecord<AzureAuthProviderConfig> expected =
            AzureAuthPersistedRecord<AzureAuthProviderConfig>.Present(recordName, "r7", current);

        Assert.Throws<InvalidOperationException>(() =>
            persistence.Replace(expected, CreateAzureAuthConfig())
        );
    }

    private static AzureAuthProviderConfig CreateAzureAuthConfig() =>
        AzureAuthProviderConfig.CreateAzureAuth(
            new AzureAuthDeploymentConfig
            {
                SchemaVersion = ContractVersions.AzureAuthDeploymentConfigSchemaMajor,
                ExecutablePath = @"C:\Tools\AzureAuth.exe",
                ExecutableSha256 = new string('a', 64),
                SignerIdentity = "CN=AzureAuth, O=Hcoona, C=US",
                PublisherName = "Hcoona AzureAuth",
                ExecutableVersion = "1.0.0.0",
                ProvenanceIdentifier = "foundation/wp2",
            }
        );

    private static AzureAuthTrustResult CreateTrustedResult(AzureAuthDeploymentConfig deploymentConfig) =>
        AzureAuthTrustPolicy.Evaluate(
            deploymentConfig,
            AzureAuthArtifactInspection.Trusted(
                new AzureAuthArtifactEvidence
                {
                    CanonicalPath = deploymentConfig.ExecutablePath,
                    StableArtifactIdentity = new FileSystemEntryIdentity("artifact-1"),
                    Sha256Hash = deploymentConfig.ExecutableSha256,
                    SignerIdentity = deploymentConfig.SignerIdentity,
                    PublisherName = deploymentConfig.PublisherName,
                    ExecutableVersion = deploymentConfig.ExecutableVersion,
                    ProvenanceIdentifier = deploymentConfig.ProvenanceIdentifier,
                    Owner = new FileSystemOwner("current-user"),
                    CurrentUserOwnsArtifact = true,
                    OwnerOnlyWritable = true,
                    TrustedWorkingDirectory = @"C:\ProgramData\AzureAuth",
                    TrustedPathEntries = [@"C:\Windows\System32", @"C:\Windows"],
                }
            )
        );

    private sealed class InMemorySecureRecordStore : IAzureAuthSecureRecordStore
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private int _revisionCounter;

        public int WriteCount { get; private set; }

        public AzureAuthSecureRecordReadResult Read(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return _entries.TryGetValue(path, out Entry? entry)
                ? AzureAuthSecureRecordReadResult.Present(entry.Revision, entry.Content)
                : AzureAuthSecureRecordReadResult.Missing();
        }

        public AzureAuthSecureRecordRevisionCheckResult CompareRevision(
            string path,
            string expectedRevision
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);

            bool exists = _entries.TryGetValue(path, out Entry? current);
            if (expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision)
            {
                return exists
                    ? AzureAuthSecureRecordRevisionCheckResult.Conflict()
                    : AzureAuthSecureRecordRevisionCheckResult.Match();
            }

            return exists
                && string.Equals(current!.Revision, expectedRevision, StringComparison.Ordinal)
                ? AzureAuthSecureRecordRevisionCheckResult.Match()
                : AzureAuthSecureRecordRevisionCheckResult.Conflict();
        }

        public AzureAuthSecureRecordWriteResult CompareExchange(
            string path,
            string expectedRevision,
            ReadOnlyMemory<byte> newContent
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);

            bool exists = _entries.TryGetValue(path, out Entry? current);
            if (expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision)
            {
                if (exists)
                {
                    return AzureAuthSecureRecordWriteResult.Conflict();
                }
            }
            else if (!exists || !string.Equals(current!.Revision, expectedRevision, StringComparison.Ordinal))
            {
                return AzureAuthSecureRecordWriteResult.Conflict();
            }

            WriteCount++;
            string revision = $"r{++_revisionCounter}";
            _entries[path] = new Entry(revision, newContent.ToArray());
            return AzureAuthSecureRecordWriteResult.Success(revision);
        }

        public void PutText(string path, string revision, string json)
        {
            _entries[path] = new Entry(revision, Encoding.UTF8.GetBytes(json));
            if (
                revision.Length > 1
                && revision[0] == 'r'
                && int.TryParse(revision.AsSpan(1), out int parsedRevision)
            )
            {
                _revisionCounter = Math.Max(_revisionCounter, parsedRevision);
            }
        }

        private sealed record Entry(string Revision, byte[] Content);
    }

    private sealed class UnsafeSecureRecordStore : IAzureAuthSecureRecordStore
    {
        public AzureAuthSecureRecordReadResult Read(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return AzureAuthSecureRecordReadResult.Unsafe();
        }

        public AzureAuthSecureRecordRevisionCheckResult CompareRevision(
            string path,
            string expectedRevision
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
            return AzureAuthSecureRecordRevisionCheckResult.Unsafe();
        }

        public AzureAuthSecureRecordWriteResult CompareExchange(
            string path,
            string expectedRevision,
            ReadOnlyMemory<byte> newContent
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
            return AzureAuthSecureRecordWriteResult.Unsafe();
        }
    }

    private sealed class SameRevisionOnSuccessSecureRecordStore : IAzureAuthSecureRecordStore
    {
        public AzureAuthSecureRecordReadResult Read(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return AzureAuthSecureRecordReadResult.Missing();
        }

        public AzureAuthSecureRecordRevisionCheckResult CompareRevision(
            string path,
            string expectedRevision
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
            return AzureAuthSecureRecordRevisionCheckResult.Match();
        }

        public AzureAuthSecureRecordWriteResult CompareExchange(
            string path,
            string expectedRevision,
            ReadOnlyMemory<byte> newContent
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
            return AzureAuthSecureRecordWriteResult.Success(expectedRevision);
        }
    }
}
