using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class GitReaderTests(TestContext context)
{
    [TestMethod]
    public async Task CompareReturnsCommittedChangesAndPeeledIdentities()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("src/project/source.cs", "before");
        await repo.SetAsync("deleted.txt");
        await repo.SetAsync("old-name.txt", "rename");
        await repo.SetAsync("unchanged.txt");
        string basis = await repo.CommitAsync();
        await repo.GitAsync("tag", "--annotate", "basis", "--message", "fixture", basis);
        await repo.SetAsync("src/project/source.cs", "after");
        await repo.RemoveAsync("deleted.txt");
        await repo.RemoveAsync("old-name.txt");
        await repo.SetAsync("new-name.txt", "rename");
        await repo.SetAsync("added.txt");
        string candidate = await repo.CommitAsync(basis);
        System.IO.Directory.CreateDirectory(Path.Combine(repo.Directory, "src/project"));
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, "src/project/source.cs"),
            "dirty", context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, "untracked.txt"),
            "untracked", context.CancellationToken);

        GitComparison result = await new GitReader(repo.Directory)
            .CompareAsync("basis", "HEAD", context.CancellationToken);

        Assert.AreEqual(basis, result.Basis.Commit);
        Assert.AreEqual(candidate, result.Candidate.Commit);
        string[] before = ["src/project/source.cs", "deleted.txt", "old-name.txt", "unchanged.txt"];
        string[] after = ["src/project/source.cs", "added.txt", "new-name.txt", "unchanged.txt"];
        string[] changed = ["src/project/source.cs", "deleted.txt", "old-name.txt",
            "new-name.txt", "added.txt"];
        CollectionAssert.AreEquivalent(before, Paths(result.Basis));
        CollectionAssert.AreEquivalent(after, Paths(result.Candidate));
        CollectionAssert.AreEquivalent(changed, result.ChangedPaths);
    }

    [TestMethod]
    public async Task CompareUsesDivergentEndpointsWithoutSubstitutingMergeBase()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("common.txt");
        string common = await repo.CommitAsync();
        await repo.SetAsync("only-left.txt");
        string left = await repo.CommitAsync(common);
        await repo.RemoveAsync("only-left.txt");
        await repo.SetAsync("only-right.txt");
        string right = await repo.CommitAsync(common);

        GitComparison result = await new GitReader(repo.Directory)
            .CompareAsync(left, right, context.CancellationToken);

        string[] changed = ["only-left.txt", "only-right.txt"];
        CollectionAssert.AreEquivalent(changed, result.ChangedPaths);
        Assert.AreEqual(left, result.Basis.Commit);
        Assert.AreEqual(right, result.Candidate.Commit);
    }

    [TestMethod]
    public async Task CompareKeepsRootPathsAndNativeKindsUnderConflictingConfiguration()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("seed.txt");
        string seed = await repo.CommitAsync();
        await repo.SetAsync("nested/link", "../seed.txt", "120000");
        await repo.SetAsync("nested/executable", "fixture", "100755");
        await repo.LinkAsync("vendor/submodule", seed);
        string basis = await repo.CommitAsync(seed);
        await repo.SetAsync("outside.txt");
        await repo.LinkAsync("vendor/submodule", basis);
        string candidate = await repo.CommitAsync(basis);
        await repo.GitAsync("config", "diff.relative", "true");
        await repo.GitAsync("config", "diff.renames", "true");
        await repo.GitAsync("config", "diff.ignoreSubmodules", "all");
        await repo.GitAsync("config", "diff.external", "missing-fixture-diff-tool");
        string nested = Path.Combine(repo.Directory, "nested");
        System.IO.Directory.CreateDirectory(nested);

        GitComparison result = await new GitReader(nested)
            .CompareAsync(basis, candidate, context.CancellationToken);

        Assert.Contains(new GitEntry("nested/link", "120000", "blob"), result.Basis.Entries);
        Assert.Contains(new GitEntry("nested/executable", "100755", "blob"),
            result.Candidate.Entries);
        Assert.Contains(new GitEntry("vendor/submodule", "160000", "commit"),
            result.Candidate.Entries);
        Assert.Contains(new GitEntry("seed.txt", "100644", "blob"), result.Candidate.Entries);
        string[] changed = ["outside.txt", "vendor/submodule"];
        CollectionAssert.AreEquivalent(changed, result.ChangedPaths);
    }

    [TestMethod]
    public async Task CompareReadsStoredObjectsDespiteReplacementRefs()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("stored.txt");
        string stored = await repo.CommitAsync();
        await repo.RemoveAsync("stored.txt");
        await repo.SetAsync("replacement.txt");
        string replacement = await repo.CommitAsync();
        await repo.GitAsync("replace", stored, replacement);

        GitComparison result = await new GitReader(repo.Directory)
            .CompareAsync(stored, replacement, context.CancellationToken);

        Assert.AreEqual(stored, result.Basis.Commit);
        Assert.AreEqual("stored.txt", Assert.ContainsSingle(result.Basis.Entries).Path);
        Assert.AreEqual("replacement.txt", Assert.ContainsSingle(result.Candidate.Entries).Path);
        string[] changed = ["stored.txt", "replacement.txt"];
        CollectionAssert.AreEquivalent(changed, result.ChangedPaths);
    }

    [TestMethod]
    public async Task ReadAndComparePreserveUnusualUtf8NamesIncludingInitialBom()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string basis = await repo.CommitAsync();
        const string initialBom = "\uFEFFfirst.txt";
        await repo.SetAsync(initialBom);
        string single = await repo.CommitAsync(basis);
        var reader = new GitReader(repo.Directory);

        GitComparison first = await reader.CompareAsync(basis, single,
            context.CancellationToken);

        CollectionAssert.AreEqual(new[] { initialBom }, first.ChangedPaths);
        string[] names = [initialBom, " spaced .txt", "tab\tname", "line\nname",
            "雪/é.txt", "Case.txt", "case.txt"];
        foreach (string name in names[1..])
            await repo.SetAsync(name);
        string candidate = await repo.CommitAsync(single);

        GitRevision result = await reader.ReadAsync(candidate, context.CancellationToken);
        GitComparison all = await reader.CompareAsync(basis, candidate,
            context.CancellationToken);

        CollectionAssert.AreEquivalent(names, Paths(result));
        CollectionAssert.AreEquivalent(names, all.ChangedPaths);
    }

    [TestMethod]
    [DataRow("sha1")]
    [DataRow("sha256")]
    public async Task ReadAcceptsNativeCommitIdsAndEmptyCommittedTrees(string format)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken, format);
        string commit = await repo.CommitAsync();
        var reader = new GitReader(repo.Directory);

        GitRevision revision = await reader.ReadAsync("HEAD", context.CancellationToken);
        GitComparison same = await reader.CompareAsync(commit, commit,
            context.CancellationToken);

        Assert.AreEqual(commit, revision.Commit);
        Assert.IsEmpty(revision.Entries);
        Assert.IsEmpty(same.ChangedPaths);
    }

    [TestMethod]
    [DataRow("missing-ref")]
    [DataRow("HEAD^{tree}")]
    [DataRow("--help")]
    public async Task ReadRejectsMissingOrNoncommitReferences(string reference)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.CommitAsync();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new GitReader(repo.Directory).ReadAsync(reference, context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadRejectsMissingRequiredTreeWithoutReturningPartialFacts()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("project/source.txt");
        string commit = await repo.CommitAsync();
        string tree = (await repo.GitAsync("rev-parse", "HEAD^{tree}")).Trim();
        string treeFile = Path.Combine(repo.Directory, ".git", "objects", tree[..2], tree[2..]);
        File.SetAttributes(treeFile, FileAttributes.Normal);
        File.Delete(treeFile);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new GitReader(repo.Directory).ReadAsync(commit, context.CancellationToken));
    }

    [TestMethod]
    public async Task CompareCancellationDoesNotReturnSuccessfulFacts()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await repo.CommitAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new GitReader(repo.Directory).CompareAsync(commit, commit, cancellation.Token));
    }

    private static string[] Paths(GitRevision revision) =>
        revision.Entries.Select(entry => entry.Path).ToArray();

    private sealed class GitFixture(CancellationToken token) : IDisposable
    {
        private readonly string temporary = Path.Combine(Path.GetTempPath(),
            "workflow git facts " + Guid.NewGuid().ToString("N"));
        internal string Directory => Path.Combine(temporary, "repository");

        internal static async Task<GitFixture> CreateAsync(CancellationToken token,
            string format = "sha1")
        {
            var fixture = new GitFixture(token);
            try
            {
                System.IO.Directory.CreateDirectory(fixture.Directory);
                string empty = Path.Combine(fixture.temporary, "empty-template");
                System.IO.Directory.CreateDirectory(empty);
                await fixture.GitAsync("init", "--quiet", "--initial-branch=fixture",
                    "--object-format=" + format, "--template=" + empty);
                await fixture.GitAsync("config", "user.name", "Workflow fixture");
                await fixture.GitAsync("config", "user.email", "fixture@example.invalid");
                await fixture.GitAsync("config", "commit.gpgSign", "false");
                await fixture.GitAsync("config", "tag.gpgSign", "false");
                await fixture.GitAsync("config", "core.hooksPath", empty);
                // Index-only names exercise committed metadata without OS path restrictions.
                await fixture.GitAsync("config", "core.ignoreCase", "false");
                await fixture.GitAsync("config", "core.protectNTFS", "false");
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        internal Task<string> GitAsync(params string[] arguments) =>
            NativeProcess.RunAsync("git", Directory, arguments, token);

        internal async Task SetAsync(string path, string content = "fixture",
            string mode = "100644")
        {
            string file = Path.Combine(temporary, "content.txt");
            await File.WriteAllTextAsync(file, content, token);
            string blob = (await GitAsync("hash-object", "-w", "--", file)).Trim();
            await GitAsync("update-index", "--add", "--cacheinfo", mode, blob, path);
        }

        internal Task<string> LinkAsync(string path, string commit) =>
            GitAsync("update-index", "--add", "--cacheinfo", "160000", commit, path);

        internal Task<string> RemoveAsync(string path) =>
            GitAsync("update-index", "--force-remove", "--", path);

        internal async Task<string> CommitAsync(params string[] parents)
        {
            string tree = (await GitAsync("write-tree")).Trim();
            var arguments = new List<string> { "commit-tree", tree, "-m", "Fixture" };
            foreach (string parent in parents)
                arguments.AddRange(["-p", parent]);
            string commit = (await GitAsync(arguments.ToArray())).Trim();
            await GitAsync("update-ref", "HEAD", commit);
            return commit;
        }

        public void Dispose()
        {
            if (!System.IO.Directory.Exists(temporary))
                return;
            foreach (string file in System.IO.Directory.EnumerateFiles(temporary, "*",
                SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(temporary, recursive: true);
        }
    }
}
