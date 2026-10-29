using DeviceScanner.Motion;
using DeviceScanner.Native;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;
using DeviceScanner.Scanning;
using DeviceScanner.UI;
using DeviceScanner.Utils;
using DeviceScanner.WindowsSpec;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;


namespace DeviceScanner
{
    internal static class Program
    {
        /// <summary>One module of a run, and whether the operator has to agree to it.</summary>
        private sealed class Step
        {
            public Module Module { get; set; }
            public string Question { get; set; }      // null = always runs (nothing on the machine moves)
            public bool Caution { get; set; }         // the machine moves: asked with a warning, and confirmed before a full run
            public bool Available { get; set; } = true;
            public Action Work { get; set; }
        }

        // usage: DeviceScanner.exe [profile file or name] [--lang en|zh]     (no profile = use the Profiles folder; no --lang = ask)
        [STAThread]                                // the Windows dialogs need an STA thread
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Run(args, new ScanConfig()); }
            catch (Exception ex) { Dialogs.Error(L.T("msg.error", ex.Message)); }
        }

        private static void Run(string[] args, ScanConfig cfg)
        {
            var log = new RunLog();                 // every check logs here; the scan window shows it live

            DateTime started = DateTime.Now;
            args = L.Choose(args);                 // English / 中文 for the dialogs and the report
            if (args == null) return;

            // not elevated: start again through the UAC prompt and let that copy do the run
            if (AdminRestart.Run(ref args) != AdminRestart.Outcome.Continue) return;
            FirewallAccess.Allow(cfg, log.Add);     // so Windows never stops the scan to ask "Allow access?"

            string user = Dialogs.AskText(L.T("prompt.username"));     // who is running the audit, printed in the report as typed
            if (user == null) return;

            MachineProfile profile = ProfileSelector.Select(args, log.Add);
            if (profile == null) return;

            var report = new RunReport(profile) { UserName = user, StartedAt = started };
            List<Step> steps = Steps(cfg, profile, report, log.Add);

            bool? runAll = ChooseMode(steps);
            if (runAll == null) return;

            // "Scan again" closes the window and runs once more with the same operator, profile and run mode
            while (true)
            {
                var window = new ScanWindow(report, steps.Select(s => s.Module), log,
                                            w => RunSteps(w, steps, runAll.Value, report, cfg, log));
                Application.Run(window);
                if (!window.RescanRequested) return;

                log = new RunLog();
                report = new RunReport(profile) { UserName = user, StartedAt = DateTime.Now };
                steps = Steps(cfg, profile, report, log.Add);
            }
        }

        /// <summary>The modules in the order they run.</summary>
        private static List<Step> Steps(ScanConfig cfg, MachineProfile profile, RunReport report, Action<string> log) => new List<Step>
        {
            // 0. Windows golden-image spec (read only: OS, locale, Defender, NIC settings, drivers, apps, Chinese file names)
            new Step { Module = Module.WindowsSpec, Question = L.T("prompt.spec"),
                       Work = () => report.WindowsSpec = new WindowsSpecChecker(cfg, new WindowsSpecConfig(), profile, log).Run() },

            // 1. every device the profile lists, at the address the profile gives
            new Step { Module = Module.Devices, Work = () => new ProfileVerifier(cfg, profile, log).Run(report) },

            // 2. find everything on the network / COM ports, not just what the profile lists
            new Step { Module = Module.Discovery, Question = L.T("prompt.discovery"), Work = () => Discover(cfg, profile, report, log) },

            // 3. homing
            new Step { Module = Module.Homing, Caution = true, Available = profile.HomePrograms.Count > 0,
                       Question = L.T("prompt.home", string.Join(", ", profile.HomePrograms)),
                       Work = () => report.Home = new ZmcHoming(cfg, profile, log).Run() },

            // 4. discrete IO loopback
            new Step { Module = Module.Io, Caution = true, Available = profile.IoTests.Count > 0,
                       Question = L.T("prompt.io", profile.IoTests.Count), Work = () => RunIoTests(cfg, profile, report, log) },
        };

        /// <summary>
        /// Full run (no more questions) or one by one (asked before each module). A full run that moves the machine
        /// is confirmed once, listing the same warnings the single questions give. Null = cancelled.
        /// </summary>
        private static bool? ChooseMode(List<Step> steps)
        {
            int selected = 1;
            while (true)
            {
                selected = Dialogs.Choose(L.T("mode.question"), new[] { L.T("mode.all"), L.T("mode.each") }, selected);
                if (selected < 0) return null;
                if (selected == 1) return false;

                var moving = steps.Where(s => s.Available && s.Caution).Select(s => "• " + s.Question).ToList();
                if (moving.Count == 0 || Dialogs.Confirm(L.T("mode.allCaution") + "\n\n" + string.Join("\n\n", moving), caution: true))
                    return true;
            }
        }

        /// <summary>Runs on the scan window's worker thread.</summary>
        private static void RunSteps(ScanWindow window, List<Step> steps, bool runAll, RunReport report, ScanConfig cfg, RunLog log)
        {
            try
            {
                foreach (var step in steps)
                {
                    bool run = step.Available && (step.Question == null || runAll || window.Ask(step.Question, step.Caution));
                    if (run)
                    {
                        log.Add("");
                        log.Add($"===== {ModuleVerdicts.Title(step.Module)} =====");
                        window.Begin(step.Module);
                        var clock = Stopwatch.StartNew();
                        try { step.Work(); }
                        finally { report.Durations[step.Module] = clock.Elapsed; }
                    }
                    window.End(ModuleVerdicts.For(report, step.Module));
                    if (run && step.Module == Module.Discovery) RecheckDevices(window, report, cfg, log);
                }
            }
            catch (Exception ex)
            {
                // keep what ran so far: the report is still written
                log.Add(L.T("msg.error", ex));
                window.ShowError(L.T("msg.error", ex.Message));
            }

            ScanFiles files = ScanSummary.Save(report, cfg, log);
            window.Finish(ModuleVerdicts.Overall(ModuleVerdicts.All(report)), files);
        }

        /// <summary>
        /// The PC's NIC addresses are set to the profile subnets before the sweep (so it can reach them), and COM port
        /// numbers after the COM scan (it tells which device sits on which port). Devices are never changed.
        /// </summary>
        private static void Discover(ScanConfig cfg, MachineProfile profile, RunReport report, Action<string> log)
        {
            var fixer = new PcAddressFixer(cfg, profile, log);
            fixer.FixNics(report);
            report.DiscoveredIp = new IpScanner(cfg, IpProbes.Build(cfg), log).Scan();
            report.DiscoveredCom = new ComScanner(cfg, ComProbes.Build(profile.TempSlaves), log).Scan();
            fixer.FixComPorts(report);
        }

        /// <summary>After discovery changed a PC address, the Devices verdict is redone so it shows the state now.</summary>
        private static void RecheckDevices(ScanWindow window, RunReport report, ScanConfig cfg, RunLog log)
        {
            if (!report.AddressFixes.Any(f => f.Status != CheckStatus.Fail)) return;
            log.Add("");
            log.Add($"===== {ModuleVerdicts.Title(Module.Devices)} ({L.T("ui.recheck")}) =====");
            window.Begin(Module.Devices);
            var clock = Stopwatch.StartNew();
            report.Devices.Clear();
            try { new ProfileVerifier(cfg, report.Profile, log.Add).Run(report); }
            finally { report.Durations[Module.Devices] = (report.Durations.TryGetValue(Module.Devices, out var t) ? t : TimeSpan.Zero) + clock.Elapsed; }
            window.End(ModuleVerdicts.For(report, Module.Devices));
        }

        private static void RunIoTests(ScanConfig cfg, MachineProfile profile, RunReport report, Action<string> log)
        {
            using (var zmc = new ZmcConnection())
            {
                if (!zmc.Open(profile.ZmcIp)) { report.IoError = L.T("msg.zmcOpenFail", profile.ZmcIp); return; }

                if (!new ZmcInit(cfg, profile.InitProgram, log).EnsureReady(zmc))
                    log(L.T("msg.zmcNotInit"));

                report.Io.AddRange(new ZmcDiscreteIo(cfg, log).RunAll(zmc, profile.IoTests));
                report.IoRun = true;
            }
        }
    }
}
