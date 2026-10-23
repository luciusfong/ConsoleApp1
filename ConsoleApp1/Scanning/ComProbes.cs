using System;
using System.Collections.Generic;
using NSW_Components;

namespace DeviceScanner.Scanning
{
    /// <summary>COM-port device probes (slow ones last).</summary>
    internal static class ComProbes
    {
        public static IReadOnlyList<DeviceProbe> Build(int[] tempSlaves) => new[]
        {
            new DeviceProbe("ITV regulator", com =>
            {
                var d = new SMC_ITV_RS_232C();
                return ProbeHelpers.Run(() => { d.Open(com); double v = 0; return d.Mon(ref v); }, () => d.Close());
            }),
            new DeviceProbe("MT balance", com =>
            {
                var d = new MT_WXS26S();
                return ProbeHelpers.Run(() => { d.Open(com); double g = 0; return d.ReadImme(ref g); }, () => d.Close());
            }),
            new DeviceProbe("OPT light", com =>
            {
                var d = new OPT();
                return ProbeHelpers.Run(() => d.Open(com), () => d.Close());
            }),
            new DeviceProbe("Temp TK",  com => TxtkProbe(com, "TK", tempSlaves)),
            new DeviceProbe("Temp TX",  com => TxtkProbe(com, "TX", tempSlaves)),
            new DeviceProbe("Temp TM2", com => TmProbe(com, "TM2", tempSlaves)),
            new DeviceProbe("Temp TM4", com => TmProbe(com, "TM4", tempSlaves)),
            new DeviceProbe("LC18 light", com =>
            {
                var d = new LC18();
                return ProbeHelpers.Run(() => d.Open(com, 115200) ? true : throw new Exception("no reply"), () => d.Close());
            }),
            new DeviceProbe("ViscoTec EcoSpray", com =>
            {
                var d = new ViscoTec_EC200();
                return ProbeHelpers.Run(() => d.Open(com) ? true : throw new Exception("no reply"), () => d.Close());
            }),
        };

        private static ProbeResult TxtkProbe(string com, string model, int[] slaves)
        {
            var d = new AutonicTXTK();
            var m = (AutonicTXTK.ETempModel)Enum.Parse(typeof(AutonicTXTK.ETempModel), model);
            return ProbeHelpers.Run(() =>
            {
                if (!d.Open(com, m)) return false;
                foreach (int s in slaves) if (d.Read_PresentValue(s, out int pv)) return true;
                throw new Exception("no slave replied");
            }, () => d.Close());
        }

        private static ProbeResult TmProbe(string com, string model, int[] slaves)
        {
            var d = new AutonicTM();
            var m = (AutonicTM.ETempModel)Enum.Parse(typeof(AutonicTM.ETempModel), model);
            return ProbeHelpers.Run(() =>
            {
                if (!d.Open(com, m)) return false;
                foreach (int s in slaves) if (d.Read_PresentValue(s, 1, out int pv)) return true; // channel 1: use 0 if zero-based
                throw new Exception("no slave replied");
            }, () => d.Close());
        }
    }
}
