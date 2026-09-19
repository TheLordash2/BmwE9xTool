using System.Text.RegularExpressions;
using BmwE9xTool.Core;
using BmwE9xTool.Data;

namespace BmwE9xTool.Coding;

public sealed record CabdParameter(int PswId, string Keyword, byte[] Data);

public sealed class CabdFunction
{
    public int FswId { get; init; }
    public string Keyword { get; init; } = string.Empty;
    public int Block { get; init; }
    public int Address { get; init; }
    public int Length { get; init; }
    public byte[] Mask { get; init; } = Array.Empty<byte>();
    public List<CabdParameter> Parameters { get; } = new();
}

public sealed record CabdCodingBlock(int Block, int Address, int Length, string Description);

public sealed class CabdModule
{
    public string FilePath { get; init; } = string.Empty;
    public string MemoryStructure { get; init; } = "BYTE";
    public string MemoryType { get; init; } = "FREI";
    public IReadOnlyList<int> CodingIndices { get; init; } = Array.Empty<int>();
    public IReadOnlyList<CabdFunction> Functions { get; init; } = Array.Empty<CabdFunction>();
    public IReadOnlyList<CabdCodingBlock> CodingBlocks { get; init; } = Array.Empty<CabdCodingBlock>();

    public CabdFunction? Function(string keyword) =>
        Functions.FirstOrDefault(x => x.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
}

public sealed class CabdCatalog
{
    private readonly AppPaths _paths;
    private readonly SwtCatalog _swt;

    public CabdCatalog(AppPaths paths, SwtCatalog swt)
    {
        _paths = paths;
        _swt = swt;
    }

    public IEnumerable<string> ModuleFiles()
    {
        if (!Directory.Exists(_paths.NcsDaten))
            yield break;

        var re = new Regex(@"\.c[0-9a-f]{2}$", RegexOptions.IgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_paths.NcsDaten))
        {
            if (re.IsMatch(Path.GetExtension(path)))
                yield return path;
        }
    }

