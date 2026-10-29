using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DeviceScanner.Reporting;
using DeviceScanner.Utils;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// The fix for what the Windows spec found missing, installed from files shipped next to the exe:
    ///   driver INFs reported MISSING  -> "pnputil /add-driver /install" from the driver package
    ///                                    (a zip of DriverStore\FileRepository folders, WindowsSpecConfig.DriverPackage)
    ///   Visual C++ x64 runtime        -> vc_redist.x64.exe /install /quiet /norestart
    /// Only ever started by the operator; needs admin rights. Every result is confirmed by reading the PC again.
    /// </summary>
    internal static class SpecFixer
    {
        internal sealed class Outcome
        {
            public List<string> Installed { get; } = new List<string>();
            public List<string> Problems { get; } = new List<string>();     // "item: why", one line each
            public bool RebootNeeded { get; set; }
        }

        private const int RebootRequired = 3010;       // ERROR_SUCCESS_REBOOT_REQUIRED
        private const int NewerInstalled = 1638;       // ERROR_PRODUCT_VERSION: a newer runtime is already there

        /// <summary>INF names the driver store check reported MISSING.</summary>
        public static List<string> MissingDrivers(SpecReport spec) =>
            spec == null ? new List<string>() :
            spec.Items.Where(i => i.Group.StartsWith("Drivers:", StringComparison.Ordinal) && i.Actual == "MISSING")
                      .Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>A file shipped next to the exe, or null if it is not there.</summary>
        public static string NextToExe(string fileName)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            return File.Exists(path) ? path : null;
        }

        // ---------- drivers ----------

        public static void InstallDrivers(IEnumerable<string> infs, string package, Outcome outcome, Action<string> log)
        {
            string temp = Path.Combine(Path.GetTempPath(), "DeviceScanner_drivers_" + Guid.NewGuid().ToString("N"));
            try
            {
                log($"Extracting {package}");
                ZipFile.ExtractToDirectory(package, temp);

                // inf name -> every copy in the package (the store can hold several versions of one INF)
                var inPackage = Directory.EnumerateFiles(temp, "*.inf", SearchOption.AllDirectories)
                                         .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                                         .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (string inf in infs)
                {
                    if (!inPackage.TryGetValue(inf, out var copies))
                    {
                        log($"    {inf}: not in the driver package");
                        outcome.Problems.Add($"{inf}: {L.T("fix.notInPackage")}");
                        continue;
                    }

                    string error = null;
                    foreach (string path in copies)
                    {
                        log($"    pnputil /add-driver {Path.GetFileName(Path.GetDirectoryName(path))}\\{inf} /install");
                        int code = Run(Path.Combine(SystemDir(), "pnputil.exe"), $"/add-driver \"{path}\" /install", log, out string output);
                        if (code == RebootRequired) outcome.RebootNeeded = true;
                        else if (code != 0) error = $"pnputil exit code {code}" + (output.Length > 0 ? ": " + output : "");
                    }

                    // pnputil also returns non-zero when the package was added but no device uses it, so the store decides
                    if (DriverStoreCheck.InStore(inf)) outcome.Installed.Add(inf);
                    else outcome.Problems.Add($"{inf}: {error ?? L.T("fix.notAdded")}");
                }
            }
            catch (Exception ex)
            {
                log("    driver install stopped: " + ex.Message);
                outcome.Problems.Add(ex.Message);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        // ---------- Visual C++ runtime ----------

        public static void InstallVcRuntime(string installer, Version min, Outcome outcome, Action<string> log)
        {
            string name = VcRuntimeCheck.Name;
            try
            {
                log($"    {Path.GetFileName(installer)} /install /quiet /norestart");
                int code = Run(installer, "/install /quiet /norestart", log, out _);
                log($"        exit code {code}");
                if (code == RebootRequired) outcome.RebootNeeded = true;

                Version now = VcRuntimeCheck.Installed();
                if (now != null && now >= min) outcome.Installed.Add($"{name} {now}");
                else outcome.Problems.Add($"{name}: " + (code == 0 || code == RebootRequired || code == NewerInstalled
                                                             ? L.T("fix.notAdded") : $"vc_redist exit code {code}"));
            }
            catch (Exception ex)
            {
                log("    VC++ runtime install stopped: " + ex.Message);
                outcome.Problems.Add($"{name}: {ex.Message}");
            }
        }

        // ---------- NIC advanced + power management settings ----------

        /// <summary>
        /// Sets each failed advanced property with Set-NetAdapterAdvancedProperty (by registry keyword, so it works with
        /// a Chinese driver UI) and unchecks the power-management boxes through root\WMI; each adapter is restarted once
        /// so the advanced values take effect (its link drops for a few seconds). Every value is read back.
        /// </summary>
        public static void FixNicSettings(IEnumerable<NicSettingFix> fixes, Outcome outcome, Action<string> log)
        {
            foreach (var adapter in fixes.GroupBy(f => f.Connection))
            {
                string n = Ps(adapter.Key);
                var script = new StringBuilder("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'\n");
                var advanced = adapter.Where(f => f.Keyword != null).ToList();
                foreach (var f in advanced)
                    script.Append($"try {{ Set-NetAdapterAdvancedProperty -Name {n} -RegistryKeyword {Ps(f.Keyword)} -RegistryValue {Ps(f.Value)} -NoRestart; " +
                                  $"'A|{f.Keyword}|' + ((Get-NetAdapterAdvancedProperty -Name {n} -RegistryKeyword {Ps(f.Keyword)}).RegistryValue -join ',') }} " +
                                  $"catch {{ 'E|{f.Keyword}|' + $_.Exception.Message }}\n");

                // "turn off" last: unchecking it greys out the two wake boxes
                foreach (var f in adapter.Where(x => x.PowerClass != null).OrderBy(x => x.PowerClass == "MSPower_DeviceEnable" ? 1 : 0))
                {
                    string match = f.PowerClass.StartsWith("MSPower_", StringComparison.Ordinal)
                        ? $"$_.InstanceName.StartsWith({Ps(f.PowerInstance)}, [StringComparison]::OrdinalIgnoreCase)"
                        : $"$_.InstanceName -eq {Ps(f.PowerInstance)}";
                    script.Append($"try {{ $i = @(Get-CimInstance -Namespace root/wmi -ClassName {f.PowerClass} | Where-Object {{ {match} }}); " +
                                  $"if ($i.Count -eq 0) {{ throw 'no {f.PowerClass} instance for this adapter' }}; " +
                                  $"foreach ($x in $i) {{ $x.{f.PowerProperty} = $false; Set-CimInstance -InputObject $x }}; " +
                                  $"'P|{f.PowerClass}|' + ((@(Get-CimInstance -Namespace root/wmi -ClassName {f.PowerClass} | Where-Object {{ {match} }}) | ForEach-Object {{ $_.{f.PowerProperty} }}) -join ',') }} " +
                                  $"catch {{ 'E|{f.PowerClass}|' + $_.Exception.Message }}\n");
                }

                if (advanced.Count > 0) script.Append($"try {{ Restart-NetAdapter -Name {n}; 'R|restart|ok' }} catch {{ 'E|restart|' + $_.Exception.Message }}\n");

                log($"    {adapter.Key}: {adapter.Count()} setting(s)");
                Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString())), log, out _, out List<string> lines);

                // results by keyword / WMI class
                var result = lines.Select(l => l.Split(new[] { '|' }, 3)).Where(p => p.Length == 3)
                                  .GroupBy(p => p[1]).ToDictionary(g => g.Key, g => g.Last());
                foreach (var f in adapter)
                {
                    string key = f.Keyword ?? f.PowerClass;
                    string want = f.Keyword != null ? f.Value : "False";
                    if (!result.TryGetValue(key, out var r)) { outcome.Problems.Add($"{f}: no result from PowerShell"); continue; }
                    if (r[0] == "E") { outcome.Problems.Add($"{f}: {r[2]}"); continue; }
                    if (r[2].Split(',').All(v => string.Equals(v.Trim(), want, StringComparison.OrdinalIgnoreCase))) outcome.Installed.Add(f.ToString());
                    else outcome.Problems.Add($"{f}: still {r[2]} (wanted {want})");
                }
                if (result.TryGetValue("restart", out var restart) && restart[0] == "E")
                    outcome.Problems.Add($"{adapter.Key}: restart the adapter (or the PC) to apply: {restart[2]}");
            }
        }

        /// <summary>A PowerShell single-quoted string literal.</summary>
        private static string Ps(string s) => "'" + (s ?? "").Replace("'", "''") + "'";

        // ---------- helpers ----------

        /// <summary>System32 as a 64-bit process sees it (a 32-bit process is redirected to SysWOW64, which has no pnputil).</summary>
        private static string SystemDir() =>
            Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                : Environment.SystemDirectory;

        /// <summary>Runs a tool and logs its output; its last output line comes back in <paramref name="output"/>.</summary>
        private static int Run(string file, string args, Action<string> log, out string output) => Run(file, args, log, out output, out _);

        private static int Run(string file, string args, Action<string> log, out string output, out List<string> lines)
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = oem, StandardErrorEncoding = oem,
            };
            using (var p = Process.Start(psi))
            {
                var stderr = p.StandardError.ReadToEndAsync();
                lines = p.StandardOutput.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                p.WaitForExit();
                lines.AddRange(stderr.Result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
                foreach (string l in lines) log("        " + l);
                output = lines.LastOrDefault() ?? "";
                return p.ExitCode;
            }
        }
    }
}
