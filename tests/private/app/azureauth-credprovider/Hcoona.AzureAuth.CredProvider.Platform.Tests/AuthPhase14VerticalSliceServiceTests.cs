using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AuthPhase14VerticalSliceServiceTests
{
    [Fact]
    public void LoginInteractiveBrowserUsesAcceptedMvpFlowWithoutPersistentDerivedCredentials()
    {
        var service = new AuthPhase14VerticalSliceService();

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.InteractiveBrowser,
                AccountHint = "Alice@Example",
                TenantHint = "TenantA",
            }
        );

        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Equal("alice@example", result.CredentialResult.Account);
        Assert.Equal("tenanta", result.CredentialResult.Tenant);
        Assert.False(result.PersistentDerivedCredentialsStored);
        Assert.True(result.CredentialResult.ContainsCredentialMaterial);
    }

    [Fact]
    public void LoginPatCompatibilityRequiresExplicitPatMaterial()
    {
        var service = new AuthPhase14VerticalSliceService();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () =>
                service.Login(
                    new AuthPhase14LoginRequest
                    {
                        IdentityFlow = IdentityFlow.PatCompatibility,
                    }
                )
        );

        Assert.Contains("requires an explicit --pat value", exception.Message);
    }

    [Fact]
    public void LoginAzurePipelinesRequiresExplicitCiModeAndTokenEnvironment()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions { EnvironmentVariableReader = _ => null }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () =>
                service.Login(
                    new AuthPhase14LoginRequest
                    {
                        IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
                        ExplicitAzurePipelinesCiMode = true,
                    }
                )
        );

        Assert.Contains("system access token is unavailable", exception.Message);
    }

    [Fact]
    public void LoginAzurePipelinesUsesNonPersistentCiPolicy()
    {
        var service = new AuthPhase14VerticalSliceService(
            new AuthPhase14VerticalSliceOptions { EnvironmentVariableReader = _ => "token" }
        );

        AuthPhase14LoginResult result = service.Login(
            new AuthPhase14LoginRequest
            {
                IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
                ExplicitAzurePipelinesCiMode = true,
            }
        );

        Assert.Equal(CredentialResultStatus.Success, result.CredentialResult.Status);
        Assert.Equal("build-service@phase14", result.CredentialResult.Account);
        Assert.False(result.PersistentDerivedCredentialsStored);
    }

    [Fact]
    public void LoginDeferredServiceIdentityFlowThrowsNotSupported()
    {
        var service = new AuthPhase14VerticalSliceService();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () =>
                service.Login(
                    new AuthPhase14LoginRequest
                    {
                        IdentityFlow = IdentityFlow.ManagedIdentity,
                    }
                )
        );

        Assert.Contains("deferred for MVP", exception.Message);
    }
}
