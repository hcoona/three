using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class GitMaterializationTests(TestContext context)
{
    [TestMethod]
    public async Task BindAcceptsExactCleanTrackedMaterializationAndCommittedContent()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("regular.txt", "regular content");
        await repo.SetAsync("executable.sh", "executable content", "100755");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);

        GitMaterialization bound = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);

        Assert.AreEqual(Path.GetFullPath(repo.Directory), bound.Root);
        Assert.AreEqual(commit, bound.Revision.Commit);
        Assert.AreEqual("regular content", await bound.ReadOptionalTextAsync("regular.txt",
            context.CancellationToken));
        Assert.AreEqual("executable content", await bound.ReadOptionalTextAsync("executable.sh",
            context.CancellationToken));

        // Read-only binding must not rewrite .git/index when stat metadata differs.
        File.SetLastWriteTimeUtc(Path.Combine(repo.Directory, "regular.txt"),
            DateTime.UtcNow.AddMinutes(-2));
        string indexPath = Path.Combine(repo.Directory, ".git", "index");
        byte[] before = await File.ReadAllBytesAsync(indexPath, context.CancellationToken);
        GitMaterialization concurrent = await GitMaterialization.BindAsync(repo.Directory,
            revision, context.CancellationToken);
        Assert.AreEqual(commit, concurrent.Revision.Commit);
        CollectionAssert.AreEqual(before,
            await File.ReadAllBytesAsync(indexPath, context.CancellationToken));
        WorkflowDelivery.Platform.NativeCommandResult control =
            await WorkflowDelivery.Platform.NativeProcess.ExecuteAsync(
                new("git", repo.Directory, ["status", "--porcelain=v1"], 30,
                    new Dictionary<string, string?> { ["GIT_OPTIONAL_LOCKS"] = "1" }),
                context.CancellationToken);
        Assert.IsTrue(control.Succeeded, control.Stderr);
        CollectionAssert.AreNotEqual(before,
            await File.ReadAllBytesAsync(indexPath, context.CancellationToken));
    }

    [TestMethod]
    public async Task BindRejectsUnbornHead()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(repo.Directory,
                new GitRevision(NodeScenario.Basis, []), context.CancellationToken));
    }

    [TestMethod]
    public async Task BindRejectsDifferentHead()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await CommitContentAsync(repo, "content.txt");
        GitRevision revision = await ReadRevisionAsync(repo, commit);
        await AdvanceHeadAsync(repo, "content.txt", commit);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(repo.Directory, revision, context.CancellationToken));
    }

    [TestMethod]
    public Task BindRejectsModifiedTrackedContent() => RejectDirtyCheckoutAsync(repo =>
        File.WriteAllTextAsync(Path.Combine(repo.Directory, "content.txt"), "dirty",
            context.CancellationToken));

    [TestMethod]
    public Task BindRejectsStagedContent() => RejectDirtyCheckoutAsync(async repo =>
    {
        await repo.SetAsync("content.txt", "staged change");
        await repo.FlushAsync();
    });

    [TestMethod]
    public Task BindRejectsDeletedContent() => RejectDirtyCheckoutAsync(repo =>
    {
        File.Delete(Path.Combine(repo.Directory, "content.txt"));
        return Task.CompletedTask;
    });

    [TestMethod]
    public async Task BindRejectsRenameAndEscapesBothStatusPaths()
    {
        string status = await RejectDirtyCheckoutAsync(repo =>
            repo.GitAsync("mv", "--", "content.txt", "renamed file.txt"));
        Assert.AreEqual("R  renamed file.txt\0content.txt\0", status);
    }

    private async Task<string> RejectDirtyCheckoutAsync(Func<GitFixture, Task> arrange)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await CommitContentAsync(repo, "content.txt");
        GitRevision revision = await ReadRevisionAsync(repo, commit);
        await arrange(repo);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(repo.Directory, revision, context.CancellationToken));
        string status = await repo.GitAsync("status", "--porcelain=v1", "-z",
            "--untracked-files=no", "--ignore-submodules=none");
        Assert.Contains(JsonSerializer.Serialize(Path.GetFullPath(repo.Directory)), error.Message);
        Assert.Contains(JsonSerializer.Serialize(status), error.Message);
        Assert.DoesNotContain('\0', error.Message);
        return status;
    }

    private Task<GitRevision> ReadRevisionAsync(GitFixture repo, string commit) =>
        new GitReader(repo.Directory).ReadAsync(commit, context.CancellationToken);

    private static async Task<string> CommitContentAsync(GitFixture repo, string path)
    {
        await repo.SetAsync(path, "original");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        return commit;
    }

    private static async Task AdvanceHeadAsync(GitFixture repo, string path, string commit)
    {
        await repo.SetAsync(path, "next");
        await repo.GitAsync("reset", "--hard", await repo.CommitAsync(commit));
    }

    [TestMethod]
    public async Task ReadDeclarationsExcludesIgnoredAndUntrackedAdditions()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(".gitignore", "ignored.yml\n");
        await repo.SetAsync("committed.yml", "committed declaration");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, "ignored.yml"), "extra ignored",
            context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, "untracked.yml"),
            "extra untracked",
            context.CancellationToken);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization bound = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);

        Assert.AreEqual("committed declaration", await bound.ReadOptionalTextAsync("committed.yml",
            context.CancellationToken));
        Assert.IsNull(await bound.ReadOptionalTextAsync("ignored.yml", context.CancellationToken));
        Assert.IsNull(await bound.ReadOptionalTextAsync("untracked.yml",
            context.CancellationToken));
    }

    [TestMethod]
    [DataRow("symlink")]
    [DataRow("gitlink")]
    public async Task ReadRejectsCommittedNonregularDeclaration(string kind)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("target.yml", "ordinary content");
        string first = await repo.CommitAsync();
        if (kind == "symlink")
            await repo.SetAsync("link.yml", "target.yml", "120000");
        else
            await repo.LinkAsync("link.yml", first);
        string commit = await repo.CommitAsync(first);
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization bound = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            bound.ReadOptionalTextAsync("link.yml", context.CancellationToken));
    }
    [TestMethod]
    public async Task ReadContentUsesRequestedCommittedRevision()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("declaration.yml", "basis content");
        string first = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", first);
        GitRevision basis = await new GitReader(repo.Directory).ReadAsync(first,
            context.CancellationToken);
        GitMaterialization before = await GitMaterialization.BindAsync(repo.Directory, basis,
            context.CancellationToken);
        string? oldContent = await before.ReadOptionalTextAsync("declaration.yml",
            context.CancellationToken);
        await repo.SetAsync("declaration.yml", "candidate content");
        string next = await repo.CommitAsync(first);
        await repo.GitAsync("reset", "--hard", next);
        GitRevision candidate = await new GitReader(repo.Directory).ReadAsync(next,
            context.CancellationToken);
        GitMaterialization after = await GitMaterialization.BindAsync(repo.Directory, candidate,
            context.CancellationToken);

        Assert.AreEqual("basis content", oldContent);
        Assert.AreEqual("candidate content", await after.ReadOptionalTextAsync("declaration.yml",
            context.CancellationToken));
        Assert.AreEqual(first, before.Revision.Commit);
        Assert.AreEqual(next, after.Revision.Commit);
    }

    [TestMethod]
    [DataRow("commit")]
    [DataRow("alias")]
    [DataRow("head")]
    public async Task BindReferenceReadsTheCheckedOutCommitTree(string form)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("content.txt", "committed");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        await repo.GitAsync("tag", "alias", commit);

        GitMaterialization bound = form == "head"
            ? await GitMaterialization.BindHeadAsync(repo.Directory, context.CancellationToken)
            : await GitMaterialization.BindAsync(repo.Directory,
                form == "commit" ? commit : "alias", context.CancellationToken);

        Assert.AreEqual(commit, bound.Revision.Commit);
        Assert.AreEqual("content.txt", Assert.ContainsSingle(bound.Revision.Entries).Path);
        Assert.AreEqual("committed", await bound.ReadOptionalTextAsync("content.txt",
            context.CancellationToken));
    }

    [TestMethod]
    public async Task BindReferenceRejectsDifferentHead()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        const string path = "nested/content.txt";
        string commit = await CommitContentAsync(repo, path);
        await AdvanceHeadAsync(repo, path, commit);
        await RejectReferenceAsync(repo.Directory, commit);
    }

    [TestMethod]
    public async Task BindReferenceRejectsMissingReference()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await CommitContentAsync(repo, "nested/content.txt");
        await RejectReferenceAsync(repo.Directory, "missing-ref");
    }

    [TestMethod]
    public async Task BindReferenceRejectsCheckoutSubdirectory()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await CommitContentAsync(repo, "nested/content.txt");
        await RejectReferenceAsync(Path.Combine(repo.Directory, "nested"), commit);
    }

    [TestMethod]
    public async Task BindReferenceAndHeadRejectModifiedTrackedContent()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        const string path = "nested/content.txt";
        string commit = await CommitContentAsync(repo, path);
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, path), "dirty",
            context.CancellationToken);
        await RejectReferenceAsync(repo.Directory, commit);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindHeadAsync(repo.Directory, context.CancellationToken));
    }

    private Task<InvalidDataException> RejectReferenceAsync(string path, string reference) =>
        Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(path, reference, context.CancellationToken));

    [TestMethod]
    public async Task BindRejectsTagObjectIdNamingTheCheckedOutCommit()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        await repo.GitAsync("tag", "-a", "annotated", "-m", "tag", commit);
        string tag = (await repo.GitAsync("rev-parse", "refs/tags/annotated")).Trim();
        Assert.AreNotEqual(commit, tag);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(repo.Directory, tag, context.CancellationToken));
    }

    [TestMethod]
    public async Task ReuseAcceptsOnlyTheSameCleanCheckoutAndCommit()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("content.txt", "committed");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitMaterialization bound = await GitMaterialization.BindAsync(repo.Directory, commit,
            context.CancellationToken);

        Assert.AreSame(bound, await GitMaterialization.ReuseOrBindAsync(bound,
            repo.Directory + Path.DirectorySeparatorChar, commit, context.CancellationToken));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.ReuseOrBindAsync(bound, repo.Directory, new string('0', 40),
                context.CancellationToken));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.ReuseOrBindAsync(bound, Path.GetTempPath(), commit,
                context.CancellationToken));
        // Earlier native work that changes a tracked file stops a later partition.
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, "content.txt"), "changed",
            context.CancellationToken);
        InvalidDataException dirty = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.ReuseOrBindAsync(bound, repo.Directory, commit,
                context.CancellationToken));
        Assert.Contains("clean tracked checkout", dirty.Message);
    }

    [TestMethod]
    public async Task ReuseRejectsMovedHeadEvenWhenCheckoutIsClean()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("content.txt", "committed");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitMaterialization bound = await GitMaterialization.BindAsync(repo.Directory, commit,
            context.CancellationToken);
        string successor = await repo.CommitAsync(commit);
        await repo.GitAsync("reset", "--hard", successor);
        Assert.AreEqual("", await repo.GitAsync("status", "--porcelain"));

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.ReuseOrBindAsync(bound, repo.Directory, commit,
                context.CancellationToken));

        Assert.Contains("checkout HEAD is not the requested revision", failure.Message);
    }

    [TestMethod]
    [DataRow("0123456789abcdef0123456789abcdef01234567", true)]
    [DataRow("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [DataRow("0123456789ABCDEF0123456789ABCDEF01234567", false)]
    [DataRow("0123456789abc", false)]
    [DataRow("HEAD", false)]
    [DataRow("", false)]
    public void CommitIdentityIsOnlyACompleteNativeObjectName(string value, bool expected) =>
        Assert.AreEqual(expected, GitReader.IsCommitId(value));

    [TestMethod]
    public async Task BindRejectsCheckoutSubdirectory()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("nested/file.txt");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(Path.Combine(repo.Directory, "nested"), revision,
                context.CancellationToken));

        Assert.Contains("checkout root", error.Message);
    }

    [TestMethod]
    [OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
    public async Task BindNamesLinkedCheckoutAliasAsUnsupported()
    {
        // Native tools report physical paths, so a checkout is bound at its physical root.
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("content.txt", "content");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        string alias = Path.Combine(Path.GetTempPath(),
            "git-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(alias, repo.Directory);
        try
        {
            InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => GitMaterialization.BindAsync(alias, revision, context.CancellationToken));
            Assert.Contains("physical (unlinked) checkout root", error.Message);
        }
        finally { Directory.Delete(alias); }
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task BindAcceptsRootCaseDifference()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("product/source.cs", "source");
        await repo.SetAsync("version.json", """{"version":"1.0"}""");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        string differentlySpelledRoot = repo.Directory.ToUpperInvariant();

        GitMaterialization bound = await GitMaterialization.BindAsync(differentlySpelledRoot,
            revision, context.CancellationToken);
        var nbgv = new NbgvInputReader(differentlySpelledRoot);
        NbgvInputs inputs = nbgv.Read(revision, "product", context.CancellationToken);
        string version = nbgv.NpmVersion(revision, "product", context.CancellationToken);

        Assert.AreEqual(differentlySpelledRoot, bound.Root);
        Assert.AreEqual(commit, inputs.Commit);
        Assert.Contains("product/source.cs", inputs.Paths);
        Assert.StartsWith("1.0.", version);
    }

    [TestMethod]
    public async Task BindPreservesLfsCleanFilterForStatDirtyHydratedFile()
    {
        const string hydrated = "hydrated-soundfont-binary-payload\n";
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string hooks = Directory.CreateDirectory(Path.Combine(repo.Directory, ".git", "hooks"))
            .FullName.Replace('\\', '/');
        await repo.GitAsync("config", "core.hooksPath", hooks);
        await repo.GitAsync("lfs", "install", "--local");
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, ".gitattributes"),
            "*.sf2 filter=lfs diff=lfs merge=lfs -text\n", context.CancellationToken);
        await repo.GitAsync("add", ".gitattributes");
        await repo.GitAsync("commit", "-q", "-m", "attributes");

        string asset = Path.Combine(repo.Directory, "asset.sf2");
        await File.WriteAllTextAsync(asset, hydrated, context.CancellationToken);
        await repo.GitAsync("add", "asset.sf2");
        await repo.GitAsync("commit", "-q", "-m", "hydrated");
        string stored = await repo.GitAsync("show", "HEAD:asset.sf2");
        Assert.StartsWith("version https://git-lfs.github.com/spec/v1\n", stored);

        File.SetLastWriteTimeUtc(asset, DateTime.UtcNow.AddMinutes(-2));
        GitMaterialization bound = await GitMaterialization.BindHeadAsync(repo.Directory,
            context.CancellationToken);
        Assert.AreEqual(hydrated, await bound.ReadOptionalTextAsync("asset.sf2",
            context.CancellationToken));

        await File.WriteAllTextAsync(asset, "modified-payload\n", context.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindHeadAsync(repo.Directory, context.CancellationToken));
    }

}
