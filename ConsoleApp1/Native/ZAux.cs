using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceScanner.Native
{
    /// <summary>Raw P/Invoke declarations for zauxdll.dll (ZMotion controller SDK).</summary>
    internal static class ZAux
    {
        private const string Dll = "zauxdll.dll";

        [DllImport(Dll, EntryPoint = "ZAux_OpenEth", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int OpenEth(string ipaddr, out IntPtr phandle);

        [DllImport(Dll, EntryPoint = "ZAux_Close", CallingConvention = CallingConvention.StdCall)]
        public static extern int Close(IntPtr handle);

        [DllImport(Dll, EntryPoint = "ZAux_Execute", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int Execute(IntPtr handle, string cmd, StringBuilder resp, uint respLen);

        /// <summary>value: 0 = moving, -1 = idle.</summary>
        [DllImport(Dll, EntryPoint = "ZAux_Direct_GetIfIdle", CallingConvention = CallingConvention.StdCall)]
        public static extern int GetIfIdle(IntPtr handle, int axis, ref int value);

        [DllImport(Dll, EntryPoint = "ZAux_Direct_GetVrf", CallingConvention = CallingConvention.StdCall)]
        public static extern int GetVrf(IntPtr handle, int start, int num, float[] values);

        [DllImport(Dll, EntryPoint = "ZAux_Direct_SetVrf", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetVrf(IntPtr handle, int start, int num, float[] values);
    }
}
