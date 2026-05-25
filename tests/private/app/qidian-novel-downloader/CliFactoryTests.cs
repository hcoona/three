using System.Reflection;
using Hcoona.QidianNovelDownloader.Browser;
using Hcoona.QidianNovelDownloader.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.CommandLine;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class CliFactoryTests
{
    private static readonly string[] DownloadWithConfigWithoutValueArgs =
        ["download", "--config", "--dry-run"];
    private static readonly string[] LoginWithEmptyConfigValueArgs = ["--config=", "login"];

    [Fact]
    public void CreateRootCommandAllowsNonExistentConfigOverridePath()
    {
        AppCommandService commandService = new(
            Options.Create(new AppSettings()),
            new QidianBrowserManager(NullLogger<QidianBrowserManager>.Instance),
            new AcceptAllConsole(),
            TimeProvider.System,
            new AppStorageService(),
            NullLogger<AppCommandService>.Instance);
        RootCommand rootCommand = CliFactory.CreateRootCommand(
            new StaticServiceProvider(commandService));

        ParseResult parseResult = rootCommand.Parse(
            ["--config", "Q:\\does-not-exist\\config.json", "login"]);

        Assert.Empty(parseResult.Errors);
    }

    public static TheoryData<string[]> ConfigOptionWithoutUsableValueArgs =>
        new()
        {
            DownloadWithConfigWithoutValueArgs,
            LoginWithEmptyConfigValueArgs,
        };

    [Theory]
    [MemberData(nameof(ConfigOptionWithoutUsableValueArgs))]
    public void NormalizeArgsForCommandLineParsingAllowsConfigOptionWithoutUsableValue(
        string[] args)
    {
        AppCommandService commandService = new(
            Options.Create(new AppSettings()),
            new QidianBrowserManager(NullLogger<QidianBrowserManager>.Instance),
            new AcceptAllConsole(),
            TimeProvider.System,
            new AppStorageService(),
            NullLogger<AppCommandService>.Instance);
        RootCommand rootCommand = CliFactory.CreateRootCommand(
            new StaticServiceProvider(commandService));
        MethodInfo method = typeof(AppSettings).Assembly
            .GetType("Hcoona.QidianNovelDownloader.Program")!
            .GetMethod(
                "NormalizeArgsForCommandLineParsing",
                BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] parseArgs = (string[])method.Invoke(null, [args])!;

        ParseResult parseResult = rootCommand.Parse(parseArgs);

        Assert.Empty(parseResult.Errors);
    }

    [Theory]
    [InlineData(new[] { "--config", "Q:\\temp\\config.json" }, "Q:\\temp\\config.json")]
    [InlineData(new[] { "--config=Q:\\temp\\config.json" }, "Q:\\temp\\config.json")]
    [InlineData(new[] { "--config", "--browser-path" }, null)]
    [InlineData(new[] { "--config=" }, null)]
    [InlineData(new[] { "--config", "   " }, null)]
    public void TryGetConfigPathOverrideValidatesValue(string[] args, string? expected)
    {
        MethodInfo method = typeof(AppSettings).Assembly
            .GetType("Hcoona.QidianNovelDownloader.Program")!
            .GetMethod("TryGetConfigPathOverride", BindingFlags.NonPublic | BindingFlags.Static)!;

        string? actual = (string?)method.Invoke(null, [args]);

        Assert.Equal(expected, actual);
    }

    private sealed class AcceptAllConsole : IInteractiveConsole
    {
        public Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class StaticServiceProvider(AppCommandService commandService) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(AppCommandService) ? commandService : null;
    }
}
