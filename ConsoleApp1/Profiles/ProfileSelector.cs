using System;
using System.IO;
using System.Linq;

namespace DeviceScanner.Profiles
{
    /// <summary>
    /// Chooses which profile to run: a path/name on the command line, otherwise the files in
    /// the Profiles folder next to the exe (asks if there is more than one).
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
                log($"Profile: {profile.Name}  ({path})");
                return profile;
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException)
            {
                log("Profile error: " + ex.Message);
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
                log($"No profiles found. Wrote an example to {sample}");
                log("Edit it for this machine (or copy it for another one), then run again.");
                return null;
            }

            if (files.Length == 1) return files[0];

            log("Profiles:");
            for (int i = 0; i < files.Length; i++) log($"  {i + 1}. {Path.GetFileNameWithoutExtension(files[i])}");
            Console.Write("Choose a profile number: ");

            if (int.TryParse(Console.ReadLine(), out int n) && n >= 1 && n <= files.Length) return files[n - 1];

            log("Invalid choice.");
            return null;
        }
    }
}
