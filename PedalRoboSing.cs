// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — a vocal-tract instrument for ReBuzz.
//
// Text is the score for timbre. Typed phrases are compiled into mouth movement
// (formants, voicing, noise); notes play that movement a syllable or a phoneme
// at a time, in step with the tempo, or scrubbed like a wavetable. The mouth's
// resonances can be tuned to the harmonics of the note or to a scale, and the
// consonant noise can be pitched, so the words become part of the harmony.
// The voice itself is a 10-pole lattice filter with optional LPC-chip grit.
// Everything is original: English letter-to-sound rules and a formant model,
// converted to reflection coefficients on the fly. No speech ROM data is used.
// Word pronunciations come from the CMU Pronouncing Dictionary (see
// Speech/Lexicon.cs and Speech/cmudict.LICENSE.txt), with the rules as fallback.
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. See LICENSE.

using System;
using System.Collections.Generic;
using System.Windows;
using Buzz.MachineInterface;   // IBuzzMachine, IBuzzMachineHost, MachineDecl, ParameterDecl, Note, Sample, WorkModes
using BuzzGUI.Common;          // MenuItemVM, SimpleCommand
using BuzzGUI.Interfaces;      // IMachine, IBuzz, IParameter, IMenuItem
using PedalRoboSing.Speech;

namespace PedalRoboSing
{
    [MachineDecl(Name = "Pedal RoboSing", ShortName = "RoboSing", Author = "thepedal", MaxTracks = 8)]
    public sealed class PedalRoboSingMachine : IBuzzMachine
    {
        internal const string Version = "2.1.0";
        public const int MaxVoices = 8;
        const float SampleScale = 32768f;
        const float MixHeadroom = 0.9f;

        static readonly int[] ChipRates = { 4000, 5512, 8000, 10000, 12000, 16000, 22050 };
        static readonly float[] ConsRatios = { 1f, 1.5f, 2f, 3f, 4f };
        static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        // Bit n = semitone n above the root. Order matches the Scale parameter.
        static readonly int[] ScaleMasks =
        {
            0xFFF,                                   // Chromatic
            Mask(0, 2, 4, 5, 7, 9, 11),              // Major
            Mask(0, 2, 3, 5, 7, 8, 10),              // Minor
            Mask(0, 2, 3, 5, 7, 9, 10),              // Dorian
            Mask(0, 2, 4, 5, 7, 9, 10),              // Mixolydian
            Mask(0, 2, 3, 5, 7, 8, 11),              // Harmonic Minor
            Mask(0, 2, 4, 7, 9),                     // Major Pentatonic
            Mask(0, 3, 5, 7, 10),                    // Minor Pentatonic
            Mask(0, 2, 4, 6, 8, 10),                 // Whole Tone
            Mask(0, 4, 7),                           // Major Triad
            Mask(0, 3, 7),                           // Minor Triad
            Mask(0, 4, 7, 11),                       // Major 7
            Mask(0, 3, 7, 10),                       // Minor 7
            Mask(0, 7),                              // Fifths
            Mask(0),                                 // Octaves
        };
        internal static readonly string[] ScaleNames =
        {
            "Chromatic", "Major", "Minor", "Dorian", "Mixolydian", "Harmonic Minor", "Major Pentatonic",
            "Minor Pentatonic", "Whole Tone", "Major Triad", "Minor Triad", "Major 7", "Minor 7", "Fifths", "Octaves",
        };
        internal static string[] RootNames => NoteNames;

        // ── The song's key ───────────────────────────────────────────────────
        // Set in the phrase editor and saved with the phrases, so loading a preset
        // (which resets every parameter) cannot change it. Root and Scale stay as
        // sound controls: they choose what Tune To Scale and Inflect Snap aim at.
        // -1 = a song from before v1.7: follow the Root and Scale parameters.
        int _keyRoot = 0, _keyScale = 1;
        bool _keyLock = true;

        public int KeyRootNow => _keyRoot >= 0 ? _keyRoot : Math.Clamp(Root, 0, 11);
        public int KeyScaleNow => Math.Clamp(_keyScale >= 0 ? _keyScale : Scale, 0, ScaleMasks.Length - 1);
        /// <summary>The key's scale as a 12-bit mask, bit n = semitone n above its root.</summary>
        public int KeyMask => ScaleMasks[KeyScaleNow];
        public bool KeyLock => _keyLock;
        public void SetKey(int root, int scale, bool lock_)
        {
            _keyRoot = Math.Clamp(root, 0, 11);
            _keyScale = Math.Clamp(scale, 0, ScaleMasks.Length - 1);
            _keyLock = lock_;
        }

        // A syllable's note: the played note moved by the melody, in semitones or, with Key Lock, in scale steps
        float MelodyNote(float midi, LineScore sc, int syl)
        {
            if (sc == null) return midi;
            int p = sc.PitchAt(syl);
            return _keyLock ? ScaleSteps.Transpose(midi, p, KeyRootNow, KeyMask) : midi + p;
        }

