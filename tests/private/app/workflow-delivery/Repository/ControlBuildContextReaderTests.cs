using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlBuildContextReaderTests(TestContext context)
{
    private const string Project = "control/Control.csproj";

    [TestMethod]
    public async Task NativeContextRetainsOperationAndOptionalConfiguration()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        JsonObject native = Output(checkout);
        string editor = Path.Combine(repo.Directory, "control/.editorconfig");
        string external = Path.GetFullPath("external/analyzer.globalconfig");
        native["Items"]!["PotentialEditorConfigFiles"] = new JsonArray(
            new JsonObject { ["FullPath"] = editor }, new JsonObject { ["FullPath"] = editor });
        native["Items"]!["GlobalAnalyzerConfigFiles"] = new JsonArray(
            new JsonObject { ["FullPath"] = external });

        ControlBuildContext result = await Reader(checkout, native).ReadAsync(Project,
            context.CancellationToken);

        Assert.AreEqual(checkout.Revision.Commit, result.Revision);
        Assert.AreEqual(Path.Combine(repo.Directory, Project), result.Project);
        Assert.AreEqual(new("Debug", "net10.0"), result.Dimension);
        Assert.AreEqual("10.0.401", result.SdkVersion);
        Assert.AreEqual(Path.GetFullPath("sdk/10.0.401"), result.SdkDirectory);
        Assert.AreEqual(Path.GetFullPath("dotnet"), result.DotNetDirectory);
        Assert.AreEqual(Path.GetFullPath("packages"), result.PackageDirectory);
        Assert.AreEqual(Path.Combine(repo.Directory, "control/bin/Debug/net10.0"),
            result.OutputDirectory);
        Assert.AreEqual(Path.Combine(repo.Directory, "control/obj/Debug/net10.0"),
            result.IntermediateDirectory);
        Assert.AreEqual(Path.Combine(repo.Directory, "control/obj"),
            result.BaseIntermediateDirectory);
        Assert.AreEqual(Path.Combine(repo.Directory, "control/obj/project.assets.json"),
            result.AssetsFile);
        Assert.AreEqual(Path.Combine(repo.Directory, "control/bin/Debug/net10.0/Control.dll"),
            result.TargetFile);
        Assert.AreEqual(editor, Assert.ContainsSingle(result.EditorConfigCandidates));
        Assert.AreEqual(external, Assert.ContainsSingle(result.GlobalConfigCandidates));
        Assert.AreEqual("true", result.Properties["RestoreLockedMode"]);
        Assert.AreEqual("true", result.Properties["ContinuousIntegrationBuild"]);
        Assert.AreEqual(repo.Directory, result.Properties["MSBuildStartupDirectory"]);
        Assert.AreEqual("true", result.Properties["MSBuildLogVerboseTaskParameters"]);
    }

    [TestMethod]
    public async Task ContextQueryUsesOnlyTheConcreteOperationProperties()
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        string[] arguments = [];
        var reader = new ControlBuildContextReader(checkout, (actual, _) =>
        {
            arguments = actual;
            return Task.FromResult(Output(checkout).ToJsonString());
        });

        await reader.ReadAsync(Project, context.CancellationToken);

        Assert.Contains("-noAutoResponse", arguments);
        Assert.Contains("-property:Configuration=Debug", arguments);
        Assert.Contains("-property:ContinuousIntegrationBuild=true", arguments);
        Assert.Contains("-property:RestoreLockedMode=true", arguments);
        Assert.Contains("-property:NuGetInteractive=false", arguments);
        Assert.Contains("-property:MSBuildLogVerboseTaskParameters=true", arguments);
        Assert.DoesNotContain("-property:TargetFramework=net10.0", arguments);
        Assert.IsEmpty(arguments.Where(a => a.StartsWith("-target:", StringComparison.Ordinal)));
        Assert.IsEmpty(arguments.Where(a => a.StartsWith("-property:RuntimeIdentifier=",
            StringComparison.Ordinal)));
        Assert.Contains(
            "-getItem:ProjectReference,PotentialEditorConfigFiles,GlobalAnalyzerConfigFiles",
            arguments);
    }

    [TestMethod]
    [DataRow("different-project")]
    [DataRow("relative-project")]
    [DataRow("release")]
    [DataRow("empty-tfm")]
    [DataRow("tfm-list")]
    [DataRow("multiple-tfm")]
    [DataRow("rid")]
    [DataRow("multiple-rid")]
    [DataRow("project-reference")]
    [DataRow("not-locked")]
    [DataRow("not-ci")]
    [DataRow("missing-sdk")]
    [DataRow("relative-sdk-root")]
    [DataRow("missing-output")]
    [DataRow("missing-property")]
    [DataRow("null-property")]
    [DataRow("malformed-candidates")]
    [DataRow("relative-candidate")]
    [DataRow("duplicate-property")]
    [DataRow("different-working-directory")]
    [DataRow("missing-task-logging")]
    public async Task UnsupportedContextCannotProduceOperationFacts(string defect)
    {
        using var repo = await FixtureAsync();
        GitMaterialization checkout = await BindAsync(repo);
        JsonObject native = Output(checkout);
        JsonObject properties = native["Properties"]!.AsObject();
        JsonObject items = native["Items"]!.AsObject();
        switch (defect)
        {
            case "different-project":
                properties["MSBuildProjectFullPath"] = Path.GetFullPath("other.csproj"); break;
            case "relative-project": properties["MSBuildProjectFullPath"] = Project; break;
            case "release": properties["Configuration"] = "Release"; break;
            case "empty-tfm": properties["TargetFramework"] = ""; break;
            case "tfm-list": properties["TargetFramework"] = "net9.0;net10.0"; break;
            case "multiple-tfm": properties["TargetFrameworks"] = "net9.0;net10.0"; break;
            case "rid": properties["RuntimeIdentifier"] = "linux-x64"; break;
            case "multiple-rid": properties["RuntimeIdentifiers"] = "linux-x64;win-x64"; break;
            case "project-reference":
                items["ProjectReference"] = new JsonArray("other.csproj"); break;
            case "not-locked": properties["RestoreLockedMode"] = "false"; break;
            case "not-ci": properties["ContinuousIntegrationBuild"] = ""; break;
            case "missing-sdk": properties["NETCoreSdkVersion"] = ""; break;
            case "relative-sdk-root": properties["MSBuildToolsPath"] = "sdk"; break;
            case "missing-output": properties["OutputPath"] = ""; break;
            case "missing-property": properties.Remove("IntermediateOutputPath"); break;
            case "null-property": properties["OutputPath"] = null; break;
            case "malformed-candidates":
                items["PotentialEditorConfigFiles"] = new JsonObject(); break;
            case "relative-candidate":
                items["PotentialEditorConfigFiles"] = new JsonArray(
                new JsonObject { ["FullPath"] = ".editorconfig" }); break;
            case "duplicate-property": break;
            case "different-working-directory":
                properties["MSBuildStartupDirectory"] = Path.GetFullPath("other"); break;
            case "missing-task-logging":
                properties["MSBuildLogVerboseTaskParameters"] = "false"; break;
            default: Assert.Fail("Unknown native context defect."); break;
        }
        string output = native.ToJsonString();
        if (defect == "duplicate-property")
            output = output.Replace("\"Properties\":{",
                "\"Properties\":{\"Configuration\":\"Debug\",",
                StringComparison.Ordinal);
        var reader = new ControlBuildContextReader(checkout, (_, _) => Task.FromResult(output));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(Project,
            context.CancellationToken));
    }

    private async Task<GitFixture> FixtureAsync()
    {
        GitFixture repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(Project, "<Project />");
        await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", "HEAD");
        return repo;
    }

    private async Task<GitMaterialization> BindAsync(GitFixture repo) =>
        await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync("HEAD", context.CancellationToken),
            context.CancellationToken);

    private static ControlBuildContextReader Reader(GitMaterialization checkout,
        JsonObject native) =>
        new(checkout, (_, _) => Task.FromResult(native.ToJsonString()));

    private static JsonObject Output(GitMaterialization checkout) => new()
    {
        ["Properties"] = new JsonObject
        {
            ["MSBuildProjectFullPath"] = Path.Combine(checkout.Root, Project),
            ["Configuration"] = "Debug",
            ["TargetFramework"] = "net10.0",
            ["TargetFrameworks"] = "",
            ["RuntimeIdentifier"] = "",
            ["RuntimeIdentifiers"] = "",
            ["NETCoreSdkVersion"] = "10.0.401",
            ["MSBuildToolsPath"] = Path.GetFullPath("sdk/10.0.401"),
            ["NetCoreRoot"] = Path.GetFullPath("dotnet"),
            ["NuGetPackageRoot"] = Path.GetFullPath("packages"),
            ["OutputPath"] = "bin\\Debug/net10.0/",
            ["IntermediateOutputPath"] = "obj\\Debug/net10.0/",
            ["BaseIntermediateOutputPath"] = "obj\\",
            ["ProjectAssetsFile"] = Path.Combine(checkout.Root, "control/obj/project.assets.json"),
            ["TargetPath"] = Path.Combine(checkout.Root, "control/bin/Debug/net10.0/Control.dll"),
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
            ["MSBuildStartupDirectory"] = checkout.Root,
            ["MSBuildLogVerboseTaskParameters"] = "true",
            ["NuGetInteractive"] = "false",
        },
        ["Items"] = new JsonObject
        {
            ["ProjectReference"] = new JsonArray(),
            ["PotentialEditorConfigFiles"] = new JsonArray(),
            ["GlobalAnalyzerConfigFiles"] = new JsonArray(),
        },
    };
}
