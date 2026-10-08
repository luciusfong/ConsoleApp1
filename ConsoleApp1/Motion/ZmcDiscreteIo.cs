using System;
using System.Collections.Generic;
using System.Threading;
using DeviceScanner.Native;

namespace DeviceScanner.Motion
{
    /// <summary>
    /// Output-to-input loopback test. Toggles one output, snapshots ALL inputs before and after,
    /// then restores the output and checks the inputs returned. Shows raw ZMC values (1/0),
    /// before any inversion the main app's TInput/TOutput classes may apply.
    /// </summary>
    internal sealed class ZmcDiscreteIo
    {
        private readonly ScanConfig _cfg;
        private readonly Action<string> _log;

        public ZmcDiscreteIo(ScanConfig cfg, Action<string> log) { _cfg = cfg; _log = log; }

        /// <param name="expectedIn">Input you expect to follow the output, or null to just discover what reacts.</param>
        public bool Run(ZmcConnection zmc, int outNo, int? expectedIn, int settleMs)
        {
            int original = zmc.QueryInt($"OP({outNo})");
            if (original < 0)
            {
                _log($"OP({outNo}) could not be read. Wrong number, or the reply was not numeric: \"{zmc.Query($"OP({outNo})")}\"");
                return false;
            }

            bool wasOn = original != 0;
            _log($"OP({outNo}) is {(wasOn ? "ON" : "OFF")}; will switch {(wasOn ? "OFF" : "ON")}, then back.");

            int[] before = ReadInputs(zmc);
            try
            {
                if (!Drive(zmc, outNo, !wasOn, settleMs)) return false;
                int[] toggled = ReadInputs(zmc);

                if (!Drive(zmc, outNo, wasOn, settleMs)) return false;
                int[] restored = ReadInputs(zmc);

                return Report(outNo, expectedIn, before, toggled, restored);
            }
            finally
            {
                zmc.Execute($"OP({outNo},{(wasOn ? 1 : 0)})");   // always leave the output as we found it
            }
        }

        private bool Drive(ZmcConnection zmc, int outNo, bool on, int settleMs)
        {
            zmc.Execute($"OP({outNo},{(on ? 1 : 0)})");
            Thread.Sleep(settleMs);

            int now = zmc.QueryInt($"OP({outNo})");
            if (now != (on ? 1 : 0))
            {
                _log($"  OP({outNo}) readback = {now}, expected {(on ? 1 : 0)}. The controller did not accept the write.");
                return false;
            }
            return true;
        }

        private int[] ReadInputs(ZmcConnection zmc)
        {
            var v = new int[_cfg.IoScanCount];
            for (int i = 0; i < v.Length; i++) v[i] = zmc.QueryInt($"IN({i})");
            return v;
        }

        private bool Report(int outNo, int? expectedIn, int[] before, int[] toggled, int[] restored)
        {
            var changed = new List<int>();
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] < 0 || toggled[i] < 0) continue;      // input does not exist / unreadable
                if (before[i] == toggled[i]) continue;

                bool returned = restored[i] == before[i];
                changed.Add(i);
                _log($"  IN({i,-3}) {before[i]} -> {toggled[i]}{(returned ? "" : $"  (did not return, now {restored[i]}: external event?)")}");
            }

            if (changed.Count == 0) _log("  No input changed.");

            if (expectedIn.HasValue)
            {
                int e = expectedIn.Value;
                bool ok = changed.Contains(e) && restored[e] == before[e];
                _log(ok
                    ? $"  PASS: OP({outNo}) -> IN({e})"
                    : $"  FAIL: IN({e}) did not follow OP({outNo})" + (changed.Count > 0 ? $"; inputs that did change: {string.Join(", ", changed)}" : ""));
                return ok;
            }

            return changed.Count > 0;
        }
    }
}