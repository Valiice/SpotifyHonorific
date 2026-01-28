using System;
using System.Runtime.InteropServices;
using System.Security;

namespace SpotifyHonorific.Utils;

[SuppressUnmanagedCodeSecurity]
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    internal static partial class IdleTimeFinder
    {
        [DllImport("User32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [LibraryImport("Kernel32.dll")]
        private static partial uint GetLastError();

        public static uint GetIdleTime()
        {
            var lastInPut = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };

            if (!GetLastInputInfo(ref lastInPut))
            {
                throw new Exception(GetLastError().ToString());
            }

            return (uint)Environment.TickCount64 - lastInPut.dwTime;
        }
    }
}
