// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — a small Standard MIDI File reader, for importing a melody.
//
// Reads formats 0 and 1 and returns the notes of each part: one part per track
// and channel that has notes. Only note on/off, the track name and tempo are kept.
// Times are in the file's own ticks; Division is ticks per quarter note.
// Runs on the UI thread only.

using System;
using System.Collections.Generic;
using System.Text;

namespace PedalRoboSing
{
    public sealed class MidiNote
    {
        public int Start, End, Pitch;               // ticks, ticks, MIDI note number (60 = middle C)
    }

    public sealed class MidiPart
    {
        public string Name = "";
        public int Track, Channel;                  // both 0-based
        public readonly List<MidiNote> Notes = new List<MidiNote>();
        public bool Drums => Channel == 9;
    }

    public sealed class MidiTempo
    {
        public int Tick;                            // where it takes effect
        public double Bpm;                          // quarter notes per minute
    }

    public static class MidiFile
    {
        public const int MaxBytes = 16 * 1024 * 1024;

        /// <summary>Throws FormatException with a plain message when the data is not a usable MIDI file.</summary>
        public static List<MidiPart> Read(byte[] d, out int division) => Read(d, out division, out _);

        /// <summary>Also returns the file's tempo marks in time order (none = the MIDI default, 120).</summary>
        public static List<MidiPart> Read(byte[] d, out int division, out List<MidiTempo> tempos)
        {
            tempos = new List<MidiTempo>();
            division = 480;
            if (d == null || d.Length < 14 || d.Length > MaxBytes) throw new FormatException("That is not a MIDI file (wrong size).");
            try
            {
                int p = 0;
                if (Tag(d, ref p) != "MThd") throw new FormatException("That is not a MIDI file (no MThd header).");
                int hlen = Int(d, ref p, 4), format = Int(d, ref p, 2), tracks = Int(d, ref p, 2);
                division = Int(d, ref p, 2);
                p += hlen - 6;
                if (format > 1) throw new FormatException("MIDI format " + format + " files are not supported (only 0 and 1).");
                if ((division & 0x8000) != 0 || division == 0) throw new FormatException("This MIDI file uses SMPTE timing, which is not supported.");

                var parts = new List<MidiPart>();
                for (int t = 0; t < tracks && p + 8 <= d.Length; t++)
                {
                    string tag = Tag(d, ref p);
                    int len = Int(d, ref p, 4), end = Math.Min(d.Length, p + len);
                    if (tag == "MTrk") ReadTrack(d, p, end, t, parts, tempos);
                    p = end;
                }
                tempos.Sort((x, y) => x.Tick.CompareTo(y.Tick));
                return parts;
            }
            catch (FormatException) { throw; }
            catch (Exception) { throw new FormatException("The MIDI file is damaged or cut short."); }
        }

        static string Tag(byte[] d, ref int p) { string s = Encoding.ASCII.GetString(d, p, 4); p += 4; return s; }
        static int Int(byte[] d, ref int p, int n) { int v = 0; for (int i = 0; i < n; i++) v = (v << 8) | d[p++]; return v; }
        static int Var(byte[] d, ref int p) { int v = 0; byte b; do { b = d[p++]; v = (v << 7) | (b & 0x7F); } while ((b & 0x80) != 0); return v; }

        static void ReadTrack(byte[] d, int p, int end, int track, List<MidiPart> parts, List<MidiTempo> tempos)
        {
            var byChannel = new MidiPart[16];
            var on = new int[16, 128];                       // start tick + 1 of a sounding note, 0 = silent
            string name = "";
            int time = 0, status = 0;
            while (p < end)
            {
                time += Var(d, ref p);
                int b = d[p];
                if (b == 0xFF)
                {
                    int type = d[p + 1]; p += 2;
                    int len = Var(d, ref p);
                    if (type == 0x03 && name.Length == 0) name = Encoding.Latin1.GetString(d, p, Math.Min(len, end - p)).Trim();
                    if (type == 0x51 && len == 3 && p + 3 <= end)
                    {
                        int us = (d[p] << 16) | (d[p + 1] << 8) | d[p + 2];            // microseconds per quarter note
                        if (us > 0) tempos.Add(new MidiTempo { Tick = time, Bpm = 60000000.0 / us });
                    }
                    p += len;
                    if (type == 0x2F) break;
                    continue;
                }
                if (b == 0xF0 || b == 0xF7) { p++; int len = Var(d, ref p); p += len; continue; }
                if ((b & 0x80) != 0) { status = b; p++; }
                int kind = status & 0xF0, ch = status & 0x0F;
                int a1 = d[p++] & 0x7F, a2 = (kind == 0xC0 || kind == 0xD0) ? 0 : d[p++] & 0x7F;
                bool noteOn = kind == 0x90 && a2 > 0, noteOff = kind == 0x80 || (kind == 0x90 && a2 == 0);
                if (!noteOn && !noteOff) continue;
                if (on[ch, a1] != 0)
                {
                    byChannel[ch] ??= new MidiPart { Track = track, Channel = ch };
                    byChannel[ch].Notes.Add(new MidiNote { Start = on[ch, a1] - 1, End = Math.Max(on[ch, a1], time), Pitch = a1 });
                    on[ch, a1] = 0;
                }
                if (noteOn) on[ch, a1] = time + 1;
            }
            foreach (var part in byChannel)
            {
                if (part == null || part.Notes.Count == 0) continue;
                part.Name = name;
                part.Notes.Sort((x, y) => x.Start != y.Start ? x.Start.CompareTo(y.Start) : y.Pitch.CompareTo(x.Pitch));
                parts.Add(part);
            }
        }
    }
}
