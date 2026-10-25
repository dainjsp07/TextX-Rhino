#!/usr/bin/env python3
"""
Generates src/RhinoTranslator/Core/Text/UnicodeTables.g.cs from ICU's
ppucd.txt (preparsed Unicode Character Database) and UnicodeData.txt.

Usage:
    python3 gen_unicode_tables.py <ppucd.txt> <UnicodeData.txt> <out.cs>

Tables generated:
  * Bidi_Class for every code point (compressed into ranges)
  * Bidi_Mirroring_Glyph pairs
  * Bidi_Paired_Bracket / Bidi_Paired_Bracket_Type
  * Joining_Type for the Arabic-script ranges (for contextual shaping)
  * Arabic presentation forms (isolated / final / initial / medial) and
    the mandatory LAM-ALEF ligatures, derived from compatibility decompositions.
"""
import sys

BIDI_CLASSES = ["L", "R", "AL", "EN", "ES", "ET", "AN", "CS", "NSM", "BN", "B", "S", "WS",
                "ON", "LRE", "LRO", "RLE", "RLO", "PDF", "LRI", "RLI", "FSI", "PDI"]
JOINING = {"U": 0, "R": 1, "L": 2, "D": 3, "C": 4, "T": 5}


def parse_props(fields):
    props = {}
    for f in fields:
        if not f:
            continue
        if "=" in f:
            k, v = f.split("=", 1)
            props[k] = v
        else:
            props[f.lstrip("-")] = not f.startswith("-")
    return props


def parse_range(r):
    if ".." in r:
        a, b = r.split("..")
        return int(a, 16), int(b, 16)
    v = int(r, 16)
    return v, v


