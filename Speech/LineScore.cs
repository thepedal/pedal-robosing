// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — the melody of one line.
//
// One step per syllable: a gap of silence before it, a length, and a pitch
// relative to the note that starts the line. Times are in ticks. Stored with the
// song as text, one "gap,length,pitch" triple per syllable: "0,4,0 0,2,3 2,4,-2".
// A syllable sung over several notes (v1.8) adds "+length,pitch" for each further
// note: "0,2,0+2,4+4,7" is one syllable on three notes. The vowel is held across them.
// The phrase bank parses its own copy and never changes it; the audio thread only
// reads that one. The editor works on a separate copy.

using System;
using System.Text;

namespace PedalRoboSing.Speech
{
    public sealed class LineScore
    {
        public const int DefaultLen = 2, MaxLen = 64, MaxGap = 64, PitchRange = 24, MaxNotes = 8;

        /// <summary>Per syllable: silence before it, and the length and pitch of its first note.</summary>
        public readonly int[] Gap, Len, Pitch;
        // Further notes of a syllable, after the first; null where there are none
        readonly int[][] _xLen, _xPitch;
        public int Count => Len.Length;

        public LineScore(int n)
        {
            Gap = new int[n]; Len = new int[n]; Pitch = new int[n];
            _xLen = new int[n][]; _xPitch = new int[n][];
            for (int i = 0; i < n; i++) Len[i] = DefaultLen;
        }

        public int GapAt(int k) => (uint)k < (uint)Gap.Length ? Gap[k] : 0;
        /// <summary>The first note's pitch: what the play modes without the melody's timing use.</summary>
        public int PitchAt(int k) => (uint)k < (uint)Pitch.Length ? Pitch[k] : 0;
        /// <summary>The whole syllable's length in ticks, all its notes together.</summary>
        public int LenAt(int k)
        {
            if ((uint)k >= (uint)Len.Length) return DefaultLen;
            int t = Len[k]; var x = _xLen[k];
            if (x != null) for (int i = 0; i < x.Length; i++) t += x[i];
            return t;
        }

        // ── Notes within a syllable ──────────────────────────────────────────
        public int Notes(int k) => (uint)k < (uint)Len.Length ? 1 + (_xLen[k]?.Length ?? 0) : 1;
        public bool HasMore(int k) => (uint)k < (uint)Len.Length && _xLen[k] != null;
        public int NoteLen(int k, int i) => i == 0 ? Len[k] : _xLen[k][i - 1];
        public int NotePitch(int k, int i) => i == 0 ? Pitch[k] : _xPitch[k][i - 1];
        public void SetNoteLen(int k, int i, int v) { if (i == 0) Len[k] = v; else _xLen[k][i - 1] = v; }
        public void SetNotePitch(int k, int i, int v) { if (i == 0) Pitch[k] = v; else _xPitch[k][i - 1] = v; }

        /// <summary>The pitch sounding `ticks` into syllable k (from its vowel's beat). Backwards, its notes run last to first.</summary>
        public int PitchAtTime(int k, float ticks, bool backwards)
        {
            if ((uint)k >= (uint)Len.Length) return 0;
            int n = Notes(k);
            if (n == 1) return Pitch[k];
            float t = 0;
            for (int j = 0; j < n; j++)
            {
                int i = backwards ? n - 1 - j : j;
                t += NoteLen(k, i);
                if (ticks < t) return NotePitch(k, i);
            }
            return NotePitch(k, backwards ? 0 : n - 1);
        }

        /// <summary>Cut note i of syllable k in two, `at` ticks from its start. Editor only (allocates).</summary>
        public bool Split(int k, int i, int at)
        {
            int n = Notes(k);
            if ((uint)k >= (uint)Len.Length || i < 0 || i >= n || n >= MaxNotes) return false;
            int len = NoteLen(k, i), pitch = NotePitch(k, i);
            if (len < 2) return false;
            at = Math.Clamp(at, 1, len - 1);
            var L = new int[n + 1]; var P = new int[n + 1];
            for (int j = 0, w = 0; j < n; j++)
            {
                if (j == i) { L[w] = at; P[w++] = pitch; L[w] = len - at; P[w++] = pitch; }
                else { L[w] = NoteLen(k, j); P[w++] = NotePitch(k, j); }
            }
            Store(k, L, P);
            return true;
        }

