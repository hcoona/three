using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationNpmrcPhysicalWriterPhase4DTests
{
    private const string Path = "/home/user/.npmrc";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void WritePreservesBomCrLfCommentsAndUnrelatedEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllBytes(
            Path,
            [.. Bom, .. Encoding.UTF8.GetBytes("# keep\r\nfund=false\r\n")]
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChange(selector, "token"), resource),
            TestContext.Current.CancellationToken
        );

        byte[] bytes = fileSystem.ReadAllBytes(Path);
        string text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.True(bytes.AsSpan().StartsWith(Bom));
        Assert.Contains("# keep\r\n", text, StringComparison.Ordinal);
        Assert.Contains("fund=false\r\n", text, StringComparison.Ordinal);
        Assert.Contains($"{selector}=token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoveDeletesOnlyTheOwnedSelectorEvenAfterValueDrift()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(
            Path,
            $"{selector}=changed\nregistry=https://registry.npmjs.org/\n"
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange remove = CreateChange(selector, null) with
        {
            Operation = ConfigurationChangeOperation.Remove,
        };

        writer.Write(
            CreateRequest(remove, resource, ConfigurationPlanOperation.Remove, [Owned(remove)]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("registry=https://registry.npmjs.org/\n", fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void ExistingUnownedSelectorIsRejected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(Path, $"{selector}=existing\n");
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(CreateChange(selector, "replacement"), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal($"{selector}=existing\n", fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void SecretWriteUsesOwnerOnlyMode()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChange(selector, "token"), resource),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(Path)
        );
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateRequest(
        ConfigurationChange change,
        CanonicalResourceIdentity resource,
        ConfigurationPlanOperation operation = ConfigurationPlanOperation.Apply,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? ownership = null
    ) =>
        new(operation, ConfigurationTargetKind.Npmrc, [change], ownership)
        {
            ResourceIdentity = resource,
        };

    private static ConfigurationChange CreateChange(string key, string? value) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = Path,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
            IsSecretValue = true,
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
}
