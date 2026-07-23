using System.CommandLine;
using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Logging;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddFilter(static (category, logLevel) =>
            logLevel != LogLevel.None
            && logLevel >= LogLevel.Debug
            && SessionFileLoggerProvider.IsCategoryAllowed(category));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SessionLogFileContext>();
        builder.Services.AddSingleton<HookExecutionContext>();
        builder.Services.AddSingleton<ILoggerProvider, SessionFileLoggerProvider>();
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.AddSingleton<IInteractiveConsole, SystemInteractiveConsole>();
        builder.Services.AddSingleton<TelegramCredentialProvider>();
        builder.Services.AddSingleton<CopilotCliRuntimeProbe>();
        builder.Services.AddSingleton<GitRepositoryProbe>();
        builder.Services.AddSingleton<WorkspaceStateStore>();
        builder.Services.AddTransient<HookCommandService>();
        builder.Services.AddTransient<CopilotCliNotificationService>();
        builder.Services.AddTransient<UserCommandService>();

        builder.Services
            .AddHttpClient<TelegramBotClient>(static client =>
            {
                client.BaseAddress = new Uri("https://api.telegram.org/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "hcoona-vscode-copilot-telegram-hook/1.0");
            })
            .AddStandardResilienceHandler();

        using IHost host = builder.Build();
        RootCommand rootCommand = CliFactory.CreateRootCommand(host.Services);
        return await rootCommand.Parse(args).InvokeAsync();
    }
}
