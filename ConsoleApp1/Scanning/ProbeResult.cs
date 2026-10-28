namespace DeviceScanner.Scanning
{
    internal enum ProbeStatus { Ok, Busy, Fail }

    /// <summary>Outcome of trying to open one device on one address/port.</summary>
    internal readonly struct ProbeResult
    {
        public ProbeStatus Status { get; }
        public string Message { get; }
        /// <summary>The device type was recognised but rejected (e.g. camera on the wrong NIC). Stops "first hit wins".</summary>
        public bool Identified { get; }
        /// <summary>Extra info shown even on success (e.g. which NIC a camera is on). null = none.</summary>
        public string Note { get; }

        private ProbeResult(ProbeStatus status, string message, bool identified = false, string note = null)
        {
            Status = status; Message = message; Identified = identified; Note = note;
        }

        /// <summary>Same result with an informational note attached.</summary>
        public ProbeResult WithNote(string note) => new ProbeResult(Status, Message, Identified, note);

        public static ProbeResult Ok() => new ProbeResult(ProbeStatus.Ok, "OK");
        public static ProbeResult Busy() => new ProbeResult(ProbeStatus.Busy, "BUSY");
        public static ProbeResult Fail(string reason = null) =>
            new ProbeResult(ProbeStatus.Fail, string.IsNullOrEmpty(reason) ? "FAIL" : "FAIL: " + reason);

        /// <summary>It IS this device, but it fails a rule (e.g. wrong NIC). Counts as FAIL.</summary>
        public static ProbeResult Rejected(string reason) =>
            new ProbeResult(ProbeStatus.Fail, "FAIL: " + reason, identified: true);

        /// <summary>Device was located (open succeeded, or it is held by another process).</summary>
        public bool Found => Status != ProbeStatus.Fail;

        public override string ToString() => Message;
    }
}
