// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — English letter-to-sound.
//
// The rule set follows the method of Elovitz, Johnson, McHugh and Shore,
// "Automatic Translation of English Text to Phonetics by Means of
// Letter-to-Sound Rules" (NRL Report 7948, U.S. Naval Research Laboratory,
// 1976), a U.S. Government work. The rules below were written out for this
// machine in that notation; they are not copied from any program's source.
//
// Rule notation:  left [match] right = phonemes
//   ' '  word boundary (any non-letter)
//   '#'  one or more vowels          ':'  zero or more consonants
//   '^'  one consonant               '*'  one or more consonants
//   '.'  one voiced consonant (B D V G J L M N R W Z)
//   '+'  a front vowel (E I Y)       '&'  a sibilant (S C G Z X J CH SH)
//   '@'  T S R D L Z N J TH CH SH    '%'  a suffix (E ER ES ED ING ELY)
// An empty context matches anything. Rules for a letter are tried in order and
// the first match wins, so the specific rules come before the general ones.
// Vowels for context purposes are A E I O U; Y counts as a consonant.

using System;
using System.Collections.Generic;
using System.Text;

namespace PedalRoboSing.Speech
{
    public static class LetterToSound
    {
        readonly struct Rule
        {
            public readonly string L, M, R, Out;
            public Rule(string l, string m, string r, string o) { L = l; M = m; R = r; Out = o; }
        }

        static readonly Dictionary<char, Rule[]> _rules = new Dictionary<char, Rule[]>();

        static Rule R(string l, string m, string r, string o) => new Rule(l, m, r, o);
        const string A = "";     // anything
        const string N = " ";    // word boundary

