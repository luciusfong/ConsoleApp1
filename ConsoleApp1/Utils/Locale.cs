using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeviceScanner.Utils
{
    internal enum Lang { En, Zh }

    /// <summary>
    /// UI language for prompts and the scan report. Texts live in Strings.cs.
    /// Usage: L.T("sum.title", profileName)  -> "===== SUMMARY: SD1X =====" or "===== 检测报告：SD1X ====="
    /// Device names, IPs, file paths and technical messages from the checks are not translated.
    /// </summary>
    internal static class L
    {
        public static Lang Current { get; private set; } = Lang.En;

        public static void Set(Lang lang) => Current = lang;

        /// <summary>Translated text for <paramref name="key"/>, formatted with <paramref name="args"/> ({0}, {1}, ...).</summary>
        public static string T(string key, params object[] args)
        {
            string text;
            if (!Strings.Table.TryGetValue(key, out var pair)) text = key;                 // unknown key: show the key itself
            else text = Current == Lang.Zh && !string.IsNullOrEmpty(pair[1]) ? pair[1] : pair[0];
            return args == null || args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
        }

        /// <summary>
        /// Picks the language: "--lang zh" / "--lang=en" on the command line, otherwise asks in a dialog.
        /// Returns the remaining arguments (so args[0] is still the profile), or null if the dialog was cancelled.
        /// </summary>
        public static string[] Choose(string[] args)
        {
            var rest = new List<string>();
            string chosen = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) chosen = a.Substring(7);
                else if (a.Equals("--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) chosen = args[++i];
                else rest.Add(a);
            }

            if (chosen == null)
            {
                // asked before any language is set, so this one dialog is bilingual
                int pick = Dialogs.Choose("Choose the language for this scan and its report.\n请选择本次检测及报告的语言。",
                                          new[] { "English", "中文" }, 0, "OK / 确定", "Cancel / 取消", "Language / 语言");
                if (pick < 0) return null;
                chosen = pick == 1 ? "zh" : "en";
            }

            string c = chosen.ToLowerInvariant();
            Set(c == "2" || c.StartsWith("zh") || c == "cn" || c == "中文" ? Lang.Zh : Lang.En);
            return rest.ToArray();
        }
    }
}
