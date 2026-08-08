using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthInstallationDoctorTests
{
    [Fact]
    public void DoctorReportsAvailableInstallationAndMatchingBinding()
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant",
            DateTimeOffset.UtcNow
        );
        AzureAuthDoctorReport report = AzureAuthDoctor.Run(
            config,
            AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                "azureauth/account-binding.json",
                "revision",
                binding
            ),
            AzureAuthInstallation.Available(
                @"C:\Users\User\AppData\Local\Programs\AzureAuth\0.9.5\azureauth.exe",
                "/mnt/c/Users/User/AppData/Local/Programs/AzureAuth/0.9.5/azureauth.exe",
                "0.9.5"
            )
        );

        Assert.All(
            report.Checks,
            check => Assert.Equal(AzureAuthDoctorCheckStatus.Pass, check.Status)
        );
    }

    [Theory]
    [InlineData(AzureAuthInstallationStatus.Missing, "AzureAuthInstallationMissing")]
    [InlineData(AzureAuthInstallationStatus.WrongVersion, "AzureAuthVersionMismatch")]
    [InlineData(AzureAuthInstallationStatus.Unsupported, "AzureAuthLaunchHostUnsupported")]
    public void DoctorReportsActionableInstallationFailure(
        AzureAuthInstallationStatus status,
        string code
    )
    {
        AzureAuthDoctorReport report = AzureAuthDoctor.Run(
            AzureAuthProviderConfig.CreateAzureAuth(),
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing("azureauth/account-binding.json"),
            AzureAuthInstallation.Failure(status, code, "actionable")
        );

        Assert.Equal("actionable", report.Checks[1].Message);
        Assert.NotEqual(AzureAuthDoctorCheckStatus.Pass, report.Checks[1].Status);
    }
}
