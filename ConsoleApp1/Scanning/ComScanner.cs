using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace DeviceScanner.Scanning
{
    internal sealed class ComScanReport
    {
        public List<(string Name, string Port)> Found { get; } = new List<(string, string)>();
        public List<string> BusyPorts { get; } = new List<string>();
    }

    /// <summary>Walks every COM port and tries each probe until one claims it.</summary>
    internal sealed class ComScanner
    {
        private readonly ScanConfig _cfg;
        private readonly IReadOnlyList<DeviceProbe> _probes;
        private readonly Action<string> _log;

        public ComScanner(ScanConfig cfg, IReadOnlyList<DeviceProbe> probes, Action<string> log)
        {
            _cfg = cfg; _probes = probes; _log = log;
        }

        public ComScanReport Scan()
        {
            _log("\n--- COM scan ---");
            string[] ports = SerialPort.GetPortNames().OrderBy(p => int.Parse(p.Substring(3))).ToArray();
            _log($"Ports: {string.Join(", ", ports)}");

            var report = new ComScanReport();
            foreach (string com in ports)
            {
                if (ProbeHelpers.PortBusy(com))
                {
                    _log($"{com}: in use (access denied)");
                    report.BusyPorts.Add(com);
                    continue;
                }

                foreach (var probe in _probes)
                {
                    // single-instance devices are skipped once located
                    bool multi = _cfg.MultiInstanceComDevices.Contains(probe.Name);
                    if (!multi && report.Found.Any(f => f.Name == probe.Name)) continue;

                    var r = probe.Run(com);
                    _log($"{com,-6} {probe.Name,-14} -> {r}");
                    if (r.Status == ProbeStatus.Ok) { report.Found.Add((probe.Name, com)); break; } // port claimed
                }
            }
            return report;
        }
    }
}
