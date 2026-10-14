using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class CiCallerTransferTests(TestContext context)
{
    [TestMethod]
    [DataRow("id", "12")]
    [DataRow("name", "\"ci-dotnet-plan\"")]
    [DataRow("expired", "true")]
    [DataRow("expired", "\"false\"")]
    [DataRow("workflow_run", "{\"id\":8,\"repository_id\":9}")]
    [DataRow("workflow_run", "{\"id\":7,\"repository_id\":10}")]
    [DataRow("workflow_run", "null")]
    public void ReceiptRejectsSubstitutedOrExpiredProducer(string field, string replacement)
    {
        var value = System.Text.Json.Nodes.JsonNode.Parse(Receipt())!.AsObject();
        value[field] = System.Text.Json.Nodes.JsonNode.Parse(replacement);
        using JsonDocument document = JsonDocument.Parse(value.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => CiArtifactReceipt.Validate(
            document.RootElement, "ci-scope", 11, 7, 9));
    }

    [TestMethod]
    [DataRow(11L, 7L, 9L, true)]
    [DataRow(0L, 7L, 9L, false)]
    [DataRow(11L, 0L, 9L, false)]
    [DataRow(11L, 7L, -1L, false)]
    public void ReceiptRequiresPositiveNativeIdentity(long artifact, long run, long repository,
        bool valid)
    {
        using JsonDocument document = JsonDocument.Parse(Receipt());
        if (valid) CiArtifactReceipt.Validate(document.RootElement, "ci-scope", artifact, run,
            repository);
        else Assert.ThrowsExactly<InvalidDataException>(() => CiArtifactReceipt.Validate(
            document.RootElement, "ci-scope", artifact, run, repository));
    }

    [TestMethod]
    public void CliRejectsDuplicateMetadataAndExtraArguments()
    {
        using var files = new CiCallerFiles();
        string metadata = Path.Combine(files.Root, "metadata.json");
        File.WriteAllText(metadata, Receipt().Replace("\"id\":11", "\"id\":12,\"id\":11",
            StringComparison.Ordinal));
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(2, Program.Run(["ci", "caller-artifact", metadata, "ci-scope", "11",
            "7", "9"], output, error));
        Assert.Contains("Duplicate", error.ToString());
        error.GetStringBuilder().Clear();
        File.WriteAllText(metadata, Receipt());
        Assert.AreEqual(0, Program.Run(["ci", "caller-artifact", metadata, "ci-scope", "11",
            "7", "9"], output, error));
        Assert.AreEqual(2, Program.Run(["ci", "caller-artifact", metadata, "ci-scope", "11",
            "7", "9", "extra"], output, error));
        Assert.Contains("argument count", error.ToString());
    }

    [TestMethod]
    public void TypedReadRejectsMissingUnknownAndDuplicateFields()
    {
        using var files = new CiCallerFiles();
        string path = Path.Combine(files.Root, "context.json");
        foreach (string payload in new[]
                 {
                     "{}",
                         "{\"application\":\"a\",\"root\":\"r\",\"transfer\":\"t\",\"unknown\":1}",
                     "{\"application\":\"a\",\"root\":\"r\",\"root\":\"other\",\"transfer\":\"t\"}",
                 })
        {
            File.WriteAllText(path, payload);
            if (payload.Contains("other", StringComparison.Ordinal))
                Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Read(path,
                    TransferJson.Default.CiCallerControl));
            else Assert.ThrowsExactly<JsonException>(() => CiCallerTransfer.Read(path,
                TransferJson.Default.CiCallerControl));
        }
    }

    [TestMethod]
    public async Task MaterializeUsesExactCommittedSeparateNodeEndpointsAndFullPythonReuse()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("source.txt", "original");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        using var files = new CiCallerFiles();
        string scope = files.Scope(commit);
        string nodeDirectory = Path.Combine(files.Root, "node");
        NodeCiCallerContext node = await NodeCiCaller.MaterializeAsync(repo.Directory, scope,
            files.Transfer(), nodeDirectory, context.CancellationToken);
        Assert.AreNotEqual(node.Basis.Directory, node.Candidate.Directory);
        Assert.AreEqual(commit, node.Basis.Reference);
        Assert.AreEqual(commit, node.Candidate.Reference);
        Assert.AreEqual("original", await
            File.ReadAllTextAsync(Path.Combine(node.Candidate.Directory, "source.txt"),
            context.CancellationToken));
        string pythonDirectory = Path.Combine(files.Root, "python");
        PythonCiCallerContext python = await PythonCiCaller.MaterializeAsync(repo.Directory, scope,
            files.Transfer(), pythonDirectory, context.CancellationToken);
        Assert.AreEqual(python.Basis, python.Candidate);
        Assert.IsTrue(Directory.Exists(python.ControlSources));
        Assert.AreEqual(scope, python.ScopePath);
        Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Fresh(nodeDirectory));
    }

    [TestMethod]
    public async Task MaterializeRejectsForeignCandidateBeforeCreatingState()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("source.txt");
        string commit = await repo.CommitAsync();
        using var files = new CiCallerFiles();
        string scope = files.Scope(commit);
        await repo.SetAsync("other.txt");
        await repo.CommitAsync(commit);
        string directory = Path.Combine(files.Root, "rejected");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            CiCallerTransfer.MaterializeAsync(
            repo.Directory, scope, directory, true, context.CancellationToken));
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void DistributionAndStateRejectMissingFilesAndSourceOverlap()
    {
        using var files = new CiCallerFiles();
        string transfer = files.Transfer();
        File.Delete(Path.Combine(transfer, "control", "WorkflowDelivery.deps.json"));
        Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.Application(transfer));
        Assert.ThrowsExactly<InvalidDataException>(() => CiCallerTransfer.External(files.Root,
            Path.Combine(files.Root, "state")));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            CiCallerTransfer.External(Path.Combine(files.Root, "source"), files.Root));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DoNotParallelize]
    public void ActionsDirectoryArrayIsOneNativeJsonLine(bool empty)
    {
        using var files = new CiCallerFiles();
        string path = Path.Combine(files.Root, "outputs");
        string? original = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        string[] expected = empty ? [] : ["src/ordinary", "src/with spaces", "quote\"\\\n\r"];
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_OUTPUT", path);
            CiCallerTransfer.OutputArray("retained_directories", expected);
            string[] lines = File.ReadAllLines(path);
            string line = Assert.ContainsSingle(lines);
            Assert.StartsWith("retained_directories=", line);
            string[] actual = JsonSerializer.Deserialize(line["retained_directories=".Length..],
                TransferJson.Default.StringArray)!;
            CollectionAssert.AreEqual(expected, actual);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                CiCallerTransfer.Output("bad", "a\nb"));
            Assert.HasCount(1, File.ReadAllLines(path));
        }
        finally { Environment.SetEnvironmentVariable("GITHUB_OUTPUT", original); }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ExternalRejectsLinkedAncestorsBeforeStateCreation(bool linkedSource)
    {
        using var files = new CiCallerFiles();
        string source = Directory.CreateDirectory(Path.Combine(files.Root, "source")).FullName;
        string alias = Path.Combine(files.Root, "alias");
        Directory.CreateSymbolicLink(alias, source);
        try
        {
            string state = linkedSource ? Path.Combine(source, "state") :
                Path.Combine(alias, "missing", "state");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                CiCallerTransfer.External(linkedSource ? alias : source, state));
            Assert.IsFalse(Directory.Exists(state));
            Assert.AreEqual(alias, CiCallerTransfer.Absolute(alias));
            CiCallerTransfer.External(source, Path.Combine(files.Root, "outside", "state"));
        }
        finally { Directory.Delete(alias); }
    }

    [TestMethod]
    [DataRow("{\"full\":false}")]
    [DataRow("{\"full\":\"true\"}")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void CliLocalFullRejectsIncrementalOrMalformedScope(string payload)
    {
        using var files = new CiCallerFiles();
        string scope = Path.Combine(files.Root, "scope.json");
        File.WriteAllText(scope, payload);
        using var output = new StringWriter();
        using var error = new StringWriter();
        string state = Path.Combine(files.Root, "state");
        Assert.AreEqual(2, Program.Run(["ci", "caller-node", "local-full", files.Root,
            scope, Path.Combine(files.Root, "transfer"), state], output, error));
        Assert.IsFalse(Directory.Exists(state));
        Assert.IsNotEmpty(error.ToString());
    }

    private static string Receipt() =>
        "{\"id\":11,\"name\":\"ci-scope\",\"expired\":false," +
        "\"workflow_run\":{\"id\":7,\"repository_id\":9}}";
}

