using System;

namespace DeviceScanner.Scanning
{
    /// <summary>A named check that tries to open one kind of device on a given IP or COM port.</summary>
    internal sealed class DeviceProbe
    {
        public string Name { get; }
        public Func<string, ProbeResult> Run { get; }

        public DeviceProbe(string name, Func<string, ProbeResult> run) { Name = name; Run = run; }
    }
}
