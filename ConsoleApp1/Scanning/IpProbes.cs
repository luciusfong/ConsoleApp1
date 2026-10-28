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

        public static IReadOnlyList<DeviceProbe> Build(ScanConfig cfg) => new[]
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
                var r = ProbeHelpers.Run(() => d.OpenDevice("Scan", ip), () => { if (d.IsConnected) d.CloseDevice(); });
                return r.Found ? CameraNic(ip, r, cfg.CameraNicPnpId) : r;
            }),
            new DeviceProbe(Zmc, ip =>
            {
                var zmc = new ZmcConnection();
                return ProbeHelpers.Run(() => zmc.Open(ip), () => zmc.Close());
            }),
        };

        /// <summary>Every camera must be reached through the required NIC (PCI\VEN_8086&amp;DEV_1521 = Intel I350), else FAIL.</summary>
        private static ProbeResult CameraNic(string ip, ProbeResult opened, string requiredPnp)
        {
            var nic = NicRoute.ForIp(ip);
            if (nic == null)
                return ProbeResult.Rejected($"camera found but its local NIC could not be identified (required {requiredPnp})");
            if (nic.PnpId.IndexOf(requiredPnp, StringComparison.OrdinalIgnoreCase) < 0)
                return ProbeResult.Rejected($"camera on wrong NIC: {nic.Connection} / {nic.Description} [{nic.ShortPnpId}], required {requiredPnp}");
            return opened.WithNote($"via {nic.Connection} / {nic.Description} [{nic.ShortPnpId}]");   // Ok or Busy, right NIC
        }
    }
}
