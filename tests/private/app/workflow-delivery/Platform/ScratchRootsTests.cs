using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ScratchRootsTests
{
    [TestMethod]
    public void SweepNeverFollowsALinkedStaleRoot()
    {
        string parent = Directory.CreateTempSubdirectory("wdt-sweep-test-").FullName;
        try
        {
            string target = Directory.CreateDirectory(Path.Combine(parent, "target")).FullName;
            string kept = Path.Combine(target, "kept.txt");
            File.WriteAllText(kept, "unrelated");
            File.SetAttributes(kept, FileAttributes.ReadOnly);
            string link = Path.Combine(parent, ScratchRoots.Prefix + "999999-linked");
            string dead = Directory.CreateDirectory(
                Path.Combine(parent, ScratchRoots.Prefix + "999998-dead", "nested")).FullName;
            try
            {
                Directory.CreateSymbolicLink(link, target);
                Directory.CreateSymbolicLink(Path.Combine(dead, "inner"), target);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Assert.Inconclusive("This host cannot create directory symbolic links.");
            }

            ScratchRoots.Sweep(parent, _ => false);

            Assert.IsTrue(File.Exists(kept));
            Assert.IsTrue(File.GetAttributes(kept).HasFlag(FileAttributes.ReadOnly));
            Assert.IsNotNull(new DirectoryInfo(link).LinkTarget);
            Assert.IsFalse(Directory.Exists(Path.Combine(parent,
                ScratchRoots.Prefix + "999998-dead")));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(parent, "*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                     }))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(parent, recursive: true);
        }
    }
}
