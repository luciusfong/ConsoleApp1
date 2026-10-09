using System;
using DeviceScanner.Motion;
using DeviceScanner.Native;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;
using DeviceScanner.Scanning;

namespace DeviceScanner
{
    internal static class Program
    {
        // usage: DeviceScanner.exe [profile file or name]     (no argument = use the Profiles folder)
        private static void Main(string[] args)
        {
            var cfg = new ScanConfig();
            Action<string> log = Console.WriteLine;

            MachineProfile profile = ProfileSelector.Select(args, log);
            if (profile == null) { Pause(); return; }

            var report = new RunReport(profile);

            // 1. every device the profile lists, at the address the profile gives
            new ProfileVerifier(cfg, profile, log).Run(report);

            // 2. optional: find everything on the network / COM ports, not just what the profile lists
            if (Confirm("\nAlso run full discovery (ping sweep + COM scan, slow)?"))
                Discover(cfg, profile, report, log);

            // 3. homing
            if (profile.HomePrograms.Count == 0)
                log("\nProfile has no home.programs, skipping homing.");
            else if (Confirm($"\nHome gantry ({string.Join(", ", profile.HomePrograms)})? Make sure the gantry is clear."))
                report.Home = new ZmcHoming(cfg, profile, log).Run();

            // 4. discrete IO loopback
            if (profile.IoTests.Count == 0)
                log("\nProfile has no io tests, skipping.");
            else if (Confirm($"\nRun {profile.IoTests.Count} discrete IO tests? Outputs will really switch (lifters, stoppers, vacuum...). Clear the machine first."))
                RunIoTests(cfg, profile, report, log);

            ScanSummary.Print(report, log);
            Pause();
        }

        private static void Discover(ScanConfig cfg, MachineProfile profile, RunReport report, Action<string> log)
        {
            report.DiscoveredIp = new IpScanner(cfg, IpProbes.Build(), log).Scan();
            report.DiscoveredCom = new ComScanner(cfg, ComProbes.Build(profile.TempSlaves), log).Scan();
        }

        private static void RunIoTests(ScanConfig cfg, MachineProfile profile, RunReport report, Action<string> log)
        {
            using (var zmc = new ZmcConnection())
            {
                if (!zmc.Open(profile.ZmcIp)) { log($"Cannot open ZMC at {profile.ZmcIp}."); return; }

                if (!new ZmcInit(cfg, profile.InitProgram, log).EnsureReady(zmc))
                    log("Warning: controller not initialized; IO readings may differ from the main app.");

                report.Io.AddRange(new ZmcDiscreteIo(cfg, log).RunAll(zmc, profile.IoTests));
                report.IoRun = true;
            }
        }

        private static bool Confirm(string question)
        {
            Console.Write($"{question} (y/N): ");
            return Console.ReadLine()?.Trim().ToLower() == "y";
        }

        private static void Pause()
        {
            Console.WriteLine("\nDone. Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
