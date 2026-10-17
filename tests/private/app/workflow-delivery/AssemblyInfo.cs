using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WorkflowDelivery.Tests;

[TestClass]
public static class RunnerCommandFiles
{
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

    // Best effort: a child still holding a handle or a read-only file leaves the root for the
    // next run's sweep rather than failing the run.
    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (scratch is not null) ScratchRoots.TryDelete(scratch);
    }

    // Test fixtures and native children (MSBuild, Git, package managers and .NET runtime IPC)
    // use the process temporary directory. Give this run one root outside the checkout, so
    // native ancestor discovery is unaffected, and delete it when the assembly finishes. A
    // killed run leaves only that root; the next run removes roots whose process has exited.
    private static void RedirectTemporaryFiles()
    {
        string parent = Path.GetTempPath();
        ScratchRoots.Sweep(parent, ScratchRoots.Running);
        // The native random suffix keeps another user from pre-creating the root.
        scratch = Directory.CreateTempSubdirectory(
            ScratchRoots.Prefix + Environment.ProcessId + "-").FullName;
        foreach (string name in new[] { "TMPDIR", "TMP", "TEMP" })
            Environment.SetEnvironmentVariable(name, scratch);
    }
}

internal static class ScratchRoots
{
    // Short on purpose (wdt-<pid>-<random>): Windows native fixtures nest deep repository
    // paths under this root and must stay within the default 260-character path limit.
    internal const string Prefix = "wdt-";

    private static readonly EnumerationOptions NoLinks = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    // Removes roots left by exited runs. Assumes one PID namespace per temporary directory;
    // a root shared across PID namespaces could make a live foreign run look exited.
    internal static void Sweep(string parent, Func<int, bool> running)
    {
        foreach (string stale in Directory.EnumerateDirectories(parent, Prefix + "*"))
        {
            string[] parts = Path.GetFileName(stale)[Prefix.Length..].Split('-', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out int owner) || running(owner))
                continue;
            TryDelete(stale);
        }
    }

    // Deletes a real, writable root without following links: a symlinked or junctioned
    // root is left alone, linked entries inside it are not traversed for attribute changes,
    // and Directory.Delete removes reparse points themselves rather than recursing into them.
    internal static void TryDelete(string path)
    {
        try
        {
            var root = new DirectoryInfo(path);
            if (!root.Exists || root.LinkTarget is not null ||
                root.Attributes.HasFlag(FileAttributes.ReparsePoint) || !Writable(path))
                return;
            foreach (string file in Directory.EnumerateFiles(path, "*", NoLinks))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static bool Running(int process)
    {
        try
        {
            using Process found = Process.GetProcessById(process);
            return !found.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // Require the root to be writable by this user; a foreign root (for example 0700 on Unix
    // or an inaccessible ACL on Windows) is left alone. The probe creates and removes one file.
    private static bool Writable(string path)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(path, ".wdt-probe-" + Guid.NewGuid()),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
