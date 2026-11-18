// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
// Measures and renders the React controls: the same sung line with React off, then on.
using System;
using System.Collections.Generic;
using System.IO;
using Buzz.MachineInterface;
using PedalRoboSing;
using PedalRoboSing.Speech;

static class ReactDemo
{
    const int Tick = 5250;
    static readonly Sample[] buf = new Sample[256];

    static PedalRoboSingMachine M(string phrase)
    {
        var h = new Host(); h.MasterInfo.SamplesPerTick = Tick;
        // Everything the test doesn't set is pinned off, whatever the machine's defaults are
        var m = new PedalRoboSingMachine(h) { PlayMode = 1, Excitation = 1, ChipRate = 5, Frame = 15, CoefBits = 10, DacBits = 14, Speaker = 30,
            Tune = 0, VibDepth = 0, Portamento = 0, Glide = 0, ConsTune = 0,
            ReactLevel = 64, ReactTune = 64, ReactSize = 64, ReactRing = 64, ReactVib = 64, ReactRhyme = 64, ReactAllit = 64 };
        m.SetPhrases(new[] { phrase });
        return m;
    }

    static double Play(PedalRoboSingMachine m, double ticks, List<float> sink)
    {
        int left = (int)(ticks * Tick); double e = 0; int n0 = left;
        while (left > 0)
        {
            int n = Math.Min(256, left); m.Work(buf, n, WorkModes.WM_WRITE);
            for (int i = 0; i < n; i++) { float s = buf[i].L / 32768f; e += s * s; sink?.Add(s); }
            left -= n;
        }
        return Math.Sqrt(e / n0);
    }

    public static void Run(string path)
    {
        var u = Planner.Build("Stop the cool machine, feel it sing.", SpeakMode.Speak);
        Console.WriteLine(u.Phonetic);
        Console.WriteLine("feel keys (ms: open bright hard sung):");
        foreach (var k in u.Q) Console.Write($" {k.T:0}:{k.Open:0.0}/{k.Bright:0.0}/{k.Hard:0.0}/{k.Sung:0.0}");
        Console.WriteLine();

        // Loudness of every vowel, held, averaged over six notes (React off): should be roughly even
        string[] vowels = { "IY", "IH", "EH", "AE", "AA", "AO", "UH", "UW", "AH", "AX", "ER", "EY", "AY", "AW", "OY", "OW" };
        int[] notes = { 0x29, 0x31, 0x35, 0x38, 0x41, 0x45 };
        Console.Write("vowel rms:");
        foreach (string v in vowels)
        {
            double sum = 0;
            foreach (int n in notes) { var m = M("[" + v + "]"); m.SetNote(new Note(n), 0); Play(m, 2, null); sum += Play(m, 4, null); }
            Console.Write($" {v}={sum / notes.Length:0.000}");
        }
        Console.WriteLine();

        // Level of three held vowels, React Level off / full / reversed
        foreach (int rl in new[] { 64, 127, 0 })
        {
            Console.Write($"React Level {rl,3}:");
            foreach (string v in new[] { "[AA]", "[EH]", "[IY]", "[UW]" })
            {
                var m = M(v); m.ReactLevel = rl;
                m.SetNote(new Note(0x34), 0); Play(m, 2, null);
                Console.Write($"  {v} {Play(m, 4, null):0.000}");
            }
            Console.WriteLine();
        }

        // What the detector finds
        string[] lines = { "The cat sat on the mat.", "Seven silver circuits sing.", "Big bad bots break beats.",
                           "I make music in the night.", "Robots dancing in the light.", "Give it to me, set it free.",
                           "A plain line with nothing special." };
        var rh = Planner.FindRhymes(lines);
        foreach (var l in lines) Console.WriteLine($"  {l,-34} {Planner.Build(l, SpeakMode.Speak, rh).SoundNotes}");

        // Rhyme and alliteration lift, measured on the marked sounds
        foreach (int r in new[] { 64, 127 })
        {
            var m = M("The cat sat on the mat."); m.PlayMode = 0; m.ReactRhyme = r;
            m.SetNote(new Note(0x34), 0); double e1 = Play(m, 14, null);
            var m2 = M("Big bad bots break beats."); m2.PlayMode = 0; m2.ReactAllit = r;
            m2.SetNote(new Note(0x34), 0); double e2 = Play(m2, 14, null);
            Console.WriteLine($"  React {r,3}: rhyme line rms {e1:0.000}   alliteration line rms {e2:0.000}");
        }

        var all = new List<float>();
        string line = "Stop the cool machine, feel it sing.";
        int[] mel = { 0x34, 0x34, 0x37, 0x39, 0x3B, 0x39, 0x37, 0x34 };
        void Pass(Action<PedalRoboSingMachine> set)
        {
            var m = M(line); m.Tune = 50; m.VibDepth = 30; m.ConsTune = 50; set(m);
            foreach (int n in mel) { m.SetNote(new Note(n), 0); Play(m, 4, all); }
            m.SetNote(new Note(Note.Off), 0); Play(m, 5, all);
            for (int i = 0; i < 22050; i++) all.Add(0);
        }
        Pass(m => { });                                                             // 1. React off
        Pass(m => { m.ReactLevel = 110; m.ReactTune = 127; m.ReactSize = 110; m.ReactRing = 127; m.ReactVib = 127; });   // 2. everything on
        Pass(m => { m.ReactTune = 127; });                                          // 3. tune only
        Pass(m => { m.ReactSize = 127; });                                          // 4. size only
        line = "Seven silver circuits sing, the cat sat on the mat.";
        mel = new[] { 0x34, 0x34, 0x37, 0x37, 0x39, 0x39, 0x3B, 0x39, 0x37, 0x37, 0x34, 0x34, 0x32 };
        Pass(m => { });                                                             // 5. new line, React off
        Pass(m => { m.ReactRhyme = 127; m.ReactAllit = 127; });                     // 6. rhyme and alliteration only

        float peak = 0; foreach (var s in all) peak = Math.Max(peak, Math.Abs(s));
        using var bw = new BinaryWriter(File.Create(path));
        int cnt = all.Count;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + cnt * 2); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(44100); bw.Write(88200); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(cnt * 2);
        foreach (var s in all) bw.Write((short)Math.Clamp((int)(s * 32767), -32768, 32767));
        Console.WriteLine($"react demo: {cnt / 44100.0:F1} s, peak {peak:0.00} -> {path}");
    }
}
