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
        public SpecReport WindowsSpec { get; set; }            // null = not run
        public List<DeviceCheck> Devices { get; } = new List<DeviceCheck>();
        public IpScanReport DiscoveredIp { get; set; }         // null = discovery not run
        public ComScanReport DiscoveredCom { get; set; }
        public HomeResult Home { get; set; }                   // null = not run
        public bool IoRun { get; set; }
        public List<IoTestResult> Io { get; } = new List<IoTestResult>();

        public RunReport(MachineProfile profile) { Profile = profile; }
    }
}
