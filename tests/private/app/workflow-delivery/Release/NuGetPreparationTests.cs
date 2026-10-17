using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Tests.CI;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetPreparationTests(TestContext context)
{
    // Rows that vary only native answers read one committed repository; each row still
    // owns fresh scratch and runs the complete preparation.
    private static GitFixture? shared;
    private static string sharedRevision = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedRevision = await Fixture.CommitAsync(repo, NuGetPreparation.Preset);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task SharedRepositoryControlReachesOriginalArchiveCheck()
    {
        // The fake Pack produces no archive, so the furthest defect-free stage is the
        // original archive check; every earlier stage must qualify from the shared input.
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Passed, result.Prepared.Checks.Single(item =>
            item.Id == "native-input").Status);
        Assert.AreEqual(ReleaseCheckStatus.Passed, result.Prepared.Checks.Single(item =>
            item.Id == "build").Status);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks.Single(item =>
            item.Id == "pack").Status);
        Assert.Contains("original native archive set", string.Join(";", result.Errors));
        Assert.IsTrue(result.Commands.All(item => item.Result.Succeeded));
        // Only the SDK selection query runs without MSBuild; every MSBuild command, including
        // restore, owns its native process tree.
        Assert.AreEqual("--version", string.Join(' ', result.Commands[0].Command.Arguments));
        ReleaseCommand[] native = result.Commands.Skip(1).ToArray();
        Assert.AreEqual("restore msbuild msbuild msbuild msbuild",
            string.Join(' ', native.Select(item => item.Command.Arguments[0])));
        Assert.IsTrue(native.All(item => item.Command.Executable == "dotnet" &&
            item.Command.Arguments[1] == "--disable-build-servers"));
    }

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
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Override = command => command.Arguments.Contains(phase) ||
            command.Arguments.Contains("-target:" + phase) ?
                new(termination, termination == NativeTermination.Exited ? 1 : null,
                    "partial native output", "native diagnostic", 0.1, null) : null;
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        string check = phase == "restore" ? "native-input" : phase == "Build" ? "build" : "pack";
        Assert.AreEqual(expected, result.Prepared.Checks.Single(item => item.Id == check).Status);
        Assert.IsTrue(result.Prepared.Checks.TakeWhile(item => item.Id != check)
            .All(item => item.Status == ReleaseCheckStatus.Passed));
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
        ReleaseCommand failed = Assert.ContainsSingle(result.Commands.Where(item =>
            !item.Result.Succeeded));
        Assert.AreEqual(check, failed.Check);
        Assert.AreEqual(termination, failed.Result.Termination);
        Assert.AreEqual("partial native output", failed.Result.Stdout);
        Assert.IsTrue(result.Prepared.Checks.SkipWhile(item => item.Id != check).Skip(1)
            .All(item => item.Status == ReleaseCheckStatus.Skipped));
        Assert.DoesNotContain("consumer", result.Commands.Select(item => item.Check).ToArray());
    }

    [TestMethod]
    [DataRow("Build", "NuGetPackageVersion", "2.0.0", "build",
        "inconsistent native package projections")]
    [DataRow("Build", "AssemblyVersion", "2.0.0.0", "build", "recomputation differs")]
    [DataRow("Pack,_GetOutputItemsFromPack", "AssemblyInformationalVersion", "changed", "pack",
        "recomputation differs")]
    [DataRow("GetBuildVersion", "RuntimeIdentifier", "linux-x64", "native-input",
        "inconsistent native package projections")]
    [DataRow("GetBuildVersion", "NETCoreSdkVersion", "10.0.402", "native-input",
        "Unsupported native NuGet product or SDK")]
    public async Task NativeProjectionMismatchCannotQualifyRelease(string target,
        string property, string value, string check, string reason)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Modify = (phase, document) =>
        { if (phase == target) document["Properties"]![property] = value; };
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        // The defect-free shared control fails at pack for a missing archive, so each row
        // must fail at its own check for its own reason, after every earlier check passed.
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks.Single(item =>
            item.Id == check).Status);
        Assert.IsTrue(result.Prepared.Checks.TakeWhile(item => item.Id != check)
            .All(item => item.Status == ReleaseCheckStatus.Passed));
        string errors = string.Join(";", result.Errors);
        Assert.Contains(reason, errors);
        Assert.DoesNotContain("original native archive set", errors);
        Assert.IsTrue(result.Commands.All(item => item.Result.Succeeded));
        Assert.DoesNotContain("consumer", result.Commands.Select(item => item.Check).ToArray());
    }

    [TestMethod]
    [DataRow("GetBuildVersion")]
    [DataRow("Build")]
    [DataRow("Pack,_GetOutputItemsFromPack")]
    public async Task SuccessfulCommandWithoutRequiredTargetResultCannotPass(string target)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
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
        // Private repository: the unknown preset requires a different committed revision.
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
        using var fixture = Fixture.Shared(shared!, sharedRevision);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        NuGetPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        // Preparation does not branch on DryRun; the request subject is carried unchanged.
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
        Assert.IsTrue(result.Prepared.Subject.DryRun);
        Assert.IsNotEmpty(result.Commands);
        // Every MSBuild projection maps the checkout root so originals carry no local path.
        string pathMap = "-property:PathMap=" +
            ProjectCollection.Escape(fixture.Repo.Directory + "=/_/");
        NativeCommand[] queries = result.Commands.Select(command => command.Command)
            .Where(command => command.Arguments.Any(argument =>
                argument.StartsWith("-getResultOutputFile:", StringComparison.Ordinal)))
            .ToArray();
        Assert.IsNotEmpty(queries);
        foreach (NativeCommand query in queries)
            Assert.Contains(pathMap, query.Arguments);
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

    private sealed class Fixture(GitFixture repo, string revision,
        bool ownsRepository = true) : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        internal ReleaseSubject Subject { get; } = new(revision, "refs/heads/development",
            "product", DotNetPackageFixture.Declaration, "buddy", "github-packages-nuget", true);
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
            try { return new(repo, await CommitAsync(repo, preset)); }
            catch { repo.Dispose(); throw; }
        }

        // Reads a class-owned committed repository; the row owns only its scratch.
        internal static Fixture Shared(GitFixture repo, string revision) =>
            new(repo, revision, ownsRepository: false);

        internal static async Task<string> CommitAsync(GitFixture repo, string preset)
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
            return revision;
        }
        internal Task<NuGetPreparationResult> RunAsync(CancellationToken token) =>
            NuGetPreparation.PrepareAsync(new(Subject, Repo.Directory, scratch), ExecuteAsync,
                token);
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
        public void Dispose()
        {
            if (ownsRepository) Repo.Dispose();
            Directory.Delete(scratch, true);
        }
    }
}