        static LetterToSound()
        {
            _rules['A'] = new[] {
                R(N, "A", N, "AX"),
                R(N, "ARE", N, "AA R"),
                R(N, "AR", "O", "AX R"),
                R(A, "AR", "#", "EH R"),
                R(" ^", "AS", "#", "EY S"),
                R(A, "A", "WA", "AX"),
                R(A, "AW", A, "AO"),
                R(" :", "ANY", A, "EH N IY"),
                R(A, "A", "^+#", "EY"),
                R("#:", "ALLY", A, "AX L IY"),
                R(N, "AL", "#", "AX L"),
                R(A, "AGAIN", A, "AX G EH N"),
                R("#:", "AG", "E", "IH JH"),
                R(A, "A", "^+:#", "AE"),
                R(" :", "A", "^+ ", "EY"),
                R(A, "A", "^%", "EY"),
                R(N, "ARR", A, "AX R"),
                R(A, "ARR", A, "AE R"),
                R(" :", "AR", N, "AA R"),
                R(A, "AR", N, "ER"),
                R(A, "AR", A, "AA R"),
                R(A, "AIR", A, "EH R"),
                R(A, "AI", A, "EY"),
                R(A, "AY", A, "EY"),
                R(A, "AU", A, "AO"),
                R("#:", "AL", N, "AX L"),
                R("#:", "ALS", N, "AX L Z"),
                R(A, "ALK", A, "AO K"),
                R(A, "AL", "^", "AO L"),
                R(" :", "ABLE", A, "EY B AX L"),
                R(A, "ABLE", A, "AX B AX L"),
                R(A, "ANG", "+", "EY N JH"),
                R(A, "A", A, "AE"),
            };
            _rules['B'] = new[] {
                R(N, "BE", "^#", "B IH"),
                R(A, "BEING", A, "B IY IH NG"),
                R(N, "BOTH", N, "B OW TH"),
                R(N, "BUS", "#", "B IH Z"),
                R(A, "BUIL", A, "B IH L"),
                R(A, "B", A, "B"),
            };
            _rules['C'] = new[] {
                R(N, "CH", "^", "K"),
                R("^E", "CH", A, "K"),
                R(A, "CH", A, "CH"),
                R(" S", "CI", "#", "S AY"),
                R(A, "CI", "A", "SH"),
                R(A, "CI", "O", "SH"),
                R(A, "CI", "EN", "SH"),
                R(A, "C", "+", "S"),
                R(A, "CK", A, "K"),
                R(A, "COM", "%", "K AH M"),
                R(A, "C", A, "K"),
            };
            _rules['D'] = new[] {
                R("#:", "DED", N, "D IH D"),
                R(".E", "D", N, "D"),
                R("#^:E", "D", N, "T"),
                R(N, "DE", "^#", "D IH"),
                R(N, "DO", N, "D UW"),
                R(N, "DOES", A, "D AH Z"),
                R(N, "DOING", A, "D UW IH NG"),
                R(N, "DOW", A, "D AW"),
                R(A, "DU", "A", "JH UW"),
                R(A, "D", A, "D"),
            };
            _rules['E'] = new[] {
                R("#:", "E", N, ""),
                R("'^:", "E", N, ""),
                R(" :", "E", N, "IY"),
                R("#", "ED", N, "D"),
                R("#:", "E", "D ", ""),
                R(A, "EV", "ER", "EH V"),
                R(A, "E", "^%", "IY"),
                R(A, "ERI", "#", "IY R IY"),
                R(A, "ERI", A, "EH R IH"),
                R("#:", "ER", "#", "ER"),
                R(A, "ER", "#", "EH R"),
                R(A, "ER", A, "ER"),
                R(N, "EVEN", A, "IY V EH N"),
                R("#:", "E", "W", ""),
                R("@", "EW", A, "UW"),
                R(A, "EW", A, "Y UW"),
                R(A, "E", "O", "IY"),
                R("#:&", "ES", N, "IH Z"),
                R("#:", "E", "S ", ""),
                R("#:", "ELY", N, "L IY"),
                R("#:", "EMENT", A, "M EH N T"),
                R(A, "EFUL", A, "F UH L"),
                R(A, "EE", A, "IY"),
                R(A, "EARN", A, "ER N"),
                R(N, "EAR", "^", "ER"),
                R(A, "EAD", A, "EH D"),
                R("#:", "EA", N, "IY AX"),
                R(A, "EA", "SU", "EH"),
                R(A, "EA", A, "IY"),
                R(A, "EIGH", A, "EY"),
                R(A, "EI", A, "IY"),
                R(N, "EYE", A, "AY"),
                R(A, "EY", A, "IY"),
                R(A, "EU", A, "Y UW"),
                R(A, "E", A, "EH"),
            };
            _rules['F'] = new[] {
                R(A, "FUL", A, "F UH L"),
                R(A, "F", A, "F"),
            };
            _rules['G'] = new[] {
                R(A, "GIV", A, "G IH V"),
                R(N, "G", "I^", "G"),
                R(A, "GE", "T", "G EH"),
                R("SU", "GGES", A, "G JH EH S"),
                R(A, "GG", A, "G"),
                R(" B#", "G", A, "G"),
                R(A, "G", "+", "JH"),
                R(A, "GREAT", A, "G R EY T"),
                R("#", "GH", A, ""),
                R(A, "G", A, "G"),
            };
            _rules['H'] = new[] {
                R(N, "HAV", A, "HH AE V"),
                R(N, "HERE", A, "HH IY R"),
                R(N, "HOUR", A, "AW ER"),
                R(A, "HOW", A, "HH AW"),
                R(A, "H", "#", "HH"),
                R(A, "H", A, ""),
            };
            _rules['I'] = new[] {
                R(N, "IN", A, "IH N"),
                R(N, "I", N, "AY"),
                R(A, "IN", "D", "AY N"),
                R(A, "IER", A, "IY ER"),
                R("#:R", "IED", A, "IY D"),
                R(A, "IED", N, "AY D"),
                R(A, "IEN", A, "IY EH N"),
                R(A, "IE", "T", "AY EH"),
                R(" :", "I", "%", "AY"),
                R(A, "I", "%", "IY"),
                R(A, "IE", A, "IY"),
                R(A, "I", "^+:#", "IH"),
                R(A, "IR", "#", "AY R"),
                R(A, "IZ", "%", "AY Z"),
                R(A, "IS", "%", "AY Z"),
                R(A, "I", "D%", "AY"),
                R("+^", "I", "^+", "IH"),
                R(A, "I", "T%", "AY"),
                R("#^:", "I", "^+", "IH"),
                R(A, "I", "^+", "AY"),
                R(A, "IR", A, "ER"),
                R(A, "IGH", A, "AY"),
                R(A, "ILD", A, "AY L D"),
                R(A, "IGN", N, "AY N"),
                R(A, "IGN", "^", "AY N"),
                R(A, "IGN", "%", "AY N"),
                R(A, "IQUE", A, "IY K"),
                R(A, "I", A, "IH"),
            };
            _rules['J'] = new[] { R(A, "J", A, "JH") };
            _rules['K'] = new[] {
                R(N, "K", "N", ""),
                R(A, "K", A, "K"),
            };
            _rules['L'] = new[] {
                R(A, "LO", "C#", "L OW"),
                R("L", "L", A, ""),
                R("#^:", "L", "%", "AX L"),
                R(A, "LEAD", A, "L IY D"),
                R(A, "L", A, "L"),
            };
            _rules['M'] = new[] {
                R(A, "MOV", A, "M UW V"),
                R(A, "M", A, "M"),
            };
            _rules['N'] = new[] {
                R("E", "NG", "+", "N JH"),
                R(A, "NG", "R", "NG G"),
                R(A, "NG", "#", "NG G"),
                R(A, "NGL", "%", "NG G AX L"),
                R(A, "NG", A, "NG"),
                R(A, "NK", A, "NG K"),
                R(N, "NOW", N, "N AW"),
                R(A, "N", A, "N"),
            };
            _rules['O'] = new[] {
                R(A, "OF", N, "AX V"),
                R(A, "OROUGH", A, "ER OW"),
                R("#:", "OR", N, "ER"),
                R("#:", "ORS", N, "ER Z"),
                R(A, "OR", A, "AO R"),
                R(N, "ONE", A, "W AH N"),
                R(A, "OW", A, "OW"),
                R(N, "OVER", A, "OW V ER"),
                R(A, "OV", A, "AH V"),
                R(A, "O", "^%", "OW"),
                R(A, "O", "^EN", "OW"),
                R(A, "O", "^I#", "OW"),
                R(A, "OL", "D", "OW L"),
                R(A, "OUGHT", A, "AO T"),
                R(A, "OUGH", A, "AH F"),
                R(N, "OU", A, "AW"),
                R("H", "OU", "S#", "AW"),
                R(A, "OUS", A, "AX S"),
                R(A, "OUR", A, "AO R"),
                R(A, "OULD", A, "UH D"),
                R("^", "OU", "^L", "AH"),
                R(A, "OUP", A, "UW P"),
                R(A, "OU", A, "AW"),
                R(A, "OY", A, "OY"),
                R(A, "OING", A, "OW IH NG"),
                R(A, "OI", A, "OY"),
                R(A, "OOR", A, "AO R"),
                R(A, "OOK", A, "UH K"),
                R(A, "OOD", A, "UH D"),
                R(A, "OO", A, "UW"),
                R(A, "O", "E", "OW"),
                R(A, "O", N, "OW"),
                R(A, "OA", A, "OW"),
                R(N, "ONLY", A, "OW N L IY"),
                R(N, "ONCE", A, "W AH N S"),
                R(A, "ON'T", A, "OW N T"),
                R("C", "O", "N", "AA"),
                R(A, "O", "NG", "AO"),
                R(" :^", "O", "N", "AH"),
                R("I", "ON", A, "AX N"),
                R("#:", "ON", N, "AX N"),
                R("#^", "ON", A, "AX N"),
                R(A, "O", "ST ", "OW"),
                R(A, "OF", "^", "AO F"),
                R(A, "OTHER", A, "AH DH ER"),
                R(A, "OSS", N, "AO S"),
                R("#^:", "OM", A, "AH M"),
                R(A, "O", A, "AA"),
            };
            _rules['P'] = new[] {
                R(A, "PH", A, "F"),
                R(A, "PEOP", A, "P IY P"),
                R(A, "POW", A, "P AW"),
                R(A, "PUT", N, "P UH T"),
                R(A, "P", A, "P"),
            };
            _rules['Q'] = new[] {
                R(A, "QUAR", A, "K W AO R"),
                R(A, "QU", A, "K W"),
                R(A, "Q", A, "K"),
            };
            _rules['R'] = new[] {
                R(N, "RE", "^#", "R IY"),
                R(A, "R", A, "R"),
            };
            _rules['S'] = new[] {
                R(A, "SH", A, "SH"),
                R("#", "SION", A, "ZH AX N"),
                R(A, "SOME", A, "S AH M"),
                R("#", "SUR", "#", "ZH ER"),
                R(A, "SUR", "#", "SH ER"),
                R("#", "SU", "#", "ZH UW"),
                R("#", "SSU", "#", "SH UW"),
                R("#", "SED", N, "Z D"),
                R("#", "S", "#", "Z"),
                R(A, "SAID", A, "S EH D"),
                R("^", "SION", A, "SH AX N"),
                R(A, "S", "S", ""),
                R(".", "S", N, "Z"),
                R("#:.E", "S", N, "Z"),
                R("#^:##", "S", N, "Z"),
                R("#^:#", "S", N, "S"),
                R("U", "S", N, "S"),
                R(" :#", "S", N, "Z"),
                R(N, "SCH", A, "S K"),
                R(A, "S", "C+", ""),
                R("#", "SM", A, "Z M"),
                R("#", "SN", "'", "Z AX N"),
                R(A, "S", A, "S"),
            };
            _rules['T'] = new[] {
                R(N, "THE", N, "DH AX"),
                R(A, "TO", N, "T UW"),
                R(A, "THAT", N, "DH AE T"),
                R(N, "THIS", N, "DH IH S"),
                R(N, "THEY", A, "DH EY"),
                R(N, "THERE", A, "DH EH R"),
                R(A, "THER", A, "DH ER"),
                R(A, "THEIR", A, "DH EH R"),
                R(N, "THAN", N, "DH AE N"),
                R(N, "THEM", N, "DH EH M"),
                R(A, "THESE", N, "DH IY Z"),
                R(N, "THEN", A, "DH EH N"),
                R(A, "THROUGH", A, "TH R UW"),
                R(A, "THOSE", A, "DH OW Z"),
                R(A, "THOUGH", N, "DH OW"),
                R(N, "THUS", A, "DH AH S"),
                R(A, "TH", A, "TH"),
                R("#:", "TED", N, "T IH D"),
                R("S", "TI", "#N", "CH"),
                R(A, "TI", "O", "SH"),
                R(A, "TI", "A", "SH"),
                R(A, "TIEN", A, "SH AX N"),
                R(A, "TUR", "#", "CH ER"),
                R(A, "TU", "A", "CH UW"),
                R(N, "TWO", A, "T UW"),
                R(A, "T", A, "T"),
            };
            _rules['U'] = new[] {
                R(N, "UN", "I", "Y UW N"),
                R(N, "UN", A, "AH N"),
                R(N, "UPON", A, "AX P AO N"),
                R("@", "UR", "#", "UH R"),
                R(A, "UR", "#", "Y UH R"),
                R(A, "UR", "*", "ER"),
                R(A, "U", "^ ", "AH"),
                R(A, "U", "^^", "AH"),
                R(A, "UY", A, "AY"),
                R(" G", "U", "#", ""),
                R("G", "U", "%", ""),
                R("G", "U", "#", "W"),
                R("#N", "U", A, "Y UW"),
                R("@", "U", A, "UW"),
                R(A, "U", A, "Y UW"),
            };
            _rules['V'] = new[] {
                R(A, "VIEW", A, "V Y UW"),
                R(A, "V", A, "V"),
            };
            _rules['W'] = new[] {
                R(N, "WERE", A, "W ER"),
                R(A, "WA", "S", "W AA"),
                R(A, "WA", "T", "W AA"),
                R(A, "WHERE", A, "WH EH R"),
                R(A, "WHAT", A, "WH AA T"),
                R(A, "WHOL", A, "HH OW L"),
                R(A, "WHO", A, "HH UW"),
                R(A, "WH", A, "WH"),
                R(A, "WAR", A, "W AO R"),
                R(A, "WOR", "^", "W ER"),
                R(A, "WR", A, "R"),
                R(A, "W", A, "W"),
            };
            _rules['X'] = new[] { R(A, "X", A, "K S") };
            _rules['Y'] = new[] {
                R(A, "YOUNG", A, "Y AH NG"),
                R(N, "YOU", A, "Y UW"),
                R(N, "YES", A, "Y EH S"),
                R(N, "Y", A, "Y"),
                R("#^:", "Y", N, "IY"),
                R("#^:", "Y", "I", "IY"),
                R(" :", "Y", N, "AY"),
                R(" :", "Y", "#", "AY"),
                R(" :", "Y", "^+:#", "IH"),
                R(" :", "Y", "^#", "AY"),
                R(A, "Y", A, "IH"),
            };
            _rules['Z'] = new[] { R(A, "Z", A, "Z") };
            _rules['\''] = new[] { R(A, "'", A, "") };
        }

