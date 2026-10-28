using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// dism /online /get-intl: system UI language = en-US, system locale = en-US.
    /// Also warns if a Chinese language pack is still installed (that is why some dialogs stay Chinese).
    /// dism needs admin; without it the same values are read through the Win32 API.
    /// </summary>
    internal sealed class LocaleCheck : SpecCheck
    {
        public LocaleCheck(WindowsSpecConfig spec, Action<string> log) : base(spec, log) { }

        public override string Title => "Locale (dism /get-intl)";

        protected override void Execute()
        {
            const string group = "Locale";

            string ui = null, locale = null, installed = null, source;
            string dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
            int rc = Proc.Run(dism, "/online /get-intl /English", 60000, out string text);

            if (rc == 0)
            {
                source = "dism";
                ui = Field(text, @"Default system UI language");
                locale = Field(text, @"System locale");
                installed = InstalledLanguages(text);
            }
            else
            {
                // 740 = elevation required. Fall back to the API (same values dism reports).
                source = rc == 740 ? "Win32 API (dism needs admin)" : $"Win32 API (dism rc={rc})";
                ui = LangIdToName(GetSystemDefaultUILanguage());
                var sb = new StringBuilder(85);
                if (GetSystemDefaultLocaleName(sb, sb.Capacity) > 0) locale = sb.ToString();
            }

            Add(group, "System UI language", Spec.SystemUiLanguage, ui,
                PassIf(Same(ui, Spec.SystemUiLanguage)), "source: " + source);
            Add(group, "System locale", Spec.SystemLocale, locale,
                PassIf(Same(locale, Spec.SystemLocale)), "source: " + source);

            if (installed != null)
            {
                bool chinese = installed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Any(l => l.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
                Add(group, "Installed languages", "no zh-*", installed,
                    chinese ? CheckStatus.Unverified : CheckStatus.Pass,
                    chinese ? "Chinese language pack still installed" : "");
            }
        }

        /// <summary>dism prints "Name : value"; the language list may continue on the next lines.</summary>
        private static string Field(string text, string label)
        {
            var m = Regex.Match(text, @"^\s*" + label + @"\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>Every language tag listed from "Installed language(s):" to the end of the output (one block per language).</summary>
        private static string InstalledLanguages(string text)
        {
            int i = text.IndexOf("Installed language", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var tags = Regex.Matches(text.Substring(i), @"\b[a-z]{2,3}-[A-Za-z]{2,4}\b")
                            .Cast<Match>().Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", tags);
        }

        private static bool Same(string a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);

        private static string LangIdToName(ushort langId)
        {
            try { return CultureInfo.GetCultureInfo(langId).Name; }
            catch { return "0x" + langId.ToString("X4"); }
        }

        [DllImport("kernel32.dll")]
        private static extern ushort GetSystemDefaultUILanguage();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetSystemDefaultLocaleName(StringBuilder lpLocaleName, int cchLocaleName);
    }
}
