using System;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// Runs every Windows spec check against the golden image (WindowsSpecConfig).
    /// CHECK ONLY: nothing is deleted, uninstalled or changed. Run as administrator for complete results
    /// (dism, Defender exclusions and the NIC power-management tab need it; otherwise those show WARN).
    /// </summary>
    internal sealed class WindowsSpecChecker
    {
        private readonly ScanConfig _cfg;
        private readonly WindowsSpecConfig _spec;
        private readonly MachineProfile _profile;     // decides which driver modules are required
        private readonly Action<string> _log;

        public WindowsSpecChecker(ScanConfig cfg, WindowsSpecConfig spec, MachineProfile profile, Action<string> log)
        {
            _cfg = cfg; _spec = spec; _profile = profile; _log = log;
        }

        public SpecReport Run()
        {
            _log("\n--- Windows spec check (read only) ---");
            var report = new SpecReport();

            bool admin = Elevation.IsAdmin();
            report.Items.Add(new SpecItem
            {
                Group = "Run", Name = "Administrator", Expected = "yes", Actual = admin ? "yes" : "no",
                Status = admin ? CheckStatus.Pass : CheckStatus.Unverified,
                Detail = admin ? "" : "some checks cannot be verified; right-click > Run as administrator",
            });
            if (!admin) _log("  WARNING: not running as administrator; some checks will show WARN.");

            SpecCheck[] checks =
            {
                new OsVersionCheck(_spec, _log),
                new LocaleCheck(_spec, _log),
                new DefenderCheck(_spec, _log),
                new NicSettingsCheck(_spec, _log),
                new DriverStoreCheck(_spec, _profile, _log),
                new VcRuntimeCheck(_spec, _log),
                new ForbiddenAppCheck(_spec, _log),
                new ChineseNameCheck(_spec, _cfg.ReportDir, _log),   // slowest, last
            };
            foreach (var c in checks) c.Run(report);

            _log($"\n  Windows spec: {report.Failures} failure(s), {report.Warnings} warning(s) of {report.Items.Count} checks");
            return report;
        }
    }
}
