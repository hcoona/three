using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
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
                  refresh <ecosystem>          Refresh an npm, pnpm, or Yarn 4+ credential.
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

    [Fact]
    public void VersionUsesAssemblyInformationalVersion()
    {
        string informationalVersion = Assert.IsType<AssemblyInformationalVersionAttribute>(
            typeof(CliApplication).Assembly.GetCustomAttribute<
                AssemblyInformationalVersionAttribute
            >()
        ).InformationalVersion;

        CommandResult result = Invoke("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"azureauth-credprovider {informationalVersion}\n", result.StdOut);
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

    private const string KeyringModuleFoundOutput = "ACP_KEYRING_PROBE_V1:FOUND\n";
    private const string KeyringModuleNotFoundOutput =
        "ACP_KEYRING_PROBE_V1:NOT_FOUND\n";

    private sealed class RecordingPythonResolutionProcessRunner : IProcessRunner
    {
        private readonly Queue<Func<ProcessStartSpec, ProcessResult>> handlers = [];

        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public void EnqueueResult(ProcessResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            handlers.Enqueue(_ => result);
        }

        public void EnqueueFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            handlers.Enqueue(_ => throw exception);
        }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();
            StartSpecs.Add(startSpec);

            if (handlers.Count == 0)
            {
                string script =
                    startSpec.Arguments.Count > 1
                        ? startSpec.Arguments[1]
                        : string.Empty;
                return Task.FromResult(
                    new ProcessResult(
                        0,
                        script.Contains(
                            "ACP_AZUREAUTH_PRODUCT_PROBE_V1",
                            StringComparison.Ordinal
                        )
                            ? "ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY\n"
                            : KeyringModuleFoundOutput,
                        string.Empty
                    )
                );
            }

            return Task.FromResult(handlers.Dequeue()(startSpec));
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
            "standalone-linux-x64-platform-acceptance: pass\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "standalone-linux-x64-platform-acceptance-evidence: "
                + "phase-wp3-azureauth-process-provider; "
                + "phase-wp16-deployment-validation-bundle; implementation after "
                + "commit 46424808; "
                + "AzureAuth 0.9.5 release commit 21258ff3\n",
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
                product-plaintext-fallback: disabled
                """
            ),
            result.StdOut
        );
        Assert.Equal(string.Empty, result.StdErr);
        Assert.DoesNotContain("fake-token-", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-secret-", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginDeviceCodeWithAcceptedCredentialReturnsSuccessWithoutBrowserRemediation()
    {
        var acquisitionService = new CapturingAcceptedDeviceCodeAcquisitionService();
        CommandResult result = InvokeWithRuntime(
            new CliRuntimeOptions
            {
                CompositionRoot = CredentialProviderCompositionRoot.CreateExplicitTestScaffold(
                    acquisitionService
                ),
            },
            "login",
            "--device-code"
        );

        Assert.Equal(
            Normalize(
                """
                command: login
                phase: 15-end-to-end-hardening
                ci-mode: none
                identity-flow: device-code
                status: success
                account: unbound
                tenant: unbound
                credential-material: issued-not-printed
                persistent-derived-credentials: disabled
                product-plaintext-fallback: disabled
                """
            ),
            result.StdOut
        );
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        CredentialRequestV2 request = Assert.IsType<CredentialRequestV2>(
            acquisitionService.Request
        );
        Assert.Equal(CredentialKind.BasicPassword, request.CredentialKind);
        Assert.Equal(IdentityFlow.DeviceCode, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
        Assert.DoesNotContain(
            "Device-code login is unavailable",
            result.StdErr,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            CapturingAcceptedDeviceCodeAcquisitionService.Password,
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            CapturingAcceptedDeviceCodeAcquisitionService.Password,
            result.StdErr,
            StringComparison.Ordinal
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
                product-plaintext-fallback: disabled
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
                product-plaintext-fallback: disabled
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
    public void GitCredentialHelperProtocolPropagatesRuntimeCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var acquisition = new CapturingCancellationTokenAcquisitionService(cancellation);
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        var runtimeOptions = new CliRuntimeOptions
        {
            CancellationToken = cancellation.Token,
            CompositionRoot =
                CredentialProviderCompositionRoot.CreateExplicitTestScaffold(acquisition),
        };

        int exitCode = CliApplication.Run(
            ["git", "credential-helper", "get"],
            stdout,
            stderr,
            runtimeOptions,
            new StringReader(
                """
                protocol=https
                host=dev.azure.com
                path=org/project/_git/repository

                """
            ),
            "azureauth-credprovider"
        );

        Assert.Equal(cancellation.Token, acquisition.CancellationToken);
        Assert.Equal(130, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal("error: operation canceled.\n", stderr.ToString());
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
        string configRoot = CreateTestDirectory();
        var runner = new SystemProcessRunner();

        try
        {
            ProcessResult result = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    ["git", "credential-helper", "get"],
                    environment: new Dictionary<string, string?>
                    {
                        [SystemAzureAuthSecureRecordStoreOptions.ConfigRootEnvironmentVariable] =
                            configRoot,
                        ["GIT_TERMINAL_PROMPT"] = "0",
                    },
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
            DeleteDirectoryIfExists(configRoot);
        }
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
            ProcessResult unsupportedKeyringHost = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    ["keyring", "get", "https://example.com/simple/", "requested-user"],
                    environment: environment
                ),
                TestContext.Current.CancellationToken
            );
            ProcessResult unsupportedLegacyKeyringHost = await runner.RunAsync(
                new ProcessStartSpec(
                    CliAppHostPath(),
                    [
                        "keyring",
                        "get",
                        "https://foo.bar.visualstudio.com/_packaging/feed/pypi/simple/",
                        "requested-user",
                    ],
                    environment: environment
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
            Assert.Equal(1, unsupportedKeyringHost.ExitCode);
            Assert.Equal(string.Empty, unsupportedKeyringHost.StandardOutput);
            Assert.Equal(string.Empty, unsupportedKeyringHost.StandardError);
            Assert.Equal(1, unsupportedLegacyKeyringHost.ExitCode);
            Assert.Equal(string.Empty, unsupportedLegacyKeyringHost.StandardOutput);
            Assert.Equal(string.Empty, unsupportedLegacyKeyringHost.StandardError);
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

    [Fact(
        Skip = "System secure-store integration is Linux/WSL-specific.",
        SkipWhen = nameof(IsWindows)
    )]
    public async Task AppHostConfigureAndDryRunIgnoreMalformedProviderRecord()
    {
        const string SecretMarker = "must-not-leak-malformed-configure-secret";
        string rootPath = Path.Combine(
            AppContext.BaseDirectory,
            "malformed-configure-" + Guid.NewGuid().ToString("N")
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
            };
            var runner = new SystemProcessRunner();

            foreach (
                (string ecosystem, bool dryRun) in new[]
                {
                    ("git", false),
                    ("git", true),
                    ("nuget", false),
                    ("nuget", true),
                }
            )
            {
                string[] args = dryRun
                    ? ["configure", ecosystem, "--dry-run"]
                    : ["configure", ecosystem];
                ProcessResult result = await runner.RunAsync(
                    new ProcessStartSpec(CliAppHostPath(), args, environment: environment),
                    TestContext.Current.CancellationToken
                );

                Assert.Equal(0, result.ExitCode);
                Assert.Contains(
                    "command: configure",
                    result.StandardOutput,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    $"ecosystem: {ecosystem}",
                    result.StandardOutput,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    dryRun ? "mutates-state: no" : "mutates-state: yes",
                    result.StandardOutput,
                    StringComparison.Ordinal
                );
                Assert.Equal(string.Empty, result.StandardError);
                Assert.DoesNotContain(
                    SecretMarker,
                    result.StandardOutput,
                    StringComparison.Ordinal
                );
            }
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
        string configRoot = CreateTestDirectory();
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
                    environment: new Dictionary<string, string?>
                    {
                        [SystemAzureAuthSecureRecordStoreOptions.ConfigRootEnvironmentVariable] =
                            configRoot,
                        ["GIT_TERMINAL_PROMPT"] = "0",
                    },
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
            DeleteDirectoryIfExists(configRoot);
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
        string stateDirectory = CreateTestDirectory();
        try
        {
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = CreateTestCompositionRoot(),
                ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
                {
                    StateDirectoryPath = Path.Combine(
                        stateDirectory,
                        "phase14-cli-dry-run"
                    ),
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
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void Phase14DryRunRejectsUnsupportedPythonCiScope()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = CreateTestCompositionRoot(),
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(stateDirectory),
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
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void Phase14PythonDryRunRendersActualPlanWithoutMutation()
    {
        string parentPath = CreateTestDirectory();
        string statePath = Path.Combine(parentPath, "phase14-cli-real-dry-run");
        using var pythonFixture = new PythonDoctorFixture(
            PythonDoctorFixtureMode.Healthy
        );
        var runtime = new CliRuntimeOptions
        {
            PythonPhase11Options = pythonFixture.Options,
            ConfigurationPhase14Options = new ConfigurationPhase14VerticalSliceOptions
            {
                StateDirectoryPath = statePath,
                EnvironmentVariableReader = name =>
                    ReadConfigurationPhase14Environment(parentPath, name),
                ProductExecutablePath = GetTestProductExecutablePath(),
            },
        };

        try
        {
            CommandResult result = InvokeWithRuntime(runtime, "configure", "python", "--dry-run");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                OperatingSystem.IsWindows()
                    ? "planned-change-count: 1\n"
                    : "planned-change-count: 2\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "1. set product-owned Python keyring backend\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            if (!OperatingSystem.IsWindows())
            {
                Assert.Contains(
                    "2. set product-owned Python keyring shim\n",
                    result.StdOut,
                    StringComparison.Ordinal
                );
            }
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

    [Theory]
    [InlineData("git", false)]
    [InlineData("git", true)]
    [InlineData("nuget", false)]
    [InlineData("nuget", true)]
    public void ConfigureDoesNotLoadCredentialProviderCompositionWhenProviderRecordIsMalformed(
        string ecosystem,
        bool dryRun
    )
    {
        string stateDirectory = CreateTestDirectory();
        var compositionFactoryCalls = 0;
        var nuGetFileSystem =
            new EmptyNuGetDryRunFileSystem.RecordingNuGetConfigurationFileSystem();
        var nuGetOptions = new NuGetPhase10VerticalSliceOptions
        {
            StateDirectoryPath = Path.Combine(stateDirectory, "nuget"),
            FileSystem = nuGetFileSystem,
            EnvironmentVariableReader = _ => null,
        };
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory) with
        {
            CompositionRoot = null,
            CompositionRootFactory = () =>
            {
                compositionFactoryCalls++;
                throw new InvalidOperationException("Malformed provider record.");
            },
            NuGetPhase10Options = nuGetOptions,
        };
        GitPhase8VerticalSliceService gitService =
            GitPhase8VerticalSliceService.CreateConfigurationOnly(
                runtimeOptions.GitPhase8Options
            );
        NuGetPhase10VerticalSliceService nuGetService =
            NuGetPhase10VerticalSliceService.CreateConfigurationOnly(nuGetOptions);
        string[] args = dryRun
            ? ["configure", ecosystem, "--dry-run"]
            : ["configure", ecosystem];

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, args);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                ecosystem == "git"
                    ? dryRun
                        ? GetExpectedGitConfigureDryRunOutput()
                        : GetExpectedGitMutationOutput("configure", "apply", 2, true, true)
                    : dryRun
                        ? GetExpectedNuGetConfigureDryRunOutput()
                        : GetExpectedNuGetConfigureOutput(),
                result.StdOut
            );
            Assert.Equal(string.Empty, result.StdErr);
            Assert.Equal(0, compositionFactoryCalls);

            if (ecosystem == "git")
            {
                Assert.Equal(!dryRun, File.Exists(gitService.Paths.GitConfigPath));
                Assert.Equal(!dryRun, File.Exists(gitService.Paths.OwnershipManifestPath));
                Assert.Equal(!dryRun, File.Exists(gitService.Paths.GitHelperPath));
                if (!dryRun)
                {
                    string gitConfig = File.ReadAllText(gitService.Paths.GitConfigPath);
                    string serializedHelper = GetSerializedGitHelperPath(
                        gitService.Paths.GitHelperPath
                    );
                    Assert.Contains(
                        $"helper = \"{serializedHelper}\"",
                        gitConfig,
                        StringComparison.Ordinal
                    );
                    Assert.Contains("useHttpPath = \"true\"", gitConfig, StringComparison.Ordinal);
                }
            }
            else
            {
                Assert.Equal(
                    !dryRun,
                    nuGetFileSystem.FileExists(nuGetService.Paths.PluginLayoutMarkerPath)
                );
                Assert.Equal(
                    !dryRun,
                    nuGetFileSystem.FileExists(nuGetService.Paths.OwnershipManifestPath)
                );
                if (!dryRun)
                {
                    Assert.Equal(
                        "azureauth-credprovider nuget-plugin-layout\n"
                            + "phase=10\n"
                            + "runtime=netcore\n"
                            + "entrypoint=azureauth-credprovider.dll\n",
                        nuGetFileSystem.ReadAllText(nuGetService.Paths.PluginLayoutMarkerPath)
                    );
                    Assert.Contains(
                        "phase10-nuget-plugin-layout",
                        nuGetFileSystem.ReadAllText(nuGetService.Paths.OwnershipManifestPath),
                        StringComparison.Ordinal
                    );
                }
            }
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
                $"helper = \"{GetSerializedGitHelperPath(service.Paths.GitHelperPath)}\"",
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
                $"helper = \"\\\"{GetSerializedGitHelperPath(service.Paths.GitHelperPath)}\\\"\"",
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
    public void DoctorProductionCompositionIncludesNpmAndYarnDoctorResults()
    {
        string stateDirectory = CreateTestDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(stateDirectory, ".npmrc"),
                $"registry={TestRegistryUrl}\n"
            );
            File.WriteAllText(
                Path.Combine(stateDirectory, ".yarnrc.yml"),
                $"npmRegistryServer: \"{TestRegistryUrl}\"\n"
            );
            var runtimeOptions = new CliRuntimeOptions
            {
                CompositionRootFactory = () =>
                    CredentialProviderCompositionRoot.CreateProduction(
                        new CredentialProviderProductionOptions
                        {
                            SecureStoreRootPath = stateDirectory,
                            EnvironmentVariableReader = _ => null,
                        }
                    ),
                GitPhase8Options = new GitPhase8VerticalSliceOptions
                {
                    StateDirectoryPath = stateDirectory,
                    GitConfigurationProbeWorkingDirectoryPath = stateDirectory,
                    ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                    ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
                },
                NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
                NpmPhase12Options = CreateIsolatedNpmPhase12Options(stateDirectory),
                YarnPhase13Options = CreateIsolatedYarnPhase13Options(stateDirectory),
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(stateDirectory),
            };

            CommandResult doctor = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.Equal(1, doctor.ExitCode);
            AssertDoctorCheck(doctor.StdOut, "composition-mode", "Production");
            AssertDoctorCheck(doctor.StdOut, "npm-registry-declaration", "present");
            AssertDoctorCheck(
                doctor.StdOut,
                "npm-azure-artifacts-endpoint-canonicalization",
                "pass"
            );
            AssertDoctorCheck(doctor.StdOut, "yarn-registry-declaration", "present");
            AssertDoctorCheck(
                doctor.StdOut,
                "yarn-azure-artifacts-endpoint-canonicalization",
                "pass"
            );
            AssertDoctorCheck(doctor.StdOut, "yarn-writes", "pass");
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
                GitConfigurationProbeWorkingDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                LocalShellGitDiscoverySupported = false,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            },
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
            NpmPhase12Options = CreateIsolatedNpmPhase12Options(stateDirectory),
            YarnPhase13Options = CreateIsolatedYarnPhase13Options(stateDirectory),
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
                    devAzureUseHttpPathPresent: true,
                    productHelperActive: true
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
                    devAzureUseHttpPathPresent: true,
                    productHelperActive: true
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
                    $"helper = \"{GetSerializedGitHelperPath(service.Paths.GitHelperPath)}\"",
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

    private static string GetSerializedGitHelperPath(string path) => path.Replace('\\', '/');

    [Fact]
    public void UnconfigureGitResumesInactiveOwnedCleanup()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateGitPhase8Service(stateDirectory);
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory);

        try
        {
            CommandResult configureResult = InvokeWithRuntime(runtimeOptions, "configure", "git");
            Assert.Equal(0, configureResult.ExitCode);
            File.WriteAllText(service.Paths.UserGitConfigPath, string.Empty);

            CommandResult unconfigureResult = InvokeWithRuntime(
                runtimeOptions,
                "unconfigure",
                "git"
            );

            Assert.Equal(0, unconfigureResult.ExitCode);
            Assert.Equal(
                GetExpectedGitMutationOutput("unconfigure", "remove", 2, false, false),
                unconfigureResult.StdOut
            );
            Assert.Equal(string.Empty, unconfigureResult.StdErr);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfigureGitWithoutManifestFailsClosedAndLeavesConfigurationUntouched(
        bool dryRun
    )
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
                unconfigureResult.StdErr
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

    [Fact]
    public void ConfigureNpmRejectsSameRegistryProjectNpmrcAuthThroughPhase14()
    {
        const string ProjectSecret = "project-secret-value";
        string stateDirectory = CreateTestDirectory();
        string projectDirectory = Path.Combine(stateDirectory, "project");
        string projectNpmrcPath = Path.Combine(projectDirectory, ".npmrc");
        string userNpmrcPath = Path.Combine(stateDirectory, "npm", "user.npmrc");
        string projectNpmrc =
            "//pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
            + ":_authToken="
            + ProjectSecret
            + "\n";
        try
        {
            CreateOwnerOnlyDirectory(projectDirectory);
            WriteOwnerOnlyText(projectNpmrcPath, projectNpmrc);

            CommandResult result = InvokeWithRuntimeFromDirectory(
                projectDirectory,
                CreateConfigurationRuntime(stateDirectory),
                "configure",
                "npm",
                "--registry-url",
                TestRegistryUrl
            );

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Contains("Project-local npm authentication", result.StdErr);
            Assert.Contains("would shadow", result.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain(ProjectSecret, result.StdErr, StringComparison.Ordinal);
            Assert.Equal(projectNpmrc, File.ReadAllText(projectNpmrcPath));
            Assert.False(File.Exists(userNpmrcPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfigureYarnRejectsSameRegistryProjectYarnrcAuthThroughPhase14()
    {
        const string ProjectSecret = "project-secret-value";
        string stateDirectory = CreateTestDirectory();
        string projectDirectory = Path.Combine(stateDirectory, "project");
        string projectYarnrcPath = Path.Combine(projectDirectory, ".yarnrc.yml");
        string userYarnrcPath = Path.Combine(stateDirectory, "yarn", ".yarnrc.yml");
        string projectYarnrc =
            $"""
            npmRegistries:
              {TestRegistryUrl}:
                npmAuthToken: {ProjectSecret}
            """;
        try
        {
            CreateOwnerOnlyDirectory(projectDirectory);
            WriteOwnerOnlyText(projectYarnrcPath, projectYarnrc);

            CommandResult result = InvokeWithRuntimeFromDirectory(
                projectDirectory,
                CreateConfigurationRuntime(stateDirectory),
                "configure",
                "yarn",
                "--registry-url",
                TestRegistryUrl
            );

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Contains("Project-local Yarn selector", result.StdErr);
            Assert.Contains("npmAuthToken", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("would shadow", result.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain(ProjectSecret, result.StdErr, StringComparison.Ordinal);
            Assert.Equal(projectYarnrc, File.ReadAllText(projectYarnrcPath));
            Assert.False(File.Exists(userYarnrcPath));
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
                    ? Path.Combine(home, ".yarnrc.yml")
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
        string stateDirectory = CreateTestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        CliRuntimeOptions runtimeOptions = CreateConfigurationRuntime(stateDirectory) with
        {
            CancellationToken = cancellation.Token,
        };

        try
        {
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
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("configure", false)]
    [InlineData("configure", true)]
    [InlineData("unconfigure", false)]
    [InlineData("unconfigure", true)]
    [InlineData("doctor", false)]
    public void GitCliOperationsPropagateRuntimeCancellation(string command, bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory) with
        {
            CancellationToken = cancellation.Token,
        };
        string[] args =
            command == "doctor"
                ? ["doctor"]
                : dryRun
                    ? [command, "git", "--dry-run"]
                    : [command, "git"];

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, args);

            Assert.Equal(130, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Equal("error: operation canceled.\n", result.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("configure", false)]
    [InlineData("configure", true)]
    [InlineData("unconfigure", false)]
    [InlineData("unconfigure", true)]
    public void NuGetCliOperationsPropagateRuntimeCancellation(string command, bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        CliRuntimeOptions runtimeOptions = CreateGitPhase8RuntimeOptions(stateDirectory) with
        {
            CancellationToken = cancellation.Token,
        };
        string[] args = dryRun
            ? [command, "nuget", "--dry-run"]
            : [command, "nuget"];

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, args);

            Assert.Equal(130, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Equal("error: operation canceled.\n", result.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void DoctorPropagatesCancellationDuringProductionHealthProbe()
    {
        string stateDirectory = CreateTestDirectory();
        using var cancellation = new CancellationTokenSource();
        var nuGetFileSystem = new EmptyNuGetDryRunFileSystem();
        var acquisition = new CancelingCredentialAcquisitionService(
            cancellation,
            cancelOnInvocation: 2
        );
        CliRuntimeOptions baseOptions = CreateGitPhase8RuntimeOptions(stateDirectory);
        CliRuntimeOptions runtimeOptions = baseOptions with
        {
            CancellationToken = cancellation.Token,
            CompositionRoot =
                CredentialProviderCompositionRoot.CreateExplicitTestScaffold(acquisition),
            NuGetPhase10Options = baseOptions.NuGetPhase10Options! with
            {
                FileSystem = nuGetFileSystem,
            },
        };

        try
        {
            CommandResult result = InvokeWithRuntime(runtimeOptions, "doctor");

            Assert.True(acquisition.RuntimeCancellationObserved);
            Assert.Equal(2, acquisition.InvocationCount);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(0, nuGetFileSystem.FileExistsInvocationCount);
            Assert.Equal(130, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Equal("error: operation canceled.\n", result.StdErr);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
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
            Assert.Equal(string.Empty, executed.StdErr);

            string[] executedLines = Normalize(executed.StdOut)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string ciModeLine = Assert.Single(
                executedLines,
                static line => line.StartsWith("ci-mode: ", StringComparison.Ordinal)
            );
            string scopeLine = Assert.Single(
                executedLines,
                static line => line.StartsWith("scope: ", StringComparison.Ordinal)
            );
            Assert.Equal("ci-mode: azure-pipelines", ciModeLine);
            Assert.Equal("scope: ci-temporary", scopeLine);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YarnRepositoryTransitionCleanupPreservesUnrelatedYaml(bool logout)
    {
        const string UnrelatedYaml =
            "nodeLinker: node-modules\n"
            + "npmRegistries:\n"
            + "  'https://registry.example.test/':\n"
            + "    npmAlwaysAuth: true\n"
            + "    npmAuthToken: preserved-marker\n";
        string stateDirectory = CreateTestDirectory();
        string homeDirectory = Path.Combine(stateDirectory, "yarn");
        string yarnrcPath = Path.Combine(homeDirectory, ".yarnrc.yml");
        string manifestPath = Path.Combine(
            stateDirectory,
            "manifests",
            "yarn-user-ownership-manifest.json"
        );
        try
        {
            Directory.CreateDirectory(homeDirectory);
            File.WriteAllText(yarnrcPath, UnrelatedYaml);
            CliRuntimeOptions runtimeOptions = CreateConfigurationRuntime(stateDirectory);
            CommandResult configured = InvokeWithRuntime(
                runtimeOptions,
                "configure",
                "yarn",
                "--registry-url",
                TestRegistryUrl
            );
            Assert.Equal(0, configured.ExitCode);
            Assert.True(File.Exists(manifestPath));
            Assert.Contains(
                "pkgs.dev.azure.com",
                File.ReadAllText(yarnrcPath),
                StringComparison.Ordinal
            );
            Directory.CreateDirectory(Path.Combine(homeDirectory, ".git"));

            CommandResult doctor = InvokeWithRuntime(runtimeOptions, "doctor");
            Assert.NotEqual(0, doctor.ExitCode);
            Assert.Contains("yarn-user-configuration-plan: fail\n", doctor.StdOut);

            CommandResult cleanup = logout
                ? InvokeWithRuntime(runtimeOptions, "logout")
                : InvokeWithRuntime(runtimeOptions, "unconfigure", "yarn");

            Assert.Equal(0, cleanup.ExitCode);
            Assert.Equal(UnrelatedYaml, File.ReadAllText(yarnrcPath));
            Assert.False(File.Exists(manifestPath));
            Assert.DoesNotContain("fake-token-", cleanup.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-token-", cleanup.StdErr, StringComparison.Ordinal);
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
                      yarn (Yarn 4+ only)

                    Options:
                    """
                    + "\n"
                    + "  --dry-run                    Render planned actions without "
                    + "mutating files.\n"
                    + "  --ci <mode>                  Select CI mode explicitly: "
                    + "none | azure-pipelines.\n"
                    + "  --registry-url <url>         Required Azure Artifacts npm URL for "
                    + "npm, pnpm, and Yarn 4+.\n"
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
                      yarn (Yarn 4+ only)

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
                      yarn (Yarn 4+ only)

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
                    + "--ci azure-pipelines [--help]\n"
                    + """

                    Ecosystems:
                      npm
                      pnpm
                      yarn (Yarn 4+ only)

                    Status:
                      Clean product-owned CI temporary package configuration.
                      User-level integration removal stays under unconfigure <ecosystem>.

                    Options:
                      --dry-run                    Render cleanup actions without mutating files.
                      --ci azure-pipelines         Required; clean Azure Pipelines temporary state.
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

    private static string GetExpectedNuGetConfigureOutput()
    {
        return Normalize(
            """
            command: configure
            ecosystem: nuget
            phase: 15-end-to-end-hardening
            ci-mode: none
            scope: user
            mutates-state: yes
            plan-operation: apply
            applied-change-count: 1
            nuget-plugin-layout-marker: present
            ownership-manifest: present
            note: credential material is not printed
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
        bool? devAzureUseHttpPathPresent = null,
        bool? productHelperActive = null
    )
    {
        bool useHttpPathPresent = devAzureUseHttpPathPresent ?? ownedGitEntriesPresent;
        bool localShellSuccess = localShellHelperShorthandSuccess ?? ownedGitEntriesPresent;
        bool effectiveProductHelperActive = productHelperActive ?? ownedGitEntriesPresent;
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
                    "azureauth-version-probe: not-required",
                    "azureauth-version-probe-code: AzureAuthVersionProbeNotRequired",
                    $"configuration-plan: {(configurationPlanValid ? "pass" : "fail")}",
                    $"owned-git-entries: {(ownedGitEntriesPresent ? "present" : "absent")}",
                    $"ownership-manifest: {(ownershipManifestPresent ? "present" : "absent")}",
                    "dev.azure.com-useHttpPath: " + (useHttpPathPresent ? "present" : "absent"),
                    "dev.azure.com-useHttpPath-truncated: no",
                    "credential-core: pass",
                    "git-credential-helper-get: pass",
                    "git-credential-helper-store: pass",
                    "git-credential-helper-erase: pass",
                    "local-shell-helper-shorthand: " + (localShellSuccess ? "pass" : "fail"),
                    "git-effective-credential-helper: "
                        + (effectiveProductHelperActive ? "active" : "not-configured"),
                    "git-effective-credential-helper-order: "
                        + (effectiveProductHelperActive ? "product" : "none"),
                    "git-effective-credential-helper-truncated: no",
                    "git-effective-credential-helper-conflict: none",
                    "git-effective-credential-helper-remediation: "
                        + (
                            effectiveProductHelperActive
                                ? "none"
                                : "run configure git to add the product-managed helper"
                        ),
                    "protocol-payload: captured-not-printed",
                    "auth-accepted-identity-flows: browser, azure-pipelines",
                    "auth-unavailable-identity-flows: device-code",
                    "auth-deferred-identity-flows: "
                        + "pat-compatibility, service-principal, managed-identity, "
                        + "workload-identity",
                    "auth-pat-compatibility: deferred-disabled",
                    "auth-persistent-derived-credentials: disabled",
                    "auth-product-plaintext-fallback: disabled",
                    "nuget-configuration-plan: pass",
                    "nuget-plugin-layout-marker: absent",
                    "nuget-ownership-manifest: absent",
                    "nuget-netcore-plugin-entrypoint: fail",
                    "nuget-plugin-mode-entrypoint: fail",
                    "nuget-azure-artifacts-source: pass",
                    "nuget-interactive-policy: pass",
                    "nuget-environment-overrides: absent",
                    "python-keyring-shim-exists: "
                        + (OperatingSystem.IsWindows() ? "N/A" : "fail"),
                    "python-keyring-shim-first-on-path: "
                        + (OperatingSystem.IsWindows() ? "N/A" : "fail"),
                    "python-interpreter: not-found",
                    "python-keyring-module: fail",
                    "python-keyring-module-probe: interpreter-not-found",
                    "python-azureauth-keyring-backend: N/A",
                    "python-azureauth-keyring-backend-probe: N/A",
                    "python-azureauth-keyring-helper: N/A",
                    "python-azureauth-keyring-helper-expected: N/A",
                    "python-azureauth-keyring-helper-resolved: N/A",
                    "python-azure-artifacts-endpoint-canonicalization: pass",
                    "npm-workspace-resolution-status: NotRequired",
                    "npm-workspace-npmrc: absent",
                    "npm-effective-user-npmrc: absent",
                    "npm-userconfig-environment-override: absent",
                    "npm-registry-declaration: absent",
                    "npm-azure-artifacts-endpoint-canonicalization: pass",
                    "npm-user-credential-plan: not-applicable",
                    "pnpm-user-credential-plan: not-applicable",
                    "npm-ci-temporary-credential-plan: not-applicable",
                    "yarn-workspace-yarnrc: absent",
                    "yarn-effective-user-yarnrc: absent",
                    "yarn-rc-filename-override: absent",
                    "yarn-registry-declaration: absent",
                    "yarn-forbidden-auth-ident-conflict: absent",
                    "yarn-azure-artifacts-endpoint-canonicalization: pass",
                    "yarn-writes: pass",
                    "yarn-write-gate-status: "
                        + "phase-1.4-accepted; writes-supported-by-phase-13b",
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
                    "doctor-aggregation: fail",
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

    private static CommandResult InvokeWithRuntimeFromDirectory(
        string workingDirectory,
        CliRuntimeOptions runtimeOptions,
        params string[] args
    )
    {
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = workingDirectory;
            return InvokeWithRuntime(runtimeOptions, args);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
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
                GitConfigurationProbeWorkingDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            },
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
            NpmPhase12Options = CreateIsolatedNpmPhase12Options(stateDirectory),
            YarnPhase13Options = CreateIsolatedYarnPhase13Options(stateDirectory),
            PythonPhase11Options = new PythonPhase11VerticalSliceOptions
            {
                FileSystem = new SystemFileSystem(),
                ProcessRunner = new RecordingPythonResolutionProcessRunner(),
                EnvironmentVariableReader = _ => null,
                ExpectedKeyringShimPath = Path.Combine(
                    stateDirectory,
                    "missing-keyring"
                ),
                CurrentDirectoryPath = stateDirectory,
            },
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
            EnvironmentVariableReader = name =>
                ReadConfigurationPhase14Environment(stateDirectoryPath, name),
            ProductExecutablePath = GetTestProductExecutablePath(),
            RegistryUrls = CreateTestRegistryUrls(),
        };

    private static string? ReadConfigurationPhase14Environment(
        string testRootPath,
        string name
    ) =>
        string.Equals(name, "LOCALAPPDATA", StringComparison.Ordinal)
            ? Path.Combine(testRootPath, "local-app-data")
            : null;

    private static string GetTestProductExecutablePath() =>
        OperatingSystem.IsWindows()
            ? @"C:\Program Files\AzureAuth\CredProvider\azureauth-credprovider.exe"
            : "/opt/azureauth-credprovider/azureauth-credprovider";

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

    private static NpmPhase12VerticalSliceOptions CreateIsolatedNpmPhase12Options(
        string rootPath
    ) =>
        new()
        {
            WorkspaceDirectoryPath = rootPath,
            UserHomeDirectoryPath = rootPath,
            UserNpmrcPath = Path.Combine(rootPath, "user.npmrc"),
            CiTemporaryNpmrcPath = Path.Combine(rootPath, "ci", ".npmrc"),
            EnvironmentVariableReader = _ => null,
        };

    private static YarnPhase13VerticalSliceOptions CreateIsolatedYarnPhase13Options(
        string rootPath
    ) =>
        new()
        {
            WorkspaceDirectoryPath = rootPath,
            UserHomeDirectoryPath = rootPath,
            UserYarnrcPath = Path.Combine(rootPath, "user.yarnrc.yml"),
            EnvironmentVariableReader = _ => null,
        };

    private static GitPhase8VerticalSliceService CreateGitPhase8Service(string stateDirectory)
    {
        return new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                GitConfigurationProbeWorkingDirectoryPath = stateDirectory,
                ProcessRunner = new PassingGitDiscoveryProcessRunner(),
                LocalShellGitDiscoverySupported = true,
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

    private sealed class CapturingCancellationTokenAcquisitionService(
        CancellationTokenSource cancellation
    )
        : ICredentialAcquisitionService
    {
        public CancellationToken? CancellationToken { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            CancellationToken = cancellationToken;
            cancellation.Cancel();
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "fake-secret-token-capture",
                    DiagnosticsCorrelationId = "git-cli-token-capture",
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

    private static Dictionary<string, string?> CreateIsolatedGitEnvironment(
        IReadOnlyDictionary<string, string?> explicitEnvironment,
        IEnumerable<string>? inheritedGitConfigVariables = null
    )
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG"] = null,
            ["GIT_CONFIG_COUNT"] = null,
            ["GIT_CONFIG_GLOBAL"] = null,
            ["GIT_CONFIG_PARAMETERS"] = null,
            ["GIT_CONFIG_SYSTEM"] = null,
        };
        IEnumerable<string> inheritedVariables =
            inheritedGitConfigVariables
            ?? Environment
                .GetEnvironmentVariables()
                .Keys
                .OfType<string>();
        foreach (string variable in inheritedVariables)
        {
            if (variable.StartsWith("GIT_CONFIG_", StringComparison.Ordinal))
            {
                environment[variable] = null;
            }
        }
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach ((string key, string? value) in explicitEnvironment)
        {
            if (
                !string.Equals(key, "GIT_CONFIG_COUNT", StringComparison.Ordinal)
                && !key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal)
                && !key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal)
            )
            {
                environment[key] = value;
            }
        }

        var entries = new List<(string Key, string Value)>();
        if (
            explicitEnvironment.TryGetValue(
                "GIT_CONFIG_COUNT",
                out string? countValue
            )
            && int.TryParse(
                countValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int count
            )
        )
        {
            for (var index = 0; index < count; index++)
            {
                if (
                    explicitEnvironment.TryGetValue(
                        "GIT_CONFIG_KEY_" + index,
                        out string? key
                    )
                    && key is not null
                    && explicitEnvironment.TryGetValue(
                        "GIT_CONFIG_VALUE_" + index,
                        out string? value
                    )
                    && value is not null
                )
                {
                    entries.Add((key, value));
                }
            }
            environment["GIT_CONFIG_COUNT"] = entries.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            for (var index = 0; index < entries.Count; index++)
            {
                environment["GIT_CONFIG_KEY_" + index] = entries[index].Key;
                environment["GIT_CONFIG_VALUE_" + index] = entries[index].Value;
            }
        }
        return environment;
    }

    private sealed class PassingGitDiscoveryProcessRunner(
        IEnumerable<string>? inheritedGitConfigVariables = null
    ) : IProcessRunner
    {
        public List<ProcessStartSpec> EffectiveGitStartSpecs { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();

            if (
                startSpec.Arguments.Contains("config")
                || startSpec.Arguments.Contains("credential")
            )
            {
                var effectiveStartSpec = new ProcessStartSpec(
                    "git",
                    startSpec.Arguments,
                    startSpec.WorkingDirectory,
                    CreateIsolatedGitEnvironment(
                        startSpec.Environment,
                        inheritedGitConfigVariables
                    ),
                    startSpec.StandardInput,
                    startSpec.Timeout,
                    startSpec.OutputCaptureOptions
                );
                EffectiveGitStartSpecs.Add(effectiveStartSpec);
                return new SystemProcessRunner().RunAsync(
                    effectiveStartSpec,
                    cancellationToken
                );
            }

            TryWriteHelperMarker(startSpec);
            return Task.FromResult(
                new ProcessResult(
                    0,
                    "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                        + "username=azureauth-use-http-path-present\n"
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
        public int FileExistsInvocationCount { get; private set; }

        public bool FileExists(string path)
        {
            FileExistsInvocationCount++;
            return false;
        }

        public sealed class RecordingNuGetConfigurationFileSystem : IFileSystem
        {
            private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
            private readonly HashSet<string> directories = new(StringComparer.Ordinal);

            public bool FileExists(string path) => files.ContainsKey(GetFullPath(path));

            public bool IsExecutableFile(string path) => FileExists(path);

            public bool DirectoryExists(string path)
            {
                string fullPath = GetFullPath(path);
                return directories.Contains(fullPath)
                    || files.Keys.Any(file => IsDescendant(file, fullPath));
            }

            public string GetFullPath(string path) => Path.GetFullPath(path);

            public bool IsPathFullyQualified(string path) => Path.IsPathFullyQualified(path);

            public string ReadAllText(string path, Encoding? encoding = null) =>
                (encoding ?? Encoding.UTF8).GetString(ReadAllBytes(path));

            public byte[] ReadAllBytes(string path) =>
                files.TryGetValue(GetFullPath(path), out byte[]? contents)
                    ? contents.ToArray()
                    : throw new FileNotFoundException("The file does not exist.", path);

            public long GetFileLength(string path) => ReadAllBytes(path).LongLength;

            public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
                AtomicWriteAllBytes(path, (encoding ?? Encoding.UTF8).GetBytes(contents));

            public void AtomicWriteAllText(
                string path,
                string contents,
                Encoding? encoding = null,
                AtomicWriteOptions options = AtomicWriteOptions.None
            ) => AtomicWriteAllBytes(path, (encoding ?? Encoding.UTF8).GetBytes(contents), options);

            public void AtomicWriteAllBytes(
                string path,
                byte[] contents,
                AtomicWriteOptions options = AtomicWriteOptions.None
            )
            {
                string fullPath = GetFullPath(path);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    CreateDirectory(directory);
                }

                files[fullPath] = contents.ToArray();
            }

            public UnixFileMode GetUnixFileMode(string path) =>
                FileExists(path)
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                    : throw new FileNotFoundException("The file does not exist.", path);

            public void SetUnixFileMode(string path, UnixFileMode mode)
            {
                _ = GetUnixFileMode(path);
            }

            public void CreateDirectory(string path)
            {
                string? current = GetFullPath(path);
                while (!string.IsNullOrEmpty(current) && directories.Add(current))
                {
                    current = Path.GetDirectoryName(current);
                }
            }

            public void DeleteFile(string path) => files.Remove(GetFullPath(path));

            public void DeleteDirectory(string path, bool recursive = false)
            {
                string fullPath = GetFullPath(path);
                if (
                    !recursive
                    && (
                        files.Keys.Any(file => IsDescendant(file, fullPath))
                        || directories.Any(directory => IsDescendant(directory, fullPath))
                    )
                )
                {
                    throw new IOException("The directory is not empty.");
                }

                foreach (
                    string file in files.Keys
                        .Where(file => IsDescendant(file, fullPath))
                        .ToArray()
                )
                {
                    files.Remove(file);
                }
                directories.RemoveWhere(directory =>
                    string.Equals(directory, fullPath, StringComparison.Ordinal)
                    || IsDescendant(directory, fullPath)
                );
            }

            public IEnumerable<string> EnumerateFiles(
                string path,
                string searchPattern = "*",
                SearchOption searchOption = SearchOption.TopDirectoryOnly
            )
            {
                string fullPath = GetFullPath(path);
                return files.Keys.Where(file =>
                    MatchesDirectory(file, fullPath, searchOption)
                    && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                        searchPattern,
                        Path.GetFileName(file),
                        ignoreCase: false
                    )
                );
            }

            public IEnumerable<string> EnumerateDirectories(
                string path,
                string searchPattern = "*",
                SearchOption searchOption = SearchOption.TopDirectoryOnly
            )
            {
                string fullPath = GetFullPath(path);
                return directories.Where(directory =>
                    !string.Equals(directory, fullPath, StringComparison.Ordinal)
                    && MatchesDirectory(directory, fullPath, searchOption)
                    && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                        searchPattern,
                        Path.GetFileName(directory),
                        ignoreCase: false
                    )
                );
            }

            private static bool MatchesDirectory(
                string candidate,
                string parent,
                SearchOption searchOption
            ) =>
                searchOption == SearchOption.AllDirectories
                    ? IsDescendant(candidate, parent)
                    : string.Equals(
                        Path.GetDirectoryName(candidate),
                        parent,
                        StringComparison.Ordinal
                    );

            private static bool IsDescendant(string candidate, string parent) =>
                candidate.StartsWith(
                    Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                );
        }

        public bool IsExecutableFile(string path) => false;

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

    private sealed class CancelingCredentialAcquisitionService(
        CancellationTokenSource cancellation,
        int cancelOnInvocation
    ) : ICredentialAcquisitionService
    {
        public int InvocationCount { get; private set; }

        public bool RuntimeCancellationObserved { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            InvocationCount++;
            if (cancellationToken == cancellation.Token)
            {
                RuntimeCancellationObserved = true;
                if (InvocationCount == cancelOnInvocation)
                {
                    cancellation.Cancel();
                }
            }

            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "fake-secret-doctor-cancellation",
                    DiagnosticsCorrelationId = "nuget-doctor-runtime-cancellation",
                }
            );
        }
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

    [Fact]
    public async Task LoginDeviceCodeLaunchesOnceWithoutVersionPreflightAndStreamsPromptSafely()
    {
        const string Token = "phase2-cli-private-token";
        string rootPath = CreateTestDirectory();
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        var runner = new CoordinatedDeviceCodeProcessRunner(
            new ProcessResult(0, Token, string.Empty)
        );
        try
        {
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                CreatePhase2Installation(AzureAuthHostPlatform.NativeLinux),
                runner,
                stderr
            );
            var runtime = new CliRuntimeOptions { CompositionRoot = root };

            Task<int> run = Task.Run(() =>
                CliApplication.Run(["login", "--device-code"], stdout, stderr, runtime)
            );
            await runner.PromptWritten.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            );

            Assert.False(run.IsCompleted);
            Assert.Equal(CoordinatedDeviceCodeProcessRunner.Prompt, stderr.ToString());
            Assert.DoesNotContain(Token, stderr.ToString(), StringComparison.Ordinal);

            runner.Release();
            int exitCode = await run.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(0, exitCode);
            Assert.Equal(
                Normalize(
                    """
                    command: login
                    phase: 15-end-to-end-hardening
                    ci-mode: none
                    identity-flow: device-code
                    status: success
                    account: unselected
                    tenant: tenant-cli
                    credential-material: issued-not-printed
                    persistent-derived-credentials: disabled
                    product-plaintext-fallback: disabled
                    """
                ),
                stdout.ToString()
            );
            Assert.Equal(CoordinatedDeviceCodeProcessRunner.Prompt, stderr.ToString());
            Assert.DoesNotContain(Token, stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Token, stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal(1, runner.InvocationCount);
            ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
            Assert.Same(stderr, start.StandardErrorTee);
            Assert.DoesNotContain("--version", start.Arguments);
            Assert.Equal(
                [
                    "aad",
                    "--client",
                    "872cd9fa-d31f-45e0-9eab-6e460a02d1f1",
                    "--tenant",
                    "tenant-cli",
                    "--scope",
                    "499b84ac-1321-427f-aa17-267ca6975798/.default",
                    "--mode",
                    "devicecode",
                    "--domain",
                    "example.com",
                    "--output",
                    "token",
                ],
                start.Arguments
            );
            Assert.Equal(8192, start.OutputCaptureOptions.StandardOutputByteLimit);
            Assert.Equal(8192, start.OutputCaptureOptions.StandardErrorByteLimit);
            Assert.Equal(3, start.Environment.Count);
            Assert.All(start.Environment.Values, Assert.Null);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Theory]
    [InlineData(AzureAuthHostPlatform.Windows)]
    [InlineData(AzureAuthHostPlatform.Wsl)]
    public void LoginDeviceCodeRejectsWindowsAndWslBeforeLaunch(AzureAuthHostPlatform hostPlatform)
    {
        const string Token = "must-not-launch-private-token";
        string rootPath = CreateTestDirectory();
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        var runner = new PromptingResultProcessRunner(
            new ProcessResult(0, Token, "must-not-stream")
        );
        try
        {
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                CreatePhase2Installation(hostPlatform),
                runner,
                stderr
            );

            int exitCode = CliApplication.Run(
                ["login", "--device-code"],
                stdout,
                stderr,
                new CliRuntimeOptions { CompositionRoot = root }
            );

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stdout.ToString());
            Assert.Equal(
                "error: AzureAuth device-code login requires an explicit interactive "
                    + "native Linux request.\n",
                stderr.ToString()
            );
            Assert.Equal(0, runner.InvocationCount);
            Assert.Null(runner.StartSpec);
            Assert.DoesNotContain(Token, stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("must-not-stream", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public async Task LoginDeviceCodeCancellationReturns130WithoutTokenOrDiagnosticLeak()
    {
        const string Token = "canceled-private-token";
        string rootPath = CreateTestDirectory();
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        var runner = new CoordinatedDeviceCodeProcessRunner(
            new ProcessResult(0, Token, "canceled-diagnostic-secret")
        );
        using var cancellation = new CancellationTokenSource();
        try
        {
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                CreatePhase2Installation(AzureAuthHostPlatform.NativeLinux),
                runner,
                stderr
            );
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = root,
                CancellationToken = cancellation.Token,
            };

            Task<int> run = Task.Run(() =>
                CliApplication.Run(["login", "--device-code"], stdout, stderr, runtime)
            );
            await runner.PromptWritten.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            );

            cancellation.Cancel();
            int exitCode = await run.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(130, exitCode);
            Assert.Equal(string.Empty, stdout.ToString());
            Assert.Equal(
                CoordinatedDeviceCodeProcessRunner.Prompt + "error: operation canceled.\n",
                stderr.ToString()
            );
            Assert.Equal(1, runner.InvocationCount);
            Assert.DoesNotContain(Token, stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(
                "canceled-diagnostic-secret",
                stderr.ToString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Theory]
    [InlineData("nonzero", "AzureAuth did not return a token.", true)]
    [InlineData("timeout", "AzureAuth token acquisition timed out.", true)]
    [InlineData("launch-failure", "AzureAuth process launch failed.", false)]
    [InlineData(
        "stdout-too-large",
        "AzureAuth process output exceeded the configured limit.",
        true
    )]
    [InlineData(
        "stderr-too-large",
        "AzureAuth process output exceeded the configured limit.",
        true
    )]
    [InlineData("invalid-utf8", "AzureAuth process output was invalid.", true)]
    public void LoginDeviceCodeFailureUsesSafeDiagnosticAndNeverPrintsToken(
        string failureCase,
        string expectedSafeMessage,
        bool streamPrompt
    )
    {
        const string Token = "failure-private-token";
        const string DiagnosticSecret = "arbitrary-azureauth-diagnostic-secret";
        ProcessResult processResult = failureCase switch
        {
            "nonzero" => new ProcessResult(19, string.Empty, DiagnosticSecret),
            "timeout" => ProcessResult.TimedOut(string.Empty, DiagnosticSecret),
            "launch-failure" => ProcessResult.LaunchFailure(string.Empty, DiagnosticSecret),
            "stdout-too-large" => ProcessResult.OutputTooLarge(Token, string.Empty),
            "stderr-too-large" => ProcessResult.OutputTooLarge(string.Empty, DiagnosticSecret),
            "invalid-utf8" => ProcessResult.InvalidOutput(string.Empty, DiagnosticSecret),
            _ => throw new ArgumentOutOfRangeException(nameof(failureCase)),
        };
        string rootPath = CreateTestDirectory();
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        var runner = new PromptingResultProcessRunner(processResult, streamPrompt);
        try
        {
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                CreatePhase2Installation(AzureAuthHostPlatform.NativeLinux),
                runner,
                stderr
            );

            int exitCode = CliApplication.Run(
                ["login", "--device-code"],
                stdout,
                stderr,
                new CliRuntimeOptions { CompositionRoot = root }
            );

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stdout.ToString());
            Assert.Equal(
                (streamPrompt ? PromptingResultProcessRunner.Prompt : string.Empty)
                    + "error: "
                    + expectedSafeMessage
                    + "\n",
                stderr.ToString()
            );
            Assert.Equal(1, runner.InvocationCount);
            Assert.DoesNotContain(Token, stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(DiagnosticSecret, stderr.ToString(), StringComparison.Ordinal);
            ProcessStartSpec start = Assert.IsType<ProcessStartSpec>(runner.StartSpec);
            Assert.Same(stderr, start.StandardErrorTee);
            Assert.Equal(8192, start.OutputCaptureOptions.StandardOutputByteLimit);
            Assert.Equal(8192, start.OutputCaptureOptions.StandardErrorByteLimit);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void StatusReportsDeviceCodeAcceptedOnlyForNativeLinuxWithPromptWriter()
    {
        string rootPath = CreateTestDirectory();
        var promptWriter = new StringWriter();
        var runner = new PromptingResultProcessRunner(
            new ProcessResult(0, "unused-private-token", string.Empty)
        );
        try
        {
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                CreatePhase2Installation(AzureAuthHostPlatform.NativeLinux),
                runner,
                promptWriter
            );
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = root,
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(rootPath),
            };

            CommandResult result = InvokeWithRuntime(runtime, "status");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                "accepted-identity-flows: browser, device-code, azure-pipelines\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "unavailable-identity-flows: none\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "interactive-readiness: interactive-ready\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "silent-readiness: silent-ready\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, result.StdErr);
            Assert.Equal(0, runner.InvocationCount);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Theory]
    [InlineData(AzureAuthHostPlatform.Windows, true, true)]
    [InlineData(AzureAuthHostPlatform.Wsl, true, true)]
    [InlineData(AzureAuthHostPlatform.NativeLinux, false, true)]
    [InlineData(AzureAuthHostPlatform.NativeLinux, true, false)]
    public void StatusReportsDeviceCodeUnavailableWhenUnsupported(
        AzureAuthHostPlatform hostPlatform,
        bool hasPromptWriter,
        bool installationReady
    )
    {
        string rootPath = CreateTestDirectory();
        TextWriter? promptWriter = hasPromptWriter ? new StringWriter() : null;
        var runner = new PromptingResultProcessRunner(
            new ProcessResult(0, "unused-private-token", string.Empty),
            streamPrompt: false
        );
        try
        {
            AzureAuthInstallation installation = installationReady
                ? CreatePhase2Installation(hostPlatform)
                : AzureAuthInstallation.Failure(
                    AzureAuthInstallationStatus.Missing,
                    "AzureAuthInstallationMissing",
                    "AzureAuth installation is unavailable."
                );
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                installation,
                runner,
                promptWriter
            );
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = root,
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(rootPath),
            };

            CommandResult result = InvokeWithRuntime(runtime, "status");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                "accepted-identity-flows: browser, azure-pipelines\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "unavailable-identity-flows: device-code\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "browser, device-code, azure-pipelines",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, result.StdErr);
            Assert.Equal(0, runner.InvocationCount);
            if (!installationReady)
            {
                Assert.Contains(
                    "interactive-readiness: interactive-unavailable\n",
                    result.StdOut,
                    StringComparison.Ordinal
                );
            }
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Theory]
    [InlineData(AzureAuthHostPlatform.NativeLinux, true, true, true)]
    [InlineData(AzureAuthHostPlatform.Windows, true, true, false)]
    [InlineData(AzureAuthHostPlatform.Wsl, true, true, false)]
    [InlineData(AzureAuthHostPlatform.NativeLinux, false, true, false)]
    [InlineData(AzureAuthHostPlatform.NativeLinux, true, false, false)]
    public void DoctorReportsDeviceCodeAcceptedOnlyWhenReady(
        AzureAuthHostPlatform hostPlatform,
        bool hasPromptWriter,
        bool installationReady,
        bool expectedDeviceCodeReady
    )
    {
        string rootPath = CreateTestDirectory();
        TextWriter? promptWriter = hasPromptWriter ? new StringWriter() : null;
        var runner = new PromptingResultProcessRunner(
            new ProcessResult(0, "unused-private-token", string.Empty),
            streamPrompt: false
        );
        try
        {
            AzureAuthInstallation installation = installationReady
                ? CreatePhase2Installation(hostPlatform)
                : AzureAuthInstallation.Failure(
                    AzureAuthInstallationStatus.Missing,
                    "AzureAuthInstallationMissing",
                    "AzureAuth installation is unavailable."
                );
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                installation,
                runner,
                promptWriter
            );
            CliRuntimeOptions runtime = CreateGitPhase8RuntimeOptions(rootPath) with
            {
                CompositionRoot = root,
            };

            CommandResult result = InvokeWithRuntime(runtime, "doctor");

            Assert.Contains("command: doctor\n", result.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "composition-mode: Production\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                expectedDeviceCodeReady
                    ? "auth-accepted-identity-flows: browser, device-code, azure-pipelines\n"
                    : "auth-accepted-identity-flows: browser, azure-pipelines\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                expectedDeviceCodeReady
                    ? "auth-unavailable-identity-flows: none\n"
                    : "auth-unavailable-identity-flows: device-code\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, result.StdErr);
            Assert.All(runner.StartSpecs, start => Assert.Null(start.StandardErrorTee));
            Assert.Equal(string.Empty, promptWriter?.ToString() ?? string.Empty);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void RootHelpListsDeviceCodeLoginExample()
    {
        CommandResult result = Invoke();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "azureauth-credprovider login --device-code\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public void LoginHelpListsDeviceCodeLoginOption()
    {
        CommandResult result = Invoke("login", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "azureauth-credprovider login [--browser|--device-code|--pat <value>]\n",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "--device-code                Use device-code authentication.",
            result.StdOut,
            StringComparison.Ordinal
        );
        Assert.Equal(string.Empty, result.StdErr);
    }

    private static CredentialProviderCompositionRoot CreatePhase2ProductionRoot(
        string secureStoreRootPath,
        AzureAuthInstallation installation,
        IProcessRunner processRunner,
        TextWriter? promptWriter
    )
    {
        AzureAuthProviderConfig config = AzureAuthProviderConfig.CreateAzureAuth();
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "cli.user@example.com",
            "tenant-cli",
            DateTimeOffset.UtcNow
        );
        return CredentialProviderCompositionRoot.CreateProduction(
            new CredentialProviderProductionOptions
            {
                SecureStoreRootPath = secureStoreRootPath,
                ProviderConfig = config,
                Binding = binding,
                InstallationDiscovery = new StaticPhase2InstallationDiscovery(installation),
                ProcessRunner = processRunner,
                DeviceCodePromptWriter = promptWriter,
            }
        );
    }

    private static AzureAuthInstallation CreatePhase2Installation(
        AzureAuthHostPlatform hostPlatform
    ) =>
        AzureAuthInstallation.Available(
            hostPlatform == AzureAuthHostPlatform.Windows
                ? @"C:\Program Files\AzureAuth\azureauth.exe"
                : "/opt/azureauth/azureauth",
            hostPlatform == AzureAuthHostPlatform.Windows
                ? @"C:\Program Files\AzureAuth\azureauth.exe"
                : "/opt/azureauth/azureauth",
            "0.9.5",
            hostPlatform
        );

    private sealed class StaticPhase2InstallationDiscovery(AzureAuthInstallation installation)
        : IAzureAuthInstallationDiscovery
    {
        public AzureAuthInstallation Discover(
            AzureAuthProviderConfig config,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return installation;
        }
    }

    private sealed class PromptingResultProcessRunner(
        ProcessResult result,
        bool streamPrompt = true
    ) : IProcessRunner
    {
        public const string Prompt =
            "Open https://microsoft.com/devicelogin and enter CLI-CODE-1234.\n";

        public int InvocationCount { get; private set; }

        public ProcessStartSpec? StartSpec { get; private set; }

        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            StartSpec = startSpec;
            StartSpecs.Add(startSpec);
            if (streamPrompt)
            {
                TextWriter writer = Assert.IsAssignableFrom<TextWriter>(startSpec.StandardErrorTee);
                await writer.WriteAsync(Prompt);
                await writer.FlushAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class CoordinatedDeviceCodeProcessRunner(ProcessResult result) : IProcessRunner
    {
        public const string Prompt =
            "Open https://microsoft.com/devicelogin and enter CLI-CODE-1234.\n";

        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource<bool> PromptWritten { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InvocationCount { get; private set; }

        public ProcessStartSpec? StartSpec { get; private set; }

        public void Release() => release.TrySetResult(true);

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            StartSpec = startSpec;
            TextWriter writer = Assert.IsAssignableFrom<TextWriter>(startSpec.StandardErrorTee);
            await writer.WriteAsync(Prompt);
            await writer.FlushAsync(cancellationToken);
            PromptWritten.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class CapturingAcceptedDeviceCodeAcquisitionService
        : ICredentialAcquisitionService
    {
        public const string Password = "accepted-device-private-secret";

        public CredentialRequestV2? Request { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = Password,
                    Account = request.AccountHint ?? "unbound",
                    Tenant = request.TenantHint ?? "unbound",
                    DiagnosticsCorrelationId = "accepted-device-code-cli-test",
                }
            );
        }
    }

    [Theory]
    [InlineData(AzureAuthHostPlatform.NativeLinux, true, true, true)]
    [InlineData(AzureAuthHostPlatform.Windows, true, true, false)]
    [InlineData(AzureAuthHostPlatform.Wsl, true, true, false)]
    [InlineData(AzureAuthHostPlatform.NativeLinux, false, true, false)]
    [InlineData(AzureAuthHostPlatform.NativeLinux, true, false, false)]
    public void DoctorDeviceCodeReadinessRowsReturnExactAggregateExitCode(
        AzureAuthHostPlatform hostPlatform,
        bool hasPromptWriter,
        bool installationReady,
        bool expectedDeviceCodeReady
    )
    {
        string rootPath = CreateTestDirectory();
        TextWriter? promptWriter = hasPromptWriter ? new StringWriter() : null;
        var runner = new PromptingResultProcessRunner(
            new ProcessResult(0, "unused-private-token", string.Empty),
            streamPrompt: false
        );
        try
        {
            AzureAuthInstallation installation = installationReady
                ? CreatePhase2Installation(hostPlatform)
                : AzureAuthInstallation.Failure(
                    AzureAuthInstallationStatus.Missing,
                    "AzureAuthInstallationMissing",
                    "AzureAuth installation is unavailable."
                );
            CredentialProviderCompositionRoot root = CreatePhase2ProductionRoot(
                rootPath,
                installation,
                runner,
                promptWriter
            );
            CliRuntimeOptions runtime = CreateGitPhase8RuntimeOptions(rootPath) with
            {
                CompositionRoot = root,
            };

            CommandResult result = InvokeWithRuntime(runtime, "doctor");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                expectedDeviceCodeReady
                    ? "auth-accepted-identity-flows: browser, device-code, azure-pipelines\n"
                    : "auth-accepted-identity-flows: browser, azure-pipelines\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                expectedDeviceCodeReady
                    ? "auth-unavailable-identity-flows: none\n"
                    : "auth-unavailable-identity-flows: device-code\n",
                result.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, result.StdErr);
            Assert.All(runner.StartSpecs, start => Assert.Null(start.StandardErrorTee));
            Assert.Equal(string.Empty, promptWriter?.ToString() ?? string.Empty);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void DoctorFullyHealthyAggregateRunsOneHealthProbeWithoutSecretLeak()
    {
        const string PrivateToken = "doctor-private-auth-token";
        const string PrivateDiagnostic = "doctor-private-auth-diagnostic";
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        string rootPath = pythonFixture.RootPath;
        var promptWriter = new StringWriter();
        var authenticationRunner = new PromptingResultProcessRunner(
            new ProcessResult(0, PrivateToken, PrivateDiagnostic),
            streamPrompt: false
        );
        try
        {
            CredentialProviderCompositionRoot productionRoot = CreatePhase2ProductionRoot(
                rootPath,
                CreatePhase2Installation(AzureAuthHostPlatform.NativeLinux),
                authenticationRunner,
                promptWriter
            );
            System.Reflection.ConstructorInfo rootConstructor = Assert.Single(
                typeof(CredentialProviderCompositionRoot).GetConstructors(
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )
            );
            var acquisitionService = new HealthyDoctorAcquisitionService();
            CredentialProviderCompositionRoot root =
                Assert.IsType<CredentialProviderCompositionRoot>(
                    rootConstructor.Invoke([
                        CredentialProviderCompositionMode.Production,
                        productionRoot.ProviderConfig,
                        productionRoot.BindingRecord,
                        productionRoot.Installation,
                        AzureAuthProcessLaunchOptions.FromInstallation(
                            productionRoot.Installation
                        ),
                        acquisitionService,
                        productionRoot.Readiness,
                        productionRoot.ProductionOptions,
                    ])
                );
            var gitDiscoveryRunner = new HealthyDoctorGitDiscoveryProcessRunner();
            var gitOptions = new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = rootPath,
                GitConfigurationProbeWorkingDirectoryPath = rootPath,
                ProcessRunner = gitDiscoveryRunner,
                GitExecutablePath = "fake-git",
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(rootPath),
            };
            string expectedHelperCommand = new GitPhase8VerticalSliceService(gitOptions)
                .Paths
                .GitHelperPath;
            gitDiscoveryRunner.ExpectedHelperCommand = OperatingSystem.IsWindows()
                ? expectedHelperCommand.Replace('\\', '/')
                : expectedHelperCommand;
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = root,
                GitPhase8Options = gitOptions,
                NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
                NpmPhase12Options = CreateIsolatedNpmPhase12Options(rootPath),
                YarnPhase13Options = CreateIsolatedYarnPhase13Options(rootPath),
                PythonPhase11Options = pythonFixture.Options,
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(rootPath),
            };
            CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");

            CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

            Assert.Equal(0, configureGit.ExitCode);
            Assert.Equal(string.Empty, configureGit.StdErr);
            Assert.Equal(0, doctor.ExitCode);
            Assert.True(pythonFixture.PathWasRequested);
            Assert.Contains(
                "composition-mode: Production\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "interactive-readiness: interactive-ready\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "silent-readiness: silent-ready\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "azureauth-version-probe: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "azureauth-version-probe-code: AzureAuthVersionProbeSucceeded\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains("configuration-plan: pass\n", doctor.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "owned-git-entries: present\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "ownership-manifest: present\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "dev.azure.com-useHttpPath: present\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains("credential-core: pass\n", doctor.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "git-credential-helper-get: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "git-credential-helper-store: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "git-credential-helper-erase: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "local-shell-helper-shorthand: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "nuget-configuration-plan: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "nuget-azure-artifacts-source: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "nuget-interactive-policy: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "nuget-environment-overrides: absent\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "python-keyring-shim-exists: "
                    + (OperatingSystem.IsWindows() ? "N/A" : "pass")
                    + "\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "python-keyring-shim-first-on-path: "
                    + (OperatingSystem.IsWindows() ? "N/A" : "pass")
                    + "\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "python-keyring-module: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "python-keyring-module-probe: module-found\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "npm-azure-artifacts-endpoint-canonicalization: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "yarn-azure-artifacts-endpoint-canonicalization: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains("yarn-writes: pass\n", doctor.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "configuration-aggregation: pass\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains("doctor-aggregation: pass\n", doctor.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                "auth-accepted-identity-flows: browser, device-code, azure-pipelines\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "auth-unavailable-identity-flows: none\n",
                doctor.StdOut,
                StringComparison.Ordinal
            );
            Assert.Equal(string.Empty, doctor.StdErr);
            Assert.Equal(1, authenticationRunner.InvocationCount);
            ProcessStartSpec healthProbe = Assert.Single(authenticationRunner.StartSpecs);
            Assert.Equal("/opt/azureauth/azureauth", healthProbe.FileName);
            Assert.Equal(["--version"], healthProbe.Arguments);
            Assert.Equal("/opt/azureauth", healthProbe.WorkingDirectory);
            Assert.Equal(TimeSpan.FromSeconds(10), healthProbe.Timeout);
            Assert.Empty(healthProbe.Environment);
            Assert.Null(healthProbe.StandardErrorTee);
            Assert.NotEmpty(acquisitionService.Requests);
            Assert.All(
                acquisitionService.Requests,
                request => Assert.NotEqual(InteractivePolicy.UserAllowed, request.InteractivePolicy)
            );
            Assert.Equal(2, gitDiscoveryRunner.StartSpecs.Count);
            ProcessStartSpec gitDiscovery = gitDiscoveryRunner.StartSpecs[0];
            Assert.Equal("fake-git", gitDiscovery.FileName);
            Assert.Equal(
                [
                    "config",
                    "--includes",
                    "--show-scope",
                    "--null",
                    "--get-regexp",
                    @"^credential(\..*)?\.helper$",
                ],
                gitDiscovery.Arguments
            );
            Assert.False(gitDiscovery.Environment.ContainsKey("GIT_CONFIG_GLOBAL"));
            Assert.Null(gitDiscovery.StandardErrorTee);
            Assert.Equal(string.Empty, promptWriter.ToString());
            Assert.DoesNotContain(PrivateToken, doctor.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateToken, doctor.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateDiagnostic, doctor.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateDiagnostic, doctor.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    private sealed class HealthyDoctorAcquisitionService : ICredentialAcquisitionService
    {
        public List<CredentialRequestV2> Requests { get; } = [];

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (
                request.Ecosystem == CredentialEcosystem.NuGet
                && request.AcquisitionMode == AcquisitionMode.SilentOnly
            )
            {
                return ValueTask.FromResult(
                    new CredentialResult
                    {
                        Status = CredentialResultStatus.InteractionBlocked,
                        DiagnosticsCorrelationId = "healthy-doctor-interaction-blocked",
                        Error = new CredentialError
                        {
                            Kind = CredentialErrorKind.InteractionBlocked,
                            Code = "InteractionBlocked",
                            SafeMessage =
                                "Credential request requires interaction, but interaction is "
                                + "blocked by policy.",
                        },
                    }
                );
            }

            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    Username =
                        request.Ecosystem == CredentialEcosystem.NuGet
                            ? "VssSessionToken"
                            : "AzureDevOps",
                    Password =
                        request.Ecosystem == CredentialEcosystem.NuGet
                            ? "opaque-session-token-healthy-doctor"
                            : "fake-secret-healthy-doctor",
                    Account = request.AccountHint ?? "unbound",
                    Tenant = request.TenantHint ?? "unbound",
                    DiagnosticsCorrelationId = "healthy-doctor-success",
                }
            );
        }
    }

    [Fact]
    public void DoctorFailsWhenAzureAuthVersionProbeExitsNonzero() =>
        AssertDoctorAzureAuthProbeFailure(
            new ProcessResult(23, string.Empty, "private diagnostic"),
            "AzureAuthVersionProbeExitNonZero"
        );

    [Fact]
    public void DoctorFailsWhenAzureAuthVersionProbeTimesOut() =>
        AssertDoctorAzureAuthProbeFailure(
            ProcessResult.TimedOut(string.Empty, "private diagnostic"),
            "AzureAuthVersionProbeTimedOut"
        );

    [Fact]
    public void DoctorFailsWhenAzureAuthVersionProbeCannotLaunch() =>
        AssertDoctorAzureAuthProbeFailure(
            ProcessResult.LaunchFailure(standardError: "private diagnostic"),
            "AzureAuthVersionProbeLaunchFailed"
        );

    [Fact]
    public void DoctorNpmAndYarnFailuresAffectAggregateExitCode()
    {
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        string npmrcPath = Path.Combine(pythonFixture.RootPath, "user.npmrc");
        File.WriteAllText(npmrcPath, $"registry={TestRegistryUrl}\n");
        File.WriteAllText(
            Path.Combine(pythonFixture.RootPath, ".yarnrc.yml"),
            $$"""
            npmRegistries:
              '{{TestRegistryUrl}}':
                npmAuthIdent: 'user:password'
            """
        );
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture) with
        {
            NpmPhase12Options = CreateIsolatedNpmPhase12Options(pythonFixture.RootPath),
        };

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "npm-ci-temporary-credential-plan", "fail");
        AssertDoctorCheck(doctor.StdOut, "yarn-forbidden-auth-ident-conflict", "present");
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
    }

    private static void AssertDoctorAzureAuthProbeFailure(
        ProcessResult processResult,
        string expectedCode
    )
    {
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(
            pythonFixture,
            processResult,
            out PromptingResultProcessRunner runner
        );

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "azureauth-version-probe", "fail");
        AssertDoctorCheck(doctor.StdOut, "azureauth-version-probe-code", expectedCode);
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        Assert.Single(runner.StartSpecs);
        Assert.Equal(["--version"], runner.StartSpecs[0].Arguments);
        Assert.DoesNotContain("private diagnostic", doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("private diagnostic", doctor.StdErr, StringComparison.Ordinal);
    }

    private sealed class HealthyDoctorGitDiscoveryProcessRunner : IProcessRunner
    {
        public string ExpectedHelperCommand { private get; set; } = null!;

        public string? ConflictingHelperValue { private get; set; }

        public string? BypassHelperValue { private get; set; }

        public ProcessResult? UseHttpPathResultOverride { private get; set; }

        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();
            StartSpecs.Add(startSpec);
            if (startSpec.Arguments.Contains("fill"))
            {
                if (UseHttpPathResultOverride is not null)
                {
                    return Task.FromResult(UseHttpPathResultOverride);
                }
                return Task.FromResult(
                    new ProcessResult(
                        0,
                        "protocol=https\n"
                            + "host=dev.azure.com\n"
                            + "path=org/project/_git/repository\n"
                            + "username=azureauth-use-http-path-present\n"
                            + "password=azureauth-use-http-path-probe\n",
                        string.Empty
                    )
                );
            }

            if (startSpec.Arguments.Contains("--get-urlmatch"))
            {
                string probeKey = startSpec.Environment
                    .Where(pair =>
                        pair.Key.StartsWith(
                            "GIT_CONFIG_KEY_",
                            StringComparison.Ordinal
                        )
                        && pair.Value?.StartsWith(
                            "credential.https://dev.azure.com/org.azureauthdiscovery",
                            StringComparison.Ordinal
                        ) == true
                    )
                    .Select(pair => pair.Value!)
                    .Single();
                string variable = probeKey[(probeKey.LastIndexOf('.') + 1)..];
                return Task.FromResult(
                    new ProcessResult(
                        0,
                        "credential." + variable + "\ntrue\0",
                        string.Empty
                    )
                );
            }

            return Task.FromResult(
                new ProcessResult(
                    0,
                    BypassHelperValue is not null
                        ? "local\0credential.helper\n"
                            + BypassHelperValue
                            + "\0"
                        : "global\0credential.helper\n"
                            + ExpectedHelperCommand
                            + "\0"
                            + (
                                ConflictingHelperValue is null
                                    ? string.Empty
                                    : "global\0credential.https://dev.azure.com/org.helper\n\0"
                                        + "global\0credential.https://dev.azure.com/org.helper\n"
                                        + ConflictingHelperValue
                                        + "\0"
                            ),
                    string.Empty
                )
            );
        }
    }

    [Fact]
    public void DoctorPrintsOnlySanitizedGitConflictProvenance()
    {
        const string SensitiveHelper =
            "!provider --token secret-value --path /private/provider/location";
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(
            pythonFixture,
            new ProcessResult(0, "unused-private-token", "unused-private-diagnostic"),
            out _,
            out HealthyDoctorGitDiscoveryProcessRunner gitDiscoveryRunner
        );

        Assert.Equal(0, InvokeWithRuntime(runtime, "configure", "git").ExitCode);
        gitDiscoveryRunner.ConflictingHelperValue = SensitiveHelper;

        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(
            doctor.StdOut,
            "git-effective-credential-helper-conflict",
            "scope=global; selector=url-specific; directive=reset"
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "git-effective-credential-helper-remediation",
            "remove the conflicting reset or configure the product helper after it"
        );
        Assert.DoesNotContain(SensitiveHelper, doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", doctor.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/private/provider/location",
            doctor.StdOut,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void DoctorDoesNotAttributeBypassToObservedThirdPartyHelper()
    {
        const string SensitiveHelper =
            "!local-provider --token bypass-secret --path /private/local/provider";
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(
            pythonFixture,
            new ProcessResult(0, "unused-private-token", "unused-private-diagnostic"),
            out _,
            out HealthyDoctorGitDiscoveryProcessRunner gitDiscoveryRunner
        );

        Assert.Equal(0, InvokeWithRuntime(runtime, "configure", "git").ExitCode);
        gitDiscoveryRunner.BypassHelperValue = SensitiveHelper;

        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "git-effective-credential-helper", "bypassed");
        AssertDoctorCheck(doctor.StdOut, "git-effective-credential-helper-order", "other");
        AssertDoctorCheck(
            doctor.StdOut,
            "git-effective-credential-helper-conflict",
            "scope=unknown; selector=unknown; directive=activation-bypassed"
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "git-effective-credential-helper-remediation",
            "make the product-managed Git include effective and check Git configuration "
                + "overrides such as GIT_CONFIG_GLOBAL"
        );
        Assert.DoesNotContain(SensitiveHelper, doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("bypass-secret", doctor.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/private/local/provider",
            doctor.StdOut,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void DoctorReportsIncompleteTruncatedUseHttpPathInspection()
    {
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(
            pythonFixture,
            new ProcessResult(0, "unused-private-token", "unused-private-diagnostic"),
            out _,
            out HealthyDoctorGitDiscoveryProcessRunner gitDiscoveryRunner
        );

        Assert.Equal(0, InvokeWithRuntime(runtime, "configure", "git").ExitCode);
        gitDiscoveryRunner.UseHttpPathResultOverride = ProcessResult.OutputTooLarge(
            "protocol=https\nhost=dev.azure.com\n"
                + "username=azureauth-use-http-path-present\n"
                + "******",
            string.Empty,
            exitCode: 0
        );

        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(
            doctor.StdOut,
            "dev.azure.com-useHttpPath",
            "inspection-incomplete"
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "dev.azure.com-useHttpPath-truncated",
            "yes"
        );
        AssertDoctorCheck(doctor.StdOut, "git-effective-credential-helper-truncated", "no");
    }

    [Fact]
    public async Task PassingGitDiscoveryRunnerIsolatesAndAppliesExplicitConfig()
    {
        string directory = CreateTestDirectory();
        string globalConfig = Path.Combine(directory, "global.gitconfig");
        File.WriteAllText(globalConfig, string.Empty);
        var runner = new PassingGitDiscoveryProcessRunner(
            ["GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_KEY_99"]
        );

        try
        {
            _ = await runner.RunAsync(
                new ProcessStartSpec(
                    "ignored-git",
                    ["config", "--get-all", "credential.helper"],
                    directory,
                    new Dictionary<string, string?>
                    {
                        ["GIT_CONFIG_GLOBAL"] = globalConfig,
                        ["GIT_CONFIG_COUNT"] = "1",
                        ["GIT_CONFIG_KEY_0"] = "credential.helper",
                        ["GIT_CONFIG_VALUE_0"] = "manager",
                    }
                ),
                TestContext.Current.CancellationToken
            );

            ProcessStartSpec effective = Assert.Single(runner.EffectiveGitStartSpecs);
            Assert.Null(effective.Environment["GIT_CONFIG"]);
            Assert.Equal(globalConfig, effective.Environment["GIT_CONFIG_GLOBAL"]);
            Assert.Equal("1", effective.Environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Null(effective.Environment["GIT_CONFIG_KEY_99"]);
            Assert.Equal("1", effective.Environment["GIT_CONFIG_COUNT"]);
            Assert.Equal(
                "credential.helper",
                effective.Environment["GIT_CONFIG_KEY_0"]
            );
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    [Theory]
    [InlineData("cleanup")]
    [InlineData("cleanup", "npm")]
    [InlineData("cleanup", "--ci", "none")]
    [InlineData("cleanup", "npm", "--ci", "none")]
    public void CleanupRequiresExplicitAzurePipelinesCiMode(params string[] args)
    {
        const string ExpectedCleanupCiRequiredDiagnostic =
            "error: cleanup requires '--ci azure-pipelines'. "
            + "Run 'azureauth-credprovider cleanup --help' for usage.\n";

        CommandResult result = Invoke(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(ExpectedCleanupCiRequiredDiagnostic, Normalize(result.StdErr));
    }

    private static CliRuntimeOptions CreateHealthyDoctorRuntimeOptions(
        PythonDoctorFixture pythonFixture
    ) =>
        CreateHealthyDoctorRuntimeOptions(
            pythonFixture,
            new ProcessResult(0, "unused-private-token", "unused-private-diagnostic"),
            out _
        );

    private static CliRuntimeOptions CreateHealthyDoctorRuntimeOptions(
        PythonDoctorFixture pythonFixture,
        ProcessResult healthProbeResult,
        out PromptingResultProcessRunner healthProbeRunner
    ) =>
        CreateHealthyDoctorRuntimeOptions(
            pythonFixture,
            healthProbeResult,
            out healthProbeRunner,
            out _
        );

    private static CliRuntimeOptions CreateHealthyDoctorRuntimeOptions(
        PythonDoctorFixture pythonFixture,
        ProcessResult healthProbeResult,
        out PromptingResultProcessRunner healthProbeRunner,
        out HealthyDoctorGitDiscoveryProcessRunner gitDiscoveryRunner
    )
    {
        var promptWriter = new StringWriter();
        healthProbeRunner = new PromptingResultProcessRunner(
            healthProbeResult,
            streamPrompt: false
        );
        CredentialProviderCompositionRoot productionRoot = CreatePhase2ProductionRoot(
            pythonFixture.RootPath,
            CreatePhase2Installation(AzureAuthHostPlatform.NativeLinux),
            healthProbeRunner,
            promptWriter
        );
        System.Reflection.ConstructorInfo rootConstructor = Assert.Single(
            typeof(CredentialProviderCompositionRoot).GetConstructors(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
            )
        );
        var acquisitionService = new HealthyDoctorAcquisitionService();
        CredentialProviderCompositionRoot root = Assert.IsType<CredentialProviderCompositionRoot>(
            rootConstructor.Invoke([
                CredentialProviderCompositionMode.Production,
                productionRoot.ProviderConfig,
                productionRoot.BindingRecord,
                productionRoot.Installation,
                AzureAuthProcessLaunchOptions.FromInstallation(productionRoot.Installation),
                acquisitionService,
                productionRoot.Readiness,
                productionRoot.ProductionOptions,
            ])
        );
        gitDiscoveryRunner = new HealthyDoctorGitDiscoveryProcessRunner();
        var gitOptions = new GitPhase8VerticalSliceOptions
        {
            StateDirectoryPath = pythonFixture.RootPath,
            GitConfigurationProbeWorkingDirectoryPath = pythonFixture.RootPath,
            ProcessRunner = gitDiscoveryRunner,
            GitExecutablePath = "fake-git",
            LocalShellGitDiscoverySupported = true,
            ProductExecutablePath = CreateFakeProductExecutable(pythonFixture.RootPath),
        };
        string expectedHelperCommand = new GitPhase8VerticalSliceService(gitOptions)
            .Paths
            .GitHelperPath;
        gitDiscoveryRunner.ExpectedHelperCommand = OperatingSystem.IsWindows()
            ? expectedHelperCommand.Replace('\\', '/')
            : expectedHelperCommand;
        return new CliRuntimeOptions
        {
            CompositionRoot = root,
            GitPhase8Options = gitOptions,
            NuGetPhase10Options = CreateIsolatedNuGetPhase10Options(),
            NpmPhase12Options = CreateIsolatedNpmPhase12Options(pythonFixture.RootPath),
            YarnPhase13Options = CreateIsolatedYarnPhase13Options(pythonFixture.RootPath),
            PythonPhase11Options = pythonFixture.Options,
            ConfigurationPhase14Options = CreateConfigurationPhase14Options(
                pythonFixture.RootPath
            ),
        };
    }

    private static void AssertDoctorCheck(string output, string key, string expectedValue)
    {
        string line = Assert.Single(
            Normalize(output).Split('\n', StringSplitOptions.RemoveEmptyEntries),
            candidate => candidate.StartsWith(key + ": ", StringComparison.Ordinal)
        );
        Assert.Equal($"{key}: {expectedValue}", line);
    }

    private enum PythonDoctorFixtureMode
    {
        Healthy,
        MissingKeyringModule,
        ShadowedExpectedShim,
        HealthyResolvedFromVirtualEnvironment,
        ResolvedInterpreterMissingKeyringModule,
        ResolvedInterpreterProbeLaunchFailure,
        NoCurrentTerminalInterpreter,
    }

    private sealed class PythonDoctorFixture : IDisposable
    {
        private readonly Dictionary<string, string?> environmentVariables = [];
        private string modeledPath;

        public PythonDoctorFixture(PythonDoctorFixtureMode mode)
        {
            RootPath = CreateTestDirectory();
            string shimDirectory = Path.Combine(RootPath, "python-keyring-shim");
            CreateOwnerOnlyDirectory(shimDirectory);
            string keyringExecutableFileName = OperatingSystem.IsWindows()
                ? "keyring.exe"
                : "keyring";
            string expectedKeyringShimPath = Path.Combine(
                shimDirectory,
                keyringExecutableFileName
            );
            WriteExecutable(expectedKeyringShimPath);

            string environmentRoot = Path.Combine(RootPath, "python-environment");
            string scriptsDirectory = Path.Combine(
                environmentRoot,
                OperatingSystem.IsWindows() ? "Scripts" : "bin"
            );
            CreateOwnerOnlyDirectory(scriptsDirectory);
            PythonExecutablePath = Path.Combine(
                scriptsDirectory,
                OperatingSystem.IsWindows() ? "python.exe" : "python"
            );
            WriteExecutable(PythonExecutablePath);
            string helperPath = Path.Combine(
                scriptsDirectory,
                OperatingSystem.IsWindows()
                    ? "azureauth-keyring.exe"
                    : "azureauth-keyring"
            );
            WriteExecutable(helperPath);
            modeledPath =
                shimDirectory + Path.PathSeparator + scriptsDirectory;
            ResolutionProcessRunner = new RecordingPythonResolutionProcessRunner();
            bool useExplicitPythonExecutablePath = true;
            if (mode == PythonDoctorFixtureMode.MissingKeyringModule)
            {
                ResolutionProcessRunner.EnqueueResult(
                    new ProcessResult(
                        20,
                        KeyringModuleNotFoundOutput,
                        string.Empty
                    )
                );
            }
            else if (mode == PythonDoctorFixtureMode.ShadowedExpectedShim)
            {
                string shadowDirectory = Path.Combine(RootPath, "path-shadow");
                CreateOwnerOnlyDirectory(shadowDirectory);
                WriteExecutable(
                    Path.Combine(shadowDirectory, Path.GetFileName(expectedKeyringShimPath))
                );
                modeledPath =
                    shadowDirectory
                    + Path.PathSeparator
                    + Path.GetDirectoryName(expectedKeyringShimPath);
            }
            else if (mode == PythonDoctorFixtureMode.HealthyResolvedFromVirtualEnvironment)
            {
                useExplicitPythonExecutablePath = false;
                environmentVariables["VIRTUAL_ENV"] = environmentRoot;
            }
            else if (mode == PythonDoctorFixtureMode.ResolvedInterpreterMissingKeyringModule)
            {
                useExplicitPythonExecutablePath = false;
                environmentVariables["VIRTUAL_ENV"] = environmentRoot;
                ResolutionProcessRunner.EnqueueResult(
                    new ProcessResult(
                        20,
                        KeyringModuleNotFoundOutput,
                        string.Empty
                    )
                );
            }
            else if (mode == PythonDoctorFixtureMode.ResolvedInterpreterProbeLaunchFailure)
            {
                useExplicitPythonExecutablePath = false;
                environmentVariables["VIRTUAL_ENV"] = environmentRoot;
                ResolutionProcessRunner.EnqueueResult(ProcessResult.LaunchFailure());
            }
            else if (mode == PythonDoctorFixtureMode.NoCurrentTerminalInterpreter)
            {
                useExplicitPythonExecutablePath = false;
                environmentVariables["TOX_ENV_DIR"] = environmentRoot;
                ResolutionProcessRunner.EnqueueResult(ProcessResult.LaunchFailure());
                ResolutionProcessRunner.EnqueueResult(ProcessResult.LaunchFailure());
            }

            environmentVariables["PATH"] = modeledPath;

            Options = new PythonPhase11VerticalSliceOptions
            {
                FileSystem = new SystemFileSystem(),
                ProcessRunner = ResolutionProcessRunner,
                EnvironmentVariableReader = ReadEnvironmentVariable,
                ExpectedKeyringShimPath = expectedKeyringShimPath,
                PythonExecutablePath = useExplicitPythonExecutablePath
                    ? PythonExecutablePath
                    : null,
                CurrentDirectoryPath = RootPath,
                PathListSeparator = Path.PathSeparator,
                KeyringExecutableFileName = keyringExecutableFileName,
            };
        }

        public string RootPath { get; }

        public bool PathWasRequested { get; private set; }

        public string PythonExecutablePath { get; }

        public RecordingPythonResolutionProcessRunner ResolutionProcessRunner { get; }

        public PythonPhase11VerticalSliceOptions Options { get; }

        public void Dispose() => DeleteDirectoryIfExists(RootPath);

        private string? ReadEnvironmentVariable(string name)
        {
            if (string.Equals(name, "PATH", StringComparison.Ordinal))
            {
                PathWasRequested = true;
            }

            return environmentVariables.TryGetValue(name, out string? value) ? value : null;
        }

        private static void WriteExecutable(string path)
        {
            WriteOwnerOnlyText(path, "#!/usr/bin/env python\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
            }
        }
    }

    [Fact]
    public void DoctorMissingPythonKeyringModuleRendersFailureAndReturnsNonZero() =>
        AssertDoctorPythonFailure(PythonDoctorFixtureMode.MissingKeyringModule);

    [Fact(
        Skip = "POSIX Python keyring shim PATH behavior is unsupported on Windows.",
        SkipWhen = nameof(IsWindows)
    )]
    public void DoctorPathShadowedPythonKeyringShimRendersFailureAndReturnsNonZero() =>
        AssertDoctorPythonFailure(PythonDoctorFixtureMode.ShadowedExpectedShim);

    [Fact]
    public void DoctorResolvesCurrentTerminalVirtualEnvironmentWithoutExplicitPythonPath()
    {
        using var pythonFixture = new PythonDoctorFixture(
            PythonDoctorFixtureMode.HealthyResolvedFromVirtualEnvironment
        );
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture);

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(string.Empty, configureGit.StdErr);
        Assert.Equal(0, doctor.ExitCode);
        string expectedShimStatus = OperatingSystem.IsWindows() ? "N/A" : "pass";
        AssertDoctorCheck(doctor.StdOut, "python-keyring-shim-exists", expectedShimStatus);
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-shim-first-on-path",
            expectedShimStatus
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-interpreter",
            pythonFixture.PythonExecutablePath
        );
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "pass");
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module-probe", "module-found");
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "pass");
        Assert.Collection(
            pythonFixture.ResolutionProcessRunner.StartSpecs,
            startSpec => Assert.Equal(
                pythonFixture.PythonExecutablePath,
                startSpec.FileName
            ),
            startSpec => Assert.Equal(
                pythonFixture.PythonExecutablePath,
                startSpec.FileName
            )
        );
        Assert.True(pythonFixture.PathWasRequested);
        Assert.DoesNotContain("machine-wide", doctor.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoctorFailsResolvedCurrentTerminalInterpreterWhenKeyringModuleIsMissing()
    {
        using var pythonFixture = new PythonDoctorFixture(
            PythonDoctorFixtureMode.ResolvedInterpreterMissingKeyringModule
        );
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture);

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(string.Empty, configureGit.StdErr);
        Assert.Equal(1, doctor.ExitCode);
        string expectedShimStatus = OperatingSystem.IsWindows() ? "N/A" : "pass";
        AssertDoctorCheck(doctor.StdOut, "python-keyring-shim-exists", expectedShimStatus);
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-shim-first-on-path",
            expectedShimStatus
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-interpreter",
            pythonFixture.PythonExecutablePath
        );
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "fail");
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-module-probe",
            "module-not-found"
        );
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        ProcessStartSpec startSpec = Assert.Single(
            pythonFixture.ResolutionProcessRunner.StartSpecs
        );
        Assert.Equal(pythonFixture.PythonExecutablePath, startSpec.FileName);
        Assert.True(pythonFixture.PathWasRequested);
        Assert.DoesNotContain("machine-wide", doctor.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoctorFailsWhenNoCurrentTerminalPythonInterpreterExists()
    {
        using var pythonFixture = new PythonDoctorFixture(
            PythonDoctorFixtureMode.NoCurrentTerminalInterpreter
        );
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture);

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(string.Empty, configureGit.StdErr);
        Assert.Equal(1, doctor.ExitCode);
        string expectedShimStatus = OperatingSystem.IsWindows() ? "N/A" : "pass";
        AssertDoctorCheck(doctor.StdOut, "python-keyring-shim-exists", expectedShimStatus);
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-shim-first-on-path",
            expectedShimStatus
        );
        AssertDoctorCheck(doctor.StdOut, "python-interpreter", "not-found");
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "fail");
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-module-probe",
            "interpreter-not-found"
        );
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        Assert.Collection(
            pythonFixture.ResolutionProcessRunner.StartSpecs,
            startSpec => Assert.Equal("python3", startSpec.FileName),
            startSpec => Assert.Equal("python", startSpec.FileName)
        );
        Assert.True(pythonFixture.PathWasRequested);
        Assert.DoesNotContain("machine-wide", doctor.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoctorReportsSelectedInterpreterProbeLaunchFailureTruthfully()
    {
        using var pythonFixture = new PythonDoctorFixture(
            PythonDoctorFixtureMode.ResolvedInterpreterProbeLaunchFailure
        );
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture);

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(string.Empty, configureGit.StdErr);
        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(
            doctor.StdOut,
            "python-interpreter",
            pythonFixture.PythonExecutablePath
        );
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "fail");
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-module-probe",
            "launch-failed"
        );
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        ProcessStartSpec startSpec = Assert.Single(
            pythonFixture.ResolutionProcessRunner.StartSpecs
        );
        Assert.Equal(pythonFixture.PythonExecutablePath, startSpec.FileName);
    }

    private static void AssertDoctorPythonFailure(PythonDoctorFixtureMode mode)
    {
        using var pythonFixture = new PythonDoctorFixture(mode);
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture);

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(string.Empty, configureGit.StdErr);
        Assert.Equal(1, doctor.ExitCode);
        string expectedShimStatus = OperatingSystem.IsWindows() ? "N/A" : "pass";
        AssertDoctorCheck(doctor.StdOut, "python-keyring-shim-exists", expectedShimStatus);
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-shim-first-on-path",
            OperatingSystem.IsWindows()
                ? "N/A"
                : mode == PythonDoctorFixtureMode.ShadowedExpectedShim
                    ? "fail"
                    : "pass"
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-module",
            mode == PythonDoctorFixtureMode.MissingKeyringModule ? "fail" : "pass"
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-module-probe",
            mode == PythonDoctorFixtureMode.MissingKeyringModule
                ? "module-not-found"
                : "module-found"
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-azure-artifacts-endpoint-canonicalization",
            "pass"
        );
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        Assert.Equal(string.Empty, doctor.StdErr);
        Assert.True(pythonFixture.PathWasRequested);
    }

