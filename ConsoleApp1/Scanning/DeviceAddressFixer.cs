using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using DeviceScanner.Native;
using DeviceScanner.Profiles;
using DeviceScanner.Reporting;
using DeviceScanner.Utils;
using DeviceScanner.WindowsSpec;
using MvCamCtrl.NET;

namespace DeviceScanner.Scanning
{
    /// <summary>
    /// Device side of discovery: a GigE camera or the ZMC that answers at an address the profile does not list is
    /// moved to the profile address. Only when it is unambiguous: exactly one such device of that type and exactly
    /// one profile address of that type with nothing on it, free, and on a subnet a local NIC is on. Anything else is
    /// a conflict (FAIL) and nothing is changed. The operator confirms every change.
    ///   Camera  found by GigE Vision enumeration (any subnet), forced to the new IP and stored as its persistent IP.
    ///   ZMC     found by the ping sweep, set with IP_ADDRESS; some firmware applies it only after a power cycle.
    /// </summary>
    internal sealed class DeviceAddressFixer
    {
        private const string Camera = "Camera GigE";

        private readonly ScanConfig _cfg;
        private readonly MachineProfile _profile;
        private readonly Func<string, bool> _confirm;
        private readonly Action<string> _log;

        public DeviceAddressFixer(ScanConfig cfg, MachineProfile profile, Func<string, bool> confirm, Action<string> log)
        {
            _cfg = cfg; _profile = profile; _confirm = confirm; _log = log;
        }

        // ================= GigE cameras =================

        private sealed class Cam
        {
            public MyCamera.MV_CC_DEVICE_INFO Info;
            public string Mac, Ip, Via, Model, Serial;
            public string Label => $"{Model} {Serial} ({Mac})";
        }

        /// <summary>Run after the ping sweep, so profile addresses that answer are known.</summary>
        public void FixCameras(RunReport report)
        {
            var expected = _profile.ExpectedIp.Where(d => d.Name == Camera).Select(d => d.Address).ToList();
            if (expected.Count == 0) return;
            _log("\n--- Camera addresses (profile) ---");

            List<Cam> cams;
            try { cams = EnumCameras(); }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is TypeLoadException)
            {
                _log($"  camera SDK not available: {ex.Message}");
                return;
            }
            foreach (var c in cams) _log($"  {c.Label}: {c.Ip} via {c.Via}");

            var wrong = cams.Where(c => !expected.Contains(c.Ip)).ToList();
            var missing = expected.Where(ip => !cams.Any(c => c.Ip == ip)).ToList();
            if (wrong.Count == 0 || missing.Count == 0) return;           // nothing to move, or an extra camera only

            if (wrong.Count > 1 || missing.Count > 1)
            {
                Conflict(report, "Camera", string.Join(", ", wrong.Select(c => c.Label)),
                    string.Join(", ", wrong.Select(c => c.Ip)) + " -> " + string.Join(", ", missing),
                    $"{wrong.Count} camera(s) at {string.Join(", ", wrong.Select(c => c.Ip))}, profile expects {string.Join(", ", missing)}; cannot tell which camera is which");
                return;
            }

            var cam = wrong[0];
            string to = missing[0];
            if (!CanTake(report, "Camera", cam.Label, cam.Ip, to)) return;
            if (!Holds(cam.Via, Subnet(to)))
            {
                Conflict(report, "Camera", cam.Label, $"{cam.Ip} -> {to}", $"the camera is cabled to the NIC at {cam.Via}, which is not on {Subnet(to)}.0/24");
                return;
            }

            var fix = Start(report, "Camera", cam.Label, cam.Ip, to);
            if (!_confirm(L.T("confirm.deviceIp", L.T("dev.camera"), cam.Label, cam.Ip, to))) { Done(fix, CheckStatus.Fail, "not changed (declined)"); return; }

            var dev = new MyCamera();
            int rc = dev.MV_CC_CreateDevice_NET(ref cam.Info);
            if (rc == MyCamera.MV_OK)
            {
                rc = dev.MV_GIGE_ForceIpEx_NET(ToUint(to), ToUint("255.255.255.0"), 0);
                dev.MV_CC_DestroyDevice_NET();
            }
            if (rc != MyCamera.MV_OK) { Done(fix, CheckStatus.Fail, $"force IP failed (0x{rc:X8}); is it open in another program?"); return; }
            _log($"    forced {cam.Mac} to {to}");

