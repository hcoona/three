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
        CollectionAssert.AreEqual(new[] { "msbuild", Path.Combine(checkout.Root, Project),
            "-nologo", "-noAutoResponse",
            "-getProperty:MSBuildProjectFullPath,Configuration,TargetFramework,TargetFrameworks,"
                + "RuntimeIdentifier,RuntimeIdentifiers",
            "-getItem:EmbeddedResource", "-property:Configuration=Debug" }, arguments);
        Assert.AreEqual(dimension, result.Dimension);
        CollectionAssert.AreEqual(VersionOnlyInputs, result.Inputs);
    }

    [TestMethod]
    [DataRow("frameworks")]
    [DataRow("rids")]
    [DataRow("missing-frameworks")]
    [DataRow("empty-framework")]
    [DataRow("framework-list")]
    [DataRow("rid-list")]
    public async Task DefaultQueryRejectsUnsupportedRequiredShapes(string defect)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        JsonNode native = JsonNode.Parse(Output(checkout, new("Debug", "net10.0")))!;
        JsonObject properties = native["Properties"]!.AsObject();
        properties["TargetFrameworks"] = defect == "frameworks" ? "net9.0;net10.0" : "";
        properties["RuntimeIdentifiers"] = defect == "rids" ? "linux-x64;win-x64" : "";
        if (defect == "missing-frameworks") properties.Remove("TargetFrameworks");
        if (defect == "empty-framework") properties["TargetFramework"] = "";
        if (defect == "framework-list") properties["TargetFramework"] = "net9.0;net10.0";
        if (defect == "rid-list") properties["RuntimeIdentifier"] = "linux-x64;win-x64";
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

        CollectionAssert.AreEqual(new[] { "msbuild", Path.Combine(checkout.Root, Project),
            "-nologo", "-noAutoResponse",
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
    [DataRow("root-array")]
    [DataRow("properties-missing")]
    [DataRow("properties-null")]
    [DataRow("items-missing")]
    [DataRow("items-array")]
    [DataRow("resources-missing")]
    [DataRow("resources-null")]
    [DataRow("resources-object")]
    [DataRow("item-null")]
    [DataRow("fullpath-missing")]
    [DataRow("fullpath-null")]
    [DataRow("fullpath-number")]
    [DataRow("fullpath-empty")]
    [DataRow("project-missing")]
    [DataRow("project-null")]
    [DataRow("configuration-null")]
    [DataRow("framework-empty")]
    [DataRow("rid-missing")]
    [DataRow("rid-null")]
    [DataRow("rid-number")]
    [DataRow("duplicate-properties")]
    [DataRow("duplicate-items")]
    [DataRow("duplicate-project")]
    [DataRow("duplicate-resources")]
    [DataRow("duplicate-fullpath")]
    public async Task ReadRejectsInvalidRequiredNativeResults(string defect)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        string output = Defective(Output(checkout, Release, "shared/Version 雪.json"), defect);
        var reader = new MsBuildResourceReader(checkout, (_, _) => Task.FromResult(output));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(Project,
            Release, context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadRejectsInvalidJson()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        var reader = new MsBuildResourceReader(checkout, (_, _) => Task.FromResult("{"));

        await Assert.ThrowsAsync<JsonException>(() => reader.ReadAsync(Project, Release,
            context.CancellationToken));
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("configuration")]
    [DataRow("framework")]
    [DataRow("rid")]
    public async Task ReadRejectsConflictingNativeIdentityAndDimensions(string field)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        JsonNode root = JsonNode.Parse(Output(checkout, Release))!;
        JsonNode properties = root["Properties"]!;
        properties[field switch
        {
            "project" => "MSBuildProjectFullPath",
            "configuration" => "Configuration",
            "framework" => "TargetFramework",
            _ => "RuntimeIdentifier",
        }] = field == "project" ? Path.Combine(checkout.Root, "shared/executable.txt") : "other";
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            Task.FromResult(root.ToJsonString()));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(Project, Release, context.CancellationToken));

        Assert.Contains("different project or dimension", error.Message);
    }

    [TestMethod]
    [DataRow("relative")]
    [DataRow("outside")]
    [DataRow("sibling-root")]
    [DataRow("untracked")]
    [DataRow("missing")]
    [DataRow("directory")]
    public async Task ReadRejectsUnavailableResourceCoordinates(string defect)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        if (defect == "untracked")
            await File.WriteAllTextAsync(Path.Combine(checkout.Root, "extra.txt"), "untracked",
                context.CancellationToken);
        if (defect == "missing")
            File.Delete(Path.Combine(checkout.Root, "shared/Version 雪.json"));
        string native = defect switch
        {
            "relative" => "shared/Version 雪.json",
            "outside" => Path.GetFullPath("../outside.txt", checkout.Root),
            "sibling-root" => checkout.Root + "-sibling/input.txt",
            "untracked" => Path.Combine(checkout.Root, "extra.txt"),
            "missing" => Path.Combine(checkout.Root, "shared/Version 雪.json"),
            _ => Path.Combine(checkout.Root, "shared"),
        };
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
    [DataRow("project-parent")]
    [DataRow("project-backslash")]
    [DataRow("project-uncommitted")]
    [DataRow("configuration-empty")]
    [DataRow("configuration-list")]
    [DataRow("configuration-quote")]
    [DataRow("framework-empty")]
    [DataRow("framework-escape")]
    [DataRow("rid-list")]
    [DataRow("rid-control")]
    public async Task ReadRejectsUnsupportedRequestsBeforeNativeQuery(string defect)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        string project = defect switch
        {
            "project-parent" => "../Control.csproj",
            "project-backslash" => "tests\\Control\\Control.csproj",
            "project-uncommitted" => "extra.csproj",
            _ => Project,
        };
        MsBuildDimension dimension = defect switch
        {
            "configuration-empty" => Release with { Configuration = "" },
            "configuration-list" => Release with { Configuration = "Release;Other=1" },
            "configuration-quote" => Release with { Configuration = "Release\"" },
            "framework-empty" => Release with { TargetFramework = "" },
            "framework-escape" => Release with { TargetFramework = "net10%2e0" },
            "rid-list" => Release with { RuntimeIdentifier = "linux-x64,Other=1" },
            "rid-control" => Release with { RuntimeIdentifier = "linux-x64\n" },
            _ => Release,
        };
        var reader = new MsBuildResourceReader(checkout, (_, _) =>
            throw new AssertFailedException("Unsupported requests must not invoke MSBuild."));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(project,
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

        Assert.Contains("Native query failed with exit code", error.Message);
        Assert.DoesNotContain("missing-private-detail", error.Message);
    }

    [TestMethod]
    public async Task NativeEvaluationHonorsCancellation()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new MsBuildResourceReader(checkout).ReadAsync(Project, Release, cancellation.Token));
    }

    private async Task<GitFixture> FixtureAsync()
    {
        GitFixture repo = await GitFixture.CreateAsync(context.CancellationToken);
        try
        {
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

    private async Task<GitMaterialization> BindAsync(GitFixture repo) =>
        await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync("HEAD", context.CancellationToken),
            context.CancellationToken);

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

    private static string Defective(string output, string defect)
    {
        JsonNode root = JsonNode.Parse(output)!;
        JsonNode properties = root["Properties"]!;
        JsonNode items = root["Items"]!;
        JsonNode resources = items["EmbeddedResource"]!;
        JsonNode item = resources[0]!;
        switch (defect)
        {
            case "root-array": return "[]";
            case "properties-missing": root.AsObject().Remove("Properties"); break;
            case "properties-null": root["Properties"] = null; break;
            case "items-missing": root.AsObject().Remove("Items"); break;
            case "items-array": root["Items"] = new JsonArray(); break;
            case "resources-missing": items.AsObject().Remove("EmbeddedResource"); break;
            case "resources-null": items["EmbeddedResource"] = null; break;
            case "resources-object": items["EmbeddedResource"] = new JsonObject(); break;
            case "item-null": resources[0] = null; break;
            case "fullpath-missing": item.AsObject().Remove("FullPath"); break;
            case "fullpath-null": item["FullPath"] = null; break;
            case "fullpath-number": item["FullPath"] = 42; break;
            case "fullpath-empty": item["FullPath"] = ""; break;
            case "project-missing": properties.AsObject().Remove("MSBuildProjectFullPath"); break;
            case "project-null": properties["MSBuildProjectFullPath"] = null; break;
            case "configuration-null": properties["Configuration"] = null; break;
            case "framework-empty": properties["TargetFramework"] = ""; break;
            case "rid-missing": properties.AsObject().Remove("RuntimeIdentifier"); break;
            case "rid-null": properties["RuntimeIdentifier"] = null; break;
            case "rid-number": properties["RuntimeIdentifier"] = 42; break;
            case "duplicate-properties": return "{\"Properties\":{}," + output[1..];
            case "duplicate-items": return "{\"Items\":{}," + output[1..];
            case "duplicate-project":
                return output.Replace("\"MSBuildProjectFullPath\":",
                "\"MSBuildProjectFullPath\":null,\"MSBuildProjectFullPath\":",
                StringComparison.Ordinal);
            case "duplicate-resources":
                return output.Replace("\"EmbeddedResource\":",
                "\"EmbeddedResource\":[],\"EmbeddedResource\":", StringComparison.Ordinal);
            case "duplicate-fullpath":
                return output.Replace("\"FullPath\":",
                "\"FullPath\":null,\"FullPath\":", StringComparison.Ordinal);
            default: throw new AssertFailedException("Unknown invalid native result case.");
        }
        return root.ToJsonString();
    }
}
