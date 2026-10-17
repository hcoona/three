using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WorkflowDelivery.Tests;

[TestClass]
public static class RunnerCommandFiles
{
    // The test process inherits the CI step's workflow command files. Caller code under
    // test appends to GITHUB_OUTPUT, so parallel tests would write the step's real outputs
    // and contend for that file. GITHUB_ENV/PATH stay: native NBGV runs detect cloud builds
    // from them. Tests that need an output file set their own.
    [AssemblyInitialize]
    public static void Detach(TestContext context)
    {
        _ = context;
        foreach (string name in new[] { "GITHUB_OUTPUT", "GITHUB_STATE", "GITHUB_STEP_SUMMARY" })
            Environment.SetEnvironmentVariable(name, null);
    }
}
