using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Tests.CI;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetPreparationTests(TestContext context)
{
    [TestMethod]
    [DataRow("restore", "Exited", "Failed")]
    [DataRow("restore", "Cancelled", "Cancelled")]
    [DataRow("restore", "TimedOut", "TimedOut")]
    [DataRow("Build", "Exited", "Failed")]
    [DataRow("Build", "Cancelled", "Cancelled")]
    [DataRow("Build", "TimedOut", "TimedOut")]
    [DataRow("Pack,_GetOutputItemsFromPack", "Exited", "Failed")]
    [DataRow("Pack,_GetOutputItemsFromPack", "Cancelled", "Cancelled")]
    [DataRow("Pack,_GetOutputItemsFromPack", "TimedOut", "TimedOut")]
    public async Task NativeFailureStopsPreparationAndRetainsTermination(string phase,
        string terminationText, string expectedText)
    {
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationText);
        ReleaseCheckStatus expected = Enum.Parse<ReleaseCheckStatus>(expectedText);
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Override = command => command.Arguments.Contains(phase) ||
            command.Arguments.Contains("-target:" + phase) ?
                new(termination, termination == NativeTermination.Exited ? 1 : null,
                    "partial native output", "native diagnostic", 0.1, null) : null;
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        string check = phase == "restore" ? "native-input" : phase == "Build" ? "build" : "pack";
        Assert.AreEqual(expected, result.Prepared.Checks.Single(item => item.Id == check).Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
        ReleaseCommand failed = Assert.ContainsSingle(result.Commands.Where(item =>
            !item.Result.Succeeded));
        Assert.AreEqual(termination, failed.Result.Termination);
        Assert.AreEqual("partial native output", failed.Result.Stdout);
        Assert.IsTrue(result.Prepared.Checks.SkipWhile(item => item.Id != check).Skip(1)
            .All(item => item.Status == ReleaseCheckStatus.Skipped));
        Assert.DoesNotContain("consumer", result.Commands.Select(item => item.Check).ToArray());
    }

    [TestMethod]
    [DataRow("Build", "NuGetPackageVersion", "2.0.0")]
    [DataRow("Build", "AssemblyVersion", "2.0.0.0")]
    [DataRow("Pack,_GetOutputItemsFromPack", "AssemblyInformationalVersion", "changed")]
    [DataRow("GetBuildVersion", "RuntimeIdentifier", "linux-x64")]
    [DataRow("GetBuildVersion", "NETCoreSdkVersion", "10.0.402")]
    public async Task NativeProjectionMismatchCannotQualifyRelease(string target,
        string property, string value)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Modify = (phase, document) =>
        { if (phase == target) document["Properties"]![property] = value; };
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
        Assert.Contains(ReleaseCheckStatus.Failed, result.Prepared.Checks.Select(item =>
            item.Status).ToArray());
        Assert.DoesNotContain("consumer", result.Commands.Select(item => item.Check).ToArray());
    }

    [TestMethod]
    [DataRow("GetBuildVersion")]
    [DataRow("Build")]
    [DataRow("Pack,_GetOutputItemsFromPack")]
    public async Task SuccessfulCommandWithoutRequiredTargetResultCannotPass(string target)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Modify = (phase, document) =>
        { if (phase == target) document["TargetResults"]!.AsObject().Clear(); };
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.Contains("Missing or failed native package target", string.Join(";", result.Errors));
        Assert.IsTrue(result.Commands.All(item => item.Result.Succeeded));
    }

    [TestMethod]
    public async Task UnknownQualitySelectionIsUnavailableBeforeNativeBuild()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken,
            "dotnet/unknown-v1");
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.Contains("quality selection", string.Join(";", result.Errors));
    }

    [TestMethod]
    [DataRow("TargetFrameworks", "net10.0;net9.0")]
    [DataRow("RuntimeIdentifiers", "linux-x64;win-x64")]
    [DataRow("IsTestProject", "true")]
    [DataRow("IsTestingPlatformApplication", "true")]
    public async Task UnavailableNativeVariantOrRequiredTestCannotBeDropped(string property,
        string value)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Shape[property] = value;
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.Contains("variants or required tests", string.Join(";", result.Errors));
        Assert.DoesNotContain("build", result.Commands.Select(item => item.Check).ToArray());
    }

    [TestMethod]
    public async Task MissingOriginalOutputDoesNotBecomeEmptySuccessfulRelease()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks.Single(item => item.Id
            == "pack").Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.Contains("original native archive set", string.Join(";", result.Errors));
        Assert.IsFalse(result.Native.ContainsKey("MSBuildProjectFullPath"));
        Assert.IsFalse(result.Native.Values.Any(value => value.Contains(fixture.Repo.Directory,
            StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CancellationBeforePreparationProducesCompleteUnsatisfiedChecks()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        NuGetPreparationResult result = await fixture.RunAsync(cancellation.Token);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled, result.Prepared.Checks[0].Status);
        Assert.HasCount(6, result.Prepared.Checks);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Prepared.Files);
    }

    [TestMethod]
    public async Task EveryNativeCommandUsesSelectedVersionContextAndOwnedVariableOutput()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsNotEmpty(result.Commands);
        foreach (ReleaseCommand command in result.Commands)
        {
            Assert.IsFalse(command.Command.InheritEnvironment);
            Assert.IsFalse(command.Command.Environment!.ContainsKey("SOURCE_DATE_EPOCH"));
            Assert.AreEqual("true", command.Command.Environment!["GITHUB_ACTIONS"]);
            Assert.AreEqual(result.Prepared.Subject.SelectedRef,
                command.Command.Environment["GITHUB_REF"]);
            Assert.AreEqual(result.Prepared.Subject.Revision,
                command.Command.Environment["GITHUB_SHA"]);
            string output = command.Command.Environment["GITHUB_ENV"]!;
            Assert.IsTrue(File.Exists(output));
            Assert.IsFalse(output.StartsWith(fixture.Repo.Directory,
                StringComparison.Ordinal));
        }
    }

    private sealed class Fixture(GitFixture repo, string revision) : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        private readonly string scratch =
            Directory.CreateTempSubdirectory("nuget-release-").FullName;
        internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }
        internal Action<string, JsonObject>? Modify { get; set; }
        internal Dictionary<string, string> Shape { get; } = new()
        {
            ["TargetFramework"] = "net10.0",
            ["TargetFrameworks"] = "",
            ["RuntimeIdentifier"] = "",
            ["RuntimeIdentifiers"] = "",
            ["IsTestProject"] = "",
            ["IsTestingPlatformApplication"] = "",
        };
        internal static async Task<Fixture> CreateAsync(CancellationToken token,
            string preset = NuGetPreparation.Preset)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try
            {
                await repo.SetAsync(DotNetPackageFixture.Project, "<Project />");
                await repo.SetAsync(DotNetPackageFixture.Declaration,
                    DotNetPackageFixture.UnitText);
                await repo.SetAsync("product/workflow-delivery.quality.yml", $"""
                    schema: workflow-delivery/v3/quality-selection
                    ecosystems:
                      dotnet:
                        preset: {preset}
                    """);
                await repo.SetAsync("global.json", """
                    {"sdk":{"version":"10.0.401","rollForward":"disable"}}
                    """);
                string revision = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", revision);
                return new(repo, revision);
            }
            catch { repo.Dispose(); throw; }
        }
        internal Task<NuGetPreparationResult> RunAsync(CancellationToken token) =>
            NuGetPreparation.PrepareAsync(new(new(revision, "refs/heads/development", "product",
                DotNetPackageFixture.Declaration, "buddy", "github-packages-nuget", true),
                Repo.Directory, scratch), ExecuteAsync, token);
        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            if (Override?.Invoke(command) is { } result) return result;
            if (command.Arguments.Contains("--version"))
                return new(NativeTermination.Exited, 0, "10.0.401\n", "", 0.1, null);
            if (command.Arguments.Any(argument =>
                argument.StartsWith("-getProperty:TargetFramework,", StringComparison.Ordinal)))
                return new(NativeTermination.Exited, 0, JsonSerializer.Serialize(new
                {
                    Properties = Shape
                }), "", 0.1, null);
            string? response = command.Arguments.SingleOrDefault(argument => argument.StartsWith(
                "-getResultOutputFile:", StringComparison.Ordinal))?
                    ["-getResultOutputFile:".Length..];
            if (response is not null)
            {
                var values = DotNetPackageResults.NativeProperties.ToDictionary(name => name,
                    _ => "", StringComparer.Ordinal);
                values["MSBuildProjectFullPath"] = Path.Combine(Repo.Directory,
                    DotNetPackageFixture.Project);
                values["TargetFramework"] = "net10.0";
                values["NETCoreSdkVersion"] = "10.0.401";
                values["IsPackable"] = values["GenerateDocumentationFile"] = "true";
                values["DocumentationFile"] = "obj/Release/Product.xml";
                values["PackageReadmeFile"] = "README.md";
                values["NuGetPackageVersion"] = values["PackageVersion"] = "1.0.0";
                values["AssemblyVersion"] = values["AssemblyFileVersion"] = "1.0.0.0";
                values["AssemblyInformationalVersion"] = "1.0.0";
                values["PackageId"] = values["AssemblyName"] = "Hcoona.ReleaseSmoke.GithubPackages";
                string target = command.Arguments.Single(argument => argument.StartsWith("-target:",
                    StringComparison.Ordinal))["-target:".Length..];
                var targets = new JsonObject();
                foreach (string name in target.Split(','))
                    targets[name] = new JsonObject
                    {
                        ["Result"] = "Success",
                        ["Items"] = new
                        JsonArray()
                    };
                var document = new JsonObject
                {
                    ["Properties"] = JsonSerializer.SerializeToNode(values),
                    ["TargetResults"] = targets
                };
                Modify?.Invoke(target, document);
                await File.WriteAllTextAsync(response, document.ToJsonString(), token);
            }
            return new(NativeTermination.Exited, 0, "", "", 0.1, null);
        }
        public void Dispose() { Repo.Dispose(); Directory.Delete(scratch, true); }
    }
}
