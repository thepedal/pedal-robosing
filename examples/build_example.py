#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 thepedal
"""Builds robosing_showcase.bmxml: five Pedal RoboSing instances, one per idea.

Method (project song-format notes): never fabricate a machine block. The
RoboSing block and its pattern-editor block are taken from robosing_ref.bmxml,
a song ReBuzz itself saved, and cloned by name. Only names, stored parameter
values, the phrase-list state, pattern length and the editor's note data change.

Also writes robosing_showcase.events.txt, a plain list of the same events, which
tests/compile can play through the real machine code to render a WAV.
"""
import base64, os, re, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REF = os.path.join(HERE, "robosing_ref.bmxml")
OUT = os.path.join(HERE, "robosing_showcase.bmxml")
EVT = os.path.join(HERE, "robosing_showcase.events.txt")

BAR = 16                      # rows per bar (TPB 4, 4/4)
BARS = 24
LENGTH = BAR * BARS           # 384 rows, about 46 s at 126 BPM
NOTE_OFF = 255

# Column layout, confirmed by decoding the editor blob in the reference save:
# 29 global columns, then 7 track columns repeated per track.
GLOBALS = ["Volume", "Speed", "Voice", "Inflection", "Mode", "Excitation", "Chip Rate", "Frame",
           "Coef Bits", "DAC Bits", "Speaker", "Note Off", "Breath", "Play Mode", "Hold", "Sync Step",
           "Glide", "Scan", "Mouth", "Tune", "Tune To", "Root", "Scale", "Inflect Snap", "Cons Tune",
           "Cons Pitch", "Vib Depth", "Vib Rate", "Portamento"]
# Values of those 29 in the reference save (the v1.0 defaults).
V1_DEFAULTS = dict(zip(GLOBALS, [100, 64, 64, 64, 0, 0, 2, 25, 6, 8, 64, 0, 0, 1, 1, 2, 0, 64, 0, 0, 0, 0, 1, 0, 0, 2, 0, 64, 0]))
BASE = len(GLOBALS)
NOTE, VEL, PHRASE, WORD, WORDS, STEP, POS = range(BASE, BASE + 7)
TRACK_COLS = 7
TRACKS = 8

NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
def n(tok):
    """'A4' / 'C#5' -> Buzz note byte."""
    name, octave = tok[:-1], int(tok[-1])
    return octave * 16 + NAMES.index(name) + 1

CLEAN = {"Excitation": 1, "Chip Rate": 5, "Frame": 15, "Coef Bits": 10, "DAC Bits": 14, "Speaker": 30}


class Part:
    def __init__(self, name, x, y, amp, phrases, params):
        self.name, self.x, self.y, self.amp = name, x, y, amp
        self.phrases, self.params = phrases, params
        self.ev = {}                                  # (track, col) -> [(row, value)]

    def at(self, row, track, col, value):
        self.ev.setdefault((track, col), []).append((row, value))

    def note(self, row, track, tok):
        self.at(row, track, NOTE, NOTE_OFF if tok == "off" else n(tok))


# ── The parts ───────────────────────────────────────────────────────────────

def perc():
    """Bars 1-20. Phoneme steps, one-shot, consonant noise pitched an octave up."""
    p = Part("Perc", -0.95, -0.55, 0.86,
             ["Stop. Tick. Kiss. Spit. Shock. Chit. Pass."],
             dict(CLEAN, **{"Play Mode": 2, "Hold": 0, "Cons Tune": 115, "Cons Pitch": 2, "Speaker": 20}))
    riff = [(0, "A2"), (2, "A2"), (3, "C3"), (4, "A2"), (6, "D3"), (7, "A2"),
            (8, "E3"), (10, "A2"), (11, "G2"), (12, "A2"), (14, "C3"), (15, "D3")]
    for bar in range(0, 20):
        r0 = bar * BAR
        if bar % 4 == 0: p.at(r0, 0, PHRASE, 1)       # restart the step counter every 4 bars
        for step, tok in riff:
            p.note(r0 + step, 0, tok)
    p.note(20 * BAR, 0, "off")
    return p


