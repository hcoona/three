namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthDoctorCheckStatus
{
    Pass = 1,
    Warning = 2,
    Fail = 3,
    Unsupported = 4,
}

public sealed record AzureAuthDoctorCheck
{
    public required string Code { get; init; }
    public required AzureAuthDoctorCheckStatus Status { get; init; }
    public required string Message { get; init; }
}

public sealed record AzureAuthDoctorReport
{
    public required IReadOnlyList<AzureAuthDoctorCheck> Checks { get; init; }
}

public static class AzureAuthDoctor
{
    public static AzureAuthDoctorReport Run(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        AzureAuthInstallation? installation = null
    )
    {
        if (config.Selection != AzureAuthProviderSelection.Unspecified)
        {
            AzureAuthProviderConfigPolicy.EnsureValid(config);
        }
        ArgumentNullException.ThrowIfNull(bindingRecord);
        return new AzureAuthDoctorReport
        {
            Checks =
            [
                CreateProviderCheck(config),
                CreateInstallationCheck(config, installation),
                CreateBindingCheck(config, bindingRecord),
            ],
        };
    }

    private static AzureAuthDoctorCheck CreateProviderCheck(AzureAuthProviderConfig config) =>
        config.Selection switch
        {
            AzureAuthProviderSelection.DirectMsal => new()
            {
                Code = "provider-selection",
                Status = AzureAuthDoctorCheckStatus.Unsupported,
                Message = "Direct MSAL is selected but is not implemented.",
            },
            AzureAuthProviderSelection.AzureAuth => new()
            {
                Code = "provider-selection",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message = $"AzureAuth {config.AzureAuthVersion} is selected.",
            },
            _ => new AzureAuthDoctorCheck
            {
                Code = "provider-selection",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "Provider configuration is missing.",
            },
        };

    private static AzureAuthDoctorCheck CreateInstallationCheck(
        AzureAuthProviderConfig config,
        AzureAuthInstallation? installation
    )
    {
        if (config.Selection != AzureAuthProviderSelection.AzureAuth)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "azureauth-installation",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message = "AzureAuth installation is not required for the selected provider.",
            };
        }

        if (installation is null)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "azureauth-installation",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "AzureAuth installation discovery has not run.",
            };
        }

        return new AzureAuthDoctorCheck
        {
            Code = "azureauth-installation",
            Status =
                installation.IsAvailable ? AzureAuthDoctorCheckStatus.Pass
                : installation.Status == AzureAuthInstallationStatus.Unsupported
                    ? AzureAuthDoctorCheckStatus.Unsupported
                : AzureAuthDoctorCheckStatus.Fail,
            Message = installation.SafeMessage,
        };
    }

    private static AzureAuthDoctorCheck CreateBindingCheck(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord
    )
    {
        if (bindingRecord.Status == AzureAuthPersistedRecordStatus.Missing)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Warning,
                Message = "No binding record exists.",
            };
        }

        if (bindingRecord.Status == AzureAuthPersistedRecordStatus.Malformed)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "Binding record is malformed. Rebind or unbind it.",
            };
        }

        AzureAuthBinding binding = AzureAuthPersistenceCore.RequireValue(bindingRecord);
        AzureAuthBindingPolicy.EnsureValid(binding);
        return new AzureAuthDoctorCheck
        {
            Code = "binding-state",
            Status =
                binding.ProviderSelection == config.Selection
                    ? AzureAuthDoctorCheckStatus.Pass
                    : AzureAuthDoctorCheckStatus.Fail,
            Message =
                binding.ProviderSelection == config.Selection
                    ? "Binding matches the selected provider."
                    : "Binding provider does not match the selected provider.",
        };
    }
}
