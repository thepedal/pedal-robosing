#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 thepedal
"""Turn a MIDI file into a ReBuzz song: drums on one Pedal DrumGrid, and
optionally one melody line sung by a Pedal RoboSing.

  python midi_to_song.py song.mid --list
  python midi_to_song.py song.mid -o song.bmxml                      # drums only (channel 10)
  python midi_to_song.py song.mid --melody-channel 1 -o song.bmxml   # drums + sung melody
  python midi_to_song.py song.mid --melody-channel 1 --no-drums -o song.bmxml

Drums: every drum sound on the drum channel gets its own DrumGrid lane, in kit
order (kicks, snares, hats, toms, cymbals, the rest). Each hit is a 1 in that
lane's Trig column with the MIDI velocity in the lane's Velocity column. The
lane list is printed and written to <name>.lanes.txt so you know which sample
to load where. DrumGrid is left without samples.

Melody: as midi_to_robosing.py. One syllable per note; the line is cut into
phrases at rests, each phrase is one line of the phrase list filled with "la",
and <name>.lyrics.txt lists the syllables each line needs.

Machine blocks are taken from songs ReBuzz saved (drums_singer_ref.bmxml for
DrumGrid and RoboSing, robosing_ref.bmxml for the Master and its tempo
pattern); nothing is written by hand.
"""
import argparse, base64, math, os, re, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_example as be
import midi_to_robosing as mr

GM = {35: "Bass drum 2", 36: "Bass drum 1", 37: "Side stick", 38: "Snare", 39: "Hand clap", 40: "Electric snare",
      41: "Low floor tom", 42: "Closed hi-hat", 43: "High floor tom", 44: "Pedal hi-hat", 45: "Low tom",
      46: "Open hi-hat", 47: "Low-mid tom", 48: "Hi-mid tom", 49: "Crash 1", 50: "High tom", 51: "Ride 1",
      52: "China", 53: "Ride bell", 54: "Tambourine", 55: "Splash", 56: "Cowbell", 57: "Crash 2", 58: "Vibraslap",
      59: "Ride 2", 60: "High bongo", 61: "Low bongo", 62: "Mute high conga", 63: "Open high conga", 64: "Low conga",
      65: "High timbale", 66: "Low timbale", 67: "High agogo", 68: "Low agogo", 69: "Cabasa", 70: "Maracas",
      75: "Claves", 76: "High wood block", 77: "Low wood block", 80: "Mute triangle", 81: "Open triangle"}
KIT_ORDER = [35, 36, 38, 40, 37, 39, 42, 44, 46, 41, 43, 45, 47, 48, 50, 49, 51, 52, 53, 55, 57, 59]
LANES = 16


def layout(block):
    """(global names, track names, track count) from a saved machine block."""
    groups = re.findall(r"<ParameterGroup>\s*<Type>(\w+)</Type>(.*?)<TrackCount>(\d+)</TrackCount>\s*</ParameterGroup>", block, re.S)
    names = lambda body: [x.replace("_x0020_", " ") for x in re.findall(r"</Values>\s*<Type>\w+</Type>\s*<Name>(.*?)</Name>|<Values />\s*<Type>\w+</Type>\s*<Name>(.*?)</Name>", body) for x in x if x]
    g = [b for t, b, c in groups if t == "Global"][0]
    t = [(b, int(c)) for ty, b, c in groups if ty == "Track"][0]
    return names(g), names(t[0]), t[1]


def build_blob(machine, patname, nglobal, ntrackcols, tracks, events):
    """Editor blob. events: (track, col) -> [(row, value)]; global columns use track 0."""
    def rec(col, track):
        evs = sorted(events.get((track, col), []))
        r = machine.encode("latin1") + b"\x00" + struct.pack("<ii", col, track) + b"\x00" + struct.pack("<i", len(evs))
        for row, val in evs: r += struct.pack("<ii", row * 240, val)
        return r + struct.pack("<iiii", 4, 4, 4, 4)
    recs = [rec(c, 0) for c in range(nglobal)]
    for t in range(tracks):
        recs += [rec(nglobal + k, t) for k in range(ntrackcols)]
    body = struct.pack("<i", 1) + patname.encode("latin1") + b"\x00" + struct.pack("<iii", 4, 4, len(recs)) + b"".join(recs)
    return b"\xff\x01" + struct.pack("<I", 6 + len(body)) + body


