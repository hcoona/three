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
        string[] parseArgs = NormalizeArgsForCommandLineParsing(args);

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
        return await rootCommand.Parse(parseArgs).InvokeAsync();
    }

    private static string? TryGetConfigPathOverride(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (TryParseConfigPathOverrideArgument(
                args,
                index,
                out string? configPath,
                out _))
            {
                return configPath;
            }
        }

        return null;
    }

    private static bool IsValidConfigPathOverride(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith('-');

    private static string[] NormalizeArgsForCommandLineParsing(string[] args)
    {
        List<string> normalizedArgs = [];
        for (int index = 0; index < args.Length; index++)
        {
            if (TryParseConfigPathOverrideArgument(
                args,
                index,
                out string? configPath,
                out int consumedArgCount))
            {
                if (configPath is not null)
                {
                    normalizedArgs.Add(args[index]);
                    if (consumedArgCount == 2)
                    {
                        normalizedArgs.Add(args[index + 1]);
                    }
                }

                index += consumedArgCount - 1;
                continue;
            }

            normalizedArgs.Add(args[index]);
        }

        return [.. normalizedArgs];
    }

    private static bool TryParseConfigPathOverrideArgument(
        string[] args,
        int index,
        out string? configPath,
        out int consumedArgCount)
    {
        configPath = null;
        consumedArgCount = 0;

        if (string.Equals(args[index], "--config", StringComparison.Ordinal))
        {
            consumedArgCount = 1;
            if (index + 1 < args.Length && IsValidConfigPathOverride(args[index + 1]))
            {
                configPath = args[index + 1];
                consumedArgCount = 2;
            }

            return true;
        }

        const string configPrefix = "--config=";
        if (!args[index].StartsWith(configPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = args[index][configPrefix.Length..];
        configPath = IsValidConfigPathOverride(candidate) ? candidate : null;
        consumedArgCount = 1;
        return true;
    }
}
