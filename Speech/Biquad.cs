// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — RBJ biquad, used for the "Speaker" voicing at the host rate.

using System;

namespace PedalRoboSing.Speech
{
    public sealed class Biquad
    {
        double b0 = 1, b1, b2, a1, a2;
        double z1, z2;

        public void Reset() { z1 = z2 = 0; }

        public void SetLowPass(double fc, double q, double sr) => Set(fc, q, sr, false);
        public void SetHighPass(double fc, double q, double sr) => Set(fc, q, sr, true);

        void Set(double fc, double q, double sr, bool high)
        {
            fc = Math.Clamp(fc, 10.0, sr * 0.49);
            double w = 2.0 * Math.PI * fc / sr;
            double cw = Math.Cos(w), alpha = Math.Sin(w) / (2.0 * q);
            double a0 = 1.0 + alpha;
            if (high)
            {
                b0 = (1.0 + cw) / 2.0 / a0; b1 = -(1.0 + cw) / a0; b2 = b0;
            }
            else
            {
                b0 = (1.0 - cw) / 2.0 / a0; b1 = (1.0 - cw) / a0; b2 = b0;
            }
            a1 = -2.0 * cw / a0; a2 = (1.0 - alpha) / a0;
        }

        public float Process(float x)
        {
            // Transposed direct form II
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            if (Math.Abs(z1) < 1e-25) z1 = 0;   // denormal guard (Core §30)
            if (Math.Abs(z2) < 1e-25) z2 = 0;
            return (float)y;
        }
    }
}
