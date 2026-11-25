// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — phoneme inventory.
//
// Each phoneme is a set of acoustic targets for the formant-to-LPC engine:
// four formant frequencies and bandwidths, a voicing amplitude (AV) and a noise
// amplitude (AN), plus an inherent duration. Stops and affricates also carry a
// closure / burst / aspiration timing and a burst spectrum; their formant values
// are the place-of-articulation locus that neighbouring vowels glide to and from.
//
// Values are rounded textbook figures for an adult voice (in the spirit of the
// classic formant-synthesis literature), tuned by ear for an 8 kHz LPC chip:
// they are not taken from any speech ROM. Symbols are ARPAbet, as produced by
// the letter-to-sound rules and accepted in [bracketed] phoneme input.

using System;
using System.Collections.Generic;

namespace PedalRoboSing.Speech
{
    public enum PKind
    {
        Vowel, Diphthong, Glide, Nasal,
        Fricative, VoicedFricative,
        Stop, VoicedStop,
        Affricate, VoicedAffricate,
        Aspirate, Pause
    }

    public sealed class Phoneme
    {
        public string Name;
        public PKind Kind;
        public float Dur;                       // inherent duration, ms

        public float[] F = new float[4];        // formant targets (Hz) — the locus for stops
        public float[] B = new float[4];        // bandwidths (Hz)
        public float[] FEnd;                    // diphthong end target F1..F3 (null if none)
        public float[] LocF, LocB;              // fricatives: the mouth shape neighbours move to and from (null = F, B)

        public float AV;                        // voicing amplitude 0..1
        public float AN;                        // noise amplitude 0..1

        // Stops / affricates
        public float Closure, Burst, Asp;       // ms
        public float[] BurstF, BurstB;          // burst spectrum
        public float BurstAmp;
        public float VoiceBar;                  // AV during closure (voiced stops)
        public string FricRef;                  // affricate: fricative whose spectrum follows the burst
        public float FricDur;

        public bool IsVowel => Kind == PKind.Vowel || Kind == PKind.Diphthong;
        public bool IsSonorant => IsVowel || Kind == PKind.Glide || Kind == PKind.Nasal;
        public bool IsStopLike => Kind == PKind.Stop || Kind == PKind.VoicedStop
                               || Kind == PKind.Affricate || Kind == PKind.VoicedAffricate;
    }

    public static class Phonemes
    {
        static readonly Dictionary<string, Phoneme> _map = new Dictionary<string, Phoneme>(StringComparer.Ordinal);

        public static Phoneme Get(string name) => _map.TryGetValue(name, out var p) ? p : null;
        public static readonly Phoneme Pause;

        static Phoneme V(string n, float dur, float f1, float f2, float f3, float b1, float b2, float b3,
                         float f4 = 3300f, float b4 = 250f)
        {
            var p = new Phoneme { Name = n, Kind = PKind.Vowel, Dur = dur, AV = 1f, AN = 0f };
            p.F[0] = f1; p.F[1] = f2; p.F[2] = f3; p.F[3] = f4;
            p.B[0] = b1; p.B[1] = b2; p.B[2] = b3; p.B[3] = b4;
            _map[n] = p;
            return p;
        }

        static void D(string n, float dur, float f1, float f2, float f3, float b1, float b2, float b3,
                      float e1, float e2, float e3)
        {
            var p = V(n, dur, f1, f2, f3, b1, b2, b3);
            p.Kind = PKind.Diphthong;
            p.FEnd = new[] { e1, e2, e3 };
        }

        static void Loc(string n, float f1, float f2, float f3, float f4)
        {
            var p = _map[n];
            p.LocF = new[] { f1, f2, f3, f4 };
            p.LocB = new[] { p.B[0] > 1000f ? 300f : p.B[0], 300f, 350f, 400f };
        }

        static Phoneme C(string n, PKind k, float dur, float av, float an,
                         float f1, float f2, float f3, float f4,
                         float b1, float b2, float b3, float b4)
        {
            var p = new Phoneme { Name = n, Kind = k, Dur = dur, AV = av, AN = an };
            p.F[0] = f1; p.F[1] = f2; p.F[2] = f3; p.F[3] = f4;
            p.B[0] = b1; p.B[1] = b2; p.B[2] = b3; p.B[3] = b4;
            _map[n] = p;
            return p;
        }

