using DeviceScanner.Profiles;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DeviceScanner.Reporting
{
    internal sealed class PdfReport
    {
        private readonly ScanConfig _cfg;
        private readonly MachineProfile _profile;

        public PdfReport(ScanConfig cfg, MachineProfile profile)
        {
            _cfg = cfg;
            _profile = profile;
        }

        public string SaveSummaryPdf(string summaryText)
        {
            Directory.CreateDirectory(_cfg.ReportDir);

            string path = Path.Combine(
                _cfg.ReportDir,
                $"{_profile.Name}_{DateTime.Now:yyyyMMddHHmmss}_ScanSummary.pdf");

            var props = new WriterProperties()
                .SetStandardEncryption(
                    null,
                    Encoding.UTF8.GetBytes(_cfg.Password),
                    EncryptionConstants.ALLOW_PRINTING,
                    EncryptionConstants.ENCRYPTION_AES_256);

            using (var writer = new PdfWriter(path, props))
            using (var pdf = new PdfDocument(writer))
            using (var doc = new Document(pdf))
            {
                doc.Add(new Paragraph(summaryText)
                    .SetFontSize(9));
            }

            return path;
        }
    }

    internal static class ScanSummary
    {
        public static void Print(
            RunReport r,
            ScanConfig cfg,
            Action<string> log)
        {
            int failures = 0;
            int warnings = 0;

            var notRun = new List<string>();
            var lines = new List<string>();

            Action<string> reportLog = s =>
            {
                log(s);
                lines.Add(s);
            };

            reportLog($"");
            reportLog($"===== SUMMARY: {r.Profile.Name} =====");

            // ---------- Windows spec ----------

            reportLog("");
            reportLog("-- Windows spec (golden image, check only) --");

            if (r.WindowsSpec == null)
            {
                reportLog("  NOT RUN");
                notRun.Add("Windows spec");
            }
            else
            {
                string lastGroup = null;
                foreach (var s in r.WindowsSpec.Items)
                {
                    if (s.Group != lastGroup)
                    {
                        reportLog($"  [{s.Group}]");
                        lastGroup = s.Group;
                    }

                    string line = $"    {Tag(s.Status),-5} {s.Name,-40} {s.Actual}";
                    if (s.Status != CheckStatus.Pass && s.Expected.Length > 0)
                        line += $"  (expected {s.Expected})";
                    if (s.Detail.Length > 0)
                        line += $"  {s.Detail}";
                    reportLog(line);

                    foreach (var n in s.Notes)
                        reportLog($"          {n}");
                }

                reportLog(
                    $"  Windows spec: {r.WindowsSpec.Items.Count - r.WindowsSpec.Failures - r.WindowsSpec.Warnings} passed, " +
                    $"{r.WindowsSpec.Failures} failed, {r.WindowsSpec.Warnings} warning(s) of {r.WindowsSpec.Items.Count}");

                failures += r.WindowsSpec.Failures;
                warnings += r.WindowsSpec.Warnings;
            }

            // ---------- devices ----------

            reportLog("");
            reportLog("-- Devices (from profile) --");

            foreach (var d in r.Devices)
            {
                reportLog(
                    $"  {Tag(d.Status),-5} {d.Kind,-3} {d.Address,-15} {d.Device,-14} {d.Detail}");

                if (d.Status == CheckStatus.Fail)
                    failures++;
                else if (d.Status == CheckStatus.Unverified)
                    warnings++;
            }

            // ---------- homing ----------

            reportLog("");
            reportLog("-- Gantry homing --");

            if (r.Home == null)
            {
                reportLog("  NOT RUN");
                notRun.Add("homing");
            }
            else
            {
                var h = r.Home;

                reportLog(
                    $"  {(h.Success ? "PASS" : "FAIL")}  {h.Programs}   axes: {string.Join(",", h.Axes)}   {h.Elapsed.TotalSeconds:F1} s");

                if (!h.Success)
                {
                    reportLog($"        {h.Detail}");
                    failures++;
                }
            }

            // ---------- IO ----------

            reportLog("");
            reportLog("-- Discrete IO --");

            if (!r.IoRun)
            {
                reportLog("  NOT RUN");
                notRun.Add("IO test");
            }
            else
            {
                int pass = 0;
                int fail = 0;
                int warn = 0;

                foreach (var t in r.Io)
                {
                    var c = t.Case;

                    string tag =
                        t.Passed
                            ? "PASS"
                            : c.Optional
                                ? "WARN"
                                : "FAIL";

                    if (t.Passed)
                        pass++;
                    else if (c.Optional)
                        warn++;
                    else
                        fail++;

                    string inText =
                        c.In.HasValue
                            ? c.In.Value.ToString()
                            : "?";

                    reportLog(
                        $"  {tag,-5} OP {c.Out,-3} -> IN {inText,-3} {c.Name,-30} {t.Detail}");
                }

                reportLog(
                    $"  IO: {pass} passed, {fail} failed, {warn} warning(s) of {r.Io.Count}");

                failures += fail;
                warnings += warn;
            }

            // ---------- discovery ----------

            if (r.DiscoveredIp != null || r.DiscoveredCom != null)
            {
                reportLog("");
                reportLog("-- Discovery (every device found, not limited to the profile) --");

                if (r.DiscoveredIp != null)
                {
                    foreach (var e in r.DiscoveredIp.Entries)
                        reportLog($"  IP   {e}");

                    string found = r.DiscoveredIp.ZmcIp;

                    if (!string.IsNullOrWhiteSpace(found) &&
                        found != r.Profile.ZmcIp)
                    {
                        reportLog(
                            $"  WARN ZMC found at {found} but the profile says {r.Profile.ZmcIp}");

                        warnings++;
                    }
                }

                if (r.DiscoveredCom != null)
                {
                    foreach (var f in r.DiscoveredCom.Found.OrderBy(x => x.Name))
                    {
                        reportLog(
                            $"  COM  {f.Name,-14} {f.Port}");
                    }

                    reportLog(
                        $"  COM  ITV count: {r.DiscoveredCom.Found.Count(x => x.Name == "ITV regulator")}");

                    if (r.DiscoveredCom.BusyPorts.Count > 0)
                    {
                        reportLog(
                            $"  COM  in use (device unknown): {string.Join(", ", r.DiscoveredCom.BusyPorts)}");
                    }
                }
            }

            // ---------- verdict ----------

            reportLog("");
            reportLog("===== RESULT =====");

            reportLog(
                failures == 0
                    ? "OVERALL: PASS"
                    : $"OVERALL: FAIL ({failures} failure(s))");

            if (warnings > 0)
                reportLog($"  {warnings} warning(s)");

            if (notRun.Count > 0)
                reportLog($"  not run: {string.Join(", ", notRun)}");

            // ---------- save PDF ----------

            try
            {
                var pdfReport = new PdfReport(cfg, r.Profile);

                string filePath =
                    pdfReport.SaveSummaryPdf(
                        string.Join(Environment.NewLine, lines));

                if (File.Exists(filePath))
                {
                    File.SetAttributes(
                        filePath,
                        File.GetAttributes(filePath) | FileAttributes.ReadOnly);

                    reportLog("");
                    reportLog($"Summary PDF saved to: {filePath}");
                }
                else
                {
                    reportLog("");
                    reportLog($"ERROR: PDF was not created: {filePath}");
                }
            }
            catch (Exception ex)
            {
                reportLog("");
                reportLog($"ERROR creating PDF: {ex.Message}");
            }
        }

        private static string Tag(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Pass:
                    return "PASS";

                case CheckStatus.Unverified:
                    return "WARN";

                default:
                    return "FAIL";
            }
        }
    }
}