using System.Text.RegularExpressions;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Coding;

/// <summary>
/// Edits only $SA tokens while preserving every other token and ordering from
/// the ECU-provided STANDARD_FA string.
/// </summary>
public sealed class FaEditor
{
    private readonly VehicleOrder _original;
    private readonly SortedSet<string> _sa;

    public FaEditor(VehicleOrder original)
    {
        _original = original;
        _sa = new SortedSet<string>(
            original.Sa.Select(OptionCatalog.Normalize),
            StringComparer.OrdinalIgnoreCase);
    }

    public VehicleOrder Original => _original;
    public IReadOnlyCollection<string> Sa => _sa;

    public bool Add(string code) => _sa.Add(OptionCatalog.Normalize(code));
    public bool Remove(string code) => _sa.Remove(OptionCatalog.Normalize(code));

    public string BuildStandardFa()
    {
        var source = _original.StandardFa;
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("STANDARD_FA is missing; refusing to rebuild FA.");

        // Strip only existing $SA tokens. Everything else remains byte-for-byte
        // in the same textual order.
        var withoutSa = Regex.Replace(
            source,
            @"\$[A-Za-z0-9]{3,4}",
            string.Empty,
            RegexOptions.CultureInvariant);

        var insertAt = withoutSa.Length;
        var ho = withoutSa.IndexOf('+');
        var ew = withoutSa.IndexOf('-');
        if (ho >= 0) insertAt = Math.Min(insertAt, ho);
        if (ew >= 0) insertAt = Math.Min(insertAt, ew);

        var saBlock = string.Concat(_sa.Select(x => "$" + x));
        return withoutSa.Insert(insertAt, saBlock);
    }
}
