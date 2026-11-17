// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — the phrase bank and its saved state.
//
// The bank is immutable once built: the UI thread compiles a new one (phrase
// edit, song load) and swaps the reference; the audio thread only reads it.

using System;
using System.Collections.Generic;
using PedalRoboSing.Speech;

namespace PedalRoboSing
{
    /// <summary>Saved with the song through the host's MachineState mechanism (XmlSerializer).</summary>
    public class RoboSingState
    {
        public int Version { get; set; } = 1;
        public List<string> Phrases { get; set; } = new List<string>();
    }

    public sealed class PhraseBank
    {
        public const int MaxPhrases = 100;

        public static readonly string[] Defaults =
        {
            "Hello.",
            "Spell.",
            "That is correct!",
            "Wrong. Try again.",
            "You are correct. Now spell.",
            "Perfect score.",
            "Say it.",
            "I am a robot. I make music.",
            "One, two, three, four.",
            "Peace.",
        };

        public readonly string[] Texts;
        readonly Utterance[][] _byMode;       // [mode][phrase]

        /// <summary>Rimes shared by different words anywhere in the list.</summary>
        public readonly HashSet<string> Rhymes;

        public int Count => Texts.Length;

        public PhraseBank(IReadOnlyList<string> texts)
        {
            int n = Math.Min(texts?.Count ?? 0, MaxPhrases);
            Texts = new string[n];
            for (int i = 0; i < n; i++) Texts[i] = texts[i] ?? "";
            Rhymes = Planner.FindRhymes(Texts);
            _byMode = new Utterance[3][];
            for (int m = 0; m < 3; m++)
            {
                _byMode[m] = new Utterance[n];
                for (int i = 0; i < n; i++)
                {
                    try { _byMode[m][i] = Planner.Build(Texts[i], (SpeakMode)m, Rhymes); }
                    catch { _byMode[m][i] = Utterance.Empty; }   // a bad line must never take the machine down
                }
            }
        }

        /// <summary>Phrase number is 1-based; out-of-range numbers wrap round the bank.</summary>
        public Utterance Get(int phraseNumber, int mode)
        {
            if (Texts.Length == 0) return Utterance.Empty;
            int i = ((phraseNumber - 1) % Texts.Length + Texts.Length) % Texts.Length;
            return _byMode[Math.Clamp(mode, 0, 2)][i];
        }

        public string TextFor(int phraseNumber)
        {
            if (Texts.Length == 0) return "(no phrases)";
            int i = ((phraseNumber - 1) % Texts.Length + Texts.Length) % Texts.Length;
            return Texts[i];
        }
    }
}
