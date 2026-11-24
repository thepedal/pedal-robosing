// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
// Plays an events file written by examples/build_example.py through real machine
// instances (126 BPM, 4 ticks per beat), mixes them and writes a WAV. Columns are
// mapped the way ReBuzz lays them out: globals in declaration order, then the
// seven track columns.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Buzz.MachineInterface;
using PedalRoboSing;
using PedalRoboSing.Speech;

static class SongPlayer
{
    const int Sr = 44100, Tick = 5250;                      // 60 / 126 / 4 s

    public static void Run(string eventsPath, string wavPath)
    {
        var globals = typeof(PedalRoboSingMachine).GetProperties()
            .Where(p => p.GetCustomAttribute<ParameterDecl>() != null).OrderBy(p => p.MetadataToken).ToArray();
        int baseCol = globals.Length;
        var machines = new Dictionary<string, PedalRoboSingMachine>();
        var amps = new Dictionary<string, float>();
        var phrases = new Dictionary<string, List<string>>();
        var events = new Dictionary<int, List<(string m, int track, int col, int val)>>();
        int length = 0;

        foreach (var line in File.ReadAllLines(eventsPath))
        {
            var f = line.Split('\t');
            if (line.StartsWith("LENGTH")) { length = int.Parse(line.Split(' ')[1]); continue; }
            if (line.StartsWith("BASE")) { baseCol = int.Parse(line.Split(' ')[1]); continue; }   // layout the song was written for
            switch (f[0])
            {
                case "M":
                {
                    var h = new Host(); h.MasterInfo.SamplesPerTick = Tick;
                    machines[f[1]] = new PedalRoboSingMachine(h); machines[f[1]].SetPhrases(PhraseBank.Defaults);
                    amps[f[1]] = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                    phrases[f[1]] = new List<string>();
                    break;
                }
                case "P": phrases[f[1]].Add(f[2]); break;
                case "G": globals[int.Parse(f[2])].SetValue(machines[f[1]], int.Parse(f[3])); break;
                case "E":
                {
                    int row = int.Parse(f[2]);
                    if (!events.TryGetValue(row, out var l)) events[row] = l = new List<(string, int, int, int)>();
                    l.Add((f[1], int.Parse(f[3]), int.Parse(f[4]), int.Parse(f[5])));
                    break;
                }
            }
        }
        foreach (var kv in machines)
        {
            kv.Value.SetPhrases(phrases[kv.Key]);
            Console.WriteLine($"{kv.Key}:");
            foreach (var p in phrases[kv.Key])
            {
                var u = Planner.Build(p, SpeakMode.Speak);
                Console.WriteLine($"   {u.Phonetic}   [{u.SylS.Length} syllables, {u.PhS.Length} phonemes]");
            }
        }

        var mix = new float[(length + 8) * Tick];
        var stemRms = machines.Keys.ToDictionary(k => k, k => 0.0);
        var buf = new Sample[256];
        for (int row = 0; row < length + 8; row++)
        {
            if (events.TryGetValue(row, out var evs))
                foreach (var (m, track, col, val) in evs)
                {
                    var x = machines[m];
                    switch (col - baseCol)
                    {
                        case 0: x.SetNote(new Note(val), track); break;
                        case 1: x.SetVelocity(val, track); break;
                        case 2: x.SetPhrase(val, track); break;
                        case 3: x.SetWord(val, track); break;
                        case 4: x.SetWords(val, track); break;
                        case 5: x.SetStep(val, track); break;
                        case 6: x.SetPosition(val, track); break;
                        default: globals[col].SetValue(x, val); break;
                    }
                }
            foreach (var kv in machines)
            {
                int done = 0;
                while (done < Tick)
                {
                    int n = Math.Min(256, Tick - done);
                    if (kv.Value.Work(buf, n, WorkModes.WM_WRITE))
                        for (int i = 0; i < n; i++)
                        {
                            float s = buf[i].L / 32768f * amps[kv.Key];
                            mix[row * Tick + done + i] += s;
                            stemRms[kv.Key] += s * s;
                        }
                    done += n;
                }
            }
        }

        float peak = 0; double e = 0; int over = 0;
        foreach (var s in mix) { peak = Math.Max(peak, Math.Abs(s)); e += s * s; if (Math.Abs(s) >= 1f) over++; }
        Console.WriteLine($"mix: {mix.Length / (double)Sr:F1} s  peak {peak:F2}  rms {Math.Sqrt(e / mix.Length):F3}  samples over full scale {over}");
        foreach (var kv in stemRms) Console.WriteLine($"   {kv.Key,-6} rms over whole song {Math.Sqrt(kv.Value / mix.Length):F3}");
        bool tailSilent = true;
        for (int i = (length + 6) * Tick; i < mix.Length; i++) if (Math.Abs(mix[i]) > 1e-4f) tailSilent = false;
        Console.WriteLine($"silent after the last row: {tailSilent}");

        using var bw = new BinaryWriter(File.Create(wavPath));
        int cnt = mix.Length;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + cnt * 2); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(Sr); bw.Write(Sr * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8.ToArray()); bw.Write(cnt * 2);
        foreach (var s in mix) bw.Write((short)Math.Clamp((int)(s * 32767), -32768, 32767));
    }
}
