using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class MsBuildResourceReaderTests(TestContext context)
{
    private const string Project = "tests/Control/Control.csproj";
    private static readonly MsBuildDimension Release = new("Release", "net10.0", "linux-x64");
    private static readonly string[] BaseInputs =
        ["shared/Version 雪.json", "shared/executable.txt"];
    private static readonly string[] VersionOnlyInputs = ["shared/Version 雪.json"];
    private static readonly string[] MovedInputs = ["moved/version.json"];
    private static readonly string[] ReleaseNativeInputs =
        ["shared/Version 雪.json", "tests/Control/Resources/kept.txt"];
    private static readonly string[] DebugNativeInputs = ["tests/Control/Resources/kept.txt"];

    // Rows that vary only the request or native answers read one committed checkout.
    private static GitFixture? shared;
    private static GitMaterialization? sharedCheckout;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await CommitAsync(classContext.CancellationToken);
        try
        {
            sharedCheckout = await BindAsync(repo, classContext.CancellationToken);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task SharedCheckoutControlQualifies()
    {
        GitMaterialization checkout = sharedCheckout!;
        const string resource = "shared/Version 雪.json";
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(Output(checkout, Release, resource)));
        MsBuildResources result = await reader.ReadAsync(Project, Release,
            context.CancellationToken);
        Assert.AreEqual(checkout.Revision.Commit, result.Revision);
        CollectionAssert.AreEqual(VersionOnlyInputs, result.Inputs);
        JsonNode native = JsonNode.Parse(Output(checkout, new("Debug", "net10.0")))!;
        native["Properties"]!["TargetFrameworks"] = "";
        native["Properties"]!["RuntimeIdentifiers"] = "";
        MsBuildResources fallback = await new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(native.ToJsonString())).ReadDefaultAsync(Project, "Debug",
                context.CancellationToken);
        Assert.AreEqual(new MsBuildDimension("Debug", "net10.0"), fallback.Dimension);
    }

    [TestMethod]
    public async Task DefaultQueryPreservesNativeDimensionWithoutOverrides()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        var dimension = new MsBuildDimension("Debug", "net10.0");
        JsonNode native = JsonNode.Parse(Output(checkout, dimension, "shared/Version 雪.json"))!;
        native["Properties"]!["TargetFrameworks"] = "";
        native["Properties"]!["RuntimeIdentifiers"] = "";
        string[]? arguments = null;
        int queries = 0;
        var reader = new MsBuildResourceReader(checkout, (actual, _) =>
        {
            arguments = actual;
            queries++;
            return Task.FromResult(native.ToJsonString());
        });

        MsBuildResources result = await reader.ReadDefaultAsync(Project, "Debug",
            context.CancellationToken);

        Assert.AreEqual(1, queries);
        CollectionAssert.AreEqual(new[] { "msbuild", "--disable-build-servers",
            Path.Combine(checkout.Root, Project), "-nologo", "-noAutoResponse",
            "-getProperty:MSBuildProjectFullPath,Configuration,TargetFramework,TargetFrameworks,"
                + "RuntimeIdentifier,RuntimeIdentifiers",
            "-getItem:EmbeddedResource", "-property:Configuration=Debug" }, arguments);
        Assert.AreEqual(dimension, result.Dimension);
        CollectionAssert.AreEqual(VersionOnlyInputs, result.Inputs);
    }

    [TestMethod]
    [DataRow("TargetFrameworks", "net9.0;net10.0")]
    [DataRow("RuntimeIdentifiers", "linux-x64;win-x64")]
    [DataRow("TargetFrameworks", null)]
    [DataRow("TargetFramework", "")]
    [DataRow("TargetFramework", "net9.0;net10.0")]
    [DataRow("RuntimeIdentifier", "linux-x64;win-x64")]
    public async Task DefaultQueryRejectsUnsupportedRequiredShapes(string property, string? value)
    {
        GitMaterialization checkout = sharedCheckout!;
        JsonNode native = JsonNode.Parse(Output(checkout, new("Debug", "net10.0")))!;
        JsonObject properties = native["Properties"]!.AsObject();
        properties["TargetFrameworks"] = "";
        properties["RuntimeIdentifiers"] = "";
        if (value is null) properties.Remove(property);
        else properties[property] = value;
        var reader = new MsBuildResourceReader(checkout,
            (_, _) => Task.FromResult(native.ToJsonString()));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadDefaultAsync(Project, "Debug", context.CancellationToken));
    }

    [TestMethod]
    public async Task NativeDefaultContextHonorsDebugConditionalResources()
    {
        using var repo = await FixtureAsync();
        await repo.SetAsync(Project, """
            <Project DefaultTargets="Forbidden">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <EmbeddedResource Include="../../shared/Version 雪.json" />
              </ItemGroup>
              <Target Name="Forbidden"><Error Text="No targets are permitted." /></Target>
            </Project>
            """);
        await repo.CommitAsync("HEAD");
        await repo.GitAsync("reset", "--hard", "HEAD");

        MsBuildResources result = await new MsBuildResourceReader(await BindAsync(repo))
            .ReadDefaultAsync(Project, "Debug", context.CancellationToken);

        Assert.AreEqual(new("Debug", "net10.0"), result.Dimension);
        CollectionAssert.AreEqual(VersionOnlyInputs, result.Inputs);
    }

    [TestMethod]
    public async Task ReadBindsRevisionProjectDimensionsAndNativeResourcePaths()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        const string resource = "shared/Version 雪.json";
        string output = Output(checkout, Release, resource, "shared/executable.txt", resource);
        var reader = new MsBuildResourceReader(checkout, (_, _) => Task.FromResult(output));

        MsBuildResources result = await reader.ReadAsync(Project, Release,
            context.CancellationToken);

        Assert.AreEqual(checkout.Revision.Commit, result.Revision);
        Assert.AreEqual(Project, result.Project);
        Assert.AreEqual(Release, result.Dimension);
        CollectionAssert.AreEqual(BaseInputs, result.Inputs);
    }

    [TestMethod]
    public async Task ReadRequestsEvaluationOnly()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        string[]? actual = null;
        var reader = new MsBuildResourceReader(checkout, (arguments, _) =>
        {
            actual = arguments;
            return Task.FromResult(Output(checkout, Release));
        });

        MsBuildResources result = await reader.ReadAsync(Project, Release,
            context.CancellationToken);

        CollectionAssert.AreEqual(new[] { "msbuild", "--disable-build-servers",
            Path.Combine(checkout.Root, Project), "-nologo", "-noAutoResponse",
            "-getProperty:MSBuildProjectFullPath,Configuration,TargetFramework,RuntimeIdentifier",
            "-getItem:EmbeddedResource", "-property:Configuration=Release",
            "-property:TargetFramework=net10.0", "-property:RuntimeIdentifier=linux-x64" }, actual);
        Assert.IsEmpty(result.Inputs);
    }

    [TestMethod]
    public async Task ReadAcceptsEmptyResourcesAndUnspecifiedRid()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        var dimension = new MsBuildDimension("Debug", "net10.0");
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(Output(checkout, dimension)));

        MsBuildResources result = await reader.ReadAsync(Project, dimension,
            context.CancellationToken);

        Assert.AreEqual(dimension, result.Dimension);
        Assert.AreEqual("", result.Dimension.RuntimeIdentifier);
        Assert.IsEmpty(result.Inputs);
    }

    [TestMethod]
    [DataRow("Properties", null)]
    [DataRow("Items", "[]")]
    [DataRow("Properties/Configuration", "null")]
    [DataRow("Properties/TargetFramework", "\" \"")]
    [DataRow("Properties/RuntimeIdentifier", "42")]
    [DataRow("Items/EmbeddedResource", "{}")]
    [DataRow("Items/EmbeddedResource", "[null]")]
    [DataRow("Items/EmbeddedResource/0/FullPath", null)]
    public Task ReadRejectsInvalidNativeShape(string path, string? json)
    {
        JsonNode output = JsonNode.Parse(ValidNativeOutput())!;
        string[] fields = path.Split('/');
        JsonNode target = output;
        foreach (string field in fields[..^1])
            target = field == "0" ? target[0]! : target[field]!;
        if (json is null) target.AsObject().Remove(fields[^1]);
        else target[fields[^1]] = JsonNode.Parse(json);
        return RejectNativeAsync(output.ToJsonString());
    }

    private static string ValidNativeOutput() =>
        Output(sharedCheckout!, Release, "shared/Version 雪.json");

    private Task<InvalidDataException> RejectNativeAsync(string output) =>
        Assert.ThrowsExactlyAsync<InvalidDataException>(() => new MsBuildResourceReader(
            sharedCheckout!, (_, _) => Task.FromResult(output))
            .ReadAsync(Project, Release, context.CancellationToken));

    [TestMethod]
    [DataRow(null, "{")]
    [DataRow("Properties", "{}")]
    [DataRow("FullPath", "null")]
    public async Task ReadRejectsMalformedOrDuplicateJson(string? duplicate, string json)
    {
        string output = duplicate is null ? json : ValidNativeOutput().Replace(
            $"\"{duplicate}\":", $"\"{duplicate}\":{json},\"{duplicate}\":",
            StringComparison.Ordinal);
        var reader = new MsBuildResourceReader(sharedCheckout!, (_, _) => Task.FromResult(output));

        await Assert.ThrowsAsync<JsonException>(() => reader.ReadAsync(Project, Release,
            context.CancellationToken));
    }

    [TestMethod]
    [DataRow("MSBuildProjectFullPath")]
    [DataRow("Configuration")]
    [DataRow("TargetFramework")]
    [DataRow("RuntimeIdentifier")]
    public async Task ReadRejectsConflictingNativeIdentityAndDimensions(string field)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        JsonNode root = JsonNode.Parse(Output(checkout, Release))!;
        JsonNode properties = root["Properties"]!;
        properties[field] = field == "MSBuildProjectFullPath"
            ? Path.Combine(checkout.Root, "shared/executable.txt") : "other";
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(root.ToJsonString()));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(Project, Release, context.CancellationToken));

        Assert.Contains("different project or dimension", error.Message);
    }

    [TestMethod]
    public Task ReadRejectsRelativeResource() =>
        RejectResourceAsync(sharedCheckout!, "shared/Version 雪.json");

    [TestMethod]
    public Task ReadRejectsResourceOutsideCheckout() =>
        RejectResourceAsync(sharedCheckout!,
            Path.GetFullPath("../outside.txt", sharedCheckout!.Root));

    [TestMethod]
    public Task ReadRejectsResourceUnderSiblingRoot() =>
        RejectResourceAsync(sharedCheckout!, sharedCheckout!.Root + "-sibling/input.txt");

    [TestMethod]
    public Task ReadRejectsResourceDirectory() =>
        RejectResourceAsync(sharedCheckout!, Path.Combine(sharedCheckout!.Root, "shared"));

    [TestMethod]
    public async Task ReadRejectsUntrackedResource()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        string path = Path.Combine(checkout.Root, "extra.txt");
        await File.WriteAllTextAsync(path, "untracked", context.CancellationToken);
        await RejectResourceAsync(checkout, path);
    }

    [TestMethod]
    public async Task ReadRejectsMissingTrackedResource()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        string path = Path.Combine(checkout.Root, "shared/Version 雪.json");
        File.Delete(path);
        await RejectResourceAsync(checkout, path);
    }

    private async Task RejectResourceAsync(GitMaterialization checkout, string native)
    {
        JsonNode root = JsonNode.Parse(Output(checkout, Release, "shared/Version 雪.json"))!;
        root["Items"]!["EmbeddedResource"]![0]!["FullPath"] = native;
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(root.ToJsonString()));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(Project,
            Release, context.CancellationToken));
    }

    [TestMethod]
    [DataRow("symlink")]
    [DataRow("gitlink")]
    public async Task ReadRejectsNonregularCommittedResource(string kind)
    {
        using var repo = await FixtureAsync();
        string basis = (await new GitReader(repo.Directory).ReadAsync("HEAD",
            context.CancellationToken)).Commit;
        if (kind == "symlink")
            await repo.SetAsync("shared/nonregular", "Version 雪.json", "120000");
        else
            await repo.LinkAsync("shared/nonregular", basis);
        await repo.CommitAsync(basis);
        await repo.GitAsync("reset", "--hard", "HEAD");
        GitMaterialization checkout = await BindAsync(repo);
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(Output(checkout, Release, "shared/nonregular")));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(Project,
            Release, context.CancellationToken));
    }

    [TestMethod]
    [DataRow("../Control.csproj")]
    [DataRow("tests\\Control\\Control.csproj")]
    [DataRow("extra.csproj")]
    public Task ReadRejectsUnsupportedProjectBeforeNativeQuery(string project) =>
        RejectRequestAsync(project, Release);

    [TestMethod]
    [DataRow("", "net10.0", "linux-x64")]
    [DataRow("Release;Other=1", "net10.0", "linux-x64")]
    [DataRow("Release\"", "net10.0", "linux-x64")]
    [DataRow("Release", "", "linux-x64")]
    [DataRow("Release", "net10%2e0", "linux-x64")]
    [DataRow("Release", "net10.0", "linux-x64,Other=1")]
    [DataRow("Release", "net10.0", "linux-x64\n")]
    public Task ReadRejectsUnsupportedDimensionsBeforeNativeQuery(string configuration,
        string framework, string rid) =>
        RejectRequestAsync(Project, new(configuration, framework, rid));

    private Task<InvalidDataException> RejectRequestAsync(string project,
        MsBuildDimension dimension)
    {
        var reader = new MsBuildResourceReader(sharedCheckout!, (_, _) =>
            throw new AssertFailedException("Unsupported requests must not invoke MSBuild."));

        return Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(project,
            dimension, context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadPropagatesNativeFailureAndCancellation()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        var failure = new InvalidDataException("Native evaluation unavailable.");
        var failing = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromException<string>(failure));

        InvalidDataException actual = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            failing.ReadAsync(Project, Release, context.CancellationToken));

        Assert.AreSame(failure, actual);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        var cancelled = new MsBuildResourceReader(checkout, (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Output(checkout, Release));
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            cancelled.ReadAsync(Project, Release, cancellation.Token));
        using var after = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        var cancelledAfterQuery = new MsBuildResourceReader(checkout, (_, _) =>
        {
            after.Cancel();
            return Task.FromResult(Output(checkout, Release));
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            cancelledAfterQuery.ReadAsync(Project, Release, after.Token));
    }

    [TestMethod]
    public async Task ReadBothRevisionsPreservesRemovedAndMovedResourceRelations()
    {
        using var repo = await FixtureAsync();
        GitMaterialization before = await BindAsync(repo);
        var firstReader = new MsBuildResourceReader(before, (_, _) =>
            Task.FromResult(Output(before, Release, BaseInputs)));
        MsBuildResources first = await firstReader.ReadAsync(Project, Release,
            context.CancellationToken);
        await repo.RemoveAsync("shared/Version 雪.json");
        await repo.SetAsync("moved/version.json", "same native input");
        await repo.CommitAsync(before.Revision.Commit);
        await repo.GitAsync("reset", "--hard", "HEAD");
        GitMaterialization after = await BindAsync(repo);
        var nextReader = new MsBuildResourceReader(after, (_, _) =>
            Task.FromResult(Output(after, Release, "moved/version.json")));

        MsBuildResources next = await nextReader.ReadAsync(Project, Release,
            context.CancellationToken);

        Assert.AreEqual(before.Revision.Commit, first.Revision);
        Assert.AreEqual(after.Revision.Commit, next.Revision);
        Assert.AreNotEqual(first.Revision, next.Revision);
        Assert.AreEqual(Project, first.Project);
        Assert.AreEqual(Project, next.Project);
        CollectionAssert.AreEqual(BaseInputs, first.Inputs);
        CollectionAssert.AreEqual(MovedInputs, next.Inputs);
    }

    [TestMethod]
    public async Task NativeEvaluationResolvesImportsItemsAndDimensionsWithoutTargets()
    {
        using var repo = await FixtureAsync();
        await repo.SetAsync(Project, """
            <Project DefaultTargets="Forbidden">
              <Import Project="resources.props" />
              <Target Name="Forbidden">
                <Error Text="Resource reading must not run targets." />
              </Target>
            </Project>
            """);
        await repo.SetAsync("tests/Control/resources.props", """
            <Project>
              <ItemGroup>
                <EmbeddedResource Include="Resources/*.txt" />
                <EmbeddedResource Remove="Resources/skipped.txt" />
                <EmbeddedResource Include="../../shared/Version 雪.json"
                  Condition="'$(Configuration)' == 'Release'
                    and '$(TargetFramework)' == 'net10.0'
                    and '$(RuntimeIdentifier)' == 'linux-x64'" />
              </ItemGroup>
            </Project>
            """);
        await repo.SetAsync("tests/Control/Resources/kept.txt");
        await repo.SetAsync("tests/Control/Resources/skipped.txt");
        await repo.SetAsync("Directory.Build.rsp", "-target:Forbidden\n");
        await repo.CommitAsync("HEAD");
        await repo.GitAsync("reset", "--hard", "HEAD");
        GitMaterialization checkout = await BindAsync(repo);
        var reader = new MsBuildResourceReader(checkout);

        MsBuildResources release = await reader.ReadAsync(Project, Release,
            context.CancellationToken);
        MsBuildResources debug = await reader.ReadAsync(Project, new("Debug", "net10.0"),
            context.CancellationToken);

        Assert.AreEqual(checkout.Revision.Commit, release.Revision);
        Assert.AreEqual(Project, release.Project);
        Assert.AreEqual(Release, release.Dimension);
        CollectionAssert.AreEqual(ReleaseNativeInputs, release.Inputs);
        Assert.AreEqual(new("Debug", "net10.0"), debug.Dimension);
        CollectionAssert.AreEqual(DebugNativeInputs, debug.Inputs);
    }

    // The native rows above contract the switches and JSON output of the pinned SDK only if
    // their fixture resolves that SDK rather than whatever the host would choose.
    [TestMethod]
    public async Task NativeRowsResolveThePinnedSdk()
    {
        string version = await WorkflowDelivery.Platform.NativeProcess.RunAsync("dotnet",
            sharedCheckout!.Root, ["--version"], context.CancellationToken);
        Assert.AreEqual(RepositoryToolSelection.DotNetSdkVersion, version.Trim());
    }

    [TestMethod]
    public async Task NativeEvaluationFailureReturnsNoFacts()
    {
        using var repo = await FixtureAsync();
        await repo.SetAsync(Project, """
            <Project><Import Project="missing-private-detail.props" /></Project>
            """);
        await repo.CommitAsync("HEAD");
        await repo.GitAsync("reset", "--hard", "HEAD");
        GitMaterialization checkout = await BindAsync(repo);

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new MsBuildResourceReader(checkout).ReadAsync(Project, Release,
                context.CancellationToken));

        Assert.Contains("Native query (dotnet msbuild) failed: Exited, exit 1.", error.Message);
        Assert.Contains("missing-private-detail.props", error.Message);
    }

    [TestMethod]
    public async Task EvaluationHonorsCancellation()
    {
        // Cancellation is observed before the native query; the query must never start.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.Cancel();
        bool queried = false;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new MsBuildResourceReader(sharedCheckout!, (_, _) =>
            {
                queried = true;
                return Task.FromResult("");
            }).ReadAsync(Project, Release, cancellation.Token));
        Assert.IsFalse(queried);
    }

    private Task<GitFixture> FixtureAsync() => CommitAsync(context.CancellationToken);

    private static async Task<GitFixture> CommitAsync(CancellationToken token)
    {
        GitFixture repo = await GitFixture.CreateAsync(token);
        try
        {
            // The committed root SDK selection, so native rows run the pinned SDK on any host.
            await repo.SetAsync("global.json", RepositoryToolSelection.GlobalJson());
            await repo.SetAsync(Project, "<Project />");
            await repo.SetAsync("shared/Version 雪.json", "{}");
            await repo.SetAsync("shared/executable.txt", "executable resource", "100755");
            await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", "HEAD");
            return repo;
        }
        catch
        {
            repo.Dispose();
            throw;
        }
    }

    private Task<GitMaterialization> BindAsync(GitFixture repo) =>
        BindAsync(repo, context.CancellationToken);

    private static async Task<GitMaterialization> BindAsync(GitFixture repo,
        CancellationToken token) =>
        await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync("HEAD", token), token);

    private static string Output(GitMaterialization checkout, MsBuildDimension dimension,
        params string[] resources) => JsonSerializer.Serialize(new
        {
            Properties = new
            {
                MSBuildProjectFullPath = Path.Combine(checkout.Root, Project),
                dimension.Configuration,
                dimension.TargetFramework,
                dimension.RuntimeIdentifier,
                Unrelated = 42,
            },
            Items = new
            {
                EmbeddedResource = resources.Select(path => new
                {
                    FullPath = Path.Combine(checkout.Root, path),
                    Identity = "opaque native declaration",
                    DefiningProjectFullPath = "opaque native import coordinate",
                }).ToArray(),
                Unrelated = true,
            },
        });

}
