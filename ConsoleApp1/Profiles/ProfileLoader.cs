using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DeviceScanner.Profiles
{
    /// <summary>Reads a .profile text file ("key = value" lines, '#' comments) into a MachineProfile.</summary>
    internal static class ProfileLoader
    {
        public static MachineProfile Load(string path)
        {
            string file = Path.GetFileName(path);
            var p = new MachineProfile { SourcePath = path, Name = Path.GetFileNameWithoutExtension(path) };

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) throw new FormatException($"{file} line {i + 1}: expected \"key = value\"");

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                try { Apply(p, key, val); }
                catch (FormatException ex) { throw new FormatException($"{file} line {i + 1}: {ex.Message}"); }
            }

            if (string.IsNullOrWhiteSpace(p.ZmcIp)) throw new FormatException($"{file}: zmc.ip is required");
            return p;
        }

        private static void Apply(MachineProfile p, string key, string val)
        {
            switch (key)
            {
                case "name": p.Name = val; break;
                case "zmc.ip": p.ZmcIp = val; break;
                case "zmc.init": p.InitProgram = val; break;
                case "home.programs": p.HomePrograms.AddRange(SplitList(val)); break;
                case "home.axes": p.HomeAxes = SplitList(val).Select(s => ParseInt(s, "axis")).ToArray(); break;
                case "com.slaves": p.TempSlaves = SplitList(val).Select(s => ParseInt(s, "slave address")).ToArray(); break;
                case "ip.expect": p.ExpectedIp.Add(ParseDevice(val)); break;
                case "com.expect": p.ExpectedCom.Add(ParseDevice(val)); break;
                case "io": p.IoTests.Add(ParseIo(val)); break;
                default: throw new FormatException($"unknown key \"{key}\"");
            }
        }

        private static ExpectedDevice ParseDevice(string val)
        {
            string[] f = SplitFields(val);
            if (f.Length != 2 || f[0].Length == 0 || f[1].Length == 0)
                throw new FormatException("expected \"address | device name\"");
            return new ExpectedDevice(f[0], f[1]);
        }

        private static IoTestCase ParseIo(string val)
        {
            string[] f = SplitFields(val);
            if (f.Length < 2 || f.Length > 5 || f[0].Length == 0)
                throw new FormatException("expected \"name | output | input | settle ms | optional\"");

            int outNo = ParseInt(f[1], "output");
            int? inNo = f.Length > 2 && f[2].Length > 0 && f[2] != "-" ? ParseInt(f[2], "input") : (int?)null;
            int settle = f.Length > 3 && f[3].Length > 0 ? ParseInt(f[3], "settle ms") : 0;

            bool optional = false;
            if (f.Length > 4 && f[4].Length > 0)
            {
                if (!f[4].Equals("optional", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"last field must be \"optional\" or empty, got \"{f[4]}\"");
                optional = true;
            }

            return new IoTestCase(f[0], outNo, inNo, settle, optional);
        }

        // ---------- small helpers ----------

        private static string StripComment(string s)
        {
            int hash = s.IndexOf('#');
            return hash < 0 ? s : s.Substring(0, hash);
        }

        private static string[] SplitList(string s) =>
            s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        private static string[] SplitFields(string s) =>
            s.Split('|').Select(x => x.Trim()).ToArray();

        private static int ParseInt(string s, string what) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : throw new FormatException($"{what} must be a whole number, got \"{s}\"");
    }
}