def plan_drums(notes, channel, row_ticks):
    hits = [n for n in notes if n[2] == channel - 1]
    count = {}
    for n in hits: count[n[3]] = count.get(n[3], 0) + 1
    order = [p for p in KIT_ORDER if p in count] + sorted(p for p in count if p not in KIT_ORDER)
    dropped = []
    if len(order) > LANES:
        keep = set(sorted(order, key=lambda p: -count[p])[:LANES])
        dropped = [p for p in order if p not in keep]
        order = [p for p in order if p in keep]
    lane = {p: i for i, p in enumerate(order)}
    grid, merged, late = {}, 0, 0                        # (row, lane) -> velocity
    for t0, _, _, pitch, vel in hits:
        if pitch not in lane: continue
        r = int(round(t0 / row_ticks))
        if abs(t0 / row_ticks - r) > 0.2: late += 1
        key = (r, lane[pitch])
        if key in grid: merged += 1
        grid[key] = max(grid.get(key, 0), vel)
    return order, count, grid, dropped, merged, late


def plan_melody(notes, channel, row_ticks, tpb, gap, transpose, lyrics, cols):
    NOTE, VEL, PHRASE = cols
    mel = [n for n in notes if n[2] == channel - 1]
    if not mel: raise SystemExit("no notes on channel %d" % channel)
    by_row = {}
    for t0, t1, _, pitch, vel in mel:
        r0 = int(round(t0 / row_ticks)); r1 = max(r0 + 1, int(round(t1 / row_ticks)))
        if r0 not in by_row or pitch > by_row[r0][1]: by_row[r0] = (r1, pitch, vel)
    rows = sorted(by_row)
    phrases, cur = [], [rows[0]]
    for prev, r in zip(rows, rows[1:]):
        if r - by_row[prev][0] >= gap * tpb: phrases.append(cur); cur = []
        cur.append(r)
    phrases.append(cur)
    while len(phrases) > 100:
        i = min(range(len(phrases) - 1), key=lambda k: len(phrases[k]) + len(phrases[k + 1]))
        phrases[i:i + 2] = [phrases[i] + phrases[i + 1]]
    la = lambda p: " ".join(["la"] * len(p))
    texts = [la(p) for p in phrases]
    if lyrics:
        lines = [l.strip() for l in open(lyrics, encoding="utf-8") if l.strip() and not l.lstrip().startswith("#")]
        if len(lines) != len(phrases):
            print("warning: %d lyric lines for %d phrases; missing lines are filled with la" % (len(lines), len(phrases)))
        texts = [lines[i] if i < len(lines) else la(p) for i, p in enumerate(phrases)]
    ev, skipped = {}, 0
    def at(row, col, val): ev.setdefault((0, col), []).append((row, val))
    for i, p in enumerate(phrases): at(p[0], PHRASE, i + 1)
    for i, r in enumerate(rows):
        end, pitch, vel = by_row[r]
        note = mr.buzz_note(pitch + transpose)
        if note is None: skipped += 1; continue
        at(r, NOTE, note); at(r, VEL, max(40, min(127, vel)))
        nxt = rows[i + 1] if i + 1 < len(rows) else None
        if nxt is None or end < nxt: at(end, NOTE, be.NOTE_OFF)
    return ev, texts, phrases, max(by_row[r][0] for r in rows), len(rows) - skipped, len(mel) - len(rows), skipped