#pragma warning disable CA1707
    [Fact]
    public async Task Doctor_ContinuesAggregationAfterExpectedNpmResolutionFailure()
    {
        const string SensitiveDiagnostic = "_authToken=phase3-secret";
        string stateDirectory = CreateTestDirectory();
        var callOrder = new List<string>();
        var npmRunner = new Phase3NpmProcessRunner(
            () =>
            {
                callOrder.Add("npm");
                return ProcessResult.TimedOut(string.Empty, SensitiveDiagnostic);
            }
        );

        try
        {
            string invocationDirectory = CreatePhase3NpmWorkspace(stateDirectory);
            File.WriteAllText(
                Path.Combine(stateDirectory, ".yarnrc.yml"),
                $"npmRegistryServer: \"{TestRegistryUrl}\"\n"
            );
            var expectedYarnObservations = 0;
            YarnPhase13VerticalSliceOptions referenceYarnOptions =
                CreatePhase3YarnOptions(stateDirectory, () => expectedYarnObservations++);
            _ = await new YarnPhase13VerticalSliceService(referenceYarnOptions)
                .RunDoctorAsync(TestContext.Current.CancellationToken);
            var expectedConfigurationObservations = 0;
            ConfigurationPhase14VerticalSliceOptions referenceConfigurationOptions =
                CreateConfigurationPhase14Options(stateDirectory) with
                {
                    EnvironmentVariableReader = name =>
                    {
                        expectedConfigurationObservations++;
                        return ReadConfigurationPhase14Environment(stateDirectory, name);
                    },
                };
            _ = await new ConfigurationPhase14VerticalSliceService(
                referenceConfigurationOptions
            )
                .DoctorAsync(TestContext.Current.CancellationToken);
            var yarnObservations = 0;
            var configurationObservations = 0;
            CliRuntimeOptions runtime = CreateGitPhase8RuntimeOptions(stateDirectory) with
            {
                NpmPhase12Options = CreatePhase3NpmOptions(
                    invocationDirectory,
                    stateDirectory,
                    npmRunner
                ),
                YarnPhase13Options = CreatePhase3YarnOptions(
                    stateDirectory,
                    () =>
                    {
                        yarnObservations++;
                        callOrder.Add("yarn");
                    }
                ),
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(
                    stateDirectory
                ) with
                {
                    EnvironmentVariableReader = name =>
                    {
                        configurationObservations++;
                        callOrder.Add("configuration");
                        return ReadConfigurationPhase14Environment(stateDirectory, name);
                    },
                },
            };

            CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

            Assert.Equal(1, doctor.ExitCode);
            AssertDoctorCheck(
                doctor.StdOut,
                "npm-workspace-resolution-status",
                "TimedOut"
            );
            AssertDoctorCheck(doctor.StdOut, "npm-workspace-npmrc", "fail");
            AssertDoctorCheck(doctor.StdOut, "npm-registry-declaration", "skipped");
            AssertDoctorCheck(doctor.StdOut, "npm-user-credential-plan", "skipped");
            AssertDoctorCheck(doctor.StdOut, "pnpm-user-credential-plan", "skipped");
            AssertDoctorCheck(
                doctor.StdOut,
                "npm-ci-temporary-credential-plan",
                "skipped"
            );
            AssertDoctorCheck(doctor.StdOut, "yarn-registry-declaration", "present");
            AssertDoctorCheck(doctor.StdOut, "yarn-writes", "pass");
            AssertDoctorCheck(doctor.StdOut, "configuration-aggregation", "pass");
            AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
            Assert.Equal(string.Empty, doctor.StdErr);
            Assert.DoesNotContain(SensitiveDiagnostic, doctor.StdOut, StringComparison.Ordinal);
            Assert.Single(npmRunner.StartSpecs);
            int npmCall = callOrder.IndexOf("npm");
            int yarnCall = callOrder.IndexOf("yarn");
            int configurationCall = callOrder.IndexOf("configuration");
            Assert.True(npmCall >= 0);
            Assert.True(yarnCall > npmCall);
            Assert.True(configurationCall > yarnCall);
            Assert.True(expectedYarnObservations > 0);
            Assert.True(expectedConfigurationObservations > 0);
            Assert.Equal(expectedYarnObservations, yarnObservations);
            Assert.Equal(
                expectedConfigurationObservations,
                configurationObservations
            );
            Assert.Equal(
                1,
                Normalize(doctor.StdOut)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Count(line =>
                        line.StartsWith(
                            "yarn-registry-declaration: ",
                            StringComparison.Ordinal
                        )
                    )
            );
            Assert.Equal(
                1,
                Normalize(doctor.StdOut)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Count(line =>
                        line.StartsWith(
                            "configuration-aggregation: ",
                            StringComparison.Ordinal
                        )
                    )
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void Doctor_UsesCapturedCurrentDirectoryForDefaultNpmAndYarnOptions()
    {
        string stateDirectory = CreateTestDirectory();
        string firstDirectory = Path.Combine(stateDirectory, "first-capture");
        Directory.CreateDirectory(firstDirectory);
        File.WriteAllText(
            Path.Combine(firstDirectory, ".npmrc"),
            $"registry={TestRegistryUrl}\n"
        );
        File.WriteAllText(
            Path.Combine(firstDirectory, ".yarnrc.yml"),
            $"npmRegistryServer: \"{TestRegistryUrl}\"\n"
        );
        string userNpmrcPath = Path.Combine(stateDirectory, "injected-user.npmrc");
        string userYarnrcPath = Path.Combine(stateDirectory, "injected-user.yarnrc.yml");
        File.WriteAllText(userNpmrcPath, "fund=false\n");
        File.WriteAllText(userYarnrcPath, "enableTelemetry: false\n");
        var npmRunner = new Phase3NpmProcessRunner(
            () => throw new InvalidOperationException("npm prefix was not expected")
        );
        var npmOptionObservations = 0;
        var yarnOptionObservations = 0;

        try
        {
            CliRuntimeOptions runtime = CreateGitPhase8RuntimeOptions(stateDirectory) with
            {
                NpmPhase12Options = new NpmPhase12VerticalSliceOptions
                {
                    ProcessRunner = npmRunner,
                    WorkspaceDirectoryPath = null,
                    UserHomeDirectoryPath = stateDirectory,
                    UserNpmrcPath = userNpmrcPath,
                    CiTemporaryNpmrcPath = Path.Combine(stateDirectory, "ci", ".npmrc"),
                    EnvironmentVariableReader = _ =>
                    {
                        npmOptionObservations++;
                        return null;
                    },
                },
                YarnPhase13Options = new YarnPhase13VerticalSliceOptions
                {
                    WorkspaceDirectoryPath = null,
                    UserHomeDirectoryPath = stateDirectory,
                    UserYarnrcPath = userYarnrcPath,
                    EnvironmentVariableReader = _ =>
                    {
                        yarnOptionObservations++;
                        return null;
                    },
                },
            };
            CommandResult doctor = InvokeWithRuntimeFromDirectory(
                firstDirectory,
                runtime,
                "doctor"
            );

            Assert.Equal(1, doctor.ExitCode);
            Assert.Equal(string.Empty, doctor.StdErr);
            AssertDoctorCheck(doctor.StdOut, "npm-workspace-npmrc", "present");
            AssertDoctorCheck(doctor.StdOut, "yarn-workspace-yarnrc", "present");
            AssertDoctorCheck(doctor.StdOut, "npm-registry-declaration", "present");
            AssertDoctorCheck(doctor.StdOut, "yarn-registry-declaration", "present");
            AssertDoctorCheck(doctor.StdOut, "npm-effective-user-npmrc", "present");
            AssertDoctorCheck(doctor.StdOut, "yarn-effective-user-yarnrc", "present");
            Assert.True(npmOptionObservations > 0);
            Assert.True(yarnOptionObservations > 0);
            Assert.Empty(npmRunner.StartSpecs);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void Doctor_PreservesExplicitNpmAndYarnWorkspaceDirectories()
    {
        string stateDirectory = CreateTestDirectory();
        string defaultDirectory = Path.Combine(stateDirectory, "captured-default");
        string npmDirectory = Path.Combine(stateDirectory, "explicit-npm");
        string yarnDirectory = Path.Combine(stateDirectory, "explicit-yarn");
        Directory.CreateDirectory(defaultDirectory);
        Directory.CreateDirectory(npmDirectory);
        Directory.CreateDirectory(yarnDirectory);
        File.WriteAllText(
            Path.Combine(npmDirectory, ".npmrc"),
            $"registry={TestRegistryUrl}\n"
        );
        File.WriteAllText(
            Path.Combine(yarnDirectory, ".yarnrc.yml"),
            $"npmRegistryServer: \"{TestRegistryUrl}\"\n"
        );
        var npmRunner = new Phase3NpmProcessRunner(
            () => throw new InvalidOperationException("npm prefix was not expected")
        );
        var npmOptionObservations = 0;
        var yarnOptionObservations = 0;

        try
        {
            CliRuntimeOptions runtime = CreateGitPhase8RuntimeOptions(stateDirectory) with
            {
                NpmPhase12Options = CreatePhase3NpmOptions(
                        npmDirectory,
                        stateDirectory,
                        npmRunner
                    ) with
                {
                    EnvironmentVariableReader = _ =>
                    {
                        npmOptionObservations++;
                        return null;
                    },
                },
                YarnPhase13Options = new YarnPhase13VerticalSliceOptions
                {
                    WorkspaceDirectoryPath = yarnDirectory,
                    UserHomeDirectoryPath = stateDirectory,
                    UserYarnrcPath = Path.Combine(stateDirectory, "user.yarnrc.yml"),
                    EnvironmentVariableReader = _ =>
                    {
                        yarnOptionObservations++;
                        return null;
                    },
                },
            };
            CommandResult doctor = InvokeWithRuntimeFromDirectory(
                defaultDirectory,
                runtime,
                "doctor"
            );

            Assert.Equal(1, doctor.ExitCode);
            Assert.Equal(string.Empty, doctor.StdErr);
            AssertDoctorCheck(doctor.StdOut, "npm-workspace-npmrc", "present");
            AssertDoctorCheck(doctor.StdOut, "yarn-workspace-yarnrc", "present");
            AssertDoctorCheck(doctor.StdOut, "npm-registry-declaration", "present");
            AssertDoctorCheck(doctor.StdOut, "yarn-registry-declaration", "present");
            Assert.True(npmOptionObservations > 0);
            Assert.True(yarnOptionObservations > 0);
            Assert.Empty(npmRunner.StartSpecs);
            Assert.DoesNotContain(defaultDirectory, doctor.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void Doctor_DoesNotReclassifyUnexpectedNpmExceptionAsExpectedResolutionFailure()
    {
        const string SensitiveMessage = "unexpected phase3 sentinel secret";
        string stateDirectory = CreateTestDirectory();
        var callOrder = new List<string>();
        var sentinel = new InvalidOperationException(SensitiveMessage);
        var npmRunner = new Phase3NpmProcessRunner(() =>
        {
            callOrder.Add("npm");
            throw sentinel;
        });

        try
        {
            string invocationDirectory = CreatePhase3NpmWorkspace(stateDirectory);
            CliRuntimeOptions runtime = CreateGitPhase8RuntimeOptions(stateDirectory) with
            {
                NpmPhase12Options = CreatePhase3NpmOptions(
                    invocationDirectory,
                    stateDirectory,
                    npmRunner
                ),
                YarnPhase13Options = new YarnPhase13VerticalSliceOptions
                {
                    WorkspaceDirectoryPath = stateDirectory,
                    UserHomeDirectoryPath = stateDirectory,
                    UserYarnrcPath = Path.Combine(stateDirectory, "user.yarnrc.yml"),
                    EnvironmentVariableReader = _ =>
                    {
                        callOrder.Add("yarn");
                        return null;
                    },
                },
                ConfigurationPhase14Options = CreateConfigurationPhase14Options(
                    stateDirectory
                ) with
                {
                    EnvironmentVariableReader = name =>
                    {
                        callOrder.Add("configuration");
                        return ReadConfigurationPhase14Environment(stateDirectory, name);
                    },
                },
            };

            CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

            Assert.Equal(70, doctor.ExitCode);
            Assert.Equal(string.Empty, doctor.StdOut);
            Assert.Equal("error: unexpected fatal failure.\n", doctor.StdErr);
            Assert.Equal(["npm"], callOrder);
            Assert.Single(npmRunner.StartSpecs);
            Assert.DoesNotContain("TimedOut", doctor.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain(SensitiveMessage, doctor.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    private static string CreatePhase3NpmWorkspace(string rootPath)
    {
        string invocationDirectory = Path.Combine(rootPath, "packages", "member");
        Directory.CreateDirectory(invocationDirectory);
        File.WriteAllText(
            Path.Combine(rootPath, "package.json"),
            """{"name":"phase3-root","private":true,"workspaces":["packages/*"]}"""
        );
        File.WriteAllText(
            Path.Combine(invocationDirectory, "package.json"),
            """{"name":"phase3-member"}"""
        );
        File.WriteAllText(
            Path.Combine(rootPath, ".npmrc"),
            $"registry={TestRegistryUrl}\n"
        );
        return invocationDirectory;
    }

    private static NpmPhase12VerticalSliceOptions CreatePhase3NpmOptions(
        string workspaceDirectory,
        string stateDirectory,
        IProcessRunner processRunner
    )
    {
        var fileSystem = new SystemFileSystem();
        string? npmDirectory = null;
        if (fileSystem.IsPathFullyQualified(@"C:\"))
        {
            npmDirectory = Path.Combine(stateDirectory, "npm-bin");
            Directory.CreateDirectory(npmDirectory);
            File.WriteAllText(Path.Combine(npmDirectory, "npm.exe"), string.Empty);
        }

        return new NpmPhase12VerticalSliceOptions
        {
            FileSystem = fileSystem,
            ProcessRunner = processRunner,
            WorkspaceDirectoryPath = workspaceDirectory,
            UserHomeDirectoryPath = stateDirectory,
            UserNpmrcPath = Path.Combine(stateDirectory, "user.npmrc"),
            CiTemporaryNpmrcPath = Path.Combine(stateDirectory, "ci", ".npmrc"),
            EnvironmentVariableReader = name =>
                npmDirectory is not null
                    ? name switch
                    {
                        "PATH" => npmDirectory,
                        "PATHEXT" => ".EXE",
                        _ => null,
                    }
                    : null,
        };
    }

    private static YarnPhase13VerticalSliceOptions CreatePhase3YarnOptions(
        string workspaceDirectory,
        Action environmentObservation
    ) =>
        new()
        {
            WorkspaceDirectoryPath = workspaceDirectory,
            UserHomeDirectoryPath = workspaceDirectory,
            UserYarnrcPath = Path.Combine(workspaceDirectory, "user.yarnrc.yml"),
            EnvironmentVariableReader = _ =>
            {
                environmentObservation();
                return null;
            },
        };

    private sealed class Phase3NpmProcessRunner(Func<ProcessResult> resultFactory)
        : IProcessRunner
    {
        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();
            StartSpecs.Add(startSpec);
            return Task.FromResult(resultFactory());
        }
    }
#pragma warning restore CA1707

    [Fact]
    public void DoctorRendersPythonKeyringModuleFinderErrorWithoutLeakingDiagnostics()
    {
        const string SensitiveDiagnostic = "phase2-module-finder-secret";
        const string RawProbeMarker = "ACP_KEYRING_PROBE_V1:ERROR";
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        var processRunner = new RecordingPythonResolutionProcessRunner();
        var processResult = new ProcessResult(21, RawProbeMarker + "\n", string.Empty);
        processRunner.EnqueueResult(processResult);
        Func<string, string?> environmentVariableReader =
            pythonFixture.Options.EnvironmentVariableReader!;
        CliRuntimeOptions runtime = CreateHealthyDoctorRuntimeOptions(pythonFixture) with
        {
            PythonPhase11Options = pythonFixture.Options with
            {
                ProcessRunner = processRunner,
                EnvironmentVariableReader = name =>
                    string.Equals(name, "PIPX_HOME", StringComparison.Ordinal)
                        ? Path.Combine(pythonFixture.RootPath, SensitiveDiagnostic)
                        : environmentVariableReader(name),
            },
        };

        CommandResult configureGit = InvokeWithRuntime(runtime, "configure", "git");
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, configureGit.ExitCode);
        Assert.Equal(string.Empty, configureGit.StdErr);
        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "fail");
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        Assert.Equal(string.Empty, doctor.StdErr);
        Assert.DoesNotContain(SensitiveDiagnostic, doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveDiagnostic, doctor.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(RawProbeMarker, doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(RawProbeMarker, doctor.StdErr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, processResult.StandardError);
        ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
        Assert.Equal(2, startSpec.Arguments.Count);
        Assert.DoesNotContain(startSpec.Arguments[1], doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(startSpec.Arguments[1], doctor.StdErr, StringComparison.Ordinal);

        Assert.Equal(8, (int)PythonPhase11KeyringModuleProbeStatus.ModuleFinderError);
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-module-probe",
            "module-finder-error"
        );
    }

#pragma warning disable CA1707, CA1861
    [Fact]
    public void HandleDoctor_RendersGenericBackendHelperShimAndFirstPathRows()
    {
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        CliRuntimeOptions runtime = CreatePhase3ProductDoctorRuntime(
            pythonFixture,
            new ProcessResult(
                0,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY\n",
                string.Empty
            )
        );

        Assert.Equal(0, InvokeWithRuntime(runtime, "configure", "git").ExitCode);
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "pass");
        AssertDoctorCheck(doctor.StdOut, "python-azureauth-keyring-backend", "pass");
        AssertDoctorCheck(
            doctor.StdOut,
            "python-azureauth-keyring-backend-probe",
            "healthy"
        );
        string platformSpecificStatus = OperatingSystem.IsWindows() ? "N/A" : "pass";
        AssertDoctorCheck(
            doctor.StdOut,
            "python-azureauth-keyring-helper",
            platformSpecificStatus
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-shim-exists",
            platformSpecificStatus
        );
        AssertDoctorCheck(
            doctor.StdOut,
            "python-keyring-shim-first-on-path",
            platformSpecificStatus
        );
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "pass");
        Assert.Equal(string.Empty, doctor.StdErr);
    }

    [Fact]
    public void HandleDoctor_OnWindows_RendersShimRowsAsExplicitNotApplicable()
    {
        PythonPhase11DoctorResult result = CreatePhase3DoctorResult(
            shimApplicable: false,
            helperApplicable: false
        );

        string[] lines = InvokePhase3PythonDoctorLines(result);

        Assert.Contains("python-keyring-shim-exists: N/A", lines);
        Assert.Contains("python-keyring-shim-first-on-path: N/A", lines);
        Assert.Contains("python-azureauth-keyring-helper: N/A", lines);
        Assert.DoesNotContain("python-keyring-shim-exists: fail", lines);
        Assert.DoesNotContain("python-keyring-shim-first-on-path: fail", lines);
    }

    [Fact]
    public void HandleDoctor_ExcludesNotApplicableRowsFromSuccessAggregation()
    {
        PythonPhase11DoctorResult result = CreatePhase3DoctorResult(
            shimApplicable: false,
            helperApplicable: false,
            shimExists: false,
            shimFirstOnPath: false
        );

        bool success = InvokePhase3PythonDoctorSuccess(result);

        Assert.True(success);
        Assert.False(result.KeyringShim.Applicable);
        Assert.False(result.AzureAuthKeyringHelper.Applicable);
        Assert.True(result.ProductProbe.BackendLoadable);
    }

    [Theory]
    [InlineData("backend")]
    [InlineData("helper")]
    public void HandleDoctor_WhenRequiredBackendOrHelperFails_ReturnsNonzero(string failure)
    {
        PythonPhase11DoctorResult result = CreatePhase3DoctorResult(
            backendLoadable: !string.Equals(failure, "backend", StringComparison.Ordinal),
            helperFound: !string.Equals(failure, "helper", StringComparison.Ordinal)
        );

        bool success = InvokePhase3PythonDoctorSuccess(result);

        Assert.False(success);
        Assert.True(result.KeyringShim.Applicable);
        Assert.True(result.AzureAuthKeyringHelper.Applicable);
    }

    [Fact]
    public void HandleDoctor_DoesNotRenderRawProbeOutput()
    {
        const string SensitiveOutput =
            "Traceback: AZURE_CLIENT_SECRET=doctor-secret arbitrary backend exception";
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        CliRuntimeOptions runtime = CreatePhase3ProductDoctorRuntime(
            pythonFixture,
            new ProcessResult(
                33,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE\n",
                SensitiveOutput
            )
        );

        Assert.Equal(0, InvokeWithRuntime(runtime, "configure", "git").ExitCode);
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(1, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "python-azureauth-keyring-backend", "fail");
        AssertDoctorCheck(
            doctor.StdOut,
            "python-azureauth-keyring-backend-probe",
            "invalid-output"
        );
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "fail");
        Assert.DoesNotContain(SensitiveOutput, doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveOutput, doctor.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("doctor-secret", doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("doctor-secret", doctor.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleConfigure_WhenPythonPreflightFails_PrintsBootstrapGuidance()
    {
        using var fixture = new Phase3ConfigureFixture(productHealthy: false);
        string[] before = fixture.GetFileSystemEntries();

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Contains(
            $"selected-python: {fixture.PythonExecutablePath}\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "bootstrap-command: "
                + Phase3BootstrapCommand(
                    fixture.PythonExecutablePath,
                    "azureauth-credprovider-keyring"
                )
                + "\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("planned-change", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("export PATH=", result.StdErr, StringComparison.Ordinal);
        Assert.Equal(before, fixture.GetFileSystemEntries());
        Assert.Equal(2, fixture.Runner.StartSpecs.Count);
        Assert.All(
            fixture.Runner.StartSpecs,
            spec => Assert.DoesNotContain("-m", spec.Arguments)
        );
    }

    [Fact]
    public void HandleConfigure_WhenGenericKeyringModuleIsMissing_InstallsKeyringAndProduct()
    {
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: false,
            processResults:
            [
                new ProcessResult(
                    20,
                    "ACP_KEYRING_PROBE_V1:NOT_FOUND\n",
                    string.Empty
                ),
            ]
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "bootstrap-command: "
                + Phase3BootstrapCommand(
                    fixture.PythonExecutablePath,
                    "keyring azureauth-credprovider-keyring"
                )
                + "\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(31, "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISSING")]
    [InlineData(32, "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISMATCH")]
    [InlineData(33, "ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE")]
    public void HandleConfigure_WhenProductContractFails_SuggestsProductReinstall(
        int exitCode,
        string marker
    )
    {
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: false,
            processResults:
            [
                new ProcessResult(0, KeyringModuleFoundOutput, string.Empty),
                new ProcessResult(exitCode, marker + "\n", string.Empty),
            ]
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "bootstrap-command: "
                + Phase3BootstrapCommand(
                    fixture.PythonExecutablePath,
                    "--force-reinstall azureauth-credprovider-keyring"
                )
                + "\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void HandleConfigure_WhenPosixHelperIsMissing_SuggestsProductReinstall()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "The Python helper preflight is POSIX-only."
        );
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: true,
            helperPresent: false
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "bootstrap-command: "
                + Phase3BootstrapCommand(
                    fixture.PythonExecutablePath,
                    "--force-reinstall azureauth-credprovider-keyring"
                )
                + "\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void HandleConfigure_WhenPosixHelperIsShadowed_SuggestsPathActivation()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "The Python helper preflight is POSIX-only."
        );
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: true,
            helperShadowed: true
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "guidance: activate the selected Python environment so "
                + fixture.ExpectedHelperPath
                + " resolves before "
                + fixture.ShadowingHelperPath
                + " on PATH, then retry.\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("bootstrap-command:", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("pip install", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleConfigure_WhenPosixHelperIsAbsentButAnotherHelperIsOnPath_SuggestsReinstall()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "The Python helper preflight is POSIX-only."
        );
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: true,
            helperPresent: false,
            helperShadowed: true
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "bootstrap-command: "
                + Phase3BootstrapCommand(
                    fixture.PythonExecutablePath,
                    "--force-reinstall azureauth-credprovider-keyring"
                )
                + "\n",
            Normalize(result.StdErr),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(" resolves before ", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleConfigure_WhenPythonProbeFails_DoesNotMaskFailureWithHelperPathGuidance()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "The Python helper preflight is POSIX-only."
        );
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: false,
            processResults: [ProcessResult.LaunchFailure()],
            helperShadowed: true
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "guidance: verify that the selected Python interpreter can be launched, then retry.",
            result.StdErr,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(" resolves before ", result.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PythonProbeFailuresWithoutInstallGuidance))]
    public void HandleConfigure_WhenProbeCannotDiagnoseDependency_OmitsInstallCommand(
        ProcessResult firstResult,
        ProcessResult? secondResult
    )
    {
        List<ProcessResult> processResults = [firstResult];
        if (secondResult is not null)
        {
            processResults.Add(secondResult);
        }
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: false,
            processResults: processResults
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("guidance: ", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap-command:", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("pip install", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<ProcessResult, ProcessResult?>
        PythonProbeFailuresWithoutInstallGuidance =>
            new()
            {
                { ProcessResult.LaunchFailure(), null },
                { ProcessResult.TimedOut(string.Empty, string.Empty), null },
                { new ProcessResult(0, "malformed\n", string.Empty), null },
                {
                    new ProcessResult(0, KeyringModuleFoundOutput, string.Empty),
                    ProcessResult.LaunchFailure()
                },
                {
                    new ProcessResult(0, KeyringModuleFoundOutput, string.Empty),
                    ProcessResult.TimedOut(string.Empty, string.Empty)
                },
                {
                    new ProcessResult(0, KeyringModuleFoundOutput, string.Empty),
                    new ProcessResult(0, "malformed\n", string.Empty)
                },
            };

    [Fact]
    public void HandleConfigure_WhenInterpreterCannotBeResolved_OmitsInstallCommand()
    {
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: false,
            processResults:
            [
                ProcessResult.LaunchFailure(),
                ProcessResult.LaunchFailure(),
            ],
            useExplicitPythonExecutable: false
        );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("selected-python: not-found", result.StdErr, StringComparison.Ordinal);
        Assert.Contains("guidance: ", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap-command:", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("pip install", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleConfigure_DryRunWhenPythonPreflightFails_ReturnsNonzeroWithoutPlannedWrites()
    {
        using var fixture = new Phase3ConfigureFixture(productHealthy: false);
        string[] before = fixture.GetFileSystemEntries();

        CommandResult result = InvokeWithRuntime(
            fixture.Runtime,
            "configure",
            "python",
            "--dry-run"
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Contains(
            "python preflight failed",
            result.StdErr,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("planned-actions", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("planned-change-count", result.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("export PATH=", result.StdErr, StringComparison.Ordinal);
        Assert.Equal(before, fixture.GetFileSystemEntries());
        Assert.Equal(2, fixture.Runner.StartSpecs.Count);
    }

    [Fact(
        Skip = "POSIX keyring subprocess bootstrap is unsupported on Windows.",
        SkipWhen = nameof(IsWindows)
    )]
    public void HandleConfigure_OnPosixWithoutExplicitImportPreflight_ConfiguresBootstrapShim()
    {
        using var fixture = new Phase3ConfigureFixture(productHealthy: false);
        ConfigurationPhase14VerticalSliceOptions configurationOptions =
            fixture.ConfigurationOptions with
            {
                PythonDoctorService = null,
            };
        var runtime = new CliRuntimeOptions
        {
            CompositionRoot = CreateTestCompositionRoot(),
            ConfigurationPhase14Options = configurationOptions,
        };

        CommandResult result = InvokeWithRuntime(runtime, "configure", "python");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Empty(fixture.Runner.StartSpecs);
        string shimPath = Path.Combine(
            fixture.HomePath,
            ".local",
            "share",
            "azureauth-credprovider",
            "keyring-shim",
            "keyring"
        );
        Assert.Equal(
            "#!/bin/sh\nexec '/opt/azureauth-credprovider/azureauth-credprovider' "
                + "keyring \"$@\"\n",
            File.ReadAllText(shimPath)
        );
        Assert.Contains("export PATH=", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap-command:", result.StdErr, StringComparison.Ordinal);
    }

    [Theory(
        Skip = "POSIX shell PATH export behavior is unsupported on Windows.",
        SkipWhen = nameof(IsWindows)
    )]
    [InlineData("home with spaces")]
    [InlineData("home-with-'quote")]
    public void HandleConfigure_OnPosixSuccess_PrintsSafelyQuotedProcessScopedPathExport(
        string homeDirectoryName
    )
    {
        using var fixture = new Phase3ConfigureFixture(
            productHealthy: true,
            homeDirectoryName
        );
        string expectedShimDirectory = OperatingSystem.IsMacOS()
            ? Path.Combine(
                fixture.HomePath,
                "Library",
                "Application Support",
                "AzureAuth",
                "CredProvider",
                "keyring-shim"
            )
            : Path.Combine(
                fixture.HomePath,
                ".local",
                "share",
                "azureauth-credprovider",
                "keyring-shim"
            );

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdErr);
        string exportLine = Assert.Single(
            Normalize(result.StdOut).Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith("export PATH=", StringComparison.Ordinal)
        );
        Assert.Equal(
            "export PATH=" + Phase3ShellQuote(expectedShimDirectory) + ":\"$PATH\"",
            exportLine
        );
        Assert.DoesNotContain(">>", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("source ", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleConfigure_OnWindowsSuccess_DoesNotPrintPathExport()
    {
        using var fixture = new Phase3ConfigureFixture(productHealthy: true);
        var service = new ConfigurationPhase14VerticalSliceService(
            fixture.ConfigurationOptions
        );
        ConfigurationPhase14PlanResult posixResult = await service.DryRunConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult backendOnlyResult = posixResult with
        {
            PlanResults = posixResult
                .PlanResults.Where(planResult =>
                    planResult.Plan.Changes.All(change =>
                        change.TargetKind != ConfigurationTargetKind.KeyringShim
                    )
                )
                .ToArray(),
        };

        string output = InvokePhase3ConfigurationOutput(backendOnlyResult);

        Assert.DoesNotContain("export PATH=", output, StringComparison.Ordinal);
        Assert.DoesNotContain("profile", output, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(backendOnlyResult.PlanResults);
        Assert.All(
            backendOnlyResult.PlanResults.SelectMany(result => result.Plan.Changes),
            change => Assert.NotEqual(ConfigurationTargetKind.KeyringShim, change.TargetKind)
        );
    }

    [Fact(
        Skip = "POSIX shell profile and PATH behavior is unsupported on Windows.",
        SkipWhen = nameof(IsWindows)
    )]
    public void HandleConfigure_OnPosixSuccess_DoesNotMutateShellProfileOrParentPath()
    {
        using var fixture = new Phase3ConfigureFixture(productHealthy: true);
        string? originalPath = Environment.GetEnvironmentVariable("PATH");

        CommandResult result = InvokeWithRuntime(fixture.Runtime, "configure", "python");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(originalPath, Environment.GetEnvironmentVariable("PATH"));
        Assert.False(File.Exists(Path.Combine(fixture.HomePath, ".profile")));
        Assert.False(File.Exists(Path.Combine(fixture.HomePath, ".bashrc")));
        Assert.False(File.Exists(Path.Combine(fixture.HomePath, ".zshrc")));
        Assert.Contains("export PATH=", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("shell profile", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(
        Skip = "POSIX protocol-wrapper integration is unsupported on Windows.",
        SkipWhen = nameof(IsWindows)
    )]
    public void Run_DoctorWithProtocolWrapper_RendersHealthyProductChecks()
    {
        using var pythonFixture = new PythonDoctorFixture(PythonDoctorFixtureMode.Healthy);
        string invocationLog = Path.Combine(pythonFixture.RootPath, "wrapper-invocations");
        string forbiddenInvocation = Path.Combine(pythonFixture.RootPath, "forbidden-invocation");
        string selectedWrapper = Path.Combine(
            Path.GetDirectoryName(pythonFixture.PythonExecutablePath)!,
            "selected-interpreter"
        );
        WritePhase3Executable(
            selectedWrapper,
            $$"""
            #!/bin/sh
            printf x >> {{Phase3ShellQuote(invocationLog)}}
            case "$2" in
              *"importlib.metadata.distribution"*)
                printf '%s\n' 'ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY'
                exit 0
                ;;
              *"find_spec('keyring')"*)
                printf '%s\n' 'ACP_KEYRING_PROBE_V1:FOUND'
                exit 0
                ;;
              *)
                exit 97
                ;;
            esac
            """
        );
        CliRuntimeOptions runtime = CreatePhase3ProductDoctorRuntime(
            pythonFixture,
            new SystemProcessRunner()
        );
        foreach (
            string forbiddenExecutable in new[]
            {
                pythonFixture.PythonExecutablePath,
                Path.Combine(Path.GetDirectoryName(selectedWrapper)!, "python3"),
                Path.Combine(Path.GetDirectoryName(selectedWrapper)!, "uv"),
                Path.Combine(Path.GetDirectoryName(selectedWrapper)!, "pip"),
                Path.Combine(Path.GetDirectoryName(selectedWrapper)!, "azureauth-keyring"),
                pythonFixture.Options.ExpectedKeyringShimPath!,
            }
        )
        {
            WritePhase3Executable(
                forbiddenExecutable,
                $$"""
                #!/bin/sh
                printf '%s\n' "$0" >> {{Phase3ShellQuote(forbiddenInvocation)}}
                exit 96
                """
            );
        }
        runtime = runtime with
        {
            PythonPhase11Options = runtime.PythonPhase11Options! with
            {
                PythonExecutablePath = selectedWrapper,
            },
        };

        Assert.Equal(0, InvokeWithRuntime(runtime, "configure", "git").ExitCode);
        CommandResult doctor = InvokeWithRuntime(runtime, "doctor");

        Assert.Equal(0, doctor.ExitCode);
        AssertDoctorCheck(doctor.StdOut, "python-keyring-module", "pass");
        AssertDoctorCheck(doctor.StdOut, "python-azureauth-keyring-backend", "pass");
        AssertDoctorCheck(doctor.StdOut, "python-azureauth-keyring-helper", "pass");
        AssertDoctorCheck(doctor.StdOut, "doctor-aggregation", "pass");
        Assert.Equal("xx", File.ReadAllText(invocationLog));
        Assert.False(File.Exists(forbiddenInvocation));
        Assert.DoesNotContain("/usr/bin/python", doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("python3", doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("uv ", doctor.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("pip install", doctor.StdOut, StringComparison.Ordinal);
    }

    private static CliRuntimeOptions CreatePhase3ProductDoctorRuntime(
        PythonDoctorFixture fixture,
        ProcessResult productResult
    )
    {
        var runner = new RecordingPythonResolutionProcessRunner();
        runner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        runner.EnqueueResult(productResult);
        return CreatePhase3ProductDoctorRuntime(fixture, runner);
    }

    private static CliRuntimeOptions CreatePhase3ProductDoctorRuntime(
        PythonDoctorFixture fixture,
        IProcessRunner runner
    )
    {
        string pythonDirectory = Path.GetDirectoryName(fixture.PythonExecutablePath)!;
        string helperPath = Path.Combine(
            pythonDirectory,
            OperatingSystem.IsWindows() ? "azureauth-keyring.exe" : "azureauth-keyring"
        );
        WritePhase3Executable(
            helperPath,
            OperatingSystem.IsWindows() ? string.Empty : "#!/bin/sh\nexit 89\n"
        );
        string shimDirectory = Path.GetDirectoryName(
            fixture.Options.ExpectedKeyringShimPath!
        )!;
        string modeledPath =
            shimDirectory + Path.PathSeparator + pythonDirectory;
        Func<string, string?> originalEnvironmentReader =
            fixture.Options.EnvironmentVariableReader!;
        PythonPhase11VerticalSliceOptions pythonOptions = fixture.Options with
        {
            ProcessRunner = runner,
            EnableProductProbe = true,
            EnvironmentVariableReader = name =>
                string.Equals(name, "PATH", StringComparison.Ordinal)
                    ? modeledPath
                    : originalEnvironmentReader(name),
        };
        return CreateHealthyDoctorRuntimeOptions(fixture) with
        {
            PythonPhase11Options = pythonOptions,
        };
    }

    private static PythonPhase11DoctorResult CreatePhase3DoctorResult(
        bool shimApplicable = true,
        bool helperApplicable = true,
        bool shimExists = true,
        bool shimFirstOnPath = true,
        bool backendLoadable = true,
        bool helperFound = true
    ) =>
        new()
        {
            KeyringShim = new PythonPhase11KeyringShimProbe
            {
                Applicable = shimApplicable,
                ExpectedShimPath = "/home/test/.local/share/azureauth/keyring",
                ExpectedShimDirectoryPath = "/home/test/.local/share/azureauth",
                ExpectedShimExists = shimExists,
                FirstKeyringExecutablePath =
                    shimFirstOnPath ? "/home/test/.local/share/azureauth/keyring" : null,
                AnyKeyringExecutableOnPath = shimFirstOnPath,
                ExpectedShimFirstOnPath = shimFirstOnPath,
                PathDirectories = ["/home/test/.local/share/azureauth"],
            },
            EnvironmentProbes = [],
            KeyringModuleProbe = new PythonPhase11KeyringModuleProbe
            {
                PythonExecutablePath = "/workspace/.venv/bin/python",
                PythonExecutableExists = true,
                Attempted = true,
                KeyringModuleResolvable = true,
                Status = PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            },
            ProductProbe = new PythonPhase11ProductProbe
            {
                PythonExecutablePath = "/workspace/.venv/bin/python",
                Attempted = true,
                BackendLoadable = backendLoadable,
                Status = backendLoadable
                    ? PythonPhase11ProductProbeStatus.Healthy
                    : PythonPhase11ProductProbeStatus.LoadFailure,
            },
            AzureAuthKeyringHelper = new PythonPhase11AzureAuthKeyringHelperProbe
            {
                Applicable = helperApplicable,
                ExpectedExecutablePath =
                    helperApplicable ? "/workspace/.venv/bin/azureauth-keyring" : null,
                ResolvedExecutablePath =
                    helperApplicable && helperFound
                        ? "/workspace/.venv/bin/azureauth-keyring"
                        : null,
                Status = !helperApplicable
                    ? PythonPhase11AzureAuthKeyringHelperProbeStatus.NotApplicable
                    : helperFound
                        ? PythonPhase11AzureAuthKeyringHelperProbeStatus.Found
                        : PythonPhase11AzureAuthKeyringHelperProbeStatus.Missing,
            },
            AzureArtifactsPythonEndpointCanonicalizationSuccess = true,
        };

    private static string[] InvokePhase3PythonDoctorLines(
        PythonPhase11DoctorResult result
    )
    {
        MethodInfo? method = typeof(CliApplication).GetMethod(
            "BuildPythonDoctorLines",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        return Assert
            .IsAssignableFrom<IEnumerable<string>>(method.Invoke(null, [result]))
            .ToArray();
    }

    private static bool InvokePhase3PythonDoctorSuccess(PythonPhase11DoctorResult result)
    {
        MethodInfo? method = typeof(CliApplication).GetMethod(
            "IsPythonDoctorSuccess",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, [result]));
    }

    private static string InvokePhase3ConfigurationOutput(
        ConfigurationPhase14PlanResult result
    )
    {
        MethodInfo? parseMethod = typeof(CliApplication).GetMethod(
            "Parse",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(parseMethod);
        object? invocation = parseMethod.Invoke(
            null,
            [new[] { "configure", "python" }]
        );
        Assert.NotNull(invocation);
        MethodInfo? outputMethod = typeof(CliApplication).GetMethod(
            "BuildConfigurationPhase14Output",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(outputMethod);
        return Assert.IsType<string>(outputMethod.Invoke(null, [invocation, result]));
    }

    private static string Phase3ShellQuote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string Phase3BootstrapCommand(string pythonPath, string installArguments) =>
        OperatingSystem.IsWindows()
            ? "& '"
                + pythonPath.Replace("'", "''", StringComparison.Ordinal)
                + "' -m pip install "
                + installArguments
            : Phase3ShellQuote(pythonPath) + " -m pip install " + installArguments;

    private static void WritePhase3Executable(string path, string contents)
    {
        WriteOwnerOnlyText(path, contents);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private sealed class Phase3ConfigureFixture : IDisposable
    {
        public Phase3ConfigureFixture(
            bool productHealthy,
            string homeDirectoryName = "home",
            IReadOnlyList<ProcessResult>? processResults = null,
            bool helperPresent = true,
            bool helperShadowed = false,
            bool useExplicitPythonExecutable = true
        )
        {
            RootPath = CreateTestDirectory();
            HomePath = Path.Combine(RootPath, homeDirectoryName);
            Directory.CreateDirectory(HomePath);
            string pythonDirectory = Path.Combine(RootPath, "selected environment", "bin");
            Directory.CreateDirectory(pythonDirectory);
            PythonExecutablePath = Path.Combine(pythonDirectory, "python");
            WritePhase3Executable(PythonExecutablePath, "#!/bin/sh\nexit 91\n");
            string helperPath = Path.Combine(pythonDirectory, "azureauth-keyring");
            ExpectedHelperPath = helperPath;
            if (helperPresent)
            {
                WritePhase3Executable(helperPath, "#!/bin/sh\nexit 92\n");
            }
            string shadowingDirectory = Path.Combine(RootPath, "shadowing environment", "bin");
            ShadowingHelperPath = Path.Combine(shadowingDirectory, "azureauth-keyring");
            if (helperShadowed)
            {
                Directory.CreateDirectory(shadowingDirectory);
                WritePhase3Executable(ShadowingHelperPath, "#!/bin/sh\nexit 93\n");
            }
            Runner = new RecordingPythonResolutionProcessRunner();
            IReadOnlyList<ProcessResult> effectiveProcessResults =
                processResults
                ??
                [
                    new ProcessResult(0, KeyringModuleFoundOutput, string.Empty),
                    productHealthy
                        ? new ProcessResult(
                            0,
                            "ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY\n",
                            string.Empty
                        )
                        : new ProcessResult(
                            30,
                            "ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING\n",
                            string.Empty
                        ),
                ];
            foreach (ProcessResult processResult in effectiveProcessResults)
            {
                Runner.EnqueueResult(processResult);
            }
            var pythonDoctor = new PythonPhase11VerticalSliceService(
                new PythonPhase11VerticalSliceOptions
                {
                    FileSystem = new SystemFileSystem(),
                    ProcessRunner = Runner,
                    EnvironmentVariableReader = name =>
                        string.Equals(name, "PATH", StringComparison.Ordinal)
                            ? helperShadowed
                                ? shadowingDirectory
                                    + Path.PathSeparator
                                    + pythonDirectory
                                : pythonDirectory
                            : null,
                    ExpectedKeyringShimPath = Path.Combine(
                        RootPath,
                        "not-yet-generated",
                        "keyring"
                    ),
                    PythonExecutablePath =
                        useExplicitPythonExecutable ? PythonExecutablePath : null,
                    CurrentDirectoryPath = RootPath,
                    EnableProductProbe = true,
                }
            );
            ConfigurationPhase14VerticalSliceOptions baseConfigurationOptions =
                CreateConfigurationPhase14Options(RootPath);
            ConfigurationOptions = baseConfigurationOptions with
            {
                StateDirectoryPath = Path.Combine(RootPath, "configuration-state"),
                EnvironmentVariableReader = name =>
                    string.Equals(name, "HOME", StringComparison.Ordinal)
                        ? HomePath
                        : baseConfigurationOptions.EnvironmentVariableReader!(name),
                PythonDoctorService = pythonDoctor,
            };
            Runtime = new CliRuntimeOptions
            {
                CompositionRoot = CreateTestCompositionRoot(),
                ConfigurationPhase14Options = ConfigurationOptions,
            };
        }

        public ConfigurationPhase14VerticalSliceOptions ConfigurationOptions { get; }

        public string HomePath { get; }

        public string ExpectedHelperPath { get; }

        public string PythonExecutablePath { get; }

        public RecordingPythonResolutionProcessRunner Runner { get; }

        public string RootPath { get; }

        public string ShadowingHelperPath { get; }

        public CliRuntimeOptions Runtime { get; }

        public void Dispose() => DeleteDirectoryIfExists(RootPath);

        public string[] GetFileSystemEntries() =>
            Directory
                .EnumerateFileSystemEntries(
                    RootPath,
                    "*",
                    SearchOption.AllDirectories
                )
                .Order(StringComparer.Ordinal)
                .ToArray();
    }

    [Fact]
    public void HandleConfigure_WithRegistryUrlAndFailingProductPreflight_DoesNotWrite()
    {
        string rootPath = CreateTestDirectory();
        try
        {
            string homePath = Path.Combine(rootPath, "home");
            string pythonDirectory = Path.Combine(rootPath, "selected-environment", "bin");
            Directory.CreateDirectory(homePath);
            Directory.CreateDirectory(pythonDirectory);
            string pythonPath = Path.Combine(pythonDirectory, "python");
            string helperPath = Path.Combine(pythonDirectory, "azureauth-keyring");
            WritePhase3Executable(pythonPath, "#!/bin/sh\nexit 91\n");
            WritePhase3Executable(helperPath, "#!/bin/sh\nexit 92\n");
            var runner = new RecordingPythonResolutionProcessRunner();
            runner.EnqueueResult(
                new ProcessResult(0, KeyringModuleFoundOutput, string.Empty)
            );
            runner.EnqueueResult(
                new ProcessResult(
                    30,
                    "ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING\n",
                    string.Empty
                )
            );
            ConfigurationPhase14VerticalSliceOptions baseConfigurationOptions =
                CreateConfigurationPhase14Options(rootPath);
            ConfigurationPhase14VerticalSliceOptions configurationOptions =
                baseConfigurationOptions with
                {
                    StateDirectoryPath = Path.Combine(rootPath, "configuration-state"),
                    EnvironmentVariableReader = name =>
                        string.Equals(name, "HOME", StringComparison.Ordinal)
                            ? homePath
                            : baseConfigurationOptions.EnvironmentVariableReader!(name),
                    RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                    {
                        [CredentialEcosystem.Npm] = new Uri(
                            TestRegistryUrl,
                            UriKind.Absolute
                        ),
                    },
                    PythonDoctorService = null,
                };
            var runtime = new CliRuntimeOptions
            {
                CompositionRoot = CreateTestCompositionRoot(),
                ConfigurationPhase14Options = configurationOptions,
                PythonPhase11Options = new PythonPhase11VerticalSliceOptions
                {
                    FileSystem = new SystemFileSystem(),
                    ProcessRunner = runner,
                    EnvironmentVariableReader = name =>
                        string.Equals(name, "PATH", StringComparison.Ordinal)
                            ? pythonDirectory
                            : null,
                    ExpectedKeyringShimPath = Path.Combine(
                        rootPath,
                        "not-yet-generated",
                        "keyring"
                    ),
                    PythonExecutablePath = pythonPath,
                    CurrentDirectoryPath = rootPath,
                    EnableProductProbe = true,
                },
            };
            string[] before = Directory
                .EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();

            CommandResult result = InvokeWithRuntime(
                runtime,
                "configure",
                "python"
            );

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.Contains(
                "python preflight failed",
                result.StdErr,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(2, runner.StartSpecs.Count);
            Assert.All(runner.StartSpecs, spec => Assert.Equal(pythonPath, spec.FileName));
            Assert.DoesNotContain("planned-change", result.StdErr, StringComparison.Ordinal);
            Assert.Equal(
                before,
                Directory
                    .EnumerateFileSystemEntries(
                        rootPath,
                        "*",
                        SearchOption.AllDirectories
                    )
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            );
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void HandleDoctor_WhenProductNotAttempted_RendersExplicitNotApplicableRows()
    {
        PythonPhase11DoctorResult baseline = CreatePhase3DoctorResult();
        PythonPhase11DoctorResult result = baseline with
        {
            KeyringModuleProbe = baseline.KeyringModuleProbe with
            {
                KeyringModuleResolvable = false,
                Status = PythonPhase11KeyringModuleProbeStatus.ModuleNotFound,
            },
            ProductProbe = baseline.ProductProbe with
            {
                Enabled = true,
                Attempted = false,
                BackendLoadable = false,
                Status = PythonPhase11ProductProbeStatus.NotAttempted,
            },
        };

        string[] lines = InvokePhase3PythonDoctorLines(result);

        Assert.Contains("python-azureauth-keyring-backend: N/A", lines);
        Assert.Contains("python-azureauth-keyring-backend-probe: N/A", lines);
        Assert.Contains("python-azureauth-keyring-helper: N/A", lines);
        Assert.Contains("python-azureauth-keyring-helper-expected: N/A", lines);
        Assert.Contains("python-azureauth-keyring-helper-resolved: N/A", lines);
    }

    [Fact]
    public void HandleDoctor_RendersDistinctExpectedAndResolvedHelperPaths()
    {
        const string expectedPath = "/selected environment/bin/azureauth-keyring";
        const string resolvedPath = "/terminal/bin/azureauth-keyring";
        PythonPhase11DoctorResult baseline = CreatePhase3DoctorResult();
        PythonPhase11DoctorResult result = baseline with
        {
            AzureAuthKeyringHelper = baseline.AzureAuthKeyringHelper with
            {
                ExpectedExecutablePath = expectedPath,
                ResolvedExecutablePath = resolvedPath,
            },
        };

        string[] lines = InvokePhase3PythonDoctorLines(result);

        Assert.Contains(
            "python-azureauth-keyring-helper-expected: " + expectedPath,
            lines
        );
        Assert.Contains(
            "python-azureauth-keyring-helper-resolved: " + resolvedPath,
            lines
        );
        Assert.NotEqual(expectedPath, resolvedPath);
    }

    [Theory]
    [InlineData(PythonPhase11ProductProbeStatus.NotAttempted, false, "N/A")]
    [InlineData(PythonPhase11ProductProbeStatus.Healthy, true, "healthy")]
    [InlineData(PythonPhase11ProductProbeStatus.DistributionMissing, true, "distribution-missing")]
    [InlineData(PythonPhase11ProductProbeStatus.EntryPointMissing, true, "entry-point-missing")]
    [InlineData(PythonPhase11ProductProbeStatus.EntryPointMismatch, true, "entry-point-mismatch")]
    [InlineData(PythonPhase11ProductProbeStatus.LoadFailure, true, "load-failed")]
    [InlineData(PythonPhase11ProductProbeStatus.LaunchFailure, true, "launch-failed")]
    [InlineData(PythonPhase11ProductProbeStatus.TimedOut, true, "timed-out")]
    [InlineData(
        PythonPhase11ProductProbeStatus.UnexpectedNonZeroExit,
        true,
        "unexpected-nonzero-exit"
    )]
    [InlineData(PythonPhase11ProductProbeStatus.OutputTooLarge, true, "output-too-large")]
    [InlineData(PythonPhase11ProductProbeStatus.InvalidOutput, true, "invalid-output")]
    public void HandleDoctor_ProductProbeStatusTextMappings(
        PythonPhase11ProductProbeStatus status,
        bool attempted,
        string expectedText
    )
    {
        PythonPhase11DoctorResult baseline = CreatePhase3DoctorResult();
        PythonPhase11DoctorResult result = baseline with
        {
            ProductProbe = baseline.ProductProbe with
            {
                Enabled = attempted || status == PythonPhase11ProductProbeStatus.NotAttempted,
                Attempted = attempted,
                BackendLoadable = status == PythonPhase11ProductProbeStatus.Healthy,
                Status = status,
            },
        };

        string[] lines = InvokePhase3PythonDoctorLines(result);

        Assert.Contains(
            "python-azureauth-keyring-backend-probe: " + expectedText,
            lines
        );
        Assert.Contains(
            "python-azureauth-keyring-backend: "
                + (
                    !attempted
                        ? "N/A"
                        : status == PythonPhase11ProductProbeStatus.Healthy
                            ? "pass"
                            : "fail"
                ),
            lines
        );
    }
#pragma warning restore CA1707, CA1861

}
