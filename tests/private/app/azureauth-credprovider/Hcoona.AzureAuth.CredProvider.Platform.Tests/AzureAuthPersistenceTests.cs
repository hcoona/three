using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthPersistenceTests
{
    [Fact]
    public void ProviderConfigSupportsCreateReadReplaceAndMalformedRepair()
    {
        const string Name = "azureauth/provider-config.json";
        var store = new InMemoryRecordStore();
        var persistence = new AzureAuthProviderConfigPersistence(store);

        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> created = persistence.Create(
            Name,
            AzureAuthProviderConfig.CreateDirectMsal()
        );
        AzureAuthPersistedRecord<AzureAuthProviderConfig> read = persistence.Read(Name);
        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> replaced = persistence.Replace(
            read,
            AzureAuthProviderConfig.CreateAzureAuth()
        );
        store.Put(Name, "{");
        AzureAuthPersistedRecord<AzureAuthProviderConfig> malformed = persistence.Read(Name);
        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> repaired = persistence.Repair(
            malformed,
            AzureAuthProviderConfig.CreateDirectMsal()
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, created.Status);
        Assert.Equal(AzureAuthProviderSelection.DirectMsal, read.Value!.Selection);
        Assert.Equal(AzureAuthProviderSelection.AzureAuth, replaced.Record!.Value!.Selection);
        Assert.Equal(AzureAuthPersistedRecordStatus.Malformed, malformed.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, repaired.Status);
    }

    [Fact]
    public void StaleWritesConflict()
    {
        const string Name = "azureauth/provider-config.json";
        var store = new InMemoryRecordStore();
        var persistence = new AzureAuthProviderConfigPersistence(store);
        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> created = persistence.Create(
            Name,
            AzureAuthProviderConfig.CreateDirectMsal()
        );
        AzureAuthPersistedRecord<AzureAuthProviderConfig> stale = created.Record! with { };
        _ = persistence.Replace(created.Record!, AzureAuthProviderConfig.CreateAzureAuth());

        AzureAuthPersistedWriteResult<AzureAuthProviderConfig> conflict = persistence.Replace(
            stale,
            AzureAuthProviderConfig.CreateDirectMsal()
        );

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, conflict.Status);
    }

    [Fact]
    public void BindingPersistenceSupportsOptionalAccountAndMalformedRecovery()
    {
        const string Name = "azureauth/account-binding.json";
        var store = new InMemoryRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthPersistedWriteResult<AzureAuthBinding> bound = persistence.Bind(
            persistence.Read(Name),
            AzureAuthProviderConfig.CreateAzureAuth(),
            null,
            "tenant",
            DateTimeOffset.UtcNow
        );
        store.Put(Name, "{");
        AzureAuthPersistedRecord<AzureAuthBinding> malformed = persistence.Read(Name);
        AzureAuthPersistedWriteResult<AzureAuthBinding> repaired = persistence.Rebind(
            malformed,
            AzureAuthProviderConfig.CreateAzureAuth(),
            "user@example.com",
            "tenant",
            DateTimeOffset.UtcNow
        );

        Assert.Null(bound.Record!.Value!.AccountId);
        Assert.Equal(AzureAuthPersistedRecordStatus.Malformed, malformed.Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, repaired.Status);
    }

    [Fact]
    public void UnbindDeletesTheBindingWithConflictDetection()
    {
        const string Name = "azureauth/account-binding.json";
        var store = new InMemoryRecordStore();
        var persistence = new AzureAuthBindingPersistence(store);
        AzureAuthPersistedWriteResult<AzureAuthBinding> bound = persistence.Bind(
            persistence.Read(Name),
            AzureAuthProviderConfig.CreateAzureAuth(),
            null,
            "tenant",
            DateTimeOffset.UtcNow
        );
        AzureAuthPersistedRecord<AzureAuthBinding> stale = bound.Record! with { };
        AzureAuthSecureRecordWriteResult deleted = persistence.Unbind(bound.Record!);
        AzureAuthSecureRecordWriteResult conflict = persistence.Unbind(stale);

        Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, deleted.Status);
        Assert.Equal(AzureAuthPersistedRecordStatus.Missing, persistence.Read(Name).Status);
        Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, conflict.Status);
    }

    [Fact]
    public void OnlyFixedRecordNamesAreAccepted()
    {
        var persistence = new AzureAuthProviderConfigPersistence(new InMemoryRecordStore());

        Assert.Throws<ArgumentException>(() => persistence.Read("../provider.json"));
    }

    private sealed class InMemoryRecordStore : IAzureAuthSecureRecordStore
    {
        private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

        public AzureAuthSecureRecordReadResult Read(string path) =>
            entries.TryGetValue(path, out byte[]? content)
                ? AzureAuthSecureRecordReadResult.Present(Revision(content), content)
                : AzureAuthSecureRecordReadResult.Missing();

        public AzureAuthSecureRecordWriteResult CompareExchange(
            string path,
            string expectedRevision,
            ReadOnlyMemory<byte> newContent
        )
        {
            AzureAuthSecureRecordReadResult current = Read(path);
            bool matches =
                current.Status == AzureAuthSecureRecordReadStatus.Missing
                    ? expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision
                    : string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal);
            if (!matches)
            {
                return AzureAuthSecureRecordWriteResult.Conflict();
            }

            byte[] content = newContent.ToArray();
            entries[path] = content;
            return AzureAuthSecureRecordWriteResult.Success(Revision(content));
        }

        public AzureAuthSecureRecordWriteResult CompareDelete(string path, string expectedRevision)
        {
            AzureAuthSecureRecordReadResult current = Read(path);
            bool matches =
                current.Status == AzureAuthSecureRecordReadStatus.Missing
                    ? expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision
                    : string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal);
            if (!matches)
            {
                return AzureAuthSecureRecordWriteResult.Conflict();
            }

            entries.Remove(path);
            return AzureAuthSecureRecordWriteResult.Success(
                AzureAuthSecureRecordStoreContract.MissingRevision
            );
        }

        public void Put(string path, string content) =>
            entries[path] = Encoding.UTF8.GetBytes(content);

        private static string Revision(ReadOnlySpan<byte> content) =>
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}
