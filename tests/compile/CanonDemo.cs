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
            Console.WriteLine($"Line from the second word only: {n2 / (119.05 * 16):0.0} ticks (want about 16)");
        }
    }
}
