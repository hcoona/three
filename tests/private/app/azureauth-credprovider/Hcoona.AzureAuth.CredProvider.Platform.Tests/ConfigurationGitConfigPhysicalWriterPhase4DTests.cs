using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationGitConfigPhysicalWriterPhase4DTests
{
    private const string Path = "/home/user/.gitconfig";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void WritePreservesBomNewlinesCommentsAndUnrelatedSections()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllBytes(
            Path,
            [.. Bom, .. Encoding.UTF8.GetBytes("# keep\r\n[user]\r\n\tname = Example\r\n")]
        );
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);
        ConfigurationChange[] changes =
        [
            CreateChange("credential.helper", "/tools/git-credential-azureauth"),
            CreateChange("credential.https://dev.azure.com.useHttpPath", "true"),
        ];

        writer.Write(
            new ConfigurationPhysicalTargetWriterRequest(
                ConfigurationPlanOperation.Apply,
                ConfigurationTargetKind.GitConfig,
                changes
            ),
            TestContext.Current.CancellationToken
        );

        byte[] bytes = fileSystem.ReadAllBytes(Path);
        string text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.True(bytes.AsSpan().StartsWith(Bom));
        Assert.Contains("# keep\r\n[user]\r\n\tname = Example\r\n", text, StringComparison.Ordinal);
        Assert.Contains("[credential]\r\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "[credential \"https://dev.azure.com\"]\r\n",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoveDeletesOnlyOwnedSelectors()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(
            Path,
            "[user]\n\tname = Example\n[credential]\n\thelper = /tools/helper\n"
        );
        ConfigurationChange change = CreateChange("credential.helper", null) with
        {
            Operation = ConfigurationChangeOperation.Remove,
        };
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);

        writer.Write(
            new ConfigurationPhysicalTargetWriterRequest(
                ConfigurationPlanOperation.Remove,
                ConfigurationTargetKind.GitConfig,
                [change],
                [Owned(change)]
            ),
            TestContext.Current.CancellationToken
        );

        string remaining = fileSystem.ReadAllText(Path);
        Assert.Contains("[user]\n\tname = Example\n", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/helper", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingUnownedSelectorIsRejectedWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string original = "[credential]\n\thelper = existing-helper\n";
        fileSystem.AtomicWriteAllText(Path, original);
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                new ConfigurationPhysicalTargetWriterRequest(
                    ConfigurationPlanOperation.Apply,
                    ConfigurationTargetKind.GitConfig,
                    [CreateChange("credential.helper", "/tools/replacement")]
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(original, fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void SetUpdatesHelperDeclaredInLaterRepeatedCredentialSection()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(
            Path,
            "[credential]\n\tusername = AzureDevOps\n"
                + "[credential]\n\thelper = /tools/old-helper\n"
        );
        ConfigurationChange change = CreateChange("credential.helper", "/tools/replacement-helper");
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);

        writer.Write(
            new ConfigurationPhysicalTargetWriterRequest(
                ConfigurationPlanOperation.Apply,
                ConfigurationTargetKind.GitConfig,
                [change],
                [Owned(change)]
            ),
            TestContext.Current.CancellationToken
        );

        string updated = fileSystem.ReadAllText(Path);
        Assert.Equal(1, CountOccurrences(updated, "\thelper = "));
        Assert.Contains(
            "[credential]\n\thelper = \"/tools/replacement-helper\"\n",
            updated,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RemoveDeletesHelperDeclaredInLaterRepeatedCredentialSection()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.AtomicWriteAllText(
            Path,
            "[credential]\n\tusername = AzureDevOps\n" + "[credential]\n\thelper = /tools/helper\n"
        );
        ConfigurationChange change = CreateChange("credential.helper", null) with
        {
            Operation = ConfigurationChangeOperation.Remove,
        };
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);

        writer.Write(
            new ConfigurationPhysicalTargetWriterRequest(
                ConfigurationPlanOperation.Remove,
                ConfigurationTargetKind.GitConfig,
                [change],
                [Owned(change)]
            ),
            TestContext.Current.CancellationToken
        );

        string updated = fileSystem.ReadAllText(Path);
        Assert.Contains("\tusername = AzureDevOps\n", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("\thelper =", updated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetAndRemoveRejectDuplicateHelpersAcrossRepeatedCredentialSections(bool remove)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string original =
            "[credential]\n\thelper = /tools/first\n" + "[credential]\n\thelper = /tools/second\n";
        fileSystem.AtomicWriteAllText(Path, original);
        ConfigurationChange change = CreateChange(
            "credential.helper",
            remove ? null : "/tools/replacement"
        ) with
        {
            Operation = remove
                ? ConfigurationChangeOperation.Remove
                : ConfigurationChangeOperation.Set,
        };
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                new ConfigurationPhysicalTargetWriterRequest(
                    remove ? ConfigurationPlanOperation.Remove : ConfigurationPlanOperation.Apply,
                    ConfigurationTargetKind.GitConfig,
                    [change],
                    [Owned(change)]
                ),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(original, fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void WindowsHelperPathWithSpacesAndParenthesesIsNormalizedQuotedAndRoundTrips()
    {
        const string windowsConfigPath = @"C:\Users\Alice Example\.gitconfig";
        const string helperPath =
            @"C:\Users\Alice Example\AppData\Local\AzureAuth (Preview)\git-credential-azureauth";
        const string expectedCommand =
            "\"C:/Users/Alice Example/AppData/Local/AzureAuth (Preview)/"
            + "git-credential-azureauth\"";
        const string expectedSerializedValue =
            "\thelper = \"\\\"C:/Users/Alice Example/AppData/Local/AzureAuth (Preview)/"
            + "git-credential-azureauth\\\"\"";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        var writer = new GitConfigPhysicalTargetWriter(fileSystem);
        ConfigurationChange change = CreateChange(
            "credential.helper",
            helperPath,
            windowsConfigPath
        );
        var request = new ConfigurationPhysicalTargetWriterRequest(
            ConfigurationPlanOperation.Apply,
            ConfigurationTargetKind.GitConfig,
            [change]
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(
            expectedCommand,
            GitConfigPhysicalTargetWriter.RenderCredentialHelperCommandValue(helperPath)
        );
        Assert.Contains(
            expectedSerializedValue,
            fileSystem.ReadAllText(windowsConfigPath),
            StringComparison.Ordinal
        );
        Assert.True(writer.IsSatisfied(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GitConfigGetPreservesQuotedWindowsHelperCommandValue()
    {
        const string helperPath =
            @"C:\Users\Alice Example\AppData\Local\AzureAuth (Preview)\git-credential-azureauth";
        const string expectedCommand =
            "\"C:/Users/Alice Example/AppData/Local/AzureAuth (Preview)/"
            + "git-credential-azureauth\"";
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "azureauth-git-writer-tests",
            Guid.NewGuid().ToString("N")
        );
        string configPath = System.IO.Path.Combine(directory, "gitconfig");
        Directory.CreateDirectory(directory);

        try
        {
            var writer = new GitConfigPhysicalTargetWriter(new SystemFileSystem());
            writer.Write(
                new ConfigurationPhysicalTargetWriterRequest(
                    ConfigurationPlanOperation.Apply,
                    ConfigurationTargetKind.GitConfig,
                    [CreateChange("credential.helper", helperPath, configPath)]
                ),
                TestContext.Current.CancellationToken
            );

            ProcessResult result = await new SystemProcessRunner().RunAsync(
                new ProcessStartSpec(
                    "git",
                    ["config", "--file", configPath, "--get", "credential.helper"]
                ),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                expectedCommand + "\n",
                result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal)
            );
            Assert.Equal(string.Empty, result.StandardError);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static int CountOccurrences(string value, string match)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(match, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += match.Length;
        }

        return count;
    }

    private static ConfigurationChange CreateChange(
        string key,
        string? value,
        string targetPath = Path
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.GitConfig,
            TargetPathOrName = targetPath,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
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
