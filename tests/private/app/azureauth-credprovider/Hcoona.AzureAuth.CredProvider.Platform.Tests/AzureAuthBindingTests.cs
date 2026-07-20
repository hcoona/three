using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthBindingTests
{
    [Fact]
    public void BindAndUnbindNoOpsReturnCurrentBindingsWithoutTimestampChurn()
    {
        DateTimeOffset boundAt = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        AzureAuthBinding current = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateDefault(),
            "User@Example.com",
            "Tenant-One",
            boundAt
        );
        AzureAuthBinding noOpBind = AzureAuthBindingPolicy.Bind(
            current,
            AzureAuthProviderConfig.CreateDefault(),
            "user@example.com",
            "tenant-one",
            boundAt.AddHours(1)
        );
        AzureAuthBinding unbound = AzureAuthBindingPolicy.CreateUnbound(boundAt.AddHours(2));
        AzureAuthBinding noOpUnbind = AzureAuthBindingPolicy.Unbind(
            unbound,
            boundAt.AddHours(3)
        );

        Assert.Same(current, noOpBind);
        Assert.Equal(boundAt, noOpBind.RecordedAtUtc);
        Assert.Same(unbound, noOpUnbind);
        Assert.Equal(unbound.RecordedAtUtc, noOpUnbind.RecordedAtUtc);
    }

    [Fact]
    public void SerializeEmitsExactFrozenUnboundBindingJson()
    {
        Assert.Equal(
            """{"schemaVersion":1,"state":"unbound","providerSelection":"unspecified","deploymentKey":null,"accountId":null,"tenantId":null,"recordedAtUtc":"2026-07-20T00:00:00Z"}""",
            AzureAuthBindingJson.Serialize(
                AzureAuthBindingPolicy.CreateUnbound(
                    new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
                )
            )
        );
    }

    [Fact]
    public void SerializeEmitsExactFrozenBoundBindingJson()
    {
        Assert.Equal(
            """{"schemaVersion":1,"state":"bound","providerSelection":"directMsal","deploymentKey":null,"accountId":"user@example.com","tenantId":"tenant-one","recordedAtUtc":"2026-07-20T00:00:00Z"}""",
            AzureAuthBindingJson.Serialize(
                AzureAuthBindingPolicy.CreateBound(
                    AzureAuthProviderConfig.CreateDefault(),
                    "User@Example.com",
                    "Tenant-One",
                    new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
                )
            )
        );
    }

    [Fact]
    public void CreateBoundNormalizesAsciiUppercaseObservedIdentifiers()
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateDefault(),
            "User@Example.com",
            "Tenant-One",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal("user@example.com", binding.AccountId);
        Assert.Equal("tenant-one", binding.TenantId);
    }

    [Fact]
    public void CreateBoundTrimsOnlyOrdinaryAsciiSpacesBeforeNormalization()
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            AzureAuthProviderConfig.CreateDefault(),
            " User@Example.com ",
            " Tenant-One ",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal("user@example.com", binding.AccountId);
        Assert.Equal("tenant-one", binding.TenantId);
    }

    [Theory]
    [InlineData("\tuser@example.com", "tenant-one")]
    [InlineData("user@example.com", "tenant-one\n")]
    [InlineData("\u00A0user@example.com", "tenant-one")]
    [InlineData("user@example.com", "tenant-\u2003one")]
    [InlineData("Kuser@example.com", "tenant-one")]
    [InlineData("user@example.com", "tenant-K")]
    public void CreateBoundRejectsControlOrNonAsciiObservedIdentifiersBeforeNormalization(
        string accountId,
        string tenantId
    )
    {
        Assert.Throws<ArgumentException>(() =>
            AzureAuthBindingPolicy.CreateBound(
                AzureAuthProviderConfig.CreateDefault(),
                accountId,
                tenantId,
                new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
            )
        );
    }

    [Theory]
    [InlineData("   ", "tenant-one")]
    [InlineData("user@example.com", "   ")]
    public void CreateBoundRejectsIdentifiersThatBecomeEmptyAfterAsciiSpaceTrim(
        string accountId,
        string tenantId
    )
    {
        Assert.Throws<ArgumentException>(() =>
            AzureAuthBindingPolicy.CreateBound(
                AzureAuthProviderConfig.CreateDefault(),
                accountId,
                tenantId,
                new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
            )
        );
    }

    [Fact]
    public void CreateBoundRejectsSubsecondUtcTimestamps()
    {
        Assert.Throws<ArgumentException>(() =>
            AzureAuthBindingPolicy.CreateBound(
                AzureAuthProviderConfig.CreateDefault(),
                "user@example.com",
                "tenant-one",
                new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero).AddTicks(1)
            )
        );
    }

    [Theory]
    [InlineData("User@Example.com", "tenant-one")]
    [InlineData("user@example.com", "tenant-K")]
    public void EnsureValidRejectsStoredIdentifiersThatAreNotAlreadyCanonicalLowercaseAscii(
        string accountId,
        string tenantId
    )
    {
        AzureAuthBinding binding = new()
        {
            SchemaVersion = ContractVersions.AzureAuthAccountBindingSchemaMajor,
            State = AzureAuthBindingState.Bound,
            ProviderSelection = AzureAuthProviderSelection.DirectMsal,
            AccountId = accountId,
            TenantId = tenantId,
            RecordedAtUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Throws<ArgumentException>(() => AzureAuthBindingPolicy.EnsureValid(binding));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("{")]
    public void StrictJsonRejectsMalformedPayloads(string json)
    {
        Assert.Throws<JsonException>(() => AzureAuthBindingJson.Deserialize(json));
    }

    [Fact]
    public void StrictJsonRejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => AzureAuthBindingJson.Deserialize(null!));
    }

    [Theory]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z",
          "unexpected": "value"
        }
        """
    )]
    [InlineData(
        """
        {
          "SchemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "accountId": "other@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": 2,
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    public void StrictJsonRejectsUnknownCaseDuplicateNumericEnumAndMissingRequiredPayloads(
        string json
    )
    {
        Assert.Throws<JsonException>(() => AzureAuthBindingJson.Deserialize(json));
    }

    [Theory]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00+01:00"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00+00:00"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00.123Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20t00:00:00Z"
        }
        """
    )]
    public void StrictJsonRejectsRecordedAtUtcFormsOutsideFrozenCanonicalUtcFormat(string json)
    {
        Assert.Throws<JsonException>(() => AzureAuthBindingJson.Deserialize(json));
    }

    [Theory]
    [InlineData(
        """
        {
          "schemaVersion": 2,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "unspecified",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "User@Example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "directMsal",
          "accountId": "user@example.com",
          "tenantId": "tenant-K",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    [InlineData(
        """
        {
          "schemaVersion": 1,
          "state": "bound",
          "providerSelection": "azureAuth",
          "deploymentKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
          "accountId": "user@example.com",
          "tenantId": "tenant-one",
          "recordedAtUtc": "2026-07-20T00:00:00Z"
        }
        """
    )]
    public void StrictJsonRejectsWrongSchemaAndSemanticallyInvalidPayloads(string json)
    {
        Assert.Throws<ArgumentException>(() => AzureAuthBindingJson.Deserialize(json));
    }
}
