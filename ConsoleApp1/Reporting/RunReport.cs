using System;
using System.Collections.Generic;
using DeviceScanner.Profiles;
using DeviceScanner.Scanning;
using DeviceScanner.WindowsSpec;

namespace DeviceScanner.Reporting
{
    internal enum CheckStatus { Pass, Fail, Unverified }

    /// <summary>One device from the profile, checked at the address the profile gives.</summary>
    internal sealed class DeviceCheck
    {
        public string Kind { get; set; }         // "IP" or "COM"
        public string Address { get; set; }
        public string Device { get; set; }
        public CheckStatus Status { get; set; }
        public string Detail { get; set; } = "";
    }

    /// <summary>An address discovery set to match the profile (PC: NIC IP or COM port number; device: camera or ZMC IP), or the conflict that stopped it.</summary>
    internal sealed class AddressFix
    {
        public bool OnDevice { get; set; }       // false = this PC was changed, true = the device was
        public string Where => OnDevice ? "Device" : "PC";
        public string Kind { get; set; }         // "NIC" or "COM"; device: "Camera" or "ZMC"
        public string Target { get; set; }       // adapter name, or device name
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public CheckStatus Status { get; set; }  // Pass = changed, Unverified = changed but not confirmed, Fail = conflict / not changed
        public string Detail { get; set; } = "";
    }

    internal sealed class HomeResult
    {
        public bool Success { get; set; }
        public string Detail { get; set; } = "";     // failure reason
        public string Programs { get; set; } = "";
        public int[] Axes { get; set; } = new int[0];
        public TimeSpan Elapsed { get; set; }
    }

    /// <summary>Everything one run produced; ScanSummary prints it at the end.</summary>
    internal sealed class RunReport
    {
        public MachineProfile Profile { get; }
        public string UserName { get; set; } = "";             // typed in at the start of the run
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public SpecReport WindowsSpec { get; set; }            // null = not run
        public List<DeviceCheck> Devices { get; } = new List<DeviceCheck>();
        public IpScanReport DiscoveredIp { get; set; }         // null = discovery not run
        public ComScanReport DiscoveredCom { get; set; }
        public List<AddressFix> AddressFixes { get; } = new List<AddressFix>();   // address changes made by discovery
        public HomeResult Home { get; set; }                   // null = not run
        public bool IoRun { get; set; }
        public string IoError { get; set; }                    // IO test was wanted but could not start (e.g. ZMC not reachable)
        public List<IoTestResult> Io { get; } = new List<IoTestResult>();
        public Dictionary<Module, TimeSpan> Durations { get; } = new Dictionary<Module, TimeSpan>();

        public RunReport(MachineProfile profile) { Profile = profile; }
    }
}
