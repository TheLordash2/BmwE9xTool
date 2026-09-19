using BmwE9xTool.Core;
using BmwE9xTool.Ediabas;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Coding;

/// <summary>
/// Experimental BN2000 FA writer for E8x/E9x.
///
/// FA.PRG converts STANDARD_FA text to the ECU representation via
/// FA_STREAM_FOR_ECU. Each target is written with C_FA_AUFTRAG and then read
/// back with C_FA_LESEN + FA_STREAM2STRUCT. Any mismatch aborts immediately.
/// </summary>
public sealed class FaWriteEngine
{
    private readonly EdiabasSession _session;
    private readonly FaService _fa;
    private readonly AppLog _log;

    private static readonly (string Name, string Sgbd)[] PreferredTargets =
    [
        ("CAS", "D_CAS"),
        ("FRM87", "FRM_87")
    ];

    public FaWriteEngine(EdiabasSession session, FaService fa, AppLog log)
    {
        _session = session;
        _fa = fa;
        _log = log;
    }

    public bool DirectWriteImplemented => true;
    public bool HardwareVerified => false;

    public async Task<IReadOnlyList<string>> DiscoverTargetsAsync(CancellationToken ct = default)
    {
        var targets = new List<string>();
        foreach (var target in PreferredTargets)
        {
            try
            {
                if (await _session.JobExistsAsync(target.Sgbd, "C_FA_AUFTRAG", ct) &&
                    await _session.JobExistsAsync(target.Sgbd, "C_FA_LESEN", ct))
                    targets.Add(target.Name);
            }
            catch (Exception ex)
            {
                _log.Add($"FA target probe {target.Name} skipped: {ex.Message}");
            }
        }
        return targets;
    }

    public async Task<string> ConvertForEcuAsync(
        VehicleOrder original,
        string modifiedStandardFa,
        CancellationToken ct = default)
    {
        var version = string.IsNullOrWhiteSpace(original.Version) ? "02" : original.Version!;
        // BLOCK;VERSION;FA_STREAM_OF;FA_STREAM_ECU
        var args = $"1;{version};{modifiedStandardFa};{original.RawFa}";
        var converted = await _session.RunJobAsync("FA", "FA_STREAM_FOR_ECU", args, ct: ct);

        if (!string.IsNullOrWhiteSpace(converted.JobStatus) &&
            !converted.JobStatus.Equals("OKAY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"FA conversion failed: {converted.JobStatus}");

        var stream = FaService.FindString(converted, "FA_STREAM_FOR_ECU");
        if (string.IsNullOrWhiteSpace(stream))
            throw new InvalidOperationException("FA.PRG returned no FA_STREAM_FOR_ECU.");

        _log.Add($"FA converted for ECU, stream length={stream.Length}");
        return stream;
    }

    public async Task<IReadOnlyList<string>> WriteAndVerifyAsync(
        VehicleOrder original,
        string modifiedStandardFa,
        CancellationToken ct = default)
    {
        var expected = NormalizeFa(modifiedStandardFa);
        var ecuStream = await ConvertForEcuAsync(original, modifiedStandardFa, ct);
        var written = new List<string>();

        foreach (var target in PreferredTargets)
        {
            ct.ThrowIfCancellationRequested();

            if (!await _session.JobExistsAsync(target.Sgbd, "C_FA_AUFTRAG", ct) ||
                !await _session.JobExistsAsync(target.Sgbd, "C_FA_LESEN", ct))
            {
                _log.Add($"FA target {target.Name} not supported by installed SGBD; skipped.");
                continue;
            }

            _log.Add($"FA write starting: {target.Name}");
            var write = await _session.RunJobAsync(target.Sgbd, "C_FA_AUFTRAG", ecuStream, ct: ct);
            if (!string.IsNullOrWhiteSpace(write.JobStatus) &&
                !write.JobStatus.Equals("OKAY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{target.Name} FA write failed: {write.JobStatus}");

            var verify = await _fa.ReadFromAsync(target.Name, target.Sgbd, ct);
            var actual = NormalizeFa(verify.StandardFa ?? "");
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{target.Name} FA read-back mismatch. Expected '{expected}', got '{actual}'.");

            written.Add(target.Name);
            _log.Add($"FA write verified: {target.Name}");
        }

        if (written.Count == 0)
            throw new InvalidOperationException("No supported FA write target was found.");

        return written;
    }

    private static string NormalizeFa(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
