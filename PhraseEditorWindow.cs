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

        const string Help =
            "One phrase per line: line 1 is Phrase 1, line 2 is Phrase 2, and so on (up to 100).\n" +
            "The strip below shows the syllables (split by \u00B7) that Syllable mode steps through, one per note.\n" +
            "Punctuation shapes the delivery:  ,  short pause   .  falling end   ?  rising end   !  bigger accents.\n" +
            "Fix a mispronounced word with phonemes in brackets, e.g.  [R IY B AH1 Z]  (1 marks the stressed vowel, _ is a pause).\n" +
            "Vowels: IY IH EY EH AE AA AO OW UH UW AH AX ER AY AW OY   Consonants: P B T D K G CH JH F V TH DH S Z SH ZH HH M N NG L R W Y WH\n" +
            "Melody (Play Mode: Line): each block is a syllable of the line under the caret. Drag it up or down for pitch, sideways for the silence before it, " +
            "and drag its right edge for length. One note in the pattern then sings the whole line.";

        public PhraseEditorWindow(PedalRoboSingMachine machine)
        {
            _m = machine;
            Title = "Pedal RoboSing - Phrases";
            Width = 720;
            Height = 860;
            MinWidth = 420;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(8) };

            var help = new TextBlock
            {
                Text = Help,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 11.5,
            };
            DockPanel.SetDock(help, Dock.Top);
            root.Children.Add(help);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            buttons.Children.Add(MakeButton("Sing line", (s, e) => SingLine()));
            buttons.Children.Add(MakeButton("Speak line", (s, e) => SpeakLine()));
            buttons.Children.Add(MakeButton("Apply", (s, e) => Apply()));
            buttons.Children.Add(MakeButton("OK", (s, e) => { Apply(); Close(); }));
            buttons.Children.Add(MakeButton("Cancel", (s, e) => Close()));
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _preview = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Brushes.DimGray,
                MinHeight = 48,
            };
            DockPanel.SetDock(_melody.View, Dock.Bottom);
            root.Children.Add(_melody.View);
            _melody.Changed += () =>
            {
                if (_shownLine < 0) return;
                while (_scores.Count <= _shownLine) _scores.Add("");
                _scores[_shownLine] = _melody.Score.IsDefault ? "" : _melody.Score.ToText();
            };
            _scores.AddRange(machine.GetScores());

            DockPanel.SetDock(_preview, Dock.Bottom);
            root.Children.Add(_preview);

            _box = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = string.Join(Environment.NewLine, machine.GetPhrases()),
            };
            _box.SelectionChanged += (s, e) => UpdatePreview();
            _lastLines = AllLines();
            _box.TextChanged += (s, e) => { _rhymes = null; RealignScores(); UpdatePreview(); };
            root.Children.Add(_box);                   // last child fills the remaining space

            Content = root;
            Loaded += (s, e) => { _box.Focus(); UpdatePreview(); };
        }

        static Button MakeButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                MinWidth = 84,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
            };
            b.Click += onClick;
            return b;
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
            _shownLine = idx; _shownText = line;
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
