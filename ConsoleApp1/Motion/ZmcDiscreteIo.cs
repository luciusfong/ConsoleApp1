using System;
using System.Collections.Generic;
using System.Threading;
using DeviceScanner.Native;
using DeviceScanner.Profiles;

namespace DeviceScanner.Motion
{
    /// <summary>
    /// Output-to-input loopback test. Toggles one output, snapshots ALL inputs before and after,
    /// restores the output, and checks the inputs returned. Shows raw ZMC values (1/0), before any
    /// inversion the main app's TInput/TOutput classes may apply.
    /// </summary>
    internal sealed class ZmcDiscreteIo
    {
        private readonly ScanConfig _cfg;
        private readonly Action<string> _log;

        public ZmcDiscreteIo(ScanConfig cfg, Action<string> log) { _cfg = cfg; _log = log; }

        public List<IoTestResult> RunAll(ZmcConnection zmc, IEnumerable<IoTestCase> cases)
        {
            var results = new List<IoTestResult>();
            foreach (var tc in cases) results.Add(Run(zmc, tc));
            return results;
        }

        public IoTestResult Run(ZmcConnection zmc, IoTestCase tc)
        {
            var res = new IoTestResult(tc);
            int outNo = tc.Out;
            int settle = tc.SettleMs > 0 ? tc.SettleMs : _cfg.IoSettleMs;
            int count = Math.Max(_cfg.IoScanCount, (tc.In ?? 0) + 1);

            _log($"\n[{tc.Name}] OP({outNo})" + (tc.In.HasValue ? $" -> IN({tc.In.Value})" : " -> discover"));

            int original = zmc.QueryInt($"OP({outNo})");
            if (original < 0)
            {
                string reply = zmc.Query($"OP({outNo})");
                return Abort(res, $"OP({outNo}) not readable (reply: \"{reply}\")");
            }

            bool wasOn = original != 0;
            _log($"  OP({outNo}) is {(wasOn ? "ON" : "OFF")}; switching {(wasOn ? "OFF" : "ON")}, then back");

            int[] before = ReadInputs(zmc, count);
            try
            {
                string error;
                if (!Drive(zmc, outNo, !wasOn, settle, out error)) return Abort(res, error);
                int[] toggled = ReadInputs(zmc, count);

                if (!Drive(zmc, outNo, wasOn, settle, out error)) return Abort(res, error);
                int[] restored = ReadInputs(zmc, count);

                Analyze(res, before, toggled, restored);
            }
            finally
            {
                zmc.Execute($"OP({outNo},{(wasOn ? 1 : 0)})");   // always leave the output as we found it
            }
            return res;
        }

        private IoTestResult Abort(IoTestResult res, string detail)
        {
            res.Passed = false;
            res.Detail = detail;
            _log("  FAIL: " + detail);
            return res;
        }

        private bool Drive(ZmcConnection zmc, int outNo, bool on, int settleMs, out string error)
        {
            zmc.Execute($"OP({outNo},{(on ? 1 : 0)})");
            Thread.Sleep(settleMs);

            int now = zmc.QueryInt($"OP({outNo})");
            if (now != (on ? 1 : 0))
            {
                error = $"OP({outNo}) readback = {now}, expected {(on ? 1 : 0)} (controller did not accept the write)";
                return false;
            }

            error = null;
            return true;
        }

        private int[] ReadInputs(ZmcConnection zmc, int count)
        {
            var v = new int[count];
            for (int i = 0; i < v.Length; i++) v[i] = zmc.QueryInt($"IN({i})");
            return v;
        }

        private void Analyze(IoTestResult res, int[] before, int[] toggled, int[] restored)
        {
            var tc = res.Case;

            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] < 0 || toggled[i] < 0) continue;      // input does not exist / unreadable
                if (before[i] == toggled[i]) continue;

                bool returned = restored[i] == before[i];
                res.ChangedInputs.Add(i);
                _log($"  IN({i,-3}) {before[i]} -> {toggled[i]}{(returned ? "" : $"  (did not return, now {restored[i]}: external event?)")}");
            }

            if (res.ChangedInputs.Count == 0) _log("  No input changed.");

            if (tc.In.HasValue)
            {
                int e = tc.In.Value;
                string others = res.ChangedInputs.Count > 0
                    ? "; inputs that did change: " + string.Join(", ", res.ChangedInputs)
                    : "; no input changed";

                if (!res.ChangedInputs.Contains(e))
                {
                    res.Passed = false;
                    res.Detail = $"IN({e}) did not follow{others}";
                }
                else if (restored[e] != before[e])
                {
                    res.Passed = false;
                    res.Detail = $"IN({e}) did not return after the output was restored";
                }
                else
                {
                    res.Passed = true;
                    res.Detail = $"IN({e}) {before[e]} -> {toggled[e]}";
                }
            }
            else
            {
                res.Passed = res.ChangedInputs.Count > 0;
                res.Detail = res.Passed
                    ? "reacted: IN(" + string.Join("), IN(", res.ChangedInputs) + ")"
                    : "no input reacted";
            }

            _log($"  {(res.Passed ? "PASS" : "FAIL")}: {res.Detail}");
        }
    }
}
