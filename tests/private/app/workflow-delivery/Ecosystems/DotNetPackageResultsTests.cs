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

    [TestMethod]
    public void LinkedOriginalIsComparedByItsTargetBytes()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-linked-original-").FullName;
        try
        {
            string target = Path.Combine(root, "original.nupkg");
            File.WriteAllBytes(target, [1, 2, 3, 4, 5]);
            string path = Path.Combine(root, "linked.nupkg");
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Assert.Inconclusive("The platform cannot create a file link.");
            }

            DotNetPackageArchive.RequireOriginal(new("unit", "build", "output", "role",
                "nuget-package", path, 5, DotNetPackageArchive.Digest(target)));
        }
        finally { Directory.Delete(root, true); }
    }
}
