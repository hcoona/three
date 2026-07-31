using System.Reflection;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
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
                    IsWslEnvironment = true,
                    WindowsMountRoot = mountRoot,
                    WindowsPowerShellPath = powerShell,
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
                    IsWslEnvironment = false,
                    EnvironmentVariableReader = name =>
                        name
                        == SystemAzureAuthInstallationDiscovery.NativeLinuxExecutablePathEnvironmentVariable
                            ? executable
                            : null,
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
