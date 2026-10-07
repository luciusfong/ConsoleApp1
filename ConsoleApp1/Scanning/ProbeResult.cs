namespace DeviceScanner.Scanning
{
    internal enum ProbeStatus { Ok, Busy, Fail }

    /// <summary>Outcome of trying to open one device on one address/port.</summary>
    internal readonly struct ProbeResult
    {
        public ProbeStatus Status { get; }
        public string Message { get; }

        private ProbeResult(ProbeStatus status, string message) { Status = status; Message = message; }

        public static ProbeResult Ok() => new ProbeResult(ProbeStatus.Ok, "OK");
        public static ProbeResult Busy() => new ProbeResult(ProbeStatus.Busy, "BUSY");
        public static ProbeResult Fail(string reason = null) =>
            new ProbeResult(ProbeStatus.Fail, string.IsNullOrEmpty(reason) ? "FAIL" : "FAIL: " + reason);

        /// <summary>Device was located (open succeeded, or it is held by another process).</summary>
        public bool Found => Status != ProbeStatus.Fail;

        public override string ToString() => Message;
    }
}
