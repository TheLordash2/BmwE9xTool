using BmwE9xTool.Core;
using BmwE9xTool.Ediabas;

namespace BmwE9xTool.Vehicle;

public sealed class VehicleIdentityService
{
    private readonly EdiabasSession _session;
    private readonly AppLog _log;

    private static readonly (string Sgbd, string Job, string Result)[] VinJobs =
    [
        ("D_CAS", "STATUS_FAHRGESTELLNUMMER", "FGNUMMER"),
        ("D_LM", "READ_FVIN", "FVIN"),
        ("FRM_87", "READ_FVIN", "FVIN"),
        ("D_ZGM", "C_FG_LESEN", "FG_NR")
    ];

    public VehicleIdentityService(EdiabasSession session, AppLog log)
    {
        _session = session;
        _log = log;
    }

    public async Task<string> ReadVinAsync(CancellationToken ct = default)
    {
        foreach (var candidate in VinJobs)
        {
            try
            {
                if (!await _session.JobExistsAsync(candidate.Sgbd, candidate.Job, ct)) continue;
                var result = await _session.RunJobAsync(candidate.Sgbd, candidate.Job, ct: ct);
                foreach (var set in result.Sets)
                {
                    var vin = set.String(candidate.Result)?.Trim();
                    if (!string.IsNullOrEmpty(vin) && vin.Length >= 7)
                    {
                        _log.Add($"VIN read successfully through {candidate.Sgbd} (value intentionally not logged).");
                        return vin;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Add($"VIN candidate {candidate.Sgbd} failed: {ex.Message}");
            }
        }
        throw new InvalidOperationException("Could not read VIN from the configured E9x identity modules.");
    }
}
