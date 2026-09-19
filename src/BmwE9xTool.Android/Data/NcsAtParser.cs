using System.Text.RegularExpressions;
using BmwE9xTool.Core;

namespace BmwE9xTool.Data;

public sealed class NcsAtParser
{
    private readonly AppPaths _paths;
    private Dictionary<string, string>? _cache;

    public NcsAtParser(AppPaths paths) => _paths = paths;

    public IReadOnlyDictionary<string, string> Read(string chassis = "E89")
    {
        if (_cache != null) return _cache;

        var preferred = Path.Combine(_paths.NcsDaten, chassis.ToLowerInvariant() + "at.000");
        var file = File.Exists(preferred)
            ? preferred
            : Directory.Exists(_paths.NcsDaten)
                ? Directory.EnumerateFiles(_paths.NcsDaten, "*at.000", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault()
                : null;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (file == null)
        {
            _cache = map;
            return map;
        }

        foreach (var original in File.ReadLines(file))
        {
            var line = original.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;

            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            var body = commentIndex >= 0 ? line[..commentIndex].Trim() : line;
            var comment = commentIndex >= 0 ? line[(commentIndex + 2)..].Trim() : string.Empty;
            var columns = Regex.Split(body, @"\s+");

            if (columns.Length < 2) continue;
            var code = columns[1].Trim().TrimStart('$', '#', '*', '%', '&', '|', '+', '-');
            if (code.Length < 2) continue;

            if (!map.ContainsKey(code) && !string.IsNullOrWhiteSpace(comment))
                map[code] = comment;
        }

        _cache = map;
        return map;
    }

    public string? Describe(string code)
    {
        var normalized = code.Trim().TrimStart('$').ToUpperInvariant();
        var map = Read();
        return map.TryGetValue(normalized, out var description) ? description : null;
    }

    public void Invalidate() => _cache = null;
}
