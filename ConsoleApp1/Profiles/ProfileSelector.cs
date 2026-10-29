using System;
using System.IO;
using System.Linq;
using DeviceScanner.Utils;

namespace DeviceScanner.Profiles
{
    /// <summary>
    /// Chooses which profile to run: a path/name on the command line, otherwise the files in
    /// the Profiles folder next to the exe (a dialog asks if there is more than one).
    /// Problems are shown in a dialog too, since the run stops there.
    /// </summary>
    internal static class ProfileSelector
    {
        private const string Folder = "Profiles";
        private const string Pattern = "*.profile";

        public static MachineProfile Select(string[] args, Action<string> log)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Folder);
            try
            {
                string path = args.Length > 0 ? Resolve(args[0], dir) : Pick(dir, log);
                if (path == null) return null;

                var profile = ProfileLoader.Load(path);
                log(L.T("profile.loaded", profile.Name, path));
                return profile;
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException)
            {
                log(L.T("profile.error", ex.Message));
                Dialogs.Error(L.T("profile.error", ex.Message));
                return null;
            }
        }

        private static string Resolve(string arg, string dir)
        {
            if (File.Exists(arg)) return Path.GetFullPath(arg);

            string name = Path.HasExtension(arg) ? arg : arg + ".profile";
            string inFolder = Path.Combine(dir, name);
            if (File.Exists(inFolder)) return inFolder;

            throw new FileNotFoundException($"profile not found: {arg} (also looked in {dir})");
        }

        private static string Pick(string dir, Action<string> log)
        {
            Directory.CreateDirectory(dir);
            string[] files = Directory.GetFiles(dir, Pattern).OrderBy(f => f).ToArray();

            if (files.Length == 0)
            {
                string sample = Path.Combine(dir, "sd1x.profile");
                File.WriteAllText(sample, SampleProfile.Text);
                log(L.T("profile.none", sample));
                log(L.T("profile.editHint"));
                Dialogs.Inform(L.T("profile.none", sample) + "\n\n" + L.T("profile.editHint"));
                return null;
            }

            if (files.Length == 1) return files[0];

            int n = Dialogs.Choose(L.T("profile.choose"), files.Select(Path.GetFileNameWithoutExtension).ToList());
            return n < 0 ? null : files[n];
        }
    }
}
