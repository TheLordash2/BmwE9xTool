namespace BmwE9xTool.Core;

public sealed class AppPaths
{
    public AppPaths(string filesDir)
    {
        Root = filesDir;
        Ecu = Path.Combine(Root, "ecu");
        Ncs = Path.Combine(Root, "ncs");
        Backups = Path.Combine(Root, "backups");
        Logs = Path.Combine(Root, "logs");
        Directory.CreateDirectory(Ecu);
        Directory.CreateDirectory(Ncs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
    }

    public string Root { get; }
    public string Ecu { get; }
    public string Ncs { get; }
    public string Backups { get; }
    public string Logs { get; }

    public bool HasMinimumEcuData =>
        File.Exists(Path.Combine(Ecu, "d_cas.prg")) ||
        File.Exists(Path.Combine(Ecu, "D_CAS.PRG"));
}
