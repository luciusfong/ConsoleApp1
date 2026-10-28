using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// Finds files and folders whose NAME contains Chinese characters, on every fixed drive, skipping
    /// Windows' own top-level folders (WindowsSpecConfig.SkipTopLevelDirs). REPORT ONLY: nothing is deleted.
    /// A Chinese-named folder is reported once and not descended into.
    /// The full list is written to ReportDir\ChineseNames_&lt;PC&gt;_&lt;time&gt;.txt.
    /// </summary>
    internal sealed class ChineseNameCheck : SpecCheck
    {
        private readonly string _reportDir;

        public ChineseNameCheck(WindowsSpecConfig spec, string reportDir, Action<string> log) : base(spec, log)
        {
            _reportDir = reportDir;
        }

        public override string Title => "Files/folders with Chinese names";

        protected override void Execute()
        {
            const string group = "Files";
            var hits = new List<string>();
            var roots = new List<string>();
            long scanned = 0;
            int denied = 0;

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                Log($"    scanning {drive.Name} ...");
                roots.Add(drive.Name.TrimEnd('\\'));
                ScanDrive(drive.RootDirectory, hits, ref scanned, ref denied);
            }

            string listFile = WriteList(hits);
            string scope = $"{string.Join(", ", roots)} (skipping {string.Join(", ", Spec.SkipTopLevelDirs.OrderBy(s => s))})";
            string detail = $"{scanned:N0} entries scanned, {denied} folder(s) not readable" +
                            (listFile != null ? $"; full list: {listFile}" : "");

            var item = Add(group, "Chinese-named files/folders", "none", $"{hits.Count} found",
                           PassIf(hits.Count == 0), detail);
            item.Notes.Add("scope: " + scope);
            foreach (string h in hits.Take(Spec.ChineseNamesShownInSummary)) item.Notes.Add(h);
            if (hits.Count > Spec.ChineseNamesShownInSummary)
                item.Notes.Add($"... and {hits.Count - Spec.ChineseNamesShownInSummary} more (see list file)");
        }

        private void ScanDrive(DirectoryInfo root, List<string> hits, ref long scanned, ref int denied)
        {
            var stack = new Stack<DirectoryInfo>();

            // top level: apply the skip list (Windows, Program Files, ...)
            foreach (var e in SafeEntries(root, ref denied))
            {
                scanned++;
                if (e is DirectoryInfo d)
                {
                    if (Spec.SkipTopLevelDirs.Contains(d.Name) || IsReparse(d)) continue;
                    if (Cjk.HasChinese(d.Name)) { hits.Add("[folder] " + d.FullName); continue; }
                    stack.Push(d);
                }
                else if (Cjk.HasChinese(e.Name)) hits.Add(e.FullName);
            }

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                foreach (var e in SafeEntries(dir, ref denied))
                {
                    if (++scanned % 100000 == 0) Log($"      {scanned:N0} entries...");
                    if (e is DirectoryInfo d)
                    {
                        if (IsReparse(d)) continue;               // junctions ("Application Data", "My Documents") loop back
                        if (Cjk.HasChinese(d.Name)) { hits.Add("[folder] " + d.FullName); continue; }
                        stack.Push(d);
                    }
                    else if (Cjk.HasChinese(e.Name)) hits.Add(e.FullName);
                }
            }
        }

        private static List<FileSystemInfo> SafeEntries(DirectoryInfo dir, ref int denied)
        {
            try { return dir.EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                denied++;
                return new List<FileSystemInfo>();
            }
        }

        private static bool IsReparse(FileSystemInfo d)
        {
            try { return (d.Attributes & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        private string WriteList(List<string> hits)
        {
            if (hits.Count == 0) return null;
            try
            {
                Directory.CreateDirectory(_reportDir);
                string path = Path.GetFullPath(Path.Combine(_reportDir,
                    $"ChineseNames_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmmss}.txt"));
                File.WriteAllLines(path, hits, new System.Text.UTF8Encoding(true));   // BOM so Notepad shows the characters
                return path;
            }
            catch (Exception ex)
            {
                Log($"    could not write list file: {ex.Message}");
                return null;
            }
        }
    }
}
