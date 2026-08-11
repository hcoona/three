using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class GitPhase8DoctorTests
{
    [Fact]
    public async Task DoctorUsesGitConfigDiscoveryForConfiguredHelper()
    {
        string stateDirectory = CreateTestDirectory("doctor path (preview)");
        var processRunner = new RecordingGitDiscoveryProcessRunner();
        var service = CreateService(stateDirectory, processRunner, gitExecutablePath: "git-probe");

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(2, processRunner.StartSpecs.Count);
            ProcessStartSpec startSpec = processRunner.StartSpecs[0];
            Assert.True(result.LocalShellHelperShorthandSuccess);
            Assert.Equal(
                GitUseHttpPathInspectionState.Present,
                result.DevAzureUseHttpPath.State
            );
            Assert.Equal("git-probe", startSpec.FileName);
            Assert.Equal(
                [
                    "config",
                    "--includes",
                    "--show-scope",
                    "--null",
                    "--get-regexp",
                    @"^credential(\..*)?\.helper$",
                ],
                startSpec.Arguments
            );
            Assert.Null(startSpec.StandardInput);
            Assert.False(startSpec.Environment.ContainsKey("GIT_CONFIG_GLOBAL"));
            Assert.Equal(
                service.Paths.UserHomeDirectoryPath,
                startSpec.Environment["HOME"]
            );
            Assert.True(processRunner.HelperAliasWasPresent);
            Assert.All(
                processRunner.FallbackStartSpecs,
                fallback =>
                {
                    Assert.Null(fallback.Environment["GIT_CONFIG"]);
                    Assert.Null(fallback.Environment["GIT_CONFIG_GLOBAL"]);
                    Assert.Equal("1", fallback.Environment["GIT_CONFIG_NOSYSTEM"]);
                }
            );
            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                result.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Product],
                result.EffectiveCredentialHelper.EffectiveOrder
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
    public async Task UnconfigureRejectsActiveActivationWhenOwnedSelectorsAreAbsent(bool dryRun)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.WriteAllText(service.Paths.GitConfigPath, string.Empty);
            byte[] activation = File.ReadAllBytes(service.Paths.UserGitConfigPath);
            byte[] manifest = File.ReadAllBytes(service.Paths.OwnershipManifestPath);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
            {
                if (dryRun)
                {
                    await service.ValidateUnconfigureDryRunAsync(
                        TestContext.Current.CancellationToken
                    );
                }
                else
                {
                    await service.UnconfigureAsync(TestContext.Current.CancellationToken);
                }
            });

            Assert.Equal(activation, File.ReadAllBytes(service.Paths.UserGitConfigPath));
            Assert.Empty(File.ReadAllText(service.Paths.GitConfigPath));
            Assert.Equal(manifest, File.ReadAllBytes(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsBypassWhenGitDoesNotReturnExpectedConfiguredHelper()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "global\0credential.helper\nmanager\0",
                string.Empty
            )
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Equal(2, processRunner.StartSpecs.Count);
            Assert.Equal(
                GitEffectiveCredentialHelperState.Bypassed,
                result.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Other],
                result.EffectiveCredentialHelper.EffectiveOrder
            );
            Assert.Equal(
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Unknown,
                    Selector = GitCredentialHelperSelectorKind.Unknown,
                    Directive = GitCredentialHelperConflictDirective.ActivationBypassed,
                },
                result.EffectiveCredentialHelper.Conflict
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsEffectiveThirdPartyOrderWithoutProductConfiguration()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "global\0credential.helper\nmanager\0",
                string.Empty
            )
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.NotConfigured,
                result.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Other],
                result.EffectiveCredentialHelper.EffectiveOrder
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorBoundsOversizedCredentialHelperConfiguration()
    {
        string stateDirectory = CreateTestDirectory();
        string oversizedOutput = string.Concat(
            Enumerable.Repeat("global\0credential.helper\nmanager\0", 129)
        );
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(0, oversizedOutput, string.Empty)
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.DiscoveryFailed,
                result.EffectiveCredentialHelper.State
            );
            Assert.True(result.EffectiveCredentialHelper.ConfigurationTruncated);
            Assert.Empty(result.EffectiveCredentialHelper.EffectiveOrder);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    public static IEnumerable<object[]> AbnormalHelperDiscoveryResults()
    {
        const string Partial = "global\0credential.helper\nmanager\0";
        yield return
        [
            ProcessResult.OutputTooLarge(Partial, string.Empty, exitCode: 0),
            true,
        ];
        yield return
        [
            ProcessResult.InvalidOutput(Partial, string.Empty, exitCode: 0),
            false,
        ];
        yield return
        [
            ProcessResult.TimedOut(Partial, string.Empty, exitCode: 0),
            false,
        ];
    }

    [Theory]
    [MemberData(nameof(AbnormalHelperDiscoveryResults))]
    public async Task DoctorRejectsPartialHelperOutputFromAbnormalProcessResult(
        ProcessResult processResult,
        bool expectedTruncated
    )
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(processResult);
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.DiscoveryFailed,
                result.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                expectedTruncated,
                result.EffectiveCredentialHelper.ConfigurationTruncated
            );
            Assert.Empty(result.EffectiveCredentialHelper.EffectiveOrder);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    public static IEnumerable<object[]> AbnormalUseHttpPathResults()
    {
        const string Partial =
            "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
            + "username=azureauth-use-http-path-present\n"
            + "password=azureauth-use-http-path-probe\n";
        yield return [ProcessResult.OutputTooLarge(Partial, string.Empty, exitCode: 0)];
        yield return [ProcessResult.InvalidOutput(Partial, string.Empty, exitCode: 0)];
        yield return [new ProcessResult(0, "protocol=https\nmalformed\n", string.Empty)];
    }

    [Theory]
    [MemberData(nameof(AbnormalUseHttpPathResults))]
    public async Task DoctorDoesNotReportUseHttpPathFromAbnormalProcessResult(
        ProcessResult processResult
    )
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            useHttpPathResult: processResult
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                result.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                GitUseHttpPathInspectionState.InspectionIncomplete,
                result.DevAzureUseHttpPath.State
            );
            Assert.Equal(
                processResult.Status == ProcessExecutionStatus.OutputTooLarge,
                result.DevAzureUseHttpPath.OutputTruncated
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorDefersLocalShellDiscoveryWhenUnsupported()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(0, string.Empty, string.Empty)
        );
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                LocalShellGitDiscoverySupported = false,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            }
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.True(result.LocalShellHelperShorthandDeferred);
            Assert.Empty(processRunner.StartSpecs);
            Assert.Equal(
                GitEffectiveCredentialHelperState.DiscoveryDeferred,
                result.EffectiveCredentialHelper.State
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsDiscoveryFailureForLaunchFailureResult()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            helperResult: ProcessResult.LaunchFailure()
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.False(result.LocalShellHelperShorthandDeferred);
            Assert.Contains(
                processRunner.StartSpecs,
                startSpec => startSpec.Arguments.Contains("--get-regexp")
            );
            Assert.Equal(
                GitEffectiveCredentialHelperState.DiscoveryFailed,
                result.EffectiveCredentialHelper.State
            );
            Assert.False(result.EffectiveCredentialHelper.ConfigurationTruncated);
            Assert.Empty(result.EffectiveCredentialHelper.EffectiveOrder);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsTruncatedConfigurationWhenUrlMatchOutputIsTooLarge()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            helperResult: new ProcessResult(
                0,
                "global\0credential.https://dev.azure.com/org.helper\nmanager\0",
                string.Empty
            ),
            urlMatchResult: ProcessResult.OutputTooLarge(
                "credential.partial\ntrue\0",
                string.Empty,
                exitCode: 0
            )
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Single(
                processRunner.StartSpecs,
                startSpec => startSpec.Arguments.Contains("--get-urlmatch")
            );
            Assert.Equal(
                GitEffectiveCredentialHelperState.DiscoveryFailed,
                result.EffectiveCredentialHelper.State
            );
            Assert.True(result.EffectiveCredentialHelper.ConfigurationTruncated);
            Assert.Empty(result.EffectiveCredentialHelper.EffectiveOrder);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorRejectsMalformedNulDelimitedUrlMatchOutput()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            helperResult: new ProcessResult(
                0,
                "global\0credential.https://dev.azure.com/org.helper\nmanager\0",
                string.Empty
            ),
            urlMatchResult: new ProcessResult(
                0,
                "credential.unknown\ntrue\0unterminated",
                string.Empty
            )
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Single(
                processRunner.StartSpecs,
                startSpec => startSpec.Arguments.Contains("--get-urlmatch")
            );
            Assert.Equal(
                GitEffectiveCredentialHelperState.DiscoveryFailed,
                result.EffectiveCredentialHelper.State
            );
            Assert.False(result.EffectiveCredentialHelper.ConfigurationTruncated);
            Assert.Empty(result.EffectiveCredentialHelper.EffectiveOrder);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorSeparatesSelectedConfigurationFromNonExecutableHelperArtifact()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner();
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.SetUnixFileMode(
                service.Paths.GitHelperPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
            );

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Equal(2, processRunner.StartSpecs.Count);
            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                result.EffectiveCredentialHelper.State
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData(false, "helper", "manager")]
    [InlineData(true, "helper", "manager")]
    [InlineData(false, "useHttpPath", "false")]
    [InlineData(true, "useHttpPath", "false")]
    public async Task UnconfigureLeavesOverwrittenOwnedSelectorsAndManifestUntouched(
        bool dryRun,
        string variable,
        string replacement
    )
    {
        string stateDirectory = CreateTestDirectory();
        var service = CreateService(stateDirectory, new RecordingGitDiscoveryProcessRunner());

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            string configuredGitConfig = File.ReadAllText(service.Paths.GitConfigPath);
            string overwrittenGitConfig = ReplaceConfiguredValue(
                configuredGitConfig,
                variable,
                replacement
            );
            File.WriteAllText(service.Paths.GitConfigPath, overwrittenGitConfig);
            string manifestBefore = File.ReadAllText(service.Paths.OwnershipManifestPath);
            string helperBefore = File.ReadAllText(service.Paths.GitHelperPath);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
            {
                if (dryRun)
                {
                    await service.ValidateUnconfigureDryRunAsync(
                        TestContext.Current.CancellationToken
                    );
                }
                else
                {
                    await service.UnconfigureAsync(TestContext.Current.CancellationToken);
                }
            });

            Assert.Equal(overwrittenGitConfig, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.Equal(manifestBefore, File.ReadAllText(service.Paths.OwnershipManifestPath));
            Assert.Equal(helperBefore, File.ReadAllText(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureRejectsUnsupportedSharedCliName()
    {
        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductExecutable(
            stateDirectory,
            "renamed-credential-provider"
        );
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)
                ),
                ProductExecutablePath = productExecutablePath,
            }
        );

        try
        {
            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
                await service.ConfigureAsync(TestContext.Current.CancellationToken)
            );
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void ConfiguredProductExecutablePathMustBeFullyQualified()
    {
        Assert.Throws<ArgumentException>(() =>
            new GitPhase8VerticalSliceService(
                new GitPhase8VerticalSliceOptions
                {
                    ProductExecutablePath = "azureauth-credprovider",
                }
            )
        );
    }

    [Fact]
    public async Task ConfigureAndUnconfigurePreserveIsolatedUserGlobalGitConfig()
    {
        string stateDirectory = CreateTestDirectory();
        string homeDirectory = Path.Combine(stateDirectory, "user-home");
        Directory.CreateDirectory(homeDirectory);
        string userGitConfigPath = Path.Combine(homeDirectory, ".gitconfig");
        byte[] original =
        [
            0xEF,
            0xBB,
            0xBF,
            .. System.Text.Encoding.UTF8.GetBytes(
                "# keep\r\n[user]\r\n\tname = Existing User"
            ),
        ];
        File.WriteAllBytes(userGitConfigPath, original);
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            homeDirectory
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            byte[] configured = File.ReadAllBytes(userGitConfigPath);
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            Assert.Equal(configured, File.ReadAllBytes(userGitConfigPath));
            Assert.True(configured.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            string configuredText = System.Text.Encoding.UTF8.GetString(configured[3..]);
            Assert.StartsWith(
                "# keep\r\n[user]\r\n\tname = Existing User\r\n",
                configuredText,
                StringComparison.Ordinal
            );
            Assert.Equal(
                1,
                CountOccurrences(
                    configuredText,
                    "# BEGIN azureauth-credprovider managed include"
                )
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );
            Assert.True(doctor.OwnedGitEntriesPresent);
            Assert.True(doctor.LocalShellHelperShorthandSuccess);
            Assert.Equal(
                GitUseHttpPathInspectionState.Present,
                doctor.DevAzureUseHttpPath.State
            );

            await service.UnconfigureAsync(TestContext.Current.CancellationToken);

            Assert.Equal(original, File.ReadAllBytes(userGitConfigPath));
            Assert.DoesNotContain(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
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
    public async Task ConfigureHonorsGitLockAndPreservesConcurrentCommittedUpdate()
    {
        string stateDirectory = CreateTestDirectory();
        string homeDirectory = Path.Combine(stateDirectory, "user-home");
        Directory.CreateDirectory(homeDirectory);
        string userGitConfigPath = Path.Combine(homeDirectory, ".gitconfig");
        string lockPath = userGitConfigPath + ".lock";
        const string Original = "# original\n";
        const string ConcurrentUpdate =
            "# original\n[user]\n\temail = concurrent@example.test\n";
        File.WriteAllText(userGitConfigPath, Original);
        File.WriteAllText(lockPath, ConcurrentUpdate);
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            homeDirectory
        );

        try
        {
            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
                await service.ConfigureAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(Original, File.ReadAllText(userGitConfigPath));
            Assert.Equal(ConcurrentUpdate, File.ReadAllText(lockPath));

            File.Move(lockPath, userGitConfigPath, overwrite: true);
            GitPhase8ConfigureResult result = await service.ConfigureAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(result.OwnedGitEntriesPresent);
            Assert.Contains(
                "email = concurrent@example.test",
                File.ReadAllText(userGitConfigPath),
                StringComparison.Ordinal
            );
            Assert.False(File.Exists(lockPath));

            await service.UnconfigureAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ConcurrentUpdate, File.ReadAllText(userGitConfigPath));
            Assert.False(File.Exists(lockPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public void GitActivationCleansLockWhenUpdateFails()
    {
        string stateDirectory = CreateTestDirectory();
        string userGitConfigPath = Path.Combine(stateDirectory, ".gitconfig");
        string productGitConfigPath = Path.Combine(stateDirectory, "product.gitconfig");
        const string Malformed =
            "# BEGIN azureauth-credprovider managed include\n[include]\n";
        File.WriteAllText(userGitConfigPath, Malformed);
        var activation = new GitUserGlobalConfigActivation(new SystemFileSystem());

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                activation.EnsurePresent(userGitConfigPath, productGitConfigPath)
            );

            Assert.Equal(Malformed, File.ReadAllText(userGitConfigPath));
            Assert.False(File.Exists(userGitConfigPath + ".lock"));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureWaitsForUnconfigureLifecycleToFinish()
    {
        string stateDirectory = CreateTestDirectory();
        using var activationRemoved = new ManualResetEventSlim();
        using var continueUnconfigure = new ManualResetEventSlim();
        GitPhase8VerticalSliceService configuredService = CreateRealGitService(stateDirectory);
        Task<GitPhase8UnconfigureResult>? unconfigureTask = null;
        Task<GitPhase8ConfigureResult>? configureTask = null;

        try
        {
            await configuredService.ConfigureAsync(TestContext.Current.CancellationToken);
            GitPhase8VerticalSliceService unconfigureService = CreateRealGitService(
                stateDirectory,
                afterOwnedGitActivationRemoved: () =>
                {
                    activationRemoved.Set();
                    continueUnconfigure.Wait(TestContext.Current.CancellationToken);
                }
            );
            unconfigureTask = Task.Run(
                async () =>
                    await unconfigureService.UnconfigureAsync(
                        TestContext.Current.CancellationToken
                    ),
                TestContext.Current.CancellationToken
            );
            Assert.True(
                activationRemoved.Wait(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken
                )
            );

            GitPhase8VerticalSliceService configureService = CreateRealGitService(
                stateDirectory
            );
            configureTask = Task.Run(
                async () =>
                    await configureService.ConfigureAsync(
                        TestContext.Current.CancellationToken
                    ),
                TestContext.Current.CancellationToken
            );

            Task firstCompletion = await Task.WhenAny(
                configureTask,
                Task.Delay(
                    TimeSpan.FromMilliseconds(500),
                    TestContext.Current.CancellationToken
                )
            );
            Assert.NotSame(configureTask, firstCompletion);
            Assert.DoesNotContain(
                "# BEGIN azureauth-credprovider managed include",
                File.ReadAllText(configureService.Paths.UserGitConfigPath),
                StringComparison.Ordinal
            );

            continueUnconfigure.Set();
            GitPhase8UnconfigureResult unconfigureResult = await unconfigureTask;
            GitPhase8ConfigureResult configureResult = await configureTask;

            Assert.False(unconfigureResult.OwnedGitEntriesPresent);
            Assert.False(unconfigureResult.OwnershipManifestPresent);
            Assert.True(configureResult.OwnedGitEntriesPresent);
            Assert.True(configureResult.OwnershipManifestPresent);
            Assert.Contains(
                "# BEGIN azureauth-credprovider managed include",
                File.ReadAllText(configureService.Paths.UserGitConfigPath),
                StringComparison.Ordinal
            );
        }
        finally
        {
            continueUnconfigure.Set();
            if (unconfigureTask is not null || configureTask is not null)
            {
                Task[] tasks =
                [
                    unconfigureTask ?? Task.CompletedTask,
                    configureTask ?? Task.CompletedTask,
                ];
                _ = await Task.WhenAny(
                    Task.WhenAll(tasks),
                    Task.Delay(
                        TimeSpan.FromSeconds(5),
                        TestContext.Current.CancellationToken
                    )
                );
            }
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task InterruptedUnconfigureRetryResumesInactiveOwnedCleanup()
    {
        string stateDirectory = CreateTestDirectory();
        using var cancellation = new CancellationTokenSource();
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            afterOwnedGitActivationRemoved: cancellation.Cancel
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            byte[] privateConfig = File.ReadAllBytes(service.Paths.GitConfigPath);
            byte[] manifest = File.ReadAllBytes(service.Paths.OwnershipManifestPath);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await service.UnconfigureAsync(cancellation.Token)
            );

            Assert.DoesNotContain(
                "# BEGIN azureauth-credprovider managed include",
                File.ReadAllText(service.Paths.UserGitConfigPath),
                StringComparison.Ordinal
            );
            Assert.Equal(privateConfig, File.ReadAllBytes(service.Paths.GitConfigPath));
            Assert.Equal(manifest, File.ReadAllBytes(service.Paths.OwnershipManifestPath));

            GitPhase8UnconfigureResult result = await CreateRealGitService(stateDirectory)
                .UnconfigureAsync(TestContext.Current.CancellationToken);

            Assert.True(result.HadOwnedConfiguration);
            Assert.False(result.OwnedGitEntriesPresent);
            Assert.False(result.OwnershipManifestPresent);
            Assert.DoesNotContain(
                "azureauth-credprovider",
                File.ReadAllText(service.Paths.GitConfigPath),
                StringComparison.Ordinal
            );
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task InterruptedUnconfigureRetryFailsClosedOnTamperedPrivateState()
    {
        string stateDirectory = CreateTestDirectory();
        using var cancellation = new CancellationTokenSource();
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            afterOwnedGitActivationRemoved: cancellation.Cancel
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await service.UnconfigureAsync(cancellation.Token)
            );
            string tampered = ReplaceConfiguredValue(
                File.ReadAllText(service.Paths.GitConfigPath),
                "useHttpPath",
                "false"
            );
            File.WriteAllText(service.Paths.GitConfigPath, tampered);
            byte[] manifest = File.ReadAllBytes(service.Paths.OwnershipManifestPath);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
                await CreateRealGitService(stateDirectory)
                    .UnconfigureAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(tampered, File.ReadAllText(service.Paths.GitConfigPath));
            Assert.Equal(manifest, File.ReadAllBytes(service.Paths.OwnershipManifestPath));
            Assert.DoesNotContain(
                "# BEGIN azureauth-credprovider managed include",
                File.ReadAllText(service.Paths.UserGitConfigPath),
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureAndUnconfigurePreserveUserGitConfigSymbolicLink()
    {
        string stateDirectory = CreateTestDirectory();
        string homeDirectory = Path.Combine(stateDirectory, "user-home");
        Directory.CreateDirectory(homeDirectory);
        string targetPath = Path.Combine(homeDirectory, "actual.gitconfig");
        string userGitConfigPath = Path.Combine(homeDirectory, ".gitconfig");
        const string Original = "# existing user config\n";
        File.WriteAllText(targetPath, Original);
        if (!TryCreateFileSymbolicLink(userGitConfigPath, Path.GetFileName(targetPath)))
        {
            DeleteDirectoryIfExists(stateDirectory);
            return;
        }

        string? originalLinkTarget = new FileInfo(userGitConfigPath).LinkTarget;
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            homeDirectory
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            Assert.Equal(originalLinkTarget, new FileInfo(userGitConfigPath).LinkTarget);
            Assert.Contains(
                "# BEGIN azureauth-credprovider managed include",
                File.ReadAllText(targetPath),
                StringComparison.Ordinal
            );

            await service.UnconfigureAsync(TestContext.Current.CancellationToken);

            Assert.Equal(originalLinkTarget, new FileInfo(userGitConfigPath).LinkTarget);
            Assert.Equal(Original, File.ReadAllText(targetPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureFailsClosedOnDanglingUserGitConfigSymbolicLink()
    {
        string stateDirectory = CreateTestDirectory();
        string homeDirectory = Path.Combine(stateDirectory, "user-home");
        Directory.CreateDirectory(homeDirectory);
        string userGitConfigPath = Path.Combine(homeDirectory, ".gitconfig");
        const string MissingTarget = "missing.gitconfig";
        if (!TryCreateFileSymbolicLink(userGitConfigPath, MissingTarget))
        {
            DeleteDirectoryIfExists(stateDirectory);
            return;
        }

        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            homeDirectory
        );

        try
        {
            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
                await service.ConfigureAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(MissingTarget, new FileInfo(userGitConfigPath).LinkTarget);
            Assert.False(File.Exists(Path.Combine(homeDirectory, MissingTarget)));
            Assert.False(File.Exists(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureMigratesUntouchedLegacyPrivateOnlyState()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            byte[] privateConfig = File.ReadAllBytes(service.Paths.GitConfigPath);
            byte[] manifest = File.ReadAllBytes(service.Paths.OwnershipManifestPath);
            File.WriteAllText(service.Paths.UserGitConfigPath, "# legacy user config\n");

            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            Assert.Equal(privateConfig, File.ReadAllBytes(service.Paths.GitConfigPath));
            Assert.Equal(manifest, File.ReadAllBytes(service.Paths.OwnershipManifestPath));
            Assert.Contains(
                "# BEGIN azureauth-credprovider managed include",
                File.ReadAllText(service.Paths.UserGitConfigPath),
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureUsesExistingXdgGlobalConfigInIsolatedHome()
    {
        string stateDirectory = CreateTestDirectory();
        string homeDirectory = Path.Combine(stateDirectory, "user-home");
        string xdgConfigHome = Path.Combine(stateDirectory, "xdg-config");
        string xdgGitConfig = Path.Combine(xdgConfigHome, "git", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(xdgGitConfig)!);
        File.WriteAllText(xdgGitConfig, "# existing XDG config\n");
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                UserHomeDirectoryPath = homeDirectory,
                XdgConfigHomeDirectoryPath = xdgConfigHome,
                ProcessRunner = new SystemProcessRunner(),
                GitExecutablePath = "git",
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            }
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(xdgGitConfig, service.Paths.UserGitConfigPath);
            Assert.True(doctor.LocalShellHelperShorthandSuccess);
            Assert.Equal(
                GitUseHttpPathInspectionState.Present,
                doctor.DevAzureUseHttpPath.State
            );
            Assert.Contains(
                "# existing XDG config\n",
                File.ReadAllText(xdgGitConfig),
                StringComparison.Ordinal
            );
            Assert.False(File.Exists(Path.Combine(homeDirectory, ".gitconfig")));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureFailsClosedOnUnownedIncludeCollision()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(service.Paths.UserGitConfigPath)!);
        string collision =
            "[include]\n\tpath = \"" + service.Paths.GitConfigPath + "\"\n";
        File.WriteAllText(service.Paths.UserGitConfigPath, collision);

        try
        {
            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
                await service.ConfigureAsync(TestContext.Current.CancellationToken)
            );

            Assert.Equal(collision, File.ReadAllText(service.Paths.UserGitConfigPath));
            Assert.False(File.Exists(service.Paths.GitConfigPath));
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
    public async Task ConfigureAndUnconfigureFailClosedOnTamperedOwnedInclude(bool unconfigure)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            string tampered = File.ReadAllText(service.Paths.UserGitConfigPath)
                .Replace(
                    "# END azureauth-credprovider managed include",
                    "# END azureauth-credprovider modified include",
                    StringComparison.Ordinal
                );
            File.WriteAllText(service.Paths.UserGitConfigPath, tampered);
            byte[] privateConfig = File.ReadAllBytes(service.Paths.GitConfigPath);
            byte[] manifest = File.ReadAllBytes(service.Paths.OwnershipManifestPath);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
            {
                if (unconfigure)
                {
                    await service.UnconfigureAsync(TestContext.Current.CancellationToken);
                }
                else
                {
                    await service.ConfigureAsync(TestContext.Current.CancellationToken);
                }
            });

            Assert.Equal(tampered, File.ReadAllText(service.Paths.UserGitConfigPath));
            Assert.Equal(privateConfig, File.ReadAllBytes(service.Paths.GitConfigPath));
            Assert.Equal(manifest, File.ReadAllBytes(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorDetectsUrlSpecificHelperResetWithoutInvokingHelpers()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                """
                [credential "https://dev.azure.com/org"]
                    helper =
                    helper = manager

                """
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.False(doctor.LocalShellHelperShorthandSuccess);
            Assert.Equal(
                GitUseHttpPathInspectionState.Present,
                doctor.DevAzureUseHttpPath.State
            );
            Assert.Equal(
                GitEffectiveCredentialHelperState.Shadowed,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Other],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
            Assert.Equal(
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Global,
                    Selector = GitCredentialHelperSelectorKind.UrlSpecific,
                    Directive = GitCredentialHelperConflictDirective.Reset,
                },
                doctor.EffectiveCredentialHelper.Conflict
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorAcceptsUrlSpecificResetFollowedByProductHelper()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            string helperAssignment = File.ReadLines(service.Paths.GitConfigPath)
                .Single(line => line.TrimStart().StartsWith("helper =", StringComparison.Ordinal));
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                "[credential \"https://dev.azure.com/org\"]\n"
                    + "\thelper =\n"
                    + helperAssignment
                    + "\n"
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(doctor.LocalShellHelperShorthandSuccess);
            Assert.Equal(
                GitUseHttpPathInspectionState.Present,
                doctor.DevAzureUseHttpPath.State
            );
            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Product],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorDetectsEffectiveUrlSpecificUseHttpPathOverride()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                """
                [credential "https://dev.azure.com/org/project"]
                    useHttpPath = false

                """
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(doctor.LocalShellHelperShorthandSuccess);
            Assert.Equal(
                GitUseHttpPathInspectionState.Absent,
                doctor.DevAzureUseHttpPath.State
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsHelperResetWhenNoLaterHelperIsConfigured()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                """
                [credential "https://dev.azure.com/org"]
                    helper =

                """
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Reset,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Empty(doctor.EffectiveCredentialHelper.EffectiveOrder);
            Assert.Equal(
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Global,
                    Selector = GitCredentialHelperSelectorKind.UrlSpecific,
                    Directive = GitCredentialHelperConflictDirective.Reset,
                },
                doctor.EffectiveCredentialHelper.Conflict
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
    public async Task DoctorTreatsOrdinaryHelperChainingAsActive(bool otherHelperAfterProduct)
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            const string OtherHelper = "[credential]\n\thelper = manager\n";
            string userConfig = File.ReadAllText(service.Paths.UserGitConfigPath);
            File.WriteAllText(
                service.Paths.UserGitConfigPath,
                otherHelperAfterProduct
                    ? userConfig + OtherHelper
                    : OtherHelper + userConfig
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                otherHelperAfterProduct
                    ?
                    [
                        GitEffectiveCredentialHelperEntryKind.Product,
                        GitEffectiveCredentialHelperEntryKind.Other,
                    ]
                    :
                    [
                        GitEffectiveCredentialHelperEntryKind.Other,
                        GitEffectiveCredentialHelperEntryKind.Product,
                    ],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsBypassWhenGitGlobalConfigurationOverrideSkipsActivation()
    {
        string stateDirectory = CreateTestDirectory();
        string repositoryDirectory = Path.Combine(stateDirectory, "repository");
        string gitDirectory = Path.Combine(repositoryDirectory, ".git");
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs"));
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(
            Path.Combine(gitDirectory, "config"),
            """
            [core]
                repositoryFormatVersion = 0
                bare = false
            [credential]
                helper = manager

            """
        );
        string overrideConfigPath = Path.Combine(stateDirectory, "override.gitconfig");
        File.WriteAllText(overrideConfigPath, string.Empty);
        var processRunner = new EnvironmentOverlayProcessRunner(
            new Dictionary<string, string?>
            {
                ["GIT_CONFIG_GLOBAL"] = overrideConfigPath,
                ["GIT_CONFIG_NOSYSTEM"] = "1",
            }
        );
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            processRunner: processRunner,
            gitConfigurationProbeWorkingDirectory: repositoryDirectory
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(doctor.OwnedGitEntriesPresent);
            Assert.True(doctor.OwnershipManifestPresent);
            Assert.Equal(
                GitEffectiveCredentialHelperState.Bypassed,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Other],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
            Assert.Equal(
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Unknown,
                    Selector = GitCredentialHelperSelectorKind.Unknown,
                    Directive = GitCredentialHelperConflictDirective.ActivationBypassed,
                },
                doctor.EffectiveCredentialHelper.Conflict
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorHonorsExplicitCommandScopeOverrideAfterIsolation()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new EnvironmentOverlayProcessRunner(
            new Dictionary<string, string?>
            {
                ["GIT_CONFIG_COUNT"] = "2",
                ["GIT_CONFIG_KEY_0"] = "credential.https://dev.azure.com/org.helper",
                ["GIT_CONFIG_VALUE_0"] = string.Empty,
                ["GIT_CONFIG_KEY_1"] = "credential.https://dev.azure.com/org.helper",
                ["GIT_CONFIG_VALUE_1"] = "manager",
            }
        );
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            processRunner: processRunner
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Shadowed,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Command,
                    Selector = GitCredentialHelperSelectorKind.UrlSpecific,
                    Directive = GitCredentialHelperConflictDirective.Reset,
                },
                doctor.EffectiveCredentialHelper.Conflict
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorIgnoresCredentialHelpersForUnrelatedHosts()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                """
                [credential "https://github.com"]
                    helper = manager

                """
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Product],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorHandlesUnrelatedCredentialUrlPatternContainingEquals()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner();
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            processRunner: processRunner
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                """
                [credential "https://example.com/path=segment"]
                    helper = manager

                """
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Active,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Product],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
            ProcessStartSpec applicabilityProbe = Assert.Single(
                processRunner.StartSpecs,
                startSpec => startSpec.Arguments.Contains("--get-urlmatch")
            );
            Assert.DoesNotContain("-c", applicabilityProbe.Arguments);
            Assert.Contains(
                applicabilityProbe.Environment,
                pair =>
                    pair.Key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal)
                    && pair.Value?.Contains("path=segment", StringComparison.Ordinal) == true
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorIncludesRepositoryLocalCredentialHelperShadowing()
    {
        string stateDirectory = CreateTestDirectory();
        string repositoryDirectory = Path.Combine(stateDirectory, "repository");
        string gitDirectory = Path.Combine(repositoryDirectory, ".git");
        Directory.CreateDirectory(gitDirectory);
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs"));
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(
            Path.Combine(gitDirectory, "config"),
            """
            [core]
                repositoryFormatVersion = 0
                bare = false
            [credential "https://dev.azure.com/org"]
                helper =
                helper = manager

            """
        );
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            gitConfigurationProbeWorkingDirectory: repositoryDirectory
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitEffectiveCredentialHelperState.Shadowed,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                [GitEffectiveCredentialHelperEntryKind.Other],
                doctor.EffectiveCredentialHelper.EffectiveOrder
            );
            Assert.Equal(
                new GitCredentialHelperConflictDescriptor
                {
                    Scope = GitConfigurationScope.Local,
                    Selector = GitCredentialHelperSelectorKind.UrlSpecific,
                    Directive = GitCredentialHelperConflictDirective.Reset,
                },
                doctor.EffectiveCredentialHelper.Conflict
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
    public async Task DoctorDelegatesWildcardCredentialScopeMatchingToGit(
        bool replacementAfterReset
    )
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                "[credential \"https://*.azure.com/org\"]\n"
                    + "\thelper =\n"
                    + (replacementAfterReset ? "\thelper = manager\n" : string.Empty)
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                replacementAfterReset
                    ? GitEffectiveCredentialHelperState.Shadowed
                    : GitEffectiveCredentialHelperState.Reset,
                doctor.EffectiveCredentialHelper.State
            );
            Assert.Equal(
                GitCredentialHelperSelectorKind.UrlSpecific,
                doctor.EffectiveCredentialHelper.Conflict?.Selector
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorUsesCredentialConfigOrderForUseHttpPath()
    {
        string stateDirectory = CreateTestDirectory();
        GitPhase8VerticalSliceService service = CreateRealGitService(stateDirectory);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.AppendAllText(
                service.Paths.UserGitConfigPath,
                "[credential]\n\tuseHttpPath = false\n"
            );

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                GitUseHttpPathInspectionState.Absent,
                doctor.DevAzureUseHttpPath.State
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Theory]
    [InlineData("future", GitConfigurationScope.Unknown)]
    [InlineData("GLOBAL", null)]
    public async Task DoctorSanitizesUnknownScopeAndRejectsMalformedScope(
        string scopeToken,
        GitConfigurationScope? expectedScope
    )
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                scopeToken + "\0credential.helper\nmanager\0",
                string.Empty
            )
        );
        var service = CreateService(stateDirectory, processRunner);

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult doctor = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            if (expectedScope is null)
            {
                Assert.Equal(
                    GitEffectiveCredentialHelperState.DiscoveryFailed,
                    doctor.EffectiveCredentialHelper.State
                );
                Assert.Null(doctor.EffectiveCredentialHelper.Conflict);
            }
            else
            {
                Assert.Equal(
                    GitEffectiveCredentialHelperState.Bypassed,
                    doctor.EffectiveCredentialHelper.State
                );
                Assert.Equal(
                    expectedScope,
                    doctor.EffectiveCredentialHelper.Conflict?.Scope
                );
            }
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task CreateRealGitServiceRemovesAmbientGitConfigurationVariables()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner();
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            processRunner: processRunner
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            _ = await service.DoctorAsync(TestContext.Current.CancellationToken);

            Assert.All(
                processRunner.StartSpecs,
                startSpec =>
                {
                    Assert.Null(startSpec.Environment["GIT_CONFIG_GLOBAL"]);
                    Assert.Null(startSpec.Environment["GIT_CONFIG_COUNT"]);
                    Assert.Equal("1", startSpec.Environment["GIT_CONFIG_NOSYSTEM"]);
                }
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    private static GitPhase8VerticalSliceService CreateService(
        string stateDirectory,
        IProcessRunner processRunner,
        string? gitExecutablePath = null
    ) =>
        new(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                GitConfigurationProbeWorkingDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                GitExecutablePath = gitExecutablePath,
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            }
        );

    private static GitPhase8VerticalSliceService CreateRealGitService(
        string stateDirectory,
        string? homeDirectory = null,
        Action? afterOwnedGitActivationRemoved = null,
        IFileSystem? fileSystem = null,
        IProcessRunner? processRunner = null,
        string? gitConfigurationProbeWorkingDirectory = null
    ) =>
        new(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                UserHomeDirectoryPath = homeDirectory ?? Path.Combine(stateDirectory, "user-home"),
                GitConfigurationProbeWorkingDirectoryPath =
                    gitConfigurationProbeWorkingDirectory ?? stateDirectory,
                ProcessRunner = new IsolatedRealGitProcessRunner(
                    processRunner ?? new SystemProcessRunner()
                ),
                GitExecutablePath = "git",
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
                AfterOwnedGitActivationRemoved = afterOwnedGitActivationRemoved,
                FileSystem = fileSystem,
            }
        );

    private static bool TryCreateFileSymbolicLink(string path, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(path, targetPath);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            return false;
        }
    }

    private static int CountOccurrences(string value, string match)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(match, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += match.Length;
        }

        return count;
    }

    private static string CreateTestDirectory(string? directoryName = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider-doctor-tests",
            directoryName ?? "state",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ReplaceConfiguredValue(
        string gitConfig,
        string variable,
        string replacement
    )
    {
        string[] lines = gitConfig.Split('\n');
        var replaced = false;
        for (var index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].Trim();
            int equalsIndex = trimmed.IndexOf('=');
            if (
                equalsIndex > 0
                && string.Equals(
                    trimmed[..equalsIndex].Trim(),
                    variable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                lines[index] = "\t" + variable + " = \"" + replacement + "\"";
                replaced = true;
            }
        }

        Assert.True(replaced);
        return string.Join('\n', lines);
    }

    private static string CreateFakeProductExecutable(
        string stateDirectory,
        string executableName = "azureauth-credprovider"
    )
    {
        string directory = Path.Combine(stateDirectory, "product-bin");
        Directory.CreateDirectory(directory);
        string executablePath = Path.Combine(directory, executableName);
        File.WriteAllText(executablePath, "#!/bin/sh\nexit 70\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        return executablePath;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static Dictionary<string, string?> CreateIsolatedGitEnvironment(
        IReadOnlyDictionary<string, string?> explicitEnvironment
    )
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        environment["GIT_CONFIG"] = null;
        environment["GIT_CONFIG_COUNT"] = null;
        environment["GIT_CONFIG_GLOBAL"] = null;
        environment["GIT_CONFIG_PARAMETERS"] = null;
        environment["GIT_CONFIG_SYSTEM"] = null;
        foreach (
            System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()
        )
        {
            string? variable = entry.Key as string;
            if (
                variable is not null
                && variable.StartsWith("GIT_CONFIG_", StringComparison.Ordinal)
            )
            {
                environment[variable] = null;
            }
        }
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        ApplyExplicitEnvironment(environment, explicitEnvironment);
        return environment;
    }

    private static void ApplyExplicitEnvironment(
        Dictionary<string, string?> target,
        IReadOnlyDictionary<string, string?> explicitEnvironment
    )
    {
        foreach ((string key, string? value) in explicitEnvironment)
        {
            if (
                !string.Equals(key, "GIT_CONFIG_COUNT", StringComparison.Ordinal)
                && !key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal)
                && !key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal)
            )
            {
                target[key] = value;
            }
        }

        List<(string Key, string Value)> entries =
            ReadExplicitGitConfigEntries(explicitEnvironment);
        if (
            entries.Count != 0
            || explicitEnvironment.ContainsKey("GIT_CONFIG_COUNT")
        )
        {
            target["GIT_CONFIG_COUNT"] = entries.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            for (var index = 0; index < entries.Count; index++)
            {
                target["GIT_CONFIG_KEY_" + index] = entries[index].Key;
                target["GIT_CONFIG_VALUE_" + index] = entries[index].Value;
            }
        }
    }

    private static List<(string Key, string Value)> ReadExplicitGitConfigEntries(
        IReadOnlyDictionary<string, string?> environment
    )
    {
        if (
            !environment.TryGetValue("GIT_CONFIG_COUNT", out string? countValue)
            || countValue is null
        )
        {
            return new List<(string Key, string Value)>();
        }
        if (
            !int.TryParse(
                countValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int count
            )
            || count < 0
        )
        {
            throw new InvalidOperationException("Invalid explicit Git configuration count.");
        }

        var entries = new List<(string Key, string Value)>(count);
        for (var index = 0; index < count; index++)
        {
            if (
                !environment.TryGetValue("GIT_CONFIG_KEY_" + index, out string? key)
                || key is null
                || !environment.TryGetValue(
                    "GIT_CONFIG_VALUE_" + index,
                    out string? value
                )
                || value is null
            )
            {
                continue;
            }
            entries.Add((key, value));
        }
        return entries;
    }

    private sealed class RecordingGitDiscoveryProcessRunner(
        ProcessResult? helperResult = null,
        ProcessResult? useHttpPathResult = null,
        ProcessResult? urlMatchResult = null
    )
        : IProcessRunner
    {
        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public List<ProcessStartSpec> FallbackStartSpecs { get; } = [];

        public bool HelperAliasWasPresent { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();

            StartSpecs.Add(startSpec);
            if (
                helperResult is not null
                && startSpec.Arguments.Contains("--get-regexp")
            )
            {
                return Task.FromResult(helperResult);
            }

            if (
                urlMatchResult is not null
                && startSpec.Arguments.Contains("--get-urlmatch")
            )
            {
                return Task.FromResult(urlMatchResult);
            }

            if (
                useHttpPathResult is not null
                && startSpec.Arguments.Contains("fill")
            )
            {
                return Task.FromResult(useHttpPathResult);
            }

            if (
                startSpec.Environment.TryGetValue("HOME", out string? home)
                && home is not null
            )
            {
                string userGitConfig = Path.Combine(home, ".gitconfig");
                if (File.Exists(userGitConfig))
                {
                    string includeLine = File.ReadLines(userGitConfig)
                        .Single(line =>
                            line.TrimStart().StartsWith("path =", StringComparison.Ordinal)
                        );
                    string includedPath = includeLine[(includeLine.IndexOf('=') + 1)..]
                        .Trim()
                        .Trim('"');
                    HelperAliasWasPresent =
                        File.Exists(includedPath)
                        && File.ReadAllText(includedPath)
                            .Contains(
                            GitCredentialHelperAdapter.HelperExecutableName,
                            StringComparison.Ordinal
                        );
                }
            }

            var fallbackStartSpec = new ProcessStartSpec(
                    "git",
                    startSpec.Arguments,
                    startSpec.WorkingDirectory,
                    CreateIsolatedGitEnvironment(startSpec.Environment),
                    startSpec.StandardInput,
                    startSpec.Timeout,
                    startSpec.OutputCaptureOptions
            );
            FallbackStartSpecs.Add(fallbackStartSpec);
            return new SystemProcessRunner().RunAsync(
                fallbackStartSpec,
                cancellationToken
            );
        }
    }

    private sealed class EnvironmentOverlayProcessRunner(
        IReadOnlyDictionary<string, string?> environment
    ) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            var combinedEnvironment = new Dictionary<string, string?>(
                StringComparer.Ordinal
            );
            ApplyExplicitEnvironment(combinedEnvironment, startSpec.Environment);
            foreach ((string key, string? value) in environment)
            {
                if (
                    !string.Equals(key, "GIT_CONFIG_COUNT", StringComparison.Ordinal)
                    && !key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal)
                    && !key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal)
                )
                {
                    combinedEnvironment[key] = value;
                }
            }
            List<(string Key, string Value)> overlayEntries =
                ReadExplicitGitConfigEntries(environment);
            List<(string Key, string Value)> startEntries =
                ReadExplicitGitConfigEntries(startSpec.Environment);
            var combinedEntries = overlayEntries.Concat(startEntries).ToArray();
            if (combinedEntries.Length != 0)
            {
                combinedEnvironment["GIT_CONFIG_COUNT"] =
                    combinedEntries.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                for (var index = 0; index < combinedEntries.Length; index++)
                {
                    combinedEnvironment["GIT_CONFIG_KEY_" + index] =
                        combinedEntries[index].Key;
                    combinedEnvironment["GIT_CONFIG_VALUE_" + index] =
                        combinedEntries[index].Value;
                }
            }

            return new SystemProcessRunner().RunAsync(
                new ProcessStartSpec(
                    startSpec.FileName,
                    startSpec.Arguments,
                    startSpec.WorkingDirectory,
                    combinedEnvironment,
                    startSpec.StandardInput,
                    startSpec.Timeout,
                    startSpec.OutputCaptureOptions,
                    startSpec.StandardErrorTee
                ),
                cancellationToken
            );
        }
    }

    private sealed class IsolatedRealGitProcessRunner(IProcessRunner inner) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            Dictionary<string, string?> environment =
                CreateIsolatedGitEnvironment(startSpec.Environment);

            return inner.RunAsync(
                new ProcessStartSpec(
                    startSpec.FileName,
                    startSpec.Arguments,
                    startSpec.WorkingDirectory,
                    environment,
                    startSpec.StandardInput,
                    startSpec.Timeout,
                    startSpec.OutputCaptureOptions,
                    startSpec.StandardErrorTee
                ),
                cancellationToken
            );
        }
    }

    [Fact]
    public async Task GitDoctorCreatesNonPromptingInteractiveBrowserRequest()
    {
        string stateDirectory = CreateTestDirectory();
        var credentialAcquisition = new CapturingDoctorCredentialAcquisitionService();
        var processRunner = new RecordingGitDiscoveryProcessRunner();
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                LocalShellGitDiscoverySupported = false,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    credentialAcquisition
                ),
            }
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(result.CredentialCoreSuccess);
            Assert.True(result.GitCredentialHelperGetSuccess);
            Assert.True(result.ProtocolPayloadCaptured);
            Assert.Empty(processRunner.StartSpecs);
            Assert.Equal(2, credentialAcquisition.Requests.Count);

            Hcoona.AzureAuth.CredProvider.Contracts.CredentialRequestV2 request =
                credentialAcquisition.Requests[0];
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.CredentialEcosystem.Git,
                request.Ecosystem
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.CredentialOperation.Get,
                request.Operation
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.TokenAudience.AzureDevOps,
                request.RequestedAudience
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.CredentialKind.BasicPassword,
                request.CredentialKind
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.IdentityFlow.InteractiveBrowser,
                request.IdentityFlow
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.InteractivePolicy.Never,
                request.InteractivePolicy
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.AcquisitionMode.SilentOnly,
                request.AcquisitionMode
            );
            Assert.Equal(
                Hcoona
                    .AzureAuth
                    .CredProvider
                    .Contracts
                    .CachePolicyMode
                    .ProductPersistentCacheDisabled,
                request.CachePolicy
            );
            Hcoona.AzureAuth.CredProvider.Contracts.CiContext ciContext =
                Assert.IsType<Hcoona.AzureAuth.CredProvider.Contracts.CiContext>(request.CiContext);
            Assert.False(ciContext.ExplicitCiMode);
            Assert.False(ciContext.AllowsPersistentWrites);
            Hcoona.AzureAuth.CredProvider.Contracts.CredentialRequestV2 helperRequest =
                credentialAcquisition.Requests[1];
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.IdentityFlow.InteractiveBrowser,
                helperRequest.IdentityFlow
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.InteractivePolicy.Never,
                helperRequest.InteractivePolicy
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.AcquisitionMode.SilentOnly,
                helperRequest.AcquisitionMode
            );
            Assert.DoesNotContain(
                credentialAcquisition.Requests,
                candidate =>
                    candidate.IdentityFlow
                    == Hcoona.AzureAuth.CredProvider.Contracts.IdentityFlow.DeviceCode
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    private sealed class CapturingDoctorCredentialAcquisitionService
        : Hcoona.AzureAuth.CredProvider.Platform.Composition.ICredentialAcquisitionService
    {
        public List<Hcoona.AzureAuth.CredProvider.Contracts.CredentialRequestV2> Requests { get; } =
        [];

        public ValueTask<Hcoona.AzureAuth.CredProvider.Contracts.CredentialResult> AcquireAsync(
            Hcoona.AzureAuth.CredProvider.Contracts.CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(
                new Hcoona.AzureAuth.CredProvider.Contracts.CredentialResult
                {
                    Status = Hcoona.AzureAuth.CredProvider.Contracts.CredentialResultStatus.Success,
                    Username = "AzureDevOps",
                    Password = "doctor-private-token",
                    DiagnosticsCorrelationId = "git-doctor-silent-request-test",
                }
            );
        }
    }

    [Fact]
    public async Task GitDoctorSilentRequestPreservesDefaultServiceAndCanonicalResource()
    {
        string stateDirectory = CreateTestDirectory();
        var credentialAcquisition = new CapturingDoctorCredentialAcquisitionService();
        var processRunner = new RecordingGitDiscoveryProcessRunner();
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                LocalShellGitDiscoverySupported = false,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    credentialAcquisition
                ),
            }
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(result.CredentialCoreSuccess);
            Assert.True(result.GitCredentialHelperGetSuccess);
            Assert.Empty(processRunner.StartSpecs);
            Assert.Equal(2, credentialAcquisition.Requests.Count);
            Hcoona.AzureAuth.CredProvider.Contracts.CredentialRequestV2 request =
                credentialAcquisition.Requests[0];
            Assert.Equal("default", request.ServiceIdentity);
            Hcoona.AzureAuth.CredProvider.Contracts.CanonicalResourceIdentity resource =
                request.Resource;
            Assert.Equal("dev.azure.com", resource.AzureDevOpsHost);
            Assert.Equal("org", resource.Organization);
            Assert.Null(resource.Project);
            Assert.Null(resource.Feed);
            Assert.Null(resource.Repository);
            Assert.Equal(new Uri("https://dev.azure.com/org"), resource.ServiceEndpoint);
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.IdentityFlow.InteractiveBrowser,
                request.IdentityFlow
            );
            Assert.Equal(
                Hcoona.AzureAuth.CredProvider.Contracts.AcquisitionMode.SilentOnly,
                request.AcquisitionMode
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task UnconfigureManifestDeletionFailureRetainsRetryableStateAndRetryConverges()
    {
        string stateDirectory = CreateTestDirectory();
        var fileSystem = new OneShotManifestDeleteFailureFileSystem();
        GitPhase8VerticalSliceService service = CreateRealGitService(
            stateDirectory,
            fileSystem: fileSystem
        );

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
            fileSystem.ArmDeleteFailure(service.Paths.OwnershipManifestPath);

            GitPhase8UnrecognizedStateException firstFailure =
                await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(async () =>
                    await service.UnconfigureAsync(TestContext.Current.CancellationToken)
                );

            IOException injectedFailure = Assert.IsType<IOException>(
                firstFailure.InnerException
            );
            Assert.Equal(
                OneShotManifestDeleteFailureFileSystem.InjectedFailureMessage,
                injectedFailure.Message
            );
            Assert.Equal(1, fileSystem.MatchingDeleteAttempts);
            Assert.Equal(1, fileSystem.InjectedFailureCount);
            AssertOwnedGitActivationAbsent(service);
            await AssertGitSelectorAbsentAsync(
                service.Paths.GitConfigPath,
                GitPhase8VerticalSliceService.GitCredentialHelperKey
            );
            await AssertGitSelectorAbsentAsync(
                service.Paths.GitConfigPath,
                GitPhase8VerticalSliceService.GitUseHttpPathKey
            );

            Assert.True(File.Exists(service.Paths.OwnershipManifestPath));
            string retainedManifestContents = File.ReadAllText(
                service.Paths.OwnershipManifestPath
            );
            ConfigurationOwnershipManifest manifest =
                ConfigurationOwnershipManifestSerializer.Deserialize(retainedManifestContents);
            Assert.True(ConfigurationOwnershipManifestPolicy.IsValid(manifest));
            Assert.Collection(
                manifest.Entries,
                entry =>
                {
                    Assert.Equal(1, entry.Sequence);
                    Assert.Equal(
                        Hcoona.AzureAuth.CredProvider.Contracts.ConfigurationTargetKind.GitConfig,
                        entry.TargetKind
                    );
                    Assert.Equal(service.Paths.GitConfigPath, entry.TargetPathOrName);
                    Assert.Equal(
                        GitPhase8VerticalSliceService.GitCredentialHelperKey,
                        entry.Key
                    );
                },
                entry =>
                {
                    Assert.Equal(2, entry.Sequence);
                    Assert.Equal(
                        Hcoona.AzureAuth.CredProvider.Contracts.ConfigurationTargetKind.GitConfig,
                        entry.TargetKind
                    );
                    Assert.Equal(service.Paths.GitConfigPath, entry.TargetPathOrName);
                    Assert.Equal(
                        GitPhase8VerticalSliceService.GitUseHttpPathKey,
                        entry.Key
                    );
                }
            );

            await service.ValidateUnconfigureDryRunAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(
                retainedManifestContents,
                File.ReadAllText(service.Paths.OwnershipManifestPath)
            );
            Assert.Equal(1, fileSystem.MatchingDeleteAttempts);
            Assert.Equal(1, fileSystem.InjectedFailureCount);

            GitPhase8UnconfigureResult retryResult = await service.UnconfigureAsync(
                TestContext.Current.CancellationToken
            );

            Assert.True(retryResult.HadOwnedConfiguration);
            Assert.False(retryResult.OwnedGitEntriesPresent);
            Assert.False(retryResult.OwnershipManifestPresent);
            Assert.Equal(2, fileSystem.MatchingDeleteAttempts);
            Assert.Equal(1, fileSystem.InjectedFailureCount);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
            AssertOwnedGitActivationAbsent(service);
            await AssertGitSelectorAbsentAsync(
                service.Paths.GitConfigPath,
                GitPhase8VerticalSliceService.GitCredentialHelperKey
            );
            await AssertGitSelectorAbsentAsync(
                service.Paths.GitConfigPath,
                GitPhase8VerticalSliceService.GitUseHttpPathKey
            );
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    private static void AssertOwnedGitActivationAbsent(
        GitPhase8VerticalSliceService service
    )
    {
        Assert.DoesNotContain(
            "# BEGIN azureauth-credprovider managed include",
            File.ReadAllText(service.Paths.UserGitConfigPath),
            StringComparison.Ordinal
        );
    }

    private static async Task AssertGitSelectorAbsentAsync(
        string gitConfigPath,
        string selector
    )
    {
        ProcessResult result = await new SystemProcessRunner()
            .RunAsync(
                new ProcessStartSpec(
                    "git",
                    ["config", "--file", gitConfigPath, "--get-all", selector]
                ),
                TestContext.Current.CancellationToken
            );

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    private sealed class OneShotManifestDeleteFailureFileSystem
        : IFileSystem,
            IFileSystemMutationLock,
            IFileSystemLinkResolver,
            IFileSystemGitConfigLock
    {
        internal const string InjectedFailureMessage =
            "Injected ownership manifest deletion failure.";

        private readonly SystemFileSystem inner = new();
        private string? deleteFailurePath;

        public int MatchingDeleteAttempts { get; private set; }

        public int InjectedFailureCount { get; private set; }

        public void ArmDeleteFailure(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Assert.Null(deleteFailurePath);
            deleteFailurePath = inner.GetFullPath(path);
        }

        public bool FileExists(string path) => inner.FileExists(path);

        public bool IsExecutableFile(string path) => inner.IsExecutableFile(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string GetFullPath(string path) => inner.GetFullPath(path);

        public bool IsPathFullyQualified(string path) => inner.IsPathFullyQualified(path);

        public string ReadAllText(string path, System.Text.Encoding? encoding = null) =>
            inner.ReadAllText(path, encoding);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public long GetFileLength(string path) => inner.GetFileLength(path);

        public void WriteAllText(
            string path,
            string contents,
            System.Text.Encoding? encoding = null
        ) => inner.WriteAllText(path, contents, encoding);

        public void AtomicWriteAllText(
            string path,
            string contents,
            System.Text.Encoding? encoding = null,
            AtomicWriteOptions options = AtomicWriteOptions.None
        ) => inner.AtomicWriteAllText(path, contents, encoding, options);

        public void AtomicWriteAllBytes(
            string path,
            byte[] contents,
            AtomicWriteOptions options = AtomicWriteOptions.None
        ) => inner.AtomicWriteAllBytes(path, contents, options);

        public UnixFileMode GetUnixFileMode(string path) => inner.GetUnixFileMode(path);

        public void SetUnixFileMode(string path, UnixFileMode mode) =>
            inner.SetUnixFileMode(path, mode);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteFile(string path)
        {
            if (
                deleteFailurePath is not null
                && string.Equals(
                    inner.GetFullPath(path),
                    deleteFailurePath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal
                )
            )
            {
                MatchingDeleteAttempts++;
                if (InjectedFailureCount == 0)
                {
                    InjectedFailureCount++;
                    throw new IOException(InjectedFailureMessage);
                }
            }

            inner.DeleteFile(path);
        }

        public void DeleteDirectory(string path, bool recursive = false) =>
            inner.DeleteDirectory(path, recursive);

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateFiles(path, searchPattern, searchOption);

        public IEnumerable<string> EnumerateDirectories(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        ) => inner.EnumerateDirectories(path, searchPattern, searchOption);

        IDisposable IFileSystemMutationLock.AcquireMutationLock(string directory) =>
            ((IFileSystemMutationLock)inner).AcquireMutationLock(directory);

        string IFileSystemLinkResolver.ResolveFilePathForWrite(string path) =>
            ((IFileSystemLinkResolver)inner).ResolveFilePathForWrite(path);

        IGitConfigLockFile IFileSystemGitConfigLock.AcquireGitConfigLock(
            string targetPath
        ) => ((IFileSystemGitConfigLock)inner).AcquireGitConfigLock(targetPath);
    }
}
