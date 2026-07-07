using System.Text;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void NoArgumentsWritesRootHelp()
    {
        CommandResult result = Invoke();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                """
                azureauth-credprovider
                Usage:
                  azureauth-credprovider <command> [options]

                Commands:
                  status                       Show deterministic Phase 7 shell status.
                  doctor                       Phase 7 stub; not implemented yet.
                  login                        Phase 7 stub; not implemented yet.
                  logout                       Phase 7 stub; not implemented yet.
                  configure <ecosystem>        Phase 7 dry-run only for git, nuget, python, or npm.
                  unconfigure <ecosystem>      Phase 7 dry-run only for git, nuget, python, or npm.

                Options:
                  -h, --help                   Show help.

                Examples:
                  azureauth-credprovider status
                  azureauth-credprovider status --ci azure-pipelines
                  azureauth-credprovider configure git --dry-run
                  azureauth-credprovider unconfigure npm --dry-run
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("configure")]
    [InlineData("unconfigure")]
    [InlineData("doctor")]
    [InlineData("login")]
    [InlineData("logout")]
    public void CommandHelpWritesGoldenText(string command)
    {
        CommandResult result = Invoke(command, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(GetExpectedHelp(command), result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData("status", null, "-h", "unexpected")]
    [InlineData("configure", "git", "--help", "--bogus")]
    [InlineData("unconfigure", "python", "-h", "unexpected")]
    [InlineData("doctor", null, "--help", "--bogus")]
    [InlineData("login", null, "-h", "unexpected")]
    [InlineData("logout", null, "--help", "--bogus")]
    public void HelpShortCircuitsInvalidTrailingTokens(
        string command,
        string? argumentBeforeHelp,
        string helpToken,
        string invalidTrailingToken)
    {
        string[] args = argumentBeforeHelp is null
            ? [command, helpToken, invalidTrailingToken]
            : [command, argumentBeforeHelp, helpToken, invalidTrailingToken];

        CommandResult result = Invoke(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(GetExpectedHelp(command), result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(HelpAfterEarlierValidationErrorCases))]
    public void HelpShortCircuitsEarlierValidationErrors(string[] args, string command)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(GetExpectedHelp(command), result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(AssignedDryRunWithHelpCases))]
    public void AssignedDryRunValidationBeatsHelpShortCircuit(string[] args)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--dry-run' does not accept a value.\n",
            result.StdErr);
    }

    [Fact]
    public void StatusWritesDeterministicShellOutput()
    {
        CommandResult result = Invoke("status");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                """
                command: status
                product: azureauth-credprovider
                phase: 7-cli-shell
                ci-mode: none
                status-shell: ready
                environment-probing: disabled
                persistent-cache: disabled
                dry-run-rendering: enabled
                mutating-commands: disabled
                supported-ecosystems: git, nuget, python, npm
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData("--ci", "azure-pipelines")]
    [InlineData("--ci=azure-pipelines", null)]
    [InlineData("--ci:azure-pipelines", null)]
    public void StatusAllowsExplicitAzurePipelinesCiMode(string ciToken, string? ciValue)
    {
        string[] args = ciValue is null
            ? ["status", ciToken]
            : ["status", ciToken, ciValue];

        CommandResult result = Invoke(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                """
                command: status
                product: azureauth-credprovider
                phase: 7-cli-shell
                ci-mode: azure-pipelines
                status-shell: ready
                environment-probing: disabled
                persistent-cache: disabled
                dry-run-rendering: enabled
                mutating-commands: disabled
                supported-ecosystems: git, nuget, python, npm
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData(
        "configure",
        "git",
        "azure-pipelines",
        "prepare temporary Azure Pipelines git credential helper scaffold",
        "prepare temporary dev.azure.com useHttpPath scaffold")]
    [InlineData(
        "unconfigure",
        "git",
        "none",
        "remove product-owned git credential helper scaffold",
        "remove product-owned dev.azure.com useHttpPath scaffold")]
    public void DryRunCommandsAllowColonDelimitedCiMode(
        string command,
        string ecosystem,
        string ciMode,
        string plannedAction1,
        string plannedAction2)
    {
        CommandResult result = Invoke(command, ecosystem, "--dry-run", $"--ci:{ciMode}");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            GetExpectedDryRunOutput(command, ecosystem, ciMode, plannedAction1, plannedAction2),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void DryRunCommandsAllowEqualsDelimitedCiMode()
    {
        CommandResult result = Invoke("configure", "git", "--dry-run", "--ci=azure-pipelines");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            GetExpectedDryRunOutput(
                "configure",
                "git",
                "azure-pipelines",
                "prepare temporary Azure Pipelines git credential helper scaffold",
                "prepare temporary dev.azure.com useHttpPath scaffold"),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(DryRunGoldenCases))]
    public void DryRunCommandsWriteDeterministicOutput(
        string command,
        string ecosystem,
        string ciMode,
        string plannedAction1,
        string plannedAction2)
    {
        CommandResult result = Invoke(command, ecosystem, "--dry-run", "--ci", ciMode);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            GetExpectedDryRunOutput(command, ecosystem, ciMode, plannedAction1, plannedAction2),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(DryRunDefaultCiGoldenCases))]
    public void DryRunCommandsWithoutCiMatchExplicitNoneGolden(
        string command,
        string ecosystem,
        string plannedAction1,
        string plannedAction2)
    {
        CommandResult implicitCiResult = Invoke(command, ecosystem, "--dry-run");
        CommandResult explicitCiResult = Invoke(command, ecosystem, "--dry-run", "--ci", "none");
        string expectedOutput = GetExpectedDryRunOutput(
            command,
            ecosystem,
            "none",
            plannedAction1,
            plannedAction2);

        Assert.Equal(0, implicitCiResult.ExitCode);
        Assert.Equal(0, explicitCiResult.ExitCode);
        Assert.Equal(expectedOutput, implicitCiResult.StdOut);
        Assert.Equal(expectedOutput, explicitCiResult.StdOut);
        Assert.Equal(implicitCiResult.StdOut, explicitCiResult.StdOut);
        Assert.Equal(string.Empty, implicitCiResult.StdErr);
        Assert.Equal(string.Empty, explicitCiResult.StdErr);
    }

    [Theory]
    [InlineData(
        "configure",
        "git",
        "error: configure without '--dry-run' is not implemented in phase 7.\n")]
    [InlineData(
        "unconfigure",
        "git",
        "error: unconfigure without '--dry-run' is not implemented in phase 7.\n")]
    public void NonDryRunConfigurationCommandsReturnPhaseStubErrors(
        string command,
        string ecosystem,
        string expectedError)
    {
        CommandResult result = Invoke(command, ecosystem);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(expectedError, result.StdErr);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("login")]
    [InlineData("logout")]
    public void StubCommandsReturnNotImplementedErrors(string command)
    {
        CommandResult result = Invoke(command);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal($"error: {command} is not implemented in phase 7.\n", result.StdErr);
    }

    [Fact]
    public void UnknownCommandReturnsDeterministicUsageError()
    {
        CommandResult result = Invoke("surprise");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: command is not recognized. Run 'azureauth-credprovider --help' for usage.\n",
            result.StdErr);
    }

    [Theory]
    [MemberData(nameof(UnknownOptionCases))]
    public void UnknownOptionReturnsDeterministicUsageError(string[] args, string expectedError)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(expectedError, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(ValuelessOptionAssignmentCases))]
    public void ValuelessFlagsWithAssignedValuesReturnDeterministicUsageError(
        string[] args,
        string expectedError)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(expectedError, result.StdErr);
    }

    [Fact]
    public void InvalidEcosystemReturnsDeterministicUsageError()
    {
        CommandResult result = Invoke("configure", "cargo", "--dry-run");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: ecosystem must be one of: git, nuget, python, npm.\n",
            result.StdErr);
    }

    [Theory]
    [MemberData(nameof(InvalidCiModeCases))]
    public void InvalidCiModeReturnsDeterministicUsageError(string[] args, string expectedError)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(expectedError, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(DuplicateCiModeCases))]
    public void DuplicateCiModeReturnsDeterministicUsageError(string[] args)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--ci' cannot be specified more than once.\n",
            result.StdErr);
    }

    [Theory]
    [InlineData("configure")]
    [InlineData("unconfigure")]
    public void MissingEcosystemReturnsDeterministicUsageError(string command)
    {
        CommandResult result = Invoke(command, "--dry-run");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: missing required <ecosystem> argument. "
            + $"Run 'azureauth-credprovider {command} --help' for usage.\n",
            result.StdErr);
    }

    [Theory]
    [InlineData("configure")]
    [InlineData("unconfigure")]
    public void DuplicateEcosystemReturnsDeterministicUsageError(string command)
    {
        CommandResult result = Invoke(command, "git", "npm", "--dry-run");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            $"error: {command} accepts exactly one <ecosystem> argument. "
            + $"Run 'azureauth-credprovider {command} --help' for usage.\n",
            result.StdErr);
    }

    [Theory]
    [MemberData(nameof(ExtraPositionalArgumentCases))]
    public void CommandsRejectExtraPositionalArguments(string[] args, string expectedError)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(expectedError, result.StdErr);
    }

    [Theory]
    [InlineData("super-secret-token")]
    [InlineData("error")]
    [InlineData("option")]
    public void UnknownOptionWithInlineSecretValueDoesNotAlterStaticDiagnostics(string secret)
    {
        CommandResult result = Invoke("status", $"--token={secret}");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Theory]
    [InlineData("super-secret-token")]
    [InlineData("error")]
    [InlineData("option")]
    public void UnknownOptionWithColonDelimitedSecretValueDoesNotAlterStaticDiagnostics(
        string secret)
    {
        CommandResult result = Invoke("status", $"--token:{secret}");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Theory]
    [InlineData("super-secret-token")]
    [InlineData("error")]
    [InlineData("option")]
    public void UnknownOptionWithSplitSecretValueDoesNotAlterStaticDiagnostics(string secret)
    {
        CommandResult result = Invoke("status", "--token", secret);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Fact]
    public void
        UnknownOptionWithSingleTokenWhitespaceSeparatedSecretValueDoesNotAlterStaticDiagnostics()
    {
        CommandResult result = Invoke("status", "--token super-secret-token");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Fact]
    public void UnknownOptionWithSingleTokenTabSeparatedSecretValueDoesNotAlterStaticDiagnostics()
    {
        CommandResult result = Invoke("status", "--token\tsuper-secret-token");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Fact]
    public void
        UnknownOptionWithSingleTokenControlSeparatedSecretValueDoesNotAlterStaticDiagnostics()
    {
        CommandResult result = Invoke("status", "--token\u001Fsuper-secret-token");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Theory]
    [InlineData("--token\u202Esuper-secret-token")]
    [InlineData("--token\u2066super-secret-token")]
    public void UnknownOptionWithSingleTokenFormatSeparatedSecretValueDoesNotAlterStaticDiagnostics(
        string token)
    {
        CommandResult result = Invoke("status", token);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: option '--token' is not supported for this command.\n",
            result.StdErr);
    }

    [Theory]
    [InlineData("--bogus\nline=secret", "--bogus")]
    [InlineData("--bogus\u001B[31m=secret", "--bogus")]
    [InlineData("--bogus\u202Eline=secret", "--bogus")]
    [InlineData("--bogus\u2066line=secret", "--bogus")]
    public void UnknownOptionTruncatesDisplayedOptionNameAtUnsafeBoundary(
        string token,
        string expectedDisplayedOption)
    {
        CommandResult result = Invoke("status", token);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            $"error: option '{expectedDisplayedOption}' is not supported for this command.\n",
            result.StdErr);
    }

    [Fact]
    public void FatalPathDoesNotRedactFixedBanner()
    {
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = CliApplication.Run(
            ["--help", "--token", "error"],
            new ThrowingTextWriter(),
            stderr);

        Assert.Equal(70, exitCode);
        Assert.Equal(
            "error: unexpected fatal failure.\n",
            stderr.ToString());
    }

    [Fact]
    public void UsageErrorReturnsExitCodeWhenStderrWriterThrows()
    {
        int exitCode = CliApplication.Run(
            ["status", "--bogus"],
            new StringWriter(new StringBuilder()),
            new ThrowingTextWriter());

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void NotImplementedPathReturnsExitCodeWhenStderrWriterThrows()
    {
        int exitCode = CliApplication.Run(
            ["doctor"],
            new StringWriter(new StringBuilder()),
            new ThrowingTextWriter());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void FatalPathReturnsExitCodeWhenStderrWriterThrows()
    {
        int exitCode = CliApplication.Run(
            ["--help", "--token", "error"],
            new ThrowingTextWriter(),
            new ThrowingTextWriter());

        Assert.Equal(70, exitCode);
    }

    public static TheoryData<string, string, string, string, string> DryRunGoldenCases =>
        new()
        {
            {
                "configure",
                "git",
                "none",
                "register product-owned git credential helper scaffold",
                "set product-owned dev.azure.com useHttpPath scaffold"
            },
            {
                "configure",
                "git",
                "azure-pipelines",
                "prepare temporary Azure Pipelines git credential helper scaffold",
                "prepare temporary dev.azure.com useHttpPath scaffold"
            },
            {
                "configure",
                "nuget",
                "none",
                "register product-owned NuGet plugin discovery scaffold",
                "register product-owned Azure Artifacts NuGet credential scaffold"
            },
            {
                "configure",
                "nuget",
                "azure-pipelines",
                "prepare temporary Azure Pipelines NuGet plugin discovery scaffold",
                "prepare temporary Azure Artifacts NuGet credential scaffold"
            },
            {
                "configure",
                "python",
                "none",
                "register product-owned Python keyring backend scaffold",
                "register product-owned Python keyring helper scaffold"
            },
            {
                "configure",
                "python",
                "azure-pipelines",
                "prepare temporary Azure Pipelines Python keyring backend scaffold",
                "prepare temporary Python keyring helper scaffold"
            },
            {
                "configure",
                "npm",
                "none",
                "register product-owned npm auth refresh scaffold",
                "register product-owned npm registry credential scaffold"
            },
            {
                "configure",
                "npm",
                "azure-pipelines",
                "prepare temporary Azure Pipelines npm auth refresh scaffold",
                "prepare temporary npm registry credential scaffold"
            },
            {
                "unconfigure",
                "git",
                "none",
                "remove product-owned git credential helper scaffold",
                "remove product-owned dev.azure.com useHttpPath scaffold"
            },
            {
                "unconfigure",
                "git",
                "azure-pipelines",
                "remove temporary Azure Pipelines git credential helper scaffold",
                "remove temporary dev.azure.com useHttpPath scaffold"
            },
            {
                "unconfigure",
                "nuget",
                "none",
                "remove product-owned NuGet plugin discovery scaffold",
                "remove product-owned Azure Artifacts NuGet credential scaffold"
            },
            {
                "unconfigure",
                "nuget",
                "azure-pipelines",
                "remove temporary Azure Pipelines NuGet plugin discovery scaffold",
                "remove temporary Azure Artifacts NuGet credential scaffold"
            },
            {
                "unconfigure",
                "python",
                "none",
                "remove product-owned Python keyring backend scaffold",
                "remove product-owned Python keyring helper scaffold"
            },
            {
                "unconfigure",
                "python",
                "azure-pipelines",
                "remove temporary Azure Pipelines Python keyring backend scaffold",
                "remove temporary Python keyring helper scaffold"
            },
            {
                "unconfigure",
                "npm",
                "none",
                "remove product-owned npm auth refresh scaffold",
                "remove product-owned npm registry credential scaffold"
            },
            {
                "unconfigure",
                "npm",
                "azure-pipelines",
                "remove temporary Azure Pipelines npm auth refresh scaffold",
                "remove temporary npm registry credential scaffold"
            },
        };

    public static TheoryData<string, string, string, string> DryRunDefaultCiGoldenCases =>
        new()
        {
            {
                "configure",
                "git",
                "register product-owned git credential helper scaffold",
                "set product-owned dev.azure.com useHttpPath scaffold"
            },
            {
                "configure",
                "nuget",
                "register product-owned NuGet plugin discovery scaffold",
                "register product-owned Azure Artifacts NuGet credential scaffold"
            },
            {
                "configure",
                "python",
                "register product-owned Python keyring backend scaffold",
                "register product-owned Python keyring helper scaffold"
            },
            {
                "configure",
                "npm",
                "register product-owned npm auth refresh scaffold",
                "register product-owned npm registry credential scaffold"
            },
            {
                "unconfigure",
                "git",
                "remove product-owned git credential helper scaffold",
                "remove product-owned dev.azure.com useHttpPath scaffold"
            },
            {
                "unconfigure",
                "nuget",
                "remove product-owned NuGet plugin discovery scaffold",
                "remove product-owned Azure Artifacts NuGet credential scaffold"
            },
            {
                "unconfigure",
                "python",
                "remove product-owned Python keyring backend scaffold",
                "remove product-owned Python keyring helper scaffold"
            },
            {
                "unconfigure",
                "npm",
                "remove product-owned npm auth refresh scaffold",
                "remove product-owned npm registry credential scaffold"
            },
        };

    public static TheoryData<string[], string> InvalidCiModeCases =>
        new()
        {
            {
                ["status", "--ci"],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["status", "--ci="],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["status", "--ci:"],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["status", "--ci", "--bogus"],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["status", "--ci", ""],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["status", "--ci", "   "],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["status", "--ci", "github-actions"],
                "error: option '--ci' must be one of: none, azure-pipelines.\n"
            },
            {
                ["status", "--ci=github-actions"],
                "error: option '--ci' must be one of: none, azure-pipelines.\n"
            },
            {
                ["status", "--ci:github-actions"],
                "error: option '--ci' must be one of: none, azure-pipelines.\n"
            },
            {
                ["configure", "git", "--dry-run", "--ci:"],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["configure", "git", "--dry-run", "--ci", ""],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["configure", "git", "--dry-run", "--ci", "   "],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["configure", "git", "--dry-run", "--ci:github-actions"],
                "error: option '--ci' must be one of: none, azure-pipelines.\n"
            },
            {
                ["unconfigure", "git", "--dry-run", "--ci:"],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["unconfigure", "git", "--dry-run", "--ci", ""],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["unconfigure", "git", "--dry-run", "--ci", "   "],
                "error: option '--ci' requires a value: none or azure-pipelines.\n"
            },
            {
                ["unconfigure", "git", "--dry-run", "--ci:github-actions"],
                "error: option '--ci' must be one of: none, azure-pipelines.\n"
            },
        };

    public static TheoryData<string[], string> ValuelessOptionAssignmentCases =>
        new()
        {
            {
                ["--help=1"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["--help", "--help=1"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["-h:1"],
                "error: option '-h' does not accept a value.\n"
            },
            {
                ["status", "--help:1"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["status", "--help", "--help=1"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["status", "--help=1", "--help"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["configure", "git", "--help", "--help:1"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["unconfigure", "git", "--help:1", "--help"],
                "error: option '--help' does not accept a value.\n"
            },
            {
                ["configure", "git", "--dry-run=yes"],
                "error: option '--dry-run' does not accept a value.\n"
            },
            {
                ["configure", "git", "--dry-run=yes", "--help"],
                "error: option '--dry-run' does not accept a value.\n"
            },
            {
                ["unconfigure", "git", "--dry-run:yes"],
                "error: option '--dry-run' does not accept a value.\n"
            },
            {
                ["unconfigure", "git", "--dry-run=yes", "--help"],
                "error: option '--dry-run' does not accept a value.\n"
            },
        };

    public static TheoryData<string[], string> HelpAfterEarlierValidationErrorCases =>
        new()
        {
            { ["status", "--bogus", "--help"], "status" },
            { ["configure", "git", "--bogus", "--help"], "configure" },
            { ["unconfigure", "python", "--ci", "--help"], "unconfigure" },
            { ["doctor", "--bogus", "--help"], "doctor" },
            { ["login", "unexpected", "-h"], "login" },
            { ["logout", "--bogus", "--help"], "logout" },
        };

    public static TheoryData<string[]> AssignedDryRunWithHelpCases =>
        new()
        {
            { ["--help", "--dry-run="] },
            { ["-h", "--dry-run:"] },
            { ["status", "--help", "--dry-run="] },
            { ["status", "--dry-run:", "-h"] },
            { ["doctor", "--help", "--dry-run="] },
            { ["doctor", "--dry-run:", "-h"] },
            { ["login", "--help", "--dry-run="] },
            { ["login", "--dry-run:", "-h"] },
            { ["logout", "--help", "--dry-run="] },
            { ["logout", "--dry-run:", "-h"] },
        };

    public static TheoryData<string[], string> ExtraPositionalArgumentCases =>
        new()
        {
            {
                ["status", "unexpected"],
                "error: status does not accept positional arguments. "
                    + "Run 'azureauth-credprovider status --help' for usage.\n"
            },
            {
                ["status", "--ci", "none", "unexpected"],
                "error: status does not accept positional arguments. "
                    + "Run 'azureauth-credprovider status --help' for usage.\n"
            },
            {
                ["doctor", "unexpected"],
                "error: doctor does not accept positional arguments. "
                    + "Run 'azureauth-credprovider doctor --help' for usage.\n"
            },
            {
                ["login", "unexpected"],
                "error: login does not accept positional arguments. "
                    + "Run 'azureauth-credprovider login --help' for usage.\n"
            },
            {
                ["logout", "unexpected"],
                "error: logout does not accept positional arguments. "
                    + "Run 'azureauth-credprovider logout --help' for usage.\n"
            },
        };

    public static TheoryData<string[]> DuplicateCiModeCases =>
        new()
        {
            { ["status", "--ci", "none", "--ci=azure-pipelines"] },
            { ["status", "--ci=none", "--ci:azure-pipelines"] },
            { ["status", "--ci:none", "--ci", "azure-pipelines"] },
            { ["configure", "git", "--dry-run", "--ci", "none", "--ci=azure-pipelines"] },
            { ["configure", "git", "--dry-run", "--ci=none", "--ci:azure-pipelines"] },
            { ["configure", "git", "--dry-run", "--ci:none", "--ci", "azure-pipelines"] },
            { ["unconfigure", "git", "--dry-run", "--ci", "none", "--ci=azure-pipelines"] },
            { ["unconfigure", "git", "--dry-run", "--ci=none", "--ci:azure-pipelines"] },
            { ["unconfigure", "git", "--dry-run", "--ci:none", "--ci", "azure-pipelines"] },
        };

    public static TheoryData<string[], string> UnknownOptionCases =>
        new()
        {
            {
                ["--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
            {
                ["status", "--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
            {
                ["doctor", "--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
            {
                ["configure", "git", "--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
            {
                ["unconfigure", "git", "--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
        };

    private static string GetExpectedHelp(string command)
    {
        return Normalize(
            command switch
            {
                "status" =>
                    """
                    azureauth-credprovider status
                    Usage:
                      azureauth-credprovider status [--ci <mode>] [--help]

                    Options:
                    """
                    + "\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "configure" =>
                    """
                    azureauth-credprovider configure
                    Usage:
                      azureauth-credprovider configure <ecosystem> --dry-run [--ci <mode>] [--help]

                    Ecosystems:
                      git
                      nuget
                      python
                      npm

                    Options:
                    """
                    + "\n"
                    + "  --dry-run                    Required in phase 7; render deterministic "
                    + "no-mutation output.\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "unconfigure" =>
                    """
                    azureauth-credprovider unconfigure
                    Usage:
                    """
                    + "\n"
                    + "  azureauth-credprovider unconfigure <ecosystem> --dry-run [--ci <mode>] "
                    + "[--help]\n"
                    + """

                    Ecosystems:
                      git
                      nuget
                      python
                      npm

                    Options:
                    """
                    + "\n"
                    + "  --dry-run                    Required in phase 7; render deterministic "
                    + "no-mutation output.\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "doctor" =>
                    """
                    azureauth-credprovider doctor
                    Usage:
                      azureauth-credprovider doctor [--help]

                    Status:
                      Phase 7 stub only. This command is not implemented yet.

                    Options:
                      -h, --help                   Show help.
                    """,
                "login" =>
                    """
                    azureauth-credprovider login
                    Usage:
                      azureauth-credprovider login [--help]

                    Status:
                      Phase 7 stub only. This command is not implemented yet.

                    Options:
                      -h, --help                   Show help.
                    """,
                "logout" =>
                    """
                    azureauth-credprovider logout
                    Usage:
                      azureauth-credprovider logout [--help]

                    Status:
                      Phase 7 stub only. This command is not implemented yet.

                    Options:
                      -h, --help                   Show help.
                    """,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(command),
                    command,
                    "Unsupported help command."),
            });
    }

    private static string GetExpectedDryRunOutput(
        string command,
        string ecosystem,
        string ciMode,
        params string[] plannedActions)
    {
        List<string> lines =
        [
            $"command: {command}",
            $"ecosystem: {ecosystem}",
            "phase: 7-cli-shell",
            $"ci-mode: {ciMode}",
            $"scope: {GetExpectedScope(ciMode)}",
            "mutates-state: no",
            "planned-actions:",
        ];

        for (var index = 0; index < plannedActions.Length; index++)
        {
            lines.Add($"  {index + 1}. {plannedActions[index]}");
        }

        lines.Add("note: no files, credentials, or caches are changed in phase 7");
        return Normalize(string.Join("\n", lines));
    }

    private static string GetExpectedScope(string ciMode)
    {
        return string.Equals(ciMode, "azure-pipelines", StringComparison.Ordinal)
            ? "ci-temporary"
            : "user";
    }

    private static CommandResult Invoke(params string[] args)
    {
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = CliApplication.Run(args, stdout, stderr);

        return new CommandResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString());
    }

    private static string Normalize(string text)
    {
        string normalized = text.ReplaceLineEndings("\n");
        return string.IsNullOrEmpty(normalized) || normalized.EndsWith('\n')
            ? normalized
            : normalized + "\n";
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            throw new InvalidOperationException("Simulated writer failure.");
        }
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
