using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeviceScanner.Scanning
{
    /// <summary>Which local network adapter Windows uses to reach a given IP (routing table lookup).</summary>
    internal sealed class LocalNic
    {
        public string Description { get; set; }   // "Intel(R) I350 Gigabit Network Connection #2"
        public string Connection { get; set; }    // "Ethernet 3"
        public string PnpId { get; set; }         // PCI\VEN_8086&DEV_1521&SUBSYS...\...

        /// <summary>PCI\VEN_8086&DEV_1521 part only.</summary>
        public string ShortPnpId
        {
            get
            {
                if (string.IsNullOrEmpty(PnpId)) return "?";
                int i = PnpId.IndexOf("&SUBSYS", StringComparison.OrdinalIgnoreCase);
                return i > 0 ? PnpId.Substring(0, i) : PnpId;
            }
        }
    }

    internal static class NicRoute
    {
        private const string NetworkClass = @"SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}";

        /// <summary>Adapter used to reach <paramref name="ip"/>, or null if there is no route / it cannot be identified.</summary>
        public static LocalNic ForIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork) return null;

            uint dest = BitConverter.ToUInt32(addr.GetAddressBytes(), 0);   // IPAddr = network-order bytes in memory
            if (GetBestInterface(dest, out uint index) != 0) return null;

            var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            {
                try
                {
                    var p = n.GetIPProperties().GetIPv4Properties();
                    return p != null && p.Index == index;
                }
                catch { return false; }
            });
            if (nic == null) return null;

            string pnp = null;
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                          .OpenSubKey($@"{NetworkClass}\{nic.Id}\Connection", false))
                    pnp = k?.GetValue("PnPInstanceId") as string;
            }
            catch { }

            return new LocalNic { Description = nic.Description, Connection = nic.Name, PnpId = pnp ?? "" };
        }

        /// <summary>
        /// "via Ethernet 3 / Intel(R) I350 ... [PCI\VEN_8086&amp;DEV_1521]" from the routing table (device need not answer).
        /// If <paramref name="requiredPnp"/> is given and the NIC does not match, appends " - not &lt;requiredPnp&gt;".
        /// </summary>
        public static string Note(string ip, string requiredPnp = null)
        {
            var nic = ForIp(ip);
            if (nic == null) return "via ? (no local NIC routes to this IP)";

            string note = $"via {nic.Connection} / {nic.Description} [{nic.ShortPnpId}]";
            if (!string.IsNullOrEmpty(requiredPnp) && nic.PnpId.IndexOf(requiredPnp, StringComparison.OrdinalIgnoreCase) < 0)
                note += $" - not {requiredPnp}";
            return note;
        }

        [DllImport("iphlpapi.dll")]
        private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);
    }
}
