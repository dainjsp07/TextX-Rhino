using System;
using System.Collections.Generic;
using System.IO;
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
