using System.Security.Cryptography;
using System.Text;
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
        var vehicleKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(vin.Trim().ToUpperInvariant())))[..12];

        var folder = Path.Combine(
            _paths.Backups,
            "vehicle_" + vehicleKey,
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        Directory.CreateDirectory(folder);

        foreach (var vo in orders)
        {
            var prefix = Sanitize(vo.Source).ToLowerInvariant();
            await File.WriteAllTextAsync(Path.Combine(folder, prefix + "_raw_fa.txt"), vo.RawFa, ct);
            await File.WriteAllTextAsync(
                Path.Combine(folder, prefix + "_decoded.json"),
                JsonSerializer.Serialize(vo, new JsonSerializerOptions { WriteIndented = true }),
                ct);
        }

        _log.Add("Coding backup created in private app storage (vehicle identifier hashed in path).");
        return folder;
    }

    public async Task<string> SaveBinaryAsync(
        string vin,
        string name,
        byte[] data,
        CancellationToken ct = default)
    {
        var vehicleKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(vin.Trim().ToUpperInvariant())))[..12];

        var folder = Path.Combine(
            _paths.Backups,
            "vehicle_" + vehicleKey,
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, Sanitize(name).ToLowerInvariant() + ".bin");
        await File.WriteAllBytesAsync(path, data, ct);

        _log.Add("Binary coding backup created in private app storage.");
        return path;
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
}
