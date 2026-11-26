using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Deterministic checks of the real native observations, loader restriction, Profile
// reader and console cancellation on any Windows host. They read this process's or
// thread's own state, temporarily impersonate on a dedicated thread and revert it, keep
// files and one junction under the run's fresh TEMP root, or run one owned child process
// with its own hidden console. No account, broker, provider display or network is used.
[TestClass]
[DoNotParallelize]
public sealed partial class NativeObservationScenarios
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void PlatformObservationReportsThisProcess()
    {
        var platform = new NativeWindowsHostObservations().ReadPlatform(CancellationToken.None);

        Assert.IsTrue(platform.IsWindows);
        Assert.AreEqual(RuntimeInformation.ProcessArchitecture, platform.ProcessArchitecture);
        Assert.AreEqual(RuntimeInformation.OSArchitecture, platform.OsArchitecture);
        Assert.AreEqual(Environment.OSVersion.Version, platform.Version);
        Assert.AreEqual(10, platform.Version.Major);
    }

    [TestMethod]
    public void CancelledPlatformObservationStopsBeforeReading()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            new NativeWindowsHostObservations().ReadPlatform(cancelled.Token));
    }

    [TestMethod]
    public void ThreadIdentityDistinguishesImpersonationOnTheCallingThread()
    {
        // A dedicated thread keeps any impersonation token away from pooled threads;
        // RevertToSelf runs before the thread ends, whatever the observations return.
        WindowsThreadIdentity? before = null, during = null, after = null;
        var impersonated = false;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var observations = new NativeWindowsHostObservations();
            try
            {
                before = observations.ReadThreadIdentity(CancellationToken.None);
                impersonated = ImpersonateSelf(2) != 0; // SecurityImpersonation.
                if (impersonated) during = observations.ReadThreadIdentity(CancellationToken.None);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (impersonated && RevertToSelf() == 0)
                    failure ??= new InvalidOperationException("RevertToSelf failed.");
            }
            try { after = observations.ReadThreadIdentity(CancellationToken.None); }
            catch (Exception exception) { failure ??= exception; }
        }) { IsBackground = true };
        thread.Start();
        Assert.IsTrue(thread.Join(Limit), "The identity thread did not finish.");

        Assert.IsNull(failure, failure?.Message);
        Assert.IsTrue(impersonated, "ImpersonateSelf must succeed for the current user.");
        Assert.AreEqual(WindowsThreadIdentity.NoToken, before);
        Assert.AreEqual(WindowsThreadIdentity.Impersonating, during);
        Assert.AreEqual(WindowsThreadIdentity.NoToken, after);
    }

    [TestMethod]
    public void RestrictedDllSearchExcludesTheWorkingDirectory()
    {
        // The child places a non-image probe in its own working directory. Before the
        // restriction the default search reaches it (bad image format); afterward it
        // is not searched (module not found). No image is mapped or executed.
        var (exit, _) = RunChild(NativeProcessChild.RestrictSearch);
        if (exit == NativeProcessChild.ProbeUnreachable)
            Assert.Inconclusive("The default search did not reach the working-directory probe on this host.");
        Assert.AreEqual(0, exit, "The restricted search still reached the working-directory probe.");
    }

    [TestMethod]
    public void ConsoleBreakCancelsThePendingRequestWithATypedResult()
    {
        // Deviation from the accepted wording "a new process group": the child gets its
        // own hidden console through CreateNoWindow and raises GenerateConsoleCtrlEvent
        // (CTRL_BREAK_EVENT, process group 0), which reaches only processes attached to
        // that console, the child itself. No separate process group is created.
        var (exit, output) = RunChild(NativeProcessChild.ConsoleBreak);
        Assert.AreEqual(1, exit, "Console cancellation must end with one typed failure.");
        using var json = JsonDocument.Parse(output);
        Assert.AreEqual("cancelled", json.RootElement.GetProperty("outcome").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("accessToken", out _));
    }

    [TestMethod]
    public void ProfileReaderRejectsNonLocalPathFormsBeforeOpening()
    {
        var source = new WindowsProfileSource();
        foreach (var path in new[]
        {
            "profile.json", @"relative\profile.json", "C:profile.json", @"\profile.json",
            @"\\server\share\profile.json", @"\\?\C:\profile.json", @"\\.\C:\profile.json",
            "C:\\profile\u0001.json", "/tmp/profile.json",
        })
        {
            var exception = Assert.ThrowsExactly<IOException>(() =>
                source.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult(), path);
            Assert.IsFalse(exception.Message.Contains(path, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task ProfileReachedThroughALocalJunctionIsRead()
    {
        var root = Path.Combine(Path.GetTempPath(), "profile-junction-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var junction = Path.Combine(root, "junction");
        Directory.CreateDirectory(target);
        var bytes = "{\"synthetic\":true}"u8.ToArray();
        File.WriteAllBytes(Path.Combine(target, "profile.json"), bytes);
        Directory.CreateDirectory(junction);
        try
        {
            CreateJunction(junction, target);
            var read = await new WindowsProfileSource().ReadAsync(Path.Combine(junction, "profile.json"),
                CancellationToken.None).WaitAsync(Limit);
            CollectionAssert.AreEqual(bytes, read.ToArray());
        }
        finally
        {
            // Deleting the directory entry removes only the junction, never its target.
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
        Assert.IsFalse(Directory.Exists(junction));
        Assert.IsTrue(File.Exists(Path.Combine(target, "profile.json")), "Junction removal must not touch the target.");
    }

    private static (int Exit, byte[] Output) RunChild(string scenario)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "native-child-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("The test host path is unavailable.");
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Authentication.Windows.Scenarios.dll"));
        start.ArgumentList.Add("--scenario-child");
        start.ArgumentList.Add(scenario);
        start.ArgumentList.Add(workingDirectory);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The child did not start.");
        child.StandardInput.Close();
        using var output = new MemoryStream();
        var copy = child.StandardOutput.BaseStream.CopyToAsync(output);
        var error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(Limit))
        {
            child.Kill();
            child.WaitForExit();
            Assert.Fail("The native child did not finish within its bound.");
        }
        Assert.IsTrue(copy.Wait(Limit) && error.Wait(Limit), "The native child output did not close.");
        return (child.ExitCode, output.ToArray());
    }

    // A mount-point reparse point owned by this scenario; the caller removes it.
    private static unsafe void CreateJunction(string junction, string target)
    {
        var print = Path.GetFullPath(target);
        var substitute = @"\??\" + print;
        var pathBytes = (substitute.Length + 1 + print.Length + 1) * sizeof(char);
        var buffer = new byte[16 + pathBytes];
        fixed (byte* data = buffer)
        {
            *(uint*)data = 0xA0000003; // IO_REPARSE_TAG_MOUNT_POINT.
            *(ushort*)(data + 4) = (ushort)(8 + pathBytes);
            *(ushort*)(data + 8) = 0;
            *(ushort*)(data + 10) = (ushort)(substitute.Length * sizeof(char));
            *(ushort*)(data + 12) = (ushort)((substitute.Length + 1) * sizeof(char));
            *(ushort*)(data + 14) = (ushort)(print.Length * sizeof(char));
            substitute.AsSpan().CopyTo(new Span<char>(data + 16, substitute.Length));
            print.AsSpan().CopyTo(new Span<char>(data + 16 + (substitute.Length + 1) * sizeof(char), print.Length));
            // GENERIC_WRITE, OPEN_EXISTING, backup semantics and the reparse point itself.
            var handle = CreateFileW(junction, 0x40000000, 0, 0, 3, 0x02000000 | 0x00200000, 0);
            if (handle == -1) throw new InvalidOperationException("The junction directory could not be opened.");
            try
            {
                if (DeviceIoControl(handle, 0x000900A4, data, (uint)buffer.Length, null, 0, out _, 0) == 0)
                    throw new InvalidOperationException("The junction could not be created.");
            }
            finally { CloseHandle(handle); }
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ImpersonateSelf(int level);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RevertToSelf();

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateFileW(string name, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int DeviceIoControl(nint device, uint code, void* input, uint inputSize,
        void* output, uint outputSize, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CloseHandle(nint handle);
}
