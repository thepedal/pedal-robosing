// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
// Drives the real machine class through the stub host: every play mode, tuning,
// pitched consonants, shared mouth, hold/release, MachineState round trip.
using System;
using System.IO;
using System.Xml.Serialization;
using Buzz.MachineInterface;
using BuzzGUI.Interfaces;
using PedalRoboSing;
using PedalRoboSing.Speech;

class Host : IBuzzMachineHost
{
    public IMachine Machine { get; set; }
    public MasterInfo MasterInfo { get; } = new MasterInfo { SamplesPerSec = 44100 };
}
class P : IParameter { public string Name { get; set; } public int Last = -1; public void SetValue(int track, int value) { Last = value; } }
class PG : IParameterGroup { public System.Collections.ObjectModel.ReadOnlyCollection<IParameter> Parameters { get; set; } }
class FakeMachine : IMachine
{
    public bool IsPlaying;
    public Buzz.MachineInterface.IBuzzMachine ManagedMachine { get; set; }
    sealed class B : IBuzz { public FakeMachine M; public bool Playing => M.IsPlaying; }
    sealed class G : IMachineGraph { public IBuzz Buzz { get; set; } }
    readonly G _graph;
    public FakeMachine() { _graph = new G { Buzz = new B { M = this } }; }
    public IMachineGraph Graph => _graph;
    public System.Collections.ObjectModel.ReadOnlyCollection<IParameterGroup> ParameterGroups { get; set; }
}

static class Smoke
{
    static readonly Sample[] buf = new Sample[256];
    static int nan; static float peak;

