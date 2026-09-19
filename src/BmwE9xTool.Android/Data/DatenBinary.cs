using System.Text;

namespace BmwE9xTool.Data;

public enum DatenFieldKind
{
    Scalar,
    Optional,
    Collection,
    NonEmptyList,
    RangeList
}

public enum DatenScalar
{
    B, W, L, S, A
}

public sealed record DatenField(DatenFieldKind Kind, DatenScalar Scalar, string Name);

public sealed class DatenRawBytes
{
    public DatenRawBytes(byte[] bytes) => Bytes = bytes;
    public byte[] Bytes { get; }
}

public sealed class DatenBlock
{
    public ushort Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<DatenField> Fields { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; } = new();
}

public sealed record DatenOrderedRow(DatenBlock Block, Dictionary<string, object?> Values);

public sealed class DatenFile
{
    public List<DatenBlock> Blocks { get; } = new();
    public List<DatenOrderedRow> RowsInOrder { get; } = new();

    public DatenBlock? Block(string name) =>
        Blocks.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public static class DatenBinary
{
    private const ushort Signature1 = 0x0100;
    private const ushort Signature2 = 0x0200;
    private const ushort BlockIdName = 0x0300;
    private const ushort BlockFormat = 0x0400;
    private const ushort BlockNames = 0x0500;
    private const ushort Divider = 0xff00;

    public static DatenFile ParseFile(string path, bool strictCrc = false)
    {
        return Parse(File.ReadAllBytes(path), strictCrc);
    }

    public static DatenFile Parse(byte[] data, bool strictCrc = false)
    {
        var file = new DatenFile();
        DatenBlock? current = null;
        List<(DatenFieldKind Kind, DatenScalar Scalar)>? pending = null;
        var off = 0;

        while (off + 4 <= data.Length)
        {
            var size = data[off];
            var frameLen = 3 + size + 1;
            if (off + frameLen > data.Length)
                throw new InvalidDataException($"Truncated DATEN frame at 0x{off:X}.");

            var type = (ushort)(data[off + 1] | (data[off + 2] << 8));
            var payload = data.AsSpan(off + 3, size).ToArray();
            var crc = data[off + 3 + size];
            var calc = Xor(data.AsSpan(off, frameLen - 1));

            if (calc != crc)
            {
                if (strictCrc)
                    throw new InvalidDataException($"DATEN CRC mismatch at 0x{off:X}: expected {crc:X2}, got {calc:X2}.");
                off += frameLen;
                continue;
            }

            switch (type)
            {
                case Signature1:
                case Signature2:
                case Divider:
                    break;

                case BlockIdName:
                    if (payload.Length < 2) break;
                    current = new DatenBlock
                    {
                        Id = (ushort)(payload[0] | (payload[1] << 8)),
                        Name = AsciiZ(payload.AsSpan(2))
                    };
                    pending = null;
                    file.Blocks.Add(current);
                    break;

                case BlockFormat:
                    if (current != null)
                        pending = ParseFormat(AsciiZ(payload));
                    break;

                case BlockNames:
                    if (current != null && pending != null)
                    {
                        var names = AsciiZ(payload).Split(',');
                        var fields = new List<DatenField>();
                        for (var i = 0; i < pending.Count; i++)
                        {
                            var name = i < names.Length ? names[i] : $"field_{i}";
                            fields.Add(new DatenField(pending[i].Kind, pending[i].Scalar, name));
                        }
                        current.Fields = fields;
                        pending = null;
                    }
                    break;

                default:
                    var block = file.Blocks.FirstOrDefault(x => x.Id == type);
                    if (block != null)
                    {
                        var row = ReadRow(block.Fields, payload);
                        block.Rows.Add(row);
                        file.RowsInOrder.Add(new DatenOrderedRow(block, row));
                    }
                    break;
            }

            off += frameLen;
        }

        return file;
    }

    private static Dictionary<string, object?> ReadRow(IReadOnlyList<DatenField> fields, byte[] payload)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var off = 0;

        ushort ReadU16()
        {
            Ensure(payload, off, 2);
            var v = (ushort)(payload[off] | (payload[off + 1] << 8));
            off += 2;
            return v;
        }

        foreach (var field in fields)
        {
            switch (field.Kind)
            {
                case DatenFieldKind.Scalar:
                    values[field.Name] = ReadScalar(field.Scalar, payload, ref off);
                    break;

                case DatenFieldKind.Optional:
                    Ensure(payload, off, 1);
                    var present = payload[off++];
                    values[field.Name] = present == 0
                        ? null
                        : ReadScalar(field.Scalar, payload, ref off);
                    break;

                case DatenFieldKind.Collection:
                {
                    var count = ReadU16();
                    var list = new List<object?>(count);
                    for (var i = 0; i < count; i++)
                        list.Add(ReadScalar(field.Scalar, payload, ref off));
                    values[field.Name] = list;
                    break;
                }

                case DatenFieldKind.NonEmptyList:
                {
                    var list = new List<object?> { ReadScalar(field.Scalar, payload, ref off) };
                    var count = ReadU16();
                    for (var i = 0; i < count; i++)
                        list.Add(ReadScalar(field.Scalar, payload, ref off));
                    values[field.Name] = list;
                    break;
                }

                case DatenFieldKind.RangeList:
                {
                    var list = new List<object?>
                    {
                        ReadScalar(field.Scalar, payload, ref off),
                        ReadScalar(field.Scalar, payload, ref off)
                    };
                    var count = ReadU16();
                    for (var i = 0; i < count; i++)
                    {
                        list.Add(ReadScalar(field.Scalar, payload, ref off));
                        list.Add(ReadScalar(field.Scalar, payload, ref off));
                    }
                    values[field.Name] = list;
                    break;
                }
            }
        }

        return values;
    }

