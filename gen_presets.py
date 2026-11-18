#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 thepedal
"""Generate 'Pedal RoboSing.prs.xml' (Build §3). Run with --check to verify the
parameter order and ranges against PedalRoboSing.cs without writing anything.
Globals only, unique names, so ReBuzz applies these by name (Build §3.3)."""
import re, sys, os
from xml.sax.saxutils import quoteattr

MACHINE = "Pedal RoboSing"
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "PedalRoboSing.cs")
OUT = os.path.join(HERE, MACHINE + ".prs.xml")

# Declaration order of the global parameters (the preset contract), with defaults.
GLOBALS = [
    ("Volume", 100), ("Speed", 64), ("Voice", 64), ("Inflection", 64), ("Mode", 0),
    ("Excitation", 1), ("Chip Rate", 5), ("Frame", 15), ("Coef Bits", 10), ("DAC Bits", 14),
    ("Speaker", 30), ("Note Off", 0), ("Breath", 0),
    ("Play Mode", 5), ("Hold", 1), ("Sync Step", 2), ("Glide", 15), ("Scan", 64), ("Mouth", 0),
    ("Tune", 50), ("Tune To", 0), ("Root", 0), ("Scale", 1), ("Inflect Snap", 0),
    ("Cons Tune", 40), ("Cons Pitch", 2), ("Vib Depth", 30), ("Vib Rate", 64), ("Portamento", 20),
    ("React Level", 95), ("React Tune", 120), ("React Size", 90), ("React Ring", 110), ("React Vib", 120),
    ("React Rhyme", 100), ("React Allit", 100),
]

# Since v1.5 every preset sings the line drawn in the phrase editor (Play Mode
# Line) and only changes how it sounds; 23 is the one for playing syllables by hand.
# The machine's defaults changed in v1.2. Presets are written against this plain
# baseline (the v1.0 defaults, every React control off) so they don't move with them.
PLAIN = {"Excitation": 0, "Chip Rate": 2, "Frame": 25, "Coef Bits": 6, "DAC Bits": 8, "Speaker": 64,
         "Play Mode": 5, "Glide": 0, "Tune": 0, "Cons Tune": 0, "Vib Depth": 0, "Portamento": 0,
         "React Level": 64, "React Tune": 64, "React Size": 64, "React Ring": 64, "React Vib": 64,
         "React Rhyme": 64, "React Allit": 64}

CLEAN = {"Excitation": 1, "Chip Rate": 5, "Frame": 15, "Coef Bits": 10, "DAC Bits": 14, "Speaker": 30}

PRESETS = {
    "01 Simple Singer":      dict(CLEAN, **{"Vib Depth": 20, "Portamento": 30}),
    "02 Overtone Singer":    dict(CLEAN, **{"Tune": 115, "Glide": 30, "Vib Depth": 15}),
    "03 Scale Bells":        dict(CLEAN, **{"Excitation": 2, "Tune": 127, "Tune To": 1, "Scale": 7, "Glide": 45}),
    "04 Chant in Time":      dict(CLEAN, **{"Inflection": 0, "Excitation": 2, "Tune": 50}),
    "05 Choir One Mouth":    dict(CLEAN, **{"Mouth": 1, "Tune": 60, "Vib Depth": 18, "Glide": 25}),
    "06 Tuned Percussion":   dict(CLEAN, **{"Hold": 0, "Cons Tune": 115, "Speaker": 20}),
    "07 Smear Pad":          dict(CLEAN, **{"Glide": 80, "Tune": 80, "Breath": 25, "Portamento": 60}),
    "08 Backwards Line":     dict(CLEAN, **{"Scan": 44, "Tune": 60, "Glide": 20}),
    "09 Speech Melody":      dict(CLEAN, **{"Inflection": 110, "Inflect Snap": 1, "Scale": 6, "Portamento": 20}),
    "10 Whisper Hiss":       dict(CLEAN, **{"Excitation": 4, "Cons Tune": 90, "Cons Pitch": 1, "Volume": 115}),
    "11 Chip Talker":        {},
    "12 Chip Singer":        {"Tune": 40},
    "13 Spelling Bee":       {"Mode": 2},
    "14 Broken Batteries":   {"Coef Bits": 3, "DAC Bits": 4, "Frame": 45, "Chip Rate": 0, "Speed": 50, "Speaker": 90},
    "15 Giant Drone":        dict(CLEAN, **{"Voice": 15, "Glide": 100, "Tune": 100, "Tune To": 1, "Scale": 13, "Excitation": 2}),
    "16 Tiny Robot":         {"Voice": 110, "Chip Rate": 3, "Tune": 70, "Excitation": 3, "Inflection": 0},
    "17 Reactive Singer":    dict(CLEAN, **{"Tune": 50, "Vib Depth": 30, "Portamento": 20, "Glide": 15, "Cons Tune": 40,
                                    "React Level": 95, "React Tune": 120, "React Size": 90, "React Ring": 110, "React Vib": 120,
                                    "React Rhyme": 100, "React Allit": 100}),
    "18 Open Vowels Ring":   dict(CLEAN, **{"Tune": 55, "React Tune": 127, "Glide": 25}),
    "19 Rubber Mouth":       dict(CLEAN, **{"React Size": 127, "Glide": 30, "Tune": 40}),
    "20 Hard Edges":         dict(CLEAN, **{"Hold": 0, "Cons Tune": 60, "React Ring": 127, "React Level": 100}),
    "21 Rhyme Spotlight":    dict(CLEAN, **{"Tune": 30, "Vib Depth": 25, "React Rhyme": 127, "Glide": 15}),
    "22 Tongue Twister":     dict(CLEAN, **{"Cons Tune": 50, "React Allit": 127, "React Ring": 100}),
    "23 By Hand Syllables":  dict(CLEAN, **{"Play Mode": 1, "Tune": 50, "Vib Depth": 30, "Portamento": 20, "Glide": 15, "Cons Tune": 40,
                                    "React Level": 95, "React Tune": 120, "React Size": 90, "React Ring": 110, "React Vib": 120,
                                    "React Rhyme": 100, "React Allit": 100}),
}

