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
            // Pump 1
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 4, 0, 1000); // SV_PRESS_1 -> PUMP_PRESS_ON_1
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 5, 1, 1000); // SV_VAC_1 -> PUMP_VAC_ON_1

            // Pump 2
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 7, 5, 1000); // SV_PRESS_2 -> PUMP_PRESS_ON_2 ?
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 8, 6, 1000); // SV_VAC_2 -> PUMP_VAC_ON_2 ?

            // Door Lock
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 11, 15, 1000); // LOCK_DOOR -> DOOR_LOCKED

            // SMEMA 1
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 36, 32, 1000); // SMEMA_1_UPLINE_MC_READY -> SMEMA_1_UPLINE_BD_READY
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 37, 33, 1000); // SMEMA_1_DNLINE_BD_READY -> SMEMA_1_DNLINE_MC_READY

            // Station 1 Stopper
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 39, 38, 1000); // SV_STOPPER_UP_1 -> SENS_STOPPER_UP_1

            // Station 1 Lifter
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 40, 39, 1000); // SV_LIFTER_UP_1 -> SENS_LIFTER_UP_1
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 41, 40, 1000); // SV_LIFTER_DOWN_1 -> SENS_LIFTER_DOWN_1

            // Station 1 Chuck Vacuum
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 42, 41, 1000); // CHUCK_HEATER_VAC_HIGH_1 -> VAC_CHUCK_ON
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 53, 41, 1000); // CHUCK_HEATER_VAC_LOW_1 -> VAC_CHUCK_ON

            // Station 1 Cleaning
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 43, 22, 1000); // SV_CLEAN_PRESS -> CLEAN_VAC_ON_1
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 44, 22, 1000); // SV_CLEAN_VAC -> CLEAN_VAC_ON_1

            // Station 2 Stopper
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 45, 44, 1000); // SV_STOPPER_UP_2 -> SENS_STOPPER_UP_2

            // Station 2 Lifter
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 46, 45, 1000); // SV_LIFTER_UP_2 -> SENS_LIFTER_UP_2
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 47, 46, 1000); // SV_LIFTER_DOWN_2 -> SENS_LIFTER_DOWN_2

            // Station 2 Chuck Vacuum
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 48, 47, 1000); // CHUCK_HEATER_VAC_HIGH_2 -> VAC_CHUCK_ON_2
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 54, 47, 1000); // CHUCK_HEATER_VAC_LOW_2 -> VAC_CHUCK_ON_2

            // Station 2 Cleaning
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 49, 23, 1000); // SV_CLEAN_PRESS_2 -> CLEAN_VAC_ON_2
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 50, 23, 1000); // SV_CLEAN_VAC_2 -> CLEAN_VAC_ON_2

            // SMEMA 2
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 51, 34, 1000); // SMEMA_2_UPLINE_MC_READY -> SMEMA_2_UPLINE_BD_READY
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 52, 35, 1000); // SMEMA_2_DNLINE_BD_READY -> SMEMA_2_DNLINE_MC_READY

            // Nozzle Clean
            new ZmcDiscreteIo(_cfg, _log).Run(zmc, 55, 52, 1000); // SV_NOOZLE_CLEAN -> VERMES_NOZ_CLEAN_ON


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
