// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — text to Utterance.
//
//   text ─► tokens (words, [phoneme groups], punctuation)
//        ─► phonemes per word (exceptions dictionary, then letter-to-sound rules)
//        ─► speech items for the chosen mode (Speak / Spell / Say+Spell)
//        ─► durations, stress, intonation (declination, accents, final fall/rise)
//        ─► formant / source / pitch keyframes
//
// Runs on the UI thread (phrase edits, song load); never on the audio thread.

using System;
using System.Collections.Generic;
using System.Text;

namespace PedalRoboSing.Speech
{
    public enum SpeakMode { Speak = 0, Spell = 1, SaySpell = 2 }

    public static class Planner
    {
        // ── Timing constants (ms) ────────────────────────────────────────────
        const float WordGap      = 15f;    // tiny gap between words, for clarity
        const float CommaPause   = 200f;
        const float StopPause    = 420f;
        const float LetterPause  = 110f;
        const float SpellEnd     = 330f;
        const float SaySpellGap  = 320f;

        static readonly HashSet<string> FunctionWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "A","AN","THE","OF","TO","IN","ON","AT","FOR","AND","OR","BUT","IS","ARE","WAS","WERE",
            "BE","IT","ITS","THAT","THIS","WITH","AS","BY","FROM","HE","SHE","WE","THEY","ME","MY",
            "YOUR","HIS","HER","OUR","THEIR","AM","DO","DOES","CAN","WILL","IF","SO","THEN","THAN",
        };

        enum TokKind { Word, Phon, Punct }
        struct Tok { public TokKind Kind; public string Text; }

        sealed class Item
        {
            public readonly List<Phoneme> Ph = new List<Phoneme>();
            public readonly List<bool> Stress = new List<bool>();
            public bool Accent;
            public float PauseAfter;
            public char Boundary;      // '\0', ',', '.', '?', '!'
            public int Word;
            public string Text;        // the spoken word, null for letters and [phonemes]
            public int RimeFrom = -1;  // index of the vowel the word's rhyme starts on
            public bool Rhymes;
            public int AllitLen;       // leading consonants that alliterate with a neighbour
        }

        sealed class Inst
        {
            public Phoneme Ph;
            public float T, D;
            public bool Stress, AccentVowel, NoAsp;
            public int Item, Idx;
        }

        // ── Public entry point ───────────────────────────────────────────────
        public static Utterance Build(string text, SpeakMode mode, ISet<string> rhymes = null)
        {
            var items = MakeItems(text, mode, out int wordCount);
            if (items.Count == 0) return Utterance.Empty;
            var u = new Utterance();
            if (mode == SpeakMode.Speak) MarkSound(items, rhymes, u);

            var insts = Time(items);
            BuildPitch(items, insts, u);
            BuildSpectral(insts, u);
            BuildWords(items, insts, wordCount, u);
            BuildUnits(items, insts, u);
            BuildFeel(items, insts, u);
            return u;
        }

        static List<Item> MakeItems(string text, SpeakMode mode, out int wordCount)
        {
            var toks = Tokenize(text ?? "");
            var items = new List<Item>();
            int wordIdx = 0;
            var scratch = new List<string>();

            foreach (var t in toks)
            {
                if (t.Kind == TokKind.Punct)
                {
                    if (mode == SpeakMode.Speak && items.Count > 0)
                    {
                        var last = items[items.Count - 1];
                        char p = t.Text[0];
                        if (p == ';' || p == ':') p = ',';
                        last.Boundary = p;
                        last.PauseAfter = p == ',' ? CommaPause : StopPause;
                    }
                    continue;
                }

                int w = wordIdx++;
                if (mode == SpeakMode.Speak || mode == SpeakMode.SaySpell)
                {
                    int first = items.Count;
                    AddSaid(t, w, items, scratch);
                    if (mode == SpeakMode.SaySpell && items.Count > first)
                    {
                        var last = items[items.Count - 1];
                        last.Boundary = ',';
                        last.PauseAfter = SaySpellGap;
                    }
                }
                if (mode == SpeakMode.Spell || mode == SpeakMode.SaySpell)
                    AddSpelled(t, w, items, scratch);
            }

            wordCount = wordIdx;
            if (items.Count == 0) return items;
            var fin = items[items.Count - 1];
            if (fin.Boundary == '\0' || fin.Boundary == ',') { fin.Boundary = '.'; }
            fin.PauseAfter = 0;
            return items;
        }

