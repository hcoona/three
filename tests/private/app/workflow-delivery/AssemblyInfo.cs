using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WorkflowDelivery.Tests;

[TestClass]
public static class RunnerCommandFiles
{
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
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
