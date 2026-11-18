// v1.5: the phrase editor's line is what every preset and every play mode performs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Buzz.MachineInterface;
using PedalRoboSing;
using PedalRoboSing.Speech;

static class CanonDemo
{
    const int Tick = 5250;                       // 126 BPM, 4 ticks per beat at 44.1 kHz
    static readonly Sample[] buf = new Sample[256];
    const string Text = "Seven silver circuits sing.";
    const string Score = "0,2,0 0,2,2 0,2,4 0,2,5 0,2,7 0,2,5 2,6,12";

    static float[] Play(PedalRoboSingMachine m, double ticks, out bool active)
    {
        var o = new List<float>(); active = false;
        int left = (int)(ticks * Tick);
        while (left > 0)
        {
            int n = Math.Min(256, left); active = m.Work(buf, n, WorkModes.WM_WRITE);
            for (int i = 0; i < n; i++) o.Add(buf[i].L / 32768f);
            left -= n;
        }
        return o.ToArray();
    }

    static double Rms(float[] a, int from, int to)
    {
        from = Math.Clamp(from, 0, a.Length); to = Math.Clamp(to, from, a.Length);
        double s = 0; for (int i = from; i < to; i++) s += a[i] * a[i];
        return to > from ? Math.Sqrt(s / (to - from)) : 0;
    }

    // Pitch by autocorrelation, 60..900 Hz
    static double F0(float[] a, int from, int len, int sr = 44100)
    {
        int lo = sr / 900, hi = sr / 60;
        var r = new double[hi + 1]; double best = 0; int bestLag = 0;
        for (int lag = lo; lag <= hi; lag++)
        {
            double x = 0; for (int i = from; i < from + len - lag && i + lag < a.Length; i++) x += a[i] * a[i + lag];
            r[lag] = x / (len - lag);
            if (r[lag] > best) best = r[lag];
        }
        // the shortest period that is nearly as good as the best, so a sub-octave doesn't win
        for (int lag = lo + 1; lag < hi; lag++)
            if (r[lag] >= 0.85 * best && r[lag] >= r[lag - 1] && r[lag] >= r[lag + 1]) { bestLag = lag; break; }
        return bestLag > 0 ? (double)sr / bestLag : 0;
    }

