// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — fitting the notes of a tune to the syllables of typed lines.
//
// The notes are reduced to one line of melody and snapped to song ticks. Then
// the tune is cut into as many stretches as there are lines, choosing the cuts
// so that each line gets about as many notes as it has syllables and the cuts
// fall on the rests. Inside a line, spare notes are tied onto the syllable before
// them (the notes that run into each other first), and a line short of notes has
// its longest notes split. Each line's melody is written from a starting note
// that is the key's root, so it reads the same with Key Lock on or off.
// Runs on the UI thread only.

using System;
using System.Collections.Generic;
using System.Text;
using PedalRoboSing.Speech;

namespace PedalRoboSing
{
    public sealed class ImportedLine
    {
        public string Score = "";
        public int StartTick;                       // song ticks from the start of the file
        public int Base;                            // starting note, semitone number (C-0 = 0)
        public int Follow;                          // ticks from this line's start to the next line's, 0 = last
        public int Notes, Syllables, Tied, Split;
    }

    public sealed class ImportResult
    {
        public ImportedLine[] Lines = Array.Empty<ImportedLine>();   // one per input line; null where nothing was put
        public int KeyRoot, KeyScale;               // scale: index into the machine's list (1 major, 2 minor)
        public bool KeyChanged;
        public int UsedNotes, TotalNotes, FirstLine = -1;
    }

    public static class MelodyImport
    {
        const int Major = 0xAB5, Minor = 0x5AD;

        sealed class N { public int S, E, P; }      // start and end in song ticks, pitch as a semitone number

        /// <param name="syllables">Syllables in each typed line, 0 for an empty one.</param>
        /// <param name="keyMask">The song's key now; kept if the tune fits it.</param>
        public static ImportResult Run(IList<MidiNote> midi, int division, int ticksPerBeat, int[] syllables, int keyRoot, int keyMask)
        {
            var r = new ImportResult { Lines = new ImportedLine[syllables.Length], KeyRoot = keyRoot };
            var notes = Melody(midi, division, ticksPerBeat);
            r.TotalNotes = notes.Count;
            var lines = new List<int>();
            for (int i = 0; i < syllables.Length; i++) if (syllables[i] > 0) lines.Add(i);
            if (notes.Count == 0 || lines.Count == 0) return r;

            PickKey(notes, r, keyRoot, keyMask);

            int[] count = Cut(notes, lines, syllables, ticksPerBeat);
            int at = 0; ImportedLine prev = null;
            for (int l = 0; l < lines.Count; l++)
            {
                int n = count[l];
                if (n == 0) continue;
                var il = Fit(notes, at, n, syllables[lines[l]], r.KeyRoot);
                r.Lines[lines[l]] = il;
                if (prev != null) prev.Follow = il.StartTick - prev.StartTick; else r.FirstLine = lines[l];
                prev = il;
                at += n;
            }
            r.UsedNotes = at;
            return r;
        }

        // One note at a time, the highest where several start together, on the song's tick grid
        static List<N> Melody(IList<MidiNote> midi, int division, int tpb)
        {
            var o = new List<N>();
            if (midi == null || division <= 0) return o;
            foreach (var m in midi)
            {
                int s = (int)Math.Round((double)m.Start * tpb / division), e = Math.Max(s + 1, (int)Math.Round((double)m.End * tpb / division));
                int p = m.Pitch - 12;                                  // MIDI 69 and Buzz 57 are both A 440
                if (o.Count > 0 && o[o.Count - 1].S == s) { if (p > o[o.Count - 1].P) { o[o.Count - 1].P = p; o[o.Count - 1].E = e; } continue; }
                if (o.Count > 0 && o[o.Count - 1].S > s) continue;     // out of order: ignore
                if (o.Count > 0 && o[o.Count - 1].E > s) o[o.Count - 1].E = s;   // a new note ends the one before
                o.Add(new N { S = s, E = e, P = p });
            }
            return o;
        }

        static void PickKey(List<N> notes, ImportResult r, int keyRoot, int keyMask)
        {
            var w = new double[12]; double total = 0;
            foreach (var n in notes) { int pc = ((n.P % 12) + 12) % 12; w[pc] += n.E - n.S; total += n.E - n.S; }
            double In(int root, int mask) { double s = 0; for (int pc = 0; pc < 12; pc++) if (((mask >> ((pc - root + 12) % 12)) & 1) != 0) s += w[pc]; return s / total; }

            keyMask &= 0xFFF;
            if (keyMask != 0xFFF && keyMask != 0 && In(keyRoot, keyMask) >= 0.95) { r.KeyRoot = keyRoot; r.KeyScale = -1; return; }
            int last = ((notes[notes.Count - 1].P % 12) + 12) % 12;
            double best = -1; r.KeyRoot = 0; r.KeyScale = 1;
            for (int root = 0; root < 12; root++)
                foreach (var (mask, scale) in new[] { (Major, 1), (Minor, 2) })
                {
                    // Fit first; between a major key and its relative minor, the one whose own root and fifth carry the tune
                    double s = In(root, mask) + 0.15 * (w[root] + 0.5 * w[(root + 7) % 12]) / total + (last == root ? 0.03 : 0);
                    if (s > best) { best = s; r.KeyRoot = root; r.KeyScale = scale; }
                }
            r.KeyChanged = true;
        }

