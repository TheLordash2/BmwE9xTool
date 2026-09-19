using BmwE9xTool.Core;
using BmwE9xTool.Data;
using BmwE9xTool.Ediabas;

namespace BmwE9xTool.Vehicle;

public sealed class FaService
{
    private readonly EdiabasSession _session;
    private readonly AppLog _log;
    private readonly NcsSgfamParser _sgfam;

    private static readonly (string Name, string Sgbd)[] FallbackCandidates =
    [
        ("CAS", "D_CAS"),
        ("FRM87", "FRM_87"),
        ("LM", "D_LM"),
        ("KBM", "D_KBM")
    ];

    public FaService(EdiabasSession session, AppLog log, NcsSgfamParser sgfam)
    {
        _session = session;
        _log = log;
        _sgfam = sgfam;
    }

    public IReadOnlyList<(string Name, string Sgbd)> GetFaCandidates()
    {
        var candidates = new List<(string Name, string Sgbd)>();
        candidates.AddRange(FallbackCandidates);

        foreach (var entry in _sgfam.Read().Where(x => x.FaHolder))
            candidates.Add((entry.LogicalName, entry.Sgbd));

        return candidates
            .GroupBy(x => x.Sgbd, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public async Task<IReadOnlyList<VehicleOrder>> ReadAllAvailableAsync(
        CancellationToken ct = default)
    {
        var list = new List<VehicleOrder>();

        foreach (var candidate in GetFaCandidates())
        {
            try
            {
                if (!await _session.JobExistsAsync(
                        candidate.Sgbd,
                        "C_FA_LESEN",
                        ct))
                    continue;

                list.Add(
                    await ReadFromAsync(
                        candidate.Name,
                        candidate.Sgbd,
                        ct));
            }
            catch (Exception ex)
            {
                _log.Add(
                    $"FA read {candidate.Name}/{candidate.Sgbd} skipped: {ex.Message}");
            }
        }

        if (list.Count == 0)
            throw new InvalidOperationException("No readable FA/VO copy found.");

        return list;
    }

    public Task<VehicleOrder> ReadCasAsync(CancellationToken ct = default) =>
        ReadFromAsync("CAS", "D_CAS", ct);

    public async Task<VehicleOrder> ReadFromAsync(
        string source,
        string sgbd,
        CancellationToken ct = default)
    {
        var read = await _session.RunJobAsync(
            sgbd,
            "C_FA_LESEN",
            ct: ct);

        var raw = FindString(read, "FAHRZEUGAUFTRAG")
                  ?? throw new InvalidOperationException(
                      $"{source} returned no FAHRZEUGAUFTRAG.");

        return await ParseAsync(source, raw, ct);
    }

    public async Task<VehicleOrder> ParseAsync(
        string source,
        string raw,
        CancellationToken ct = default)
    {
        var parse = await _session.RunJobAsync(
            "FA",
            "FA_STREAM2STRUCT",
            "1;" + raw,
            ct: ct);

        var all = parse.Sets
            .SelectMany(x => x.Values)
            .ToLookup(
                x => x.Key,
                x => x.Value,
                StringComparer.OrdinalIgnoreCase);

        string? One(string name) =>
            all[name]
                .Select(v => v?.ToString())
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));

        IReadOnlyList<string> Indexed(string prefix)
        {
            var result = new List<(int Index, string Value)>();

            foreach (var set in parse.Sets)
            foreach (var kv in set.Values)
            {
                if (!kv.Key.StartsWith(
                        prefix + "_",
                        StringComparison.OrdinalIgnoreCase) ||
                    kv.Value == null)
                    continue;

                if (int.TryParse(
                        kv.Key[(prefix.Length + 1)..],
                        out var index))
                {
                    result.Add((index, kv.Value.ToString()!));
                }
            }

            return result
                .OrderBy(x => x.Index)
                .Select(x => x.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var vo = new VehicleOrder(
            source,
            raw,
            One("STANDARD_FA"),
            One("VERSION"),
            One("BR")?.TrimEnd('_'),
            One("C_DATE")?.TrimStart('#'),
            One("C_TYP")?.TrimStart('*'),
            One("LACK")?.TrimStart('%'),
            One("POLSTER")?.TrimStart('&'),
            Indexed("SA"),
            Indexed("HO_WORT"),
            Indexed("E_WORT"),
            Indexed("ZUSBAU"));

        _log.Add(
            $"FA {source}: version={vo.Version ?? "?"}, " +
            $"{vo.Sa.Count} SA codes, BR={vo.Chassis}, date={vo.ProductionDate}");

        return vo;
    }

    public static string? FindString(JobResult result, string key)
    {
        foreach (var set in result.Sets)
        {
            var value = set.String(key);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }
}
