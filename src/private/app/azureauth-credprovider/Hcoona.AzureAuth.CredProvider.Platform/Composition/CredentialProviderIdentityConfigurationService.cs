using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

namespace Hcoona.AzureAuth.CredProvider.Platform.Composition;

public enum CredentialProviderIdentityConfigurationAction
{
    Configure = 1,
    Reconfigure = 2,
    Unconfigure = 3,
}

public sealed record CredentialProviderIdentityConfigurationResult
{
    public required CredentialProviderIdentityConfigurationAction Action { get; init; }

    public required bool IsConfigured { get; init; }

    public required bool Changed { get; init; }

    public string? TenantId { get; init; }

    public string? AccountPreference { get; init; }
}

public sealed class CredentialProviderIdentityConfigurationConflictException()
    : InvalidOperationException(
        "Identity configuration changed concurrently. Retry the operation."
    );

public sealed class CredentialProviderIdentityConfigurationService
{
    private readonly AzureAuthProviderConfigPersistence providerConfigPersistence;
    private readonly AzureAuthBindingPersistence bindingPersistence;
    private readonly TimeProvider timeProvider;

    public CredentialProviderIdentityConfigurationService(
        IAzureAuthSecureRecordStore? secureRecordStore = null,
        TimeProvider? timeProvider = null
    )
    {
        secureRecordStore ??= new SystemAzureAuthSecureRecordStore();
        providerConfigPersistence = new AzureAuthProviderConfigPersistence(secureRecordStore);
        bindingPersistence = new AzureAuthBindingPersistence(secureRecordStore);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CredentialProviderIdentityConfigurationResult Configure(
        string tenantId,
        string? accountPreference = null
    )
    {
        AzureAuthProviderConfig targetConfig = AzureAuthProviderConfig.CreateAzureAuth();
        DateTimeOffset recordedAtUtc = timeProvider.GetUtcNow();
        AzureAuthBinding targetBinding = AzureAuthBindingPolicy.CreateBound(
            targetConfig,
            accountPreference,
            tenantId,
            recordedAtUtc
        );
        AzureAuthPersistedRecord<AzureAuthProviderConfig> configRecord =
            providerConfigPersistence.Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord = bindingPersistence.Read(
            CredentialProviderCompositionRoot.BindingRecordName
        );

        ValidateConfigForConfigure(configRecord, targetConfig);
        AzureAuthBinding configuredBinding = ValidateBindingForConfigure(
            bindingRecord,
            targetConfig,
            accountPreference,
            tenantId,
            recordedAtUtc,
            targetBinding
        );

        bool stateChanged = false;
        if (configRecord.Status == AzureAuthPersistedRecordStatus.Missing)
        {
            EnsureSuccess(
                providerConfigPersistence.Create(configRecord.RecordName, targetConfig).Status
            );
            stateChanged = true;
        }

        if (bindingRecord.Status == AzureAuthPersistedRecordStatus.Missing)
        {
            AzureAuthPersistedWriteResult<AzureAuthBinding> writeResult = bindingPersistence.Bind(
                bindingRecord,
                targetConfig,
                accountPreference,
                tenantId,
                recordedAtUtc
            );
            EnsureSuccess(writeResult.Status);
            configuredBinding = writeResult.Record!.Value!;
            stateChanged = true;
        }

        return ConfiguredResult(
            CredentialProviderIdentityConfigurationAction.Configure,
            stateChanged,
            configuredBinding
        );
    }

    public CredentialProviderIdentityConfigurationResult Reconfigure(
        string tenantId,
        string? accountPreference = null
    )
    {
        AzureAuthProviderConfig targetConfig = AzureAuthProviderConfig.CreateAzureAuth();
        DateTimeOffset recordedAtUtc = timeProvider.GetUtcNow();
        AzureAuthBinding targetBinding = AzureAuthBindingPolicy.Rebind(
            targetConfig,
            accountPreference,
            tenantId,
            recordedAtUtc
        );
        AzureAuthPersistedRecord<AzureAuthProviderConfig> configRecord =
            providerConfigPersistence.Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord = bindingPersistence.Read(
            CredentialProviderCompositionRoot.BindingRecordName
        );

        bool configChanged =
            configRecord.Status != AzureAuthPersistedRecordStatus.Present
            || configRecord.Value != targetConfig;
        if (configChanged)
        {
            AzureAuthPersistedWriteResult<AzureAuthProviderConfig> writeResult =
                configRecord.Status switch
                {
                    AzureAuthPersistedRecordStatus.Missing => providerConfigPersistence.Create(
                        configRecord.RecordName,
                        targetConfig
                    ),
                    AzureAuthPersistedRecordStatus.Present => providerConfigPersistence.Replace(
                        configRecord,
                        targetConfig
                    ),
                    AzureAuthPersistedRecordStatus.Malformed => providerConfigPersistence.Repair(
                        configRecord,
                        targetConfig
                    ),
                    _ => throw new InvalidOperationException(
                        "Unsupported provider configuration status."
                    ),
                };
            EnsureSuccess(writeResult.Status);
        }

        bool bindingChanged =
            bindingRecord.Status != AzureAuthPersistedRecordStatus.Present
            || bindingRecord.Value != targetBinding;
        AzureAuthBinding configuredBinding;
        if (bindingChanged)
        {
            AzureAuthPersistedWriteResult<AzureAuthBinding> bindingWriteResult =
                bindingPersistence.Rebind(
                    bindingRecord,
                    targetConfig,
                    accountPreference,
                    tenantId,
                    recordedAtUtc
                );
            EnsureSuccess(bindingWriteResult.Status);
            configuredBinding = bindingWriteResult.Record!.Value!;
        }
        else
        {
            configuredBinding =
                bindingRecord.Value
                ?? throw new InvalidOperationException("Binding value is missing.");
        }

        return ConfiguredResult(
            CredentialProviderIdentityConfigurationAction.Reconfigure,
            configChanged || bindingChanged,
            configuredBinding
        );
    }

    public CredentialProviderIdentityConfigurationResult Unconfigure()
    {
        AzureAuthPersistedRecord<AzureAuthProviderConfig> configRecord =
            providerConfigPersistence.Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord = bindingPersistence.Read(
            CredentialProviderCompositionRoot.BindingRecordName
        );

        EnsureSuccess(bindingPersistence.Unbind(bindingRecord).Status);
        EnsureSuccess(providerConfigPersistence.Delete(configRecord).Status);

        return new CredentialProviderIdentityConfigurationResult
        {
            Action = CredentialProviderIdentityConfigurationAction.Unconfigure,
            IsConfigured = false,
            Changed =
                bindingRecord.Status != AzureAuthPersistedRecordStatus.Missing
                || configRecord.Status != AzureAuthPersistedRecordStatus.Missing,
        };
    }

    private static void ValidateConfigForConfigure(
        AzureAuthPersistedRecord<AzureAuthProviderConfig> configRecord,
        AzureAuthProviderConfig targetConfig
    )
    {
        if (configRecord.Status == AzureAuthPersistedRecordStatus.Malformed)
        {
            throw new InvalidOperationException(
                "Provider configuration is malformed. Use reconfigure to replace it."
            );
        }

        if (configRecord.Status == AzureAuthPersistedRecordStatus.Missing)
        {
            return;
        }

        AzureAuthProviderConfig current =
            configRecord.Value
            ?? throw new InvalidOperationException("Provider configuration value is missing.");
        AzureAuthProviderConfigPolicy.EnsureValid(current);
        if (current != targetConfig)
        {
            throw new InvalidOperationException(
                "Another provider configuration is already selected. "
                    + "Use reconfigure to replace it."
            );
        }
    }

    private static AzureAuthBinding ValidateBindingForConfigure(
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        AzureAuthProviderConfig targetConfig,
        string? accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc,
        AzureAuthBinding targetBinding
    ) =>
        bindingRecord.Status switch
        {
            AzureAuthPersistedRecordStatus.Missing => targetBinding,
            AzureAuthPersistedRecordStatus.Present => AzureAuthBindingPolicy.Bind(
                bindingRecord.Value
                    ?? throw new InvalidOperationException("Binding value is missing."),
                targetConfig,
                accountId,
                tenantId,
                recordedAtUtc
            ),
            AzureAuthPersistedRecordStatus.Malformed => throw new InvalidOperationException(
                "Identity binding is malformed. Use reconfigure to replace it."
            ),
            _ => throw new InvalidOperationException("Unsupported binding status."),
        };

    private static CredentialProviderIdentityConfigurationResult ConfiguredResult(
        CredentialProviderIdentityConfigurationAction action,
        bool stateChanged,
        AzureAuthBinding binding
    ) =>
        new()
        {
            Action = action,
            IsConfigured = true,
            Changed = stateChanged,
            TenantId = binding.TenantId,
            AccountPreference = binding.AccountId,
        };

    private static void EnsureSuccess(AzureAuthSecureRecordWriteStatus status)
    {
        if (status == AzureAuthSecureRecordWriteStatus.Conflict)
        {
            throw new CredentialProviderIdentityConfigurationConflictException();
        }
    }
}
