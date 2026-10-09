using System;
using System.Diagnostics;
using System.Threading;
using DeviceScanner.Native;

namespace DeviceScanner.Motion
{
    /// <summary>
    /// Brings a freshly powered ZMC controller to the "ready" state, the same way
    /// TF_MOTION.HomeGantry does: if sysstate != 1, run the controller program
    /// "initsystem" and wait until sysstate becomes 1.
    /// </summary>
    internal sealed class ZmcInit
    {
        private readonly ScanConfig _cfg;
        private readonly string _initProgram;
        private readonly Action<string> _log;

        public ZmcInit(ScanConfig cfg, string initProgram, Action<string> log) { _cfg = cfg; _initProgram = initProgram; _log = log; }

        /// <summary>Returns true when sysstate == 1 (already, or after running initsystem).</summary>
        public bool EnsureReady(ZmcConnection zmc)
        {
            int state = zmc.QueryInt("sysstate");
            _log($"sysstate = {state}");
            if (state == 1) return true;                 // already initialized, nothing to do

            int rc = zmc.Execute($"RUN {_initProgram}", out string resp);
            _log($"  > RUN {_initProgram}  rc={rc}  resp=\"{resp}\"");
            if (rc != 0) return false;

            var sw = Stopwatch.StartNew();
            while (zmc.QueryInt("sysstate") != 1)
            {
                if (sw.ElapsedMilliseconds > _cfg.InitTimeoutMs)
                {
                    _log($"initsystem did not reach sysstate=1 (now {zmc.QueryInt("sysstate")}).");
                    return false;
                }
                Thread.Sleep(10);
            }

            _log("initsystem done.");
            return true;
        }
    }
}
