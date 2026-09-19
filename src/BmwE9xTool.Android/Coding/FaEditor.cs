using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Coding;

public sealed class FaEditor
{
    private readonly VehicleOrder _original;
    private readonly SortedSet<string> _sa;

    public FaEditor(VehicleOrder original)
    {
        _original = original;
        _sa = new SortedSet<string>(original.Sa.Select(OptionCatalog.Normalize), StringComparer.OrdinalIgnoreCase);
    }

    public VehicleOrder Original => _original;
    public IReadOnlyCollection<string> Sa => _sa;

    public bool Add(string code) => _sa.Add(OptionCatalog.Normalize(code));
    public bool Remove(string code) => _sa.Remove(OptionCatalog.Normalize(code));

    public string BuildStandardFa()
    {
        if (string.IsNullOrWhiteSpace(_original.Chassis))
            throw new InvalidOperationException("FA chassis/BR is missing.");
        if (string.IsNullOrWhiteSpace(_original.ProductionDate))
            throw new InvalidOperationException("FA production date is missing.");
        if (string.IsNullOrWhiteSpace(_original.TypeCode))
            throw new InvalidOperationException("FA type code is missing.");

        var s = _original.Chassis!;
        if (!s.EndsWith("_", StringComparison.Ordinal)) s += "_";
        s += "#" + _original.ProductionDate!.TrimStart('#');
        s += "*" + _original.TypeCode!.TrimStart('*');
        if (!string.IsNullOrEmpty(_original.Paint)) s += "%" + _original.Paint!.TrimStart('%');
        if (!string.IsNullOrEmpty(_original.Upholstery)) s += "&" + _original.Upholstery!.TrimStart('&');
        foreach (var z in _original.ZbWords) s += "|" + z.TrimStart('|');
        foreach (var code in _sa) s += "$" + code;
        foreach (var h in _original.HoWords) s += "+" + h.TrimStart('+');
        foreach (var e in _original.EWords) s += "-" + e.TrimStart('-');
        return s;
    }
}
