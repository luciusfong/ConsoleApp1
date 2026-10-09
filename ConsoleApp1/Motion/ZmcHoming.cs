using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using DeviceScanner.Native;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;

namespace DeviceScanner.Motion
{
    /// <summary>Initializes the controller, runs the profile's homing programs and waits for them to finish.</summary>
    internal sealed class ZmcHoming
    {
        private readonly ScanConfig _cfg;
        private readonly MachineProfile _profile;
        private readonly Action<string> _log;
        private int[] _axes = new int[0];
        private string _failure = "";

        public ZmcHoming(ScanConfig cfg, MachineProfile profile, Action<string> log)
        {
            _cfg = cfg; _profile = profile; _log = log;
        }

        public HomeResult Run()
        {
            var sw = Stopwatch.StartNew();
            bool ok;

            using (var zmc = new ZmcConnection())
            {
                ok = zmc.Open(_profile.ZmcIp) ? Execute(zmc) : Fail($"cannot open ZMC at {_profile.ZmcIp}");
            }

            return new HomeResult
            {
                Success = ok,
                Detail = ok ? "" : _failure,
                Programs = string.Join(", ", _profile.HomePrograms),
                Axes = _axes,
                Elapsed = sw.Elapsed
            };
        }

        private bool Execute(ZmcConnection zmc)
        {
            if (_profile.HomePrograms.Count == 0) return Fail("profile has no home.programs");

            _axes = DiscoverAxes(zmc);
            if (_axes.Length == 0) return Fail("no axes found (set home.axes in the profile)");
            _log($"Axes: {string.Join(", ", _axes)}");

            if (!new ZmcInit(_cfg, _profile.InitProgram, _log).EnsureReady(zmc))
                return Fail($"{_profile.InitProgram} did not bring the controller to sysstate=1");

            if (!StartPrograms(zmc)) return false;
            if (!(_cfg.UseDoneFlag ? WaitForDoneFlag(zmc) : WaitForQuiet(zmc))) return false;
            return WaitForAxesIdle(zmc);
        }

        // ---------- axis discovery ----------

        private int[] DiscoverAxes(ZmcConnection zmc)
        {
            if (_profile.HomeAxes.Length > 0) return _profile.HomeAxes;

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

        private bool StartPrograms(ZmcConnection zmc)
        {
            zmc.SetVr(_cfg.HomeDoneVr, 0);               // clear stale done flag (only used if UseDoneFlag)

            var failed = new List<string>();
            foreach (string program in _profile.HomePrograms)
            {
                int rc = zmc.Execute($"RUN {program}", out string resp);
                _log($"  > RUN {program}  rc={rc}  resp=\"{resp}\"");

                // the controller reports errors as reply text with rc=0
                if (rc != 0 || resp.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) failed.Add(program);
            }

            if (failed.Count == 0) return true;

            CancelAxes(zmc);
            return Fail($"could not start: {string.Join(", ", failed)}");
        }

        private void CancelAxes(ZmcConnection zmc)
        {
            foreach (int a in _axes) zmc.Execute($"BASE({a}) CANCEL(2)");
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
                    return Fail("no axis moved after RUN (programs did not start, or home.axes is wrong)");
                Thread.Sleep(20);
            }

            var total = Stopwatch.StartNew();
            var quiet = Stopwatch.StartNew();
            while (quiet.ElapsedMilliseconds < _cfg.IdleQuietMs)
            {
                if (AnyBusy(zmc)) quiet.Restart();
                if (total.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                    return Fail($"axes still moving after {_cfg.HomeTimeoutMs / 1000} s");
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
                if (!zmc.GetVr(_cfg.HomeDoneVr, out float v)) return Fail("GetVr failed (comms/SDK error)");
                if (v == 1) return true;

                if (sw.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                {
                    CancelAxes(zmc);
                    return Fail($"timeout waiting for VR({_cfg.HomeDoneVr})=1 (now {v}) from {string.Join(", ", _profile.HomePrograms)}");
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
                        return Fail($"axis {a} still busy after {_cfg.AxisSettleTimeoutMs / 1000} s");
                    Thread.Sleep(20);
                }
            }
            return true;
        }

        private bool Fail(string message)
        {
            _failure = message;
            _log("  " + message);
            return false;
        }
    }
}
