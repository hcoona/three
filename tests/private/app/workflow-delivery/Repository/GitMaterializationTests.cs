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
    }

    [TestMethod]
    [DataRow("wrong-head")]
    [DataRow("missing-head")]
    [DataRow("modified")]
    [DataRow("staged")]
    [DataRow("missing")]
    [DataRow("renamed")]
    public async Task BindRejectsWrongMissingOrDirtyCheckout(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        if (defect == "missing-head")
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                GitMaterialization.BindAsync(repo.Directory,
                    new GitRevision(NodeScenario.Basis, []), context.CancellationToken));
            return;
        }
        await repo.SetAsync("content.txt", "original");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        switch (defect)
        {
            case "wrong-head":
                await repo.SetAsync("content.txt", "new revision");
                string next = await repo.CommitAsync(commit);
                await repo.GitAsync("reset", "--hard", next);
                break;
            case "modified":
                await File.WriteAllTextAsync(Path.Combine(repo.Directory, "content.txt"), "dirty",
                    context.CancellationToken);
                break;
            case "staged":
                await repo.SetAsync("content.txt", "staged change");
                await repo.FlushAsync();
                break;
            case "missing":
                File.Delete(Path.Combine(repo.Directory, "content.txt"));
                break;
            case "renamed":
                await repo.GitAsync("mv", "--", "content.txt", "renamed file.txt");
                break;
        }

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(repo.Directory, revision, context.CancellationToken));
        if (defect != "wrong-head")
        {
            string status = await repo.GitAsync("status", "--porcelain=v1", "-z",
                "--untracked-files=no", "--ignore-submodules=none");
            Assert.Contains(JsonSerializer.Serialize(Path.GetFullPath(repo.Directory)),
                error.Message);
            Assert.Contains(JsonSerializer.Serialize(status), error.Message);
            Assert.DoesNotContain('\0', error.Message);
            if (defect == "renamed")
                Assert.AreEqual("R  renamed file.txt\0content.txt\0", status);
        }
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
    [DataRow("wrong-head")]
    [DataRow("missing")]
    [DataRow("modified")]
    [DataRow("subdirectory")]
    public async Task BindReferenceRejectsWrongOrDirtyCheckout(string defect)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("nested/content.txt", "original");
        string commit = await repo.CommitAsync();
        await repo.SetAsync("nested/content.txt", "next");
        string next = await repo.CommitAsync(commit);
        await repo.GitAsync("reset", "--hard", defect == "wrong-head" ? next : commit);
        if (defect == "modified")
            await File.WriteAllTextAsync(Path.Combine(repo.Directory, "nested", "content.txt"),
                "dirty", context.CancellationToken);
        string directory = defect == "subdirectory"
            ? Path.Combine(repo.Directory, "nested") : repo.Directory;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            GitMaterialization.BindAsync(directory, defect == "missing"
                ? "missing-ref" : commit, context.CancellationToken));
        if (defect == "modified")
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                GitMaterialization.BindHeadAsync(directory, context.CancellationToken));
    }

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

}
