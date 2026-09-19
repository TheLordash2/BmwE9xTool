using System.IO.Compression;

namespace BmwE9xTool.Data;

public static class DataImporter
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".prg", ".grp", ".ipo", ".000", ".dat", ".asc", ".zus", ".m00", ".ssd", ".ini", ".txt"
    };

    public static async Task<int> ImportZipAsync(Stream zipStream, string appRoot)
    {
        var count = 0;
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var ext = Path.GetExtension(entry.Name);
            if (!Allowed.Contains(ext)) continue;

            var lower = entry.FullName.Replace('\\', '/').ToLowerInvariant();
            var targetBase = lower.Contains("/sgdat/") || ext.Equals(".ipo", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(appRoot, "ncs", "sgdat")
                : lower.Contains("/daten/")
                    ? Path.Combine(appRoot, "ncs", "daten")
                    : Path.Combine(appRoot, "ecu");

            Directory.CreateDirectory(targetBase);
            var safeName = Path.GetFileName(entry.Name);
            var target = Path.Combine(targetBase, safeName.ToLowerInvariant());
            await using var input = entry.Open();
            await using var output = File.Create(target);
            await input.CopyToAsync(output);
            count++;
        }
        return count;
    }
}
