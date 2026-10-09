using System.Collections.Generic;

namespace DeviceScanner
{
    /// <summary>
    /// Tuning that is the same on every machine. Anything machine-specific (IPs, COM ports, programs,
    /// axes, IO numbers) lives in a .profile file instead.
    /// </summary>
    internal sealed class ScanConfig
    {
        // ----- device verification / discovery -----
        public int[] Subnets { get; } = { 0, 1 };            // discovery sweep: 192.168.<n>.1-254
        public int PingTimeoutMs { get; } = 500;
        public int PingParallelism { get; } = 64;
        public HashSet<string> MultiInstanceComDevices { get; } =
            new HashSet<string> { "ITV regulator", "OPT light", "LC18 light" };

        // ----- controller init -----
        public int InitTimeoutMs { get; } = 10000;           // wait for sysstate == 1 after RUN <init program>

        // ----- homing -----
        public int MaxProbeAxes { get; } = 32;               // upper bound when auto-discovering axes (profile home.axes empty)
        public int[] ExcludeAxes { get; } = { };             // pumps / encoders / conveyors you don't want awaited
        public bool UseDoneFlag { get; } = false;            // true only if the home programs set VR(HomeDoneVr)=1 at the end
        public int HomeDoneVr { get; } = 100;
        public int StartWaitMs { get; } = 3000;              // max wait for first axis to move after RUN
        public int IdleQuietMs { get; } = 7000;              // all axes idle this long = homing finished
        public int HomeTimeoutMs { get; } = 60000;
        public int AxisSettleTimeoutMs { get; } = 10000;

        // ----- discrete IO test -----
        public int IoScanCount { get; } = 64;                // inputs 0..N-1 are read before/after each toggle
        public int IoSettleMs { get; } = 1000;               // used when a profile io line has no settle time
    }
}
