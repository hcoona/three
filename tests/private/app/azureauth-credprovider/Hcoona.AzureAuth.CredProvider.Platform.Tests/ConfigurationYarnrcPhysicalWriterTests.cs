using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
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
        Assert.Contains(
            "  'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry':\r\n"
                + "    npmAlwaysAuth: true\r\n"
                + "    npmAuthToken: 'token'\r\n",
            text,
            StringComparison.Ordinal
        );
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

    [Fact]
    public void WriteUpdatesUnquotedUrlRegistryKeyWithoutAddingDuplicate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        fileSystem.AtomicWriteAllText(
            Path,
            $"npmRegistries:\n  {registry}:\n    npmAuthToken: 'old-token'\n"
        );
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
        Assert.Equal(
            1,
            text.Split($"  {registry}:", StringSplitOptions.None).Length - 1
        );
        Assert.Contains("npmAuthToken: 'replacement-token'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAuthToken: 'old-token'", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"  '{registry}':", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveFindsUnquotedUrlRegistryKeyWithoutLeavingDuplicate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        fileSystem.AtomicWriteAllText(
            Path,
            $"npmRegistries:\n  {registry}:\n    npmAuthToken: 'token'\n"
                + "nodeLinker: node-modules\n"
        );
        ConfigurationChange tokenChange = CreateChange(
            $"npmRegistries.{registry}.npmAuthToken",
            "token",
            isSecret: true
        );
        ConfigurationChange remove = tokenChange with
        {
            Operation = ConfigurationChangeOperation.Remove,
            Value = null,
        };
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(
                [remove],
                resource,
                ConfigurationPlanOperation.Remove,
                [Owned(tokenChange)]
            ),
            TestContext.Current.CancellationToken
        );

        string text = fileSystem.ReadAllText(Path);
        Assert.DoesNotContain(registry, text, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAuthToken", text, StringComparison.Ordinal);
        Assert.Contains("nodeLinker: node-modules", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConfigureNormalizesFlowRegistriesAndRetainsUnrelatedEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        const string UnrelatedRegistry = "https://registry.example.com/";
        fileSystem.AtomicWriteAllText(
            Path,
            "npmRegistries: { "
                + $"\"{UnrelatedRegistry}\": {{ npmAlwaysAuth: false, "
                + "npmPublishRegistry: 'https://publish.example.com/' }"
                + " } # keep flow comment\nnodeLinker: node-modules\n"
        );
        ConfigurationChange[] changes = CreateChanges(resource);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(changes, resource),
            TestContext.Current.CancellationToken
        );

        string text = fileSystem.ReadAllText(Path);
        Assert.DoesNotContain("npmRegistries: {", text, StringComparison.Ordinal);
        Assert.Contains(
            $"  \"{UnrelatedRegistry}\":\n"
                + "    npmAlwaysAuth: false\n"
                + "    npmPublishRegistry: 'https://publish.example.com/'\n",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"  '{resource.ServiceEndpoint.AbsoluteUri.TrimEnd('/')}':\n"
                + "    npmAlwaysAuth: true\n"
                + "    npmAuthToken: 'token'\n",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "npmRegistries: # keep flow comment\n",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains("nodeLinker: node-modules\n", text, StringComparison.Ordinal);
        Assert.True(
            writer.IsSatisfied(
                CreateRequest(changes, resource),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public void WriteUnconfigureNormalizesFlowRegistriesAndRetainsUnrelatedEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        const string UnrelatedRegistry = "https://registry.example.com/";
        fileSystem.AtomicWriteAllText(
            Path,
            $"npmRegistryServer: '{registry}'\n"
                + "npmRegistries: { "
                + $"\"{UnrelatedRegistry}\": {{ npmAlwaysAuth: false }}, "
                + $"\"{registry}\": {{ npmAlwaysAuth: true, npmAuthToken: 'token', "
                + "npmPublishRegistry: 'https://publish.example.com/' }"
                + " }\nnodeLinker: node-modules\n"
        );
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
        Assert.DoesNotContain("npmRegistries: {", text, StringComparison.Ordinal);
        Assert.Contains(
            $"npmRegistries:\n  \"{UnrelatedRegistry}\":\n    npmAlwaysAuth: false\n",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"  \"{registry}\":\n"
                + "    npmPublishRegistry: 'https://publish.example.com/'\n",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("npmAlwaysAuth: true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAuthToken", text, StringComparison.Ordinal);
        Assert.Contains("nodeLinker: node-modules\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePreservesFourSpaceNpmRegistriesSectionIndentation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        const string UnrelatedRegistry = "https://registry.example.com/";
        fileSystem.AtomicWriteAllText(
            Path,
            "npmRegistries:\n"
                + $"    \"{UnrelatedRegistry}\":\n"
                + "        npmAlwaysAuth: false\n"
        );
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChanges(resource), resource),
            TestContext.Current.CancellationToken
        );

        string text = fileSystem.ReadAllText(Path);
        Assert.Contains(
            $"    '{resource.ServiceEndpoint.AbsoluteUri.TrimEnd('/')}':\n"
                + "        npmAlwaysAuth: true\n"
                + "        npmAuthToken: 'token'\n",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"    \"{UnrelatedRegistry}\":\n        npmAlwaysAuth: false\n",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            $"\n  '{resource.ServiceEndpoint.AbsoluteUri.TrimEnd('/')}':",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\n    npmAuthToken:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConfigureAndUnconfigurePreserveYarnrcSymbolicLink()
    {
        const string LinkPath = "/home/user/.yarnrc-link.yml";
        const string TargetPath = "/home/user/actual.yarnrc.yml";
        const string Original = "nodeLinker: node-modules\n";
        var fileSystem = new RevalidatingLinkFileSystem(
            LinkPath,
            TargetPath,
            TargetPath
        );
        fileSystem.Inner.AtomicWriteAllText(TargetPath, Original);
        CanonicalResourceIdentity resource = CreateResource();
        ConfigurationChange[] apply = CreateChanges(resource, LinkPath);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(apply, resource),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, fileSystem.ResolutionCount);
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
        Assert.Contains(
            "npmAuthToken: 'token'\n",
            fileSystem.Inner.ReadAllText(TargetPath),
            StringComparison.Ordinal
        );
        Assert.Contains(
            Original,
            fileSystem.Inner.ReadAllText(TargetPath),
            StringComparison.Ordinal
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

        Assert.Equal(4, fileSystem.ResolutionCount);
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
        Assert.Equal(Original, fileSystem.Inner.ReadAllText(TargetPath));
    }

    [Fact]
    public void WriteConfigureAndUnconfigurePreserveOrdinaryYarnrcSymbolicLink()
    {
        string directory = CreateNonRepositoryTestDirectory("yarnrc-link");
        string targetPath = System.IO.Path.Combine(directory, "actual.yarnrc.yml");
        string linkPath = System.IO.Path.Combine(directory, ".yarnrc.yml");
        const string Original = "nodeLinker: node-modules\n";
        File.WriteAllText(targetPath, Original);
        if (
            !ConfigurationPhysicalWriterSymlinkTestSupport.TryCreateFileSymbolicLink(
                linkPath,
                System.IO.Path.GetFileName(targetPath)
            )
        )
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        string? originalLinkTarget = new FileInfo(linkPath).LinkTarget;
        CanonicalResourceIdentity resource = CreateResource();
        ConfigurationChange[] apply = CreateChanges(resource, linkPath);
        var writer = new YarnrcPhysicalTargetWriter(new SystemFileSystem());

        try
        {
            writer.Write(
                CreateRequest(apply, resource),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(originalLinkTarget, new FileInfo(linkPath).LinkTarget);
            Assert.Contains(
                "npmAuthToken: 'token'\n",
                File.ReadAllText(targetPath),
                StringComparison.Ordinal
            );
            Assert.Contains(
                Original,
                File.ReadAllText(targetPath),
                StringComparison.Ordinal
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

            Assert.Equal(originalLinkTarget, new FileInfo(linkPath).LinkTarget);
            Assert.Equal(Original, File.ReadAllText(targetPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateNonRepositoryTestDirectory(string name)
    {
        string baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        string directory = System.IO.Path.Combine(
            baseDirectory,
            "azureauth-credprovider-tests",
            $"{name}-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void WriteConfigureFailsExplicitlyForDanglingYarnrcSymbolicLink()
    {
        const string LinkPath = "/home/user/.yarnrc-link.yml";
        const string Message = "The file link does not resolve to an existing file.";
        var fileSystem = new RevalidatingLinkFileSystem(
            LinkPath,
            new IOException(Message)
        );
        CanonicalResourceIdentity resource = CreateResource();
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        IOException exception = Assert.Throws<IOException>(() =>
            writer.Write(
                CreateRequest(CreateChanges(resource, LinkPath), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(Message, exception.Message);
        Assert.Equal(1, fileSystem.ResolutionCount);
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
    }

    [Fact]
    public void WriteConfigureFailsExplicitlyForOrdinaryDanglingYarnrcSymbolicLink()
    {
        string directory = ConfigurationPhysicalWriterSymlinkTestSupport.CreateDirectory(
            "yarnrc-dangling"
        );
        string linkPath = System.IO.Path.Combine(directory, ".yarnrc.yml");
        const string MissingTarget = "missing.yarnrc.yml";
        if (
            !ConfigurationPhysicalWriterSymlinkTestSupport.TryCreateFileSymbolicLink(
                linkPath,
                MissingTarget
            )
        )
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        CanonicalResourceIdentity resource = CreateResource();
        var writer = new YarnrcPhysicalTargetWriter(new SystemFileSystem());

        try
        {
            IOException exception = Assert.Throws<IOException>(() =>
                writer.Write(
                    CreateRequest(CreateChanges(resource, linkPath), resource),
                    TestContext.Current.CancellationToken
                )
            );

            Assert.Contains(
                "does not resolve to an existing file",
                exception.Message,
                StringComparison.Ordinal
            );
            Assert.Equal(MissingTarget, new FileInfo(linkPath).LinkTarget);
            Assert.False(File.Exists(System.IO.Path.Combine(directory, MissingTarget)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteFailsWhenYarnrcLinkResolutionChangesBeforeAtomicWrite()
    {
        const string LinkPath = "/home/user/.yarnrc-link.yml";
        const string FirstTarget = "/home/user/first.yarnrc.yml";
        const string SecondTarget = "/home/user/second.yarnrc.yml";
        var fileSystem = new RevalidatingLinkFileSystem(
            LinkPath,
            FirstTarget,
            SecondTarget
        );
        fileSystem.Inner.AtomicWriteAllText(FirstTarget, "nodeLinker: node-modules\n");
        fileSystem.Inner.AtomicWriteAllText(SecondTarget, "enableScripts: false\n");
        CanonicalResourceIdentity resource = CreateResource();
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        IOException exception = Assert.Throws<IOException>(() =>
            writer.Write(
                CreateRequest(CreateChanges(resource, LinkPath), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(
            "link changed while it was being updated",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(2, fileSystem.ResolutionCount);
        Assert.Equal(
            "nodeLinker: node-modules\n",
            fileSystem.Inner.ReadAllText(FirstTarget)
        );
        Assert.Equal("enableScripts: false\n", fileSystem.Inner.ReadAllText(SecondTarget));
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("''")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("Null")]
    [InlineData("NULL")]
    [InlineData("~")]
    [InlineData("# empty token")]
    public void IsSatisfiedTreatsEmptyOrYamlNullYarnAuthTokenAsMissing(string tokenScalar)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri.TrimEnd('/');
        string original =
            $"npmRegistries:\n  '{registry}':\n    npmAuthToken: {tokenScalar}\n"
            + "nodeLinker: node-modules\n";
        fileSystem.AtomicWriteAllText(Path, original);
        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(resource);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        bool satisfied = writer.IsSatisfied(
            CreateRequest(
                [CreateChange(selectors.YarnAuthTokenKey, "requested-token", isSecret: true)],
                resource
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(satisfied);
        Assert.Equal(original, fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void IsSatisfiedTreatsEmptyFlowStyleYarnAuthTokenAsMissing()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri.TrimEnd('/');
        string original =
            $"npmRegistries: {{ '{registry}': {{ npmAuthToken: }} }}\n"
            + "nodeLinker: node-modules\n";
        fileSystem.AtomicWriteAllText(Path, original);
        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(resource);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        bool satisfied = writer.IsSatisfied(
            CreateRequest(
                [CreateChange(selectors.YarnAuthTokenKey, "requested-token", isSecret: true)],
                resource
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(satisfied);
        Assert.Equal(original, fileSystem.ReadAllText(Path));
    }

    [Theory]
    [InlineData("'different-opaque-token'")]
    [InlineData("'null'")]
    [InlineData("different-opaque-token # keep comment")]
    public void IsSatisfiedTreatsNonEmptyYarnAuthTokenAsOpaquePresence(string tokenScalar)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string registry = resource.ServiceEndpoint.AbsoluteUri.TrimEnd('/');
        string original =
            $"npmRegistries:\n  '{registry}':\n    npmAuthToken: {tokenScalar}\n";
        fileSystem.AtomicWriteAllText(Path, original);
        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(resource);
        var writer = new YarnrcPhysicalTargetWriter(fileSystem);

        bool satisfied = writer.IsSatisfied(
            CreateRequest(
                [CreateChange(selectors.YarnAuthTokenKey, "requested-token", isSecret: true)],
                resource
            ),
            TestContext.Current.CancellationToken
        );

        Assert.True(satisfied);
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

    private static ConfigurationChange[] CreateChanges(
        CanonicalResourceIdentity resource,
        string path = Path
    )
    {
        string registry = resource.ServiceEndpoint.AbsoluteUri;
        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(resource);
        return
        [
            CreateChange("npmRegistryServer", registry, isSecret: false, path),
            CreateChange(selectors.YarnAlwaysAuthKey, "true", isSecret: false, path),
            CreateChange(selectors.YarnAuthTokenKey, "token", isSecret: true, path),
        ];
    }

    private static ConfigurationChange CreateChange(
        string key,
        string value,
        bool isSecret,
        string path = Path
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = path,
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
