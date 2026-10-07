using System;
using DeviceScanner.Motion;
using DeviceScanner.Reporting;
using DeviceScanner.Scanning;

namespace DeviceScanner
{
    internal static class Program
    {
        private static void Main()
        {
            var cfg = new ScanConfig();
            Action<string> log = Console.WriteLine;

            //var ipReport  = new IpScanner(cfg, IpProbes.Build(), log).Scan();
            //var comReport = new ComScanner(cfg, ComProbes.Build(cfg), log).Scan();

            //ScanSummary.Print(ipReport, comReport, log);

            //OfferHoming(cfg, ipReport.ZmcIp, log);
            OfferHoming(cfg, "192.168.0.11", log);

            Console.WriteLine("\nDone. Press Enter to exit.");
            Console.ReadLine();
        }

        private static void OfferHoming(ScanConfig cfg, string zmcIp, Action<string> log)
        {
            if (zmcIp == null) { log("\nNo ZMC found, skipping homing."); return; }

            Console.Write($"\nHome all axes on ZMC {zmcIp}? Make sure the gantry is clear. (y/N): ");
            if (Console.ReadLine()?.Trim().ToLower() != "y") return;

            log(new ZmcHoming(cfg, log).Run(zmcIp) ? "Homing complete." : "Homing FAILED.");
        }
    }
}
