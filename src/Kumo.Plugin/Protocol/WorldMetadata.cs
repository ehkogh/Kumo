using System.IO.Compression;
using System.Text;

namespace Kumo.Plugin.Protocol;

// The FlatBuffers tables VRChat's state manager exchanges (VRC.Core.Networking.FlatBuffers.FlatBuffers32).
// Field slots were read from the 2026 client's native accessors (the `Table.__offset(n)` each one
// calls) and agree with the schema order of the object-API classes. Only what Kumo needs is
// modelled; unknown fields are skipped on read and never written.
//
//   WorldMetadata      0 Hash (struct, 32 bytes)  1 PlayerPersistenceGuid (struct, 16)
//                      2 PlayerPersistenceMetadatas [ObjectMetadataPair]  3 ObjectMetadatas [ObjectMetadataPair]
//                      4 MaximumViewIDs  5 MinimumViewID  6 FirstPlayerPersistenceID  7 MaxPlayerPersistenceID
//                      8 NetworkCallingViews [..]
//   ObjectMetadataPair 0 ViewId  1 Metadata
//   ObjectMetadata     0 Is8Bit  1 DoNotPersist  2 Code  3 ComponentInfo [ComponentInfo]  4 Payload [ObjectBunch]  5 InitialPath
//   ComponentInfo      0 TypeId  1 UniqueHash  2 Hashes [int]  3 GameObjectHash  4 UdonInfo
//   UdonInfo           0 Names [string]  1 Types [string]
//   ObjectBunch        0 Time  1 Sender  2 Index  3 Data [ubyte]

public sealed class WorldMetadata
{
    public List<ObjectMetadataPair> PlayerPersistence { get; set; } = new();
    public List<ObjectMetadataPair> Objects { get; set; } = new();
    public int MaximumViewIds { get; set; }
    public int MinimumViewId { get; set; }
    public int FirstPlayerPersistenceId { get; set; }
    public int MaxPlayerPersistenceId { get; set; }

    // Both directions use the client's Compress.Deflate/Inflate framing: gzip of
    // [int32 length][FlatBuffers buffer], the buffer's root offset at its first byte.

    /// <summary>Event 25's third element (byte[] on the wire).</summary>
    public static WorldMetadata FromEvent25(string base64) => FromEvent25(Convert.FromBase64String(base64));

    public static WorldMetadata FromEvent25(byte[] gzipped)
    {
        var bytes = Gunzip(gzipped);
        var root = 4 + (int)BitConverter.ToUInt32(bytes, 4);
        return Read(new FbTable(bytes, root));
    }

    /// <summary>Event 24's payload.</summary>
    public byte[] ToEvent24()
    {
        var buffer = FbWriter.Write(this);
        var framed = new byte[4 + buffer.Length];
        BitConverter.GetBytes(buffer.Length).CopyTo(framed, 0);
        buffer.CopyTo(framed, 4);
        return Gzip(framed);
    }

    private static WorldMetadata Read(FbTable t) => new()
    {
        PlayerPersistence = t.Tables(2).Select(ObjectMetadataPair.Read).ToList(),
        Objects = t.Tables(3).Select(ObjectMetadataPair.Read).ToList(),
        MaximumViewIds = t.Int(4),
        MinimumViewId = t.Int(5),
        FirstPlayerPersistenceId = t.Int(6),
        MaxPlayerPersistenceId = t.Int(7),
    };

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }
}

public sealed class ObjectMetadataPair
{
    public int ViewId { get; set; }
    public ObjectMetadata Metadata { get; set; } = new();

    internal static ObjectMetadataPair Read(FbTable t) => new()
    {
        ViewId = t.Int(0),
        Metadata = t.Table(1) is { } m ? ObjectMetadata.Read(m) : new ObjectMetadata(),
    };
}

public sealed class ObjectMetadata
{
    public bool Is8Bit { get; set; }
    public bool DoNotPersist { get; set; }
    public byte Code { get; set; }
    public List<ComponentInfo> Components { get; set; } = new();
    public List<ObjectBunch> Payload { get; set; } = new();
    public string? InitialPath { get; set; }

    internal static ObjectMetadata Read(FbTable t) => new()
    {
        Is8Bit = t.Byte(0) != 0,
        DoNotPersist = t.Byte(1) != 0,
        Code = t.Byte(2),
        Components = t.Tables(3).Select(ComponentInfo.Read).ToList(),
        Payload = t.Tables(4).Select(ObjectBunch.Read).ToList(),
        InitialPath = t.String(5),
    };

    public ObjectMetadata CloneWithPayload(byte code, bool is8Bit, List<ObjectBunch> payload) => new()
    {
        Is8Bit = is8Bit,
        DoNotPersist = DoNotPersist,
        Code = code,
        Components = Components,
        Payload = payload,
        InitialPath = InitialPath,
    };
}

public sealed class ComponentInfo
{
    public int TypeId { get; set; }
    public int UniqueHash { get; set; }
    public int[] Hashes { get; set; } = Array.Empty<int>();
    public int GameObjectHash { get; set; }
    public string[]? UdonNames { get; set; }
    public string[]? UdonTypes { get; set; }

