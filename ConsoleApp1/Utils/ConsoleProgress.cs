using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DeviceScanner.Reporting;

namespace DeviceScanner.Utils
{
    /// <summary>
    /// Console output for a run: while a module works, one animated line shows the module and its latest log message;
    /// when it finishes, a single PASS / WARN / FAIL line (plus the failing items) replaces it.
    /// Every log line is still kept in <see cref="Transcript"/>, which is saved next to the PDF.
    /// If the console output is redirected, log lines are printed as they come instead.
    /// </summary>
    internal sealed class ConsoleProgress
    {
        private static readonly char[] Frames = { '|', '/', '-', '\\' };
        private const int MaxProblemLines = 8;

        private readonly object _gate = new object();
        private readonly List<string> _transcript = new List<string>();
        private readonly bool _animate = !Console.IsOutputRedirected;
        private Thread _thread;
        private volatile bool _running;
        private string _title = "";
        private string _status = "";
        private Stopwatch _clock;
        private int _frame;
        private int _drawnWidth;

        public IReadOnlyList<string> Transcript { get { lock (_gate) return _transcript.ToList(); } }

        /// <summary>The <c>log</c> callback handed to every check: updates the spinner (or prints, when no module is running).</summary>
        public void Log(string text)
        {
            text = text ?? "";
            lock (_gate)
            {
                string[] lines = text.Replace("\r", "").Split('\n');
                _transcript.AddRange(lines);

                if (_running && _animate)
                {
                    string last = lines.LastOrDefault(l => l.Trim().Length > 0);
                    if (last != null) _status = last.Trim();
                }
                else Console.WriteLine(text);
            }
        }

        /// <summary>Prints a line that stays (not part of the spinner) and keeps it in the transcript.</summary>
        public void Line(string text, ConsoleColor? color = null)
        {
            lock (_gate)
            {
                _transcript.Add(text);
                Write(text + Environment.NewLine, color);
            }
        }

        /// <summary>Runs <paramref name="work"/> under the spinner and returns how long it took.</summary>
        public TimeSpan Run(string title, Action work)
        {
            lock (_gate)
            {
                _transcript.Add("");
                _transcript.Add($"===== {title} =====");
                _title = title;
                _status = "";
                _clock = Stopwatch.StartNew();
                _running = true;
            }
            if (_animate)
            {
                _thread = new Thread(Animate) { IsBackground = true, Name = "spinner" };
                _thread.Start();
            }
            try { work(); }
            finally { Stop(); }
            return _clock.Elapsed;
        }

        /// <summary>The finished line for one module: coloured tag, title, counts, time, then the items that did not pass.</summary>
        public void Result(ModuleResult m)
        {
            lock (_gate)
            {
                string tag = "[" + PdfReport.TagOf(m.Status) + "]";
                string time = m.Duration.HasValue ? $"  ({Elapsed(m.Duration.Value)})" : "";
                string rest = $" {Pad(m.Title, 16)} {m.Summary}{time}";
                _transcript.Add(tag + rest);

                Write("  ", null);
                Write(Pad(tag, 10), ColorOf(m.Status));
                Write(rest + Environment.NewLine, m.Status == ModuleStatus.NotRun ? ConsoleColor.DarkGray : (ConsoleColor?)null);

                foreach (string p in m.Problems)
                    _transcript.Add("      - " + p);
                foreach (string p in m.Problems.Take(MaxProblemLines))
                    Write("      - " + Fit(p, ConsoleWidth() - 9) + Environment.NewLine, m.Status == ModuleStatus.Fail ? ConsoleColor.Red : ConsoleColor.Yellow);
                if (m.Problems.Count > MaxProblemLines)
                    Write("      " + L.T("con.more", m.Problems.Count - MaxProblemLines) + Environment.NewLine, ConsoleColor.DarkGray);
            }
        }

        public static ConsoleColor ColorOf(ModuleStatus s) =>
            s == ModuleStatus.Pass ? ConsoleColor.Green : s == ModuleStatus.Warn ? ConsoleColor.Yellow
          : s == ModuleStatus.Fail ? ConsoleColor.Red : ConsoleColor.DarkGray;

        // ---------- spinner ----------

        private void Stop()
        {
            _running = false;
            _thread?.Join();
            _thread = null;
            lock (_gate) Clear();
        }

        private void Animate()
        {
            while (_running)
            {
                lock (_gate)
                {
                    if (!_running) break;
                    string head = $"  {Frames[_frame++ % Frames.Length]} {_title}  {Elapsed(_clock.Elapsed)}  ";
                    string line = Fit(head + _status, ConsoleWidth() - 1);
                    int width = DisplayWidth(line);
                    Console.Write("\r" + line + new string(' ', Math.Max(0, _drawnWidth - width)));
                    _drawnWidth = width;
                }
                Thread.Sleep(120);
            }
        }

        private void Clear()
        {
            if (_drawnWidth == 0) return;
            Console.Write("\r" + new string(' ', _drawnWidth) + "\r");
            _drawnWidth = 0;
        }

        private static void Write(string text, ConsoleColor? color)
        {
            if (color == null) { Console.Write(text); return; }
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color.Value;
            Console.Write(text);
            Console.ForegroundColor = old;
        }

        // ---------- width helpers (Chinese characters take two console columns) ----------

        private static int ConsoleWidth()
        {
            try { return Math.Max(40, Console.WindowWidth); } catch { return 120; }
        }

        private static bool Wide(char c) =>
            (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) ||
            (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);

        private static int DisplayWidth(string s) => s.Sum(c => Wide(c) ? 2 : 1);

        private static string Fit(string s, int width)
        {
            if (DisplayWidth(s) <= width) return s;
            int used = 0, i = 0;
            for (; i < s.Length; i++)
            {
                int w = Wide(s[i]) ? 2 : 1;
                if (used + w > width - 3) break;
                used += w;
            }
            return s.Substring(0, i) + "...";
        }

        private static string Pad(string s, int width) => s + new string(' ', Math.Max(1, width - DisplayWidth(s)));

        private static string Elapsed(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
