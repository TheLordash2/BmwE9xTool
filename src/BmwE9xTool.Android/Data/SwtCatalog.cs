using System.Text.RegularExpressions;
using BmwE9xTool.Core;

namespace BmwE9xTool.Data;

public sealed class SwtCatalog
{
    private readonly AppPaths _paths;
    private Dictionary<string, int>? _fswByName;
    private Dictionary<int, string>? _fswById;
    private Dictionary<string, int>? _pswByName;
    private Dictionary<int, string>? _pswById;

    public SwtCatalog(AppPaths paths) => _paths = paths;

    public void Invalidate()
    {
        _fswByName = null;
        _fswById = null;
        _pswByName = null;
        _pswById = null;
    }

    public int? FswId(string keyword)
    {
        EnsureLoaded();
        return _fswByName!.TryGetValue(keyword, out var id) ? id : null;
    }

    public int? PswId(string keyword)
    {
        EnsureLoaded();
        return _pswByName!.TryGetValue(keyword, out var id) ? id : null;
    }

    public string? FswName(int id)
    {
        EnsureLoaded();
        return _fswById!.TryGetValue(id, out var name) ? name : null;
    }

    public string? PswName(int id)
    {
        EnsureLoaded();
        return _pswById!.TryGetValue(id, out var name) ? name : null;
    }

    private void EnsureLoaded()
    {
        if (_fswByName != null) return;

        (_fswByName, _fswById) = Load("FSW");
        (_pswByName, _pswById) = Load("PSW");
    }

    private (Dictionary<string, int>, Dictionary<int, string>) Load(string kind)
    {
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<int, string>();

        if (!Directory.Exists(_paths.NcsDaten))
            return (byName, byId);

        var re = new Regex($"^swt{kind.ToLowerInvariant()}[0-9a-f]{{2}}\\.dat$", RegexOptions.IgnoreCase);
        var file = Directory.EnumerateFiles(_paths.NcsDaten)
            .FirstOrDefault(x => re.IsMatch(Path.GetFileName(x)));

        if (file == null)
            return (byName, byId);

        var daten = DatenBinary.ParseFile(file);
        var block = daten.Block("SWT_EINTRAG");
        if (block == null)
            return (byName, byId);

        foreach (var row in block.Rows)
        {
            if (!row.TryGetValue("KEYID", out var idObj) ||
                !row.TryGetValue("KEYWORD", out var kwObj))
                continue;

            var id = ToInt(idObj);
            var keyword = kwObj as string;
            if (id == null || string.IsNullOrWhiteSpace(keyword))
                continue;

            byName[keyword] = id.Value;
            byId[id.Value] = keyword;
        }

        return (byName, byId);
    }

    internal static int? ToInt(object? value) => value switch
    {
        byte b => b,
        ushort w => w,
        int i => i,
        uint u when u <= int.MaxValue => (int)u,
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        _ => null
    };
}