    internal static ComponentInfo Read(FbTable t)
    {
        var udon = t.Table(4);
        return new ComponentInfo
        {
            TypeId = t.Int(0),
            UniqueHash = t.Int(1),
            Hashes = t.Ints(2),
            GameObjectHash = t.Int(3),
            UdonNames = udon?.Strings(0),
            UdonTypes = udon?.Strings(1),
        };
    }
}

public sealed class ObjectBunch
{
    public int Time { get; set; }
    public int Sender { get; set; }
    public byte Index { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    internal static ObjectBunch Read(FbTable t) => new()
    {
        Time = t.Int(0),
        Sender = t.Int(1),
        Index = t.Byte(2),
        Data = t.Bytes(3) ?? Array.Empty<byte>(),
    };
}

/// <summary>Minimal FlatBuffers table reader.</summary>
internal readonly struct FbTable
{
    private readonly byte[] _b;
    private readonly int _pos;
    private readonly int _vtable;
    private readonly int _vtableLength;

    public FbTable(byte[] b, int pos)
    {
        _b = b;
        _pos = pos;
        _vtable = pos - BitConverter.ToInt32(b, pos);
        _vtableLength = BitConverter.ToUInt16(b, _vtable);
    }

    private int Field(int slot)
    {
        var v = 4 + 2 * slot;
        return v < _vtableLength ? BitConverter.ToUInt16(_b, _vtable + v) : 0;
    }

    public int Int(int slot) => Field(slot) is var o && o != 0 ? BitConverter.ToInt32(_b, _pos + o) : 0;
    public byte Byte(int slot) => Field(slot) is var o && o != 0 ? _b[_pos + o] : (byte)0;

    private int? Ref(int slot)
    {
        var o = Field(slot);
        if (o == 0) return null;
        var at = _pos + o;
        return at + (int)BitConverter.ToUInt32(_b, at);
    }

    public FbTable? Table(int slot) => Ref(slot) is { } p ? new FbTable(_b, p) : null;

    private (int Start, int Count)? Vector(int slot) =>
        Ref(slot) is { } p ? (p + 4, (int)BitConverter.ToUInt32(_b, p)) : null;

    public IEnumerable<FbTable> Tables(int slot)
    {
        if (Vector(slot) is not var (start, count)) yield break;
        for (var i = 0; i < count; i++)
        {
            var at = start + 4 * i;
            yield return new FbTable(_b, at + (int)BitConverter.ToUInt32(_b, at));
        }
    }

    public int[] Ints(int slot)
    {
        if (Vector(slot) is not var (start, count)) return Array.Empty<int>();
        var result = new int[count];
        for (var i = 0; i < count; i++) result[i] = BitConverter.ToInt32(_b, start + 4 * i);
        return result;
    }

    public byte[]? Bytes(int slot)
    {
        if (Vector(slot) is not var (start, count)) return null;
        var result = new byte[count];
        Array.Copy(_b, start, result, 0, count);
        return result;
    }

    public string? String(int slot) =>
        Vector(slot) is var (start, count) ? Encoding.UTF8.GetString(_b, start, count) : null;

    public string[]? Strings(int slot)
    {
        if (Vector(slot) is not var (start, count)) return null;
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var at = start + 4 * i;
            var s = at + (int)BitConverter.ToUInt32(_b, at);
            result[i] = Encoding.UTF8.GetString(_b, s + 4, (int)BitConverter.ToUInt32(_b, s));
        }
        return result;
    }
}

/// <summary>
/// Minimal FlatBuffers writer, front to back: every table is written as its vtable followed by
/// the table, and everything a table refers to is appended after it, so every uoffset points
/// forward as the format requires. Every inline field gets a 4-byte-aligned slot.
/// </summary>
internal sealed class FbWriter
{
    private readonly List<byte> _b = new();

    public static byte[] Write(WorldMetadata world)
    {
        var w = new FbWriter();
        var rootSlot = w.Reserve(4);
        w.Patch(rootSlot, w.WriteTable(new Field[]
        {
            Field.TableVector(3, world.Objects.Select(p => (Func<FbWriter, int>)(x => x.WritePair(p))).ToList()),
            Field.Int(4, world.MaximumViewIds),
            Field.Int(5, world.MinimumViewId),
            Field.Int(6, world.FirstPlayerPersistenceId),
            Field.Int(7, world.MaxPlayerPersistenceId),
        }));
        return w._b.ToArray();
    }

    private int WritePair(ObjectMetadataPair p) => WriteTable(new[]
    {
        Field.Int(0, p.ViewId),
        Field.Child(1, x => x.WriteMetadata(p.Metadata)),
    });

