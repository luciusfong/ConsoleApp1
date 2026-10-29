using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DeviceScanner.Utils;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// The fix for the driver part of the Windows spec: installs the driver INFs the check found MISSING from the
    /// driver package (a zip of DriverStore\FileRepository folders, WindowsSpecConfig.DriverPackage) with
    /// "pnputil /add-driver /install". Only ever started by the operator; needs admin rights.
    /// </summary>
    internal static class DriverInstaller
    {
        internal sealed class Outcome
        {
            public List<string> Installed { get; } = new List<string>();
            public List<string> Problems { get; } = new List<string>();     // "inf: why", one line each
            public bool RebootNeeded { get; set; }
        }

        private const int RebootRequired = 3010;   // ERROR_SUCCESS_REBOOT_REQUIRED

        /// <summary>INF names the driver store check reported MISSING.</summary>
        public static List<string> Missing(SpecReport spec) =>
            spec == null ? new List<string>() :
            spec.Items.Where(i => i.Group.StartsWith("Drivers:", StringComparison.Ordinal) && i.Actual == "MISSING")
                      .Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The driver package next to the exe, or null if it is not there.</summary>
        public static string FindPackage(WindowsSpecConfig spec)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, spec.DriverPackage);
            return File.Exists(path) ? path : null;
        }

        public static Outcome Install(IEnumerable<string> infs, string package, Action<string> log)
        {
            var outcome = new Outcome();
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
                        int code = PnpUtil(path, log, out string output);
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
            log($"    {outcome.Installed.Count} driver(s) installed, {outcome.Problems.Count} problem(s)");
            return outcome;
        }

        /// <summary>Runs pnputil; its last output line (the result) comes back in <paramref name="output"/>.</summary>
        private static int PnpUtil(string infPath, Action<string> log, out string output)
        {
            // a 32-bit process sees SysWOW64 as System32, and there is no 32-bit pnputil
            string system = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                : Environment.SystemDirectory;
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

            var psi = new ProcessStartInfo(Path.Combine(system, "pnputil.exe"), $"/add-driver \"{infPath}\" /install")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = oem, StandardErrorEncoding = oem,
            };
            using (var p = Process.Start(psi))
            {
                var stderr = p.StandardError.ReadToEndAsync();
                var lines = p.StandardOutput.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                p.WaitForExit();
                lines.AddRange(stderr.Result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
                foreach (string l in lines) log("        " + l);
                output = lines.LastOrDefault() ?? "";
                return p.ExitCode;
            }
        }
    }
}
