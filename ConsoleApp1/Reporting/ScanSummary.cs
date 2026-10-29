using DeviceScanner.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeviceScanner.Reporting
{
    /// <summary>Where the end-of-run files went. A path is null if that file could not be written; Error says why.</summary>
    internal sealed class ScanFiles
    {
        public string PdfPath { get; set; }
        public string LogPath { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Closing part of a run: the PDF report and the full log, saved side by side in the report folder.</summary>
    internal static class ScanSummary
    {
        public static ScanFiles Save(RunReport r, ScanConfig cfg, RunLog log)
        {
            DateTime finished = DateTime.Now;
            var modules = ModuleVerdicts.All(r);
            var verdict = ModuleVerdicts.Overall(modules);
            var files = new ScanFiles();

            log.Add("");
            log.Add(L.T("sum.result"));
            log.Add(verdict.Passed ? L.T("sum.overallPass") : L.T("sum.overallFail", verdict.Failures));
            if (verdict.Warnings > 0) log.Add(L.T("sum.warnings", verdict.Warnings));
            if (verdict.NotRun.Count > 0) log.Add(L.T("sum.notRunList", string.Join(", ", verdict.NotRun)));

            string stamp = $"{r.Profile.Name}_{finished:yyyyMMddHHmmss}";
            try { Directory.CreateDirectory(cfg.ReportDir); }
            catch (Exception ex)
            {
                files.Error = L.T("sum.pdfError", ex.Message);
                log.Add(files.Error);
                return files;
            }

            files.PdfPath = SavePdf(Path.Combine(cfg.ReportDir, stamp + "_ScanSummary.pdf"), r, cfg, modules, finished, log, files);

            // the window only shows the latest step, so the full log is kept for troubleshooting
            string logPath = Path.GetFullPath(Path.Combine(cfg.ReportDir, stamp + "_ScanLog.txt"));
            try
            {
                log.Add(L.T("sum.logSaved", logPath));
                File.WriteAllLines(logPath, log.Snapshot(), new UTF8Encoding(true));    // BOM so Notepad shows Chinese
                files.LogPath = logPath;
            }
            catch (Exception ex) { log.Add(L.T("sum.logError", ex.Message)); }

            return files;
        }

        private static string SavePdf(string path, RunReport r, ScanConfig cfg, IList<ModuleResult> modules,
                                      DateTime finished, RunLog log, ScanFiles files)
        {
            try
            {
                new PdfReport(cfg).Save(path, r, modules, finished);

                if (!File.Exists(path))
                {
                    files.Error = L.T("sum.pdfMissing", path);
                    log.Add(files.Error);
                    return null;
                }

                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                log.Add(L.T("sum.pdfSaved", Path.GetFullPath(path)));
                return Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                files.Error = L.T("sum.pdfError", ex.Message);
                log.Add(files.Error);
                return null;
            }
        }
    }
}
