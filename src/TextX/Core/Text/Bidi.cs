using System;
using System.Collections.Generic;

namespace TextX.Core.Text
{
    /// <summary>Unicode Bidi_Class values (order matches the generated tables).</summary>
    public enum BidiClass : byte
    {
        L, R, AL, EN, ES, ET, AN, CS, NSM, BN, B, S, WS, ON,
        LRE, LRO, RLE, RLO, PDF, LRI, RLI, FSI, PDI
    }

    /// <summary>Character property lookups used by the bidi algorithm and the Arabic shaper.</summary>
    public static class UnicodeProps
    {
        private static readonly Dictionary<int, int> Mirrors = BuildPairs(UnicodeTables.MirrorPairs);
        private static readonly Dictionary<int, int> BracketPairs = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> BracketTypes = new Dictionary<int, int>();

        static UnicodeProps()
        {
            var d = UnicodeTables.BracketData;
            for (int i = 0; i + 2 < d.Length; i += 3)
            {
                BracketPairs[d[i]] = d[i + 1];
                BracketTypes[d[i]] = d[i + 2];
            }
        }

        private static Dictionary<int, int> BuildPairs(int[] data)
        {
            var map = new Dictionary<int, int>(data.Length / 2);
            for (int i = 0; i + 1 < data.Length; i += 2) map[data[i]] = data[i + 1];
            return map;
        }

        /// <summary>Bidi_Class of a code point.</summary>
        public static BidiClass GetBidiClass(int cp)
        {
            var starts = UnicodeTables.BidiStarts;
            int lo = 0, hi = starts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (starts[mid] <= cp) lo = mid; else hi = mid - 1;
            }
            return (BidiClass)UnicodeTables.BidiValues[lo];
        }

        /// <summary>Bidi_Mirroring_Glyph of a code point, or the code point itself.</summary>
        public static int GetMirror(int cp)
        {
            int m;
            return Mirrors.TryGetValue(cp, out m) ? m : cp;
        }

        /// <summary>0 = none, 1 = open, 2 = close.</summary>
        public static int GetBracketType(int cp)
        {
            int t;
            return BracketTypes.TryGetValue(cp, out t) ? t : 0;
        }

        public static int GetPairedBracket(int cp)
        {
            int p;
            return BracketPairs.TryGetValue(cp, out p) ? p : cp;
        }

