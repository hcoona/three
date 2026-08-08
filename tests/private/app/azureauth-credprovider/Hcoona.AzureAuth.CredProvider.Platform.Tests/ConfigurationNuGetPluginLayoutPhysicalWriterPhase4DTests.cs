using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationNuGetPluginLayoutPhysicalWriterPhase4DTests
{
    private const string Root = "/home/user/.nuget/plugins/netcore";
    private const string Marker = Root + "/.azureauth-credprovider.nuget-plugin-layout";

    [Fact]
    public void WriteAndRemoveMarkerPreserveUnrelatedLayout()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(Root + "/other/plugin", "keep");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationChange apply = CreateChange(ConfigurationChangeOperation.Set, "owned");
        writer.Write(CreateRequest(apply), TestContext.Current.CancellationToken);

        ConfigurationChange remove = CreateChange(ConfigurationChangeOperation.Remove, null);
        writer.Write(
            CreateRequest(remove, ConfigurationPlanOperation.Remove, [Owned(remove)]),
            TestContext.Current.CancellationToken
        );

        Assert.False(fileSystem.FileExists(Marker));
        Assert.Equal("keep", fileSystem.ReadAllText(Root + "/other/plugin"));
    }

    [Fact]
    public void ExistingUnownedMarkerIsRejected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(Marker, "foreign");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(CreateChange(ConfigurationChangeOperation.Set, "owned")),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("foreign", fileSystem.ReadAllText(Marker));
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateRequest(
        ConfigurationChange change,
        ConfigurationPlanOperation operation = ConfigurationPlanOperation.Apply,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? ownership = null
    ) => new(operation, ConfigurationTargetKind.NuGetPluginLayout, [change], ownership);

    private static ConfigurationChange CreateChange(
        ConfigurationChangeOperation operation,
        string? value
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
            TargetPathOrName = Root,
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
