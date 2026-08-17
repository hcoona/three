using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationPythonKeyringPhysicalWriterPhase4DTests
{
    [Theory]
    [InlineData(ConfigurationTargetKind.PythonKeyringBackend, "/home/user/keyring/backend.py")]
    [InlineData(ConfigurationTargetKind.KeyringShim, "/home/user/bin/keyring")]
    public void WriteAndRemoveOwnedTarget(ConfigurationTargetKind targetKind, string path)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var writer = new PythonKeyringPhysicalTargetWriter(fileSystem);
        ConfigurationChange apply = CreateChange(
            targetKind,
            path,
            ConfigurationChangeOperation.Set,
            "contents"
        );
        writer.Write(CreateRequest(apply), TestContext.Current.CancellationToken);

        ConfigurationChange remove = CreateChange(
            targetKind,
            path,
            ConfigurationChangeOperation.Remove,
            null
        );
        writer.Write(
            CreateRequest(remove, ConfigurationPlanOperation.Remove, [Owned(remove)]),
            TestContext.Current.CancellationToken
        );

        Assert.False(fileSystem.FileExists(path));
    }

    [Fact]
    public void KeyringShimIsOwnerExecutable()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string path = "/home/user/bin/keyring";
        var writer = new PythonKeyringPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(
                CreateChange(
                    ConfigurationTargetKind.KeyringShim,
                    path,
                    ConfigurationChangeOperation.Set,
                    "#!/bin/sh\n"
                )
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            fileSystem.GetUnixFileMode(path)
        );
    }

    [Fact]
    public void ExistingUnownedTargetIsRejected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string path = "/home/user/keyring/backend.py";
        fileSystem.AtomicWriteAllText(path, "foreign");
        var writer = new PythonKeyringPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(
                    CreateChange(
                        ConfigurationTargetKind.PythonKeyringBackend,
                        path,
                        ConfigurationChangeOperation.Set,
                        "owned"
                    )
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("foreign", fileSystem.ReadAllText(path));
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateRequest(
        ConfigurationChange change,
        ConfigurationPlanOperation operation = ConfigurationPlanOperation.Apply,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? ownership = null
    ) => new(operation, change.TargetKind, [change], ownership);

    private static ConfigurationChange CreateChange(
        ConfigurationTargetKind targetKind,
        string path,
        ConfigurationChangeOperation operation,
        string? value
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = targetKind,
            TargetPathOrName = path,
            Key = "physical-target",
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = false,
        };

    private static ConfigurationOwnershipManifestEntry Owned(ConfigurationChange change) =>
        new()
        {
            Sequence = 1,
            TargetKind = change.TargetKind,
            TargetPathOrName = change.TargetPathOrName,
            Key = change.Key,
        };
}