        /// <summary>Set one of this machine's own global parameters the way the host does, so the
        /// slider moves and the value is saved with the song (writing the property alone is lost on save).</summary>
        public void SetGlobal(string name, int value)
        {
            try
            {
                var groups = host?.Machine?.ParameterGroups;
                if (groups != null && groups.Count > 1)
                    foreach (var p in groups[1].Parameters)
                        if (p?.Name == name) { p.SetValue(0, value); return; }
            }
            catch { }
            switch (name)                                   // no host parameter found: at least act on it
            {
                case "Root": Root = Math.Clamp(value, 0, 11); break;
                case "Scale": Scale = Math.Clamp(value, 0, ScaleMasks.Length - 1); break;
            }
        }

        static int Mask(params int[] semis) { int m = 0; foreach (int x in semis) m |= 1 << x; return m; }

        readonly IBuzzMachineHost host;
        readonly LpcVoice[] _voices = new LpcVoice[MaxVoices];
        readonly Biquad _hp = new Biquad(), _lp1 = new Biquad(), _lp2 = new Biquad();

        volatile PhraseBank _bank = new PhraseBank(Array.Empty<string>());   // a new machine starts with no words
        sealed class SyllableRequest { public Utterance U; public int K, Pitch; }
        volatile SyllableRequest _auditionSyllable;  // set by the editor when a block is clicked or moved
        volatile Utterance _audition;                // set by the editor's Speak line button
        volatile LineScore _auditionScore;           // set with it by Sing line

        // Per-track held values and pending note events (setters run before Work)
        readonly int[] _velocity = new int[MaxVoices];
        readonly int[] _phrase = new int[MaxVoices];
        readonly int[] _word = new int[MaxVoices];
        readonly int[] _words = new int[MaxVoices];
        readonly int[] _position = new int[MaxVoices];
        readonly int[] _cursor = new int[MaxVoices];          // next syllable / phoneme to play
        readonly byte[] _pendingNote = new byte[MaxVoices];   // 0 none, 255 off, else note
        readonly byte[] _lastNote = new byte[MaxVoices];

        int _lastSr, _lastSpeaker = -1, _lastChip = -1;
        double _chipPhase;
        float _chipSample;
        bool _wasPlaying;
        int _tail;

        PhraseEditorWindow _editor;

        // ── Song timing: a line that knows when the next one starts ──────────
        // An imported song gives each line the ticks to the next line and the note it
        // starts on. One note on a line then sings that line and every one after it,
        // each at its time. A new note on the track starts afresh; a note-off, or
        // stopping the song, lets the current line finish and sings no more.
        readonly int[] _chainNext = { -1, -1, -1, -1, -1, -1, -1, -1 };   // phrase index due next on each track, -1 = none
        readonly float[] _chainClock = new float[MaxVoices];              // ticks since the current line's start
        readonly float[] _chainDue = new float[MaxVoices];                // ticks at which the next line starts
        readonly float[] _chainNote = new float[MaxVoices];               // the note the next line starts on

        // Only while the song plays: a note tried out with the transport stopped (typed in the
        // pattern editor, a MIDI key, the editor's own previews) sings its one line and stops.
        void ArmChain(int track, int index, float note, float clock, PhraseBank bank)
        {
            _chainNext[track] = -1;
            if (!_wasPlaying) return;
            int follow = bank.Follow(index);
            if (follow <= 0) return;
            int next = bank.NextSung(index);
            if (next < 0) return;
            int a = bank.Base(index), b = bank.Base(next);
            _chainNext[track] = next;
            _chainClock[track] = clock;
            _chainDue[track] = follow;
            _chainNote[track] = a >= 0 && b >= 0 ? note + (b - a) : note;   // each line from its own starting note, moved with the one played
        }

        // Called once a buffer, before rendering: start any line that has come due
        void RunChains(PhraseBank bank, float tickMs, float speed, float bufferTicks)
        {
            for (int i = 0; i < MaxVoices; i++)
            {
                int next = _chainNext[i];
                if (next < 0) continue;
                Due(i, next, bank, tickMs, speed);
                if (_chainNext[i] >= 0) _chainClock[i] += bufferTicks;
            }
        }

        void Due(int i, int next, PhraseBank bank, float tickMs, float speed)
        {
            if (next >= bank.Count || PlayMode != 5) { _chainNext[i] = -1; return; }
            var u = bank.Get(next + 1, Mode);
            // Start early by the first syllable's consonants, so its vowel lands on the line's beat
            float leadMs = LpcVoice.LeadFor(u, speed), left = (_chainDue[i] - _chainClock[i]) * tickMs;
            if (left > leadMs) return;
            float note = _chainNote[i];
            int last = u.SylS.Length - 1;
            if (last >= 0)
                _voices[i].StartLine(u, Mode == 0 ? bank.ScoreFor(next + 1) : null, 0, last, Scan < 64, note, _velocity[i] / 100f, Math.Max(0f, left));
            ArmChain(i, next, note, _chainClock[i] - _chainDue[i], bank);
        }

