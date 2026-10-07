using System;
using System.IO.Ports;
using System.Net.Sockets;

namespace DeviceScanner.Scanning
{
    /// <summary>Shared low-level helpers used by the probes.</summary>
    internal static class ProbeHelpers
    {
        /// <summary>Opens a device, always closes it, and classifies the outcome.</summary>
        public static ProbeResult Run(Func<bool> open, Action close)
        {
            try { return open() ? ProbeResult.Ok() : ProbeResult.Fail(); }
            catch (Exception ex)
            {
                string m = (ex.Message ?? "").ToLower();
                bool busy = ex is UnauthorizedAccessException
                    || m.Contains("denied") || m.Contains("in use") || m.Contains("already")
                    || m.Contains("no permission") || m.Contains("busy");
                return busy ? ProbeResult.Busy() : ProbeResult.Fail(ex.Message);
            }
            finally { try { close(); } catch { } }
        }

        public static bool TcpOpen(string ip, int port, int ms = 500)
        {
            try
            {
                using (var c = new TcpClient())
                    return c.ConnectAsync(ip, port).Wait(ms) && c.Connected;
            }
            catch { return false; }
        }

        public static bool PortBusy(string com)
        {
            try { using (var p = new SerialPort(com)) p.Open(); return false; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }
    }
}
