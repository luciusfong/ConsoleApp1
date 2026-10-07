using System;
using System.Linq;
using DeviceScanner.Scanning;

namespace DeviceScanner.Reporting
{
    internal static class ScanSummary
    {
        public static void Print(IpScanReport ip, ComScanReport com, Action<string> log)
        {
            log("\n===== SUMMARY =====");
            foreach (var e in ip.Entries) log($"IP   {e}");
            foreach (var f in com.Found.OrderBy(f => f.Name)) log($"COM  {f.Name,-14} {f.Port}");
            log($"COM  ITV count: {com.Found.Count(f => f.Name == "ITV regulator")}");
            if (com.BusyPorts.Count > 0)
                log($"COM  in use (device unknown): {string.Join(", ", com.BusyPorts)}");
        }
    }
}
