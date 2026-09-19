using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Safety;

public sealed record WriteGateState(
    bool VinMatches,
    bool BackupCreated,
    bool VoltageKnown,
    double? Voltage,
    bool UserArmed,
    bool CodingEngineVerified)
{
    public bool Allowed =>
        VinMatches &&
        BackupCreated &&
        VoltageKnown &&
        Voltage >= 12.0 &&
        UserArmed &&
        CodingEngineVerified;

    public IReadOnlyList<string> Blockers()
    {
        var b = new List<string>();
        if (!VinMatches) b.Add("VIN mismatch");
        if (!BackupCreated) b.Add("backup not created");
        if (!VoltageKnown) b.Add("battery voltage unknown");
        else if (Voltage < 12.0) b.Add($"battery voltage too low ({Voltage:0.0} V)");
        if (!UserArmed) b.Add("write mode not armed");
        if (!CodingEngineVerified) b.Add("coding engine not hardware-verified");
        return b;
    }
}
