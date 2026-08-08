using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthBindingTests
{
    [Fact]
    public void BoundBindingTrimsValuesPreservesCaseAndNormalizesUtc()
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateAzureAuth(),
            " User@Example.COM ",
            " Tenant-One ",
            new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.FromHours(2))
        );

        Assert.Equal("User@Example.COM", binding.AccountId);
        Assert.Equal("Tenant-One", binding.TenantId);
        Assert.Equal(TimeSpan.Zero, binding.RecordedAtUtc.Offset);
    }

    [Fact]
    public void AccountIsOptionalButTenantIsRequired()
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateAzureAuth(),
            null,
            "tenant",
            DateTimeOffset.UtcNow
        );

        Assert.Null(binding.AccountId);
        Assert.Throws<ArgumentException>(() =>
            AzureAuthBindingPolicy.CreateBound(
                AzureAuthProviderConfig.CreateAzureAuth(),
                null,
                " ",
                DateTimeOffset.UtcNow
            )
        );
    }

    [Fact]
    public void BindIdentityComparisonIsCaseInsensitive()
    {
        AzureAuthBinding current = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateAzureAuth(),
            "User@Example.com",
            "Tenant-One",
            DateTimeOffset.UtcNow
        );

        AzureAuthBinding result = AzureAuthBindingPolicy.Bind(
            current,
            AzureAuthProviderConfig.CreateAzureAuth(),
            "user@example.COM",
            "tenant-one",
            DateTimeOffset.UtcNow.AddMinutes(1)
        );

        Assert.Same(current, result);
    }

    [Fact]
    public void JsonRoundTripsWithoutDeploymentIdentity()
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateAzureAuth(),
            "user@example.com",
            "tenant",
            DateTimeOffset.UtcNow
        );
        string json = AzureAuthBindingJson.Serialize(binding);

        Assert.Equal(binding, AzureAuthBindingJson.Deserialize(json));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            ["provider", "account", "tenant", "timestamp"],
            document.RootElement.EnumerateObject().Select(property => property.Name)
        );
        Assert.DoesNotContain("deployment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<JsonException>(() => AzureAuthBindingJson.Deserialize("{"));
    }
}
