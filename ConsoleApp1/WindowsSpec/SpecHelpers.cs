using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>Chinese (Han) character detection for file names, app names, adapter names.</summary>
    internal static class Cjk
    {
        public static bool HasChinese(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 0x3400 && c <= 0x4DBF) ||      // CJK extension A
                    (c >= 0x4E00 && c <= 0x9FFF) ||      // CJK unified ideographs
                    (c >= 0xF900 && c <= 0xFAFF) ||      // CJK compatibility ideographs
                    (c >= 0x3000 && c <= 0x303F))        // CJK punctuation (ideographic comma, full stop, brackets)
                    return true;

                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    int cp = char.ConvertToUtf32(c, s[i + 1]);
                    if (cp >= 0x20000 && cp <= 0x3134F) return true;   // CJK extensions B-G
                    i++;
                }
            }
            return false;
        }
    }

    internal static class Reg
    {
        /// <summary>HKLM in the 64-bit view, regardless of how the process was built.</summary>
        public static RegistryKey Hklm(string path, RegistryView view = RegistryView.Registry64) =>
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(path, false);

        public static string Str(RegistryKey k, string name)
        {
            object v = k?.GetValue(name);
            if (v == null) return null;
            if (v is string[] arr) return string.Join(",", arr);
            return Convert.ToString(v);
        }

        public static int? Int(RegistryKey k, string name)
        {
            object v = k?.GetValue(name);
            if (v == null) return null;
            try { return Convert.ToInt32(v); } catch { return null; }
        }

        /// <summary>SIDs of real user hives currently loaded under HKEY_USERS (logged-on / service users).</summary>
        public static IEnumerable<string> LoadedUserSids()
        {
            using (var hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
            {
                foreach (string sid in hku.GetSubKeyNames())
                    if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                        yield return sid;
            }
        }

        public static RegistryKey User(string sid, string path) =>
            RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64).OpenSubKey(sid + "\\" + path, false);

        public static string SidToName(string sid)
        {
            try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
            catch { return sid; }
        }
    }

    internal static class Proc
    {
        /// <summary>Runs a console tool, returns exit code and stdout+stderr. -1 if it could not start or timed out.</summary>
        public static int Run(string file, string args, int timeoutMs, out string output)
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        output = sb.ToString();
                        return -1;
                    }
                    p.WaitForExit();     // flush async readers
                    output = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }

    internal static class Wmi
    {
        /// <summary>Runs a WQL query; returns an empty list (and sets error) instead of throwing.</summary>
        public static List<ManagementBaseObject> Query(string scope, string wql, out string error)
        {
            var list = new List<ManagementBaseObject>();
            error = null;
            try
            {
                using (var s = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql)))
                    foreach (ManagementBaseObject o in s.Get()) list.Add(o);
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
            {
                error = "access denied (run as administrator)";
            }
            catch (UnauthorizedAccessException)
            {
                error = "access denied (run as administrator)";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return list;
        }
    }

    internal static class Elevation
    {
        public static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
