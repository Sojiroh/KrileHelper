using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sharlayan.Core.Native;

[SupportedOSPlatform("linux")]
public sealed class LinuxNativeMemory : INativeMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IoVec
    {
        public IntPtr Base;
        public nuint Length;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern long process_vm_readv(
        int pid,
        ref IoVec localIov, nuint liovcnt,
        ref IoVec remoteIov, nuint riovcnt,
        nuint flags);

    public unsafe int Read(int pid, ulong remoteAddress, Span<byte> destination)
    {
        if (destination.IsEmpty) return 0;
        fixed (byte* p = destination)
        {
            var local = new IoVec { Base = (IntPtr)p, Length = (nuint)destination.Length };
            var remote = new IoVec { Base = (IntPtr)remoteAddress, Length = (nuint)destination.Length };
            var n = process_vm_readv(pid, ref local, 1, ref remote, 1, 0);
            if (n < 0)
            {
                var err = Marshal.GetLastPInvokeError();
                // ESRCH = process gone
                if (err == 3) throw new ProcessDetachedException();
                throw new NativeMemoryReadException(err,
                    $"process_vm_readv failed: errno={err} ({Marshal.GetLastPInvokeErrorMessage()}) " +
                    $"pid={pid} addr=0x{remoteAddress:X} len={destination.Length}");
            }
            return (int)n;
        }
    }
}