        // ── Character classes ────────────────────────────────────────────────
        static bool IsLetter(char c) => c >= 'A' && c <= 'Z';
        static bool IsVowel(char c) => c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U';
        static bool IsCons(char c) => IsLetter(c) && !IsVowel(c);
        static bool IsVoicedCons(char c) => "BDVGJLMNRWZ".IndexOf(c) >= 0;
        static bool IsFront(char c) => c == 'E' || c == 'I' || c == 'Y';

        // Context matching with backtracking, so variable-length classes ('#', ':',
        // '*') don't swallow letters a later class needs (e.g. "#^:" before Y in LAZY).
        static bool LeftMatch(string ctx, string w, int pos) => MatchL(ctx, ctx.Length - 1, w, pos - 1);
        static bool RightMatch(string ctx, string w, int pos) => MatchR(ctx, 0, w, pos);

        static char At(string w, int i) => (i >= 0 && i < w.Length) ? w[i] : ' ';

        static bool MatchL(string ctx, int k, string w, int i)
        {
            if (k < 0) return true;
            char c = ctx[k], ch = At(w, i);
            switch (c)
            {
                case ' ': return !IsLetter(ch) && MatchL(ctx, k - 1, w, i - 1);
                case '#':
                    for (int j = i; IsVowel(At(w, j)); j--)
                        if (MatchL(ctx, k - 1, w, j - 1)) return true;
                    return false;
                case '*':
                    for (int j = i; IsCons(At(w, j)); j--)
                        if (MatchL(ctx, k - 1, w, j - 1)) return true;
                    return false;
                case ':':
                    for (int j = i; ; j--)
                    {
                        if (MatchL(ctx, k - 1, w, j)) return true;
                        if (!IsCons(At(w, j))) return false;
                    }
                case '^': return IsCons(ch) && MatchL(ctx, k - 1, w, i - 1);
                case '.': return IsVoicedCons(ch) && MatchL(ctx, k - 1, w, i - 1);
                case '+': return IsFront(ch) && MatchL(ctx, k - 1, w, i - 1);
                case '&':
                    if (ch == 'H' && (At(w, i - 1) == 'C' || At(w, i - 1) == 'S') && MatchL(ctx, k - 1, w, i - 2)) return true;
                    return "SCGZXJ".IndexOf(ch) >= 0 && ch != ' ' && MatchL(ctx, k - 1, w, i - 1);
                case '@':
                    if (ch == 'H' && "TCS".IndexOf(At(w, i - 1)) >= 0 && At(w, i - 1) != ' ' && MatchL(ctx, k - 1, w, i - 2)) return true;
                    return "TSRDLZNJ".IndexOf(ch) >= 0 && ch != ' ' && MatchL(ctx, k - 1, w, i - 1);
                default: return ch == c && MatchL(ctx, k - 1, w, i - 1);
            }
        }

