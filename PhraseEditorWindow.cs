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

        const string Help =
            "One phrase per line: line 1 is Phrase 1, line 2 is Phrase 2, and so on (up to 100).\n" +
            "The strip below shows the syllables (split by \u00B7) that Syllable mode steps through, one per note.\n" +
            "Punctuation shapes the delivery:  ,  short pause   .  falling end   ?  rising end   !  bigger accents.\n" +
            "Fix a mispronounced word with phonemes in brackets, e.g.  [R IY B AH1 Z]  (1 marks the stressed vowel, _ is a pause).\n" +
            "Vowels: IY IH EY EH AE AA AO OW UH UW AH AX ER AY AW OY   Consonants: P B T D K G CH JH F V TH DH S Z SH ZH HH M N NG L R W Y WH";

        public PhraseEditorWindow(PedalRoboSingMachine machine)
        {
            _m = machine;
            Title = "Pedal RoboSing - Phrases";
            Width = 720;
            Height = 560;
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
                MinHeight = 32,
            };
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
            _box.TextChanged += (s, e) => UpdatePreview();
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
                var u = Planner.Build(line, SpeakMode.Speak);
                _preview.Text = $"Phrase {idx + 1}: " + (u.Phonetic.Length > 0 ? u.Phonetic : "(silent)")
                              + (u.WordCount > 0 ? $"    [{u.WordCount} words, {u.SylS.Length} syllables, {u.PhS.Length} phonemes, {u.Length / 1000f:0.0} s]" : "");
            }
            catch (Exception ex)
            {
                _preview.Text = "Preview failed: " + ex.Message;
            }
        }

        List<string> Lines()
        {
            var lines = new List<string>((_box.Text ?? "").Split('\n'));
            for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].TrimEnd('\r');
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > PhraseBank.MaxPhrases) lines.RemoveRange(PhraseBank.MaxPhrases, lines.Count - PhraseBank.MaxPhrases);
            return lines;
        }

        void Apply() => _m.SetPhrases(Lines());

        void SpeakLine()
        {
            CurrentLine(out _, out string line);
            _m.Audition(line);
        }
    }
}