            // forced IPs are lost on power-up: store it as the persistent IP
            string persist = Persist(cam.Mac, to);
            var probe = IpProbes.Build(_cfg).First(p => p.Name == Camera);
            ProbeResult answered = probe.Run(to);
            if (persist != null)
                Done(fix, CheckStatus.Unverified, $"moved to {to}, but storing it failed ({persist}); it goes back after a power cycle");
            else if (answered.Status == ProbeStatus.Fail)
                Done(fix, CheckStatus.Unverified, $"moved and stored, but the camera did not answer on {to}: {answered.Message}");
            else
                Done(fix, CheckStatus.Pass, $"moved and stored; the camera answers on {to}");
        }

        private static List<Cam> EnumCameras()
        {
            var list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            var cams = new List<Cam>();
            if (MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE, ref list) != MyCamera.MV_OK) return cams;

            for (int i = 0; i < list.nDeviceNum; i++)
            {
                var info = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(list.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                if (info.nTLayerType != MyCamera.MV_GIGE_DEVICE) continue;
                var gige = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(info.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                cams.Add(new Cam
                {
                    Info = info,
                    Mac = $"{info.nMacAddrHigh:X4}{info.nMacAddrLow:X8}",
                    Ip = FromUint(gige.nCurrentIp),
                    Via = FromUint(gige.nNetExport),
                    Model = gige.chModelName?.Trim(),
                    Serial = gige.chSerialNumber?.Trim(),
                });
            }
            return cams;
        }

        /// <summary>Writes the persistent IP (SFNC GevPersistent*) and switches the camera to it. Null = done, else why not.</summary>
        private static string Persist(string mac, string ip)
        {
            Cam cam = null;
            for (int i = 0; i < 10 && cam == null; i++)
            {
                cam = EnumCameras().FirstOrDefault(c => c.Mac == mac && c.Ip == ip);
                if (cam == null) Thread.Sleep(500);
            }
            if (cam == null) return "not seen at the new address";

            var dev = new MyCamera();
            if (dev.MV_CC_CreateDevice_NET(ref cam.Info) != MyCamera.MV_OK) return "create device";
            try
            {
                int rc = dev.MV_CC_OpenDevice_NET();
                if (rc != MyCamera.MV_OK) return $"open 0x{rc:X8}";
                try
                {
                    if ((rc = dev.MV_CC_SetIntValueEx_NET("GevPersistentIPAddress", ToUint(ip))) != MyCamera.MV_OK) return $"GevPersistentIPAddress 0x{rc:X8}";
                    if ((rc = dev.MV_CC_SetIntValueEx_NET("GevPersistentSubnetMask", ToUint("255.255.255.0"))) != MyCamera.MV_OK) return $"GevPersistentSubnetMask 0x{rc:X8}";
                    dev.MV_CC_SetIntValueEx_NET("GevPersistentDefaultGateway", 0);
                    if ((rc = dev.MV_GIGE_SetIpConfig_NET(MyCamera.MV_IP_CFG_STATIC)) != MyCamera.MV_OK) return $"static IP mode 0x{rc:X8}";
                    return null;
                }
                finally { dev.MV_CC_CloseDevice_NET(); }
            }
            finally { dev.MV_CC_DestroyDevice_NET(); }
        }

        // ================= ZMC =================

        /// <summary>Run after the ping sweep: a ZMC that opened at another address is moved to the profile's zmc.ip.</summary>
        public void FixZmc(RunReport report)
        {
            string to = _profile.ZmcIp;
            if (string.IsNullOrWhiteSpace(to) || report.DiscoveredIp == null) return;
            var zmcs = report.DiscoveredIp.Entries.Where(e => e.DeviceName == IpProbes.Zmc).ToList();
            if (zmcs.Count == 0 || zmcs.Any(e => e.Ip == to)) return;
            _log("\n--- ZMC address (profile) ---");

            if (zmcs.Count > 1)
            {
                Conflict(report, "ZMC", "ZMC", string.Join(", ", zmcs.Select(e => e.Ip)) + " -> " + to,
                    $"{zmcs.Count} controllers found ({string.Join(", ", zmcs.Select(e => e.Ip))}); cannot tell which one belongs at {to}");
                return;
            }
            string from = zmcs[0].Ip;
            if (zmcs[0].Status != ProbeStatus.Ok)
            {
                Conflict(report, "ZMC", "ZMC", $"{from} -> {to}", $"the controller at {from} did not open cleanly ({zmcs[0].Status}); close other programs connected to it");
                return;
            }
            if (!CanTake(report, "ZMC", "ZMC", from, to)) return;
            if (!LocalNics().Any(n => n.Any(a => Subnet(a) == Subnet(to))))
            {
                Conflict(report, "ZMC", "ZMC", $"{from} -> {to}", $"no NIC of this PC is on {Subnet(to)}.0/24, so the controller could not be reached there");
                return;
            }

            var fix = Start(report, "ZMC", "ZMC", from, to);
            if (!_confirm(L.T("confirm.deviceIp", L.T("dev.zmc"), "ZMC", from, to))) { Done(fix, CheckStatus.Fail, "not changed (declined)"); return; }

            using (var zmc = new ZmcConnection())
            {
                if (!zmc.Open(from)) { Done(fix, CheckStatus.Fail, $"could not open {from}"); return; }
                int rc = zmc.Execute($"IP_ADDRESS={to}", out string resp);
                _log($"    IP_ADDRESS={to} -> rc={rc} {resp}");
                if (rc != 0) { Done(fix, CheckStatus.Fail, $"IP_ADDRESS command failed (rc={rc}) {resp}".Trim()); return; }
            }

            // newer firmware switches at once, older keeps the old address until it is powered off and on
            using (var zmc = new ZmcConnection())
            {
                for (int i = 0; i < 10; i++)
                {
                    if (zmc.Open(to))
                    {
                        report.DiscoveredIp.ZmcIp = to;
                        Done(fix, CheckStatus.Pass, $"set; the controller answers on {to}");
                        return;
                    }
                    Thread.Sleep(1000);
                }
                if (zmc.Open(from))
                {
                    string now = zmc.Query("IP_ADDRESS");
                    if (now.Contains(to)) Done(fix, CheckStatus.Unverified, $"set to {to}; power the controller off and on to apply, then scan again");
                    else Done(fix, CheckStatus.Fail, $"the controller still reports IP_ADDRESS {now}");
                    return;
                }
            }
            Done(fix, CheckStatus.Unverified, $"set, but the controller answers on neither {from} nor {to}; power it off and on, then scan again");
        }

        // ================= helpers =================

        /// <summary>The target answers nothing, so moving a device there takes no one's address.</summary>
        private bool CanTake(RunReport report, string kind, string target, string from, string to)
        {
            bool taken = report.DiscoveredIp?.Entries.Any(e => e.Ip == to) == true;
            if (!taken)
            {
                try { using (var p = new Ping()) taken = p.Send(to, _cfg.PingTimeoutMs).Status == IPStatus.Success; }
                catch { }
            }
            if (taken) Conflict(report, kind, target, $"{from} -> {to}", $"{to} already answers; something else is on the profile address");
            return !taken;
        }

        private static List<List<string>> LocalNics() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Select(n => n.GetIPProperties().UnicastAddresses
                              .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                              .Select(a => a.Address.ToString()).ToList())
                .ToList();

        /// <summary>The NIC with local address <paramref name="via"/> also has an address in 192.168.x.0/24.</summary>
        private static bool Holds(string via, string subnet) =>
            LocalNics().Any(n => n.Contains(via) && n.Any(a => Subnet(a) == subnet));

        private static string Subnet(string ip)
        {
            if (!IPAddress.TryParse(ip ?? "", out var a) || a.AddressFamily != AddressFamily.InterNetwork) return null;
            var b = a.GetAddressBytes();
            return $"{b[0]}.{b[1]}.{b[2]}";
        }

        // GigE Vision addresses: first octet in the high byte
        private static string FromUint(uint ip) => $"{(ip >> 24) & 255}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}";

        private static uint ToUint(string ip)
        {
            var b = IPAddress.Parse(ip).GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static AddressFix Start(RunReport report, string kind, string target, string from, string to)
        {
            var fix = new AddressFix { OnDevice = true, Kind = kind, Target = target, From = from, To = to };
            report.AddressFixes.Add(fix);
            return fix;
        }

        private void Conflict(RunReport report, string kind, string target, string what, string detail)
        {
            var fix = Start(report, kind, target, "", what);
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
