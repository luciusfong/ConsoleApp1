using System.Collections.Generic;
using System.Linq;

namespace DeviceScanner.Utils
{
    /// <summary>
    /// Every log line of one run, from any thread. The scan window shows it live; it is saved next to the PDF at the end.
    /// </summary>
    internal sealed class RunLog
    {
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();

        /// <summary>The <c>log</c> callback handed to every check.</summary>
        public void Add(string text)
        {
            lock (_gate) _lines.AddRange((text ?? "").Replace("\r", "").Split('\n'));
        }

        public int Count { get { lock (_gate) return _lines.Count; } }

        public List<string> Snapshot() { lock (_gate) return _lines.ToList(); }

        /// <summary>Lines from <paramref name="start"/> on (for the window to append what is new).</summary>
        public List<string> Since(int start)
        {
            lock (_gate) return start >= _lines.Count ? new List<string>() : _lines.GetRange(start, _lines.Count - start);
        }
    }
}