def main(ppucd, unidata, out):
    N = 0x110000
    bc = [None] * N
    bmg = {}
    bpb = {}
    bpt = {}
    jt = [None] * N

    defaults = {}
    block_props = None
    block_range = None
    for raw in open(ppucd, encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(";")
        kind = parts[0]
        if kind == "defaults":
            defaults = parse_props(parts[2:])
            lo, hi = parse_range(parts[1])
            for c in range(lo, hi + 1):
                bc[c] = defaults["bc"]
                jt[c] = defaults.get("jt", "U")
        elif kind == "block":
            lo, hi = parse_range(parts[1])
            block_props = dict(defaults)
            block_props.update(parse_props(parts[2:]))
            block_range = (lo, hi)
            for c in range(lo, hi + 1):
                bc[c] = block_props["bc"]
                jt[c] = block_props.get("jt", "U")
        elif kind in ("cp", "cps", "unassigned"):
            lo, hi = parse_range(parts[1])
            base = dict(defaults)
            if block_props is not None and block_range[0] <= lo <= block_range[1]:
                base = dict(block_props)
            p = dict(base)
            p.update(parse_props(parts[2:]))
            for c in range(lo, hi + 1):
                bc[c] = p["bc"]
                jt[c] = p.get("jt", "U")
            own = parse_props(parts[2:])
            if "bmg" in own:
                for c in range(lo, hi + 1):
                    bmg[c] = int(own["bmg"], 16)
            if "bpb" in own:
                for c in range(lo, hi + 1):
                    bpb[c] = int(own["bpb"], 16)
            if "bpt" in own and own["bpt"] in ("o", "c"):
                for c in range(lo, hi + 1):
                    bpt[c] = own["bpt"]

    # ---- compress bidi classes into ranges
    starts, classes = [], []
    prev = None
    for c in range(N):
        v = bc[c] if bc[c] is not None else "L"
        if v != prev:
            starts.append(c)
            classes.append(BIDI_CLASSES.index(v))
            prev = v

    # ---- joining types: only keep the ranges that matter for Arabic-script shaping
    jt_ranges = []
    prev = None
    for c in range(N):
        v = jt[c] or "U"
        if v != prev:
            jt_ranges.append((c, JOINING[v]))
            prev = v

    # ---- presentation forms from UnicodeData decompositions
    forms = {}  # base -> [isolated, final, initial, medial]
    ligatures = {}  # (lam, alef) -> [isolated, final]
    idx = {"<isolated>": 0, "<final>": 1, "<initial>": 2, "<medial>": 3}
    for raw in open(unidata, encoding="utf-8"):
        f = raw.split(";")
        cp = int(f[0], 16)
        if not (0xFB50 <= cp <= 0xFDFF or 0xFE70 <= cp <= 0xFEFF):
            continue
        dm = f[5].split()
        if not dm or dm[0] not in idx:
            continue
        tag = idx[dm[0]]
        chars = [int(x, 16) for x in dm[1:]]
        if len(chars) == 1:
            forms.setdefault(chars[0], [0, 0, 0, 0])
            if forms[chars[0]][tag] == 0:
                forms[chars[0]][tag] = cp
        elif len(chars) == 2 and chars[0] == 0x0644 and chars[1] in (0x0622, 0x0623, 0x0625, 0x0627):
            ligatures.setdefault(chars[1], [0, 0])
            if tag in (0, 1) and ligatures[chars[1]][tag] == 0:
                ligatures[chars[1]][tag] = cp

    def int_array(values, per_line=16):
        lines = []
        for i in range(0, len(values), per_line):
            lines.append("            " + ", ".join(str(v) for v in values[i:i + per_line]) + ",")
        return "\n".join(lines)

    with open(out, "w", encoding="utf-8", newline="\n") as o:
        o.write("// <auto-generated>\n")
        o.write("// Generated by tools/gen_unicode_tables.py from the Unicode Character Database\n")
        o.write("// (ICU ppucd.txt + UnicodeData.txt). Do not edit by hand.\n")
        o.write("// </auto-generated>\n")
        o.write("namespace RhinoTranslator.Core.Text\n{\n")
        o.write("    internal static class UnicodeTables\n    {\n")
        o.write("        /// <summary>Start code point of each Bidi_Class range.</summary>\n")
        o.write("        internal static readonly int[] BidiStarts = new int[]\n        {\n")
        o.write(int_array(starts) + "\n        };\n\n")
        o.write("        /// <summary>Bidi_Class (as BidiClass enum value) of each range.</summary>\n")
        o.write("        internal static readonly byte[] BidiValues = new byte[]\n        {\n")
        o.write(int_array(classes, 32) + "\n        };\n\n")
        mk = sorted(bmg)
        o.write("        /// <summary>Bidi_Mirroring_Glyph pairs: [cp0, mirror0, cp1, mirror1, ...].</summary>\n")
        o.write("        internal static readonly int[] MirrorPairs = new int[]\n        {\n")
        o.write(int_array([x for k in mk for x in (k, bmg[k])]) + "\n        };\n\n")
        bk = sorted(bpt)
        o.write("        /// <summary>Paired brackets: [cp, pairedBracket, type(1=open,2=close), ...].</summary>\n")
        o.write("        internal static readonly int[] BracketData = new int[]\n        {\n")
        o.write(int_array([x for k in bk for x in (k, bpb[k], 1 if bpt[k] == 'o' else 2)], 15) + "\n        };\n\n")
        o.write("        /// <summary>Joining_Type ranges: [start, type, start, type, ...]. 0=U 1=R 2=L 3=D 4=C 5=T.</summary>\n")
        o.write("        internal static readonly int[] JoiningRanges = new int[]\n        {\n")
        o.write(int_array([x for r in jt_ranges for x in r]) + "\n        };\n\n")
        fk = sorted(forms)
        o.write("        /// <summary>Arabic presentation forms: [base, isolated, final, initial, medial, ...] (0 = none).</summary>\n")
        o.write("        internal static readonly int[] ArabicForms = new int[]\n        {\n")
        o.write(int_array([x for k in fk for x in [k] + forms[k]], 15) + "\n        };\n\n")
        lk = sorted(ligatures)
        o.write("        /// <summary>LAM + ALEF ligatures: [alef, isolated, final, ...].</summary>\n")
        o.write("        internal static readonly int[] LamAlef = new int[]\n        {\n")
        o.write(int_array([x for k in lk for x in [k] + ligatures[k]], 15) + "\n        };\n")
        o.write("    }\n}\n")

    print("bidi ranges:", len(starts), "mirror pairs:", len(bmg), "brackets:", len(bpt),
          "joining ranges:", len(jt_ranges), "arabic forms:", len(forms), "lam-alef:", len(ligatures))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