internal sealed class CiCallerFiles : IDisposable
{
    internal string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
        "ci caller " + Guid.NewGuid().ToString("N"))).FullName;

    internal string Transfer()
    {
        string transfer = Path.Combine(Root, "transfer");
        Directory.CreateDirectory(Path.Combine(transfer, "control"));
        foreach (string file in new[] { "WorkflowDelivery.dll", "WorkflowDelivery.deps.json",
            "WorkflowDelivery.runtimeconfig.json" })
            File.WriteAllText(Path.Combine(transfer, "control", file), "fixture");
        Directory.CreateDirectory(Path.Combine(transfer, "ruby"));
        File.WriteAllText(Path.Combine(transfer, "ruby", "facts.rb"), "fixture");
        string archive = Path.Combine(transfer, "python-control.zip");
        if (!File.Exists(archive))
        {
            using var zip = System.IO.Compression.ZipFile.Open(archive,
                System.IO.Compression.ZipArchiveMode.Create);
            zip.CreateEntry("src/private/app/workflow-delivery/Native/Python/passive.py");
        }
        return transfer;
    }

    internal string Scope(string commit)
    {
        string path = Path.Combine(Root, "scope.json");
        File.WriteAllText(path, """
            {"base":"@commit","candidate":"@commit","full":true,"changed_paths":[],
             "endpoint_owners":{"basis":{"revision":"@commit","paths":[]},
                "candidate":{"revision":"@commit","paths":[]}}}
            """.Replace("@commit", commit, StringComparison.Ordinal));
        return path;
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
