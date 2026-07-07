using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Cli;

internal static class CliApplication
{
    private const string CommandName = "azureauth-credprovider";
    private const string PhaseName = "7-cli-shell";
    private const int SuccessExitCode = 0;
    private const int NotImplementedExitCode = 1;
    private const int UsageExitCode = 2;
    private const int FatalExitCode = 70;
    private const string FatalErrorMessage = "error: unexpected fatal failure.";

    private static readonly string[] SupportedEcosystems = ["git", "nuget", "python", "npm"];
    private static readonly HashSet<string> SecretLikeOptionNames = new(StringComparer.Ordinal)
    {
        "--access-token",
        "--client-secret",
        "--password",
        "--pat",
        "--secret",
        "--token",
    };

    private static readonly HashSet<string> ValuelessOptionNames = new(StringComparer.Ordinal)
    {
        "-h",
        "--help",
        "--dry-run",
    };

    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        SecretRedactor redactor = CreateRedactor(args);
        try
        {
            CliInvocation invocation = Parse(args);
            if (invocation.HelpText is not null)
            {
                WriteText(stdout, invocation.HelpText);
                return SuccessExitCode;
            }

            return invocation.Command switch
            {
                CliCommand.Status => HandleStatus(invocation, stdout),
                CliCommand.Configure => HandleConfigure(invocation, stdout, stderr),
                CliCommand.Unconfigure => HandleUnconfigure(invocation, stdout, stderr),
                CliCommand.Doctor or CliCommand.Login or CliCommand.Logout => HandlePhaseStub(
                    invocation,
                    stderr),
                _ => throw new InvalidOperationException("Unsupported CLI command."),
            };
        }
        catch (CliUsageException ex)
        {
            TryWriteDiagnosticText(stderr, ex.Message);
            return ex.ExitCode;
        }
        catch (Exception)
        {
            WriteFatalError(stderr, redactor);
            return FatalExitCode;
        }
    }

    private static int HandleStatus(CliInvocation invocation, TextWriter stdout)
    {
        WriteText(stdout, BuildStatusOutput(invocation.CiMode));
        return SuccessExitCode;
    }

    private static int HandleConfigure(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (!invocation.DryRun)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: configure without '--dry-run' is not implemented in phase 7.");
            return NotImplementedExitCode;
        }

        WriteText(stdout, BuildDryRunOutput(invocation));
        return SuccessExitCode;
    }

    private static int HandleUnconfigure(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (!invocation.DryRun)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: unconfigure without '--dry-run' is not implemented in phase 7.");
            return NotImplementedExitCode;
        }

        WriteText(stdout, BuildDryRunOutput(invocation));
        return SuccessExitCode;
    }

    private static int HandlePhaseStub(CliInvocation invocation, TextWriter stderr)
    {
        TryWriteDiagnosticText(
            stderr,
            $"error: {invocation.CommandName} is not implemented in phase 7.");
        return NotImplementedExitCode;
    }

    private static CliInvocation Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return CliInvocation.CreateHelp(BuildRootHelp());
        }

        ThrowIfValuelessOptionHasAssignedValue(args[0]);
        if (IsHelpToken(args[0]))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildRootHelp());
        }

        string commandToken = args[0];
        if (IsOptionToken(commandToken))
        {
            throw CreateUnknownOptionError(commandToken);
        }

        IReadOnlyList<string> remainingArgs = args.Skip(1).ToArray();
        return NormalizeCommand(commandToken) switch
        {
            CliCommand.Status => ParseStatus(remainingArgs),
            CliCommand.Doctor => ParsePhaseStub(CliCommand.Doctor, remainingArgs),
            CliCommand.Login => ParsePhaseStub(CliCommand.Login, remainingArgs),
            CliCommand.Logout => ParsePhaseStub(CliCommand.Logout, remainingArgs),
            CliCommand.Configure => ParseConfigurationCommand(CliCommand.Configure, remainingArgs),
            CliCommand.Unconfigure =>
                ParseConfigurationCommand(CliCommand.Unconfigure, remainingArgs),
            _ => throw CreateUsageError(
                $"error: command is not recognized. Run '{CommandName} --help' for usage."),
        };
    }

    private static CliInvocation ParseStatus(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildStatusHelp());
        }

        var ciMode = CliCiMode.None;
        var ciSpecified = false;

        for (var index = 0; index < args.Count; index++)
        {
            string token = args[index];
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildStatusHelp());
            }

            if (ciSpecified && IsCiOptionToken(token))
            {
                throw CreateUsageError(
                    "error: option '--ci' cannot be specified more than once.");
            }

            if (TryParseCiMode(args, ref index, out CliCiMode parsedCiMode))
            {
                ciSpecified = true;
                ciMode = parsedCiMode;
                continue;
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                "error: status does not accept positional arguments. "
                + $"Run '{CommandName} status --help' for usage.");
        }

        return new CliInvocation(CliCommand.Status, null, ciMode, DryRun: false, HelpText: null);
    }

    private static CliInvocation ParsePhaseStub(CliCommand command, IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildPhaseStubHelp(command));
        }

        string commandName = GetCommandName(command);
        foreach (string token in args)
        {
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildPhaseStubHelp(command));
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                $"error: {commandName} does not accept positional arguments. "
                + $"Run '{CommandName} {commandName} --help' for usage.");
        }

        return new CliInvocation(command, null, CliCiMode.None, DryRun: false, HelpText: null);
    }

    private static CliInvocation ParseConfigurationCommand(
        CliCommand command,
        IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildConfigurationHelp(command));
        }

        var ciMode = CliCiMode.None;
        var ciSpecified = false;
        var dryRun = false;
        CredentialEcosystem? ecosystem = null;
        string commandName = GetCommandName(command);

        for (var index = 0; index < args.Count; index++)
        {
            string token = args[index];
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildConfigurationHelp(command));
            }

            if (string.Equals(token, "--dry-run", StringComparison.Ordinal))
            {
                dryRun = true;
                continue;
            }

            if (ciSpecified && IsCiOptionToken(token))
            {
                throw CreateUsageError(
                    "error: option '--ci' cannot be specified more than once.");
            }

            if (TryParseCiMode(args, ref index, out CliCiMode parsedCiMode))
            {
                ciSpecified = true;
                ciMode = parsedCiMode;
                continue;
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            if (ecosystem is null)
            {
                ecosystem = ParseEcosystem(token);
                continue;
            }

            throw CreateUsageError(
                $"error: {commandName} accepts exactly one <ecosystem> argument. "
                + $"Run '{CommandName} {commandName} --help' for usage.");
        }

        if (ecosystem is null)
        {
            throw CreateUsageError(
                "error: missing required <ecosystem> argument. "
                + $"Run '{CommandName} {commandName} --help' for usage.");
        }

        return new CliInvocation(command, ecosystem.Value, ciMode, dryRun, HelpText: null);
    }

    private static SecretRedactor CreateRedactor(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        List<string> secrets = [];
        string? pendingSecretOption = null;
        foreach (string token in args)
        {
            if (pendingSecretOption is not null)
            {
                if (!IsOptionToken(token))
                {
                    secrets.Add(token);
                }

                pendingSecretOption = null;
            }

            string optionName = GetOptionName(token);
            if (SecretLikeOptionNames.Contains(optionName))
            {
                string? optionValue = GetOptionValue(token);
                if (!string.IsNullOrEmpty(optionValue))
                {
                    secrets.Add(optionValue);
                }
                else
                {
                    pendingSecretOption = optionName;
                }
            }
        }

        return secrets.Count == 0 ? SecretRedactor.Empty : new SecretRedactor(secrets);
    }

    private static bool TryParseCiMode(
        IReadOnlyList<string> args,
        ref int index,
        out CliCiMode ciMode)
    {
        string token = args[index];
        int assignmentIndex = GetOptionAssignmentIndex(token);
        if (assignmentIndex >= 0
            && string.Equals(token[..assignmentIndex], "--ci", StringComparison.Ordinal))
        {
            string? value = GetOptionValue(token);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw CreateUsageError(
                    "error: option '--ci' requires a value: none or azure-pipelines.");
            }

            ciMode = ParseCiMode(value);
            return true;
        }

        if (string.Equals(token, "--ci", StringComparison.Ordinal))
        {
            if (index + 1 >= args.Count
                || IsOptionToken(args[index + 1])
                || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw CreateUsageError(
                    "error: option '--ci' requires a value: none or azure-pipelines.");
            }

            index++;
            ciMode = ParseCiMode(args[index]);
            return true;
        }

        ciMode = default;
        return false;
    }

    private static CliCiMode ParseCiMode(string value)
    {
        return value switch
        {
            { } v when string.Equals(v, "none", StringComparison.OrdinalIgnoreCase) =>
                CliCiMode.None,
            { } v when string.Equals(v, "azure-pipelines", StringComparison.OrdinalIgnoreCase) =>
                CliCiMode.AzurePipelines,
            _ => throw CreateUsageError(
                "error: option '--ci' must be one of: none, azure-pipelines."),
        };
    }

    private static CredentialEcosystem ParseEcosystem(string token)
    {
        return token switch
        {
            { } value when string.Equals(value, "git", StringComparison.OrdinalIgnoreCase) =>
                CredentialEcosystem.Git,
            { } value when string.Equals(value, "nuget", StringComparison.OrdinalIgnoreCase) =>
                CredentialEcosystem.NuGet,
            { } value when string.Equals(value, "python", StringComparison.OrdinalIgnoreCase) =>
                CredentialEcosystem.Python,
            { } value when string.Equals(value, "npm", StringComparison.OrdinalIgnoreCase) =>
                CredentialEcosystem.Npm,
            _ => throw CreateUsageError(
                "error: ecosystem must be one of: git, nuget, python, npm."),
        };
    }

    private static CliCommand NormalizeCommand(string token)
    {
        return token switch
        {
            { } value when string.Equals(value, "status", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Status,
            { } value when string.Equals(value, "doctor", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Doctor,
            { } value when string.Equals(value, "login", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Login,
            { } value when string.Equals(value, "logout", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Logout,
            { } value when string.Equals(value, "configure", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Configure,
            { } value
                when string.Equals(value, "unconfigure", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Unconfigure,
            _ => CliCommand.Unknown,
        };
    }

    private static CliUsageException CreateUnknownOptionError(string token)
    {
        return CreateUsageError(
            $"error: option '{SanitizeDisplayedOptionToken(token)}' "
            + "is not supported for this command.");
    }

    private static void ThrowIfAnyValuelessOptionHasAssignedValue(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (string token in args)
        {
            ThrowIfValuelessOptionHasAssignedValue(token);
        }
    }

    private static void ThrowIfValuelessOptionHasAssignedValue(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (GetOptionAssignmentIndex(token) < 0)
        {
            return;
        }

        if (!ValuelessOptionNames.Contains(GetOptionName(token)))
        {
            return;
        }

        throw CreateUsageError(
            $"error: option '{SanitizeOptionToken(token)}' does not accept a value.");
    }

    private static CliUsageException CreateUsageError(string message)
    {
        return new CliUsageException(message, UsageExitCode);
    }

    private static void WriteFatalError(
        TextWriter stderr,
        SecretRedactor redactor,
        string? details = null)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(redactor);

        if (!TryWriteDiagnosticText(stderr, FatalErrorMessage))
        {
            return;
        }

        if (!string.IsNullOrEmpty(details))
        {
            TryWriteDiagnosticText(stderr, redactor.Redact(details)!);
        }
    }

    private static string BuildRootHelp()
    {
        return JoinLines(
            CommandName,
            "Usage:",
            $"  {CommandName} <command> [options]",
            string.Empty,
            "Commands:",
            "  status                       Show deterministic Phase 7 shell status.",
            "  doctor                       Phase 7 stub; not implemented yet.",
            "  login                        Phase 7 stub; not implemented yet.",
            "  logout                       Phase 7 stub; not implemented yet.",
            "  configure <ecosystem>        Phase 7 dry-run only for git, nuget, python, or npm.",
            "  unconfigure <ecosystem>      Phase 7 dry-run only for git, nuget, python, or npm.",
            string.Empty,
            "Options:",
            "  -h, --help                   Show help.",
            string.Empty,
            "Examples:",
            $"  {CommandName} status",
            $"  {CommandName} status --ci azure-pipelines",
            $"  {CommandName} configure git --dry-run",
            $"  {CommandName} unconfigure npm --dry-run");
    }

    private static string BuildStatusHelp()
    {
        return JoinLines(
            $"{CommandName} status",
            "Usage:",
            $"  {CommandName} status [--ci <mode>] [--help]",
            string.Empty,
            "Options:",
            "  --ci <mode>                  Select CI mode explicitly: none | azure-pipelines.",
            "  -h, --help                   Show help.");
    }

    private static string BuildConfigurationHelp(CliCommand command)
    {
        string commandName = GetCommandName(command);
        return JoinLines(
            $"{CommandName} {commandName}",
            "Usage:",
            $"  {CommandName} {commandName} <ecosystem> --dry-run [--ci <mode>] [--help]",
            string.Empty,
            "Ecosystems:",
            "  git",
            "  nuget",
            "  python",
            "  npm",
            string.Empty,
            "Options:",
            "  --dry-run                    Required in phase 7; render deterministic "
            + "no-mutation output.",
            "  --ci <mode>                  Select CI mode explicitly: none | azure-pipelines.",
            "  -h, --help                   Show help.");
    }

    private static string BuildPhaseStubHelp(CliCommand command)
    {
        string commandName = GetCommandName(command);
        return JoinLines(
            $"{CommandName} {commandName}",
            "Usage:",
            $"  {CommandName} {commandName} [--help]",
            string.Empty,
            "Status:",
            "  Phase 7 stub only. This command is not implemented yet.",
            string.Empty,
            "Options:",
            "  -h, --help                   Show help.");
    }

    private static string BuildStatusOutput(CliCiMode ciMode)
    {
        return JoinLines(
            "command: status",
            $"product: {CommandName}",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(ciMode)}",
            "status-shell: ready",
            "environment-probing: disabled",
            "persistent-cache: disabled",
            "dry-run-rendering: enabled",
            "mutating-commands: disabled",
            $"supported-ecosystems: {string.Join(", ", SupportedEcosystems)}");
    }

    private static string BuildDryRunOutput(CliInvocation invocation)
    {
        CredentialEcosystem ecosystem = invocation.Ecosystem
            ?? throw new InvalidOperationException("Dry-run commands require an ecosystem.");
        string[] actions = GetPlannedActions(invocation.Command, ecosystem, invocation.CiMode);

        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            $"ecosystem: {GetEcosystemText(ecosystem)}",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: no",
            "planned-actions:",
        ];

        for (var index = 0; index < actions.Length; index++)
        {
            lines.Add($"  {index + 1}. {actions[index]}");
        }

        lines.Add("note: no files, credentials, or caches are changed in phase 7");
        return JoinLines(lines);
    }

    private static string[] GetPlannedActions(
        CliCommand command,
        CredentialEcosystem ecosystem,
        CliCiMode ciMode)
    {
        bool configure = command == CliCommand.Configure;
        bool ciTemporary = ciMode == CliCiMode.AzurePipelines;

        return ecosystem switch
        {
            CredentialEcosystem.Git => configure
                ? ciTemporary
                    ? [
                        "prepare temporary Azure Pipelines git credential helper scaffold",
                        "prepare temporary dev.azure.com useHttpPath scaffold",
                    ]
                    : [
                        "register product-owned git credential helper scaffold",
                        "set product-owned dev.azure.com useHttpPath scaffold",
                    ]
                : ciTemporary
                    ? [
                        "remove temporary Azure Pipelines git credential helper scaffold",
                        "remove temporary dev.azure.com useHttpPath scaffold",
                    ]
                    : [
                        "remove product-owned git credential helper scaffold",
                        "remove product-owned dev.azure.com useHttpPath scaffold",
                    ],
            CredentialEcosystem.NuGet => configure
                ? ciTemporary
                    ? [
                        "prepare temporary Azure Pipelines NuGet plugin discovery scaffold",
                        "prepare temporary Azure Artifacts NuGet credential scaffold",
                    ]
                    : [
                        "register product-owned NuGet plugin discovery scaffold",
                        "register product-owned Azure Artifacts NuGet credential scaffold",
                    ]
                : ciTemporary
                    ? [
                        "remove temporary Azure Pipelines NuGet plugin discovery scaffold",
                        "remove temporary Azure Artifacts NuGet credential scaffold",
                    ]
                    : [
                        "remove product-owned NuGet plugin discovery scaffold",
                        "remove product-owned Azure Artifacts NuGet credential scaffold",
                    ],
            CredentialEcosystem.Python => configure
                ? ciTemporary
                    ? [
                        "prepare temporary Azure Pipelines Python keyring backend scaffold",
                        "prepare temporary Python keyring helper scaffold",
                    ]
                    : [
                        "register product-owned Python keyring backend scaffold",
                        "register product-owned Python keyring helper scaffold",
                    ]
                : ciTemporary
                    ? [
                        "remove temporary Azure Pipelines Python keyring backend scaffold",
                        "remove temporary Python keyring helper scaffold",
                    ]
                    : [
                        "remove product-owned Python keyring backend scaffold",
                        "remove product-owned Python keyring helper scaffold",
                    ],
            CredentialEcosystem.Npm => configure
                ? ciTemporary
                    ? [
                        "prepare temporary Azure Pipelines npm auth refresh scaffold",
                        "prepare temporary npm registry credential scaffold",
                    ]
                    : [
                        "register product-owned npm auth refresh scaffold",
                        "register product-owned npm registry credential scaffold",
                    ]
                : ciTemporary
                    ? [
                        "remove temporary Azure Pipelines npm auth refresh scaffold",
                        "remove temporary npm registry credential scaffold",
                    ]
                    : [
                        "remove product-owned npm auth refresh scaffold",
                        "remove product-owned npm registry credential scaffold",
                    ],
            _ => throw new InvalidOperationException("Unsupported dry-run ecosystem."),
        };
    }

    private static string GetCommandName(CliCommand command)
    {
        return command switch
        {
            CliCommand.Status => "status",
            CliCommand.Doctor => "doctor",
            CliCommand.Login => "login",
            CliCommand.Logout => "logout",
            CliCommand.Configure => "configure",
            CliCommand.Unconfigure => "unconfigure",
            _ => "unknown",
        };
    }

    private static string GetCiModeText(CliCiMode ciMode)
    {
        return ciMode switch
        {
            CliCiMode.None => "none",
            CliCiMode.AzurePipelines => "azure-pipelines",
            _ => throw new InvalidOperationException("Unsupported CI mode."),
        };
    }

    private static string GetScopeText(CliCiMode ciMode)
    {
        return ciMode == CliCiMode.AzurePipelines ? "ci-temporary" : "user";
    }

    private static string GetEcosystemText(CredentialEcosystem ecosystem)
    {
        return ecosystem switch
        {
            CredentialEcosystem.Git => "git",
            CredentialEcosystem.NuGet => "nuget",
            CredentialEcosystem.Python => "python",
            CredentialEcosystem.Npm => "npm",
            _ => throw new InvalidOperationException("Unsupported ecosystem."),
        };
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join("\n", lines) + "\n";
    }

    private static string JoinLines(params string[] lines)
    {
        return JoinLines((IEnumerable<string>)lines);
    }

    private static bool IsHelpToken(string token)
    {
        return string.Equals(token, "-h", StringComparison.Ordinal)
            || string.Equals(token, "--help", StringComparison.Ordinal);
    }

    private static bool ContainsStandaloneHelpToken(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (string token in args)
        {
            if (IsHelpToken(token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOptionToken(string token)
    {
        return token.StartsWith('-');
    }

    private static bool IsCiOptionToken(string token)
    {
        return string.Equals(GetOptionName(token), "--ci", StringComparison.Ordinal);
    }

    private static string SanitizeDisplayedOptionToken(string token)
    {
        return EscapeNonPrintingCharacters(GetDisplayedOptionName(token));
    }

    private static string SanitizeOptionToken(string token)
    {
        return EscapeNonPrintingCharacters(GetOptionName(token));
    }

    private static string EscapeNonPrintingCharacters(string value)
    {
        StringBuilder? builder = null;
        for (var index = 0; index < value.Length;)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            bool isSurrogatePair = char.IsHighSurrogate(value[index])
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]);
            int codeUnitLength = isSurrogatePair ? 2 : 1;
            int codePoint = isSurrogatePair ? char.ConvertToUtf32(value, index) : value[index];
            if (!ShouldEscapeDisplayedOptionCodePoint(category))
            {
                builder?.Append(value, index, codeUnitLength);
                index += codeUnitLength;
                continue;
            }

            builder ??= new StringBuilder(value.Length + 5).Append(value, 0, index);
            builder.Append(codePoint <= 0xFFFF ? @"\u" : @"\U");
            builder.Append(codePoint.ToString(codePoint <= 0xFFFF ? "X4" : "X8"));
            index += codeUnitLength;
        }

        return builder?.ToString() ?? value;
    }

    private static bool ShouldEscapeDisplayedOptionCodePoint(UnicodeCategory category)
    {
        return category is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned;
    }

    private static bool IsDisplayedOptionBoundary(
        string optionName,
        int index,
        UnicodeCategory category)
    {
        return char.IsWhiteSpace(optionName[index])
            || ShouldEscapeDisplayedOptionCodePoint(category);
    }

    private static string GetDisplayedOptionName(string token)
    {
        string optionName = GetOptionName(token);
        for (var index = 0; index < optionName.Length;)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(optionName, index);
            if (IsDisplayedOptionBoundary(optionName, index, category))
            {
                return optionName[..index];
            }

            bool isSurrogatePair = char.IsHighSurrogate(optionName[index])
                && index + 1 < optionName.Length
                && char.IsLowSurrogate(optionName[index + 1]);
            index += isSurrogatePair ? 2 : 1;
        }

        return optionName;
    }

    private static string GetOptionName(string token)
    {
        int assignmentIndex = GetOptionAssignmentIndex(token);
        return assignmentIndex >= 0 ? token[..assignmentIndex] : token;
    }

    private static string? GetOptionValue(string token)
    {
        int assignmentIndex = GetOptionAssignmentIndex(token);
        return assignmentIndex >= 0 && assignmentIndex + 1 < token.Length
            ? token[(assignmentIndex + 1)..]
            : null;
    }

    private static int GetOptionAssignmentIndex(string token)
    {
        int equalsIndex = token.IndexOf('=');
        int colonIndex = token.IndexOf(':');
        if (equalsIndex < 0)
        {
            return colonIndex;
        }

        if (colonIndex < 0)
        {
            return equalsIndex;
        }

        return Math.Min(equalsIndex, colonIndex);
    }

    private static void WriteText(TextWriter writer, string text)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(text);
        writer.Write(text);
        if (!text.EndsWith('\n'))
        {
            writer.Write('\n');
        }

        writer.Flush();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Stderr diagnostics must not override the intended process exit code.")]
    private static bool TryWriteDiagnosticText(TextWriter writer, string text)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            WriteText(writer, text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal sealed record CliInvocation(
    CliCommand Command,
    CredentialEcosystem? Ecosystem,
    CliCiMode CiMode,
    bool DryRun,
    string? HelpText)
{
    public string CommandName => CliApplicationCommandNames.Get(Command);

    public static CliInvocation CreateHelp(string helpText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helpText);
        return new CliInvocation(
            CliCommand.Help,
            null,
            CliCiMode.None,
            DryRun: false,
            HelpText: helpText);
    }
}

internal enum CliCommand
{
    Unknown = 0,
    Help = 1,
    Status = 2,
    Doctor = 3,
    Login = 4,
    Logout = 5,
    Configure = 6,
    Unconfigure = 7,
}

internal enum CliCiMode
{
    None = 0,
    AzurePipelines = 1,
}

internal static class CliApplicationCommandNames
{
    public static string Get(CliCommand command)
    {
        return command switch
        {
            CliCommand.Status => "status",
            CliCommand.Doctor => "doctor",
            CliCommand.Login => "login",
            CliCommand.Logout => "logout",
            CliCommand.Configure => "configure",
            CliCommand.Unconfigure => "unconfigure",
            CliCommand.Help => "help",
            _ => "unknown",
        };
    }
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
