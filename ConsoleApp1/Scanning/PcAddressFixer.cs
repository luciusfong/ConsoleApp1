using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;
using DeviceScanner.WindowsSpec;
using Microsoft.Win32;

namespace DeviceScanner.Scanning
{
    /// <summary>
    /// PC side of discovery: makes this PC's addresses match the profile. Devices themselves are never changed.
    ///   NIC  every profile subnet (192.168.x.0/24 of zmc.ip and ip.expect) needs exactly one local NIC on it.
    ///        Camera subnets must be on the camera NIC (ScanConfig.CameraNicPnpId, Intel I350); others on any wired NIC.
    ///        A subnet with no NIC is given to the one free, connected, wired NIC as a static 192.168.x.200.
    ///   COM  a profile COM device found by the COM scan on another port is renamed to the profile's port number.
    /// Anything that cannot be decided safely is a conflict (FAIL) and nothing is changed for it.
    /// Needs admin rights; <paramref name="apply"/> = false only plans and logs (nothing is changed).
    /// </summary>
    internal sealed class PcAddressFixer
    {
        private readonly ScanConfig _cfg;
        private readonly MachineProfile _profile;
        private readonly Action<string> _log;
        private readonly bool _apply;

        public PcAddressFixer(ScanConfig cfg, MachineProfile profile, Action<string> log, bool apply = true)
        {
            _cfg = cfg; _profile = profile; _log = log; _apply = apply;
        }

        // ================= NICs =================

        private sealed class Nic
        {
            public NetworkInterface Net;
            public string Name => Net.Name;
            public string PnpId = "";
            public List<(IPAddress Ip, IPAddress Mask)> V4 = new List<(IPAddress, IPAddress)>();
            public bool Wired => Net.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                                 (PnpId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
                                  PnpId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase));
            public bool Up => Net.OperationalStatus == OperationalStatus.Up;
            public string Label => $"{Net.Name} / {Net.Description}";
        }

        /// <summary>Run before the ping sweep, so the sweep can reach every profile subnet.</summary>
        public void FixNics(RunReport report)
        {
            _log("\n--- PC network addresses (profile subnets) ---");

            // profile subnets ("192.168.0"), and which of them hold a camera
            var devices = new List<ExpectedDevice>(_profile.ExpectedIp);
            if (!string.IsNullOrWhiteSpace(_profile.ZmcIp)) devices.Add(new ExpectedDevice(_profile.ZmcIp, IpProbes.Zmc));
            var subnets = devices.Select(d => Subnet(d.Address)).Where(s => s != null).Distinct().ToList();
            var cameraSubnets = new HashSet<string>(devices.Where(d => d.Name == "Camera GigE").Select(d => Subnet(d.Address)).Where(s => s != null));
            var profileIps = new HashSet<string>(devices.Select(d => d.Address));

            List<Nic> nics = ReadNics();
            foreach (var n in nics)
                _log($"  {n.Label} [{Short(n.PnpId)}] {(n.Up ? "up" : "down")}: {(n.V4.Count == 0 ? "no IPv4" : string.Join(", ", n.V4.Select(a => a.Ip)))}");

            var taken = new HashSet<Nic>();                       // NICs already serving a profile subnet (or just given one)
            foreach (string s in subnets) foreach (var n in nics) if (Holds(n, s)) taken.Add(n);

            // camera subnets first: they are the only ones tied to a NIC model
            foreach (string subnet in subnets.OrderBy(s => cameraSubnets.Contains(s) ? 0 : 1))
            {
                bool camera = cameraSubnets.Contains(subnet);
                var holders = nics.Where(n => Holds(n, subnet)).ToList();
                string net = subnet + ".0/24";

                if (camera && holders.Any(h => !IsCameraNic(h)))
                {
                    foreach (var h in holders.Where(x => !IsCameraNic(x)))
                        Conflict(report, "NIC", h.Name, net, $"{net} (cameras) is on {h.Label}, but cameras must be on {_cfg.CameraNicPnpId}; remove it from that adapter");
                    continue;
                }
                if (holders.Count > 1)
                {
                    Conflict(report, "NIC", string.Join(", ", holders.Select(h => h.Name)), net, $"{net} is on {holders.Count} adapters ({string.Join("; ", holders.Select(h => h.Label))}); keep it on one");
                    continue;
                }
                if (holders.Count == 1)
                {
                    _log($"  {net}: OK on {holders[0].Label}");
                    continue;
                }

                // nobody serves this subnet: the one free, connected, wired NIC (camera NIC model for camera subnets)
                var free = nics.Where(n => n.Wired && n.Up && !taken.Contains(n) && OnlyLinkLocal(n) && (!camera || IsCameraNic(n))).ToList();
                if (free.Count != 1)
                {
                    string what = camera ? $"connected {_cfg.CameraNicPnpId} port" : "connected wired adapter";
                    Conflict(report, "NIC", "", net, free.Count == 0
                        ? $"no NIC is on {net} and no free {what} without another address was found"
                        : $"no NIC is on {net} and {free.Count} free {what}s could take it ({string.Join("; ", free.Select(f => f.Label))}); cannot tell which one is cabled to it");
                    continue;
                }

                var nic = free[0];
                taken.Add(nic);
                string ip = FreeHostIp(subnet, profileIps);
                if (ip == null) { Conflict(report, "NIC", nic.Name, net, $"no free host address .{_cfg.PcHostOctet}-.254 in {net}"); continue; }
                SetStaticIp(report, nic, ip);
            }
        }

