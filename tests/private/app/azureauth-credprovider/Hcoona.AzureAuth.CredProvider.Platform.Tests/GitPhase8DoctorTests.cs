using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
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

            ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
            Assert.True(result.LocalShellHelperShorthandSuccess);
            Assert.Equal("git-probe", startSpec.FileName);
            Assert.Equal(
                [
                    "config",
                    "--global",
                    "--get",
                    GitPhase8VerticalSliceService.GitCredentialHelperKey,
                ],
                startSpec.Arguments
            );
            Assert.Null(startSpec.StandardInput);
            Assert.Equal("1", startSpec.Environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal(service.Paths.GitConfigPath, startSpec.Environment["GIT_CONFIG_GLOBAL"]);
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
            Assert.Single(processRunner.StartSpecs);
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
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            }
        );

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
            if (
                startSpec.Environment.TryGetValue("GIT_CONFIG_GLOBAL", out string? gitConfigPath)
                && gitConfigPath is not null
                && File.Exists(gitConfigPath)
            )
            {
                string gitConfig = File.ReadAllText(gitConfigPath);
                HelperAliasWasPresent = gitConfig.Contains(
                    GitCredentialHelperAdapter.HelperExecutableName,
                    StringComparison.Ordinal
                );
                if (result is null)
                {
                    return Task.FromResult(
                        new ProcessResult(
                            0,
                            ReadConfiguredHelperValue(gitConfig) + "\n",
                            string.Empty
                        )
                    );
                }
            }

            return Task.FromResult(result ?? new ProcessResult(1, string.Empty, string.Empty));
        }

        private static string ReadConfiguredHelperValue(string gitConfig)
        {
            var inCredentialSection = false;
            foreach (string line in gitConfig.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inCredentialSection = string.Equals(
                        trimmed,
                        "[credential]",
                        StringComparison.OrdinalIgnoreCase
                    );
                    continue;
                }

                if (
                    !inCredentialSection
                    || !trimmed.StartsWith("helper", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                int equalsIndex = trimmed.IndexOf('=');
                Assert.True(equalsIndex >= 0);
                string serializedValue = trimmed[(equalsIndex + 1)..].Trim();
                Assert.True(
                    serializedValue.Length >= 2
                        && serializedValue[0] == '"'
                        && serializedValue[^1] == '"'
                );
                return serializedValue[1..^1]
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
            }

            throw new Xunit.Sdk.XunitException("Configured credential.helper was not found.");
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
}
