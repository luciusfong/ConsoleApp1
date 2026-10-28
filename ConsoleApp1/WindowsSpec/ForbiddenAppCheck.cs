using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DeviceScanner.Reporting;
using Microsoft.Win32;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// WPS Office / WinRAR / RARLAB (Chinese builds) must not be installed. REPORT ONLY: nothing is uninstalled.
    /// Reads the Uninstall keys (64-bit, 32-bit, and every loaded user hive, because WPS installs per user),
    /// plus the usual install folders in case the uninstall entry was removed but the files were left behind.
    /// </summary>
    internal sealed class ForbiddenAppCheck : SpecCheck
    {
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public ForbiddenAppCheck(WindowsSpecConfig spec, Action<string> log) : base(spec, log) { }

        public override string Title => "Forbidden apps (WPS, WinRAR, RARLAB)";

        private sealed class AppEntry
        {
            public string Name, Publisher, Version, Location, Scope;
            public bool Chinese;
            public string ChineseWhy;
        }

        protected override void Execute()
        {
            const string group = "Apps";
            var patterns = Spec.ForbiddenAppPatterns.Select(p => new Regex(p, RegexOptions.IgnoreCase)).ToArray();

            var found = new List<AppEntry>();
            foreach (var e in AllUninstallEntries())
                if (patterns.Any(p => p.IsMatch(e.Name ?? "") || p.IsMatch(e.Publisher ?? "")))
                    found.Add(e);

            int problems = 0;
            foreach (var e in found)
            {
                problems++;
                ClassifyChinese(e);
                CheckStatus st = !Spec.ForbiddenAppsChineseOnly || e.Chinese ? CheckStatus.Fail : CheckStatus.Unverified;
                Add(group, e.Name, "not installed",
                    $"installed {e.Version} ({e.Publisher}) [{e.Scope}]",
                    st,
                    (e.Chinese ? "Chinese build: " + e.ChineseWhy : "Chinese build not detected") +
                    (string.IsNullOrEmpty(e.Location) ? "" : "; " + e.Location));
            }

            // leftovers without an uninstall entry
            foreach (string dir in LeftoverFolders())
                if (!found.Any(f => !string.IsNullOrEmpty(f.Location) &&
                                    (dir.StartsWith(f.Location.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                                     f.Location.StartsWith(dir, StringComparison.OrdinalIgnoreCase))))
                {
                    problems++;
                    Add(group, "Leftover folder", "absent", dir, CheckStatus.Fail, "folder exists without an uninstall entry");
                }

            if (problems == 0) Add(group, "WPS / WinRAR / RARLAB", "not installed", "none found", CheckStatus.Pass);
        }

        // ---------- sources ----------

        private IEnumerable<AppEntry> AllUninstallEntries()
        {
            foreach (var e in Read(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), UninstallPath, "machine 64-bit")) yield return e;
            foreach (var e in Read(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), UninstallPath, "machine 32-bit")) yield return e;

            foreach (string sid in Reg.LoadedUserSids())
            {
                var hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
                foreach (var e in Read(hku, sid + "\\" + UninstallPath, "user " + Reg.SidToName(sid))) yield return e;
            }
        }

        private static IEnumerable<AppEntry> Read(RegistryKey baseKey, string path, string scope)
        {
            var list = new List<AppEntry>();
            using (baseKey)
            using (var root = baseKey.OpenSubKey(path, false))
            {
                if (root == null) return list;
                foreach (string sub in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(sub, false))
                        {
                            string name = Reg.Str(k, "DisplayName");
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            int lcid = Reg.Int(k, "Language") ?? 0;     // MSI installer language
                            bool zhLcid = lcid == 2052 || lcid == 1028 || lcid == 3076 || lcid == 4100;
                            list.Add(new AppEntry
                            {
                                Name = name.Trim(),
                                Publisher = Reg.Str(k, "Publisher") ?? "",
                                Version = Reg.Str(k, "DisplayVersion") ?? "",
                                Location = Reg.Str(k, "InstallLocation") ?? DirOf(Reg.Str(k, "DisplayIcon")) ?? "",
                                Scope = scope,
                                ChineseWhy = zhLcid ? $"installer language LCID {lcid}" : null,
                            });
                        }
                    }
                    catch { /* unreadable entry: skip */ }
                }
            }
            return list;
        }

        private static IEnumerable<string> LeftoverFolders()
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Kingsoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Kingsoft"),
            };

            // WPS per-user install: C:\Users\<user>\AppData\Local\Kingsoft\WPS Office
            string users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            try
            {
                foreach (string profile in Directory.GetDirectories(users))
                    candidates.Add(Path.Combine(profile, @"AppData\Local\Kingsoft"));
            }
            catch { }

            return candidates.Where(Directory.Exists);
        }

        // ---------- Chinese build detection ----------

        private static void ClassifyChinese(AppEntry e)
        {
            var why = new List<string>();
            if (!string.IsNullOrEmpty(e.ChineseWhy)) why.Add(e.ChineseWhy);
            if (Cjk.HasChinese(e.Name) || Cjk.HasChinese(e.Publisher) || Cjk.HasChinese(e.Location)) why.Add("Chinese text in uninstall entry");
            if (Regex.IsMatch(e.Name, @"\b(SC|Chinese|zh[-_]?CN)\b|\u7B80\u4F53|\u4E2D\u6587", RegexOptions.IgnoreCase)) why.Add("name says Chinese");

            if (!string.IsNullOrEmpty(e.Location) && Directory.Exists(e.Location))
            {
                try
                {
                    // WPS keeps UI languages in ...\office6\mui\zh_CN
                    if (Directory.EnumerateDirectories(e.Location, "zh_CN", SearchOption.AllDirectories).Any())
                        why.Add(@"zh_CN language folder in install dir");
                    // Chinese WinRAR (winrar-x64-xxxsc.exe) ships Chinese-named files
                    if (Directory.EnumerateFiles(e.Location).Any(f => Cjk.HasChinese(Path.GetFileName(f))))
                        why.Add("Chinese-named files in install dir");
                }
                catch { }
            }

            e.Chinese = why.Count > 0;
            e.ChineseWhy = string.Join(", ", why);
        }

        private static string DirOf(string displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon)) return null;
            string p = displayIcon.Split(',')[0].Trim('"', ' ');
            try { return Path.GetDirectoryName(p); } catch { return null; }
        }
    }
}
