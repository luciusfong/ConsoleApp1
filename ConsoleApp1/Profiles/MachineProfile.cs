using System.Collections.Generic;

namespace DeviceScanner.Profiles
{
    /// <summary>
    /// Everything that differs from one machine to the next. Loaded from a .profile text file,
    /// so testing a new machine means copying a file, not changing code.
    /// </summary>
    internal sealed class MachineProfile
    {
        public string Name { get; set; } = "unnamed";
        public string SourcePath { get; set; }

        // ----- controller -----
        public string ZmcIp { get; set; }
        public string InitProgram { get; set; } = "INITSYSTEM";
        public List<string> HomePrograms { get; } = new List<string>();
        public int[] HomeAxes { get; set; } = new int[0];          // empty = auto-discover

        // ----- devices that must be present -----
        public int[] TempSlaves { get; set; } = { 1, 2, 3, 4 };
        public List<ExpectedDevice> ExpectedIp { get; } = new List<ExpectedDevice>();
        public List<ExpectedDevice> ExpectedCom { get; } = new List<ExpectedDevice>();

        // ----- discrete IO loopback -----
        public List<IoTestCase> IoTests { get; } = new List<IoTestCase>();
    }

    /// <summary>A device the profile says must answer at an IP address or COM port.</summary>
    internal sealed class ExpectedDevice
    {
        public string Address { get; }     // IP or COM port
        public string Name { get; }        // must match a DeviceProbe name

        public ExpectedDevice(string address, string name) { Address = address; Name = name; }
    }

    /// <summary>One output that should make one input follow.</summary>
    internal sealed class IoTestCase
    {
        public string Name { get; }
        public int Out { get; }
        public int? In { get; }            // null = just report which inputs react
        public int SettleMs { get; }       // 0 = use ScanConfig.IoSettleMs
        public bool Optional { get; }      // failure is a warning only

        public IoTestCase(string name, int outNo, int? inNo, int settleMs, bool optional)
        {
            Name = name; Out = outNo; In = inNo; SettleMs = settleMs; Optional = optional;
        }
    }
}