def blocks_of(path):
    raw = open(path, "rb").read().decode("utf-8-sig")
    out = {}
    blocks = be.machine_blocks(raw)
    editors = {re.search(r"<Name>(.*?)</Name>", b).group(1): b for b in blocks
               if "<Library>Modern Pattern Editor</Library>" in b}
    for b in blocks:
        lib = re.search(r"<Library>(.*?)</Library>", b).group(1)
        if lib == "Modern Pattern Editor": continue
        ed = re.search(r"<EditorMachine>(.*?)</EditorMachine>", b)
        out[lib] = (b, editors.get(ed.group(1)) if ed else None)
    return raw, out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("midi")
    ap.add_argument("--list", action="store_true", help="show the channels and stop")
    ap.add_argument("--drum-channel", type=int, default=10)
    ap.add_argument("--no-drums", action="store_true")
    ap.add_argument("--melody-channel", type=int, help="channel to sing, 1-16")
    ap.add_argument("--tpb", type=int, default=4, help="rows per beat (default 4; use 8 for fast fills)")
    ap.add_argument("--gap", type=float, default=1.5, help="melody: a rest this many beats long starts a new phrase")
    ap.add_argument("--transpose", type=int, default=0, help="melody: semitones")
    ap.add_argument("--lyrics", help="melody: text file, one line per phrase")
    ap.add_argument("--bpm", type=int, help="override the tempo in the MIDI file")
    ap.add_argument("--ref", default=os.path.join(HERE, "drums_singer_ref.bmxml"), help="saved song holding a DrumGrid and a RoboSing")
    ap.add_argument("-o", "--out")
    a = ap.parse_args()

    ppq, tempo, notes = mr.read_midi(a.midi)
    if a.list:
        mr.list_channels(notes, ppq); return
    drums, melody = not a.no_drums, a.melody_channel is not None
    if not drums and not melody: raise SystemExit("nothing to do: --no-drums and no --melody-channel")

    bpm = max(16, min(500, a.bpm or (round(60e6 / tempo) if tempo else 120)))
    row_ticks = ppq / a.tpb
    bar = a.tpb * 4
    b64 = lambda x: base64.b64encode(x).decode()
    out_path = a.out or os.path.splitext(a.midi)[0] + ".bmxml"
    stem = os.path.splitext(out_path)[0]

    raw, ref = blocks_of(a.ref)
    _, mref = blocks_of(be.REF)
    master, master_ed = mref["Master"]
    if master_ed is None: raise SystemExit("robosing_ref.bmxml has no Master pattern editor")

    parts, last_row, report = [], 0, []                  # parts: (name, gen block, editor block, events, amp)

    if drums:
        if "Pedal DrumGrid" not in ref: raise SystemExit("the reference song has no Pedal DrumGrid")
        g, e = ref["Pedal DrumGrid"]
        gl, tr, ntr = layout(g)
        trig = [gl.index("Trig %d" % (i + 1)) for i in range(LANES)]
        velcol = len(gl) + tr.index("Velocity")
        name = re.search(r"<Name>(.*?)</Name>", g).group(1)
        ref_blob = base64.b64decode(re.search(r"<Data>(.*?)</Data>", e).group(1))
        assert build_blob(name, "00", len(gl), len(tr), ntr, {}) == ref_blob, "DrumGrid pattern layout differs from the reference save"
        order, count, grid, dropped, merged, late = plan_drums(notes, a.drum_channel, row_ticks)
        if not order: raise SystemExit("no notes on drum channel %d" % a.drum_channel)
        ev, held = {}, {}
        for (r, ln), vel in sorted(grid.items()):
            ev.setdefault((0, trig[ln]), []).append((r, 1))
            v = max(1, min(127, vel))
            if held.get(ln) != v:                        # Velocity is held per lane: write it only when it changes
                ev.setdefault((ln, velcol), []).append((r, v)); held[ln] = v
        last_row = max(last_row, max(r for r, _ in grid))
        parts.append((name, g, e, (len(gl), len(tr), ntr, ev), 16384))
        lanes = ["lane %2d  %-18s (MIDI note %d)  %d hits" % (i + 1, GM.get(p, "Percussion"), p, count[p]) for i, p in enumerate(order)]
        open(stem + ".lanes.txt", "w", encoding="utf-8").write("\n".join(lanes) + "\n")
        report.append("drums: %d hits on %d lanes" % (len(grid), len(order)))
        report += ["  " + l for l in lanes]
        if dropped: report.append("  more than 16 drum sounds: dropped the least used: %s" % ", ".join(GM.get(p, str(p)) for p in dropped))
        if merged: report.append("  %d hits landed on a row already taken by the same drum and were merged" % merged)
        if late: report.append("  %d hits sit well between rows at %d rows per beat; try --tpb %d" % (late, a.tpb, a.tpb * 2))

    if melody:
        if "Pedal RoboSing" not in ref: raise SystemExit("the reference song has no Pedal RoboSing")
        g, e = ref["Pedal RoboSing"]
        gl, tr, ntr = layout(g)
        old = re.search(r"<Name>(.*?)</Name>", g).group(1)
        ref_blob = base64.b64decode(re.search(r"<Data>(.*?)</Data>", e).group(1))
        assert build_blob(old, "00", len(gl), len(tr), ntr, {}) == ref_blob, "RoboSing pattern layout differs from the reference save"
        cols = (len(gl) + tr.index("SetNote"), len(gl) + tr.index("Velocity"), len(gl) + tr.index("Phrase"))
        ev, texts, phrases, end, kept, shared, skipped = plan_melody(notes, a.melody_channel, row_ticks, a.tpb, a.gap, a.transpose, a.lyrics, cols)
        last_row = max(last_row, end)
        g = be.sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % b64(be.build_state(texts)), g, re.S)
        g = be.sub1(r"<Name>%s</Name>" % re.escape(old), "<Name>Singer</Name>", g)
        for k, v in dict(be.CLEAN, **{"Play Mode": 1, "Hold": 1, "Tune": 60, "Vib Depth": 20, "Portamento": 15, "Glide": 10}).items():
            g = be.set_global(g, k, v)
        parts.append(("Singer", g, e, (len(gl), len(tr), ntr, ev), 13000))
        with open(stem + ".lyrics.txt", "w", encoding="utf-8") as f:
            f.write("# One line per phrase. Replace each line with words that have the stated number of syllables.\n")
            for i, (p, t) in enumerate(zip(phrases, texts)):
                f.write("# phrase %d: %d syllables, starts bar %d (row %d)\n%s\n" % (i + 1, len(p), p[0] // bar + 1, p[0], t))
        report.append("melody: %d notes in %d phrases (lyric template: %s)" % (kept, len(phrases), os.path.basename(stem + ".lyrics.txt")))
        if shared: report.append("  %d notes shared a row with another and were dropped" % shared)
        if skipped: report.append("  %d notes were outside the playable range and were skipped" % skipped)

    length = int(math.ceil((last_row + 1 + bar) / float(bar)) * bar)

    # Master: stored tempo, plus a row-0 tempo event on its own pattern.
    num = lambda v, ty, nm: (r"(<Value>)\d+(</Value>\s*</Value>\s*</Values>\s*<Type>%s</Type>\s*<Name>%s</Name>)" % (ty, nm),
                             lambda m: m.group(1) + str(v) + m.group(2))
    master = be.sub1(*num(bpm, "Word", "BPM"), master)
    master = be.sub1(*num(a.tpb, "Byte", "TPB"), master)
    master = be.sub1(r"<Patterns />", "<Patterns>\r\n        <Pattern>\r\n          <Name>00</Name>\r\n          <Length>%d</Length>\r\n"
                     "          <Columns />\r\n        </Pattern>\r\n      </Patterns>" % length, master)
    master_ed = be.sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % b64(mr.blob_cols("Master", "00", 3, {1: [(0, bpm)], 2: [(0, a.tpb)]})), master_ed, re.S)

    out_blocks, conns, seqs = [master, master_ed], [be.CONN % ("_x0001_pe1", 16384)], [be.SEQ % (length, "Master")]
    for i, (name, g, e, (ng, nt, ntr, ev), amp) in enumerate(parts):
        ed = "_x0001_pe%d" % (i + 2)
        g = be.sub1(r"<EditorMachine>.*?</EditorMachine>", "<EditorMachine>%s</EditorMachine>" % ed, g)
        g = be.sub1(r"<Length>\d+</Length>", "<Length>%d</Length>" % length, g)
        e = be.sub1(r"<Data>.*?</Data>", "<Data>%s</Data>" % b64(build_blob(name, "00", ng, nt, ntr, ev)), e, re.S)
        e = be.sub1(r"<Name>_x0001_pe\d+</Name>", "<Name>%s</Name>" % ed, e)
        out_blocks += [g, e]
        conns += [be.CONN % (ed, 16384), be.CONN % (name, amp)]
        seqs.append(be.SEQ % (length, name))

    out = raw
    i0, i1 = out.index("<Machines>") + 10, out.index("</Machines>")
    out = out[:i0] + "\r\n    " + "\r\n    ".join(out_blocks) + "\r\n  " + out[i1:]
    out = be.sub1(r"<Sequences>.*?</Sequences>", lambda m: "<Sequences>\r\n" + "".join(seqs) + "  </Sequences>", out, re.S)
    out = be.sub1(r"<MachineConnections>.*?</MachineConnections>", lambda m: "<MachineConnections>\r\n" + "".join(conns) + "  </MachineConnections>", out, re.S)
    out = be.sub1(r"<LoopEnd>\d+</LoopEnd>", "<LoopEnd>%d</LoopEnd>" % length, out)
    out = be.sub1(r"<SongEnd>\d+</SongEnd>", "<SongEnd>%d</SongEnd>" % length, out)
    out = be.sub1(r'Name="[^"]*"', 'Name="%s"' % re.sub(r"[^\w \-]", "_", os.path.basename(stem)), out)
    open(out_path, "wb").write(b"\xef\xbb\xbf" + out.encode("utf-8"))

    print("%s: %d rows (%d bars) at %d BPM, %d rows per beat" % (os.path.basename(out_path), length, length // bar, bpm, a.tpb))
    print("\n".join(report))


if __name__ == "__main__":
    main()
