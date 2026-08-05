using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

namespace Hcoona.AzureAuth.CredProvider.Cli;

internal static class CliApplication
{
    private const string CommandName = "azureauth-credprovider";
    private const string PhaseName = "15-end-to-end-hardening";
    private const int SuccessExitCode = 0;
    private const int NotImplementedExitCode = 1;
    private const int UsageExitCode = 2;
    private const int CanceledExitCode = 130;
    private const int FatalExitCode = 70;
    private const string FatalErrorMessage = "error: unexpected fatal failure.";
    private const string GitCredentialHelperConfigurationKey = "credential.helper";
    private const string GitUseHttpPathConfigurationKey =
        "credential.https://dev.azure.com.useHttpPath";
    private const string NuGetPluginLayoutConfigurationKey = "physical-target";

    private static readonly string[] SupportedEcosystems =
    [
        "git",
        "nuget",
        "python",
        "npm",
        "pnpm",
        "yarn",
    ];
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
        return Run(args, stdout, stderr, runtimeOptions: null);
    }

    internal static int Run(
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        return Run(args, stdout, stderr, runtimeOptions, Console.In, CommandName);
    }

    internal static int Run(
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions,
        TextReader stdin,
        string? executablePath
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(stdin);

        SecretRedactor redactor = CreateRedactor(args);
        try
        {
            if (args.Count == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
            {
                WriteText(
                    stdout,
                    $"{CommandName} {typeof(CliApplication).Assembly.GetName().Version}"
                );
                return SuccessExitCode;
            }

            if (
                GitCredentialHelperAdapter.TryResolveProtocolInvocation(
                    executablePath ?? CommandName,
                    args,
                    out _
                )
            )
            {
                runtimeOptions = EnsureCompositionRootFactory(runtimeOptions, stderr, redactor);
                return HandleGitCredentialHelperProtocol(
                    args,
                    stdin,
                    stdout,
                    stderr,
                    redactor,
                    executablePath ?? CommandName,
                    runtimeOptions
                );
            }

            CliInvocation invocation = Parse(args);
            if (invocation.HelpText is not null)
            {
                WriteText(stdout, invocation.HelpText);
                return SuccessExitCode;
            }

            runtimeOptions = EnsureCompositionRootFactory(runtimeOptions, stderr, redactor);
            return invocation.Command switch
            {
                CliCommand.Status => HandleStatus(invocation, stdout, runtimeOptions),
                CliCommand.Configure => HandleConfigure(invocation, stdout, stderr, runtimeOptions),
                CliCommand.Refresh => HandleRefresh(invocation, stdout, stderr, runtimeOptions),
                CliCommand.Unconfigure => HandleUnconfigure(
                    invocation,
                    stdout,
                    stderr,
                    runtimeOptions
                ),
                CliCommand.Doctor => HandleDoctor(invocation, stdout, runtimeOptions),
                CliCommand.Cleanup => HandleCleanup(invocation, stdout, stderr, runtimeOptions),
                CliCommand.Acceptance => HandleAcceptance(invocation, stdout),
                CliCommand.Login => HandleLogin(invocation, stdout, stderr, runtimeOptions),
                CliCommand.Logout => HandleLogout(invocation, stdout, stderr, runtimeOptions),
                CliCommand.Identity => HandleIdentity(invocation, stdout, stderr, runtimeOptions),
                _ => throw new InvalidOperationException("Unsupported CLI command."),
            };
        }
        catch (CliUsageException ex)
        {
            TryWriteDiagnosticText(stderr, ex.Message);
            return ex.ExitCode;
        }
        catch (CredentialProviderConfigurationUnavailableException)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: credential provider configuration is unavailable."
            );
            return FatalExitCode;
        }
        catch (OperationCanceledException)
        {
            TryWriteDiagnosticText(stderr, "error: operation canceled.");
            return CanceledExitCode;
        }
        catch (Exception)
        {
            WriteFatalError(stderr, redactor);
            return FatalExitCode;
        }
    }

    private static CliRuntimeOptions EnsureCompositionRootFactory(
        CliRuntimeOptions? runtimeOptions,
        TextWriter stderr,
        SecretRedactor redactor
    )
    {
        if (
            runtimeOptions?.CompositionRoot is not null
            || runtimeOptions?.CompositionRootFactory is not null
        )
        {
            return runtimeOptions;
        }

        CredentialCoreService? explicitTestCore =
            runtimeOptions?.AuthPhase14Options?.CredentialCoreService
            ?? runtimeOptions?.ConfigurationPhase14Options?.CredentialCoreService;
        var root = new Lazy<CredentialProviderCompositionRoot>(
            () =>
                explicitTestCore is not null
                    ? CredentialProviderCompositionRoot.CreateExplicitTestScaffold(explicitTestCore)
                    : CredentialProviderCompositionRoot.CreateProduction(
                        new CredentialProviderProductionOptions
                        {
                            Diagnostics = new DiagnosticRouter(
                                [new TextWriterDiagnosticSink(stderr)],
                                redactor
                            ),
                            DeviceCodePromptWriter = stderr,
                        }
                    ),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        return (runtimeOptions ?? new CliRuntimeOptions()) with
        {
            CompositionRootFactory = () => root.Value,
        };
    }

    private sealed class CredentialProviderConfigurationUnavailableException(
        Exception innerException
    ) : Exception("Credential provider configuration is unavailable.", innerException);

    private static CredentialProviderCompositionRoot GetCompositionRoot(
        CliRuntimeOptions? runtimeOptions
    )
    {
        try
        {
            return runtimeOptions?.CompositionRoot
                ?? runtimeOptions?.CompositionRootFactory?.Invoke()
                ?? CredentialProviderCompositionRoot.CreateProduction();
        }
        catch (Exception exception)
        {
            throw new CredentialProviderConfigurationUnavailableException(exception);
        }
    }

    private static int HandleGitCredentialHelperProtocol(
        IReadOnlyList<string> args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        SecretRedactor redactor,
        string executablePath,
        CliRuntimeOptions? runtimeOptions
    )
    {
        var diagnosticRouter = new DiagnosticRouter(
            [new TextWriterDiagnosticSink(stderr)],
            redactor
        );
        CredentialProviderCompositionRoot root = GetCompositionRoot(runtimeOptions);
        AdapterHostExecutionOutcome outcome = root.CreateGitCredentialHelperAdapter()
            .Execute(
                executablePath,
                args,
                stdin,
                stdout,
                TextWriter.Null,
                diagnosticRouter,
                GetCancellationToken(runtimeOptions)
            );
        return (int)outcome.Result.ExitCode;
    }

    private static int HandleStatus(
        CliInvocation invocation,
        TextWriter stdout,
        CliRuntimeOptions? runtimeOptions
    )
    {
        CredentialProviderCompositionRoot root = GetCompositionRoot(runtimeOptions);
        CancellationToken cancellationToken = GetCancellationToken(runtimeOptions);
        CredentialProviderReadiness readiness = root.GetReadiness(cancellationToken);
        ConfigurationPhase14DoctorResult configuration =
            CreateConfigurationPhase14VerticalSliceService(
                    runtimeOptions,
                    requireCredentialProvider: false
                )
                .DoctorAsync(cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        WriteText(
            stdout,
            BuildStatusOutput(invocation.CiMode, root, readiness)
                + BuildConfigurationLifecycleStatusOutput(configuration)
        );
        return SuccessExitCode;
    }

    private static string BuildConfigurationLifecycleStatusOutput(
        ConfigurationPhase14DoctorResult doctor
    ) =>
        string.Concat(
            doctor
                .Ecosystems.Where(static result => IsPackageRegistryEcosystem(result.Ecosystem))
                .OrderBy(static result => result.Ecosystem)
                .ThenBy(static result => result.Scope)
                .Select(result =>
                    $"{GetConfigurationPhase14DoctorPrefix(result)}-lifecycle: "
                    + $"{GetLifecycleStateText(result.LifecycleState)}\n"
                )
        );

    private static int HandleConfigure(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        CredentialEcosystem ecosystem =
            invocation.Ecosystem
            ?? throw new InvalidOperationException("Configure requires an ecosystem.");

        if (invocation.DryRun)
        {
            if (ecosystem == CredentialEcosystem.Git && invocation.CiMode == CliCiMode.None)
            {
                GitPhase8ConfigureDryRunResult dryRunResult;
                try
                {
                    dryRunResult = CreateGitPhase8VerticalSliceService(runtimeOptions)
                        .DryRunConfigureAsync(GetCancellationToken(runtimeOptions))
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (GitPhase8UnrecognizedStateException)
                {
                    TryWriteDiagnosticText(
                        stderr,
                        "error: configure cannot modify unrecognized Phase 8 Git state."
                    );
                    return NotImplementedExitCode;
                }

                WriteText(stdout, BuildGitConfigureDryRunOutput(invocation, dryRunResult));
                return dryRunResult.Validation.IsValid ? SuccessExitCode : NotImplementedExitCode;
            }

            if (ecosystem == CredentialEcosystem.NuGet && invocation.CiMode == CliCiMode.None)
            {
                NuGetPhase10ConfigureDryRunResult dryRunResult;
                try
                {
                    dryRunResult = CreateNuGetPhase10VerticalSliceService(runtimeOptions)
                        .DryRunConfigureAsync(GetCancellationToken(runtimeOptions))
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (NuGetPhase10UnrecognizedStateException)
                {
                    TryWriteDiagnosticText(
                        stderr,
                        "error: configure cannot modify unrecognized Phase 10 NuGet state."
                    );
                    return NotImplementedExitCode;
                }

                WriteText(stdout, BuildNuGetConfigureDryRunOutput(invocation, dryRunResult));
                return dryRunResult.Validation.IsValid ? SuccessExitCode : NotImplementedExitCode;
            }

            if (IsPhase14ConfigurationEcosystem(ecosystem))
            {
                ConfigurationPhase14PlanResult dryRunResult;
                try
                {
                    dryRunResult = CreateConfigurationPhase14VerticalSliceService(
                            runtimeOptions,
                            ecosystem,
                            invocation.RegistryUrl,
                            requireCredentialProvider: false
                        )
                        .DryRunConfigureAsync(
                            ecosystem,
                            GetConfigurationPhase14Scope(invocation.CiMode),
                            GetCancellationToken(runtimeOptions)
                        )
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception)
                    when (exception
                            is InvalidOperationException
                                or NotSupportedException
                                or ArgumentException
                    )
                {
                    TryWriteDiagnosticText(stderr, "error: " + exception.Message);
                    return NotImplementedExitCode;
                }

                WriteText(stdout, BuildConfigurationPhase14DryRunOutput(invocation, dryRunResult));
                return SuccessExitCode;
            }

            WriteText(stdout, BuildDryRunOutput(invocation));
            return SuccessExitCode;
        }

        if (ecosystem == CredentialEcosystem.Git && invocation.CiMode == CliCiMode.None)
        {
            GitPhase8ConfigureResult configureResult;
            try
            {
                configureResult = CreateGitPhase8VerticalSliceService(runtimeOptions)
                    .ConfigureAsync(GetCancellationToken(runtimeOptions))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (GitPhase8UnrecognizedStateException)
            {
                TryWriteDiagnosticText(
                    stderr,
                    "error: configure cannot modify unrecognized Phase 8 Git state."
                );
                return NotImplementedExitCode;
            }

            WriteText(stdout, BuildGitConfigureOutput(invocation, configureResult));
            return SuccessExitCode;
        }

        if (ecosystem == CredentialEcosystem.NuGet && invocation.CiMode == CliCiMode.None)
        {
            NuGetPhase10ConfigureResult configureResult;
            try
            {
                configureResult = CreateNuGetPhase10VerticalSliceService(runtimeOptions)
                    .ConfigureAsync(GetCancellationToken(runtimeOptions))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (NuGetPhase10UnrecognizedStateException)
            {
                TryWriteDiagnosticText(
                    stderr,
                    "error: configure cannot modify unrecognized Phase 10 NuGet state."
                );
                return NotImplementedExitCode;
            }

            WriteText(stdout, BuildNuGetConfigureOutput(invocation, configureResult));
            return SuccessExitCode;
        }

        if (IsPhase14ConfigurationEcosystem(ecosystem))
        {
            ConfigurationPhase14PlanResult configureResult;
            try
            {
                configureResult = CreateConfigurationPhase14VerticalSliceService(
                        runtimeOptions,
                        ecosystem,
                        invocation.RegistryUrl,
                        requireCredentialProvider: RequiresCredentialProviderForConfigure(
                            ecosystem,
                            invocation.CiMode
                        )
                    )
                    .ConfigureAsync(
                        ecosystem,
                        GetConfigurationPhase14Scope(invocation.CiMode),
                        GetCancellationToken(runtimeOptions)
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or NotSupportedException
                            or ArgumentException
                )
            {
                TryWriteDiagnosticText(stderr, "error: " + exception.Message);
                return NotImplementedExitCode;
            }

            WriteText(stdout, BuildConfigurationPhase14Output(invocation, configureResult));
            return SuccessExitCode;
        }

        if (ecosystem != CredentialEcosystem.Git || invocation.CiMode != CliCiMode.None)
        {
            TryWriteDiagnosticText(
                stderr,
                $"error: configure without '--dry-run' is not implemented in {PhaseName}."
            );
            return NotImplementedExitCode;
        }

        throw new InvalidOperationException("Unsupported configure command.");
    }

    private static int HandleRefresh(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        CredentialEcosystem ecosystem =
            invocation.Ecosystem
            ?? throw new InvalidOperationException("Refresh requires an ecosystem.");
        if (!IsPackageRegistryEcosystem(ecosystem))
        {
            TryWriteDiagnosticText(stderr, "error: refresh supports only npm, pnpm, and yarn.");
            return UsageExitCode;
        }

        try
        {
            Uri registryUrl =
                invocation.RegistryUrl
                ?? CreateConfigurationPhase14VerticalSliceService(
                        runtimeOptions,
                        requireCredentialProvider: false
                    )
                    .ResolvePersistedRegistryUrl(
                        ecosystem,
                        GetConfigurationPhase14Scope(invocation.CiMode)
                    );
            ConfigurationPhase14VerticalSliceService service =
                CreateConfigurationPhase14VerticalSliceService(
                    runtimeOptions,
                    ecosystem,
                    registryUrl,
                    requireCredentialProvider: !invocation.DryRun
                        && invocation.CiMode != CliCiMode.AzurePipelines
                );
            ConfigurationPhase14PlanResult result;
            if (invocation.DryRun)
            {
                result = service
                    .DryRunRefreshAsync(
                        ecosystem,
                        GetConfigurationPhase14Scope(invocation.CiMode),
                        GetCancellationToken(runtimeOptions)
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            else
            {
                result = service
                    .RefreshAsync(
                        ecosystem,
                        GetConfigurationPhase14Scope(invocation.CiMode),
                        GetCancellationToken(runtimeOptions)
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            WriteText(
                stdout,
                invocation.DryRun
                    ? BuildConfigurationPhase14DryRunOutput(invocation, result)
                    : BuildConfigurationPhase14Output(invocation, result)
            );
            return SuccessExitCode;
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or NotSupportedException
                        or ArgumentException
                        or UnauthorizedAccessException
            )
        {
            TryWriteDiagnosticText(stderr, "error: " + exception.Message);
            return NotImplementedExitCode;
        }
    }

    private static int HandleUnconfigure(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        CredentialEcosystem ecosystem =
            invocation.Ecosystem
            ?? throw new InvalidOperationException("Unconfigure requires an ecosystem.");

        if (invocation.DryRun)
        {
            if (ecosystem == CredentialEcosystem.Git && invocation.CiMode == CliCiMode.None)
            {
                try
                {
                    CreateGitPhase8ConfigurationService(runtimeOptions)
                        .ValidateUnconfigureDryRunAsync(GetCancellationToken(runtimeOptions))
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (GitPhase8UnrecognizedStateException)
                {
                    TryWriteDiagnosticText(
                        stderr,
                        "error: unconfigure cannot modify unrecognized Phase 8 Git state."
                    );
                    return NotImplementedExitCode;
                }
            }
            else if (ecosystem == CredentialEcosystem.NuGet && invocation.CiMode == CliCiMode.None)
            {
                try
                {
                    CreateNuGetPhase10ConfigurationService(runtimeOptions)
                        .ValidateUnconfigureDryRunAsync(GetCancellationToken(runtimeOptions))
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (NuGetPhase10UnrecognizedStateException)
                {
                    TryWriteDiagnosticText(
                        stderr,
                        "error: unconfigure cannot modify unrecognized Phase 10 NuGet state."
                    );
                    return NotImplementedExitCode;
                }
            }
            else if (IsPhase14ConfigurationEcosystem(ecosystem))
            {
                ConfigurationPhase14PlanResult dryRunResult;
                try
                {
                    dryRunResult = CreateConfigurationPhase14VerticalSliceService(
                            runtimeOptions,
                            requireCredentialProvider: false
                        )
                        .DryRunUnconfigureAsync(
                            ecosystem,
                            GetConfigurationPhase14Scope(invocation.CiMode),
                            GetCancellationToken(runtimeOptions)
                        )
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception)
                    when (exception
                            is InvalidOperationException
                                or NotSupportedException
                                or ArgumentException
                    )
                {
                    TryWriteDiagnosticText(stderr, "error: " + exception.Message);
                    return NotImplementedExitCode;
                }

                WriteText(stdout, BuildConfigurationPhase14DryRunOutput(invocation, dryRunResult));
                if (dryRunResult.OwnershipManifestCleanupIncomplete)
                {
                    WriteIncompleteCredentialCleanupDiagnostic(
                        stderr,
                        GetConfigurationPhase14Scope(invocation.CiMode)
                    );
                    return NotImplementedExitCode;
                }
                return SuccessExitCode;
            }

            WriteText(stdout, BuildDryRunOutput(invocation));
            return SuccessExitCode;
        }

        if (ecosystem == CredentialEcosystem.Git && invocation.CiMode == CliCiMode.None)
        {
            GitPhase8UnconfigureResult unconfigureResult;
            try
            {
                unconfigureResult = CreateGitPhase8ConfigurationService(runtimeOptions)
                    .UnconfigureAsync(GetCancellationToken(runtimeOptions))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (GitPhase8UnrecognizedStateException)
            {
                TryWriteDiagnosticText(
                    stderr,
                    "error: unconfigure cannot modify unrecognized Phase 8 Git state."
                );
                return NotImplementedExitCode;
            }

            WriteText(stdout, BuildGitUnconfigureOutput(invocation, unconfigureResult));
            return SuccessExitCode;
        }

        if (ecosystem == CredentialEcosystem.NuGet && invocation.CiMode == CliCiMode.None)
        {
            NuGetPhase10UnconfigureResult unconfigureResult;
            try
            {
                unconfigureResult = CreateNuGetPhase10ConfigurationService(runtimeOptions)
                    .UnconfigureAsync(GetCancellationToken(runtimeOptions))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (NuGetPhase10UnrecognizedStateException)
            {
                TryWriteDiagnosticText(
                    stderr,
                    "error: unconfigure cannot modify unrecognized Phase 10 NuGet state."
                );
                return NotImplementedExitCode;
            }

            WriteText(stdout, BuildNuGetUnconfigureOutput(invocation, unconfigureResult));
            return SuccessExitCode;
        }

        if (IsPhase14ConfigurationEcosystem(ecosystem))
        {
            ConfigurationPhase14PlanResult unconfigureResult;
            try
            {
                unconfigureResult = CreateConfigurationPhase14VerticalSliceService(
                        runtimeOptions,
                        requireCredentialProvider: false
                    )
                    .UnconfigureAsync(
                        ecosystem,
                        GetConfigurationPhase14Scope(invocation.CiMode),
                        GetCancellationToken(runtimeOptions)
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or NotSupportedException
                            or ArgumentException
                )
            {
                TryWriteDiagnosticText(stderr, "error: " + exception.Message);
                return NotImplementedExitCode;
            }

            WriteText(stdout, BuildConfigurationPhase14Output(invocation, unconfigureResult));
            if (unconfigureResult.OwnershipManifestCleanupIncomplete)
            {
                WriteIncompleteCredentialCleanupDiagnostic(
                    stderr,
                    GetConfigurationPhase14Scope(invocation.CiMode)
                );
                return NotImplementedExitCode;
            }

            return SuccessExitCode;
        }

        if (ecosystem != CredentialEcosystem.Git || invocation.CiMode != CliCiMode.None)
        {
            TryWriteDiagnosticText(
                stderr,
                $"error: unconfigure without '--dry-run' is not implemented in {PhaseName}."
            );
            return NotImplementedExitCode;
        }

        throw new InvalidOperationException("Unsupported unconfigure command.");
    }

    private static int HandleDoctor(
        CliInvocation invocation,
        TextWriter stdout,
        CliRuntimeOptions? runtimeOptions
    )
    {
        CredentialProviderCompositionRoot root = GetCompositionRoot(runtimeOptions);
        CancellationToken cancellationToken = GetCancellationToken(runtimeOptions);
        CredentialProviderReadiness readiness = root.GetReadiness(cancellationToken);
        GitPhase8DoctorResult doctorResult = CreateGitPhase8VerticalSliceService(runtimeOptions)
            .DoctorAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        NuGetPhase10DoctorResult nuGetDoctorResult = CreateNuGetPhase10VerticalSliceService(
                runtimeOptions
            )
            .DoctorAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        PythonPhase11DoctorResult pythonDoctorResult = CreatePythonPhase11VerticalSliceService(
                runtimeOptions
            )
            .RunDoctorAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        ConfigurationPhase14DoctorResult configurationDoctorResult =
            CreateConfigurationPhase14VerticalSliceService(
                    runtimeOptions,
                    requireCredentialProvider: false
                )
                .DoctorAsync(cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        bool doctorSuccess =
            IsGitDoctorSuccess(doctorResult)
            && IsNuGetDoctorSuccess(nuGetDoctorResult)
            && IsPythonDoctorSuccess(pythonDoctorResult)
            && IsConfigurationPhase14DoctorSuccess(configurationDoctorResult)
            && readiness.IsReady;
        WriteText(
            stdout,
            BuildDoctorOutput(
                invocation,
                doctorResult,
                nuGetDoctorResult,
                pythonDoctorResult,
                configurationDoctorResult,
                root,
                readiness,
                doctorSuccess
            )
        );
        return doctorSuccess ? SuccessExitCode : NotImplementedExitCode;
    }

    private static int HandleAcceptance(CliInvocation invocation, TextWriter stdout)
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();
        WriteText(stdout, BuildAcceptanceOutput(invocation, result));
        return result.MvpLocalAcceptancePassed && !result.BlockingFailuresPresent
            ? SuccessExitCode
            : NotImplementedExitCode;
    }

    private static int HandleCleanup(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        ConfigurationPhase14CleanupResult cleanupResult;
        try
        {
            ConfigurationPhase14VerticalSliceService service =
                CreateConfigurationPhase14VerticalSliceService(
                    runtimeOptions,
                    requireCredentialProvider: false
                );
            ValueTask<ConfigurationPhase14CleanupResult> cleanup = invocation.DryRun
                ? service.DryRunCleanupAsync(
                    invocation.Ecosystem,
                    GetConfigurationPhase14Scope(invocation.CiMode),
                    GetCancellationToken(runtimeOptions)
                )
                : service.CleanupAsync(
                    invocation.Ecosystem,
                    GetConfigurationPhase14Scope(invocation.CiMode),
                    GetCancellationToken(runtimeOptions)
                );
            cleanupResult = cleanup.AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            TryWriteDiagnosticText(stderr, "error: " + exception.Message);
            return NotImplementedExitCode;
        }

        WriteText(stdout, BuildCleanupOutput(invocation, cleanupResult, invocation.DryRun));
        bool cleanupSucceeded = invocation.DryRun
            ? IsConfigurationPhase14CleanupPlanComplete(cleanupResult)
            : IsConfigurationPhase14CleanupSuccess(cleanupResult);
        if (!cleanupSucceeded)
        {
            WriteIncompleteCleanupDiagnostic(stderr, cleanupResult.Scope);
            return NotImplementedExitCode;
        }

        return SuccessExitCode;
    }

    private static int HandleLogin(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        if (invocation.AuthOptions.DeferredFlowName is { } deferredFlowName)
        {
            TryWriteDiagnosticText(
                stderr,
                $"error: identity flow '{deferredFlowName}' is deferred for MVP."
            );
            return NotImplementedExitCode;
        }

        AuthPhase14LoginResult loginResult;
        CancellationToken cancellationToken = GetCancellationToken(runtimeOptions);
        try
        {
            loginResult = CreateAuthPhase14VerticalSliceService(
                    runtimeOptions,
                    requireCredentialProvider: invocation.AuthOptions.IdentityFlow
                        != IdentityFlow.AzurePipelinesSystemAccessToken
                )
                .Login(
                    new AuthPhase14LoginRequest
                    {
                        IdentityFlow = invocation.AuthOptions.IdentityFlow,
                        AccountHint = invocation.AuthOptions.AccountHint,
                        TenantHint = invocation.AuthOptions.TenantHint,
                        ExplicitPatMaterialProvided = invocation
                            .AuthOptions
                            .ExplicitPatMaterialProvided,
                        ExplicitAzurePipelinesCiMode =
                            invocation.CiMode == CliCiMode.AzurePipelines,
                    },
                    cancellationToken
                );
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or NotSupportedException)
        {
            TryWriteDiagnosticText(stderr, "error: " + exception.Message);
            return NotImplementedExitCode;
        }

        if (ShouldTreatLoginResultAsCanceled(loginResult.CredentialResult, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (loginResult.CredentialResult.Status != CredentialResultStatus.Success)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: "
                    + (
                        loginResult.CredentialResult.Error?.SafeMessage
                        ?? "credential login failed."
                    )
            );
            return NotImplementedExitCode;
        }

        WriteText(stdout, BuildLoginOutput(invocation, loginResult));
        return SuccessExitCode;
    }

    internal static bool ShouldTreatLoginResultAsCanceled(
        CredentialResult credentialResult,
        CancellationToken cancellationToken
    ) =>
        cancellationToken.IsCancellationRequested
        && credentialResult.Status != CredentialResultStatus.Success;

    private static int HandleLogout(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        AuthPhase14LogoutResult logoutResult = AuthPhase14VerticalSliceService.Logout();
        ConfigurationPhase14CleanupResult cleanupResult;
        try
        {
            cleanupResult = CreateConfigurationPhase14VerticalSliceService(
                    runtimeOptions,
                    requireCredentialProvider: false
                )
                .LogoutAsync(GetCancellationToken(runtimeOptions))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
                        or ArgumentException
                        or System.Text.Json.JsonException
            )
        {
            TryWriteDiagnosticText(
                stderr,
                "error: authentication state was cleared, but CI temporary cleanup failed."
            );
            return NotImplementedExitCode;
        }

        if (!IsConfigurationPhase14CleanupSuccess(cleanupResult))
        {
            WriteText(stdout, BuildLogoutOutput(invocation, logoutResult, cleanupResult));
            TryWriteDiagnosticText(
                stderr,
                "error: authentication state was cleared, but CI temporary cleanup "
                    + "was incomplete."
            );
            return NotImplementedExitCode;
        }

        WriteText(stdout, BuildLogoutOutput(invocation, logoutResult, cleanupResult));
        return SuccessExitCode;
    }

    private static int HandleIdentity(
        CliInvocation invocation,
        TextWriter stdout,
        TextWriter stderr,
        CliRuntimeOptions? runtimeOptions
    )
    {
        CredentialProviderIdentityConfigurationService service =
            runtimeOptions?.IdentityConfiguration
            ?? new CredentialProviderIdentityConfigurationService();
        CredentialProviderIdentityConfigurationResult result;
        try
        {
            result = invocation.IdentityOptions.Action switch
            {
                CliIdentityAction.Configure => service.Configure(
                    invocation.IdentityOptions.TenantId!,
                    invocation.IdentityOptions.AccountPreference
                ),
                CliIdentityAction.Reconfigure => service.Reconfigure(
                    invocation.IdentityOptions.TenantId!,
                    invocation.IdentityOptions.AccountPreference
                ),
                CliIdentityAction.Unconfigure => service.Unconfigure(),
                _ => throw new InvalidOperationException("Unsupported identity action."),
            };
        }
        catch (AzureAuthBindingMismatchException)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: identity context differs from the recorded configuration; "
                    + $"run '{CommandName} identity reconfigure' to replace it."
            );
            return NotImplementedExitCode;
        }
        catch (CredentialProviderIdentityConfigurationConflictException)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: identity configuration changed concurrently; retry the command."
            );
            return NotImplementedExitCode;
        }
        catch (InvalidOperationException exception)
        {
            TryWriteDiagnosticText(
                stderr,
                "error: " + EscapeNonPrintingCharacters(exception.Message)
            );
            return NotImplementedExitCode;
        }

        WriteText(stdout, BuildIdentityOutput(invocation.IdentityOptions.Action, result));
        return SuccessExitCode;
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
            CliCommand.Doctor => ParseDoctor(remainingArgs),
            CliCommand.Cleanup => ParseCleanup(remainingArgs),
            CliCommand.Acceptance => ParseAcceptance(remainingArgs),
            CliCommand.Login => ParseLogin(remainingArgs),
            CliCommand.Logout => ParseLogout(remainingArgs),
            CliCommand.Identity => ParseIdentity(remainingArgs),
            CliCommand.Configure => ParseConfigurationCommand(CliCommand.Configure, remainingArgs),
            CliCommand.Refresh => ParseConfigurationCommand(CliCommand.Refresh, remainingArgs),
            CliCommand.Unconfigure => ParseConfigurationCommand(
                CliCommand.Unconfigure,
                remainingArgs
            ),
            _ => throw CreateUsageError(
                $"error: command is not recognized. Run '{CommandName} --help' for usage."
            ),
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
                throw CreateUsageError("error: option '--ci' cannot be specified more than once.");
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
                    + $"Run '{CommandName} status --help' for usage."
            );
        }

        return new CliInvocation(CliCommand.Status, null, ciMode, DryRun: false, HelpText: null);
    }

    private static CliInvocation ParseLogin(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildLoginHelp());
        }

        var ciMode = CliCiMode.None;
        var ciSpecified = false;
        var flowSpecified = false;
        var authOptions = new CliAuthOptions();

        for (var index = 0; index < args.Count; index++)
        {
            string token = args[index];
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildLoginHelp());
            }

            if (ciSpecified && IsCiOptionToken(token))
            {
                throw CreateUsageError("error: option '--ci' cannot be specified more than once.");
            }

            if (TryParseCiMode(args, ref index, out CliCiMode parsedCiMode))
            {
                ciSpecified = true;
                ciMode = parsedCiMode;
                authOptions = authOptions with
                {
                    IdentityFlow =
                        parsedCiMode == CliCiMode.AzurePipelines
                            ? IdentityFlow.AzurePipelinesSystemAccessToken
                            : authOptions.IdentityFlow,
                };
                continue;
            }

            if (TryParseLoginFlowOption(token, ref flowSpecified, out CliAuthOptions flowOptions))
            {
                authOptions = authOptions with
                {
                    IdentityFlow = flowOptions.IdentityFlow,
                    DeferredFlowName = flowOptions.DeferredFlowName,
                };
                continue;
            }

            if (TryParseStringOption(args, ref index, "--account", out string? accountHint))
            {
                authOptions = authOptions with { AccountHint = accountHint };
                continue;
            }

            if (TryParseStringOption(args, ref index, "--tenant", out string? tenantHint))
            {
                authOptions = authOptions with { TenantHint = tenantHint };
                continue;
            }

            if (TryParseStringOption(args, ref index, "--pat", out _))
            {
                if (flowSpecified)
                {
                    throw CreateUsageError("error: login accepts only one identity-flow option.");
                }

                flowSpecified = true;
                authOptions = authOptions with
                {
                    IdentityFlow = IdentityFlow.PatCompatibility,
                    ExplicitPatMaterialProvided = true,
                };
                continue;
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                "error: login does not accept positional arguments. "
                    + $"Run '{CommandName} login --help' for usage."
            );
        }

        if (ciMode == CliCiMode.AzurePipelines && flowSpecified)
        {
            throw CreateUsageError(
                "error: login --ci azure-pipelines cannot be combined with another "
                    + "identity-flow option."
            );
        }

        return new CliInvocation(CliCommand.Login, null, ciMode, DryRun: false, HelpText: null)
        {
            AuthOptions = authOptions,
        };
    }

    private static CliInvocation ParseLogout(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildLogoutHelp());
        }

        foreach (string token in args)
        {
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildLogoutHelp());
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                "error: logout does not accept positional arguments. "
                    + $"Run '{CommandName} logout --help' for usage."
            );
        }

        return new CliInvocation(
            CliCommand.Logout,
            null,
            CliCiMode.None,
            DryRun: false,
            HelpText: null
        );
    }

    private static CliInvocation ParseIdentity(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildIdentityHelp());
        }

        if (args.Count == 0)
        {
            throw CreateUsageError(
                "error: identity requires an action: configure, reconfigure, or unconfigure."
            );
        }

        string actionToken = args[0];
        if (IsOptionToken(actionToken))
        {
            throw CreateUnknownOptionError(actionToken);
        }

        CliIdentityAction action = actionToken switch
        {
            { } value when string.Equals(value, "configure", StringComparison.OrdinalIgnoreCase) =>
                CliIdentityAction.Configure,
            { } value
                when string.Equals(value, "reconfigure", StringComparison.OrdinalIgnoreCase) =>
                CliIdentityAction.Reconfigure,
            { } value
                when string.Equals(value, "unconfigure", StringComparison.OrdinalIgnoreCase) =>
                CliIdentityAction.Unconfigure,
            _ => throw CreateUsageError(
                "error: identity action must be configure, reconfigure, or unconfigure."
            ),
        };

        string? tenantId = null;
        string? accountPreference = null;
        var tenantSpecified = false;
        var accountSpecified = false;
        for (var index = 1; index < args.Count; index++)
        {
            string token = args[index];
            if (TryParseStringOption(args, ref index, "--tenant", out string? parsedTenant))
            {
                if (tenantSpecified)
                {
                    throw CreateUsageError(
                        "error: option '--tenant' cannot be specified more than once."
                    );
                }

                tenantSpecified = true;
                tenantId = parsedTenant;
                continue;
            }

            if (
                TryParseStringOption(
                    args,
                    ref index,
                    "--account",
                    out string? parsedAccountPreference
                )
            )
            {
                if (accountSpecified)
                {
                    throw CreateUsageError(
                        "error: option '--account' cannot be specified more than once."
                    );
                }

                accountSpecified = true;
                accountPreference = parsedAccountPreference;
                continue;
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                "error: identity does not accept additional positional arguments. "
                    + $"Run '{CommandName} identity --help' for usage."
            );
        }

        if (action == CliIdentityAction.Unconfigure)
        {
            if (tenantSpecified || accountSpecified)
            {
                throw CreateUsageError(
                    "error: identity unconfigure does not accept --tenant or --account."
                );
            }
        }
        else if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw CreateUsageError(
                $"error: identity {actionToken.ToLowerInvariant()} requires --tenant <id>."
            );
        }

        return new CliInvocation(
            CliCommand.Identity,
            null,
            CliCiMode.None,
            DryRun: false,
            HelpText: null
        )
        {
            IdentityOptions = new CliIdentityOptions
            {
                Action = action,
                TenantId = tenantId,
                AccountPreference = accountPreference,
            },
        };
    }

    private static CliInvocation ParseDoctor(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildDoctorHelp());
        }

        foreach (string token in args)
        {
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildDoctorHelp());
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                "error: doctor does not accept positional arguments. "
                    + $"Run '{CommandName} doctor --help' for usage."
            );
        }

        return new CliInvocation(
            CliCommand.Doctor,
            null,
            CliCiMode.None,
            DryRun: false,
            HelpText: null
        );
    }

    private static CliInvocation ParseAcceptance(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildAcceptanceHelp());
        }

        foreach (string token in args)
        {
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildAcceptanceHelp());
            }

            if (IsOptionToken(token))
            {
                throw CreateUnknownOptionError(token);
            }

            throw CreateUsageError(
                "error: acceptance does not accept positional arguments. "
                    + $"Run '{CommandName} acceptance --help' for usage."
            );
        }

        return new CliInvocation(
            CliCommand.Acceptance,
            null,
            CliCiMode.None,
            DryRun: false,
            HelpText: null
        );
    }

    private static CliInvocation ParseCleanup(IReadOnlyList<string> args)
    {
        if (ContainsStandaloneHelpToken(args))
        {
            ThrowIfAnyValuelessOptionHasAssignedValue(args);
            return CliInvocation.CreateHelp(BuildCleanupHelp());
        }

        var ciMode = CliCiMode.None;
        var ciSpecified = false;
        var dryRun = false;
        var ecosystemSpecified = false;
        CredentialEcosystem? ecosystem = null;

        for (var index = 0; index < args.Count; index++)
        {
            string token = args[index];
            ThrowIfValuelessOptionHasAssignedValue(token);
            if (IsHelpToken(token))
            {
                return CliInvocation.CreateHelp(BuildCleanupHelp());
            }

            if (string.Equals(token, "--dry-run", StringComparison.Ordinal))
            {
                dryRun = true;
                continue;
            }

            if (ciSpecified && IsCiOptionToken(token))
            {
                throw CreateUsageError("error: option '--ci' cannot be specified more than once.");
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

            if (!ecosystemSpecified)
            {
                ecosystemSpecified = true;
                ecosystem = string.Equals(token, "all", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : ParseEcosystem(token);
                if (
                    ecosystem
                    is CredentialEcosystem.Git
                        or CredentialEcosystem.NuGet
                        or CredentialEcosystem.Python
                )
                {
                    throw CreateUsageError(
                        "error: cleanup ecosystem must be one of: npm, pnpm, yarn, all."
                    );
                }

                continue;
            }

            throw CreateUsageError(
                "error: cleanup accepts at most one <ecosystem> argument. "
                    + $"Run '{CommandName} cleanup --help' for usage."
            );
        }

        if (!ciSpecified || ciMode != CliCiMode.AzurePipelines)
        {
            throw CreateUsageError(
                $"error: cleanup requires '--ci azure-pipelines'. "
                    + $"Run '{CommandName} cleanup --help' for usage."
            );
        }

        return new CliInvocation(CliCommand.Cleanup, ecosystem, ciMode, dryRun, HelpText: null);
    }

    private static CliInvocation ParseConfigurationCommand(
        CliCommand command,
        IReadOnlyList<string> args
    )
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
        Uri? registryUrl = null;
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
                throw CreateUsageError("error: option '--ci' cannot be specified more than once.");
            }

            if (TryParseCiMode(args, ref index, out CliCiMode parsedCiMode))
            {
                ciSpecified = true;
                ciMode = parsedCiMode;
                continue;
            }

            if (TryParseStringOption(args, ref index, "--registry-url", out string registryUrlText))
            {
                if (command is not CliCommand.Configure and not CliCommand.Refresh)
                {
                    throw CreateUsageError(
                        "error: option '--registry-url' is supported only by configure "
                            + "and refresh."
                    );
                }

                if (registryUrl is not null)
                {
                    throw CreateUsageError(
                        "error: option '--registry-url' cannot be specified more than once."
                    );
                }

                if (!Uri.TryCreate(registryUrlText, UriKind.Absolute, out registryUrl))
                {
                    throw CreateUsageError(
                        "error: option '--registry-url' requires an absolute URL."
                    );
                }

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
                    + $"Run '{CommandName} {commandName} --help' for usage."
            );
        }

        if (ecosystem is null)
        {
            throw CreateUsageError(
                "error: missing required <ecosystem> argument. "
                    + $"Run '{CommandName} {commandName} --help' for usage."
            );
        }

        if (
            registryUrl is not null
            && ecosystem
                is not CredentialEcosystem.Npm
                    and not CredentialEcosystem.Pnpm
                    and not CredentialEcosystem.Yarn
        )
        {
            throw CreateUsageError(
                "error: option '--registry-url' is supported only for npm, pnpm, and yarn."
            );
        }

        if (
            command == CliCommand.Refresh
            && ecosystem
                is not CredentialEcosystem.Npm
                    and not CredentialEcosystem.Pnpm
                    and not CredentialEcosystem.Yarn
        )
        {
            throw CreateUsageError("error: refresh supports only npm, pnpm, and yarn.");
        }

        if (
            command == CliCommand.Configure
            && ecosystem
                is CredentialEcosystem.Npm
                    or CredentialEcosystem.Pnpm
                    or CredentialEcosystem.Yarn
            && registryUrl is null
        )
        {
            throw CreateUsageError(
                "error: configure for npm, pnpm, and yarn requires " + "'--registry-url <url>'."
            );
        }

        return new CliInvocation(command, ecosystem.Value, ciMode, dryRun, HelpText: null)
        {
            RegistryUrl = registryUrl,
        };
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
        out CliCiMode ciMode
    )
    {
        string token = args[index];
        int assignmentIndex = GetOptionAssignmentIndex(token);
        if (
            assignmentIndex >= 0
            && string.Equals(token[..assignmentIndex], "--ci", StringComparison.Ordinal)
        )
        {
            string? value = GetOptionValue(token);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw CreateUsageError(
                    "error: option '--ci' requires a value: none or azure-pipelines."
                );
            }

            ciMode = ParseCiMode(value);
            return true;
        }

        if (string.Equals(token, "--ci", StringComparison.Ordinal))
        {
            if (
                index + 1 >= args.Count
                || IsOptionToken(args[index + 1])
                || string.IsNullOrWhiteSpace(args[index + 1])
            )
            {
                throw CreateUsageError(
                    "error: option '--ci' requires a value: none or azure-pipelines."
                );
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
                "error: option '--ci' must be one of: none, azure-pipelines."
            ),
        };
    }

    private static bool TryParseStringOption(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string value
    )
    {
        string token = args[index];
        int assignmentIndex = GetOptionAssignmentIndex(token);
        if (
            assignmentIndex >= 0
            && string.Equals(token[..assignmentIndex], optionName, StringComparison.Ordinal)
        )
        {
            string? assignedValue = GetOptionValue(token);
            if (string.IsNullOrWhiteSpace(assignedValue))
            {
                throw CreateUsageError($"error: option '{optionName}' requires a value.");
            }

            value = assignedValue;
            return true;
        }

        if (string.Equals(token, optionName, StringComparison.Ordinal))
        {
            if (
                index + 1 >= args.Count
                || IsOptionToken(args[index + 1])
                || string.IsNullOrWhiteSpace(args[index + 1])
            )
            {
                throw CreateUsageError($"error: option '{optionName}' requires a value.");
            }

            index++;
            value = args[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseLoginFlowOption(
        string token,
        ref bool flowSpecified,
        out CliAuthOptions authOptions
    )
    {
        authOptions = new CliAuthOptions();
        IdentityFlow? flow = GetOptionName(token) switch
        {
            "--browser" => IdentityFlow.InteractiveBrowser,
            "--device-code" => IdentityFlow.DeviceCode,
            "--service-principal" => IdentityFlow.ServicePrincipal,
            "--managed-identity" => IdentityFlow.ManagedIdentity,
            "--workload-identity" => IdentityFlow.WorkloadIdentityFederation,
            _ => null,
        };

        if (flow is null)
        {
            return false;
        }

        if (GetOptionAssignmentIndex(token) >= 0)
        {
            throw CreateUsageError(
                $"error: option '{SanitizeOptionToken(token)}' does not accept a value."
            );
        }

        if (flowSpecified)
        {
            throw CreateUsageError("error: login accepts only one identity-flow option.");
        }

        flowSpecified = true;
        authOptions = new CliAuthOptions
        {
            IdentityFlow = flow.Value,
            DeferredFlowName = GetDeferredFlowName(flow.Value),
        };
        return true;
    }

    private static string? GetDeferredFlowName(IdentityFlow flow)
    {
        return IdentityFlowPolicy.GetMvpState(flow) == IdentityFlowState.Deferred
            ? GetIdentityFlowText(flow)
            : null;
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
            { } value when string.Equals(value, "pnpm", StringComparison.OrdinalIgnoreCase) =>
                CredentialEcosystem.Pnpm,
            { } value when string.Equals(value, "yarn", StringComparison.OrdinalIgnoreCase) =>
                CredentialEcosystem.Yarn,
            _ => throw CreateUsageError(
                "error: ecosystem must be one of: git, nuget, python, npm, pnpm, yarn."
            ),
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
            { } value when string.Equals(value, "cleanup", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Cleanup,
            { } value when string.Equals(value, "acceptance", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Acceptance,
            { } value when string.Equals(value, "login", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Login,
            { } value when string.Equals(value, "logout", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Logout,
            { } value when string.Equals(value, "identity", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Identity,
            { } value when string.Equals(value, "configure", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Configure,
            { } value when string.Equals(value, "refresh", StringComparison.OrdinalIgnoreCase) =>
                CliCommand.Refresh,
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
                + "is not supported for this command."
        );
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
            $"error: option '{SanitizeOptionToken(token)}' does not accept a value."
        );
    }

    private static CliUsageException CreateUsageError(string message)
    {
        return new CliUsageException(message, UsageExitCode);
    }

    private static void WriteFatalError(
        TextWriter stderr,
        SecretRedactor redactor,
        string? details = null
    )
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
            "  status                       Show deterministic Phase 15 hardening status.",
            "  doctor                       Run aggregate adapter, config, and auth checks.",
            "  cleanup [ecosystem]          Clean product-owned temporary CI state.",
            "  acceptance                   Render Phase 15 hardening matrix.",
            "  login                        Run accepted MVP authentication orchestration.",
            "  logout                       Clear product-owned authentication state.",
            "  identity                     Configure product identity context.",
            "  configure <ecosystem>        Apply supported configuration plans.",
            "  refresh <ecosystem>          Refresh an npm, pnpm, or Yarn credential.",
            "  unconfigure <ecosystem>      Remove supported configuration plans.",
            string.Empty,
            "Options:",
            "  -h, --help                   Show help.",
            string.Empty,
            "Examples:",
            $"  {CommandName} status",
            $"  {CommandName} login --device-code",
            $"  {CommandName} login --ci azure-pipelines",
            $"  {CommandName} identity configure --tenant <id> [--account <name>]",
            $"  {CommandName} status --ci azure-pipelines",
            $"  {CommandName} configure git --dry-run",
            $"  {CommandName} acceptance",
            $"  {CommandName} cleanup --ci azure-pipelines",
            $"  {CommandName} unconfigure npm --dry-run"
        );
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
            "  -h, --help                   Show help."
        );
    }

    private static string BuildConfigurationHelp(CliCommand command)
    {
        string commandName = GetCommandName(command);
        var lines = new List<string>
        {
            $"{CommandName} {commandName}",
            "Usage:",
            $"  {CommandName} {commandName} <ecosystem> [--dry-run] [--ci <mode>] "
                + (
                    command == CliCommand.Configure ? "--registry-url <url> "
                    : command == CliCommand.Refresh ? "[--registry-url <url>] "
                    : string.Empty
                )
                + "[--help]",
            string.Empty,
            "Ecosystems:",
        };
        if (command != CliCommand.Refresh)
        {
            lines.AddRange(["  git", "  nuget", "  python"]);
        }

        lines.AddRange([
            "  npm",
            "  pnpm",
            "  yarn",
            string.Empty,
            "Options:",
            "  --dry-run                    Render planned actions without mutating files.",
            "  --ci <mode>                  Select CI mode explicitly: none | azure-pipelines.",
        ]);
        if (command is CliCommand.Configure or CliCommand.Refresh)
        {
            lines.Add(
                command == CliCommand.Configure
                    ? "  --registry-url <url>         Required Azure Artifacts npm URL "
                        + "for npm, pnpm, and Yarn."
                    : "  --registry-url <url>         Azure Artifacts npm URL; optional only "
                        + "when the canonical ownership manifest is valid."
            );
        }

        lines.Add("  -h, --help                   Show help.");
        return JoinLines(lines);
    }

    private static string BuildDoctorHelp()
    {
        return JoinLines(
            $"{CommandName} doctor",
            "Usage:",
            $"  {CommandName} doctor [--help]",
            string.Empty,
            "Status:",
            "  Run safe deterministic cross-ecosystem checks and remediation guidance.",
            string.Empty,
            "Options:",
            "  -h, --help                   Show help."
        );
    }

    private static string BuildCleanupHelp()
    {
        return JoinLines(
            $"{CommandName} cleanup",
            "Usage:",
            $"  {CommandName} cleanup [<ecosystem>|all] [--dry-run] "
                + "--ci azure-pipelines [--help]",
            string.Empty,
            "Ecosystems:",
            "  npm",
            "  pnpm",
            "  yarn",
            string.Empty,
            "Status:",
            "  Clean product-owned CI temporary package configuration.",
            "  User-level integration removal stays under unconfigure <ecosystem>.",
            string.Empty,
            "Options:",
            "  --dry-run                    Render cleanup actions without mutating files.",
            "  --ci azure-pipelines         Required; clean Azure Pipelines temporary state.",
            "  -h, --help                   Show help."
        );
    }

    private static string BuildAcceptanceHelp()
    {
        return JoinLines(
            $"{CommandName} acceptance",
            "Usage:",
            $"  {CommandName} acceptance [--help]",
            string.Empty,
            "Status:",
            "  Render the executable Phase 15 release-hardening acceptance matrix.",
            "  Deferred rows are not accepted support claims.",
            string.Empty,
            "Options:",
            "  -h, --help                   Show help."
        );
    }

    private static string BuildLoginHelp()
    {
        return JoinLines(
            $"{CommandName} login",
            "Usage:",
            $"  {CommandName} login [--browser|--device-code|--pat <value>]",
            $"  {CommandName} login --ci azure-pipelines",
            string.Empty,
            "Identity flow options:",
            "  --browser                    Use interactive browser authentication.",
            "  --device-code                Use device-code authentication.",
            "  --pat <value>                Deferred PAT compatibility placeholder; "
                + "never persisted.",
            "  --ci azure-pipelines         Use SYSTEM_ACCESSTOKEN without persistence.",
            string.Empty,
            "Deferred service identity flows:",
            "  --service-principal",
            "  --managed-identity",
            "  --workload-identity",
            string.Empty,
            "Options:",
            "  --account <name>             Optional account hint.",
            "  --tenant <id>                Optional tenant hint.",
            "  -h, --help                   Show help."
        );
    }

    private static string BuildLogoutHelp()
    {
        return JoinLines(
            $"{CommandName} logout",
            "Usage:",
            $"  {CommandName} logout [--help]",
            string.Empty,
            "Status:",
            "  Clears product-owned authentication state, then job-scoped CI temporary state.",
            string.Empty,
            "Options:",
            "  -h, --help                   Show help."
        );
    }

    private static string BuildIdentityHelp()
    {
        return JoinLines(
            $"{CommandName} identity",
            "Usage:",
            $"  {CommandName} identity configure --tenant <id> [--account <name>] [--help]",
            $"  {CommandName} identity reconfigure --tenant <id> [--account <name>] [--help]",
            $"  {CommandName} identity unconfigure [--help]",
            string.Empty,
            "Actions:",
            "  configure                    Record identity context if none exists.",
            "  reconfigure                  Replace or repair identity context.",
            "  unconfigure                  Remove recorded identity context.",
            string.Empty,
            "Options:",
            "  --tenant <id>                Required tenant for configure and reconfigure.",
            "  --account <name>             Optional account preference.",
            "  -h, --help                   Show help.",
            string.Empty,
            "Identity configuration stores invocation context only.",
            "It stores no credentials and does not verify the account."
        );
    }

    private static string BuildStatusOutput(
        CliCiMode ciMode,
        CredentialProviderCompositionRoot root,
        CredentialProviderReadiness readiness
    )
    {
        bool deviceCodeReady = IsDeviceCodeReady(root, readiness);
        List<string> lines =
        [
            "command: status",
            $"product: {CommandName}",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(ciMode)}",
            $"composition-mode: {root.Mode}",
            $"provider: {root.ProviderConfig.Selection}",
            "interactive-readiness: "
                + (readiness.Interactive.IsReady ? "interactive-ready" : "interactive-unavailable"),
            $"interactive-readiness-code: {readiness.Interactive.Code}",
        ];
        if (!readiness.Interactive.IsReady)
        {
            lines.Add($"interactive-blocker: {readiness.Interactive.SafeMessage}");
        }

        lines.AddRange([
            "silent-readiness: "
                + (readiness.Silent.IsReady ? "silent-ready" : "silent-unavailable"),
            $"silent-readiness-code: {readiness.Silent.Code}",
            $"silent-remediation: {readiness.Silent.SafeMessage}",
            "status-shell: ready",
            "environment-probing: disabled",
            "persistent-cache: disabled",
            "persistent-derived-credentials: disabled",
            "accepted-identity-flows: "
                + (
                    deviceCodeReady
                        ? "browser, device-code, azure-pipelines"
                        : "browser, azure-pipelines"
                ),
            "unavailable-identity-flows: " + (deviceCodeReady ? "none" : "device-code"),
            "deferred-identity-flows: pat-compatibility, service-principal, "
                + "managed-identity, workload-identity",
            "pat-compatibility: deferred-disabled",
            "dry-run-rendering: enabled",
            "mutating-commands: identity-configuration, host-tool-configuration, auth, cleanup",
            $"supported-ecosystems: {string.Join(", ", SupportedEcosystems)}",
        ]);
        return JoinLines(lines);
    }

    private static string BuildAcceptanceOutput(
        CliInvocation invocation,
        ReleaseHardeningPhase15MatrixResult result
    )
    {
        ArgumentNullException.ThrowIfNull(result);
        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            $"phase: {PhaseName}",
            "mvp-local-acceptance: " + (result.MvpLocalAcceptancePassed ? "pass" : "fail"),
            "full-release-evidence: "
                + (result.FullReleaseEvidenceComplete ? "complete" : "deferred"),
            "blocking-checks: " + (result.BlockingFailuresPresent ? "present" : "none"),
            "pat-compatibility: deferred-disabled",
            "deferred-non-mvp: "
                + JoinCheckIds(result.Checks, ReleaseHardeningPhase15CheckStatus.DeferredNonMvp),
            "deferred-release-evidence: "
                + JoinCheckIds(
                    result.Checks,
                    ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence
                ),
            "deferred-optional-feature: "
                + JoinCheckIds(
                    result.Checks,
                    ReleaseHardeningPhase15CheckStatus.DeferredOptionalFeature
                ),
        ];

        foreach (ReleaseHardeningPhase15Check check in result.Checks)
        {
            lines.Add($"{check.Id}: {GetPhase15CheckStatusText(check.Status)}");
            lines.Add($"{check.Id}-area: {check.Area}");
            lines.Add($"{check.Id}-required-for-mvp: {GetYesNo(check.RequiredForMvp)}");
            lines.Add(
                $"{check.Id}-required-for-full-release: " + GetYesNo(check.RequiredForFullRelease)
            );
            lines.Add($"{check.Id}-evidence: {check.Evidence}");
        }

        lines.Add("note: credential material is not printed");
        lines.Add("note: deferred rows are not accepted support claims");
        return JoinLines(lines);
    }

    private static string BuildLoginOutput(
        CliInvocation invocation,
        AuthPhase14LoginResult loginResult
    )
    {
        CredentialResult credentialResult = loginResult.CredentialResult;
        bool opaqueAzurePipelinesToken =
            loginResult.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken;
        return JoinLines(
            "command: login",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"identity-flow: {GetIdentityFlowText(loginResult.IdentityFlow)}",
            $"status: {credentialResult.Status.ToString().ToLowerInvariant()}",
            "account: "
                + (
                    credentialResult.Account
                    ?? (opaqueAzurePipelinesToken ? "unbound" : "unselected")
                ),
            "tenant: "
                + (credentialResult.Tenant ?? (opaqueAzurePipelinesToken ? "unbound" : "default")),
            "credential-material: "
                + (opaqueAzurePipelinesToken ? "provided-not-printed" : "issued-not-printed"),
            "persistent-derived-credentials: "
                + (loginResult.PersistentDerivedCredentialsStored ? "stored" : "disabled"),
            "product-plaintext-fallback: disabled"
        );
    }

    private static string BuildLogoutOutput(
        CliInvocation invocation,
        AuthPhase14LogoutResult logoutResult,
        ConfigurationPhase14CleanupResult cleanupResult
    )
    {
        List<string> lines =
        [
            "command: logout",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            "persistent-derived-credentials-removed: "
                + (logoutResult.PersistentDerivedCredentialsRemoved ? "yes" : "none"),
            $"removed-change-count: {cleanupResult.AppliedChangeCount}",
            "cleanup: "
                + (IsConfigurationPhase14CleanupSuccess(cleanupResult) ? "complete" : "incomplete"),
            "product-plaintext-fallback: disabled",
        ];
        AddIncompleteCleanupRemediation(lines, cleanupResult);
        return JoinLines(lines);
    }

    private static string BuildIdentityOutput(
        CliIdentityAction action,
        CredentialProviderIdentityConfigurationResult result
    )
    {
        string actionText = action switch
        {
            CliIdentityAction.Configure => "configure",
            CliIdentityAction.Reconfigure => "reconfigure",
            CliIdentityAction.Unconfigure => "unconfigure",
            _ => throw new InvalidOperationException("Unsupported identity action."),
        };
        string status = result.Changed
            ? action switch
            {
                CliIdentityAction.Configure => "configured",
                CliIdentityAction.Reconfigure => "reconfigured",
                CliIdentityAction.Unconfigure => "unconfigured",
                _ => throw new InvalidOperationException("Unsupported identity action."),
            }
            : "unchanged";
        var lines = new List<string>
        {
            "command: identity",
            $"action: {actionText}",
            $"status: {status}",
        };
        if (result.IsConfigured)
        {
            lines.Add("tenant: " + EscapeNonPrintingCharacters(result.TenantId!));
            lines.Add(
                "account-preference: "
                    + (
                        result.AccountPreference is null
                            ? "none"
                            : EscapeNonPrintingCharacters(result.AccountPreference)
                    )
            );
        }

        lines.Add("credential-material: not-stored");
        lines.Add("identity-verification: not-performed");
        return JoinLines(lines);
    }

    private static string BuildConfigurationPhase14Output(
        CliInvocation invocation,
        ConfigurationPhase14PlanResult result
    )
    {
        string changeCountLabel = invocation.Command is CliCommand.Configure or CliCommand.Refresh
            ? "applied-change-count"
            : "removed-change-count";
        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            $"ecosystem: {GetEcosystemText(invocation.Ecosystem!.Value)}",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: "
                + (result.AppliedChangeCount > 0 || result.LifecycleStateMutated ? "yes" : "no"),
            $"plan-operation: {GetPlanOperationText(result.PlanResult.Operation)}",
            $"{changeCountLabel}: {result.AppliedChangeCount}",
            "ownership-manifest: " + GetPresenceText(result.OwnershipManifestPresent),
            "credential-material: not-printed",
        ];

        bool temporaryContainerEmitted = AddTemporaryContainerOutput(
            lines,
            invocation,
            result.PlanResult.Plan
        );
        if (
            !temporaryContainerEmitted
            && invocation.Command is CliCommand.Configure or CliCommand.Refresh
            && invocation.CiMode == CliCiMode.None
            && IsPackageRegistryEcosystem(invocation.Ecosystem!.Value)
        )
        {
            lines.Add(
                "configuration-path: "
                    + GetConfigurationPath(invocation.Ecosystem.Value, result.Paths)
            );
        }

        return JoinLines(lines);
    }

    private static bool AddTemporaryContainerOutput(
        List<string> lines,
        CliInvocation invocation,
        ConfigurationDryRunPlan plan
    )
    {
        if (
            invocation.Command is not (CliCommand.Configure or CliCommand.Refresh)
            || plan.TemporaryContainer is not { } temporaryContainer
        )
        {
            return false;
        }

        lines.Add("temporary-container: " + temporaryContainer.Kind.ToString().ToLowerInvariant());
        lines.Add("configuration-path: " + temporaryContainer.ProductOwnedPath);
        if (temporaryContainer.Kind == ConfigurationTemporaryContainerKind.NpmrcFile)
        {
            lines.Add(
                invocation.Ecosystem == CredentialEcosystem.Pnpm
                    ? "package-manager-argument: --config.userconfig="
                        + temporaryContainer.ProductOwnedPath
                    : "package-manager-argument: --userconfig "
                        + temporaryContainer.ProductOwnedPath
            );
        }

        if (temporaryContainer.ActivationEnvironment is { } activation)
        {
            foreach (
                (string name, string value) in activation.SetVariables.OrderBy(
                    static pair => pair.Key,
                    StringComparer.Ordinal
                )
            )
            {
                lines.Add($"set-environment: {name}={value}");
            }

            foreach (string name in activation.ClearVariables.Order(StringComparer.Ordinal))
            {
                lines.Add($"clear-environment: {name}");
            }
        }

        return true;
    }

    private static void WriteIncompleteCredentialCleanupDiagnostic(
        TextWriter stderr,
        ConfigurationPhase14Scope scope
    ) =>
        TryWriteDiagnosticText(
            stderr,
            scope == ConfigurationPhase14Scope.CiTemporary
                ? "error: CI temporary credential cleanup is incomplete; "
                    + "the ownership manifest was preserved for diagnosis."
                : "error: user credential cleanup is incomplete; "
                    + "the ownership manifest was preserved for diagnosis."
        );

    private static void WriteIncompleteCleanupDiagnostic(
        TextWriter stderr,
        ConfigurationPhase14Scope scope
    ) =>
        TryWriteDiagnosticText(
            stderr,
            scope == ConfigurationPhase14Scope.CiTemporary
                ? "error: CI temporary credential cleanup is incomplete; verify SYSTEM_JOBID "
                    + "and inspect the cleanup remediation."
                : "error: user credential cleanup is incomplete; inspect the cleanup remediation."
        );

    private static string BuildCleanupOutput(
        CliInvocation invocation,
        ConfigurationPhase14CleanupResult cleanupResult,
        bool dryRun = false
    )
    {
        ArgumentNullException.ThrowIfNull(cleanupResult);
        string ecosystemText = invocation.Ecosystem is { } ecosystem
            ? GetEcosystemText(ecosystem)
            : "all";
        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            $"ecosystem: {ecosystemText}",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: "
                + (
                    !dryRun
                    && (
                        cleanupResult.AppliedChangeCount > 0
                        || cleanupResult.PersistentDerivedCredentialsRemoved
                        || cleanupResult.Ecosystems.Any(static result => result.State == "removed")
                    )
                        ? "yes"
                        : "no"
                ),
            (dryRun ? "planned-change-count: " : "removed-change-count: ")
                + (dryRun ? cleanupResult.ChangeCount : cleanupResult.AppliedChangeCount),
            "persistent-derived-credentials-removed: "
                + (cleanupResult.PersistentDerivedCredentialsRemoved ? "yes" : "none"),
        ];

        foreach (
            var ecosystemResult in cleanupResult.Ecosystems.OrderBy(static result =>
                GetEcosystemText(result.Ecosystem)
            )
        )
        {
            string prefix = GetConfigurationPhase14CleanupPrefix(ecosystemResult);
            lines.Add($"{prefix}-cleanup: {ecosystemResult.State}");
            lines.Add(
                $"{prefix}-{(dryRun ? "planned" : "removed")}-change-count: "
                    + (dryRun ? ecosystemResult.ChangeCount : ecosystemResult.AppliedChangeCount)
            );
            lines.Add(
                $"{prefix}-ownership-manifest: "
                    + GetPresenceText(ecosystemResult.OwnershipManifestPresent)
            );
            lines.Add(
                $"{prefix}-temporary-container: "
                    + GetPresenceText(ecosystemResult.TemporaryContainerPresent)
            );
        }

        if (cleanupResult.Ecosystems.Count == 0)
        {
            lines.Add("cleanup-state: not-needed");
            lines.Add("remediation: use unconfigure <ecosystem> to remove user-level integrations");
        }
        else
        {
            AddIncompleteCleanupRemediation(lines, cleanupResult);
        }

        lines.Add(
            dryRun
                ? "note: dry-run only; no files, credentials, or caches are changed"
                : "note: credential material is not printed"
        );
        return JoinLines(lines);
    }

    private static string BuildDryRunOutput(CliInvocation invocation)
    {
        CredentialEcosystem ecosystem =
            invocation.Ecosystem
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

        lines.Add("note: no files, credentials, or caches are changed in phase 10");
        return JoinLines(lines);
    }

    private static string BuildConfigurationPhase14DryRunOutput(
        CliInvocation invocation,
        ConfigurationPhase14PlanResult dryRunResult
    )
    {
        CredentialEcosystem ecosystem =
            invocation.Ecosystem
            ?? throw new InvalidOperationException("Dry-run commands require an ecosystem.");
        List<ConfigurationPlannedChange> changes = dryRunResult
            .PlanResults.SelectMany(static result => result.Changes)
            .ToList();
        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            $"ecosystem: {GetEcosystemText(ecosystem)}",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: no",
            $"planned-change-count: {changes.Count}",
            "planned-actions:",
        ];

        for (var index = 0; index < changes.Count; index++)
        {
            lines.Add($"  {index + 1}. {GetPhase14PlannedActionText(changes[index])}");
        }

        bool temporaryContainerEmitted = AddTemporaryContainerOutput(
            lines,
            invocation,
            dryRunResult.PlanResult.Plan
        );
        if (
            !temporaryContainerEmitted
            && invocation.Command is CliCommand.Configure or CliCommand.Refresh
            && invocation.CiMode == CliCiMode.None
            && IsPackageRegistryEcosystem(ecosystem)
        )
        {
            lines.Add("configuration-path: " + GetConfigurationPath(ecosystem, dryRunResult.Paths));
        }

        lines.Add("note: dry-run only; no files, credentials, or caches are changed");
        return JoinLines(lines);
    }

    private static string GetConfigurationPath(
        CredentialEcosystem ecosystem,
        ConfigurationPhase14ResolvedPaths paths
    ) =>
        ecosystem switch
        {
            CredentialEcosystem.Npm => paths.NpmUserNpmrcPath,
            CredentialEcosystem.Pnpm => paths.PnpmUserNpmrcPath,
            CredentialEcosystem.Yarn => paths.YarnUserYarnrcPath,
            _ => throw new InvalidOperationException(
                "Only package configuration plans identify a configuration path."
            ),
        };

    private static string GetPhase14PlannedActionText(ConfigurationPlannedChange change)
    {
        string operation = change.Operation switch
        {
            ConfigurationChangeOperation.Set => "set",
            ConfigurationChangeOperation.Remove => "remove",
            ConfigurationChangeOperation.InstallAdapter => "install",
            ConfigurationChangeOperation.RemoveAdapter => "remove",
            ConfigurationChangeOperation.EnsureFile => "ensure",
            _ => change.Operation.ToString().ToLowerInvariant(),
        };
        string target = change.TargetKind switch
        {
            ConfigurationTargetKind.PythonKeyringBackend => "Python keyring backend",
            ConfigurationTargetKind.KeyringShim => "Python keyring shim",
            ConfigurationTargetKind.Npmrc => "npm-compatible registry credential",
            ConfigurationTargetKind.Yarnrc => "Yarn registry credential",
            ConfigurationTargetKind.CiTemporaryFile => "CI temporary credential file",
            _ => change.TargetKind.ToString(),
        };
        return $"{operation} product-owned {target}";
    }

    private static string BuildGitConfigureDryRunOutput(
        CliInvocation invocation,
        GitPhase8ConfigureDryRunResult dryRunResult
    )
    {
        ArgumentNullException.ThrowIfNull(dryRunResult);

        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            "ecosystem: git",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: no",
            $"configuration-plan: {GetValidityText(dryRunResult.Validation.IsValid)}",
            $"planned-change-count: {dryRunResult.PlanResult.Changes.Count}",
            "planned-actions:",
        ];

        foreach (ConfigurationPlannedChange change in dryRunResult.PlanResult.Changes)
        {
            lines.Add($"  {change.Sequence}. {GetPlannedActionText(change)}");
        }

        lines.Add("note: dry-run only; no files, credentials, or caches are changed in phase 10");
        return JoinLines(lines);
    }

    private static string BuildNuGetConfigureDryRunOutput(
        CliInvocation invocation,
        NuGetPhase10ConfigureDryRunResult dryRunResult
    )
    {
        ArgumentNullException.ThrowIfNull(dryRunResult);

        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            "ecosystem: nuget",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: no",
            $"configuration-plan: {GetValidityText(dryRunResult.Validation.IsValid)}",
            $"planned-change-count: {dryRunResult.PlanResult.Changes.Count}",
            "planned-actions:",
        ];

        foreach (ConfigurationPlannedChange change in dryRunResult.PlanResult.Changes)
        {
            lines.Add($"  {change.Sequence}. {GetPlannedActionText(change)}");
        }

        lines.Add("note: dry-run only; no files, credentials, or caches are changed in phase 10");
        return JoinLines(lines);
    }

    private static string BuildGitConfigureOutput(
        CliInvocation invocation,
        GitPhase8ConfigureResult configureResult
    )
    {
        ArgumentNullException.ThrowIfNull(configureResult);

        return JoinLines(
            $"command: {invocation.CommandName}",
            "ecosystem: git",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: yes",
            $"plan-operation: {GetPlanOperationText(configureResult.PlanResult.Operation)}",
            $"applied-change-count: {configureResult.PlanResult.Changes.Count}",
            $"owned-git-entries: {GetPresenceText(configureResult.OwnedGitEntriesPresent)}",
            $"ownership-manifest: {GetPresenceText(configureResult.OwnershipManifestPresent)}",
            "note: credential material is not printed"
        );
    }

    private static string BuildGitUnconfigureOutput(
        CliInvocation invocation,
        GitPhase8UnconfigureResult unconfigureResult
    )
    {
        ArgumentNullException.ThrowIfNull(unconfigureResult);

        ConfigurationPlanResult? planResult = unconfigureResult.PlanResult;
        return JoinLines(
            $"command: {invocation.CommandName}",
            "ecosystem: git",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: yes",
            "plan-operation: "
                + (planResult is null ? "not-needed" : GetPlanOperationText(planResult.Operation)),
            $"removed-change-count: {planResult?.Changes.Count ?? 0}",
            $"owned-git-entries: {GetPresenceText(unconfigureResult.OwnedGitEntriesPresent)}",
            $"ownership-manifest: {GetPresenceText(unconfigureResult.OwnershipManifestPresent)}",
            "note: credential material is not printed"
        );
    }

    private static string BuildNuGetConfigureOutput(
        CliInvocation invocation,
        NuGetPhase10ConfigureResult configureResult
    )
    {
        ArgumentNullException.ThrowIfNull(configureResult);

        return JoinLines(
            $"command: {invocation.CommandName}",
            "ecosystem: nuget",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: yes",
            $"plan-operation: {GetPlanOperationText(configureResult.PlanResult.Operation)}",
            $"applied-change-count: {configureResult.PlanResult.Changes.Count}",
            "nuget-plugin-layout-marker: "
                + GetPresenceText(configureResult.PluginLayoutMarkerPresent),
            $"ownership-manifest: {GetPresenceText(configureResult.OwnershipManifestPresent)}",
            "note: credential material is not printed"
        );
    }

    private static string BuildNuGetUnconfigureOutput(
        CliInvocation invocation,
        NuGetPhase10UnconfigureResult unconfigureResult
    )
    {
        ArgumentNullException.ThrowIfNull(unconfigureResult);

        ConfigurationPlanResult? planResult = unconfigureResult.PlanResult;
        return JoinLines(
            $"command: {invocation.CommandName}",
            "ecosystem: nuget",
            $"phase: {PhaseName}",
            $"ci-mode: {GetCiModeText(invocation.CiMode)}",
            $"scope: {GetScopeText(invocation.CiMode)}",
            "mutates-state: yes",
            "plan-operation: "
                + (planResult is null ? "not-needed" : GetPlanOperationText(planResult.Operation)),
            $"removed-change-count: {planResult?.Changes.Count ?? 0}",
            "nuget-plugin-layout-marker: "
                + GetPresenceText(unconfigureResult.PluginLayoutMarkerPresent),
            $"ownership-manifest: {GetPresenceText(unconfigureResult.OwnershipManifestPresent)}",
            "note: credential material is not printed"
        );
    }

    private static string BuildDoctorOutput(
        CliInvocation invocation,
        GitPhase8DoctorResult doctorResult,
        NuGetPhase10DoctorResult nuGetDoctorResult,
        PythonPhase11DoctorResult pythonDoctorResult,
        ConfigurationPhase14DoctorResult configurationDoctorResult,
        CredentialProviderCompositionRoot root,
        CredentialProviderReadiness readiness,
        bool doctorSuccess
    )
    {
        ArgumentNullException.ThrowIfNull(doctorResult);
        ArgumentNullException.ThrowIfNull(nuGetDoctorResult);
        ArgumentNullException.ThrowIfNull(pythonDoctorResult);
        ArgumentNullException.ThrowIfNull(configurationDoctorResult);

        bool deviceCodeReady = IsDeviceCodeReady(root, readiness);
        List<string> lines =
        [
            $"command: {invocation.CommandName}",
            $"phase: {PhaseName}",
            $"composition-mode: {root.Mode}",
            $"provider: {root.ProviderConfig.Selection}",
            "interactive-readiness: "
                + (readiness.Interactive.IsReady ? "interactive-ready" : "unavailable"),
            $"interactive-readiness-code: {readiness.Interactive.Code}",
            "silent-readiness: "
                + (readiness.Silent.IsReady ? "silent-ready" : "silent-unavailable"),
            $"silent-readiness-code: {readiness.Silent.Code}",
            $"silent-remediation: {readiness.Silent.SafeMessage}",
            $"configuration-plan: {GetCheckStatusText(doctorResult.ConfigurationPlanValid)}",
            $"owned-git-entries: {GetPresenceText(doctorResult.OwnedGitEntriesPresent)}",
            $"ownership-manifest: {GetPresenceText(doctorResult.OwnershipManifestPresent)}",
            "dev.azure.com-useHttpPath: "
                + GetPresenceText(doctorResult.DevAzureUseHttpPathPresent),
            $"credential-core: {GetCheckStatusText(doctorResult.CredentialCoreSuccess)}",
            "git-credential-helper-get: "
                + GetCheckStatusText(doctorResult.GitCredentialHelperGetSuccess),
            "git-credential-helper-store: "
                + GetCheckStatusText(doctorResult.GitCredentialHelperStoreSuccess),
            "git-credential-helper-erase: "
                + GetCheckStatusText(doctorResult.GitCredentialHelperEraseSuccess),
            "local-shell-helper-shorthand: " + GetLocalShellHelperShorthandStatusText(doctorResult),
            "protocol-payload: "
                + (doctorResult.ProtocolPayloadCaptured ? "captured-not-printed" : "not-captured"),
            "auth-accepted-identity-flows: "
                + (
                    deviceCodeReady
                        ? "browser, device-code, azure-pipelines"
                        : "browser, azure-pipelines"
                ),
            "auth-unavailable-identity-flows: " + (deviceCodeReady ? "none" : "device-code"),
            "auth-deferred-identity-flows: pat-compatibility, service-principal, "
                + "managed-identity, workload-identity",
            "auth-pat-compatibility: deferred-disabled",
            "auth-persistent-derived-credentials: disabled",
            "auth-product-plaintext-fallback: disabled",
        ];
        if (!readiness.Interactive.IsReady)
        {
            lines.Insert(6, $"interactive-blocker: {readiness.Interactive.SafeMessage}");
        }

        lines.AddRange(BuildNuGetDoctorLines(nuGetDoctorResult));
        lines.AddRange(BuildPythonDoctorLines(pythonDoctorResult));
        lines.AddRange(BuildConfigurationPhase14DoctorLines(configurationDoctorResult));
        lines.Add(
            "doctor-aggregation: " + GetCheckStatusText(doctorSuccess)
        );

        return JoinLines(lines);
    }

    private static bool IsDeviceCodeReady(
        CredentialProviderCompositionRoot root,
        CredentialProviderReadiness readiness
    ) =>
        readiness.Interactive.IsReady
        && root.Installation.HostPlatform == AzureAuthHostPlatform.NativeLinux
        && root.ProductionOptions.DeviceCodePromptWriter is not null;

    private static IEnumerable<string> BuildNuGetDoctorLines(NuGetPhase10DoctorResult doctorResult)
    {
        ArgumentNullException.ThrowIfNull(doctorResult);
        return
        [
            "nuget-configuration-plan: " + GetCheckStatusText(doctorResult.ConfigurationPlanValid),
            "nuget-plugin-layout-marker: "
                + GetPresenceText(doctorResult.PluginLayoutMarkerPresent),
            "nuget-ownership-manifest: " + GetPresenceText(doctorResult.OwnershipManifestPresent),
            "nuget-netcore-plugin-entrypoint: "
                + GetCheckStatusText(doctorResult.NetCorePluginEntrypointPresent),
            "nuget-plugin-mode-entrypoint: "
                + GetCheckStatusText(doctorResult.PluginModeEntrypointResolvable),
            "nuget-azure-artifacts-source: "
                + GetCheckStatusText(doctorResult.AzureArtifactsSourceCanonicalizationSuccess),
            "nuget-interactive-policy: "
                + GetCheckStatusText(doctorResult.InteractivePolicyGuidanceSuccess),
            "nuget-environment-overrides: "
                + (doctorResult.OptionalEnvironmentOverridesAbsent ? "absent" : "present"),
        ];
    }

    private static IEnumerable<string> BuildPythonDoctorLines(
        PythonPhase11DoctorResult doctorResult
    )
    {
        ArgumentNullException.ThrowIfNull(doctorResult);
        return
        [
            "python-keyring-shim-exists: "
                + GetCheckStatusText(doctorResult.KeyringShim.ExpectedShimExists),
            "python-keyring-shim-first-on-path: "
                + GetCheckStatusText(doctorResult.KeyringShim.ExpectedShimFirstOnPath),
            "python-keyring-module: "
                + GetCheckStatusText(doctorResult.KeyringModuleProbe.KeyringModuleResolvable),
            "python-azure-artifacts-endpoint-canonicalization: "
                + GetCheckStatusText(
                    doctorResult.AzureArtifactsPythonEndpointCanonicalizationSuccess
                ),
        ];
    }

    private static List<string> BuildConfigurationPhase14DoctorLines(
        ConfigurationPhase14DoctorResult doctorResult
    )
    {
        ArgumentNullException.ThrowIfNull(doctorResult);
        List<string> lines =
        [
            "configuration-aggregation: "
                + GetCheckStatusText(IsConfigurationPhase14DoctorSuccess(doctorResult)),
        ];

        foreach (
            ConfigurationPhase14EcosystemDoctorResult ecosystemResult in doctorResult
                .Ecosystems.OrderBy(static result => GetEcosystemText(result.Ecosystem))
                .ThenBy(static result => result.Scope)
        )
        {
            string prefix = GetConfigurationPhase14DoctorPrefix(ecosystemResult);
            lines.Add(
                $"{prefix}-configuration-plan: "
                    + GetCheckStatusText(ecosystemResult.ConfigurationPlanValid)
            );
            lines.Add(
                $"{prefix}-owned-targets: " + GetPresenceText(ecosystemResult.OwnedTargetPresent)
            );
            lines.Add(
                $"{prefix}-ownership-manifest: "
                    + GetPresenceText(ecosystemResult.OwnershipManifestPresent)
            );
            if (IsPackageRegistryEcosystem(ecosystemResult.Ecosystem))
            {
                lines.Add(
                    $"{prefix}-lifecycle: " + GetLifecycleStateText(ecosystemResult.LifecycleState)
                );
                if (ecosystemResult.CredentialExpiresAt is { } expiresAt)
                {
                    lines.Add($"{prefix}-expires-at: {expiresAt:O}");
                }
            }
            if (ecosystemResult.Scope == ConfigurationPhase14Scope.CiTemporary)
            {
                lines.Add(
                    $"{prefix}-temporary-container: "
                        + GetPresenceText(ecosystemResult.TemporaryContainerPresent)
                );
            }

            if (ShouldEmitConfigurationPhase14Remediation(ecosystemResult))
            {
                lines.Add(
                    $"{prefix}-remediation: "
                        + GetConfigurationPhase14RemediationCommand(ecosystemResult)
                );
            }
        }

        lines.Add(
            "ci-system-access-token: "
                + GetPresenceText(doctorResult.AzurePipelinesSystemAccessTokenPresent)
        );
        lines.Add("ci-temporary-cleanup-command: " + $"{CommandName} cleanup --ci azure-pipelines");
        lines.Add("ci-guidance: set SYSTEM_ACCESSTOKEN and use --ci azure-pipelines in CI");
        lines.Add(
            "persistent-derived-credential-cache: "
                + (doctorResult.PersistentDerivedCredentialCacheEnabled ? "enabled" : "disabled")
        );
        return lines;
    }

    private static string GetLocalShellHelperShorthandStatusText(GitPhase8DoctorResult doctorResult)
    {
        if (doctorResult.LocalShellHelperShorthandDeferred)
        {
            return "unsupported-mvp";
        }

        return GetCheckStatusText(doctorResult.LocalShellHelperShorthandSuccess);
    }

    private static string[] GetPlannedActions(
        CliCommand command,
        CredentialEcosystem ecosystem,
        CliCiMode ciMode
    )
    {
        bool configure = command == CliCommand.Configure;
        bool ciTemporary = ciMode == CliCiMode.AzurePipelines;

        return ecosystem switch
        {
            CredentialEcosystem.Git => configure
                ? ciTemporary
                    ?
                    [
                        "prepare temporary Azure Pipelines git credential helper scaffold",
                        "prepare temporary dev.azure.com useHttpPath scaffold",
                    ]
                    :
                    [
                        "set product-owned git credential.helper entry",
                        "set product-owned dev.azure.com useHttpPath entry",
                    ]
                : ciTemporary
                    ?
                    [
                        "remove temporary Azure Pipelines git credential helper scaffold",
                        "remove temporary dev.azure.com useHttpPath scaffold",
                    ]
                    :
                    [
                        "remove product-owned git credential.helper entry",
                        "remove product-owned dev.azure.com useHttpPath entry",
                    ],
            CredentialEcosystem.NuGet => configure
                ? ciTemporary
                    ?
                    [
                        "prepare temporary Azure Pipelines NuGet plugin discovery scaffold",
                        "prepare temporary Azure Artifacts NuGet credential scaffold",
                    ]
                    :
                    [
                        "register product-owned NuGet plugin discovery scaffold",
                        "register product-owned Azure Artifacts NuGet credential scaffold",
                    ]
                : ciTemporary
                    ?
                    [
                        "remove temporary Azure Pipelines NuGet plugin discovery scaffold",
                        "remove temporary Azure Artifacts NuGet credential scaffold",
                    ]
                    :
                    [
                        "remove product-owned NuGet plugin discovery scaffold",
                        "remove product-owned Azure Artifacts NuGet credential scaffold",
                    ],
            _ => throw new InvalidOperationException("Unsupported dry-run ecosystem."),
        };
    }

    private static GitPhase8VerticalSliceService CreateGitPhase8VerticalSliceService(
        CliRuntimeOptions? runtimeOptions
    )
    {
        return GetCompositionRoot(runtimeOptions)
            .CreateGitService(runtimeOptions?.GitPhase8Options);
    }

    private static NuGetPhase10VerticalSliceService CreateNuGetPhase10VerticalSliceService(
        CliRuntimeOptions? runtimeOptions
    )
    {
        return GetCompositionRoot(runtimeOptions)
            .CreateNuGetService(runtimeOptions?.NuGetPhase10Options);
    }

    private static PythonPhase11VerticalSliceService CreatePythonPhase11VerticalSliceService(
        CliRuntimeOptions? runtimeOptions
    ) => new(runtimeOptions?.PythonPhase11Options);

    private static GitPhase8VerticalSliceService CreateGitPhase8ConfigurationService(
        CliRuntimeOptions? runtimeOptions
    ) => GitPhase8VerticalSliceService.CreateConfigurationOnly(runtimeOptions?.GitPhase8Options);

    private static NuGetPhase10VerticalSliceService CreateNuGetPhase10ConfigurationService(
        CliRuntimeOptions? runtimeOptions
    ) =>
        NuGetPhase10VerticalSliceService.CreateConfigurationOnly(
            runtimeOptions?.NuGetPhase10Options
        );

    private static AuthPhase14VerticalSliceService CreateAuthPhase14VerticalSliceService(
        CliRuntimeOptions? runtimeOptions,
        bool requireCredentialProvider = true
    )
    {
        return requireCredentialProvider
            ? GetCompositionRoot(runtimeOptions)
                .CreateAuthService(runtimeOptions?.AuthPhase14Options)
            : new AuthPhase14VerticalSliceService(runtimeOptions?.AuthPhase14Options);
    }

    // editorconfig-checker-disable
    private static ConfigurationPhase14VerticalSliceService CreateConfigurationPhase14VerticalSliceService(
        CliRuntimeOptions? runtimeOptions,
        CredentialEcosystem? registryEcosystem = null,
        Uri? registryUrl = null,
        bool requireCredentialProvider = true
    )
    // editorconfig-checker-enable
    {
        ConfigurationPhase14VerticalSliceOptions? options =
            runtimeOptions?.ConfigurationPhase14Options;
        if (registryEcosystem is not null && registryUrl is not null)
        {
            var registryUrls = options?.RegistryUrls is null
                ? new Dictionary<CredentialEcosystem, Uri>()
                : new Dictionary<CredentialEcosystem, Uri>(options.RegistryUrls);
            registryUrls[registryEcosystem.Value] = registryUrl;
            options = (options ?? new ConfigurationPhase14VerticalSliceOptions()) with
            {
                RegistryUrls = registryUrls,
            };
        }

        if (requireCredentialProvider)
        {
            options = (options ?? new ConfigurationPhase14VerticalSliceOptions()) with
            {
                CredentialAcquisitionFactory = () =>
                    GetCompositionRoot(runtimeOptions).AcquisitionService,
            };
        }

        return new ConfigurationPhase14VerticalSliceService(options);
    }

    private static bool RequiresCredentialProviderForConfigure(
        CredentialEcosystem ecosystem,
        CliCiMode ciMode
    ) =>
        ciMode != CliCiMode.AzurePipelines
        && ecosystem
            is CredentialEcosystem.Npm
                or CredentialEcosystem.Pnpm
                or CredentialEcosystem.Yarn;

    private static string GetPlannedActionText(ConfigurationPlannedChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return (change.Operation, change.TargetKind, change.Key) switch
        {
            (
                ConfigurationChangeOperation.Set,
                ConfigurationTargetKind.GitConfig,
                GitCredentialHelperConfigurationKey
            ) => "set product-owned git credential.helper entry",
            (
                ConfigurationChangeOperation.Set,
                ConfigurationTargetKind.GitConfig,
                GitUseHttpPathConfigurationKey
            ) => "set product-owned dev.azure.com useHttpPath entry",
            (
                ConfigurationChangeOperation.Remove,
                ConfigurationTargetKind.GitConfig,
                GitCredentialHelperConfigurationKey
            ) => "remove product-owned git credential.helper entry",
            (
                ConfigurationChangeOperation.Remove,
                ConfigurationTargetKind.GitConfig,
                GitUseHttpPathConfigurationKey
            ) => "remove product-owned dev.azure.com useHttpPath entry",
            (
                ConfigurationChangeOperation.Set,
                ConfigurationTargetKind.NuGetPluginLayout,
                NuGetPluginLayoutConfigurationKey
            ) => "register product-owned NuGet netcore plugin layout marker",
            (
                ConfigurationChangeOperation.Remove,
                ConfigurationTargetKind.NuGetPluginLayout,
                NuGetPluginLayoutConfigurationKey
            ) => "remove product-owned NuGet netcore plugin layout marker",
            _ => throw new InvalidOperationException("Unsupported planned change."),
        };
    }

    private static bool IsGitDoctorSuccess(GitPhase8DoctorResult doctorResult)
    {
        ArgumentNullException.ThrowIfNull(doctorResult);

        return doctorResult.ConfigurationPlanValid
            && doctorResult.OwnedGitEntriesPresent
            && doctorResult.OwnershipManifestPresent
            && doctorResult.CredentialCoreSuccess
            && doctorResult.GitCredentialHelperGetSuccess
            && doctorResult.GitCredentialHelperStoreSuccess
            && doctorResult.GitCredentialHelperEraseSuccess
            && doctorResult.LocalShellHelperShorthandSuccess
            && doctorResult.DevAzureUseHttpPathPresent;
    }

    private static bool IsNuGetDoctorSuccess(NuGetPhase10DoctorResult doctorResult)
    {
        ArgumentNullException.ThrowIfNull(doctorResult);

        return doctorResult.ConfigurationPlanValid
            && doctorResult.AzureArtifactsSourceCanonicalizationSuccess
            && doctorResult.InteractivePolicyGuidanceSuccess
            && doctorResult.OptionalEnvironmentOverridesAbsent
            && (
                NuGetDoctorStateAbsent(doctorResult)
                || (
                    doctorResult.PluginLayoutMarkerPresent
                    && doctorResult.OwnershipManifestPresent
                    && doctorResult.NetCorePluginEntrypointPresent
                    && doctorResult.PluginModeEntrypointResolvable
                )
            );
    }

    private static bool NuGetDoctorStateAbsent(NuGetPhase10DoctorResult doctorResult) =>
        !doctorResult.PluginLayoutMarkerPresent
        && !doctorResult.OwnershipManifestPresent
        && !doctorResult.NetCorePluginEntrypointPresent;

    private static bool IsPythonDoctorSuccess(PythonPhase11DoctorResult doctorResult)
    {
        ArgumentNullException.ThrowIfNull(doctorResult);

        return doctorResult.KeyringShim.ExpectedShimExists
            && doctorResult.KeyringShim.ExpectedShimFirstOnPath
            && doctorResult.KeyringModuleProbe.KeyringModuleResolvable
            && doctorResult.AzureArtifactsPythonEndpointCanonicalizationSuccess;
    }

    private static bool IsConfigurationPhase14DoctorSuccess(
        ConfigurationPhase14DoctorResult doctorResult
    )
    {
        ArgumentNullException.ThrowIfNull(doctorResult);
        return doctorResult
                .Ecosystems.Where(static result => result.Scope == ConfigurationPhase14Scope.User)
                .All(IsConfigurationPhase14EcosystemDoctorSuccess)
            && doctorResult
                .Ecosystems.Where(static result =>
                    result.Scope == ConfigurationPhase14Scope.CiTemporary
                )
                .All(IsConfigurationPhase14EcosystemDoctorSuccess);
    }

    private static bool IsConfigurationPhase14EcosystemDoctorSuccess(
        ConfigurationPhase14EcosystemDoctorResult doctorResult
    )
    {
        ArgumentNullException.ThrowIfNull(doctorResult);
        if (doctorResult.Scope == ConfigurationPhase14Scope.CiTemporary)
        {
            return doctorResult.ConfigurationPlanValid
                && (
                    (
                        !doctorResult.OwnershipManifestPresent
                        && !doctorResult.OwnedTargetPresent
                        && !doctorResult.TemporaryContainerPresent
                    )
                    || (
                        doctorResult.OwnershipManifestPresent
                        && doctorResult.OwnedTargetPresent
                        && IsAcceptableLifecycleState(doctorResult.LifecycleState)
                    )
                );
        }

        return doctorResult.ConfigurationPlanValid
            && (
                ConfigurationPhase14DoctorStateAbsent(doctorResult)
                || (
                    doctorResult.OwnershipManifestPresent
                    && doctorResult.OwnedTargetPresent
                    && (
                        !IsPackageRegistryEcosystem(doctorResult.Ecosystem)
                        || IsAcceptableLifecycleState(doctorResult.LifecycleState)
                    )
                )
            );
    }

    private static bool ConfigurationPhase14DoctorStateAbsent(
        ConfigurationPhase14EcosystemDoctorResult doctorResult
    ) => !doctorResult.OwnershipManifestPresent && !doctorResult.OwnedTargetPresent;

    private static bool IsAcceptableLifecycleState(RegistryCredentialLifecycleState state) =>
        state
            is RegistryCredentialLifecycleState.Fresh
                or RegistryCredentialLifecycleState.RefreshRecommended;

    private static bool IsConfigurationPhase14CleanupSuccess(
        ConfigurationPhase14CleanupResult cleanupResult
    )
    {
        ArgumentNullException.ThrowIfNull(cleanupResult);
        return IsConfigurationPhase14CleanupPlanComplete(cleanupResult)
            && cleanupResult.Ecosystems.All(static result =>
                !result.TemporaryContainerPresent && !result.OwnershipManifestPresent
            );
    }

    private static bool IsConfigurationPhase14CleanupPlanComplete(
        ConfigurationPhase14CleanupResult cleanupResult
    )
    {
        ArgumentNullException.ThrowIfNull(cleanupResult);
        return cleanupResult.Ecosystems.All(static result =>
            result.State is "removed" or "not-needed"
        );
    }

    private static void AddIncompleteCleanupRemediation(
        List<string> lines,
        ConfigurationPhase14CleanupResult cleanupResult
    )
    {
        foreach (
            ConfigurationPhase14CleanupEcosystemResult result in cleanupResult
                .Ecosystems.Where(static result => result.State is not ("removed" or "not-needed"))
                .OrderBy(static result => GetEcosystemText(result.Ecosystem))
                .ThenBy(static result => result.Scope)
        )
        {
            string ecosystem = GetEcosystemText(result.Ecosystem);
            string command =
                result.Scope == ConfigurationPhase14Scope.CiTemporary
                    ? $"{CommandName} cleanup {ecosystem} --ci azure-pipelines"
                    : $"{CommandName} unconfigure {ecosystem}";
            lines.Add($"{GetConfigurationPhase14CleanupPrefix(result)}-remediation: {command}");
        }
    }

    private static string JoinCheckIds(
        IEnumerable<ReleaseHardeningPhase15Check> checks,
        ReleaseHardeningPhase15CheckStatus status
    )
    {
        string[] checkIds = checks
            .Where(check => check.Status == status)
            .Select(static check => check.Id)
            .ToArray();
        return checkIds.Length == 0 ? "none" : string.Join(", ", checkIds);
    }

    private static string GetPhase15CheckStatusText(ReleaseHardeningPhase15CheckStatus status)
    {
        return status switch
        {
            ReleaseHardeningPhase15CheckStatus.Pass => "pass",
            ReleaseHardeningPhase15CheckStatus.DeferredNonMvp => "deferred-non-mvp",
            ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence =>
                "deferred-release-evidence",
            ReleaseHardeningPhase15CheckStatus.DeferredOptionalFeature =>
                "deferred-optional-feature",
            ReleaseHardeningPhase15CheckStatus.Blocked => "blocked",
            _ => throw new InvalidOperationException("Unsupported Phase 15 check status."),
        };
    }

    private static string GetYesNo(bool value) => value ? "yes" : "no";

    private static bool ShouldEmitConfigurationPhase14Remediation(
        ConfigurationPhase14EcosystemDoctorResult doctorResult
    )
    {
        return !IsConfigurationPhase14EcosystemDoctorSuccess(doctorResult)
            || doctorResult.LifecycleState == RegistryCredentialLifecycleState.RefreshRecommended
            || (
                doctorResult.Scope == ConfigurationPhase14Scope.User
                && ConfigurationPhase14DoctorStateAbsent(doctorResult)
            );
    }

    private static string GetConfigurationPhase14DoctorPrefix(
        ConfigurationPhase14EcosystemDoctorResult doctorResult
    )
    {
        return GetEcosystemText(doctorResult.Ecosystem)
            + "-"
            + GetConfigurationPhase14ScopeText(doctorResult.Scope);
    }

    private static string GetConfigurationPhase14CleanupPrefix(
        ConfigurationPhase14CleanupEcosystemResult cleanupResult
    )
    {
        return GetEcosystemText(cleanupResult.Ecosystem)
            + "-"
            + GetConfigurationPhase14ScopeText(cleanupResult.Scope);
    }

    private static string GetConfigurationPhase14RemediationCommand(
        ConfigurationPhase14EcosystemDoctorResult doctorResult
    )
    {
        if (doctorResult.Scope == ConfigurationPhase14Scope.CiTemporary)
        {
            return $"{CommandName} cleanup {GetEcosystemText(doctorResult.Ecosystem)} "
                + "--ci azure-pipelines";
        }

        string ecosystem = GetEcosystemText(doctorResult.Ecosystem);
        if (
            doctorResult.LifecycleState
            is RegistryCredentialLifecycleState.RefreshRecommended
                or RegistryCredentialLifecycleState.Expired
        )
        {
            return $"{CommandName} refresh {ecosystem} --registry-url "
                + (doctorResult.RegistryUrl?.AbsoluteUri ?? "<azure-artifacts-npm-registry-url>");
        }

        return IsPackageRegistryEcosystem(doctorResult.Ecosystem)
            ? $"{CommandName} configure {ecosystem} --registry-url "
                + "<azure-artifacts-npm-registry-url>"
            : $"{CommandName} configure {ecosystem}";
    }

    private static string GetLifecycleStateText(RegistryCredentialLifecycleState state) =>
        state switch
        {
            RegistryCredentialLifecycleState.Missing => "missing",
            RegistryCredentialLifecycleState.Fresh => "fresh",
            RegistryCredentialLifecycleState.RefreshRecommended => "refresh-recommended",
            RegistryCredentialLifecycleState.Expired => "expired",
            _ => "invalid",
        };

    private static string GetConfigurationPhase14ScopeText(ConfigurationPhase14Scope scope)
    {
        return scope switch
        {
            ConfigurationPhase14Scope.User => "user",
            ConfigurationPhase14Scope.CiTemporary => "ci-temporary",
            _ => throw new InvalidOperationException("Unsupported Phase 14 scope."),
        };
    }

    private static string GetPlanOperationText(ConfigurationPlanOperation operation)
    {
        return operation.ToString().ToLowerInvariant();
    }

    private static string GetValidityText(bool isValid) => isValid ? "valid" : "invalid";

    private static string GetCheckStatusText(bool value) => value ? "pass" : "fail";

    private static string GetPresenceText(bool value) => value ? "present" : "absent";

    private static string GetCommandName(CliCommand command)
    {
        return command switch
        {
            CliCommand.Status => "status",
            CliCommand.Doctor => "doctor",
            CliCommand.Cleanup => "cleanup",
            CliCommand.Login => "login",
            CliCommand.Logout => "logout",
            CliCommand.Identity => "identity",
            CliCommand.Configure => "configure",
            CliCommand.Refresh => "refresh",
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
            CredentialEcosystem.Pnpm => "pnpm",
            CredentialEcosystem.Yarn => "yarn",
            _ => throw new InvalidOperationException("Unsupported ecosystem."),
        };
    }

    private static bool IsPhase14ConfigurationEcosystem(CredentialEcosystem ecosystem) =>
        ecosystem
            is CredentialEcosystem.Python
                or CredentialEcosystem.Npm
                or CredentialEcosystem.Pnpm
                or CredentialEcosystem.Yarn;

    private static bool IsPackageRegistryEcosystem(CredentialEcosystem ecosystem) =>
        ecosystem
            is CredentialEcosystem.Npm
                or CredentialEcosystem.Pnpm
                or CredentialEcosystem.Yarn;

    private static ConfigurationPhase14Scope GetConfigurationPhase14Scope(CliCiMode ciMode) =>
        ciMode == CliCiMode.AzurePipelines
            ? ConfigurationPhase14Scope.CiTemporary
            : ConfigurationPhase14Scope.User;

    private static CancellationToken GetCancellationToken(CliRuntimeOptions? runtimeOptions) =>
        runtimeOptions?.CancellationToken ?? CancellationToken.None;

    private static string GetIdentityFlowText(IdentityFlow flow)
    {
        return flow switch
        {
            IdentityFlow.InteractiveBrowser => "browser",
            IdentityFlow.DeviceCode => "device-code",
            IdentityFlow.PatCompatibility => "pat",
            IdentityFlow.AzurePipelinesSystemAccessToken => "azure-pipelines",
            IdentityFlow.ServicePrincipal => "service-principal",
            IdentityFlow.ManagedIdentity => "managed-identity",
            IdentityFlow.WorkloadIdentityFederation => "workload-identity",
            _ => "unsupported",
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
            bool isSurrogatePair =
                char.IsHighSurrogate(value[index])
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
        return category
            is UnicodeCategory.Control
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
        UnicodeCategory category
    )
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

            bool isSurrogatePair =
                char.IsHighSurrogate(optionName[index])
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
        Justification = "Stderr diagnostics must not override the intended process exit code."
    )]
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
    string? HelpText
)
{
    public CliAuthOptions AuthOptions { get; init; } = new();

    public CliIdentityOptions IdentityOptions { get; init; } = new();

    public Uri? RegistryUrl { get; init; }

    public string CommandName => CliApplicationCommandNames.Get(Command);

    public static CliInvocation CreateHelp(string helpText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helpText);
        return new CliInvocation(
            CliCommand.Help,
            null,
            CliCiMode.None,
            DryRun: false,
            HelpText: helpText
        );
    }
}

