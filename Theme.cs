// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — the dark look shared by the machine's windows, to sit with ReBuzz.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PedalRoboSing
{
    static class Theme
    {
        public static Brush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        public static readonly Brush WinBack = Rgb(0x2B, 0x2B, 0x2B), FieldBack = Rgb(0x1E, 0x1E, 0x1E), Edge = Rgb(0x55, 0x55, 0x55);
        public static readonly Brush Ink = Rgb(0xE6, 0xE6, 0xE6), DimInk = Rgb(0xA8, 0xA8, 0xA8), ButtonBack = Rgb(0x3C, 0x3C, 0x3C);
        public static readonly Brush Selection = Rgb(0x2A, 0x6E, 0xA8), Divider = Rgb(0x4A, 0x4A, 0x4A);

        // The stock button template lights up pale blue under the mouse, which makes
        // light text unreadable, so buttons get a small template of their own. It is
        // parsed from text at run time (no compiled XAML); if that ever fails the
        // buttons keep the stock template with dark colours.
        const string ButtonXaml =
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
            "<Setter Property='Foreground' Value='#E6E6E6'/><Setter Property='Background' Value='#3C3C3C'/><Setter Property='BorderBrush' Value='#606060'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>" +
            "<Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='2' Padding='{TemplateBinding Padding}'>" +
            "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#505050'/></Trigger>" +
            "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#4F9DD9'/></Trigger>" +
            "<Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#2A6EA8'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
        static Style _buttonStyle;
        static bool _buttonStyleTried;

        public static Button MakeButton(string text, RoutedEventHandler onClick, double minWidth = 84, double gap = 6)
        {
            var b = new Button
            {
                Content = text,
                MinWidth = minWidth,
                Margin = new Thickness(gap, 0, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
            };
            if (!_buttonStyleTried)
            {
                _buttonStyleTried = true;
                try { _buttonStyle = System.Windows.Markup.XamlReader.Parse(ButtonXaml) as Style; } catch { _buttonStyle = null; }
            }
            if (_buttonStyle != null) b.Style = _buttonStyle;
            else { b.Background = ButtonBack; b.Foreground = Ink; b.BorderBrush = Edge; }
            if (onClick != null) b.Click += onClick;
            return b;
        }

        // Scroll bars: the stock ones stay light whatever the window colours are, so they
        // get a slim dark template too, for both directions. Same run-time parse and the
        // same fallback: if it fails, the stock scroll bars remain.
        const string ScrollXaml =
            "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
            "<Style x:Key='RsPage' TargetType='RepeatButton'><Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style x:Key='RsThumb' TargetType='Thumb'><Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>" +
            "<Border x:Name='t' Background='#5C5C5C' CornerRadius='3' Margin='2'/>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#7A7A7A'/></Trigger>" +
            "<Trigger Property='IsDragging' Value='True'><Setter TargetName='t' Property='Background' Value='#2A6EA8'/></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>" +
            "<Style x:Key='{x:Type ScrollBar}' TargetType='ScrollBar'>" +
            "<Setter Property='Background' Value='#262626'/><Setter Property='Width' Value='14'/><Setter Property='MinWidth' Value='14'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
            "<Border Background='{TemplateBinding Background}'><Track x:Name='PART_Track' IsDirectionReversed='True'>" +
            "<Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource RsPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb Style='{StaticResource RsThumb}'/></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource RsPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>" +
            "</Track></Border></ControlTemplate></Setter.Value></Setter>" +
            "<Style.Triggers><Trigger Property='Orientation' Value='Horizontal'>" +
            "<Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='14'/><Setter Property='MinHeight' Value='14'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
            "<Border Background='{TemplateBinding Background}'><Track x:Name='PART_Track' IsDirectionReversed='False'>" +
            "<Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource RsPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb Style='{StaticResource RsThumb}'/></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource RsPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>" +
            "</Track></Border></ControlTemplate></Setter.Value></Setter>" +
            "</Trigger></Style.Triggers></Style>" +
            "</ResourceDictionary>";

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Dark background, and a dark title bar where Windows offers one (10 version 2004 and later).</summary>
        public static void Apply(Window w)
        {
            w.Background = WinBack;
            try
            {
                // Parsed per window: a ResourceDictionary can be merged into one owner only.
                if (System.Windows.Markup.XamlReader.Parse(ScrollXaml) is ResourceDictionary bars) w.Resources.MergedDictionaries.Add(bars);
            }
            catch { }
            w.SourceInitialized += (s, e) =>
            {
                try
                {
                    IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    int on = 1;
                    if (h != IntPtr.Zero) DwmSetWindowAttribute(h, 20, ref on, sizeof(int));
                }
                catch { }
            };
        }

        /// <summary>A dark replacement for MessageBox.Show(text, title) with an OK button.</summary>
        public static void ShowMessage(Window owner, string title, string text)
        {
            var w = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            Apply(w);
            try { w.Owner = owner ?? Application.Current?.MainWindow; if (w.Owner != null) w.WindowStartupLocation = WindowStartupLocation.CenterOwner; } catch { }

            var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Foreground = Ink, FontSize = 12.5 });
            var ok = MakeButton("OK", (s, e) => w.Close(), 84, 0);
            ok.IsDefault = true; ok.IsCancel = true;
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            ok.Margin = new Thickness(0, 16, 0, 0);
            panel.Children.Add(ok);
            w.Content = panel;
            w.ShowDialog();
        }
    }
}
