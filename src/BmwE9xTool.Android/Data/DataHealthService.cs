using BmwE9xTool.Coding;
using BmwE9xTool.Core;

namespace BmwE9xTool.Data;

public sealed record DataHealthItem(string Name, bool Ok, string Detail);

public sealed class DataHealthService
{
    private readonly AppPaths _paths;
    private readonly NcsSgfamParser _sgfam;
    private readonly SwtCatalog _swt;
    private readonly CabdCatalog _cabd;

    public DataHealthService(
        AppPaths paths,
        NcsSgfamParser sgfam,
        SwtCatalog swt,
        CabdCatalog cabd)
    {
        _paths = paths;
        _sgfam = sgfam;
        _swt = swt;
        _cabd = cabd;
    }

    public IReadOnlyList<DataHealthItem> Run()
    {
        var result = new List<DataHealthItem>();

        result.Add(new DataHealthItem(
            "EDIABAS ECU data",
            _paths.HasMinimumEcuData,
            _paths.HasMinimumEcuData
                ? "CAS group/variant data found."
                : "CAS group/variant data is missing."));

        var faPrg = FindFile(_paths.Ecu, "fa.prg");
        result.Add(new DataHealthItem(
            "FA.PRG",
            faPrg != null,
            faPrg != null
                ? Path.GetFileName(faPrg)
                : "Required for FA/VO conversion and write staging."));

        IReadOnlyList<SgfamEntry> sgfam = Array.Empty<SgfamEntry>();
        try
        {
            sgfam = _sgfam.Read();
            result.Add(new DataHealthItem(
                "E89 SGFAM",
                sgfam.Count > 0,
                sgfam.Count > 0
                    ? $"{sgfam.Count} logical ECU entries parsed."
                    : "E89SGFAM.DAT was not found or contained no entries."));
        }
        catch (Exception ex)
        {
            result.Add(new DataHealthItem("E89 SGFAM", false, ex.Message));
        }

        try
        {
            var aktiv = _swt.PswId("aktiv");
            var usb = _swt.FswId("USB_RAD2");

            result.Add(new DataHealthItem(
                "SWT keyword tables",
                aktiv.HasValue && usb.HasValue,
                $"USB_RAD2={(usb.HasValue ? $"0x{usb.Value:X4}" : "missing")}, " +
                $"aktiv={(aktiv.HasValue ? $"0x{aktiv.Value:X4}" : "missing")}"));
        }
        catch (Exception ex)
        {
            result.Add(new DataHealthItem("SWT keyword tables", false, ex.Message));
        }

        try
        {
            var files = _cabd.ModuleFiles().ToList();
            var rad2Files = new List<string>();

            foreach (var path in files)
            {
                try
                {
                    var module = _cabd.Parse(path);
                    if (module.Function("USB_RAD2") != null)
                        rad2Files.Add(Path.GetFileName(path));
                }
                catch
                {
                    // Report aggregate health; individual malformed files are ignored here.
                }
            }

            result.Add(new DataHealthItem(
                "CABD modules",
                files.Count > 0,
                $"{files.Count} Cxx files imported; {rad2Files.Count} expose USB_RAD2."));

            result.Add(new DataHealthItem(
                "RAD2 CABD",
                rad2Files.Count > 0,
                rad2Files.Count > 0
                    ? string.Join(", ", rad2Files.Take(8))
                    : "No imported Cxx module exposes USB_RAD2."));
        }
        catch (Exception ex)
        {
            result.Add(new DataHealthItem("CABD modules", false, ex.Message));
        }

        var radioPrgs = new[]
        {
            "rad2.prg",
            "rad2_gw.prg",
            "d_mmi.prg",
            "mcgwpl2.prg"
        }
        .Where(name => FindFile(_paths.Ecu, name) != null)
        .ToList();

        result.Add(new DataHealthItem(
            "RAD2 diagnostic SGBD",
            radioPrgs.Count > 0,
            radioPrgs.Count > 0
                ? string.Join(", ", radioPrgs)
                : "No RAD2-compatible PRG candidate found."));

        return result;
    }

    private static string? FindFile(string folder, string filename)
    {
        if (!Directory.Exists(folder)) return null;

        return Directory.EnumerateFiles(folder)
            .FirstOrDefault(x =>
                Path.GetFileName(x).Equals(filename, StringComparison.OrdinalIgnoreCase));
    }
}
