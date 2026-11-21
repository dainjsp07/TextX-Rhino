using System;
using System.IO;
using System.IO.Compression;

namespace TextX.Core.Documents.Imaging
{
    /// <summary>Minimal PNG encoder (8-bit RGB or grey), so page images need no imaging library.</summary>
    public static class PngWriter
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Encodes BGRA pixels (top row first) as an RGB PNG.</summary>
        public static byte[] FromBgra(byte[] bgra, int width, int height)
        {
            var raw = new byte[(width * 3 + 1) * height];
            for (int y = 0, o = 0; y < height; y++)
            {
                raw[o++] = 0; // filter: none
                int p = y * width * 4;
                for (int x = 0; x < width; x++, p += 4)
                {
                    raw[o++] = bgra[p + 2];
                    raw[o++] = bgra[p + 1];
                    raw[o++] = bgra[p];
                }
            }
            return Encode(raw, width, height, 2);
        }

        /// <summary>Encodes 8-bit grey pixels (top row first).</summary>
        public static byte[] FromGray(byte[] gray, int width, int height)
        {
            var raw = new byte[(width + 1) * height];
            for (int y = 0; y < height; y++)
            {
                raw[y * (width + 1)] = 0;
                Buffer.BlockCopy(gray, y * width, raw, y * (width + 1) + 1, width);
            }
            return Encode(raw, width, height, 0);
        }

        private static byte[] Encode(byte[] raw, int width, int height, byte colorType)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                WriteBE(ihdr, 0, (uint)width);
                WriteBE(ihdr, 4, (uint)height);
                ihdr[8] = 8;          // bit depth
                ihdr[9] = colorType;  // 0 grey, 2 RGB
                WriteChunk(ms, "IHDR", ihdr);
                WriteChunk(ms, "IDAT", Zlib(raw));
                WriteChunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        private static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (var d in data)
                {
                    a = (a + d) % 65521;
                    b = (b + a) % 65521;
                }
                var adler = new byte[4];
                WriteBE(adler, 0, (b << 16) | a);
                ms.Write(adler, 0, 4);
                return ms.ToArray();
            }
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBE(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var typeBytes = new byte[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFF;
            foreach (var b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            var c = new byte[4];
            WriteBE(c, 0, crc ^ 0xFFFFFFFF);
            s.Write(c, 0, 4);
        }

        private static void WriteBE(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }
    }
}
