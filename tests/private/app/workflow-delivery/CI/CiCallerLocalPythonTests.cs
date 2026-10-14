using WorkflowDelivery.CI.Callers;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
[DoNotParallelize]
public sealed class CiCallerLocalPythonTests
{
    [TestMethod]
    public void NativeToolDataKeepsOptionEnvironmentAndOperatorDirectoryPrecedence()
    {
        string? originalMise = Environment.GetEnvironmentVariable("MISE_DATA_DIR");
        string? originalShared = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "operator"));
        try
        {
            Environment.SetEnvironmentVariable("MISE_DATA_DIR", "native-tool-data");
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", "shared-data");
            Assert.AreEqual(Path.Combine(root, "explicit"),
                LocalPythonCiCaller.ToolData("explicit", root));
            Assert.AreEqual(Path.Combine(root, "native-tool-data"),
                LocalPythonCiCaller.ToolData(null, root));
            Environment.SetEnvironmentVariable("MISE_DATA_DIR", null);
            Assert.AreEqual(Path.Combine(root, "shared-data", "mise"),
                LocalPythonCiCaller.ToolData(null, root));
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Assert.AreEqual(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".local", "share", "mise"),
                LocalPythonCiCaller.ToolData(null, root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MISE_DATA_DIR", originalMise);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalShared);
        }
    }
}
