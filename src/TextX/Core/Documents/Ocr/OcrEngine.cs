using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Documents.Native;
using TextX.Core.Text;
using T = TextX.Core.Documents.Native.TesseractNative;

namespace TextX.Core.Documents.Ocr
{
    /// <summary>One recognised word, in pixels of the image it was read from (origin top-left).</summary>
    public sealed class OcrWord
    {
        public string Text;
        public int Left, Top, Right, Bottom;
        public float Confidence; // 0-100
        public int Block, Paragraph, Line;
        public override string ToString() { return Text; }
    }

    public sealed class OcrResult
    {
        public List<OcrWord> Words = new List<OcrWord>();
        public int ImageWidth, ImageHeight, Dpi;
        public string Languages;
        public float MeanConfidence
        {
            get { return Words.Count == 0 ? 0 : Words.Average(w => w.Confidence); }
        }
    }

    public sealed class ScriptDetection
    {
        public string Script;          // Tesseract script name: Latin, Cyrillic, Han, Hebrew, Arabic ...
        public float ScriptConfidence;
        public int Orientation;        // degrees the page is rotated (0, 90, 180, 270)
        public float OrientationConfidence;
    }

    /// <summary>
    /// Tesseract OCR. Language data (".traineddata") is downloaded on first use into
    /// <see cref="DataDirectory"/> from the official tessdata_fast repository on GitHub.
    /// </summary>
    public static class OcrEngine
    {
        public static readonly object Sync = new object();
        private static IntPtr _api;
        private static string _apiLanguages;

        public const string DataBaseUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/";

        /// <summary>Folder holding the .traineddata files.</summary>
        public static string DataDirectory { get; set; }

        public static bool IsAvailable { get { return NativeLibraries.TesseractAvailable; } }

        public static string Version
        {
            get
            {
                NativeLibraries.EnsureTesseract();
                return T.FromUtf8(T.TessVersion());
            }
        }

        /// <summary>The .traineddata files a language string ("eng+heb", "script/Latin") needs that are not on disk.</summary>
        public static List<string> MissingData(string languages)
        {
            var missing = new List<string>();
            foreach (var lang in (languages ?? "eng").Split('+'))
            {
                var file = Path.Combine(DataDirectory ?? string.Empty, lang.Replace('/', Path.DirectorySeparatorChar) + ".traineddata");
                if (!File.Exists(file)) missing.Add(lang);
            }
            return missing;
        }

        /// <summary>Downloads missing language data. <paramref name="progress"/> receives the file being fetched.</summary>
        public static async Task EnsureDataAsync(string languages, IProgress<string> progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(DataDirectory)) throw new InvalidOperationException("No OCR data folder set.");
            foreach (var lang in MissingData(languages))
            {
                ct.ThrowIfCancellationRequested();
                if (progress != null) progress.Report(lang);
                var target = Path.Combine(DataDirectory, lang.Replace('/', Path.DirectorySeparatorChar) + ".traineddata");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                var temp = target + ".download";
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                using (var response = await client.GetAsync(DataBaseUrl + lang + ".traineddata", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new IOException("OCR language data '" + lang + "' could not be downloaded (" + (int)response.StatusCode + ").");
                    using (var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write))
                        await src.CopyToAsync(dst, 81920, ct).ConfigureAwait(false);
                }
                if (new FileInfo(temp).Length < 10000) { File.Delete(temp); throw new IOException("OCR language data '" + lang + "' is incomplete."); }
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
            }
        }

