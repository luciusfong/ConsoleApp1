using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using DeviceScanner.Native;

namespace DeviceScanner.Motion
{
    /// <summary>Initializes the controller, runs the homing BASIC program and waits for it to finish.</summary>
    internal sealed class ZmcHoming
    {
        private readonly ScanConfig _cfg;
        private readonly Action<string> _log;
        private int[] _axes = new int[0];

        public ZmcHoming(ScanConfig cfg, Action<string> log) { _cfg = cfg; _log = log; }

        public bool Run(string ip)
        {
            using (var zmc = new ZmcConnection())
            {
                if (!zmc.Open(ip)) { _log("Cannot reopen ZMC."); return false; }

                _axes = DiscoverAxes(zmc);
                if (_axes.Length == 0) { _log("No axes found."); return false; }
                _log($"Axes: {string.Join(", ", _axes)}");

                return new ZmcInit(_cfg, _log).EnsureReady(zmc)
                    && StartProgram(zmc)
                    && (_cfg.UseDoneFlag ? WaitForDoneFlag(zmc) : WaitForQuiet(zmc))
                    && WaitForAxesIdle(zmc);
            }
        }

        // ---------- axis discovery ----------

        private int[] DiscoverAxes(ZmcConnection zmc)
        {
            if (_cfg.HomeAxesOverride.Length > 0) return _cfg.HomeAxesOverride;

            var list = new List<int>();
            for (int i = 0; i < _cfg.MaxProbeAxes; i++)
            {
                // AXIS_ENABLE(n) is valid for every axis the controller has; an error reply means we are past the last one
                string en = zmc.Query($"AXIS_ENABLE({i})");
                if (!double.TryParse(en, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) break;

                // ATYPE 0 = virtual/unused axis. If ATYPE isn't available (-1), don't filter on it.
                int type = zmc.QueryInt($"ATYPE({i})", -1);
                _log($"  axis {i}: AXIS_ENABLE={en}  ATYPE={(type < 0 ? "n/a" : type.ToString())}");

                if (type == 0 || _cfg.ExcludeAxes.Contains(i)) continue;
                list.Add(i);
            }
            return list.ToArray();
        }

        // ---------- start ----------

        private bool StartProgram(ZmcConnection zmc)
        {
            zmc.SetVr(_cfg.HomeDoneVr, 0);               // clear stale done flag (only used if UseDoneFlag)

            int rcL = zmc.Execute($"RUN {_cfg.HomeLProgram}", out string respL);
            _log($"  > RUN {_cfg.HomeLProgram}  rc={rcL}  resp=\"{respL}\"");

            int rcR = zmc.Execute($"RUN {_cfg.HomeRProgram}", out string respR);
            _log($"  > RUN {_cfg.HomeRProgram}  rc={rcR}  resp=\"{respR}\"");
            return rcL == 0 && rcR == 0;
        }

        // ---------- waiting strategies ----------

        private bool AnyBusy(ZmcConnection zmc)
        {
            foreach (int a in _axes)
            {
                if (!zmc.TryGetIdle(a, out bool idle)) { _log($"GetIfIdle axis {a} failed"); return true; }
                if (!idle) return true;
            }
            return false;
        }

        /// <summary>No done flag: wait for motion to start, then for all axes to stay idle for IdleQuietMs.</summary>
        private bool WaitForQuiet(ZmcConnection zmc)
        {
            var sw = Stopwatch.StartNew();
            while (!AnyBusy(zmc))
            {
                if (sw.ElapsedMilliseconds > _cfg.StartWaitMs)
                {
                    _log("No axis moved after RUN. GHome may not have started, or the axes are wrong.");
                    return false;
                }
                Thread.Sleep(20);
            }

            var total = Stopwatch.StartNew();
            var quiet = Stopwatch.StartNew();
            while (quiet.ElapsedMilliseconds < _cfg.IdleQuietMs)
            {
                if (AnyBusy(zmc)) quiet.Restart();
                if (total.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                {
                    _log("Homing still moving after timeout.");
                    return false;
                }
                Thread.Sleep(20);
            }
            return true;
        }

        /// <summary>Done flag: the homing program sets VR(HomeDoneVr)=1 as its last line.</summary>
        private bool WaitForDoneFlag(ZmcConnection zmc)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                if (!zmc.GetVr(_cfg.HomeDoneVr, out float v)) { _log("GetVr failed (comms/SDK error)."); return false; }
                if (v == 1) return true;

                if (sw.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                {
                    foreach (int a in _axes) zmc.Execute($"BASE({a}) CANCEL(2)");
                    _log($"{_cfg.HomeLProgram} timeout. VR({_cfg.HomeDoneVr}) = {v}");
                    return false;
                }
                if (sw.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                {
                    foreach (int a in _axes) zmc.Execute($"BASE({a}) CANCEL(2)");
                    _log($"{_cfg.HomeRProgram} timeout. VR({_cfg.HomeDoneVr}) = {v}");
                    return false;
                }
                Thread.Sleep(50);
            }
        }

        private bool WaitForAxesIdle(ZmcConnection zmc)
        {
            foreach (int a in _axes)
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    if (zmc.TryGetIdle(a, out bool idle) && idle) break;
                    if (sw.ElapsedMilliseconds > _cfg.AxisSettleTimeoutMs)
                    {
                        _log($"Axis {a} still busy.");
                        return false;
                    }
                    Thread.Sleep(20);
                }
            }
            return true;
        }
    }
}
