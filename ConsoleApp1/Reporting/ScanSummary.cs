using DeviceScanner.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeviceScanner.Reporting
{
    /// <summary>Closing part of a run: overall verdict in the console, then the PDF report and the full log next to it.</summary>
    internal static class ScanSummary
    {
        /// <summary>Prints the verdict and saves the reports. Returns the PDF path, or null if it could not be written.</summary>
        public static string Print(RunReport r, ScanConfig cfg, ConsoleProgress ui)
        {
            DateTime finished = DateTime.Now;
            var modules = ModuleVerdicts.All(r);
            var verdict = ModuleVerdicts.Overall(modules);

            ui.Line("");
            ui.Line(L.T("sum.result"));
            ui.Line(verdict.Passed ? L.T("sum.overallPass") : L.T("sum.overallFail", verdict.Failures),
                    verdict.Passed ? ConsoleColor.Green : ConsoleColor.Red);
            if (verdict.Warnings > 0)
                ui.Line(L.T("sum.warnings", verdict.Warnings), ConsoleColor.Yellow);
            if (verdict.NotRun.Count > 0)
                ui.Line(L.T("sum.notRunList", string.Join(", ", verdict.NotRun)), ConsoleColor.DarkGray);

            string stamp = $"{r.Profile.Name}_{finished:yyyyMMddHHmmss}";
            try { Directory.CreateDirectory(cfg.ReportDir); }
            catch (Exception ex) { ui.Line(L.T("sum.pdfError", ex.Message), ConsoleColor.Red); return null; }

            string pdfPath = SavePdf(Path.Combine(cfg.ReportDir, stamp + "_ScanSummary.pdf"), r, cfg, modules, finished, ui);

            // the console only showed a spinner, so the full log is kept for troubleshooting
            string logPath = Path.Combine(cfg.ReportDir, stamp + "_ScanLog.txt");
            try
            {
                ui.Line(L.T("sum.logSaved", Path.GetFullPath(logPath)));
                File.WriteAllLines(logPath, ui.Transcript, new UTF8Encoding(true));    // BOM so Notepad shows Chinese
            }
            catch (Exception ex) { ui.Line(L.T("sum.logError", ex.Message), ConsoleColor.Red); }

            return pdfPath;
        }

        private static string SavePdf(string path, RunReport r, ScanConfig cfg, IList<ModuleResult> modules,
                                      DateTime finished, ConsoleProgress ui)
        {
            try
            {
                new PdfReport(cfg).Save(path, r, modules, finished);

                if (!File.Exists(path))
                {
                    ui.Line(L.T("sum.pdfMissing", path), ConsoleColor.Red);
                    return null;
                }

                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                ui.Line("");
                ui.Line(L.T("sum.pdfSaved", Path.GetFullPath(path)));
                return Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                ui.Line("");
                ui.Line(L.T("sum.pdfError", ex.Message), ConsoleColor.Red);
                return null;
            }
        }
    }
}