        /// <summary>Joining_Type: 0=U 1=R 2=L 3=D 4=C 5=T.</summary>
        public static int GetJoiningType(int cp)
        {
            var r = UnicodeTables.JoiningRanges;
            int lo = 0, hi = r.Length / 2 - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (r[mid * 2] <= cp) lo = mid; else hi = mid - 1;
            }
            return r[lo * 2 + 1];
        }

        public static bool IsRtlClass(BidiClass c)
        {
            return c == BidiClass.R || c == BidiClass.AL;
        }

        /// <summary>True for code points whose bidi class is R or AL (Hebrew, Arabic, Syriac, Thaana, N'Ko ...).</summary>
        public static bool IsStrongRtl(int cp)
        {
            return IsRtlClass(GetBidiClass(cp));
        }
    }

    /// <summary>
    /// Implementation of the Unicode Bidirectional Algorithm (UAX #9, Unicode 6.3+ rules
    /// including isolates and paired brackets) for a single paragraph.
    /// Works on code points (not UTF-16 code units).
    /// </summary>
    public sealed class BidiParagraph
    {
        public const int MaxDepth = 125;

        private readonly BidiClass[] _initialTypes;
        private readonly int[] _codePoints;
        private readonly BidiClass[] _resultTypes;
        private readonly byte[] _resultLevels;
        private readonly int[] _matchingPdi;
        private readonly int[] _matchingIsolateInitiator;
        private readonly int _length;
        private byte _paragraphLevel;

        /// <summary>Resolved paragraph embedding level (0 = LTR, 1 = RTL).</summary>
        public byte ParagraphLevel { get { return _paragraphLevel; } }

        /// <summary>Resolved embedding levels per code point (before line-level rule L1).</summary>
        public byte[] Levels { get { return _resultLevels; } }

        /// <param name="codePoints">Code points of the paragraph (used for paired-bracket lookup); may be null.</param>
        /// <param name="types">Bidi classes of the code points.</param>
        /// <param name="paragraphLevel">0 = LTR, 1 = RTL, -1 = auto (P2/P3).</param>
        public BidiParagraph(int[] codePoints, BidiClass[] types, int paragraphLevel)
        {
            if (types == null) throw new ArgumentNullException("types");
            _initialTypes = (BidiClass[])types.Clone();
            _codePoints = codePoints;
            _length = types.Length;
            _resultTypes = (BidiClass[])types.Clone();
            _resultLevels = new byte[_length];
            _matchingPdi = new int[_length];
            _matchingIsolateInitiator = new int[_length];

            DetermineMatchingIsolates();

            if (paragraphLevel < 0)
                _paragraphLevel = DetermineParagraphEmbeddingLevel(0, _length);
            else
                _paragraphLevel = (byte)(paragraphLevel & 1);

            for (int i = 0; i < _length; i++) _resultLevels[i] = _paragraphLevel;

            DetermineExplicitEmbeddingLevels();

            // X9: treat removed characters as BN (they are skipped when building sequences).
            for (int i = 0; i < _length; i++)
            {
                if (IsRemovedByX9(_resultTypes[i]))
                    _resultTypes[i] = BidiClass.BN;
            }

            var levelRuns = DetermineLevelRuns();
            var sequences = DetermineIsolatingRunSequences(levelRuns);
            foreach (var seq in sequences)
            {
                seq.ResolveWeakTypes();
                seq.ResolvePairedBrackets();
                seq.ResolveNeutralTypes();
                seq.ResolveImplicitLevels();
                seq.ApplyLevelsAndTypes();
            }

            AssignLevelsToCharactersRemovedByX9();
        }

        public static BidiParagraph FromCodePoints(int[] cps, int paragraphLevel)
        {
            var types = new BidiClass[cps.Length];
            for (int i = 0; i < cps.Length; i++) types[i] = UnicodeProps.GetBidiClass(cps[i]);
            return new BidiParagraph(cps, types, paragraphLevel);
        }

        private static bool IsRemovedByX9(BidiClass t)
        {
            switch (t)
            {
                case BidiClass.LRE:
                case BidiClass.RLE:
                case BidiClass.LRO:
                case BidiClass.RLO:
                case BidiClass.PDF:
                case BidiClass.BN:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the character at the index was removed by rule X9 (has no meaningful level).</summary>
        public bool IsRemoved(int index)
        {
            return IsRemovedByX9(_initialTypes[index]);
        }

        private static bool IsIsolateInitiator(BidiClass t)
        {
            return t == BidiClass.LRI || t == BidiClass.RLI || t == BidiClass.FSI;
        }

        private static BidiClass TypeForLevel(int level)
        {
            return (level & 1) == 0 ? BidiClass.L : BidiClass.R;
        }

        private void DetermineMatchingIsolates()
        {
            for (int i = 0; i < _length; i++) _matchingIsolateInitiator[i] = -1;
            for (int i = 0; i < _length; i++)
            {
                _matchingPdi[i] = -1;
                var t = _resultTypes[i];
                if (IsIsolateInitiator(t))
                {
                    int depth = 1;
                    for (int j = i + 1; j < _length; j++)
                    {
                        var u = _resultTypes[j];
                        if (IsIsolateInitiator(u))
                            depth++;
                        else if (u == BidiClass.PDI)
                        {
                            depth--;
                            if (depth == 0)
                            {
                                _matchingPdi[i] = j;
                                _matchingIsolateInitiator[j] = i;
                                break;
                            }
                        }
                    }
                    if (_matchingPdi[i] == -1) _matchingPdi[i] = _length;
                }
            }
        }

        /// <summary>P2/P3 over the range [start, end).</summary>
        private byte DetermineParagraphEmbeddingLevel(int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                var t = _resultTypes[i];
                if (t == BidiClass.L) return 0;
                if (t == BidiClass.R || t == BidiClass.AL) return 1;
                if (IsIsolateInitiator(t))
                {
                    i = _matchingPdi[i]; // skip to matching PDI (or end)
                }
            }
            return 0;
        }

        private struct StackEntry
        {
            public byte Level;
            public BidiClass Override; // ON = neutral
            public bool Isolate;
        }

        private void DetermineExplicitEmbeddingLevels()
        {
            var stack = new StackEntry[MaxDepth + 2];
            int sp = 0;
            int overflowIsolateCount = 0, overflowEmbeddingCount = 0, validIsolateCount = 0;
            stack[sp++] = new StackEntry { Level = _paragraphLevel, Override = BidiClass.ON, Isolate = false };

            for (int i = 0; i < _length; i++)
            {
                var t = _resultTypes[i];
                var top = stack[sp - 1];
                switch (t)
                {
                    case BidiClass.RLI:
                    case BidiClass.LRI:
                    case BidiClass.FSI:
                        {
                            bool isRtl = t == BidiClass.RLI;
                            if (t == BidiClass.FSI)
                                isRtl = DetermineParagraphEmbeddingLevel(i + 1, _matchingPdi[i]) == 1;

                            _resultLevels[i] = top.Level;
                            if (top.Override != BidiClass.ON) _resultTypes[i] = top.Override;

                            int newLevel = isRtl ? ((top.Level + 1) | 1) : ((top.Level + 2) & ~1);
                            if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                            {
                                validIsolateCount++;
                                stack[sp++] = new StackEntry { Level = (byte)newLevel, Override = BidiClass.ON, Isolate = true };
                            }
                            else
                            {
                                overflowIsolateCount++;
                            }
                            break;
                        }
                    case BidiClass.RLE:
                    case BidiClass.LRE:
                    case BidiClass.RLO:
                    case BidiClass.LRO:
                        {
                            bool isRtl = t == BidiClass.RLE || t == BidiClass.RLO;
                            int newLevel = isRtl ? ((top.Level + 1) | 1) : ((top.Level + 2) & ~1);
                            if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                            {
                                var ov = t == BidiClass.RLO ? BidiClass.R : (t == BidiClass.LRO ? BidiClass.L : BidiClass.ON);
                                stack[sp++] = new StackEntry { Level = (byte)newLevel, Override = ov, Isolate = false };
                                _resultLevels[i] = (byte)newLevel;
                            }
                            else
                            {
                                if (overflowIsolateCount == 0) overflowEmbeddingCount++;
                                _resultLevels[i] = top.Level;
                            }
                            break;
                        }
                    case BidiClass.PDI:
                        {
                            if (overflowIsolateCount > 0)
                            {
                                overflowIsolateCount--;
                            }
                            else if (validIsolateCount == 0)
                            {
                                // nothing
                            }
                            else
                            {
                                overflowEmbeddingCount = 0;
                                while (!stack[sp - 1].Isolate) sp--;
                                sp--;
                                validIsolateCount--;
                            }
                            var e = stack[sp - 1];
                            _resultLevels[i] = e.Level;
                            if (e.Override != BidiClass.ON) _resultTypes[i] = e.Override;
                            break;
                        }
                    case BidiClass.PDF:
                        {
                            if (overflowIsolateCount > 0)
                            {
                                // nothing
                            }
                            else if (overflowEmbeddingCount > 0)
                            {
                                overflowEmbeddingCount--;
                            }
                            else if (!stack[sp - 1].Isolate && sp >= 2)
                            {
                                sp--;
                            }
                            _resultLevels[i] = stack[sp - 1].Level;
                            break;
                        }
                    case BidiClass.B:
                        _resultLevels[i] = _paragraphLevel;
                        break;
                    default:
                        _resultLevels[i] = top.Level;
                        if (top.Override != BidiClass.ON) _resultTypes[i] = top.Override;
                        break;
                }
            }
        }

        private List<int[]> DetermineLevelRuns()
        {
            var runs = new List<int[]>();
            var current = new List<int>();
            int currentLevel = -1;
            for (int i = 0; i < _length; i++)
            {
                if (IsRemovedByX9(_initialTypes[i])) continue;
                if (_resultLevels[i] != currentLevel)
                {
                    if (current.Count > 0) runs.Add(current.ToArray());
                    current.Clear();
                    currentLevel = _resultLevels[i];
                }
                current.Add(i);
            }
            if (current.Count > 0) runs.Add(current.ToArray());
            return runs;
        }

        private List<IsolatingRunSequence> DetermineIsolatingRunSequences(List<int[]> levelRuns)
        {
            // map each character to its run
            var runForCharacter = new int[_length];
            for (int r = 0; r < levelRuns.Count; r++)
                foreach (var idx in levelRuns[r]) runForCharacter[idx] = r;

            var sequences = new List<IsolatingRunSequence>();
            var current = new List<int>();
            foreach (var run in levelRuns)
            {
                int first = run[0];
                if (_initialTypes[first] == BidiClass.PDI && _matchingIsolateInitiator[first] != -1)
                    continue; // continuation of an isolate; already appended

                current.Clear();
                var r = run;
                while (true)
                {
                    current.AddRange(r);
                    int last = r[r.Length - 1];
                    var lastType = _initialTypes[last];
                    if (IsIsolateInitiator(lastType) && _matchingPdi[last] != _length)
                    {
                        r = levelRuns[runForCharacter[_matchingPdi[last]]];
                    }
                    else break;
                }
                sequences.Add(new IsolatingRunSequence(this, current.ToArray()));
            }
            return sequences;
        }

        private void AssignLevelsToCharactersRemovedByX9()
        {
            for (int i = 0; i < _length; i++)
            {
                if (IsRemovedByX9(_initialTypes[i]))
                {
                    _resultTypes[i] = _initialTypes[i];
                    _resultLevels[i] = i == 0 ? _paragraphLevel : _resultLevels[i - 1];
                }
            }
        }

        /// <summary>
        /// Returns the line levels after rule L1 for the line [lineStart, lineEnd).
        /// </summary>
        public byte[] GetLineLevels(int lineStart, int lineEnd)
        {
            int n = lineEnd - lineStart;
            var levels = new byte[n];
            Array.Copy(_resultLevels, lineStart, levels, 0, n);

            // L1
            for (int i = 0; i < n; i++)
            {
                var t = _initialTypes[lineStart + i];
                if (t == BidiClass.B || t == BidiClass.S)
                {
                    levels[i] = _paragraphLevel;
                    for (int j = i - 1; j >= 0; j--)
                    {
                        if (IsWhitespaceForL1(_initialTypes[lineStart + j])) levels[j] = _paragraphLevel;
                        else break;
                    }
                }
            }
            for (int j = n - 1; j >= 0; j--)
            {
                if (IsWhitespaceForL1(_initialTypes[lineStart + j])) levels[j] = _paragraphLevel;
                else break;
            }
            return levels;
        }

        private static bool IsWhitespaceForL1(BidiClass t)
        {
            switch (t)
            {
                case BidiClass.WS:
                case BidiClass.LRI:
                case BidiClass.RLI:
                case BidiClass.FSI:
                case BidiClass.PDI:
                case BidiClass.LRE:
                case BidiClass.RLE:
                case BidiClass.LRO:
                case BidiClass.RLO:
                case BidiClass.PDF:
                case BidiClass.BN:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Rule L2: returns logical indices in visual (left-to-right) order for the line.
        /// Characters removed by X9 are included unless <paramref name="excludeRemoved"/> is set.
        /// </summary>
        public int[] GetVisualOrder(int lineStart, int lineEnd, bool excludeRemoved)
        {
            var levels = GetLineLevels(lineStart, lineEnd);
            int n = levels.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = lineStart + i;

            int highest = 0, lowestOdd = MaxDepth + 2;
            for (int i = 0; i < n; i++)
            {
                if (excludeRemoved && IsRemovedByX9(_initialTypes[lineStart + i])) continue;
                int l = levels[i];
                if (l > highest) highest = l;
                if ((l & 1) != 0 && l < lowestOdd) lowestOdd = l;
            }

            var lv = (byte[])levels.Clone();
            for (int level = highest; level >= lowestOdd; level--)
            {
                for (int i = 0; i < n; i++)
                {
                    if (lv[i] >= level)
                    {
                        int start = i;
                        int limit = i + 1;
                        while (limit < n && lv[limit] >= level) limit++;
                        Array.Reverse(order, start, limit - start);
                        Array.Reverse(lv, start, limit - start);
                        i = limit;
                    }
                }
            }

            if (!excludeRemoved) return order;
            var result = new List<int>(n);
            foreach (var idx in order)
                if (!IsRemovedByX9(_initialTypes[idx])) result.Add(idx);
            return result.ToArray();
        }

        // ------------------------------------------------------------------
        private sealed class IsolatingRunSequence
        {
            private readonly BidiParagraph _p;
            private readonly int[] _indexes;
            private readonly BidiClass[] _types;
            private byte[] _resolvedLevels;
            private readonly int _length;
            private readonly byte _level;
            private readonly BidiClass _sos;
            private readonly BidiClass _eos;

            public IsolatingRunSequence(BidiParagraph p, int[] indexes)
            {
                _p = p;
                _indexes = indexes;
                _length = indexes.Length;
                _types = new BidiClass[_length];
                for (int i = 0; i < _length; i++) _types[i] = p._resultTypes[indexes[i]];

                _level = p._resultLevels[indexes[0]];

                int prevChar = indexes[0] - 1;
                while (prevChar >= 0 && IsRemovedByX9(p._initialTypes[prevChar])) prevChar--;
                int prevLevel = prevChar >= 0 ? p._resultLevels[prevChar] : p._paragraphLevel;
                _sos = TypeForLevel(Math.Max(prevLevel, _level));

                var lastType = p._initialTypes[indexes[_length - 1]];
                int succLevel;
                if (IsIsolateInitiator(lastType))
                {
                    succLevel = p._paragraphLevel;
                }
                else
                {
                    int limit = indexes[_length - 1] + 1;
                    while (limit < p._length && IsRemovedByX9(p._initialTypes[limit])) limit++;
                    succLevel = limit < p._length ? p._resultLevels[limit] : p._paragraphLevel;
                }
                _eos = TypeForLevel(Math.Max(succLevel, _level));
            }

            private int FindRunLimit(int index, int limit, Func<BidiClass, bool> valid)
            {
                while (index < limit && valid(_types[index])) index++;
                return index;
            }

            public void ResolveWeakTypes()
            {
                // W1
                for (int i = 0; i < _length; i++)
                {
                    if (_types[i] == BidiClass.NSM)
                    {
                        if (i == 0)
                        {
                            _types[i] = _sos;
                        }
                        else
                        {
                            var prev = _types[i - 1];
                            _types[i] = (IsIsolateInitiator(prev) || prev == BidiClass.PDI) ? BidiClass.ON : prev;
                        }
                    }
                }

                // W2
                for (int i = 0; i < _length; i++)
                {
                    if (_types[i] == BidiClass.EN)
                    {
                        for (int j = i - 1; j >= 0; j--)
                        {
                            var t = _types[j];
                            if (t == BidiClass.L || t == BidiClass.R || t == BidiClass.AL)
                            {
                                if (t == BidiClass.AL) _types[i] = BidiClass.AN;
                                break;
                            }
                        }
                    }
                }

                // W3
                for (int i = 0; i < _length; i++)
                    if (_types[i] == BidiClass.AL) _types[i] = BidiClass.R;

                // W4
                for (int i = 1; i < _length - 1; i++)
                {
                    if (_types[i] == BidiClass.ES || _types[i] == BidiClass.CS)
                    {
                        var prev = _types[i - 1];
                        var succ = _types[i + 1];
                        if (prev == BidiClass.EN && succ == BidiClass.EN)
                            _types[i] = BidiClass.EN;
                        else if (_types[i] == BidiClass.CS && prev == BidiClass.AN && succ == BidiClass.AN)
                            _types[i] = BidiClass.AN;
                    }
                }

                // W5
                for (int i = 0; i < _length; i++)
                {
                    if (_types[i] == BidiClass.ET)
                    {
                        int runStart = i;
                        int runLimit = FindRunLimit(runStart, _length, t => t == BidiClass.ET);
                        var t2 = runStart == 0 ? _sos : _types[runStart - 1];
                        if (t2 != BidiClass.EN) t2 = runLimit == _length ? _eos : _types[runLimit];
                        if (t2 == BidiClass.EN)
                        {
                            for (int k = runStart; k < runLimit; k++) _types[k] = BidiClass.EN;
                        }
                        i = runLimit;
                    }
                }

                // W6
                for (int i = 0; i < _length; i++)
                {
                    var t = _types[i];
                    if (t == BidiClass.ES || t == BidiClass.ET || t == BidiClass.CS)
                        _types[i] = BidiClass.ON;
                }

                // W7
                for (int i = 0; i < _length; i++)
                {
                    if (_types[i] == BidiClass.EN)
                    {
                        var prevStrong = _sos;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            var t = _types[j];
                            if (t == BidiClass.L || t == BidiClass.R)
                            {
                                prevStrong = t;
                                break;
                            }
                        }
                        if (prevStrong == BidiClass.L) _types[i] = BidiClass.L;
                    }
                }
            }

            // N0 --------------------------------------------------------------
            private struct BracketPair
            {
                public int Open;
                public int Close;
            }

            private int CanonicalBracket(int cp)
            {
                // canonical equivalents of angle brackets
                if (cp == 0x2329) return 0x3008;
                if (cp == 0x232A) return 0x3009;
                return cp;
            }

            public void ResolvePairedBrackets()
            {
                if (_p._codePoints == null) return;
                var pairs = LocateBrackets();
                if (pairs.Count == 0) return;
                var dirEmbed = TypeForLevel(_level);
                foreach (var pair in pairs)
                    AssignBracketType(pair, dirEmbed);
            }

            private List<BracketPair> LocateBrackets()
            {
                const int maxStack = 63;
                var openers = new List<KeyValuePair<int, int>>(); // (closing bracket cp, position)
                var pairs = new List<BracketPair>();
                for (int i = 0; i < _length; i++)
                {
                    if (_types[i] != BidiClass.ON) continue;
                    int cp = _p._codePoints[_indexes[i]];
                    int bt = UnicodeProps.GetBracketType(cp);
                    if (bt == 1)
                    {
                        if (openers.Count == maxStack) { break; }
                        openers.Add(new KeyValuePair<int, int>(CanonicalBracket(UnicodeProps.GetPairedBracket(cp)), i));
                    }
                    else if (bt == 2)
                    {
                        int canon = CanonicalBracket(cp);
                        for (int s = openers.Count - 1; s >= 0; s--)
                        {
                            if (openers[s].Key == canon)
                            {
                                pairs.Add(new BracketPair { Open = openers[s].Value, Close = i });
                                openers.RemoveRange(s, openers.Count - s);
                                break;
                            }
                        }
                    }
                }
                if (openers.Count == maxStack && pairs.Count == 0) return pairs;
                pairs.Sort((a, b) => a.Open.CompareTo(b.Open));
                return pairs;
            }

            private static BidiClass StrongTypeForN0(BidiClass t)
            {
                switch (t)
                {
                    case BidiClass.L: return BidiClass.L;
                    case BidiClass.R:
                    case BidiClass.EN:
                    case BidiClass.AN:
                    case BidiClass.AL:
                        return BidiClass.R;
                    default: return BidiClass.ON;
                }
            }

            private void AssignBracketType(BracketPair pair, BidiClass dirEmbed)
            {
                // inside the brackets
                var dirPair = BidiClass.ON;
                for (int i = pair.Open + 1; i < pair.Close; i++)
                {
                    var dir = StrongTypeForN0(_types[i]);
                    if (dir == BidiClass.ON) continue;
                    if (dir == dirEmbed) { dirPair = dir; break; }
                    dirPair = dir;
                }
                if (dirPair == BidiClass.ON) return; // no strong type inside

                if (dirPair != dirEmbed)
                {
                    // opposite direction found inside; look at preceding context
                    var dirBefore = _sos;
                    for (int i = pair.Open - 1; i >= 0; i--)
                    {
                        var dir = StrongTypeForN0(_types[i]);
                        if (dir != BidiClass.ON) { dirBefore = dir; break; }
                    }
                    dirPair = dirBefore == dirPair ? dirPair : dirEmbed;
                }
                SetBracketsToType(pair, dirPair);
            }

            private void SetBracketsToType(BracketPair pair, BidiClass dir)
            {
                _types[pair.Open] = dir;
                _types[pair.Close] = dir;
                for (int i = pair.Open + 1; i < pair.Close; i++)
                {
                    if (_p._initialTypes[_indexes[i]] == BidiClass.NSM) _types[i] = dir; else break;
                }
                for (int i = pair.Close + 1; i < _length; i++)
                {
                    if (_p._initialTypes[_indexes[i]] == BidiClass.NSM) _types[i] = dir; else break;
                }
            }

            // N1/N2 -----------------------------------------------------------
            private static bool IsNI(BidiClass t)
            {
                switch (t)
                {
                    case BidiClass.B:
                    case BidiClass.S:
                    case BidiClass.WS:
                    case BidiClass.ON:
                    case BidiClass.RLI:
                    case BidiClass.LRI:
                    case BidiClass.FSI:
                    case BidiClass.PDI:
                        return true;
                    default:
                        return false;
                }
            }

            public void ResolveNeutralTypes()
            {
                for (int i = 0; i < _length; i++)
                {
                    var t = _types[i];
                    if (IsNI(t))
                    {
                        int runStart = i;
                        int runLimit = FindRunLimit(runStart, _length, IsNI);

                        BidiClass leading, trailing;
                        if (runStart == 0)
                        {
                            leading = _sos;
                        }
                        else
                        {
                            leading = _types[runStart - 1];
                            if (leading == BidiClass.AN || leading == BidiClass.EN) leading = BidiClass.R;
                        }
                        if (runLimit == _length)
                        {
                            trailing = _eos;
                        }
                        else
                        {
                            trailing = _types[runLimit];
                            if (trailing == BidiClass.AN || trailing == BidiClass.EN) trailing = BidiClass.R;
                        }

                        var resolved = leading == trailing ? leading : TypeForLevel(_level);
                        for (int k = runStart; k < runLimit; k++) _types[k] = resolved;
                        i = runLimit;
                    }
                }
            }

            public void ResolveImplicitLevels()
            {
                _resolvedLevels = new byte[_length];
                for (int i = 0; i < _length; i++) _resolvedLevels[i] = _level;

                if ((_level & 1) == 0)
                {
                    for (int i = 0; i < _length; i++)
                    {
                        var t = _types[i];
                        if (t == BidiClass.L) { }
                        else if (t == BidiClass.R) _resolvedLevels[i] += 1;
                        else _resolvedLevels[i] += 2; // AN or EN
                    }
                }
                else
                {
                    for (int i = 0; i < _length; i++)
                    {
                        var t = _types[i];
                        if (t != BidiClass.R) _resolvedLevels[i] += 1; // L, EN, AN
                    }
                }
            }

            public void ApplyLevelsAndTypes()
            {
                for (int i = 0; i < _length; i++)
                {
                    int idx = _indexes[i];
                    _p._resultTypes[idx] = _types[i];
                    _p._resultLevels[idx] = _resolvedLevels[i];
                }
            }
        }
    }
}