PRESETS_FULL = {k: dict(PLAIN, **v) for k, v in PRESETS.items()}


def parse_source():
    """Global [ParameterDecl]s on properties, in order: (name, min, max, default)."""
    text = open(SRC, encoding="utf-8").read()
    out = []
    for m in re.finditer(r"\[ParameterDecl\((.*?)\)\]\s*public\s+(\w+)\s+(\w+)\s*(\{|\()", text, re.S):
        attrs, _type, member, kind = m.groups()
        if kind == "(":            # method = track parameter
            continue
        if "IsStateless = true" in attrs:
            continue
        get = lambda k: re.search(k + r'\s*=\s*("?)([^",]+)\1', attrs).group(2)
        out.append((get("Name"), int(get("MinValue")), int(get("MaxValue")), int(get("DefValue"))))
    return out

def check():
    src = parse_source()
    ok = True
    if [s[0] for s in src] != [g[0] for g in GLOBALS]:
        print("ORDER MISMATCH\n source: %s\n script: %s" % ([s[0] for s in src], [g[0] for g in GLOBALS])); ok = False
    rng = {s[0]: s for s in src}
    for name, d in GLOBALS:
        if name in rng and rng[name][3] != d:
            print("default mismatch for %s: source %d, script %d" % (name, rng[name][3], d)); ok = False
    for pname, ov in PRESETS_FULL.items():
        for k, v in ov.items():
            if k not in rng: print("%s: unknown parameter %s" % (pname, k)); ok = False; continue
            lo, hi = rng[k][1], rng[k][2]
            if not lo <= v <= hi: print("%s: %s=%d outside %d..%d" % (pname, k, v, lo, hi)); ok = False
    print("check OK: %d globals, %d presets" % (len(src), len(PRESETS)) if ok else "check FAILED")
    return ok

def build():
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<PresetDictionary>"]
    for pname, ov in PRESETS.items():
        lines.append("  <Item Key=%s>" % quoteattr(pname))
        lines.append("    <Preset Machine=%s>" % quoteattr(MACHINE))
        lines.append("      <Parameters>")
        for idx, (name, d) in enumerate(GLOBALS):
            lines.append('        <Parameter Name=%s Group="1" Index="%d" Track="0" Value="%d" />'
                         % (quoteattr(name), idx, PRESETS_FULL[pname].get(name, d)))
        lines.append("      </Parameters>")
        lines.append("      <Attributes />")
        lines.append("      <Comment></Comment>")
        lines.append("    </Preset>")
        lines.append("  </Item>")
    lines.append("</PresetDictionary>")
    with open(OUT, "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write("\n".join(lines) + "\n")
    print("wrote %s (%d presets)" % (os.path.basename(OUT), len(PRESETS)))

if __name__ == "__main__":
    if not check(): sys.exit(1)
    if "--check" not in sys.argv: build()
