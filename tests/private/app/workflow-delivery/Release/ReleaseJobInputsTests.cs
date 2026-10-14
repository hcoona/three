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
        ReleaseJobProducer producer = fixture.Producer("prepared");
        Assert.AreEqual(fixture.Env["GITHUB_SHA"], producer.Subject.Revision);
        Assert.AreEqual("refs/heads/feature", producer.Subject.SelectedRef);
        Assert.AreEqual(channel, producer.Subject.Channel);
        Assert.AreEqual(expectedDestination, producer.Subject.Destination);
        Assert.IsTrue(producer.Subject.DryRun);
        Assert.AreEqual("2", producer.Attempt);
        Assert.AreEqual(producer, fixture.Producer("control"));
        fixture.Run("phase", "observation");
        Assert.AreEqual(producer, fixture.Producer("observation"));
        fixture.Preparation(producer.Subject);
        fixture.Run("validate", "control", "prepared", "observation");
        using JsonDocument request = JsonDocument.Parse(File.ReadAllText(fixture.At(
            "prepared/request.json")));
        Assert.AreEqual(fixture.Env["GITHUB_WORKSPACE"], request.RootElement.GetProperty(
            "checkout").GetString());
        Assert.AreEqual(fixture.At("preparation-scratch"), request.RootElement.GetProperty(
            "scratch").GetString());
        if (ecosystem == "ruby")
            Assert.AreEqual("selected-ruby", request.RootElement.GetProperty(
                "tools").GetProperty("ruby").GetString());
        else if (ecosystem == "python")
            Assert.AreEqual(fixture.Env["MISE_DATA_DIR"], request.RootElement.GetProperty(
                "miseDataDirectory").GetString());
    }

    [TestMethod]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void ProducerAndPreparedSubjectSubstitutionFails(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        fixture.Run("request");
        ReleaseJobProducer original = fixture.Producer("prepared");
        fixture.Preparation(original.Subject);
        foreach (ReleaseJobProducer wrong in new[] { original with { Run = "other" },
            original with { Attempt = "1" },
                original with { Subject = original.Subject with { DryRun = false } } })
        {
            File.WriteAllText(fixture.At("control/producer.json"),
                JsonSerializer.Serialize(wrong, TransferJson.Default.ReleaseJobProducer));
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("validate", "control",
                "prepared"));
        }
        File.WriteAllText(fixture.At("control/producer.json"), JsonSerializer.Serialize(original,
            TransferJson.Default.ReleaseJobProducer));
        fixture.Preparation(original.Subject with { Revision = new string('b', 40) });
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("validate", "control",
            "prepared"));
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
        ReleaseSubject subject = fixture.Producer("prepared").Subject;
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
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("phase", "../escape"));
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("unsupported"));
        fixture.Env["DRY_RUN"] = "false";
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("request"));
        Assert.IsFalse(Directory.Exists(fixture.At("prepared")));
        fixture.Env["DRY_RUN"] = "true";
        fixture.Env["GITHUB_REPOSITORY"] = "other/repo";
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("request"));
        Assert.IsFalse(Directory.Exists(fixture.At("preparation-scratch")));
    }

    [TestMethod]
    [DataRow("ruby", "source-config", "source-tools.json")]
    [DataRow("ruby", "publisher-config", "publisher-tools.json")]
    [DataRow("ruby", "consumer-config", "consumer-tools.json")]
    [DataRow("python", "consumer-config", "python-wheel-tools.json")]
    public void ConcreteConfigsUseSelectedToolsAndFreshScratch(string ecosystem,
        string operation, string file)
    {
        using var fixture = new Caller(ecosystem);
        string scratch = ecosystem == "python" ? "python-wheel" : "selected-scratch";
        fixture.Run(operation, scratch);
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(fixture.At(file)));
        Assert.AreEqual(ecosystem == "ruby" ? "selected-ruby" : "selected-uv",
            config.RootElement.GetProperty(ecosystem == "ruby" ? "ruby" : "uv").GetString());
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run(operation, scratch));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("ruby")]
    [DataRow("python")]
    [DataRow("npm")]
    [DataRow("nuget")]
    public void LinkedOriginalsCannotEscapeOwnedTransfer(string ecosystem)
    {
        using var fixture = new Caller(ecosystem);
        fixture.Run("request");
        ReleaseSubject subject = fixture.Producer("prepared").Subject;
        fixture.Preparation(subject, "linked/original");
        Directory.CreateSymbolicLink(fixture.At("preparation-scratch/linked"), fixture.At(
            "control"));
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("originals"));
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
        fixture.Run("phase", "observation");
        ReleaseSubject subject = fixture.Producer("prepared").Subject;
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
        Assert.IsFalse(File.Exists(fixture.At("control/producer.json")));
        Assert.IsFalse(File.Exists(fixture.At("prepared/producer.json")));
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
                ["GITHUB_OUTPUT"] = At("output"),
            };
        }
        internal string At(string path) => Path.Combine(root, path);
        internal void Run(params string[] args) => Assert.AreEqual(0, ReleaseJobInputs.Run(
            ["release", "job-inputs", ecosystem, .. args], name => Env.GetValueOrDefault(name)));
        internal ReleaseJobProducer Producer(string phase) => JsonSerializer.Deserialize(
            File.ReadAllText(At(phase + "/producer.json")),
                TransferJson.Default.ReleaseJobProducer)!;
        internal void Preparation(ReleaseSubject subject, string path = "build/original")
        {
            var prepared = new PreparedRelease(subject, [new("build", "output", "package",
                "fixture", "1.0",
                path, 5, new string('a', 64))], ["check"], [new("check",
                    ReleaseCheckStatus.Passed)]);
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
