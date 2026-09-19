using System.Text.RegularExpressions;
using BmwE9xTool.Core;

namespace BmwE9xTool.Data;

public sealed record SgfamEntry(
    string LogicalName,
    string Cabd,
    string Sgbd,
    bool ZcsHolder,
    bool FaHolder,
    string RawLine);

public sealed class NcsSgfamParser
{
    private readonly AppPaths _paths;

    public NcsSgfamParser(AppPaths paths) => _paths = paths;

    public IReadOnlyList<SgfamEntry> Read(string chassis = "E89")
    {
        var preferred = Path.Combine(_paths.NcsDaten, chassis.ToLowerInvariant() + "sgfam.dat");
        var file = File.Exists(preferred)
            ? preferred
            : Directory.Exists(_paths.NcsDaten)
                ? Directory.EnumerateFiles(_paths.NcsDaten, "*sgfam.dat", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault()
                : null;

        if (file == null) return Array.Empty<SgfamEntry>();

        var result = new List<SgfamEntry>();
        foreach (var original in File.ReadLines(file))
        {
            var line = original.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;

            var semicolon = line.IndexOf(';');
            if (semicolon >= 0) line = line[..semicolon].Trim();

            var columns = Regex.Split(line, @"\s+");
            if (columns.Length < 6 || !columns[0].Equals("S", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new SgfamEntry(
                columns[1],
                columns[2],
                columns[3],
                columns[4] == "1",
                columns[5] == "1",
                original));
        }

        return result
            .GroupBy(x => x.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
}
