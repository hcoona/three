using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetPackageExecutionTests(TestContext context)
{
    [TestMethod]
    public async Task SelectedRecipePreservesNativeContextAndPrerequisites()
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        string scratch =
            Directory.CreateDirectory(Path.Combine(fixture.Scratch, "projection")).FullName;
        CiPlan plan = await DotNetFactsAssembler.CompleteAsync(fixture.Selection,
            fixture.Request with { Scratch = scratch },
                context.CancellationToken, fixture.ExecuteAsync);
        Assert.HasCount(3, plan.Checks);
        CollectionAssert.AreEquivalent(new[] {
            DotNetPackageChecks.Pack, DotNetPackageChecks.Contents,
            DotNetPackageChecks.Consumer },
                plan.Checks.Select(item => item.Work.Key.Check).ToArray());
        Assert.IsTrue(plan.Checks.All(item => item.Origins.SequenceEqual([CheckOrigin.Preset]) &&
            item.QualityPresets.SequenceEqual([DotNetPackageChecks.Preset]) &&
            item.Work.Package!.ExpectedVersion == "1.0.0"));
        NativeCommand projection = Assert.ContainsSingle(fixture.Commands);
        Assert.Contains("-target:GetBuildVersion", projection.Arguments);
        Assert.AreEqual("space%3Bsemi%25percent", projection.Arguments.Single(argument =>
            argument.StartsWith("-property:Probe=", StringComparison.Ordinal))
            ["-property:Probe=".Length..].Trim('"'));
        Assert.IsFalse(projection.Arguments.Any(argument => argument.Contains("Frozen",
            StringComparison.Ordinal) || argument == "-target:Build"));
        Assert.HasCount(1, plan.Checks.Single(item => item.Work.Key.Check ==
            DotNetPackageChecks.Contents).Work.Prerequisites);
        Assert.HasCount(2, plan.Checks.Single(item => item.Work.Key.Check ==
            DotNetPackageChecks.Consumer).Work.Prerequisites);
    }

    [TestMethod]
    [DataRow("preset")]
    [DataRow("missing-unit")]
    [DataRow("variant")]
    [DataRow("storage")]
    [DataRow("definition")]
    [DataRow("extra-output")]
    public async Task UnresolvedSelectedRecipeStopsBeforeNativeEffects(string defect)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        DotNetSelection selection = fixture.Selection;
        if (defect == "missing-unit") selection = selection with { CandidateUnits = [] };
        else if (defect is "variant" or "storage")
        {
            DotNetGraphNode node = fixture.Node;
            if (defect == "variant") node = node with { Dimension = new("Release", "net10.0", "") };
            else node.Identity.Globals["PackageOutputPath"] = "somewhere";
            selection = selection with
            {
                CandidateNodes = new Dictionary<string, DotNetGraphNode[]>
                { [DotNetPackageFixture.Project] = [node] }
            };
        }
        else if (defect == "preset")
            selection = selection with
            {
                Request = selection.Request with
                {
                    Candidate = selection.Request.Candidate with
                    {
                        Projects = [selection.Request.Candidate
                    .Projects[0] with { QualityPreset = "unknown" }]
                    }
                }
            };
        else
        {
            ReleaseBuild build = DotNetPackageFixture.Unit.Builds[0];
            build = defect == "definition" ? build with { Definition = "dotnet/unknown" } :
                build with { Outputs = [.. build.Outputs, new("extra", "symbols", "symbols")] };
            selection = selection with
            {
                CandidateUnits =
                [DotNetPackageFixture.Unit with { Builds = [build] }]
            };
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetFactsAssembler.CompleteAsync(
            selection, fixture.Request, context.CancellationToken, fixture.ExecuteAsync));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task NativeProjectionFailurePreventsProductCompilation()
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        fixture.Override = _ => new(NativeTermination.Exited, 1, "", "failed NBGV", 0.1, null);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetFactsAssembler.CompleteAsync(
            fixture.Selection, fixture.Request, context.CancellationToken, fixture.ExecuteAsync));
        Assert.Contains("-target:GetBuildVersion",
            Assert.ContainsSingle(fixture.Commands).Arguments);
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("origin")]
    [DataRow("preset")]
    [DataRow("missing-contents")]
    [DataRow("missing-consumer")]
    [DataRow("native-dimension")]
    [DataRow("dirty")]
    public async Task InvalidPackagePlanStopsBeforeNativeEffects(string defect)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        CiPlan plan = fixture.Plan;
        if (defect == "scope") plan = plan with { Scope = "other" };
        else if (defect.StartsWith("missing-", StringComparison.Ordinal))
            plan = plan with
            {
                Checks = plan.Checks.Where(item => item.Work.Key.Check !=
                (defect == "missing-contents" ? DotNetPackageChecks.Contents :
                    DotNetPackageChecks.Consumer)).ToArray()
            };
        else if (defect == "dirty") await File.AppendAllTextAsync(
            Path.Combine(fixture.Repo.Directory,
            DotNetPackageFixture.Project), "changed", context.CancellationToken);
        else
        {
            PlannedCheck first = plan.Checks[0];
            if (defect == "origin") first =
                first with { Origins = [CheckOrigin.NativeRetained], QualityPresets = [] };
            if (defect == "preset") first = first with { QualityPresets = ["unknown"] };
            if (defect == "native-dimension")
            {
                var dimensions = new Dictionary<string, string>(first.Work.Dimensions);
                dimensions["native"] = "{}";
                first = first with { Work = first.Work with { Dimensions = dimensions } };
            }
            plan = plan with { Checks = [first, .. plan.Checks[1..]] };
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken, plan));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task OriginalArchiveCoordinateAndBytesAreRetained()
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied,
            string.Join("; ", result.Failures.Select(failure => failure.Error)));
        DotNetOriginalPackage package = Assert.ContainsSingle(result.Packages);
        Assert.AreEqual("product", package.Unit);
        Assert.AreEqual("package", package.Build);
        Assert.AreEqual("package", package.Output);
        Assert.AreEqual("primary-package", package.Role);
        Assert.AreEqual("nuget-package", package.Kind);
        Assert.AreEqual("original.nupkg", Path.GetFileName(package.Path));
        Assert.AreEqual(new FileInfo(package.Path).Length, package.Size);
        Assert.AreEqual(DotNetPackageArchive.Digest(package.Path), package.Sha256);
        Assert.HasCount(4, result.Commands);
        Assert.Contains("-property:Configuration=Debug", result.Commands[0].Command.Arguments);
        Assert.AreEqual("space%3Bsemi%25percent", result.Commands[0].Command.Arguments.Single(
            argument => argument.StartsWith("-property:Probe=", StringComparison.Ordinal))
            ["-property:Probe=".Length..].Trim('"'));
        Assert.IsNotNull(result.Commands[1].Command.Environment);
        Assert.IsTrue(result.Commands.Skip(1).All(
            item => item.Command.Directory != fixture.Repo.Directory));
        Assert.HasCount(1, result.ConsumerOutputs);
        DotNetPackageRunResult transfer = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(result,
            TransferJson.Default.DotNetPackageRunResult), TransferJson.
                Default.DotNetPackageRunResult)!;
        CollectionAssert.AreEqual(result.Results, transfer.Results);
        CollectionAssert.AreEqual(result.Packages, transfer.Packages);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("undeclared")]
    [DataRow("symbols")]
    [DataRow("wrong-identity")]
    [DataRow("different-projection")]
    [DataRow("missing-receipt")]
    public async Task MissingOrUndeclaredNativeArchivesCannotPassPack(string defect)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        fixture.ModifyPack = document =>
        {
            string path =
                document["TargetResults"]!["_GetOutputItemsFromPack"]!["Items"]![0]!["FullPath"]!
                    .GetValue<string>();
            if (defect == "missing") File.Delete(path);
            if (defect is "undeclared" or "symbols")
                File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "extra." +
                    (defect == "symbols" ? "snupkg" : "nupkg")));
            if (defect == "wrong-identity") Replace(path, "product.nuspec",
                "<package><metadata><id>Other</id><version>1.0.0</version></metadata></package>");
            if (defect == "different-projection")
                document["Properties"]!["AssemblyInformationalVersion"] = "other";
            if (defect == "missing-receipt") document["TargetResults"]!.AsObject().Remove("Pack");
        };
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        CollectionAssert.AreEqual(new[] {
            CheckStatus.Failed, CheckStatus.Skipped, CheckStatus.Skipped },
            result.Results.Select(item => item.Status).ToArray());
        Assert.IsEmpty(result.Packages);
        Assert.HasCount(1, result.Commands);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("missing-dll")]
    [DataRow("wrong-documentation")]
    [DataRow("empty-readme")]
    [DataRow("dependency")]
    [DataRow("version")]
    public async Task ProductContentRejectsIdentityVersionAndPayloadDefects(string defect)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        fixture.ModifyArchive = path =>
        {
            if (defect == "missing-dll")
                Replace(path, "lib/net10.0/WorkflowDelivery.Tests.dll", null);
            if (defect == "wrong-documentation")
                Replace(path, "lib/net10.0/WorkflowDelivery.Tests.xml", "<doc />");
            if (defect == "empty-readme") Replace(path, "README.md", "");
            if (defect == "dependency") Replace(path, "product.nuspec", """
                <package><metadata><id>WorkflowDelivery.Tests</id><version>1.0.0</version>
                <dependencies><dependency id="MissingDependency" version="1.0.0" /></dependencies>
                </metadata></package>
                """);
        };
        if (defect == "version") fixture.ModifyPack = document =>
        {
            Dictionary<string, string> expected = fixture.Values;
            expected["AssemblyFileVersion"] = "999.0.0";
            Assert.ThrowsExactly<InvalidDataException>(() => DotNetPackageArchive.Contents(
                document["TargetResults"]!["_GetOutputItemsFromPack"]!["Items"]![0]!["FullPath"]!
                    .GetValue<string>(), expected));
            Replace(
                document["TargetResults"]!["_GetOutputItemsFromPack"]!["Items"]![0]!["FullPath"]!
                .GetValue<string>(), "lib/net10.0/WorkflowDelivery.Tests.dll", "not a PE file");
        };
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        CollectionAssert.AreEqual(new[] {
            CheckStatus.Passed, CheckStatus.Failed, CheckStatus.Skipped },
            result.Results.Select(item => item.Status).ToArray());
        Assert.HasCount(1, result.Packages);
        Assert.HasCount(1, result.Commands);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("README.md")]
    [DataRow("readme.md")]
    [DataRow("%52EADME.md")]
    [DataRow("../bad")]
    [DataRow("bad\\name")]
    public async Task NativeDecodedArchiveNamesRejectAmbiguityAndUnsafePaths(string name)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        fixture.ModifyArchive = path =>
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("ambiguous or unsafe");
        };
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.IsEmpty(result.Packages);
    }

    [TestMethod]
    [DataRow("restore")]
    [DataRow("build")]
    [DataRow("marker")]
    public async Task LaterPhaseFailureRetainsSuccessfulPrerequisites(string defect)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command =>
            defect == "restore" && command.Arguments.Contains("restore") ||
            defect == "build" && command.Arguments.Contains("-target:Build,GetTargetPath") ?
                new(NativeTermination.Exited, 1, "", "native failure", 0.1, null) :
            defect == "marker" && command.Arguments.Length == 1 ?
                new(NativeTermination.Exited, 0, "wrong marker", "", 0.1, null) : null;
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        CollectionAssert.AreEqual(new[] {
            CheckStatus.Passed, CheckStatus.Passed, CheckStatus.Failed },
            result.Results.Select(item => item.Status).ToArray());
        Assert.HasCount(1, result.Packages);
        Assert.IsEmpty(result.ConsumerOutputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("missing-assets")]
    [DataRow("different-folder")]
    [DataRow("different-identity")]
    [DataRow("different-bytes")]
    [DataRow("missing-build-receipt")]
    public async Task MissingConsumerReceiptOrRestoredBytesCannotPass(string defect)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        if (defect == "different-folder") fixture.ModifyAssets = assets =>
            assets["packageFolders"] =
                new JsonObject { [fixture.Repo.Directory] = new JsonObject() };
        if (defect == "different-identity") fixture.ModifyAssets = assets =>
            assets["libraries"] =
                new JsonObject { ["Other/1.0.0"] = new JsonObject { ["type"] = "package" } };
        if (defect == "different-bytes") fixture.ModifyInstalled =
            path => File.AppendAllText(path, "different");
        fixture.Override = command =>
            defect == "missing-assets" && command.Arguments.Contains("restore") ||
            defect == "missing-build-receipt" &&
                command.Arguments.Contains("-target:Build,GetTargetPath") ?
                new(NativeTermination.Exited, 0,
                    "diagnostic success without receipt", "", 0.1, null) : null;
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        CollectionAssert.AreEqual(new[] {
            CheckStatus.Passed, CheckStatus.Passed, CheckStatus.Failed },
            result.Results.Select(item => item.Status).ToArray());
        Assert.IsEmpty(result.ConsumerOutputs);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("CleanupFailed", "Failed")]
    public async Task NativePackageInterruptionCannotFabricateSuccess(string
        termination, string status)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        fixture.Override =
            _ => new(Enum.Parse<NativeTermination>(termination), null, "", "", 0.1, "interrupted");
        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(Enum.Parse<CheckStatus>(status), result.Results[0].Status);
        Assert.IsTrue(result.Results.Skip(1).All(item => item.Status == CheckStatus.Skipped));
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results[..1]).Satisfied);
    }

    private static void Replace(string path, string name, string? content)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        archive.GetEntry(name)?.Delete();
        if (content is not null)
        {
            using var writer =
            new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(content);
        }
    }
}
