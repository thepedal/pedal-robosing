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
        List<string> _lastLines = new List<string>();
        int _shownLine = -1;
        string _shownText;

        string _shownPhonetic;

        System.Windows.Threading.DispatcherTimer _timer;

        const string Intro =
            "One phrase per line (line 1 is Phrase 1). The grid shows the melody of the line under the caret: drag a block up or down for pitch, " +
            "sideways for the silence before it, and its right edge for length. One note in the pattern with the line's Phrase number sings it; presets and parameters change how it sounds.";

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
            "Sing line plays it from the last note played on track 1. The orange marker shows where the voice is.\n" +
            "Drag the bar between the text and the grid to resize them.\n\n" +
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
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Theme.MakeButton("Sing line", (s, e) => SingLine()));
            buttons.Children.Add(Theme.MakeButton("Speak line", (s, e) => SpeakLine()));
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

            Grid.SetRow(_melody.View, 3);
            grid.Children.Add(_melody.View);
            root.Children.Add(grid);                   // last child fills the remaining space

            _melody.Changed += () =>
            {
                if (_shownLine < 0) return;
                while (_scores.Count <= _shownLine) _scores.Add("");
                _scores[_shownLine] = _melody.Score.IsDefault ? "" : _melody.Score.ToText();
            };
            _scores.AddRange(machine.GetScores());

            _box.SelectionChanged += (s, e) => UpdatePreview();
            _lastLines = AllLines();
            _box.TextChanged += (s, e) => { _rhymes = null; RealignScores(); UpdatePreview(); };

            // Marker and note names follow the machine while the window is open
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (s, e) =>
            {
                int note = _m.LastNoteNumber;
                if (note != _melody.BaseNote) { _melody.BaseNote = note; _melody.Redraw(); }
                _melody.SetPlayhead(_m.LineProgress(_shownPhonetic));
            };
            _timer.Start();
            Closed += (s, e) => _timer.Stop();

            Content = root;
            Loaded += (s, e) => { _box.Focus(); UpdatePreview(); };
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
                _preview.Text = $"Phrase {idx + 1}: " + (u.Phonetic.Length > 0 ? u.Phonetic : "(silent)")
                              + (u.SoundNotes.Length > 0 ? "\n" + u.SoundNotes : "")
                              + (u.WordCount > 0 ? $"    [{u.WordCount} words, {u.SylS.Length} syllables, {u.PhS.Length} phonemes, {u.Length / 1000f:0.0} s]" : "");
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
                var byText = new Dictionary<string, string>();
                for (int i = 0; i < _lastLines.Count && i < _scores.Count; i++)
                    if (_scores[i].Length > 0 && !byText.ContainsKey(_lastLines[i])) byText[_lastLines[i]] = _scores[i];
                _scores.Clear();
                foreach (var line in now) _scores.Add(byText.TryGetValue(line, out var sc) ? sc : "");
            }
            _lastLines = now;
            _shownLine = -1;                       // force the melody panel to reload
        }

        void ShowMelody(int idx, string line, Utterance u)
        {
            if (idx == _shownLine && line == _shownText) return;
            _shownLine = idx; _shownText = line; _shownPhonetic = u.Phonetic;
            _melody.BaseNote = _m.LastNoteNumber;
            while (_scores.Count <= idx) _scores.Add("");
            _melody.Load(u.SylText, LineScore.Parse(_scores[idx], u.SylS.Length), _m.TicksPerBeat);
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
            for (int i = 0; i < lines.Count; i++) scores.Add(i < _scores.Count ? _scores[i] : "");
            _m.SetPhrases(lines, scores);
        }

        void SpeakLine()
        {
            CurrentLine(out _, out string line);
            _m.Audition(line);
        }
    }
}
