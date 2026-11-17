#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 thepedal
"""Turn one melody line of a MIDI file into a Pedal RoboSing song (.bmxml).

  python midi_to_robosing.py song.mid --list
  python midi_to_robosing.py song.mid --channel 4 -o song.bmxml
  python midi_to_robosing.py song.mid --channel 4 --lyrics words.txt -o song.bmxml

Each note sings the next syllable (Syllable mode, Hold on). The melody is cut
into phrases wherever it rests for --gap beats or more; each phrase becomes one
line of the machine's phrase list, and a Phrase event at its first note restarts
the syllable counter there, so a slip in one line cannot throw the next one out.

Without --lyrics every line is filled with one "la" per note. A template is
written next to the output (<name>.lyrics.txt) listing how many syllables each
line needs. Edit the words in ReBuzz (right-click > Edit Phrases: the strip
under the text shows each line's syllable count), or edit the template and run
again with --lyrics. A line with fewer syllables than notes wraps round to its
first syllable; hold a syllable over several notes by repeating a vowel in
brackets, e.g. "to [AY] night".

The machine and editor blocks come from robosing_ref.bmxml, saved by ReBuzz.
"""
import argparse, base64, math, os, re, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_example as be

NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


# ── Standard MIDI file reader ───────────────────────────────────────────────

def read_midi(path):
    """-> (ppq, tempo_us_per_beat or None, notes[(start_tick, end_tick, channel, pitch, velocity)])."""
    d = open(path, "rb").read()
    if d[:4] != b"MThd": raise SystemExit("not a standard MIDI file")
    hlen, fmt, ntrk, div = struct.unpack(">IHHH", d[4:14])
    if div & 0x8000: raise SystemExit("SMPTE time division is not supported")
    pos, notes, tempo = 8 + hlen, [], None
    for _ in range(ntrk):
        while d[pos:pos + 4] != b"MTrk":                    # skip unknown chunks
            pos += 8 + struct.unpack(">I", d[pos + 4:pos + 8])[0]
            if pos >= len(d): break
        if pos >= len(d): break
        end = pos + 8 + struct.unpack(">I", d[pos + 4:pos + 8])[0]
        pos += 8
        tick, status, on = 0, 0, {}

        def varlen():
            nonlocal pos
            v = 0
            while True:
                b = d[pos]; pos += 1
                v = (v << 7) | (b & 0x7F)
                if b < 0x80: return v

        while pos < end:
            tick += varlen()
            b = d[pos]
            if b == 0xFF:                                   # meta
                kind = d[pos + 1]; pos += 2
                n = varlen()
                if kind == 0x51 and n == 3 and tempo is None:
                    tempo = int.from_bytes(d[pos:pos + 3], "big")
                pos += n
            elif b in (0xF0, 0xF7):                         # sysex
                pos += 1
                n = varlen(); pos += n
            else:
                if b & 0x80: status = b; pos += 1           # else running status
                kind, ch = status & 0xF0, status & 0x0F
                if kind in (0xC0, 0xD0):
                    pos += 1
                else:
                    a, v = d[pos], d[pos + 1]; pos += 2
                    if kind == 0x90 and v > 0:
                        on.setdefault((ch, a), []).append((tick, v))
                    elif kind == 0x80 or (kind == 0x90 and v == 0):
                        if on.get((ch, a)):
                            t0, vel = on[(ch, a)].pop(0)
                            notes.append((t0, tick, ch, a, vel))
        pos = end
    notes.sort()
    return div, tempo, notes


