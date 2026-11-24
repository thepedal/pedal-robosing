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
        /// <summary>Line melodies, one per phrase ("gap,length,pitch" per syllable); empty = none. Added in v1.4.</summary>
        public List<string> Scores { get; set; } = new List<string>();
        /// <summary>The song's key, set in the phrase editor: root 0..11 and an index into the scale list.
        /// -1 (a song saved before v1.7) = follow the Root and Scale parameters.</summary>
        public int KeyRoot { get; set; } = -1;
        public int KeyScale { get; set; } = -1;
        /// <summary>The melody moves by scale steps from the note that starts the line. Added in v1.7.</summary>
        public bool KeyLock { get; set; }
        /// <summary>Song timing, one per phrase: "follow,base". follow = ticks from this line's start to the next
        /// line's (0 = nothing follows); base = the note the line was written from. Empty = none. Added in v2.0.</summary>
        public List<string> Links { get; set; } = new List<string>();
    }

    public sealed class PhraseBank
    {
        public const int MaxPhrases = 100;

        /// <summary>Sample phrases (the tests use them). A new machine starts empty.</summary>
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
        public readonly string[] ScoreTexts;
        public readonly string[] LinkTexts;
        readonly int[] _follow, _base;        // song timing: ticks to the next line (0 = none), starting note (-1 = none)
        readonly LineScore[] _scores;         // null where a line has no melody of its own
        readonly Utterance[][] _byMode;       // [mode][phrase]

        /// <summary>Rimes shared by different words anywhere in the list.</summary>
        public readonly HashSet<string> Rhymes;

        public int Count => Texts.Length;

        public PhraseBank(IReadOnlyList<string> texts, IReadOnlyList<string> scores = null, IReadOnlyList<string> links = null)
        {
            int n = Math.Min(texts?.Count ?? 0, MaxPhrases);
            Texts = new string[n];
            ScoreTexts = new string[n];
            _scores = new LineScore[n];
            LinkTexts = new string[n]; _follow = new int[n]; _base = new int[n];
            for (int i = 0; i < n; i++)
            {
                Texts[i] = texts[i] ?? "";
                ScoreTexts[i] = scores != null && i < scores.Count ? scores[i] ?? "" : "";
                LinkTexts[i] = links != null && i < links.Count ? links[i] ?? "" : "";
                ParseLink(LinkTexts[i], out _follow[i], out _base[i]);
            }
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
            for (int i = 0; i < n; i++)
                if (ScoreTexts[i].Trim().Length > 0)
                    _scores[i] = LineScore.Parse(ScoreTexts[i], _byMode[0][i].SylS.Length);
        }

        /// <summary>Phrase number is 1-based; out-of-range numbers wrap round the bank.</summary>
        public Utterance Get(int phraseNumber, int mode)
        {
            if (Texts.Length == 0) return Utterance.Empty;
            int i = ((phraseNumber - 1) % Texts.Length + Texts.Length) % Texts.Length;
            return _byMode[Math.Clamp(mode, 0, 2)][i];
        }

        /// <summary>The line's melody, or null. It belongs to the spoken (Speak mode) syllables.</summary>
        public LineScore ScoreFor(int phraseNumber)
        {
            if (Texts.Length == 0) return null;
            return _scores[((phraseNumber - 1) % Texts.Length + Texts.Length) % Texts.Length];
        }

        public static void ParseLink(string text, out int follow, out int baseNote)
        {
            follow = 0; baseNote = -1;
            var f = (text ?? "").Split(',');
            if (f.Length != 2) return;
            if (int.TryParse(f[0], out int a)) follow = Math.Clamp(a, 0, 100000);
            if (int.TryParse(f[1], out int b)) baseNote = Math.Clamp(b, -1, 119);
        }

        /// <summary>0-based index of a phrase number (they wrap round the bank), or -1 when the bank is empty.</summary>
        public int IndexOf(int phraseNumber) => Texts.Length == 0 ? -1 : ((phraseNumber - 1) % Texts.Length + Texts.Length) % Texts.Length;
        public int Follow(int index) => (uint)index < (uint)_follow.Length ? _follow[index] : 0;
        public int Base(int index) => (uint)index < (uint)_base.Length ? _base[index] : -1;
        /// <summary>The next line after `index` that has anything to sing, or -1.</summary>
        public int NextSung(int index)
        {
            for (int i = index + 1; i < Texts.Length; i++) if (_byMode[0][i].Length > 0f) return i;
            return -1;
        }

        public string TextFor(int phraseNumber)
        {
            if (Texts.Length == 0) return "(no phrases)";
            int i = ((phraseNumber - 1) % Texts.Length + Texts.Length) % Texts.Length;
            return Texts[i];
        }
    }
}
