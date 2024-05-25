using System.Runtime.InteropServices;

namespace DfsLib;

internal static class Helpers
{
    public static byte[] GetBytes<T>(T str) where T : struct
    {
        int size = Marshal.SizeOf(str);

        byte[] arr = new byte[size];

        GCHandle h = default(GCHandle);
        try
        {
            h = GCHandle.Alloc(arr, GCHandleType.Pinned);

            Marshal.StructureToPtr<T>(str, h.AddrOfPinnedObject(), false);
        }
        finally
        {
            if (h.IsAllocated)
            {
                h.Free();
            }
        }
        return arr;
    }
}