        public PedalRoboSingMachine(IBuzzMachineHost host)
        {
            this.host = host;
            for (int i = 0; i < MaxVoices; i++)
            {
                _voices[i] = new LpcVoice(0x5EEDu + (uint)i * 0x9E3779B9u);
                _velocity[i] = 100; _phrase[i] = 1; _word[i] = 1; _words[i] = 0;
                _lastNote[i] = 0x34;                      // C-3
            }
        }

        // Defaults since v1.2 are close to the Reactive Singer preset: clean chip
        // settings, some Tune, vibrato and glide, and the React controls on.
        // ── Global parameters. DECLARATION ORDER IS THE PRESET CONTRACT ─────────
        // ── (Build §3.3): only ever append; never reorder or insert. ────────────

        [ParameterDecl(Name = "Volume", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public int Volume { get; set; } = 100;

        [ParameterDecl(Name = "Speed", Description = "Speaking rate (64 = normal)", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int Speed { get; set; } = 64;

        [ParameterDecl(Name = "Voice", Description = "Vocal tract size: low = giant, high = tiny (64 = normal)", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int VoiceSize { get; set; } = 64;

        [ParameterDecl(Name = "Inflection", Description = "Pitch movement around the note: 0 = monotone robot, 64 = normal, 127 = very expressive", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int Inflection { get; set; } = 64;

        [ParameterDecl(Name = "Mode", MinValue = 0, MaxValue = 2, DefValue = 0,
            ValueDescriptions = new[] { "Speak", "Spell", "Say+Spell" })]
        public int Mode { get; set; } = 0;

        [ParameterDecl(Name = "Excitation", MinValue = 0, MaxValue = 4, DefValue = 1,
            ValueDescriptions = new[] { "Chirp", "Glottal", "Saw", "Square", "Whisper" })]
        public int Excitation { get; set; } = 1;

        [ParameterDecl(Name = "Chip Rate", MinValue = 0, MaxValue = 6, DefValue = 5,
            ValueDescriptions = new[] { "4 kHz", "5.5 kHz", "8 kHz", "10 kHz", "12 kHz", "16 kHz", "22 kHz" })]
        public int ChipRate { get; set; } = 5;

        [ParameterDecl(Name = "Frame", Description = "Coefficient update interval in ms (longer = steppier)", MinValue = 5, MaxValue = 60, DefValue = 15)]
        public int Frame { get; set; } = 15;

        [ParameterDecl(Name = "Coef Bits", Description = "Filter coefficient resolution (11 = unquantised)", MinValue = 2, MaxValue = 11, DefValue = 10)]
        public int CoefBits { get; set; } = 10;

        [ParameterDecl(Name = "DAC Bits", Description = "Output resolution (16 = unquantised)", MinValue = 3, MaxValue = 16, DefValue = 14)]
        public int DacBits { get; set; } = 14;

        [ParameterDecl(Name = "Speaker", Description = "Small-speaker voicing and image filtering (0 = raw)", MinValue = 0, MaxValue = 127, DefValue = 30)]
        public int Speaker { get; set; } = 30;

        [ParameterDecl(Name = "Note Off", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Ignore", "Cut" })]
        public int NoteOffMode { get; set; } = 0;

        [ParameterDecl(Name = "Breath", Description = "Adds aspiration noise to voiced sounds", MinValue = 0, MaxValue = 127, DefValue = 0)]
        public int Breath { get; set; } = 0;

        [ParameterDecl(Name = "Play Mode", Description = "How notes move the mouth through the phrase", MinValue = 0, MaxValue = 5, DefValue = 5,
            ValueDescriptions = new[] { "Phrase", "Syllable", "Phoneme", "Sync", "Scrub", "Line" })]
        public int PlayMode { get; set; } = 5;

        [ParameterDecl(Name = "Hold", Description = "Line: vowels fill their blocks (off = short and separate). Syllable/Phoneme: sustain while the note is held", MinValue = 0, MaxValue = 1, DefValue = 1,
            ValueDescriptions = new[] { "Off", "On" })]
        public int Hold { get; set; } = 1;

        [ParameterDecl(Name = "Sync Step", Description = "Sync: ticks per syllable", MinValue = 1, MaxValue = 32, DefValue = 2)]
        public int SyncStep { get; set; } = 2;

        [ParameterDecl(Name = "Glide", Description = "How slowly the mouth moves to a new shape", MinValue = 0, MaxValue = 127, DefValue = 15)]
        public int Glide { get; set; } = 15;

        [ParameterDecl(Name = "Scan", Description = "Line: below 64 sings the line backwards. Scrub: travel through the window on its own (64 = still)", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int Scan { get; set; } = 64;

        [ParameterDecl(Name = "Mouth", Description = "Shared: tracks 2-8 copy track 1's mouth, so a chord speaks as one", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Per Track", "Shared" })]
        public int Mouth { get; set; } = 0;

        [ParameterDecl(Name = "Tune", Description = "Pulls the vowel resonances onto musical pitches", MinValue = 0, MaxValue = 127, DefValue = 50)]
        public int Tune { get; set; } = 50;

        [ParameterDecl(Name = "Tune To", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Harmonics", "Scale" })]
        public int TuneTo { get; set; } = 0;

        [ParameterDecl(Name = "Root", MinValue = 0, MaxValue = 11, DefValue = 0,
            ValueDescriptions = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" })]
        public int Root { get; set; } = 0;

        [ParameterDecl(Name = "Scale", MinValue = 0, MaxValue = 14, DefValue = 1,
            ValueDescriptions = new[] { "Chromatic", "Major", "Minor", "Dorian", "Mixolydian", "Harmonic Minor", "Major Pentatonic",
                                        "Minor Pentatonic", "Whole Tone", "Major Triad", "Minor Triad", "Major 7", "Minor 7", "Fifths", "Octaves" })]
        public int Scale { get; set; } = 1;

        [ParameterDecl(Name = "Inflect Snap", Description = "Quantise the pitch movement to the scale", MinValue = 0, MaxValue = 1, DefValue = 0,
            ValueDescriptions = new[] { "Off", "On" })]
        public int InflectSnap { get; set; } = 0;

        [ParameterDecl(Name = "Cons Tune", Description = "Pitches the consonant noise", MinValue = 0, MaxValue = 127, DefValue = 40)]
        public int ConsTune { get; set; } = 40;

        [ParameterDecl(Name = "Cons Pitch", MinValue = 0, MaxValue = 4, DefValue = 2,
            ValueDescriptions = new[] { "Unison", "Fifth", "Octave", "Octave+Fifth", "2 Octaves" })]
        public int ConsPitch { get; set; } = 2;

        [ParameterDecl(Name = "Vib Depth", MinValue = 0, MaxValue = 127, DefValue = 30)]
        public int VibDepth { get; set; } = 30;

        [ParameterDecl(Name = "Vib Rate", MinValue = 0, MaxValue = 127, DefValue = 64)]
        public int VibRate { get; set; } = 64;

        [ParameterDecl(Name = "Portamento", MinValue = 0, MaxValue = 127, DefValue = 20)]
        public int Portamento { get; set; } = 20;

        // ── New in v1.1 — React: the sound of each word moves the voice. Appended. ──
        // 64 = off. Above 64 works as described; below 64 does the opposite.

        [ParameterDecl(Name = "React Level", Description = "Open vowels (ah) louder, closed ones (ee, oo) quieter", MinValue = 0, MaxValue = 127, DefValue = 95)]
        public int ReactLevel { get; set; } = 95;

        [ParameterDecl(Name = "React Tune", Description = "Open vowels get more Tune, closed ones less", MinValue = 0, MaxValue = 127, DefValue = 120)]
        public int ReactTune { get; set; } = 120;

        [ParameterDecl(Name = "React Size", Description = "Bright sounds (ee, s) shrink the mouth, dark ones (oo, m) enlarge it", MinValue = 0, MaxValue = 127, DefValue = 90)]
        public int ReactSize { get; set; } = 90;

        [ParameterDecl(Name = "React Ring", Description = "Hard consonants (t, k, p) ring with more pitch, soft ones (f, th) with less", MinValue = 0, MaxValue = 127, DefValue = 110)]
        public int ReactRing { get; set; } = 110;

        [ParameterDecl(Name = "React Vib", Description = "Vibrato grows on stressed vowels and drops out on consonants", MinValue = 0, MaxValue = 127, DefValue = 120)]
        public int ReactVib { get; set; } = 120;

        // ── New in v1.2 ───────────────────────────────────────────────────────

        [ParameterDecl(Name = "React Rhyme", Description = "Rhyming syllables ring out: more tuning, level and vibrato", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public int ReactRhyme { get; set; } = 100;

        [ParameterDecl(Name = "React Allit", Description = "Words that open on the same consonant hit harder and ring more", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public int ReactAllit { get; set; } = 100;

        // ── Track parameters (group 2) ─────────────────────────────────────────
        // From ReBuzz 1848 (PR #221) each track's setter fires on its own, so no
        // pvalues sibling poll is needed (Core §14 is for older builds).

        [ParameterDecl(IsStateless = true)]
        public void SetNote(Note value, int track)
        {
            if ((uint)track >= MaxVoices) return;          // untrusted index (PedalInvFFT §24)
            byte v = value.Value;
            if (v == Note.Off || (v != 0 && v <= Note.Max)) _pendingNote[track] = v;
        }

        [ParameterDecl(Name = "Velocity", MinValue = 0, MaxValue = 127, DefValue = 100)]
        public void SetVelocity(int value, int track)
        {
            if ((uint)track < MaxVoices && (uint)value <= 127) _velocity[track] = value;
        }

        [ParameterDecl(Name = "Phrase", Description = "Phrase number from the phrase list (right-click > Edit Phrases)", MinValue = 1, MaxValue = 100, DefValue = 1)]
        public void SetPhrase(int value, int track)
        {
            if ((uint)track < MaxVoices && value >= 1 && value <= 100) { _phrase[track] = value; _cursor[track] = 0; }
        }

        [ParameterDecl(Name = "Word", Description = "Start at this word of the phrase", MinValue = 1, MaxValue = 100, DefValue = 1)]
        public void SetWord(int value, int track)
        {
            if ((uint)track < MaxVoices && value >= 1 && value <= 100) { _word[track] = value; _cursor[track] = 0; }
        }

        [ParameterDecl(Name = "Words", Description = "How many words to say (0 = to the end)", MinValue = 0, MaxValue = 100, DefValue = 0)]
        public void SetWords(int value, int track)
        {
            if ((uint)track < MaxVoices && (uint)value <= 100) { _words[track] = value; _cursor[track] = 0; }
        }

        [ParameterDecl(Name = "Step", Description = "Syllable/Phoneme: jump to this step of the Word/Words window", MinValue = 1, MaxValue = 200, IsStateless = true)]
        public void SetStep(int value, int track)
        {
            if ((uint)track < MaxVoices && value >= 1 && value <= 200) _cursor[track] = value - 1;
        }

        [ParameterDecl(Name = "Position", Description = "Scrub: where the mouth is in the Word/Words window (0 = start, 1000 = end)", MinValue = 0, MaxValue = 1000, DefValue = 0)]
        public void SetPosition(int value, int track)
        {
            if ((uint)track < MaxVoices && (uint)value <= 1000) _position[track] = value;
        }

        // ── Value read-outs (Build §12.1) ──────────────────────────────────────
        public string DescribeValue(IParameter parameter, int value)
        {
            switch (parameter?.Name)
            {
                case "Phrase":
                {
                    string t = _bank.TextFor(value);
                    if (t.Trim().Length == 0) t = "(empty)";
                    if (t.Length > 40) t = t.Substring(0, 39) + "…";
                    return value + ": " + t;
                }
                case "Words": return value == 0 ? "All" : value.ToString();
                case "Speed": return SpeedFactor(value).ToString("0.00") + "x";
                case "Voice": return "formants x" + FormantScaleFor(value).ToString("0.00");
                case "Inflection": return (value * 100 / 64) + "%";
                case "Frame": return value + " ms";
                case "Coef Bits": return value >= 11 ? "Off" : value + " bit";
                case "DAC Bits": return value >= 16 ? "Off" : value + " bit";
                case "Speaker": return value == 0 ? "Raw" : value.ToString();
                case "Sync Step": return value == 1 ? "1 tick" : value + " ticks";
                case "Glide": return value == 0 ? "Off" : GlideMsFor(value).ToString("0") + " ms";
                case "Scan": return value == 64 ? "Still" : ((value - 64) / 32f).ToString("+0.00;-0.00") + "x";
                case "Tune": case "Cons Tune": return value == 0 ? "Off" : (value * 100 / 127) + "%";
                case "Vib Depth": return value == 0 ? "Off" : (value * 100 / 127) + " cents";
                case "Vib Rate": return (2f + value / 127f * 8f).ToString("0.0") + " Hz";
                case "Portamento": return value == 0 ? "Off" : PortaMsFor(value).ToString("0") + " ms";
                case "Position": return (value / 10f).ToString("0.0") + "%";
                case "React Level": case "React Tune": case "React Size": case "React Ring": case "React Vib":
                case "React Rhyme": case "React Allit":
                    return value == 64 ? "Off" : ((value - 64) * 100 / 63).ToString("+0;-0") + "%";
                default: return null;
            }
        }

        static float SpeedFactor(int v) => MathF.Pow(2f, (v - 64) / 48f);
        // Voice up = smaller vocal tract = formants scaled up (0.66x .. 1.52x)
        static float FormantScaleFor(int v) => MathF.Pow(2f, (v - 64) / 64f * 0.6f);

        static float React(int v) => Math.Clamp((v - 64) / 63f, -1f, 1f);
        static float GlideMsFor(int v) => v <= 0 ? 0f : 2f * MathF.Pow(2f, v / 127f * 9f);      // 2 ms .. 1 s
        static float PortaMsFor(int v) => v <= 0 ? 0f : 5f * MathF.Pow(2f, v / 127f * 7.6f);    // 5 ms .. 1 s

        // ── Phrase bank I/O ───────────────────────────────────────────────────
        public string[] GetPhrases() => (string[])_bank.Texts.Clone();

        public string[] GetScores() => (string[])_bank.ScoreTexts.Clone();
        public string[] GetLinks() => (string[])_bank.LinkTexts.Clone();

        public void SetPhrases(IReadOnlyList<string> lines, IReadOnlyList<string> scores = null, IReadOnlyList<string> links = null) =>
            _bank = new PhraseBank(lines, scores, links);

        /// <summary>Editor preview: sing a line with its melody on voice 1, from the last note played there.</summary>
        public void AuditionLine(string text, string score)
        {
            var u = Planner.Build(text ?? "", SpeakMode.Speak, _bank.Rhymes);
            _auditionScore = LineScore.Parse(score, u.SylS.Length);
            _audition = u;
        }

        volatile bool _stopAll;
        /// <summary>Silence every voice and drop anything queued to follow (the editor's Stop button).</summary>
        public void StopAll() => _stopAll = true;

        /// <summary>Editor preview: sing one syllable of a line on voice 1, at a melody pitch above the last note played there.</summary>
        public void AuditionSyllable(Utterance u, int syllable, int pitch)
        {
            if (u == null || syllable < 0 || syllable >= u.SylS.Length) return;
            _auditionSyllable = new SyllableRequest { U = u, K = syllable, Pitch = pitch };
        }

        /// <summary>Editor preview: speak arbitrary text on voice 1 at the last note played there.</summary>
        public void Audition(string text) { _auditionScore = null; _audition = AuditionBuild(text); }
        Utterance AuditionBuild(string text) => Planner.Build(text ?? "", (SpeakMode)Math.Clamp(Mode, 0, 2), _bank.Rhymes);

        public RoboSingState MachineState
        {
            get
            {
                var s = new RoboSingState();
                s.Phrases.AddRange(_bank.Texts);
                s.Scores.AddRange(_bank.ScoreTexts);
                s.Links.AddRange(_bank.LinkTexts);
                s.Version = 3;
                s.KeyRoot = _keyRoot; s.KeyScale = _keyScale; s.KeyLock = _keyLock;
                return s;
            }
            set
            {
                if (value?.Phrases == null) return;
                _bank = new PhraseBank(value.Phrases, value.Scores, value.Links);
                _keyRoot = value.KeyRoot < 0 ? -1 : Math.Clamp(value.KeyRoot, 0, 11);
                _keyScale = value.KeyScale < 0 ? -1 : Math.Clamp(value.KeyScale, 0, ScaleMasks.Length - 1);
                _keyLock = value.KeyLock;
            }
        }

        // ── Audio ─────────────────────────────────────────────────────────────
        // Buzz note byte → semitone number (C-0 = 0; A-4 = 57 = 440 Hz)
        static float NoteMidi(byte v)
        {
            int oct = v >> 4, n = (v & 15) - 1;
            if (n < 0 || n > 11) n = 0;
            return oct * 12 + n;
        }

        void StartNote(int i, byte note, PhraseBank bank)
        {
            var v = _voices[i];
            float midi = NoteMidi(note), amp = _velocity[i] / 100f;

            _chainNext[i] = -1;                              // a new note on the track always starts afresh
            if (Mouth == 1 && i > 0) { v.StartFollow(_voices[0], midi, amp); return; }

            var u = bank.Get(_phrase[i], Mode);
            // The melody drawn for the line: its timing drives Line mode, its pitches every mode
            var sc = Mode == 0 ? bank.ScoreFor(_phrase[i]) : null;
            int wc = u.WordCount;
            if (wc == 0 || u.Length <= 0f) return;
            int w0 = Math.Clamp(_word[i] - 1, 0, wc - 1);
            int w1 = _words[i] <= 0 ? wc - 1 : Math.Clamp(w0 + _words[i] - 1, w0, wc - 1);
            float ts = w0 == 0 ? 0f : u.WordStart[w0];
            float te = w1 == wc - 1 ? u.Length : u.WordEnd[w1];

            switch (PlayMode)
            {
                case 1:
                case 2:
                {
                    bool ph = PlayMode == 2;
                    u.Range(ph, w0, w1, out int first, out int count);
                    if (count == 0) return;
                    int c = _cursor[i];
                    if (c < 0) c = 0;
                    int k = first + c % count;
                    _cursor[i] = (c + 1) % count;                 // wraps round the window
                    float slot = Hold == 1 ? float.PositiveInfinity : -1f;
                    if (ph)
                    {
                        int syl = 0;                              // the syllable this phoneme belongs to
                        while (syl + 1 < u.SylS.Length && u.PhS[k] >= u.SylS[syl + 1]) syl++;
                        v.StartSegment(u, u.PhS[k], u.PhH[k], u.PhE[k], slot, MelodyNote(midi, sc, syl), amp);
                    }
                    else v.StartSegment(u, u.SylS[k], u.SylH[k], u.SylE[k], slot, MelodyNote(midi, sc, k), amp);
                    break;
                }
                case 3:
                {
                    u.Range(false, w0, w1, out int first, out int count);
                    if (count == 0) return;
                    v.StartSync(u, first, first + count - 1, midi, amp, sc);
                    break;
                }
                case 4:
                    v.ScrubTarget01 = _position[i] / 1000f;
                    v.StartScrub(u, ts, te, midi, amp, sc);
                    break;
                case 5:                                           // the line's own melody; one note starts it
                {
                    u.Range(false, w0, w1, out int first, out int count);
                    if (count == 0) return;
                    v.StartLine(u, sc, first, first + count - 1, Scan < 64, midi, amp);
                    // The whole line, and it knows what follows: sing on from here
                    if (first == 0 && count == u.SylS.Length) ArmChain(i, bank.IndexOf(_phrase[i]), midi, 0f, bank);
                    break;
                }
                default:
                    v.StartPhrase(u, ts, te, midi, amp, sc);
                    break;
            }
        }

        void UpdateFilters(int sr, int chip)
        {
            if (sr == _lastSr && Speaker == _lastSpeaker && chip == _lastChip) return;
            double s = Speaker / 127.0;
            _hp.SetHighPass(40.0 * Math.Pow(12.0, s), 0.707, sr);
            double fc = Math.Min(0.45 * sr, 0.5 * chip * (1.0 - 0.3 * s));
            _lp1.SetLowPass(fc, 0.54, sr);
            _lp2.SetLowPass(fc, 1.31 + 1.5 * s, sr);
            if (sr != _lastSr) { _hp.Reset(); _lp1.Reset(); _lp2.Reset(); }
            _lastSr = sr; _lastSpeaker = Speaker; _lastChip = chip;
        }

        static float SoftClip(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            return x * (27f + x * x) / (27f + 9f * x * x);
        }

        public bool Work(Sample[] output, int n, WorkModes mode)
        {
            int sr = host?.MasterInfo?.SamplesPerSec ?? 44100;
            if (sr < 8000) sr = 44100;
            int chip = ChipRates[Math.Clamp(ChipRate, 0, ChipRates.Length - 1)];
            UpdateFilters(sr, chip);                      // also handles sample-rate changes (Core §29)

            // Transport stop (Core §27): fade every voice on the falling edge
            bool playing = _wasPlaying;
            try { playing = host?.Machine?.Graph?.Buzz?.Playing ?? false; } catch { }
            if (_wasPlaying && !playing)
                foreach (var v in _voices) v.Kill();
            if (!playing)
                for (int i = 0; i < MaxVoices; i++) _chainNext[i] = -1;
            _wasPlaying = playing;
            if (_stopAll)
            {
                _stopAll = false;
                _audition = null; _auditionScore = null; _auditionSyllable = null;
                foreach (var v in _voices) v.Kill();
                for (int i = 0; i < MaxVoices; i++) { _chainNext[i] = -1; _pendingNote[i] = 0; }
            }

            // Per-buffer voice settings
            var bank = _bank;
            float speed = SpeedFactor(Speed), fscale = FormantScaleFor(VoiceSize);
            float infl = Inflection / 64f, breath = Breath / 127f;
            var mi = host?.MasterInfo;
            float tickMs = (mi != null && mi.SamplesPerTick > 0) ? 1000f * mi.SamplesPerTick / sr : 125f;
            float glide = GlideMsFor(Glide), porta = PortaMsFor(Portamento);
            int mask = ScaleMasks[Math.Clamp(Scale, 0, ScaleMasks.Length - 1)];
            int keyRoot = KeyRootNow, keyMask = KeyMask;
            float consRatio = ConsRatios[Math.Clamp(ConsPitch, 0, ConsRatios.Length - 1)];
            for (int i = 0; i < MaxVoices; i++)
            {
                var v = _voices[i];
                v.ChipRate = chip; v.FrameMs = Frame; v.CoefBits = CoefBits; v.DacBits = DacBits;
                v.Excitation = Excitation; v.FormantScale = fscale; v.Inflection = infl;
                v.Speed = speed; v.Breath = breath;
                v.GlideMs = glide; v.TuneAmt = Tune / 127f; v.TuneMode = TuneTo; v.Root = Root; v.ScaleMask = mask;
                v.InflSnap = InflectSnap == 1; v.ConsTune = ConsTune / 127f; v.ConsRatio = consRatio;
                v.VibDepth = VibDepth / 127f; v.VibRateHz = 2f + VibRate / 127f * 8f; v.PortaMs = porta;
                v.TickMs = tickMs; v.SyncTicks = SyncStep; v.ScanRate = (Scan - 64) / 32f; v.LineHold = Hold == 1;
                v.KeyRoot = keyRoot; v.KeyMask = keyMask; v.KeyLock = _keyLock;
                v.ScrubTarget01 = _position[i] / 1000f;
                v.ReactLevel = React(ReactLevel); v.ReactTune = React(ReactTune); v.ReactSize = React(ReactSize);
                v.ReactRing = React(ReactRing); v.ReactVib = React(ReactVib);
                v.ReactRhyme = React(ReactRhyme); v.ReactAllit = React(ReactAllit);
            }

            // Note events
            for (int i = 0; i < MaxVoices; i++)
            {
                byte pn = _pendingNote[i];
                if (pn == 0) continue;
                _pendingNote[i] = 0;
                if (pn == Note.Off) { _chainNext[i] = -1; _voices[i].NoteOff(NoteOffMode == 1); continue; }
                _lastNote[i] = pn;
                StartNote(i, pn, bank);
            }
            RunChains(bank, tickMs, speed, n * 1000f / (sr * Math.Max(0.001f, tickMs)));
            var one = _auditionSyllable;
            if (one != null)
            {
                _auditionSyllable = null;
                _chainNext[0] = -1;                          // the editor has taken this voice
                float from = NoteMidi(_lastNote[0]);
                float to = _keyLock ? ScaleSteps.Transpose(from, one.Pitch, keyRoot, keyMask) : from + one.Pitch;
                _voices[0].StartSegment(one.U, one.U.SylS[one.K], one.U.SylH[one.K], one.U.SylE[one.K], -1f, to, _velocity[0] / 100f);
            }
            var aud = _audition;
            if (aud != null)
            {
                var sc = _auditionScore;
                _audition = null; _auditionScore = null;
                _chainNext[0] = -1;
                if (sc != null) _voices[0].StartLine(aud, sc, 0, aud.SylS.Length - 1, Scan < 64, NoteMidi(_lastNote[0]), _velocity[0] / 100f);
                else _voices[0].StartPhrase(aud, 0f, aud.Length, NoteMidi(_lastNote[0]), _velocity[0] / 100f);
            }

            bool any = false;
            for (int i = 0; i < MaxVoices; i++) if (_voices[i].IsActive) { any = true; break; }
            if (any) _tail = 4;
            else if (_tail <= 0)
            {
                for (int i = 0; i < n; i++) { output[i].L = 0f; output[i].R = 0f; }
                return false;
            }
            else _tail--;

            float vol = Volume / 127f * MixHeadroom;
            double inc = (double)chip / sr;
            bool speaker = Speaker > 0;
            for (int i = 0; i < n; i++)
            {
                _chipPhase += inc;
                while (_chipPhase >= 1.0)
                {
                    _chipPhase -= 1.0;
                    float sum = 0f;
                    for (int k = 0; k < MaxVoices; k++)
                        if (_voices[k].IsActive) sum += _voices[k].Next();
                    _chipSample = sum;                    // zero-order hold, like a DAC
                }
                float y = _chipSample;
                if (speaker) y = _lp2.Process(_lp1.Process(_hp.Process(y)));
                float s = SoftClip(y * vol) * SampleScale;
                output[i].L = s; output[i].R = s;
            }
            return true;
        }

        // ── Right-click menu ──────────────────────────────────────────────────
        public IEnumerable<IMenuItem> Commands
        {
            get
            {
                yield return new MenuItemVM()
                {
                    Text = "Edit Phrases...",
                    Command = new SimpleCommand()
                    {
                        CanExecuteDelegate = p => true,
                        ExecuteDelegate = p => OpenEditor()
                    }
                };
                yield return new MenuItemVM()
                {
                    Text = "About...",
                    Command = new SimpleCommand()
                    {
                        CanExecuteDelegate = p => true,
                        ExecuteDelegate = p => Theme.ShowMessage(null, "About Pedal RoboSing",
                            $"Pedal RoboSing   v{Version}\n\n" +
                            "Vocal-tract instrument: typed phrases become mouth movement that notes play a syllable at a time, " +
                            "in sync, scrubbed, or as whole lines with their own melody, with resonances tuned to the music. " +
                            "Original rules and formant model; no speech ROM data.\n\n" +
                            "Pronunciations from the CMU Pronouncing Dictionary, Copyright (C) 1993-2015 Carnegie Mellon University.\n" +
                            "Dictionary: " + Lexicon.Status + ".\n\n" +
                            "github.com/thepedal/pedal-robosing\n" +
                            "GPL-3.0-or-later")
                    }
                };
            }
        }

        /// <summary>Rows per beat, for the melody editor's grid (4 until the host has told us).</summary>
        /// <summary>The song's tempo, or 0 when the host does not say.</summary>
        public int BeatsPerMin
        {
            get { try { return host?.MasterInfo?.BeatsPerMin ?? 0; } catch { return 0; } }
        }

        public int TicksPerBeat
        {
            get { try { int t = host?.MasterInfo?.TicksPerBeat ?? 4; return t > 0 ? t : 4; } catch { return 4; } }
        }

        /// <summary>Ticks into the line that is being sung (any track), or -1. Read from the UI thread; approximate.</summary>
        public float LineProgress(string phonetic)
        {
            if (string.IsNullOrEmpty(phonetic)) return -1f;
            foreach (var v in _voices) if (v.TryLineProgress(phonetic, out float t)) return t;
            return -1f;
        }

        /// <summary>The last note played on track 1 as a semitone number (C-0 = 0): the key Sing line uses.</summary>
        public int LastNoteNumber => (int)NoteMidi(_lastNote[0]);

        /// <summary>Open the phrase editor, or bring it forward (right-click menu, and a double-click on the machine).</summary>
        internal void OpenEditor()
        {
            if (_editor != null) { _editor.Activate(); return; }
            _editor = new PhraseEditorWindow(this);
            try { _editor.Owner = Application.Current?.MainWindow; } catch { }
            _editor.Closed += (s, e) => _editor = null;
            _editor.Show();
        }
    }
}