internal sealed record CliAuthOptions
{
    public IdentityFlow IdentityFlow { get; init; } = IdentityFlow.InteractiveBrowser;

    public string? AccountHint { get; init; }

    public string? TenantHint { get; init; }

    public bool ExplicitPatMaterialProvided { get; init; }

    public string? DeferredFlowName { get; init; }
}

internal sealed record CliIdentityOptions
{
    public CliIdentityAction Action { get; init; }

    public string? TenantId { get; init; }

    public string? AccountPreference { get; init; }
}

internal enum CliIdentityAction
{
    Unknown = 0,
    Configure = 1,
    Reconfigure = 2,
    Unconfigure = 3,
}

internal enum CliCommand
{
    Unknown = 0,
    Help = 1,
    Status = 2,
    Doctor = 3,
    Cleanup = 4,
    Acceptance = 5,
    Login = 6,
    Logout = 7,
    Configure = 8,
    Unconfigure = 9,
    Refresh = 10,
    Identity = 11,
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
            CliCommand.Cleanup => "cleanup",
            CliCommand.Acceptance => "acceptance",
            CliCommand.Login => "login",
            CliCommand.Logout => "logout",
            CliCommand.Identity => "identity",
            CliCommand.Configure => "configure",
            CliCommand.Refresh => "refresh",
            CliCommand.Unconfigure => "unconfigure",
            CliCommand.Help => "help",
            _ => "unknown",
        };
    }
}

internal sealed record CliRuntimeOptions
{
    public CredentialProviderCompositionRoot? CompositionRoot { get; init; }

    public Func<CredentialProviderCompositionRoot>? CompositionRootFactory { get; init; }

    public GitPhase8VerticalSliceOptions? GitPhase8Options { get; init; }

    public NuGetPhase10VerticalSliceOptions? NuGetPhase10Options { get; init; }

    public PythonPhase11VerticalSliceOptions? PythonPhase11Options { get; init; }

    public AuthPhase14VerticalSliceOptions? AuthPhase14Options { get; init; }

    public ConfigurationPhase14VerticalSliceOptions? ConfigurationPhase14Options { get; init; }

    public CredentialProviderIdentityConfigurationService? IdentityConfiguration { get; init; }

    public CancellationToken CancellationToken { get; init; }
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
