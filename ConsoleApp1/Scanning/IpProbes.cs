using System;
using System.Collections.Generic;
using NSW_Components;
using DeviceScanner.Native;

namespace DeviceScanner.Scanning
{
    /// <summary>IP device probes. Order matters: first hit wins.</summary>
    internal static class IpProbes
    {
        public const string Zmc = "ZMC";

        public static IReadOnlyList<DeviceProbe> Build() => new[]
        {
            new DeviceProbe("CL3 (Keyence)", ip =>
            {
                if (!ProbeHelpers.TcpOpen(ip, 24685)) return ProbeResult.Fail("port closed"); // avoids CL3's 10 s timeout
                var d = new CL3();
                var r = ProbeHelpers.Run(() => d.Open(ip), () => d.Close());
                return r.Status == ProbeStatus.Fail ? ProbeResult.Busy() : r;                // port listening but open failed
            }),
            new DeviceProbe("OPT light", ip =>
            {
                var d = new OPT();
                return ProbeHelpers.Run(() => d.Open(ip), () => d.Close());
            }),
            new DeviceProbe("Camera GigE", ip =>
            {
                var d = new MVC_GenTL();
                return ProbeHelpers.Run(() => d.OpenDevice("Scan", ip), () => { if (d.IsConnected) d.CloseDevice(); });
            }),
            new DeviceProbe(Zmc, ip =>
            {
                var zmc = new ZmcConnection();
                return ProbeHelpers.Run(() => zmc.Open(ip), () => zmc.Close());
            }),
        };
    }
}
