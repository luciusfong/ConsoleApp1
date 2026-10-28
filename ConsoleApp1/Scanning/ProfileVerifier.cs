using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.NetworkInformation;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;

namespace DeviceScanner.Scanning
{
    /// <summary>
    /// Checks that every device the profile lists answers at the address the profile gives.
    /// Much faster than a full sweep, and it tells you exactly which expected device is wrong.
    /// </summary>
    internal sealed class ProfileVerifier
    {
        private readonly ScanConfig _cfg;
        private readonly MachineProfile _profile;
        private readonly Action<string> _log;

        public ProfileVerifier(ScanConfig cfg, MachineProfile profile, Action<string> log)
        {
            _cfg = cfg; _profile = profile; _log = log;
        }

        public void Run(RunReport report)
        {
            _log("\n--- Verifying devices against profile ---");

            var ipProbes = IpProbes.Build(_cfg);
            var comProbes = ComProbes.Build(_profile.TempSlaves);

            // the controller first, then everything else the profile lists
            report.Devices.Add(CheckIp(new ExpectedDevice(_profile.ZmcIp, IpProbes.Zmc), ipProbes));
            foreach (var e in _profile.ExpectedIp) report.Devices.Add(CheckIp(e, ipProbes));

            var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            foreach (var e in _profile.ExpectedCom) report.Devices.Add(CheckCom(e, comProbes, present));
        }

        private DeviceCheck CheckIp(ExpectedDevice e, IReadOnlyList<DeviceProbe> probes)
        {
            var c = new DeviceCheck { Kind = "IP", Address = e.Address, Device = e.Name };

            var probe = Find(probes, e.Name);
            if (probe == null) return Finish(c, CheckStatus.Fail, UnknownName(probes));
            if (!Pings(e.Address)) return Finish(c, CheckStatus.Fail, "no ping reply");

            return FromProbe(c, probe.Run(e.Address));
        }

        private DeviceCheck CheckCom(ExpectedDevice e, IReadOnlyList<DeviceProbe> probes, HashSet<string> present)
        {
            var c = new DeviceCheck { Kind = "COM", Address = e.Address, Device = e.Name };

            var probe = Find(probes, e.Name);
            if (probe == null) return Finish(c, CheckStatus.Fail, UnknownName(probes));
            if (!present.Contains(e.Address)) return Finish(c, CheckStatus.Fail, "port not present");
            if (ProbeHelpers.PortBusy(e.Address)) return Finish(c, CheckStatus.Unverified, "port in use, cannot verify");

            return FromProbe(c, probe.Run(e.Address));
        }

        private DeviceCheck FromProbe(DeviceCheck c, ProbeResult r)
        {
            switch (r.Status)
            {
                case ProbeStatus.Ok: return Finish(c, CheckStatus.Pass, r.Note ?? "");
                case ProbeStatus.Busy: return Finish(c, CheckStatus.Pass, "present, in use by another program" + (r.Note == null ? "" : "; " + r.Note));
                default: return Finish(c, CheckStatus.Fail, r.Message);
            }
        }

        private DeviceCheck Finish(DeviceCheck c, CheckStatus status, string detail)
        {
            // IP devices: always show which local NIC Windows routes to them through, even if the device did not answer
            if (c.Kind == "IP" && (detail ?? "").IndexOf("[PCI", StringComparison.OrdinalIgnoreCase) < 0)
                detail = (string.IsNullOrEmpty(detail) ? "" : detail + "; ") + RouteNote(c.Address, c.Device);

            c.Status = status;
            c.Detail = detail;
            _log($"  {c.Kind,-3} {c.Address,-15} {c.Device,-14} -> {status.ToString().ToUpper()} {detail}");
            return c;
        }

        /// <summary>"via Ethernet 3 / Intel(R) I350 ... [PCI\VEN_8086&DEV_1521]" from the routing table (no device needed).</summary>
        private string RouteNote(string ip, string device) =>
            NicRoute.Note(ip, device.Equals("Camera GigE", StringComparison.OrdinalIgnoreCase) ? _cfg.CameraNicPnpId : null);

        private bool Pings(string ip)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { using (var p = new Ping()) if (p.Send(ip, _cfg.PingTimeoutMs).Status == IPStatus.Success) return true; }
                catch { }
            }
            return false;
        }

        private static DeviceProbe Find(IReadOnlyList<DeviceProbe> probes, string name) =>
            probes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string UnknownName(IReadOnlyList<DeviceProbe> probes) =>
            "unknown device name; valid: " + string.Join(", ", probes.Select(p => p.Name));
    }
}