    public CabdModule Parse(string path)
    {
        var daten = DatenBinary.ParseFile(path);
        var functions = new List<CabdFunction>();
        var groups = new List<CabdCodingBlock>();
        var codingIndices = new List<int>();

        CabdFunction? lastFunction = null;
        CabdParameter? lastParameter = null;
        var memoryStructure = "BYTE";
        var memoryType = "FREI";

        foreach (var ordered in daten.RowsInOrder)
        {
            var row = ordered.Values;
            var fields = ordered.Block.Fields;
            object? At(int index) =>
                index >= 0 && index < fields.Count &&
                row.TryGetValue(fields[index].Name, out var value)
                    ? value
                    : null;

            switch (ordered.Block.Name.ToUpperInvariant())
            {
                case "PARZUWEISUNG_FSW":
                {
                    var block = OptionalInt(At(0)) ?? 0;
                    var address = SwtCatalog.ToInt(At(1));
                    var length = SwtCatalog.ToInt(At(2));
                    var fsw = SwtCatalog.ToInt(At(3));
                    var mask = Bytes(At(5));

                    if (address == null || length == null || fsw == null || mask.Length != length)
                    {
                        lastFunction = null;
                        lastParameter = null;
                        break;
                    }

                    lastFunction = new CabdFunction
                    {
                        Block = block,
                        Address = address.Value,
                        Length = length.Value,
                        FswId = fsw.Value,
                        Keyword = _swt.FswName(fsw.Value) ?? $"FSW_{fsw.Value:X4}",
                        Mask = mask
                    };

                    functions.Add(lastFunction);
                    lastParameter = null;
                    break;
                }

                case "PARZUWEISUNG_PSW1":
                {
                    if (lastFunction == null) break;

                    var psw = SwtCatalog.ToInt(At(0));
                    var data = Bytes(At(1));
                    if (psw == null) break;

                    lastParameter = new CabdParameter(
                        psw.Value,
                        _swt.PswName(psw.Value) ?? $"PSW_{psw.Value:X4}",
                        data);

                    lastFunction.Parameters.Add(lastParameter);
                    break;
                }

                case "PARZUWEISUNG_PSW2":
                {
                    if (lastFunction == null || lastParameter == null) break;

                    var more = Bytes(At(0));
                    if (more.Length == 0) break;

                    var merged = new byte[lastParameter.Data.Length + more.Length];
                    Buffer.BlockCopy(lastParameter.Data, 0, merged, 0, lastParameter.Data.Length);
                    Buffer.BlockCopy(more, 0, merged, lastParameter.Data.Length, more.Length);

                    var replacement = lastParameter with { Data = merged };
                    var index = lastFunction.Parameters.IndexOf(lastParameter);
                    if (index >= 0) lastFunction.Parameters[index] = replacement;
                    lastParameter = replacement;
                    break;
                }

                case "CODIERDATENBLOCK":
                {
                    var block = OptionalInt(At(0)) ?? 0;
                    var address = SwtCatalog.ToInt(At(1)) ?? 0;
                    var length = SwtCatalog.ToInt(At(2)) ?? 0;
                    var description = At(3) as string ?? string.Empty;
                    if (length > 0)
                        groups.Add(new CabdCodingBlock(block, address, length, description));
                    lastFunction = null;
                    lastParameter = null;
                    break;
                }

                case "SPEICHERORG":
                    memoryStructure = At(0) as string ?? memoryStructure;
                    memoryType = At(1) as string ?? memoryType;
                    break;

                case "SGID_CODIERINDEX":
                    foreach (var n in Numbers(row.Values))
                        codingIndices.Add(n);
                    break;

                default:
                    if (!ordered.Block.Name.Equals("PARZUWEISUNG_PSW1", StringComparison.OrdinalIgnoreCase) &&
                        !ordered.Block.Name.Equals("PARZUWEISUNG_PSW2", StringComparison.OrdinalIgnoreCase))
                    {
                        lastFunction = null;
                        lastParameter = null;
                    }
                    break;
            }
        }

        return new CabdModule
        {
            FilePath = path,
            MemoryStructure = memoryStructure,
            MemoryType = memoryType,
            CodingIndices = codingIndices.Distinct().ToList(),
            Functions = functions,
            CodingBlocks = groups
        };
    }

    public CabdModule? FindModule(int codingIndex, params string[] requiredFunctions)
    {
        foreach (var path in ModuleFiles())
        {
            try
            {
                var module = Parse(path);

                if (module.CodingIndices.Count > 0 &&
                    !module.CodingIndices.Contains(codingIndex))
                    continue;

                if (requiredFunctions.All(x => module.Function(x) != null))
                    return module;
            }
            catch
            {
                // One malformed/unrelated CABD must not prevent scanning the rest.
            }
        }

        return null;
    }

    private static int? OptionalInt(object? value)
    {
        if (value == null) return null;
        if (value is List<object?> list && list.Count > 0)
            return SwtCatalog.ToInt(list[0]);
        return SwtCatalog.ToInt(value);
    }

    private static byte[] Bytes(object? value)
    {
        if (value == null) return Array.Empty<byte>();
        if (value is DatenRawBytes raw) return raw.Bytes;
        if (value is byte b) return new[] { b };

        if (value is List<object?> list)
        {
            var bytes = new List<byte>();
            foreach (var item in list)
            {
                var n = SwtCatalog.ToInt(item);
                if (n.HasValue) bytes.Add((byte)(n.Value & 0xff));
                else if (item is DatenRawBytes r) bytes.AddRange(r.Bytes);
            }
            return bytes.ToArray();
        }

        return Array.Empty<byte>();
    }

    private static IEnumerable<int> Numbers(IEnumerable<object?> values)
    {
        foreach (var value in values)
        {
            var n = SwtCatalog.ToInt(value);
            if (n.HasValue)
            {
                yield return n.Value;
                continue;
            }

            if (value is List<object?> list)
            {
                foreach (var item in list)
                {
                    var x = SwtCatalog.ToInt(item);
                    if (x.HasValue) yield return x.Value;
                }
            }
        }
    }
}
