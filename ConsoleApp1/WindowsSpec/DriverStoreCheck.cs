using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// Required driver INFs are in the driver store (what "pnputil /enum-drivers" calls Original Name),
    /// grouped by the module that needs them (WindowsSpecConfig.DriverModules). A module is only required
    /// when the selected profile uses it; otherwise it is reported as N/A.
    /// Reads DriverStore\FileRepository\&lt;inf&gt;_&lt;arch&gt;_&lt;hash&gt; folder names, so it is language
    /// independent and needs no admin. Also lists devices that have a driver problem (yellow bang).
    /// </summary>
    internal sealed class DriverStoreCheck : SpecCheck
    {
        private readonly MachineProfile _profile;

        public DriverStoreCheck(WindowsSpecConfig spec, MachineProfile profile, Action<string> log) : base(spec, log)
        {
            _profile = profile;
        }

        public override string Title => "Drivers (driver store, by module)";

        protected override void Execute()
        {
            var store = ReadStore(out string error);
            if (store == null)
            {
                Add("Drivers", "driver store", "readable", "error", CheckStatus.Unverified, error);
                ProblemDevices();
                return;
            }

            var uses = ProfileUses();
            Log($"    profile '{_profile?.Name ?? "(none)"}' uses: {string.Join(", ", uses.OrderBy(u => u))}");

            int required = 0, missing = 0;
            foreach (var m in Spec.DriverModules)
            {
                string group = "Drivers: " + m.Name;
                string why = WhyRequired(m, uses);

                if (why == null)
                {
                    int present = m.Drivers.Count(store.ContainsKey);
                    Add(group, "module", "used by profile", "N/A", CheckStatus.Pass,
                        $"not used by profile ({string.Join(" / ", m.UsedBy)}); {present}/{m.Drivers.Length} drivers present anyway");
                    continue;
                }

                foreach (string inf in m.Drivers)
                {
                    required++;
                    if (store.TryGetValue(inf, out var dirs))
                    {
                        string versions = string.Join(" | ", dirs.Select(d => DriverVer(Path.Combine(d, inf))).Distinct());
                        Add(group, inf, "in driver store", "present", CheckStatus.Pass, versions);
                    }
                    else
                    {
                        missing++;
                        Add(group, inf, "in driver store", "MISSING", CheckStatus.Fail, "required by " + why);
                    }
                }
            }
            Log($"    {required - missing}/{required} required drivers present");

            ProblemDevices();
        }

        // ---------- which modules the profile uses ----------

        /// <summary>Tokens the profile uses: device names, plus "com" and "gantry".</summary>
        private HashSet<string> ProfileUses()
        {
            var uses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_profile == null) return uses;

            if (!string.IsNullOrWhiteSpace(_profile.ZmcIp)) uses.Add("ZMC");
            foreach (var d in _profile.ExpectedIp) uses.Add(d.Name);
            foreach (var d in _profile.ExpectedCom) uses.Add(d.Name);
            if (_profile.ExpectedCom.Count > 0) uses.Add("com");
            if (_profile.HomePrograms.Count > 0) uses.Add("gantry");
            return uses;
        }

        /// <summary>Why the module is required (for the FAIL detail), or null if the profile does not use it.</summary>
        private static string WhyRequired(DriverModule m, HashSet<string> uses)
        {
            if (m.UsedBy.Contains("*")) return "every machine";
            var hit = m.UsedBy.Where(uses.Contains).ToList();
            if (hit.Count == 0) return null;
            return string.Join(", ", hit.Select(h =>
                h.Equals("com", StringComparison.OrdinalIgnoreCase) ? "COM devices in profile" :
                h.Equals("gantry", StringComparison.OrdinalIgnoreCase) ? "gantry homing in profile" : h));
        }

        // ---------- driver store ----------

        /// <summary>inf name -> FileRepository folders holding it (several versions can be staged). null = not readable.</summary>
        private static Dictionary<string, List<string>> ReadStore(out string error)
        {
            error = null;
            string repo = Path.Combine(Environment.SystemDirectory, @"DriverStore\FileRepository");
            var store = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(repo))
                {
                    string folder = Path.GetFileName(dir);
                    int i = folder.IndexOf(".inf_", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    string inf = folder.Substring(0, i + 4);
                    if (!store.TryGetValue(inf, out var l)) store[inf] = l = new List<string>();
                    l.Add(dir);
                }
                return store;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>DriverVer = date,version from the INF's [Version] section.</summary>
        private static string DriverVer(string infPath)
        {
            try
            {
                foreach (string line in File.ReadLines(infPath))      // INFs are ANSI or UTF-16 with BOM; both detected
                {
                    var m = Regex.Match(line, @"^\s*DriverVer\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (m.Success) return m.Groups[1].Value.Trim();
                }
            }
            catch { }
            return "version ?";
        }

        /// <summary>Devices Device Manager shows with a warning (code 28 = no driver, 10 = cannot start, ...).</summary>
        private void ProblemDevices()
        {
            const string group = "Devices";
            var rows = Wmi.Query(@"\\.\root\cimv2",
                "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0", out string err);
            if (err != null) { Add(group, "Problem devices", "none", "unknown", CheckStatus.Unverified, err); return; }

            // code 22 = disabled by the user: report but do not fail
            int count = 0;
            foreach (var o in rows)
            {
                uint code = Convert.ToUInt32(o["ConfigManagerErrorCode"] ?? 0u);
                string name = o["Name"] as string ?? "(unknown device)";
                string pnp = o["PNPDeviceID"] as string ?? "";
                count++;
                Add(group, name, "working", $"error code {code}", code == 22 ? CheckStatus.Unverified : CheckStatus.Fail, pnp);
            }
            if (count == 0) Add(group, "Problem devices", "none", "none", CheckStatus.Pass);
        }
    }
}
