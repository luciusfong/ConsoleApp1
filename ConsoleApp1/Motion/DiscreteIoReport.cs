using DeviceScanner.Profiles;
using System.Collections.Generic;
using System.Linq;
internal sealed class IoTestResult
{
    public IoTestCase Case { get; }
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
    public List<int> ChangedInputs { get; } = new List<int>();

    public IoTestResult(IoTestCase testCase) { Case = testCase; }
}

internal sealed class IoScanReport
{
    public List<IoTestResult> Results { get; } = new List<IoTestResult>();

    public int PassCount => Results.Count(r => r.Passed);
    public int FailCount => Results.Count(r => !r.Passed);
}