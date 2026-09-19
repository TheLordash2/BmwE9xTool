using BmwE9xTool.Core;
using BmwE9xTool.Ediabas;

namespace BmwE9xTool.Vehicle;

public sealed class FaService
{
    private readonly EdiabasSession _session;
    private readonly AppLog _log;

    private static readonly (string Name, string Sgbd)[] Candidates =
    [
        ("CAS", "D_CAS"),
        ("FRM87", "FRM_87"),
        ("LM", "D_LM"),
        ("KBM", "D_KBM")
    ];

    public FaService(EdiabasSession session, AppLog log)
    {
        _session = session;
        _log = log;
    }

    public async Task<IReadOnlyList<VehicleOrder>> ReadAllAvailableAsync(CancellationToken ct = default)
    {
        var list = new List<VehicleOrder>();
        foreach (var c in Candidates)
        {
            try
            {
                if (!await _session.JobExistsAsync(c.Sgbd, "C_FA_LESEN", ct)) continue;
                var read = await _session.RunJobAsync(c.Sgbd, "C_FA_LESEN", ct: ct);
                var raw = FindString(read, "FAHRZEUGAUFTRAG");
                if (string.IsNullOrEmpty(raw)) continue;
                var parsed = await ParseAsync(c.Name, raw, ct);
                list.Add(parsed);
            }
            catch (Exception ex)
            {
                _log.Add($"FA read {c.Name}/{c.Sgbd} skipped: {ex.Message}");
            }
        }

        if (list.Count == 0)
            throw new InvalidOperationException("No readable FA/VO copy found.");
        return list;
    }

    public async Task<VehicleOrder> ReadCasAsync(CancellationToken ct = default)
    {
        var read = await _session.RunJobAsync("D_CAS", "C_FA_LESEN", ct: ct);
        var raw = FindString(read, "FAHRZEUGAUFTRAG")
                  ?? throw new InvalidOperationException("CAS returned no FAHRZEUGAUFTRAG.");
        return await ParseAsync("CAS", raw, ct);
    }

    private async Task<VehicleOrder> ParseAsync(string source, string raw, CancellationToken ct)
    {
        var parse = await _session.RunJobAsync("FA", "FA_STREAM2STRUCT", "1;" + raw, ct: ct);
        var all = parse.Sets.SelectMany(x => x.Values).ToLookup(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        string? One(string name) => all[name].Select(v => v?.ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        IReadOnlyList<string> Indexed(string prefix, string countName)
        {
            var result = new List<(int Index, string Value)>();
            foreach (var set in parse.Sets)
            foreach (var kv in set.Values)
            {
                if (!kv.Key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) || kv.Value == null) continue;
                if (int.TryParse(kv.Key[(prefix.Length + 1)..], out var index))
                    result.Add((index, kv.Value.ToString()!));
            }
            return result.OrderBy(x => x.Index).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var vo = new VehicleOrder(
            source,
            raw,
            One("STANDARD_FA"),
            One("BR")?.TrimEnd('_'),
            One("C_DATE")?.TrimStart('#'),
            One("C_TYP")?.TrimStart('*'),
            One("LACK")?.TrimStart('%'),
            One("POLSTER")?.TrimStart('&'),
            Indexed("SA", "SA_ANZ"),
            Indexed("HO_WORT", "HO_WORT_ANZ"),
            Indexed("E_WORT", "E_WORT_ANZ"),
            Indexed("ZUSBAU", "ZUSBAU_ANZ"));

        _log.Add($"FA {source}: {vo.Sa.Count} SA codes, BR={vo.Chassis}, date={vo.ProductionDate}");
        return vo;
    }

    private static string? FindString(JobResult result, string key)
    {
        foreach (var set in result.Sets)
        {
            var value = set.String(key);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return null;
    }
}
