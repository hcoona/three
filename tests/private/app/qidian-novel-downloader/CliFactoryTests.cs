using Hcoona.QidianNovelDownloader.Browser;
using Hcoona.QidianNovelDownloader.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.CommandLine;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class CliFactoryTests
{
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
