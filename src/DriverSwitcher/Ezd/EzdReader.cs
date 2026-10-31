using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DriverSwitcher.Ezd;

/// <summary>
/// Чтение бинарного формата .ezd (EzCAD2).
/// Портировано с открытого парсера meerk40t (meerk40t/extra/ezd.py, MIT).
/// </summary>
public static class EzdReader
{
    public static EzdFile Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public static EzdFile Load(Stream file)
    {
        var result = new EzdFile();
        var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

        // --- Header ---
        string magic = Encoding.Unicode.GetString(br.ReadBytes(16));
        if (magic != "EZCADUNI")
            throw new InvalidDataException("Это не файл EzCAD2 (нет сигнатуры EZCADUNI).");
        br.ReadInt32(); // 0
        br.ReadInt32(); // 2001
        br.ReadBytes(60); // s1
        br.ReadBytes(60); // s2
        br.ReadBytes(60); // s3
        br.ReadBytes(140);

        // --- Seek table ---
        int preview = br.ReadInt32();
        int v1 = br.ReadInt32();
        int pens = br.ReadInt32();
        int font = br.ReadInt32();
        int v4 = br.ReadInt32();
        int vectors = br.ReadInt32();
        int prevectors = br.ReadInt32();
        br.ReadBytes(96);

        if (font != 0)
        {
            file.Position = font;
            int count = br.ReadInt32();
            for (int i = 0; i < count && count < 1000; i++)
                result.Fonts.Add(Encoding.Unicode.GetString(br.ReadBytes(100)).Trim('\0'));
        }

        if (pens != 0)
        {
            file.Position = pens;
            int parameterCount = br.ReadInt32();
            int seek = br.ReadInt32();
            file.Position = seek;
            for (int i = 0; i < parameterCount; i++)
                result.Pens.Add(ReadPen(file, i));
        }

        if (vectors != 0)
        {
            file.Position = vectors;
            byte[] data = HuffmanDecode(br);
            ParseObjects(new MemoryStream(data), result.Objects);
        }

        return result;
    }

    // ---------- generic structure ----------

    private static List<byte[]> ParseStruct(BinaryReader br, int maxItemSize = 1024 * 1024)
    {
        var list = new List<byte[]>();
        byte[] countBytes = br.ReadBytes(4);
        if (countBytes.Length != 4)
            return list;
        int count = BitConverter.ToInt32(countBytes, 0);
        if (count < 0 || count > 100000)
            return list;

        for (int i = 0; i < count; i++)
        {
            byte[] lenBytes = br.ReadBytes(4);
            if (lenBytes.Length != 4)
                return list;
            int length = BitConverter.ToInt32(lenBytes, 0);
            if (length == -1 || length < 0 || length > maxItemSize)
                return list;
            byte[] data = br.ReadBytes(length);
            if (data.Length != length)
                return list;
            list.Add(data);
        }
        return list;
    }

    private static short AsShort(List<byte[]> args, int i) =>
        i < args.Count && args[i].Length == 2 ? BitConverter.ToInt16(args[i], 0) : (short)0;

    private static int AsInt(List<byte[]> args, int i) =>
        i < args.Count && args[i].Length == 4 ? BitConverter.ToInt32(args[i], 0) : 0;

    private static double AsDouble(List<byte[]> args, int i) =>
        i < args.Count && args[i].Length == 8 ? BitConverter.ToDouble(args[i], 0) : 0.0;

    /// <summary>Число из поля переменного типа: 8 байт = double, 4 = int32, 2 = int16.</summary>
    private static double AsNum(List<byte[]> args, int i)
    {
        if (i >= args.Count)
            return 0.0;
        byte[] b = args[i];
        return b.Length switch
        {
            8 => BitConverter.ToDouble(b, 0),
            4 => BitConverter.ToInt32(b, 0),
            2 => BitConverter.ToInt16(b, 0),
            _ => 0.0,
        };
    }

    private static (double x, double y) AsPoint(List<byte[]> args, int i) =>
        i < args.Count && args[i].Length == 16
            ? (BitConverter.ToDouble(args[i], 0), BitConverter.ToDouble(args[i], 8))
            : (0.0, 0.0);

    private static string AsString(List<byte[]> args, int i) =>
        i < args.Count ? Encoding.Unicode.GetString(args[i]).Trim('\0') : string.Empty;

    private static double[]? AsMatrix(List<byte[]> args, int i)
    {
        if (i >= args.Count || args[i].Length != 72)
            return null;
        var m = new double[9];
        for (int k = 0; k < 9; k++)
            m[k] = BitConverter.ToDouble(args[i], k * 8);
        return m;
    }

