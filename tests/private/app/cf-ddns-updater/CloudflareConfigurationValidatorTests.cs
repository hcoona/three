using Microsoft.Extensions.Options;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

public sealed class CloudflareConfigurationValidatorTests
{
    [Fact]
    public void CreateCanonicalizesAndDeduplicatesDomains()
    {
        CloudflareOptions options = new()
        {
            ApiToken = "token",
            DomainsCsv = " Example.COM. , example.com, xn--bcher-kva.example ",
            DisableIpv6Raw = "false",
        };

        CloudflareConfiguration configuration = CloudflareConfiguration.Create(options);

        Assert.Equal("token", configuration.ApiToken);
        Assert.False(configuration.DisableIpv6);
        Assert.Equal(["example.com", "xn--bcher-kva.example"], configuration.Domains);
    }

    [Fact]
    public void CreateDropsEmptyDomainEntries()
    {
        CloudflareOptions options = new()
        {
            ApiToken = "token",
            DomainsCsv = " , example.com, , example.org , ",
            DisableIpv6Raw = "false",
        };

        CloudflareConfiguration configuration = CloudflareConfiguration.Create(options);

        Assert.Equal(["example.com", "example.org"], configuration.Domains);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("maybe")]
    public void CreateRejectsInvalidDisableIpv6Value(string? disableIpv6Raw)
    {
        CloudflareOptions options = new()
        {
            ApiToken = "token",
            DomainsCsv = "example.com",
            DisableIpv6Raw = disableIpv6Raw,
        };

        Assert.Throws<OptionsValidationException>(() => CloudflareConfiguration.Create(options));
    }

    [Fact]
    public void CreateAllowsMissingDisableIpv6Value()
    {
        CloudflareOptions options = new()
        {
            ApiToken = "token",
            DomainsCsv = "example.com",
        };

        CloudflareConfiguration configuration = CloudflareConfiguration.Create(options);

        Assert.False(configuration.DisableIpv6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    [InlineData("bad host name")]
    [InlineData("example")]
    [InlineData("-bad.example")]
    [InlineData("127.0.0.1")]
    public void CreateRejectsInvalidDomains(string? domainsCsv)
    {
        CloudflareOptions options = new()
        {
            ApiToken = "token",
            DomainsCsv = domainsCsv,
            DisableIpv6Raw = "true",
        };

        Assert.Throws<OptionsValidationException>(() => CloudflareConfiguration.Create(options));
    }

    [Fact]
    public void CreateRejectsMissingApiToken()
    {
        CloudflareOptions options = new()
        {
            ApiToken = "  ",
            DomainsCsv = "example.com",
            DisableIpv6Raw = "false",
        };

        Assert.Throws<OptionsValidationException>(() => CloudflareConfiguration.Create(options));
    }
}
