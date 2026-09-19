using BmwE9xTool.Core;
using BmwE9xTool.Ediabas;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Coding;

public sealed record Rad2Target(string Sgbd, int CodingIndex, CabdModule Cabd);

public sealed record Rad2ParameterState(
    string Function,
    string? CurrentValue,
    IReadOnlyList<string> AvailableValues);

public sealed class Rad2CodingService
{
    private readonly EdiabasSession _session;
    private readonly CabdCatalog _cabd;
    private readonly AppLog _log;

    private static readonly string[] TargetCandidates =
    [
        "D_MMI",
        "RAD2",
        "RAD2_GW",
        "MCGWPL2"
    ];

    public Rad2CodingService(EdiabasSession session, CabdCatalog cabd, AppLog log)
    {
        _session = session;
        _cabd = cabd;
        _log = log;
    }

    public async Task<Rad2Target> DetectAsync(CancellationToken ct = default)
    {
        foreach (var sgbd in TargetCandidates)
        {
            try
            {
                if (!await _session.JobExistsAsync(sgbd, "IDENT", ct) ||
                    !await _session.JobExistsAsync(sgbd, "C_C_LESEN", ct))
                    continue;

                var ident = await _session.RunJobAsync(sgbd, "IDENT", ct: ct);
                var ci = FindInt(ident, "ID_COD_INDEX");
                if (!ci.HasValue)
                    continue;

                var module = _cabd.FindModule(
                    ci.Value,
                    "USB_RAD2");

                if (module == null)
                {
                    _log.Add($"RAD2 candidate {sgbd}: CI={ci.Value:X2}, but matching CABD with USB_RAD2 not found.");
                    continue;
                }

                _log.Add($"RAD2 coding target found through {sgbd}, CI={ci.Value:X2}, CABD={Path.GetFileName(module.FilePath)}");
                return new Rad2Target(sgbd, ci.Value, module);
            }
            catch (Exception ex)
            {
                _log.Add($"RAD2 target candidate {sgbd} skipped: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "Could not resolve a RAD2 coding target. Confirm RAD2 ECU data and E89 Cxx/SWT files were imported.");
    }

    public async Task<IReadOnlyList<Rad2ParameterState>> ReadKnownStatesAsync(
        Rad2Target target,
        CancellationToken ct = default)
    {
        var netto = await ReadCodingAsync(target, ct);
        var names = new[] { "USB_RAD2", "BLUETOOTH_RAD2", "ULF_ECE" };
        var states = new List<Rad2ParameterState>();

        foreach (var name in names)
        {
            var function = target.Cabd.Function(name);
            if (function == null) continue;

            var current = DecodeParameter(function, netto);
            states.Add(new Rad2ParameterState(
                name,
                current?.Keyword,
                function.Parameters.Select(x => x.Keyword).ToList()));
        }

        return states;
    }

    public async Task<string> SetParameterAsync(
        Rad2Target target,
        string functionName,
        string parameterName,
        CancellationToken ct = default)
    {
        var function = target.Cabd.Function(functionName)
            ?? throw new InvalidOperationException(
                $"CABD does not contain FSW {functionName}.");

        var parameter = function.Parameters.FirstOrDefault(
            x => x.Keyword.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"FSW {functionName} has no PSW '{parameterName}'.");

        var before = await ReadCodingAsync(target, ct);
        var after = (byte[])before.Clone();

        Apply(function, parameter, after);

        if (before.SequenceEqual(after))
        {
            _log.Add($"RAD2 {functionName} already equals {parameterName}; no write required.");
            return "NO CHANGE";
        }

        await WriteCodingAsync(target, after, ct);

        var verify = await ReadCodingAsync(target, ct);
        var verified = DecodeParameter(function, verify);

        if (verified == null ||
            !verified.Keyword.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RAD2 read-back verification failed for {functionName}. " +
                $"Expected {parameterName}, got {verified?.Keyword ?? "unknown"}.");
        }

        _log.Add($"RAD2 coding verified: {functionName}={parameterName}");
        return "VERIFIED";
    }

    public async Task<byte[]> ReadCodingAsync(Rad2Target target, CancellationToken ct = default)
    {
        var groups = target.Cabd.CodingBlocks
            .Where(x => x.Length > 0)
            .OrderBy(x => x.Address)
            .ToList();

        if (groups.Count == 0)
            throw new InvalidOperationException("CABD contains no CODIERDATENBLOCK ranges.");

        var maxEnd = groups.Max(x => x.Address + x.Length);
        var netto = new byte[maxEnd];

        foreach (var group in groups)
        {
            var offset = 0;
            while (offset < group.Length)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = Math.Min(32, group.Length - offset);
                var address = group.Address + offset;
                var packet = BuildPacket(
                    target.Cabd,
                    address,
                    chunk,
                    data: null);

                var result = await _session.RunJobAsync(
                    target.Sgbd,
                    "C_C_LESEN",
                    binaryArgs: packet,
                    ct: ct);

                EnsureOkay(result, "RAD2 C_C_LESEN");

                var bytes = FindBinary(result, "CODIER_DATEN")
                    ?? throw new InvalidOperationException(
                        $"RAD2 returned no CODIER_DATEN for address 0x{address:X}.");

                if (bytes.Length < chunk)
                    throw new InvalidOperationException(
                        $"RAD2 returned only {bytes.Length} coding bytes for requested {chunk} at 0x{address:X}.");

                Buffer.BlockCopy(bytes, 0, netto, address, chunk);
                offset += chunk;
            }
        }

        _log.Add($"RAD2 coding read complete: {netto.Length} bytes.");
        return netto;
    }

    private async Task WriteCodingAsync(
        Rad2Target target,
        byte[] netto,
        CancellationToken ct)
    {
        if (!await _session.JobExistsAsync(target.Sgbd, "C_C_AUFTRAG", ct))
            throw new InvalidOperationException(
                "RAD2 SGBD does not expose C_C_AUFTRAG; refusing an unverified alternate write path.");

        var groups = target.Cabd.CodingBlocks
            .Where(x => x.Length > 0)
            .OrderBy(x => x.Address)
            .ToList();

        foreach (var group in groups)
        {
            var offset = 0;
            while (offset < group.Length)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = Math.Min(32, group.Length - offset);
                var address = group.Address + offset;

                if (address + chunk > netto.Length)
                    throw new InvalidOperationException(
                        $"Coding buffer is too short for CABD range 0x{address:X}+{chunk}.");

                var data = new byte[chunk];
                Buffer.BlockCopy(netto, address, data, 0, chunk);

                var packet = BuildPacket(
                    target.Cabd,
                    address,
                    chunk,
                    data);

                var result = await _session.RunJobAsync(
                    target.Sgbd,
                    "C_C_AUFTRAG",
                    binaryArgs: packet,
                    ct: ct);

                EnsureOkay(result, $"RAD2 C_C_AUFTRAG @0x{address:X}");
                offset += chunk;
            }
        }
    }

