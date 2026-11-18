// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — melody editor for one line.
//
// One block per syllable on a piano-roll grid: time runs left to right in ticks,
// pitch up and down in semitones around the note that starts the line.
//   drag a block up or down        change its pitch
//   drag a block left or right     change the silence before it
//   drag a block's right edge      change its length
// With a key set (root and scale), rows outside it are dimmed, blocks outside it
// are tinted, and a block dragged up or down lands only on notes in the key.
// A fixed strip on the left names the notes; a marker shows where the voice is
// while the line sings. Built from plain WPF elements in code (no XAML).

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PedalRoboSing.Speech;

namespace PedalRoboSing
{
    sealed class MelodyEditor
    {
        const double EdgeGrab = 8, GutterW = 46;
        const int Range = 12;                       // semitones shown above and below the starting note
        const int Rows = 2 * Range + 1;
        static readonly double[] TickSizes = { 8, 12, 18, 26, 36, 48 };
        static readonly double[] RowSizes = { 12, 14, 16, 19, 22, 26 };
        static readonly string[] NoteNames = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };

        public readonly ScrollViewer View;
        public event Action Changed;

        /// <summary>Semitone number of the note the line starts on (C-0 = 0), for the note names. -1 = unknown.</summary>
        public int BaseNote = -1;

        /// <summary>The key: root as a pitch class (C = 0) and a 12-bit mask, bit n = semitone n above the root. 0xFFF = no key.</summary>
        public int KeyRoot, KeyMask = 0xFFF;
        /// <summary>Dragging a block up or down lands only on notes in the key.</summary>
        public bool Snap = true;

        readonly Canvas _canvas, _gutter;
        readonly Border _playhead;
        string[] _labels = Array.Empty<string>();
        LineScore _score = new LineScore(0);
        int _ticksPerBeat = 4, _zoom = 2;
        double _playTicks = -1;
        double PxTick => TickSizes[_zoom];
        double RowH => RowSizes[_zoom];

        // Drag state
        int _drag = -1;
        bool _resize;
        double _startX, _startY;
        int _startGap, _startLen, _startPitch;

        static Brush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        static readonly Brush Back = Rgb(0x2E, 0x2E, 0x2E), SharpRow = Rgb(0x25, 0x25, 0x25), RootRow = Rgb(0x3A, 0x40, 0x48);
        static readonly Brush RowLine = Rgb(0x38, 0x38, 0x38), BeatLine = Rgb(0x4A, 0x4A, 0x4A), BarLine = Rgb(0x70, 0x70, 0x70);
        static readonly Brush Block = Rgb(0x4F, 0x9D, 0xD9), BlockEdge = Rgb(0xC8, 0xE4, 0xFA), BlockDrag = Rgb(0xF0, 0xA0, 0x40);
        static readonly Brush LabelInk = Rgb(0x08, 0x08, 0x08), GutterInk = Rgb(0xB0, 0xB0, 0xB0), GutterRoot = Rgb(0xFF, 0xFF, 0xFF);
        static readonly Brush GutterBack = Rgb(0x22, 0x22, 0x22), PlayInk = Rgb(0xFF, 0xB0, 0x30);
        static readonly Brush OutRow = Rgb(0x21, 0x21, 0x21), GutterOut = Rgb(0x6A, 0x6A, 0x6A), GutterKey = Rgb(0x8C, 0xC8, 0xF4);
        static readonly Brush BlockOut = Rgb(0xC0, 0x78, 0x78), BlockOutEdge = Rgb(0xF4, 0xC8, 0xC8);

