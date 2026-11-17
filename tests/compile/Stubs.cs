// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
// Minimal stand-ins for the ReBuzz SDK and WPF types the machine touches, written
// from scratch for a Linux type-check. Shapes follow the public interfaces only.
using System;
using System.Collections.Generic;

namespace Buzz.MachineInterface
{
    [Flags] public enum WorkModes { WM_NOIO = 0, WM_READ = 1, WM_WRITE = 2, WM_READWRITE = 3 }
    [AttributeUsage(AttributeTargets.Class)] public class MachineDecl : Attribute
    { public string Name { get; set; } public string ShortName { get; set; } public string Author { get; set; } public int MaxTracks { get; set; } }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)] public class ParameterDecl : Attribute
    { public string Name { get; set; } public string Description { get; set; } public int MinValue { get; set; } public int MaxValue { get; set; }
      public object DefValue { get; set; } public string[] ValueDescriptions { get; set; } public bool IsStateless { get; set; } }
    public struct Note { public const int Max = 16 * 9 + 12; public const int Off = 255; public byte Value; public Note(int v) { Value = (byte)v; } }
    public struct Sample { public float L, R; }
    public class MasterInfo { public int SamplesPerSec = 44100; public int BeatsPerMin = 120; public int SamplesPerTick = 5512; }
    public interface IBuzzMachine { }
    public interface IBuzzMachineHost { BuzzGUI.Interfaces.IMachine Machine { get; } MasterInfo MasterInfo { get; } }
}
namespace BuzzGUI.Interfaces
{
    public interface IBuzz { bool Playing { get; } }
    public interface IMachineGraph { IBuzz Buzz { get; } }
    public interface IMachine { IMachineGraph Graph { get; } }
    public interface IParameter { string Name { get; } }
    public interface IMenuItem { }
}
namespace BuzzGUI.Common
{
    public class SimpleCommand { public Func<object, bool> CanExecuteDelegate { get; set; } public Action<object> ExecuteDelegate { get; set; } }
    public class MenuItemVM : BuzzGUI.Interfaces.IMenuItem { public string Text { get; set; } public SimpleCommand Command { get; set; } }
}
namespace System.Windows
{
    public delegate void RoutedEventHandler(object sender, EventArgs e);
    public struct Thickness { public Thickness(double u) { } public Thickness(double l, double t, double r, double b) { } }
    public enum TextWrapping { NoWrap, Wrap }
    public enum HorizontalAlignment { Left, Right }
    public enum WindowStartupLocation { Manual, CenterOwner }
    public static class MessageBox { public static void Show(string text, string caption) { } }
    public class Application { public static Application Current => null; public Window MainWindow => null; }
    public class FrameworkElement { public Thickness Margin { get; set; } public double MinWidth { get; set; } public double MinHeight { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; } public event RoutedEventHandler Loaded; public bool Focus() => true; }
    public class Window : FrameworkElement { public string Title { get; set; } public double Width { get; set; } public double Height { get; set; }
        public WindowStartupLocation WindowStartupLocation { get; set; } public bool ShowInTaskbar { get; set; } public object Content { get; set; }
        public Window Owner { get; set; } public event EventHandler Closed; public void Show() { } public bool Activate() => true; public void Close() { } }
}
namespace System.Windows.Media
{
    public class FontFamily { public FontFamily(string n) { } }
    public class Brush { }
    public static class Brushes { public static Brush DimGray => null; }
}
namespace System.Windows.Controls
{
    using System.Windows.Media;
    public enum Dock { Left, Top, Right, Bottom }
    public enum Orientation { Horizontal, Vertical }
    public enum ScrollBarVisibility { Disabled, Auto }
    public class UIElementCollection { public void Add(FrameworkElement e) { } }
    public class Panel : FrameworkElement { public UIElementCollection Children { get; } = new UIElementCollection(); }
    public class DockPanel : Panel { public static void SetDock(FrameworkElement e, Dock d) { } }
    public class StackPanel : Panel { public Orientation Orientation { get; set; } }
    public class TextBlock : FrameworkElement { public string Text { get; set; } public TextWrapping TextWrapping { get; set; } public double FontSize { get; set; }
        public FontFamily FontFamily { get; set; } public Brush Foreground { get; set; } }
    public class Button : FrameworkElement { public object Content { get; set; } public Thickness Padding { get; set; } public event RoutedEventHandler Click; }
    public class TextBox : FrameworkElement { public bool AcceptsReturn { get; set; } public bool AcceptsTab { get; set; } public FontFamily FontFamily { get; set; }
        public double FontSize { get; set; } public TextWrapping TextWrapping { get; set; } public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; } public string Text { get; set; } public int CaretIndex { get; set; }
        public event RoutedEventHandler SelectionChanged; public event EventHandler TextChanged; }
}
