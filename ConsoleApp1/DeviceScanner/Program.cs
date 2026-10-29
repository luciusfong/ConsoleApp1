using DeviceScanner.Motion;
using DeviceScanner.Native;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;
using DeviceScanner.Scanning;
using DeviceScanner.Utils;
using DeviceScanner.WindowsSpec;
using System;
using System.Diagnostics;


namespace DeviceScanner
{
    internal static class Program
    {
        // usage: DeviceScanner.exe [profile file or name] [--lang en|zh]     (no profile = use the Profiles folder; no --lang = ask)
        [STAThread]                                // the Windows dialogs need an STA thread
        private static void Main(string[] args)
        {
            var ui = new ConsoleProgress();
            try { Run(args, new ScanConfig(), ui); }
            catch (Exception ex)
            {
                ui.Line(L.T("msg.error", ex), ConsoleColor.Red);
                Dialogs.Error(L.T("msg.error", ex.Message));
            }
        }

        private static void Run(string[] args, ScanConfig cfg, ConsoleProgress ui)
        {
            Action<string> log = ui.Log;           // every check logs here; the console shows it as a spinner

            DateTime started = DateTime.Now;
            args = L.Choose(args);                 // English / 中文 for the dialogs and the report
            if (args == null) return;

            string user = Dialogs.AskText(L.T("prompt.username"));     // who is running the audit, printed in the report as typed
            if (user == null) return;

            MachineProfile profile = ProfileSelector.Select(args, log);
            if (profile == null) return;

            var report = new RunReport(profile) { UserName = user, StartedAt = started };
            ui.Line("");

            // 0. Windows golden-image spec (read only: OS, locale, Defender, NIC settings, drivers, apps, Chinese file names)
            if (Dialogs.Confirm(L.T("prompt.spec")))
                RunModule(ui, report, Module.WindowsSpec, () => report.WindowsSpec = new WindowsSpecChecker(cfg, new WindowsSpecConfig(), profile, log).Run());
            else Skipped(ui, report, Module.WindowsSpec);

            // 1. every device the profile lists, at the address the profile gives
            RunModule(ui, report, Module.Devices, () => new ProfileVerifier(cfg, profile, log).Run(report));

            // 2. optional: find everything on the network / COM ports, not just what the profile lists
            if (Dialogs.Confirm(L.T("prompt.discovery")))
                RunModule(ui, report, Module.Discovery, () => Discover(cfg, profile, report, log));
            else Skipped(ui, report, Module.Discovery);

            // 3. homing
            if (profile.HomePrograms.Count > 0 && Dialogs.Confirm(L.T("prompt.home", string.Join(", ", profile.HomePrograms)), caution: true))
                RunModule(ui, report, Module.Homing, () => report.Home = new ZmcHoming(cfg, profile, log).Run());
            else Skipped(ui, report, Module.Homing);

            // 4. discrete IO loopback
            if (profile.IoTests.Count > 0 && Dialogs.Confirm(L.T("prompt.io", profile.IoTests.Count), caution: true))
                RunModule(ui, report, Module.Io, () => RunIoTests(cfg, profile, report, log));
            else Skipped(ui, report, Module.Io);

            string pdf = ScanSummary.Print(report, cfg, ui);
            Finish(report, pdf);
        }

        private static void RunModule(ConsoleProgress ui, RunReport report, Module module, Action work)
        {
            report.Durations[module] = ui.Run(ModuleVerdicts.Title(module), work);
            ui.Result(ModuleVerdicts.For(report, module));
        }

        private static void Skipped(ConsoleProgress ui, RunReport report, Module module) =>
            ui.Result(ModuleVerdicts.For(report, module));

        private static void Discover(ScanConfig cfg, MachineProfile profile, RunReport report, Action<string> log)
        {
            report.DiscoveredIp = new IpScanner(cfg, IpProbes.Build(cfg), log).Scan();
            report.DiscoveredCom = new ComScanner(cfg, ComProbes.Build(profile.TempSlaves), log).Scan();
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

        /// <summary>Closing dialog: the overall verdict, and an offer to open the PDF.</summary>
        private static void Finish(RunReport report, string pdf)
        {
            var verdict = ModuleVerdicts.Overall(ModuleVerdicts.All(report));
            string result = verdict.Passed ? L.T("sum.overallPass") : L.T("sum.overallFail", verdict.Failures);
            if (verdict.Warnings > 0) result += "\n" + L.T("sum.warnings", verdict.Warnings).Trim();

            if (pdf == null)
            {
                Dialogs.Error(result + "\n\n" + L.T("msg.noPdf"));
                return;
            }

            if (Dialogs.Confirm(result + "\n\n" + L.T("msg.openPdf"), yesByDefault: true))
            {
                try { Process.Start(pdf); }
                catch (Exception ex) { Dialogs.Error(L.T("sum.pdfError", ex.Message)); }
            }
        }
    }
}
