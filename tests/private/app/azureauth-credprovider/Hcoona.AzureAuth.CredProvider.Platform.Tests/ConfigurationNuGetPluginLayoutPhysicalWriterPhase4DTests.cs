using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationNuGetPluginLayoutPhysicalWriterPhase4DTests
{
    private const string Source = "/installation/app";
    private const string Root = "/home/user/.nuget/plugins/netcore/azureauth-credprovider";
    private const string Marker = Root + "/.azureauth-credprovider.nuget-plugin-layout";

    [Fact]
    public void WriteCopiesPayloadAndRemovePreservesUnrelatedFiles()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(Source + "/azureauth-credprovider.dll", "plugin");
        fileSystem.AtomicWriteAllText(Source + "/nested/dependency.dll", "dependency");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationChange apply = CreateChange(ConfigurationChangeOperation.Set, Source);

        writer.Write(CreateRequest(apply), TestContext.Current.CancellationToken);
        fileSystem.AtomicWriteAllText(Root + "/preserve.txt", "keep");
        fileSystem.CreateDirectory(Root + "/unrelated/empty/subtree");

        ConfigurationChange remove = CreateChange(ConfigurationChangeOperation.Remove, null);
        writer.Write(
            CreateRequest(remove, ConfigurationPlanOperation.Remove, [Owned(remove)]),
            TestContext.Current.CancellationToken
        );

        Assert.False(fileSystem.FileExists(Marker));
        Assert.False(fileSystem.FileExists(Root + "/azureauth-credprovider.dll"));
        Assert.False(fileSystem.FileExists(Root + "/nested/dependency.dll"));
        Assert.Equal("keep", fileSystem.ReadAllText(Root + "/preserve.txt"));
        Assert.True(fileSystem.DirectoryExists(Root + "/unrelated/empty/subtree"));
    }

    [Fact]
    public void ExistingMarkerlessDirectoryWithUnrelatedFileIsPreserved()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(Source + "/azureauth-credprovider.dll", "plugin");
        fileSystem.AtomicWriteAllText(Root + "/foreign", "keep");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChange(ConfigurationChangeOperation.Set, Source)),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("keep", fileSystem.ReadAllText(Root + "/foreign"));
        Assert.Equal("plugin", fileSystem.ReadAllText(Root + "/azureauth-credprovider.dll"));
        Assert.True(fileSystem.FileExists(Marker));
    }

    [Fact]
    public void MarkerlessDirectoryWithOwnershipClaimIsRejectedDuringValidation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(Source + "/azureauth-credprovider.dll", "plugin");
        fileSystem.AtomicWriteAllText(Root + "/foreign", "keep");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationChange apply = CreateChange(ConfigurationChangeOperation.Set, Source);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Validate(
                CreateRequest(apply, ownership: [Owned(apply)]),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("keep", fileSystem.ReadAllText(Root + "/foreign"));
        Assert.False(fileSystem.FileExists(Root + "/azureauth-credprovider.dll"));
        Assert.False(fileSystem.FileExists(Marker));
    }

    [Fact]
    public void WindowsReservedMarkerCaseVariantIsRejectedWithoutCreatingActivation()
    {
        const string windowsSource = @"C:\installation\app";
        const string windowsRoot =
            @"C:\Users\user\.nuget\plugins\netcore\azureauth-credprovider";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        fileSystem.AtomicWriteAllText(
            windowsSource + @"\azureauth-credprovider.dll",
            "plugin"
        );
        fileSystem.AtomicWriteAllText(
            windowsSource + @"\.AZUREAUTH-CREDPROVIDER.NUGET-PLUGIN-LAYOUT",
            "reserved"
        );
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(
                    CreateChange(
                        ConfigurationChangeOperation.Set,
                        windowsSource,
                        windowsRoot
                    )
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(fileSystem.DirectoryExists(windowsRoot));
        Assert.False(
            fileSystem.FileExists(
                windowsRoot + @"\.azureauth-credprovider.nuget-plugin-layout"
            )
        );
    }

    [Fact]
    public void MarkerWriteFailureRestoresV1AndRetryConvergesToV2()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(Source + "/azureauth-credprovider.dll", "plugin-v1");
        fileSystem.AtomicWriteAllText(Source + "/old/obsolete.dll", "obsolete-v1");
        var writer = new NuGetPluginLayoutPhysicalTargetWriter(fileSystem);
        ConfigurationChange apply = CreateChange(ConfigurationChangeOperation.Set, Source);
        writer.Write(CreateRequest(apply), TestContext.Current.CancellationToken);
        string v1Marker = fileSystem.ReadAllText(Marker);

        fileSystem.AtomicWriteAllText(Source + "/azureauth-credprovider.dll", "plugin-v2");
        fileSystem.DeleteFile(Source + "/old/obsolete.dll");
        fileSystem.AtomicWriteAllText(Source + "/new/addition.dll", "addition-v2");
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllText),
            Marker,
            1,
            new IOException("Injected marker write failure.")
        );

        Assert.Throws<IOException>(() =>
            writer.Write(
                CreateRequest(apply, ownership: [Owned(apply)]),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("plugin-v1", fileSystem.ReadAllText(Root + "/azureauth-credprovider.dll"));
        Assert.Equal("obsolete-v1", fileSystem.ReadAllText(Root + "/old/obsolete.dll"));
        Assert.False(fileSystem.FileExists(Root + "/new/addition.dll"));
        Assert.Equal(v1Marker, fileSystem.ReadAllText(Marker));

        writer.Write(
            CreateRequest(apply, ownership: [Owned(apply)]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("plugin-v2", fileSystem.ReadAllText(Root + "/azureauth-credprovider.dll"));
        Assert.False(fileSystem.FileExists(Root + "/old/obsolete.dll"));
        Assert.False(fileSystem.DirectoryExists(Root + "/old"));
        Assert.Equal("addition-v2", fileSystem.ReadAllText(Root + "/new/addition.dll"));
        Assert.Contains(
            "new/addition.dll",
            fileSystem.ReadAllText(Marker),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "old/obsolete.dll",
            fileSystem.ReadAllText(Marker),
            StringComparison.Ordinal
        );
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateRequest(
        ConfigurationChange change,
        ConfigurationPlanOperation operation = ConfigurationPlanOperation.Apply,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? ownership = null
    ) => new(operation, ConfigurationTargetKind.NuGetPluginLayout, [change], ownership);

    private static ConfigurationChange CreateChange(
        ConfigurationChangeOperation operation,
        string? value,
        string targetRoot = Root
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
            TargetPathOrName = targetRoot,
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
