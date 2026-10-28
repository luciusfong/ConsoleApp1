using System.Collections.Generic;
using System.Linq;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>One line of the Windows spec check: what the golden image expects vs. what this PC has.</summary>
    internal sealed class SpecItem
    {
        public string Group { get; set; }          // "OS", "Locale", "Defender", "NIC: Ethernet 2", ...
        public string Name { get; set; }           // "Build", "Interrupt moderation", ...
        public string Expected { get; set; } = "";
        public string Actual { get; set; } = "";
        public CheckStatus Status { get; set; }    // Unverified = WARN
        public string Detail { get; set; } = "";

        /// <summary>Extra lines printed under the item (e.g. offending file paths). Keep short; long lists go to a text file.</summary>
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>All results of one Windows spec run. Check only: nothing on the PC is changed.</summary>
    internal sealed class SpecReport
    {
        public List<SpecItem> Items { get; } = new List<SpecItem>();

        public int Failures => Items.Count(i => i.Status == CheckStatus.Fail);
        public int Warnings => Items.Count(i => i.Status == CheckStatus.Unverified);
    }
}