        static bool StartsAt(string w, int i, string s)
        {
            if (i < 0 || i + s.Length > w.Length) return false;
            return string.CompareOrdinal(w, i, s, 0, s.Length) == 0;
        }

        static bool MatchR(string ctx, int k, string w, int i)
        {
            if (k >= ctx.Length) return true;
            char c = ctx[k], ch = At(w, i);
            switch (c)
            {
                case ' ': return !IsLetter(ch) && MatchR(ctx, k + 1, w, i + 1);
                case '#':
                    for (int j = i; IsVowel(At(w, j)); j++)
                        if (MatchR(ctx, k + 1, w, j + 1)) return true;
                    return false;
                case '*':
                    for (int j = i; IsCons(At(w, j)); j++)
                        if (MatchR(ctx, k + 1, w, j + 1)) return true;
                    return false;
                case ':':
                    for (int j = i; ; j++)
                    {
                        if (MatchR(ctx, k + 1, w, j)) return true;
                        if (!IsCons(At(w, j))) return false;
                    }
                case '^': return IsCons(ch) && MatchR(ctx, k + 1, w, i + 1);
                case '.': return IsVoicedCons(ch) && MatchR(ctx, k + 1, w, i + 1);
                case '+': return IsFront(ch) && MatchR(ctx, k + 1, w, i + 1);
                case '&':
                    if ((ch == 'C' || ch == 'S') && At(w, i + 1) == 'H' && MatchR(ctx, k + 1, w, i + 2)) return true;
                    return "SCGZXJ".IndexOf(ch) >= 0 && ch != ' ' && MatchR(ctx, k + 1, w, i + 1);
                case '@':
                    if ("TCS".IndexOf(ch) >= 0 && ch != ' ' && At(w, i + 1) == 'H' && MatchR(ctx, k + 1, w, i + 2)) return true;
                    return "TSRDLZNJ".IndexOf(ch) >= 0 && ch != ' ' && MatchR(ctx, k + 1, w, i + 1);
                case '%':
                    if (StartsAt(w, i, "ING") || StartsAt(w, i, "ELY")) return MatchR(ctx, k + 1, w, i + 3);
                    if (StartsAt(w, i, "ER") || StartsAt(w, i, "ES") || StartsAt(w, i, "ED")) return MatchR(ctx, k + 1, w, i + 2);
                    return ch == 'E' && MatchR(ctx, k + 1, w, i + 1);
                default: return ch == c && MatchR(ctx, k + 1, w, i + 1);
            }
        }

