// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — moving a melody by scale steps (Key Lock).
//
// A melody is stored as semitone steps. With Key Lock, those steps are read as
// drawn from the key's root: a step that lands on a scale note is so many scale
// degrees, and one that falls between scale notes is the degree below, raised.
// The note that starts the line picks the degree to begin on, so the same line
// started a third higher stays in the key. Started on the root it is unchanged.
// No allocation: called on the audio thread.

using System;
using System.Numerics;

namespace PedalRoboSing.Speech
{
    public static class ScaleSteps
    {
        static int FloorDiv(int a, int b) => (a - (((a % b) + b) % b)) / b;
        static bool In(int mask, int r) => ((mask >> r) & 1) != 0;
        static int Index(int mask, int r) => BitOperations.PopCount((uint)(mask & ((1 << r) - 1)));
        static int Tone(int mask, int i)
        {
            for (int r = 0; r < 12; r++) if (In(mask, r) && i-- == 0) return r;
            return 0;
        }

        /// <summary>root: pitch class 0..11. mask: bit n = semitone n above the root (bit 0 must be set).</summary>
        public static float Transpose(float note, int offset, int root, int mask)
        {
            mask &= 0xFFF;
            if (mask == 0xFFF || (mask & 1) == 0) return note + offset;
            int n = (int)MathF.Round(note);
            float frac = note - n;
            int m = BitOperations.PopCount((uint)mask);

            // The starting note's degree: the nearest note in the key, the lower one when two are as near
            int rel = n - root, oct = FloorDiv(rel, 12), r = rel - 12 * oct;
            int start = 0;
            for (int d = 0; d <= 6; d++)
            {
                if (r - d >= 0 && In(mask, r - d)) { start = oct * m + Index(mask, r - d); break; }
                if (r + d == 12) { start = (oct + 1) * m; break; }
                if (r + d < 12 && In(mask, r + d)) { start = oct * m + Index(mask, r + d); break; }
            }

            // The step, as degrees from the root plus any raising
            int po = FloorDiv(offset, 12), pr = offset - 12 * po, raised = 0;
            while (!In(mask, pr)) { pr--; raised++; }
            int t = start + po * m + Index(mask, pr);
            int to = FloorDiv(t, m);
            return root + 12 * to + Tone(mask, t - to * m) + raised + frac;
        }
    }
}
