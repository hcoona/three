using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Tests.Ecosystems;

[TestClass]
public sealed class DotNetPackageResultsTests(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResponseLimitAppliesToTheParsedTarget(bool linked)
    {
        string root = Directory.CreateTempSubdirectory("dotnet-package-results-").FullName;
        try
        {
            string target = Path.Combine(root, "result.json");
            await using (FileStream file = File.Create(target))
                file.SetLength(32 * 1024 * 1024 + 1);
            string path = target;
            if (linked)
            {
                path = Path.Combine(root, "linked.json");
                try { File.CreateSymbolicLink(path, target); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Assert.Inconclusive("The platform cannot create a file link.");
                }
            }

            InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => DotNetPackageResults.ResponseAsync(path, context.CancellationToken));
            Assert.Contains("exceeds its limit", failure.Message);
        }
        finally { Directory.Delete(root, true); }
    }
}
