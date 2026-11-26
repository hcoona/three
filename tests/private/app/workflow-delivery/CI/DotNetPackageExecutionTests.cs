using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetPackageExecutionTests(TestContext context)
{
    private static GitFixture? shared;
    private static string sharedCandidate = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedCandidate = await DotNetPackageFixture.CommitAsync(repo);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static DotNetPackageFixture Shared() =>
        DotNetPackageFixture.Shared(shared!, sharedCandidate);

    [TestMethod]
    public async Task ProjectedRecipeRetainsOriginalNuGetArchiveAndRunsCleanConsumer()
    {
        using var projection = Shared();
        using var execution = Shared();
        CiPlan plan = await DotNetFactsAssembler.CompleteAsync(projection.Selection,
            projection.Request, context.CancellationToken, projection.ExecuteAsync);

        DotNetPackageRunResult result = await execution.RunAsync(context.CancellationToken, plan);

        Assert.IsTrue(ResultCollector.Collect(plan, result.Results).Satisfied,
            string.Join("; ", result.Failures.Select(failure => failure.Error)));
        DotNetOriginalPackage package = Assert.ContainsSingle(result.Packages);
        Assert.AreEqual("product", package.Unit);
        Assert.AreEqual("package", package.Output);
        Assert.AreEqual(new FileInfo(package.Path).Length, package.Size);
        Assert.AreEqual(DotNetPackageArchive.Digest(package.Path), package.Sha256);
        NativeCommand pack = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Command.Arguments.Contains("-target:Pack,_GetOutputItemsFromPack"))).Command;
        Assert.Contains("-property:Configuration=Debug", pack.Arguments);
        string probe = Assert.ContainsSingle(pack.Arguments.Where(argument =>
            argument.StartsWith("-property:Probe=", StringComparison.Ordinal)));
        Assert.AreEqual("space%3Bsemi%25percent", probe["-property:Probe=".Length..].Trim('"'));
        NativeCommand restore = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Command.Arguments[0] == "restore")).Command;
        Assert.StartsWith(execution.Scratch, restore.Directory);
        Assert.StartsWith(execution.Scratch, restore.Environment!["NUGET_PACKAGES"]!);
        string output = Assert.ContainsSingle(result.ConsumerOutputs).Path;
        Assert.IsTrue(result.Commands.Any(item => item.Command.Arguments.Contains(output)));
    }

    [TestMethod]
    public async Task MissingReleaseUnitStopsBeforeProjection()
    {
        using var fixture = Shared();
        DotNetSelection selection = fixture.Selection with { CandidateUnits = [] };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetFactsAssembler.CompleteAsync(selection, fixture.Request,
                context.CancellationToken, fixture.ExecuteAsync));

        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task NativeProjectionFailurePreventsProductCompilation()
    {
        using var fixture = Shared();
        fixture.Override = _ => new(NativeTermination.Exited, 1, "", "failed NBGV", 0.1, null);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            DotNetFactsAssembler.CompleteAsync(fixture.Selection, fixture.Request,
                context.CancellationToken, fixture.ExecuteAsync));

        Assert.Contains("-target:GetBuildVersion",
            Assert.ContainsSingle(fixture.Commands).Arguments);
    }

    [TestMethod]
    public async Task MissingConsumerObligationStopsBeforeNativeEffects()
    {
        using var fixture = Shared();
        CiPlan plan = fixture.Plan with
        {
            Checks = fixture.Plan.Checks.Where(item =>
                item.Work.Key.Check != DotNetPackageChecks.Consumer).ToArray()
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));

        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow("nupkg")]
    [DataRow("snupkg")]
    public async Task UndeclaredNativeArchivePreventsQualification(string extension)
    {
        using var fixture = Shared();
        fixture.ModifyArchive = path =>
            File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "extra." + extension));

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, DotNetPackageChecks.Pack));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Consumer));
        Assert.IsEmpty(result.Packages);
        Assert.IsEmpty(result.ConsumerOutputs);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WrongNuGetIdentityOrMissingPackReceiptCannotRetainOutput(bool omitReceipt)
    {
        using var fixture = Shared();
        if (omitReceipt)
            fixture.ModifyPack = document => document["TargetResults"]!.AsObject().Remove("Pack");
        else
            fixture.ModifyArchive = ChangePackageId;

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, DotNetPackageChecks.Pack));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Consumer));
        Assert.IsEmpty(result.Packages);
        Assert.IsEmpty(result.ConsumerOutputs);
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments[0] == "restore"));
        string error = Assert.ContainsSingle(result.Failures).Error;
        Assert.AreEqual(omitReceipt ? "Missing or failed native package target: Pack" :
            "Package identity differs from native NBGV projection.", error);
    }

    private static void ChangePackageId(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        ZipArchiveEntry entry = archive.GetEntry("product.nuspec")!;
        XDocument nuspec;
        using (Stream contents = entry.Open()) nuspec = XDocument.Load(contents);
        nuspec.Root!.Element("metadata")!.Element("id")!.Value = "Other.Package";
        entry.Delete();
        using Stream output = archive.CreateEntry("product.nuspec").Open();
        nuspec.Save(output);
    }

    [TestMethod]
    public async Task MissingAssemblyInOriginalArchiveSkipsConsumerRestore()
    {
        using var fixture = Shared();
        fixture.ModifyArchive = path =>
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            archive.GetEntry("lib/net10.0/WorkflowDelivery.Tests.dll")!.Delete();
        };

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Passed, Status(result, DotNetPackageChecks.Pack));
        Assert.AreEqual(CheckStatus.Failed, Status(result, DotNetPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Consumer));
        Assert.ContainsSingle(result.Packages);
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments[0] == "restore"));
    }

    [TestMethod]
    [DataRow("analyzers/dotnet/cs/WorkflowDelivery.Tests.dll", false)]
    [DataRow(null, true)]
    [DataRow("Analyzers/dotnet/cs/WorkflowDelivery.Tests.dll", true)]
    public void ArchiveContentsRejectsDifferentAnalyzerPaths(string? actual, bool expected)
    {
        using var fixture = Shared();
        string path = Path.Combine(fixture.Scratch, "original.nupkg");
        fixture.CreateArchive(path);
        if (actual is not null)
        {
            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update);
            archive.CreateEntryFromFile(typeof(DotNetPackageExecutionTests).Assembly.Location,
                actual);
        }

        var error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetPackageArchive.Contents(path, fixture.Values, ["net10.0"],
                expected ? ["analyzers/dotnet/cs/WorkflowDelivery.Tests.dll"] : null));

        Assert.Contains("analyzer payload", error.Message);
    }

    [TestMethod]
    public void ArchiveContentsRequiresTheAnalyzerAssemblyToMatchItsPath()
    {
        using var fixture = Shared();
        string path = Path.Combine(fixture.Scratch, "original.nupkg");
        fixture.CreateArchive(path);
        const string analyzer = "analyzers/dotnet/cs/WrongAssembly.dll";
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
            archive.CreateEntryFromFile(typeof(DotNetPackageExecutionTests).Assembly.Location,
                analyzer);

        var error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetPackageArchive.Contents(path, fixture.Values, ["net10.0"], [analyzer]));

        Assert.Contains("assembly identity/version", error.Message);
    }

    [TestMethod]
    [DataRow("%52EADME.md")]
    [DataRow("../bad")]
    public async Task NativeArchiveWithAmbiguousOrUnsafeEntryCannotBeRetained(string name)
    {
        using var fixture = Shared();
        fixture.ModifyArchive = path =>
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("ambiguous or unsafe");
        };

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, Status(result, DotNetPackageChecks.Pack));
        Assert.IsEmpty(result.Packages);
    }

    [TestMethod]
    public async Task RestoreFailurePreservesBothDiagnosticStreamsAndEarlierQualification()
    {
        using var fixture = Shared();
        var native = new NativeCommandResult(NativeTermination.Exited, 1,
            "error NU1101: missing package", "welcome notice", 0.1, null);
        fixture.Override = command => command.Arguments[0] == "restore" ? native : null;

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        AssertFailedConsumer(result);
        string failure = Assert.ContainsSingle(result.Failures).Error;
        Assert.Contains(native.Stdout, failure);
        Assert.Contains(native.Stderr, failure);
        Assert.AreEqual(native, Assert.ContainsSingle(result.Commands.Where(item =>
            item.Command.Arguments[0] == "restore")).Result);
    }

    [TestMethod]
    public async Task AssetsPointingOutsideCleanConsumerCannotQualify()
    {
        using var fixture = Shared();
        fixture.ModifyAssets = assets => assets["packageFolders"] =
            new JsonObject { [fixture.Repo.Directory] = new JsonObject() };

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        AssertFailedConsumer(result);
        Assert.IsFalse(result.Commands.Any(item =>
            item.Command.Arguments.Contains("-target:Build,GetTargetPath")));
    }

    [TestMethod]
    public async Task RestoredBytesMustMatchOriginalArchive()
    {
        using var fixture = Shared();
        fixture.ModifyInstalled = path => File.AppendAllText(path, "different");

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        AssertFailedConsumer(result);
    }

    [TestMethod]
    public async Task WrongPublicMarkerCannotQualifyConsumer()
    {
        using var fixture = Shared();
        fixture.Override = command => command.Arguments.Length == 1
            ? new(NativeTermination.Exited, 0, "wrong marker", "", 0.1, null) : null;

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        AssertFailedConsumer(result);
        Assert.Contains("marker API", Assert.ContainsSingle(result.Failures).Error);
    }

    [TestMethod]
    [DataRow((int)NativeTermination.Cancelled, (int)CheckStatus.Cancelled)]
    [DataRow((int)NativeTermination.TimedOut, (int)CheckStatus.TimedOut)]
    [DataRow((int)NativeTermination.CleanupFailed, (int)CheckStatus.Failed)]
    public async Task InterruptedNativePackCannotFabricateArchiveOrRunConsumers(
        int termination, int status)
    {
        using var fixture = Shared();
        fixture.Override =
            _ => new((NativeTermination)termination, null, "", "", 0.1, "interrupted");

        DotNetPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual((CheckStatus)status, Status(result, DotNetPackageChecks.Pack));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Skipped, Status(result, DotNetPackageChecks.Consumer));
        Assert.IsEmpty(result.Packages);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResponseSizeLimitAppliesToTheParsedTarget(bool linked)
    {
        using var fixture = Shared();
        string target = Path.Combine(fixture.Scratch, "result.json");
        await using (FileStream file = File.Create(target))
            file.SetLength(32 * 1024 * 1024 + 1);
        string path = linked ? Link(Path.Combine(fixture.Scratch, "linked.json"), target) : target;

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => DotNetPackageResults.ResponseAsync(path, context.CancellationToken));

        Assert.Contains("exceeds its limit", failure.Message);
    }

    [TestMethod]
    public void LinkedOriginalIsComparedByItsTargetBytes()
    {
        using var fixture = Shared();
        string target = Path.Combine(fixture.Scratch, "original.nupkg");
        File.WriteAllBytes(target, [1, 2, 3, 4, 5]);
        string path = Link(Path.Combine(fixture.Scratch, "linked.nupkg"), target);

        DotNetPackageArchive.RequireOriginal(new("unit", "build", "output", "role",
            "nuget-package", path, 5, DotNetPackageArchive.Digest(target)));
    }

    private static string Link(string path, string target)
    {
        try { File.CreateSymbolicLink(path, target); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("The platform cannot create a file link.");
        }
        return path;
    }

    private static CheckStatus Status(DotNetPackageRunResult result, string check) =>
        result.Results.Single(item => item.Key.Check == check).Status;

    private static void AssertFailedConsumer(DotNetPackageRunResult result)
    {
        Assert.AreEqual(CheckStatus.Passed, Status(result, DotNetPackageChecks.Pack));
        Assert.AreEqual(CheckStatus.Passed, Status(result, DotNetPackageChecks.Contents));
        Assert.AreEqual(CheckStatus.Failed, Status(result, DotNetPackageChecks.Consumer));
        Assert.ContainsSingle(result.Packages);
        Assert.IsEmpty(result.ConsumerOutputs);
    }
}
