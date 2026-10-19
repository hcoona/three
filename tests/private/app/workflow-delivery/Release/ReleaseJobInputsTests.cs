using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Release;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class ReleaseJobInputsTests
{
    [TestMethod]
    [DataRow("ruby", "github-packages", "buddy", "github-packages-ruby")]
    [DataRow("ruby", "rubygems", "official", "rubygems")]
    [DataRow("python", "testpypi", "buddy", "testpypi")]
    [DataRow("python", "pypi", "official", "pypi")]
    [DataRow("npm", "github-packages-npm", "buddy", "github-packages-npm")]
    [DataRow("npm", "npmjs", "official", "npmjs")]
    [DataRow("nuget", "github-packages-nuget", "buddy", "github-packages-nuget")]
    public void RequestRetainsNativeSubjectAndTypedTools(string ecosystem, string destination,
        string channel, string expectedDestination)
    {
        using var fixture = new Caller(ecosystem, destination);
        fixture.Run("request");
        ReleaseSubject subject = fixture.Subject();
        Assert.AreEqual(fixture.Env["GITHUB_SHA"], subject.Revision);
        Assert.AreEqual("refs/heads/feature", subject.SelectedRef);
        Assert.AreEqual(channel, subject.Channel);
        Assert.AreEqual(expectedDestination, subject.Destination);
        Assert.IsTrue(subject.DryRun);
        Assert.IsFalse(File.Exists(fixture.At("control/producer.json")));
        Assert.IsFalse(File.Exists(fixture.At("prepared/producer.json")));
        using JsonDocument request = JsonDocument.Parse(File.ReadAllText(fixture.At(
            "prepared/request.json")));
        Assert.AreEqual(fixture.Env["GITHUB_WORKSPACE"], request.RootElement.GetProperty(
            "checkout").GetString());
        Assert.AreEqual(fixture.At("preparation-scratch"), request.RootElement.GetProperty(
            "scratch").GetString());
        if (ecosystem == "ruby")
            Assert.AreEqual("selected-ruby", request.RootElement.GetProperty(
                "tools").GetProperty("ruby").GetString());
        if (ecosystem is "ruby" or "python")
            Assert.AreEqual(fixture.Env["MISE_DATA_DIR"], request.RootElement.GetProperty(
                "miseDataDirectory").GetString());
    }

    [TestMethod]
    [DataRow(RubyReleaseUnits.Smoke, "github-packages",
        "tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-ruby/")]
    [DataRow(RubyReleaseUnits.Smoke, "rubygems",
        "tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-ruby/")]
    [DataRow(RubyReleaseUnits.Latexmath, "github-packages",
        "src/public/lib/asciidoctor-latexmath/")]
    public void RubyRequestSelectsTheDispatchedUnit(string unit, string destination,
        string directory)
    {
        using var fixture = new Caller("ruby", destination);
        fixture.Env["UNIT"] = unit;
        fixture.Run("request");
        ReleaseSubject subject = fixture.Subject();
        Assert.AreEqual(unit, subject.Unit);
        Assert.AreEqual(directory + "workflow-delivery.release-unit.yml", subject.Declaration);
        Assert.IsTrue(File.Exists(Path.Combine(RepositoryToolSelection.SourceRoot,
            subject.Declaration)), subject.Declaration);
    }

    [TestMethod]
    [DataRow(RubyReleaseUnits.Latexmath, "rubygems")]
    [DataRow("another-gem", "github-packages")]
    [DataRow("", "github-packages")]
    public void UnsupportedRubyUnitStopsBeforePreparation(string unit, string destination)
    {
        using var fixture = new Caller("ruby", destination);
        fixture.Env["UNIT"] = unit;
        foreach (string[] entry in new[] { new[] { "request" }, ["receive", "publication"],
            ["state"] })
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run(entry));
        Assert.IsFalse(Directory.Exists(fixture.At("prepared")));
        Assert.IsFalse(Directory.Exists(fixture.At("preparation-scratch")));
    }

    [TestMethod]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void ReceivedPreparationBindsRequestSubjectOnce(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        fixture.Run("request");
        ReleaseSubject subject = fixture.Subject();
        foreach (ReleaseSubject wrong in new[] { subject with { Revision = new string('b', 40) },
            subject with { DryRun = false }, subject with { SelectedRef = "refs/heads/other" } })
        {
            fixture.Preparation(wrong);
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("receive",
                "observation"));
            Assert.IsFalse(Directory.Exists(fixture.At("observation")));
        }
        fixture.Preparation(subject);
        if (ecosystem == "npm")
        {
            fixture.Run("receive", "observation");
            Assert.IsTrue(Directory.Exists(fixture.At("observation")));
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("receive",
                "observation"));
        }
        else
        {
            // The same receive also applies the ecosystem's complete preparation check,
            // which this generic preparation does not satisfy.
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("receive",
                "observation"));
            Assert.IsFalse(Directory.Exists(fixture.At("observation")));
        }
    }

    [TestMethod]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void ReceivedUnqualifiedPreparationStopsBeforePhaseOutput(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        fixture.Run("request");
        fixture.Preparation(fixture.Subject(), status: ReleaseCheckStatus.Failed);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("receive", "publication"));
        Assert.IsFalse(Directory.Exists(fixture.At("publication")));
    }

    [TestMethod]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void OriginalTransferRetainsBytesAndRejectsEscapingInputs(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        fixture.Run("request");
        ReleaseSubject subject = fixture.Subject();
        fixture.Preparation(subject);
        Directory.CreateDirectory(fixture.At("preparation-scratch/build"));
        byte[] original = [0, 1, 255, 13, 10];
        File.WriteAllBytes(fixture.At("preparation-scratch/build/original"), original);
        fixture.Run("originals");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.At(
            "prepared/build/original")));
        Assert.ThrowsExactly<IOException>(() => fixture.Run("originals"));
        foreach (string path in new[] { "../escape", "/absolute", "build/../../escape",
            "C:/absolute" })
        {
            fixture.Preparation(subject, path);
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("originals"));
        }

    }

    [TestMethod]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void InvalidOperationsAndLiveRerunsCreateNoState(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("receive", "../escape"));
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("unsupported"));
        // A live rerun stops at every job entry, including jobs a partial rerun repeats.
        fixture.Env["DRY_RUN"] = "false";
        foreach (string[] entry in new[] { new[] { "request" }, ["receive", "publication"],
            ["state"] })
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run(entry));
        Assert.IsFalse(Directory.Exists(fixture.At("prepared")));
        Assert.IsFalse(Directory.Exists(fixture.At("publication")));
        fixture.Env["DRY_RUN"] = "true";
        fixture.Env["GITHUB_REPOSITORY"] = "other/repo";
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("request"));
        Assert.IsFalse(Directory.Exists(fixture.At("preparation-scratch")));
    }

    [TestMethod]
    [DataRow("source-config", "source-tools.json")]
    [DataRow("publisher-config", "publisher-tools.json")]
    public void ConcreteConfigsUseSelectedToolsAndFreshScratch(string operation, string file)
    {
        using var fixture = new Caller("ruby");
        fixture.Run(operation, "selected-scratch");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(fixture.At(file)));
        Assert.AreEqual("selected-ruby", config.RootElement.GetProperty("ruby").GetString());
        Assert.ThrowsExactly<InvalidDataException>(() =>
            fixture.Run(operation, "selected-scratch"));
    }

    [TestMethod]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void WriteNeededUsesActualObservationAndRejectsSubjectSubstitution(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        fixture.Run("request");
        ReleaseSubject subject = fixture.Subject();
        Directory.CreateDirectory(fixture.At("observation"));
        foreach (ReleaseFileState state in new[] { ReleaseFileState.Absent,
            ReleaseFileState.Exact })
        {
            var observation = new ReleaseObservation(subject, [new("output", "fixture", "1.0",
                state, null, null)],
                ["check"], [new("check", ReleaseCheckStatus.Passed)]);
            string json = ecosystem switch
            {
                "ruby" => JsonSerializer.Serialize(new RubyObservationResult(observation,
                    new(RubySourceState.Absent, null, null, [])),
                        TransferJson.Default.RubyObservationResult),
                "python" => JsonSerializer.Serialize(new PythonIndexObservationResult(
                    observation, []), TransferJson.Default.PythonIndexObservationResult),
                "npm" => JsonSerializer.Serialize(new GithubNpmObservationResult(observation,
                    []), TransferJson.Default.GithubNpmObservationResult),
                "nuget" => JsonSerializer.Serialize(new GithubNuGetObservationResult(observation,
                    null, []), TransferJson.Default.GithubNuGetObservationResult),
                _ => throw new InvalidOperationException(),
            };
            File.WriteAllText(fixture.At("observation/observation.json"), json);
            fixture.Run("state");
            Assert.EndsWith("write-needed=" + (
                state == ReleaseFileState.Absent ? "true" : "false") + "\n",
                File.ReadAllText(fixture.At("output")));
            fixture.Env["GITHUB_SHA"] = new string('b', 40);
            string original = File.ReadAllText(fixture.At("output"));
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("state"));
            Assert.AreEqual(original, File.ReadAllText(fixture.At("output")));
            fixture.Env["GITHUB_SHA"] = subject.Revision;
        }
    }

    [TestMethod]
    public void OfficialSimulationCreatesOnlyConsumedRequest()
    {
        using var fixture = new Caller("npm", "npmjs");
        fixture.Run("simulation-request");
        NpmPreparationRequest request = JsonSerializer.Deserialize(File.ReadAllText(
            fixture.At("prepared/request.json")), TransferJson.Default.NpmPreparationRequest)!;
        Assert.AreEqual("official", request.Subject.Channel);
        Assert.AreEqual("npmjs", request.Subject.Destination);
        Assert.IsTrue(request.Subject.DryRun);
    }

    private sealed class Caller : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "release-caller-" +
            Guid.NewGuid().ToString("N"));
        private readonly string ecosystem;
        internal Dictionary<string, string> Env { get; }
        internal Caller(string ecosystem, string? destination = null)
        {
            this.ecosystem = ecosystem;
            Directory.CreateDirectory(At("control"));
            Env = new()
            {
                ["RUNNER_TEMP"] = root, ["DESTINATION"] = destination ?? (
                    ecosystem switch { "ruby" => "github-packages", "python" => "testpypi",
                        "npm" => "github-packages-npm", "nuget" => "github-packages-nuget",
                        _ => throw new InvalidOperationException() }),
                ["GITHUB_SHA"] = new string('a', 40), ["GITHUB_REF"] = "refs/heads/feature",
                ["GITHUB_RUN_ID"] = "123", ["GITHUB_RUN_ATTEMPT"] = "2", ["DRY_RUN"] = "true",
                ["GITHUB_REPOSITORY"] = "hcoona/three", ["GITHUB_WORKSPACE"] = root,
                ["RUBY_EXECUTABLE"] = "selected-ruby", ["GEM_EXECUTABLE"] = "selected-gem",
                ["DOTNET_EXECUTABLE"] = "selected-dotnet", ["UV_EXECUTABLE"] = "selected-uv",
                ["PYTHON_EXECUTABLE"] = "selected-python", ["MISE_DATA_DIR"] = At("mise"),
                ["GITHUB_OUTPUT"] = At("output"), ["UNIT"] = RubyReleaseUnits.Smoke,
            };
        }
        internal string At(string path) => Path.Combine(root, path);
        internal void Run(params string[] args) => Assert.AreEqual(0, ReleaseJobInputs.Run(
            ["release", "job-inputs", ecosystem, .. args], name => Env.GetValueOrDefault(name)));
        internal ReleaseSubject Subject()
        {
            using JsonDocument request = JsonDocument.Parse(File.ReadAllText(At(
                "prepared/request.json")));
            return request.RootElement.GetProperty("subject").Deserialize(
                TransferJson.Default.ReleaseSubject)!;
        }
        internal void Preparation(ReleaseSubject subject, string path = "build/original",
            ReleaseCheckStatus status = ReleaseCheckStatus.Passed)
        {
            var prepared = new PreparedRelease(subject, [new("build", "output", "package",
                "fixture", "1.0",
                path, 5, new string('a', 64))], ["check"], [new("check", status)]);
            string json = ecosystem switch
            {
                "ruby" => JsonSerializer.Serialize(new RubyPreparationResult(prepared,
                    new(), [], []), TransferJson.Default.RubyPreparationResult),
                "python" => JsonSerializer.Serialize(new PythonPreparationResult(prepared,
                    new(), [], []), TransferJson.Default.PythonPreparationResult),
                "npm" => JsonSerializer.Serialize(new NpmPreparationResult(prepared, [], []),
                    TransferJson.Default.NpmPreparationResult),
                "nuget" => JsonSerializer.Serialize(new NuGetPreparationResult(prepared,
                    new(), [], []), TransferJson.Default.NuGetPreparationResult),
                _ => throw new InvalidOperationException(),
            };
            File.WriteAllText(At("prepared/preparation.json"), json);
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
