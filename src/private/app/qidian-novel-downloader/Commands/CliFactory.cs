using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Hcoona.QidianNovelDownloader.Commands;

internal static class CliFactory
{
    public static RootCommand CreateRootCommand(IServiceProvider services)
    {
        AppCommandService commandService = services.GetRequiredService<AppCommandService>();

        Option<FileInfo?> configOption = new("--config")
        {
            Description = "Override the tool-managed config file path for this invocation.",
        };
        RootCommand rootCommand = new("Download Qidian novels to Markdown with Playwright.");
        rootCommand.Options.Add(configOption);
        rootCommand.Subcommands.Add(CreateDownloadCommand(commandService));
        rootCommand.Subcommands.Add(CreateLoginCommand(commandService));
        rootCommand.Subcommands.Add(CreateCacheClearCommand(commandService));
        rootCommand.Subcommands.Add(CreateInfoCommand(commandService));
        return rootCommand;
    }

    private static Command CreateDownloadCommand(AppCommandService commandService)
    {
        Argument<string[]> bookReferencesArgument = new("books")
        {
            Description = "One or more numeric book ids or canonical Qidian book URLs.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        Option<string?> browserPathOption = CreateStringOption(
            "--browser-path",
            "Override the browser executable path for this invocation.");
        Option<string?> browserProfileDirOption = CreateStringOption(
            "--browser-profile-dir",
            "Override the browser profile directory for this invocation.");
        Option<string?> outputDirOption = CreateStringOption(
            "--output-dir",
            "Override the output directory for this invocation.");
        Option<bool> dryRunOption = new("--dry-run")
        {
            Description = "Inspect catalog and cache reuse status without downloading chapter bodies.",
        };
        Option<bool> overwriteOption = new("--overwrite")
        {
            Description = "Overwrite existing Markdown output files without prompting.",
        };
        Option<int?> readingSpeedOption = CreateOption<int?>(
            "--reading-speed",
            "Override reading speed in characters per minute.");
        Option<double?> minimumDelayOption = CreateOption<double?>(
            "--min-delay-seconds",
            "Override the minimum delay between chapter fetches.");
        Option<double?> maximumDelayOption = CreateOption<double?>(
            "--max-delay-seconds",
            "Override the maximum delay between chapter fetches.");
        Option<int?> retryCountOption = CreateOption<int?>(
            "--retry-count",
            "Override the number of additional retries per chapter.");
        Option<int?> catalogCacheTtlOption = CreateOption<int?>(
            "--catalog-cache-ttl-hours",
            "Override the catalog cache time-to-live in hours.");

        Command command = new("download", "Download one or more Qidian books to Markdown.")
        {
            bookReferencesArgument,
            browserPathOption,
            browserProfileDirOption,
            outputDirOption,
            dryRunOption,
            overwriteOption,
            readingSpeedOption,
            minimumDelayOption,
            maximumDelayOption,
            retryCountOption,
            catalogCacheTtlOption,
        };
        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            commandService.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = parseResult.GetValue(bookReferencesArgument) ?? [],
                    BrowserPath = parseResult.GetValue(browserPathOption),
                    BrowserProfileDir = parseResult.GetValue(browserProfileDirOption),
                    OutputDir = parseResult.GetValue(outputDirOption),
                    DryRun = parseResult.GetValue(dryRunOption),
                    Overwrite = parseResult.GetValue(overwriteOption),
                    ReadingSpeed = parseResult.GetValue(readingSpeedOption),
                    MinimumRequestDelaySeconds = parseResult.GetValue(minimumDelayOption),
                    MaximumRequestDelaySeconds = parseResult.GetValue(maximumDelayOption),
                    RetryCount = parseResult.GetValue(retryCountOption),
                    CatalogCacheTtlHours = parseResult.GetValue(catalogCacheTtlOption),
                },
                cancellationToken));
        return command;
    }

    private static Command CreateLoginCommand(AppCommandService commandService)
    {
        Option<string?> browserPathOption = CreateStringOption(
            "--browser-path",
            "Override the browser executable path for this invocation.");
        Option<string?> browserProfileDirOption = CreateStringOption(
            "--browser-profile-dir",
            "Override the browser profile directory for this invocation.");

        Command command = new("login", "Open a visible browser window and persist an authenticated session.")
        {
            browserPathOption,
            browserProfileDirOption,
        };
        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            commandService.LoginAsync(
                new LoginCommandOptions
                {
                    BrowserPath = parseResult.GetValue(browserPathOption),
                    BrowserProfileDir = parseResult.GetValue(browserProfileDirOption),
                },
                cancellationToken));
        return command;
    }

    private static Command CreateCacheClearCommand(AppCommandService commandService)
    {
        Argument<string?> bookReferenceArgument = new("book")
        {
            Description = "Optional numeric book id or canonical Qidian book URL.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Option<bool> catalogOnlyOption = new("--catalog-only")
        {
            Description = "Remove only catalog cache data and keep cached chapter bodies.",
        };

        Command command = new("cache-clear", "Clear downloader cache data.")
        {
            bookReferenceArgument,
            catalogOnlyOption,
        };
        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            commandService.CacheClearAsync(
                new CacheClearCommandOptions
                {
                    BookReference = parseResult.GetValue(bookReferenceArgument),
                    CatalogOnly = parseResult.GetValue(catalogOnlyOption),
                },
                cancellationToken));
        return command;
    }

    private static Command CreateInfoCommand(AppCommandService commandService)
    {
        Argument<string> bookReferenceArgument = new("book")
        {
            Description = "A numeric book id or canonical Qidian book URL.",
        };
        Option<string?> browserPathOption = CreateStringOption(
            "--browser-path",
            "Override the browser executable path for this invocation.");
        Option<string?> browserProfileDirOption = CreateStringOption(
            "--browser-profile-dir",
            "Override the browser profile directory for this invocation.");
        Option<int?> catalogCacheTtlOption = CreateOption<int?>(
            "--catalog-cache-ttl-hours",
            "Override the catalog cache time-to-live in hours.");

        Command command = new("info", "Display metadata and cache coverage for a book.")
        {
            bookReferenceArgument,
            browserPathOption,
            browserProfileDirOption,
            catalogCacheTtlOption,
        };
        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            commandService.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = parseResult.GetValue(bookReferenceArgument)!,
                    BrowserPath = parseResult.GetValue(browserPathOption),
                    BrowserProfileDir = parseResult.GetValue(browserProfileDirOption),
                    CatalogCacheTtlHours = parseResult.GetValue(catalogCacheTtlOption),
                },
                cancellationToken));
        return command;
    }

    private static Option<string?> CreateStringOption(string name, string description)
        => new(name)
        {
            Description = description,
        };

    private static Option<T> CreateOption<T>(string name, string description)
        => new(name)
        {
            Description = description,
        };
}
