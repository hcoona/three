using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class ProgramBootstrapTests
{
    [Fact]
    public void CreateHostReadsPrefixedEnvironmentVariablesAndStrictBooleanValues()
    {
        string apiTokenName = "HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN";
        string domainsName = "HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS";
        string disableIpv6Name = "HCOONA_CLOUDFLARE_DDNS_UPDATER_DISABLE_IPV6";

        string? previousApiToken = Environment.GetEnvironmentVariable(apiTokenName);
        string? previousDomains = Environment.GetEnvironmentVariable(domainsName);
        string? previousDisableIpv6 = Environment.GetEnvironmentVariable(disableIpv6Name);

        try
        {
            Environment.SetEnvironmentVariable(apiTokenName, "token");
            Environment.SetEnvironmentVariable(domainsName, "example.com, host.example.com");

            Environment.SetEnvironmentVariable(disableIpv6Name, "false");
            AssertConfiguration(disableIpv6Expected: false);

            Environment.SetEnvironmentVariable(disableIpv6Name, "TRUE");
            AssertConfiguration(disableIpv6Expected: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiTokenName, previousApiToken);
            Environment.SetEnvironmentVariable(domainsName, previousDomains);
            Environment.SetEnvironmentVariable(disableIpv6Name, previousDisableIpv6);
        }

        static void AssertConfiguration(bool disableIpv6Expected)
        {
            MethodInfo createHost = typeof(Program).GetMethod(
                "CreateHost",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            using IHost host = (IHost)createHost.Invoke(
                null,
                new object?[] { Array.Empty<string>() })!;

            CloudflareConfiguration configuration =
                host.Services.GetRequiredService<CloudflareConfiguration>();

            Assert.Equal("token", configuration.ApiToken);
            Assert.Equal(["example.com", "host.example.com"], configuration.Domains);
            Assert.Equal(disableIpv6Expected, configuration.DisableIpv6);
        }
    }

    [Theory]
    [InlineData(
        "HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN",
        null,
        "HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS",
        "example.com",
        "HCOONA_CLOUDFLARE_DDNS_UPDATER_DISABLE_IPV6",
        null)]
    [InlineData(
        "HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN",
        "token",
        "HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS",
        "example.com",
        "HCOONA_CLOUDFLARE_DDNS_UPDATER_DISABLE_IPV6",
        "maybe")]
    public async Task MainReturnsFailureForInvalidEnvironmentAndSkipsReconciliationWork(
        string key1,
        string? value1,
        string key2,
        string value2,
        string key3,
        string? value3)
    {
        string[] keys = [key1, key2, key3];
        string?[] values = [value1, value2, value3];
        string?[] previousValues = new string?[keys.Length];

        using ActivityRecorder recorder = ActivityRecorder.Start(
            CloudflareTelemetry.ActivitySourceName);

        try
        {
            for (int i = 0; i < keys.Length; i++)
            {
                previousValues[i] = Environment.GetEnvironmentVariable(keys[i]);
                Environment.SetEnvironmentVariable(keys[i], values[i]);
            }

            int exitCode = await Program.Main(Array.Empty<string>());

            Assert.Equal(1, exitCode);
            Assert.DoesNotContain(
                recorder.StoppedActivities,
                activity =>
                    activity.OperationName == CloudflareTelemetry.ReconciliationTargetActivityName);
        }
        finally
        {
            for (int i = 0; i < keys.Length; i++)
            {
                Environment.SetEnvironmentVariable(keys[i], previousValues[i]);
            }
        }
    }

    [Fact]
    public async Task MainReturnsFailureForInvalidDomainConfigurationAndSkipsReconciliationWork()
    {
        string apiTokenName = "HCOONA_CLOUDFLARE_DDNS_UPDATER_API_TOKEN";
        string domainsName = "HCOONA_CLOUDFLARE_DDNS_UPDATER_DOMAINS";
        string? previousApiToken = Environment.GetEnvironmentVariable(apiTokenName);
        string? previousDomains = Environment.GetEnvironmentVariable(domainsName);

        using ActivityRecorder recorder = ActivityRecorder.Start(
            CloudflareTelemetry.ActivitySourceName);

        try
        {
            Environment.SetEnvironmentVariable(apiTokenName, "token");
            Environment.SetEnvironmentVariable(domainsName, "bad host name");

            int exitCode = await Program.Main(Array.Empty<string>());

            Assert.Equal(1, exitCode);
            Assert.DoesNotContain(
                recorder.StoppedActivities,
                activity =>
                    activity.OperationName == CloudflareTelemetry.ReconciliationTargetActivityName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiTokenName, previousApiToken);
            Environment.SetEnvironmentVariable(domainsName, previousDomains);
        }
    }
}
