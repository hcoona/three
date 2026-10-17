using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class CiScopeTests
{
    private const string Basis = "1111111111111111111111111111111111111111";

    // Pinned against eng/scripts/repository_path_patterns.py over every current binding.
    [TestMethod]
    [DataRow("README.md", "README.md", true)]
    [DataRow("x/README.md", "README.md", false)]
    [DataRow("README.md/x", "README.md", false)]
    [DataRow("docs/engineering/agent-execution.md", "docs/engineering/*.md", true)]
    [DataRow("docs/engineering/x/y.md", "docs/engineering/*.md", false)]
    [DataRow("docs/engineering/.md", "docs/engineering/*.md", true)]
    [DataRow("docs/README.MD", "docs/README.md", false)]
    [DataRow("src/private/app/workflow-delivery/docs/a.md",
        "src/private/app/workflow-delivery/docs/**/*.md", true)]
    [DataRow("src/private/app/workflow-delivery/docs/research/x/y.md",
        "src/private/app/workflow-delivery/docs/**/*.md", true)]
    [DataRow("src/private/app/workflow-delivery/docs/.hidden.md",
        "src/private/app/workflow-delivery/docs/**/*.md", true)]
    [DataRow("src/private/app/workflow-delivery/docs/a.txt",
        "src/private/app/workflow-delivery/docs/**/*.md", false)]
    [DataRow("src/private/app/workflow-delivery/README.md",
        "src/private/app/workflow-delivery/docs/**/*.md", false)]
    [DataRow(".apm/skills/migrate-xunit-to-mstest/a/b.md",
        ".apm/skills/migrate-xunit-to-mstest/**/*.md", true)]
    [DataRow("src/private/app/im-acp-gateway/poc/a/README.md",
        "src/private/app/im-acp-gateway/poc/*/README.md", true)]
    [DataRow("src/private/app/im-acp-gateway/poc/a/b/README.md",
        "src/private/app/im-acp-gateway/poc/*/README.md", false)]
    [DataRow("src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/CLARIFY-1.md",
        "src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/CLARIFY*.md", true)]
    [DataRow("schemas/governance/record.schema.json", "schemas/governance/*.schema.json", true)]
    [DataRow("schemas/governance/record.json", "schemas/governance/*.schema.json", false)]
    public void RecordMatcherUsesComponentAwareStars(string path, string pattern, bool expected) =>
        Assert.AreEqual(expected, CiScope.Matches(path, pattern));

    [TestMethod]
    public void CurrentCatalogBindingsAreReadWithoutSchemaAdmission()
    {
        string catalog = File.ReadAllText(Path.Combine(Root(), CiScope.FamilyCatalog));
        (string Id, string Pattern)[] bindings = CiScope.Bindings(catalog);
        Assert.IsNotEmpty(bindings);
        Assert.Contains(("agent-interface", "AGENTS.md"), bindings);
        (string Id, string Pattern)[] read = CiScope.Bindings("""
            schema_version: 99
            unknown: ignored
            bindings:
              - {id: kept, state: current, path: "a/*.md", extra: ignored}
              - {id: later, state: scheduled, path: b.md}
            """);
        CollectionAssert.AreEqual(new[] { ("kept", "a/*.md") }, read);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("bindings: {}")]
    [DataRow("bindings: [{id: a, path: b.md}]")]
    [DataRow("bindings: [{id: a, state: current, path: [b.md]}]")]
    [DataRow("bindings: [")]
    [DataRow("bindings: [{id: a, state: scheduled, path: a/x**y}]")]
    [DataRow("bindings: [{id: a, state: current, path: 'a**'}]")]
    public void UnreadableBindingsFailInsteadOfDroppingResponsibility(string catalog) =>
        Assert.ThrowsExactly<InvalidDataException>(() => CiScope.Bindings(catalog));

    [TestMethod]
    public void NativeMiseTasksNameTheRecordScripts()
    {
        RecordTaskOwner[] owners = CiScope.RecordTasks("""
            [{"name":"//:records:check","dir":"/repo",
              "run":["uv run --script eng/scripts/check_repository_records.py"]},
             {"name":"//:records:test","dir":"/repo/",
              "run":["uv run --script tests/eng/test_repository_records.py"]},
             {"name":"//:other","run":["anything"]}]
            """, "/repo");
        CollectionAssert.AreEqual(new[] {
            new RecordTaskOwner("eng/scripts/check_repository_records.py",
                "record-control:repository-record-validation"),
            new RecordTaskOwner("tests/eng/test_repository_records.py",
                "record-regression:records:test") }, owners);
        Assert.ThrowsExactly<InvalidDataException>(() => CiScope.RecordTasks(
            """[{"name":"//:records:check","dir":"/repo","run":["python checker.py"]}]""",
            "/repo"));
        Assert.ThrowsExactly<InvalidDataException>(() => CiScope.RecordTasks("""
            [{"name":"//:records:check","dir":"/repo/tools",
              "run":["uv run --script eng/record_check.py"]}]
            """, "/repo"));
        Assert.ThrowsExactly<InvalidDataException>(() => CiScope.RecordTasks("{}", "/repo"));
    }

    [TestMethod]
    [DataRow("docs/delivery-wave.md", "record-system", "record-binding:delivery-wave")]
    [DataRow("eng/scripts/check_repository_records.py", "record-system",
        "record-control:repository-record-validation")]
    [DataRow(CiScope.RecordMatcher, "record-system",
        "record-control:repository-record-validation")]
    [DataRow("tests/eng/test_repository_records.py", "record-system",
        "record-regression:records:test")]
    [DataRow(".github/CODEOWNERS", "github", "native-codeowners-routing")]
    [DataRow("hk.pkl", "conformance", "native-hk-configuration")]
    [DataRow("src/private/lib/hk/Config.pkl", "conformance", "native-hk-configuration")]
    [DataRow("src/private/lib/hk/steps/Typos.pkl", "conformance", "native-hk-configuration")]
    [DataRow("renovate.json", "python", "configured-python-runtime-input")]
    [DataRow("mise.lock", "mise", "retained-special-job-input")]
    [DataRow("eng/native-resource.json", "dotnet", "native-embedded-resource")]
    [DataRow("src/private/lib/scholarly-publication/x.py", "scholarly",
        "retained-special-job-input")]
    [DataRow("src/lib/gem/lib/a.rb", "ruby", "retained-gem-input")]
    public void PresentCoordinatesReceiveTheirFiniteResponsibility(string path, string owner,
        string rule)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path],
            ["eng/native-resource.json"], [("delivery-wave", "docs/delivery-wave.md")], Tasks, Hk));
        Assert.IsTrue(row.Present);
        Assert.IsTrue(row.Reasons.Any(reason => reason.Owner == owner && reason.Rule == rule),
            string.Join(", ", row.Reasons.Select(reason => reason.Owner + ":" + reason.Rule)));
    }

    [TestMethod]
    [DataRow("tests/eng/test_repository_records.py", "record-regression:records:test")]
    [DataRow("eng/scripts/check_repository_records.py",
        "record-control:repository-record-validation")]
    [DataRow(CiScope.RecordMatcher, "record-control:repository-record-validation")]
    public void RecordTaskScriptsAreNotPythonGroupInputs(string path, string rule)
    {
        // PEP 723 record-task scripts run under their mise task; the Python group cannot
        // resolve them, so a Python reason would fail its planning (PR #1226).
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        ScopeResponsibility reason = Assert.ContainsSingle(row.Reasons);
        Assert.AreEqual("record-system", reason.Owner);
        Assert.AreEqual(path, reason.Target);
        Assert.AreEqual(rule, reason.Rule);
    }

    [TestMethod]
    [DataRow("tests/eng/test_repository_records.py")]
    [DataRow("eng/scripts/check_repository_records.py")]
    [DataRow(CiScope.RecordMatcher)]
    public void RecordTaskDiscoveryDecidesPythonExclusion(string path)
    {
        // Without native record-task metadata the path is an ordinary Python caller input.
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], [], Hk));
        Assert.AreEqual("python", Assert.ContainsSingle(row.Reasons).Owner);
        // Other tests/eng paths keep their Python responsibility.
        row = Assert.ContainsSingle(CiScope.Owners(Revision("tests/eng/test_typos_config.py"),
            ["tests/eng/test_typos_config.py"], [], [], Tasks, Hk));
        Assert.AreEqual("python", Assert.ContainsSingle(row.Reasons).Owner);
    }

    [TestMethod]
    [DataRow("docs/unbound/notes.md")]
    [DataRow("src/public/lib/hexo-renderer-asciidoc/index.js")]
    [DataRow("docs/root-gem.md")]
    public void UnownedPresentCoordinatesStayUnresolvedForNativeConsumers(string path)
    {
        // A root gemspec is not a retained owner of every repository path.
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(
            Revision(path, "root.gemspec"), [path], [],
            [("delivery-wave", "docs/delivery-wave.md")], Tasks, Hk));
        Assert.IsTrue(row.Present);
        Assert.IsEmpty(row.Reasons);
    }

    [TestMethod]
    public void AbsentCoordinatesAreConfirmedAbsenceWithoutReasons()
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(),
            ["docs/delivery-wave.md"], [], [("delivery-wave", "docs/delivery-wave.md")], Tasks,
            Hk));
        Assert.IsFalse(row.Present);
        Assert.IsNull(row.Mode);
        Assert.IsEmpty(row.Reasons);
    }

    [TestMethod]
    [DataRow("docs/README.md", false, false, false)]
    [DataRow("mise.toml", true, true, true)]
    [DataRow("src/mise.toml", true, false, false)]
    [DataRow("eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1", false, true,
        false)]
    [DataRow("src/public/lib/nbgv-python/src/nbgv_python/cli.py", false, true, false)]
    [DataRow("uv.lock", false, true, false)]
    [DataRow("src/private/lib/scholarly-publication/tests/test_x.py", false, false, true)]
    [DataRow("apm.lock.yaml", false, false, true)]
    public void RetainedJobsFollowTheirFiniteInputs(string path, bool mise, bool azure,
        bool scholarly)
    {
        CiScopeApplicability result = CiScope.Select([path], Comparison(path), false, []);
        Assert.AreEqual(mise, result.Mise);
        Assert.AreEqual(azure, result.AzureAuth);
        Assert.AreEqual(scholarly, result.Scholarly);
        Assert.AreEqual("source conformance", Assert.ContainsSingle(result.Reasons["validation"]));
    }

    [TestMethod]
    [DataRow("src/private/app/workflow-delivery/Release/NuGetPreparation.cs", true)]
    [DataRow("tests/private/app/workflow-delivery/Release/NuGetPreparationNativeTests.cs", true)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-github-packages/Smoke.cs", true)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/Directory.Build.props",
        true)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/shared.cs", false)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-ruby/.gitattributes", false)]
    [DataRow("src/private/Directory.Build.targets", true)]
    [DataRow("src/public/.gitattributes", false)]
    [DataRow("global.json", true)]
    [DataRow("stylecop.json", true)]
    [DataRow(".github/workflows/ci.yml", true)]
    [DataRow("pyproject.toml", false)]
    [DataRow("docs/README.md", false)]
    [DataRow("eng/native-resource.json", true)]
    public void NuGetNativePreparationFollowsItsManagedAndProductInputs(string path,
        bool selected)
    {
        Assert.AreEqual(selected, CiScope.Select([path], Comparison(path), false,
            ["eng/native-resource.json"]).NuGetNativePreparation);
        Assert.IsTrue(CiScope.Select([], Comparison(), true, []).NuGetNativePreparation);
    }

    [TestMethod]
    public void NativeHkImportsNameTheLocalConfigurationModules()
    {
        string root = Path.Combine(Path.GetTempPath(), "hk root");
        string json = $$$"""
            {"imports":{
              "{{{FileUri(root, "hk.pkl")}}}":[
                {"uri":"{{{FileUri(root, "src/private/lib/hk/Config.pkl")}}}"},
                {"uri":"{{{FileUri(root, "src/private/lib/hk/Steps.pkl")}}}"}],
              "{{{FileUri(root, "src/private/lib/hk/Config.pkl")}}}":[
                {"uri":"package://github.com/jdx/hk/releases/download/v2.6.0/hk@2.6.0#/x.pkl"}],
              "{{{FileUri(root, "src/private/lib/hk/Steps.pkl")}}}":[
                {"uri":"{{{FileUri(root, "src/private/lib/hk/steps/Typos.pkl")}}}"},
                {"uri":"pkl:json"}],
              "{{{FileUri(root, "src/private/lib/hk/steps/Typos.pkl")}}}":[]},
             "resolvedImports":{
              "{{{FileUri(root, "hk.pkl")}}}":"{{{FileUri(root, "hk.pkl")}}}"}}
            """;
        CollectionAssert.AreEqual(LocalHkModules, CiScope.HkModules(json, root));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("""{"imports":[],"resolvedImports":{}}""")]
    [DataRow("""{"imports":{},"resolvedImports":{}}""")]
    [DataRow("""{"imports":{"ROOT":{}},"resolvedImports":{}}""")]
    [DataRow("""{"imports":{"ROOT":[{}]},"resolvedImports":{}}""")]
    [DataRow("""{"imports":{"ROOT":[{"uri":"https://example.com/x.pkl"}]},"resolvedImports":{}}""")]
    [DataRow("""{"imports":{"ROOT":[{"uri":"OUTSIDE"}]},"resolvedImports":{}}""")]
    [DataRow("""{"imports":{"ROOT":[]},"resolvedImports":{"ROOT":1}}""")]
    [DataRow("not json")]
    public void UnreadableHkImportsFailInsteadOfDroppingResponsibility(string json)
    {
        string root = Path.Combine(Path.GetTempPath(), "hk root");
        json = json.Replace("ROOT", FileUri(root, "hk.pkl"), StringComparison.Ordinal)
            .Replace("OUTSIDE", FileUri(Path.GetTempPath(), "outside.pkl"),
                StringComparison.Ordinal);
        Exception error = Assert.Throws<Exception>(() => CiScope.HkModules(json, root));
        Assert.IsTrue(error is InvalidDataException or JsonException, error.ToString());
    }

    private static string FileUri(string root, string path) =>
        new Uri(Path.GetFullPath(Path.Combine(root, path))).AbsoluteUri;

    private static readonly string[] LocalHkModules = ["hk.pkl",
        "src/private/lib/hk/Config.pkl", "src/private/lib/hk/Steps.pkl",
        "src/private/lib/hk/steps/Typos.pkl"];

    private static readonly string[] Hk = ["hk.pkl", "src/private/lib/hk/Config.pkl",
        "src/private/lib/hk/steps/Typos.pkl"];

    private static readonly RecordTaskOwner[] Tasks = [
        new("eng/scripts/check_repository_records.py",
            "record-control:repository-record-validation"),
        new("tests/eng/test_repository_records.py", "record-regression:records:test")];

    private static GitRevision Revision(params string[] paths) => new(Basis,
        [.. new[] { ".github/workflows/ci.yml", "hk.pkl", "mise.toml", "pyproject.toml",
                "tests/eng/test_python_runtime.py", "Gemfile", "src/lib/gem/gem.gemspec",
                "dirs.proj", "apm.yml", ControlTestInputs.Project,
                "src/private/lib/hk/Config.pkl", "src/private/lib/hk/steps/Typos.pkl",
                "src/private/lib/scholarly-publication/pyproject.toml",
                CiScope.FamilyCatalog }
            .Concat(paths).Distinct(StringComparer.Ordinal)
            .Select(path => new GitEntry(path, "100644", "blob"))]);

    private static GitComparison Comparison(params string[] paths) =>
        new(Revision(paths), Revision(paths), paths);

    private static string Root()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "global.json")))
            directory = Path.GetDirectoryName(directory) ??
                throw new InvalidDataException("Missing source root.");
        return directory;
    }
}
