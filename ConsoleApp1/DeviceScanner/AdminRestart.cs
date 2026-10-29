using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using DeviceScanner.Utils;
using DeviceScanner.WindowsSpec;

namespace DeviceScanner
{
    /// <summary>
    /// Some Windows spec checks need admin rights (dism, Defender exclusions, NIC power settings), and so does the firewall rule.
    /// When the app is not elevated, it starts itself again through the UAC prompt and this copy exits.
    /// If the prompt is declined, the operator can carry on without admin rights or quit.
    /// </summary>
    internal static class AdminRestart
    {
        public enum Outcome { Continue, Restarted, Quit }

        private const string Marker = "--restarted";       // the new copy never asks again, even if it still is not elevated
        private const int Cancelled = 1223;                // ERROR_CANCELLED: No on the UAC prompt

        /// <summary><paramref name="args"/> without the marker; the language is passed on so it is not asked twice.</summary>
        public static Outcome Run(ref string[] args)
        {
            bool restarted = args.Contains(Marker);
            args = args.Where(a => a != Marker).ToArray();
            if (restarted || Elevation.IsAdmin()) return Outcome.Continue;

            string argLine = string.Join(" ", args.Select(Quote).Concat(new[] { "--lang", L.Current == Lang.Zh ? "zh" : "en", Marker }));
            string reason;
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, argLine)
                {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.CurrentDirectory,
                });
                return Outcome.Restarted;
            }
            catch (Win32Exception ex) { reason = ex.NativeErrorCode == Cancelled ? "" : ex.Message; }

            string question = L.T("admin.declined") + (reason.Length > 0 ? "\n\n" + reason : "");
            return Dialogs.Confirm(question, caution: true) ? Outcome.Continue : Outcome.Quit;
        }

        private static string Quote(string a) => a.Length > 0 && a.IndexOfAny(new[] { ' ', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
    }
}