    // ---------- pens ----------

    private static EzdPen ReadPen(Stream file, int index)
    {
        var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        var args = ParseStruct(br);
        double power = AsNum(args, 6);
        // В разных версиях EzCAD мощность хранится 0-100 или 0-1000
        if (power > 100.0)
            power /= 10.0;
        return new EzdPen
        {
            Index = index,
            ColorBgr = AsInt(args, 0),
            Label = AsString(args, 1),
            MarkEnable = AsInt(args, 2) != 0,
            Passes = Math.Max(1, AsInt(args, 4)),
            Speed = AsNum(args, 5),
            Power = power,
            Frequency = AsNum(args, 7),
            StartTc = AsNum(args, 9),
            EndTc = AsNum(args, 10),
            PolygonTc = AsNum(args, 11),
            JumpSpeed = AsNum(args, 12),
        };
    }

    // ---------- huffman ----------

    private static byte[] HuffmanDecode(BinaryReader br)
    {
        int uncompressedLength = br.ReadInt32();
        br.ReadUInt32(); // unknown2
        br.ReadUInt32(); // unknown3
        br.ReadUInt32(); // data_start
        br.ReadUInt32(); // unknown5

        int tableLength = br.ReadUInt16();
        var decodeMap = new Dictionary<(uint bits, int length), byte>();
        for (int i = 0; i < tableLength; i++)
        {
            byte character = br.ReadByte();
            uint bits = br.ReadUInt32();
            int length = br.ReadUInt16();
            if (length > 0 && length <= 32)
            {
                uint code = length == 32 ? bits : bits & ((1u << length) - 1);
                decodeMap[(code, length)] = character;
            }
        }

        byte[] data = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));
        var output = new List<byte>(uncompressedLength);
        ulong acc = 0;
        int accBits = 0;
        foreach (byte b in data)
        {
            acc = (acc << 8) | b;
            accBits += 8;
            while (accBits > 0)
            {
                bool matched = false;
                for (int len = 1; len <= Math.Min(accBits, 32); len++)
                {
                    uint candidate = (uint)((acc >> (accBits - len)) & ((1ul << len) - 1ul));
                    if (decodeMap.TryGetValue((candidate, len), out byte value))
                    {
                        output.Add(value);
                        accBits -= len;
                        acc &= accBits == 0 ? 0ul : (1ul << accBits) - 1ul;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    if (accBits >= 32)
                    {
                        // Поток не сходится — сбрасываем старший бит и продолжаем
                        acc &= (1ul << (accBits - 1)) - 1ul;
                        accBits--;
                    }
                    else
                    {
                        break; // ждём следующие биты
                    }
                }
                if (output.Count >= uncompressedLength)
                    return output.ToArray();
            }
        }
        return output.ToArray();
    }

    // ---------- objects ----------

    private static readonly Dictionary<int, Func<BinaryReader, EzObject>> ObjectFactories = new()
    {
        { 1, br => ReadCurve(br, new EzCurve()) },
        { 3, br => ReadRect(br, new EzRect()) },
        { 4, br => ReadCircle(br, new EzCircle()) },
        { 5, br => ReadEllipse(br, new EzEllipse()) },
        { 6, br => ReadPolygon(br, new EzPolygon()) },
        { 0x30, br => ReadContainer(br, new EzCombine()) },
        { 0x40, br => ReadImage(br, new EzImage()) },
        { 0x60, br => ReadSpiral(br) },
        { 0x6000, br => ReadSimple(br, new EzOther()) },
        { 0x5000, br => ReadSimple(br, new EzOther()) },
        { 0x4000, br => ReadSimple(br, new EzOther()) },
        { 0x3000, br => ReadSimple(br, new EzOther()) },
        { 0x2000, br => ReadSimple(br, new EzOther()) },
        { 0x800, br => ReadText(br, new EzText()) },
        { 0x10, br => ReadContainer(br, new EzGroup()) },
        { 0x50, br => ReadVectorFile(br, new EzVectorFile()) },
        { 0x20, br => ReadHatch(br, new EzHatch()) },
    };

    private static void ParseObjects(MemoryStream data, List<EzObject> output)
    {
        var br = new BinaryReader(data, Encoding.UTF8, leaveOpen: true);
        int iterations = 0;
        while (data.Position < data.Length && iterations++ < 20000)
        {
            if (!ParseObject(br, output, enableResync: true))
            {
                if (data.Position >= data.Length)
                    break;
                // Терминатор 0x00000000 — пропускаем, следующая секция
            }
        }
    }

    private static bool ParseObject(BinaryReader br, List<EzObject> output, bool enableResync)
    {
        var data = br.BaseStream;
        if (data.Length - data.Position < 4)
            return false;
        int objectType = br.ReadInt32();
        if (objectType == 0)
            return false;

        if (!ObjectFactories.TryGetValue(objectType, out var factory))
        {
            if (enableResync)
                return Resync(br, output);
            return false;
        }

        try
        {
            var obj = factory(br);
            output.Add(obj);
            return true;
        }
        catch (Exception)
        {
            if (enableResync)
                return Resync(br, output);
            return false;
        }
    }

    private static bool Resync(BinaryReader br, List<EzObject> output)
    {
        var data = br.BaseStream;
        long start = data.Position;
        long end = Math.Min(start + 100 * 1024, data.Length);
        for (long pos = start; pos + 4 <= end; pos++)
        {
            data.Position = pos;
            int marker = br.ReadInt32();
            if (ObjectFactories.ContainsKey(marker))
            {
                data.Position = pos;
                return ParseObject(br, output, enableResync: true);
            }
        }
        return false;
    }

    /// <summary>Общий заголовок из 15 полей + дети для контейнеров.</summary>
    private static void ReadObjectHeader(BinaryReader br, EzObject obj, bool hasChildren)
    {
        var header = ParseStruct(br);
        obj.Pen = AsShort(header, 0);
        obj.State = AsShort(header, 2);
        obj.Label = AsString(header, 3);
        (obj.PosX, obj.PosY) = AsPoint(header, 13);

        if (!hasChildren)
            return;

        int count = br.ReadInt32();
        if (count < 0 || count > 10000)
            count = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                if (!ParseObject(br, obj.Children, enableResync: false))
                    break;
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    private static EzObject ReadContainer(BinaryReader br, EzObject obj)
    {
        ReadObjectHeader(br, obj, hasChildren: true);
        return obj;
    }

    private static EzObject ReadVectorFile(BinaryReader br, EzVectorFile obj)
    {
        ReadObjectHeader(br, obj, hasChildren: true);
        ParseStruct(br); // path + args
        return obj;
    }

    private static EzObject ReadCurve(BinaryReader br, EzCurve obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        uint count = br.ReadUInt32();
        uint closed = br.ReadUInt32();
        if (count > 100000)
            return obj;
        for (int i = 0; i < count; i++)
        {
            br.ReadByte(); // unk1
            int curveType = br.ReadByte();
            br.ReadUInt16(); // unk2
            br.ReadUInt16(); // unk3
            if (curveType == 0)
            {
                br.ReadBytes(40); // 5 doubles
                continue;
            }
            int ptCount = br.ReadInt32();
            if (ptCount < 0 || ptCount > 1000000)
                break;
            var contour = new EzContour { CurveType = curveType, Closed = closed != 0 };
            for (int p = 0; p < ptCount * 2; p++)
                contour.Points.Add(br.ReadDouble());
            obj.Contours.Add(contour);
        }
        return obj;
    }

    private static EzObject ReadRect(BinaryReader br, EzRect obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        var args = ParseStruct(br);
        (obj.MinX, obj.MinY) = AsPoint(args, 0);
        (obj.MaxX, obj.MaxY) = AsPoint(args, 1);
        obj.Matrix = AsMatrix(args, 7);
        return obj;
    }

    private static EzObject ReadCircle(BinaryReader br, EzCircle obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        var args = ParseStruct(br);
        (obj.Cx, obj.Cy) = AsPoint(args, 0);
        obj.Radius = AsDouble(args, 1);
        obj.Matrix = AsMatrix(args, 5);
        return obj;
    }

    private static EzObject ReadEllipse(BinaryReader br, EzEllipse obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        var args = ParseStruct(br);
        (obj.MinX, obj.MinY) = AsPoint(args, 1);
        (obj.MaxX, obj.MaxY) = AsPoint(args, 2);
        obj.StartAngle = AsDouble(args, 3);
        obj.EndAngle = AsDouble(args, 4);
        obj.Matrix = AsMatrix(args, 6);
        return obj;
    }

    private static EzObject ReadPolygon(BinaryReader br, EzPolygon obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        var args = ParseStruct(br);
        (obj.MinX, obj.MinY) = AsPoint(args, 1);
        (obj.MaxX, obj.MaxY) = AsPoint(args, 2);
        obj.Sides = AsInt(args, 7);
        obj.Matrix = AsMatrix(args, 9);
        return obj;
    }

    private static EzObject ReadSimple(BinaryReader br, EzOther obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        ParseStruct(br);
        return obj;
    }

    private static EzObject ReadSpiral(BinaryReader br)
    {
        var obj = new EzGroup { Label = "Spiral" };
        ReadObjectHeader(br, obj, hasChildren: true);
        ParseStruct(br);
        // Cached group (опционально)
        long tell = br.BaseStream.Position;
        if (br.BaseStream.Length - tell >= 4)
        {
            int check = br.ReadInt32();
            br.BaseStream.Position = tell;
            if (check == 0x10 || check == 15)
            {
                try { ParseObject(br, new List<EzObject>(), enableResync: false); }
                catch (Exception) { br.BaseStream.Position = tell; }
            }
        }
        return obj;
    }

    private static EzObject ReadText(BinaryReader br, EzText obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        var args = ParseStruct(br);
        obj.Text = AsString(args, 10);
        obj.Font = AsString(args, 18);
        obj.Height = AsDouble(args, 1);
        (obj.PosX, obj.PosY) = AsPoint(args, 7);

        int count = br.ReadInt32();
        if (count < 0 || count > 10000)
            count = 0;
        for (int i = 0; i < count; i++)
        {
            br.ReadUInt16(); // type
            ParseStruct(br);
            ParseStruct(br);
        }
        br.ReadInt32(); // unk
        return obj;
    }

    private static EzObject ReadImage(BinaryReader br, EzImage obj)
    {
        ReadObjectHeader(br, obj, hasChildren: false);
        ParseStruct(br); // args
        // BMP: "BM" + int32 size + данные
        br.ReadBytes(2);
        int size = br.ReadInt32();
        if (size > 6 && size < 500 * 1024 * 1024)
            br.ReadBytes(size - 6);
        return obj;
    }

    private static EzObject ReadHatch(BinaryReader br, EzHatch obj)
    {
        ReadObjectHeader(br, obj, hasChildren: true);
        var args = ParseStruct(br);

        if (args.Count >= 3 && args.Count < 6)
        {
            // V1: свойства упакованы в args[2] (big-endian)
            obj.MarkContours = args[0].Length > 0 && args[0][0] != 0;
            if (args.Count > 2 && args[2].Length >= 116)
            {
                byte[] d = args[2];
                obj.Hatch1Pen = (d[4] << 24) | (d[5] << 16) | (d[6] << 8) | d[7];
                int hatchType = (d[8] << 24) | (d[9] << 16) | (d[10] << 8) | d[11];
                obj.Hatch1Crosshatch = (hatchType & 0x400) != 0;
                obj.Hatch1Angle = (d[112] << 24) | (d[113] << 16) | (d[114] << 8) | d[115];
                obj.Hatch1Enabled = true;
                obj.Hatch1LineSpace = 0.03;
            }
        }
        else if (args.Count < 42)
        {
            obj.UnsupportedFormat = true;
        }
        else
        {
            obj.MarkContours = AsNum(args, 0) != 0;

            obj.Hatch1Enabled = AsNum(args, 1) != 0;
            obj.Hatch1Pen = (int)AsNum(args, 2);
            obj.Hatch1Crosshatch = ((int)AsNum(args, 3) & 0x400) != 0;
            obj.Hatch1Angle = AsNum(args, 8);
            obj.Hatch1LineSpace = AsNum(args, 5);

            obj.Hatch2Enabled = AsNum(args, 9) != 0;
            obj.Hatch2Pen = (int)AsNum(args, 10);
            obj.Hatch2Angle = AsNum(args, 16);
            obj.Hatch2LineSpace = AsNum(args, 13);

            obj.Hatch3Enabled = AsNum(args, 20) != 0;
            obj.Hatch3Pen = (int)AsNum(args, 21);
            obj.Hatch3Angle = AsNum(args, 27);
            obj.Hatch3LineSpace = AsNum(args, 24);
        }

        // Cached group (рендер штриховки) — пропускаем, но корректно сдвигаем поток
        long pos = br.BaseStream.Position;
        if (br.BaseStream.Length - pos >= 4)
        {
            int check = br.ReadInt32();
            if (check == 15)
            {
                br.BaseStream.Position = pos;
                try { ParseObject(br, new List<EzObject>(), enableResync: false); }
                catch (Exception) { br.BaseStream.Position = pos + 4; }
            }
            else
            {
                br.BaseStream.Position = pos;
            }
        }
        return obj;
    }
}