        /// <summary>Finds the writing system and orientation of a page image (needs osd.traineddata).</summary>
        public static ScriptDetection DetectScript(byte[] gray, int width, int height, int dpi)
        {
            lock (Sync)
            {
                NativeLibraries.EnsureTesseract();
                var api = T.TessBaseAPICreate();
                try
                {
                    if (T.TessBaseAPIInit2(api, T.Utf8Z(DataDirectory), T.Utf8Z("osd"), T.OemTesseractOnly) != 0)
                        throw new InvalidOperationException("OCR script detection could not start (osd.traineddata missing?).");
                    T.TessBaseAPISetPageSegMode(api, T.PsmOsdOnly);
                    T.TessBaseAPISetVariable(api, T.Utf8Z("user_defined_dpi"), T.Utf8Z(dpi.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    T.TessBaseAPISetImage(api, gray, width, height, 1, width);
                    T.TessBaseAPISetSourceResolution(api, dpi);
                    int deg; float oconf, sconf; IntPtr name;
                    if (T.TessBaseAPIDetectOrientationScript(api, out deg, out oconf, out name, out sconf) == 0) return null;
                    return new ScriptDetection { Script = T.FromUtf8(name), ScriptConfidence = sconf, Orientation = deg, OrientationConfidence = oconf };
                }
                finally
                {
                    T.TessBaseAPIEnd(api);
                    T.TessBaseAPIDelete(api);
                }
            }
        }

        /// <summary>
        /// Recognises text in an 8-bit grey image. <paramref name="languages"/> is a Tesseract language
        /// string such as "eng", "heb+eng" or "script/Latin". Sparse mode suits drawings (text anywhere).
        /// </summary>
        public static OcrResult Recognize(byte[] gray, int width, int height, int dpi, string languages, bool sparse = false)
        {
            return Recognize(gray, width, height, dpi, languages, sparse ? T.PsmSparseText : T.PsmAuto);
        }

        /// <summary>Recognises with a given page segmentation mode (7 = one line of text).</summary>
        public static OcrResult Recognize(byte[] gray, int width, int height, int dpi, string languages, int pageSegMode)
        {
            lock (Sync)
            {
                NativeLibraries.EnsureTesseract();
                var missing = MissingData(languages);
                if (missing.Count > 0) throw new FileNotFoundException("OCR language data missing: " + string.Join(", ", missing));
                if (_api == IntPtr.Zero || _apiLanguages != languages)
                {
                    if (_api != IntPtr.Zero) { T.TessBaseAPIEnd(_api); T.TessBaseAPIDelete(_api); _api = IntPtr.Zero; }
                    var api = T.TessBaseAPICreate();
                    if (T.TessBaseAPIInit2(api, T.Utf8Z(DataDirectory), T.Utf8Z(languages), T.OemLstmOnly) != 0)
                    {
                        T.TessBaseAPIDelete(api);
                        throw new InvalidOperationException("The OCR engine could not load '" + languages + "'.");
                    }
                    T.TessBaseAPISetVariable(api, T.Utf8Z("preserve_interword_spaces"), T.Utf8Z("1"));
                    _api = api;
                    _apiLanguages = languages;
                }
                T.TessBaseAPISetPageSegMode(_api, pageSegMode);
                T.TessBaseAPISetImage(_api, gray, width, height, 1, width);
                T.TessBaseAPISetSourceResolution(_api, dpi);
                var result = new OcrResult { ImageWidth = width, ImageHeight = height, Dpi = dpi, Languages = languages };
                if (T.TessBaseAPIRecognize(_api, IntPtr.Zero) != 0) throw new InvalidOperationException("OCR failed on this image.");
                var it = T.TessBaseAPIGetIterator(_api);
                if (it == IntPtr.Zero) return result;
                try
                {
                    var page = T.TessResultIteratorGetPageIterator(it);
                    int block = -1, para = -1, line = -1;
                    do
                    {
                        if (T.TessPageIteratorIsAtBeginningOf(page, T.LevelBlock) != 0) block++;
                        if (T.TessPageIteratorIsAtBeginningOf(page, T.LevelPara) != 0) para++;
                        if (T.TessPageIteratorIsAtBeginningOf(page, T.LevelLine) != 0) line++;
                        var ptr = T.TessResultIteratorGetUTF8Text(it, T.LevelWord);
                        if (ptr == IntPtr.Zero) continue;
                        var text = T.FromUtf8(ptr);
                        T.TessDeleteText(ptr);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        int l, t, r, b;
                        T.TessPageIteratorBoundingBox(page, T.LevelWord, out l, out t, out r, out b);
                        result.Words.Add(new OcrWord
                        {
                            Text = text.Trim(),
                            Left = l, Top = t, Right = r, Bottom = b,
                            Confidence = T.TessResultIteratorConfidence(it, T.LevelWord),
                            Block = Math.Max(0, block), Paragraph = Math.Max(0, para), Line = Math.Max(0, line),
                        });
                    }
                    while (T.TessResultIteratorNext(it, T.LevelWord) != 0);
                }
                finally
                {
                    T.TessResultIteratorDelete(it);
                    T.TessBaseAPIClear(_api);
                }
                return result;
            }
        }

        /// <summary>
        /// On a Hebrew (or Arabic) drawing read with "heb+eng", Tesseract often takes a Hebrew word for
        /// English ("אחת" becomes "NAN") or is unsure of it, and such words were dropped, leaving Hebrew
        /// untranslated on the sheet. Each doubtful word in a right-to-left line is read again on its own,
        /// enlarged, with the right-to-left model alone; the new reading is kept when it is Hebrew and surer.
        /// Returns the number of words read again successfully.
        /// </summary>
        public static int RereadDoubtfulWords(OcrResult ocr, byte[] gray, int width, int height, int dpi, string languages)
        {
            if (ocr == null || ocr.Words.Count == 0 || string.IsNullOrEmpty(languages)) return 0;
            var rtlModel = languages.Split('+').FirstOrDefault(l => l == "heb" || l == "ara" || l == "fas" || l == "urd" || l == "yid");
            if (rtlModel == null || rtlModel == languages) return 0;
            var doubtful = new List<OcrWord>();
            foreach (var line in ocr.Words.GroupBy(w => w.Line))
            {
                var words = line.ToList();
                int rtl = words.Count(w => RtlText.ContainsRtl(w.Text));
                bool rtlLine = rtl > 0 && rtl * 3 >= words.Count;
                foreach (var w in words)
                {
                    int letters = w.Text.Count(char.IsLetter);
                    if (letters == 0) continue;
                    bool hebrew = RtlText.ContainsRtl(w.Text);
                    bool latin = !hebrew;
                    // codes and brands in capitals (MDF, ECHOSHAPE) are real English when the engine is sure
                    bool code = latin && letters >= 3 && w.Text.Where(char.IsLetter).All(char.IsUpper) && w.Confidence >= 93;
                    if ((rtlLine && latin && !code && w.Confidence < 90) || (hebrew && w.Confidence < 60)) doubtful.Add(w);
                }
            }
            if (doubtful.Count > 600) doubtful = doubtful.OrderBy(w => w.Confidence).Take(600).ToList();
            int fixedCount = 0;
            foreach (var w in doubtful)
            {
                int h = Math.Max(1, w.Bottom - w.Top), pad = Math.Max(2, (int)(h * 0.35f));
                int l = Math.Max(0, w.Left - pad), t = Math.Max(0, w.Top - pad), r = Math.Min(width, w.Right + pad), b = Math.Min(height, w.Bottom + pad);
                if (r - l < 4 || b - t < 4) continue;
                int scale = h < 40 ? 2 : 1;
                int cw = (r - l) * scale, ch = (b - t) * scale;
                var crop = new byte[cw * ch];
                for (int y = 0; y < ch; y++)
                {
                    int sy = t + y / scale;
                    for (int x = 0; x < cw; x++) crop[y * cw + x] = gray[sy * width + l + x / scale];
                }
                OcrResult again;
                try { again = Recognize(crop, cw, ch, dpi * scale, rtlModel, T.PsmSingleLine); }
                catch (Exception) { continue; }
                var read = again.Words.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
                if (read.Count == 0) continue;
                var text = string.Join(" ", read.OrderByDescending(x => x.Right).Select(x => x.Text.Trim()));
                float conf = read.Min(x => x.Confidence);
                if (!RtlText.ContainsRtl(text) || conf < 40) continue;
                if (RtlText.ContainsRtl(w.Text) && conf < w.Confidence + 5) continue;
                // a sure capital-letter word (a code like "NAN" or "MDF") turns Hebrew only when the Hebrew reading is sure too
                bool capitals = !RtlText.ContainsRtl(w.Text) && w.Text.Where(char.IsLetter).All(char.IsUpper) && w.Confidence >= 80;
                if (capitals && conf < 70) continue;
                w.Text = text;
                w.Confidence = conf;
                fixedCount++;
            }
            return fixedCount;
        }

        /// <summary>Releases the cached engine (frees the loaded language model).</summary>
        public static void Release()
        {
            lock (Sync)
            {
                if (_api == IntPtr.Zero) return;
                T.TessBaseAPIEnd(_api);
                T.TessBaseAPIDelete(_api);
                _api = IntPtr.Zero;
                _apiLanguages = null;
            }
        }
    }

    /// <summary>Maps Text X language codes and detected scripts to Tesseract language data.</summary>
    public static class OcrLanguages
    {
        private static readonly Dictionary<string, string> ByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "eng" }, { "fr", "fra" }, { "de", "deu" }, { "es", "spa" }, { "it", "ita" }, { "pt", "por" },
            { "nl", "nld" }, { "ru", "rus" }, { "uk", "ukr" }, { "pl", "pol" }, { "cs", "ces" }, { "sk", "slk" },
            { "hu", "hun" }, { "ro", "ron" }, { "bg", "bul" }, { "el", "ell" }, { "tr", "tur" }, { "sv", "swe" },
            { "da", "dan" }, { "no", "nor" }, { "fi", "fin" }, { "et", "est" }, { "lv", "lav" }, { "lt", "lit" },
            { "sl", "slv" }, { "hr", "hrv" }, { "sr", "srp" }, { "zh-CN", "chi_sim" }, { "zh-TW", "chi_tra" },
            { "ja", "jpn" }, { "ko", "kor" }, { "hi", "hin" }, { "th", "tha" }, { "vi", "vie" }, { "id", "ind" },
            { "ms", "msa" }, { "ka", "kat" }, { "hy", "hye" }, { "he", "heb" }, { "ar", "ara" }, { "fa", "fas" },
            { "ur", "urd" }, { "yi", "yid" },
        };

