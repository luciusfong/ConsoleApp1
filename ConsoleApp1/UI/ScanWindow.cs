using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DeviceScanner.Reporting;
using DeviceScanner.Utils;
using DeviceScanner.WindowsSpec;

namespace DeviceScanner.UI
{
    /// <summary>
    /// The one window of a run. The top shows a spinner and the module being checked right now with its latest step;
    /// below, one row per module fills in with PASS / WARN / FAIL / NOT RUN as each finishes. The full log can be shown,
    /// and at the end the PDF and the log can be opened from here.
    /// The checks run on a worker thread; the methods it calls (Ask, Begin, End, Finish, ShowError) marshal to the UI thread.
    /// </summary>
    internal sealed class ScanWindow : Form
    {
        // palette of the report template
        private static readonly Color Navy = Color.FromArgb(0x1F, 0x3A, 0x5F);
        private static readonly Color NavyLight = Color.FromArgb(0xC9, 0xD6, 0xE6);
        private static readonly Color Ink = Color.FromArgb(0x1F, 0x29, 0x33);
        private static readonly Color Muted = Color.FromArgb(0x6B, 0x77, 0x85);
        private static readonly Color Rule = Color.FromArgb(0xD5, 0xDB, 0xE1);
        private static readonly Color HeadBg = Color.FromArgb(0xEE, 0xF2, 0xF6);
        private static readonly Color PassFg = Color.FromArgb(0x1E, 0x7B, 0x3C), PassBg = Color.FromArgb(0xE3, 0xF4, 0xE8);
        private static readonly Color WarnFg = Color.FromArgb(0x9A, 0x67, 0x00), WarnBg = Color.FromArgb(0xFF, 0xF3, 0xD6);
        private static readonly Color FailFg = Color.FromArgb(0xB4, 0x23, 0x18), FailBg = Color.FromArgb(0xFB, 0xE4, 0xE4);

        private const int LogHeight = 240;
        private const int MaxProblemLines = 3;

