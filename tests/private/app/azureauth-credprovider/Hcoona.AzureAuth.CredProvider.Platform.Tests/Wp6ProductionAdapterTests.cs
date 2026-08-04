using System.Reflection;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class Wp6ProductionAdapterTests
{
    [Fact]
    public void SecureStoreUsesPlainJsonAndCooperativeRevisions()
    {
        string root = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(root);
            AzureAuthSecureRecordWriteResult created = store.CompareExchange(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision,
                Encoding.UTF8.GetBytes("""{"schemaVersion":1}""")
            );
            AzureAuthSecureRecordReadResult read = store.Read("azureauth/provider-config.json");
            AzureAuthSecureRecordWriteResult conflict = store.CompareExchange(
                "azureauth/provider-config.json",
                "stale",
                Encoding.UTF8.GetBytes("{}")
            );
            AzureAuthSecureRecordWriteResult deleted = store.CompareDelete(
                "azureauth/provider-config.json",
                read.Revision!
            );

            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, created.Status);
            Assert.Equal("""{"schemaVersion":1}""", read.GetUtf8String());
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, conflict.Status);
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, deleted.Status);
            string physicalPath = Path.Combine(root, "azureauth", "provider-config.json");
            Assert.False(File.Exists(physicalPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SecureStoreSerializesConcurrentCreates()
    {
        string root = CreateTestDirectory();
        try
        {
            var first = new SystemAzureAuthSecureRecordStore(root);
            var second = new SystemAzureAuthSecureRecordStore(root);
            Task<AzureAuthSecureRecordWriteResult>[] writes =
            [
                Task.Run(
                    () =>
                        first.CompareExchange(
                            "azureauth/provider-config.json",
                            AzureAuthSecureRecordStoreContract.MissingRevision,
                            Encoding.UTF8.GetBytes("""{"writer":1}""")
                        ),
                    TestContext.Current.CancellationToken
                ),
                Task.Run(
                    () =>
                        second.CompareExchange(
                            "azureauth/provider-config.json",
                            AzureAuthSecureRecordStoreContract.MissingRevision,
                            Encoding.UTF8.GetBytes("""{"writer":2}""")
                        ),
                    TestContext.Current.CancellationToken
                ),
            ];

            AzureAuthSecureRecordWriteResult[] results = await Task.WhenAll(writes);

            Assert.Single(
                results,
                result => result.Status == AzureAuthSecureRecordWriteStatus.Success
            );
            Assert.Single(
                results,
                result => result.Status == AzureAuthSecureRecordWriteStatus.Conflict
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SecureStoreMultiRecordScopeRetainsPerRecordCas()
    {
        string root = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(root);

            AzureAuthSecureRecordStoreOperationScope.Execute(
                store,
                scopedStore =>
                {
                    AzureAuthSecureRecordWriteResult config = scopedStore.CompareExchange(
                        CredentialProviderCompositionRoot.ProviderConfigRecordName,
                        AzureAuthSecureRecordStoreContract.MissingRevision,
                        Encoding.UTF8.GetBytes("""{"record":"config"}""")
                    );
                    AzureAuthSecureRecordWriteResult binding = scopedStore.CompareExchange(
                        CredentialProviderCompositionRoot.BindingRecordName,
                        AzureAuthSecureRecordStoreContract.MissingRevision,
                        Encoding.UTF8.GetBytes("""{"record":"binding"}""")
                    );

                    Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, config.Status);
                    Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, binding.Status);
                    Assert.Equal(
                        AzureAuthSecureRecordWriteStatus.Conflict,
                        scopedStore
                            .CompareDelete(
                                CredentialProviderCompositionRoot.ProviderConfigRecordName,
                                "stale"
                            )
                            .Status
                    );
                    return true;
                }
            );

            Assert.Equal(
                """{"record":"config"}""",
                store.Read(CredentialProviderCompositionRoot.ProviderConfigRecordName)
                    .GetUtf8String()
            );
            Assert.Equal(
                """{"record":"binding"}""",
                store.Read(CredentialProviderCompositionRoot.BindingRecordName).GetUtf8String()
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SecureStoreRejectsOversizedContent()
    {
        string root = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(root);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    new byte[SystemAzureAuthSecureRecordStore.MaximumRecordBytesForTesting + 1]
                )
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SecureStoreReturnsInvalidUtf8ForPersistenceRepairAndDelete()
    {
        const string ConfigName = "azureauth/provider-config.json";
        const string BindingName = "azureauth/account-binding.json";
        string root = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(root);
            string recordsRoot = Path.Combine(root, "azureauth");
            Directory.CreateDirectory(recordsRoot);
            byte[] invalidUtf8 = [0xC3, 0x28];
            File.WriteAllBytes(Path.Combine(recordsRoot, "provider-config.json"), invalidUtf8);

            AzureAuthSecureRecordReadResult rawConfig = store.Read(ConfigName);
            var configPersistence = new AzureAuthProviderConfigPersistence(store);
            AzureAuthPersistedRecord<AzureAuthProviderConfig> malformedConfig =
                configPersistence.Read(ConfigName);
            AzureAuthPersistedWriteResult<AzureAuthProviderConfig> repaired =
                configPersistence.Repair(
                    malformedConfig,
                    AzureAuthProviderConfig.CreateDirectMsal()
                );

            Assert.Equal(AzureAuthSecureRecordReadStatus.Present, rawConfig.Status);
            Assert.Equal(invalidUtf8, rawConfig.Content.ToArray());
            Assert.Equal(AzureAuthPersistedRecordStatus.Malformed, malformedConfig.Status);
            Assert.Equal(rawConfig.Revision, malformedConfig.Revision);
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, repaired.Status);

            File.WriteAllBytes(Path.Combine(recordsRoot, "account-binding.json"), invalidUtf8);
            var bindingPersistence = new AzureAuthBindingPersistence(store);
            AzureAuthPersistedRecord<AzureAuthBinding> malformedBinding =
                bindingPersistence.Read(BindingName);
            AzureAuthSecureRecordWriteResult deleted = bindingPersistence.Unbind(
                malformedBinding
            );

            Assert.Equal(AzureAuthPersistedRecordStatus.Malformed, malformedBinding.Status);
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, deleted.Status);
            Assert.Equal(
                AzureAuthSecureRecordReadStatus.Missing,
                store.Read(BindingName).Status
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("0.9.5.0", AzureAuthInstallationStatus.Available)]
    [InlineData("0.9.4.0", AzureAuthInstallationStatus.WrongVersion)]
    public void WslDiscoveryDerivesVersionedLocalAppDataPath(
        string fileVersion,
        AzureAuthInstallationStatus expectedStatus
    )
    {
        string mountRoot = CreateTestDirectory();
        try
        {
            string powerShell = Path.Combine(
                mountRoot,
                "Windows",
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(powerShell)!);
            File.WriteAllText(powerShell, "");
            string executable = Path.Combine(
                mountRoot,
                "Users",
                "User",
                "AppData",
                "Local",
                "Programs",
                "AzureAuth",
                "0.9.5",
                "azureauth.exe"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "");
            var runner = new DiscoveryRunner(
                "{\"localApplicationData\":\"C:\\\\Users\\\\User\\\\AppData\\\\Local\","
                    + $"\"exists\":true,\"fileVersion\":\"{fileVersion}\"}}"
            );
            var discovery = new SystemAzureAuthInstallationDiscovery(
                runner,
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.Wsl,
                    IsWslEnvironment = true,
                    WindowsMountRoot = mountRoot,
                    WindowsPowerShellPath = powerShell,
                    LinuxExecuteAccessChecker = _ =>
                        throw new InvalidOperationException(
                            "WSL discovery must not check Linux execute access."
                        ),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(expectedStatus, result.Status);
            Assert.DoesNotContain(
                "ACL",
                runner.StartSpec!.Arguments[^1],
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Empty(runner.StartSpec.Environment);
            if (expectedStatus == AzureAuthInstallationStatus.Available)
            {
                Assert.Equal(executable, result.HostExecutablePath);
                Assert.Equal(AzureAuthHostPlatform.Wsl, result.HostPlatform);
            }
        }
        finally
        {
            Directory.Delete(mountRoot, recursive: true);
        }
    }

    [Fact]
    public void ForcedWslDiscoveryMapsLocalAppDataFromArbitraryWindowsDrive()
    {
        string root = CreateTestDirectory();
        try
        {
            string cMount = Path.Combine(root, "mnt", "c");
            string dMount = Path.Combine(root, "mnt", "d");
            string powerShell = Path.Combine(
                cMount,
                "Windows",
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(powerShell)!);
            File.WriteAllText(powerShell, "");
            string executable = Path.Combine(
                dMount,
                "Users",
                "User",
                "AppData",
                "Local",
                "Programs",
                "AzureAuth",
                "0.9.5",
                "azureauth.exe"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "");
            var runner = new DiscoveryRunner(
                "{\"localApplicationData\":\"D:\\\\Users\\\\User\\\\AppData\\\\Local\","
                    + "\"exists\":true,\"fileVersion\":\"0.9.5.0\"}"
            );
            var discovery = new SystemAzureAuthInstallationDiscovery(
                runner,
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.Wsl,
                    WindowsMountRoot = cMount,
                    WindowsPowerShellPath = powerShell,
                    LinuxExecuteAccessChecker = _ =>
                        throw new InvalidOperationException(
                            "WSL discovery must not check Linux execute access."
                        ),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Available, result.Status);
            Assert.Equal(
                @"D:\Users\User\AppData\Local\Programs\AzureAuth\0.9.5\azureauth.exe",
                result.InstalledExecutablePath
            );
            Assert.Equal(executable, result.HostExecutablePath);
            Assert.Equal(AzureAuthHostPlatform.Wsl, result.HostPlatform);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WslDiscoverySmokeMapsConfiguredDriveLayoutOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTestDirectory();
        try
        {
            string cMount = Path.Combine(root, "mnt", "c");
            string dMount = Path.Combine(root, "mnt", "d");
            string powerShell = Path.Combine(cMount, "powershell.exe");
            Directory.CreateDirectory(cMount);
            File.WriteAllText(powerShell, "");
            string executable = Path.Combine(
                dMount,
                "Users",
                "User",
                "AppData",
                "Local",
                "Programs",
                "AzureAuth",
                "0.9.5",
                "azureauth.exe"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "");
            var discovery = new SystemAzureAuthInstallationDiscovery(
                new DiscoveryRunner(
                    "{\"localApplicationData\":\"D:\\\\Users\\\\User\\\\AppData\\\\Local\","
                        + "\"exists\":true,\"fileVersion\":\"0.9.5.0\"}"
                ),
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    IsWslEnvironment = true,
                    WindowsMountRoot = cMount,
                    WindowsPowerShellPath = powerShell,
                    LinuxExecuteAccessChecker = _ =>
                        throw new InvalidOperationException(
                            "WSL discovery must not check Linux execute access."
                        ),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Available, result.Status);
            Assert.Equal(executable, result.HostExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("0.9.5.0", AzureAuthInstallationStatus.Available)]
    [InlineData("0.9.4.0", AzureAuthInstallationStatus.WrongVersion)]
    public void NativeLinuxDiscoveryReadsAdjacentManagedAssemblyIdentity(
        string assemblyVersion,
        AzureAuthInstallationStatus expectedStatus
    )
    {
        string root = CreateTestDirectory();
        try
        {
            string executable = Path.Combine(root, "azureauth");
            File.WriteAllText(executable, "");
            File.WriteAllText(Path.Combine(root, "azureauth.dll"), "");
            var runner = new DiscoveryRunner("must not be used");
            var discovery = new SystemAzureAuthInstallationDiscovery(
                runner,
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.NativeLinux,
                    IsWslEnvironment = false,
                    EnvironmentVariableReader = name =>
                        name
                        == SystemAzureAuthInstallationDiscovery
                            .NativeLinuxExecutablePathEnvironmentVariable
                            ? executable
                            : null,
                    LinuxExecuteAccessChecker = _ => LinuxExecuteAccessResult.Allowed,
                    ManagedAssemblyIdentityReader = path =>
                    {
                        Assert.Equal(Path.Combine(root, "azureauth.dll"), path);
                        return new AssemblyName("azureauth")
                        {
                            Version = Version.Parse(assemblyVersion),
                        };
                    },
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(expectedStatus, result.Status);
            Assert.Null(runner.StartSpec);
            if (expectedStatus == AzureAuthInstallationStatus.Available)
            {
                Assert.Equal(executable, result.InstalledExecutablePath);
                Assert.Equal(executable, result.HostExecutablePath);
                Assert.Equal(AzureAuthHostPlatform.NativeLinux, result.HostPlatform);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(UnixFileMode.UserExecute)]
    [InlineData(UnixFileMode.GroupExecute)]
    [InlineData(UnixFileMode.OtherExecute)]
    public void NativeLinuxDiscoveryRejectsExecuteBitWhenEffectiveAccessIsDenied(
        UnixFileMode unrelatedExecuteBit
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTestDirectory();
        try
        {
            string executable = Path.Combine(root, "azureauth");
            File.WriteAllText(executable, "");
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | unrelatedExecuteBit
            );
            var discovery = new SystemAzureAuthInstallationDiscovery(
                new DiscoveryRunner("must not be used"),
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.NativeLinux,
                    NativeLinuxExecutablePath = executable,
                    LinuxExecuteAccessChecker = path =>
                    {
                        Assert.Equal(executable, path);
                        return LinuxExecuteAccessResult.Denied;
                    },
                    ManagedAssemblyIdentityReader = _ =>
                        throw new InvalidOperationException("Version must not be read."),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Unavailable, result.Status);
            Assert.Equal("AzureAuthLinuxExecutableNotExecutable", result.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ForcedNativeLinuxDiscoveryRejectsNonExecutableAppHost()
    {
        string root = CreateTestDirectory();
        try
        {
            string executable = Path.Combine(root, "azureauth");
            File.WriteAllText(executable, "");
            var discovery = new SystemAzureAuthInstallationDiscovery(
                new DiscoveryRunner("must not be used"),
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.NativeLinux,
                    NativeLinuxExecutablePath = executable,
                    LinuxExecuteAccessChecker = _ => LinuxExecuteAccessResult.Denied,
                    ManagedAssemblyIdentityReader = _ =>
                        throw new InvalidOperationException("Version must not be read."),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Unavailable, result.Status);
            Assert.Equal("AzureAuthLinuxExecutableNotExecutable", result.Code);
            Assert.False(result.IsAvailable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ForcedNativeLinuxDiscoveryMapsAccessCheckFailureToUnavailable()
    {
        string root = CreateTestDirectory();
        try
        {
            string executable = Path.Combine(root, "azureauth");
            File.WriteAllText(executable, "");
            var discovery = new SystemAzureAuthInstallationDiscovery(
                new DiscoveryRunner("must not be used"),
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.NativeLinux,
                    NativeLinuxExecutablePath = executable,
                    LinuxExecuteAccessChecker = _ => LinuxExecuteAccessResult.Unavailable,
                    ManagedAssemblyIdentityReader = _ =>
                        throw new InvalidOperationException("Version must not be read."),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Unavailable, result.Status);
            Assert.Equal("AzureAuthLinuxExecutableAccessUnavailable", result.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ForcedWindowsDiscoveryDoesNotCheckLinuxExecuteAccess()
    {
        string root = CreateTestDirectory();
        try
        {
            var discovery = new SystemAzureAuthInstallationDiscovery(
                new DiscoveryRunner("must not be used"),
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    ForcedHostPlatform = AzureAuthHostPlatform.Windows,
                    LocalApplicationDataPath = root,
                    LinuxExecuteAccessChecker = _ =>
                        throw new InvalidOperationException(
                            "Windows discovery must not check Linux execute access."
                        ),
                }
            );

            AzureAuthInstallation result = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Missing, result.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NativeLinuxDiscoverySmokeRequiresActualExecuteBit()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTestDirectory();
        try
        {
            string executable = Path.Combine(root, "azureauth");
            File.WriteAllText(executable, "");
            File.WriteAllText(Path.Combine(root, "azureauth.dll"), "");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var discovery = new SystemAzureAuthInstallationDiscovery(
                new DiscoveryRunner("must not be used"),
                new SystemAzureAuthInstallationDiscoveryOptions
                {
                    IsWslEnvironment = false,
                    NativeLinuxExecutablePath = executable,
                    ManagedAssemblyIdentityReader = _ =>
                        new AssemblyName("azureauth") { Version = Version.Parse("0.9.5.0") },
                }
            );

            AzureAuthInstallation unavailable = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
            );
            AzureAuthInstallation available = discovery.Discover(
                AzureAuthProviderConfig.CreateAzureAuth(),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(AzureAuthInstallationStatus.Unavailable, unavailable.Status);
            Assert.Equal("AzureAuthLinuxExecutableNotExecutable", unavailable.Code);
            Assert.Equal(AzureAuthInstallationStatus.Available, available.Status);
            Assert.True(available.IsAvailable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            Environment.CurrentDirectory,
            ".test-output",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class DiscoveryRunner(string output) : IProcessRunner
    {
        public ProcessStartSpec? StartSpec { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default
        )
        {
            StartSpec = startSpec;
            return Task.FromResult(new ProcessResult(0, output, ""));
        }
    }
}