        private static readonly Dictionary<string, string> ByScript = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Latin", "script/Latin" }, { "Cyrillic", "script/Cyrillic" }, { "Greek", "ell" }, { "Hebrew", "heb" },
            { "Arabic", "ara" }, { "Han", "chi_sim" }, { "HanS", "chi_sim" }, { "HanT", "chi_tra" }, { "Japanese", "jpn" },
            { "Katakana", "jpn" }, { "Hiragana", "jpn" }, { "Hangul", "kor" }, { "Korean", "kor" }, { "Devanagari", "hin" },
            { "Thai", "tha" }, { "Georgian", "kat" }, { "Armenian", "hye" }, { "Bengali", "ben" }, { "Tamil", "tam" },
            { "Telugu", "tel" }, { "Kannada", "kan" }, { "Malayalam", "mal" }, { "Gujarati", "guj" }, { "Gurmukhi", "pan" },
        };

        private static readonly HashSet<string> NonLatin = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "heb", "ara", "fas", "urd", "yid", "rus", "ukr", "bul", "srp", "ell", "chi_sim", "chi_tra", "jpn", "kor", "hin", "tha",
            "kat", "hye", "ben", "tam", "tel", "kan", "mal", "guj", "pan", "script/Cyrillic",
        };

        /// <summary>True when OCR ran with Latin-script models only (or not at all).</summary>
        public static bool IsLatinOnly(string languages)
        {
            if (string.IsNullOrEmpty(languages)) return true;
            foreach (var part in languages.Split('+'))
                if (NonLatin.Contains(part.Trim())) return false;
            return true;
        }

        /// <summary>Tesseract data for a known source language; English is added for mixed drawings.</summary>
        public static string ForLanguage(string code)
        {
            string t;
            if (string.IsNullOrEmpty(code) || !ByCode.TryGetValue(code, out t)) return null;
            return t == "eng" ? "eng" : t + "+eng";
        }

        /// <summary>Tesseract data for a detected script (Latin pages use the all-Latin model).</summary>
        public static string ForScript(string script)
        {
            string t;
            if (string.IsNullOrEmpty(script) || !ByScript.TryGetValue(script, out t)) return "script/Latin";
            return t.StartsWith("script/", StringComparison.Ordinal) ? t : t + "+eng";
        }
    }
}
