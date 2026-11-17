// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — melody editor for one line.
//
// One block per syllable on a grid: time runs left to right in ticks, pitch up
// and down in semitones around the note that starts the line.
//   drag a block up or down        change its pitch
//   drag a block left or right     change the silence before it
//   drag a block's right edge      change its length
// Built from plain WPF elements in code (no XAML, no custom drawing).

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
        const double PxTick = 18, RowH = 11, EdgeGrab = 7;
        const int Range = 12;                       // semitones shown above and below the starting note
        const int Rows = 2 * Range + 1;

        public readonly ScrollViewer View;
        public event Action Changed;

        readonly Canvas _canvas;
        string[] _labels = Array.Empty<string>();
        LineScore _score = new LineScore(0);
        int _ticksPerBeat = 4;

        // Drag state
        int _drag = -1;
        bool _resize;
        double _startX, _startY;
        int _startGap, _startLen, _startPitch;

        static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        static readonly Brush RowLine = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        static readonly Brush RootLine = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        static readonly Brush BeatLine = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));
        static readonly Brush BarLine = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
        static readonly Brush Block = new SolidColorBrush(Color.FromRgb(0x4F, 0x9D, 0xD9));
        static readonly Brush BlockEdge = new SolidColorBrush(Color.FromRgb(0xC8, 0xE4, 0xFA));
        static readonly Brush BlockDrag = new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x40));
        static readonly Brush LabelInk = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
        static readonly Brush GutterInk = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));

        public MelodyEditor()
        {
            _canvas = new Canvas { Height = Rows * RowH, Width = 64 * PxTick, Background = Back };
            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            View = new ScrollViewer
            {
                Content = _canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = Rows * RowH + 20,
                Margin = new Thickness(0, 6, 0, 0),
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

        // ── Geometry ─────────────────────────────────────────────────────────
        int StartTick(int k)
        {
            int t = 0;
            for (int i = 0; i < k; i++) t += _score.Gap[i] + _score.Len[i];
            return t + _score.Gap[k];
        }

        static double RowY(int pitch) => (Range - Math.Clamp(pitch, -Range, Range)) * RowH;

        int HitTest(double x, double y, out bool edge)
        {
            edge = false;
            for (int k = _score.Count - 1; k >= 0; k--)
            {
                double x0 = StartTick(k) * PxTick, x1 = x0 + _score.Len[k] * PxTick, y0 = RowY(_score.Pitch[k]);
                if (x < x0 || x > x1 || y < y0 || y > y0 + RowH) continue;
                edge = x >= x1 - EdgeGrab;
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
        void Add(FrameworkElement el, double x, double y)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            _canvas.Children.Add(el);
        }

        void Redraw()
        {
            int total = Math.Max(_score.TotalTicks + 8 * _ticksPerBeat, 16 * _ticksPerBeat);
            double w = total * PxTick, h = Rows * RowH;
            _canvas.Width = w;
            _canvas.Children.Clear();

            for (int r = 0; r <= Rows; r++)
            {
                int pitch = Range - r;
                bool root = pitch == 0 || pitch == -1;          // the two lines framing the root row
                Add(new Border { Width = w, Height = 1, Background = root ? RootLine : RowLine, IsHitTestVisible = false }, 0, r * RowH);
            }
            for (int t = 0; t <= total; t += 1)
            {
                bool beat = t % _ticksPerBeat == 0, bar = t % (_ticksPerBeat * 4) == 0;
                if (!beat) continue;
                Add(new Border { Width = 1, Height = h, Background = bar ? BarLine : BeatLine, IsHitTestVisible = false }, t * PxTick, 0);
            }
            foreach (int pitch in new[] { 12, 7, 0, -5, -12 })
                Add(new TextBlock
                {
                    Text = pitch > 0 ? "+" + pitch : pitch.ToString(),
                    FontSize = 9, Foreground = GutterInk, IsHitTestVisible = false,
                }, 2, RowY(pitch) - 1);

            for (int k = 0; k < _score.Count; k++)
            {
                var label = new TextBlock
                {
                    Text = k < _labels.Length ? _labels[k] : "?",
                    FontSize = 9, Foreground = LabelInk, IsHitTestVisible = false,
                    Margin = new Thickness(3, -2, 0, 0),
                };
                Add(new Border
                {
                    Width = Math.Max(4, _score.Len[k] * PxTick - 1), Height = RowH - 1,
                    Background = k == _drag ? BlockDrag : Block,
                    BorderBrush = BlockEdge, BorderThickness = new Thickness(0, 0, 3, 0),
                    CornerRadius = new CornerRadius(2), Child = label, IsHitTestVisible = false,
                }, StartTick(k) * PxTick, RowY(_score.Pitch[k]) + 1);
            }
        }
    }
}