    static double Run(PedalRoboSingMachine m, double seconds, out bool lastActive)
    {
        double e = 0; int n = 0; lastActive = false;
        int blocks = Math.Max(1, (int)(seconds * 44100 / 256));
        for (int b = 0; b < blocks; b++)
        {
            lastActive = m.Work(buf, 256, WorkModes.WM_WRITE);
            foreach (var s in buf) { if (float.IsNaN(s.L)) nan++; peak = Math.Max(peak, Math.Abs(s.L)); e += s.L * s.L; n++; }
        }
        return Math.Sqrt(e / n);
    }

    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "demo") { Demo.Run(args[1]); return; }
        if (Environment.GetEnvironmentVariable("KIT_NOLEX") == "1") Lexicon.Off = true;
        if (args.Length >= 2 && args[0] == "say")
        {
            var sw0 = System.Diagnostics.Stopwatch.StartNew();
            int words = Lexicon.Count;
            Console.WriteLine($"lexicon: {words} words, loaded in {sw0.ElapsedMilliseconds} ms");
            for (int i = 1; i < args.Length; i++)
            {
                var uu = Planner.Build(args[i], SpeakMode.Speak);
                Console.WriteLine($"{args[i],-12} {uu.Phonetic.Replace(" .", "")}   [{string.Join(" | ", uu.SylText)}]");
            }
            return;
        }
        if (args.Length == 1 && args[0] == "lex") { LexProbe.Run(); return; }
        if (args.Length == 2 && args[0] == "rhymes") { RhymeProbe.Run(args[1]); return; }
        if (args.Length >= 4 && args[0] == "kit") { CanonDemo.Kit(args[1], args[2], args[3], args.Length > 4 ? args[4] : null); return; }
        if (args.Length == 2 && args[0] == "canon") { CanonDemo.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "line") { LineDemo.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "react") { ReactDemo.Run(args[1]); return; }
        if (args.Length == 3 && args[0] == "song") { SongPlayer.Run(args[1], args[2]); return; }

        var u = Planner.Build("Hello. I am a robot, I make strange music.", SpeakMode.Speak);
        Console.WriteLine($"{u.Phonetic}\n  words {u.WordCount}  syllables {u.SylS.Length}  phonemes {u.PhS.Length}");

        // Syllable mode, Hold on: sustains until note-off, then finishes and goes idle
        var m = new PedalRoboSingMachine(new Host());
        Console.WriteLine($"a new machine has {m.GetPhrases().Length} phrases (want 0)");
        m.SetPhrases(PhraseBank.Defaults);
        m.SetPhrase(8, 0); m.SetNote(new Note(0x34), 0);
        double r1 = Run(m, 1.0, out bool a1);
        m.SetNote(new Note(Note.Off), 0);
        Run(m, 1.0, out bool a2);
        Console.WriteLine($"syllable+hold: sustaining rms {r1:F0} active {a1}; after note-off active {a2}");

        // Hold off: one-shot syllables, cursor wraps
        m.Hold = 0;
        for (int k = 0; k < 12; k++) { m.SetNote(new Note(0x34 + k % 5), 0); Run(m, 0.2, out _); }
        Run(m, 1.0, out bool a3);
        Console.WriteLine($"syllable one-shots: idle afterwards {!a3}");

        // Each mode with tuning, pitched consonants, vibrato, portamento, glide
        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
        {
            var x = new PedalRoboSingMachine(new Host()) { PlayMode = mode, Tune = 100, TuneTo = mode % 2, Scale = 6, ConsTune = 100,
                VibDepth = 40, Portamento = 50, ReactLevel = 127, ReactTune = 0, ReactSize = 127, ReactRing = 127, ReactVib = 127, ReactRhyme = 127, ReactAllit = 0, Glide = mode * 20, InflectSnap = mode % 2, Scan = mode == 4 ? 80 : 64, Mouth = mode == 1 ? 1 : 0 };
            x.SetPhrases(PhraseBank.Defaults);
            x.SetPhrase(8, 0); x.SetPhrase(8, 1);
            double e = 0;
            for (int k = 0; k < 8; k++)
            {
                x.SetPosition(k * 120, 0);
                x.SetNote(new Note(0x30 + k), 0); x.SetNote(new Note(0x37 + k), 1); x.SetNote(new Note(0x30), 11);
                e += Run(x, 0.3, out _);
            }
            x.SetNote(new Note(Note.Off), 0); x.SetNote(new Note(Note.Off), 1);
            Run(x, 2.5, out bool act);
            Console.WriteLine($"mode {mode}: mean rms {e / 8:F0}  idle after release {!act}");
        }

        // Level against Tune amount (sustained vowel), both tuning targets
        foreach (int to in new[] { 0, 1 })
        {
            Console.Write($"tune-to {to} rms:");
            foreach (int t in new[] { 0, 32, 64, 96, 127 })
            {
                var x = new PedalRoboSingMachine(new Host()) { Tune = t, TuneTo = to };
                x.SetPhrases(new[] { "ah", "ee", "oo" });
                double e = 0;
                for (int ph = 1; ph <= 3; ph++) foreach (int note in new[] { 0x24, 0x34, 0x44 })
                { x.SetPhrase(ph, 0); x.SetNote(new Note(note), 0); Run(x, 0.15, out _); e += Run(x, 0.4, out _); }
                Console.Write($"  {t}:{e / 9:F0}");
            }
            Console.WriteLine();
        }
        Console.WriteLine($"peak {peak:F0}  NaN {nan}");

        // MachineState round trip through XmlSerializer, as the host does
        m.SetPhrases(new[] { "Alpha beta.", "", "Gamma [G AE1 M AX] delta?" });
        var ser = new XmlSerializer(typeof(RoboSingState));
        var sw = new StringWriter(); ser.Serialize(sw, m.MachineState);
        var m2 = new PedalRoboSingMachine(new Host());
        m2.MachineState = (RoboSingState)ser.Deserialize(new StringReader(sw.ToString()));
        Console.WriteLine("state round trip: " + string.Join(" | ", m2.GetPhrases()));
        foreach (var (n, v) in new[] { ("Phrase", 3), ("Glide", 64), ("Scan", 32), ("Tune", 64), ("Vib Rate", 64), ("Portamento", 127), ("Position", 500) })
            Console.WriteLine($"  {n}={v}: {m2.DescribeValue(new P { Name = n }, v) ?? "(default)"}");
    }
}
