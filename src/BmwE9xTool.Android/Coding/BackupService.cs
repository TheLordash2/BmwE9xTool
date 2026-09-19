using System.Text.Json;
using BmwE9xTool.Core;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Coding;

public sealed class BackupService
{
    private readonly AppPaths _paths;
    private readonly AppLog _log;

    public BackupService(AppPaths paths, AppLog log)
    {
        _paths = paths;
        _log = log;
    }

    public async Task<string> SaveAsync(string vin, IReadOnlyList<VehicleOrder> orders, CancellationToken ct = default)
    {
        var folder = Path.Combine(_paths.Backups, Sanitize(vin), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(folder);

        foreach (var vo in orders)
        {
            var prefix = Sanitize(vo.Source).ToLowerInvariant();
            await File.WriteAllTextAsync(Path.Combine(folder, prefix + "_raw_fa.txt"), vo.RawFa, ct);
            await File.WriteAllTextAsync(Path.Combine(folder, prefix + "_decoded.json"),
                JsonSerializer.Serialize(vo, new JsonSerializerOptions { WriteIndented = true }), ct);
        }

        _log.Add($"Backup created: {folder}");
        return folder;
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
}
