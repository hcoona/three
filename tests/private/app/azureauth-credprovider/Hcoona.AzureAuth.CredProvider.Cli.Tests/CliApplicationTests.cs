using System.Text;
using System.Text.RegularExpressions;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Cli.Tests;

public sealed class CliApplicationTests
{
    private const string TestRegistryUrl =
        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/";

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public void NoArgumentsWritesRootHelp()
    {
        CommandResult result = Invoke();

        Assert.Equal(0, result.ExitCode);
        // editorconfig-checker-disable
        Assert.Equal(
            Normalize(
                """
                azureauth-credprovider
                Usage:
                  azureauth-credprovider <command> [options]

                Commands:
                  status                       Show deterministic Phase 15 hardening status.
                  doctor                       Run aggregate adapter, config, and auth checks.
                  cleanup [ecosystem]          Clean product-owned temporary CI state.
                  acceptance                   Render Phase 15 hardening matrix.
                  login                        Run accepted MVP authentication orchestration.
                  logout                       Clear product-owned authentication state.
                  identity                     Configure product identity context.
                  configure <ecosystem>        Apply supported configuration plans.
                  refresh <ecosystem>          Refresh an npm, pnpm, or Yarn credential.
                  unconfigure <ecosystem>      Remove supported configuration plans.

                Options:
                  -h, --help                   Show help.

                Examples:
                  azureauth-credprovider status
                  azureauth-credprovider login --device-code
                  azureauth-credprovider login --ci azure-pipelines
                  azureauth-credprovider identity configure --tenant <id> [--account <name>]
                  azureauth-credprovider status --ci azure-pipelines
                  azureauth-credprovider configure git --dry-run
                  azureauth-credprovider acceptance
                  azureauth-credprovider cleanup --ci azure-pipelines
                  azureauth-credprovider unconfigure npm --dry-run
                """
            ),
            result.StdOut
        );
        // editorconfig-checker-enable
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("configure")]
    [InlineData("refresh")]
    [InlineData("unconfigure")]
    [InlineData("doctor")]
    [InlineData("cleanup")]
    [InlineData("acceptance")]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("identity")]
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
    [InlineData("cleanup", "npm", "-h", "unexpected")]
    [InlineData("acceptance", null, "-h", "unexpected")]
    [InlineData("login", null, "-h", "unexpected")]
    [InlineData("logout", null, "--help", "--bogus")]
    [InlineData("identity", "configure", "--help", "--bogus")]
    public void HelpShortCircuitsInvalidTrailingTokens(
        string command,
        string? argumentBeforeHelp,
        string helpToken,
        string invalidTrailingToken
    )
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
    [InlineData("identity")]
    [InlineData("identity", "configure")]
    [InlineData("identity", "reconfigure", "--account", "alice@example.com")]
    public void IdentityRequiresActionAndTenant(params string[] args)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Contains("error: identity", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityConfigureAndUnconfigurePersistSafeProductOwnedContext()
    {
        string rootPath = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(rootPath);
            var runtimeOptions = new CliRuntimeOptions
            {
                IdentityConfiguration = new CredentialProviderIdentityConfigurationService(
                    store,
                    new MutableTimeProvider(
                        new DateTimeOffset(2026, 7, 30, 16, 8, 47, TimeSpan.Zero)
                    )
                ),
            };

            CommandResult configured = InvokeWithRuntime(
                runtimeOptions,
                "identity",
                "configure",
                "--tenant",
                " TenantA ",
                "--account",
                " Alice@Example.com "
            );
            CommandResult unchanged = InvokeWithRuntime(
                runtimeOptions,
                "identity",
                "configure",
                "--tenant",
                "tenanta",
                "--account",
                "alice@example.com"
            );
            CommandResult unconfigured = InvokeWithRuntime(
                runtimeOptions,
                "identity",
                "unconfigure"
            );
            CommandResult alreadyUnconfigured = InvokeWithRuntime(
                runtimeOptions,
                "identity",
                "unconfigure"
            );

            Assert.Equal(0, configured.ExitCode);
            Assert.Equal(
                Normalize(
                    """
                    command: identity
                    action: configure
                    status: configured
                    tenant: TenantA
                    account-preference: Alice@Example.com
                    credential-material: not-stored
                    identity-verification: not-performed
                    """
                ),
                configured.StdOut
            );
            Assert.Equal(string.Empty, configured.StdErr);
            Assert.Equal(0, unchanged.ExitCode);
            Assert.Contains("status: unchanged\n", unchanged.StdOut, StringComparison.Ordinal);
            Assert.Contains("tenant: TenantA\n", unchanged.StdOut, StringComparison.Ordinal);
            Assert.Equal(0, unconfigured.ExitCode);
            Assert.Contains(
                "status: unconfigured\n",
                unconfigured.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(0, alreadyUnconfigured.ExitCode);
            Assert.Contains(
                "status: unchanged\n",
                alreadyUnconfigured.StdOut,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "azureauth",
                configured.StdOut,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(
                AzureAuthSecureRecordReadStatus.Missing,
                store.Read(CredentialProviderCompositionRoot.ProviderConfigRecordName).Status
            );
            Assert.Equal(
                AzureAuthSecureRecordReadStatus.Missing,
                store.Read(CredentialProviderCompositionRoot.BindingRecordName).Status
            );
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void IdentityConcurrentChangeReturnsRetryableSafeError()
    {
        var runtimeOptions = new CliRuntimeOptions
        {
            IdentityConfiguration = new CredentialProviderIdentityConfigurationService(
                new ConflictingIdentityRecordStore()
            ),
        };

        CommandResult result = InvokeWithRuntime(
            runtimeOptions,
            "identity",
            "configure",
            "--tenant",
            "tenant"
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: identity configuration changed concurrently; retry the command.\n",
            result.StdErr
        );
    }

    [Theory]
    [MemberData(nameof(AssignedDryRunWithHelpCases))]
    public void AssignedDryRunValidationBeatsHelpShortCircuit(string[] args)
    {
        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--dry-run' does not accept a value.\n", result.StdErr);
    }

    [Fact]
    public void StatusWritesDeterministicShellOutput()
    {
        CommandResult result = Invoke("status");

        Assert.Equal(0, result.ExitCode);
        // editorconfig-checker-disable
        Assert.Equal(
            Normalize(
                """
                command: status
                product: azureauth-credprovider
                phase: 15-end-to-end-hardening
                ci-mode: none
                composition-mode: Production
                provider: Unspecified
                interactive-readiness: interactive-unavailable
                interactive-readiness-code: ProviderNotConfigured
                interactive-blocker: Provider configuration is missing.
                silent-readiness: silent-unavailable
                silent-readiness-code: ProviderNotConfigured
                silent-remediation: Provider configuration is missing.
                status-shell: ready
                environment-probing: disabled
                persistent-cache: disabled
                persistent-derived-credentials: disabled
                accepted-identity-flows: browser, azure-pipelines
                unavailable-identity-flows: device-code
                deferred-identity-flows: pat-compatibility, service-principal, managed-identity, workload-identity
                pat-compatibility: deferred-disabled
                dry-run-rendering: enabled
                mutating-commands: identity-configuration, host-tool-configuration, auth, cleanup
                supported-ecosystems: git, nuget, python, npm, pnpm, yarn
                npm-user-lifecycle: missing
                pnpm-user-lifecycle: missing
                yarn-user-lifecycle: missing
                """
            ),
            result.StdOut
        );
        // editorconfig-checker-enable
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void AcceptanceWritesEvidenceBackedMvpMatrixWithoutOverclaimingDeferredRows()
    {
        CommandResult result = Invoke("acceptance");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "phase: 15-end-to-end-hardening\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains("mvp-local-acceptance: pass\n", result.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "full-release-evidence: deferred\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains("blocking-checks: none\n", result.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "pat-compatibility: deferred-disabled\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "git-for-windows-helper-discovery: deferred-non-mvp\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "remote-windows-first-platform-acceptance: deferred-release-evidence\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "standalone-linux-x64-platform-acceptance: deferred-release-evidence\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "azureauth-wsl-live-acceptance: pass\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "real-package-manager-invocation-paths: pass\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "note: deferred rows are not accepted support claims\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Equal(string.Empty, result.StdErr);
        Assert.DoesNotContain("fake-token-", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("system-token", result.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--ci", "azure-pipelines")]
    [InlineData("--ci=azure-pipelines", null)]
    [InlineData("--ci:azure-pipelines", null)]
    public void StatusAllowsExplicitAzurePipelinesCiMode(string ciToken, string? ciValue)
    {
        string[] args = ciValue is null ? ["status", ciToken] : ["status", ciToken, ciValue];

        CommandResult result = Invoke(args);

        Assert.Equal(0, result.ExitCode);
        // editorconfig-checker-disable
        Assert.Equal(
            Normalize(
                """
                command: status
                product: azureauth-credprovider
                phase: 15-end-to-end-hardening
                ci-mode: azure-pipelines
                composition-mode: Production
                provider: Unspecified
                interactive-readiness: interactive-unavailable
                interactive-readiness-code: ProviderNotConfigured
                interactive-blocker: Provider configuration is missing.
                silent-readiness: silent-unavailable
                silent-readiness-code: ProviderNotConfigured
                silent-remediation: Provider configuration is missing.
                status-shell: ready
                environment-probing: disabled
                persistent-cache: disabled
                persistent-derived-credentials: disabled
                accepted-identity-flows: browser, azure-pipelines
                unavailable-identity-flows: device-code
                deferred-identity-flows: pat-compatibility, service-principal, managed-identity, workload-identity
                pat-compatibility: deferred-disabled
                dry-run-rendering: enabled
                mutating-commands: identity-configuration, host-tool-configuration, auth, cleanup
                supported-ecosystems: git, nuget, python, npm, pnpm, yarn
                npm-user-lifecycle: missing
                pnpm-user-lifecycle: missing
                yarn-user-lifecycle: missing
                """
            ),
            result.StdOut
        );
        // editorconfig-checker-enable
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void LoginAcceptedInteractiveBrowserWritesSafeOutput()
    {
        CommandResult result = InvokeWithRuntime(
            new CliRuntimeOptions { CompositionRoot = CreateTestCompositionRoot() },
            "login",
            "--browser",
            "--account",
            "Alice@Example",
            "--tenant",
            "TenantA"
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                $$"""
                command: login
                phase: 15-end-to-end-hardening
                ci-mode: none
                identity-flow: browser
                status: success
                account: alice@example
                tenant: tenanta
                credential-material: issued-not-printed
                persistent-derived-credentials: disabled
                plaintext-fallback: disabled
                """
            ),
            result.StdOut
        );
        Assert.Equal(string.Empty, result.StdErr);
        Assert.DoesNotContain("fake-token-", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-secret-", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginDeviceCodeFailsWithStableBrowserRemediation()
    {
        CommandResult result = InvokeWithRuntime(
            new CliRuntimeOptions { CompositionRoot = CreateTestCompositionRoot() },
            "login",
            "--device-code"
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: Device-code login is unavailable; use interactive-browser login.\n",
            result.StdErr
        );
    }

    [Fact]
    public void LoginPatCompatibilityIsDeferredAndNeverEchoesIt()
    {
        const string Secret = "super-secret-pat";

        CommandResult result = Invoke("login", "--pat", Secret);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: PAT compatibility is deferred and has no production acquisition "
                + "or materialization path.\n",
            result.StdErr
        );
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
            "azure-pipelines"
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: Azure Pipelines system access token is unavailable in the environment.\n",
            result.StdErr
        );
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
                }
            ),
            "login",
            "--ci",
            "azure-pipelines"
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            Normalize(
                """
                command: login
                phase: 15-end-to-end-hardening
                ci-mode: azure-pipelines
                identity-flow: azure-pipelines
                status: success
                account: unbound
                tenant: unbound
                credential-material: provided-not-printed
                persistent-derived-credentials: disabled
                plaintext-fallback: disabled
                """
            ),
            result.StdOut
        );
        Assert.Equal(string.Empty, result.StdErr);
        Assert.DoesNotContain(Secret, result.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--service-principal", "service-principal")]
    [InlineData("--managed-identity", "managed-identity")]
    [InlineData("--workload-identity", "workload-identity")]
    public void LoginDeferredServiceIdentityFlowsReportDeferred(string flowOption, string flowName)
    {
        CommandResult result = Invoke("login", flowOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal($"error: identity flow '{flowName}' is deferred for MVP.\n", result.StdErr);
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
                phase: 15-end-to-end-hardening
                ci-mode: none
                persistent-derived-credentials-removed: none
                removed-change-count: 0
                cleanup: complete
                plaintext-fallback: disabled
                """
            ),
            result.StdOut
        );
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void LogoutRemovesCurrentJobTokenFilesAndOwnershipManifests()
    {
        const string Secret = "logout-system-access-token";
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(
            stateDirectory,
            Secret
        );

        try
        {
            foreach (string ecosystem in new[] { "npm", "pnpm", "yarn" })
            {
                CommandResult configure = InvokeWithRuntime(
                    runtimeOptions,
                    "configure",
                    ecosystem,
                    "--registry-url",
                    TestRegistryUrl,
                    "--ci",
                    "azure-pipelines"
                );
                Assert.Equal(0, configure.ExitCode);
            }

            string jobRoot = Path.Combine(stateDirectory, "ci-jobs", "cli-test-job");
            Assert.Contains(
                Secret,
                File.ReadAllText(Path.Combine(jobRoot, "npm", "userconfig.npmrc")),
                StringComparison.Ordinal
            );
            Assert.Equal(
                2,
                Directory
                    .GetFiles(
                        Path.Combine(jobRoot, "manifests"),
                        "*-ci-temporary-ownership-manifest.json"
                    )
                    .Length
            );

            CommandResult logout = InvokeWithRuntime(runtimeOptions, "logout");

            Assert.True(logout.ExitCode == 0, logout.StdOut + Environment.NewLine + logout.StdErr);
            Assert.Contains("removed-change-count: 5\n", logout.StdOut, StringComparison.Ordinal);
            Assert.Contains("cleanup: complete\n", logout.StdOut);
            Assert.Equal(string.Empty, logout.StdErr);
            Assert.False(File.Exists(Path.Combine(jobRoot, "npm", "userconfig.npmrc")));
            Assert.False(File.Exists(Path.Combine(jobRoot, "pnpm", "userconfig.npmrc")));
            Assert.False(Directory.Exists(Path.Combine(jobRoot, "yarn", "home")));
            foreach (string ecosystem in new[] { "npm-compatible", "yarn" })
            {
                Assert.False(
                    File.Exists(
                        Path.Combine(
                            jobRoot,
                            "manifests",
                            ecosystem + "-ci-temporary-ownership-manifest.json"
                        )
                    )
                );
            }
            Assert.DoesNotContain(Secret, logout.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, logout.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void LogoutSurfacesTemporaryCleanupFailureWithoutLeakingSecret()
    {
        const string Secret = "logout-failure-system-access-token";
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(
            stateDirectory,
            Secret
        );

        try
        {
            foreach (string ecosystem in new[] { "npm", "pnpm", "yarn" })
            {
                CommandResult configure = InvokeWithRuntime(
                    runtimeOptions,
                    "configure",
                    ecosystem,
                    "--registry-url",
                    TestRegistryUrl,
                    "--ci",
                    "azure-pipelines"
                );
                Assert.Equal(0, configure.ExitCode);
            }
            string jobRoot = Path.Combine(stateDirectory, "ci-jobs", "cli-test-job");
            string manifestPath = Path.Combine(
                jobRoot,
                "manifests",
                "npm-compatible-ci-temporary-ownership-manifest.json"
            );
            File.WriteAllText(manifestPath, Secret);

            CommandResult logout = InvokeWithRuntime(runtimeOptions, "logout");

            Assert.Equal(1, logout.ExitCode);
            Assert.Contains("cleanup: incomplete\n", logout.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "npm-ci-temporary-remediation: azureauth-credprovider cleanup npm "
                    + "--ci azure-pipelines\n",
                logout.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(
                "error: authentication state was cleared, but CI temporary cleanup "
                    + "was incomplete.\n",
                logout.StdErr
            );
            Assert.True(File.Exists(manifestPath));
            Assert.Equal(Secret, File.ReadAllText(manifestPath));
            Assert.True(File.Exists(Path.Combine(jobRoot, "npm", "userconfig.npmrc")));
            Assert.False(File.Exists(Path.Combine(jobRoot, "pnpm", "userconfig.npmrc")));
            Assert.False(Directory.Exists(Path.Combine(jobRoot, "yarn", "home")));
            Assert.False(
                File.Exists(
                    Path.Combine(jobRoot, "manifests", "yarn-ci-temporary-ownership-manifest.json")
                )
            );
            Assert.DoesNotContain(Secret, logout.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void LogoutRemovesCompleteOwnedManifestOnlyState()
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(stateDirectory);

        try
        {
            CommandResult configure = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines"
            );
            Assert.Equal(0, configure.ExitCode);
            string jobRoot = Path.Combine(stateDirectory, "ci-jobs", "cli-test-job");
            string manifestPath = Path.Combine(
                jobRoot,
                "manifests",
                "npm-compatible-ci-temporary-ownership-manifest.json"
            );
            File.Delete(Path.Combine(jobRoot, "npm", "userconfig.npmrc"));

            CommandResult logout = InvokeWithRuntime(runtimeOptions, "logout");

            Assert.Equal(0, logout.ExitCode);
            Assert.Contains("cleanup: complete\n", logout.StdOut);
            Assert.Equal(string.Empty, logout.StdErr);
            Assert.False(File.Exists(manifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
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
            "get"
        );

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
            "get"
        );

        Assert.Equal(64, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Contains("code=ProtocolViolation", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-leak", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitCredentialHelperAppHostFailsClosedWithoutProvider()
    {
        var runner = new SystemProcessRunner();

        ProcessResult result = await runner.RunAsync(
            new ProcessStartSpec(
                CliAppHostPath(),
                ["git", "credential-helper", "get"],
                standardInput: """
                protocol=https
                host=dev.azure.com
                path=org/project/_git/repository

                """
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(64, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Contains("ProviderNotConfigured", result.StandardError, StringComparison.Ordinal);
    }

    [Fact(
        Skip = "System secure-store integration is Linux/WSL-specific.",
        SkipWhen = nameof(IsWindows)
    )]
    public async Task AppHostMalformedProviderConfigurationFailsClosedForCliAndProtocolButHelpRuns()
    {
        const string SecretMarker = "must-not-leak-malformed-secret";
        string rootPath = Path.Combine(
            AppContext.BaseDirectory,
            "malformed-production-" + Guid.NewGuid().ToString("N")
        );
        CreateOwnerOnlyDirectory(rootPath);
        string configurationHome = Path.Combine(rootPath, "configuration-home");
        CreateOwnerOnlyDirectory(configurationHome);
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(rootPath);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                store
                    .CompareExchange(
                        CredentialProviderCompositionRoot.ProviderConfigRecordName,
                        AzureAuthSecureRecordStoreContract.MissingRevision,
                        Encoding.UTF8.GetBytes(
                            $$"""{"schemaVersion":1,"selection":"{{SecretMarker}}"}"""
                        )
                    )
                    .Status
            );
            var environment = new Dictionary<string, string?>
            {
                [SystemAzureAuthSecureRecordStoreOptions.ConfigRootEnvironmentVariable] = rootPath,
                ["HOME"] = configurationHome,
                ["SYSTEM_ACCESSTOKEN"] = "opaque-ci-token",
                ["SYSTEM_JOBID"] = "malformed-provider-job",
            };
            var runner = new SystemProcessRunner();

            ProcessResult cli = await runner.RunAsync(
                new ProcessStartSpec(CliAppHostPath(), ["status"], environment: environment),
                TestContext.Current.CancellationToken
            );
            ProcessResult protocol = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    ["git", "credential-helper", "get"],
                    environment: environment,
                    standardInput: "protocol=https\nhost=dev.azure.com\n"
                        + "path=org/project/_git/repository\n\n"
                ),
                TestContext.Current.CancellationToken
            );
            ProcessResult help = await runner.RunAsync(
                new ProcessStartSpec(CliAppHostPath(), ["--help"], environment: environment),
                TestContext.Current.CancellationToken
            );
            ProcessResult acceptance = await runner.RunAsync(
                new ProcessStartSpec(CliAppHostPath(), ["acceptance"], environment: environment),
                TestContext.Current.CancellationToken
            );
            ProcessResult version = await runner.RunAsync(
                new ProcessStartSpec(CliAppHostPath(), ["--version"], environment: environment),
                TestContext.Current.CancellationToken
            );
            ProcessResult ciLogin = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    ["login", "--ci", "azure-pipelines"],
                    environment: environment
                ),
                TestContext.Current.CancellationToken
            );
            ProcessResult cleanup = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    ["cleanup", "--ci", "azure-pipelines"],
                    environment: environment
                ),
                TestContext.Current.CancellationToken
            );
            ProcessResult logout = await runner.RunAsync(
                new ProcessStartSpec(CliAppHostPath(), ["logout"], environment: environment),
                TestContext.Current.CancellationToken
            );
            ProcessResult identityRepair = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    [
                        "identity",
                        "reconfigure",
                        "--tenant",
                        "TenantA",
                        "--account",
                        "alice@example.com",
                    ],
                    environment: environment
                ),
                TestContext.Current.CancellationToken
            );
            ProcessResult repairedStatus = await runner.RunAsync(
                new ProcessStartSpec(CliAppHostPath(), ["status"], environment: environment),
                TestContext.Current.CancellationToken
            );
            AzureAuthSecureRecordReadResult repairedProvider = store.Read(
                CredentialProviderCompositionRoot.ProviderConfigRecordName
            );
            AzureAuthSecureRecordReadResult repairedBinding = store.Read(
                CredentialProviderCompositionRoot.BindingRecordName
            );
            var teardownResults = new List<ProcessResult>();
            foreach (
                string[] teardownArgs in new[]
                {
                    new[] { "unconfigure", "git" },
                    new[] { "unconfigure", "git", "--dry-run" },
                    new[] { "unconfigure", "nuget" },
                    new[] { "unconfigure", "nuget", "--dry-run" },
                }
            )
            {
                teardownResults.Add(
                    await runner.RunAsync(
                        new ProcessStartSpec(
                            CliAppHostPath(),
                            teardownArgs,
                            environment: environment
                        ),
                        TestContext.Current.CancellationToken
                    )
                );
            }

            Assert.Equal(70, cli.ExitCode);
            Assert.Equal(string.Empty, cli.StandardOutput);
            Assert.Contains(
                "configuration is unavailable",
                cli.StandardError,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(SecretMarker, cli.StandardError, StringComparison.Ordinal);
            Assert.Equal(70, protocol.ExitCode);
            Assert.Equal(string.Empty, protocol.StandardOutput);
            Assert.Contains(
                "configuration is unavailable",
                protocol.StandardError,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(SecretMarker, protocol.StandardError, StringComparison.Ordinal);
            Assert.Equal(0, help.ExitCode);
            Assert.Contains("Usage:", help.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretMarker, help.StandardError, StringComparison.Ordinal);
            Assert.Equal(0, acceptance.ExitCode);
            Assert.Equal(string.Empty, acceptance.StandardError);
            Assert.Equal(0, version.ExitCode);
            Assert.StartsWith(
                "azureauth-credprovider ",
                version.StandardOutput,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, version.StandardError);
            Assert.Equal(0, ciLogin.ExitCode);
            Assert.Contains("identity-flow: azure-pipelines", ciLogin.StandardOutput);
            Assert.DoesNotContain("opaque-ci-token", ciLogin.StandardOutput);
            Assert.Equal(0, cleanup.ExitCode);
            Assert.Equal(string.Empty, cleanup.StandardError);
            Assert.Equal(0, logout.ExitCode);
            Assert.Equal(string.Empty, logout.StandardError);
            Assert.Equal(0, identityRepair.ExitCode);
            Assert.Contains(
                "status: reconfigured",
                identityRepair.StandardOutput,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "azureauth",
                identityRepair.StandardOutput,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(string.Empty, identityRepair.StandardError);
            Assert.Equal(0, repairedStatus.ExitCode);
            Assert.Contains("provider: AzureAuth", repairedStatus.StandardOutput);
            AzureAuthProviderConfig providerConfig = AzureAuthProviderConfigJson.Deserialize(
                Encoding.UTF8.GetString(repairedProvider.Content.Span)
            );
            AzureAuthBinding binding = AzureAuthBindingJson.Deserialize(
                Encoding.UTF8.GetString(repairedBinding.Content.Span)
            );
            Assert.Equal(AzureAuthProviderSelection.AzureAuth, providerConfig.Selection);
            Assert.Equal("TenantA", binding.TenantId);
            Assert.Equal("alice@example.com", binding.AccountId);
            Assert.All(
                teardownResults,
                result =>
                {
                    Assert.Equal(0, result.ExitCode);
                    Assert.Equal(string.Empty, result.StandardError);
                    Assert.DoesNotContain(
                        "configuration is unavailable",
                        result.StandardOutput,
                        StringComparison.Ordinal
                    );
                }
            );
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact(Skip = "Non-Windows helper symlink test.", SkipWhen = nameof(IsWindows))]
    public async Task GitCredentialHelperAppHostAcceptsHelperNamedSymlink()
    {
        string tempDirectory = CreateTestDirectory();
        var runner = new SystemProcessRunner();
        string helperPath = Path.Combine(tempDirectory, "git-credential-azureauth-credprovider");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.CreateSymbolicLink(helperPath, CliAppHostPath());

            ProcessResult result = await runner.RunAsync(
                new ProcessStartSpec(
                    helperPath,
                    ["get"],
                    standardInput: """
                    protocol=https
                    host=dev.azure.com
                    path=org/project/_git/repository

                    """
                ),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(64, result.ExitCode);
            Assert.Equal(string.Empty, result.StandardOutput);
            Assert.Contains(
                "ProviderNotConfigured",
                result.StandardError,
                StringComparison.Ordinal
            );
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
        "prepare temporary dev.azure.com useHttpPath scaffold"
    )]
    [InlineData(
        "unconfigure",
        "git",
        "none",
        "remove product-owned git credential.helper entry",
        "remove product-owned dev.azure.com useHttpPath entry"
    )]
    public void DryRunCommandsAllowColonDelimitedCiMode(
        string command,
        string ecosystem,
        string ciMode,
        string plannedAction1,
        string plannedAction2
    )
    {
        CommandResult result = Invoke(command, ecosystem, "--dry-run", $"--ci:{ciMode}");

        Assert.Equal(0, result.ExitCode);
        // editorconfig-checker-disable
        Assert.Equal(
            GetExpectedDryRunOutput(command, ecosystem, ciMode, plannedAction1, plannedAction2),
            result.StdOut
        );
        // editorconfig-checker-enable
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../unsafe")]
    public void Phase14DryRunRejectsMissingOrInvalidAzurePipelinesJob(string? jobScopeId)
    {
        var runtime = new CliRuntimeOptions
        {
            CompositionRoot = CreateTestCompositionRoot(),
            ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
            {
                StateDirectoryPath = "/state/phase14-cli-dry-run",
                AzurePipelinesJobScopeId = jobScopeId,
                EnvironmentVariableReader = _ => null,
            },
        };

        CommandResult configure = InvokeWithRuntime(
            runtime,
            "configure",
            "npm",
            "--dry-run",
            "--ci",
            "azure-pipelines",
            "--registry-url",
            "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
        );
        CommandResult unconfigure = InvokeWithRuntime(
            runtime,
            "unconfigure",
            "npm",
            "--dry-run",
            "--ci",
            "azure-pipelines"
        );

        Assert.Equal(1, configure.ExitCode);
        Assert.Equal(string.Empty, configure.StdOut);
        Assert.Contains("SYSTEM_JOBID", configure.StdErr, StringComparison.Ordinal);
        Assert.Equal(1, unconfigure.ExitCode);
        Assert.Equal(string.Empty, unconfigure.StdOut);
        Assert.Contains("SYSTEM_JOBID", unconfigure.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase14DryRunRejectsUnsupportedPythonCiScope()
    {
        var runtime = new CliRuntimeOptions
        {
            CompositionRoot = CreateTestCompositionRoot(),
            ConfigurationPhase14Options = CreateConfigurationPhase14Options(
                "/state/phase14-cli-dry-run"
            ),
        };

        CommandResult result = InvokeWithRuntime(
            runtime,
            "configure",
            "python",
            "--dry-run",
            "--ci",
            "azure-pipelines"
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Contains("supports Python user scope", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase14PythonDryRunRendersActualPlanWithoutMutation()
    {
        string parentPath = CreateTestDirectory();
        string statePath = Path.Combine(parentPath, "phase14-cli-real-dry-run");
        var runtime = new CliRuntimeOptions
        {
            ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
            {
                StateDirectoryPath = statePath,
                EnvironmentVariableReader = _ => null,
            },
        };

        try
        {
            CommandResult result = InvokeWithRuntime(runtime, "configure", "python", "--dry-run");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("planned-change-count: 2\n", result.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "1. set product-owned Python keyring backend\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "2. set product-owned Python keyring shim\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.False(Directory.Exists(statePath));
            Assert.Equal(string.Empty, result.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(parentPath);
        }
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
                "prepare temporary dev.azure.com useHttpPath scaffold"
            ),
            result.StdOut
        );
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(DryRunGoldenCases))]
    public void DryRunCommandsWriteDeterministicOutput(
        string command,
        string ecosystem,
        string ciMode,
        string plannedAction1,
        string plannedAction2
    )
    {
        string[] args =
            command == "configure" && ecosystem is "npm" or "pnpm" or "yarn"
                ?
                [
                    command,
                    ecosystem,
                    "--dry-run",
                    "--ci",
                    ciMode,
                    "--registry-url",
                    "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/",
                ]
                : [command, ecosystem, "--dry-run", "--ci", ciMode];
        CommandResult result =
            ciMode == "azure-pipelines" && ecosystem is "npm" or "pnpm" or "yarn"
                ? InvokeWithRuntime(
                    new CliRuntimeOptions
                    {
                        CompositionRoot = CreateTestCompositionRoot(),
                        ConfigurationPhase14Options = CreateConfigurationPhase14Options(
                            "/state/phase14-golden"
                        ),
                    },
                    args
                )
                : Invoke(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            GetExpectedDryRunOutput(command, ecosystem, ciMode, plannedAction1, plannedAction2),
            result.StdOut
        );
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Theory]
    [MemberData(nameof(DryRunDefaultCiGoldenCases))]
    public void DryRunCommandsWithoutCiMatchExplicitNoneGolden(
        string command,
        string ecosystem,
        string plannedAction1,
        string plannedAction2
    )
    {
        string[] registryArguments =
            command == "configure" && ecosystem is "npm" or "pnpm" or "yarn"
                ?
                [
                    "--registry-url",
                    "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/",
                ]
                : [];
        CommandResult implicitCiResult = Invoke([
            command,
            ecosystem,
            "--dry-run",
            .. registryArguments,
        ]);
        CommandResult explicitCiResult = Invoke([
            command,
            ecosystem,
            "--dry-run",
            "--ci",
            "none",
            .. registryArguments,
        ]);
        string expectedOutput = GetExpectedDryRunOutput(
            command,
            ecosystem,
            "none",
            plannedAction1,
            plannedAction2
        );

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
                "--dry-run"
            );
            CommandResult explicitCiResult = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "git",
                "--dry-run",
                "--ci",
                "none"
            );

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
            "--dry-run"
        );
        CommandResult explicitCiResult = InvokeWithRuntime(
            runtimeOptions,
            "configure",
            "nuget",
            "--dry-run",
            "--ci",
            "none"
        );

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
            "--dry-run"
        );
        CommandResult explicitCiResult = InvokeWithRuntime(
            runtimeOptions,
            "unconfigure",
            "nuget",
            "--dry-run",
            "--ci",
            "none"
        );
        string expectedOutput = GetExpectedDryRunOutput(
            "unconfigure",
            "nuget",
            "none",
            "remove product-owned NuGet plugin discovery scaffold",
            "remove product-owned Azure Artifacts NuGet credential scaffold"
        );

        Assert.Equal(0, implicitCiResult.ExitCode);
        Assert.Equal(0, explicitCiResult.ExitCode);
        Assert.Equal(expectedOutput, implicitCiResult.StdOut);
        Assert.Equal(expectedOutput, explicitCiResult.StdOut);
        Assert.Equal(implicitCiResult.StdOut, explicitCiResult.StdOut);
        Assert.Equal(string.Empty, implicitCiResult.StdErr);
        Assert.Equal(string.Empty, explicitCiResult.StdErr);
    }

    [Theory]
    [InlineData("git", false)]
    [InlineData("git", true)]
    [InlineData("nuget", false)]
    [InlineData("nuget", true)]
    public void UnconfigureDoesNotLoadCredentialProviderComposition(string ecosystem, bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        var compositionFactoryCalls = 0;
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory) with
        {
            CompositionRoot = null,
            CompositionRootFactory = () =>
            {
                compositionFactoryCalls++;
                throw new InvalidOperationException("Malformed provider configuration.");
            },
        };
        string[] args = dryRun
            ? ["unconfigure", ecosystem, "--dry-run"]
            : ["unconfigure", ecosystem];

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, args);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StdErr);
            Assert.Equal(0, compositionFactoryCalls);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
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
                GetExpectedGitMutationOutput("configure", "apply", 2, true, true),
                result.StdOut
            );
            Assert.Equal(string.Empty, result.StdErr);
            Assert.True(File.Exists(service.Paths.GitConfigPath));
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.True(File.Exists(service.Paths.GitHelperPath));

            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Contains(
                $"helper = \"{service.Paths.GitHelperPath}\"",
                gitConfig,
                StringComparison.Ordinal
            );
            Assert.Contains("useHttpPath = \"true\"", gitConfig, StringComparison.Ordinal);

            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath);
            Assert.Contains("credential.helper", manifest, StringComparison.Ordinal);
            Assert.Contains(
                "credential.https://dev.azure.com.useHttpPath",
                manifest,
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("space path")]
    [InlineData("semi;path")]
    public void ConfigureGitQuotesShellSensitiveHelperPath(string sensitiveSegment)
    {
        string stateDirectory = Path.Combine(CreateTestDirectory(), sensitiveSegment);
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StdErr);
            Assert.Contains(
                $"helper = \"\\\"{service.Paths.GitHelperPath}\\\"\"",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
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
                doctorResult.StdOut
            );
            Assert.Equal(string.Empty, doctorResult.StdErr);
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
            ConfigurationPhase14Options = CreateConfigurationPhase14Options(stateDirectory),
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
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, doctorResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorDoesNotInferGitOwnershipWhenManifestIsMissing()
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
                    ownedGitEntriesPresent: false,
                    ownershipManifestPresent: false,
                    configurationPlanValid: false,
                    localShellHelperShorthandSuccess: false,
                    devAzureUseHttpPathPresent: true
                ),
                doctorResult.StdOut
            );
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
                    ownedGitEntriesPresent: false,
                    ownershipManifestPresent: true,
                    configurationPlanValid: false,
                    localShellHelperShorthandSuccess: false,
                    devAzureUseHttpPathPresent: true
                ),
                doctorResult.StdOut
            );
            Assert.Equal(string.Empty, doctorResult.StdErr);
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
            + "\"hcoona.azureAuthCredProvider.physicalTargetManifestState\":\"prepared\"}"
    )]
    public void UnconfigureGitDoesNotRemoveForeignManifestState(
        string originalManifestText,
        string replacementManifestText
    )
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
                    StringComparison.Ordinal
                )
            );

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git"
            );

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr
            );
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
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
                    StringComparison.Ordinal
                )
            );

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git",
                "--dry-run"
            );

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr
            );
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
                    StringComparison.Ordinal
                )
            );

            CommandResult secondConfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "configure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "configure", "git");

            Assert.Equal(1, secondConfigureResult.ExitCode);
            Assert.Equal(string.Empty, secondConfigureResult.StdOut);
            Assert.Equal(
                "error: configure cannot modify unrecognized Phase 8 Git state.\n",
                secondConfigureResult.StdErr
            );
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
                secondConfigureResult.StdErr
            );
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitRefusesToRemoveOverwrittenOwnedSelector(bool dryRun)
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
                    StringComparison.Ordinal
                );
            WriteOwnerOnlyText(service.Paths.GitConfigPath, tamperedGitConfig);

            CommandResult unconfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "unconfigure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "git");

            Assert.Equal(1, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdOut);
            Assert.Equal(
                "error: unconfigure cannot modify unrecognized Phase 8 Git state.\n",
                unconfigureResult.StdErr
            );
            Assert.Equal(tamperedGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
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
    public void UnconfigureGitWithoutManifestLeavesConfigurationUntouched(bool dryRun)
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

            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            if (dryRun)
            {
                Assert.NotEmpty(unconfigureResult.StdOut);
            }
            else
            {
                Assert.Equal(
                    GetExpectedGitMutationOutput("unconfigure", "not-needed", 0, false, false),
                    unconfigureResult.StdOut
                );
            }
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
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
                unconfigureResult.StdErr
            );
            Assert.True(Directory.Exists(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitPreservesExtraUrlScopedProductHelper(bool dryRun)
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
                    + "    helper = \"azureauth-credprovider\"\n"
            );

            CommandResult unconfigureResult = dryRun
                ? InvokeWithRuntime(runtimeOptions, "unconfigure", "git", "--dry-run")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "git");

            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            Assert.Contains(
                "[credential \"https://dev.azure.com/org\"]",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
            if (dryRun)
            {
                Assert.NotEmpty(unconfigureResult.StdOut);
                Assert.Equal(manifest, File.ReadAllText(service.Paths.OwnershipManifestPath));
            }
            else
            {
                Assert.Equal(
                    GetExpectedGitMutationOutput("unconfigure", "remove", 2, false, false),
                    unconfigureResult.StdOut
                );
                Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            }
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
                "git"
            );

            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            string remainingGitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Contains("[alias]", remainingGitConfig, StringComparison.Ordinal);
            Assert.Contains(
                "helper = \"azureauth-credprovider\"",
                remainingGitConfig,
                StringComparison.Ordinal
            );
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
                "git"
            );

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "remove", 2, false, false),
                unconfigureResult.StdOut
            );
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
                "git"
            );

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "remove", 2, false, false),
                unconfigureResult.StdOut
            );
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
                "git"
            );

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "remove", 2, false, false),
                unconfigureResult.StdOut
            );
            Assert.Equal(string.Empty, unconfigureResult.StdErr);

            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            Assert.Equal(
                existingGitConfig + "\n[credential]\n[credential \"https://dev.azure.com\"]",
                gitConfig
            );
            Assert.DoesNotContain("azureauth-credprovider", gitConfig, StringComparison.Ordinal);
            Assert.DoesNotContain("useHttpPath", gitConfig, StringComparison.Ordinal);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfigureAndUnconfigureNpmUsePhase14ConfigurationPlans()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntime(stateDirectory);

            CommandResult configureResult = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl
            );

            Assert.Equal(0, configureResult.ExitCode);
            Assert.Equal(
                Normalize(
                    $"""
                    command: configure
                    ecosystem: npm
                    phase: 15-end-to-end-hardening
                    ci-mode: none
                    scope: user
                    mutates-state: yes
                    plan-operation: apply
                    applied-change-count: 1
                    ownership-manifest: present
                    credential-material: not-printed
                    configuration-path: {Path.Combine(stateDirectory, "npm", "user.npmrc")}
                    """
                ),
                configureResult.StdOut
            );
            Assert.Equal(string.Empty, configureResult.StdErr);
            string npmrcPath = Path.Combine(stateDirectory, "npm", "user.npmrc");
            Assert.True(File.Exists(npmrcPath));
            Assert.Contains("_authToken=", File.ReadAllText(npmrcPath), StringComparison.Ordinal);
            Assert.DoesNotContain("fake-token-", configureResult.StdOut, StringComparison.Ordinal);

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "npm"
            );

            Assert.True(unconfigureResult.ExitCode == 0, unconfigureResult.StdErr);
            Assert.Contains("removed-change-count: 1\n", unconfigureResult.StdOut);
            Assert.DoesNotContain(
                "fake-token-",
                unconfigureResult.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("npm", false)]
    [InlineData("npm", true)]
    [InlineData("pnpm", false)]
    [InlineData("pnpm", true)]
    [InlineData("yarn", false)]
    [InlineData("yarn", true)]
    public void UserPackageConfigureRefreshAndNoOpPrintEffectiveConfigurationPath(
        string ecosystem,
        bool environmentSelected
    )
    {
        string stateDirectory = CreateTestDirectory();
        string home = Path.Combine(stateDirectory, "home");
        string selectedNpmrc = Path.Combine(stateDirectory, "selected", "user.npmrc");
        const string SelectedYarnFilename = "team.yarnrc.yml";
        try
        {
            CliRuntimeOptions baseOptions = CreateConfigurationRuntime(stateDirectory);
            CliRuntimeOptions runtimeOptions = baseOptions with
            {
                ConfigurationPhase14Options = baseOptions.ConfigurationPhase14Options! with
                {
                    EnvironmentVariableReader = name =>
                        name switch
                        {
                            "HOME" => home,
                            "NPM_CONFIG_USERCONFIG" when environmentSelected => selectedNpmrc,
                            "YARN_RC_FILENAME" when environmentSelected => SelectedYarnFilename,
                            _ => null,
                        },
                },
            };
            string expectedPath =
                ecosystem == "yarn"
                    ? Path.Combine(home, environmentSelected ? SelectedYarnFilename : ".yarnrc.yml")
                : environmentSelected ? selectedNpmrc
                : Path.Combine(home, ".npmrc");

            CommandResult configured = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                ecosystem,
                "--registry-url",
                TestRegistryUrl
            );
            CommandResult repeated = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                ecosystem,
                "--registry-url",
                TestRegistryUrl
            );
            CommandResult refreshed = InvokeWithRuntime(
                runtimeOptions,
                "refresh",
                ecosystem,
                "--registry-url",
                TestRegistryUrl
            );

            foreach (CommandResult result in new[] { configured, repeated, refreshed })
            {
                Assert.True(
                    result.ExitCode == 0,
                    "stdout: " + result.StdOut + "\nstderr: " + result.StdErr
                );
                Assert.Equal(
                    1,
                    result
                        .StdOut.Split(
                            "configuration-path: " + expectedPath,
                            StringSplitOptions.None
                        )
                        .Length - 1
                );
                Assert.DoesNotContain("fake-token-", result.StdOut, StringComparison.Ordinal);
            }

            Assert.Contains("applied-change-count: 0\n", repeated.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void NpmAndPnpmCommandsShareConfigurationAndPnpmPrintsValidActivationGuidance()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(
                stateDirectory
            );

            CommandResult pnpm = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "pnpm",
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines"
            );
            CommandResult npm = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines"
            );

            Assert.Equal(0, pnpm.ExitCode);
            Assert.Contains(
                "package-manager-argument: --config.userconfig=",
                pnpm.StdOut,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "package-manager-argument: --userconfig",
                pnpm.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains("set-environment: NPM_CONFIG_USERCONFIG=", pnpm.StdOut);
            Assert.Contains("applied-change-count: 0\n", npm.StdOut);
            Assert.Contains("mutates-state: no\n", npm.StdOut);
            Assert.Contains("configuration-path: ", npm.StdOut);
            CommandResult status = InvokeWithRuntime(runtimeOptions, "status");
            Assert.Equal(0, status.ExitCode);
            Assert.Contains(
                "npm-ci-temporary-lifecycle: fresh\n",
                status.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "pnpm-ci-temporary-lifecycle: fresh\n",
                status.StdOut,
                StringComparison.Ordinal
            );

            CommandResult removed = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "npm",
                "--ci",
                "azure-pipelines"
            );
            CommandResult secondPass = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "pnpm",
                "--ci",
                "azure-pipelines"
            );
            Assert.Contains("removed-change-count: 2\n", removed.StdOut);
            Assert.Contains("removed-change-count: 0\n", secondPass.StdOut);
            Assert.DoesNotContain("configuration-path:", removed.StdOut);
            Assert.DoesNotContain("set-environment:", removed.StdOut);
            Assert.DoesNotContain("package-manager-argument:", removed.StdOut);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("configure", "npm", "package-manager-argument: --userconfig ")]
    [InlineData("refresh", "pnpm", "package-manager-argument: --config.userconfig=")]
    [InlineData("configure", "yarn", null)]
    public void CiDryRunPrintsTemporaryConfigurationAndActivationGuidance(
        string command,
        string ecosystem,
        string? expectedPackageManagerArgument
    )
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions baseOptions = CreateConfigurationRuntimeWithCiToken(stateDirectory);
        CliRuntimeOptions runtimeOptions = baseOptions with
        {
            ConfigurationPhase14Options = baseOptions.ConfigurationPhase14Options! with
            {
                EnvironmentVariableReader = name =>
                    string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal)
                        ? "system-token"
                    : string.Equals(name, "YARN_RC_FILENAME", StringComparison.Ordinal)
                        ? "team.yarnrc.yml"
                    : ReadConfigurationEnvironment(stateDirectory, name),
            },
        };

        try
        {
            CommandResult result = InvokeWithRuntime(
                runtimeOptions,
                command,
                ecosystem,
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines",
                "--dry-run"
            );

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("mutates-state: no\n", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("temporary-container: ", result.StdOut, StringComparison.Ordinal);
            Assert.Equal(
                1,
                result.StdOut.Split("configuration-path: ", StringSplitOptions.None).Length - 1
            );
            if (expectedPackageManagerArgument is null)
            {
                Assert.DoesNotContain("package-manager-argument:", result.StdOut);
                Assert.Contains("set-environment: HOME=", result.StdOut, StringComparison.Ordinal);
                Assert.Contains(
                    "clear-environment: YARN_RC_FILENAME\n",
                    result.StdOut,
                    StringComparison.Ordinal
                );
            }
            else
            {
                Assert.Contains(
                    expectedPackageManagerArgument,
                    result.StdOut,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    "set-environment: NPM_CONFIG_USERCONFIG=",
                    result.StdOut,
                    StringComparison.Ordinal
                );
            }
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void CiUnconfigureDryRunSucceedsForValidPlanWithoutRequiringFilesystemMutation()
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(stateDirectory);
        string jobRoot = Path.Combine(stateDirectory, "ci-jobs", "cli-test-job");
        string npmrcPath = Path.Combine(jobRoot, "npm", "userconfig.npmrc");
        string manifestPath = Path.Combine(
            jobRoot,
            "manifests",
            "npm-compatible-ci-temporary-ownership-manifest.json"
        );
        try
        {
            CommandResult configure = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines"
            );
            Assert.Equal(0, configure.ExitCode);
            string npmrcBefore = File.ReadAllText(npmrcPath);
            string manifestBefore = File.ReadAllText(manifestPath);

            CommandResult dryRun = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "npm",
                "--ci",
                "azure-pipelines",
                "--dry-run"
            );

            Assert.Equal(0, dryRun.ExitCode);
            Assert.Contains("planned-change-count: 2\n", dryRun.StdOut);
            Assert.Equal(string.Empty, dryRun.StdErr);
            Assert.Equal(npmrcBefore, File.ReadAllText(npmrcPath));
            Assert.Equal(manifestBefore, File.ReadAllText(manifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void CiUnconfigureMalformedManifestReportsSameCleanupPlanWithoutDryRunMutation()
    {
        const string MalformedManifest = """{"secret":"preserve-for-diagnosis"}""";
        const string Diagnostic =
            "error: CI temporary credential cleanup is incomplete; "
            + "the ownership manifest was preserved for diagnosis.\n";
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(stateDirectory);
        string jobRoot = Path.Combine(stateDirectory, "ci-jobs", "cli-test-job");
        string npmrcPath = Path.Combine(jobRoot, "npm", "userconfig.npmrc");
        string manifestPath = Path.Combine(
            jobRoot,
            "manifests",
            "npm-compatible-ci-temporary-ownership-manifest.json"
        );
        try
        {
            CommandResult configure = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines"
            );
            Assert.Equal(0, configure.ExitCode);
            string npmrcBefore = File.ReadAllText(npmrcPath);
            File.WriteAllText(manifestPath, MalformedManifest);

            CommandResult dryRun = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "npm",
                "--ci",
                "azure-pipelines",
                "--dry-run"
            );

            Assert.Equal(1, dryRun.ExitCode);
            Assert.Contains("planned-change-count: 0\n", dryRun.StdOut);
            Assert.Equal(Diagnostic, dryRun.StdErr);
            Assert.Equal(npmrcBefore, File.ReadAllText(npmrcPath));
            Assert.Equal(MalformedManifest, File.ReadAllText(manifestPath));

            CommandResult executed = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "npm",
                "--ci",
                "azure-pipelines"
            );

            Assert.Equal(1, executed.ExitCode);
            Assert.Contains("removed-change-count: 0\n", executed.StdOut);
            Assert.Equal(Diagnostic, executed.StdErr);
            Assert.Equal(npmrcBefore, File.ReadAllText(npmrcPath));
            Assert.Equal(MalformedManifest, File.ReadAllText(manifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfigureNpmDryRunRequiresExplicitAzureArtifactsRegistry()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithoutRegistries(
                stateDirectory
            );

            CommandResult missing = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--dry-run"
            );
            CommandResult nonAzure = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--dry-run",
                "--registry-url",
                "https://registry.npmjs.org/"
            );

            Assert.Equal(2, missing.ExitCode);
            Assert.Equal(
                "error: configure for npm, pnpm, and yarn requires " + "'--registry-url <url>'.\n",
                missing.StdErr
            );
            Assert.Equal(1, nonAzure.ExitCode);
            Assert.Contains(
                "Registry declarations must use canonical Azure Artifacts npm registry URLs.",
                nonAzure.StdErr,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, missing.StdOut);
            Assert.Equal(string.Empty, nonAzure.StdOut);
            Assert.False(File.Exists(Path.Combine(stateDirectory, "npm", "user.npmrc")));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfigureNpmExplicitRegistryUrlRoundTripsExactTarget()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithoutRegistries(
                stateDirectory
            );
            const string RegistryUrl =
                "https://pkgs.dev.azure.com/real-org/real-project/"
                + "_packaging/real-feed/npm/registry/";

            CommandResult result = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                RegistryUrl
            );

            Assert.Equal(0, result.ExitCode);
            string npmrc = File.ReadAllText(Path.Combine(stateDirectory, "npm", "user.npmrc"));
            Assert.Contains(
                "//pkgs.dev.azure.com/real-org/real-project/_packaging/real-feed/npm/registry/",
                npmrc,
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void RefreshNpmSupportsDryRunExplicitExecutionAndPersistedUrlInference()
    {
        string stateDirectory = CreateTestDirectory();
        const string RegistryUrl =
            "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/";
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntime(stateDirectory);
            CommandResult dryRun = InvokeWithRuntime(
                runtimeOptions,
                "refresh",
                "npm",
                "--dry-run",
                "--registry-url",
                RegistryUrl
            );
            Assert.Equal(0, dryRun.ExitCode);
            Assert.Contains("command: refresh\n", dryRun.StdOut, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(stateDirectory, "npm", "user.npmrc")));

            CommandResult refreshed = InvokeWithRuntime(
                runtimeOptions,
                "refresh",
                "npm",
                "--registry-url",
                RegistryUrl
            );
            Assert.Equal(0, refreshed.ExitCode);
            Assert.Contains(
                "applied-change-count: 1\n",
                refreshed.StdOut,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("fake-token-test", refreshed.StdOut, StringComparison.Ordinal);

            CommandResult inferred = InvokeWithRuntime(
                runtimeOptions,
                "refresh",
                "npm",
                "--dry-run"
            );
            Assert.Equal(0, inferred.ExitCode);
            Assert.Contains("command: refresh\n", inferred.StdOut, StringComparison.Ordinal);
            Assert.Equal(string.Empty, inferred.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void RefreshCancellationReturnsCanceledExitCode()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntime("/state/canceled") with
        {
            CancellationToken = cancellation.Token,
        };

        CommandResult result = InvokeWithRuntime(
            runtimeOptions,
            "refresh",
            "npm",
            "--dry-run",
            "--registry-url",
            "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
        );

        Assert.Equal(130, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: operation canceled.\n", result.StdErr);
    }

    [Fact]
    public void FreshConfigureDoesNotLoadProviderButNonFreshConfigureDoes()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions configuredRuntime = CreateConfigurationRuntime(stateDirectory);
            CommandResult initial = InvokeWithRuntime(
                configuredRuntime,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl
            );
            Assert.Equal(0, initial.ExitCode);
            string manifestPath = Path.Combine(
                stateDirectory,
                "manifests",
                "npm-compatible-user-ownership-manifest.json"
            );
            string manifestBefore = File.ReadAllText(manifestPath);
            var compositionFactoryCalls = 0;
            CliRuntimeOptions unavailableProvider = configuredRuntime with
            {
                CompositionRoot = null,
                CompositionRootFactory = () =>
                {
                    compositionFactoryCalls++;
                    throw new InvalidOperationException("Malformed provider configuration.");
                },
            };

            CommandResult noOp = InvokeWithRuntime(
                unavailableProvider,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl
            );
            Assert.Equal(0, noOp.ExitCode);
            Assert.Equal(0, compositionFactoryCalls);
            Assert.Equal(manifestBefore, File.ReadAllText(manifestPath));

            File.Delete(manifestPath);
            CommandResult nonFresh = InvokeWithRuntime(
                unavailableProvider,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl
            );
            Assert.Equal(70, nonFresh.ExitCode);
            Assert.Equal(1, compositionFactoryCalls);
            Assert.Equal(
                "error: credential provider configuration is unavailable.\n",
                nonFresh.StdErr
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void YarnMalformedManifestMakesStatusAndDoctorInvalidInsteadOfFatal()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntime(stateDirectory);
            string manifestPath = Path.Combine(
                stateDirectory,
                "manifests",
                "yarn-user-ownership-manifest.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, """{"not":"a manifest"}""");

            CommandResult status = InvokeWithRuntime(runtimeOptions, "status");
            CommandResult doctor = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.NotEqual(70, status.ExitCode);
            Assert.Contains("yarn-user-lifecycle: invalid\n", status.StdOut);
            Assert.NotEqual(70, doctor.ExitCode);
            Assert.Contains("yarn-user-lifecycle: invalid\n", doctor.StdOut);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorTreatsRefreshRecommendedAsNonfatalWarning()
    {
        string stateDirectory = CreateTestDirectory();
        var time = new MutableTimeProvider(
            new DateTimeOffset(2029, 12, 31, 23, 0, 0, TimeSpan.Zero)
        );
        try
        {
            CliRuntimeOptions baseline = CreateConfigurationRuntime(stateDirectory);
            CliRuntimeOptions runtimeOptions = baseline with
            {
                ConfigurationPhase14Options = baseline.ConfigurationPhase14Options! with
                {
                    TimeProvider = time,
                },
            };
            CommandResult configured = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl
            );
            Assert.Equal(0, configured.ExitCode);
            time.Now = new DateTimeOffset(2029, 12, 31, 23, 50, 0, TimeSpan.Zero);

            CommandResult doctor = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Contains("configuration-aggregation: pass\n", doctor.StdOut);
            Assert.Contains("npm-user-lifecycle: refresh-recommended\n", doctor.StdOut);
            Assert.Contains(
                "npm-user-remediation: azureauth-credprovider refresh npm",
                doctor.StdOut
            );
            Assert.DoesNotContain("fake-token-test", doctor.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void CleanupDryRunWritesPhase14CiTemporaryGuidance()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(
                stateDirectory
            );
            CommandResult result = InvokeWithRuntime(
                runtimeOptions,
                "cleanup",
                "--dry-run",
                "--ci",
                "azure-pipelines"
            );

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("ecosystem: all\n", result.StdOut);
            Assert.Contains("mutates-state: no\n", result.StdOut);
            Assert.Contains("npm-ci-temporary-cleanup: not-needed\n", result.StdOut);
            Assert.Contains("pnpm-ci-temporary-cleanup: not-needed\n", result.StdOut);
            Assert.Contains("yarn-ci-temporary-cleanup: not-needed\n", result.StdOut);
            Assert.Equal(string.Empty, result.StdErr);
            CommandResult executed = InvokeWithRuntime(
                runtimeOptions,
                "cleanup",
                "--ci",
                "azure-pipelines"
            );
            Assert.Equal(0, executed.ExitCode);
            Assert.Contains("mutates-state: no\n", executed.StdOut);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void CleanupNpmCiTemporaryRemovesOwnedConfigurationWithoutPrintingToken()
    {
        string stateDirectory = CreateTestDirectory();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntimeWithCiToken(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl,
                "--ci",
                "azure-pipelines"
            );
            string npmrcPath = Path.Combine(
                stateDirectory,
                "ci-jobs",
                "cli-test-job",
                "npm",
                "userconfig.npmrc"
            );
            Assert.Equal(0, configureResult.ExitCode);
            Assert.True(File.Exists(npmrcPath));

            CommandResult cleanupResult = InvokeWithRuntime(
                runtimeOptions,
                "cleanup",
                "npm",
                "--ci",
                "azure-pipelines"
            );

            Assert.Equal(0, cleanupResult.ExitCode);
            Assert.Contains("mutates-state: yes\n", cleanupResult.StdOut);
            Assert.Contains("npm-ci-temporary-cleanup: removed\n", cleanupResult.StdOut);
            Assert.Contains("npm-ci-temporary-ownership-manifest: absent\n", cleanupResult.StdOut);
            Assert.Contains("npm-ci-temporary-temporary-container: absent\n", cleanupResult.StdOut);
            Assert.Equal(string.Empty, cleanupResult.StdErr);
            Assert.False(File.Exists(npmrcPath));
            Assert.DoesNotContain("system-token", cleanupResult.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-token-", cleanupResult.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfigureYarnReportsAggregatePhase14ConfigurationPlanCount()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            CommandResult result = InvokeWithRuntime(
                CreateConfigurationRuntime(stateDirectory),
                "configure",
                "yarn",
                "--registry-url",
                TestRegistryUrl
            );

            Assert.True(result.ExitCode == 0, result.StdErr);
            Assert.Contains("applied-change-count: 2\n", result.StdOut);
            Assert.DoesNotContain("fake-token-", result.StdOut, StringComparison.Ordinal);
            Assert.Equal(string.Empty, result.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void UnknownCommandReturnsDeterministicUsageError()
    {
        CommandResult result = Invoke("surprise");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: command is not recognized. Run 'azureauth-credprovider --help' for usage.\n",
            result.StdErr
        );
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
        string expectedError
    )
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
            "error: ecosystem must be one of: git, nuget, python, npm, pnpm, yarn.\n",
            result.StdErr
        );
    }

    [Theory]
    [InlineData("git", false)]
    [InlineData("git", true)]
    [InlineData("nuget", false)]
    [InlineData("nuget", true)]
    [InlineData("python", false)]
    [InlineData("python", true)]
    public void CleanupRejectsNonPackageEcosystemsAsUsageError(string ecosystem, bool ci)
    {
        string[] args = ci
            ? ["cleanup", ecosystem, "--ci", "azure-pipelines"]
            : ["cleanup", ecosystem];

        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            "error: cleanup ecosystem must be one of: npm, pnpm, yarn, all.\n",
            result.StdErr
        );
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
        Assert.Equal("error: option '--ci' cannot be specified more than once.\n", result.StdErr);
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
            result.StdErr
        );
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
            result.StdErr
        );
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
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
    }

    [Theory]
    [InlineData("super-secret-token")]
    [InlineData("error")]
    [InlineData("option")]
    public void UnknownOptionWithColonDelimitedSecretValueDoesNotAlterStaticDiagnostics(
        string secret
    )
    {
        CommandResult result = Invoke("status", $"--token:{secret}");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
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
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
    }

    [Fact]
    public void UnknownWhitespaceSeparatedSecretDoesNotAlterStaticDiagnostics()
    {
        CommandResult result = Invoke("status", "--token super-secret-token");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
    }

    [Fact]
    public void UnknownOptionWithSingleTokenTabSeparatedSecretValueDoesNotAlterStaticDiagnostics()
    {
        CommandResult result = Invoke("status", "--token\tsuper-secret-token");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
    }

    [Fact]
    public void UnknownControlSeparatedSecretDoesNotAlterStaticDiagnostics()
    {
        CommandResult result = Invoke("status", "--token\u001Fsuper-secret-token");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
    }

    [Theory]
    [InlineData("--token\u202Esuper-secret-token")]
    [InlineData("--token\u2066super-secret-token")]
    public void UnknownOptionWithSingleTokenFormatSeparatedSecretValueDoesNotAlterStaticDiagnostics(
        string token
    )
    {
        CommandResult result = Invoke("status", token);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal("error: option '--token' is not supported for this command.\n", result.StdErr);
    }

    [Theory]
    [InlineData("--bogus\nline=secret", "--bogus")]
    [InlineData("--bogus\u001B[31m=secret", "--bogus")]
    [InlineData("--bogus\u202Eline=secret", "--bogus")]
    [InlineData("--bogus\u2066line=secret", "--bogus")]
    public void UnknownOptionTruncatesDisplayedOptionNameAtUnsafeBoundary(
        string token,
        string expectedDisplayedOption
    )
    {
        CommandResult result = Invoke("status", token);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(
            $"error: option '{expectedDisplayedOption}' is not supported for this command.\n",
            result.StdErr
        );
    }

    [Fact]
    public void FatalPathDoesNotRedactFixedBanner()
    {
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = CliApplication.Run(
            ["--help", "--token", "error"],
            new ThrowingTextWriter(),
            stderr
        );

        Assert.Equal(70, exitCode);
        Assert.Equal("error: unexpected fatal failure.\n", stderr.ToString());
    }

    [Fact]
    public void UsageErrorReturnsExitCodeWhenStderrWriterThrows()
    {
        int exitCode = CliApplication.Run(
            ["status", "--bogus"],
            new StringWriter(new StringBuilder()),
            new ThrowingTextWriter()
        );

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void NotImplementedPathReturnsExitCodeWhenStderrWriterThrows()
    {
        int exitCode = CliApplication.Run(
            ["login", "--service-principal"],
            new StringWriter(new StringBuilder()),
            new ThrowingTextWriter()
        );

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void FatalPathReturnsExitCodeWhenStderrWriterThrows()
    {
        int exitCode = CliApplication.Run(
            ["--help", "--token", "error"],
            new ThrowingTextWriter(),
            new ThrowingTextWriter()
        );

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
        };

    public static TheoryData<string, string, string, string> DryRunDefaultCiGoldenCases =>
        new()
        {
            {
                "unconfigure",
                "git",
                "remove product-owned git credential.helper entry",
                "remove product-owned dev.azure.com useHttpPath entry"
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
            { ["--help=1"], "error: option '--help' does not accept a value.\n" },
            { ["--help", "--help=1"], "error: option '--help' does not accept a value.\n" },
            { ["-h:1"], "error: option '-h' does not accept a value.\n" },
            { ["status", "--help:1"], "error: option '--help' does not accept a value.\n" },
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
            { ["acceptance", "--bogus", "--help"], "acceptance" },
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
            { ["acceptance", "--help", "--dry-run="] },
            { ["acceptance", "--dry-run:", "-h"] },
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
                ["acceptance", "unexpected"],
                "error: acceptance does not accept positional arguments. "
                    + "Run 'azureauth-credprovider acceptance --help' for usage.\n"
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
            { ["--bogus"], "error: option '--bogus' is not supported for this command.\n" },
            {
                ["status", "--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
            {
                ["doctor", "--bogus"],
                "error: option '--bogus' is not supported for this command.\n"
            },
            {
                ["acceptance", "--bogus"],
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

    // editorconfig-checker-disable
    private static string GetExpectedHelp(string command)
    {
        return Normalize(
            command switch
            {
                "status" => """
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
                "configure" => """
                azureauth-credprovider configure
                Usage:
                """
                    + "\n"
                    + "  azureauth-credprovider configure <ecosystem> [--dry-run] [--ci <mode>] "
                    + "--registry-url <url> [--help]\n"
                    + """

                    Ecosystems:
                      git
                      nuget
                      python
                      npm
                      pnpm
                      yarn

                    Options:
                    """
                    + "\n"
                    + "  --dry-run                    Render planned actions without "
                    + "mutating files.\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + "  --registry-url <url>         Required Azure Artifacts npm URL for "
                    + "npm, pnpm, and Yarn.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "unconfigure" => """
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
                      pnpm
                      yarn

                    Options:
                    """
                    + "\n"
                    + "  --dry-run                    Render planned actions without "
                    + "mutating files.\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "identity" => """
                azureauth-credprovider identity
                Usage:
                  azureauth-credprovider identity configure --tenant <id> [--account <name>] [--help]
                  azureauth-credprovider identity reconfigure --tenant <id> [--account <name>] [--help]
                  azureauth-credprovider identity unconfigure [--help]

                Actions:
                  configure                    Record identity context if none exists.
                  reconfigure                  Replace or repair identity context.
                  unconfigure                  Remove recorded identity context.

                Options:
                  --tenant <id>                Required tenant for configure and reconfigure.
                  --account <name>             Optional account preference.
                  -h, --help                   Show help.

                Identity configuration stores invocation context only.
                It stores no credentials and does not verify the account.
                """,
                "refresh" => """
                azureauth-credprovider refresh
                Usage:
                """
                    + "\n"
                    + "  azureauth-credprovider refresh <ecosystem> [--dry-run] [--ci <mode>] "
                    + "[--registry-url <url>] [--help]\n"
                    + """

                    Ecosystems:
                      npm
                      pnpm
                      yarn

                    Options:
                    """
                    + "\n"
                    + "  --dry-run                    Render planned actions without "
                    + "mutating files.\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + "  --registry-url <url>         Azure Artifacts npm URL; optional only "
                    + "when the canonical ownership manifest is valid.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "doctor" => """
                azureauth-credprovider doctor
                Usage:
                  azureauth-credprovider doctor [--help]

                Status:
                  Run safe deterministic cross-ecosystem checks and remediation guidance.

                Options:
                  -h, --help                   Show help.
                """,
                "cleanup" => """
                azureauth-credprovider cleanup
                Usage:
                """
                    + "\n  azureauth-credprovider cleanup [<ecosystem>|all] [--dry-run] "
                    + "[--ci <mode>] [--help]\n"
                    + """

                    Ecosystems:
                      npm
                      pnpm
                      yarn

                    Status:
                      Clean product-owned CI temporary package configuration.
                      User-level integration removal stays under unconfigure <ecosystem>.

                    Options:
                      --dry-run                    Render cleanup actions without mutating files.
                    """
                    + "\n  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + """
                      -h, --help                   Show help.
                    """,
                "acceptance" => """
                azureauth-credprovider acceptance
                Usage:
                  azureauth-credprovider acceptance [--help]

                Status:
                  Render the executable Phase 15 release-hardening acceptance matrix.
                  Deferred rows are not accepted support claims.

                Options:
                  -h, --help                   Show help.
                """,
                "login" => """
                azureauth-credprovider login
                Usage:
                  azureauth-credprovider login [--browser|--device-code|--pat <value>]
                  azureauth-credprovider login --ci azure-pipelines

                Identity flow options:
                  --browser                    Use interactive browser authentication.
                  --device-code                Use device-code authentication.
                  --pat <value>                Deferred PAT compatibility placeholder; never persisted.
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
                "logout" => """
                azureauth-credprovider logout
                Usage:
                  azureauth-credprovider logout [--help]

                Status:
                  Clears product-owned authentication state, then job-scoped CI temporary state.

                Options:
                  -h, --help                   Show help.
                """,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(command),
                    command,
                    "Unsupported help command."
                ),
            }
        );
    }

    // editorconfig-checker-enable

    // editorconfig-checker-disable
    private static string GetExpectedDryRunOutput(
        string command,
        string ecosystem,
        string ciMode,
        params string[] plannedActions
    )
    {
        List<string> lines =
        [
            $"command: {command}",
            $"ecosystem: {ecosystem}",
            "phase: 15-end-to-end-hardening",
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
            phase: 15-end-to-end-hardening
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
            phase: 15-end-to-end-hardening
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

    // editorconfig-checker-enable
    private static string GetExpectedGitMutationOutput(
        string command,
        string planOperation,
        int changeCount,
        bool ownedGitEntriesPresent,
        bool ownershipManifestPresent
    )
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
                    "phase: 15-end-to-end-hardening",
                    "ci-mode: none",
                    "scope: user",
                    "mutates-state: yes",
                    $"plan-operation: {planOperation}",
                    $"{countLabel}: {changeCount}",
                    $"owned-git-entries: {(ownedGitEntriesPresent ? "present" : "absent")}",
                    $"ownership-manifest: {(ownershipManifestPresent ? "present" : "absent")}",
                    "note: credential material is not printed",
                ]
            )
        );
    }

    private static string GetExpectedDoctorOutput(
        bool ownedGitEntriesPresent,
        bool ownershipManifestPresent,
        bool configurationPlanValid = true,
        bool? localShellHelperShorthandSuccess = null,
        bool? devAzureUseHttpPathPresent = null
    )
    {
        bool useHttpPathPresent = devAzureUseHttpPathPresent ?? ownedGitEntriesPresent;
        bool localShellSuccess = localShellHelperShorthandSuccess ?? ownedGitEntriesPresent;
        return Normalize(
            string.Join(
                "\n",
                [
                    "command: doctor",
                    "phase: 15-end-to-end-hardening",
                    "composition-mode: TestScaffold",
                    "provider: DirectMsal",
                    "interactive-readiness: unavailable",
                    "interactive-readiness-code: TestScaffold",
                    "interactive-blocker: Explicit deterministic test scaffold; "
                        + "not production-ready.",
                    "silent-readiness: silent-unavailable",
                    "silent-readiness-code: TestScaffold",
                    "silent-remediation: Explicit deterministic test scaffold; "
                        + "not production-ready.",
                    $"configuration-plan: {(configurationPlanValid ? "pass" : "fail")}",
                    $"owned-git-entries: {(ownedGitEntriesPresent ? "present" : "absent")}",
                    $"ownership-manifest: {(ownershipManifestPresent ? "present" : "absent")}",
                    "dev.azure.com-useHttpPath: " + (useHttpPathPresent ? "present" : "absent"),
                    "credential-core: pass",
                    "git-credential-helper-get: pass",
                    "git-credential-helper-store: pass",
                    "git-credential-helper-erase: pass",
                    "local-shell-helper-shorthand: " + (localShellSuccess ? "pass" : "fail"),
                    "protocol-payload: captured-not-printed",
                    "auth-accepted-identity-flows: browser, azure-pipelines",
                    "auth-unavailable-identity-flows: device-code",
                    "auth-deferred-identity-flows: "
                        + "pat-compatibility, service-principal, managed-identity, "
                        + "workload-identity",
                    "auth-pat-compatibility: deferred-disabled",
                    "auth-persistent-derived-credentials: disabled",
                    "auth-plaintext-fallback: disabled",
                    "nuget-configuration-plan: pass",
                    "nuget-plugin-layout-marker: absent",
                    "nuget-ownership-manifest: absent",
                    "nuget-netcore-plugin-entrypoint: fail",
                    "nuget-plugin-mode-entrypoint: fail",
                    "nuget-azure-artifacts-source: pass",
                    "nuget-interactive-policy: fail",
                    "nuget-environment-overrides: absent",
                    "configuration-aggregation: pass",
                    "npm-user-configuration-plan: pass",
                    "npm-user-owned-targets: absent",
                    "npm-user-ownership-manifest: absent",
                    "npm-user-lifecycle: missing",
                    "npm-user-remediation: azureauth-credprovider configure npm --registry-url "
                        + "<azure-artifacts-npm-registry-url>",
                    "pnpm-user-configuration-plan: pass",
                    "pnpm-user-owned-targets: absent",
                    "pnpm-user-ownership-manifest: absent",
                    "pnpm-user-lifecycle: missing",
                    "pnpm-user-remediation: azureauth-credprovider configure pnpm --registry-url "
                        + "<azure-artifacts-npm-registry-url>",
                    "python-user-configuration-plan: pass",
                    "python-user-owned-targets: absent",
                    "python-user-ownership-manifest: absent",
                    "python-user-remediation: azureauth-credprovider configure python",
                    "yarn-user-configuration-plan: pass",
                    "yarn-user-owned-targets: absent",
                    "yarn-user-ownership-manifest: absent",
                    "yarn-user-lifecycle: missing",
                    "yarn-user-remediation: azureauth-credprovider configure yarn --registry-url "
                        + "<azure-artifacts-npm-registry-url>",
                    "ci-system-access-token: absent",
                    "ci-temporary-cleanup-command: "
                        + "azureauth-credprovider cleanup --ci azure-pipelines",
                    "ci-guidance: set SYSTEM_ACCESSTOKEN and use --ci azure-pipelines in CI",
                    "persistent-derived-credential-cache: disabled",
                ]
            )
        );
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
        params string[] args
    )
    {
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = runtimeOptions is null
            ? CliApplication.Run(args, stdout, stderr)
            : CliApplication.Run(args, stdout, stderr, runtimeOptions);

        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static CliRuntimeOptions CreateAuthRuntimeWithEnvironment(
        Dictionary<string, string> environment
    )
    {
        return new CliRuntimeOptions
        {
            AuthPhase14Options = new AuthPhase14VerticalSliceOptions
            {
                CredentialCoreService = new CredentialCoreService(
                    new DeterministicFakeIdentityProvider()
                ),
                EnvironmentVariableReader = name =>
                    environment.TryGetValue(name, out string? value) ? value : null,
            },
        };
    }

    private static CliRuntimeOptions CreateConfigurationRuntime(string stateDirectoryPath)
    {
        return new CliRuntimeOptions
        {
            CompositionRoot = CreateTestCompositionRoot(),
            ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectoryPath,
                AzurePipelinesJobScopeId = "cli-test-job",
                EnvironmentVariableReader = name =>
                    ReadConfigurationEnvironment(stateDirectoryPath, name),
                RegistryUrls = CreateTestRegistryUrls(),
            },
        };
    }

    private static CliRuntimeOptions CreateConfigurationRuntimeWithoutRegistries(
        string stateDirectoryPath
    ) =>
        new()
        {
            CompositionRoot = CreateTestCompositionRoot(),
            ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectoryPath,
                AzurePipelinesJobScopeId = "cli-test-job",
                EnvironmentVariableReader = name =>
                    ReadConfigurationEnvironment(stateDirectoryPath, name),
            },
        };

    private static CliRuntimeOptions CreateConfigurationRuntimeWithCiToken(
        string stateDirectoryPath,
        string token = "system-token"
    )
    {
        return new CliRuntimeOptions
        {
            CompositionRoot = CreateTestCompositionRoot(),
            ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectoryPath,
                AzurePipelinesJobScopeId = "cli-test-job",
                EnvironmentVariableReader = name =>
                    string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal)
                        ? token
                        : ReadConfigurationEnvironment(stateDirectoryPath, name),
                RegistryUrls = CreateTestRegistryUrls(),
            },
        };
    }

    private static string? ReadConfigurationEnvironment(string stateDirectoryPath, string name) =>
        name switch
        {
            "NPM_CONFIG_USERCONFIG" => Path.Combine(stateDirectoryPath, "npm", "user.npmrc"),
            "HOME" => Path.Combine(stateDirectoryPath, "yarn"),
            _ => null,
        };

    private static CommandResult InvokeWithStandardInput(
        string standardInput,
        string executablePath,
        params string[] args
    )
    {
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());

        int exitCode = CliApplication.Run(
            args,
            stdout,
            stderr,
            new CliRuntimeOptions { CompositionRoot = CreateTestCompositionRoot() },
            new StringReader(standardInput),
            executablePath
        );

        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static CliRuntimeOptions CreateGitPhase8RuntimeOptions(string stateDirectory)
    {
        return new CliRuntimeOptions
        {
            CompositionRoot = CreateTestCompositionRoot(),
            GitPhase8Options = new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            },
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
            ConfigurationPhase14Options = CreateConfigurationPhase14Options(stateDirectory),
        };
    }

    private static CredentialProviderCompositionRoot CreateTestCompositionRoot() =>
        CredentialProviderCompositionRoot.CreateExplicitTestScaffold(
            new TestScaffoldAcquisitionService()
        );

    private static ConfigurationPhase14VerticalSliceOptions CreateConfigurationPhase14Options(
        string stateDirectoryPath
    ) =>
        new()
        {
            StateDirectoryPath = Path.Combine(stateDirectoryPath, "configuration"),
            AzurePipelinesJobScopeId = "cli-test-job",
            EnvironmentVariableReader = _ => null,
            RegistryUrls = CreateTestRegistryUrls(),
        };

    private static Dictionary<CredentialEcosystem, Uri> CreateTestRegistryUrls() =>
        new Dictionary<CredentialEcosystem, Uri>
        {
            [CredentialEcosystem.Npm] = new(
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
            ),
            [CredentialEcosystem.Pnpm] = new(
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
            ),
            [CredentialEcosystem.Yarn] = new(
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
            ),
        };

    private static CliRuntimeOptions CreateNuGetPhase10DryRunRuntimeOptions()
    {
        return new CliRuntimeOptions { NuGetPhase10Options = CreateIsolatedNuGetPhase10Options() };
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
            }
        );
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
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        return executablePath;
    }

    private static string CliAppHostPath()
    {
        string assemblyPath = typeof(CliApplication).Assembly.Location;
        string directory =
            Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException(
                $"CLI assembly path '{assemblyPath}' does not have a parent directory."
            );
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
                appHostPath
            );
        }

        return appHostPath;
    }

    private sealed class TestScaffoldAcquisitionService : ICredentialAcquisitionService
    {
        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                request.CredentialKind
                    is CredentialKind.BasicPassword
                        or CredentialKind.NuGetPluginCredential
                    ? new CredentialResult
                    {
                        Status = CredentialResultStatus.Success,
                        Username = "AzureDevOps",
                        Password = "fake-secret-test",
                        Account = request.AccountHint?.ToLowerInvariant() ?? "unbound",
                        Tenant = request.TenantHint?.ToLowerInvariant() ?? "unbound",
                        DiagnosticsCorrelationId = "cli-test-scaffold",
                    }
                    : new CredentialResult
                    {
                        Status = CredentialResultStatus.Success,
                        BearerToken = "fake-token-test",
                        ExpiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        Account = request.AccountHint?.ToLowerInvariant() ?? "unbound",
                        Tenant = request.TenantHint?.ToLowerInvariant() ?? "unbound",
                        DiagnosticsCorrelationId = "cli-test-scaffold",
                    }
            );
        }
    }

    private sealed class ConflictingIdentityRecordStore : IAzureAuthSecureRecordStore
    {
        public AzureAuthSecureRecordReadResult Read(string path) =>
            AzureAuthSecureRecordReadResult.Missing();

        public AzureAuthSecureRecordWriteResult CompareExchange(
            string path,
            string expectedRevision,
            ReadOnlyMemory<byte> newContent
        ) => AzureAuthSecureRecordWriteResult.Conflict();

        public AzureAuthSecureRecordWriteResult CompareDelete(
            string path,
            string expectedRevision
        ) => AzureAuthSecureRecordWriteResult.Conflict();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PassingGitDiscoveryProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
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
                    string.Empty
                )
            );
        }

        private static void TryWriteHelperMarker(ProcessStartSpec startSpec)
        {
            if (
                !startSpec.Environment.TryGetValue(
                    "AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER",
                    out string? markerPath
                ) || string.IsNullOrEmpty(markerPath)
            )
            {
                return;
            }

            File.WriteAllText(markerPath, string.Empty);
        }
    }

    private static string CreateTestDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "azureauth-credprovider-cli-tests");
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
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
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
        public bool FileExists(string path) => false;

        public bool DirectoryExists(string path) => false;

        public string GetFullPath(string path) => Path.GetFullPath(path);

        public bool IsPathFullyQualified(string path) => Path.IsPathFullyQualified(path);

        public string ReadAllText(string path, Encoding? encoding = null) =>
            throw CreateMissingPathException(path);

        public byte[] ReadAllBytes(string path) => throw CreateMissingPathException(path);

        public long GetFileLength(string path) => throw CreateMissingPathException(path);

        public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
            throw CreateMutationException();

        public void AtomicWriteAllText(
            string path,
            string contents,
            Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None
        ) => throw CreateMutationException();

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None
        ) => throw CreateMutationException();

        public UnixFileMode GetUnixFileMode(string path) => throw CreateMissingPathException(path);

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            throw CreateMutationException();

        public void CreateDirectory(string path) => throw CreateMutationException();

        public void DeleteFile(string path) => throw CreateMutationException();

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