        /// <summary>Translate one upper-case word (letters and apostrophes) to ARPAbet tokens.</summary>
        public static void Translate(string word, List<string> outTokens)
        {
            string w = " " + word + " ";
            int i = 1;
            while (i < w.Length - 1)
            {
                char c = w[i];
                if (!_rules.TryGetValue(c, out var rules)) { i++; continue; }
                bool hit = false;
                foreach (var r in rules)
                {
                    if (!StartsAt(w, i, r.M)) continue;
                    if (!LeftMatch(r.L, w, i)) continue;
                    if (!RightMatch(r.R, w, i + r.M.Length)) continue;
                    if (r.Out.Length > 0)
                        outTokens.AddRange(r.Out.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    i += r.M.Length;
                    hit = true;
                    break;
                }
                if (!hit) i++;
            }
        }

        // ── Exceptions dictionary (stress marked with 1 on the accented vowel) ─
        public static readonly Dictionary<string, string> Exceptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["A"] = "AX", ["THE"] = "DH AX", ["OF"] = "AH V", ["TO"] = "T UW", ["AND"] = "AE N D",
            ["YOU"] = "Y UW1", ["IS"] = "IH Z", ["ARE"] = "AA R", ["WAS"] = "W AA Z", ["I"] = "AY1",
            ["ONE"] = "W AH1 N", ["TWO"] = "T UW1", ["THREE"] = "TH R IY1", ["FOUR"] = "F AO1 R",
            ["FIVE"] = "F AY1 V", ["SIX"] = "S IH1 K S", ["SEVEN"] = "S EH1 V AX N", ["EIGHT"] = "EY1 T",
            ["NINE"] = "N AY1 N", ["TEN"] = "T EH1 N", ["ELEVEN"] = "IH L EH1 V AX N", ["TWELVE"] = "T W EH1 L V",
            ["THIRTEEN"] = "TH ER T IY1 N", ["FIFTEEN"] = "F IH F T IY1 N", ["TWENTY"] = "T W EH1 N T IY",
            ["THIRTY"] = "TH ER1 T IY", ["FORTY"] = "F AO1 R T IY", ["FIFTY"] = "F IH1 F T IY",
            ["HUNDRED"] = "HH AH1 N D R AX D", ["THOUSAND"] = "TH AW1 Z AX N D", ["ZERO"] = "Z IY1 R OW",
            ["MILLION"] = "M IH1 L Y AX N",
            ["SPELL"] = "S P EH1 L", ["SAY"] = "S EY1", ["SAYS"] = "S EH1 Z", ["SPEAK"] = "S P IY1 K",
            ["CORRECT"] = "K ER EH1 K T", ["WRONG"] = "R AO1 NG", ["AGAIN"] = "AX G EH1 N",
            ["TRY"] = "T R AY1", ["PERFECT"] = "P ER1 F EH K T", ["HELLO"] = "HH EH L OW1",
            ["ROBOT"] = "R OW1 B AA T", ["MUSIC"] = "M Y UW1 Z IH K", ["MACHINE"] = "M AX SH IY1 N",
            ["WORD"] = "W ER1 D", ["WORDS"] = "W ER1 D Z", ["SCORE"] = "S K AO1 R", ["NOW"] = "N AW1",
            ["YOUR"] = "Y AO R", ["NEXT"] = "N EH1 K S T", ["THAT"] = "DH AE T", ["WHAT"] = "W AH T",
            ["DO"] = "D UW", ["SOUND"] = "S AW1 N D", ["VOICE"] = "V OY1 S", ["COMPUTER"] = "K AX M P Y UW1 T ER",
            ["PEACE"] = "P IY1 S", ["BUZZ"] = "B AH1 Z", ["REBUZZ"] = "R IY B AH1 Z", ["PEDAL"] = "P EH1 D AX L",
            ["OK"] = "OW K EY1", ["OKAY"] = "OW K EY1", ["EARTH"] = "ER1 TH", ["LOVE"] = "L AH1 V",
            ["GIVE"] = "G IH1 V", ["LIVE"] = "L IH1 V", ["HAVE"] = "HH AE V", ["SOME"] = "S AH M",
            ["COME"] = "K AH1 M", ["DONE"] = "D AH1 N", ["GONE"] = "G AO1 N", ["ANY"] = "EH1 N IY",
            ["MANY"] = "M EH1 N IY", ["WOMAN"] = "W UH1 M AX N", ["PEOPLE"] = "P IY1 P AX L",
            ["LISTEN"] = "L IH1 S AX N", ["DANCE"] = "D AE1 N S", ["BEAT"] = "B IY1 T", ["BASS"] = "B EY1 S",
            ["DOWN"] = "D AW1 N", ["TOWN"] = "T AW1 N", ["BROWN"] = "B R AW1 N", ["CROWN"] = "K R AW1 N",
            ["COW"] = "K AW1", ["WOW"] = "W AW1", ["POWER"] = "P AW1 ER", ["FLOWER"] = "F L AW1 ER",
            // Common lyric words the rules get wrong (v1.2.1): mostly a wrong stress or vowel
            ["TONIGHT"] = "T AX N AY1 T", ["TODAY"] = "T AX D EY1", ["TOMORROW"] = "T AX M AA1 R OW",
            ["TOGETHER"] = "T AX G EH1 DH ER", ["BEFORE"] = "B IH F AO1 R", ["BECAUSE"] = "B IH K AO1 Z",
            ["BELIEVE"] = "B IH L IY1 V", ["FOREVER"] = "F ER EH1 V ER", ["HEART"] = "HH AA1 R T",
            ["DESIRE"] = "D IH Z AY1 R", ["HEAVEN"] = "HH EH1 V AX N", ["REMEMBER"] = "R IH M EH1 M B ER",
            ["NOTHING"] = "N AH1 TH IH NG", ["EVERYTHING"] = "EH1 V R IY TH IH NG", ["SOMEONE"] = "S AH1 M W AH N",
            ["ANYONE"] = "EH1 N IY W AH N", ["EVERYONE"] = "EH1 V R IY W AH N", ["BEAUTIFUL"] = "B Y UW1 T AX F AX L",
            ["EYE"] = "AY1", ["EYES"] = "AY1 Z", ["MAYBE"] = "M EY1 B IY", ["MONEY"] = "M AH1 N IY", ["HONEY"] = "HH AH1 N IY",
            ["BELOW"] = "B IH L OW1", ["WINDOW"] = "W IH1 N D OW", ["SORROW"] = "S AA1 R OW", ["ABOUT"] = "AX B AW1 T",
            ["WITHOUT"] = "W IH DH AW1 T", ["DOUBT"] = "D AW1 T", ["ENOUGH"] = "IH N AH1 F", ["TOUCH"] = "T AH1 CH",
            ["FRIEND"] = "F R EH1 N D", ["BEHIND"] = "B IH HH AY1 N D", ["ALIVE"] = "AX L AY1 V", ["ABOVE"] = "AX B AH1 V",
            ["PROVE"] = "P R UW1 V", ["LOSE"] = "L UW1 Z", ["INSIDE"] = "IH N S AY1 D", ["OUTSIDE"] = "AW T S AY1 D",
            ["EASY"] = "IY1 Z IY", ["ANGEL"] = "EY1 N JH AX L", ["OCEAN"] = "OW1 SH AX N", ["EMOTION"] = "IH M OW1 SH AX N",
            ["WOMEN"] = "W IH1 M AX N", ["HEARD"] = "HH ER1 D", ["RETURN"] = "R IH T ER1 N", ["SURRENDER"] = "S ER EH1 N D ER",
            ["WATER"] = "W AO1 T ER", ["LAUGH"] = "L AE1 F", ["LAUGHTER"] = "L AE1 F T ER", ["EVERY"] = "EH1 V R IY",
            ["VERY"] = "V EH1 R IY", ["SORRY"] = "S AA1 R IY", ["EVENING"] = "IY1 V N IH NG", ["DARLING"] = "D AA1 R L IH NG",
            ["SILENT"] = "S AY1 L AX N T", ["SILENCE"] = "S AY1 L AX N S", ["VIOLENCE"] = "V AY1 AX L AX N S",
            ["AWAY"] = "AX W EY1", ["ALONE"] = "AX L OW1 N", ["AROUND"] = "AX R AW1 N D", ["AGAINST"] = "AX G EH1 N S T",
            ["BEGIN"] = "B IH G IH1 N", ["BETWEEN"] = "B IH T W IY1 N", ["BEYOND"] = "B IH Y AA1 N D", ["TOWARD"] = "T AX W AO1 R D",
            ["UNTIL"] = "AX N T IH1 L", ["FORGET"] = "F ER G EH1 T", ["FORGIVE"] = "F ER G IH1 V", ["CONTROL"] = "K AX N T R OW1 L",
            ["BLOOD"] = "B L AH1 D", ["FLOOD"] = "F L AH1 D", ["DOOR"] = "D AO1 R", ["WALK"] = "W AO1 K", ["TALK"] = "T AO1 K",
            ["COULD"] = "K UH1 D", ["WOULD"] = "W UH1 D", ["SHOULD"] = "SH UH1 D", ["THOUGHT"] = "TH AO1 T",
            ["THROUGH"] = "TH R UW1", ["THOUGH"] = "DH OW1", ["BREAK"] = "B R EY1 K", ["GREAT"] = "G R EY1 T",
            ["DEAD"] = "D EH1 D", ["HEAD"] = "HH EH1 D", ["BREATH"] = "B R EH1 TH", ["BREATHE"] = "B R IY1 DH",
            ["DEATH"] = "D EH1 TH", ["READY"] = "R EH1 D IY", ["HEAVY"] = "HH EH1 V IY", ["BODY"] = "B AA1 D IY",
            ["MOTHER"] = "M AH1 DH ER", ["FATHER"] = "F AA1 DH ER", ["BROTHER"] = "B R AH1 DH ER", ["OTHER"] = "AH1 DH ER",
            ["ANOTHER"] = "AX N AH1 DH ER", ["WORLD"] = "W ER1 L D", ["GIRL"] = "G ER1 L", ["HOUR"] = "AW1 ER",
            ["SYNTH"] = "S IH1 N TH", ["SYNTHESIZER"] = "S IH1 N TH AX S AY Z ER", ["FROM"] = "F R AH M",
        };

