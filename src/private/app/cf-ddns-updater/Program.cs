using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hcoona.CfDdnsUpdater;

internal sealed partial class Program
{
    private const string ConfigurationPrefix = "HCOONA_CLOUDFLARE_DDNS_UPDATER_";

    public static async Task<int> Main(string[] args)
    {
        using Activity? runActivity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.RunActivityName,
            ActivityKind.Internal);
        ILogger logger = NullLogger.Instance;
        using CancellationTokenSource cancellationTokenSource = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;
        try
        {
            using IHost host = CreateHost(args);
            ILoggerFactory loggerFactory =
                host.Services.GetRequiredService<ILoggerFactory>();
            logger = loggerFactory.CreateLogger("Program");

            await host.StartAsync().ConfigureAwait(false);
            try
            {
                IReconciliationApp app =
                    host.Services.GetRequiredService<IReconciliationApp>();
                int exitCode = await app.RunAsync(cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                CloudflareTelemetry.MarkRunExitCode(runActivity, exitCode);
                CloudflareTelemetry.MarkOutcome(
                    runActivity,
                    exitCode == 0 ? "success" : "failure");
                return exitCode;
            }
            catch (OperationCanceledException)
                when (cancellationTokenSource.IsCancellationRequested)
            {
                CloudflareTelemetry.MarkOutcome(runActivity, "cancelled");
                return 0;
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
        catch (OptionsValidationException ex)
        {
            CloudflareTelemetry.MarkFailure(runActivity, ex);
            LogConfigurationValidationFailed(logger, ex);
            return 1;
        }
        catch (Exception ex)
        {
            CloudflareTelemetry.MarkFailure(runActivity, ex);
            LogUnhandledFailure(logger, ex);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
        }
    }

    private static IHost CreateHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddEnvironmentVariables(ConfigurationPrefix);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.AddHttpClient<CloudflareApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/", UriKind.Absolute);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("hcoona-cf-ddns-updater");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddTransient<CloudflareTraceRetryHandler>();
        builder.Services.AddHttpClient<ITraceIpDiscoveryService, TraceIpDiscoveryService>(
            client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("hcoona-cf-ddns-updater");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<CloudflareTraceRetryHandler>();

        builder.Services.AddSingleton(_ => LoadCloudflareConfiguration(builder.Configuration));
        builder.Services.AddSingleton<CloudflareZoneResolver>();
        builder.Services.AddSingleton<CloudflareDnsRecordClient>();
        builder.Services.AddSingleton<IReconciliationApp, ReconciliationApp>();

        return builder.Build();
    }

    private static CloudflareOptions CreateCloudflareOptions(
        ConfigurationManager configuration)
        => new()
        {
            ApiToken = configuration["API_TOKEN"],
            DomainsCsv = configuration["DOMAINS"],
            DisableIpv6Raw = configuration["DISABLE_IPV6"],
        };

    private static CloudflareConfiguration LoadCloudflareConfiguration(
        ConfigurationManager configuration)
    {
        using Activity? loadActivity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.ConfigurationLoadActivityName,
            ActivityKind.Internal);

        try
        {
            CloudflareConfiguration cloudflareConfiguration = CloudflareConfiguration.Create(
                CreateCloudflareOptions(configuration));

            loadActivity?.SetTag(
                "cf.ddns.domain_count",
                cloudflareConfiguration.Domains.Length);
            loadActivity?.SetTag(
                "cf.ddns.disable_ipv6",
                cloudflareConfiguration.DisableIpv6);
            CloudflareTelemetry.MarkOutcome(loadActivity, "success");
            return cloudflareConfiguration;
        }
        catch (Exception ex)
        {
            CloudflareTelemetry.MarkFailure(loadActivity, ex);
            throw;
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Critical,
        Message = "Configuration validation failed.")]
    private static partial void LogConfigurationValidationFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Critical,
        Message = "Unhandled failure.")]
    private static partial void LogUnhandledFailure(
        ILogger logger,
        Exception exception);
}
