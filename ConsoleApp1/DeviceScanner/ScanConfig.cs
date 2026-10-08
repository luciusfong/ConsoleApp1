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

        // ----- Homing -----
        public int[] HomeAxes { get; } = { 6, 7, 8, 9, 10, 11 };        // Z, Z2, X, Y, X2, Y2 - placeholder numbers
        public string HomeProgram { get; } = "GHOME";
        public string HomeLProgram { get; } = "GLHOME";
        public string HomeRProgram { get; } = "GRHOME";
        public int HomeDoneVr { get; } = 100;                // GHome sets VR(100)=1 as its last line
        public int HomeTimeoutMs { get; } = 60000;
        public int AxisSettleTimeoutMs { get; } = 10000;
        public bool UseDoneFlag { get; } = false;        // true only after GHome sets VR(100)=1
        public int StartWaitMs { get; } = 3000;          // max wait for first axis to move after RUN
        public int IdleQuietMs { get; } = 7000;          // all axes idle this long = homing finished
    }
}
