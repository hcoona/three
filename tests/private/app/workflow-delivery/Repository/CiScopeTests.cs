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
    [DataRow("src/private/app/workflow-delivery/README.md", false)]
    [DataRow("src/private/app/workflow-delivery/docs/requirements.md", false)]
    [DataRow("src/private/app/workflow-delivery/docs/integration/dotnet.md", false)]
    [DataRow("src/private/app/workflow-delivery/Native/Ruby/README.md", true)]
    [DataRow("src/private/app/workflow-delivery/Native/x.cs", true)]
    [DataRow("tests/private/app/workflow-delivery/docs/notes.md", true)]
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

    [TestMethod]
    [DataRow("renovate.json")]
    [DataRow(".github/renovate/global.cjs")]
    [DataRow(".github/workflows/renovate.yml")]
    public void RenovateConfigurationHasItsNativeOwner(string path)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        Assert.IsTrue(row.Reasons.Any(reason => reason.Owner == "renovate" &&
            reason.Target == ".github/workflows/renovate.yml" &&
            reason.Rule == "native-renovate-configuration"),
            string.Join(", ", row.Reasons.Select(reason => reason.Owner + ":" + reason.Rule)));
    }

    [TestMethod]
    [DataRow("eng/scripts/update.ps1", HkSource)]
    [DataRow("eng/linter/check.ps1", HkSource)]
    [DataRow("global.pkl", RenovateSource)]
    [DataRow("eng/scripts/update.ps1", RenovateSource)]
    public void LiteralToolingReferencesReceiveConformanceResponsibility(string path,
        string source)
    {
        var tooling = source == HkSource
            ? new ToolingConfiguration(ToolingHk, null)
            : new ToolingConfiguration(null, ToolingRenovate);
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk, tooling));
        ScopeResponsibility reason = Assert.ContainsSingle(row.Reasons,
            string.Join(", ", row.Reasons.Select(item => item.Owner + ":" + item.Rule)));
        Assert.AreEqual("conformance", reason.Owner);
        Assert.AreEqual(".github/workflows/ci.yml#conformance", reason.Target);
        Assert.AreEqual("literal-tooling-reference", reason.Rule);
        CollectionAssert.Contains(reason.Sources, source);
    }

    [TestMethod]
    [DataRow("PklProject")]
    [DataRow(".editorconfig")]
    [DataRow("eng/scripts/update")]
    [DataRow("scripts/update.ps1")]
    [DataRow("docs/unreferenced.md")]
    public void PartialOrAbsentTokensReceiveNoToolingResponsibility(string path)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk, new ToolingConfiguration(ToolingHk, ToolingRenovate)));
        Assert.IsFalse(row.Reasons.Any(reason => reason.Rule == "literal-tooling-reference"),
            string.Join(", ", row.Reasons.Select(reason => reason.Owner + ":" + reason.Rule)));
    }

    [TestMethod]
    // The committed tool-version guard (test_python_runtime.py) reads every root *.yml.
    [DataRow(".github/workflows/lint.yaml", false)]
    [DataRow(".github/workflows/codeql.yml", true)]
    [DataRow(".github/workflows/workflow-delivery-v3-official-simulate.yml", true)]
    public void RootWorkflowsReceiveHkWorkflowLintResponsibility(string path, bool python)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        string rows = string.Join(", ", row.Reasons.Select(item => item.Owner + ":" + item.Rule));
        ScopeResponsibility lint = Assert.ContainsSingle(row.Reasons.Where(reason =>
            reason.Rule == "native-hk-workflow-lint"), rows);
        Assert.AreEqual("conformance", lint.Owner);
        Assert.AreEqual(".github/workflows/ci.yml#conformance", lint.Target);
        CollectionAssert.AreEquivalent(WorkflowLintSources, lint.Sources);
        Assert.AreEqual(python, row.Reasons.Any(reason => reason.Owner == "python"), rows);
        Assert.AreEqual(python ? 2 : 1, row.Reasons.Length, rows);
        CiScopeApplicability jobs = CiScope.Select([path], Comparison(path), false, []);
        Assert.IsFalse(jobs.Mise || jobs.AzureAuth || jobs.Scholarly ||
            jobs.NuGetNativePreparation);
    }

    [TestMethod]
    [DataRow(".github/workflows/nested/caller.yml")]
    [DataRow(".github/workflows/README.md")]
    public void OnlyRootWorkflowYamlReceivesHkWorkflowLintResponsibility(string path)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        Assert.IsFalse(row.Reasons.Any(reason => reason.Rule == "native-hk-workflow-lint"));
    }

    [TestMethod]
    [DataRow("eng/scripts/test-python.sh", true)]
    [DataRow("tools/build.bash", false)]
    [DataRow("x/.agents/y.sh", false)]
    public void LintedShellScriptsReceiveHkShellLintResponsibility(string path, bool python)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        string rows = string.Join(", ", row.Reasons.Select(item => item.Owner + ":" + item.Rule));
        ScopeResponsibility lint = Assert.ContainsSingle(row.Reasons.Where(reason =>
            reason.Rule == "native-hk-shell-lint"), rows);
        Assert.AreEqual("conformance", lint.Owner);
        Assert.AreEqual(".github/workflows/ci.yml#conformance", lint.Target);
        CollectionAssert.AreEquivalent(WorkflowLintSources, lint.Sources);
        Assert.AreEqual(python, row.Reasons.Any(reason => reason.Owner == "python"), rows);
        // The shell rule adds no retained job; only the existing Python input selects AzureAuth.
        CiScopeApplicability jobs = CiScope.Select([path], Comparison(path), false, []);
        Assert.AreEqual(python, jobs.AzureAuth, rows);
        Assert.IsFalse(jobs.Mise || jobs.Scholarly || jobs.NuGetNativePreparation);
    }

    [TestMethod]
    [DataRow(".github/x.sh")]
    [DataRow(".agents/x.sh")]
    [DataRow("a/generated/x.sh")]
    [DataRow(".specify/x.sh")]
    [DataRow("eng/scripts/notes.shell")]
    public void HkExcludedShellPathsReceiveNoShellLintResponsibility(string path)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        Assert.IsFalse(row.Reasons.Any(reason => reason.Rule == "native-hk-shell-lint"));
    }

    [TestMethod]
    public void DeletedScriptResolvesAtTheBasisAndConfirmsAbsenceAtTheCandidate()
    {
        const string path = "eng/scripts/publish_node_gpr_idempotent.sh";
        ScopeCoordinate basis = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        ScopeResponsibility reason = Assert.ContainsSingle(basis.Reasons);
        Assert.AreEqual("native-hk-shell-lint", reason.Rule);
        Assert.IsFalse(Assert.ContainsSingle(CiScope.Owners(Revision(), [path], [], [], Tasks,
            Hk)).Present);
        var bare = new GitRevision(Basis, [new(path, "100644", "blob")]);
        Assert.IsEmpty(Assert.ContainsSingle(CiScope.Owners(bare, [path], [], [], Tasks, Hk))
            .Reasons);
    }

    [TestMethod]
    [DataRow("eng/scripts/find_node_project_path.py")]
    [DataRow("x/.agents/tool.py")]
    public void StandaloneScriptsReceiveHkPythonLintInsteadOfPython(string path)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk, null, [path]));
        string rows = string.Join(", ", row.Reasons.Select(item => item.Owner + ":" + item.Rule));
        ScopeResponsibility reason = Assert.ContainsSingle(row.Reasons, rows);
        Assert.AreEqual("conformance", reason.Owner);
        Assert.AreEqual(".github/workflows/ci.yml#conformance", reason.Target);
        Assert.AreEqual("native-hk-python-lint", reason.Rule);
        CollectionAssert.AreEquivalent(WorkflowLintSources, reason.Sources);
        CiScopeApplicability jobs = CiScope.Select([path], Comparison(path), false, []);
        Assert.IsFalse(jobs.Mise || jobs.AzureAuth || jobs.Scholarly ||
            jobs.NuGetNativePreparation);
    }

    [TestMethod]
    public void DeletedStandaloneScriptResolvesAtTheBasis()
    {
        const string path = "eng/scripts/find_project_path.py";
        ScopeCoordinate basis = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk, null, [path]));
        Assert.AreEqual("native-hk-python-lint", Assert.ContainsSingle(basis.Reasons).Rule);
        ScopeCoordinate candidate = Assert.ContainsSingle(CiScope.Owners(Revision(), [path], [],
            [], Tasks, Hk));
        Assert.IsFalse(candidate.Present);
        Assert.IsEmpty(candidate.Reasons);
        var bare = new GitRevision(Basis, [new(path, "100644", "blob")]);
        // Without committed hk.pkl and ci.yml the endpoint has no HK lint owner.
        Assert.IsFalse(Assert.ContainsSingle(CiScope.Owners(bare, [path], [], [], Tasks, Hk,
            null, [path])).Reasons.Any(reason => reason.Rule == "native-hk-python-lint"));
    }

    [TestMethod]
    [DataRow("src/conftest.py")]
    [DataRow("tests/eng/helper.py")]
    [DataRow("tests/tools/script.py")]
    [DataRow("eng/scripts/sync_python_version.py")]
    [DataRow("src/private/lib/scholarly-publication/tool.py")]
    [DataRow(".agents/tool.py")]
    [DataRow(".github/scripts/tool.py")]
    [DataRow("a/generated/tool.py")]
    [DataRow("src/private/lib/scan-restoration/skills/x/tool.py")]
    public void NonStandaloneOrExcludedScriptsKeepPythonResponsibility(string path)
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk, null, [path]));
        string rows = string.Join(", ", row.Reasons.Select(item => item.Owner + ":" + item.Rule));
        Assert.IsTrue(row.Reasons.Any(reason => reason.Owner == "python"), rows);
        Assert.IsFalse(row.Reasons.Any(reason => reason.Rule == "native-hk-python-lint"), rows);
    }

    [TestMethod]
    public void ScriptsWithoutInlineMetadataKeepPythonResponsibility()
    {
        const string path = "eng/scripts/hk_exec.py";
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        Assert.AreEqual("python", Assert.ContainsSingle(row.Reasons).Owner);
    }

    [TestMethod]
    public void RecordTaskScriptsKeepRecordOwnerWithoutPythonLint()
    {
        const string path = "eng/scripts/check_repository_records.py";
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk, null, [path]));
        Assert.AreEqual("record-system", Assert.ContainsSingle(row.Reasons).Owner);
    }

    [TestMethod]
    [DataRow("# /// script\n# dependencies = []\n# ///\n", true)]
    [DataRow("# /// script\r\n# ///\r\n", true)]
    [DataRow("# /// script\n# dependencies = []\n", false)]
    [DataRow("#!/usr/bin/env python\n# /// script\n# ///\n", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void InlineScriptMetadataRequiresTheOpeningFirstLineAndAClosingLine(string? text,
        bool expected) => Assert.AreEqual(expected, CiScope.InlineScriptMetadata(text));

    [TestMethod]
    public void CurrentStandaloneScriptCarriesInlineMetadata() =>
        Assert.IsTrue(CiScope.InlineScriptMetadata(File.ReadAllText(Path.Combine(Root(),
            "eng/scripts/find_node_project_path.py"))));

    [TestMethod]
    public void HkWorkflowLintRequiresCommittedHkAndCiWorkflow()
    {
        const string path = ".github/workflows/lint.yaml";
        var revision = new GitRevision(Basis, [new(path, "100644", "blob")]);
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(revision, [path], [], [],
            Tasks, Hk));
        Assert.IsEmpty(row.Reasons);
    }

    [TestMethod]
    public void DeletedWorkflowResolvesAtTheBasisAndConfirmsAbsenceAtTheCandidate()
    {
        const string path = ".github/workflows/official.yml";
        ScopeCoordinate basis = Assert.ContainsSingle(CiScope.Owners(Revision(path), [path], [],
            [], Tasks, Hk));
        Assert.IsTrue(basis.Present);
        Assert.IsTrue(basis.Reasons.Any(reason => reason.Owner == "conformance" &&
            reason.Rule == "native-hk-workflow-lint"));
        ScopeCoordinate candidate = Assert.ContainsSingle(CiScope.Owners(Revision(), [path],
            [], [], Tasks, Hk));
        Assert.IsFalse(candidate.Present);
        Assert.IsEmpty(candidate.Reasons);
    }

    [TestMethod]
    public void DeletedToolingReferencesKeepConfirmedAbsence()
    {
        ScopeCoordinate row = Assert.ContainsSingle(CiScope.Owners(Revision(), ["global.pkl"],
            [], [], Tasks, Hk, new ToolingConfiguration(ToolingHk, ToolingRenovate)));
        Assert.IsFalse(row.Present);
        Assert.IsEmpty(row.Reasons);
    }

    [TestMethod]
    public void CurrentToolingReferencesResolveTheirOwnersInCommittedConfiguration()
    {
        string root = Root();
        var tooling = new ToolingConfiguration(
            File.ReadAllText(Path.Combine(root, CiScope.HkConfiguration)),
            File.ReadAllText(Path.Combine(root, CiScope.RenovateConfiguration)));
        foreach (string path in new[] { "eng/scripts/Update-RenovateGlobalJsonArtifacts.ps1",
            "eng/scripts/sync_python_version.py" })
            Assert.IsTrue(CiScope.ReferencesPath(tooling.Renovate!, path), path);
        Assert.IsFalse(CiScope.ReferencesPath(tooling.Hk!, "PklProject"));
        Assert.IsFalse(CiScope.ReferencesPath(tooling.Hk!, ".editorconfig"));
    }

    private static readonly string[] WorkflowLintSources = [".github/workflows/ci.yml",
        "hk.pkl"];
    private const string HkSource = "hk.pkl";
    private const string RenovateSource = "renovate.json";
    private const string ToolingHk = """
        local steps = new Mapping<String, Step> {
          ["guard"] { check = "pwsh -File eng/linter/check.ps1 -Source global.pkl" }
          ["editorconfig"] = (Steps.editorconfig_check) { glob = List("PklProject.deps.json") }
          ["update"] { fix = "pwsh -File eng/scripts/update.ps1" }
        }
        """;
    private const string ToolingRenovate = """
        {"postUpgradeTasks": {"commands": ["pwsh -File eng/scripts/update.ps1"],
          "fileFilters": ["global.pkl", "global.json"]}}
        """;

    private static readonly RecordTaskOwner[] Tasks = [
        new("eng/scripts/check_repository_records.py",
            "record-control:repository-record-validation"),
        new("tests/eng/test_repository_records.py", "record-regression:records:test")];

    private static GitRevision Revision(params string[] paths) => new(Basis,
        [.. new[] { ".github/workflows/ci.yml", "hk.pkl", "mise.toml", "pyproject.toml",
                "tests/eng/test_python_runtime.py", "Gemfile", "src/lib/gem/gem.gemspec",
                "dirs.proj", "apm.yml", ControlTestInputs.Project,
                "src/private/lib/hk/Config.pkl", "src/private/lib/hk/steps/Typos.pkl",
                CiScope.RenovateConfiguration,
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
