using System.Diagnostics;
using System.Text;
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
    public async Task CompareCancellationDoesNotReturnSuccessfulFacts()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancellation.CancelAsync();

        // Cancellation is observed before Git starts, so no repository is needed.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new GitReader(Path.GetTempPath()).CompareAsync("HEAD", "HEAD",
                cancellation.Token));
    }

    private static string[] Paths(GitRevision revision) =>
        revision.Entries.Select(entry => entry.Path).ToArray();

    // Index contract: staged edits and commits update an in-memory model; the on-disk index is
    // rebuilt only by GitAsync (except reset --hard, which rewrites it) or FlushAsync. CommitAsync
    // moves HEAD but leaves the index stale, so production code that reads the checkout must
    // follow a GitAsync call (usually reset --hard or checkout) or an explicit FlushAsync.
    internal sealed class GitFixture(bool legacyNbgvCleanup, CancellationToken token) : IDisposable
    {
        private readonly string temporary = Path.Combine(Path.GetTempPath(),
            "workflow git facts " + Guid.NewGuid().ToString("N"));
        internal string Directory => Path.Combine(temporary, "repository");

        internal static async Task<GitFixture> CreateAsync(CancellationToken token,
            string format = "sha1", bool legacyNbgvCleanup = false)
        {
            var fixture = new GitFixture(legacyNbgvCleanup, token);
            try
            {
                System.IO.Directory.CreateDirectory(fixture.Directory);
                string empty = Path.Combine(fixture.temporary, "empty-template");
                System.IO.Directory.CreateDirectory(empty);
                // One template config replaces per-key launches; Git copies it during init.
                string hooks = empty.Replace('\\', '/');
                await File.WriteAllTextAsync(Path.Combine(empty, "config"),
                    "[user]\n\tname = Workflow fixture\n\temail = fixture@example.invalid\n" +
                    "[commit]\n\tgpgSign = false\n[tag]\n\tgpgSign = false\n[gc]\n\tauto = 0\n" +
                    "[core]\n\thooksPath = \"" + hooks + "\"\n\tprotectNTFS = false\n", token);
                await fixture.GitAsync("init", "--quiet", "--initial-branch=fixture",
                    "--object-format=" + format, "--template=" + empty);
                // Index-only names exercise committed metadata without OS path restrictions.
                // Init probes and may write core.ignoreCase, so this override follows it.
                await fixture.GitAsync("config", "core.ignoreCase", "false");
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        // The staged state is modelled in memory and each commit is one git fast-import
        // stream. The index is rebuilt only before a later Git command that may observe it.
        private sealed record Entry(string Mode, string? Object, string? Content);

        // Commands that neither read nor write the index or HEAD. Pending content is hashed
        // when committed or flushed, so it uses the configuration current at that time, not
        // at the time it was staged (only CRLF content is hashed under configuration).
        private static readonly HashSet<string> IndexNeutral = new(StringComparer.Ordinal)
            { "branch", "config", "fetch", "init", "replace", "tag", "worktree" };

        private static readonly UTF8Encoding Utf8 = new(false);
        private readonly Dictionary<string, Dictionary<string, Entry>> commits =
            new(StringComparer.Ordinal);
        // Null means another Git command may have changed the index; reload lazily.
        private Dictionary<string, Entry>? model = new(StringComparer.Ordinal);
        private bool indexCurrent = true;
        private string? modelCommit;
        private string? head;

        internal async Task<string> GitAsync(params string[] arguments)
        {
            // A hard reset rewrites the index from its target, so pending edits are moot.
            bool hardReset = arguments is ["reset", "--hard", _];
            bool neutral = arguments.Length != 0 && IndexNeutral.Contains(arguments[0]);
            if (!hardReset && !neutral)
                await FlushAsync();
            string output = await RunAsync(arguments);
            if (hardReset && commits.TryGetValue(arguments[2] == "HEAD" ? head ?? ""
                : arguments[2], out var tree))
            {
                head = modelCommit = arguments[2] == "HEAD" ? head : arguments[2];
                model = new(tree, StringComparer.Ordinal);
                indexCurrent = true;
            }
            else if (!neutral && arguments is not ["status", ..])
            {
                model = null;
                modelCommit = head = null;
                indexCurrent = true;
            }
            return output;
        }

        internal async Task SetAsync(string path, string content = "fixture",
            string mode = "100644")
        {
            Dictionary<string, Entry> entries = await ModelAsync();
            Validate(path, entries);
            entries[path] = new(mode, null, content);
            indexCurrent = false;
            modelCommit = null;
        }

        internal async Task LinkAsync(string path, string commit)
        {
            Dictionary<string, Entry> entries = await ModelAsync();
            Validate(path, entries);
            entries[path] = new("160000", commit, null);
            indexCurrent = false;
            modelCommit = null;
        }

        internal async Task RemoveAsync(string path)
        {
            Validate(path, null);
            (await ModelAsync()).Remove(path);
            indexCurrent = false;
            modelCommit = null;
        }

        // Writes the cumulative staged tree as a commit with these parents and moves HEAD
        // (or the branch it names) to it, like commit-tree plus update-ref HEAD. Repeated
        // parent arguments are dropped in order, as commit-tree does for repeated ids.
        // The index is left stale rather than rebuilt here: nearly every commit is followed
        // by reset --hard, which rewrites the index anyway, so an eager read-tree would add
        // one launch per commit. When edits were staged, the next GitAsync or FlushAsync
        // rebuilds the index from this commit; otherwise it already holds the same tree.
        internal async Task<string> CommitAsync(params string[] parents)
        {
            Dictionary<string, Entry> entries = await ModelAsync();
            string commit = await ImportAsync(entries, "HEAD",
                parents.Distinct(StringComparer.Ordinal).ToArray());
            commits[commit] = new(entries, StringComparer.Ordinal);
            head = modelCommit = commit;
            return commit;
        }

        // Applies staged index edits now, for scenarios that leave them uncommitted.
        internal async Task FlushAsync()
        {
            if (indexCurrent || model is null)
                return;
            // An unreferenced scratch commit yields the staged tree without moving a ref.
            string tree = modelCommit ?? await ImportAsync(model, "refs/fixture/index", [],
                scratch: true);
            // Single-tree -m keeps stat data for unchanged entries, as update-index did.
            await RunAsync(["--no-replace-objects", "read-tree", "-m", tree]);
            indexCurrent = true;
        }

        // Rejects paths update-index rejected, at staging time rather than at a later commit.
        private static void Validate(string path, Dictionary<string, Entry>? entries)
        {
            foreach (string segment in path.Split('/'))
                if (segment is "" or "." or ".." || Path.IsPathRooted(path) ||
                    string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Invalid fixture path: {path}", nameof(path));
            if (entries is null)
                return;
            for (int slash = path.IndexOf('/', StringComparison.Ordinal); slash >= 0;
                slash = path.IndexOf('/', slash + 1))
                if (entries.ContainsKey(path[..slash]))
                    throw new ArgumentException(
                        $"Fixture path is below a file: {path}", nameof(path));
            string directory = path + "/";
            if (entries.Keys.Any(key => key.StartsWith(directory, StringComparison.Ordinal)))
                throw new ArgumentException(
                    $"Fixture path is a directory: {path}", nameof(path));
        }

        private async Task<Dictionary<string, Entry>> ModelAsync()
        {
            if (model is not null)
                return model;
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (string line in (await RunAsync(["ls-files", "--stage", "-z"])).Split('\0',
                StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line[..line.IndexOf('\t', StringComparison.Ordinal)].Split(' ');
                if (fields[2] != "0")
                    throw new InvalidDataException("The fixture index has unmerged entries.");
                entries[line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..]] =
                    new(fields[0], fields[1], null);
            }
            return model = entries;
        }

        private async Task<string> ImportAsync(Dictionary<string, Entry> entries,
            string reference, string[] parents, bool scratch = false)
        {
            await HashCarriageReturnsAsync(entries);
            using var stream = new MemoryStream();
            void Write(string text) => stream.Write(Utf8.GetBytes(text));
            var marks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((string path, Entry entry) in entries.Where(item => item.Value.Object is null))
            {
                byte[] data = Utf8.GetBytes(entry.Content!);
                marks[path] = marks.Count + 2;
                Write($"blob\nmark :{marks[path]}\ndata {data.Length}\n");
                stream.Write(data);
                Write("\n");
            }
            const string identity = "Workflow fixture <fixture@example.invalid> now\n";
            // Reset first so a commit without parents is a root even on an existing ref.
            Write($"reset {reference}\ncommit {reference}\nmark :1\nauthor {identity}" +
                $"committer {identity}data 8\nFixture\n");
            for (int index = 0; index < parents.Length; index++)
                Write((index == 0 ? "from " : "merge ") + parents[index] + "^0\n");
            Write("deleteall\n");
            foreach ((string path, Entry entry) in entries)
                Write($"M {entry.Mode} {entry.Object ?? ":" + marks[path]} {Quote(path)}\n");
            Write(scratch ? "\nls :1 \"\"\n" : "\nget-mark :1\n");
            foreach (int mark in marks.Values)
                Write($"get-mark :{mark}\n");
            if (scratch)
                Write($"reset {reference}\n");
            Write("done\n");
            string[] lines = (await FastImportAsync(stream.ToArray())).Split('\n',
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != marks.Count + 1)
                throw new InvalidDataException("Git fast-import returned unexpected output.");
            int line = 1;
            // Later streams reference stored blobs instead of resending their content.
            foreach (string path in marks.Keys)
                entries[path] = entries[path] with { Object = lines[line++], Content = null };
            return scratch ? lines[0].Split(' ', '\t')[2] : lines[0];
        }

        // fast-import stores bytes verbatim; content Git may convert (CRLF under autocrlf)
        // keeps today's hash-object conversion.
        private async Task HashCarriageReturnsAsync(Dictionary<string, Entry> entries)
        {
            string[] paths = entries.Where(item => item.Value.Content?.Contains('\r') == true)
                .Select(item => item.Key).ToArray();
            if (paths.Length == 0)
                return;
            string content = Path.Combine(temporary, "content");
            System.IO.Directory.CreateDirectory(content);
            var files = new List<string>();
            foreach (string path in paths)
            {
                string file = Path.Combine(content, files.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                await File.WriteAllTextAsync(file, entries[path].Content, token);
                files.Add(file);
            }
            var blobs = new List<string>();
            foreach (string[] chunk in Chunks(files))
                blobs.AddRange((await RunAsync(["hash-object", "-w", "--", .. chunk])).Split('\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (blobs.Count != files.Count)
                throw new InvalidDataException("Git returned an unexpected blob count.");
            for (int index = 0; index < paths.Length; index++)
                entries[paths[index]] = entries[paths[index]] with
                { Object = blobs[index], Content = null };
        }

        private static string Quote(string path)
        {
            var quoted = new StringBuilder("\"");
            foreach (char value in path)
                quoted.Append(value switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\n' => "\\n",
                    < ' ' => "\\" + Convert.ToString((int)value, 8).PadLeft(3, '0'),
                    _ => value.ToString(),
                });
            return quoted.Append('"').ToString();
        }

        private Task<string> RunAsync(string[] arguments) =>
            NativeProcess.RunAsync("git", Directory, arguments, token);

        private async Task<string> FastImportAsync(byte[] input)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var process = new Process
            {
                StartInfo = new("git")
                {
                    WorkingDirectory = Directory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            // Fixture imports stay loose objects, as the former hash-object/commit-tree path
            // wrote them. The default limit (100) packs larger imports, and in-process readers
            // such as NBGV map pack files until finalization, which blocks Windows cleanup.
            foreach (string argument in new[] { "--no-replace-objects", "-c",
                "fastimport.unpackLimit=1000000", "fast-import", "--quiet", "--force", "--done",
                "--date-format=now" })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
                    process.StandardInput.Close();
                }
                // A failed import closes its input early; its exit code and stderr explain why.
                catch (IOException) { }
                await process.WaitForExitAsync(timeout.Token);
                string message = await error;
                if (process.ExitCode != 0)
                    throw new InvalidDataException(
                        $"Git fast-import failed with exit code {process.ExitCode}: {message}");
                return await output;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("Git fast-import exceeded its 30-second deadline.");
            }
            finally
            {
                await StopAsync(process);
            }
        }

        // Kill is asynchronous, so cleanup waits for exit under its own bounded deadline.
        private static async Task StopAsync(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(cleanup.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "Git fast-import cleanup exceeded its 5-second deadline.");
            }
        }

        // Keeps each launch well below the Windows command-line limit.
        private static IEnumerable<string[]> Chunks(IEnumerable<string> values)
        {
            var chunk = new List<string>();
            int length = 0;
            foreach (string value in values)
            {
                if (chunk.Count != 0 && length + value.Length > 16000)
                {
                    yield return chunk.ToArray();
                    chunk.Clear();
                    length = 0;
                }
                chunk.Add(value);
                length += value.Length + 16;
            }
            if (chunk.Count != 0)
                yield return chunk.ToArray();
        }

        public void Dispose()
        {
            if (!System.IO.Directory.Exists(temporary))
                return;
            if (OperatingSystem.IsWindows() && legacyNbgvCleanup)
            {
                // NBGV 3.10.94 leaves committed version.txt streams open until finalization.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            foreach (string file in System.IO.Directory.EnumerateFiles(temporary, "*",
                SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(temporary, recursive: true);
        }
    }
}