        private void SetStaticIp(RunReport report, Nic nic, string ip)
        {
            string from = nic.V4.Count == 0 ? "no IPv4" : string.Join(", ", nic.V4.Select(a => a.Ip));
            var fix = new AddressFix { Kind = "NIC", Target = nic.Label, From = from, To = ip + "/24" };
            report.AddressFixes.Add(fix);

            if (!_apply) { Done(fix, CheckStatus.Unverified, "planned only (no change)"); return; }
            if (!Elevation.IsAdmin()) { Done(fix, CheckStatus.Fail, "needs administrator rights"); return; }

            int code = Proc.Run("netsh", $"interface ipv4 set address name=\"{nic.Name}\" source=static address={ip} mask=255.255.255.0", 30000, out string output);
            if (code != 0) { Done(fix, CheckStatus.Fail, $"netsh exit code {code}: {output.Trim()}"); return; }

            // the address shows up after a moment (DAD)
            for (int i = 0; i < 20; i++)
            {
                var now = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == nic.Net.Id);
                if (now != null && now.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == ip))
                {
                    Done(fix, CheckStatus.Pass, "static address set");
                    return;
                }
                Thread.Sleep(500);
            }
            Done(fix, CheckStatus.Unverified, "netsh accepted it but the address is not active yet");
        }

        private List<Nic> ReadNics() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n => new Nic
                {
                    Net = n,
                    PnpId = NicRoute.PnpIdOf(n.Id),
                    V4 = n.GetIPProperties().UnicastAddresses
                          .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                          .Select(a => (a.Address, a.IPv4Mask)).ToList(),
                })
                .ToList();

        private bool IsCameraNic(Nic n) => n.PnpId.IndexOf(_cfg.CameraNicPnpId, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The NIC has an address inside 192.168.x.0/24.</summary>
        private static bool Holds(Nic n, string subnet) => n.V4.Any(a => Subnet(a.Ip.ToString()) == subnet);

        /// <summary>No address, or only 169.254.x.x: nothing else (an office LAN, a DHCP lease) would be lost by giving it a static IP.</summary>
        private static bool OnlyLinkLocal(Nic n) => n.V4.All(a => a.Ip.ToString().StartsWith("169.254.", StringComparison.Ordinal));

        private string FreeHostIp(string subnet, HashSet<string> profileIps)
        {
            for (int h = _cfg.PcHostOctet; h <= 254; h++)
            {
                string ip = $"{subnet}.{h}";
                if (profileIps.Contains(ip)) continue;
                try { using (var p = new Ping()) if (p.Send(ip, _cfg.PingTimeoutMs).Status == IPStatus.Success) continue; }
                catch { }
                return ip;
            }
            return null;
        }

        /// <summary>"192.168.0" for 192.168.0.11; null if not an IPv4 address.</summary>
        private static string Subnet(string ip)
        {
            if (!IPAddress.TryParse(ip ?? "", out var a) || a.AddressFamily != AddressFamily.InterNetwork) return null;
            var b = a.GetAddressBytes();
            return $"{b[0]}.{b[1]}.{b[2]}";
        }

        private static string Short(string pnp)
        {
            if (string.IsNullOrEmpty(pnp)) return "?";
            int i = pnp.IndexOf("&SUBSYS", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? pnp.Substring(0, i) : pnp;
        }

        // ================= COM ports =================

        /// <summary>Run after the COM scan: renames ports so each profile COM device sits on its profile port number.</summary>
        public void FixComPorts(RunReport report)
        {
            if (report.DiscoveredCom == null || _profile.ExpectedCom.Count == 0) return;
            _log("\n--- PC COM port numbers (profile) ---");

            // ports already verified at the right number by the Devices module
            var verified = new HashSet<string>(report.Devices.Where(d => d.Kind == "COM" && d.Status == CheckStatus.Pass)
                                                             .Select(d => d.Address), StringComparer.OrdinalIgnoreCase);
            var found = report.DiscoveredCom.Found.ToList();                 // (device, port) as the scan saw it
            var probes = ComProbes.Build(_profile.TempSlaves);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);    // device types settled (moved or conflict)

            // a rename can free the number another device needs, so repeat until nothing more moves
            for (bool progress = true; progress;)
            {
                progress = false;
                foreach (var group in _profile.ExpectedCom.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string device = group.Key;
                    if (done.Contains(device)) continue;
                    var expected = group.Select(e => e.Address).ToList();
                    var missing = expected.Where(p => !verified.Contains(p) && !found.Any(f => Same(f.Port, p) && Same(f.Name, device))).ToList();
                    var elsewhere = found.Where(f => Same(f.Name, device) && !expected.Any(p => Same(p, f.Port))).Select(f => f.Port).ToList();
                    if (missing.Count == 0 || elsewhere.Count == 0) continue;

                    if (missing.Count > 1 || elsewhere.Count > 1)
                    {
                        Conflict(report, "COM", device, string.Join(", ", elsewhere) + " -> " + string.Join(", ", missing),
                            $"{elsewhere.Count} {device}(s) found on {string.Join(", ", elsewhere)}, profile expects {string.Join(", ", missing)}; identical devices cannot be told apart");
                        done.Add(device);
                        continue;
                    }

                    string from = elsewhere[0], to = missing[0];
                    if (SerialPort.GetPortNames().Any(p => Same(p, to)))
                    {
                        // maybe a later rename frees it: only a conflict when nothing else can move
                        continue;
                    }

                    var fix = Rename(report, device, from, to, probes.FirstOrDefault(p => Same(p.Name, device)));
                    done.Add(device);
                    if (fix.Status != CheckStatus.Fail)
                    {
                        found.RemoveAll(f => Same(f.Port, from));
                        found.Add((device, to));
                        progress = true;
                    }
                }
            }

            // whatever is still blocked by a taken number
            foreach (var group in _profile.ExpectedCom.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (done.Contains(group.Key)) continue;
                var expected = group.Select(e => e.Address).ToList();
                var missing = expected.Where(p => !verified.Contains(p) && !found.Any(f => Same(f.Port, p) && Same(f.Name, group.Key))).ToList();
                var elsewhere = found.Where(f => Same(f.Name, group.Key) && !expected.Any(p => Same(p, f.Port))).Select(f => f.Port).ToList();
                if (missing.Count == 1 && elsewhere.Count == 1)
                {
                    string holder = found.Where(f => Same(f.Port, missing[0])).Select(f => f.Name).FirstOrDefault() ?? "another port";
                    Conflict(report, "COM", group.Key, $"{elsewhere[0]} -> {missing[0]}", $"{missing[0]} is already used by {holder}");
                }
            }
        }

        private AddressFix Rename(RunReport report, string device, string from, string to, DeviceProbe probe)
        {
            var fix = new AddressFix { Kind = "COM", Target = device, From = from, To = to };
            report.AddressFixes.Add(fix);

            if (!_apply) { Done(fix, CheckStatus.Unverified, "planned only (no change)"); return fix; }
            if (!Elevation.IsAdmin()) { Done(fix, CheckStatus.Fail, "needs administrator rights"); return fix; }

            var rows = Wmi.Query(@"\\.\root\cimv2", $"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%({from})%'", out string err);
            if (err != null || rows.Count != 1)
            {
                Done(fix, CheckStatus.Fail, err ?? $"{rows.Count} device(s) named ({from}); cannot tell which to rename");
                return fix;
            }
            string id = rows[0]["PNPDeviceID"] as string;
            string name = rows[0]["Name"] as string ?? "";

            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var p = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{id}\Device Parameters", true))
                    {
                        if (p == null) { Done(fix, CheckStatus.Fail, $"no Device Parameters key for {id}"); return fix; }
                        p.SetValue("PortName", to, RegistryValueKind.String);
                    }
                    using (var d = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{id}", true))
                    {
                        if (d?.GetValue("FriendlyName") is string friendly)
                            d.SetValue("FriendlyName", friendly.Replace($"({from})", $"({to})"), RegistryValueKind.String);
                    }
                    // COM Name Arbiter: one bit per port number in use
                    using (var a = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter", true))
                    {
                        if (a?.GetValue("ComDB") is byte[] db)
                        {
                            db = Bit(Bit(db, Number(from), false), Number(to), true);
                            a.SetValue("ComDB", db, RegistryValueKind.Binary);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Done(fix, CheckStatus.Fail, "registry: " + ex.Message);
                return fix;
            }

            // the new name is used once the port restarts
            int code = Proc.Run("pnputil", $"/restart-device \"{id}\"", 60000, out string output);
            _log($"    pnputil /restart-device {id} -> {code}");
            bool renamed = false;
            for (int i = 0; i < 20 && !renamed; i++)
            {
                var ports = SerialPort.GetPortNames();
                renamed = ports.Any(p => Same(p, to)) && !ports.Any(p => Same(p, from));
                if (!renamed) Thread.Sleep(500);
            }
            if (!renamed)
            {
                Done(fix, CheckStatus.Unverified, $"{name}: renamed in the registry; restart the PC (or unplug the port) to apply" +
                                                  (code != 0 ? $" (pnputil exit code {code})" : ""));
                return fix;
            }

            ProbeResult? answered = probe?.Run(to);
            if (answered == null || answered.Value.Status == ProbeStatus.Fail)
                Done(fix, CheckStatus.Unverified, $"{name}: renamed, but the {device} did not answer on {to}" + (answered == null ? "" : ": " + answered.Value.Message));
            else
                Done(fix, CheckStatus.Pass, $"renamed and the {device} answers on {to}");
            return fix;
        }

        private static int Number(string com) => int.TryParse(com.Substring(3), out int n) ? n : 0;

        private static byte[] Bit(byte[] db, int port, bool on)
        {
            if (port < 1) return db;
            int i = (port - 1) / 8;
            if (i >= db.Length) Array.Resize(ref db, i + 1);
            byte mask = (byte)(1 << ((port - 1) % 8));
            db[i] = on ? (byte)(db[i] | mask) : (byte)(db[i] & ~mask);
            return db;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ================= results =================

        private void Conflict(RunReport report, string kind, string target, string what, string detail)
        {
            var fix = new AddressFix { Kind = kind, Target = target, To = what };
            report.AddressFixes.Add(fix);
            Done(fix, CheckStatus.Fail, "conflict: " + detail);
        }

        private void Done(AddressFix fix, CheckStatus status, string detail)
        {
            fix.Status = status;
            fix.Detail = detail;
            _log($"  {SpecCheck.Tag(status),-5} {fix.Kind} {fix.Target} {(fix.From.Length > 0 ? fix.From + " -> " : "")}{fix.To}: {detail}");
        }
    }
}
