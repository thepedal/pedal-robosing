// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — phrase editor (right-click > Edit Phrases...).
//
// One phrase per line; line N is Phrase N in the track column. The strip under
// the text box shows how the line under the caret will be pronounced, so a word
// the rules get wrong can be fixed with [bracketed] ARPAbet phonemes.
// Built in code rather than XAML (house convention, avoids BAML issues).

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PedalRoboSing.Speech;

namespace PedalRoboSing
{
    sealed class PhraseEditorWindow : Window
    {
        readonly PedalRoboSingMachine _m;
        readonly TextBox _box;
        readonly TextBlock _preview;
        HashSet<string> _rhymes;
        readonly MelodyEditor _melody = new MelodyEditor();
        readonly List<string> _scores = new List<string>();      // one per line of the box
        readonly List<string> _links = new List<string>();       // song timing, kept beside each line's melody
        Button _importBtn;
        List<string> _lastLines = new List<string>();
        int _shownLine = -1;
        string _shownText;

        string _shownPhonetic;
        Utterance _shownU;
        // Undo for the melodies: each entry is every line's melody before a change
        readonly List<string[]> _undo = new List<string[]>(), _redo = new List<string[]>();
        string[] _snapshot = Array.Empty<string>();
        Button _hearBtn;
        TextBlock _toolNote;                       // says what the last melody tool did
        static int _clipLine;
        static string _clipText = "";              // the copied line's words, for pasting onto an empty line
        static bool _hear = true;                  // remembered while ReBuzz runs
        static string _clip;                       // a copied melody

        System.Windows.Threading.DispatcherTimer _timer;
        Button _rootBtn, _scaleBtn, _snapBtn, _lockBtn;
        bool _keyLockShown;
        TextBlock _keyNote;
        static bool _snap = true;                  // remembered while ReBuzz runs
        int _keyRoot = -1, _keyScale = -1, _keyBase = -2;

        const string Intro =
            "One phrase per line (line 1 is Phrase 1). The grid shows the melody of the line under the caret: drag a block up or down for pitch, " +
            "sideways for the silence before it, and its right edge for length. Double-click a block to sing its syllable over two notes. One note in the pattern with the line's Phrase number sings it; presets and parameters change how it sounds.";

        const string Help =
            "PHRASES\n" +
            "One phrase per line: line 1 is Phrase 1, line 2 is Phrase 2, and so on (up to 100).\n" +
            "Punctuation shapes spoken delivery:  ,  short pause   .  falling end   ?  rising end   !  bigger accents.\n" +
            "The strip under the text shows how the line will be pronounced, with \u00B7 between syllables, and any rhymes and alliteration found.\n\n" +
            "FIXING A WORD\n" +
            "Put phonemes in brackets, e.g.  [R IY B AH1 Z]  (1 marks the stressed vowel, _ is a pause).\n" +
            "Vowels: IY IH EY EH AE AA AO OW UH UW AH AX ER AY AW OY\n" +
            "Consonants: P B T D K G CH JH F V TH DH S Z SH ZH HH M N NG L R W Y WH\n\n" +
            "MELODY\n" +
            "Each block is one syllable of the line under the caret.\n" +
            "Drag it up or down to change its pitch; the highlighted row is the note played in the pattern.\n" +
            "Drag it sideways to change the silence before it. Drag its right edge to change its length.\n" +
            "Double-click a block to cut it in two where you click: the syllable is then sung across both notes, holding its vowel. Drag the pieces to different pitches, and cut again for more (up to 8).\n" +
            "Right-click a piece to join it back to the note before it.\n" +
            "Hold Shift and drag a block up or down to move the whole line with it: by notes of the key with Snap on, by semitones with it off.\n" +
            "Undo and Redo step through melody changes. Copy takes the line under the caret; Paste puts its melody on another line, block for block, or on an empty line puts its words there too. Clear removes a melody.\n" +
            "With Hear on, clicking or moving a block sings that syllable at its new pitch.\n" +
            "Sing line plays it from the last note played on track 1. The orange marker shows where the voice is. Stop silences everything.\n" +
            "Drag the bar between the text and the grid to resize them.\n\n" +
            "KEY\n" +
            "Key sets the song's key. It is saved with the phrases, so loading a preset does not change it. Rows outside the key are dark and blocks outside it are red.\n" +
            "With Snap on, a block dragged up or down lands only on notes in the key. Fit to key moves every block of the line to the nearest note in it.\n" +
            "Key Lock on: the grid shows the melody from the key's root. Start the line on the root and it sings as drawn; start it on another note and it moves by scale steps, so it stays in key. " +
            "The same line on two tracks, started a third apart, sings in harmony.\n" +
            "Key Lock off: the note that starts the line moves the melody by semitones. Chromatic turns the key off.\n" +
            "The Root and Scale parameters are separate: they choose what Tune To Scale and Inflect Snap aim at.\n\n" +
            "IMPORT MIDI\n" +
            "Type the words first, one line of the song per line. Import MIDI then reads a tune from a MIDI file (you choose the part if there are several) and fits its notes to your syllables, line by line: " +
            "spare notes are tied onto a syllable, and the cuts between lines fall on the rests. It sets the key and remembers when each line starts, so one note in the pattern sings the whole song.\n\n" +
            "IN THE PATTERN\n" +
            "One note with the line's Phrase number sings the whole line; the note sets the key. Every preset sings what is drawn here and only changes the sound.\n" +
            "Word and Words sing part of the line. Hold off makes the syllables short. Scan below 64 sings the line backwards.\n" +
            "If you set Play Mode to Syllable, Phoneme, Sync or Scrub, your pattern sets the timing and the melody here still sets each syllable's pitch.";

