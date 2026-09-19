using Android.Content.Res;
using BmwE9xTool.Core;

namespace BmwE9xTool.Data;

public sealed record StarterInstallResult(bool Available, int FilesInstalled);

public sealed class StarterDataInstaller
{
    private const string AssetRoot = "e9x-starter";
    private readonly AssetManager _assets;
    private readonly AppPaths _paths;

    public StarterDataInstaller(AssetManager assets, AppPaths paths)
    {
        _assets = assets;
        _paths = paths;
    }

    public bool IsAvailable()
    {
        try
        {
            var entries = _assets.List(AssetRoot);
            return entries is { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public async Task<StarterInstallResult> InstallAsync(CancellationToken ct = default)
    {
        if (!IsAvailable())
            return new StarterInstallResult(false, 0);

        var installed = 0;
        installed += await CopyTreeAsync(AssetRoot + "/ecu", _paths.Ecu, ct);
        installed += await CopyTreeAsync(AssetRoot + "/ncs/daten", _paths.NcsDaten, ct);
        installed += await CopyTreeAsync(AssetRoot + "/ncs/sgdat", _paths.NcsSgdat, ct);

        return new StarterInstallResult(true, installed);
    }

    private async Task<int> CopyTreeAsync(
        string assetPath,
        string destination,
        CancellationToken ct)
    {
        var entries = _assets.List(assetPath);
        if (entries == null || entries.Length == 0)
        {
            // AssetManager.List() returns an empty array for files.
            try
            {
                await using var input = _assets.Open(assetPath, Access.Streaming);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var output = File.Create(destination);
                await input.CopyToAsync(output, ct);
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        Directory.CreateDirectory(destination);
        var count = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            count += await CopyTreeAsync(
                assetPath + "/" + entry,
                Path.Combine(destination, entry.ToLowerInvariant()),
                ct);
        }

        return count;
    }
}
