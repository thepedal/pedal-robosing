// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — a compiled phrase.
//
// An Utterance is three piecewise-linear tracks over time (ms at Speed 1x):
// formants (F1..F4, B1..B4), source (voicing and noise amplitude) and pitch
// (semitones relative to the played note, at Inflection 1.0). It is built once
// on the UI thread when phrases change and then only read by the audio thread,
// so playback allocates nothing.

using System;

namespace PedalRoboSing.Speech
{
    public struct FormantKey
    {
        public float T;
        public float F1, F2, F3, F4;
        public float B1, B2, B3, B4;
    }

    public struct SourceKey { public float T, AV, AN; }

    /// <summary>How a sound feels, each 0..1 with 0.5 neutral: open (ah) vs closed (ee, oo), bright (ee, s)
    /// vs dark (oo, m), hard (t, k) vs soft (f, vowels), and sung (stressed vowel) vs unsung (consonant).
    /// Rhyme and Allit are 0 or 1: the sound is part of a rhyme, or of an alliterating word's opening.</summary>
    public struct FeelKey
    {
        public float T, Open, Bright, Hard, Sung, Rhyme, Allit;
        public static FeelKey Neutral => new FeelKey { Open = 0.5f, Bright = 0.5f, Hard = 0.5f };
    }
    public struct PitchKey  { public float T, Semis; }

    public sealed class Utterance
    {
        public FormantKey[] F = Array.Empty<FormantKey>();
        public SourceKey[]  S = Array.Empty<SourceKey>();
        public PitchKey[]   P = Array.Empty<PitchKey>();
        public FeelKey[]    Q = Array.Empty<FeelKey>();
        public float[] WordStart = Array.Empty<float>();
        public float[] WordEnd   = Array.Empty<float>();
        public float Length;
        public string Phonetic = "";            // ARPAbet transcription, for the editor's preview
        public string SoundNotes = "";          // rhymes and alliteration found, for the editor's preview

        // Step units: syllables and phonemes, each with a start, a hold point (where
        // a sustained note freezes the mouth) and an end, plus the word it belongs to.
        public float[] SylS = Array.Empty<float>(), SylH = Array.Empty<float>(), SylE = Array.Empty<float>();
        public int[] SylWord = Array.Empty<int>();
        public float[] PhS = Array.Empty<float>(), PhH = Array.Empty<float>(), PhE = Array.Empty<float>();
        public int[] PhWord = Array.Empty<int>();

        /// <summary>Units (syllables or phonemes) that fall inside words w0..w1.</summary>
        public void Range(bool phonemes, int w0, int w1, out int first, out int count)
        {
            var w = phonemes ? PhWord : SylWord;
            first = -1; count = 0;
            for (int i = 0; i < w.Length; i++)
                if (w[i] >= w0 && w[i] <= w1) { if (first < 0) first = i; count++; }
            if (first < 0) first = 0;
        }

        public int WordCount => WordStart.Length;
        public static readonly Utterance Empty = new Utterance();

        // ── Track sampling. `idx` is a cursor kept by the caller; time moves forward
        //    during playback, so the search is amortised O(1). ─────────────────────

        public void SampleFormants(float t, ref int idx, float[] f, float[] b)
        {
            var k = F;
            if (k.Length == 0) { f[0] = 500; f[1] = 1500; f[2] = 2500; f[3] = 3300; b[0] = 80; b[1] = 90; b[2] = 150; b[3] = 250; return; }
            if (idx < 0 || idx >= k.Length || k[idx].T > t) idx = 0;
            while (idx + 1 < k.Length && k[idx + 1].T <= t) idx++;
            ref FormantKey a = ref k[idx];
            if (idx + 1 >= k.Length || t <= a.T)
            {
                f[0] = a.F1; f[1] = a.F2; f[2] = a.F3; f[3] = a.F4;
                b[0] = a.B1; b[1] = a.B2; b[2] = a.B3; b[3] = a.B4;
                return;
            }
            ref FormantKey c = ref k[idx + 1];
            float span = c.T - a.T;
            float x = span > 1e-4f ? (t - a.T) / span : 1f;
            f[0] = a.F1 + (c.F1 - a.F1) * x; f[1] = a.F2 + (c.F2 - a.F2) * x;
            f[2] = a.F3 + (c.F3 - a.F3) * x; f[3] = a.F4 + (c.F4 - a.F4) * x;
            b[0] = a.B1 + (c.B1 - a.B1) * x; b[1] = a.B2 + (c.B2 - a.B2) * x;
            b[2] = a.B3 + (c.B3 - a.B3) * x; b[3] = a.B4 + (c.B4 - a.B4) * x;
        }

        public void SampleSource(float t, ref int idx, out float av, out float an)
        {
            var k = S;
            if (k.Length == 0) { av = an = 0; return; }
            if (idx < 0 || idx >= k.Length || k[idx].T > t) idx = 0;
            while (idx + 1 < k.Length && k[idx + 1].T <= t) idx++;
            ref SourceKey a = ref k[idx];
            if (t < a.T) { av = an = 0; return; }                 // before the first key: silent
            if (idx + 1 >= k.Length) { av = a.AV; an = a.AN; return; }
            ref SourceKey c = ref k[idx + 1];
            float span = c.T - a.T;
            float x = span > 1e-4f ? (t - a.T) / span : 1f;
            av = a.AV + (c.AV - a.AV) * x;
            an = a.AN + (c.AN - a.AN) * x;
        }

        public void SampleFeel(float t, ref int idx, ref FeelKey q)
        {
            var k = Q;
            if (k.Length == 0) { q = FeelKey.Neutral; return; }
            if (idx < 0 || idx >= k.Length || k[idx].T > t) idx = 0;
            while (idx + 1 < k.Length && k[idx + 1].T <= t) idx++;
            ref FeelKey a = ref k[idx];
            if (idx + 1 >= k.Length || t <= a.T) { q = a; return; }
            ref FeelKey c = ref k[idx + 1];
            float span = c.T - a.T;
            float x = span > 1e-4f ? (t - a.T) / span : 1f;
            q.Open = a.Open + (c.Open - a.Open) * x; q.Bright = a.Bright + (c.Bright - a.Bright) * x;
            q.Hard = a.Hard + (c.Hard - a.Hard) * x; q.Sung = a.Sung + (c.Sung - a.Sung) * x;
            q.Rhyme = a.Rhyme + (c.Rhyme - a.Rhyme) * x; q.Allit = a.Allit + (c.Allit - a.Allit) * x;
        }

        public float SamplePitch(float t, ref int idx)
        {
            var k = P;
            if (k.Length == 0) return 0;
            if (idx < 0 || idx >= k.Length || k[idx].T > t) idx = 0;
            while (idx + 1 < k.Length && k[idx + 1].T <= t) idx++;
            ref PitchKey a = ref k[idx];
            if (idx + 1 >= k.Length || t <= a.T) return a.Semis;
            ref PitchKey c = ref k[idx + 1];
            float span = c.T - a.T;
            float x = span > 1e-4f ? (t - a.T) / span : 1f;
            return a.Semis + (c.Semis - a.Semis) * x;
        }
    }
}
