using System;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>Base for one group of Windows spec checks. Checks only read; they never change the PC.</summary>
    internal abstract class SpecCheck
    {
        protected readonly WindowsSpecConfig Spec;
        protected readonly Action<string> Log;
        private SpecReport _report;

        protected SpecCheck(WindowsSpecConfig spec, Action<string> log) { Spec = spec; Log = log; }

        /// <summary>Shown in the console header and used if the whole check crashes.</summary>
        public abstract string Title { get; }

        public void Run(SpecReport report)
        {
            _report = report;
            Log($"\n  [{Title}]");
            try { Execute(); }
            catch (Exception ex) { Add(Title, "check", "", "", CheckStatus.Unverified, "check crashed: " + ex.Message); }
        }

        protected abstract void Execute();

        protected SpecItem Add(string group, string name, string expected, string actual, CheckStatus status, string detail = "")
        {
            var item = new SpecItem
            {
                Group = group, Name = name, Expected = expected ?? "", Actual = actual ?? "(missing)",
                Status = status, Detail = detail ?? "",
            };
            _report.Items.Add(item);
            Log($"    {Tag(status),-5} {group} / {name}: {item.Actual}" +
                (status == CheckStatus.Pass ? "" : $"  (expected {item.Expected})") +
                (item.Detail.Length > 0 ? "  " + item.Detail : ""));
            return item;
        }

        protected static CheckStatus PassIf(bool ok) => ok ? CheckStatus.Pass : CheckStatus.Fail;

        public static string Tag(CheckStatus s) =>
            s == CheckStatus.Pass ? "PASS" : s == CheckStatus.Unverified ? "WARN" : "FAIL";
    }
}