        private readonly RunLog _log;
        private readonly Action<ScanWindow> _work;
        private readonly Dictionary<Module, Row> _rows = new Dictionary<Module, Row>();
        private readonly Spinner _spinner = new Spinner { Size = new Size(52, 52), Margin = new Padding(0, 0, 16, 0) };
        private readonly Label _subject = new Label { AutoSize = true, ForeColor = Ink, Margin = new Padding(0, 2, 0, 2) };
        private readonly Label _status = new Label { AutoSize = false, AutoEllipsis = true, UseMnemonic = false, ForeColor = Muted, Height = 20, Margin = Padding.Empty };
        private readonly Label _elapsed = new Label { AutoSize = true, ForeColor = Muted, Margin = Padding.Empty };
        private readonly TextBox _logBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5f), BackColor = Color.White, Visible = false,
        };
        private readonly Button _toggleLog, _openPdf, _fix, _openLog, _rescan, _close;

        /// <summary>The operator asked for another run (same operator, profile and run mode); set when the window closes.</summary>
        public bool RescanRequested { get; private set; }
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer { Interval = 200 };

        private Stopwatch _clock;
        private int _logShown;
        private volatile bool _running;
        private ScanFiles _files;
        private readonly RunReport _report;
        private List<string> _missingDrivers = new List<string>();
        private bool _vcMissing;
        private List<NicSettingFix> _nicFixes = new List<NicSettingFix>();

        public ScanWindow(RunReport report, IEnumerable<Module> order, RunLog log, Action<ScanWindow> work)
        {
            _report = report;
            _log = log;
            _work = work;

            Text = $"{Dialogs.Caption} - {report.Profile.Name}";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(720, 400);
            Icon = SystemIcons.Application;

            _subject.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);

            // Dock order: last added is laid out first, so Fill goes first and the edges after it
            Controls.Add(_logBox);
            Controls.Add(BuildModules(order));
            Controls.Add(BuildSubject());
            Controls.Add(BuildHeader(report));

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                Padding = new Padding(12, 10, 12, 10), BackColor = HeadBg,
            };
            _close = Button(L.T("ui.close"), (s, e) => Close());
            _rescan = Button(L.T("ui.rescan"), (s, e) => { RescanRequested = true; Close(); });
            _rescan.Enabled = false;
            _openPdf = Button(L.T("ui.openPdf"), (s, e) => Open(_files?.PdfPath));
            _openLog = Button(L.T("ui.openLog"), (s, e) => Open(_files?.LogPath));
            _toggleLog = Button(L.T("ui.showLog"), (s, e) => ToggleLog());
            _fix = Button("", (s, e) => Fix());
            _fix.ForeColor = FailFg;
            _fix.Visible = false;
            _openPdf.Enabled = _openLog.Enabled = false;
            bar.Controls.AddRange(new Control[] { _close, _rescan, _openPdf, _fix, _openLog, _toggleLog });
            Controls.Add(bar);

            _tick.Tick += (s, e) => ShowNewLog();
            Load += (s, e) => FitHeight();
            // rows may have grown while maximized: fit again when the window is restored
            var lastState = WindowState;
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Normal && lastState != FormWindowState.Normal) FitHeight();
                lastState = WindowState;
            };
            Shown += (s, e) => StartWork();
            FormClosing += OnClosing;
        }

        // ---------- called from the worker thread ----------

        public bool Ask(string question, bool caution) => (bool)Invoke(new Func<bool>(() => Dialogs.Confirm(question, caution, owner: this)));

        public void Begin(Module m) => Invoke(new Action(() =>
        {
            ShowNewLog();
            _clock = Stopwatch.StartNew();
            _subject.Text = L.T("ui.checking", ModuleVerdicts.Title(m));
            _status.Text = "";
            _spinner.Start();
            _rows[m].Set(L.T("ui.running"), Navy, NavyLight);
        }));

        public void End(ModuleResult r) => Invoke(new Action(() =>
        {
            ShowNewLog();
            var row = _rows[r.Module];
            Colors(r.Status, out var fg, out var bg);
            row.Set(PdfReport.TagOf(r.Status), fg, bg);
            row.Summary.Text = r.Summary;
            row.Summary.ForeColor = r.Status == ModuleStatus.NotRun ? Muted : Ink;
            row.Time.Text = r.Duration.HasValue ? Elapsed(r.Duration.Value) : "";

            if (r.Problems.Count > 0)
            {
                var lines = r.Problems.Take(MaxProblemLines).Select(p => "• " + p).ToList();
                if (r.Problems.Count > MaxProblemLines) lines.Add(L.T("ui.more", r.Problems.Count - MaxProblemLines));
                row.Problems.Text = string.Join("\n", lines);
                row.Problems.ForeColor = fg;
                row.Problems.Visible = true;
                FitHeight();
            }
            else if (row.Problems.Visible)          // a module checked again (Devices after discovery changed addresses)
            {
                row.Problems.Visible = false;
                FitHeight();
            }
            _subject.Text = L.T("ui.waitingNext");
            _status.Text = "";
            _elapsed.Text = "";
            _clock = null;
        }));

        public void Finish(RunVerdict verdict, ScanFiles files) => Invoke(new Action(() =>
        {
            _running = false;
            _files = files;
            ShowNewLog();
            _clock = null;
            _elapsed.Text = "";
            _tick.Stop();

            _subject.Text = verdict.Passed ? L.T("sum.overallPass") : L.T("sum.overallFail", verdict.Failures);
            _subject.ForeColor = verdict.Passed ? PassFg : FailFg;
            _spinner.Stop(verdict.Passed ? (verdict.Warnings > 0 ? WarnFg : PassFg) : FailFg,
                          verdict.Passed ? (verdict.Warnings > 0 ? "!" : "✓") : "✕");

            var notes = new List<string>();
            if (verdict.Warnings > 0) notes.Add(L.T("sum.warnings", verdict.Warnings).Trim());
            if (verdict.NotRun.Count > 0) notes.Add(L.T("sum.notRunList", string.Join(", ", verdict.NotRun)).Trim());
            _status.Text = files.Error ?? string.Join("    ", notes);
            _status.ForeColor = files.Error != null ? FailFg : Muted;

            _openPdf.Enabled = files.PdfPath != null;
            _openLog.Enabled = files.LogPath != null;
            _rescan.Enabled = true;
            if (_openPdf.Enabled) _openPdf.Focus();
            ShowFix(SpecFixer.MissingDrivers(_report.WindowsSpec), VcRuntimeCheck.Missing(_report.WindowsSpec),
                    _report.WindowsSpec?.NicFixes.ToList() ?? new List<NicSettingFix>());
            Activate();
        }));

        public void ShowError(string message) => Invoke(new Action(() => Dialogs.Error(message, this)));

        // ---------- layout ----------

        private Control BuildHeader(RunReport report)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Navy, Padding = new Padding(20, 10, 20, 10) };
            header.Controls.Add(new Label
            {
                Text = L.T("ui.operator", report.UserName, report.StartedAt.ToString("yyyy-MM-dd HH:mm")),
                ForeColor = NavyLight, AutoSize = true, Dock = DockStyle.Top,
            });
            header.Controls.Add(new Label
            {
                Text = L.T("pdf.title") + "  ·  " + report.Profile.Name,
                ForeColor = Color.White, AutoSize = true, Dock = DockStyle.Top,
                Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            });
            return header;
        }

        private Control BuildSubject()
        {
            var text = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _subject.Text = L.T("ui.starting");
            _status.Dock = DockStyle.Fill;
            text.Controls.Add(_subject);
            text.Controls.Add(_status);
            text.Controls.Add(_elapsed);

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 92, ColumnCount = 2, Padding = new Padding(20, 16, 20, 8) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.Controls.Add(_spinner, 0, 0);
            grid.Controls.Add(text, 1, 0);
            return grid;
        }

        private Control BuildModules(IEnumerable<Module> order)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(20, 4, 20, 12),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            grid.Controls.Add(Caption(L.T("col.status")), 0, 0);
            grid.Controls.Add(Caption(L.T("col.module")), 1, 0);
            grid.Controls.Add(Caption(L.T("col.result")), 2, 0);
            grid.Controls.Add(Caption(L.T("col.duration")), 3, 0);

            // explicit cells: the table's auto-flow skips hidden controls, which would shift every row after a hidden problems line
            int y = 1;
            foreach (var m in order)
            {
                var row = new Row(this, m);
                _rows[m] = row;
                grid.Controls.Add(row.Badge, 0, y);
                grid.Controls.Add(row.Title, 1, y);
                grid.Controls.Add(row.Summary, 2, y);
                grid.Controls.Add(row.Time, 3, y);
                grid.Controls.Add(row.Problems, 1, y + 1);
                grid.SetColumnSpan(row.Problems, 3);
                y += 2;
            }
            return grid;
        }

        private Label Caption(string text) => new Label
        {
            Text = text, AutoSize = true, ForeColor = Muted, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 12, 4),
        };

        private Button Button(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 30), Margin = new Padding(8, 0, 0, 0) };
            b.Click += click;
            return b;
        }

        /// <summary>Window height = everything docked at the edges (+ the log when it is shown). A maximized window keeps its size.</summary>
        private void FitHeight()
        {
            int edges = Controls.Cast<Control>().Where(c => c != _logBox && c.Visible).Sum(c => c.Height);
            int want = edges + (_logBox.Visible ? LogHeight : 0);
            if (WindowState != FormWindowState.Normal) return;
            MinimumSize = new Size(560, Height - ClientSize.Height + edges);
            if (ClientSize.Height < want || !_logBox.Visible) ClientSize = new Size(ClientSize.Width, want);
        }

        private void ToggleLog()
        {
            _logBox.Visible = !_logBox.Visible;
            _toggleLog.Text = L.T(_logBox.Visible ? "ui.hideLog" : "ui.showLog");
            FitHeight();
            if (_logBox.Visible) { _logBox.SelectionStart = _logBox.TextLength; _logBox.ScrollToCaret(); }
        }

        // ---------- running ----------

        private void StartWork()
        {
            _running = true;
            _tick.Start();
            var worker = new Thread(() =>
            {
                try { _work(this); }
                catch (Exception ex)
                {
                    _log.Add(L.T("msg.error", ex));
                    if (IsHandleCreated) ShowError(L.T("msg.error", ex.Message));
                }
                finally { _running = false; }
            }) { IsBackground = true, Name = "scan" };
            worker.Start();
        }

        /// <summary>New log lines into the log box; the latest one under the module title; the module's running time.</summary>
        private void ShowNewLog()
        {
            var lines = _log.Since(_logShown);
            if (lines.Count > 0)
            {
                _logShown += lines.Count;
                _logBox.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
                string last = lines.LastOrDefault(l => l.Trim().Length > 0);
                if (last != null && _clock != null) _status.Text = last.Trim();
            }
            if (_clock != null) _elapsed.Text = Elapsed(_clock.Elapsed);
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_running) return;
            // the checks cannot be interrupted half way (a homing program keeps running on the controller anyway)
            if (!Dialogs.Confirm(L.T("ui.quitRunning"), caution: true, owner: this)) { e.Cancel = true; return; }
            Environment.Exit(1);
        }

        // ---------- fixing ----------

        /// <summary>The fix button: shown while the Windows spec found missing drivers, a missing VC++ runtime or wrong NIC settings.</summary>
        private void ShowFix(List<string> missingDrivers, bool vcMissing, List<NicSettingFix> nicFixes)
        {
            _missingDrivers = missingDrivers;
            _vcMissing = vcMissing;
            _nicFixes = nicFixes;
            int count = missingDrivers.Count + (vcMissing ? 1 : 0) + nicFixes.Count;
            _fix.Text = L.T("ui.fix", count);
            _fix.Visible = count > 0;
        }

        /// <summary>Installs what the Windows spec found missing, from the files next to the exe, on a worker thread.</summary>
        private void Fix()
        {
            if (_running || (_missingDrivers.Count == 0 && !_vcMissing && _nicFixes.Count == 0)) return;
            if (!Elevation.IsAdmin()) { Dialogs.Error(L.T("fix.needAdmin"), this); return; }

            var spec = new WindowsSpecConfig();
            var drivers = _missingDrivers.ToList();
            bool vc = _vcMissing;
            var nics = _nicFixes.ToList();

            string package = null, vcRedist = null;
            if (drivers.Count > 0 && (package = SpecFixer.NextToExe(spec.DriverPackage) ?? Pick(spec.DriverPackage, "Zip (*.zip)|*.zip")) == null) return;
            if (vc && (vcRedist = SpecFixer.NextToExe(spec.VcRedistInstaller) ?? Pick(spec.VcRedistInstaller, "vc_redist (*.exe)|*.exe")) == null) return;

            var items = drivers.ToList();
            if (vc) items.Add(VcRuntimeCheck.Name);
            items.AddRange(nics.Select(n => n.ToString()));
            string warning = drivers.Count > 0 || nics.Count > 0 ? L.T("fix.nicWarning") : null;
            if (!Dialogs.ConfirmList(L.T("fix.confirm", items.Count), items, warning, caution: true, owner: this)) return;

            _running = true;
            _fix.Enabled = _rescan.Enabled = false;
            _subject.Text = L.T("fix.running");
            _subject.ForeColor = Ink;
            _status.ForeColor = Muted;
            _clock = Stopwatch.StartNew();
            _spinner.Start();
            _tick.Start();
            int logStart = _log.Count;
            _log.Add("");
            _log.Add($"===== {L.T("ui.fix", items.Count)} =====");

            new Thread(() =>
            {
                var outcome = new SpecFixer.Outcome();
                if (drivers.Count > 0) SpecFixer.InstallDrivers(drivers, package, outcome, _log.Add);
                if (vc) SpecFixer.InstallVcRuntime(vcRedist, spec.VcRuntimeMin, outcome, _log.Add);
                if (nics.Count > 0) SpecFixer.FixNicSettings(nics, outcome, _log.Add);
                _log.Add($"    {outcome.Installed.Count} installed, {outcome.Problems.Count} problem(s)");
                SaveFixLog(logStart);
                BeginInvoke(new Action(() => FixDone(drivers, vc, nics, outcome)));
            }) { IsBackground = true, Name = "fix" }.Start();
        }

        private void FixDone(List<string> drivers, bool vc, List<NicSettingFix> nics, SpecFixer.Outcome outcome)
        {
            _running = false;
            ShowNewLog();
            _clock = null;
            _elapsed.Text = "";
            _tick.Stop();

            int tried = drivers.Count + (vc ? 1 : 0) + nics.Count;
            bool ok = outcome.Problems.Count == 0;
            string done = L.T("fix.done", outcome.Installed.Count, tried);
            _subject.Text = done;
            _subject.ForeColor = ok ? PassFg : FailFg;
            _spinner.Stop(ok ? PassFg : FailFg, ok ? "✓" : "✕");
            _status.Text = L.T("fix.rescan");

            ShowFix(drivers.Except(outcome.Installed, StringComparer.OrdinalIgnoreCase).ToList(),
                    vc && !outcome.Installed.Any(i => i.StartsWith(VcRuntimeCheck.Name, StringComparison.Ordinal)),
                    nics.Where(n => !outcome.Installed.Contains(n.ToString())).ToList());
            _fix.Enabled = _rescan.Enabled = true;

            // the problems can be many lines: they go in the scrolling list, the rest stays short
            var footer = new List<string>();
            if (outcome.RebootNeeded) footer.Add(L.T("fix.reboot"));
            footer.Add(L.T("fix.rescan"));
            if (ok) Dialogs.Inform(done + "\n\n" + string.Join("\n\n", footer), this);
            else Dialogs.InformList(done, outcome.Problems, string.Join("\n\n", footer), error: true, owner: this);
        }

        /// <summary>A file that should be next to the exe is not there: the operator browses for it.</summary>
        private string Pick(string fileName, string filter)
        {
            using (var dlg = new OpenFileDialog { Title = L.T("fix.pickFile", fileName), Filter = filter, FileName = fileName })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }
        /// <summary>The log file was written when the scan ended: the install log is added to it.</summary>
        private void SaveFixLog(int from)
        {
            if (_files?.LogPath == null) return;
            try { File.AppendAllLines(_files.LogPath, _log.Since(from)); }
            catch (Exception ex) { _log.Add("    log file not updated: " + ex.Message); }
        }

        private void Open(string path)
        {
            if (path == null) return;
            try { Process.Start(path); }
            catch (Exception ex) { Dialogs.Error(ex.Message, this); }
        }

        private static void Colors(ModuleStatus s, out Color fg, out Color bg)
        {
            switch (s)
            {
                case ModuleStatus.Pass: fg = PassFg; bg = PassBg; break;
                case ModuleStatus.Warn: fg = WarnFg; bg = WarnBg; break;
                case ModuleStatus.Fail: fg = FailFg; bg = FailBg; break;
                default: fg = Muted; bg = HeadBg; break;
            }
        }

        private static string Elapsed(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        /// <summary>The controls of one module row.</summary>
        private sealed class Row
        {
            public readonly Label Badge, Title, Summary, Time, Problems;

            public Row(ScanWindow w, Module m)
            {
                Badge = new Label
                {
                    AutoSize = false, Size = new Size(88, 24), TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font(w.Font, FontStyle.Bold), Margin = new Padding(0, 4, 14, 4),
                };
                Title = new Label { Text = ModuleVerdicts.Title(m), AutoSize = true, ForeColor = Ink,
                                    Font = new Font(w.Font, FontStyle.Bold), Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 16, 4) };
                Summary = new Label { AutoSize = true, UseMnemonic = false, ForeColor = Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) };
                Time = new Label { AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Right, Margin = new Padding(0, 4, 0, 4) };
                Problems = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(560, 0), Visible = false, Margin = new Padding(0, 0, 0, 6) };
                Set(L.T("ui.waiting"), Muted, HeadBg);
            }

            public void Set(string tag, Color fg, Color bg)
            {
                Badge.Text = tag;
                Badge.ForeColor = fg;
                Badge.BackColor = bg;
            }
        }
    }
}
