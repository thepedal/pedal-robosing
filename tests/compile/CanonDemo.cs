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

            // 10. Editor comforts: Shift-drag moves the whole line, and a moved block is reported so it can be heard
            {
                var ed10 = new MelodyEditor { BaseNote = 48, KeyRoot = 0, KeyMask = 0xAB5, Snap = true };
                ed10.Load(new[] { "a", "b", "c" }, LineScore.Parse("0,2,0 0,2,2 0,2,4+2,7", 3), 4);
                var heard = new List<string>(); ed10.Heard += (k, p) => heard.Add(k + ":" + p);
                void Mouse(string handler, double x, double y, int clicks = 1)
                {
                    object args = handler == "OnMove" ? new System.Windows.Input.MouseEventArgs { At = new System.Windows.Point { X = x, Y = y } }
                                                      : new System.Windows.Input.MouseButtonEventArgs { At = new System.Windows.Point { X = x, Y = y }, ClickCount = clicks };
                    typeof(MelodyEditor).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ed10, new object[] { null, args });
                }
                System.Windows.Input.Keyboard.Modifiers = System.Windows.Input.ModifierKeys.Shift;
                Mouse("OnDown", 5, 200); Mouse("OnMove", 5, 168); Mouse("OnUp", 5, 168);          // first block up two rows, C to D
                Console.WriteLine($"Shift-drag C up to D in C major (C D E~G): {ed10.Score.ToText()}   heard {string.Join(" ", heard)}   (want 0,2,2 0,2,4 0,2,5+2,9: D E F~A)");
                System.Windows.Input.Keyboard.Modifiers = System.Windows.Input.ModifierKeys.None;
                Mouse("OnDown", 5, 168); Mouse("OnMove", 5, 200); Mouse("OnUp", 5, 200);
                Console.WriteLine($"plain drag of the first block back down: {ed10.Score.ToText()}   (want only the first to move: 0,2,0 ...)");
                ed10.Snap = false; System.Windows.Input.Keyboard.Modifiers = System.Windows.Input.ModifierKeys.Shift;
                Mouse("OnDown", 5, 200); Mouse("OnMove", 5, 184); Mouse("OnUp", 5, 184);
                System.Windows.Input.Keyboard.Modifiers = System.Windows.Input.ModifierKeys.None;
                Console.WriteLine($"Snap off, Shift-drag up one row: {ed10.Score.ToText()}   (want every note one semitone up: 0,2,1 0,2,5 0,2,6+2,10)");

                var h10 = new Host(); h10.MasterInfo.SamplesPerTick = Tick;
                var m10 = new PedalRoboSingMachine(h10);
                var u10 = Planner.Build("hello", SpeakMode.Speak);
                m10.AuditionSyllable(u10, 1, 4);
                var a10 = Play(m10, 6, out _); Play(m10, 10, out bool still);
                m10.AuditionSyllable(u10, 9, 0);                                    // out of range: ignored
                Console.WriteLine($"hear one syllable: level {Rms(a10, 0, a10.Length):0.000}, stops by itself {!still}");
            }

            // 11. Copy and paste through the real window code
            {
                var hw = new Host(); var mw = new PedalRoboSingMachine(hw);
                mw.SetPhrases(new[] { "Seven silver circuits sing.", "I am a robot.", "Hello.", "", "Peace." }, new[] { "0,2,0 0,2,2 0,2,4 0,2,5 0,2,7 0,2,5 2,6,12", "", "" });
                var win = new PhraseEditorWindow(mw);
                var wt = typeof(PhraseEditorWindow); const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
                var box = (System.Windows.Controls.TextBox)wt.GetField("_box", F).GetValue(win);
                var note = (System.Windows.Controls.TextBlock)wt.GetField("_toolNote", F).GetValue(win);
                var scores = (List<string>)wt.GetField("_scores", F).GetValue(win);
                void Caret(int line) { int at = 0; var ls = box.Text.Split('\n'); for (int i = 0; i < line; i++) at += ls[i].Length + 1; box.CaretIndex = at + 1; wt.GetMethod("UpdatePreview", F).Invoke(win, null); }
                void Do(string m) => wt.GetMethod(m, F).Invoke(win, null);
                Caret(1); Do("CopyMelody"); Console.WriteLine("  copy an empty line : " + note.Text);
                Do("PasteMelody"); Console.WriteLine("  paste with nothing : " + note.Text);
                Caret(0); Do("CopyMelody"); Console.WriteLine("  copy line 1        : " + note.Text);
                Caret(1); Do("PasteMelody"); Console.WriteLine("  paste on line 2    : " + note.Text);
                Caret(2); Do("PasteMelody"); Console.WriteLine("  paste on line 3    : " + note.Text);
                Caret(3); box.CaretIndex -= 1; win.GetType().GetMethod("UpdatePreview", F).Invoke(win, null);
                Do("PasteMelody"); Console.WriteLine("  paste on empty 4   : " + note.Text);
                Console.WriteLine("  text now           : " + box.Text.Replace("\r", "").Replace("\n", " / "));
                while (scores.Count > 3 && scores[3].Length > 0 && scores.Count < 6) { Console.WriteLine("  line 4 melody      : " + scores[3]); break; }
                Do("Undo");
                Console.WriteLine("  melodies now       : " + string.Join(" | ", scores));
                Do("Undo"); Do("Undo"); Console.WriteLine("  after two undos    : " + string.Join(" | ", scores) + "   " + note.Text);
                Do("Redo"); Console.WriteLine("  after one redo     : " + string.Join(" | ", scores));
            }

            // 12. Import a tune from a MIDI file onto typed lines, then sing the whole song from one note
            {
                byte[] Smf()
                {
                    var f = new List<byte>();
                    void Be(int v, int n) { for (int i = n - 1; i >= 0; i--) f.Add((byte)(v >> (8 * i))); }
                    void Chunk(string tag, List<byte> body) { f.AddRange(System.Text.Encoding.ASCII.GetBytes(tag)); Be(body.Count, 4); f.AddRange(body); }
                    void Var(List<byte> o, int v) { var st = new Stack<byte>(); st.Push((byte)(v & 0x7F)); while ((v >>= 7) > 0) st.Push((byte)(0x80 | (v & 0x7F))); o.AddRange(st); }
                    List<byte> Track(string name, int ch, (double at, double len, int pitch)[] notes)
                    {
                        var o = new List<byte>(); Var(o, 0); o.Add(0xFF); o.Add(0x03); o.Add((byte)name.Length); o.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
                        var ev = new List<(int t, bool on, int p)>();
                        foreach (var (at, len, pitch) in notes) { ev.Add(((int)(at * 480), true, pitch)); ev.Add(((int)((at + len) * 480), false, pitch)); }
                        ev.Sort((x, y) => x.t != y.t ? x.t.CompareTo(y.t) : x.on.CompareTo(y.on));
                        int now = 0;
                        foreach (var (t, on, pch) in ev) { Var(o, t - now); now = t; o.Add((byte)((on ? 0x90 : 0x80) | ch)); o.Add((byte)pch); o.Add(on ? (byte)100 : (byte)0); }
                        Var(o, 0); o.Add(0xFF); o.Add(0x2F); o.Add(0);
                        return o;
                    }
                    var head = new List<byte>(); foreach (int v in new[] { 1, 3, 480 }) { head.Add((byte)(v >> 8)); head.Add((byte)v); }
                    Chunk("MThd", head);
                    var tempo = new List<byte>();
                    foreach (var (dt, us) in new[] { (0, 631579), (480 * 16, 600000) }) { Var(tempo, dt); tempo.AddRange(new byte[] { 0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us }); }
                    Var(tempo, 0); tempo.AddRange(new byte[] { 0xFF, 0x2F, 0 });
                    Chunk("MTrk", tempo);
                    Chunk("MTrk", Track("Lead Vocal", 0, new[] {
                        (4.0, .5, 72), (4.5, .5, 74), (5.0, .5, 76), (5.5, .5, 77), (6.0, .5, 79), (6.5, .5, 77), (7.0, .25, 76), (7.25, .75, 72),
                        (12.0, 1.0, 67), (13.0, 1.0, 69), (14.0, .5, 71), (14.5, .5, 72), (15.0, 1.0, 74),
                        (20.0, 2.0, 72) }));
                    Chunk("MTrk", Track("Kit", 9, new[] { (0.0, .25, 36), (1.0, .25, 38), (2.0, .25, 36) }));
                    return f.ToArray();
                }
                var parts = MidiFile.Read(Smf(), out int div, out var tempos);
                Console.WriteLine($"  tempo marks: {string.Join(", ", tempos.Select(q => $"{q.Bpm:0.0} at tick {q.Tick}"))}   (want 95.0 at 0, 100.0 at 7680)");
                Console.WriteLine($"MIDI file: division {div}, parts: {string.Join("; ", parts.Select(q => $"{q.Name} ch{q.Channel + 1} {q.Notes.Count} notes{(q.Drums ? " drums" : "")}"))}");
                try { MidiFile.Read(new byte[40], out _); } catch (FormatException ex) { Console.WriteLine("  a bad file is refused: " + ex.Message); }

                var transport = new FakeMachine { IsPlaying = true, ParameterGroups = new System.Collections.ObjectModel.ReadOnlyCollection<BuzzGUI.Interfaces.IParameterGroup>(new BuzzGUI.Interfaces.IParameterGroup[0]) };
                var hi = new Host { Machine = transport }; hi.MasterInfo.SamplesPerTick = Tick;
                var mi = new PedalRoboSingMachine(hi) { Tune = 0, VibDepth = 0, Portamento = 0, Glide = 0, ConsTune = 0 };
                mi.SetPhrases(new[] { "Seven silver circuits sing.", "", "I am a robot.", "Hello." });
                var wi = new PhraseEditorWindow(mi);
                var rep = (string)typeof(PhraseEditorWindow).GetMethod("ImportNotes", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(wi, new object[] { parts[0], div, tempos });
                Console.WriteLine("  --- report ---\n  " + rep.Replace("\n", "\n  ") + "\n  --------------");
                Console.WriteLine("  melodies: " + string.Join(" | ", mi.GetScores()));
                Console.WriteLine("  timing  : " + string.Join(" | ", mi.GetLinks()) + "   (want 32,60 | | 32,60 | 0,60: lines 32 ticks apart, all from C-5)");

                // One note, C-5 with Phrase 1: line 1 at tick 0, line 3 at tick 32, line 4 at tick 64
                mi.SetNote(new Note(0x51), 0);
                var song = Play(mi, 80, out _);
                Play(mi, 16, out bool stillOn);
                string Lv(double a, double b) => Rms(song, (int)(a * Tick), (int)(b * Tick)).ToString("0.000");
                Console.WriteLine($"  sung from one note: line 1 (ticks 1-13) {Lv(1, 13)}, rest (22-30) {Lv(22, 30)}, line 2 (33-46) {Lv(33, 46)}, rest (54-62) {Lv(54, 62)}, line 3 (65-71) {Lv(65, 71)}, after (76-80) {Lv(76, 80)}; stops by itself {!stillOn}");
                double Onset(float[] x, double fromTick) { for (int i = (int)(fromTick * Tick); i < x.Length; i++) if (Math.Abs(x[i]) > 0.03) return i / (double)Tick; return -1; }
                Console.WriteLine($"  first sound of line 2 (opens on a vowel) at tick {Onset(song, 24):0.00} (want 32), of line 3 (opens on a consonant) at {Onset(song, 56):0.00} (want a little before 64)");
                // Starting from the second sung line, and a note-off stopping the song
                mi.SetPhrase(3, 0); mi.SetNote(new Note(0x51), 0);
                var late = Play(mi, 44, out _);
                Console.WriteLine($"  from Phrase 3: its line (1-13) {Rms(late, Tick, 13 * Tick):0.000}, the next after 32 ticks (33-39) {Rms(late, 33 * Tick, 39 * Tick):0.000}");
                mi.SetPhrase(1, 0); mi.SetNote(new Note(0x51), 0); Play(mi, 4, out _); mi.SetNote(new Note(Note.Off), 0);
                var cut = Play(mi, 44, out _);
                Console.WriteLine($"  note-off after 4 ticks: this line finishes (2-9) {Rms(cut, 2 * Tick, 9 * Tick):0.000}, nothing follows (30-42) {Rms(cut, 30 * Tick, 42 * Tick):0.000}");
                // With the transport stopped a note sings its one line; Stop silences a song in progress
                transport.IsPlaying = false; Play(mi, 2, out _);
                mi.SetNote(new Note(0x51), 0);
                var idle = Play(mi, 44, out _);
                Console.WriteLine($"  transport stopped: the line itself (1-13) {Rms(idle, Tick, 13 * Tick):0.000}, nothing follows (33-42) {Rms(idle, 33 * Tick, 42 * Tick):0.000}");
                transport.IsPlaying = true; Play(mi, 1, out _);
                mi.SetNote(new Note(0x51), 0); Play(mi, 4, out _); mi.StopAll();
                var stopped = Play(mi, 44, out _);
                Console.WriteLine($"  Stop after 4 ticks: (1-9) {Rms(stopped, Tick, 9 * Tick):0.000}, (33-42) {Rms(stopped, 33 * Tick, 42 * Tick):0.000}   (want silence in both)");
                transport.IsPlaying = false; Play(mi, 1, out _); transport.IsPlaying = true; Play(mi, 1, out _);
                mi.SetNote(new Note(0x51), 0); Play(mi, 4, out _); transport.IsPlaying = false;
                var halted = Play(mi, 44, out _);
                Console.WriteLine($"  transport stopped mid-song: (1-9) {Rms(halted, Tick, 9 * Tick):0.000}, (26-40) {Rms(halted, 26 * Tick, 40 * Tick):0.000}   (want silence in both)");
                var ser2 = new System.Xml.Serialization.XmlSerializer(typeof(RoboSingState));
                var sw2 = new System.IO.StringWriter(); ser2.Serialize(sw2, mi.MachineState);
                var back = new PedalRoboSingMachine(new Host()); back.MachineState = (RoboSingState)ser2.Deserialize(new System.IO.StringReader(sw2.ToString()));
                Console.WriteLine("  saved and loaded timing: " + string.Join(" | ", back.GetLinks()));
            }

            // 13. A double-click reaches the editor: ReBuzz shows the host window, the launcher hides it and opens the editor
            {
                var fm = new FakeMachine { ParameterGroups = new System.Collections.ObjectModel.ReadOnlyCollection<BuzzGUI.Interfaces.IParameterGroup>(new BuzzGUI.Interfaces.IParameterGroup[0]) };
                var hd = new Host { Machine = fm };
                var md = new PedalRoboSingMachine(hd); fm.ManagedMachine = md;
                var hostWin = new HostWin();
                var gui = new PedalRoboSingGuiFactory().CreateGUI(hostWin); gui.Machine = fm;
                object Ed() => typeof(PedalRoboSingMachine).GetField("_editor", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(md);
                bool wasOpen = Ed() != null;
                hostWin.FakeShow();
                Console.WriteLine($"double-click: host made invisible first (opacity {hostWin.Opacity}, no frame {hostWin.WindowStyle == System.Windows.WindowStyle.None}); editor open before {wasOpen}, after {Ed() != null}; host hidden again {!hostWin.IsVisible}");
                var firstEd = Ed(); hostWin.FakeShow();
                Console.WriteLine($"a second double-click keeps the same editor window: {ReferenceEquals(firstEd, Ed())}; attribute asks for a window of its own: {typeof(PedalRoboSingGuiFactory).GetCustomAttribute<BuzzGUI.Interfaces.MachineGUIFactoryDecl>().PreferWindowedGUI}");
                var textBox = (System.Windows.Controls.TextBox)typeof(PhraseEditorWindow).GetField("_box", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(firstEd);
                Console.WriteLine($"a new machine's editor text is empty: '{textBox.Text}'");
            }

            Console.WriteLine($"Line from the second word only: {n2 / (119.05 * 16):0.0} ticks (want about 16)");
        }
    }
}

class HostWin : System.Windows.Window, BuzzGUI.Interfaces.IMachineGUIHost { }