        public MelodyEditor()
        {
            // Top-left, not stretched: an element with a fixed size is otherwise centred in its
            // space, which pushed the grid down and the note names out of line with their rows.
            _canvas = new Canvas { Background = Back, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            _gutter = new Canvas { Width = GutterW, Background = GutterBack, VerticalAlignment = VerticalAlignment.Top };
            _playhead = new Border { Width = 2, Background = PlayInk, IsHitTestVisible = false };

            var across = new ScrollViewer                    // time scrolls; the note names stay put
            {
                Content = _canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var row = new DockPanel { VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(_gutter, Dock.Left);
            row.Children.Add(_gutter);
            row.Children.Add(across);
            View = new ScrollViewer                          // pitch scrolls both together
            {
                Content = row,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Redraw();
        }

        public LineScore Score => _score;

        public void Load(string[] labels, LineScore score, int ticksPerBeat)
        {
            _labels = labels ?? Array.Empty<string>();
            _score = score ?? new LineScore(_labels.Length);
            _ticksPerBeat = Math.Max(1, ticksPerBeat);
            _drag = -1;
            Redraw();
        }

        public void Zoom(int step)
        {
            int z = Math.Clamp(_zoom + step, 0, TickSizes.Length - 1);
            if (z == _zoom) return;
            _zoom = z;
            Redraw();
        }

        /// <summary>Show where the voice is, in ticks from the start of the line; negative hides the marker.</summary>
        public void SetPlayhead(double ticks)
        {
            if (Math.Abs(ticks - _playTicks) < 0.01) return;
            _playTicks = ticks;
            Canvas.SetLeft(_playhead, ticks < 0 ? -10 : ticks * PxTick);
        }

        // ── Geometry ─────────────────────────────────────────────────────────
        int StartTick(int k)
        {
            int t = 0;
            for (int i = 0; i < k; i++) t += _score.Gap[i] + _score.Len[i];
            return t + _score.Gap[k];
        }

        // ── Key ──────────────────────────────────────────────────────────────
        bool HasKey => BaseNote >= 0 && (KeyMask & 0xFFF) != 0xFFF && (KeyMask & 0xFFF) != 0;

        bool InKey(int pitch) => !HasKey || ((KeyMask >> ((((BaseNote + pitch - KeyRoot) % 12) + 12) % 12)) & 1) != 0;

        // The closest pitch in the key; when two are equally close, the one in the direction of travel
        int NearestInKey(int pitch, int direction)
        {
            if (InKey(pitch)) return pitch;
            int sign = direction < 0 ? -1 : 1;
            for (int d = 1; d < 12; d++)
            {
                int a = pitch + sign * d, b = pitch - sign * d;
                if (a >= -Range && a <= Range && InKey(a)) return a;
                if (b >= -Range && b <= Range && InKey(b)) return b;
            }
            return pitch;
        }

        /// <summary>Move every block that is outside the key to the nearest note in it. Returns how many moved.</summary>
        public int FitToKey()
        {
            int moved = 0;
            for (int k = 0; k < _score.Count; k++)
            {
                int p = NearestInKey(_score.Pitch[k], -1);
                if (p != _score.Pitch[k]) { _score.Pitch[k] = p; moved++; }
            }
            if (moved > 0) { Redraw(); Changed?.Invoke(); }
            return moved;
        }

        /// <summary>How many blocks of the shown line are outside the key.</summary>
        public int OutOfKey
        {
            get { int n = 0; for (int k = 0; k < _score.Count; k++) if (!InKey(_score.Pitch[k])) n++; return n; }
        }

        double RowY(int pitch) => (Range - Math.Clamp(pitch, -Range, Range)) * RowH;

        int HitTest(double x, double y, out bool edge)
        {
            edge = false;
            for (int k = _score.Count - 1; k >= 0; k--)
            {
                double x0 = StartTick(k) * PxTick, x1 = x0 + _score.Len[k] * PxTick, y0 = RowY(_score.Pitch[k]);
                if (x < x0 || x > x1 || y < y0 || y > y0 + RowH) continue;
                edge = x >= x1 - Math.Min(EdgeGrab, (x1 - x0) / 3);
                return k;
            }
            return -1;
        }

        // ── Mouse ────────────────────────────────────────────────────────────
        void OnDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(_canvas);
            int k = HitTest(p.X, p.Y, out bool edge);
            if (k < 0) return;
            _drag = k; _resize = edge;
            _startX = p.X; _startY = p.Y;
            _startGap = _score.Gap[k]; _startLen = _score.Len[k]; _startPitch = _score.Pitch[k];
            _canvas.CaptureMouse();
            Redraw();
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (_drag < 0) return;
            Point p = e.GetPosition(_canvas);
            int dTicks = (int)Math.Round((p.X - _startX) / PxTick);
            int dRows = (int)Math.Round((_startY - p.Y) / RowH);
            int gap = _score.Gap[_drag], len = _score.Len[_drag], pitch = _score.Pitch[_drag];
            if (_resize)
                len = Math.Clamp(_startLen + dTicks, 1, LineScore.MaxLen);
            else
            {
                gap = Math.Clamp(_startGap + dTicks, 0, LineScore.MaxGap);
                pitch = Math.Clamp(_startPitch + dRows, -Range, Range);
                if (Snap && dRows != 0) pitch = NearestInKey(pitch, dRows);      // a sideways drag leaves the pitch alone
            }
            if (gap == _score.Gap[_drag] && len == _score.Len[_drag] && pitch == _score.Pitch[_drag]) return;
            _score.Gap[_drag] = gap; _score.Len[_drag] = len; _score.Pitch[_drag] = pitch;
            Redraw();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag < 0) return;
            _drag = -1;
            _canvas.ReleaseMouseCapture();
            Redraw();
            Changed?.Invoke();
        }

        // ── Drawing ──────────────────────────────────────────────────────────
        static void Put(Canvas c, FrameworkElement el, double x, double y)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            c.Children.Add(el);
        }

        public void Redraw()
        {
            int total = Math.Max(_score.TotalTicks + 8 * _ticksPerBeat, 16 * _ticksPerBeat);
            double w = total * PxTick, h = Rows * RowH, font = Math.Max(8, RowH - 5);
            _canvas.Width = w; _canvas.Height = h; _gutter.Height = h;
            _canvas.Children.Clear();
            _gutter.Children.Clear();

            for (int r = 0; r < Rows; r++)
            {
                int pitch = Range - r;
                int note = BaseNote >= 0 ? BaseNote + pitch : -1;
                int pc = BaseNote >= 0 ? ((note % 12) + 12) % 12 : -1;
                bool sharp = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                bool key = HasKey, inKey = InKey(pitch), keyRoot = key && pc == KeyRoot;
                // With a key: rows outside it are dark. Without one: the sharps, piano-roll style.
                bool dim = key ? !inKey : sharp;
                if (pitch == 0 || dim)
                    Put(_canvas, new Border { Width = w, Height = RowH, Background = pitch == 0 ? RootRow : key ? OutRow : SharpRow, IsHitTestVisible = false }, 0, r * RowH);
                if (keyRoot)
                    Put(_gutter, new Border { Width = 3, Height = RowH - 1, Background = GutterKey, IsHitTestVisible = false }, 0, r * RowH + 1);
                Put(_canvas, new Border { Width = w, Height = 1, Background = RowLine, IsHitTestVisible = false }, 0, r * RowH);

                string name = note >= 0 && note < 120 ? NoteNames[pc] + (note / 12)
                            : pitch == 0 ? "0" : (pitch > 0 ? "+" + pitch : pitch.ToString());
                Put(_gutter, new TextBlock
                {
                    Text = name, FontSize = font, IsHitTestVisible = false,
                    Foreground = pitch == 0 ? GutterRoot : keyRoot ? GutterKey : key && !inKey ? GutterOut : GutterInk,
                }, 5, r * RowH - 1);
            }
            for (int t = 0; t <= total; t += _ticksPerBeat)
            {
                bool bar = t % (_ticksPerBeat * 4) == 0;
                Put(_canvas, new Border { Width = 1, Height = h, Background = bar ? BarLine : BeatLine, IsHitTestVisible = false }, t * PxTick, 0);
            }

            for (int k = 0; k < _score.Count; k++)
            {
                var label = new TextBlock
                {
                    Text = k < _labels.Length ? _labels[k] : "?",
                    FontSize = font, Foreground = LabelInk, IsHitTestVisible = false,
                    Margin = new Thickness(3, -2, 0, 0),
                };
                Put(_canvas, new Border
                {
                    Width = Math.Max(4, _score.Len[k] * PxTick - 1), Height = RowH - 1,
                    Background = k == _drag ? BlockDrag : InKey(_score.Pitch[k]) ? Block : BlockOut,
                    BorderBrush = InKey(_score.Pitch[k]) ? BlockEdge : BlockOutEdge, BorderThickness = new Thickness(0, 0, 3, 0),
                    CornerRadius = new CornerRadius(2), Child = label, IsHitTestVisible = false,
                }, StartTick(k) * PxTick, RowY(_score.Pitch[k]) + 1);
            }

            _playhead.Height = h;
            Put(_canvas, _playhead, _playTicks < 0 ? -10 : _playTicks * PxTick, 0);
        }
    }
}
