using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CredentialProviderIdentityConfigurationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 18, 8, 47, TimeSpan.FromHours(2));

    [Fact]
    public void ConfigureCleanStateCreatesPinnedConfigAndNormalizedBindingAtFixedUtc()
    {
        var store = new InMemoryRecordStore();
        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        CredentialProviderIdentityConfigurationResult result = service.Configure(
            " Tenant-One ",
            " User@Example.COM "
        );

        AzureAuthPersistedRecord<AzureAuthProviderConfig> config =
            new AzureAuthProviderConfigPersistence(store).Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> binding = new AzureAuthBindingPersistence(
            store
        ).Read(CredentialProviderCompositionRoot.BindingRecordName);
        Assert.Equal(CredentialProviderIdentityConfigurationAction.Configure, result.Action);
        Assert.True(result.IsConfigured);
        Assert.True(result.Changed);
        Assert.Equal("Tenant-One", result.TenantId);
        Assert.Equal("User@Example.COM", result.AccountPreference);
        Assert.Equal(AzureAuthProviderSelection.AzureAuth, config.Value!.Selection);
        Assert.Equal(
            AzureAuthProviderConfig.SupportedAzureAuthVersion,
            config.Value.AzureAuthVersion
        );
        Assert.Equal(Now.ToUniversalTime(), binding.Value!.RecordedAtUtc);
        Assert.Equal(TimeSpan.Zero, binding.Value.RecordedAtUtc.Offset);
        Assert.Equal(2, store.CompareExchangeCount);
    }

    [Fact]
    public void ConfigureIdenticalStateIsIdempotentAndPreservesTimestampAndRevisions()
    {
        var store = new InMemoryRecordStore();
        var timeProvider = new MutableTimeProvider(Now);
        var service = new CredentialProviderIdentityConfigurationService(store, timeProvider);
        _ = service.Configure("Tenant-One", "User@Example.COM");
        AzureAuthPersistedRecord<AzureAuthProviderConfig> originalConfig =
            new AzureAuthProviderConfigPersistence(store).Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> originalBinding =
            new AzureAuthBindingPersistence(store).Read(
                CredentialProviderCompositionRoot.BindingRecordName
            );
        timeProvider.Now = Now.AddDays(1);

        CredentialProviderIdentityConfigurationResult result = service.Configure(
            "tenant-one",
            "user@example.com"
        );

        AzureAuthPersistedRecord<AzureAuthProviderConfig> currentConfig =
            new AzureAuthProviderConfigPersistence(store).Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> currentBinding = new AzureAuthBindingPersistence(
            store
        ).Read(CredentialProviderCompositionRoot.BindingRecordName);
        Assert.True(result.IsConfigured);
        Assert.False(result.Changed);
        Assert.Equal("Tenant-One", result.TenantId);
        Assert.Equal("User@Example.COM", result.AccountPreference);
        Assert.Equal(originalConfig.Revision, currentConfig.Revision);
        Assert.Equal(originalBinding.Revision, currentBinding.Revision);
        Assert.Equal(originalBinding.Value!.RecordedAtUtc, currentBinding.Value!.RecordedAtUtc);
        Assert.Equal(2, store.CompareExchangeCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureCreatesOnlyMissingRecordAndPreservesExistingRecord(bool providerMissing)
    {
        var store = new InMemoryRecordStore();
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "User@Example.COM",
            "Tenant-One",
            Now.AddDays(-1)
        );
        if (providerMissing)
        {
            store.Put(
                CredentialProviderCompositionRoot.BindingRecordName,
                AzureAuthBindingJson.Serialize(binding)
            );
        }
        else
        {
            store.Put(
                CredentialProviderCompositionRoot.ProviderConfigRecordName,
                AzureAuthProviderConfigJson.Serialize(config)
            );
        }

        string? originalRevision = store
            .Read(
                providerMissing
                    ? CredentialProviderCompositionRoot.BindingRecordName
                    : CredentialProviderCompositionRoot.ProviderConfigRecordName
            )
            .Revision;
        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        CredentialProviderIdentityConfigurationResult result = service.Configure(
            "tenant-one",
            "user@example.com"
        );

        Assert.True(result.IsConfigured);
        Assert.True(result.Changed);
        Assert.Equal(1, store.CompareExchangeCount);
        if (providerMissing)
        {
            AzureAuthPersistedRecord<AzureAuthBinding> currentBinding =
                new AzureAuthBindingPersistence(store).Read(
                    CredentialProviderCompositionRoot.BindingRecordName
                );
            Assert.Equal(originalRevision, currentBinding.Revision);
            Assert.Equal(binding.RecordedAtUtc, currentBinding.Value!.RecordedAtUtc);
        }
        else
        {
            Assert.Equal(
                originalRevision,
                new AzureAuthProviderConfigPersistence(store)
                    .Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                    .Revision
            );
        }
    }

    [Fact]
    public void ConfigureExistingDifferentProviderRequiresReconfigureWithoutWrites()
    {
        var store = new InMemoryRecordStore();
        AzureAuthProviderConfig currentConfig = AzureAuthProviderConfig.CreateDirectMsal();
        store.Put(
            CredentialProviderCompositionRoot.ProviderConfigRecordName,
            AzureAuthProviderConfigJson.Serialize(currentConfig)
        );
        store.Put(
            CredentialProviderCompositionRoot.BindingRecordName,
            AzureAuthBindingJson.Serialize(
                AzureAuthBindingPolicy.CreateBound(currentConfig, null, "tenant", Now)
            )
        );
        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.Configure("tenant")
        );

        Assert.Contains("reconfigure", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.CompareExchangeCount);
        Assert.Equal(
            AzureAuthProviderSelection.DirectMsal,
            new AzureAuthProviderConfigPersistence(store)
                .Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                .Value!.Selection
        );
    }

    [Fact]
    public void ConfigureDifferentBindingUsesBindingMismatchExceptionWithoutWrites()
    {
        var store = new InMemoryRecordStore();
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        store.Put(
            CredentialProviderCompositionRoot.BindingRecordName,
            AzureAuthBindingJson.Serialize(
                AzureAuthBindingPolicy.CreateBound(config, "user@example.com", "tenant-one", Now)
            )
        );
        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        Assert.Throws<AzureAuthBindingMismatchException>(() =>
            service.Configure("tenant-two", "user@example.com")
        );

        Assert.Equal(0, store.CompareExchangeCount);
        Assert.Equal(
            AzureAuthPersistedRecordStatus.Missing,
            new AzureAuthProviderConfigPersistence(store)
                .Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                .Status
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureMalformedRecordRequiresReconfigureWithoutWrites(bool providerMalformed)
    {
        var store = new InMemoryRecordStore();
        if (providerMalformed)
        {
            store.Put(CredentialProviderCompositionRoot.ProviderConfigRecordName, "{");
        }
        else
        {
            store.Put(CredentialProviderCompositionRoot.BindingRecordName, "{");
        }

        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.Configure("tenant")
        );

        Assert.Contains("reconfigure", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.CompareExchangeCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReconfigureRepairsOrReplacesProviderAndBinding(bool malformed)
    {
        var store = new InMemoryRecordStore();
        if (malformed)
        {
            store.Put(CredentialProviderCompositionRoot.ProviderConfigRecordName, "{");
            store.Put(CredentialProviderCompositionRoot.BindingRecordName, "{");
        }
        else
        {
            AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDirectMsal();
            store.Put(
                CredentialProviderCompositionRoot.ProviderConfigRecordName,
                AzureAuthProviderConfigJson.Serialize(directMsal)
            );
            store.Put(
                CredentialProviderCompositionRoot.BindingRecordName,
                AzureAuthBindingJson.Serialize(
                    AzureAuthBindingPolicy.CreateBound(
                        directMsal,
                        "old@example.com",
                        "old-tenant",
                        Now.AddDays(-1)
                    )
                )
            );
        }

        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        CredentialProviderIdentityConfigurationResult result = service.Reconfigure(
            " New-Tenant ",
            " New@Example.COM "
        );

        AzureAuthPersistedRecord<AzureAuthProviderConfig> config =
            new AzureAuthProviderConfigPersistence(store).Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> binding = new AzureAuthBindingPersistence(
            store
        ).Read(CredentialProviderCompositionRoot.BindingRecordName);
        Assert.Equal(CredentialProviderIdentityConfigurationAction.Reconfigure, result.Action);
        Assert.True(result.IsConfigured);
        Assert.True(result.Changed);
        Assert.Equal(AzureAuthProviderSelection.AzureAuth, config.Value!.Selection);
        Assert.Equal(
            AzureAuthProviderConfig.SupportedAzureAuthVersion,
            config.Value.AzureAuthVersion
        );
        Assert.Equal("New-Tenant", binding.Value!.TenantId);
        Assert.Equal("New@Example.COM", binding.Value.AccountId);
        Assert.Equal(Now.ToUniversalTime(), binding.Value.RecordedAtUtc);
        Assert.Equal(2, store.CompareExchangeCount);
    }

    [Theory]
    [InlineData("configured", true)]
    [InlineData("malformed", true)]
    [InlineData("missing", false)]
    public void UnconfigureRemovesConfiguredMalformedOrMissingRecordsIdempotently(
        string initialState,
        bool expectedChanged
    )
    {
        var store = new InMemoryRecordStore();
        if (initialState == "configured")
        {
            AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
            store.Put(
                CredentialProviderCompositionRoot.ProviderConfigRecordName,
                AzureAuthProviderConfigJson.Serialize(config)
            );
            store.Put(
                CredentialProviderCompositionRoot.BindingRecordName,
                AzureAuthBindingJson.Serialize(
                    AzureAuthBindingPolicy.CreateBound(config, null, "tenant", Now)
                )
            );
        }
        else if (initialState == "malformed")
        {
            store.Put(CredentialProviderCompositionRoot.ProviderConfigRecordName, "{");
            store.Put(CredentialProviderCompositionRoot.BindingRecordName, "{");
        }

        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        CredentialProviderIdentityConfigurationResult first = service.Unconfigure();
        CredentialProviderIdentityConfigurationResult second = service.Unconfigure();

        Assert.Equal(CredentialProviderIdentityConfigurationAction.Unconfigure, first.Action);
        Assert.False(first.IsConfigured);
        Assert.Equal(expectedChanged, first.Changed);
        Assert.Null(first.TenantId);
        Assert.Null(first.AccountPreference);
        Assert.False(second.IsConfigured);
        Assert.False(second.Changed);
        Assert.Equal(
            AzureAuthPersistedRecordStatus.Missing,
            new AzureAuthBindingPersistence(store)
                .Read(CredentialProviderCompositionRoot.BindingRecordName)
                .Status
        );
        Assert.Equal(
            AzureAuthPersistedRecordStatus.Missing,
            new AzureAuthProviderConfigPersistence(store)
                .Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                .Status
        );
        Assert.Equal(
            [
                CredentialProviderCompositionRoot.BindingRecordName,
                CredentialProviderCompositionRoot.ProviderConfigRecordName,
                CredentialProviderCompositionRoot.BindingRecordName,
                CredentialProviderCompositionRoot.ProviderConfigRecordName,
            ],
            store.DeleteOrder
        );
    }

    [Fact]
    public void ReconfigureCasConflictIsRetryableAndLeavesSeparateRecordsNonAtomic()
    {
        var store = new InMemoryRecordStore();
        AzureAuthProviderConfig directMsal = AzureAuthProviderConfig.CreateDirectMsal();
        store.Put(
            CredentialProviderCompositionRoot.ProviderConfigRecordName,
            AzureAuthProviderConfigJson.Serialize(directMsal)
        );
        store.Put(
            CredentialProviderCompositionRoot.BindingRecordName,
            AzureAuthBindingJson.Serialize(
                AzureAuthBindingPolicy.CreateBound(
                    directMsal,
                    "old@example.com",
                    "old-tenant",
                    Now.AddDays(-1)
                )
            )
        );
        store.ConflictNextWrite(CredentialProviderCompositionRoot.BindingRecordName);
        var service = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );

        CredentialProviderIdentityConfigurationConflictException exception =
            Assert.Throws<CredentialProviderIdentityConfigurationConflictException>(() =>
                service.Reconfigure("new-tenant", "new@example.com")
            );

        Assert.Contains("Retry", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            AzureAuthProviderSelection.AzureAuth,
            new AzureAuthProviderConfigPersistence(store)
                .Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                .Value!.Selection
        );
        AzureAuthBinding binding = new AzureAuthBindingPersistence(store)
            .Read(CredentialProviderCompositionRoot.BindingRecordName)
            .Value!;
        Assert.Equal(AzureAuthProviderSelection.DirectMsal, binding.ProviderSelection);
        Assert.Equal("old-tenant", binding.TenantId);
    }

    [Fact]
    public async Task ConfigureThenConcurrentUnconfigureSerializesWithoutOrphanRecords()
    {
        var store = new InMemoryRecordStore(pauseAfterFirstMutation: true);
        var configureService = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );
        var unconfigureService = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now.AddMinutes(1))
        );

        Task<CredentialProviderIdentityConfigurationResult> configure = Task.Run(
            () => configureService.Configure("tenant", "user@example.com"),
            TestContext.Current.CancellationToken
        );
        await store.FirstMutationReached.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        Task<CredentialProviderIdentityConfigurationResult> unconfigure = Task.Run(
            unconfigureService.Unconfigure,
            TestContext.Current.CancellationToken
        );

        try
        {
            await store.SecondScopeAttempted.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            Assert.Equal(1, store.ScopeEntryCount);
        }
        finally
        {
            store.ReleaseFirstOperation();
        }

        CredentialProviderIdentityConfigurationResult[] results = await Task.WhenAll(
            configure,
            unconfigure
        );

        Assert.True(results[0].IsConfigured);
        Assert.False(results[1].IsConfigured);
        Assert.Equal(2, store.ScopeEntryCount);
        AssertRecordsAreBothMissing(store);
    }

    [Fact]
    public async Task UnconfigureThenConcurrentConfigureSerializesWithoutOrphanRecords()
    {
        InMemoryRecordStore store = CreateConfiguredInterleavingStore(
            AzureAuthProviderConfig.CreateAzureAuth(),
            "old-tenant",
            "old@example.com",
            Now.AddDays(-1)
        );
        var unconfigureService = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );
        var configureService = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now.AddMinutes(1))
        );

        Task<CredentialProviderIdentityConfigurationResult> unconfigure = Task.Run(
            unconfigureService.Unconfigure,
            TestContext.Current.CancellationToken
        );
        await store.FirstMutationReached.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        Task<CredentialProviderIdentityConfigurationResult> configure = Task.Run(
            () => configureService.Configure("new-tenant", "new@example.com"),
            TestContext.Current.CancellationToken
        );

        try
        {
            await store.SecondScopeAttempted.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            Assert.Equal(1, store.ScopeEntryCount);
        }
        finally
        {
            store.ReleaseFirstOperation();
        }

        CredentialProviderIdentityConfigurationResult[] results = await Task.WhenAll(
            unconfigure,
            configure
        );

        Assert.False(results[0].IsConfigured);
        Assert.True(results[1].IsConfigured);
        Assert.Equal(2, store.ScopeEntryCount);
        AssertRecordsAreConfiguredTogether(
            store,
            AzureAuthProviderSelection.AzureAuth,
            "new-tenant",
            "new@example.com"
        );
    }

    [Fact]
    public async Task ReconfigureThenConcurrentUnconfigureSerializesWithoutOrphanRecords()
    {
        InMemoryRecordStore store = CreateConfiguredInterleavingStore(
            AzureAuthProviderConfig.CreateDirectMsal(),
            "old-tenant",
            "old@example.com",
            Now.AddDays(-1)
        );
        var reconfigureService = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now)
        );
        var unconfigureService = new CredentialProviderIdentityConfigurationService(
            store,
            new MutableTimeProvider(Now.AddMinutes(1))
        );

        Task<CredentialProviderIdentityConfigurationResult> reconfigure = Task.Run(
            () => reconfigureService.Reconfigure("new-tenant", "new@example.com"),
            TestContext.Current.CancellationToken
        );
        await store.FirstMutationReached.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        Task<CredentialProviderIdentityConfigurationResult> unconfigure = Task.Run(
            unconfigureService.Unconfigure,
            TestContext.Current.CancellationToken
        );

        try
        {
            await store.SecondScopeAttempted.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            Assert.Equal(1, store.ScopeEntryCount);
        }
        finally
        {
            store.ReleaseFirstOperation();
        }

        CredentialProviderIdentityConfigurationResult[] results = await Task.WhenAll(
            reconfigure,
            unconfigure
        );

        Assert.True(results[0].IsConfigured);
        Assert.False(results[1].IsConfigured);
        Assert.Equal(2, store.ScopeEntryCount);
        AssertRecordsAreBothMissing(store);
    }

    [Fact]
    public void FreshProductionCompositionRootObservesPersistedConfigurationAndBinding()
    {
        var store = new InMemoryRecordStore();
        var timeProvider = new MutableTimeProvider(Now);
        var discovery = new AvailableInstallationDiscovery();
        var options = new CredentialProviderProductionOptions
        {
            SecureRecordStore = store,
            TimeProvider = timeProvider,
            InstallationDiscovery = discovery,
        };
        var service = new CredentialProviderIdentityConfigurationService(store, timeProvider);

        CredentialProviderIdentityConfigurationResult configured = service.Configure(
            "tenant",
            null
        );
        CredentialProviderCompositionRoot freshRoot =
            CredentialProviderCompositionRoot.CreateProduction(options);

        Assert.True(configured.IsConfigured);
        Assert.True(configured.Changed);
        Assert.Equal(AzureAuthProviderSelection.AzureAuth, freshRoot.ProviderConfig.Selection);
        Assert.Equal(
            AzureAuthProviderConfig.SupportedAzureAuthVersion,
            freshRoot.ProviderConfig.AzureAuthVersion
        );
        Assert.Equal(AzureAuthPersistedRecordStatus.Present, freshRoot.BindingRecord.Status);
        Assert.Equal("tenant", freshRoot.BindingRecord.Value!.TenantId);
        Assert.Null(freshRoot.BindingRecord.Value.AccountId);
        Assert.True(freshRoot.Readiness.Interactive.IsReady);
        Assert.Equal(1, discovery.CallCount);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AvailableInstallationDiscovery : IAzureAuthInstallationDiscovery
    {
        public int CallCount { get; private set; }

        public AzureAuthInstallation Discover(
            AzureAuthProviderConfig config,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            return AzureAuthInstallation.Available(
                @"C:\AzureAuth\azureauth.exe",
                "/mnt/c/AzureAuth/azureauth.exe",
                AzureAuthProviderConfig.SupportedAzureAuthVersion
            );
        }
    }

    private static void AssertRecordsAreBothMissing(IAzureAuthSecureRecordStore store)
    {
        Assert.Equal(
            AzureAuthPersistedRecordStatus.Missing,
            new AzureAuthProviderConfigPersistence(store)
                .Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                .Status
        );
        Assert.Equal(
            AzureAuthPersistedRecordStatus.Missing,
            new AzureAuthBindingPersistence(store)
                .Read(CredentialProviderCompositionRoot.BindingRecordName)
                .Status
        );
    }

    private static void AssertRecordsAreConfiguredTogether(
        IAzureAuthSecureRecordStore store,
        AzureAuthProviderSelection expectedSelection,
        string expectedTenant,
        string expectedAccount
    )
    {
        AzureAuthPersistedRecord<AzureAuthProviderConfig> config =
            new AzureAuthProviderConfigPersistence(store).Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> binding = new AzureAuthBindingPersistence(
            store
        ).Read(CredentialProviderCompositionRoot.BindingRecordName);
        Assert.Equal(AzureAuthPersistedRecordStatus.Present, config.Status);
        Assert.Equal(AzureAuthPersistedRecordStatus.Present, binding.Status);
        Assert.Equal(expectedSelection, config.Value!.Selection);
        Assert.Equal(expectedSelection, binding.Value!.ProviderSelection);
        Assert.Equal(expectedTenant, binding.Value.TenantId);
        Assert.Equal(expectedAccount, binding.Value.AccountId);
    }

    private static InMemoryRecordStore CreateConfiguredInterleavingStore(
        AzureAuthProviderConfig config,
        string tenantId,
        string? accountId,
        DateTimeOffset recordedAtUtc
    )
    {
        var store = new InMemoryRecordStore(pauseAfterFirstMutation: true);
        store.Put(
            CredentialProviderCompositionRoot.ProviderConfigRecordName,
            AzureAuthProviderConfigJson.Serialize(config)
        );
        store.Put(
            CredentialProviderCompositionRoot.BindingRecordName,
            AzureAuthBindingJson.Serialize(
                AzureAuthBindingPolicy.CreateBound(config, accountId, tenantId, recordedAtUtc)
            )
        );
        return store;
    }

    private sealed class InMemoryRecordStore
        : IAzureAuthSecureRecordStore,
            IAzureAuthSecureRecordStoreOperationScope
    {
        private readonly HashSet<string> conflictNextWrites = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        private readonly object operationLock = new();
        private readonly bool pauseAfterFirstMutation;
        private readonly TaskCompletionSource firstMutationReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource releaseFirstOperation = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource secondScopeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int firstScopeMutationCount;
        private int scopeAttemptCount;
        private int scopeEntryCount;

        public InMemoryRecordStore(bool pauseAfterFirstMutation = false)
        {
            this.pauseAfterFirstMutation = pauseAfterFirstMutation;
        }

        public int CompareExchangeCount { get; private set; }

        public List<string> DeleteOrder { get; } = [];

        public Task FirstMutationReached => firstMutationReached.Task;

        public Task SecondScopeAttempted => secondScopeAttempted.Task;

        public int ScopeEntryCount => Volatile.Read(ref scopeEntryCount);

        public TResult Execute<TResult>(Func<IAzureAuthSecureRecordStore, TResult> operation)
        {
            int attempt = Interlocked.Increment(ref scopeAttemptCount);
            if (pauseAfterFirstMutation && attempt == 2)
            {
                secondScopeAttempted.SetResult();
            }

            lock (operationLock)
            {
                Interlocked.Increment(ref scopeEntryCount);
                return operation(this);
            }
        }

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
            CompareExchangeCount++;
            if (conflictNextWrites.Remove(path))
            {
                return AzureAuthSecureRecordWriteResult.Conflict();
            }

            AzureAuthSecureRecordReadResult current = Read(path);
            if (!RevisionMatches(current, expectedRevision))
            {
                return AzureAuthSecureRecordWriteResult.Conflict();
            }

            byte[] content = newContent.ToArray();
            entries[path] = content;
            PauseFirstScopeAfterFirstMutation();
            return AzureAuthSecureRecordWriteResult.Success(Revision(content));
        }

        public AzureAuthSecureRecordWriteResult CompareDelete(string path, string expectedRevision)
        {
            DeleteOrder.Add(path);
            AzureAuthSecureRecordReadResult current = Read(path);
            if (!RevisionMatches(current, expectedRevision))
            {
                return AzureAuthSecureRecordWriteResult.Conflict();
            }

            entries.Remove(path);
            PauseFirstScopeAfterFirstMutation();
            return AzureAuthSecureRecordWriteResult.Success(
                AzureAuthSecureRecordStoreContract.MissingRevision
            );
        }

        public void ConflictNextWrite(string path) => conflictNextWrites.Add(path);

        public void ReleaseFirstOperation() => releaseFirstOperation.SetResult();

        public void Put(string path, string content) =>
            entries[path] = Encoding.UTF8.GetBytes(content);

        private void PauseFirstScopeAfterFirstMutation()
        {
            if (
                pauseAfterFirstMutation
                && ScopeEntryCount == 1
                && Interlocked.Increment(ref firstScopeMutationCount) == 1
            )
            {
                firstMutationReached.SetResult();
                releaseFirstOperation.Task.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken
                )
                    .GetAwaiter()
                    .GetResult();
            }
        }

        private static bool RevisionMatches(
            AzureAuthSecureRecordReadResult current,
            string expectedRevision
        ) =>
            current.Status == AzureAuthSecureRecordReadStatus.Missing
                ? expectedRevision == AzureAuthSecureRecordStoreContract.MissingRevision
                : string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal);

        private static string Revision(ReadOnlySpan<byte> content) =>
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}
