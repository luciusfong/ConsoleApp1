using System.Collections.Generic;

namespace DeviceScanner
{
    /// <summary>All tunable settings in one place.</summary>
    internal sealed class ScanConfig
    {
        // ----- IP scan -----
        public int[] Subnets { get; } = { 0, 1 };            // 192.168.<n>.1-254
        public int PingTimeoutMs { get; } = 500;
        public int PingParallelism { get; } = 64;

        // ----- COM scan -----
        public int[] TempSlaves { get; } = { 1, 2, 3, 4 };   // real temp-controller slave addresses
        public HashSet<string> MultiInstanceComDevices { get; } =
            new HashSet<string> { "ITV regulator", "OPT light", "LC18 light" };

        // ----- Controller init -----
        public int InitTimeoutMs { get; } = 10000;           // wait for sysstate == 1 after "run initsystem"

        // ----- Homing -----
        public string HomeLProgram { get; } = "GLHOME";
        public string HomeRProgram { get; } = "GRHOME";
        public string InitProgram { get; } = "INITSYSTEM";
        public int[] HomeAxesOverride { get; } = { 6, 7, 8, 9, 10, 11 };        // empty = auto-discover via NAXES/ATYPE
        public int MaxProbeAxes { get; } = 32;               // upper bound when auto-discovering axes
        public int[] ExcludeAxes { get; } = { };             // pumps / encoders / conveyors you don't want awaited
        public bool UseDoneFlag { get; } = false;            // true only if GHome sets VR(HomeDoneVr)=1 at the end
        public int HomeDoneVr { get; } = 100;
        public int StartWaitMs { get; } = 3000;              // max wait for first axis to move after RUN
        public int IdleQuietMs { get; } = 7000;              // all axes idle this long = homing finished
        public int HomeTimeoutMs { get; } = 60000;
        public int AxisSettleTimeoutMs { get; } = 10000;
        public int IoScanCount { get; } = 64;
    }
}
