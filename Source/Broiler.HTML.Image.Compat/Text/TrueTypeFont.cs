using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Broiler.HTML.Image.Adapters.Text;

/// <summary>
/// Minimal managed TrueType/OpenType (glyf-outline) font parser.  Reads the
/// tables required to measure and rasterise text: <c>head</c>, <c>maxp</c>,
/// <c>hhea</c>, <c>hmtx</c>, <c>cmap</c>, <c>loca</c> and <c>glyf</c>.
/// <para>
/// Only the glyf outline format is supported (the common case for .ttf and
/// many .otf files).  CFF/PostScript outlines are not handled; such fonts
/// load but produce no glyph contours.
/// </para>
/// </summary>
internal sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, uint> _tables;
    private readonly uint _glyfOffset;
    private readonly uint[] _loca;          // glyph data offsets into glyf, length numGlyphs + 1
    private readonly int _numGlyphs;
    private readonly int _numHMetrics;
    private readonly uint _hmtxOffset;
    private CmapLookup? _cmap;

    public int UnitsPerEm { get; }
    public int Ascender { get; }
    public int Descender { get; }       // typically negative
    public bool HasOutlines => _glyfOffset != 0 && _loca.Length > 1;

    private TrueTypeFont(byte[] data, Dictionary<string, uint> tables)
    {
        _data = data;
        _tables = tables;

        uint head = tables.GetValueOrDefault("head");
        UnitsPerEm = head != 0 ? ReadUInt16(head + 18) : 1000;
        if (UnitsPerEm <= 0) UnitsPerEm = 1000;
        int indexToLocFormat = head != 0 ? ReadInt16(head + 50) : 0;

        uint hhea = tables.GetValueOrDefault("hhea");
        Ascender = hhea != 0 ? ReadInt16(hhea + 4) : (int)(UnitsPerEm * 0.8);
        Descender = hhea != 0 ? ReadInt16(hhea + 6) : -(int)(UnitsPerEm * 0.2);
        _numHMetrics = hhea != 0 ? ReadUInt16(hhea + 34) : 0;

        uint maxp = tables.GetValueOrDefault("maxp");
        _numGlyphs = maxp != 0 ? ReadUInt16(maxp + 4) : 0;

        _hmtxOffset = tables.GetValueOrDefault("hmtx");
        _glyfOffset = tables.GetValueOrDefault("glyf");

        uint locaOffset = tables.GetValueOrDefault("loca");
        _loca = ReadLoca(locaOffset, indexToLocFormat, _numGlyphs);
    }

    /// <summary>Parses a font file's bytes; returns <c>null</c> when the data is not a recognised sfnt container.</summary>
    public static TrueTypeFont? Load(byte[] data)
    {
        if (data == null || data.Length < 12)
            return null;

        uint version = ReadU32(data, 0);
        // 0x00010000 = TrueType, 'true'/'typ1' = legacy Mac, 'OTTO' = CFF-based OpenType.
        // 'ttcf' = font collection (use the first face).
        uint tableDirOffset = 0;
        if (version == 0x74746366u) // 'ttcf'
        {
            if (data.Length < 16) return null;
            tableDirOffset = ReadU32(data, 12); // offset of first font
        }

        int numTables = ReadU16(data, (int)tableDirOffset + 4);
        var tables = new Dictionary<string, uint>(numTables, StringComparer.Ordinal);
        int recordBase = (int)tableDirOffset + 12;
        for (int i = 0; i < numTables; i++)
        {
            int rec = recordBase + i * 16;
            if (rec + 16 > data.Length) break;
            string tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
            uint offset = ReadU32(data, rec + 8);
            tables[tag] = offset;
        }

        if (!tables.ContainsKey("head") || !tables.ContainsKey("maxp"))
            return null;

        return new TrueTypeFont(data, tables);
    }

    /// <summary>Maps a Unicode code point to a glyph index (0 = .notdef / missing).</summary>
    public int GetGlyphIndex(int codepoint)
    {
        _cmap ??= BuildCmap();
        return _cmap?.Map(codepoint) ?? 0;
    }

    /// <summary>Horizontal advance for a glyph, in font design units.</summary>
    public int GetAdvanceWidth(int glyphIndex)
    {
        if (_hmtxOffset == 0 || _numHMetrics == 0)
            return UnitsPerEm / 2;

        int index = Math.Min(glyphIndex, _numHMetrics - 1);
        return ReadUInt16(_hmtxOffset + (uint)(index * 4));
    }

    /// <summary>
    /// Returns the glyph outline as a list of closed contours in font design
    /// units (y axis points up, per the TrueType convention).  Quadratic
    /// segments are flattened to polylines.  Empty glyphs (e.g. spaces) return
    /// an empty list.
    /// </summary>
    public List<PointF[]> GetGlyphContours(int glyphIndex)
    {
        var result = new List<PointF[]>();
        AppendGlyphContours(glyphIndex, result, 0f, 0f, 1f, 0f, 0f, 1f, 0);
        return result;
    }

    // ── Glyph outline parsing ─────────────────────────────────────────────

    private void AppendGlyphContours(
        int glyphIndex, List<PointF[]> output,
        float dx, float dy, float a, float b, float c, float d, int depth)
    {
        if (depth > 8 || glyphIndex < 0 || glyphIndex >= _numGlyphs || _glyfOffset == 0)
            return;

        uint start = _glyfOffset + _loca[glyphIndex];
        uint end = _glyfOffset + _loca[glyphIndex + 1];
        if (end <= start)
            return; // empty glyph

        int numberOfContours = ReadInt16(start);
        uint p = start + 10; // skip numberOfContours(2) + bbox(8)

        if (numberOfContours < 0)
        {
            AppendCompositeGlyph(p, output, dx, dy, a, b, c, d, depth);
            return;
        }

        // Simple glyph.
        var endPts = new int[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPts[i] = ReadUInt16(p);
            p += 2;
        }

        int numPoints = numberOfContours > 0 ? endPts[numberOfContours - 1] + 1 : 0;
        if (numPoints <= 0)
            return;

        int instructionLength = ReadUInt16(p);
        p += 2 + (uint)instructionLength;

        // Flags (with repeat compression).
        var flags = new byte[numPoints];
        for (int i = 0; i < numPoints;)
        {
            byte flag = _data[p++];
            flags[i++] = flag;
            if ((flag & 0x08) != 0) // REPEAT_FLAG
            {
                int repeat = _data[p++];
                for (int r = 0; r < repeat && i < numPoints; r++)
                    flags[i++] = flag;
            }
        }

        // X coordinates (deltas).
        var xs = new int[numPoints];
        int x = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte flag = flags[i];
            if ((flag & 0x02) != 0) // X_SHORT_VECTOR
            {
                int delta = _data[p++];
                x += (flag & 0x10) != 0 ? delta : -delta; // POSITIVE_X_SHORT
            }
            else if ((flag & 0x10) == 0) // not SAME → signed 16-bit delta
            {
                x += ReadInt16(p);
                p += 2;
            }
            xs[i] = x;
        }

        // Y coordinates (deltas).
        var ys = new int[numPoints];
        int y = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte flag = flags[i];
            if ((flag & 0x04) != 0) // Y_SHORT_VECTOR
            {
                int delta = _data[p++];
                y += (flag & 0x20) != 0 ? delta : -delta; // POSITIVE_Y_SHORT
            }
            else if ((flag & 0x20) == 0)
            {
                y += ReadInt16(p);
                p += 2;
            }
            ys[i] = y;
        }

        // Split into contours and flatten.
        int pointStart = 0;
        for (int ci = 0; ci < numberOfContours; ci++)
        {
            int pointEnd = endPts[ci];
            int count = pointEnd - pointStart + 1;
            if (count > 0)
            {
                var pts = new List<(PointF p, bool on)>(count);
                for (int i = pointStart; i <= pointEnd; i++)
                {
                    float tx = a * xs[i] + c * ys[i] + dx;
                    float ty = b * xs[i] + d * ys[i] + dy;
                    pts.Add((new PointF(tx, ty), (flags[i] & 0x01) != 0));
                }

                var flat = FlattenContour(pts);
                if (flat.Length >= 2)
                    output.Add(flat);
            }
            pointStart = pointEnd + 1;
        }
    }

    private void AppendCompositeGlyph(
        uint p, List<PointF[]> output,
        float pdx, float pdy, float pa, float pb, float pc, float pd, int depth)
    {
        const int ARG_1_AND_2_ARE_WORDS = 0x0001;
        const int ARGS_ARE_XY_VALUES = 0x0002;
        const int WE_HAVE_A_SCALE = 0x0008;
        const int MORE_COMPONENTS = 0x0020;
        const int WE_HAVE_AN_X_AND_Y_SCALE = 0x0040;
        const int WE_HAVE_A_TWO_BY_TWO = 0x0080;

        while (true)
        {
            int flags = ReadUInt16(p); p += 2;
            int componentGlyph = ReadUInt16(p); p += 2;

            float arg1, arg2;
            if ((flags & ARG_1_AND_2_ARE_WORDS) != 0)
            {
                arg1 = ReadInt16(p); p += 2;
                arg2 = ReadInt16(p); p += 2;
            }
            else
            {
                arg1 = (sbyte)_data[p++];
                arg2 = (sbyte)_data[p++];
            }

            float a = 1f, b = 0f, c = 0f, d = 1f;
            if ((flags & WE_HAVE_A_SCALE) != 0)
            {
                a = d = ReadF2Dot14(p); p += 2;
            }
            else if ((flags & WE_HAVE_AN_X_AND_Y_SCALE) != 0)
            {
                a = ReadF2Dot14(p); p += 2;
                d = ReadF2Dot14(p); p += 2;
            }
            else if ((flags & WE_HAVE_A_TWO_BY_TWO) != 0)
            {
                a = ReadF2Dot14(p); p += 2;
                b = ReadF2Dot14(p); p += 2;
                c = ReadF2Dot14(p); p += 2;
                d = ReadF2Dot14(p); p += 2;
            }

            // Offset (only XY-value args are supported; point matching is ignored).
            float dx = 0f, dy = 0f;
            if ((flags & ARGS_ARE_XY_VALUES) != 0)
            {
                dx = arg1;
                dy = arg2;
            }

            // Compose child transform with parent transform.
            float ca = pa * a + pc * b;
            float cb = pb * a + pd * b;
            float cc = pa * c + pc * d;
            float cd = pb * c + pd * d;
            float cdx = pa * dx + pc * dy + pdx;
            float cdy = pb * dx + pd * dy + pdy;

            AppendGlyphContours(componentGlyph, output, cdx, cdy, ca, cb, cc, cd, depth + 1);

            if ((flags & MORE_COMPONENTS) == 0)
                break;
        }
    }

    private static PointF[] FlattenContour(List<(PointF p, bool on)> raw)
    {
        int len = raw.Count;
        if (len == 0)
            return Array.Empty<PointF>();

        // Insert implied on-curve points between consecutive off-curve points.
        var norm = new List<(PointF p, bool on)>(len * 2);
        for (int i = 0; i < len; i++)
        {
            var cur = raw[i];
            norm.Add(cur);
            var next = raw[(i + 1) % len];
            if (!cur.on && !next.on)
                norm.Add((Midpoint(cur.p, next.p), true));
        }

        int firstOn = norm.FindIndex(q => q.on);
        if (firstOn < 0)
            return Array.Empty<PointF>(); // degenerate (all off-curve)

        // Rotate so iteration starts on an on-curve point.
        int count = norm.Count;
        var output = new List<PointF>(count * 4);
        PointF startPoint = norm[firstOn].p;
        output.Add(startPoint);

        int idx = 1;
        PointF last = startPoint;
        while (idx <= count)
        {
            var q = norm[(firstOn + idx) % count];
            if (q.on)
            {
                output.Add(q.p);
                last = q.p;
                idx++;
            }
            else
            {
                PointF control = q.p;
                PointF endPoint = norm[(firstOn + idx + 1) % count].p;
                FlattenQuadratic(output, last, control, endPoint);
                last = endPoint;
                idx += 2;
            }
        }

        return output.ToArray();
    }

    private static void FlattenQuadratic(List<PointF> output, PointF p0, PointF c, PointF p1)
    {
        const int segments = 8;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float mt = 1f - t;
            float x = mt * mt * p0.X + 2f * mt * t * c.X + t * t * p1.X;
            float y = mt * mt * p0.Y + 2f * mt * t * c.Y + t * t * p1.Y;
            output.Add(new PointF(x, y));
        }
    }

    private static PointF Midpoint(PointF a, PointF b) =>
        new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

    // ── cmap parsing ──────────────────────────────────────────────────────

    private CmapLookup? BuildCmap()
    {
        uint cmap = _tables.GetValueOrDefault("cmap");
        if (cmap == 0)
            return null;

        int numTables = ReadUInt16(cmap + 2);
        uint bestOffset = 0;
        int bestScore = -1;
        for (int i = 0; i < numTables; i++)
        {
            uint rec = cmap + 4 + (uint)(i * 8);
            int platformId = ReadUInt16(rec);
            int encodingId = ReadUInt16(rec + 2);
            uint subOffset = cmap + ReadU32(_data, (int)rec + 4);

            // Prefer full Unicode (3,10) > BMP Unicode (3,1) > Unicode (0,*) > symbol (3,0).
            int score = (platformId, encodingId) switch
            {
                (3, 10) => 5,
                (0, 6) => 4,
                (0, 4) => 4,
                (3, 1) => 3,
                (0, _) => 2,
                (3, 0) => 1,
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = subOffset;
            }
        }

        if (bestOffset == 0)
            return null;

        int format = ReadUInt16(bestOffset);
        return format switch
        {
            0 => ParseCmapFormat0(bestOffset),
            4 => ParseCmapFormat4(bestOffset),
            6 => ParseCmapFormat6(bestOffset),
            12 => ParseCmapFormat12(bestOffset),
            _ => null,
        };
    }

    private CmapLookup ParseCmapFormat0(uint offset)
    {
        var map = new Dictionary<int, int>(256);
        for (int i = 0; i < 256; i++)
            map[i] = _data[offset + 6 + i];
        return new CmapLookup(map, null);
    }

    private CmapLookup ParseCmapFormat6(uint offset)
    {
        int first = ReadUInt16(offset + 6);
        int count = ReadUInt16(offset + 8);
        var map = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++)
            map[first + i] = ReadUInt16(offset + 10 + (uint)(i * 2));
        return new CmapLookup(map, null);
    }

    private CmapLookup ParseCmapFormat4(uint offset)
    {
        int segCountX2 = ReadUInt16(offset + 6);
        int segCount = segCountX2 / 2;
        uint endCodes = offset + 14;
        uint startCodes = endCodes + (uint)segCountX2 + 2; // +2 reservedPad
        uint idDeltas = startCodes + (uint)segCountX2;
        uint idRangeOffsets = idDeltas + (uint)segCountX2;

        var map = new Dictionary<int, int>();
        for (int s = 0; s < segCount; s++)
        {
            int end = ReadUInt16(endCodes + (uint)(s * 2));
            int start = ReadUInt16(startCodes + (uint)(s * 2));
            int idDelta = ReadInt16(idDeltas + (uint)(s * 2));
            int idRangeOffset = ReadUInt16(idRangeOffsets + (uint)(s * 2));

            if (start == 0xFFFF)
                continue;

            for (int code = start; code <= end && code != 0xFFFF; code++)
            {
                int glyph;
                if (idRangeOffset == 0)
                {
                    glyph = (code + idDelta) & 0xFFFF;
                }
                else
                {
                    uint glyphAddr = idRangeOffsets + (uint)(s * 2) + (uint)idRangeOffset + (uint)((code - start) * 2);
                    if (glyphAddr + 1 >= _data.Length)
                        continue;
                    int g = ReadUInt16(glyphAddr);
                    glyph = g == 0 ? 0 : (g + idDelta) & 0xFFFF;
                }
                if (glyph != 0)
                    map[code] = glyph;
            }
        }

        return new CmapLookup(map, null);
    }

    private CmapLookup ParseCmapFormat12(uint offset)
    {
        uint nGroups = ReadU32(_data, (int)offset + 12);
        var groups = new List<(uint start, uint end, uint startGlyph)>((int)Math.Min(nGroups, 100000));
        uint baseAddr = offset + 16;
        for (uint i = 0; i < nGroups; i++)
        {
            uint g = baseAddr + i * 12;
            if (g + 12 > _data.Length) break;
            uint startChar = ReadU32(_data, (int)g);
            uint endChar = ReadU32(_data, (int)g + 4);
            uint startGlyph = ReadU32(_data, (int)g + 8);
            groups.Add((startChar, endChar, startGlyph));
        }
        return new CmapLookup(null, groups);
    }

    // ── Low-level readers (big-endian) ────────────────────────────────────

    private uint[] ReadLoca(uint locaOffset, int format, int numGlyphs)
    {
        if (locaOffset == 0 || numGlyphs <= 0)
            return Array.Empty<uint>();

        var loca = new uint[numGlyphs + 1];
        if (format == 0) // short: offsets are stored /2
        {
            for (int i = 0; i <= numGlyphs; i++)
                loca[i] = (uint)ReadUInt16(locaOffset + (uint)(i * 2)) * 2u;
        }
        else // long
        {
            for (int i = 0; i <= numGlyphs; i++)
                loca[i] = ReadU32(_data, (int)locaOffset + i * 4);
        }
        return loca;
    }

    private int ReadUInt16(uint offset) => ReadU16(_data, (int)offset);
    private int ReadInt16(uint offset) => (short)ReadU16(_data, (int)offset);
    private float ReadF2Dot14(uint offset) => (short)ReadU16(_data, (int)offset) / 16384f;

    private static int ReadU16(byte[] data, int offset)
    {
        if (offset < 0 || offset + 1 >= data.Length) return 0;
        return (data[offset] << 8) | data[offset + 1];
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 3 >= data.Length) return 0;
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    public static TrueTypeFont? LoadFromFile(string path)
    {
        try
        {
            return File.Exists(path) ? Load(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Resolved cmap supporting either a sparse map or format-12 ranges.</summary>
    private sealed class CmapLookup
    {
        private readonly Dictionary<int, int>? _map;
        private readonly List<(uint start, uint end, uint startGlyph)>? _groups;

        public CmapLookup(Dictionary<int, int>? map, List<(uint start, uint end, uint startGlyph)>? groups)
        {
            _map = map;
            _groups = groups;
        }

        public int Map(int codepoint)
        {
            if (_map != null)
                return _map.TryGetValue(codepoint, out int g) ? g : 0;

            if (_groups != null)
            {
                uint cp = (uint)codepoint;
                foreach (var grp in _groups)
                {
                    if (cp >= grp.start && cp <= grp.end)
                        return (int)(grp.startGlyph + (cp - grp.start));
                }
            }

            return 0;
        }
    }
}
