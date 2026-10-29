using System;
using System.Collections.Generic;

namespace DeviceScanner.WindowsSpec
{
    /// <summary>A set of driver INFs and the profile modules that need them.</summary>
    internal sealed class DriverModule
    {
        public string Name { get; }
        public string[] UsedBy { get; }
        public string[] Drivers { get; }

        public DriverModule(string name, string[] usedBy, params string[] drivers)
        {
            Name = name; UsedBy = usedBy; Drivers = drivers;
        }
    }

    /// <summary>
    /// The golden-image Windows spec every machine must match. Same on every machine, so it lives here
    /// and not in a .profile file. Check only: nothing in this module changes the PC.
    /// </summary>
    internal sealed class WindowsSpecConfig
    {
        // ----- OS (winver) -----
        public string EditionId { get; } = "IoTEnterpriseS";                         // registry EditionID of Win10 IoT Enterprise LTSC
        public string EditionText { get; } = "Windows 10 IoT Enterprise LTSC 2021";
        public string DisplayVersion { get; } = "21H2";
        public int Build { get; } = 19044;
        public int Ubr { get; } = 1288;                                               // exact = PASS, newer = WARN, older/other build = FAIL

        // ----- locale (dism /online /get-intl) -----
        public string SystemUiLanguage { get; } = "en-US";
        public string SystemLocale { get; } = "en-US";

        // ----- files with Chinese characters in the name (report only) -----
        /// <summary>The only fixed drives scanned, by letter.</summary>
        public HashSet<string> ScanDrives { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C" };
        /// <summary>Top-level folders skipped on every scanned drive (Windows' own files may legitimately use CJK names).</summary>
        public HashSet<string> SkipTopLevelDirs { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData",
            "$Recycle.Bin", "System Volume Information", "Recovery", "$WinREAgent",
            "$Windows.~BT", "$Windows.~WS", "$SysReset", "Config.Msi", "Boot", "PerfLogs",
        };
        public int ChineseNamesShownInSummary { get; } = 30;                          // the full list goes to a .txt in ReportDir

        // ----- apps that must not be installed -----
        /// <summary>Matched (case-insensitive) against DisplayName and Publisher of every uninstall entry.</summary>
        public string[] ForbiddenAppPatterns { get; } = { @"\bWPS\b", "Kingsoft", "WinRAR", "RARLAB", "\u91D1\u5C71" /* Kingsoft in Chinese */ };
        /// <summary>false = any match is FAIL. true = only a detected Chinese build is FAIL, other builds are WARN.</summary>
        public bool ForbiddenAppsChineseOnly { get; } = false;

        // ----- Defender -----
        public string DefenderService { get; } = "WinDefend";
        public string DefenderFileExclusion { get; } = @"*\OneDispenser.exe";

        // ----- Ethernet adapters (every physical wired adapter) -----
        public string ReceiveBuffers { get; } = "2048";
        public string TransmitBuffers { get; } = "2048";

        // ----- drivers that must be in the driver store, paired with the module that needs them -----
        // Uses: "*" = every machine | "com" = profile has any com.expect device | "gantry" = profile has home.programs
        //       | otherwise a device name from the profile (ip.expect / com.expect, e.g. "Camera GigE", "CL3 (Keyence)").
        // A module whose uses are not in the selected profile is reported as N/A, not FAIL.
        public DriverModule[] DriverModules { get; } =
        {
            new DriverModule("Intel Ethernet (I210/I350/X5xx) + ANS", new[] { "*" },
                "ANSMW60E.INF", "ANSPW60E.INF",
                "e1c65x64.inf", "e1d68x64.inf", "e1r65x64.inf", "e1r68x64.inf",
                "e2f.inf", "e2f68.inf", "iqvsw64e.inf", "iqvw64e.inf",
                "ixn65x64.inf", "ixn68x64.inf", "ixs65x64.inf", "ixs68x64.inf", "ixt65x64.inf", "ixt68x64.inf"),

            new DriverModule("Serial ports (PCI-1612 / WCH / FTDI)", new[] { "com" },
                "advExarPort_win10.inf", "seradvbus_win10.inf",
                "CH343SER.INF", "ch38mdrv.inf", "emports.inf",
                "ftdibus.inf", "ftdiport.inf"),

            new DriverModule("Gantry servo drives (Servotronix / HD)", new[] { "gantry" },
                "servotronix-usbcom.inf", "sd2-ftdibus.inf", "sd2-ftdiport.inf", "HD-ServoDrive.inf"),

            new DriverModule("Camera (Hikrobot MVS / GigE / U3V / frame grabbers)", new[] { "Camera GigE" },
                "hikgevsf.inf", "mvGenICamForEthernet.inf", "MvGigEFrameGrabber.inf", "mvu3v.inf",
                "mvUniversalProForU3V.inf", "mvFrameGrabber.inf", "mvPCIex86.inf", "MvVirtualSerial.inf",
                "MvCxpFrameGrabber.inf", "MvCmlFrameGrabber.inf", "flex_io_cxp.inf"),

            new DriverModule("Keyence CL-3000 confocal", new[] { "CL3 (Keyence)" },
                "keyence_cl3000.inf"),

            // not yet tied to a module: move each into the right group above once confirmed
            new DriverModule("Other (module not identified yet)", new[] { "*" },
                "driver.inf", "PTUsbDrvA5.Inf", "usb2can.inf"),
        };
    }
}
