using System.CommandLine;
using Hcoona.QidianNovelDownloader.Commands;
using Hcoona.QidianNovelDownloader.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hcoona.QidianNovelDownloader;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        string configPath = TryGetConfigPathOverride(args) ?? AppPaths.GetDefaultConfigPath();

        builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        builder.Services.Configure<AppSettings>(
            builder.Configuration.GetSection(AppConstants.QidianSectionName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        builder.Services.AddSingleton<IInteractiveConsole, SystemInteractiveConsole>();
        builder.Services.AddSingleton<IAppStorageService, AppStorageService>();
        builder.Services.AddSingleton<
            Browser.IQidianBrowserManager,
            Browser.QidianBrowserManager>();
        builder.Services.AddTransient<AppCommandService>();

        using IHost host = builder.Build();
        RootCommand rootCommand = CliFactory.CreateRootCommand(host.Services);
        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static string? TryGetConfigPathOverride(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--config", StringComparison.Ordinal))
            {
                if (index + 1 < args.Length && IsValidConfigPathOverride(args[index + 1]))
                {
                    return args[index + 1];
                }

                return null;
            }

            const string configPrefix = "--config=";
            if (args[index].StartsWith(configPrefix, StringComparison.Ordinal))
            {
                string configPath = args[index][configPrefix.Length..];
                return IsValidConfigPathOverride(configPath) ? configPath : null;
            }
        }

        return null;
    }

    private static bool IsValidConfigPathOverride(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith('-');
}
