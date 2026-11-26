using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Synthetic x64 buffers only: no token, native identity, pointer dereference or Win32 call.
[TestClass]
public sealed class WindowsLogonSidBufferScenarios
{
    private const ulong Address = 0x1000;

    [TestMethod]
    public void CompleteSingleLogonSidIsBoundedWithinReturnedBuffer()
    {
        var buffer = Buffer();
        Assert.IsTrue(NativeWindowsHostObservations.TryReadLogonSidOffset(buffer, Address, out var offset));
        Assert.AreEqual(24, offset);
        Assert.IsTrue(NativeWindowsHostObservations.TryReadLogonSidOffset(buffer.AsSpan(0, 44), Address, out offset));
        Assert.AreEqual(24, offset);
        buffer[25] = 15; // The maximum SID ends exactly at the buffer boundary.
        Assert.IsTrue(NativeWindowsHostObservations.TryReadLogonSidOffset(buffer, Address, out offset));
    }

    [TestMethod]
    public void TruncatedHeaderAndOversizedBufferAreRejected()
    {
        var buffer = Buffer();
        for (var length = 0; length < 24; length++) Reject(buffer.AsSpan(0, length));
        Reject(new byte[93]);
    }

    [TestMethod]
    public void ExactlyOneLogonGroupIsRequired()
    {
        foreach (var count in new uint[] { 0, 2, uint.MaxValue })
        {
            var buffer = Buffer();
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, count);
            Reject(buffer);
        }
        foreach (var attributes in new uint[] { 0, 0x80000000, 0x40000000, 7 })
        {
            var buffer = Buffer();
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), attributes);
            Reject(buffer);
        }
    }

    [TestMethod]
    public void SidPointerCannotEscapeBufferOrOverlapGroupHeader()
    {
        foreach (var pointer in new ulong[]
        {
            0, Address - 1, Address, Address + 8, Address + 23,
            Address + 85, Address + 92, ulong.MaxValue,
        })
        {
            var buffer = Buffer();
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), pointer);
            Reject(buffer);
        }
        Reject(Buffer(), 0);
        Reject(Buffer(), ulong.MaxValue - 91); // The supplied buffer range would wrap.
        var unaligned = Buffer();
        unaligned.AsSpan(24, 20).CopyTo(unaligned.AsSpan(25));
        BinaryPrimitives.WriteUInt64LittleEndian(unaligned.AsSpan(8), Address + 25);
        Reject(unaligned);
    }

    [TestMethod]
    public void CompleteSidHeaderAndSubauthoritiesAreRequired()
    {
        var buffer = Buffer();
        for (var length = 24; length < 44; length++) Reject(buffer.AsSpan(0, length));
        foreach (var revision in new byte[] { 0, 2, byte.MaxValue })
        {
            buffer = Buffer();
            buffer[24] = revision;
            Reject(buffer);
        }
        buffer = Buffer();
        buffer[25] = 16;
        Reject(buffer);
        buffer[25] = 15;
        Reject(buffer.AsSpan(0, 91));
    }

    private static byte[] Buffer()
    {
        var buffer = new byte[92];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), Address + 24);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), 0xC0000007);
        buffer[24] = 1;
        buffer[25] = 3;
        buffer[31] = 5; // Synthetic S-1-5-5-10-20, not a real host identity.
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(36), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(40), 20);
        return buffer;
    }

    private static void Reject(ReadOnlySpan<byte> buffer, ulong address = Address)
    {
        Assert.IsFalse(NativeWindowsHostObservations.TryReadLogonSidOffset(buffer, address, out var offset));
        Assert.AreEqual(0, offset);
    }
}
