using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using DeviceScanner.Native;

namespace DeviceScanner.Motion
{
    /// <summary>Runs the controller's homing BASIC program and waits for it to finish.</summary>
    internal sealed class ZmcHoming
    {
        private bool AnyBusy(IntPtr h)
        {
            foreach (int a in _cfg.HomeAxes)
            {
                int idle = 0;
                int rc = ZAux.GetIfIdle(h, a, ref idle);
                if (rc != 0) _log($"GetIfIdle axis {a} rc={rc}");
                if (idle != -1) return true;
            }
            return false;
        }

        private bool WaitForQuiet(IntPtr h)
        {
            // 1) motion must start
            var sw = Stopwatch.StartNew();
            while (!AnyBusy(h))
            {
                if (sw.ElapsedMilliseconds > _cfg.StartWaitMs)
                {
                    _log("No axis moved after RUN. GHome may not have started, or HomeAxes are wrong.");
                    return false;
                }
                Thread.Sleep(20);
            }

            // 2) wait until everything has been idle for IdleQuietMs
            var total = Stopwatch.StartNew();
            var quiet = Stopwatch.StartNew();
            while (quiet.ElapsedMilliseconds < _cfg.IdleQuietMs)
            {
                if (AnyBusy(h)) quiet.Restart();
                if (total.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                {
                    _log("Homing still moving after timeout.");
                    return false;
                }
                Thread.Sleep(20);
            }
            return true;
        }

        private readonly ScanConfig _cfg;
        private readonly Action<string> _log;

        public ZmcHoming(ScanConfig cfg, Action<string> log) { _cfg = cfg; _log = log; }

        public bool Run(string ip)
        {
            if (ZAux.OpenEth(ip, out IntPtr h) != 0) { _log("Cannot reopen ZMC."); return false; }

            try
            {
                return StartProgram(h)
                && (_cfg.UseDoneFlag ? WaitForDoneFlag(h) : WaitForQuiet(h))
                && WaitForAxesIdle(h);
            }
            finally { ZAux.Close(h); }
        }

        private bool StartProgram(IntPtr h)
        {
            ZAux.SetVrf(h, _cfg.HomeDoneVr, 1, new float[] { 0 });   // clear stale done flag
            if (ZAux.Execute(h, $"RUN {_cfg.HomeProgram}", new StringBuilder(256), 256) != 0)
            {
                _log($"RUN {_cfg.HomeProgram} failed.");
                return false;
            }
            return true;
        }

        private bool WaitForDoneFlag(IntPtr h)
        {
            var sw = Stopwatch.StartNew();
            var vr = new float[1];
            while (true)
            {
                ZAux.GetVrf(h, _cfg.HomeDoneVr, 1, vr);
                if (vr[0] == 1) return true;

                if (sw.ElapsedMilliseconds > _cfg.HomeTimeoutMs)
                {
                    var resp = new StringBuilder(256);
                    foreach (int a in _cfg.HomeAxes) ZAux.Execute(h, $"BASE({a}) CANCEL(2)", resp, 256);
                    _log($"{_cfg.HomeProgram} timeout.");
                    return false;
                }
                Thread.Sleep(50);
            }
        }

        private bool WaitForAxesIdle(IntPtr h)
        {
            foreach (int a in _cfg.HomeAxes)
            {
                int idle = 0;
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    ZAux.GetIfIdle(h, a, ref idle);
                    if (idle == -1) break;
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