def drone():
    """Bars 5-24. One low note per two bars; the words move scale-tuned overtones inside it."""
    p = Part("Drone", -0.55, 0.70, 0.70,
             ["Seven silver [S ER1 K AX T S] sing in the dark."],
             dict(CLEAN, **{"Excitation": 2, "Tune": 127, "Tune To": 1, "Root": 9, "Scale": 7, "Glide": 45}))
    roots = ["A2", "A2", "G2", "A2", "C3", "A2", "G2", "A2", "A2", "A2"]
    for i in range(4 * BAR, 24 * BAR - 4, 4):         # a new syllable every beat
        if (i - 4 * BAR) % (8 * BAR) == 0: p.at(i, 0, PHRASE, 1)
        p.note(i, 0, roots[(i - 4 * BAR) // (2 * BAR)])
    p.note(24 * BAR - 4, 0, "off")
    return p


def lead():
    """Bars 9-20. One syllable per note, vowels locked to the note's harmonics."""
    p = Part("Lead", 0.05, -1.00, 0.51,
             ["I am a robot, I make strange music.", "Sing with me in harmony tonight."],
             dict(CLEAN, **{"Tune": 105, "Vib Depth": 25, "Portamento": 20, "Glide": 15}))
    line1 = [(0, "A4"), (2, "C5"), (4, "D5"), (6, "E5"), (8, "D5"), (12, "off"),
             (14, "C5"), (16, "D5"), (18, "E5"), (22, "G5"), (24, "E5"), (29, "off")]
    line2 = [(0, "A5"), (2, "G5"), (4, "E5"), (7, "off"),
             (8, "D5"), (10, "E5"), (12, "G5"), (14, "E5"), (18, "D5"), (20, "A4"), (29, "off")]
    for cycle in range(3):
        r0 = (8 + cycle * 4) * BAR
        p.at(r0, 0, PHRASE, 1)
        for step, tok in line1: p.note(r0 + step, 0, tok)
        r1 = r0 + 2 * BAR
        p.at(r1, 0, PHRASE, 2)
        for step, tok in line2: p.note(r1 + step, 0, tok)
    return p


def choir():
    """Bars 13-20. Three tracks, one shared mouth, a syllable per tick."""
    p = Part("Choir", 0.75, -0.45, 0.29,
             ["We are the voices in the machine."],
             dict(CLEAN, **{"Play Mode": 3, "Sync Step": 1, "Mouth": 1, "Inflection": 0,
                            "Excitation": 2, "Tune": 50, "Glide": 15}))
    chords = [("A3", "C4", "E4"), ("A3", "C4", "F4"), ("G3", "C4", "E4"), ("G3", "B3", "D4")]
    for bar in range(12, 20):
        r0 = bar * BAR
        for t, tok in enumerate(chords[(bar - 12) % 4]):
            p.note(r0, t, tok)
            if t > 0: p.note(r0 + 12, t, "off")       # followers stop; the leader's phrase ends itself
    return p


def pad():
    """Bars 21-24. Scrub: the Position column drags two mouths through the phrase."""
    p = Part("Scrub", 0.60, 0.75, 0.33,
             ["Good [B AY1] from the machine."],
             dict(CLEAN, **{"Play Mode": 4, "Glide": 60, "Tune": 80, "Breath": 25}))
    r0, span = 20 * BAR, 4 * BAR - 4
    p.note(r0, 0, "A3"); p.note(r0, 1, "E4")
    for i in range(span):
        x = i / (span - 1)
        p.at(r0 + i, 0, POS, round(1000 * x))                 # forwards
        p.at(r0 + i, 1, POS, round(1000 * (1 - x)))           # backwards
    p.note(r0 + span, 0, "off"); p.note(r0 + span, 1, "off")
    return p


PARTS = [perc(), drone(), lead(), choir(), pad()]


# ── Format helpers ──────────────────────────────────────────────────────────

def build_blob(machine, patname, events):
    """Modern Pattern Editor blob: all columns emitted, track columns track-major."""
    def rec(col, track):
        evs = sorted(events.get((track, col), []))
        r = machine.encode("latin1") + b"\x00" + struct.pack("<ii", col, track) + b"\x00" + struct.pack("<i", len(evs))
        for row, val in evs: r += struct.pack("<ii", row * 240, val)
        return r + struct.pack("<iiii", 4, 4, 4, 4)
    recs = [rec(c, 0) for c in range(BASE)]
    for t in range(TRACKS):
        recs += [rec(BASE + k, t) for k in range(TRACK_COLS)]
    body = struct.pack("<i", 1) + patname.encode("latin1") + b"\x00" + struct.pack("<iii", 4, 4, len(recs)) + b"".join(recs)
    return b"\xff\x01" + struct.pack("<I", 6 + len(body)) + body


def build_state(phrases):
    """Managed-machine state: [0x02][int32 size][BOM + XmlSerializer text of RoboSingState]."""
    esc = lambda s: s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    lines = ['<?xml version="1.0" encoding="utf-8"?>',
             '<RoboSingState xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">',
             "  <Version>1</Version>", "  <Phrases>"]
    lines += ["    <string>%s</string>" % esc(p) for p in phrases]
    lines += ["  </Phrases>", "</RoboSingState>"]
    body = b"\xef\xbb\xbf" + "\r\n".join(lines).encode("utf-8")
    return bytes([2]) + struct.pack("<i", len(body)) + body


def machine_blocks(raw):
    a, b = raw.index("<Machines>") + 10, raw.index("</Machines>")
    seg, out, depth, start = raw[a:b], [], 0, None
    for m in re.finditer(r"</?Machine>", seg):
        if m.group(0) == "<Machine>":
            if depth == 0: start = m.start()
            depth += 1
        else:
            depth -= 1
            if depth == 0: out.append(seg[start:m.end()])
    return out


def sub1(pattern, repl, text, flags=0):
    new, k = re.subn(pattern, repl, text, count=1, flags=flags)
    assert k == 1, "pattern not found: " + pattern
    return new


def set_global(block, name, value):
    xname = name.replace(" ", "_x0020_")
    return sub1(r"(<Value>\s*<Track>0</Track>\s*<Value>)\d+(</Value>\s*</Value>\s*</Values>\s*<Type>\w+</Type>\s*<Name>%s</Name>)"
                % re.escape(xname), lambda m: m.group(1) + str(value) + m.group(2), block)


CONN = """    <MachineConnection>\r
      <Source>%s</Source>\r
      <Destination>Master</Destination>\r
      <Amp>%d</Amp>\r
      <Pan>16384</Pan>\r
      <SourceChannel>0</SourceChannel>\r
      <DestinationChannel>0</DestinationChannel>\r
      <SourceChannels>\r
        <int>0</int>\r
      </SourceChannels>\r
      <DestinationChannels>\r
        <int>0</int>\r
      </DestinationChannels>\r
    </MachineConnection>\r
"""
SEQ = """    <Sequence>\r
      <Events>\r
        <Event>\r
          <Time>0</Time>\r
          <Type>PlayPattern</Type>\r
          <Span>%d</Span>\r
          <Pattern>00</Pattern>\r
        </Event>\r
      </Events>\r
      <Machine>%s</Machine>\r
      <IsDisabled>false</IsDisabled>\r
    </Sequence>\r
"""


def main():
    raw = open(REF, "rb").read().decode("utf-8-sig")
    blocks = machine_blocks(raw)
    by_lib = {}
    for b in blocks:
        by_lib.setdefault(re.search(r"<Library>(.*?)</Library>", b).group(1), []).append(b)
    master, master_ed = by_lib["Master"][0], by_lib["Modern Pattern Editor"][0]
    gen_ref, ed_ref = by_lib["Pedal RoboSing"][0], by_lib["Modern Pattern Editor"][1]

    # The generator builds blobs the way ReBuzz does: reproduce the reference's empty one exactly.
    ref_blob = base64.b64decode(re.search(r"<Data>(.*?)</Data>", ed_ref).group(1))
    assert build_blob("RoboSing", "00", {}) == ref_blob, "blob layout differs from the reference save"
    ref_state = base64.b64decode(re.search(r"<Data>(.*?)</Data>", gen_ref).group(1))
    ref_phrases = re.findall(r"<string>(.*?)</string>", ref_state.decode("utf-8", "replace"))
    assert build_state(ref_phrases) == ref_state, "state layout differs from the reference save"

    out_blocks, conns, seqs = [master, master_ed], [CONN % ("_x0001_pe1", 16384)], []
    for i, p in enumerate(PARTS):
        ed = "_x0001_pe%d" % (i + 2)
        g = gen_ref
        g = sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % base64.b64encode(build_state(p.phrases)).decode(), g, re.S)
        g = sub1(r"<EditorMachine>.*?</EditorMachine>", "<EditorMachine>%s</EditorMachine>" % ed, g)
        g = sub1(r"<Name>RoboSing</Name>", "<Name>%s</Name>" % p.name, g)
        g = sub1(r"<Length>\d+</Length>", "<Length>%d</Length>" % LENGTH, g)
        g = sub1(r"<X>[-\d.]+</X>\s*<Y>[-\d.]+</Y>", "<X>%s</X>\r\n      <Y>%s</Y>" % (p.x, p.y), g)
        for k, v in p.params.items():
            assert k in GLOBALS, k
            g = set_global(g, k, v)
        e = ed_ref
        e = sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % base64.b64encode(build_blob(p.name, "00", p.ev)).decode(), e, re.S)
        e = sub1(r"<Name>_x0001_pe2</Name>", "<Name>%s</Name>" % ed, e)
        out_blocks += [g, e]
        conns += [CONN % (ed, 16384), CONN % (p.name, round(16384 * p.amp))]
        seqs.append(SEQ % (LENGTH, p.name))

    out = raw
    a, b = out.index("<Machines>") + 10, out.index("</Machines>")
    out = out[:a] + "\r\n    " + "\r\n    ".join(out_blocks) + "\r\n  " + out[b:]
    out = sub1(r"<Sequences>.*?</Sequences>", lambda m: "<Sequences>\r\n" + "".join(seqs) + "  </Sequences>", out, re.S)
    out = sub1(r"<MachineConnections>.*?</MachineConnections>",
               lambda m: "<MachineConnections>\r\n" + "".join(conns) + "  </MachineConnections>", out, re.S)
    out = sub1(r"<LoopEnd>\d+</LoopEnd>", "<LoopEnd>%d</LoopEnd>" % LENGTH, out)
    out = sub1(r"<SongEnd>\d+</SongEnd>", "<SongEnd>%d</SongEnd>" % LENGTH, out)
    out = sub1(r'Name="[^"]*"', 'Name="robosing_showcase"', out)
    open(OUT, "wb").write(b"\xef\xbb\xbf" + out.encode("utf-8"))

    with open(EVT, "w", encoding="utf-8") as f:
        f.write("LENGTH %d\n" % LENGTH)
        f.write("BASE %d\n" % BASE)
        for p in PARTS:
            f.write("M\t%s\t%s\n" % (p.name, p.amp))
            for ph in p.phrases: f.write("P\t%s\t%s\n" % (p.name, ph))
            full = dict(V1_DEFAULTS, **p.params)             # a saved song stores every value
            for k, v in full.items(): f.write("G\t%s\t%d\t%d\n" % (p.name, GLOBALS.index(k), v))
            for (t, c), evs in p.ev.items():
                for row, val in evs: f.write("E\t%s\t%d\t%d\t%d\t%d\n" % (p.name, row, t, c, val))
    print("wrote %s (%d machines, %d rows) and %s" % (os.path.basename(OUT), 2 + 2 * len(PARTS), LENGTH, os.path.basename(EVT)))


if __name__ == "__main__":
    main()
