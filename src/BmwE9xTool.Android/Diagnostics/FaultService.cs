using BmwE9xTool.Core;
using BmwE9xTool.Data;
using BmwE9xTool.Ediabas;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Diagnostics;

public sealed class FaultService
{
    private readonly EdiabasSession _session;
    private readonly AppLog _log;
    private readonly NcsSgfamParser _sgfam;

    public FaultService(EdiabasSession session, AppLog log, NcsSgfamParser sgfam)
    {
        _session = session;
        _log = log;
        _sgfam = sgfam;
    }

    public Task<IReadOnlyList<FaultRecord>> ScanCommonAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        ScanTargetsAsync(E90Catalog.Common, progress, ct);

    public async Task<IReadOnlyList<FaultRecord>> ScanDeepAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var targets = new List<EcuTarget>(E90Catalog.Common);
        foreach (var entry in _sgfam.Read())
            targets.Add(new EcuTarget(entry.LogicalName, entry.Sgbd, "NCS SGFAM"));

        return await ScanTargetsAsync(
            targets
                .GroupBy(x => x.Sgbd, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList(),
            progress,
            ct);
    }

    private async Task<IReadOnlyList<FaultRecord>> ScanTargetsAsync(
        IReadOnlyList<EcuTarget> targets,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var faults = new List<FaultRecord>();

        foreach (var ecu in targets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(ecu.Name);

            try
            {
                if (!await _session.JobExistsAsync(ecu.Sgbd, "FS_LESEN", ct)) continue;

                var result = await _session.RunJobAsync(ecu.Sgbd, "FS_LESEN", ct: ct);
                foreach (var set in result.Sets.Skip(1))
                {
                    var raw = new Dictionary<string, object?>(set.Values, StringComparer.OrdinalIgnoreCase);
                    var code = First(set, "F_ORT_NR", "F_CODE", "F_PCODE_STRING", "F_SAE_CODE_STRING");
                    var text = First(set, "F_ORT_TEXT", "F_TEXT", "F_FEHLERTEXT");
                    var present = ParsePresent(set);

                    if (code == null && text == null &&
                        !raw.Keys.Any(k => k.StartsWith("F_", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    faults.Add(new FaultRecord(ecu.Name, ecu.Sgbd, code, text, present, raw));
                }
            }
            catch (Exception ex)
            {
                _log.Add($"Fault scan {ecu.Name}/{ecu.Sgbd} skipped: {ex.Message}");
            }
        }

        _log.Add($"Fault scan complete: {faults.Count} record(s)");
        return faults;
    }

    public async Task ClearAsync(EcuTarget ecu, CancellationToken ct = default)
    {
        if (!await _session.JobExistsAsync(ecu.Sgbd, "FS_LOESCHEN", ct))
            throw new InvalidOperationException($"{ecu.Name} does not expose FS_LOESCHEN.");

        await _session.RunJobAsync(ecu.Sgbd, "FS_LOESCHEN", ct: ct);
    }

    private static string? First(JobSet set, params string[] names)
    {
        foreach (var name in names)
        {
            var s = set.String(name);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static bool? ParsePresent(JobSet set)
    {
        var value = First(set, "F_VORHANDEN_NR", "F_VORHANDEN");
        if (value == null) return null;
        if (long.TryParse(value, out var n)) return n != 0;

        if (value.Contains("JA", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("PRESENT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Contains("NEIN", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("NOT", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }
}
