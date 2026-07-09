using System.Text;
using System.Text.RegularExpressions;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Cli.Tests;

public sealed class CliApplicationTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

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
                  status                       Show deterministic Phase 14.1 shell status.
                  doctor                       Run adapter and auth policy checks.
                  login                        Run accepted MVP authentication orchestration.
                  logout                       Clear product-owned authentication state.
                  configure <ecosystem>        Git/NuGet --ci none applies; others dry-run.
                  unconfigure <ecosystem>      Git/NuGet --ci none removes; others dry-run.

                Options:
                  -h, --help                   Show help.

                Examples:
                  azureauth-credprovider status
                  azureauth-credprovider login --device-code
                  azureauth-credprovider login --ci azure-pipelines
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
                phase: 14.1-auth-orchestration
                ci-mode: none
                status-shell: ready
                environment-probing: disabled
                persistent-cache: disabled
                persistent-derived-credentials: disabled
                accepted-identity-flows: browser, device-code, pat, azure-pipelines
                deferred-identity-flows: service-principal, managed-identity, workload-identity
                dry-run-rendering: enabled
                mutating-commands: git-nuget-auth
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
                phase: 14.1-auth-orchestration
                ci-mode: azure-pipelines
                status-shell: ready
                environment-probing: disabled
                persistent-cache: disabled
                persistent-derived-credentials: disabled
                accepted-identity-flows: browser, device-code, pat, azure-pipelines
                deferred-identity-flows: service-principal, managed-identity, workload-identity
                dry-run-rendering: enabled
                mutating-commands: git-nuget-auth
                supported-ecosystems: git, nuget, python, npm
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData("--browser", "browser")]
    [InlineData("--device-code", "device-code")]
    public void LoginAcceptedInteractiveFlowsWriteSafeOutput(
        string flowOption,
        string expectedFlow)
    {
        CommandResult result = Invoke(
            "login",
            flowOption,
            "--account",
            "Alice@Example",
            "--tenant",
            "TenantA");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                $$"""
                command: login
                phase: 14.1-auth-orchestration
                ci-mode: none
                identity-flow: {{expectedFlow}}
                status: success
                account: alice@example
                tenant: tenanta
                credential-material: issued-not-printed
                persistent-derived-credentials: disabled
                plaintext-fallback: disabled
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.DoesNotContain("fake-token-", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-secret-", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginPatCompatibilityRequiresExplicitPatAndNeverEchoesIt()
    {
        const string Secret = "super-secret-pat";

        CommandResult result = Invoke("login", "--pat", Secret);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("identity-flow: pat\n", result.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "persistent-derived-credentials: disabled\n",
            result.StdOut,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginPatCompatibilityWithoutValueReturnsUsageError()
    {
        CommandResult result = Invoke("login", "--pat");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--pat' requires a value.\n", result.StdErr);
    }

    [Fact]
    public void LoginAzurePipelinesRequiresExplicitCiTokenEnvironment()
    {
        CommandResult result = InvokeWithRuntime(
            CreateAuthRuntimeWithEnvironment(new Dictionary<string, string>()),
            "login",
            "--ci",
            "azure-pipelines");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: Azure Pipelines system access token is unavailable in the environment.\n",
            result.StdErr);
    }

    [Fact]
    public void LoginAzurePipelinesUsesTokenWithoutPrintingOrPersistingIt()
    {
        const string Secret = "system-access-token";
        CommandResult result = InvokeWithRuntime(
            CreateAuthRuntimeWithEnvironment(
                new Dictionary<string, string>
                {
                    [AuthPhase14VerticalSliceService.AzurePipelinesSystemAccessTokenVariable] =
                        Secret,
                }),
            "login",
            "--ci",
            "azure-pipelines");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                """
                command: login
                phase: 14.1-auth-orchestration
                ci-mode: azure-pipelines
                identity-flow: azure-pipelines
                status: success
                account: build-service@phase14
                tenant: phase14-tenant
                credential-material: issued-not-printed
                persistent-derived-credentials: disabled
                plaintext-fallback: disabled
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.DoesNotContain(Secret, result.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--service-principal", "service-principal")]
    [InlineData("--managed-identity", "managed-identity")]
    [InlineData("--workload-identity", "workload-identity")]
    public void LoginDeferredServiceIdentityFlowsReportDeferred(
        string flowOption,
        string flowName)
    {
        CommandResult result = Invoke("login", flowOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            $"error: identity flow '{flowName}' is deferred for MVP.\n",
            result.StdErr);
    }

    [Fact]
    public void LogoutWritesSafeOutput()
    {
        CommandResult result = Invoke("logout");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                """
                command: logout
                phase: 14.1-auth-orchestration
                ci-mode: none
                persistent-derived-credentials-removed: none
                plaintext-fallback: disabled
                """),
            result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void GitCredentialHelperSharedEntrypointWritesProtocolStdoutOnly()
    {
        CommandResult result = InvokeWithStandardInput(
            """
            protocol=https
            host=dev.azure.com
            path=org/project/_git/repository

            """,
            executablePath: "azureauth-credprovider",
            "git",
            "credential-helper",
            "get");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("username=AzureDevOps\npassword=fake-secret-", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void GitCredentialHelperMalformedInputFailsWithoutLeakingInput()
    {
        CommandResult result = InvokeWithStandardInput(
            """
            protocol=https
            host=dev.azure.com
            host=should-not-leak.example

            """,
            executablePath: "azureauth-credprovider",
            "git",
            "credential-helper",
            "get");

        Assert.Equal(64, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Contains("code=ProtocolViolation", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-leak", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitCredentialHelperAppHostWritesProtocolStdoutToRealConsole()
    {
        var runner = new SystemProcessRunner();

        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(
                CliAppHostPath(),
                ["git", "credential-helper", "get"],
                standardInput:
                    """
                    protocol=https
                    host=dev.azure.com
                    path=org/project/_git/repository

                    """),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("username=AzureDevOps\npassword=fake-secret-", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact(Skip = "Non-Windows helper symlink test.", SkipWhen = nameof(IsWindows))]
    public async Task GitCredentialHelperAppHostAcceptsHelperNamedSymlink()
    {
        string tempDirectory = CreateTestDirectory();
        var runner = new SystemProcessRunner();
        string helperPath = Path.Combine(
            tempDirectory,
            "git-credential-azureauth-credprovider");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.CreateSymbolicLink(helperPath, CliAppHostPath());

            ProcessResult result = await runner.RunAsync(
                new ProcessStartSpec(
                    helperPath,
                    ["get"],
                    standardInput:
                        """
                        protocol=https
                        host=dev.azure.com
                        path=org/project/_git/repository

                        """),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.StartsWith("username=AzureDevOps\npassword=fake-secret-", result.StandardOutput);
            Assert.Equal(string.Empty, result.StandardError);
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
        }
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
        "remove product-owned git credential.helper entry",
        "remove product-owned dev.azure.com useHttpPath entry")]
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

    [Fact]
    public void ConfigureGitDryRunUsesPhase8PlanBackedOutputWithoutMutatingOwnedState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        const string existingGitConfig = """
            [user]
                name = Existing User
            """;

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, existingGitConfig);

            CommandResult implicitCiResult = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "git",
                "--dry-run");
            CommandResult explicitCiResult = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "git",
                "--dry-run",
                "--ci",
                "none");

            Assert.Equal(0, implicitCiResult.ExitCode);
            Assert.Equal(0, explicitCiResult.ExitCode);
            Assert.Equal(GetExpectedGitConfigureDryRunOutput(), implicitCiResult.StdOut);
            Assert.Equal(implicitCiResult.StdOut, explicitCiResult.StdOut);
            Assert.Equal(string.Empty, implicitCiResult.StdErr);
            Assert.Equal(string.Empty, explicitCiResult.StdErr);
            Assert.Equal(existingGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfigureNuGetDryRunUsesPhase10PlanBackedOutput()
    {
        CliRuntimeOptions runtimeOptions = CreateNuGetPhase10DryRunRuntimeOptions();
        CommandResult implicitCiResult = InvokeWithRuntime(
            runtimeOptions,
            "configure",
            "nuget",
            "--dry-run");
        CommandResult explicitCiResult = InvokeWithRuntime(
            runtimeOptions,
            "configure",
            "nuget",
            "--dry-run",
            "--ci",
            "none");

        Assert.Equal(0, implicitCiResult.ExitCode);
        Assert.Equal(0, explicitCiResult.ExitCode);
        Assert.Equal(GetExpectedNuGetConfigureDryRunOutput(), implicitCiResult.StdOut);
        Assert.Equal(GetExpectedNuGetConfigureDryRunOutput(), explicitCiResult.StdOut);
        Assert.Equal(implicitCiResult.StdOut, explicitCiResult.StdOut);
        Assert.Equal(string.Empty, implicitCiResult.StdErr);
        Assert.Equal(string.Empty, explicitCiResult.StdErr);
    }

    [Fact]
    public void UnconfigureNuGetDryRunValidatesPhase10StateAndWritesGenericOutput()
    {
        CliRuntimeOptions runtimeOptions = CreateNuGetPhase10DryRunRuntimeOptions();
        CommandResult implicitCiResult = InvokeWithRuntime(
            runtimeOptions,
            "unconfigure",
            "nuget",
            "--dry-run");
        CommandResult explicitCiResult = InvokeWithRuntime(
            runtimeOptions,
            "unconfigure",
            "nuget",
            "--dry-run",
            "--ci",
            "none");
        string expectedOutput = GetExpectedDryRunOutput(
            "unconfigure",
            "nuget",
            "none",
            "remove product-owned NuGet plugin discovery scaffold",
            "remove product-owned Azure Artifacts NuGet credential scaffold");

        Assert.Equal(0, implicitCiResult.ExitCode);
        Assert.Equal(0, explicitCiResult.ExitCode);
        Assert.Equal(expectedOutput, implicitCiResult.StdOut);
        Assert.Equal(expectedOutput, explicitCiResult.StdOut);
        Assert.Equal(implicitCiResult.StdOut, explicitCiResult.StdOut);
        Assert.Equal(string.Empty, implicitCiResult.StdErr);
        Assert.Equal(string.Empty, explicitCiResult.StdErr);
    }

    [Fact]
    public void ConfigureGitCreatesOwnedFakeEntriesAndManifest()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("configure", "applied", 2, true, true),
                result.StdOut);
            Assert.Equal(string.Empty, result.StdErr);
            Assert.True(File.Exists(service.Paths.GitConfigPath));
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.True(File.Exists(service.Paths.GitHelperPath));

            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Contains(
                $"helper = \"{service.Paths.GitHelperPath}\"",
                gitConfig,
                StringComparison.Ordinal);
            Assert.Contains(
                "useHttpPath = \"true\"",
                gitConfig,
                StringComparison.Ordinal);

            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            Assert.Contains("credential.helper", manifest, StringComparison.Ordinal);
            Assert.Contains(
                "credential.https://dev.azure.com.useHttpPath",
                manifest,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows symlink safety test.", SkipWhen = nameof(IsWindows))]
    public void ConfigureGitRefusesSymlinkedHelperDirectory()
    {
        string stateDirectory = CreateTestDirectory();
        string externalDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CreateOwnerOnlyDirectory(stateDirectory);
            CreateOwnerOnlyDirectory(externalDirectory);
            Directory.CreateSymbolicLink(service.Paths.GitHelperDirectoryPath, externalDirectory);

            CommandResult result = InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                result.StdErr);
            Assert.False(
                File.Exists(Path.Combine(
                    externalDirectory,
                    "git-credential-azureauth-credprovider")));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
            DeleteDirectoryIfExists(externalDirectory);
        }
    }

    [Theory]
    [InlineData("space path")]
    [InlineData("semi;path")]
    public void ConfigureGitRefusesShellUnsafeHelperPath(string unsafeSegment)
    {
        string stateDirectory = Path.Combine(CreateTestDirectory(), unsafeSegment);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                result.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorWithoutOwnedStateReturnsNonZeroAndReportsAbsentArtifacts()
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: false,
                    ownershipManifestPresent: false
                ),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorAfterConfigureReportsSuccessWithoutLeakingCredentialMaterial()
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: true,
                    ownershipManifestPresent: true),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
            Assert.DoesNotContain("fake-secret-", doctorResult.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-token-", doctorResult.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("username=", doctorResult.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("password=", doctorResult.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("AzureDevOps", doctorResult.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorReportsDeferredLocalShellHelperShorthandAsUnsupportedMvp()
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = new()
        {
            GitPhase8Options = new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                LocalShellGitDiscoverySupported = false,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            },
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
        };

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Contains(
                "local-shell-helper-shorthand: unsupported-mvp\n",
                doctorResult.StdOut,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorDoesNotTreatCommentTextAsOwnedGitState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        const string commentedGitConfig = """
            [credential]
                # helper = "azureauth-credprovider"
            [credential "https://dev.azure.com"]
                ; useHttpPath = "true"
            """;

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);

            WriteOwnerOnlyText(service.Paths.GitConfigPath, Normalize(commentedGitConfig));

            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: false,
                    ownershipManifestPresent: true,
                    configurationPlanValid: false
                ),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorReportsStaleOwnedGitConfigWhenManifestIsMissing()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.Delete(service.Paths.OwnershipManifestPath);

            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: true,
                    ownershipManifestPresent: false,
                    configurationPlanValid: false,
                    localShellHelperShorthandSuccess: false),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorReportsManifestDirectoryAsPresentUnrecognizedState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.Delete(service.Paths.OwnershipManifestPath);
            Directory.CreateDirectory(service.Paths.OwnershipManifestPath);

            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: true,
                    ownershipManifestPresent: true,
                    configurationPlanValid: false,
                    localShellHelperShorthandSuccess: false),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorFailsWhenValidManifestHasExtraProductScaffoldMarker()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.AppendAllText(
                service.Paths.GitConfigPath,
                "\n[alias]\n"
                    + "    # azureauth-credprovider: product-owned credential scaffold; "
                    + "id=0123456789abcdef0123456789abcdef\n");

            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: true,
                    ownershipManifestPresent: true,
                    configurationPlanValid: false,
                    localShellHelperShorthandSuccess: false),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitPrunesEmptyProductCreatedCredentialScaffoldsFromMissingInitialConfig()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);

            string gitConfig = File.Exists(service.Paths.GitConfigPath)
                ? File.ReadAllText(service.Paths.GitConfigPath)
                : string.Empty;
            Assert.Equal(string.Empty, gitConfig);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ReconfigureGitKeepsScaffoldOwnershipAndUnconfigurePrunesCreatedSections()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult firstConfigure = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult secondConfigure = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, firstConfigure.ExitCode);
            Assert.Equal(0, secondConfigure.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);

            string gitConfig = File.Exists(service.Paths.GitConfigPath)
                ? File.ReadAllText(service.Paths.GitConfigPath)
                : string.Empty;
            Assert.Equal(string.Empty, gitConfig);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("\"manifestId\":\"phase8-git-configuration\"", "\"manifestId\":\"foreign\"")]
    [InlineData("\"productVersion\":\"phase8\"", "\"productVersion\":\"foreign-phase\"")]
    [InlineData(
        "\"safeMetadata\":{}",
        "\"safeMetadata\":{"
            + "\"hcoona.azureAuthCredProvider.physicalTargetManifestState\":\"prepared\"}")]
    public void UnconfigureGitDoesNotRemoveForeignManifestState(
        string originalManifestText,
        string replacementManifestText)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            WriteOwnerOnlyText(
                service.Paths.OwnershipManifestPath,
                manifest.Replace(
                    originalManifestText,
                    replacementManifestText,
                    StringComparison.Ordinal));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitDryRunRefusesForeignManifestState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            WriteOwnerOnlyText(
                service.Paths.OwnershipManifestPath,
                manifest.Replace(
                    "\"manifestId\":\"phase8-git-configuration\"",
                    "\"manifestId\":\"foreign\"",
                    StringComparison.Ordinal));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git",
                "--dry-run");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureGitDoesNotFatalOnForeignManifestState(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            WriteOwnerOnlyText(
                service.Paths.OwnershipManifestPath,
                manifest.Replace(
                    "\"manifestId\":\"phase8-git-configuration\"",
                    "\"manifestId\":\"foreign\"",
                    StringComparison.Ordinal));

            CommandResult secondConfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "configure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, secondConfigureResult.ExitCode);
            Assert.Equal(string.Empty, secondConfigureResult.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                secondConfigureResult.StdErr);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureGitDoesNotFatalWhenOwnedGitConfigHasNoManifest(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.Delete(service.Paths.OwnershipManifestPath);

            CommandResult secondConfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "configure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, secondConfigureResult.ExitCode);
            Assert.Equal(string.Empty, secondConfigureResult.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                secondConfigureResult.StdErr);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureGitDoesNotAdoptOrphanedProductScaffoldMarkers(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(
                service.Paths.GitConfigPath,
                """
                [credential]
                    # azureauth-credprovider: product-owned credential scaffold; id=0123456789abcdef
                    # 0123456789abcdef
                """);

            CommandResult configureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "configure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, configureResult.ExitCode);
            Assert.Equal(string.Empty, configureResult.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                configureResult.StdErr);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureGitRefusesExtraProductScaffoldMarkerWithValidManifest(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.AppendAllText(
                service.Paths.GitConfigPath,
                "\n[alias]\n"
                    + "    # azureauth-credprovider: product-owned credential scaffold; "
                    + "id=0123456789abcdef0123456789abcdef\n");

            CommandResult secondConfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "configure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, secondConfigureResult.ExitCode);
            Assert.Equal(string.Empty, secondConfigureResult.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                secondConfigureResult.StdErr);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureGitRefusesTamperedManagedProductScaffoldMarkerWithValidManifest(
        bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            var markerIdRegex = new Regex(
                "id=([0-9a-f]{32})",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
            Match markerIdMatch = markerIdRegex.Match(gitConfig);
            Assert.True(markerIdMatch.Success);
            string originalId = markerIdMatch.Groups[1].Value;
            string replacementId = originalId[0] == '0'
                ? "10000000000000000000000000000000"
                : "00000000000000000000000000000000";
            WriteOwnerOnlyText(
                service.Paths.GitConfigPath,
                markerIdRegex.Replace(gitConfig, "id=" + replacementId, count: 1));

            CommandResult secondConfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "configure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, secondConfigureResult.ExitCode);
            Assert.Equal(string.Empty, secondConfigureResult.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                secondConfigureResult.StdErr);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitDoesNotFatalWhenOwnedGitConfigIsStale(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string tamperedGitConfig = File.ReadAllText(service.Paths.GitConfigPath)
                .Replace(
                    $"helper = \"{service.Paths.GitHelperPath}\"",
                    "helper = \"foreign\"",
                    StringComparison.Ordinal);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, tamperedGitConfig);

            CommandResult unconfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "unconfigure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Equal(tamperedGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitDoesNotDeleteManifestWhenPhysicalScaffoldMarkerIsTampered()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            var markerIdRegex = new Regex(
                "id=([0-9a-f]{32})",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
            Match markerIdMatch = markerIdRegex.Match(gitConfig);
            Assert.True(markerIdMatch.Success);
            string originalId = markerIdMatch.Groups[1].Value;
            string replacementId = originalId[0] == '0'
                ? "10000000000000000000000000000000"
                : "00000000000000000000000000000000";
            string tamperedGitConfig = markerIdRegex.Replace(
                gitConfig,
                "id=" + replacementId,
                count: 1);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, tamperedGitConfig);

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Equal(tamperedGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.Equal(manifest, File.ReadAllText(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitPrunesCanonicalEquivalentDevAzureScaffoldSection()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string tamperedGitConfig = File.ReadAllText(service.Paths.GitConfigPath)
                .Replace(
                    "[credential \"https://dev.azure.com\"]",
                    "[credential \"https://dev.azure.com/\"]",
                    StringComparison.Ordinal);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, tamperedGitConfig);

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            Assert.Equal(string.Empty, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitRemovesProductMarkerButPreservesForeignScaffoldSectionContent()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            const string markerPrefix =
                "# azureauth-credprovider: product-owned credential scaffold;";
            int markerIndex = gitConfig.IndexOf(markerPrefix, StringComparison.Ordinal);
            Assert.True(markerIndex >= 0);
            int markerLineEnd = gitConfig.IndexOf('\n', markerIndex);
            Assert.True(markerLineEnd >= 0);
            string gitConfigWithForeignContent = gitConfig.Insert(
                markerLineEnd + 1,
                "    # keep user comment\n");
            WriteOwnerOnlyText(service.Paths.GitConfigPath, gitConfigWithForeignContent);

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, unconfigureResult.ExitCode);
            string remainingGitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Contains("# keep user comment", remainingGitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain(markerPrefix, remainingGitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "azureauth-credprovider",
                remainingGitConfig,
                StringComparison.Ordinal);
            Assert.DoesNotContain("useHttpPath", remainingGitConfig, StringComparison.Ordinal);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitDoesNotFatalWhenOwnedGitConfigHasNoManifest(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.Delete(service.Paths.OwnershipManifestPath);

            CommandResult unconfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "unconfigure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitDoesNotFatalWhenManifestPathIsDirectory(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.Delete(service.Paths.OwnershipManifestPath);
            Directory.CreateDirectory(service.Paths.OwnershipManifestPath);

            CommandResult unconfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "unconfigure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.True(Directory.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("helper = \"azureauth-credprovider\" # stale")]
    [InlineData("helper = azureauth-credprovider # stale")]
    [InlineData("helper = azureauth-credprovider# stale")]
    [InlineData("helper = azureauth-credprovider; stale")]
    public void UnconfigureGitDetectsMissingManifestHelperWithTrailingComment(
        string replacementHelperLine)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string staleGitConfig = File.ReadAllText(service.Paths.GitConfigPath)
                .Replace(
                    "helper = \"azureauth-credprovider\"",
                    replacementHelperLine,
                    StringComparison.Ordinal);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, staleGitConfig);
            File.Delete(service.Paths.OwnershipManifestPath);

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Equal(staleGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("https://dev.azure.com/")]
    [InlineData("https://dev.azure.com/org")]
    [InlineData("https://dev.azure.com./org")]
    [InlineData("https://user@dev.azure.com/org")]
    [InlineData("https://dev.azure.com/org?query")]
    [InlineData("https://dev.azure.com/org#fragment")]
    [InlineData("https://dev.azure.com:444/org")]
    public void UnconfigureGitDetectsMissingManifestUrlScopedHelper(string subsection)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        string staleGitConfig = $"""
            [credential "{subsection}"]
                helper = "{service.Paths.GitHelperPath}"
            """;

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, Normalize(staleGitConfig));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Equal(Normalize(staleGitConfig), File.ReadAllText(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitRefusesExtraUrlScopedProductHelperWithValidManifest(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            File.AppendAllText(
                service.Paths.GitConfigPath,
                "\n[credential \"https://dev.azure.com/org\"]\n"
                    + "    helper = \"azureauth-credprovider\"\n");

            CommandResult unconfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "unconfigure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Contains(
                "[credential \"https://dev.azure.com/org\"]",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
            Assert.Equal(manifest, File.ReadAllText(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitIgnoresUnrelatedHelperKeyOutsideCredentialSections()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        const string unrelatedConfig = """

            [alias]
                helper = "azureauth-credprovider"
            """;

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.AppendAllText(service.Paths.GitConfigPath, Normalize(unrelatedConfig));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            string remainingGitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Contains("[alias]", remainingGitConfig, StringComparison.Ordinal);
            Assert.Contains(
                "helper = \"azureauth-credprovider\"",
                remainingGitConfig,
                StringComparison.Ordinal);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitRefusesExtraProductScaffoldMarkerOutsideManagedSections()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        const string extraMarkerConfig = """

            [alias]
                # azureauth-credprovider: product-owned credential scaffold; id=0123456789abcdef
                # 0123456789abcdef
            """;

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            File.AppendAllText(service.Paths.GitConfigPath, Normalize(extraMarkerConfig));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Contains(
                "product-owned credential scaffold",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
            Assert.Equal(manifest, File.ReadAllText(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitRefusesProductScaffoldMarkerInUnsafeDevAzureRootAlias()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            File.AppendAllText(
                service.Paths.GitConfigPath,
                "\n[credential \"https://dev.azure.com:444\"]\n"
                    + "    # azureauth-credprovider: product-owned credential scaffold; "
                    + "id=0123456789abcdef0123456789abcdef\n");

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.Equal(manifest, File.ReadAllText(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitDoesNotRemoveTamperedScaffoldMetadataState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            WriteOwnerOnlyText(
                service.Paths.OwnershipManifestPath,
                Regex.Replace(
                    manifest,
                    "\"previousOwnedEntryMetadata\":\"[^\"]+\"",
                    "\"previousOwnedEntryMetadata\":\"garbage\""));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitDoesNotRemovePartiallyTamperedScaffoldMetadataState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            var metadataRegex = new Regex(
                "\"previousOwnedEntryMetadata\":\"[^\"]+\"",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
            WriteOwnerOnlyText(
                service.Paths.OwnershipManifestPath,
                metadataRegex.Replace(
                    manifest,
                    "\"previousOwnedEntryMetadata\":\"garbage\"",
                    count: 1));

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorValidatesRealGitConfigPathSafety()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        string symlinkTarget = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CreateOwnerOnlyDirectory(stateDirectory);
            CreateOwnerOnlyDirectory(symlinkTarget);
            Directory.CreateSymbolicLink(Path.Combine(stateDirectory, "git"), symlinkTarget);

            CommandResult doctorResult = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctorResult.ExitCode);
            Assert.Equal(
                GetExpectedDoctorOutput(
                    ownedGitEntriesPresent: false,
                    ownershipManifestPresent: false,
                    configurationPlanValid: false),
                doctorResult.StdOut);
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
            DeleteDirectoryIfExists(symlinkTarget);
        }
    }

    [Fact]
    public void UnconfigureGitPreservesPreExistingEmptyCredentialScaffolds()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        string existingGitConfig = string.Join(
            "\n",
            "[credential]",
            "[credential \"https://dev.azure.com\"]"
        );

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, existingGitConfig);

            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);

            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Equal(existingGitConfig, gitConfig);
            Assert.DoesNotContain("helper", gitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("useHttpPath", gitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("azureauth-credprovider", gitConfig, StringComparison.Ordinal);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitRestoresNoNewlineConfigWhenCredentialSectionsPrecedeContent()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        string existingGitConfig = string.Join(
            "\n",
            "[credential]",
            "[credential \"https://dev.azure.com\"]",
            "[core]",
            "    editor = \"vim\""
        );

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, existingGitConfig);

            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            Assert.Equal(existingGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitPreservesTrailingNewlineFromContentAppendedAfterProductScaffold()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        const string initialGitConfig = "[user]\n    email = user@example.com";
        const string appendedGitConfig = "[core]\n    editor = vim\n";

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, initialGitConfig);

            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            File.AppendAllText(service.Paths.GitConfigPath, appendedGitConfig);
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                initialGitConfig + "\n" + appendedGitConfig,
                File.ReadAllText(service.Paths.GitConfigPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitPreservesPreExistingGenericProductMarkerComments()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        string existingGitConfig = string.Join(
            "\n",
            "[credential]",
            "# azureauth-credprovider: product-owned credential scaffold",
            "[credential \"https://dev.azure.com\"]",
            "# azureauth-credprovider: product-owned credential scaffold"
        );

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, existingGitConfig);

            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            Assert.Equal(existingGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnconfigureGitRemovesOwnedEntriesAndPreservesUnrelatedConfigContent()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        string existingGitConfig = string.Join(
            "\n",
            "# keep comment",
            "[user]",
            "    email = user@example.com"
        );

        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(service.Paths.GitConfigPath)!);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, existingGitConfig);

            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git");

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "applied", 2, false, false),
                unconfigureResult.StdOut);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);

            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Equal(existingGitConfig, gitConfig);
            Assert.DoesNotContain("azureauth-credprovider", gitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("useHttpPath", gitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("[credential]", gitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "[credential \"https://dev.azure.com\"]",
                gitConfig,
                StringComparison.Ordinal);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(
        "configure",
        "python",
        "error: configure without '--dry-run' is not implemented in phase 10.\n")]
    [InlineData(
        "unconfigure",
        "npm",
        "error: unconfigure without '--dry-run' is not implemented in phase 10.\n")]
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
            ["login", "--service-principal"],
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
                "azure-pipelines",
                "prepare temporary Azure Pipelines git credential helper scaffold",
                "prepare temporary dev.azure.com useHttpPath scaffold"
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
                "remove product-owned git credential.helper entry",
                "remove product-owned dev.azure.com useHttpPath entry"
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
                "remove product-owned git credential.helper entry",
                "remove product-owned dev.azure.com useHttpPath entry"
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
                    """
                    + "\n"
                    + "  azureauth-credprovider configure <ecosystem> [--dry-run] [--ci <mode>] "
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
                    + "  --dry-run                    Optional for git/nuget none; "
                    + "required otherwise.\n"
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
                    + "  azureauth-credprovider unconfigure <ecosystem> [--dry-run] [--ci <mode>] "
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
                    + "  --dry-run                    Optional for git/nuget none; "
                    + "required otherwise.\n"
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
                      Run safe deterministic adapter and Phase 14.1 auth policy checks.

                    Options:
                      -h, --help                   Show help.
                    """,
                "login" =>
                    """
                    azureauth-credprovider login
                    Usage:
                      azureauth-credprovider login [--browser|--device-code|--pat <value>]
                      azureauth-credprovider login --ci azure-pipelines

                    Accepted MVP flows:
                      --browser                    Use interactive browser authentication.
                      --device-code                Use device-code authentication.
                      --pat <value>                Explicit PAT compatibility; never persisted.
                      --ci azure-pipelines         Use SYSTEM_ACCESSTOKEN without persistence.

                    Deferred service identity flows:
                      --service-principal
                      --managed-identity
                      --workload-identity

                    Options:
                      --account <name>             Optional account hint.
                      --tenant <id>                Optional tenant hint.
                      -h, --help                   Show help.
                    """,
                "logout" =>
                    """
                    azureauth-credprovider logout
                    Usage:
                      azureauth-credprovider logout [--help]

                    Status:
                      Clears product-owned authentication state only.

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
            "phase: 14.1-auth-orchestration",
            $"ci-mode: {ciMode}",
            $"scope: {GetExpectedScope(ciMode)}",
            "mutates-state: no",
            "planned-actions:",
        ];

        for (var index = 0; index < plannedActions.Length; index++)
        {
            lines.Add($"  {index + 1}. {plannedActions[index]}");
        }

        lines.Add("note: no files, credentials, or caches are changed in phase 10");
        return Normalize(string.Join("\n", lines));
    }

    private static string GetExpectedGitConfigureDryRunOutput()
    {
        return Normalize(
            """
            command: configure
            ecosystem: git
            phase: 14.1-auth-orchestration
            ci-mode: none
            scope: user
            mutates-state: no
            configuration-plan: valid
            planned-change-count: 2
            planned-actions:
              1. set product-owned git credential.helper entry
              2. set product-owned dev.azure.com useHttpPath entry
            note: dry-run only; no files, credentials, or caches are changed in phase 10
            """
        );
    }

    private static string GetExpectedNuGetConfigureDryRunOutput()
    {
        return Normalize(
            """
            command: configure
            ecosystem: nuget
            phase: 14.1-auth-orchestration
            ci-mode: none
            scope: user
            mutates-state: no
            configuration-plan: valid
            planned-change-count: 1
            planned-actions:
              1. register product-owned NuGet netcore plugin layout marker
            note: dry-run only; no files, credentials, or caches are changed in phase 10
            """
        );
    }

    private static string GetExpectedGitMutationOutput(
        string command,
        string planState,
        int changeCount,
        bool ownedGitEntriesPresent,
        bool ownershipManifestPresent)
    {
        string countLabel = string.Equals(command, "configure", StringComparison.Ordinal)
            ? "applied-change-count"
            : "removed-change-count";
        return Normalize(
            string.Join(
                "\n",
                [
                    $"command: {command}",
                    "ecosystem: git",
                    "phase: 14.1-auth-orchestration",
                    "ci-mode: none",
                    "scope: user",
                    "mutates-state: yes",
                    $"plan-state: {planState}",
                    $"{countLabel}: {changeCount}",
                    $"owned-git-entries: {(ownedGitEntriesPresent ? "present" : "absent")}",
                    $"ownership-manifest: {(ownershipManifestPresent ? "present" : "absent")}",
                    "note: credential material is not printed",
                ]));
    }

    private static string GetExpectedDoctorOutput(
        bool ownedGitEntriesPresent,
        bool ownershipManifestPresent,
        bool configurationPlanValid = true,
        bool? localShellHelperShorthandSuccess = null)
    {
        bool devAzureUseHttpPathPresent = ownedGitEntriesPresent;
        bool localShellSuccess = localShellHelperShorthandSuccess ?? ownedGitEntriesPresent;
        return Normalize(
            string.Join(
                "\n",
                [
                    "command: doctor",
                    "phase: 14.1-auth-orchestration",
                    $"configuration-plan: {(configurationPlanValid ? "pass" : "fail")}",
                    $"owned-git-entries: {(ownedGitEntriesPresent ? "present" : "absent")}",
                    $"ownership-manifest: {(ownershipManifestPresent ? "present" : "absent")}",
                    "dev.azure.com-useHttpPath: "
                        + (devAzureUseHttpPathPresent ? "present" : "absent"),
                    "fake-credential-core: pass",
                    "git-credential-helper-get: pass",
                    "git-credential-helper-store: pass",
                    "git-credential-helper-erase: pass",
                    "local-shell-helper-shorthand: " + (localShellSuccess ? "pass" : "fail"),
                    "protocol-payload: captured-not-printed",
                    "auth-accepted-identity-flows: browser, device-code, pat, azure-pipelines",
                    "auth-deferred-identity-flows: "
                        + "service-principal, managed-identity, workload-identity",
                    "auth-persistent-derived-credentials: disabled",
                    "auth-plaintext-fallback: disabled",
                ]));
    }

    private static string GetExpectedScope(string ciMode)
    {
        return string.Equals(ciMode, "azure-pipelines", StringComparison.Ordinal)
            ? "ci-temporary"
            : "user";
    }

    private static CommandResult Invoke(params string[] args)
    {
        return InvokeWithRuntime(runtimeOptions: null, args: args);
    }

    private static CommandResult InvokeWithRuntime(
        CliRuntimeOptions? runtimeOptions,
        params string[] args)
    {
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = runtimeOptions is null
            ? CliApplication.Run(args, stdout, stderr)
            : CliApplication.Run(args, stdout, stderr, runtimeOptions);

        return new CommandResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString());
    }

    private static CliRuntimeOptions CreateAuthRuntimeWithEnvironment(
        Dictionary<string, string> environment)
    {
        return new CliRuntimeOptions
        {
            AuthPhase14Options = new AuthPhase14VerticalSliceOptions
            {
                EnvironmentVariableReader = name =>
                    environment.TryGetValue(name, out string? value) ? value : null,
            },
        };
    }

    private static CommandResult InvokeWithStandardInput(
        string standardInput,
        string executablePath,
        params string[] args)
    {
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = CliApplication.Run(
            args,
            stdout,
            stderr,
            runtimeOptions: null,
            new StringReader(standardInput),
            executablePath);

        return new CommandResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString());
    }

    private static CliRuntimeOptions CreateGitPhase8RuntimeOptions(string stateDirectory)
    {
        return new CliRuntimeOptions
        {
            GitPhase8Options = new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            },
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
        };
    }

    private static CliRuntimeOptions CreateNuGetPhase10DryRunRuntimeOptions()
    {
        return new CliRuntimeOptions
        {
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
        };
    }

    private static NuGetPhase10VerticalSliceOptions CreateIsolatedNuGetPhase10Options() =>
        new()
        {
            StateDirectoryPath = "/state/azureauth-credprovider/phase10",
            FileSystem = new EmptyNuGetDryRunFileSystem(),
            EnvironmentVariableReader = _ => null,
        };

    private static GitPhase8VerticalSliceService CreateGitPhase8Service(string stateDirectory)
    {
        return new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });
    }

    private static string CreateFakeProductExecutable(string stateDirectory)
    {
        string directory = Path.Combine(stateDirectory, "product-bin");
        CreateOwnerOnlyDirectory(directory);
        string executablePath = Path.Combine(directory, "azureauth-credprovider");
        WriteOwnerOnlyText(executablePath, "#!/bin/sh\nexit 70\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return executablePath;
    }

    private static string CliAppHostPath()
    {
        string assemblyPath = typeof(CliApplication).Assembly.Location;
        string directory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException(
                $"CLI assembly path '{assemblyPath}' does not have a parent directory.");
        string fileName = Path.GetFileNameWithoutExtension(assemblyPath);
        if (OperatingSystem.IsWindows())
        {
            fileName += ".exe";
        }

        string appHostPath = Path.Combine(directory, fileName);
        if (!File.Exists(appHostPath))
        {
            throw new FileNotFoundException(
                $"Sibling CLI apphost '{appHostPath}' was not found for '{assemblyPath}'.",
                appHostPath);
        }

        return appHostPath;
    }

    private sealed class PassingGitDiscoveryProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();

            TryWriteHelperMarker(startSpec);
            return Task.FromResult(
                new ProcessResult(
                    0,
                    "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                        + "username=AzureDevOps\n"
                        + "password=fake-secret-phase9-probe\n",
                    string.Empty));
        }

        private static void TryWriteHelperMarker(ProcessStartSpec startSpec)
        {
            if (
                !startSpec.Environment.TryGetValue(
                    "AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER",
                    out string? markerPath)
                || string.IsNullOrEmpty(markerPath)
            )
            {
                return;
            }

            File.WriteAllText(markerPath, string.Empty);
        }
    }

    private static string CreateTestDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider-cli-tests");
        CreateOwnerOnlyDirectory(root);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        CreateOwnerOnlyDirectory(directory);
        return directory;
    }

    private static void CreateOwnerOnlyDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WriteOwnerOnlyText(string path, string contents)
    {
        File.WriteAllText(path, contents);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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

    private sealed class EmptyNuGetDryRunFileSystem : IFileSystem
    {
        private static readonly FileSystemOwner Owner = new("fake:current");

        public bool SupportsConditionalFileMutations => true;

        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => false;

        public string GetFullPath(string path) => Path.GetFullPath(path);

        public bool IsPathFullyQualified(string path) => Path.IsPathFullyQualified(path);

        public bool IsSymbolicLink(string path) => false;

        public byte[] ComputeSha256Hash(string path) => throw CreateMissingPathException(path);

        public FileIntegritySnapshot CaptureFileIntegritySnapshot(string path) =>
            throw CreateMissingPathException(path);

        public bool FileMatchesIntegritySnapshot(
            string path,
            FileIntegritySnapshot snapshot) => false;

        public IReadOnlyList<TrustedDirectorySnapshot> CaptureTrustedParentDirectorySnapshots(
            string path
        ) => throw CreateMissingPathException(path);

        public FileSystemOwner GetCurrentOwner() => Owner;

        public FileSystemOwner GetOwner(string path) => Owner;

        public string ReadAllText(string path, Encoding? encoding = null) =>
            throw CreateMissingPathException(path);

        public byte[] ReadAllBytes(string path) => throw CreateMissingPathException(path);

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            throw CreateMutationException();

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => throw CreateMutationException();

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None,
            FileMutationExpectation? expectation = null
        ) => throw CreateMutationException();

        public UnixFileMode GetUnixFileMode(string path) => throw CreateMissingPathException(path);

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            throw CreateMutationException();

        public void CreateDirectory(string path) => throw CreateMutationException();

        public void DeleteFile(string path, FileMutationExpectation? expectation = null) =>
            throw CreateMutationException();

        public void DeleteDirectory(string path, bool recursive = false) =>
            throw CreateMutationException();

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => throw CreateMissingPathException(path);

        public IEnumerable<string> EnumerateDirectories(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => throw CreateMissingPathException(path);

        private static FileNotFoundException CreateMissingPathException(string path) =>
            new("The dry-run fake filesystem has no files.", path);

        private static InvalidOperationException CreateMutationException() =>
            new("The dry-run fake filesystem must not be mutated.");
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
