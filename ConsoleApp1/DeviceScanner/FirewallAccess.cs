using System;
using System.Text;
using System.Windows.Forms;
using DeviceScanner.WindowsSpec;

namespace DeviceScanner
{
    /// <summary>
    /// Lets this exe accept network connections through Windows Firewall, so the "Allow access?" prompt never interrupts a scan
    /// (camera / scanner discovery listens for replies). Needs admin rights; without them nothing is changed.
    /// Adds one inbound allow rule for this exe (all network profiles: machine networks are often "Public"), and removes
    /// inbound BLOCK rules for this exe only, which Windows creates when someone pressed Cancel on that prompt earlier.
    /// </summary>
    internal static class FirewallAccess
    {
        public static void Allow(ScanConfig cfg, Action<string> log)
        {
            if (!Elevation.IsAdmin())
            {
                log("Firewall: no admin rights, rule not checked; Windows may ask to allow network access.");
                return;
            }

            string script = $@"
$exe  = '{Ps(Application.ExecutablePath)}'
$name = '{Ps(cfg.FirewallRuleName)}'
$mine = @(Get-NetFirewallApplicationFilter -Program $exe -ErrorAction SilentlyContinue |
          Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object Direction -eq 'Inbound')
foreach ($b in @($mine | Where-Object Action -eq 'Block')) {{
    Remove-NetFirewallRule -Name $b.Name
    ""removed block rule: $($b.DisplayName)""
}}
if (@($mine | Where-Object {{ $_.Action -eq 'Allow' -and $_.Enabled -eq 'True' -and ""$($_.Profile)"" -eq 'Any' }}).Count -eq 0) {{
    New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Program $exe -Profile Any | Out-Null
    ""added allow rule: $name""
}} else {{ ""allow rule present"" }}";

            // -EncodedCommand: no quoting problems with paths
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            int rc = Proc.Run("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded, 30000, out string output);
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                log("Firewall: " + line.Trim());
            if (rc != 0) log($"Firewall: rule update failed (rc={rc}); Windows may ask to allow network access.");
        }

        /// <summary>Inside a PowerShell single-quoted string only ' needs escaping.</summary>
        private static string Ps(string s) => s.Replace("'", "''");
    }
}
