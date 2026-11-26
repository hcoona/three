using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Synthetic UTF-16 buffers only; no desktop, native query, private name or UI.
[TestClass]
public sealed class WindowsDesktopNameScenarios
{
    [TestMethod]
    public void CompleteNameUsesOnlyTheReportedBytes()
    {
        var buffer = "Default\0unreported".ToCharArray();
        Assert.IsTrue(NativeWindowsHostObservations.TryGetDesktopNameLength(buffer, 16, out var length));
        Assert.AreEqual(7, length);
        Assert.IsTrue(NativeWindowsHostObservations.TryGetDesktopNameLength("X\0".AsSpan(), 4, out length));
        Assert.AreEqual(1, length);
    }

    [TestMethod]
    public void LengthMustCoverCompleteNonemptyTerminatedNameWithinBothBounds()
    {
        var buffer = "Default\0".ToCharArray();
        foreach (var bytes in new uint[] { 0, 1, 2, 3, 15, 17, 18, 513, uint.MaxValue })
            Reject(buffer, bytes);
        Reject("Default".AsSpan(), 14);
        Reject("\0".AsSpan(), 2);
        Reject(ReadOnlySpan<char>.Empty, 4);
        Reject(new string('x', 257).AsSpan(), 514);
    }

    [TestMethod]
    public void FixedBufferLimitIncludesTheTerminator()
    {
        var buffer = (new string('x', 255) + "\0").ToCharArray();
        Assert.IsTrue(NativeWindowsHostObservations.TryGetDesktopNameLength(buffer, 512, out var length));
        Assert.AreEqual(255, length);
        buffer[^1] = 'x';
        Reject(buffer, 512);
    }

    [TestMethod]
    public void EmbeddedTerminatorsAndIncompleteUnicodeAreRejected()
    {
        foreach (var name in new[] { "A\0B\0", "\uD800\0", "\uDC00\0", "\uD800X\0", "X\uD800\0" })
            Reject(name.AsSpan(), (uint)(name.Length * sizeof(char)));
    }

    [TestMethod]
    public void CompleteSurrogatePairIsAcceptedWithoutNormalization()
    {
        var name = "A\uD83D\uDE00\0";
        Assert.IsTrue(NativeWindowsHostObservations.TryGetDesktopNameLength(name.AsSpan(), 8, out var length));
        Assert.AreEqual(3, length);
    }

    private static void Reject(ReadOnlySpan<char> buffer, uint bytes)
    {
        Assert.IsFalse(NativeWindowsHostObservations.TryGetDesktopNameLength(buffer, bytes, out var length));
        Assert.AreEqual(0, length);
    }
}
