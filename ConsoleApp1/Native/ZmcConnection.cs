using System;
using System.Globalization;
using System.Text;

namespace DeviceScanner.Native
{
    /// <summary>
    /// One open Ethernet connection to a ZMC controller. Owns the handle and wraps the
    /// raw ZAux calls (command, query, VR variables, idle state). Dispose closes it.
    /// </summary>
    internal sealed class ZmcConnection : IDisposable
    {
        public IntPtr Handle { get; private set; } = IntPtr.Zero;
        public string Ip { get; private set; }
        public bool IsOpen => Handle != IntPtr.Zero;

        // ---------- open / close ----------

        public bool Open(string ip)
        {
            Close();

            int rc = ZAux.OpenEth(ip, out IntPtr h);
            if (rc != 0)
            {
                if (h != IntPtr.Zero) ZAux.Close(h);
                return false;
            }

            Handle = h;
            Ip = ip;
            return true;
        }

        public void Close()
        {
            if (Handle != IntPtr.Zero) ZAux.Close(Handle);
            Handle = IntPtr.Zero;
            Ip = null;
        }

        public void Dispose() => Close();

        // ---------- commands ----------

        /// <summary>Sends a command line (e.g. "run GHome"). Returns the SDK return code, 0 = delivered.</summary>
        public int Execute(string cmd, out string response)
        {
            var sb = new StringBuilder(512);
            int rc = ZAux.Execute(Handle, cmd, sb, 512);
            response = sb.ToString().Trim();
            return rc;
        }

        public int Execute(string cmd) => Execute(cmd, out _);

        /// <summary>Evaluates "?expr" and returns the reply text, or "rc=N" if the call failed.</summary>
        public string Query(string expr)
        {
            int rc = Execute("?" + expr, out string resp);
            return rc == 0 ? resp : $"rc={rc}";
        }

        /// <summary>Evaluates an expression and parses it as a number; returns fallback on failure.</summary>
        public int QueryInt(string expr, int fallback = -1)
        {
            return double.TryParse(Query(expr), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? (int)v : fallback;
        }

        // ---------- VR variables ----------

        public bool SetVr(int index, float value) =>
            ZAux.SetVrf(Handle, index, 1, new[] { value }) == 0;

        public bool GetVr(int index, out float value)
        {
            var buf = new float[1];
            int rc = ZAux.GetVrf(Handle, index, 1, buf);
            value = buf[0];
            return rc == 0;
        }

        // ---------- axis state ----------

        /// <summary>Returns false if the read failed; idle is true when the axis is not moving.</summary>
        public bool TryGetIdle(int axis, out bool idle)
        {
            int v = 0;
            int rc = ZAux.GetIfIdle(Handle, axis, ref v);   // 0 = moving, -1 = idle
            idle = v == -1;
            return rc == 0;
        }
    }
}