        // How many notes each line takes, in order from the first note
        static int[] Cut(List<N> notes, List<int> lines, int[] syllables, int tpb)
        {
            int L = lines.Count, n = notes.Count;
            var cost = new double[L + 1, n + 1];
            var took = new int[L + 1, n + 1];
            for (int l = 0; l <= L; l++) for (int j = 0; j <= n; j++) cost[l, j] = double.MaxValue;
            cost[0, 0] = 0;
            double Rest(int j) => j <= 0 || j >= n ? 0 : Math.Max(0, notes[j].S - notes[j - 1].E) / (double)tpb;   // beats of silence before note j

            for (int l = 0; l < L; l++)
            {
                int s = syllables[lines[l]], most = s * LineScore.MaxNotes;
                for (int j = 0; j <= n; j++)
                {
                    if (cost[l, j] == double.MaxValue) continue;
                    // A cut is good on a rest and poor in the middle of a run
                    double open = l == 0 ? 0 : (Rest(j) > 0 ? -1.5 * Math.Min(Rest(j), 4) : 1.5);
                    double inside = 0;
                    for (int k = 0; k <= most && j + k <= n; k++)
                    {
                        if (k >= 2) inside += 2 * Math.Max(0, Rest(j + k - 1) - 0.5);          // a long rest inside a line
                        double c = cost[l, j] + inside + (k == 0 ? 8 * s : open + (k < s ? 6.0 * (s - k) : 1.0 * (k - s)));
                        if (c < cost[l + 1, j + k]) { cost[l + 1, j + k] = c; took[l + 1, j + k] = k; }
                    }
                }
            }
            int end = 0; double best = double.MaxValue;
            for (int j = 0; j <= n; j++) if (cost[L, j] < best) { best = cost[L, j]; end = j; }
            var count = new int[L];
            for (int l = L; l > 0; l--) { count[l - 1] = took[l, end]; end -= took[l, end]; }
            return count;
        }

        static ImportedLine Fit(List<N> all, int at, int n, int syl, int keyRoot)
        {
            var notes = new List<N>();
            for (int i = 0; i < n; i++) notes.Add(new N { S = all[at + i].S, E = all[at + i].E, P = all[at + i].P });
            var il = new ImportedLine { Notes = n, Syllables = syl, StartTick = notes[0].S };

            // Short of notes: halve the longest until there is one per syllable
            while (notes.Count < syl)
            {
                int longest = -1;
                for (int i = 0; i < notes.Count; i++) if (notes[i].E - notes[i].S >= 2 && (longest < 0 || notes[i].E - notes[i].S > notes[longest].E - notes[longest].S)) longest = i;
                if (longest < 0)
                {
                    var z = notes[notes.Count - 1];                       // nothing left to halve: add a tick on the end
                    notes.Add(new N { S = z.E, E = z.E + 1, P = z.P });
                }
                else
                {
                    var x = notes[longest]; int mid = x.S + (x.E - x.S) / 2;
                    notes.Insert(longest + 1, new N { S = mid, E = x.E, P = x.P });
                    x.E = mid;
                }
                il.Split++;
            }

            // Spare notes: tie the ones that run into the note before them, shortest first
            var tied = new bool[notes.Count];                             // tied[i]: note i belongs to the syllable of note i - 1
            for (int spare = notes.Count - syl; spare > 0; spare--)
            {
                int pick = -1; double low = double.MaxValue;
                for (int i = 1; i < notes.Count; i++)
                {
                    if (tied[i]) continue;
                    int left = 1; for (int a = i - 1; a > 0 && tied[a]; a--) left++;          // the syllable that ends at note i - 1
                    int right = 1; for (int b = i + 1; b < notes.Count && tied[b]; b++) right++;
                    if (left + right > LineScore.MaxNotes) continue;                          // a syllable holds only so many
                    double c = 4.0 * (notes[i].S - notes[i - 1].E) + (notes[i].E - notes[i].S);
                    if (c < low) { low = c; pick = i; }
                }
                if (pick < 0) break;
                tied[pick] = true; il.Tied++;
            }

            // The starting note: the key's root nearest the middle of the line
            var sorted = new List<int>(); foreach (var x in notes) sorted.Add(x.P); sorted.Sort();
            int median = sorted[sorted.Count / 2];
            int down = median - (((median - keyRoot) % 12) + 12) % 12;
            il.Base = median - down <= 6 ? down : down + 12;

            var sb = new StringBuilder();
            int prevEnd = notes[0].S, groups = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                var x = notes[i];
                if (!tied[i] && groups == syl) break;                     // more notes than the line can hold: the rest are left out
                // A note tied to the next lasts until the next begins, so the syllable has no hole in it
                bool nextTied = i + 1 < notes.Count && tied[i + 1];
                int len = Math.Clamp((nextTied ? notes[i + 1].S : x.E) - x.S, 1, LineScore.MaxLen);
                int pitch = Math.Clamp(x.P - il.Base, -LineScore.PitchRange, LineScore.PitchRange);
                if (tied[i]) sb.Append('+').Append(len).Append(',').Append(pitch);
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(Math.Clamp(x.S - prevEnd, 0, LineScore.MaxGap)).Append(',').Append(len).Append(',').Append(pitch);
                    groups++;
                }
                prevEnd = x.S + len;
            }
            il.Score = sb.ToString();
            return il;
        }
    }
}