    private static object ReadScalar(DatenScalar scalar, byte[] payload, ref int off)
    {
        switch (scalar)
        {
            case DatenScalar.B:
                Ensure(payload, off, 1);
                return payload[off++];

            case DatenScalar.W:
                Ensure(payload, off, 2);
                var w = payload[off] | (payload[off + 1] << 8);
                off += 2;
                return w;

            case DatenScalar.L:
                Ensure(payload, off, 4);
                var l = (uint)(
                    payload[off] |
                    (payload[off + 1] << 8) |
                    (payload[off + 2] << 16) |
                    (payload[off + 3] << 24));
                off += 4;
                return l;

            case DatenScalar.S:
            {
                var end = off;
                while (end < payload.Length && payload[end] != 0) end++;
                var s = Encoding.Latin1.GetString(payload, off, end - off);
                off = Math.Min(end + 1, payload.Length);
                return s;
            }

            case DatenScalar.A:
            {
                Ensure(payload, off, 1);
                var len = payload[off++];
                Ensure(payload, off, len);
                var bytes = payload.AsSpan(off, len).ToArray();
                off += len;
                return new DatenRawBytes(bytes);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(scalar));
        }
    }

    private static List<(DatenFieldKind Kind, DatenScalar Scalar)> ParseFormat(string format)
    {
        var shapes = new List<(DatenFieldKind, DatenScalar)>();
        var i = 0;

        while (i < format.Length)
        {
            var ch = format[i];

            if (ch == '{' && i + 2 < format.Length &&
                TryScalar(format[i + 1], out var optional) &&
                format[i + 2] == '}')
            {
                shapes.Add((DatenFieldKind.Optional, optional));
                i += 3;
                continue;
            }

            if (ch == '(' && i + 2 < format.Length &&
                TryScalar(format[i + 1], out var collection) &&
                format[i + 2] == ')')
            {
                shapes.Add((DatenFieldKind.Collection, collection));
                i += 3;
                continue;
            }

            if (TryScalar(ch, out var scalar))
            {
                if (i + 5 < format.Length &&
                    format[i + 1] == ch &&
                    format[i + 2] == '(' &&
                    format[i + 3] == ch &&
                    format[i + 4] == ch &&
                    format[i + 5] == ')')
                {
                    shapes.Add((DatenFieldKind.RangeList, scalar));
                    i += 6;
                    continue;
                }

                if (i + 3 < format.Length &&
                    format[i + 1] == '(' &&
                    format[i + 2] == ch &&
                    format[i + 3] == ')')
                {
                    shapes.Add((DatenFieldKind.NonEmptyList, scalar));
                    i += 4;
                    continue;
                }

                shapes.Add((DatenFieldKind.Scalar, scalar));
                i++;
                continue;
            }

            i++;
        }

        return shapes;
    }

    private static bool TryScalar(char c, out DatenScalar scalar)
    {
        scalar = c switch
        {
            'B' => DatenScalar.B,
            'W' => DatenScalar.W,
            'L' => DatenScalar.L,
            'S' => DatenScalar.S,
            'A' => DatenScalar.A,
            _ => DatenScalar.B
        };

        return c is 'B' or 'W' or 'L' or 'S' or 'A';
    }

    private static string AsciiZ(ReadOnlySpan<byte> bytes)
    {
        var end = 0;
        while (end < bytes.Length && bytes[end] != 0) end++;
        return Encoding.Latin1.GetString(bytes[..end]);
    }

    private static byte Xor(ReadOnlySpan<byte> bytes)
    {
        byte result = 0;
        foreach (var b in bytes) result ^= b;
        return result;
    }

    private static void Ensure(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > data.Length)
            throw new InvalidDataException("DATEN row overruns frame payload.");
    }
}
