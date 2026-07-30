using System.CommandLine;
using System.CommandLine.Help;

namespace Hcoona.DocumentTranslatorCli;

internal static class DocumentTranslatorCommandLineParser
{
    private const string RootHelpText =
        "Usage:\n"
        + "  document-translator translate --input <path> --output <path> "
        + "--target-language <language-code> [options]\n"
        + "\n"
        + "Commands:\n"
        + "  translate    Translate one local document.";

    private const string TranslateHelpText =
        "Usage:\n"
        + "  document-translator translate --input <path> --output <path> "
        + "--target-language <language-code> [options]\n"
        + "\n"
        + "Options:\n"
        + "  --input <path>                    Local source document path.\n"
        + "  --output <path>                   Local translated document path.\n"
        + "  --target-language <language-code> Azure target language code.\n"
        + "  --auth-mode <api-key|entra-id>    Authentication mode. Defaults to api-key.\n"
        + "  --endpoint <uri>                  Azure Document Translation endpoint.\n"
        + "  --key <api-key>                   Azure Translator API key.\n"
        + "  --region <region>                 API-key text translation resource region.\n"
        + "  --markdown-mode <auto|aware|legacy> Markdown routing mode. Defaults to auto.\n"
        + "  --force                           Replace an existing output file.\n"
        + "  -h, --help                        Show help.";

    private static readonly CommandLineDefinition Definition = CreateDefinition();

    public static CommandLineParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return Error("Specify the 'translate' command.");
        }

        if (!StringComparer.Ordinal.Equals(args[0], "translate"))
        {
            return IsHelp(args[0])
                ? Help(Definition.RootCommand)
                : Error("Unknown command. Specify the 'translate' command.");
        }

        ParseResult parseResult = Definition.RootCommand.Parse(args);
        if (parseResult.Action is HelpAction)
        {
            return Help(parseResult.CommandResult.Command);
        }

        if (parseResult.Errors.Count > 0)
        {
            return new CommandLineParseResult(
                EmptyOptions(),
                GetSafeErrors(args),
                ShowHelp: false,
                HelpText: string.Empty);
        }

        RawCommandLineOptions options = new(
            parseResult.GetValue(Definition.InputOption),
            parseResult.GetValue(Definition.OutputOption),
            parseResult.GetValue(Definition.TargetLanguageOption),
            parseResult.GetValue(Definition.AuthModeOption),
            parseResult.GetValue(Definition.EndpointOption),
            parseResult.GetValue(Definition.ApiKeyOption),
            parseResult.GetValue(Definition.MarkdownModeOption),
            parseResult.GetValue(Definition.ForceOption),
            IsSpecified(parseResult, Definition.TargetLanguageOption),
            IsSpecified(parseResult, Definition.AuthModeOption),
            IsSpecified(parseResult, Definition.EndpointOption),
            IsSpecified(parseResult, Definition.MarkdownModeOption),
            parseResult.GetValue(Definition.RegionOption),
            IsSpecified(parseResult, Definition.RegionOption));
        return new CommandLineParseResult(options, [], ShowHelp: false, HelpText: string.Empty);
    }

    private static IReadOnlyList<string> GetSafeErrors(string[] args)
    {
        foreach (string argument in args)
        {
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                int valueSeparatorIndex = argument.IndexOf('=', StringComparison.Ordinal);
                string optionName = valueSeparatorIndex < 0
                    ? argument
                    : argument[..valueSeparatorIndex];
                if (!Definition.KnownOptionNames.Contains(optionName))
                {
                    return [$"Unknown option '{optionName}'."];
                }
            }
        }

        return ["Command line arguments are invalid."];
    }

    private static bool IsHelp(string argument) =>
        StringComparer.Ordinal.Equals(argument, "--help")
        || StringComparer.Ordinal.Equals(argument, "-h");

    private static bool IsSpecified(ParseResult parseResult, Option option) =>
        parseResult.GetResult(option) is { Implicit: false };

    private static CommandLineParseResult Help(Command command)
    {
        string helpText = ReferenceEquals(command, Definition.TranslateCommand)
            ? TranslateHelpText
            : RootHelpText;
        return new CommandLineParseResult(EmptyOptions(), [], ShowHelp: true, helpText);
    }

    private static CommandLineParseResult Error(string error) =>
        new(EmptyOptions(), [error], ShowHelp: false, HelpText: string.Empty);

    private static RawCommandLineOptions EmptyOptions() =>
        new(null, null, null, null, null, null, null, Force: false);

    private static CommandLineDefinition CreateDefinition()
    {
        Option<string> inputOption = new("--input")
        {
            Description = "Local source document path.",
            HelpName = "path",
        };
        Option<string> outputOption = new("--output")
        {
            Description = "Local translated document path.",
            HelpName = "path",
        };
        Option<string> targetLanguageOption = new("--target-language")
        {
            Description = "Azure target language code.",
            HelpName = "language-code",
        };
        Option<string> authModeOption = new("--auth-mode")
        {
            Description = "Authentication mode. Defaults to api-key.",
            HelpName = "api-key|entra-id",
        };
        Option<string> endpointOption = new("--endpoint")
        {
            Description = "Azure Document Translation endpoint.",
            HelpName = "uri",
        };
        Option<string> apiKeyOption = new("--key")
        {
            Description = "Azure Translator API key.",
            HelpName = "api-key",
        };
        Option<string> regionOption = new("--region")
        {
            Description = "API-key text translation resource region.",
            HelpName = "region",
        };
        Option<string> markdownModeOption = new("--markdown-mode")
        {
            Description = "Markdown routing mode. Defaults to auto.",
            HelpName = "auto|aware|legacy",
        };
        Option<bool> forceOption = new("--force")
        {
            Description = "Replace an existing output file.",
        };

        Command translateCommand = new("translate", "Translate one local document.")
        {
            TreatUnmatchedTokensAsErrors = true,
        };
        translateCommand.Add(inputOption);
        translateCommand.Add(outputOption);
        translateCommand.Add(targetLanguageOption);
        translateCommand.Add(authModeOption);
        translateCommand.Add(endpointOption);
        translateCommand.Add(apiKeyOption);
        translateCommand.Add(regionOption);
        translateCommand.Add(markdownModeOption);
        translateCommand.Add(forceOption);

        RootCommand rootCommand = new("Translate one local document.")
        {
            TreatUnmatchedTokensAsErrors = true,
        };
        rootCommand.Add(translateCommand);

        HashSet<string> knownOptionNames = new(StringComparer.Ordinal)
        {
            "--input",
            "--output",
            "--target-language",
            "--auth-mode",
            "--endpoint",
            "--key",
            "--region",
            "--markdown-mode",
            "--force",
            "--help",
            "-h",
        };
        return new CommandLineDefinition(
            rootCommand,
            translateCommand,
            inputOption,
            outputOption,
            targetLanguageOption,
            authModeOption,
            endpointOption,
            apiKeyOption,
            regionOption,
            markdownModeOption,
            forceOption,
            knownOptionNames);
    }

    private sealed record CommandLineDefinition(
        RootCommand RootCommand,
        Command TranslateCommand,
        Option<string> InputOption,
        Option<string> OutputOption,
        Option<string> TargetLanguageOption,
        Option<string> AuthModeOption,
        Option<string> EndpointOption,
        Option<string> ApiKeyOption,
        Option<string> RegionOption,
        Option<string> MarkdownModeOption,
        Option<bool> ForceOption,
        IReadOnlySet<string> KnownOptionNames);
}