    private static byte[] BuildPacket(
        CabdModule module,
        int byteAddress,
        int byteCount,
        byte[]? data)
    {
        var wordWidth = module.MemoryStructure.Equals("BYTE", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 2;

        var alignedBytes = ((byteCount + wordWidth - 1) / wordWidth) * wordWidth;
        var wordCount = alignedBytes / wordWidth;
        var packet = new byte[22 + alignedBytes];

        packet[0] = 1;
        packet[1] = (byte)wordWidth;
        packet[2] = 0;
        packet[3] = module.MemoryType.Equals("BLOCK", StringComparison.OrdinalIgnoreCase)
            ? (byte)1
            : (byte)0;

        packet[13] = (byte)(alignedBytes & 0xff);
        packet[14] = (byte)((alignedBytes >> 8) & 0xff);
        packet[15] = (byte)(wordCount & 0xff);
        packet[16] = (byte)((wordCount >> 8) & 0xff);

        var wireAddress = byteAddress / wordWidth;
        packet[17] = (byte)(wireAddress & 0xff);
        packet[18] = (byte)((wireAddress >> 8) & 0xff);
        packet[19] = (byte)((wireAddress >> 16) & 0xff);
        packet[20] = (byte)((wireAddress >> 24) & 0xff);

        if (data != null)
        {
            if (data.Length != byteCount)
                throw new ArgumentException("Coding data length does not match byteCount.", nameof(data));

            Buffer.BlockCopy(data, 0, packet, 21, data.Length);
        }

        packet[21 + alignedBytes] = 0x03;
        return packet;
    }

    private static void Apply(CabdFunction function, CabdParameter parameter, byte[] netto)
    {
        if (function.Address < 0 ||
            function.Address + function.Length > netto.Length)
            throw new InvalidOperationException(
                $"FSW {function.Keyword} lies outside the coding buffer.");

        if (function.Mask.Length != function.Length)
            throw new InvalidOperationException(
                $"FSW {function.Keyword} has an invalid mask length.");

        for (var i = 0; i < function.Length; i++)
        {
            var mask = function.Mask[i];
            var source = i < parameter.Data.Length ? parameter.Data[i] : (byte)0;
            var index = function.Address + i;

            netto[index] = (byte)(
                (netto[index] & ~mask) |
                (source & mask));
        }
    }

    private static CabdParameter? DecodeParameter(CabdFunction function, byte[] netto)
    {
        if (function.Address < 0 ||
            function.Address + function.Length > netto.Length ||
            function.Mask.Length != function.Length)
            return null;

        foreach (var parameter in function.Parameters)
        {
            var match = true;

            for (var i = 0; i < function.Length; i++)
            {
                var mask = function.Mask[i];
                var actual = netto[function.Address + i] & mask;
                var expected =
                    (i < parameter.Data.Length ? parameter.Data[i] : (byte)0) & mask;

                if (actual != expected)
                {
                    match = false;
                    break;
                }
            }

            if (match) return parameter;
        }

        return null;
    }

    private static int? FindInt(JobResult result, string name)
    {
        foreach (var set in result.Sets)
        {
            if (!set.Values.TryGetValue(name, out var value) || value == null)
                continue;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                // continue
            }
        }

        return null;
    }

    private static byte[]? FindBinary(JobResult result, string name)
    {
        foreach (var set in result.Sets)
        {
            if (set.Values.TryGetValue(name, out var value) &&
                value is byte[] bytes)
                return bytes;
        }

        return null;
    }

    private static void EnsureOkay(JobResult result, string operation)
    {
        if (!string.IsNullOrWhiteSpace(result.JobStatus) &&
            !result.JobStatus.Equals("OKAY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{operation} failed: {result.JobStatus}");
    }
}