    private int WriteMetadata(ObjectMetadata m)
    {
        var fields = new List<Field>
        {
            Field.Byte(0, m.Is8Bit ? (byte)1 : (byte)0),
            Field.Byte(1, m.DoNotPersist ? (byte)1 : (byte)0),
            Field.Byte(2, m.Code),
            Field.TableVector(3, m.Components.Select(c => (Func<FbWriter, int>)(x => x.WriteComponent(c))).ToList()),
            Field.TableVector(4, m.Payload.Select(b => (Func<FbWriter, int>)(x => x.WriteBunch(b))).ToList()),
        };
        if (m.InitialPath is not null) fields.Add(Field.Child(5, x => x.WriteString(m.InitialPath)));
        return WriteTable(fields);
    }

    private int WriteComponent(ComponentInfo c)
    {
        var fields = new List<Field>
        {
            Field.Int(0, c.TypeId),
            Field.Int(1, c.UniqueHash),
            Field.Child(2, x => x.WriteInts(c.Hashes)),
            Field.Int(3, c.GameObjectHash),
        };
        if (c.UdonNames is not null || c.UdonTypes is not null)
            fields.Add(Field.Child(4, x => x.WriteTable(new[]
            {
                Field.StringVector(0, c.UdonNames ?? Array.Empty<string>()),
                Field.StringVector(1, c.UdonTypes ?? Array.Empty<string>()),
            })));
        return WriteTable(fields);
    }

    private int WriteBunch(ObjectBunch b) => WriteTable(new[]
    {
        Field.Int(0, b.Time),
        Field.Int(1, b.Sender),
        Field.Byte(2, b.Index),
        Field.Child(3, x => x.WriteBytes(b.Data)),
    });

    // A table: vtable (u16 vtable size, u16 table size, u16 per slot), padding, then the table:
    // soffset to the vtable, then one 4-byte slot per field. Children are written afterwards.
    private int WriteTable(IReadOnlyList<Field> fields)
    {
        var slots = fields.Count == 0 ? 0 : fields.Max(f => f.Slot) + 1;
        var vtableSize = 4 + 2 * slots;
        var tableSize = 4 + 4 * fields.Count;
        Align(2);
        var vtable = _b.Count;
        AddU16((ushort)vtableSize);
        AddU16((ushort)tableSize);
        var offsets = new ushort[slots];
        for (var i = 0; i < fields.Count; i++) offsets[fields[i].Slot] = (ushort)(4 + 4 * i);
        foreach (var o in offsets) AddU16(o);
        Align(4);
        var table = _b.Count;
        AddI32(table - vtable);
        var fieldPositions = new int[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            fieldPositions[i] = _b.Count;
            AddI32(fields[i].Scalar);
        }
        for (var i = 0; i < fields.Count; i++)
            if (fields[i].Write is { } write)
                Patch(fieldPositions[i], write(this));
        return table;
    }

    private int WriteTableVector(List<Func<FbWriter, int>> items)
    {
        Align(4);
        var start = _b.Count;
        AddI32(items.Count);
        var slots = Enumerable.Range(0, items.Count).Select(_ => Reserve(4)).ToArray();
        for (var i = 0; i < items.Count; i++) Patch(slots[i], items[i](this));
        return start;
    }

    private int WriteStringVector(string[] items) =>
        WriteTableVector(items.Select(s => (Func<FbWriter, int>)(x => x.WriteString(s))).ToList());

    private int WriteString(string s)
    {
        Align(4);
        var start = _b.Count;
        var bytes = Encoding.UTF8.GetBytes(s);
        AddI32(bytes.Length);
        _b.AddRange(bytes);
        _b.Add(0);
        return start;
    }

    private int WriteBytes(byte[] data)
    {
        Align(4);
        var start = _b.Count;
        AddI32(data.Length);
        _b.AddRange(data);
        return start;
    }

    private int WriteInts(int[] values)
    {
        Align(4);
        var start = _b.Count;
        AddI32(values.Length);
        foreach (var v in values) AddI32(v);
        return start;
    }

    private int Reserve(int bytes)
    {
        Align(4);
        var at = _b.Count;
        for (var i = 0; i < bytes; i++) _b.Add(0);
        return at;
    }

    // A uoffset at `at` pointing forward to `target`.
    private void Patch(int at, int target)
    {
        var value = BitConverter.GetBytes((uint)(target - at));
        for (var i = 0; i < 4; i++) _b[at + i] = value[i];
    }

    private void Align(int n) { while (_b.Count % n != 0) _b.Add(0); }
    private void AddU16(ushort v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); }
    private void AddI32(int v) => _b.AddRange(BitConverter.GetBytes(v));

    private sealed class Field
    {
        public int Slot { get; private set; }
        public int Scalar { get; private set; }
        public Func<FbWriter, int>? Write { get; private set; }

        public static Field Int(int slot, int v) => new() { Slot = slot, Scalar = v };
        public static Field Byte(int slot, byte v) => new() { Slot = slot, Scalar = v };
        public static Field Child(int slot, Func<FbWriter, int> write) => new() { Slot = slot, Write = write };
        public static Field TableVector(int slot, List<Func<FbWriter, int>> items) => Child(slot, x => x.WriteTableVector(items));
        public static Field StringVector(int slot, string[] items) => Child(slot, x => x.WriteStringVector(items));
    }
}
