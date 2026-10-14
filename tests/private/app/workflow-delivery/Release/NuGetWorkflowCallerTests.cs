using System.Text;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using YamlDotNet.RepresentationModel;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetWorkflowCallerTests(TestContext context)
{
    [TestMethod]
    [DataRow("Absent", "true")]
    [DataRow("Exact", "false")]
    public async Task ActualStateStepConsumesSerializedObservation(string state, string needed)
    {
        using var fixture = new NuGetReleaseTestFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "observation"));
        fixture.Write("observation/observation.json", fixture.Observe(
            Enum.Parse<ReleaseFileState>(state)),
            TransferJson.Default.GithubNuGetObservationResult);
        var environment = Environment(fixture);
        environment["GITHUB_OUTPUT"] = Path.Combine(fixture.Root, "output");

        NativeCommandResult result = await Step(fixture, "observe",
            "Describe native state and dry-run writes", environment);

        Assert.IsTrue(result.Succeeded, result.Stderr);
        Assert.AreEqual("write-needed=" + needed + "\n", File.ReadAllText(
            environment["GITHUB_OUTPUT"]!));
    }

    [TestMethod]
    [DataRow("Passed", 0, "Satisfied")]
    [DataRow("Failed", 1, "Unsatisfied")]
    public async Task ActualVerificationStepRetainsTypedConsumerOutcome(string status,
        int expectedExit, string expectedState)
    {
        using var fixture = new NuGetReleaseTestFixture();
        foreach (string phase in new[] { "prepared", "observation" })
            Directory.CreateDirectory(Path.Combine(fixture.Root, phase));
        fixture.Write("prepared/preparation.json", fixture.Preparation,
            TransferJson.Default.NuGetPreparationResult);
        fixture.Write("observation/observation.json", fixture.Observe(ReleaseFileState.Exact),
            TransferJson.Default.GithubNuGetObservationResult);
        fixture.Write("controlled-consumer.json", fixture.Consumer(
            Enum.Parse<ReleaseCheckStatus>(status)),
            TransferJson.Default.GithubNuGetConsumerResult);
        var environment = Environment(fixture);
        environment["WRITE_NEEDED"] = "false";
        environment["CONTROLLED_CONSUMER_EXIT"] = expectedExit.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

        NativeCommandResult result = await Step(fixture, "verify-result",
            "Verify original remote bytes and native scoped consumer", environment);

        Assert.AreEqual(expectedExit, result.ExitCode, result.Stderr);
        GithubNuGetCompletionResult outcome = JsonSerializer.Deserialize(File.ReadAllText(
            Path.Combine(fixture.Root, "verification/outcome.json")),
            TransferJson.Default.GithubNuGetCompletionResult)!;
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), outcome.Outcome.State);
        Assert.AreEqual(fixture.Prepared.Subject, outcome.Outcome.Subject);
        Assert.IsFalse(outcome.MutationAttempted);
    }

    private static Dictionary<string, string?> Environment(NuGetReleaseTestFixture fixture) => new()
    {
        ["RUNNER_TEMP"] = fixture.Root,
        ["DESTINATION"] = "github-packages-nuget",
        ["DRY_RUN"] = "false",
        ["GITHUB_SHA"] = fixture.Prepared.Subject.Revision,
        ["GITHUB_REF"] = fixture.Prepared.Subject.SelectedRef,
        ["GITHUB_RUN_ID"] = "123",
        ["GITHUB_RUN_ATTEMPT"] = "1",
        ["WORKFLOW_APP"] = typeof(Program).Assembly.Location,
        ["WORKFLOW_DOTNET"] = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? "dotnet",
    };

    private Task<NativeCommandResult> Step(NuGetReleaseTestFixture fixture, string job,
        string name, Dictionary<string, string?> environment)
    {
        // Only the native destination/consumer operations are controlled. The actual workflow
        // shell and typed application state/phase/completion commands run unchanged.
        const string shim = """
            $ErrorActionPreference = 'Stop'
            function mise {
              $operation = @($args | Select-Object -Skip 4)
              switch ($operation[1]) {
                'observe-github-nuget' {
                  Get-Content -LiteralPath "$env:RUNNER_TEMP/observation/observation.json" -Raw
                  $global:LASTEXITCODE = 0
                }
                'consume-github-nuget' {
                  Get-Content -LiteralPath "$env:RUNNER_TEMP/controlled-consumer.json" -Raw
                  $global:LASTEXITCODE = [int]$env:CONTROLLED_CONSUMER_EXIT
                }
                default {
                  & $env:WORKFLOW_DOTNET $env:WORKFLOW_APP @operation
                  $global:LASTEXITCODE = $LASTEXITCODE
                }
              }
            }
            """;
        var yaml = new YamlStream();
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,
            ".github/workflows/workflow-delivery-v3-nuget-live-attempt.yml"))) root = root.Parent;
        Assert.IsNotNull(root);
        using var reader = File.OpenText(Path.Combine(root.FullName,
            ".github/workflows/workflow-delivery-v3-nuget-live-attempt.yml"));
        yaml.Load(reader);
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)document.Children["jobs"];
        var selected = (YamlMappingNode)jobs.Children[job];
        var steps = (YamlSequenceNode)selected.Children["steps"];
        YamlMappingNode step = steps.Children.Cast<YamlMappingNode>().Single(value =>
            value.Children.TryGetValue("name", out YamlNode? label) && label.ToString() == name);
        string script = shim + "\n" + step.Children["run"];
        return NativeProcess.ExecuteAsync(new("pwsh", fixture.Root,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], 30, environment),
            context.CancellationToken);
    }
}
