using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WorkflowDelivery.Tests;

[TestClass]
public static class RunnerCommandFiles
{
    private const string ScratchPrefix = "workflow-delivery-tests-";
    private static string? scratch;
    private static string? directory;

    // The test process inherits the CI step's workflow command files. Caller code under test
    // appends to GITHUB_OUTPUT, and native NBGV children append to GITHUB_ENV when
    // GITHUB_ACTIONS is set, so tests would write the step's real files and race for them.
    // Clear the outputs and give the inherited environment/path files a private location;
    // tests that need a specific command file set their own.
    [AssemblyInitialize]
    public static void Detach(TestContext context)
    {
        _ = context;
        RedirectTemporaryFiles();
        foreach (string name in new[] { "GITHUB_OUTPUT", "GITHUB_STATE", "GITHUB_STEP_SUMMARY" })
            Environment.SetEnvironmentVariable(name, null);
        directory = Directory.CreateTempSubdirectory("workflow-runner-files-").FullName;
        foreach ((string name, string file) in new[] { ("GITHUB_ENV", "env"),
                     ("GITHUB_PATH", "path") })
        {
            string path = Path.Combine(directory, file);
            File.WriteAllText(path, "");
            Environment.SetEnvironmentVariable(name, path);
        }
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (scratch is not null && Directory.Exists(scratch))
            Directory.Delete(scratch, recursive: true);
    }

    // Test fixtures and native children (MSBuild, Git, package managers and .NET runtime IPC)
    // use the process temporary directory. Give this run one root outside the checkout, so
    // native ancestor discovery is unaffected, and delete it when the assembly finishes. A
    // killed run leaves only that root; the next run removes roots whose process has exited.
    private static void RedirectTemporaryFiles()
    {
        string parent = Path.GetTempPath();
        foreach (string stale in Directory.EnumerateDirectories(parent, ScratchPrefix + "*"))
        {
            string[] parts = Path.GetFileName(stale)[ScratchPrefix.Length..].Split('-');
            if (!int.TryParse(parts[0], out int owner) || Running(owner)) continue;
            try { Directory.Delete(stale, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        scratch = Directory.CreateTempSubdirectory(ScratchPrefix + Environment.ProcessId + "-")
            .FullName;
        foreach (string name in new[] { "TMPDIR", "TMP", "TEMP" })
            Environment.SetEnvironmentVariable(name, scratch);
    }

    private static bool Running(int process)
    {
        try
        {
            using Process found = Process.GetProcessById(process);
            return !found.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
