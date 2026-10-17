using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WorkflowDelivery.Tests;

[TestClass]
public static class RunnerCommandFiles
{
    // Hosted runners give the test process their own workflow command files. Caller code
    // under test appends to GITHUB_OUTPUT, so parallel tests would write the CI step's real
    // outputs and contend for that file. Tests that need a command file set their own.
    [AssemblyInitialize]
    public static void Detach(TestContext context)
    {
        _ = context;
        foreach (string name in new[] { "GITHUB_OUTPUT", "GITHUB_ENV", "GITHUB_PATH",
                     "GITHUB_STATE", "GITHUB_STEP_SUMMARY" })
            Environment.SetEnvironmentVariable(name, null);
    }
}
