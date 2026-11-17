// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — pronouncing dictionary.
//
// Looks words up in the CMU Pronouncing Dictionary, packed by
// tools/build_lexicon.py and embedded in the DLL as cmudict.bin.gz. The
// dictionary is Copyright (C) 1993-2015 Carnegie Mellon University and is used
// under its own licence (Speech/cmudict.LICENSE.txt).
//
// The packed data stays as one byte array with an offset table and is searched
// by bisection, so it costs about 2.5 MB however many machines are open. It is
// loaded once, on first use, from the UI thread (phrases are compiled there).
// If the resource is missing the lexicon is simply empty and the letter-to-sound
// rules take over.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PedalRoboSing.Speech
{
    public static class Lexicon
    {
        // Must match PHONES in tools/build_lexicon.py
        static readonly string[] Phones =
        {
            "AA", "AE", "AH", "AO", "AW", "AY", "B", "CH", "D", "DH", "EH", "ER", "EY", "F", "G", "HH", "IH", "IY",
            "JH", "K", "L", "M", "N", "NG", "OW", "OY", "P", "R", "S", "SH", "T", "TH", "UH", "UW", "V", "W", "Y",
            "Z", "ZH", "AX",
        };

        static readonly Lazy<(byte[] blob, int[] offsets)> _data = new Lazy<(byte[], int[])>(Load);

        public static int Count => _data.Value.offsets.Length;

        static (byte[], int[]) Load()
        {
            try
            {
                using var res = typeof(Lexicon).Assembly.GetManifestResourceStream("PedalRoboSing.cmudict.bin.gz");
                if (res == null) return (Array.Empty<byte>(), Array.Empty<int>());
                using var gz = new GZipStream(res, CompressionMode.Decompress);
                using var ms = new MemoryStream(3 << 20);
                gz.CopyTo(ms);
                byte[] blob = ms.ToArray();

                int n = 0, zeros = 0;
                foreach (byte b in blob) if (b == 0 && (++zeros & 1) == 0) n++;      // two terminators per record
                var offsets = new int[n];
                int pos = 0;
                for (int i = 0; i < n; i++)
                {
                    offsets[i] = pos;
                    while (blob[pos] != 0) pos++;
                    pos++;
                    while (blob[pos] != 0) pos++;
                    pos++;
                }
                return (blob, offsets);
            }
            catch
            {
                return (Array.Empty<byte>(), Array.Empty<int>());
            }
        }

        /// <summary>Pronunciation of an upper-case word as ARPAbet tokens ("T AX N AY1 T"), or null.</summary>
        public static string Lookup(string word)
        {
            var (blob, offsets) = _data.Value;
            int lo = 0, hi = offsets.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int p = offsets[mid], c = 0, k = 0;
                for (; ; k++, p++)
                {
                    int a = k < word.Length ? word[k] : 0, b = blob[p];
                    if (a != b) { c = a - b; break; }
                    if (a == 0) break;
                }
                if (c < 0) hi = mid - 1;
                else if (c > 0) lo = mid + 1;
                else
                {
                    var sb = new StringBuilder();
                    for (p++; blob[p] != 0; p++)
                    {
                        int code = blob[p], idx = (code & 0x3F) - 1;
                        if ((uint)idx >= (uint)Phones.Length) return null;
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(Phones[idx]);
                        if ((code & 0x40) != 0) sb.Append('1');
                    }
                    return sb.ToString();
                }
            }
            return null;
        }
    }
}
