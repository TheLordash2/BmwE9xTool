namespace BmwE9xTool.Safety;

public sealed record WriteGateState(
    bool VinMatches,
    bool BackupCreated,
    bool VoltageKnown,
    double? Voltage,
    bool UserArmed,
    bool ExperimentalWriteAccepted)
{
    public bool Allowed =>
        VinMatches &&
        BackupCreated &&
        VoltageKnown &&
        Voltage >= 12.0 &&
        UserArmed &&
        ExperimentalWriteAccepted;

    public IReadOnlyList<string> Blockers()
    {
        var b = new List<string>();
        if (!VinMatches) b.Add("VIN mismatch or expected VIN not configured locally");
        if (!BackupCreated) b.Add("backup not created");
        if (!VoltageKnown) b.Add("battery voltage unknown");
        else if (Voltage < 12.0) b.Add($"battery voltage too low ({Voltage:0.0} V)");
        if (!UserArmed) b.Add("write mode not armed");
        if (!ExperimentalWriteAccepted) b.Add("experimental FA-write acknowledgement not accepted");
        return b;
    }
}