    public static void Run(string presetPath)
    {
        // 1. Every preset: one note and a phrase number sings the drawn line and stops by itself
        var props = typeof(PedalRoboSingMachine).GetProperties()
            .Select(p => (p, d: p.GetCustomAttribute<ParameterDecl>())).Where(x => x.d != null).ToDictionary(x => x.d.Name, x => x.p);
        var doc = XDocument.Load(presetPath);
        int ok = 0, total = 0;
        foreach (var item in doc.Root.Elements("Item"))
        {
            var h = new Host(); h.MasterInfo.SamplesPerTick = Tick;
            var m = new PedalRoboSingMachine(h);
            foreach (var p in item.Descendants("Parameter")) props[(string)p.Attribute("Name")].SetValue(m, (int)p.Attribute("Value"));
            m.SetPhrases(new[] { Text }, new[] { Score });
            m.SetNote(new Note(0x40), 0);
            var a = Play(m, 60, out _);
            Play(m, 12, out bool late);
            double level = Rms(a, 0, a.Length);
            bool byHand = m.PlayMode != 5;
            bool pass = byHand ? level > 0.002 : level > 0.002 && !late;
            total++; if (pass) ok++;
            Console.WriteLine($"  {(string)item.Attribute("Key"),-24} mode {m.PlayMode}  level {level:0.000}  ends by itself {!late}{(byHand ? "  (played by hand)" : "")}{(pass ? "" : "   <-- FAIL")}");
        }
        Console.WriteLine($"presets singing the line from one note: {ok}/{total}");

        PedalRoboSingMachine M(int mode, string score)
        {
            var h = new Host(); h.MasterInfo.SamplesPerTick = Tick;
            var m = new PedalRoboSingMachine(h) { PlayMode = mode, Tune = 0, VibDepth = 0, Portamento = 0, Glide = 0, ConsTune = 0, Inflection = 0,
                ReactLevel = 64, ReactTune = 64, ReactSize = 64, ReactRing = 64, ReactVib = 64, ReactRhyme = 64, ReactAllit = 64 };
            m.SetPhrases(new[] { "[AA] [AA] [AA]" }, new[] { score });
            return m;
        }

        // 2. Syllable mode: the melody sets each syllable's pitch, the pattern its timing
        {
            var m = M(1, "0,2,0 0,2,12 0,2,7");
            var f = new double[3];
            for (int k = 0; k < 3; k++)
            {
                m.SetNote(new Note(0x40), 0);
                var a = Play(m, 4, out _);
                f[k] = F0(a, 2 * Tick, Tick);
                m.SetNote(new Note(Note.Off), 0); Play(m, 2, out _);
            }
            Console.WriteLine($"Syllable mode, melody 0 +12 +7: {f[0]:0} {f[1]:0} {f[2]:0} Hz   (ratios {f[1] / f[0]:0.00} {f[2] / f[0]:0.00}; want 2.00 1.50)");
        }

        // 3. Sync and Scrub take the pitch from the melody too
        {
            var m = M(3, "0,2,0 0,2,12 0,2,7"); m.SyncStep = 4;
            m.SetNote(new Note(0x40), 0);
            var a = Play(m, 12, out _);
            double f0 = F0(a, 1 * Tick, Tick), f1 = F0(a, 5 * Tick, Tick), f2 = F0(a, 9 * Tick, Tick);
            Console.WriteLine($"Sync mode: {f0:0} {f1:0} {f2:0} Hz   (ratios {f1 / f0:0.00} {f2 / f0:0.00})");

            var s = M(4, "0,2,0 0,2,12 0,2,7");
            s.SetPosition(100, 0); s.SetNote(new Note(0x40), 0);
            var b = Play(s, 4, out _); double g0 = F0(b, Tick, Tick);
            s.SetPosition(500, 0); b = Play(s, 4, out _); double g1 = F0(b, Tick, Tick);
            Console.WriteLine($"Scrub mode, position 10% then 50%: {g0:0} {g1:0} Hz   (ratio {g1 / g0:0.00}; want 2.00)");
        }

        // 4. A line with no melody is untouched in the other modes
        {
            var m = M(1, ""); m.SetNote(new Note(0x40), 0);
            var a = Play(m, 4, out _);
            var n = M(1, "0,2,0 0,2,0 0,2,0"); n.SetNote(new Note(0x40), 0);
            var b = Play(n, 4, out _);
            bool same = a.Length == b.Length && !a.Where((x, i) => x != b[i]).Any();
            Console.WriteLine($"no melody drawn: Syllable mode output identical to a flat melody: {same}");
        }

        // 5. Line: Hold off leaves silence in a long block; Scan below 64 sings last syllable first
        {
            var u = Planner.Build("[AA] [IY] [UW]", SpeakMode.Speak);
            var sc = LineScore.Parse("0,8,0 0,8,4 0,8,7", u.SylS.Length);
            foreach (bool hold in new[] { true, false })
            {
                var v = new LpcVoice(1) { ChipRate = 16000, FrameMs = 15, TickMs = 119.05f, Speed = 1f, LineHold = hold };
                v.StartLine(u, sc, 48f, 1f);
                var o = new List<float>(); for (int i = 0; i < 16000 * 4 && v.IsActive; i++) o.Add(v.Next());
                var a = o.ToArray(); int t = (int)(119.05f * 16);             // samples per tick at 16 kHz
                Console.WriteLine($"Line, Hold {(hold ? "on " : "off")}: level early in block 1 {Rms(a, 0, 2 * t):0.000}, late in block 1 {Rms(a, 5 * t, 7 * t):0.000}, length {a.Length / (double)t:0.0} ticks");
            }
            foreach (bool rev in new[] { false, true })
            {
                var v = new LpcVoice(1) { ChipRate = 16000, FrameMs = 15, TickMs = 119.05f, Speed = 1f };
                v.StartLine(u, sc, 0, 2, rev, 48f, 1f);
                var o = new List<float>(); var marks = new List<string>();
                int t = (int)(119.05f * 16);
                for (int i = 0; i < 16000 * 4 && v.IsActive; i++)
                {
                    o.Add(v.Next());
                    if (i % (8 * t) == 4 * t && v.TryLineProgress(u.Phonetic, out float ticks)) marks.Add(ticks.ToString("0"));
                }
                var a = o.ToArray();
                Console.WriteLine($"Line {(rev ? "backwards" : "forwards ")}: pitch in blocks {F0(a, 3 * t, 3 * t, 16000):0} {F0(a, 11 * t, 3 * t, 16000):0} {F0(a, 19 * t, 3 * t, 16000):0} Hz, marker at ticks {string.Join(" ", marks)}");
            }
            var w = new LpcVoice(1) { ChipRate = 16000, FrameMs = 15, TickMs = 119.05f, Speed = 1f };
            w.StartLine(u, sc, 1, 2, false, 48f, 1f);
            int n2 = 0; while (n2 < 16000 * 4 && w.IsActive) { w.Next(); n2++; }
            // 6. The key in the editor
            var ed = new MelodyEditor { BaseNote = 48, KeyRoot = 0, KeyMask = 0xAB5 };          // C major, line started on C
            ed.Load(new[] { "a", "b", "c", "d", "e", "f" }, LineScore.Parse("0,2,1 0,2,3 0,2,6 0,2,8 0,2,10 0,2,7", 6), 4);
            int before = ed.OutOfKey, moved = ed.FitToKey();
            Console.WriteLine($"C major from C, pitches 1 3 6 8 10 7: {before} outside; Fit to key moved {moved} -> {string.Join(" ", ed.Score.Pitch)}   (want 0 2 5 7 9 7)");
            ed = new MelodyEditor { BaseNote = 52, KeyRoot = 0, KeyMask = 0xAB5 };              // same key, line started on E
            ed.Load(new[] { "a", "b", "c" }, LineScore.Parse("0,2,0 0,2,2 0,2,4", 3), 4);
            ed.FitToKey();
            Console.WriteLine($"C major from E, pitches 0 2 4: -> {string.Join(" ", ed.Score.Pitch)}   (want 0 1 3: E F G)");
            ed = new MelodyEditor { BaseNote = 48, KeyRoot = 0, KeyMask = 0xFFF };
            ed.Load(new[] { "a" }, LineScore.Parse("0,2,1", 1), 4);
            Console.WriteLine($"Chromatic: outside the key {ed.OutOfKey}, Fit moved {ed.FitToKey()}");

            // 7. Root and Scale set from the editor go through the host's parameters
            var pr = new P { Name = "Root" }; var ps = new P { Name = "Scale" };
            var g1 = new PG { Parameters = new System.Collections.ObjectModel.ReadOnlyCollection<BuzzGUI.Interfaces.IParameter>(new BuzzGUI.Interfaces.IParameter[] { pr, ps }) };
            var g0 = new PG { Parameters = new System.Collections.ObjectModel.ReadOnlyCollection<BuzzGUI.Interfaces.IParameter>(new BuzzGUI.Interfaces.IParameter[0]) };
            var hh = new Host { Machine = new FakeMachine { ParameterGroups = new System.Collections.ObjectModel.ReadOnlyCollection<BuzzGUI.Interfaces.IParameterGroup>(new BuzzGUI.Interfaces.IParameterGroup[] { g0, g1 }) } };
            var mm = new PedalRoboSingMachine(hh);
            mm.SetGlobal("Root", 9); mm.SetGlobal("Scale", 2);
            var bare = new PedalRoboSingMachine(new Host()); bare.SetGlobal("Scale", 2);
            Console.WriteLine($"SetGlobal: host parameters received Root {pr.Last}, Scale {ps.Last}; with no host parameters the property is set: Scale {bare.Scale}, mask 0x{bare.KeyMask:X}");

            // 8. Key Lock: the melody moves by scale steps from the starting note
            const int Maj = 0xAB5, MinPent = 0x4A9;
            string T(int note, int[] offs, int root, int mask) => string.Join(" ", offs.Select(o => ScaleSteps.Transpose(note, o, root, mask)));
            Console.WriteLine($"C major, steps 0 4 7 12 -1 6: from C {T(48, new[] { 0, 4, 7, 12, -1, 6 }, 0, Maj)} | from E {T(52, new[] { 0, 4, 7, 12, -1, 6 }, 0, Maj)} | from C# {T(49, new[] { 0, 4 }, 0, Maj)}");
            Console.WriteLine("   want: from C 48 52 55 60 47 54 | from E 52 55 59 64 50 58 | from C# 48 52");
            Console.WriteLine($"A minor pentatonic, steps 0 3 5 7 10 12 from A {T(57, new[] { 0, 3, 5, 7, 10, 12 }, 9, MinPent)} | from D {T(62, new[] { 0, 3, 5, 7, 10, 12 }, 9, MinPent)}   (want 57 60 62 64 67 69 | 62 64 67 69 72 74)");
            Console.WriteLine($"Chromatic: {T(50, new[] { 0, 1, -3 }, 0, 0xFFF)}   (want 50 51 47)");
            foreach (var (name, nb) in new[] { ("C-4", 0x41), ("E-4", 0x45), ("D-4", 0x43) })
            {
                var h2 = new Host(); h2.MasterInfo.SamplesPerTick = Tick;
                var km = new PedalRoboSingMachine(h2) { Tune = 0, VibDepth = 0, Portamento = 0, Glide = 0, ConsTune = 0, Inflection = 0,
                    ReactLevel = 64, ReactTune = 64, ReactSize = 64, ReactRing = 64, ReactVib = 64, ReactRhyme = 64, ReactAllit = 64 };
                km.SetPhrases(new[] { "[AA] [AA] [AA]" }, new[] { "0,4,0 0,4,4 0,4,7" });
                km.SetNote(new Note(nb), 0);
                var a2 = Play(km, 12, out _);
                Console.WriteLine($"Line from {name}, Key Lock on, C major, melody 0 4 7: {F0(a2, 1 * Tick, 2 * Tick):0} {F0(a2, 5 * Tick, 2 * Tick):0} {F0(a2, 9 * Tick, 2 * Tick):0} Hz");
            }
            Console.WriteLine("   want: C E G 262 330 392 | E G B 330 392 494 | D F A 294 349 440");
            {
                var ser = new System.Xml.Serialization.XmlSerializer(typeof(RoboSingState));
                var k1 = new PedalRoboSingMachine(new Host()); k1.SetKey(9, 2, true);
                var sw = new System.IO.StringWriter(); ser.Serialize(sw, k1.MachineState);
                var k2 = new PedalRoboSingMachine(new Host()) { Root = 5, Scale = 7 };
                k2.MachineState = (RoboSingState)ser.Deserialize(new System.IO.StringReader(sw.ToString()));
                var k3 = new PedalRoboSingMachine(new Host()) { Root = 5, Scale = 7 };
                k3.MachineState = (RoboSingState)ser.Deserialize(new System.IO.StringReader(
                    "<?xml version=\"1.0\"?><RoboSingState><Version>1</Version><Phrases><string>Hello.</string></Phrases></RoboSingState>"));
                var k4 = new PedalRoboSingMachine(new Host());
                Console.WriteLine($"key saved and loaded: root {k2.KeyRootNow} scale {k2.KeyScaleNow} lock {k2.KeyLock} (want 9 2 True, whatever Root/Scale say)");
                Console.WriteLine($"a song from before v1.7: root {k3.KeyRootNow} scale {k3.KeyScaleNow} lock {k3.KeyLock} (want 5 7 False: follows the parameters, unlocked)");
                Console.WriteLine($"a new machine: root {k4.KeyRootNow} scale {k4.KeyScaleNow} lock {k4.KeyLock} (want 0 1 True)");
            }

            // 9. A syllable over several notes
            {
                var s9 = LineScore.Parse("0,4,0+4,4+4,7 2,2,5", 2);
                Console.WriteLine($"parse and write back: {s9.ToText()}   notes {s9.Notes(0)} {s9.Notes(1)}, syllable lengths {s9.LenAt(0)} {s9.LenAt(1)}, total {s9.TotalTicks} ticks (want 12 2, 16)");
                var e9 = LineScore.Parse("0,8,0 0,2,3", 2);
                e9.Split(0, 0, 3); e9.Split(0, 1, 2); e9.SetNotePitch(0, 1, 4); e9.SetNotePitch(0, 2, 7);
                string cut = e9.ToText(); e9.Join(0, 1); string j1 = e9.ToText(); e9.Join(0, 0); string j2 = e9.ToText();
                Console.WriteLine($"cut 8 at 3, then the rest at 2: {cut} | join the middle: {j1} | join again: {j2}");
                Console.WriteLine("   want: 0,3,0+2,4+3,7 0,2,3 | 0,5,0+3,7 0,2,3 | 0,8,0 0,2,3");
                Console.WriteLine($"an old reader sees a plain default for that syllable; a plain score still reads: {LineScore.Parse("0,2,0 1,3,-2", 2).ToText()}");

                var u9 = Planner.Build("[AA]", SpeakMode.Speak);
                foreach (bool rev in new[] { false, true })
                {
                    var v9 = new LpcVoice(1) { ChipRate = 16000, FrameMs = 15, TickMs = 119.05f, Speed = 1f };
                    v9.StartLine(u9, LineScore.Parse("0,4,0+4,4+4,7", 1), 0, 0, rev, 48f, 1f);
                    var o9 = new List<float>(); var notes9 = new List<string>(); int t9 = (int)(119.05f * 16);
                    for (int i = 0; i < 16000 * 4 && v9.IsActive; i++)
                    {
                        o9.Add(v9.Next());
                        if (i == 2 * t9 || i == 6 * t9 || i == 10 * t9) notes9.Add(v9.NoteNow.ToString("0"));
                    }
                    var a9 = o9.ToArray();
                    Console.WriteLine($"one syllable over 0 +4 +7, {(rev ? "backwards" : "forwards ")}: note at ticks 2 6 10 = {string.Join(" ", notes9)}, level in each {Rms(a9, t9, 3 * t9):0.00} {Rms(a9, 5 * t9, 7 * t9):0.00} {Rms(a9, 9 * t9, 11 * t9):0.00}, length {a9.Length / (double)t9:0.0} ticks");
                }
                Console.WriteLine("   want 48 52 55 forwards, 55 52 48 backwards, sound in all three, about 12 ticks");
                var ed9 = new MelodyEditor { BaseNote = 48, KeyRoot = 0, KeyMask = 0xAB5 };
                ed9.Load(new[] { "a" }, LineScore.Parse("0,4,0+4,3+4,6", 1), 4);
                int out9 = ed9.OutOfKey; ed9.FitToKey();
                Console.WriteLine($"key check sees every note: {out9} outside, fitted to {ed9.Score.ToText()}   (want 2, 0,4,0+4,2+4,5)");
            }

            Console.WriteLine($"Line from the second word only: {n2 / (119.05 * 16):0.0} ticks (want about 16)");
        }
    }
}
