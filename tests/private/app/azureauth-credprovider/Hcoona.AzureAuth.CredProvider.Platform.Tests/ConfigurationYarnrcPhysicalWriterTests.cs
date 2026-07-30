using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationYarnrcPhysicalWriterTests
{
    private const string Path = "/home/user/.yarnrc.yml";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void WritePreservesBomCrLfCommentsAndUnrelatedRegistryBlocks()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllBytes(
            Path,
            [
                .. Bom,
                .. Encoding.UTF8.GetBytes(
                    "# keep\r\nnpmRegistries:\r\n"
                        + "  \"https://registry.example.com/\":\r\n"
                        + "    npmAlwaysAuth: false\r\n"
                ),
            ]
        );
        CanonicalResourceIdentity resource = CreateResource();
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChanges(resource), resource),
            TestContext.Current.CancellationToken
        );

        byte[] bytes = fileSystem.ReadAllBytes(Path);
        string text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.True(bytes.AsSpan().StartsWith(Bom));
        Assert.Contains("# keep\r\n", text, StringComparison.Ordinal);
        Assert.Contains("https://registry.example.com/", text, StringComparison.Ordinal);
        Assert.Contains("npmAuthToken: 'token'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(Path)
        );
    }

    [Fact]
    public void RemoveDeletesOnlyOwnedRegistrySelectors()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        ConfigurationChange[] apply = CreateChanges(resource);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);
        writer.Write(CreateRequest(apply, resource), TestContext.Current.CancellationToken);
        fileSystem.WriteAllText(
            Path,
            fileSystem.ReadAllText(Path) + "\nnodeLinker: node-modules\n"
        );
        ConfigurationChange[] remove = apply
            .Select(change =>
                change with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                }
            )
            .ToArray();

        writer.Write(
            CreateRequest(
                remove,
                resource,
                ConfigurationPlanOperation.Remove,
                apply.Select(Owned).ToArray()
            ),
            TestContext.Current.CancellationToken
        );

        string text = fileSystem.ReadAllText(Path);
        Assert.DoesNotContain("npmAuthToken", text, StringComparison.Ordinal);
        Assert.Contains("nodeLinker: node-modules", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingAuthIdentConflictIsRejectedWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string original =
            "npmRegistries:\n"
            + $"  \"{resource.ServiceEndpoint.AbsoluteUri}\":\n"
            + "    npmAuthIdent: \"user:password\"\n";
        fileSystem.AtomicWriteAllText(Path, original);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(CreateChanges(resource), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(original, fileSystem.ReadAllText(Path));
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateRequest(
        IReadOnlyList<ConfigurationChange> changes,
        CanonicalResourceIdentity resource,
        ConfigurationPlanOperation operation = ConfigurationPlanOperation.Apply,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? ownership = null
    ) =>
        new(operation, ConfigurationTargetKind.Yarnrc, changes, ownership)
        {
            ResourceIdentity = resource,
        };

    private static ConfigurationChange[] CreateChanges(CanonicalResourceIdentity resource)
    {
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        return
        [
            CreateChange("npmRegistryServer", registry, isSecret: false),
            CreateChange($"npmRegistries.{registry}.npmAlwaysAuth", "true", isSecret: false),
            CreateChange($"npmRegistries.{registry}.npmAuthToken", "token", isSecret: true),
        ];
    }

    private static ConfigurationChange CreateChange(string key, string value, bool isSecret) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = Path,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
            IsSecretValue = isSecret,
        };

    private static ConfigurationOwnershipManifestEntry Owned(ConfigurationChange change) =>
        new()
        {
            Sequence = 1,
            TargetKind = change.TargetKind,
            TargetPathOrName = change.TargetPathOrName,
            Key = change.Key,
        };

    private static CanonicalResourceIdentity CreateResource() =>
        CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"),
            feed: "feed"
        );

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The exact regression-test name is specified by the test plan."
    )]
    public void Write_Set_CommentsPreservedWithoutDuplicateRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        string original =
            "npmRegistries:\n"
            + "# keep before registry block\n"
            + $"  \"{registry}\":\n"
            + "    # keep registry setting comment\n"
            + "    npmAuthToken: 'old-token'\n"
            + "# keep after registry block\n"
            + "nodeLinker: node-modules\n";
        fileSystem.AtomicWriteAllText(Path, original);
        ConfigurationChange tokenChange = CreateChange(
            $"npmRegistries.{registry}.npmAuthToken",
            "replacement-token",
            isSecret: true
        );
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest([tokenChange], resource, ownership: [Owned(tokenChange)]),
            TestContext.Current.CancellationToken
        );

        string text = fileSystem.ReadAllText(Path);
        string registryHeader = $"  \"{registry}\":";
        const string replacementSelector = "    npmAuthToken: 'replacement-token'";
        Assert.Contains("# keep before registry block\n", text, StringComparison.Ordinal);
        Assert.Contains("    # keep registry setting comment\n", text, StringComparison.Ordinal);
        Assert.Contains("# keep after registry block\n", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split(registry, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, text.Split(replacementSelector, StringSplitOptions.None).Length - 1);
        string expectedRegistryContent = string.Concat(
            registryHeader,
            "\n    # keep registry setting comment\n",
            replacementSelector,
            "\n"
        );
        Assert.Contains(expectedRegistryContent, text, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAuthToken: 'old-token'", text, StringComparison.Ordinal);
        Assert.Contains("nodeLinker: node-modules\n", text, StringComparison.Ordinal);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The exact regression-test name is specified by the test plan."
    )]
    public void Write_Remove_CommentsPreservedAndAuthRemoved()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        const string unrelatedRegistry = "https://registry.example.com/";
        string original =
            $"npmRegistryServer: '{registry}'\n"
            + "npmRegistries:\n"
            + "# keep before owned registry block\n"
            + $"  \"{registry}\":\n"
            + "    # keep owned registry comment\n"
            + "    npmAlwaysAuth: true\n"
            + "    npmAuthToken: 'token'\n"
            + "# keep between registry blocks\n"
            + $"  \"{unrelatedRegistry}\":\n"
            + "    npmPublishRegistry: 'https://publish.example.com/'\n"
            + "nodeLinker: node-modules\n";
        fileSystem.AtomicWriteAllText(Path, original);
        ConfigurationChange[] apply = CreateChanges(resource);
        ConfigurationChange[] remove = apply
            .Select(change =>
                change with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                }
            )
            .ToArray();
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(
                remove,
                resource,
                ConfigurationPlanOperation.Remove,
                apply.Select(Owned).ToArray()
            ),
            TestContext.Current.CancellationToken
        );

        string text = fileSystem.ReadAllText(Path);
        Assert.DoesNotContain("npmRegistryServer:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAlwaysAuth:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAuthToken:", text, StringComparison.Ordinal);
        Assert.Contains("# keep before owned registry block\n", text, StringComparison.Ordinal);
        Assert.Contains("    # keep owned registry comment\n", text, StringComparison.Ordinal);
        Assert.Contains("# keep between registry blocks\n", text, StringComparison.Ordinal);
        Assert.Contains($"  \"{unrelatedRegistry}\":\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "    npmPublishRegistry: 'https://publish.example.com/'\n",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains("nodeLinker: node-modules\n", text, StringComparison.Ordinal);
    }
}
