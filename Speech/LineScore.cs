// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — the melody of one line.
//
// One step per syllable: a gap of silence before it, a length, and a pitch
// relative to the note that starts the line. Times are in ticks. Stored with the
// song as text, one "gap,length,pitch" triple per syllable: "0,4,0 0,2,3 2,4,-2".
// The phrase bank parses its own copy and never changes it; the audio thread only
// reads that one. The editor works on a separate copy.

using System;
using System.Text;

namespace PedalRoboSing.Speech
{
    public sealed class LineScore
    {
        public const int DefaultLen = 2, MaxLen = 64, MaxGap = 64, PitchRange = 24;

        public readonly int[] Gap, Len, Pitch;
        public int Count => Len.Length;

        public LineScore(int n)
        {
            Gap = new int[n]; Len = new int[n]; Pitch = new int[n];
            for (int i = 0; i < n; i++) Len[i] = DefaultLen;
        }

        public int GapAt(int k) => (uint)k < (uint)Gap.Length ? Gap[k] : 0;
        public int LenAt(int k) => (uint)k < (uint)Len.Length ? Len[k] : DefaultLen;
        public int PitchAt(int k) => (uint)k < (uint)Pitch.Length ? Pitch[k] : 0;

        public int TotalTicks
        {
            get { int t = 0; for (int i = 0; i < Count; i++) t += Gap[i] + Len[i]; return t; }
        }

        /// <summary>Parse stored text into a score of exactly n syllables: extra steps are dropped, missing ones get defaults.</summary>
        public static LineScore Parse(string text, int n)
        {
            var s = new LineScore(Math.Max(0, n));
            if (string.IsNullOrWhiteSpace(text)) return s;
            var steps = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < steps.Length && i < n; i++)
            {
                var f = steps[i].Split(',');
                if (f.Length != 3) continue;
                if (int.TryParse(f[0], out int g)) s.Gap[i] = Math.Clamp(g, 0, MaxGap);
                if (int.TryParse(f[1], out int l)) s.Len[i] = Math.Clamp(l, 1, MaxLen);
                if (int.TryParse(f[2], out int p)) s.Pitch[i] = Math.Clamp(p, -PitchRange, PitchRange);
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
            }
            return sb.ToString();
        }

        public bool IsDefault
        {
            get
            {
                for (int i = 0; i < Count; i++) if (Gap[i] != 0 || Len[i] != DefaultLen || Pitch[i] != 0) return false;
                return true;
            }
        }
    }
}
