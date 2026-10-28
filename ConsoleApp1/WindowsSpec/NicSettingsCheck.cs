using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using DeviceScanner.Reporting;
using Microsoft.Win32;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// Every physical wired Ethernet adapter:
    ///   Advanced:  Energy Efficient Ethernet = Off, Interrupt Moderation = Disabled, Jumbo Packet = Disabled,
    ///              Receive Buffers = 2048, Transmit Buffers = 2048
    ///   Power management: all three boxes unchecked
    ///              (allow computer to turn off device / allow device to wake / only allow magic packet to wake)
    /// Advanced properties are read by their registry KEYWORD (*EEE, *JumboPacket, ...), not by the display
    /// name, so the check works even when the driver UI is still in Chinese.
    /// </summary>
    internal sealed class NicSettingsCheck : SpecCheck
    {
        private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        public NicSettingsCheck(WindowsSpecConfig spec, Action<string> log) : base(spec, log) { }

        public override string Title => "Ethernet adapters (advanced + power management)";

        private sealed class Adapter
        {
            public string Name;          // "Intel(R) I210 Gigabit Network Connection #2" (matches root\WMI InstanceName)
            public string Connection;    // "Ethernet 2"
            public string Guid;          // NetCfgInstanceId
            public string PnpId;         // PCI\VEN_8086&DEV_1533&...
            public bool Enabled;
        }

        /// <summary>One advanced property rule. Keywords are alternatives used by different vendors.</summary>
        private sealed class Rule
        {
            public string Label, Expected;
            public string[] Keywords;
            public string[] Accept;
            public bool MissingIsPass;   // property not exposed by the driver = it cannot be on
        }

        protected override void Execute()
        {
            var adapters = PhysicalEthernet(out string err);
            if (err != null) { Add("NIC", "adapters", "", "unknown", CheckStatus.Unverified, err); return; }
            if (adapters.Count == 0) { Add("NIC", "adapters", "at least one", "none found", CheckStatus.Fail); return; }

            var rules = new[]
            {
                new Rule { Label = "Energy Efficient Ethernet", Expected = "Off",      Keywords = new[] { "*EEE", "EEELinkAdvertisement", "EEE" }, Accept = new[] { "0" },            MissingIsPass = true },
                new Rule { Label = "Interrupt Moderation",      Expected = "Disabled", Keywords = new[] { "*InterruptModeration" },               Accept = new[] { "0" },            MissingIsPass = true },
                new Rule { Label = "Jumbo Packet",              Expected = "Disabled", Keywords = new[] { "*JumboPacket" },                       Accept = new[] { "1514", "1500" }, MissingIsPass = true },
                new Rule { Label = "Receive Buffers",           Expected = Spec.ReceiveBuffers,  Keywords = new[] { "*ReceiveBuffers" },  Accept = new[] { Spec.ReceiveBuffers } },
                new Rule { Label = "Transmit Buffers",          Expected = Spec.TransmitBuffers, Keywords = new[] { "*TransmitBuffers" }, Accept = new[] { Spec.TransmitBuffers } },
            };

            var power = PowerSettings();

            foreach (var a in adapters)
            {
                int sub = a.PnpId.IndexOf("&SUBSYS", StringComparison.OrdinalIgnoreCase);
                string shortPnp = sub > 0 ? a.PnpId.Substring(0, sub) : a.PnpId;      // PCI\VEN_8086&DEV_1521
                string group = $"NIC {a.Connection}: {a.Name} [{shortPnp}]";
                Log($"    {group}  [{a.PnpId}]{(a.Enabled ? "" : "  (disabled)")}");

                if (Cjk.HasChinese(a.Connection))
                    Add(group, "Connection name", "no Chinese", a.Connection, CheckStatus.Unverified, "rename the connection in ncpa.cpl");

                using (var key = ClassKey(a.Guid))
                {
                    if (key == null) { Add(group, "driver key", "", "not found", CheckStatus.Unverified, "no registry key for " + a.Guid); }
                    else foreach (var r in rules) CheckRule(group, key, r);
                }

                CheckPower(group, a, power);
            }
        }

        // ---------- adapters ----------

        private static List<Adapter> PhysicalEthernet(out string error)
        {
            var rows = Wmi.Query(@"\\.\root\cimv2",
                "SELECT Name, NetConnectionID, GUID, PNPDeviceID, NetEnabled FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", out error);

            var list = new List<Adapter>();
            foreach (var o in rows)
            {
                string pnp = o["PNPDeviceID"] as string ?? "";
                string guid = o["GUID"] as string;
                if (guid == null) continue;                                      // no driver bound
                if (!(pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
                      pnp.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))) continue;   // virtual (ROOT\, SWD\, VMBUS\...)

                using (var k = ClassKey(guid))
                {
                    int ifType = Reg.Int(k, "*IfType") ?? 6;
                    int media = Reg.Int(k, "*PhysicalMediaType") ?? 0;
                    if (ifType != 6) continue;                                   // 6 = ethernetCsmacd ; Wi-Fi is 71
                    if (media == 9 || media == 1 || media == 8) continue;        // 9 = native 802.11, 1 = WLAN, 8 = WWAN
                }

                bool enabled = o["NetEnabled"] is bool b && b;
                list.Add(new Adapter
                {
                    Name = o["Name"] as string ?? "",
                    Connection = o["NetConnectionID"] as string ?? "",
                    Guid = guid,
                    PnpId = pnp,
                    Enabled = enabled,
                });
            }
            return list.OrderBy(a => a.Connection, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Class\{4d36e972...}\00xx whose NetCfgInstanceId is this adapter's GUID.</summary>
        private static RegistryKey ClassKey(string guid)
        {
            using (var cls = Reg.Hklm(NetClass))
            {
                if (cls == null) return null;
                foreach (string sub in cls.GetSubKeyNames())
                {
                    if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;     // skip "Properties", "Configuration"
                    RegistryKey k = null;
                    try { k = cls.OpenSubKey(sub, false); } catch { }
                    if (k == null) continue;
                    if (string.Equals(Reg.Str(k, "NetCfgInstanceId"), guid, StringComparison.OrdinalIgnoreCase)) return k;
                    k.Dispose();
                }
            }
            return null;
        }

        // ---------- advanced properties ----------

        private void CheckRule(string group, RegistryKey key, Rule r)
        {
            var seen = new List<string>();
            bool ok = true;

            foreach (string kw in r.Keywords)
            {
                string v = Reg.Str(key, kw);
                if (v == null)
                {
                    // not written yet: the driver uses the INF default from Ndi\Params\<kw>\default
                    using (var p = key.OpenSubKey(@"Ndi\Params\" + kw, false))
                    {
                        if (p == null) continue;                                 // driver does not have this keyword
                        v = Reg.Str(p, "default");
                        if (v == null) continue;
                        v += " (default)";
                    }
                }
                seen.Add($"{kw}={v}");
                string raw = v.Replace(" (default)", "").Trim();
                if (!r.Accept.Contains(raw)) ok = false;
            }

            if (seen.Count == 0)
            {
                Add(group, r.Label, r.Expected, "not supported by driver",
                    r.MissingIsPass ? CheckStatus.Pass : CheckStatus.Unverified,
                    r.MissingIsPass ? "" : "property not exposed; cannot be set on this adapter");
                return;
            }

            Add(group, r.Label, $"{r.Expected} ({string.Join("/", r.Accept)})", string.Join(", ", seen), PassIf(ok));
        }

        // ---------- power management tab (root\WMI, needs admin) ----------

        private sealed class PowerData
        {
            public string Error;
            public Dictionary<string, bool> TurnOff = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);   // MSPower_DeviceEnable
            public Dictionary<string, bool> Wake = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);      // MSPower_DeviceWakeEnable
            public Dictionary<string, bool> MagicOnly = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // MSNdis_DeviceWakeOnMagicPacketOnly
        }

        private static PowerData PowerSettings()
        {
            var d = new PowerData();
            Fill(d.TurnOff, "MSPower_DeviceEnable", "Enable", ref d.Error);
            Fill(d.Wake, "MSPower_DeviceWakeEnable", "Enable", ref d.Error);
            Fill(d.MagicOnly, "MSNdis_DeviceWakeOnMagicPacketOnly", "EnableWakeOnMagicPacketOnly", ref d.Error);
            return d;
        }

        private static void Fill(Dictionary<string, bool> map, string cls, string prop, ref string error)
        {
            var rows = Wmi.Query(@"\\.\root\WMI", $"SELECT InstanceName, {prop} FROM {cls}", out string err);
            if (err != null) { error = error ?? err; return; }
            foreach (var o in rows)
            {
                string inst = o["InstanceName"] as string;
                if (inst != null && o[prop] is bool b) map[inst] = b;
            }
        }

        private void CheckPower(string group, Adapter a, PowerData p)
        {
            // MSPower_* InstanceName = PNPDeviceID + "_0"; MSNdis_* InstanceName = adapter name
            Power(group, a, "Allow computer to turn off this device", p, Find(p.TurnOff, a.PnpId, true));
            Power(group, a, "Allow this device to wake the computer", p, Find(p.Wake, a.PnpId, true));
            Power(group, a, "Only allow a magic packet to wake", p, Find(p.MagicOnly, a.Name, false));
        }

        private static bool? Find(Dictionary<string, bool> map, string key, bool prefix)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var kv in map)
                if (prefix ? kv.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase)
                           : string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return null;
        }

        private void Power(string group, Adapter a, string label, PowerData p, bool? enabled)
        {
            if (enabled.HasValue)
                Add(group, label, "unchecked", enabled.Value ? "checked" : "unchecked", PassIf(!enabled.Value));
            else if (p.Error != null)
                Add(group, label, "unchecked", "unknown", CheckStatus.Unverified, p.Error);
            else if (!a.Enabled)
                Add(group, label, "unchecked", "unknown", CheckStatus.Unverified, "adapter disabled; enable it and run again");
            else
                Add(group, label, "unchecked", "not supported", CheckStatus.Pass, "option not offered for this adapter");
        }
    }
}
