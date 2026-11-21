using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TextX.Core.Documents.Fonts
{
    /// <summary>
    /// Reads what text layout needs from a TrueType / OpenType font (.ttf, or one face of a .ttc):
    /// character coverage, advance widths and vertical metrics. Can also write the face as a
    /// standalone .ttf, which is how fonts from Windows font collections are embedded in PDFs.
    /// </summary>
    public sealed class TrueTypeFont
    {
        private readonly byte[] _data;
        private readonly int _faceOffset;
        private readonly Dictionary<string, KeyValuePair<int, int>> _tables = new Dictionary<string, KeyValuePair<int, int>>();
        private readonly Dictionary<int, int> _cmap = new Dictionary<int, int>();
        private ushort[] _advances;
        private int _numberOfHMetrics;

        public string FamilyName { get; private set; }
        public string FullName { get; private set; }
        public int UnitsPerEm { get; private set; }
        public int Ascender { get; private set; }
        public int Descender { get; private set; }  // negative
        public int LineGap { get; private set; }
        public int GlyphCount { get; private set; }
        public bool IsCff { get; private set; }
        public string SourcePath { get; private set; }
        public int FaceIndex { get; private set; }

        private TrueTypeFont(byte[] data, int faceOffset)
        {
            _data = data;
            _faceOffset = faceOffset;
        }

        public static TrueTypeFont Load(string path, int faceIndex = 0)
        {
            var f = Parse(File.ReadAllBytes(path), faceIndex);
            f.SourcePath = path;
            return f;
        }

        /// <summary>Number of faces in a font file (1 for .ttf, n for .ttc).</summary>
        public static int FaceCount(byte[] data)
        {
            return data.Length >= 12 && Tag(data, 0) == "ttcf" ? (int)U32(data, 8) : 1;
        }

        public static TrueTypeFont Parse(byte[] data, int faceIndex = 0)
        {
            int offset = 0;
            if (data.Length >= 12 && Tag(data, 0) == "ttcf")
            {
                int n = (int)U32(data, 8);
                if (faceIndex < 0 || faceIndex >= n) throw new InvalidDataException("Font collection has no face " + faceIndex + ".");
                offset = (int)U32(data, 12 + 4 * faceIndex);
            }
            var f = new TrueTypeFont(data, offset) { FaceIndex = faceIndex };
            f.ReadDirectory();
            f.ReadMetrics();
            f.ReadCmap();
            f.ReadNames();
            return f;
        }

        private void ReadDirectory()
        {
            uint version = U32(_data, _faceOffset);
            IsCff = version == 0x4F54544F; // 'OTTO'
            int numTables = U16(_data, _faceOffset + 4);
            for (int i = 0; i < numTables; i++)
            {
                int rec = _faceOffset + 12 + 16 * i;
                _tables[Tag(_data, rec)] = new KeyValuePair<int, int>((int)U32(_data, rec + 8), (int)U32(_data, rec + 12));
            }
            foreach (var required in new[] { "head", "hhea", "hmtx", "cmap", "maxp" })
                if (!_tables.ContainsKey(required)) throw new InvalidDataException("Font has no '" + required + "' table.");
        }

        private int Table(string tag) { return _tables[tag].Key; }

        private void ReadMetrics()
        {
            UnitsPerEm = U16(_data, Table("head") + 18);
            if (UnitsPerEm <= 0) UnitsPerEm = 1000;
            int hhea = Table("hhea");
            Ascender = S16(_data, hhea + 4);
            Descender = S16(_data, hhea + 6);
            LineGap = S16(_data, hhea + 8);
            _numberOfHMetrics = U16(_data, hhea + 34);
            GlyphCount = U16(_data, Table("maxp") + 4);
            KeyValuePair<int, int> os2;
            if (_tables.TryGetValue("OS/2", out os2) && os2.Value >= 78)
            {
                int typoAsc = S16(_data, os2.Key + 68), typoDesc = S16(_data, os2.Key + 70);
                if (Ascender == 0 && typoAsc != 0) { Ascender = typoAsc; Descender = typoDesc; }
            }
            int hmtx = Table("hmtx");
            _advances = new ushort[Math.Max(1, _numberOfHMetrics)];
            for (int i = 0; i < _numberOfHMetrics; i++) _advances[i] = (ushort)U16(_data, hmtx + 4 * i);
        }

        private void ReadCmap()
        {
            int cmap = Table("cmap");
            int n = U16(_data, cmap + 2);
            int best = -1, bestScore = -1;
            for (int i = 0; i < n; i++)
            {
                int rec = cmap + 4 + 8 * i;
                int platform = U16(_data, rec), encoding = U16(_data, rec + 2);
                int sub = cmap + (int)U32(_data, rec + 4);
                int format = U16(_data, sub);
                int score = -1;
                if (platform == 3 && encoding == 10 && format == 12) score = 4;
                else if (platform == 0 && format == 12) score = 3;
                else if (platform == 3 && encoding == 1 && format == 4) score = 2;
                else if (platform == 0 && format == 4) score = 1;
                else if (platform == 3 && encoding == 0 && format == 4) score = 0; // symbol fonts
                if (score > bestScore) { bestScore = score; best = sub; }
            }
            if (best < 0) return;
            int fmt = U16(_data, best);
            if (fmt == 4) ReadFormat4(best);
            else if (fmt == 12) ReadFormat12(best);
        }

        private void ReadFormat4(int sub)
        {
            int segX2 = U16(_data, sub + 6);
            int ends = sub + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
            for (int s = 0; s < segX2 / 2; s++)
            {
                int end = U16(_data, ends + 2 * s), start = U16(_data, starts + 2 * s);
                int delta = S16(_data, deltas + 2 * s);
                int rangeOffsetPos = ranges + 2 * s;
                int rangeOffset = U16(_data, rangeOffsetPos);
                if (start == 0xFFFF) break;
                for (int c = start; c <= end; c++)
                {
                    int glyph;
                    if (rangeOffset == 0) glyph = (c + delta) & 0xFFFF;
                    else
                    {
                        int addr = rangeOffsetPos + rangeOffset + 2 * (c - start);
                        if (addr + 1 >= _data.Length) continue;
                        glyph = U16(_data, addr);
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }
                    if (glyph != 0) _cmap[c] = glyph;
                }
            }
        }

        private void ReadFormat12(int sub)
        {
            int groups = (int)U32(_data, sub + 12);
            for (int g = 0; g < groups; g++)
            {
                int rec = sub + 16 + 12 * g;
                uint start = U32(_data, rec), end = U32(_data, rec + 4), glyph = U32(_data, rec + 8);
                if (end - start > 0x30000) continue; // corrupt group
                for (uint c = start; c <= end; c++) _cmap[(int)c] = (int)(glyph + (c - start));
            }
        }

        private void ReadNames()
        {
            KeyValuePair<int, int> name;
            if (!_tables.TryGetValue("name", out name)) return;
            int t = name.Key;
            int count = U16(_data, t + 2), strings = t + U16(_data, t + 4);
            for (int i = 0; i < count; i++)
            {
                int rec = t + 6 + 12 * i;
                int platform = U16(_data, rec), nameId = U16(_data, rec + 6), len = U16(_data, rec + 8), off = U16(_data, rec + 10);
                if (nameId != 1 && nameId != 4) continue;
                if (strings + off + len > _data.Length) continue;
                string value = platform == 3 || platform == 0
                    ? Encoding.BigEndianUnicode.GetString(_data, strings + off, len)
                    : Encoding.ASCII.GetString(_data, strings + off, len);
                if (nameId == 1 && (FamilyName == null || platform == 3)) FamilyName = value;
                if (nameId == 4 && (FullName == null || platform == 3)) FullName = value;
            }
        }

        public bool HasChar(int codePoint)
        {
            return _cmap.ContainsKey(codePoint);
        }

        public int GlyphOf(int codePoint)
        {
            int g;
            return _cmap.TryGetValue(codePoint, out g) ? g : 0;
        }

        /// <summary>Advance width of a character in font units (missing characters use glyph 0).</summary>
        public int AdvanceUnits(int codePoint)
        {
            int g = GlyphOf(codePoint);
            if (_advances.Length == 0) return UnitsPerEm / 2;
            return _advances[Math.Min(g, _advances.Length - 1)];
        }

        /// <summary>Width of <paramref name="text"/> at <paramref name="size"/> points.</summary>
        public float Measure(string text, float size)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            long units = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) { cp = char.ConvertToUtf32(text[i], text[i + 1]); i++; }
                units += AdvanceUnits(cp);
            }
            return units * size / UnitsPerEm;
        }

        /// <summary>Line height (ascender - descender + gap) as a multiple of the font size.</summary>
        public float LineHeightFactor
        {
            get { return (Ascender - Descender + Math.Max(0, LineGap)) / (float)UnitsPerEm; }
        }

        public float AscentFactor { get { return Ascender / (float)UnitsPerEm; } }
        public float DescentFactor { get { return -Descender / (float)UnitsPerEm; } }

        /// <summary>The face as a standalone font file (the original bytes for a .ttf).</summary>
        public byte[] ToStandaloneFont()
        {
            if (_faceOffset == 0) return _data;
            var tags = new List<string>(_tables.Keys);
            tags.Sort(StringComparer.Ordinal);
            int numTables = tags.Count;
            int headerSize = 12 + 16 * numTables;
            int total = headerSize;
            foreach (var t in tags) total += (_tables[t].Value + 3) & ~3;
            var output = new byte[total];
            int entrySelector = 0;
            while ((1 << (entrySelector + 1)) <= numTables) entrySelector++;
            int searchRange = (1 << entrySelector) * 16;
            WU32(output, 0, U32(_data, _faceOffset));
            WU16(output, 4, numTables);
            WU16(output, 6, searchRange);
            WU16(output, 8, entrySelector);
            WU16(output, 10, numTables * 16 - searchRange);
            int pos = headerSize;
            for (int i = 0; i < numTables; i++)
            {
                var t = tags[i];
                var tbl = _tables[t];
                int rec = 12 + 16 * i;
                for (int k = 0; k < 4; k++) output[rec + k] = (byte)t[k];
                Buffer.BlockCopy(_data, tbl.Key, output, pos, tbl.Value);
                if (t == "head") WU32(output, pos + 8, 0); // checkSumAdjustment is recomputed below
                WU32(output, rec + 4, Checksum(output, pos, tbl.Value));
                WU32(output, rec + 8, (uint)pos);
                WU32(output, rec + 12, (uint)tbl.Value);
                pos += (tbl.Value + 3) & ~3;
            }
            int headPos = (int)U32(output, 12 + 16 * tags.IndexOf("head") + 8);
            WU32(output, headPos + 8, 0xB1B0AFBAu - Checksum(output, 0, output.Length));
            return output;
        }

        /// <summary>
        /// A small font containing only the glyphs needed for <paramref name="codePoints"/> (plus glyphs
        /// they are built from), renumbered compactly, with a cmap listing exactly those characters.
        /// Text drawn with it is extracted back as exactly these characters. Falls back to the whole
        /// face for fonts without TrueType outlines.
        /// </summary>
        public byte[] Subset(ICollection<int> codePoints)
        {
            if (IsCff || !_tables.ContainsKey("glyf") || !_tables.ContainsKey("loca") || !_tables.ContainsKey("hmtx")) return ToStandaloneFont();
            int numGlyphs = GlyphCount;
            int head = Table("head");
            bool longLoca = S16(_data, head + 50) == 1;
            int loca = Table("loca"), glyf = Table("glyf"), hmtx = Table("hmtx");
            int numberOfHMetrics = Math.Max(1, U16(_data, Table("hhea") + 34));
            Func<int, int> offsetOf = g => longLoca ? (int)U32(_data, loca + 4 * g) : U16(_data, loca + 2 * g) * 2;

            var chars = new SortedDictionary<int, int>();
            var keep = new SortedSet<int> { 0 };
            foreach (var cp in codePoints)
            {
                int g = GlyphOf(cp);
                if (g <= 0 || g >= numGlyphs) continue;
                chars[cp] = g;
                keep.Add(g);
            }
            // composite glyphs need their components; remember where component numbers sit
            var componentRefs = new Dictionary<int, List<int>>(); // glyph -> offsets (within the glyph) of component numbers
            var queue = new Queue<int>(keep);
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                int start = offsetOf(g), end = offsetOf(g + 1);
                if (end - start < 10 || S16(_data, glyf + start) >= 0) continue;
                var refs = new List<int>();
                int pos = glyf + start + 10;
                while (pos + 4 <= glyf + end)
                {
                    int flags = U16(_data, pos), component = U16(_data, pos + 2);
                    refs.Add(pos + 2 - (glyf + start));
                    pos += 4;
                    pos += (flags & 0x1) != 0 ? 4 : 2;
                    if ((flags & 0x8) != 0) pos += 2;
                    else if ((flags & 0x40) != 0) pos += 4;
                    else if ((flags & 0x80) != 0) pos += 8;
                    if (component < numGlyphs && keep.Add(component)) queue.Enqueue(component);
                    if ((flags & 0x20) == 0) break;
                }
                componentRefs[g] = refs;
            }

            var order = keep.ToList();
            var newId = new Dictionary<int, int>();
            for (int i = 0; i < order.Count; i++) newId[order[i]] = i;
            int count = order.Count;

            var glyfOut = new System.IO.MemoryStream();
            var locaOut = new byte[4 * (count + 1)];
            var hmtxOut = new byte[4 * count];
            for (int i = 0; i < count; i++)
            {
                int g = order[i];
                WU32(locaOut, 4 * i, (uint)glyfOut.Length);
                int adv = U16(_data, hmtx + 4 * Math.Min(g, numberOfHMetrics - 1));
                int lsb = g < numberOfHMetrics ? U16(_data, hmtx + 4 * g + 2) : U16(_data, hmtx + 4 * numberOfHMetrics + 2 * (g - numberOfHMetrics));
                WU16(hmtxOut, 4 * i, adv);
                WU16(hmtxOut, 4 * i + 2, lsb);
                int start = offsetOf(g), end = offsetOf(g + 1);
                if (end <= start) continue;
                var bytes = new byte[end - start];
                Buffer.BlockCopy(_data, glyf + start, bytes, 0, bytes.Length);
                List<int> refs;
                if (componentRefs.TryGetValue(g, out refs))
                    foreach (var r in refs)
                    {
                        int old = U16(bytes, r), mapped;
                        WU16(bytes, r, newId.TryGetValue(old, out mapped) ? mapped : 0);
                    }
                glyfOut.Write(bytes, 0, bytes.Length);
                while (glyfOut.Length % 4 != 0) glyfOut.WriteByte(0);
            }
            WU32(locaOut, 4 * count, (uint)glyfOut.Length);

            var map = new SortedDictionary<int, int>();
            foreach (var kv in chars) map[kv.Key] = newId[kv.Value];

            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var tag in new[] { "head", "hhea", "maxp", "name", "OS/2", "cvt ", "fpgm", "prep", "gasp" })
                if (_tables.ContainsKey(tag)) tables[tag] = Slice(tag);
            WU16(tables["head"], 50, 1);        // indexToLocFormat: long
            WU16(tables["hhea"], 34, count);    // numberOfHMetrics
            WU16(tables["maxp"], 4, count);     // numGlyphs
            tables["hmtx"] = hmtxOut;
            tables["loca"] = locaOut;
            tables["glyf"] = glyfOut.ToArray();
            tables["cmap"] = BuildCmap(map);
            var post = new byte[32];
            if (_tables.ContainsKey("post")) Buffer.BlockCopy(_data, Table("post"), post, 0, Math.Min(32, _tables["post"].Value));
            WU32(post, 0, 0x00030000); // no glyph names
            tables["post"] = post;
            return BuildFont(tables);
        }

        private byte[] Slice(string tag)
        {
            var t = _tables[tag];
            var b = new byte[t.Value];
            Buffer.BlockCopy(_data, t.Key, b, 0, t.Value);
            return b;
        }

        /// <summary>A cmap with a (3,1) format 4 subtable and, if needed, a (3,10) format 12 subtable.</summary>
        private static byte[] BuildCmap(SortedDictionary<int, int> map)
        {
            var bmp = map.Where(kv => kv.Key < 0xFFFF).ToList();
            // segments of consecutive code points
            var segments = new List<KeyValuePair<int, int>>(); // start index, count
            for (int i = 0; i < bmp.Count;)
            {
                int j = i + 1;
                while (j < bmp.Count && bmp[j].Key == bmp[j - 1].Key + 1) j++;
                segments.Add(new KeyValuePair<int, int>(i, j - i));
                i = j;
            }
            int segCount = segments.Count + 1;
            int glyphIds = bmp.Count;
            int len4 = 16 + 8 * segCount + 2 * glyphIds;
            var f4 = new byte[len4];
            WU16(f4, 0, 4);
            WU16(f4, 2, len4);
            WU16(f4, 6, segCount * 2);
            int es = 0;
            while ((1 << (es + 1)) <= segCount) es++;
            WU16(f4, 8, 2 * (1 << es));
            WU16(f4, 10, es);
            WU16(f4, 12, 2 * segCount - 2 * (1 << es));
            int endOff = 14, startOff = 16 + 2 * segCount, deltaOff = startOff + 2 * segCount, rangeOff = deltaOff + 2 * segCount, glyphOff = rangeOff + 2 * segCount;
            int glyphIndex = 0;
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                WU16(f4, endOff + 2 * s, bmp[seg.Key + seg.Value - 1].Key);
                WU16(f4, startOff + 2 * s, bmp[seg.Key].Key);
                WU16(f4, deltaOff + 2 * s, 0);
                WU16(f4, rangeOff + 2 * s, 2 * (segCount - s + glyphIndex));
                for (int k = 0; k < seg.Value; k++) WU16(f4, glyphOff + 2 * (glyphIndex + k), bmp[seg.Key + k].Value);
                glyphIndex += seg.Value;
            }
            int last = segments.Count;
            WU16(f4, endOff + 2 * last, 0xFFFF);
            WU16(f4, startOff + 2 * last, 0xFFFF);
            WU16(f4, deltaOff + 2 * last, 1);
            WU16(f4, rangeOff + 2 * last, 0);

            var supplementary = map.Where(kv => kv.Key > 0xFFFF).ToList();
            byte[] f12 = null;
            if (supplementary.Count > 0)
            {
                var all = map.ToList();
                f12 = new byte[16 + 12 * all.Count];
                WU16(f12, 0, 12);
                WU32(f12, 4, (uint)f12.Length);
                WU32(f12, 12, (uint)all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    WU32(f12, 16 + 12 * i, (uint)all[i].Key);
                    WU32(f12, 20 + 12 * i, (uint)all[i].Key);
                    WU32(f12, 24 + 12 * i, (uint)all[i].Value);
                }
            }
            int numTables = f12 == null ? 1 : 2;
            int header = 4 + 8 * numTables;
            var cmap = new byte[header + f4.Length + (f12 == null ? 0 : f12.Length)];
            WU16(cmap, 2, numTables);
            WU16(cmap, 4, 3); WU16(cmap, 6, 1); WU32(cmap, 8, (uint)header);
            Buffer.BlockCopy(f4, 0, cmap, header, f4.Length);
            if (f12 != null)
            {
                WU16(cmap, 12, 3); WU16(cmap, 14, 10); WU32(cmap, 16, (uint)(header + f4.Length));
                Buffer.BlockCopy(f12, 0, cmap, header + f4.Length, f12.Length);
            }
            return cmap;
        }

        private static byte[] BuildFont(SortedDictionary<string, byte[]> tables)
        {
            int numTables = tables.Count;
            int headerSize = 12 + 16 * numTables;
            int total = headerSize + tables.Values.Sum(t => (t.Length + 3) & ~3);
            var output = new byte[total];
            int entrySelector = 0;
            while ((1 << (entrySelector + 1)) <= numTables) entrySelector++;
            int searchRange = (1 << entrySelector) * 16;
            WU32(output, 0, 0x00010000);
            WU16(output, 4, numTables);
            WU16(output, 6, searchRange);
            WU16(output, 8, entrySelector);
            WU16(output, 10, numTables * 16 - searchRange);
            int pos = headerSize, i = 0, headPos = -1;
            foreach (var kv in tables)
            {
                var t = kv.Value;
                if (kv.Key == "head") { WU32(t, 8, 0); headPos = pos; }
                int rec = 12 + 16 * i++;
                for (int k = 0; k < 4; k++) output[rec + k] = (byte)kv.Key[k];
                Buffer.BlockCopy(t, 0, output, pos, t.Length);
                WU32(output, rec + 4, Checksum(output, pos, t.Length));
                WU32(output, rec + 8, (uint)pos);
                WU32(output, rec + 12, (uint)t.Length);
                pos += (t.Length + 3) & ~3;
            }
            if (headPos >= 0) WU32(output, headPos + 8, 0xB1B0AFBAu - Checksum(output, 0, output.Length));
            return output;
        }

        private static uint Checksum(byte[] b, int offset, int length)
        {
            uint sum = 0;
            int end = offset + ((length + 3) & ~3);
            for (int i = offset; i < end; i += 4)
            {
                uint v = 0;
                for (int k = 0; k < 4; k++) v = (v << 8) | (i + k < b.Length && i + k < offset + length ? b[i + k] : (byte)0);
                sum += v;
            }
            return sum;
        }

        private static string Tag(byte[] b, int o) { return Encoding.ASCII.GetString(b, o, 4); }
        private static int U16(byte[] b, int o) { return (b[o] << 8) | b[o + 1]; }
        private static int S16(byte[] b, int o) { return (short)((b[o] << 8) | b[o + 1]); }
        private static uint U32(byte[] b, int o) { return (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]); }
        private static void WU16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
        private static void WU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    }
}
