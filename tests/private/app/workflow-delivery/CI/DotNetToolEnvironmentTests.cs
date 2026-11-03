using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetToolEnvironmentTests(TestContext context)
{
    private const string Extensions = ".EXE;.CMD";

    [TestMethod]
    public async Task DeclaredToolsKeepOnlyTheirValidatedDirectories()
    {
        using var layout = new Layout();
        string path = await DotNetToolEnvironment.PathAsync(new()
        { ["node"] = "24.1.0", ["npm"] = "bundled" }, layout.Root, layout.SearchPath, Extensions,
            layout.ExecuteAsync, context.CancellationToken);
        CollectionAssert.AreEqual(new[] { layout.Node, layout.Plain },
            path.Split(Path.PathSeparator));
        Assert.AreEqual(1, layout.Versions);
    }

    [TestMethod]
    public async Task NoneGroupRemovesEveryExternalToolWithoutLaunchingOne()
    {
        using var layout = new Layout();
        string path = await DotNetToolEnvironment.PathAsync([], layout.Root, layout.SearchPath,
            Extensions, layout.ExecuteAsync, context.CancellationToken);
        Assert.AreEqual(layout.Plain, path);
        Assert.AreEqual(0, layout.Versions);
    }

    [TestMethod]
    [DataRow("python", "3.13.2", "actual 'Python 3.13.1'")]
    [DataRow("node", "24.2.0", "actual 'v24.1.0'")]
    [DataRow("npm", "bundled", "actual '")]
    [DataRow("missing", "", "actual 'missing'")]
    public async Task DifferentOrMissingToolsFailBeforeTheBuild(string tool, string version,
        string actual)
    {
        using var layout = new Layout();
        Dictionary<string, string> tools = tool switch
        {
            "npm" => new() { ["node"] = "24.1.0", ["npm"] = version },
            "missing" => new() { ["node"] = "24.1.0" },
            _ => new() { [tool] = version },
        };
        if (tool == "npm") File.Move(Layout.Tool(layout.Node, "npm"),
            Layout.Tool(layout.Shadow, "npm"));
        if (tool == "missing") File.Delete(Layout.Tool(layout.Node, "node"));
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => DotNetToolEnvironment.PathAsync(tools, layout.Root, layout.SearchPath, Extensions,
                layout.ExecuteAsync, context.CancellationToken));
        Assert.Contains("External tool " + (tool == "missing" ? "node" : tool), error.Message);
        Assert.Contains(tool == "npm" ? actual + layout.Shadow + "'" : actual, error.Message);
    }

    [TestMethod]
    public async Task ScopedCommandsReceiveTheGroupPathUnlessTheyOwnIt()
    {
        var seen = new List<NativeCommand>();
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> scoped =
            DotNetToolEnvironment.Scoped("group", (command, _) =>
            {
                seen.Add(command);
                return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0, "",
                    "", 0, null));
            });
        await scoped(new("dotnet", ".", [], 1, new Dictionary<string, string?>
        { ["A"] = "1" }), context.CancellationToken);
        await scoped(new("dotnet", ".", [], 1, new Dictionary<string, string?>
        { ["Path"] = "own" }), context.CancellationToken);
        await scoped(new("dotnet", ".", [], 1, InheritEnvironment: false),
            context.CancellationToken);
        Assert.AreEqual("group", seen[0].Environment!["PATH"]);
        Assert.AreEqual("1", seen[0].Environment!["A"]);
        Assert.AreEqual("own", seen[1].Environment!["Path"]);
        Assert.IsNull(seen[2].Environment);
    }

    private sealed class Layout : IDisposable
    {
        internal Layout()
        {
            Root = Directory.CreateTempSubdirectory("wd-tools-").FullName;
            Python = Directory.CreateDirectory(Path.Combine(Root, "python")).FullName;
            Node = Directory.CreateDirectory(Path.Combine(Root, "node")).FullName;
            Shadow = Directory.CreateDirectory(Path.Combine(Root, "shadow")).FullName;
            Plain = Directory.CreateDirectory(Path.Combine(Root, "plain")).FullName;
            File.WriteAllText(Tool(Python, "python"), "");
            File.WriteAllText(Tool(Node, "node"), "");
            File.WriteAllText(Tool(Node, "npm"), "");
            File.WriteAllText(Tool(Shadow, "python3"), "");
            File.WriteAllText(Path.Combine(Plain, "dotnet.txt"), "");
            SearchPath = string.Join(System.IO.Path.PathSeparator, Python, Node, Shadow, Plain);
        }

        internal string Root { get; }
        internal string Python { get; }
        internal string Node { get; }
        internal string Shadow { get; }
        internal string Plain { get; }
        internal string SearchPath { get; }
        internal int Versions { get; private set; }

        internal static string Tool(string directory, string name) => System.IO.Path.Combine(
            directory, name + (OperatingSystem.IsWindows() ? name == "npm" ? ".cmd" : ".exe"
                : ""));

        internal Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            Versions++;
            Assert.AreEqual("--version", command.Arguments.Single());
            string output = System.IO.Path.GetFileNameWithoutExtension(command.Executable) ==
                "python" ? "Python 3.13.1\n" : "v24.1.0\n";
            return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0, output,
                "", 0, null));
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
