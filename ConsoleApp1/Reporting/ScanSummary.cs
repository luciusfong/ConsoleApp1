using System;
using System.Collections.Generic;
using System.Linq;

namespace DeviceScanner.Reporting
{
    internal static class ScanSummary
    {
        public static void Print(RunReport r, Action<string> log)
        {
            int failures = 0, warnings = 0;
            var notRun = new List<string>();

            log($"\n===== SUMMARY: {r.Profile.Name} =====");

            // ---------- devices ----------
            log("\n-- Devices (from profile) --");
            foreach (var d in r.Devices)
            {
                log($"  {Tag(d.Status),-5} {d.Kind,-3} {d.Address,-15} {d.Device,-14} {d.Detail}");
                if (d.Status == CheckStatus.Fail) failures++;
                else if (d.Status == CheckStatus.Unverified) warnings++;
            }

            // ---------- homing ----------
            log("\n-- Gantry homing --");
            if (r.Home == null)
            {
                log("  NOT RUN");
                notRun.Add("homing");
            }
            else
            {
                var h = r.Home;
                log($"  {(h.Success ? "PASS" : "FAIL")}  {h.Programs}   axes: {string.Join(",", h.Axes)}   {h.Elapsed.TotalSeconds:F1} s");
                if (!h.Success) { log($"        {h.Detail}"); failures++; }
            }

            // ---------- discrete IO ----------
            log("\n-- Discrete IO --");
            if (!r.IoRun)
            {
                log("  NOT RUN");
                notRun.Add("IO test");
            }
            else
            {
                int pass = 0, fail = 0, warn = 0;
                foreach (var t in r.Io)
                {
                    var c = t.Case;
                    string tag = t.Passed ? "PASS" : c.Optional ? "WARN" : "FAIL";
                    if (t.Passed) pass++; else if (c.Optional) warn++; else fail++;

                    string inText = c.In.HasValue ? c.In.Value.ToString() : "?";
                    log($"  {tag,-5} OP {c.Out,-3} -> IN {inText,-3} {c.Name,-30} {t.Detail}");
                }
                log($"  IO: {pass} passed, {fail} failed, {warn} warning(s) of {r.Io.Count}");
                failures += fail;
                warnings += warn;
            }

            // ---------- discovery (optional) ----------
            if (r.DiscoveredIp != null || r.DiscoveredCom != null)
            {
                log("\n-- Discovery (every device found, not limited to the profile) --");

                if (r.DiscoveredIp != null)
                {
                    foreach (var e in r.DiscoveredIp.Entries) log($"  IP   {e}");

                    string found = r.DiscoveredIp.ZmcIp;
                    if (found != null && found != r.Profile.ZmcIp)
                    {
                        log($"  WARN ZMC found at {found} but the profile says {r.Profile.ZmcIp}");
                        warnings++;
                    }
                }

                if (r.DiscoveredCom != null)
                {
                    foreach (var f in r.DiscoveredCom.Found.OrderBy(f => f.Name)) log($"  COM  {f.Name,-14} {f.Port}");
                    log($"  COM  ITV count: {r.DiscoveredCom.Found.Count(f => f.Name == "ITV regulator")}");
                    if (r.DiscoveredCom.BusyPorts.Count > 0)
                        log($"  COM  in use (device unknown): {string.Join(", ", r.DiscoveredCom.BusyPorts)}");
                }
            }

            // ---------- verdict ----------
            log("\n===== RESULT =====");
            log(failures == 0 ? "OVERALL: PASS" : $"OVERALL: FAIL ({failures} failure(s))");
            if (warnings > 0) log($"  {warnings} warning(s)");
            if (notRun.Count > 0) log($"  not run: {string.Join(", ", notRun)}");
        }

        private static string Tag(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Pass: return "PASS";
                case CheckStatus.Unverified: return "WARN";
                default: return "FAIL";
            }
        }
    }
}
