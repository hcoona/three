using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hcoona.CfDdnsUpdater;

internal sealed partial class Program
{
    private const string ConfigurationPrefix = "HCOONA_CLOUDFLARE_DDNS_UPDATER_";

    public static async Task<int> Main(string[] args)
    {
        using IHost host = CreateHost(args);
        ILoggerFactory loggerFactory =
            host.Services.GetRequiredService<ILoggerFactory>();
        ILogger logger = loggerFactory.CreateLogger("Program");

        try
        {
            await host.StartAsync().ConfigureAwait(false);
            try
            {
                IReconciliationApp app =
                    host.Services.GetRequiredService<IReconciliationApp>();
                return await app.RunAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
        catch (OptionsValidationException ex)
        {
            LogConfigurationValidationFailed(logger, ex);
            return 1;
        }
        catch (Exception ex)
        {
            LogUnhandledFailure(logger, ex);
            return 1;
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

        builder.Services.AddSingleton(_ => CloudflareConfiguration.Create(
            CreateCloudflareOptions(builder.Configuration)));
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
