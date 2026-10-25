using System.Collections.Generic;

namespace TextX.Core.Text
{
    /// <summary>
    /// Contextual shaping for Arabic-script text (Arabic, Persian, Urdu, ...).
    /// Rhino's text engine draws one glyph per code point, so connected letter forms
    /// must be supplied explicitly as Arabic Presentation Forms (U+FB50..U+FEFF).
    /// Hebrew does not need shaping.
    /// </summary>
    public static class ArabicShaper
    {
        private const int JtU = 0, JtR = 1, JtL = 2, JtD = 3, JtC = 4, JtT = 5;
        private const int Lam = 0x0644;

        private static readonly Dictionary<int, int[]> Forms = new Dictionary<int, int[]>();
        private static readonly Dictionary<int, int[]> LamAlef = new Dictionary<int, int[]>();

        static ArabicShaper()
        {
            var f = UnicodeTables.ArabicForms;
            for (int i = 0; i + 4 < f.Length; i += 5)
                Forms[f[i]] = new[] { f[i + 1], f[i + 2], f[i + 3], f[i + 4] };
            var l = UnicodeTables.LamAlef;
            for (int i = 0; i + 2 < l.Length; i += 3)
                LamAlef[l[i]] = new[] { l[i + 1], l[i + 2] };
        }

        /// <summary>True when the code point is in an Arabic-script block that may need shaping.</summary>
        public static bool IsArabicScript(int cp)
        {
            return (cp >= 0x0600 && cp <= 0x06FF) || (cp >= 0x0750 && cp <= 0x077F) ||
                   (cp >= 0x0870 && cp <= 0x08FF);
        }

        public static bool NeedsShaping(int[] cps)
        {
            foreach (var c in cps) if (IsArabicScript(c)) return true;
            return false;
        }

        private static int JoiningType(int cp)
        {
            if (cp == 0x200D) return JtC; // ZWJ
            if (cp == 0x200C) return JtU; // ZWNJ
            return UnicodeProps.GetJoiningType(cp);
        }

        private static bool CanJoinToPrevious(int jt) { return jt == JtD || jt == JtR || jt == JtC; }
        private static bool CanJoinToNext(int jt) { return jt == JtD || jt == JtL || jt == JtC; }

        /// <summary>
        /// Shapes a logical-order code point sequence. Returns a new sequence (LAM-ALEF ligatures
        /// shorten it). Optionally removes ZWJ/ZWNJ after shaping.
        /// </summary>
        public static int[] Shape(int[] cps, bool removeJoinControls = true)
        {
            int n = cps.Length;
            var jt = new int[n];
            for (int i = 0; i < n; i++) jt[i] = JoiningType(cps[i]);

            var output = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                int cp = cps[i];
                int t = jt[i];
                if (t == JtT || t == JtU || !IsArabicScript(cp) && t != JtC)
                {
                    if (removeJoinControls && (cp == 0x200D || cp == 0x200C)) continue;
                    output.Add(cp);
                    continue;
                }

                // previous non-transparent
                int p = i - 1;
                while (p >= 0 && jt[p] == JtT) p--;
                bool joinPrev = p >= 0 && CanJoinToPrevious(t) && CanJoinToNext(jt[p]);

                // LAM + ALEF mandatory ligature
                if (cp == Lam)
                {
                    int q = i + 1;
                    while (q < n && jt[q] == JtT) q++;
                    int[] lig;
                    if (q < n && LamAlef.TryGetValue(cps[q], out lig))
                    {
                        int form = joinPrev ? lig[1] : lig[0];
                        if (form != 0)
                        {
                            output.Add(form);
                            // keep any transparent marks that were between lam and alef
                            for (int k = i + 1; k < q; k++) output.Add(cps[k]);
                            i = q;
                            continue;
                        }
                    }
                }

                int nx = i + 1;
                while (nx < n && jt[nx] == JtT) nx++;
                bool joinNext = nx < n && CanJoinToNext(t) && CanJoinToPrevious(jt[nx]);

                if (t == JtC)
                {
                    if (removeJoinControls && cp == 0x200D) continue;
                    output.Add(cp);
                    continue;
                }

                int[] forms;
                if (!Forms.TryGetValue(cp, out forms))
                {
                    output.Add(cp);
                    continue;
                }

                int shaped = 0;
                if (joinPrev && joinNext) shaped = forms[3];          // medial
                if (shaped == 0 && joinPrev && !joinNext) shaped = forms[1]; // final
                if (shaped == 0 && !joinPrev && joinNext) shaped = forms[2]; // initial
                if (shaped == 0 && joinPrev && joinNext) shaped = forms[1];  // fallback final
                if (shaped == 0) shaped = forms[0];                    // isolated
                output.Add(shaped != 0 ? shaped : cp);
            }
            return output.ToArray();
        }
    }
}