        static void S(string n, bool voiced, float clos, float burst, float asp, float burstAmp,
                      float l1, float l2, float l3,
                      float bf1, float bf2, float bf3, float bf4,
                      float bb1, float bb2, float bb3, float bb4)
        {
            var p = C(n, voiced ? PKind.VoicedStop : PKind.Stop, clos + burst + asp, 0f, 0f,
                      l1, l2, l3, 3300f, 80f, 120f, 200f, 250f);
            p.Closure = clos; p.Burst = burst; p.Asp = asp; p.BurstAmp = burstAmp;
            p.VoiceBar = voiced ? 0.06f : 0f;
            p.BurstF = new[] { bf1, bf2, bf3, bf4 };
            p.BurstB = new[] { bb1, bb2, bb3, bb4 };
        }

        static void Trim(string name, float av) => _map[name].AV = av;

        static Phonemes()
        {
            //   name dur   F1   F2    F3   B1   B2   B3
            V("IY", 155, 310, 2200, 2960, 45, 200, 400, 3500);
            V("IH", 135, 400, 1800, 2570, 50, 100, 140);
            V("EH", 150, 530, 1680, 2500, 60,  90, 200);
            V("AE", 210, 620, 1660, 2430, 70, 150, 320);
            V("AA", 220, 700, 1220, 2600, 130, 70, 160);
            V("AO", 220, 600,  990, 2570, 90, 100,  80);
            V("UH", 160, 450, 1100, 2350, 80, 100,  80);
            V("UW", 190, 330, 1000, 2250, 65, 110, 140);
            V("AH", 140, 620, 1220, 2550, 80,  50, 140);
            V("AX", 100, 550, 1260, 2470, 80,  50, 140);
            V("ER", 180, 470, 1270, 1540, 100, 60, 110);
            //   name dur   F1   F2    F3   B1   B2   B3   → end F1  F2   F3
            D("EY", 190, 480, 1720, 2520, 70, 100, 200,  330, 2200, 2600);
            D("AY", 250, 660, 1200, 2550, 100, 70, 200,  400, 1880, 2500);
            D("AW", 260, 640, 1230, 2550, 80,  70, 140,  420,  940, 2350);
            D("OY", 280, 550,  960, 2400, 80,  50, 130,  360, 1820, 2450);
            D("OW", 220, 540, 1100, 2300, 80,  70,  70,  450,  900, 2300);

            // Glides and liquids
            C("W",  PKind.Glide, 70, 0.75f, 0f, 290,  610, 2150, 3300, 50,  80,  60, 250);
            C("Y",  PKind.Glide, 65, 0.75f, 0f, 260, 2070, 3020, 3500, 40, 250, 500, 300);
            C("R",  PKind.Glide, 70, 0.80f, 0f, 310, 1060, 1380, 3300, 70, 100, 120, 250);
            C("L",  PKind.Glide, 70, 0.80f, 0f, 310, 1050, 2880, 3300, 50, 100, 280, 250);
            C("WH", PKind.Glide, 80, 0.35f, 0.30f, 290, 610, 2150, 3300, 80, 120, 120, 300);

            // Nasals — all-pole approximation of the murmur (strong low resonance)
            C("M",  PKind.Nasal, 70, 0.72f, 0f, 250, 1100, 2200, 3300, 60, 250, 300, 300);
            C("N",  PKind.Nasal, 60, 0.72f, 0f, 250, 1600, 2600, 3300, 60, 250, 300, 300);
            C("NG", PKind.Nasal, 90, 0.72f, 0f, 250, 2000, 2800, 3300, 60, 250, 300, 300);

            // Voiceless fricatives. The first resonance is kept low but very wide, so it adds almost
            // nothing: a narrow one there put most of the hiss below 2.5 kHz. The other three carry the
            // sound: s high and tight (4.4 to 6.8 kHz), sh lower (2.3 to 4.6 kHz), f and th weak and flat.
            C("F",  PKind.Fricative, 100, 0f, 0.44f, 500, 2400, 4200, 6200, 2500, 1500, 1500, 1500);
            C("TH", PKind.Fricative,  90, 0f, 0.38f, 500, 2800, 4600, 6400, 2500, 1500, 1500, 1500);
            C("S",  PKind.Fricative, 105, 0f, 0.68f, 600, 4400, 5600, 6800, 2500, 600, 500, 700);
            C("SH", PKind.Fricative, 105, 0f, 0.70f, 600, 2300, 3200, 4600, 2500, 500, 450, 700);
            // Voiced fricatives — a low voice-bar resonance plus their voiceless partner's hiss
            C("V",  PKind.VoicedFricative, 60, 0.55f, 0.28f, 300, 2400, 4200, 6200, 150, 1500, 1500, 1500);
            C("DH", PKind.VoicedFricative, 50, 0.50f, 0.22f, 300, 2800, 4600, 6400, 150, 1500, 1500, 1500);
            C("Z",  PKind.VoicedFricative, 75, 0.40f, 0.55f, 300, 4400, 5600, 6800, 250, 600, 500, 700);
            C("ZH", PKind.VoicedFricative, 70, 0.40f, 0.55f, 300, 2300, 3200, 4600, 250, 500, 450, 700);
            // Where a neighbouring vowel's resonances head as the mouth closes for each of them. The hiss
            // takes over only once the voice has stopped, so no whistle sweeps up through the vowel.
            Loc("F", 400, 1200, 2400, 3400); Loc("V", 300, 1200, 2400, 3400);
            Loc("TH", 400, 1500, 2700, 3600); Loc("DH", 300, 1500, 2700, 3600);
            Loc("S", 400, 1800, 2700, 3600); Loc("Z", 300, 1800, 2700, 3600);
            Loc("SH", 400, 1900, 2500, 3400); Loc("ZH", 300, 1900, 2500, 3400);

            // Aspiration — takes the following sound's formants at plan time
            C("HH", PKind.Aspirate, 70, 0f, 0.45f, 500, 1500, 2500, 3300, 200, 200, 300, 400);

            // Stops: closure, burst, aspiration (ms), burst amp, locus F1..F3, burst spectrum F1..F4 / B1..B4
            S("P", false, 65, 10, 40, 0.62f, 250,  900, 2100,  500, 1000, 2500, 3400,  600, 600, 700, 700);
            S("B", true,  55,  8,  0, 0.50f, 250,  900, 2100,  500, 1000, 2500, 3400,  600, 600, 700, 700);
            S("T", false, 55, 12, 40, 0.80f, 250, 1700, 2700,  500, 1800, 3400, 4500,  800, 500, 300, 400);
            S("D", true,  45, 10,  0, 0.70f, 250, 1700, 2700,  500, 1800, 3400, 4500,  800, 500, 300, 400);
            S("K", false, 60, 15, 45, 0.80f, 250, 1900, 2500,  400, 1900, 2600, 3500,  500, 200, 300, 400);
            S("G", true,  50, 12,  0, 0.70f, 250, 1900, 2500,  400, 1900, 2600, 3500,  500, 200, 300, 400);

            // Affricates: T/D-style closure and burst, then a fricative
            S("CH", false, 50, 8, 0, 0.60f, 250, 1800, 2600,  500, 1800, 3400, 4500,  800, 500, 300, 400);
            S("JH", true,  45, 6, 0, 0.45f, 250, 1800, 2600,  500, 1800, 3400, 4500,  800, 500, 300, 400);
            var ch = _map["CH"]; ch.Kind = PKind.Affricate;       ch.FricRef = "SH"; ch.FricDur = 70; ch.Dur += 70;
            var jh = _map["JH"]; jh.Kind = PKind.VoicedAffricate; jh.FricRef = "ZH"; jh.FricDur = 60; jh.Dur += 60;

            // Vowel level trims (v1.1). A harmonic source through a narrow low
            // resonance comes out much louder than through a wide high one, so held
            // "ee" and "oo" were about 8 dB above "ah". Measured as held notes over
            // six pitches and levelled to within about 2 dB.
            Trim("IY", 0.59f); Trim("IH", 0.74f); Trim("EH", 0.90f); Trim("AE", 1.07f); Trim("AA", 1.36f);
            Trim("AO", 0.98f); Trim("UH", 0.84f); Trim("UW", 0.51f); Trim("AH", 1.13f); Trim("ER", 1.33f);
            Trim("AY", 1.11f); Trim("AW", 1.05f); Trim("OY", 0.97f); Trim("OW", 0.94f);

            Pause = new Phoneme { Name = "_", Kind = PKind.Pause, Dur = 120 };
            _map["_"] = Pause;
        }
    }
}
