using System.CommandLine;
using Hcoona.VsCodeCopilotTelegramHook.Commands;
using Hcoona.VsCodeCopilotTelegramHook.Notifications;
using Hcoona.VsCodeCopilotTelegramHook.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<WorkspaceStateStore>();
        builder.Services.AddSingleton<InstructionTemplateProvider>();
        builder.Services.AddSingleton<HookCommandService>();
        builder.Services.AddSingleton<UserCommandService>();

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
