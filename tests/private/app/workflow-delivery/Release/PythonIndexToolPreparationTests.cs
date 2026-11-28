using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonIndexToolPreparationTests(TestContext context)
{
    [TestMethod]
    public async Task FreshOwnedStateInstallsHashedRegistryWheelsWithoutInheritedCredentials()
    {
        using var f = new Fixture();
        var commands = new List<NativeCommand>();
        var result = await PythonIndexToolPreparation.PrepareAsync(f.Request, (command, token) =>
        {
            Assert.IsTrue(token.CanBeCanceled);
            Assert.IsFalse(token.IsCancellationRequested);
            commands.Add(command);
            return Task.FromResult(Fixture.Success);
        }, context.CancellationToken);
        Assert.IsEmpty(result.Errors);
        CollectionAssert.AreEqual((string[])["venv", "tools"], result.Commands.Select(v =>
            v.Stage).ToArray());
        Assert.HasCount(2, commands);
        Assert.AreEqual(Path.Combine(f.Request.Scratch, "venv", OperatingSystem.IsWindows() ?
            "Scripts/python.exe" : "bin/python"), result.Interpreter);
        NativeCommand install = commands[1];
        Assert.AreEqual(f.Request.Interpreter, Value(commands[0], "--python"));
        Assert.AreEqual(result.Interpreter, Value(install, "--python"));
        Assert.AreEqual(f.Request.Requirements, Value(install, "--requirements"));
        Assert.AreEqual("https://pypi.org/simple/", Value(install, "--index-url"));
        Assert.AreEqual(":all:", Value(install, "--only-binary"));
        Assert.AreEqual("disabled", Value(install, "--keyring-provider"));
        Assert.Contains("--require-hashes", install.Arguments);
        Assert.Contains("--no-cache", install.Arguments);
        Assert.Contains("--no-project", commands[0].Arguments);
        Assert.DoesNotContain("--extra-index-url", install.Arguments);
        foreach (NativeCommand command in commands)
        {
            Assert.AreEqual(f.Request.Uv, command.Executable);
            Assert.AreEqual(f.Request.Scratch, command.Directory);
            Assert.AreEqual(180, command.DeadlineSeconds);
            Assert.IsTrue(command.CloseStandardInput);
            Assert.IsFalse(command.InheritEnvironment);
            Assert.Contains("--no-config", command.Arguments);
            Assert.IsNotNull(command.Environment);
            CollectionAssert.AreEquivalent((string[])["PATH", "SystemRoot", "HOME", "USERPROFILE",
                "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "UV_CACHE_DIR", "TMPDIR",
                "TMP", "TEMP", "UV_PYTHON_DOWNLOADS"], command.Environment.Keys.ToArray());
            Assert.AreEqual("never", command.Environment["UV_PYTHON_DOWNLOADS"]);
            foreach (string name in new[] { "ACTIONS_ID_TOKEN_REQUEST_URL",
                "ACTIONS_ID_TOKEN_REQUEST_TOKEN", "GITHUB_ACTIONS", "UV_INDEX", "UV_CONFIG_FILE",
                "PYTHONPATH", "PYTHON_KEYRING_BACKEND" })
                Assert.IsFalse(command.Environment.ContainsKey(name), name);
            foreach (string name in new[] { "HOME", "USERPROFILE", "XDG_CONFIG_HOME",
                "XDG_DATA_HOME", "XDG_CACHE_HOME", "UV_CACHE_DIR", "TMPDIR", "TMP", "TEMP" })
            {
                string owned = command.Environment[name]!;
                Assert.AreEqual(f.Request.Scratch, Path.GetDirectoryName(owned));
                Assert.IsTrue(Directory.Exists(owned));
                Assert.IsEmpty(Directory.GetFileSystemEntries(owned));
            }
        }
        CollectionAssert.AreEqual(commands.ToArray(), result.Commands.Select(v =>
            v.Command).ToArray());
        Assert.IsTrue(result.Commands.All(v => v.Result == Fixture.Success));
        Assert.AreEqual(f.Recipe, File.ReadAllText(f.Request.Requirements));
        int replay = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonIndexToolPreparation.PrepareAsync(f.Request,
            (_, _) => { replay++; return Task.FromResult(Fixture.Success); },
                context.CancellationToken));
        Assert.AreEqual(0, replay);
    }

    [TestMethod]
    public async Task RelativePathsAreRejectedBeforeNativeExecution()
    {
        using var f = new Fixture();
        await RejectAsync(f.Request with { Uv = "uv" });
        await RejectAsync(f.Request with { Interpreter = "python" });
        await RejectAsync(f.Request with { Requirements = "recipe.txt" });
        await RejectAsync(f.Request with { Scratch = "scratch" });
    }

    [TestMethod]
    public async Task MissingRequirementsCannotStartPreparation()
    {
        using var f = new Fixture();
        File.Delete(f.Request.Requirements);
        await RejectAsync(f.Request);
    }

    [TestMethod]
    public async Task MissingScratchCannotStartPreparation()
    {
        using var f = new Fixture();
        Directory.Delete(f.Request.Scratch);
        await RejectAsync(f.Request);
    }

    [TestMethod]
    public async Task PopulatedScratchIsRejectedWithoutReplacingExistingData()
    {
        using var f = new Fixture();
        string sentinel = Path.Combine(f.Request.Scratch, "unowned");
        File.WriteAllText(sentinel, "previous state");

        await RejectAsync(f.Request);

        Assert.AreEqual("previous state", File.ReadAllText(sentinel));
    }

    private Task<InvalidDataException> RejectAsync(PythonIndexToolRequest request) =>
        Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonIndexToolPreparation.PrepareAsync(request,
                (_, _) => throw new AssertFailedException("Invalid input reached native work."),
                context.CancellationToken));

    [TestMethod]
    [DataRow(0, "Exited", 17, "native failure")]
    [DataRow(1, "StartFailed", null, "start failed")]
    [DataRow(1, "TimedOut", null, "deadline reached")]
    [DataRow(1, "Cancelled", null, "cancelled")]
    [DataRow(1, "Exited", 0, "cleanup failed")]
    public async Task NativeFailureStopsAndRetainsActualResult(int phase, string termination, int?
        exit, string error)
    {
        using var f = new Fixture();
        var failureResult = new NativeCommandResult(Enum.Parse<NativeTermination>(termination),
            exit, "actual stdout", "actual stderr", 1.25, error);
        int calls = 0;
        var result = await PythonIndexToolPreparation.PrepareAsync(f.Request,
            (_, _) => Task.FromResult(calls++ == phase ? failureResult : Fixture.Success),
                context.CancellationToken);
        Assert.AreEqual(phase + 1, calls);
        Assert.HasCount(phase + 1, result.Commands);
        Assert.AreSame(failureResult, result.Commands[^1].Result);
        Assert.AreEqual(phase == 0 ? "venv" : "tools", result.Commands[^1].Stage);
        Assert.Contains("did not succeed", Assert.ContainsSingle(result.Errors));
        Assert.AreEqual("actual stdout", result.Commands[^1].Result.Stdout);
        Assert.AreEqual("actual stderr", result.Commands[^1].Result.Stderr);
        Assert.AreEqual(error, result.Commands[^1].Result.Error);
        Assert.AreEqual(1.25, result.Commands[^1].Result.DurationSeconds);
    }

    [TestMethod]
    [DataRow("before")]
    [DataRow("between")]
    [DataRow("during")]
    public async Task CancellationStopsWithoutSecondCommand(string when)
    {
        using var f = new Fixture();
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (when == "before") cancellation.Cancel();
        int calls = 0;
        var result = await PythonIndexToolPreparation.PrepareAsync(f.Request, (_, token) =>
        {
            calls++;
            Assert.IsTrue(token.CanBeCanceled);
            cancellation.Cancel();
            if (when == "during") token.ThrowIfCancellationRequested();
            return Task.FromResult(Fixture.Success);
        }, cancellation.Token);
        Assert.AreEqual(when == "before" ? 0 : 1, calls);
        Assert.HasCount(when == "between" ? 1 : 0, result.Commands);
        Assert.Contains("interrupted", Assert.ContainsSingle(result.Errors));
        if (when == "between") Assert.AreSame(Fixture.Success, result.Commands[0].Result);
        Assert.AreEqual(f.Recipe, File.ReadAllText(f.Request.Requirements));
    }

    private static string Value(NativeCommand command, string option)
    {
        int index = Array.IndexOf(command.Arguments, option);
        Assert.IsTrue(index >= 0 && index + 1 < command.Arguments.Length, "Missing option " +
            option);
        return command.Arguments[index + 1];
    }
    private sealed class Fixture : IDisposable
    {
        internal static readonly NativeCommandResult Success = new(NativeTermination.Exited, 0,
            "controlled result", "", 0.125, null);
        private string Root { get; } =
            Directory.CreateTempSubdirectory("python-index-tools-").FullName;
        internal string Recipe { get; } = "packaging==25.0 --hash=sha256:" + new string('a', 64) +
            "\n";
        internal PythonIndexToolRequest Request { get; }
        internal Fixture()
        {
            string recipe = Path.Combine(Root, "requirements.txt");
            File.WriteAllText(recipe, Recipe);
            Request = new(Path.Combine(Root, "uv"), Path.Combine(Root, "python"), recipe,
                Directory.CreateDirectory(Path.Combine(Root, "scratch")).FullName);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
