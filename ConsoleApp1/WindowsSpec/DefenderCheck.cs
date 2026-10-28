using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// Windows Security:
    ///   sc query windefend                                   = RUNNING
    ///   App &amp; browser control > Check apps and files         = Off
    ///   App &amp; browser control > SmartScreen for MS Store apps = Off   (per user)
    ///   Virus &amp; threat protection > Exclusions > File        = *\OneDispenser.exe
    /// </summary>
    internal sealed class DefenderCheck : SpecCheck
    {
        private const string Group = "Defender";

        public DefenderCheck(WindowsSpecConfig spec, Action<string> log) : base(spec, log) { }

        public override string Title => "Windows Defender / SmartScreen";

        protected override void Execute()
        {
            ServiceRunning();
            CheckAppsAndFiles();
            StoreAppsSmartScreen();
            OneDispenserExclusion();
        }

        // sc query windefend
        private void ServiceRunning()
        {
            try
            {
                using (var sc = new ServiceController(Spec.DefenderService))
                {
                    var st = sc.Status;
                    Add(Group, $"Service {Spec.DefenderService}", "Running", st.ToString(), PassIf(st == ServiceControllerStatus.Running));
                }
            }
            catch (InvalidOperationException)
            {
                Add(Group, $"Service {Spec.DefenderService}", "Running", "service not installed", CheckStatus.Fail);
            }
        }

        // "Check apps and files" = Explorer SmartScreen. Group policy overrides the user setting.
        private void CheckAppsAndFiles()
        {
            const string name = "Check apps and files";
            using (var pol = Reg.Hklm(@"SOFTWARE\Policies\Microsoft\Windows\System"))
            {
                int? enabled = Reg.Int(pol, "EnableSmartScreen");
                if (enabled.HasValue)
                {
                    Add(Group, name, "Off", enabled == 0 ? "Off (policy)" : "On (policy)", PassIf(enabled == 0),
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System EnableSmartScreen");
                    return;
                }
            }

            using (var k = Reg.Hklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"))
            {
                string v = Reg.Str(k, "SmartScreenEnabled");            // Off / Warn / Prompt / RequireAdmin ; missing = default (on)
                Add(Group, name, "Off", v ?? "not set (default = Warn)",
                    PassIf(string.Equals(v, "Off", StringComparison.OrdinalIgnoreCase)),
                    @"HKLM\...\Explorer SmartScreenEnabled");
            }
        }

        // "SmartScreen for Microsoft Store apps" is a per-user setting: check every loaded user hive.
        private void StoreAppsSmartScreen()
        {
            const string name = "SmartScreen for MS Store apps";
            const string path = @"Software\Microsoft\Windows\CurrentVersion\AppHost";

            var sids = Reg.LoadedUserSids().ToList();
            if (sids.Count == 0)
            {
                Add(Group, name, "Off", "no user hive loaded", CheckStatus.Unverified, "log on as the machine user and run again");
                return;
            }

            foreach (string sid in sids)
            {
                using (var k = Reg.User(sid, path))
                {
                    int? v = Reg.Int(k, "EnableWebContentEvaluation");     // 0 = off ; missing/1 = on
                    Add(Group, $"{name} [{Reg.SidToName(sid)}]", "Off",
                        v == 0 ? "Off" : v.HasValue ? "On" : "not set (default = On)",
                        PassIf(v == 0));
                }
            }
        }

        // Exclusions > File > *\OneDispenser.exe
        private void OneDispenserExclusion()
        {
            string want = Spec.DefenderFileExclusion;
            string exe = want.Substring(want.LastIndexOf('\\') + 1);   // OneDispenser.exe
            const string name = "File exclusion";

            List<string> paths = ReadExclusions("ExclusionPath", @"Paths", out string err1);
            List<string> procs = ReadExclusions("ExclusionProcess", @"Processes", out string err2);

            if (paths == null)
            {
                Add(Group, name, want, "unknown", CheckStatus.Unverified, err1 ?? "exclusions not readable");
                return;
            }

            if (paths.Any(p => Same(p, want)))
            {
                Add(Group, name, want, want, CheckStatus.Pass);
                return;
            }

            // close but not what the spec says
            var nearPath = paths.Where(p => p.EndsWith("\\" + exe, StringComparison.OrdinalIgnoreCase)).ToList();
            var nearProc = (procs ?? new List<string>()).Where(p => p.EndsWith(exe, StringComparison.OrdinalIgnoreCase)).ToList();

            if (nearPath.Count > 0)
                Add(Group, name, want, string.Join("; ", nearPath), CheckStatus.Fail, "only a fixed path is excluded; other OneDispenser.exe copies are not");
            else if (nearProc.Count > 0)
                Add(Group, name, want, "process: " + string.Join("; ", nearProc), CheckStatus.Unverified, "excluded as Process, spec says File");
            else
                Add(Group, name, want, "not excluded", CheckStatus.Fail, $"{paths.Count} other path exclusion(s)");
        }

        /// <summary>
        /// MSFT_MpPreference (same as Get-MpPreference) first; registry as fallback. Both need admin on 21H2.
        /// Returns null if neither could be read.
        /// </summary>
        private static List<string> ReadExclusions(string wmiProperty, string regSubKey, out string error)
        {
            var rows = Wmi.Query(@"\\.\root\Microsoft\Windows\Defender", $"SELECT {wmiProperty} FROM MSFT_MpPreference", out error);
            if (error == null && rows.Count > 0)
            {
                var arr = rows[0][wmiProperty] as string[];
                if (arr == null) return new List<string>();
                if (!arr.Any(a => a.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)))   // "N/A: Must be an administrator to view exclusions"
                    return arr.Select(a => a.Trim()).ToList();
                error = "access denied (run as administrator)";
            }

            var list = new List<string>();
            bool readAny = false;
            foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows Defender\Exclusions\", @"SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\" })
            {
                try
                {
                    using (var k = Reg.Hklm(root + regSubKey))
                    {
                        if (k == null) continue;
                        readAny = true;
                        list.AddRange(k.GetValueNames().Select(n => n.Trim()));
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    error = "access denied (run as administrator)";
                }
            }
            return readAny ? list : null;
        }

        private static bool Same(string a, string b) =>
            string.Equals(a?.Trim().TrimEnd('\\'), b, StringComparison.OrdinalIgnoreCase);
    }
}
