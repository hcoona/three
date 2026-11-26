using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ScratchRootsTests
{
    private const string FreshScratch = "Expected fresh external scratch.";

    [TestMethod]
    public void RequireFreshExternalScratchAcceptsAnExistingEmptySibling()
    {
        using var root = new Sandbox();
        string scratch = Directory.CreateDirectory(Path.Combine(root.Directory,
            "checkout-scratch")).FullName;

        var paths = OwnedPath.RequireFreshExternalScratch(
            root.Checkout + Path.DirectorySeparatorChar, scratch, FreshScratch);

        Assert.AreEqual((root.Checkout, scratch), paths);
        Assert.IsEmpty(Directory.GetFileSystemEntries(scratch));
        Assert.AreEqual(paths,
            OwnedPath.RequireFreshExternalScratch(root.Checkout, scratch, FreshScratch));
    }

    [TestMethod]
    public void RequireFreshExternalScratchRejectsOverlapAndInvalidPathsWithoutCreatingThem()
    {
        using var root = new Sandbox();
        string nested = Path.Combine(root.Checkout, "nested");
        string sibling = Path.Combine(root.Directory, "sibling");

        Reject(root.Checkout, root.Directory);
        Reject(root.Checkout, root.Checkout);
        Reject(root.Checkout, nested);
        Reject(root.Checkout, "relative-scratch");
        Reject(root.Checkout, sibling);
        Reject("relative-checkout", sibling);
        Reject(Path.Combine(root.Directory, "missing-checkout"), sibling);
        Assert.IsFalse(Directory.Exists(nested));
        Assert.IsFalse(Directory.Exists(sibling));
    }

    [TestMethod]
    public void RequireFreshExternalScratchPreservesExistingData()
    {
        using var root = new Sandbox();
        string scratch = Directory.CreateDirectory(Path.Combine(root.Directory, "scratch"))
            .FullName;
        string sentinel = Path.Combine(scratch, "sentinel");
        File.WriteAllText(sentinel, "existing data");

        Reject(root.Checkout, scratch);
        Reject(root.Checkout, sentinel);

        Assert.AreEqual("existing data", File.ReadAllText(sentinel));
    }

    private static void Reject(string checkout, string scratch) =>
        Assert.ThrowsExactly<InvalidDataException>(() =>
            OwnedPath.RequireFreshExternalScratch(checkout, scratch, FreshScratch));

    private sealed class Sandbox : IDisposable
    {
        internal Sandbox()
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("wdt-owned-path-").FullName;
            Checkout = System.IO.Directory.CreateDirectory(Path.Combine(Directory, "checkout"))
                .FullName;
        }

        internal string Directory { get; }
        internal string Checkout { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

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
