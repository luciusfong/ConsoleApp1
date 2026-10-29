using System;
using System.Collections.Generic;
using System.Linq;
using DeviceScanner.Utils;

namespace DeviceScanner.Reporting
{
    /// <summary>The parts of one run, in the order the report lists them.</summary>
    internal enum Module { WindowsSpec, Devices, Homing, Io, Discovery }

    internal enum ModuleStatus { Pass, Warn, Fail, NotRun }

    /// <summary>Verdict for one module: shown as it finishes in the console, in the PDF and in the final dialog.</summary>
    internal sealed class ModuleResult
    {
        public Module Module { get; set; }
        public string Title { get; set; }
        public ModuleStatus Status { get; set; }
        public int Failures { get; set; }
        public int Warnings { get; set; }
        public string Summary { get; set; } = "";                 // "40 passed, 3 failed, 2 warning(s) of 45", or why it did not run
        public List<string> Problems { get; } = new List<string>();  // one line per FAIL / WARN item
        public TimeSpan? Duration { get; set; }
    }

    /// <summary>Overall verdict of the run, made from the module verdicts.</summary>
    internal sealed class RunVerdict
    {
        public int Failures { get; set; }
        public int Warnings { get; set; }
        public List<string> NotRun { get; } = new List<string>();
        public bool Passed => Failures == 0;
    }

    /// <summary>Turns the raw results in a RunReport into a PASS / WARN / FAIL / NOT RUN per module.</summary>
    internal static class ModuleVerdicts
    {
        public static readonly Module[] ReportOrder = { Module.WindowsSpec, Module.Devices, Module.Homing, Module.Io, Module.Discovery };

        public static string Title(Module m) => L.T("mod." + m.ToString().ToLowerInvariant());

        public static List<ModuleResult> All(RunReport r) => ReportOrder.Select(m => For(r, m)).ToList();

        public static RunVerdict Overall(IEnumerable<ModuleResult> modules)
        {
            var v = new RunVerdict();
            foreach (var m in modules)
            {
                v.Failures += m.Failures;
                v.Warnings += m.Warnings;
                if (m.Status == ModuleStatus.NotRun) v.NotRun.Add(m.Title);
            }
            return v;
        }

        public static ModuleResult For(RunReport r, Module m)
        {
            ModuleResult result;
            switch (m)
            {
                case Module.WindowsSpec: result = Spec(r); break;
                case Module.Devices: result = Devices(r); break;
                case Module.Homing: result = Homing(r); break;
                case Module.Io: result = Io(r); break;
                default: result = Discovery(r); break;
            }
            result.Module = m;
            result.Title = Title(m);
            if (r.Durations.TryGetValue(m, out var d)) result.Duration = d;
            return result;
        }

        private static ModuleResult Spec(RunReport r)
        {
            if (r.WindowsSpec == null) return NotRun(L.T("mod.skipped"));

            var items = r.WindowsSpec.Items;
            var result = Counted(items.Count - r.WindowsSpec.Failures - r.WindowsSpec.Warnings,
                                 r.WindowsSpec.Failures, r.WindowsSpec.Warnings, items.Count);
            foreach (var s in items.Where(i => i.Status != CheckStatus.Pass))
                result.Problems.Add($"{s.Group} / {s.Name}: {s.Actual}" +
                                    (s.Expected.Length > 0 ? "  " + L.T("sum.expected", s.Expected) : "") +
                                    (s.Detail.Length > 0 ? "  " + s.Detail : ""));
            return result;
        }

        private static ModuleResult Devices(RunReport r)
        {
            int fail = r.Devices.Count(d => d.Status == CheckStatus.Fail);
            int warn = r.Devices.Count(d => d.Status == CheckStatus.Unverified);
            var result = Counted(r.Devices.Count - fail - warn, fail, warn, r.Devices.Count);
            foreach (var d in r.Devices.Where(d => d.Status != CheckStatus.Pass))
                result.Problems.Add($"{d.Kind} {d.Address} {d.Device}: {d.Detail}");
            return result;
        }

        private static ModuleResult Homing(RunReport r)
        {
            if (r.Home == null)
                return NotRun(r.Profile.HomePrograms.Count == 0 ? L.T("msg.noHome") : L.T("mod.skipped"));

            var h = r.Home;
            string summary = L.T("mod.homeSummary", h.Programs, string.Join(",", h.Axes), h.Elapsed.TotalSeconds);
            if (h.Success) return new ModuleResult { Status = ModuleStatus.Pass, Summary = summary };

            var result = new ModuleResult { Status = ModuleStatus.Fail, Failures = 1, Summary = summary };
            result.Problems.Add(h.Detail);
            return result;
        }

        private static ModuleResult Io(RunReport r)
        {
            if (!r.IoRun)
            {
                if (r.IoError != null)
                {
                    var failed = new ModuleResult { Status = ModuleStatus.Fail, Failures = 1, Summary = r.IoError };
                    failed.Problems.Add(r.IoError);
                    return failed;
                }
                return NotRun(r.Profile.IoTests.Count == 0 ? L.T("msg.noIo") : L.T("mod.skipped"));
            }

            int pass = r.Io.Count(t => t.Passed);
            int warn = r.Io.Count(t => !t.Passed && t.Case.Optional);
            int fail = r.Io.Count - pass - warn;
            var result = Counted(pass, fail, warn, r.Io.Count);
            foreach (var t in r.Io.Where(t => !t.Passed))
                result.Problems.Add($"OP {t.Case.Out} -> IN {(t.Case.In.HasValue ? t.Case.In.Value.ToString() : "?")} {t.Case.Name}: {t.Detail}");
            return result;
        }

        private static ModuleResult Discovery(RunReport r)
        {
            if (r.DiscoveredIp == null && r.DiscoveredCom == null) return NotRun(L.T("mod.skipped"));

            var result = new ModuleResult
            {
                Summary = L.T("mod.discoverySummary", r.DiscoveredIp?.Entries.Count ?? 0, r.DiscoveredCom?.Found.Count ?? 0),
            };

            // a ZMC that answers somewhere else than the profile says is the only thing discovery warns about
            string zmc = r.DiscoveredIp?.ZmcIp;
            if (!string.IsNullOrWhiteSpace(zmc) && zmc != r.Profile.ZmcIp)
            {
                result.Warnings = 1;
                result.Problems.Add(L.T("sum.zmcElsewhere", "", zmc, r.Profile.ZmcIp).Trim());
            }
            result.Status = StatusOf(0, result.Warnings);
            return result;
        }

        private static ModuleResult Counted(int pass, int fail, int warn, int total) => new ModuleResult
        {
            Status = StatusOf(fail, warn),
            Failures = fail,
            Warnings = warn,
            Summary = L.T("mod.counts", pass, fail, warn, total),
        };

        private static ModuleResult NotRun(string why) => new ModuleResult { Status = ModuleStatus.NotRun, Summary = why };

        private static ModuleStatus StatusOf(int failures, int warnings) =>
            failures > 0 ? ModuleStatus.Fail : warnings > 0 ? ModuleStatus.Warn : ModuleStatus.Pass;
    }
}
