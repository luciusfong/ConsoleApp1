using System;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>
    /// Visual C++ 2015-2022 redistributable (x64) is installed, at least WindowsSpecConfig.VcRuntimeMin.
    /// Read from HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64 (Installed = 1, Major/Minor/Bld),
    /// which every 2015-2022 x64 redistributable writes.
    /// </summary>
    internal sealed class VcRuntimeCheck : SpecCheck
    {
        public const string Group = "VC++ runtime";
        public const string Name = "Visual C++ 2015-2022 x64";
        public const string NotInstalled = "not installed";

        public VcRuntimeCheck(WindowsSpecConfig spec, Action<string> log) : base(spec, log) { }

        public override string Title => "Visual C++ runtime";

        protected override void Execute()
        {
            Version installed = Installed();
            string expected = Spec.VcRuntimeMin + " or newer";
            if (installed == null)
                Add(Group, Name, expected, NotInstalled, CheckStatus.Fail, @"HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            else
                Add(Group, Name, expected, installed.ToString(), PassIf(installed >= Spec.VcRuntimeMin));
        }

        /// <summary>Installed x64 runtime version, or null if there is none.</summary>
        public static Version Installed()
        {
            using (var k = Reg.Hklm(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
            {
                if (Reg.Int(k, "Installed") != 1) return null;
                return new Version(Reg.Int(k, "Major") ?? 0, Reg.Int(k, "Minor") ?? 0, Reg.Int(k, "Bld") ?? 0);
            }
        }

        /// <summary>The check failed (missing or too old), so the fix button should install it.</summary>
        public static bool Missing(SpecReport spec) =>
            spec != null && spec.Items.Exists(i => i.Group == Group && i.Status == CheckStatus.Fail);
    }
}