        public PhraseEditorWindow(PedalRoboSingMachine machine)
        {
            _m = machine;
            Title = "Pedal RoboSing - Phrases";
            Width = 900;
            Height = 820;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Theme.Apply(this);

            var root = new DockPanel { Margin = new Thickness(8) };

            var intro = new TextBlock { Text = Intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), FontSize = 11.5, Foreground = Theme.DimInk };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            // Bottom bar: view controls on the left, actions on the right
            var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Theme.MakeButton("Zoom \u2212", (s, e) => _melody.Zoom(-1), 64, 0));
            left.Children.Add(Theme.MakeButton("Zoom +", (s, e) => _melody.Zoom(+1), 64));
            left.Children.Add(Theme.MakeButton("Help", (s, e) => Theme.ShowMessage(this, "Pedal RoboSing - Phrases help", Help), 64));
            _importBtn = Theme.MakeButton("Import MIDI...", (s, e) => ImportMidi(), 110);
            left.Children.Add(_importBtn);
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Theme.MakeButton("Sing line", (s, e) => SingLine()));
            buttons.Children.Add(Theme.MakeButton("Speak line", (s, e) => SpeakLine()));
            buttons.Children.Add(Theme.MakeButton("Stop", (s, e) => _m.StopAll(), 64));
            buttons.Children.Add(Theme.MakeButton("Apply", (s, e) => Apply()));
            buttons.Children.Add(Theme.MakeButton("OK", (s, e) => { Apply(); Close(); }));
            buttons.Children.Add(Theme.MakeButton("Cancel", (s, e) => Close()));
            bar.Children.Add(buttons);
            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);

            // Middle: text, pronunciation strip, a draggable bar, then the melody with most of the room
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 48 });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 80 });

            _box = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = string.Join(Environment.NewLine, machine.GetPhrases()),
                Background = Theme.FieldBack, Foreground = Theme.Ink, BorderBrush = Theme.Edge, CaretBrush = Theme.Ink, SelectionBrush = Theme.Selection,
            };
            Grid.SetRow(_box, 0);
            grid.Children.Add(_box);

            _preview = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 4),
                Foreground = Theme.DimInk,
                MinHeight = 34,
            };
            Grid.SetRow(_preview, 1);
            grid.Children.Add(_preview);

            var splitter = new GridSplitter
            {
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = Theme.Divider,
            };
            Grid.SetRow(splitter, 2);
            grid.Children.Add(splitter);

            // The song's key: kept with the phrases, so presets cannot change it
            var keyBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            keyBar.Children.Add(new TextBlock { Text = "Key", Foreground = Theme.DimInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
            _rootBtn = Theme.MakeButton("", (s, e) => Theme.ShowChoices(_rootBtn, PedalRoboSingMachine.RootNames, _m.KeyRootNow,
                i => { _m.SetKey(i, _m.KeyScaleNow, _m.KeyLock); SyncKey(); }), 52);
            _scaleBtn = Theme.MakeButton("", (s, e) => Theme.ShowChoices(_scaleBtn, PedalRoboSingMachine.ScaleNames, _m.KeyScaleNow,
                i => { _m.SetKey(_m.KeyRootNow, i, _m.KeyLock); SyncKey(); }), 150);
            _lockBtn = Theme.MakeButton("", (s, e) => { _m.SetKey(_m.KeyRootNow, _m.KeyScaleNow, !_m.KeyLock); SyncKey(); }, 110);
            _snapBtn = Theme.MakeButton("", (s, e) => { _snap = !_snap; _keyRoot = -1; SyncKey(); }, 84);
            keyBar.Children.Add(_rootBtn);
            keyBar.Children.Add(_scaleBtn);
            keyBar.Children.Add(_lockBtn);
            keyBar.Children.Add(_snapBtn);
            keyBar.Children.Add(Theme.MakeButton("Fit to key", (s, e) => { _melody.FitToKey(); _keyRoot = -1; SyncKey(); }, 84));
            _keyNote = new TextBlock { Foreground = Theme.DimInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 11.5 };
            keyBar.Children.Add(_keyNote);
            // Melody tools
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            tools.Children.Add(new TextBlock { Text = "Melody", Foreground = Theme.DimInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
            tools.Children.Add(Theme.MakeButton("Undo", (s, e) => Undo(), 64));
            tools.Children.Add(Theme.MakeButton("Redo", (s, e) => Redo(), 64));
            tools.Children.Add(Theme.MakeButton("Copy", (s, e) => CopyMelody(), 64));
            tools.Children.Add(Theme.MakeButton("Paste", (s, e) => PasteMelody(), 64));
            tools.Children.Add(Theme.MakeButton("Clear", (s, e) =>
            {
                CurrentLine(out int idx, out _);
                _toolNote.Text = SetMelody("") ? $"Cleared the melody of line {idx + 1}." : $"Line {idx + 1} has no melody to clear.";
            }, 64));
            _hearBtn = Theme.MakeButton(_hear ? "Hear: On" : "Hear: Off", (s, e) => { _hear = !_hear; _hearBtn.Content = _hear ? "Hear: On" : "Hear: Off"; }, 84);
            tools.Children.Add(_hearBtn);
            _toolNote = new TextBlock { Text = "Shift + drag moves the whole line up or down.", Foreground = Theme.DimInk, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), FontSize = 11.5 };
            tools.Children.Add(_toolNote);
            var bars = new StackPanel();
            keyBar.Margin = new Thickness(0, 6, 0, 4);
            bars.Children.Add(keyBar);
            bars.Children.Add(tools);
            Grid.SetRow(bars, 3);
            grid.Children.Add(bars);

            Grid.SetRow(_melody.View, 4);
            grid.Children.Add(_melody.View);
            root.Children.Add(grid);                   // last child fills the remaining space

            _melody.Changed += () =>
            {
                if (_shownLine < 0) return;
                while (_scores.Count <= _shownLine) _scores.Add("");
                _scores[_shownLine] = _melody.Score.IsDefault ? "" : _melody.Score.ToText();
                Record();
                _keyRoot = -1; SyncKey();              // the count of notes outside the key may have changed
            };
            _melody.Heard += (k, pitch) => { if (_hear && _shownU != null) _m.AuditionSyllable(_shownU, k, pitch); };
            _scores.AddRange(machine.GetScores());
            _links.AddRange(machine.GetLinks());
            _snapshot = _scores.ToArray();

            _box.SelectionChanged += (s, e) => UpdatePreview();
            _lastLines = AllLines();
            _box.TextChanged += (s, e) => { _rhymes = null; RealignScores(); UpdatePreview(); };

            // Marker and note names follow the machine while the window is open
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (s, e) =>
            {
                SyncKey();                             // follows the played note
                _melody.SetPlayhead(_m.LineProgress(_shownPhonetic));
            };
            _timer.Start();
            Closed += (s, e) => _timer.Stop();

            Content = root;
            Loaded += (s, e) => { _box.Focus(); UpdatePreview(); };
        }

        static readonly string[] Octaved = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };

        // Bring the grid and the key bar in line with the machine: its Root and Scale, and the last note played
        void SyncKey()
        {
            int root = _m.KeyRootNow, scale = _m.KeyScaleNow, note = _m.LastNoteNumber;
            bool locked = _m.KeyLock;
            if (root == _keyRoot && scale == _keyScale && note == _keyBase && locked == _keyLockShown) return;
            _keyRoot = root; _keyScale = scale; _keyBase = note; _keyLockShown = locked;
            // Locked, the melody is read as drawn from the key's root, so the grid shows it from the root at or below the played note
            int shown = locked ? note - (((note - root) % 12) + 12) % 12 : note;
            _melody.BaseNote = shown;
            _melody.KeyRoot = root; _melody.KeyMask = _m.KeyMask; _melody.Snap = _snap;
            _melody.Redraw();

            var roots = PedalRoboSingMachine.RootNames; var scales = PedalRoboSingMachine.ScaleNames;
            _rootBtn.Content = roots[Math.Clamp(root, 0, roots.Length - 1)] + " \u25BE";
            _scaleBtn.Content = scales[Math.Clamp(scale, 0, scales.Length - 1)] + " \u25BE";
            _snapBtn.Content = _snap ? "Snap: On" : "Snap: Off";
            _lockBtn.Content = locked ? "Key Lock: On" : "Key Lock: Off";
            int outside = _melody.OutOfKey;
            string Name(int n) => n >= 0 && n < 120 ? Octaved[n % 12] + (n / 12) : "?";
            string count = outside == 0 ? "All notes in key" : outside == 1 ? "1 note outside the key" : outside + " notes outside the key";
            _keyNote.Text = scale == 0 ? "Chromatic: every note is allowed."
                          : locked ? $"{count}. Shown from {Name(shown)}; started on another note it moves by scale steps."
                          : $"{count}, for a line started on {Name(note)}.";
        }

        // Line under the caret, worked out from the text so it doesn't depend on layout
        void CurrentLine(out int index, out string text)
        {
            string all = _box.Text ?? "";
            int caret = Math.Clamp(_box.CaretIndex, 0, all.Length);
            int start = all.LastIndexOf('\n', Math.Max(0, caret - 1));
            if (caret == 0) start = -1;
            index = 0;
            for (int i = 0; i < caret && i < all.Length; i++) if (all[i] == '\n') index++;
            int end = all.IndexOf('\n', caret);
            if (end < 0) end = all.Length;
            text = all.Substring(start + 1, end - start - 1).TrimEnd('\r');
        }

        void UpdatePreview()
        {
            try
            {
                CurrentLine(out int idx, out string line);
                if (idx >= PhraseBank.MaxPhrases)
                {
                    _preview.Text = $"Line {idx + 1}: beyond the {PhraseBank.MaxPhrases}-phrase limit, ignored.";
                    return;
                }
                _rhymes ??= Planner.FindRhymes(Lines());        // against every line in the box
                var u = Planner.Build(line, SpeakMode.Speak, _rhymes);
                ShowMelody(idx, line, u);
                _preview.Text = (Lexicon.Count == 0 ? "Pronouncing dictionary " + Lexicon.Status + ". Words are going by the spelling rules only.\n" : "")
                              + $"Phrase {idx + 1}: " + (u.Phonetic.Length > 0 ? u.Phonetic : "(silent)")
                              + (u.SoundNotes.Length > 0 ? "\n" + u.SoundNotes : "")
                              + (u.WordCount > 0 ? $"    [{u.WordCount} words, {u.SylS.Length} syllables, {u.PhS.Length} phonemes, {u.Length / 1000f:0.0} s]" : "")
                              + SongNote(idx);
            }
            catch (Exception ex)
            {
                _preview.Text = "Preview failed: " + ex.Message;
            }
        }

        // Every line of the box, including blank ones, so indexes match the caret's line number
        List<string> AllLines()
        {
            var lines = new List<string>((_box.Text ?? "").Split('\n'));
            for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].TrimEnd('\r');
            return lines;
        }

        // Melodies are kept per line. When lines are inserted or removed, each line that
        // still has the same words takes its melody with it.
        void RealignScores()
        {
            var now = AllLines();
            if (now.Count != _lastLines.Count)
            {
                var byText = new Dictionary<string, (string score, string link)>();
                for (int i = 0; i < _lastLines.Count && i < _scores.Count; i++)
                {
                    string link = i < _links.Count ? _links[i] : "";
                    if ((_scores[i].Length > 0 || link.Length > 0) && !byText.ContainsKey(_lastLines[i])) byText[_lastLines[i]] = (_scores[i], link);
                }
                _scores.Clear(); _links.Clear();
                foreach (var line in now)
                {
                    bool had = byText.TryGetValue(line, out var kept);
                    _scores.Add(had ? kept.score : ""); _links.Add(had ? kept.link : "");
                }
            }
            if (now.Count != _lastLines.Count)
            {
                _undo.Clear(); _redo.Clear();          // the steps were recorded by line number, which has just changed
                _snapshot = _scores.ToArray();
            }
            _lastLines = now;
            _shownLine = -1;                       // force the melody panel to reload
        }

        void ShowMelody(int idx, string line, Utterance u)
        {
            if (idx == _shownLine && line == _shownText) return;
            _shownLine = idx; _shownText = line; _shownPhonetic = u.Phonetic; _shownU = u;
            _melody.BaseNote = _m.LastNoteNumber;
            while (_scores.Count <= idx) _scores.Add("");
            _melody.Load(u.SylText, LineScore.Parse(_scores[idx], u.SylS.Length), _m.TicksPerBeat);
            _keyRoot = -1; SyncKey();
        }

        // ── Melody tools ─────────────────────────────────────────────────────
        // Call after _scores has changed: remembers how it was before
        void Record()
        {
            _undo.Add(_snapshot);
            if (_undo.Count > 200) _undo.RemoveAt(0);
            _redo.Clear();
            _snapshot = _scores.ToArray();
        }

        void Restore(string[] scores)
        {
            _scores.Clear(); _scores.AddRange(scores);
            _snapshot = _scores.ToArray();
            _shownLine = -1;
            UpdatePreview();
        }

        void Undo()
        {
            _toolNote.Text = _undo.Count == 0 ? "Nothing to undo." : "Undone.";
            if (_undo.Count == 0) return;
            _redo.Add(_scores.ToArray());
            var s = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            Restore(s);
        }

        void Redo()
        {
            _toolNote.Text = _redo.Count == 0 ? "Nothing to redo." : "Redone.";
            if (_redo.Count == 0) return;
            _undo.Add(_scores.ToArray());
            var s = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            Restore(s);
        }

        void CopyMelody()
        {
            CurrentLine(out int idx, out string text);
            string score = idx < _scores.Count ? _scores[idx] : "";
            if (score.Length == 0) { _toolNote.Text = $"Line {idx + 1} has no melody drawn yet, so there is nothing to copy."; return; }
            _clip = score; _clipLine = idx + 1; _clipText = text.Trim();
            _toolNote.Text = $"Copied line {_clipLine}. Click in another line (or an empty one), then Paste.";
        }

        static int Steps(string score) => score.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        // Onto the line under the caret: one block per syllable, so a longer melody loses its end and a shorter one is filled out
        void PasteMelody()
        {
            if (string.IsNullOrEmpty(_clip)) { _toolNote.Text = "Nothing copied yet: click in a line's text and press Copy first."; return; }
            CurrentLine(out int idx, out string text);
            // An empty line has no syllables to carry a melody, so it takes the copied words as well
            bool withWords = text.Trim().Length == 0 && _clipText.Length > 0;
            if (withWords) ReplaceCurrentLine(_clipText);
            if (!SetMelody(_clip) && !withWords) { _toolNote.Text = $"Line {idx + 1} already has that melody."; return; }
            int have = _melody.Score.Count, got = Steps(_clip);
            _toolNote.Text = withWords ? $"Pasted line {_clipLine}, words and melody, onto line {idx + 1}."
                           : got > have ? $"Pasted onto line {idx + 1}: {got} notes, {have} syllables, last {got - have} left off."
                           : got < have ? $"Pasted onto line {idx + 1}: {got} notes, {have} syllables, last {have - got} stay plain."
                           : $"Pasted line {_clipLine}'s melody onto line {idx + 1}.";
        }

        // Put words on the line under the caret, leaving the caret at their end
        void ReplaceCurrentLine(string words)
        {
            string all = _box.Text ?? "";
            int caret = Math.Clamp(_box.CaretIndex, 0, all.Length);
            int start = caret == 0 ? -1 : all.LastIndexOf('\n', caret - 1);
            int end = all.IndexOf('\n', caret);
            if (end < 0) end = all.Length;
            if (end > start + 1 && all[end - 1] == '\r') end--;            // keep the line break whole
            _box.Text = all.Substring(0, start + 1) + words + all.Substring(end);
            _box.CaretIndex = start + 1 + words.Length;                   // setting Text sends the caret home
            _lastLines = AllLines();
            _rhymes = null;
        }

        // Returns false when the line already had that melody
        bool SetMelody(string score)
        {
            CurrentLine(out int idx, out _);
            if (idx >= PhraseBank.MaxPhrases) return false;
            while (_scores.Count <= idx) _scores.Add("");
            if (_scores[idx] == score) return false;
            _scores[idx] = score;
            Record();
            _shownLine = -1;
            UpdatePreview();
            return true;
        }

        void SingLine()
        {
            CurrentLine(out int idx, out string line);
            _m.AuditionLine(line, idx < _scores.Count ? _scores[idx] : "");
        }

        List<string> Lines()
        {
            var lines = new List<string>((_box.Text ?? "").Split('\n'));
            for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].TrimEnd('\r');
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > PhraseBank.MaxPhrases) lines.RemoveRange(PhraseBank.MaxPhrases, lines.Count - PhraseBank.MaxPhrases);
            return lines;
        }

        void Apply()
        {
            var lines = Lines();
            var scores = new List<string>();
            var links = new List<string>();
            for (int i = 0; i < lines.Count; i++) { scores.Add(i < _scores.Count ? _scores[i] : ""); links.Add(i < _links.Count ? _links[i] : ""); }
            _m.SetPhrases(lines, scores, links);
        }

        // ── Importing a tune ─────────────────────────────────────────────────
        static string NoteName(int n) => n >= 0 && n < 120 ? Octaved[n % 12] + (n / 12) : "?";

        // What the line knows about its place in an imported song, for the strip under the text
        string SongNote(int idx)
        {
            if (idx >= _links.Count || _links[idx].Length == 0) return "";
            PhraseBank.ParseLink(_links[idx], out int follow, out int from);
            return "\nSong: written from " + NoteName(from) + (follow > 0 ? $"; the next line starts {follow} ticks after this one." : "; the last line of the song.");
        }

        void ImportMidi()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import a tune", Filter = "MIDI files (*.mid;*.midi;*.kar)|*.mid;*.midi;*.kar|All files (*.*)|*.*" };
                if (dlg.ShowDialog(this) != true) return;
                var info = new System.IO.FileInfo(dlg.FileName);
                if (info.Length > MidiFile.MaxBytes) throw new FormatException("That file is too big to be a MIDI file.");
                var parts = MidiFile.Read(System.IO.File.ReadAllBytes(dlg.FileName), out int division, out var tempos);
                parts.RemoveAll(p => p.Notes.Count == 0);
                if (parts.Count == 0) throw new FormatException("There are no notes in that file.");
                if (parts.Count == 1) { Theme.ShowMessage(this, "Import MIDI", ImportNotes(parts[0], division, tempos)); return; }

                // Several parts: ask which one carries the tune
                var names = new string[parts.Count];
                int likely = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    names[i] = $"{(p.Name.Length > 0 ? p.Name : "Track " + (p.Track + 1))}, channel {p.Channel + 1}: {p.Notes.Count} notes{(p.Drums ? " (drums)" : "")}";
                    string n = p.Name.ToLowerInvariant();
                    if (n.Contains("vocal") || n.Contains("voice") || n.Contains("melod") || n.Contains("lead") || n.Contains("sing")) likely = i;
                }
                Theme.ShowChoices(_importBtn, names, likely, i => Theme.ShowMessage(this, "Import MIDI", ImportNotes(parts[i], division, tempos)), 360);
            }
            catch (Exception ex)
            {
                Theme.ShowMessage(this, "Import MIDI", "Could not import: " + ex.Message);
            }
        }

        /// <summary>Fit one part's notes to the typed lines, apply the result to the machine, and return a report.</summary>
        // The file's tempo, for the top of the report: the song has to run at it for the timing to match
        string TempoLine(List<MidiTempo> tempos)
        {
            static string B(double bpm) => Math.Abs(bpm - Math.Round(bpm)) < 0.05 ? Math.Round(bpm).ToString("0") : bpm.ToString("0.0");
            int song = _m.BeatsPerMin;
            string line;
            double first;
            if (tempos == null || tempos.Count == 0) { first = 120; line = "BPM: the file gives no tempo (MIDI's default is 120)."; }
            else
            {
                first = tempos[0].Bpm;
                double lo = first, hi = first; int changes = 0;
                for (int i = 1; i < tempos.Count; i++)
                {
                    if (Math.Abs(tempos[i].Bpm - tempos[i - 1].Bpm) > 0.05) changes++;
                    lo = Math.Min(lo, tempos[i].Bpm); hi = Math.Max(hi, tempos[i].Bpm);
                }
                line = changes == 0 ? $"BPM: {B(first)}."
                     : $"BPM: {B(first)} at the start, then {changes} tempo change{(changes == 1 ? "" : "s")} (between {B(lo)} and {B(hi)}).";
            }
            if (song > 0)
                line += Math.Abs(song - first) < 0.5 ? $" This song is at {song}, which matches." : $" This song is at {song}: set it to {B(first)} to sing at the file's speed.";
            return line + "\n";
        }

        string ImportNotes(MidiPart part, int division, List<MidiTempo> tempos = null)
        {
            var lines = AllLines();
            var syl = new int[Math.Min(lines.Count, PhraseBank.MaxPhrases)];
            int typed = 0;
            for (int i = 0; i < syl.Length; i++)
            {
                syl[i] = lines[i].Trim().Length == 0 ? 0 : Planner.Build(lines[i], SpeakMode.Speak).SylS.Length;
                if (syl[i] > 0) typed++;
            }
            if (typed == 0) return "Type the words first, one line of the song per line, then import the tune onto them.";

            int tpb = _m.TicksPerBeat;
            var r = MelodyImport.Run(part.Notes, division, tpb, syl, _m.KeyRootNow, _m.KeyMask);
            if (r.FirstLine < 0) return "None of the notes could be placed on the lines.";

            while (_scores.Count < lines.Count) _scores.Add("");
            while (_links.Count < lines.Count) _links.Add("");
            var sb = new System.Text.StringBuilder();
            int placed = 0, without = 0;
            for (int i = 0; i < syl.Length; i++)
            {
                var il = r.Lines[i];
                if (syl[i] == 0) continue;
                if (il == null) { _scores[i] = ""; _links[i] = ""; without++; sb.Append($"\n  Line {i + 1}: no notes left for it."); continue; }
                _scores[i] = il.Score; _links[i] = il.Follow + "," + il.Base;
                placed++;
                sb.Append($"\n  Line {i + 1}: {il.Notes} note{(il.Notes == 1 ? "" : "s")} on {il.Syllables} syllable{(il.Syllables == 1 ? "" : "s")}");
                if (il.Tied > 0) sb.Append($", {il.Tied} tied onto the syllable before");
                if (il.Split > 0) sb.Append($", {il.Split} made by splitting a long note");
                sb.Append($"; from {NoteName(il.Base)} at tick {il.StartTick}.");
            }
            if (r.KeyChanged) _m.SetKey(r.KeyRoot, r.KeyScale, _m.KeyLock);
            _undo.Clear(); _redo.Clear(); _snapshot = _scores.ToArray();
            Apply();
            _shownLine = -1; _keyRoot = -1;
            UpdatePreview();

            var first = r.Lines[r.FirstLine];
            string key = PedalRoboSingMachine.RootNames[_m.KeyRootNow] + " " + PedalRoboSingMachine.ScaleNames[_m.KeyScaleNow];
            string head = TempoLine(tempos) +
                $"Imported {r.UsedNotes} of {r.TotalNotes} notes onto {placed} line{(placed == 1 ? "" : "s")}" + (without > 0 ? $" ({without} left without notes)" : "") + ".\n" +
                (r.KeyChanged ? $"Key set to {key}.\n" : $"The tune fits the key already set ({key}).\n") +
                $"\nTO PLAY IT\nPut one note, {NoteName(first.Base)}, with Phrase {r.FirstLine + 1}, in the pattern, and play the song. " +
                $"In the file the singing starts at tick {first.StartTick} (at {tpb} ticks per beat), so that row lines it up with the rest of the file's music. " +
                "That one note sings every line in time. A note with a later Phrase number starts from that line. A note-off lets the current line finish and stops there.\n" +
                "It only runs on while the song is playing: with the song stopped, a note sings its one line. Stop in this window silences everything.\n" +
                "Play it from another note to move the whole song.\n" +
                "\nThe import is already applied. Check each line in the grid: cut, join and drag to put right anything it guessed wrong.\n" +
                "\nLINES";
            return head + sb;
        }

        void SpeakLine()
        {
            CurrentLine(out _, out string line);
            _m.Audition(line);
        }
    }
}