        // ── Rhyme and alliteration ───────────────────────────────────────────
        // A word's rime runs from its last stressed vowel to its end. Two different
        // words with the same rime rhyme, wherever they are in the phrase list.
        static string Rime(Item it)
        {
            if (it.Text == null) return null;
            if (!it.Accent && it.Boundary == '\0') return null;   // filler words only rhyme at the end of a line or clause
            int from = -1;
            for (int i = it.Ph.Count - 1; i >= 0; i--) if (it.Stress[i]) { from = i; break; }
            if (from < 0) for (int i = it.Ph.Count - 1; i >= 0; i--) if (it.Ph[i].IsVowel) { from = i; break; }
            if (from < 0) return null;
            it.RimeFrom = from;
            var sb = new StringBuilder();
            for (int i = from; i < it.Ph.Count; i++) sb.Append(it.Ph[i].Name).Append(' ');
            string key = sb.ToString();
            return key == "AX " ? null : key;               // a bare schwa rhymes with nothing
        }

        /// <summary>Rimes shared by at least two different words across all the given lines.</summary>
        public static HashSet<string> FindRhymes(IEnumerable<string> texts)
        {
            var words = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var text in texts)
            {
                List<Item> items;
                try { items = MakeItems(text, SpeakMode.Speak, out _); } catch { continue; }
                foreach (var it in items)
                {
                    string key = Rime(it);
                    if (key == null) continue;
                    if (!words.TryGetValue(key, out var set)) words[key] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(it.Text);
                }
            }
            var rhymes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in words) if (kv.Value.Count >= 2) rhymes.Add(kv.Key);
            return rhymes;
        }

        static void MarkSound(List<Item> items, ISet<string> rhymes, Utterance u)
        {
            // Rhyme: against the whole phrase list if given, else within this line
            var keys = new string[items.Count];
            for (int i = 0; i < items.Count; i++) keys[i] = Rime(items[i]);
            var rhymed = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                if (keys[i] == null) continue;
                bool hit = rhymes != null && rhymes.Contains(keys[i]);
                if (rhymes == null)
                    for (int j = 0; j < items.Count && !hit; j++)
                        hit = j != i && keys[j] == keys[i] && items[j].Text != items[i].Text;
                if (!hit) continue;
                items[i].Rhymes = true;
                if (!rhymed.Contains(items[i].Text)) rhymed.Add(items[i].Text);
            }

            // Alliteration: content words close together that open on the same consonant
            var allit = new List<string>();
            var cand = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Text == null || !it.Accent || it.Ph.Count == 0 || it.Ph[0].IsVowel || it.Ph[0].Kind == PKind.Pause) continue;
                for (int c = cand.Count - 1; c >= 0 && c >= cand.Count - 2; c--)
                {
                    var other = items[cand[c]];
                    if (i - cand[c] > 4 || other.Ph[0] != it.Ph[0]) continue;
                    foreach (var w in new[] { other, it })
                    {
                        int n = 0;
                        while (n < w.Ph.Count && !w.Ph[n].IsVowel) n++;
                        w.AllitLen = n;
                        if (!allit.Contains(w.Text)) allit.Add(w.Text);
                    }
                }
                cand.Add(i);
            }

            var sb = new StringBuilder();
            if (rhymed.Count > 0) sb.Append("rhymes: ").Append(string.Join(", ", rhymed).ToLowerInvariant());
            if (allit.Count > 0) sb.Append(sb.Length > 0 ? "   " : "").Append("alliteration: ").Append(string.Join(", ", allit).ToLowerInvariant());
            u.SoundNotes = sb.ToString();
        }

        // ── Tokenizer ────────────────────────────────────────────────────────
        static List<Tok> Tokenize(string s)
        {
            var list = new List<Tok>();
            var sb = new StringBuilder();
            bool digits = false;

            void Flush()
            {
                if (sb.Length > 0) { list.Add(new Tok { Kind = TokKind.Word, Text = sb.ToString() }); sb.Clear(); }
            }

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '[')
                {
                    Flush();
                    int j = s.IndexOf(']', i + 1);
                    if (j < 0) j = s.Length;
                    string inner = s.Substring(i + 1, j - i - 1);
                    if (inner.Trim().Length > 0) list.Add(new Tok { Kind = TokKind.Phon, Text = inner.ToUpperInvariant() });
                    i = j;
                    continue;
                }
                if (char.IsLetter(c) || c == '\'')
                {
                    if (digits) Flush();
                    digits = false;
                    char u = char.ToUpperInvariant(c);
                    if (u >= 'A' && u <= 'Z') sb.Append(u);
                    else if (c == '\'' && sb.Length > 0) sb.Append('\'');
                    continue;
                }
                if (char.IsDigit(c))
                {
                    if (!digits) Flush();
                    digits = true;
                    sb.Append(c);
                    continue;
                }
                Flush();
                digits = false;
                if (c == ',' || c == ';' || c == ':' || c == '.' || c == '!' || c == '?')
                    list.Add(new Tok { Kind = TokKind.Punct, Text = c.ToString() });
            }
            Flush();
            return list;
        }

        // ── Word → phonemes ──────────────────────────────────────────────────
        static void ParseTokens(string spec, Item it, bool forceStress = true)
        {
            foreach (var raw in spec.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.TrimEnd('0', '1', '2');
                bool stress = raw.Length > name.Length && raw[raw.Length - 1] != '0';
                var ph = Phonemes.Get(name);
                if (ph == null) continue;
                it.Ph.Add(ph);
                it.Stress.Add(stress && ph.IsVowel);
            }
            if (forceStress) EnsureStress(it);
        }

        static void EnsureStress(Item it)
        {
            for (int i = 0; i < it.Stress.Count; i++) if (it.Stress[i]) return;
            int pick = -1;
            for (int i = 0; i < it.Ph.Count; i++)
                if (it.Ph[i].IsVowel && it.Ph[i].Name != "AX") { pick = i; break; }
            if (pick < 0)
                for (int i = 0; i < it.Ph.Count; i++) if (it.Ph[i].IsVowel) { pick = i; break; }
            if (pick >= 0) it.Stress[pick] = true;
        }

        static Item WordItem(string w, int wordIdx, List<string> scratch)
        {
            bool content = !FunctionWords.Contains(w);
            var it = new Item { Word = wordIdx, Accent = content, PauseAfter = WordGap, Text = w };
            if (LetterToSound.Exceptions.TryGetValue(w, out var spec)) { ParseTokens(spec, it, content); return it; }
            spec = Lexicon.Lookup(w);                    // the CMU Pronouncing Dictionary
            if (spec != null)
            {
                if (!content) spec = spec.Replace("1", "");  // filler words stay unstressed, as with the built-in list
                ParseTokens(spec, it, content);
                return it;
            }
            spec = Inflected(w);                         // a built-in word plus a regular ending the dictionary lacks
            if (spec != null) { ParseTokens(spec, it, content); return it; }
            string letters = w.Replace("'", "");
            if (letters.Length == 1) { ParseTokens(LetterToSound.LetterNames[letters[0] - 'A'], it); return it; }
            scratch.Clear();
            LetterToSound.Translate(w, scratch);
            for (int k = scratch.Count - 1; k > 0; k--)   // doubled letters are one sound (running, little)
                if (scratch[k] == scratch[k - 1] && !(Phonemes.Get(scratch[k])?.IsVowel ?? true)) scratch.RemoveAt(k);
            ParseTokens(string.Join(" ", scratch), it, content);
            return it;
        }

        // A dictionary word with a regular ending, pronounced from the dictionary entry
        static string Inflected(string w)
        {
            string stem, last;
            bool Try(string s) => LetterToSound.Exceptions.TryGetValue(s, out stem);
            string Last() { var p = stem.Split(' '); return p[p.Length - 1].TrimEnd('0', '1', '2'); }
            const string Voiceless = " P T K F TH ", Sibilant = " S Z SH ZH CH JH ";

            string bare = w.EndsWith("'S") ? w.Substring(0, w.Length - 2) : null;
            if ((bare != null && Try(bare)) || (w.EndsWith("S") && Try(w.Substring(0, w.Length - 1)))
                || (w.EndsWith("ES") && Try(w.Substring(0, w.Length - 2))))
            {
                last = " " + Last() + " ";
                return stem + (Sibilant.Contains(last) ? " IH Z" : Voiceless.Contains(last) ? " S" : " Z");
            }
            if (w.EndsWith("ING") && (Try(w.Substring(0, w.Length - 3)) || Try(w.Substring(0, w.Length - 3) + "E")))
                return stem + " IH NG";
            if (w.EndsWith("ED") && (Try(w.Substring(0, w.Length - 2)) || Try(w.Substring(0, w.Length - 1))))
            {
                last = " " + Last() + " ";
                return stem + (last == " T " || last == " D " ? " IH D" : Voiceless.Contains(last) || Sibilant.Contains(last) && last != " Z " && last != " ZH " && last != " JH " ? " T" : " D");
            }
            if (w.EndsWith("LY") && Try(w.Substring(0, w.Length - 2))) return stem + " L IY";
            return null;
        }

        static void AddSaid(Tok t, int w, List<Item> items, List<string> scratch)
        {
            if (t.Kind == TokKind.Phon)
            {
                var it = new Item { Word = w, Accent = true, PauseAfter = WordGap };
                ParseTokens(t.Text.Replace("_", " _ "), it);
                if (it.Ph.Count > 0) items.Add(it);
                return;
            }
            if (char.IsDigit(t.Text[0]))
            {
                var words = new List<string>();
                LetterToSound.NumberToWords(t.Text, words);
                foreach (var nw in words)
                {
                    var it = WordItem(nw, w, scratch);
                    if (it.Ph.Count > 0) items.Add(it);
                }
                return;
            }
            var wi = WordItem(t.Text, w, scratch);
            if (wi.Ph.Count > 0) items.Add(wi);
        }

        static void AddSpelled(Tok t, int w, List<Item> items, List<string> scratch)
        {
            int first = items.Count;
            if (t.Kind == TokKind.Phon)
            {
                AddSaid(t, w, items, scratch);
            }
            else
            {
                foreach (char c in t.Text)
                {
                    var it = new Item { Word = w, Accent = true, PauseAfter = LetterPause };
                    if (c >= 'A' && c <= 'Z') ParseTokens(LetterToSound.LetterNames[c - 'A'], it);
                    else if (c >= '0' && c <= '9') ParseTokens(LetterToSound.Exceptions[LetterToSound.DigitWords[c - '0']], it);
                    else continue;
                    items.Add(it);
                }
            }
            if (items.Count > first)
            {
                var last = items[items.Count - 1];
                last.Boundary = '.';
                last.PauseAfter = SpellEnd;
            }
        }

        // ── Durations ────────────────────────────────────────────────────────
        static List<Inst> Time(List<Item> items)
        {
            var list = new List<Inst>();
            float t = 0;
            for (int ii = 0; ii < items.Count; ii++)
            {
                var it = items[ii];
                int lastVowel = -1;
                for (int i = 0; i < it.Ph.Count; i++) if (it.Ph[i].IsVowel) lastVowel = i;

                for (int i = 0; i < it.Ph.Count; i++)
                {
                    var ph = it.Ph[i];
                    var x = new Inst { Ph = ph, Stress = it.Stress[i], Item = ii, Idx = i };
                    float d = ph.Dur;

                    if (ph.Kind == PKind.Pause) d = 120f;
                    else if (ph.IsVowel)
                    {
                        if (x.Stress) d *= 1.1f;
                        else if (ph.Name != "AX") d *= 0.75f;
                        if (i == lastVowel)
                        {
                            d *= 1.15f;
                            if (it.Boundary == '.' || it.Boundary == '?' || it.Boundary == '!') d *= 1.3f;
                            else if (it.Boundary == ',') d *= 1.12f;
                        }
                        x.AccentVowel = it.Accent && x.Stress;
                    }
                    else if (ph.IsStopLike)
                    {
                        // S + voiceless stop: no aspiration ("spell", "stop", "sky")
                        if (ph.Kind == PKind.Stop && i > 0 && it.Ph[i - 1].Name == "S")
                        {
                            x.NoAsp = true;
                            d -= ph.Asp;
                        }
                    }
                    else
                    {
                        bool prevCons = i > 0 && !it.Ph[i - 1].IsVowel;
                        bool nextCons = i + 1 < it.Ph.Count && !it.Ph[i + 1].IsVowel;
                        if (prevCons || nextCons) d *= 0.85f;
                    }

                    x.T = t; x.D = d;
                    t += d;
                    list.Add(x);
                }

                if (it.PauseAfter > 0)
                {
                    list.Add(new Inst { Ph = Phonemes.Pause, T = t, D = it.PauseAfter, Item = ii });
                    t += it.PauseAfter;
                }
            }
            return list;
        }

        // ── Intonation ───────────────────────────────────────────────────────
        static void BuildPitch(List<Item> items, List<Inst> insts, Utterance u)
        {
            var keys = new List<PitchKey>();
            int gStartItem = 0;
            int instPos = 0;

            for (int ii = 0; ii < items.Count; ii++)
            {
                char b = items[ii].Boundary;
                if (b == '\0' && ii != items.Count - 1) continue;

                // Group = items gStartItem..ii
                int i0 = instPos, i1 = i0;
                while (i1 < insts.Count && insts[i1].Item <= ii) i1++;
                instPos = i1;

                float ts = float.MaxValue, te = 0;
                int lastAcc = -1;
                for (int k = i0; k < i1; k++)
                {
                    var x = insts[k];
                    if (x.Ph.Kind == PKind.Pause) continue;
                    ts = Math.Min(ts, x.T);
                    te = Math.Max(te, x.T + x.D);
                    if (x.AccentVowel) lastAcc = k;
                }
                if (lastAcc < 0)        // no accented word: use the last vowel as the nucleus
                    for (int k = i1 - 1; k >= i0; k--) if (insts[k].Ph.IsVowel) { lastAcc = k; break; }

                if (ts < te)
                {
                    float span = Math.Max(1f, te - ts);
                    float Decl(float t) => 1.2f - 2.4f * (t - ts) / span;
                    float accScale = b == '!' ? 1.5f : 1f;
                    int accCount = 0;
                    float nucMid = -1, nucVal = 0;

                    var gk = new List<PitchKey>();
                    for (int k = i0; k < i1; k++)
                    {
                        var x = insts[k];
                        if (x.Ph.Kind == PKind.Pause) continue;
                        bool acc = x.AccentVowel || (k == lastAcc && x.Ph.IsVowel);
                        if (acc)
                        {
                            float a = (accCount++ == 0 ? 3.5f : 2.2f) * accScale;
                            float mid = x.T + x.D * 0.4f;
                            gk.Add(new PitchKey { T = x.T, Semis = Decl(x.T) + 0.5f * a });
                            gk.Add(new PitchKey { T = mid, Semis = Decl(mid) + a });
                            gk.Add(new PitchKey { T = x.T + x.D, Semis = Decl(x.T + x.D) + 0.55f * a });
                            if (k == lastAcc) { nucMid = mid; nucVal = Decl(mid) + a; }
                        }
                        else
                        {
                            gk.Add(new PitchKey { T = x.T, Semis = Decl(x.T) });
                            gk.Add(new PitchKey { T = x.T + x.D, Semis = Decl(x.T + x.D) });
                        }
                    }

                    // Final contour
                    for (int k = 0; k < gk.Count; k++)
                    {
                        var p = gk[k];
                        if ((b == '.' || b == '!' || b == '\0') && nucMid >= 0 && p.T > nucMid)
                        {
                            float x = (p.T - nucMid) / Math.Max(1f, te - nucMid);
                            p.Semis = nucVal + (Decl(te) - 3f - nucVal) * x;
                        }
                        else if (b == '?')
                        {
                            float start = te - 380f;
                            if (p.T > start) { float x = (p.T - start) / 380f; p.Semis += 6f * x * x; }
                        }
                        else if (b == ',')
                        {
                            float start = te - 220f;
                            if (p.T > start) p.Semis += 1.5f * (p.T - start) / 220f;
                        }
                        gk[k] = p;
                    }
                    keys.AddRange(gk);
                }
                gStartItem = ii + 1;
            }

            // Enforce non-decreasing time
            for (int k = 1; k < keys.Count; k++)
                if (keys[k].T < keys[k - 1].T) { var p = keys[k]; p.T = keys[k - 1].T; keys[k] = p; }
            u.P = keys.ToArray();
        }

        // ── Formant and source keyframes ─────────────────────────────────────
        static float TransMs(PKind k)
        {
            switch (k)
            {
                case PKind.Vowel: case PKind.Diphthong: return 30f;
                case PKind.Glide: return 35f;
                case PKind.Nasal: return 15f;
                default: return 15f;
            }
        }

        static FormantKey FK(float t, float[] f, float[] b, float bwMul = 1f)
        {
            return new FormantKey
            {
                T = t, F1 = f[0], F2 = f[1], F3 = f[2], F4 = f[3],
                B1 = b[0] * bwMul, B2 = b[1] * bwMul, B3 = b[2] * bwMul, B4 = b[3] * bwMul,
            };
        }

        static void BuildSpectral(List<Inst> insts, Utterance u)
        {
            var fk = new List<FormantKey>();
            var sk = new List<SourceKey>();
            var fEnd = new float[4];

            void Src(float t, float av, float an) => sk.Add(new SourceKey { T = t, AV = av, AN = an });

            for (int i = 0; i < insts.Count; i++)
            {
                var x = insts[i];
                var ph = x.Ph;
                float T = x.T, D = x.D;
                Inst prev = i > 0 ? insts[i - 1] : null;
                Inst next = i + 1 < insts.Count ? insts[i + 1] : null;

                switch (ph.Kind)
                {
                    case PKind.Pause:
                        Src(T + 1f, 0, 0);
                        Src(T + D - 1f, 0, 0);
                        break;

                    case PKind.Aspirate:
                    {
                        Phoneme tgt = (next != null && next.Ph.IsSonorant) ? next.Ph : ph;
                        float bw = tgt == ph ? 1f : 1.8f;
                        fk.Add(FK(T + 3f, tgt.F, tgt.B, bw));
                        fk.Add(FK(T + D - 3f, tgt.F, tgt.B, bw));
                        Src(T + 2f, 0, ph.AN);
                        Src(T + D - 2f, 0, ph.AN);
                        break;
                    }

                    case PKind.Stop: case PKind.VoicedStop:
                    case PKind.Affricate: case PKind.VoicedAffricate:
                    {
                        bool voiced = ph.Kind == PKind.VoicedStop || ph.Kind == PKind.VoicedAffricate;
                        float clos = ph.Closure, burst = Math.Max(2f, ph.Burst);
                        float tb = T + clos, ta = tb + burst;

                        fk.Add(FK(T, ph.F, ph.B));
                        fk.Add(FK(tb, ph.F, ph.B));
                        fk.Add(FK(tb + 0.3f, ph.BurstF, ph.BurstB));
                        fk.Add(FK(ta - 0.3f, ph.BurstF, ph.BurstB));

                        Src(T + 0.5f, ph.VoiceBar, 0);
                        Src(tb - 0.3f, ph.VoiceBar, 0);
                        Src(tb, 0, ph.BurstAmp);
                        Src(ta - 0.3f, 0, ph.BurstAmp * 0.6f);

                        if (ph.FricRef != null)
                        {
                            var fp = Phonemes.Get(ph.FricRef);
                            fk.Add(FK(ta + 0.3f, fp.F, fp.B));
                            fk.Add(FK(T + D - 8f, fp.F, fp.B));
                            Src(ta + 0.3f, voiced ? 0.35f : 0f, fp.AN * 0.9f);
                            Src(T + D - 2f, voiced ? 0.35f : 0f, fp.AN * 0.7f);
                        }
                        else
                        {
                            fk.Add(FK(ta, ph.F, ph.B));      // formants leave the locus from here
                            float asp = x.NoAsp ? 0f : ph.Asp;
                            if (asp > 0)
                            {
                                Src(ta, 0, 0.42f);
                                Src(T + D - 1f, 0, 0.32f);
                            }
                            else
                            {
                                Src(ta, voiced ? 0.25f : 0f, 0);
                            }
                        }
                        break;
                    }

                    default:
                    {
                        float tr = TransMs(ph.Kind);
                        float tIn = Math.Min(tr, D * 0.35f);
                        float tOut = Math.Min(tr, D * 0.35f);
                        if (prev != null && prev.Ph.IsStopLike) tIn = Math.Min(40f, D * 0.4f);

                        fk.Add(FK(T + tIn, ph.F, ph.B));
                        if (ph.FEnd != null)
                        {
                            fEnd[0] = ph.FEnd[0]; fEnd[1] = ph.FEnd[1]; fEnd[2] = ph.FEnd[2]; fEnd[3] = ph.F[3];
                            float tm = T + Math.Max(tIn, D * 0.25f);
                            if (tm > T + D - tOut) tm = T + D - tOut;
                            fk.Add(FK(tm, ph.F, ph.B));
                            fk.Add(FK(T + D - tOut, fEnd, ph.B));
                        }
                        else
                        {
                            fk.Add(FK(T + D - tOut, ph.F, ph.B));
                        }
                        Src(T + 2f, ph.AV, ph.AN);
                        Src(T + D - 2f, ph.AV, ph.AN);
                        break;
                    }
                }
            }

            var last = insts[insts.Count - 1];
            Src(last.T + last.D + 1f, 0, 0);

            // Enforce non-decreasing times (tiny phonemes can produce overlaps)
            for (int k = 1; k < fk.Count; k++)
                if (fk[k].T < fk[k - 1].T) { var f = fk[k]; f.T = fk[k - 1].T; fk[k] = f; }
            for (int k = 1; k < sk.Count; k++)
                if (sk[k].T < sk[k - 1].T) { var s = sk[k]; s.T = sk[k - 1].T; sk[k] = s; }

            u.F = fk.ToArray();
            u.S = sk.ToArray();
            u.Length = last.T + last.D;
        }

        // ── Word ranges (for the Word / Words track columns) ─────────────────
        static void BuildWords(List<Item> items, List<Inst> insts, int wordCount, Utterance u)
        {
            var ws = new float[wordCount];
            var we = new float[wordCount];
            for (int w = 0; w < wordCount; w++) { ws[w] = float.MaxValue; we[w] = 0; }
            foreach (var x in insts)
            {
                int w = items[x.Item].Word;
                if ((uint)w >= (uint)wordCount) continue;
                float end = x.Ph.Kind == PKind.Pause ? x.T + Math.Min(x.D, 60f) : x.T + x.D;
                if (x.Ph.Kind != PKind.Pause) ws[w] = Math.Min(ws[w], x.T);
                we[w] = Math.Max(we[w], end);
            }
            // Words that produced no sound collapse onto their neighbour's start
            float prevEnd = 0;
            for (int w = 0; w < wordCount; w++)
            {
                if (ws[w] == float.MaxValue) { ws[w] = prevEnd; we[w] = prevEnd; }
                prevEnd = we[w];
            }
            u.WordStart = ws;
            u.WordEnd = we;
        }

        // ── How each sound feels (drives the React controls) ─────────────────
        static float Unit(float x, float lo, float hi) => Math.Clamp((x - lo) / (hi - lo), 0f, 1f);

        static void BuildFeel(List<Item> items, List<Inst> insts, Utterance u)
        {
            var keys = new List<FeelKey>();
            foreach (var x in insts)
            {
                var ph = x.Ph;
                if (ph.Kind == PKind.Pause) continue;
                var k = new FeelKey { T = x.T + x.D * 0.5f, Open = 0.5f, Bright = 0.5f, Hard = 0.5f, Sung = 0f };
                var it = items[x.Item];
                if (it.Rhymes && x.Idx >= it.RimeFrom) k.Rhyme = 1f;
                if (x.Idx < it.AllitLen) k.Allit = 1f;
                switch (ph.Kind)
                {
                    case PKind.Vowel:
                    case PKind.Diphthong:
                        k.Open = Unit(ph.F[0], 300f, 700f);
                        k.Bright = Unit(ph.F[1], 900f, 2200f);
                        k.Sung = x.Stress ? 1f : 0.7f;
                        break;
                    case PKind.Glide:
                    case PKind.Nasal:
                        k.Bright = Unit(ph.F[1], 600f, 2100f) * 0.8f;
                        k.Hard = 0.3f; k.Sung = 0.5f;
                        break;
                    case PKind.Fricative:
                    case PKind.VoicedFricative:
                    case PKind.Aspirate:
                    {
                        bool sib = ph.Name == "S" || ph.Name == "SH" || ph.Name == "Z" || ph.Name == "ZH";
                        k.Bright = sib ? 1f : 0.45f;
                        k.Hard = sib ? 0.6f : 0.15f;
                        break;
                    }
                    default:                                   // stops and affricates
                    {
                        bool voiced = ph.Kind == PKind.VoicedStop || ph.Kind == PKind.VoicedAffricate;
                        k.T = x.T + ph.Closure;                // the feel peaks at the burst
                        k.Hard = voiced ? 0.85f : 1f;
                        k.Bright = Unit(ph.BurstF[1], 900f, 1900f);
                        break;
                    }
                }
                keys.Add(k);
            }
            for (int i = 1; i < keys.Count; i++)
                if (keys[i].T < keys[i - 1].T) { var k = keys[i]; k.T = keys[i - 1].T; keys[i] = k; }
            u.Q = keys.ToArray();
        }

        // ── Syllables and phonemes (the units the step modes play) ───────────
        static bool Is(string n, string set) => (" " + set + " ").Contains(" " + n + " ");

        static bool LegalOnset(List<Inst> g, int from, int to)
        {
            int n = to - from;
            string a = g[from].Ph.Name;
            if (n == 1) return a != "NG";
            string b = g[from + 1].Ph.Name;
            if (n == 2)
            {
                if (a == "S") return Is(b, "P T K M N F L W Y");
                if (b == "L") return Is(a, "P B K G F");
                if (b == "R") return Is(a, "P B T D K G F TH SH");
                if (b == "W") return Is(a, "T D K G TH");
                if (b == "Y") return Is(a, "P B K G F V M N HH T D L");
                return false;
            }
            if (n == 3) return a == "S" && Is(b, "P T K") && LegalOnset(g, from + 1, to);
            return false;
        }

        // Labels for the melody editor: the word's spelling cut at its syllables (to-, -night).
        // A guess from the letters; when the vowel groups don't match the syllable count
        // the whole word goes on the first block and a dash on the rest.
        static string[] SpellSyllables(string word, int n)
        {
            string w = word.ToLowerInvariant();
            var res = new string[n];
            if (n <= 1) { res[0] = w; return res; }
            bool V(char c) => "aeiouy".IndexOf(c) >= 0;
            var gs = new List<int>(); var ge = new List<int>();          // vowel-letter groups: start, end (exclusive)
            for (int i = 0; i < w.Length; i++)
            {
                if (!V(w[i]) || (i > 0 && V(w[i - 1]))) continue;
                int e = i; while (e < w.Length && V(w[e])) e++;
                gs.Add(i); ge.Add(e);
            }
            if (gs.Count == n + 1 && w.EndsWith("e") && ge[gs.Count - 1] - gs[gs.Count - 1] == 1) { gs.RemoveAt(n); ge.RemoveAt(n); }   // silent e
            if (gs.Count != n)
            {
                res[0] = w + "-";
                for (int i = 1; i < n; i++) res[i] = "-";
                return res;
            }
            int from = 0;
            for (int k = 0; k < n; k++)
            {
                int cut = w.Length;
                if (k < n - 1)
                {
                    int a = ge[k], b = gs[k + 1], run = b - a;           // consonant letters between two vowel groups
                    string pair = run == 2 ? w.Substring(a, 2) : "";
                    if (run == 0) cut = b;
                    else if (pair == "ng" || pair == "ck") cut = b;      // these close the syllable before
                    else if (pair == "th" || pair == "sh" || pair == "ch" || pair == "ph") cut = a;
                    else cut = b - 1;                                    // otherwise the last consonant opens the next
                }
                res[k] = (k > 0 ? "-" : "") + w.Substring(from, cut - from) + (k < n - 1 ? "-" : "");
                from = cut;
            }
            return res;
        }

        static void BuildUnits(List<Item> items, List<Inst> insts, Utterance u)
        {
            var ss = new List<float>(); var sh = new List<float>(); var se = new List<float>(); var sw = new List<int>();
            var sv = new List<float>(); var st = new List<string>(); var lab = new StringBuilder();
            var ps = new List<float>(); var ph = new List<float>(); var pe = new List<float>(); var pw = new List<int>();
            var sb = new StringBuilder();
            var g = new List<Inst>();
            var starts = new List<int>();
            int lastWord = -1;

            int i = 0;
            while (i < insts.Count)
            {
                int item = insts[i].Item;
                g.Clear();
                for (; i < insts.Count && insts[i].Item == item; i++)
                    if (insts[i].Ph.Kind != PKind.Pause) g.Add(insts[i]);
                if (g.Count == 0) continue;
                int word = items[item].Word;

                foreach (var x in g)
                {
                    ps.Add(x.T); pe.Add(x.T + x.D); pw.Add(word);
                    ph.Add(x.Ph.IsStopLike ? x.T + x.D - 1f : x.T + x.D * 0.5f);
                }

                // Maximal-onset syllabification
                starts.Clear(); starts.Add(0);
                int prevV = -1;
                for (int k = 0; k < g.Count; k++)
                {
                    if (!g[k].Ph.IsVowel) continue;
                    if (prevV >= 0)
                    {
                        int p = k;
                        while (p - 1 > prevV && LegalOnset(g, p - 1, k)) p--;
                        starts.Add(p);
                    }
                    prevV = k;
                }

                string[] spell = items[item].Text != null ? SpellSyllables(items[item].Text, starts.Count) : null;
                if (sb.Length > 0) sb.Append(word != lastWord ? "   " : " ");
                lastWord = word;
                for (int j = 0; j < starts.Count; j++)
                {
                    int a = starts[j], b = (j + 1 < starts.Count ? starts[j + 1] : g.Count) - 1;
                    float s0 = g[a].T, e0 = g[b].T + g[b].D, h0 = -1f, best = 0f, v0 = s0;
                    for (int k = a; k <= b; k++)
                        if (g[k].Ph.IsVowel) { h0 = g[k].T + Math.Min(0.45f * g[k].D, 50f); v0 = g[k].T; break; }
                    lab.Clear();
                    for (int k = a; k <= b; k++) lab.Append(k > a ? " " : "").Append(g[k].Ph.Name.ToLowerInvariant());
                    sv.Add(v0); st.Add(spell != null ? spell[j] : lab.ToString());
                    if (h0 < 0f)
                        for (int k = a; k <= b; k++)
                            if (!g[k].Ph.IsStopLike && g[k].D > best) { best = g[k].D; h0 = g[k].T + g[k].D * 0.5f; }
                    if (h0 < 0f) h0 = e0 - 1f;
                    ss.Add(s0); sh.Add(h0); se.Add(e0); sw.Add(word);

                    if (j > 0) sb.Append(" \u00B7 ");
                    for (int k = a; k <= b; k++)
                    {
                        if (k > a) sb.Append(' ');
                        sb.Append(g[k].Ph.Name);
                        if (g[k].Stress) sb.Append('1');
                    }
                }
                if (items[item].Boundary != '\0') sb.Append(' ').Append(items[item].Boundary);
            }

            u.SylS = ss.ToArray(); u.SylH = sh.ToArray(); u.SylE = se.ToArray(); u.SylWord = sw.ToArray();
            u.SylV = sv.ToArray(); u.SylText = st.ToArray();
            u.PhS = ps.ToArray(); u.PhH = ph.ToArray(); u.PhE = pe.ToArray(); u.PhWord = pw.ToArray();
            u.Phonetic = sb.ToString();
        }
    }
}
