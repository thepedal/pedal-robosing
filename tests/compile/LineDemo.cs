// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
// Line mode: checks that a line ends by itself, that vowels land on their beats,
// that the melody survives save/load, and renders a demo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Buzz.MachineInterface;
using PedalRoboSing;
using PedalRoboSing.Speech;

static class LineDemo
{
    const int Tick = 5250;                       // 126 BPM, 4 ticks per beat: 119 ms
    static readonly Sample[] buf = new Sample[256];

    static PedalRoboSingMachine M(string phrase, string score)
    {
        var h = new Host(); h.MasterInfo.SamplesPerTick = Tick;
        var m = new PedalRoboSingMachine(h) { PlayMode = 5 };
        m.SetPhrases(new[] { phrase }, new[] { score });
        return m;
    }

    static float[] Play(PedalRoboSingMachine m, double ticks, out bool activeAtEnd)
    {
        var o = new List<float>(); activeAtEnd = false;
        int left = (int)(ticks * Tick);
        while (left > 0)
        {
            int n = Math.Min(256, left); activeAtEnd = m.Work(buf, n, WorkModes.WM_WRITE);
            for (int i = 0; i < n; i++) o.Add(buf[i].L / 32768f);
            left -= n;
        }
        return o.ToArray();
    }

    public static void Run(string path)
    {
        // 1. One word, one note in the pattern: it must finish by itself with its t
        var m = M("tonight", "0,2,0 0,6,3");
        m.SetNote(new Note(0x40), 0);
        var a = Play(m, 8, out bool act8);
        Play(m, 6, out bool act14);
        double tail = 0; for (int i = (int)(7.5 * Tick); i < 8 * Tick; i++) tail = Math.Max(tail, Math.Abs(a[i]));
        Console.WriteLine($"tonight, 2 + 6 ticks: sounding at tick 8 {act8} (level {tail:0.000}), idle by tick 14 {!act14}");

        // 2. Vowels on the beat: the voice reports when each syllable's vowel begins
        foreach (var (text, score) in new[] { ("[S T R AA] [S T R AA] [S T R AA] [AA] [S T R AA]", "0,4,0 0,4,0 0,4,0 0,4,0 4,4,0"),
                                              ("Seven silver circuits sing tonight.", "0,2,0 0,2,0 0,2,0 0,2,0 0,2,0 0,2,0 0,4,0 2,2,0 0,6,0") })
        {
            var u = Planner.Build(text, SpeakMode.Speak);
            var sc = LineScore.Parse(score, u.SylS.Length);
            var v = new LpcVoice(1) { ChipRate = 16000, FrameMs = 15, TickMs = 119.05f, Speed = 1f };
            var got = new List<string>(); float worst = 0; int nom = 0;
            var want = new float[sc.Count];
            for (int k = 0; k < sc.Count; k++) { nom += sc.Gap[k]; want[k] = nom * 119.05f; nom += sc.Len[k]; }
            v.LineTrace = (k, ms) => { got.Add($"{ms - want[k]:+0;-0}"); if (k > 0) worst = Math.Max(worst, Math.Abs(ms - want[k])); };
            v.StartLine(u, sc, 48f, 1f);
            for (int i = 0; i < 16000 * 6 && v.IsActive; i++) v.Next();
            Console.WriteLine($"vowel start against its beat, ms: {string.Join(" ", got)}   (worst after the first: {worst:0} ms; one frame is 15 ms)");
        }

        // 3. Melody through save and load
        var ser = new XmlSerializer(typeof(RoboSingState));
        var sw = new StringWriter(); ser.Serialize(sw, m.MachineState);
        var m2 = new PedalRoboSingMachine(new Host());
        m2.MachineState = (RoboSingState)ser.Deserialize(new StringReader(sw.ToString()));
        Console.WriteLine($"state round trip: {m2.GetPhrases()[0]} | {m2.GetScores()[0]}");
        var old = (RoboSingState)ser.Deserialize(new StringReader(
            "<?xml version=\"1.0\"?><RoboSingState><Version>1</Version><Phrases><string>Hello.</string></Phrases></RoboSingState>"));
        m2.MachineState = old;
        Console.WriteLine($"a v1.3 state without melodies loads: {m2.GetPhrases()[0]} | '{m2.GetScores()[0]}'");

        // 4. Demo: one made-up line, one note. Default melody first, then a written one.
        var all = new List<float>();
        string line = "Seven silver circuits sing tonight.";
        var d1 = M(line, ""); d1.SetNote(new Note(0x34), 0); all.AddRange(Play(d1, 28, out _));
        var d2 = M(line, "0,2,0 0,2,2 0,2,3 0,2,2 0,2,0 0,2,-2 0,6,3 2,2,0 0,10,5");
        d2.SetNote(new Note(0x34), 0); all.AddRange(Play(d2, 44, out bool actEnd));
        Console.WriteLine($"demo line: {Planner.Build(line, SpeakMode.Speak).Phonetic}; idle at the end {!actEnd}");

        float peak = 0; foreach (var s in all) peak = Math.Max(peak, Math.Abs(s));
        using var bw = new BinaryWriter(File.Create(path));
        int cnt = all.Count;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + cnt * 2); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(44100); bw.Write(88200); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(cnt * 2);
        foreach (var s in all) bw.Write((short)Math.Clamp((int)(s * 32767), -32768, 32767));
        Console.WriteLine($"line demo: {cnt / 44100.0:F1} s, peak {peak:0.00} -> {path}");
    }
}
