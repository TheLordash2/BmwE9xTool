namespace BmwE9xTool.Vehicle;

public sealed record JobValue(string Name, object? Value);

public sealed record JobSet(IReadOnlyDictionary<string, object?> Values)
{
    public string? String(string key) =>
        Values.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public sealed record JobResult(
    string Sgbd,
    string Job,
    IReadOnlyList<JobSet> Sets,
    string? JobStatus);

public sealed record VehicleOrder(
    string Source,
    string RawFa,
    string? StandardFa,
    string? Version,
    string? Chassis,
    string? ProductionDate,
    string? TypeCode,
    string? Paint,
    string? Upholstery,
    IReadOnlyList<string> Sa,
    IReadOnlyList<string> HoWords,
    IReadOnlyList<string> EWords,
    IReadOnlyList<string> ZbWords)
{
    public string Display => string.Join(" ", Sa.Select(x => "$" + x));
}

public sealed record EcuTarget(string Name, string Sgbd, string Description);

public sealed record FaultRecord(
    string Ecu,
    string Sgbd,
    string? Code,
    string? Text,
    bool? Present,
    IReadOnlyDictionary<string, object?> Raw);