        // Letter names, used by Spell mode and for single-letter words like "X".
        public static readonly string[] LetterNames =
        {
            "EY1", "B IY1", "S IY1", "D IY1", "IY1", "EH1 F", "JH IY1", "EY1 CH", "AY1", "JH EY1",
            "K EY1", "EH1 L", "EH1 M", "EH1 N", "OW1", "P IY1", "K Y UW1", "AA1 R", "EH1 S", "T IY1",
            "Y UW1", "V IY1", "D AH1 B AX L Y UW", "EH1 K S", "W AY1", "Z IY1",
        };

        static readonly string[] Ones =
            { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE",
              "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN",
              "EIGHTEEN", "NINETEEN" };
        static readonly string[] Tens =
            { "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY" };

        public static readonly string[] DigitWords = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE" };

        /// <summary>Spell an integer as English words (up to 999,999,999; digits beyond that are read singly).</summary>
        public static void NumberToWords(string digits, List<string> outWords)
        {
            digits = digits.TrimStart('0');
            if (digits.Length == 0) { outWords.Add("ZERO"); return; }
            if (digits.Length > 9)
            {
                foreach (char d in digits) outWords.Add(DigitWords[d - '0']);
                return;
            }
            long n = long.Parse(digits);
            Group((int)(n / 1000000), "MILLION", outWords);
            Group((int)(n / 1000 % 1000), "THOUSAND", outWords);
            Group((int)(n % 1000), null, outWords);
        }

        static void Group(int n, string scale, List<string> w)
        {
            if (n == 0) return;
            if (n >= 100) { w.Add(Ones[n / 100]); w.Add("HUNDRED"); n %= 100; }
            if (n >= 20) { w.Add(Tens[n / 10]); n %= 10; if (n > 0) w.Add(Ones[n]); }
            else if (n > 0) w.Add(Ones[n]);
            if (scale != null) w.Add(scale);
        }
    }
}
