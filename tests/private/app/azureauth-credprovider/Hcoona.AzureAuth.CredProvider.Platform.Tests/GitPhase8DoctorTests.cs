using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
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
            Assert.True(result.DevAzureUseHttpPathPresent);
            Assert.Equal("git-probe", startSpec.FileName);
            Assert.Equal(
                [
                    "config",
                    "--global",
                    "--includes",
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
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorFailsDiscoveryWhenGitDoesNotReturnExpectedConfiguredHelper()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(0, "manager\n", string.Empty)
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
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorReportsDiscoveryFailureWhenGitCannotStart()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new ThrowingGitDiscoveryProcessRunner(
            new System.ComponentModel.Win32Exception(2)
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
            Assert.Equal(1, processRunner.InvocationCount);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorDoesNotDiscoverNonExecutableHelperArtifact()
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
            Assert.Empty(processRunner.StartSpecs);
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
            Assert.True(doctor.DevAzureUseHttpPathPresent);

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
            Assert.True(doctor.DevAzureUseHttpPathPresent);
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
            Assert.True(doctor.DevAzureUseHttpPathPresent);
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
            Assert.True(doctor.DevAzureUseHttpPathPresent);
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
            Assert.False(doctor.DevAzureUseHttpPathPresent);
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
                ProcessRunner = processRunner,
                GitExecutablePath = gitExecutablePath,
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            }
        );

    private static GitPhase8VerticalSliceService CreateRealGitService(
        string stateDirectory,
        string? homeDirectory = null,
        Action? afterOwnedGitActivationRemoved = null
    ) =>
        new(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                UserHomeDirectoryPath = homeDirectory ?? Path.Combine(stateDirectory, "user-home"),
                ProcessRunner = new SystemProcessRunner(),
                GitExecutablePath = "git",
                LocalShellGitDiscoverySupported = true,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
                AfterOwnedGitActivationRemoved = afterOwnedGitActivationRemoved,
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

    private sealed class RecordingGitDiscoveryProcessRunner(ProcessResult? result = null)
        : IProcessRunner
    {
        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public bool HelperAliasWasPresent { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();

            StartSpecs.Add(startSpec);
            if (result is not null && startSpec.Arguments.Contains("--get-regexp"))
            {
                return Task.FromResult(result);
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

            return new SystemProcessRunner().RunAsync(
                new ProcessStartSpec(
                    "git",
                    startSpec.Arguments,
                    startSpec.WorkingDirectory,
                    startSpec.Environment,
                    startSpec.StandardInput,
                    startSpec.Timeout,
                    startSpec.OutputCaptureOptions
                ),
                cancellationToken
            );
        }
    }

    private sealed class ThrowingGitDiscoveryProcessRunner(Exception exception) : IProcessRunner
    {
        public int InvocationCount { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();

            InvocationCount++;
            throw exception;
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
}
