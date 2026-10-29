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

    /// <summary>A NIC setting that failed the spec, with what the fix button sets it to.</summary>
    internal sealed class NicSettingFix
    {
        public string Connection { get; set; }    // "Ethernet 2" (Set-NetAdapter* -Name)
        public string Label { get; set; }         // "Receive Buffers", "Allow computer to turn off this device"
        public string Keyword { get; set; }       // advanced property: registry keyword (*ReceiveBuffers); null for power
        public string Value { get; set; }         // advanced property: value to set
        public string PowerClass { get; set; }    // power: root\WMI class (MSPower_DeviceEnable, ...)
        public string PowerProperty { get; set; } // power: its boolean property, set to false
        public string PowerInstance { get; set; } // power: InstanceName (prefix for MSPower_*, exact for MSNdis_*)

        public override string ToString() => $"{Connection}: {Label}";
    }

    /// <summary>All results of one Windows spec run. Check only: nothing on the PC is changed.</summary>
    internal sealed class SpecReport
    {
        public List<SpecItem> Items { get; } = new List<SpecItem>();
        public List<NicSettingFix> NicFixes { get; } = new List<NicSettingFix>();

        public int Failures => Items.Count(i => i.Status == CheckStatus.Fail);
        public int Warnings => Items.Count(i => i.Status == CheckStatus.Unverified);
    }
}
