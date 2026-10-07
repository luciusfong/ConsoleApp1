using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace DeviceScanner.Scanning
{
    internal sealed class IpScanEntry
    {
        public string Ip { get; set; }
        public string DeviceName { get; set; }     // null = nothing matched
        public ProbeStatus? Status { get; set; }

        public override string ToString() =>
            $"{Ip,-15} {(DeviceName == null ? "NOTHING MATCHED" : $"{DeviceName} [{Status.ToString().ToUpper()}]")}";
    }

    internal sealed class IpScanReport
    {
        public List<IpScanEntry> Entries { get; } = new List<IpScanEntry>();
        /// <summary>IP of a ZMC controller that opened cleanly, or null.</summary>
        public string ZmcIp { get; set; }
    }

    /// <summary>Ping-sweeps the subnets, then runs the IP probes against each live host.</summary>
    internal sealed class IpScanner
    {
        private readonly ScanConfig _cfg;
        private readonly IReadOnlyList<DeviceProbe> _probes;
        private readonly Action<string> _log;

        public IpScanner(ScanConfig cfg, IReadOnlyList<DeviceProbe> probes, Action<string> log)
        {
            _cfg = cfg; _probes = probes; _log = log;
        }

        public IpScanReport Scan()
        {
            _log($"--- Ping sweep 192.168.{_cfg.Subnets.First()}.1 - 192.168.{_cfg.Subnets.Last()}.254 ---");
            string[] alive = PingSweep();
            _log($"{alive.Length} hosts replied: {string.Join(", ", alive)}\n");

            var report = new IpScanReport();
            foreach (string ip in alive)
            {
                var entry = new IpScanEntry { Ip = ip };
                foreach (var probe in _probes)
                {
                    var r = probe.Run(ip);
                    _log($"{ip,-15} {probe.Name,-14} -> {r}");
                    if (!r.Found) continue;

                    entry.DeviceName = probe.Name;
                    entry.Status = r.Status;
                    if (probe.Name == IpProbes.Zmc && r.Status == ProbeStatus.Ok) report.ZmcIp = ip;
                    break;
                }
                report.Entries.Add(entry);
            }
            return report;
        }

        private string[] PingSweep()
        {
            var self = new HashSet<string>(NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address.ToString()));

            var ips = from t in _cfg.Subnets
                      from h in Enumerable.Range(1, 254)
                      select $"192.168.{t}.{h}";

            var alive = new ConcurrentBag<string>();
            Parallel.ForEach(ips, new ParallelOptions { MaxDegreeOfParallelism = _cfg.PingParallelism }, ip =>
            {
                try { using (var p = new Ping()) if (p.Send(ip, _cfg.PingTimeoutMs).Status == IPStatus.Success) alive.Add(ip); }
                catch { }
            });
            return alive.Where(ip => !self.Contains(ip)).OrderBy(ip => Version.Parse(ip)).ToArray();
        }
    }
}
