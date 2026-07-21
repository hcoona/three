using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class GitPhase8DoctorTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task DoctorUsesGitCredentialFillForLocalShellHelperShorthand()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                    + "username=AzureDevOps\n"
                    + "password=fake-secret-phase9-probe\n",
                string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                GitExecutablePath = "git-probe",
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.True(result.LocalShellHelperShorthandSuccess);
            ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
            Assert.Equal("git-probe", startSpec.FileName);
            Assert.Contains("credential", startSpec.Arguments, StringComparer.Ordinal);
            Assert.Contains("fill", startSpec.Arguments, StringComparer.Ordinal);
            Assert.DoesNotContain(
                startSpec.Arguments,
                argument => argument.StartsWith("credential.helper=", StringComparison.Ordinal));
            Assert.DoesNotContain(
                startSpec.Arguments,
                argument => argument.StartsWith(
                    "credential.https://dev.azure.com.useHttpPath=",
                    StringComparison.Ordinal));
            Assert.Equal(
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n\n",
                startSpec.StandardInput);
            Assert.IsType<string>(startSpec.Environment["PATH"]);
            string markerPath = Assert.IsType<string>(
                startSpec.Environment["AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER"]);
            Assert.Contains("doctor-git-discovery", markerPath, StringComparison.Ordinal);
            Assert.Null(startSpec.Environment["GIT_ASKPASS"]);
            Assert.Null(startSpec.Environment["GIT_SSH_ASKPASS"]);
            Assert.Null(startSpec.Environment["SSH_ASKPASS"]);
            Assert.Equal("0", startSpec.Environment["GIT_TERMINAL_PROMPT"]);
            Assert.Equal("1", startSpec.Environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal(ProcessEnvironmentMode.ExplicitOnly, startSpec.EnvironmentMode);
            Assert.True(processRunner.HelperAliasWasPresent);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorFailsLocalShellHelperShorthandWhenGitDoesNotReturnProbeCredential()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(0, "protocol=https\nhost=dev.azure.com\n", string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Single(processRunner.StartSpecs);
            Assert.True(processRunner.HelperAliasWasPresent);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorFailsLocalShellHelperShorthandWhenGitWritesStderr()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                    + "username=AzureDevOps\n"
                    + "password=fake-secret-phase9-probe\n",
                "unexpected fallback helper\n"));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Single(processRunner.StartSpecs);
            Assert.True(processRunner.HelperAliasWasPresent);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task DoctorMarksLocalShellHelperShorthandDeferredWhenModeIsUnsupported()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(0, string.Empty, string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                LocalShellGitDiscoverySupported = false,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

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
    public async Task DoctorMarksLocalShellHelperShorthandFailedWhenGitCannotStart()
    {
        string stateDirectory = CreateTestDirectory();
        var processRunner = new ThrowingGitDiscoveryProcessRunner(
            new System.ComponentModel.Win32Exception(2));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.GitCredentialHelperGetSuccess);
            Assert.True(result.GitCredentialHelperStoreSuccess);
            Assert.True(result.GitCredentialHelperEraseSuccess);
            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.False(result.LocalShellHelperShorthandDeferred);
            Assert.Equal(1, processRunner.InvocationCount);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows state mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task DoctorDoesNotExecuteGroupWritableStateHelper()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                    + "username=AzureDevOps\n"
                    + "******",
                string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.SetUnixFileMode(
                service.Paths.GitHelperPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupWrite);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Empty(processRunner.StartSpecs);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows executable mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task DoctorDoesNotExecuteCurrentUserOwnedStateHelperWithoutUserExecute()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                    + "username=AzureDevOps\n"
                    + "******",
                string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.SetUnixFileMode(
                service.Paths.GitHelperPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupExecute);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Empty(processRunner.StartSpecs);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows state mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task DoctorDoesNotExecuteCurrentUserOwnedStateHelperWithoutUserRead()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                    + "username=AzureDevOps\n"
                    + "******",
                string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.SetUnixFileMode(
                service.Paths.GitHelperPath,
                UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Empty(processRunner.StartSpecs);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows state mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task ConfigureRefusesGroupWritableStateHelperDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            Directory.CreateDirectory(service.Paths.GitHelperDirectoryPath);
            File.SetUnixFileMode(
                service.Paths.GitHelperDirectoryPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupWrite);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(
                async () => await service.ConfigureAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureRefusesUnsafeHelperPathWithoutWritingShim()
    {
        string stateDirectory = Path.Combine(CreateTestDirectory(), "space path");
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(
                async () => await service.ConfigureAsync(TestContext.Current.CancellationToken));

            Assert.False(File.Exists(service.Paths.GitHelperPath));
            Assert.False(File.Exists(service.Paths.GitConfigPath));
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows executable mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task ConfigureRefusesGroupWritableProductExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductExecutable(stateDirectory);
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = productExecutablePath,
            });

        try
        {
            File.SetUnixFileMode(
                productExecutablePath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupWrite);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(
                async () => await service.ConfigureAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows executable mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task ConfigureRefusesCurrentUserOwnedProductExecutableWithoutUserExecute()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductExecutable(stateDirectory);
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = productExecutablePath,
            });

        try
        {
            File.SetUnixFileMode(
                productExecutablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupExecute);

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(
                async () => await service.ConfigureAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact]
    public async Task ConfigureRefusesProductExecutableWithUnsupportedSharedCliName()
    {
        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductExecutable(
            stateDirectory,
            "renamed-credential-provider");
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = productExecutablePath,
            });

        try
        {
            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(
                async () => await service.ConfigureAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows legacy helper manifest test.", SkipWhen = nameof(IsWindows))]
    public async Task UnconfigureAcceptsLegacyHelperShorthandManifest()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = CreateFakeProductExecutable(stateDirectory),
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            string helperPathHash = ComputeSha256(service.Paths.GitHelperPath);
            string productIdHash = ComputeSha256("azureauth-credprovider");
            string gitConfig = File.ReadAllText(service.Paths.GitConfigPath)
                .Replace(
                    service.Paths.GitHelperPath,
                    "azureauth-credprovider",
                    StringComparison.Ordinal);
            string manifest = File.ReadAllText(service.Paths.OwnershipManifestPath)
                .Replace(helperPathHash, productIdHash, StringComparison.Ordinal);
            WriteOwnerOnlyText(service.Paths.GitConfigPath, gitConfig);
            WriteOwnerOnlyText(service.Paths.OwnershipManifestPath, manifest);

            GitPhase8UnconfigureResult result = await service.UnconfigureAsync(
                TestContext.Current.CancellationToken);

            Assert.True(result.HadOwnedConfiguration);
            Assert.False(File.Exists(service.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows unsafe legacy helper manifest test.", SkipWhen = nameof(IsWindows))]
    public async Task UnconfigureAcceptsLegacyHelperShorthandManifestWhenNewHelperPathIsUnsafe()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string safeStateDirectory = CreateTestDirectory();
        string unsafeStateDirectory = Path.Combine(CreateTestDirectory(), "space path");
        var safeService = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = safeStateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = CreateFakeProductExecutable(safeStateDirectory),
            });
        var unsafeService = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = unsafeStateDirectory,
                ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                    new ProcessResult(0, string.Empty, string.Empty)),
                ProductExecutablePath = CreateFakeProductExecutable(unsafeStateDirectory),
            });

        try
        {
            await safeService.ConfigureAsync(TestContext.Current.CancellationToken);
            CreateOwnerOnlyDirectory(unsafeStateDirectory);
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(unsafeService.Paths.GitConfigPath)!);
            CreateOwnerOnlyDirectory(
                Path.GetDirectoryName(unsafeService.Paths.OwnershipManifestPath)!);
            string helperPathHash = ComputeSha256(safeService.Paths.GitHelperPath);
            string productIdHash = ComputeSha256("azureauth-credprovider");
            string gitConfig = File.ReadAllText(safeService.Paths.GitConfigPath)
                .Replace(
                    safeService.Paths.GitHelperPath,
                    "azureauth-credprovider",
                    StringComparison.Ordinal);
            string manifest = File.ReadAllText(safeService.Paths.OwnershipManifestPath)
                .Replace(helperPathHash, productIdHash, StringComparison.Ordinal)
                .Replace(
                    safeService.Paths.GitConfigPath,
                    unsafeService.Paths.GitConfigPath,
                    StringComparison.Ordinal);
            WriteOwnerOnlyText(unsafeService.Paths.GitConfigPath, gitConfig);
            WriteOwnerOnlyText(unsafeService.Paths.OwnershipManifestPath, manifest);

            GitPhase8UnconfigureResult result = await unsafeService.UnconfigureAsync(
                TestContext.Current.CancellationToken);

            Assert.True(result.HadOwnedConfiguration);
            Assert.False(File.Exists(unsafeService.Paths.OwnershipManifestPath));
        }
        finally
        {
            DeleteDirectoryIfExists(safeStateDirectory);
            DeleteDirectoryIfExists(unsafeStateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows managed assembly mode test.", SkipWhen = nameof(IsWindows))]
    public async Task ConfigureAcceptsManagedProductAssemblyWithoutExecuteBit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductAssembly(stateDirectory);
        string? originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                CreateFakeDotnetRoot(stateDirectory));
            var service = new GitPhase8VerticalSliceService(
                new GitPhase8VerticalSliceOptions
                {
                    StateDirectoryPath = stateDirectory,
                    ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                        new ProcessResult(0, string.Empty, string.Empty)),
                    ProductExecutablePath = productExecutablePath,
                });
            GitPhase8ConfigureResult result = await service.ConfigureAsync(
                TestContext.Current.CancellationToken);

            Assert.True(result.OwnedGitEntriesPresent);
            Assert.True(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows managed assembly mode test.", SkipWhen = nameof(IsWindows))]
    public async Task ConfigureRefusesManagedProductAssemblyWithoutUserRead()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductAssembly(stateDirectory);
        string? originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                CreateFakeDotnetRoot(stateDirectory));
            File.SetUnixFileMode(productExecutablePath, UnixFileMode.UserWrite);
            var service = new GitPhase8VerticalSliceService(
                new GitPhase8VerticalSliceOptions
                {
                    StateDirectoryPath = stateDirectory,
                    ProcessRunner = new RecordingGitDiscoveryProcessRunner(
                        new ProcessResult(0, string.Empty, string.Empty)),
                    ProductExecutablePath = productExecutablePath,
                });

            await Assert.ThrowsAsync<GitPhase8UnrecognizedStateException>(
                async () => await service.ConfigureAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(service.Paths.GitHelperPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalDotnetRoot);
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    [Fact(Skip = "Non-Windows executable mode safety test.", SkipWhen = nameof(IsWindows))]
    public async Task DoctorDoesNotExecuteWhenProductExecutableBecomesGroupWritable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTestDirectory();
        string productExecutablePath = CreateFakeProductExecutable(stateDirectory);
        var processRunner = new RecordingGitDiscoveryProcessRunner(
            new ProcessResult(
                0,
                "protocol=https\nhost=dev.azure.com\npath=org/project/_git/repository\n"
                    + "username=AzureDevOps\n"
                    + "******",
                string.Empty));
        var service = new GitPhase8VerticalSliceService(
            new GitPhase8VerticalSliceOptions
            {
                StateDirectoryPath = stateDirectory,
                ProcessRunner = processRunner,
                ProductExecutablePath = productExecutablePath,
            });

        try
        {
            await service.ConfigureAsync(TestContext.Current.CancellationToken);
            File.SetUnixFileMode(
                productExecutablePath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupWrite);

            GitPhase8DoctorResult result = await service.DoctorAsync(
                TestContext.Current.CancellationToken);

            Assert.False(result.LocalShellHelperShorthandSuccess);
            Assert.Empty(processRunner.StartSpecs);
        }
        finally
        {
            DeleteDirectoryIfExists(stateDirectory);
        }
    }

    private static string CreateTestDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "azureauth-credprovider-doctor-tests");
        CreateOwnerOnlyDirectory(root);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        CreateOwnerOnlyDirectory(directory);
        return directory;
    }

    private static string CreateFakeProductExecutable(
        string stateDirectory,
        string executableName = "azureauth-credprovider")
    {
        string directory = Path.Combine(stateDirectory, "product-bin");
        CreateOwnerOnlyDirectory(directory);
        string executablePath = Path.Combine(directory, executableName);
        File.WriteAllText(executablePath, "#!/bin/sh\nexit 70\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return executablePath;
    }

    private static string CreateFakeProductAssembly(string stateDirectory)
    {
        string directory = Path.Combine(stateDirectory, "product-bin");
        CreateOwnerOnlyDirectory(directory);
        string executablePath = Path.Combine(directory, "azureauth-credprovider.dll");
        File.WriteAllText(executablePath, string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return executablePath;
    }

    private static string CreateFakeDotnetRoot(string stateDirectory)
    {
        string directory = Path.Combine(stateDirectory, "dotnet-root");
        CreateOwnerOnlyDirectory(directory);
        string executablePath = Path.Combine(
            directory,
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        File.WriteAllText(executablePath, "#!/bin/sh\nexit 70\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory;
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

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingGitDiscoveryProcessRunner : IProcessRunner
    {
        private readonly ProcessResult result;

        public RecordingGitDiscoveryProcessRunner(ProcessResult result)
        {
            this.result = result;
        }

        public List<ProcessStartSpec> StartSpecs { get; } = [];

        public bool HelperAliasWasPresent { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default)
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
                HelperAliasWasPresent = File.ReadAllText(gitConfigPath)
                    .Contains(
                        "git-credential-azureauth-credprovider",
                        StringComparison.Ordinal);
            }

            if (
                startSpec.Environment.TryGetValue(
                    "AZUREAUTH_CREDPROVIDER_DOCTOR_MARKER",
                    out string? markerPath)
                && markerPath is not null
            )
            {
                File.WriteAllText(markerPath, string.Empty);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingGitDiscoveryProcessRunner : IProcessRunner
    {
        private readonly Exception exception;

        public ThrowingGitDiscoveryProcessRunner(Exception exception)
        {
            this.exception = exception;
        }

        public int InvocationCount { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(startSpec);
            cancellationToken.ThrowIfCancellationRequested();

            InvocationCount++;
            throw exception;
        }
    }
}
