using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TextX.Core.Documents.Fonts
{
    public enum FontKind { Sans = 0, Serif = 1, Mono = 2 }

    /// <summary>A loaded font face that can be measured and embedded.</summary>
    public sealed class FontFace
    {
        private readonly Lazy<byte[]> _embed;

        internal FontFace(string key, TrueTypeFont font)
        {
            Key = key;
            Font = font;
            _embed = new Lazy<byte[]>(() => font.ToStandaloneFont());
        }

        public string Key { get; private set; }
        public TrueTypeFont Font { get; private set; }

        /// <summary>The font file to embed (a standalone .ttf, also for faces of a .ttc).</summary>
        public byte[] EmbedData { get { return _embed.Value; } }

        public bool Has(int cp) { return Font.HasChar(cp); }
        public override string ToString() { return Key; }
    }

    /// <summary>A piece of a line drawn with one font.</summary>
    public sealed class TextRun
    {
        public string Text;
        public FontFace Face;
        public float Width;
    }

    /// <summary>
    /// Finds fonts for translated text: a sans, serif or monospaced face matching the original
    /// (with bold / italic), plus fallbacks for scripts the main face lacks (CJK, Thai, Indic ...).
    /// Uses the fonts installed with Windows; any folder of .ttf/.ttc files works for testing.
    /// </summary>
    public sealed class FontCatalog
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FontFace> _faces = new Dictionary<string, FontFace>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        // [regular, bold, italic, bold italic]; the first file that exists wins
        private static readonly string[][] Sans =
        {
            new[] { "arial.ttf", "segoeui.ttf", "LiberationSans-Regular.ttf", "DejaVuSans.ttf" },
            new[] { "arialbd.ttf", "segoeuib.ttf", "LiberationSans-Bold.ttf", "DejaVuSans-Bold.ttf" },
            new[] { "ariali.ttf", "segoeuii.ttf", "LiberationSans-Italic.ttf", "DejaVuSans-Oblique.ttf" },
            new[] { "arialbi.ttf", "segoeuiz.ttf", "LiberationSans-BoldItalic.ttf", "DejaVuSans-BoldOblique.ttf" },
        };

        private static readonly string[][] Serif =
        {
            new[] { "times.ttf", "georgia.ttf", "LiberationSerif-Regular.ttf", "DejaVuSerif.ttf" },
            new[] { "timesbd.ttf", "georgiab.ttf", "LiberationSerif-Bold.ttf", "DejaVuSerif-Bold.ttf" },
            new[] { "timesi.ttf", "georgiai.ttf", "LiberationSerif-Italic.ttf", "DejaVuSerif-Italic.ttf" },
            new[] { "timesbi.ttf", "georgiaz.ttf", "LiberationSerif-BoldItalic.ttf", "DejaVuSerif-BoldItalic.ttf" },
        };

        private static readonly string[][] Mono =
        {
            new[] { "cour.ttf", "consola.ttf", "LiberationMono-Regular.ttf", "DejaVuSansMono.ttf" },
            new[] { "courbd.ttf", "consolab.ttf", "LiberationMono-Bold.ttf", "DejaVuSansMono-Bold.ttf" },
            new[] { "couri.ttf", "consolai.ttf", "LiberationMono-Italic.ttf", "DejaVuSansMono-Oblique.ttf" },
            new[] { "courbi.ttf", "consolaz.ttf", "LiberationMono-BoldItalic.ttf", "DejaVuSansMono-BoldOblique.ttf" },
        };

        // fonts for scripts the Latin faces do not cover; "file" or "file#face"
        private static readonly Dictionary<string, string[]> ByLanguage = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "zh-CN", new[] { "msyh.ttc", "simsun.ttc", "simhei.ttf", "wqy-zenhei.ttc" } },
            { "zh-TW", new[] { "msjh.ttc", "mingliu.ttc", "wqy-zenhei.ttc" } },
            { "ja", new[] { "YuGothM.ttc", "YuGothR.ttc", "meiryo.ttc", "msgothic.ttc", "ipag.ttf", "fonts-japanese-gothic.ttf" } },
            { "ko", new[] { "malgun.ttf", "gulim.ttc", "batang.ttc" } },
            { "th", new[] { "leelawui.ttf", "tahoma.ttf", "LeelawUI.ttf" } },
            { "hi", new[] { "Nirmala.ttf", "nirmala.ttc", "mangal.ttf" } },
            { "ka", new[] { "sylfaen.ttf", "segoeui.ttf" } },
            { "hy", new[] { "sylfaen.ttf", "segoeui.ttf" } },
        };

        private static readonly string[] Fallbacks =
        {
            "segoeui.ttf", "arial.ttf", "seguisym.ttf", "msyh.ttc", "malgun.ttf", "YuGothM.ttc", "meiryo.ttc", "Nirmala.ttf",
            "sylfaen.ttf", "leelawui.ttf", "tahoma.ttf", "ebrima.ttf", "gadugi.ttf", "javatext.ttf", "mvboli.ttf", "seguiemj.ttf",
            "DejaVuSans.ttf", "FreeSans.ttf", "wqy-zenhei.ttc", "ipag.ttf",
        };

        public FontCatalog(params string[] folders)
        {
            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".ttf" && ext != ".ttc") continue;
                    var name = Path.GetFileName(f);
                    if (!_files.ContainsKey(name)) _files[name] = f;
                }
            }
        }

        /// <summary>The fonts installed with Windows (and the user's own font folder).</summary>
        public static FontCatalog System()
        {
            var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
            return new FontCatalog(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), user);
        }

        public int FileCount { get { return _files.Count; } }

        /// <summary>Loads "file.ttf" or "file.ttc#index"; null when missing or unusable.</summary>
        public FontFace Get(string spec)
        {
            lock (_lock)
            {
                FontFace face;
                if (_faces.TryGetValue(spec, out face)) return face;
                string file = spec;
                int index = 0;
                int hash = spec.IndexOf('#');
                if (hash > 0) { file = spec.Substring(0, hash); int.TryParse(spec.Substring(hash + 1), out index); }
                string path;
                face = null;
                if (_files.TryGetValue(file, out path))
                {
                    try
                    {
                        var ttf = TrueTypeFont.Load(path, index);
                        if (!ttf.IsCff) face = new FontFace(spec, ttf); // CFF outlines cannot be embedded as TrueType
                    }
                    catch
                    {
                        face = null;
                    }
                }
                _faces[spec] = face;
                return face;
            }
        }

        public static FontKind KindOf(string pdfFontName)
        {
            var n = (pdfFontName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("courier") || n.Contains("mono") || n.Contains("consol") || n.Contains("typewriter")) return FontKind.Mono;
            foreach (var s in new[] { "times", "serif", "roman", "georgia", "garamond", "cambria", "book", "minion", "palatino", "baskerville", "century", "bodoni", "caslon", "mincho", "song", "batang", "ming" })
                if (n.Contains(s) && !n.Contains("sans")) return FontKind.Serif;
            return FontKind.Sans;
        }

        /// <summary>The main face for text in <paramref name="targetLang"/> drawn in the given style.</summary>
        public FontFace Primary(FontKind kind, bool bold, bool italic, string targetLang)
        {
            string[] script;
            if (!string.IsNullOrEmpty(targetLang) && ByLanguage.TryGetValue(targetLang, out script))
            {
                // CJK, Thai, Indic ...: their own faces (bold variants where Windows has them)
                if (bold)
                {
                    var b = FirstAvailable(script.Select(BoldVariant));
                    if (b != null) return b;
                }
                var s = FirstAvailable(script);
                if (s != null) return s;
            }
            var table = kind == FontKind.Serif ? Serif : kind == FontKind.Mono ? Mono : Sans;
            int style = (bold ? 1 : 0) + (italic ? 2 : 0);
            return FirstAvailable(table[style]) ?? FirstAvailable(table[bold ? 1 : 0]) ?? FirstAvailable(table[0]) ??
                   FirstAvailable(Sans[0]) ?? FirstAvailable(Fallbacks);
        }

        private static string BoldVariant(string file)
        {
            switch (file.ToLowerInvariant())
            {
                case "msyh.ttc": return "msyhbd.ttc";
                case "msjh.ttc": return "msjhbd.ttc";
                case "malgun.ttf": return "malgunbd.ttf";
                case "yugothm.ttc": return "YuGothB.ttc";
                case "leelawui.ttf": return "leelauib.ttf";
                case "nirmala.ttf": return "NirmalaB.ttf";
                case "tahoma.ttf": return "tahomabd.ttf";
                default: return file;
            }
        }

        private FontFace FirstAvailable(IEnumerable<string> specs)
        {
            foreach (var s in specs)
            {
                var f = Get(s);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Faces tried, in order, for characters the primary face lacks.</summary>
        public IList<FontFace> FallbackFaces(string targetLang)
        {
            var list = new List<FontFace>();
            string[] script;
            if (!string.IsNullOrEmpty(targetLang) && ByLanguage.TryGetValue(targetLang, out script))
                foreach (var s in script) { var f = Get(s); if (f != null && !list.Contains(f)) list.Add(f); }
            foreach (var s in Fallbacks) { var f = Get(s); if (f != null && !list.Contains(f)) list.Add(f); }
            return list;
        }

        /// <summary>Splits text into runs that each use one face able to draw all of its characters.</summary>
        public static List<TextRun> Runs(string text, FontFace primary, IList<FontFace> fallbacks, float size)
        {
            var runs = new List<TextRun>();
            if (string.IsNullOrEmpty(text)) return runs;
            FontFace current = null;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                int len = 1;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) { cp = char.ConvertToUtf32(text[i], text[i + 1]); len = 2; }
                FontFace face;
                if (cp == ' ' || cp == 0xA0) face = current ?? primary;           // spaces stay with the run
                else if (primary.Has(cp)) face = primary;
                else face = fallbacks.FirstOrDefault(f => f.Has(cp)) ?? primary;   // missing everywhere: primary's .notdef
                if (face != current && sb.Length > 0)
                {
                    runs.Add(new TextRun { Text = sb.ToString(), Face = current });
                    sb.Clear();
                }
                current = face;
                sb.Append(text, i, len);
                i += len - 1;
            }
            if (sb.Length > 0) runs.Add(new TextRun { Text = sb.ToString(), Face = current });
            foreach (var r in runs) r.Width = r.Face.Font.Measure(r.Text, size);
            return runs;
        }

        /// <summary>Width of text drawn with the primary face and its fallbacks.</summary>
        public static float Measure(string text, FontFace primary, IList<FontFace> fallbacks, float size)
        {
            float w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) { cp = char.ConvertToUtf32(text[i], text[i + 1]); i++; }
                var face = primary.Has(cp) || cp == ' ' ? primary : (fallbacks.FirstOrDefault(f => f.Has(cp)) ?? primary);
                w += face.Font.AdvanceUnits(cp) * size / face.Font.UnitsPerEm;
            }
            return w;
        }
    }
}
