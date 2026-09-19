using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Diagnostics;

public static class E90Catalog
{
    public static readonly IReadOnlyList<EcuTarget> Common =
    [
        new("CAS", "d_cas", "Car Access System"),
        new("DME/DDE", "d_motor", "Engine electronics"),
        new("EKPS", "d_ekp", "Fuel pump control"),
        new("DSC", "d_dsc", "Dynamic Stability Control"),
        new("ACSM", "d_sim", "Crash safety module"),
        new("CCC-BO", "d_mmi", "CCC front panel"),
        new("CCC-GW", "d_mostgw", "MOST gateway"),
        new("IHKA", "d_klima", "Climate control"),
        new("KBM", "d_kbm", "Body basic module"),
        new("KGM/ZGM", "d_zgm", "Body gateway"),
        new("KOMBI", "d_kombi", "Instrument cluster"),
        new("PDC", "d_pdc", "Park Distance Control"),
        new("RLS", "d_rls", "Rain/light sensor"),
        new("EPS", "d_eps", "Electric power steering"),
        new("ULF", "d_ispb", "Hands-free module"),
        new("FZD", "d_fzd", "Roof function centre")
    ];
}
