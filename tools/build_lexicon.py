#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 thepedal
"""Pack the CMU Pronouncing Dictionary into Speech/cmudict.bin.gz, the lexicon
embedded in the machine.

  python tools/build_lexicon.py path/to/cmudict.dict

Source: https://github.com/cmusphinx/cmudict (this build: commit 74790861).
The dictionary is Copyright (C) 1993-2015 Carnegie Mellon University; its
licence is in Speech/cmudict.LICENSE.txt.

Format (before gzip): records sorted by word in byte order, each
    WORD (A-Z and apostrophe)  00  one byte per phoneme  00
A phoneme byte is 1 + its index in PHONES, plus 0x40 if it carries the primary
stress. Only the first pronunciation of a word is kept; words with other
characters are skipped. An unstressed AH is written as AX (schwa), which the
machine has as a separate sound.
"""
import gzip, os, re, sys

PHONES = ["AA", "AE", "AH", "AO", "AW", "AY", "B", "CH", "D", "DH", "EH", "ER", "EY", "F", "G", "HH", "IH", "IY",
          "JH", "K", "L", "M", "N", "NG", "OW", "OY", "P", "R", "S", "SH", "T", "TH", "UH", "UW", "V", "W", "Y",
          "Z", "ZH", "AX"]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Speech", "cmudict.bin.gz")


def main():
    src = sys.argv[1]
    entries = {}
    for line in open(src, encoding="latin-1"):
        line = line.split("#")[0].strip()
        if not line: continue
        word, *pron = line.split()
        if "(" in word: continue                          # alternates: keep the first
        word = word.upper()
        if not re.fullmatch(r"[A-Z][A-Z']*", word): continue
        code = bytearray()
        for p in pron:
            name, stress = (p[:-1], p[-1]) if p[-1].isdigit() else (p, "")
            if name == "AH" and stress == "0": name = "AX"
            code.append(1 + PHONES.index(name) + (0x40 if stress == "1" else 0))
        entries.setdefault(word, bytes(code))
    blob = bytearray()
    for word in sorted(entries, key=lambda w: w.encode("ascii")):
        blob += word.encode("ascii") + b"\x00" + entries[word] + b"\x00"
    with open(OUT, "wb") as f:
        with gzip.GzipFile(fileobj=f, mode="wb", compresslevel=9, mtime=0, filename="") as g:
            g.write(bytes(blob))
    print("%d words, %d bytes packed, %d bytes gzipped -> %s" % (len(entries), len(blob), os.path.getsize(OUT), os.path.normpath(OUT)))


if __name__ == "__main__":
    main()
