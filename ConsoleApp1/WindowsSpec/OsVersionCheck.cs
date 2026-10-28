using System;
using DeviceScanner.Reporting;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>winver: Windows 10 IoT Enterprise LTSC 2021, 21H2, OS build 19044.1288.</summary>
    internal sealed class OsVersionCheck : SpecCheck
    {
        public OsVersionCheck(WindowsSpecConfig spec, Action<string> log) : base(spec, log) { }

        public override string Title => "OS version (winver)";

        protected override void Execute()
        {
            const string group = "OS";
            using (var k = Reg.Hklm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (k == null) { Add(group, "version", "", "", CheckStatus.Fail, "CurrentVersion key not readable"); return; }

                string product = Reg.Str(k, "ProductName");
                string edition = Reg.Str(k, "EditionID");
                string display = Reg.Str(k, "DisplayVersion") ?? Reg.Str(k, "ReleaseId");
                int.TryParse(Reg.Str(k, "CurrentBuildNumber") ?? Reg.Str(k, "CurrentBuild"), out int build);
                int ubr = Reg.Int(k, "UBR") ?? -1;

                // Edition: EditionID is language independent; ProductName is shown for the reader.
                Add(group, "Edition", $"{Spec.EditionText} [{Spec.EditionId}]", $"{product} [{edition}]",
                    PassIf(string.Equals(edition, Spec.EditionId, StringComparison.OrdinalIgnoreCase)));

                Add(group, "Version", Spec.DisplayVersion, display,
                    PassIf(string.Equals(display, Spec.DisplayVersion, StringComparison.OrdinalIgnoreCase)));

                // Build: exact = PASS, same build with newer patch level (Windows Update) = WARN, anything else = FAIL.
                string expected = $"{Spec.Build}.{Spec.Ubr}";
                string actual = $"{build}.{ubr}";
                CheckStatus st;
                string detail = "";
                if (build == Spec.Build && ubr == Spec.Ubr) st = CheckStatus.Pass;
                else if (build == Spec.Build && ubr > Spec.Ubr) { st = CheckStatus.Unverified; detail = "newer patch level than the golden image (Windows Update applied)"; }
                else st = CheckStatus.Fail;
                Add(group, "OS build", expected, actual, st, detail);
            }
        }
    }
}
