// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
// Renders a demo reel through PedalRoboSingMachine.Work at 44.1 kHz, 125 bpm, 4 ticks per beat.
using System;
using System.Collections.Generic;
using System.IO;
using Buzz.MachineInterface;
using PedalRoboSing;

static class Demo
{
    const int Tick = 5292;                       // samples per tick (120 ms)
    static readonly List<float> all = new List<float>();
    static readonly Sample[] buf = new Sample[256];

    static PedalRoboSingMachine M()
    {
        var h = new Host(); h.MasterInfo.SamplesPerTick = Tick;
        var m = new PedalRoboSingMachine(h) { ChipRate = 5, CoefBits = 9, DacBits = 12, Speaker = 30, Excitation = 1 };
        m.SetPhrases(new[] { "I am a robot, I make strange music.", "Seven silver circuits sing.", "Stop. Tick. Kiss. Spit. Shock." });
        return m;
    }
    static void Ticks(PedalRoboSingMachine m, double ticks)
    {
        int left = (int)(ticks * Tick);
        while (left > 0) { int n = Math.Min(256, left); m.Work(buf, n, WorkModes.WM_WRITE); for (int i = 0; i < n; i++) all.Add(buf[i].L / 32768f); left -= n; }
    }
    static void Gap() { for (int i = 0; i < 44100 / 2; i++) all.Add(0); }

    public static void Run(string path)
    {
        int[] mel = { 0x34, 0x34, 0x37, 0x39, 0x3B, 0x39, 0x37, 0x34, 0x32, 0x34 };

        // 1. Syllable mode: one syllable per note, held, plain voice
        var a = M(); a.SetPhrase(1, 0);
        foreach (int n in mel) { a.SetNote(new Note(n), 0); Ticks(a, 3); }
        a.SetNote(new Note(Note.Off), 0); Ticks(a, 4); Gap();

        // 2. Same line, formants tuned to the harmonics of each note, a little vibrato
        var b = M(); b.Tune = 110; b.VibDepth = 25; b.SetPhrase(1, 0);
        foreach (int n in mel) { b.SetNote(new Note(n), 0); Ticks(b, 3); }
        b.SetNote(new Note(Note.Off), 0); Ticks(b, 4); Gap();

        // 3. One low held note, formants tuned to A minor pentatonic: the words play a melody inside it
        var c = M(); c.Tune = 127; c.TuneTo = 1; c.Root = 9; c.Scale = 7; c.Glide = 45; c.Excitation = 2; c.SetPhrase(2, 0);
        for (int k = 0; k < 12; k++) { c.SetNote(new Note(0x2A), 0); Ticks(c, 3); }
        c.SetNote(new Note(Note.Off), 0); Ticks(c, 4); Gap();

        // 4. Sync: a syllable every 2 ticks, monotone, with a chord sharing one mouth
        var d = M(); d.PlayMode = 3; d.SyncStep = 2; d.Inflection = 0; d.Mouth = 1; d.Excitation = 2;
        d.SetPhrase(1, 0);
        d.SetNote(new Note(0x2A), 0); d.SetNote(new Note(0x34), 1); d.SetNote(new Note(0x38), 2);
        Ticks(d, 22); for (int t = 0; t < 3; t++) d.SetNote(new Note(Note.Off), t); Ticks(d, 3); Gap();

        // 5. Pitched consonants: phoneme steps over a consonant-heavy line, noise tuned an octave up
        var e = M(); e.PlayMode = 2; e.Hold = 0; e.ConsTune = 115; e.ConsPitch = 2; e.SetPhrase(3, 0);
        int[] bass = { 0x24, 0x24, 0x27, 0x24, 0x29, 0x24, 0x27, 0x22 };
        for (int k = 0; k < 24; k++) { e.SetNote(new Note(bass[k % 8]), 0); Ticks(e, 1.5); }
        Ticks(e, 6); Gap();

        // 6. Scrub: position swept forwards slowly, then scanning backwards in a loop over two words
        var f = M(); f.PlayMode = 4; f.Glide = 60; f.Tune = 70; f.SetPhrase(2, 0);
        f.SetNote(new Note(0x30), 0);
        for (int k = 0; k <= 16; k++) { f.SetPosition(k * 60, 0); Ticks(f, 2); }
        f.Scan = 40; f.SetWord(2, 0); f.SetWords(2, 0); f.SetNote(new Note(0x37), 0); Ticks(f, 24);
        f.SetNote(new Note(Note.Off), 0); Ticks(f, 3);

        using var bw = new BinaryWriter(File.Create(path));
        int cnt = all.Count;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + cnt * 2); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(44100); bw.Write(88200); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(cnt * 2);
        foreach (var s in all) bw.Write((short)Math.Clamp((int)(s * 32767), -32768, 32767));
        Console.WriteLine($"demo: {cnt / 44100.0:F1} s -> {path}");
    }
}