        /// <summary>Join note i of syllable k to the note before it (the first note takes the one after). Editor only.</summary>
        public bool Join(int k, int i)
        {
            int n = Notes(k);
            if ((uint)k >= (uint)Len.Length || n < 2 || i < 0 || i >= n) return false;
            if (i == 0) i = 1;                                   // the first note swallows the second
            var L = new int[n - 1]; var P = new int[n - 1];
            for (int j = 0, w = 0; j < n; j++)
            {
                if (j == i) { L[w - 1] = Math.Min(MaxLen, L[w - 1] + NoteLen(k, j)); continue; }
                L[w] = NoteLen(k, j); P[w++] = NotePitch(k, j);
            }
            Store(k, L, P);
            return true;
        }

        void Store(int k, int[] L, int[] P)
        {
            Len[k] = L[0]; Pitch[k] = P[0];
            if (L.Length == 1) { _xLen[k] = null; _xPitch[k] = null; return; }
            var xl = new int[L.Length - 1]; var xp = new int[L.Length - 1];
            Array.Copy(L, 1, xl, 0, xl.Length); Array.Copy(P, 1, xp, 0, xp.Length);
            _xLen[k] = xl; _xPitch[k] = xp;
        }

        public int TotalTicks
        {
            get { int t = 0; for (int i = 0; i < Count; i++) t += Gap[i] + LenAt(i); return t; }
        }

        /// <summary>Parse stored text into a score of exactly n syllables: extra steps are dropped, missing ones get defaults.</summary>
        public static LineScore Parse(string text, int n)
        {
            var s = new LineScore(Math.Max(0, n));
            if (string.IsNullOrWhiteSpace(text)) return s;
            var steps = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < steps.Length && i < n; i++)
            {
                var notes = steps[i].Split('+');
                var f = notes[0].Split(',');
                if (f.Length != 3) continue;
                if (int.TryParse(f[0], out int g)) s.Gap[i] = Math.Clamp(g, 0, MaxGap);
                if (int.TryParse(f[1], out int l)) s.Len[i] = Math.Clamp(l, 1, MaxLen);
                if (int.TryParse(f[2], out int p)) s.Pitch[i] = Math.Clamp(p, -PitchRange, PitchRange);

                int more = Math.Min(notes.Length - 1, MaxNotes - 1), ok = 0;
                if (more <= 0) continue;
                var xl = new int[more]; var xp = new int[more];
                for (int j = 1; j <= more; j++)
                {
                    var e = notes[j].Split(',');
                    if (e.Length != 2 || !int.TryParse(e[0], out int el) || !int.TryParse(e[1], out int ep)) continue;
                    xl[ok] = Math.Clamp(el, 1, MaxLen); xp[ok] = Math.Clamp(ep, -PitchRange, PitchRange); ok++;
                }
                if (ok == 0) continue;
                if (ok < more) { Array.Resize(ref xl, ok); Array.Resize(ref xp, ok); }
                s._xLen[i] = xl; s._xPitch[i] = xp;
            }
            return s;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Gap[i]).Append(',').Append(Len[i]).Append(',').Append(Pitch[i]);
                for (int j = 1; j < Notes(i); j++) sb.Append('+').Append(NoteLen(i, j)).Append(',').Append(NotePitch(i, j));
            }
            return sb.ToString();
        }

        public bool IsDefault
        {
            get
            {
                for (int i = 0; i < Count; i++) if (Gap[i] != 0 || Len[i] != DefaultLen || Pitch[i] != 0 || _xLen[i] != null) return false;
                return true;
            }
        }
    }
}
