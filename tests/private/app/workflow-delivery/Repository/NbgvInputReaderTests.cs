using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NbgvInputReaderTests(TestContext context)
{
    [TestMethod]
    public async Task ReadBindsCommittedFiltersDespiteDifferentHeadAndWorkingCopy()
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("version.json", """
            {"version":"1.2","pathFilters":[":/shared",":!version.json"]}
            """);
        await repo.SetAsync("product/version.json", """
            {"inherit":true,"pathFilters":[".","../shared",":!excluded"]}
            """);
        await repo.SetAsync("product/source.cs");
        await repo.SetAsync("product/excluded/source.cs");
        await repo.SetAsync("shared/input.txt");
        await repo.SetAsync("unrelated/source.cs");
        string basis = await repo.CommitAsync();
        await repo.SetAsync("product/version.json", """
            {"version":"2.4","pathFilters":["../unrelated"]}
            """);
        string candidate = await repo.CommitAsync(basis);
        System.IO.Directory.CreateDirectory(Path.Combine(repo.Directory, "product"));
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, "product/version.json"),
            "not valid JSON", context.CancellationToken);
        var git = new GitReader(repo.Directory);
        var reader = new NbgvInputReader(repo.Directory);

        NbgvInputs before = reader.Read(await git.ReadAsync(basis, context.CancellationToken),
            "product", context.CancellationToken);
        NbgvInputs after = reader.Read(await git.ReadAsync(candidate, context.CancellationToken),
            "product", context.CancellationToken);

        Assert.AreEqual(basis, before.Commit);
        Assert.AreEqual(candidate, after.Commit);
        Assert.AreEqual("product", before.Directory);
        string[] expectedBefore = ["version.json", "product/version.json",
            "product/source.cs", "shared/input.txt"];
        string[] expectedAfter = ["version.json", "product/version.json", "unrelated/source.cs"];
        string[] candidates = ["version.txt", "version.json",
            "product/version.txt", "product/version.json"];
        CollectionAssert.AreEquivalent(expectedBefore, before.Paths);
        CollectionAssert.AreEquivalent(expectedAfter, after.Paths);
        CollectionAssert.AreEqual(candidates, before.ConfigurationCandidates);
    }

    [TestMethod]
    public async Task ReadRetainsConfigurationChangesAndDeletedProjectInputs()
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("version.json", """
            {"version":"1.2","pathFilters":[":/shared"]}
            """);
        await repo.SetAsync("product/source.cs");
        await repo.SetAsync("shared/input.txt");
        string basis = await repo.CommitAsync();
        await repo.SetAsync("product/version.json", """
            {"inherit":true,"pathFilters":["."]}
            """);
        string added = await repo.CommitAsync(basis);
        await repo.RemoveAsync("product/version.json");
        string deleted = await repo.CommitAsync(added);
        await repo.RemoveAsync("product/source.cs");
        string removed = await repo.CommitAsync(deleted);
        var reader = new NbgvInputReader(repo.Directory);
        var git = new GitReader(repo.Directory);
        var values = new List<NbgvInputs>();
        foreach (string commit in new[] { basis, added, deleted })
            values.Add(reader.Read(await git.ReadAsync(commit, context.CancellationToken),
                "product", context.CancellationToken));

        Assert.Contains("shared/input.txt", values[0].Paths);
        Assert.DoesNotContain("product/source.cs", values[0].Paths);
        Assert.Contains("product/source.cs", values[1].Paths);
        Assert.Contains("product/version.json", values[1].Paths);
        Assert.DoesNotContain("shared/input.txt", values[1].Paths);
        CollectionAssert.AreEqual(values[0].Paths, values[2].Paths);
        foreach (NbgvInputs value in values)
            Assert.Contains("product/version.json", value.ConfigurationCandidates);
        Assert.Contains("shared/input.txt", values[2].Paths);
        GitRevision missingProject = await git.ReadAsync(removed, context.CancellationToken);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            reader.Read(missingProject, "product", context.CancellationToken));
    }

    [TestMethod]
    [DataRow("", true)]
    [DataRow(",\"pathFilters\":[]", true)]
    [DataRow(",\"pathFilters\":[\":!unrelated\"]", false)]
    public async Task ReadUsesNativeDefaultAndExclusionOnlyFilters(string filters,
        bool includesUnrelated)
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("version.json", "{\"version\":\"1.2\"" + filters + "}");
        await repo.SetAsync("product/source.cs");
        await repo.SetAsync("unrelated/source.cs");
        string commit = await repo.CommitAsync();
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);

        NbgvInputs value = new NbgvInputReader(repo.Directory).Read(revision, "product",
            context.CancellationToken);

        Assert.Contains("product/source.cs", value.Paths);
        Assert.Contains("version.json", value.Paths);
        Assert.AreEqual(includesUnrelated, value.Paths.Contains("unrelated/source.cs"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadUsesNativeRepositoryCaseSemantics(bool ignoreCase)
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.GitAsync("config", "core.ignoreCase", ignoreCase ? "true" : "false");
        await repo.SetAsync("version.json", """
            {"version":"1.2","pathFilters":[":/PRODUCT",":!PRODUCT/EXCLUDED"]}
            """);
        await repo.SetAsync("product/source.cs");
        await repo.SetAsync("product/excluded/source.cs");
        string commit = await repo.CommitAsync();
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);

        NbgvInputs value = new NbgvInputReader(repo.Directory).Read(revision, "product",
            context.CancellationToken);

        Assert.AreEqual(ignoreCase, value.Paths.Contains("product/source.cs"));
        Assert.DoesNotContain("product/excluded/source.cs", value.Paths);
        Assert.Contains("version.json", value.Paths);
    }

    [TestMethod]
    public async Task ReadRetainsGitlinkWhenNativeFiltersIncludeItsChildren()
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("version.json", """
            {"version":"1.2","pathFilters":[":/vendor/selected/nested"]}
            """);
        await repo.SetAsync("product/source.cs");
        string seed = await repo.CommitAsync();
        await repo.LinkAsync("vendor/selected", seed);
        await repo.LinkAsync("vendor/unrelated", seed);
        string commit = await repo.CommitAsync(seed);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);

        NbgvInputs value = new NbgvInputReader(repo.Directory).Read(revision, "product",
            context.CancellationToken);

        Assert.Contains("vendor/selected", value.Paths);
        Assert.DoesNotContain("vendor/unrelated", value.Paths);
        Assert.DoesNotContain("product/source.cs", value.Paths);
        Assert.Contains("version.json", value.Paths);
    }

    [TestMethod]
    public async Task ReadKeepsAncestorConfigurationInputsAfterNativeOverrides()
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("version.txt", "1.0");
        await repo.SetAsync("version.json", """
            {"version":"1.2","pathFilters":[":/unrelated"]}
            """);
        await repo.SetAsync("nested/version.json", "{\"version\":\"2.0\"}");
        await repo.SetAsync("nested/product/version.json", """
            {"version":"3.0","pathFilters":["source.cs",":!version.json"]}
            """);
        await repo.SetAsync("nested/product/source.cs");
        await repo.SetAsync("unrelated/source.cs");
        string commit = await repo.CommitAsync();
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        var reader = new NbgvInputReader(repo.Directory);

        NbgvInputs value = reader.Read(revision, "nested/product", context.CancellationToken);
        NbgvInputs root = reader.Read(revision, ".", context.CancellationToken);

        string[] inputs = ["version.txt", "version.json", "nested/version.json",
            "nested/product/version.json", "nested/product/source.cs"];
        CollectionAssert.AreEquivalent(inputs, value.Paths);
        Assert.HasCount(6, value.ConfigurationCandidates);
        Assert.Contains("nested/version.txt", value.ConfigurationCandidates);
        Assert.Contains("nested/product/version.txt", value.ConfigurationCandidates);
        string[] rootInputs = ["version.txt", "version.json", "unrelated/source.cs"];
        CollectionAssert.AreEquivalent(rootInputs, root.Paths);
        Assert.HasCount(2, root.ConfigurationCandidates);
        Assert.AreEqual(".", root.Directory);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{\"inherit\":true}")]
    public async Task ReadRejectsUnavailableNativeFacts(string options)
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("product/source.cs");
        if (options.Length != 0)
            await repo.SetAsync("version.json", options);
        string commit = await repo.CommitAsync();
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        var reader = new NbgvInputReader(repo.Directory);

        if (options.Length == 0)
            Assert.ThrowsExactly<InvalidDataException>(() => reader.Read(revision,
                "product", context.CancellationToken));
        else
            Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read(revision,
                "product", context.CancellationToken));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("../outside")]
    [DataRow("product/../other")]
    [DataRow("product\\nested")]
    public void ReadRejectsInvalidProjectScope(string directory)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => new NbgvInputReader(".").Read(
            new("HEAD", []), directory, context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadRejectsMissingExactSubjectAndNonexactIdentity()
    {
        using var repo = await GitReaderTests.GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("version.json", "{\"version\":\"1.2\"}");
        await repo.SetAsync("product/source.cs");
        string commit = await repo.CommitAsync();
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        var reader = new NbgvInputReader(repo.Directory);

        Assert.ThrowsExactly<InvalidDataException>(() => reader.Read(revision with
            { Commit = "HEAD" }, "product", context.CancellationToken));
        // Native evaluation failures propagate; their exception classes belong to NBGV.
        Assert.Throws<Exception>(() => reader.Read(revision with { Commit = new string('f', 40) },
            "product", context.CancellationToken));
    }

    [TestMethod]
    public void ReadCancellationReturnsNoFacts()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => new NbgvInputReader(".").Read(
            new("HEAD", []), "product", cancellation.Token));
    }
}