def list_channels(notes, ppq):
    print("channel  notes  lowest  highest  most at once  first beat  last beat")
    for ch in sorted({n[2] for n in notes}):
        ns = [n for n in notes if n[2] == ch]
        edges = sorted([(n[0], 1) for n in ns] + [(n[1], -1) for n in ns])
        cur = most = 0
        for _, dlt in edges: cur += dlt; most = max(most, cur)
        name = lambda p: "%s%d" % (NOTE_NAMES[p % 12], p // 12 - 1)
        tag = "  (drums)" if ch == 9 else ""
        print("%5d   %6d  %6s  %7s  %12d  %10.1f  %9.1f%s" % (
            ch + 1, len(ns), name(min(n[3] for n in ns)), name(max(n[3] for n in ns)), most,
            ns[0][0] / ppq, ns[-1][1] / ppq, tag))
    print("\nA sung melody is usually a channel with one note at a time and a mid range.")


# ── Song building ───────────────────────────────────────────────────────────

def buzz_note(pitch):
    octave = pitch // 12 - 1                                # MIDI 69 (A4) -> A-4, 440 Hz in RoboSing
    if not 0 <= octave <= 9: return None
    return octave * 16 + pitch % 12 + 1


def blob_cols(machine, patname, ncols, colevents):
    """Editor blob for a machine with ncols single-track columns (the Master's tempo pattern)."""
    body = struct.pack("<i", 1) + patname.encode("latin1") + b"\x00" + struct.pack("<iii", 4, 4, ncols)
    for c in range(ncols):
        evs = sorted(colevents.get(c, []))
        body += machine.encode("latin1") + b"\x00" + struct.pack("<ii", c, 0) + b"\x00" + struct.pack("<i", len(evs))
        for row, val in evs: body += struct.pack("<ii", row * 240, val)
        body += struct.pack("<iiii", 4, 4, 4, 4)
    return b"\xff\x01" + struct.pack("<I", 6 + len(body)) + body


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("midi")
    ap.add_argument("--list", action="store_true", help="show the channels and stop")
    ap.add_argument("--channel", type=int, help="melody channel, 1-16")
    ap.add_argument("--tpb", type=int, default=4, help="rows per beat (default 4)")
    ap.add_argument("--gap", type=float, default=1.5, help="a rest this many beats long starts a new phrase (default 1.5)")
    ap.add_argument("--transpose", type=int, default=0, help="semitones")
    ap.add_argument("--lyrics", help="text file, one line per phrase (lines starting with # are ignored)")
    ap.add_argument("--bpm", type=int, help="override the tempo in the MIDI file")
    ap.add_argument("-o", "--out", help="output .bmxml")
    a = ap.parse_args()

    ppq, tempo, notes = read_midi(a.midi)
    if a.list or a.channel is None:
        list_channels(notes, ppq)
        if not a.list: print("\nPick one with --channel N.")
        return

    bpm = a.bpm or (round(60e6 / tempo) if tempo else 120)
    bpm = max(16, min(500, bpm))
    row_ticks = ppq / a.tpb
    mel = [n for n in notes if n[2] == a.channel - 1]
    if not mel: raise SystemExit("no notes on channel %d" % a.channel)

    # One note per row: the highest pitch wins when several start together.
    by_row = {}
    for t0, t1, _, pitch, vel in mel:
        r0 = int(round(t0 / row_ticks))
        r1 = max(r0 + 1, int(round(t1 / row_ticks)))
        if r0 not in by_row or pitch > by_row[r0][1]: by_row[r0] = (r1, pitch, vel)
    rows = sorted(by_row)

    # Phrases: split at rests of --gap beats or more, at most 100.
    gap_rows = a.gap * a.tpb
    phrases, cur = [], [rows[0]]
    for prev, r in zip(rows, rows[1:]):
        if r - by_row[prev][0] >= gap_rows: phrases.append(cur); cur = []
        cur.append(r)
    phrases.append(cur)
    while len(phrases) > 100:                               # merge the two shortest neighbours
        i = min(range(len(phrases) - 1), key=lambda k: len(phrases[k]) + len(phrases[k + 1]))
        phrases[i:i + 2] = [phrases[i] + phrases[i + 1]]

    if a.lyrics:
        lines = [l.strip() for l in open(a.lyrics, encoding="utf-8") if l.strip() and not l.lstrip().startswith("#")]
        if len(lines) != len(phrases):
            print("warning: %d lyric lines for %d phrases; missing lines are filled with la" % (len(lines), len(phrases)))
        texts = [lines[i] if i < len(lines) else " ".join(["la"] * len(p)) for i, p in enumerate(phrases)]
    else:
        texts = [" ".join(["la"] * len(p)) for p in phrases]

    length = (max(by_row[r][0] for r in rows) + 2 * a.tpb * 4)
    length = int(math.ceil(length / (a.tpb * 4.0)) * a.tpb * 4)

    ev, skipped = {}, 0
    def at(row, col, val): ev.setdefault((0, col), []).append((row, val))
    for pi, prows in enumerate(phrases):
        at(prows[0], be.PHRASE, pi + 1)
    for i, r in enumerate(rows):
        end, pitch, vel = by_row[r]
        note = buzz_note(pitch + a.transpose)
        if note is None: skipped += 1; continue
        at(r, be.NOTE, note)
        at(r, be.VEL, max(40, min(127, vel)))
        nxt = rows[i + 1] if i + 1 < len(rows) else None
        if nxt is None or end < nxt: at(end, be.NOTE, be.NOTE_OFF)

    # Assemble from the reference save.
    raw = open(be.REF, "rb").read().decode("utf-8-sig")
    lib = {}
    for b in be.machine_blocks(raw):
        lib.setdefault(re.search(r"<Library>(.*?)</Library>", b).group(1), []).append(b)
    master, master_ed = lib["Master"][0], lib["Modern Pattern Editor"][0]
    g, e = lib["Pedal RoboSing"][0], lib["Modern Pattern Editor"][1]
    name, b64 = "Singer", lambda x: base64.b64encode(x).decode()

    g = be.sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % b64(be.build_state(texts)), g, re.S)
    g = be.sub1(r"<Name>RoboSing</Name>", "<Name>%s</Name>" % name, g)
    g = be.sub1(r"<Length>\d+</Length>", "<Length>%d</Length>" % length, g)
    settings = dict(be.CLEAN, **{"Play Mode": 1, "Hold": 1, "Tune": 60, "Vib Depth": 20, "Portamento": 15, "Glide": 10})
    for k, v in settings.items(): g = be.set_global(g, k, v)
    e = be.sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % b64(be.build_blob(name, "00", ev)), e, re.S)

    # Tempo: stored Master values, plus a row-0 event on the Master's own pattern for older builds.
    master = be.sub1(r"(<Value>)\d+(</Value>\s*</Value>\s*</Values>\s*<Type>Word</Type>\s*<Name>BPM</Name>)",
                     lambda m: m.group(1) + str(bpm) + m.group(2), master)
    master = be.sub1(r"(<Value>)\d+(</Value>\s*</Value>\s*</Values>\s*<Type>Byte</Type>\s*<Name>TPB</Name>)",
                     lambda m: m.group(1) + str(a.tpb) + m.group(2), master)
    master = be.sub1(r"<Patterns />", "<Patterns>\r\n        <Pattern>\r\n          <Name>00</Name>\r\n          <Length>%d</Length>\r\n"
                     "          <Columns />\r\n        </Pattern>\r\n      </Patterns>" % length, master)
    master_ed = be.sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % b64(blob_cols("Master", "00", 3, {1: [(0, bpm)], 2: [(0, a.tpb)]})), master_ed, re.S)

    out = raw
    i0, i1 = out.index("<Machines>") + 10, out.index("</Machines>")
    out = out[:i0] + "\r\n    " + "\r\n    ".join([master, master_ed, g, e]) + "\r\n  " + out[i1:]
    out = be.sub1(r"<Sequences>.*?</Sequences>",
                  lambda m: "<Sequences>\r\n" + be.SEQ % (length, "Master") + be.SEQ % (length, name) + "  </Sequences>", out, re.S)
    out = be.sub1(r"<MachineConnections>.*?</MachineConnections>",
                  lambda m: "<MachineConnections>\r\n" + be.CONN % ("_x0001_pe1", 16384) + be.CONN % ("_x0001_pe2", 16384)
                  + be.CONN % (name, 13000) + "  </MachineConnections>", out, re.S)
    out = be.sub1(r"<LoopEnd>\d+</LoopEnd>", "<LoopEnd>%d</LoopEnd>" % length, out)
    out = be.sub1(r"<SongEnd>\d+</SongEnd>", "<SongEnd>%d</SongEnd>" % length, out)
    title = os.path.splitext(os.path.basename(a.out or a.midi))[0]
    out = be.sub1(r'Name="[^"]*"', 'Name="%s"' % re.sub(r'[^\w \-]', "_", title), out)

    out_path = a.out or os.path.splitext(a.midi)[0] + ".bmxml"
    open(out_path, "wb").write(b"\xef\xbb\xbf" + out.encode("utf-8"))
    tmpl = os.path.splitext(out_path)[0] + ".lyrics.txt"
    with open(tmpl, "w", encoding="utf-8") as f:
        f.write("# One line per phrase. Replace each line with words that have the stated number of syllables.\n")
        for i, (p, t) in enumerate(zip(phrases, texts)):
            bar = p[0] // (a.tpb * 4) + 1
            f.write("# phrase %d: %d syllables, starts bar %d (row %d)\n%s\n" % (i + 1, len(p), bar, p[0], t))

    print("%s: %d notes in %d phrases, %d rows (%d bars) at %d BPM, %d rows per beat"
          % (os.path.basename(out_path), len(rows) - skipped, len(phrases), length, length // (a.tpb * 4), bpm, a.tpb))
    if len(mel) != len(rows): print("  %d notes shared a row with another and were dropped" % (len(mel) - len(rows)))
    if skipped: print("  %d notes were outside the playable range and were skipped" % skipped)
    print("  lyric template: %s" % os.path.basename(tmpl))


if __name__ == "__main__":
    main()
