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
    public class MasterInfo { public int SamplesPerSec = 44100; public int BeatsPerMin = 120; public int SamplesPerTick = 5512; public int TicksPerBeat = 4; }
    public interface IBuzzMachine { }
    public interface IBuzzMachineHost { BuzzGUI.Interfaces.IMachine Machine { get; } MasterInfo MasterInfo { get; } }
}
namespace BuzzGUI.Interfaces
{
    public interface IBuzz { bool Playing { get; } }
    public interface IMachineGraph { IBuzz Buzz { get; } }
    public interface IMachine { IMachineGraph Graph { get; } System.Collections.ObjectModel.ReadOnlyCollection<IParameterGroup> ParameterGroups { get; } }
    public interface IParameterGroup { System.Collections.ObjectModel.ReadOnlyCollection<IParameter> Parameters { get; } }
    public interface IParameter { string Name { get; } void SetValue(int track, int value); }
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
    public enum HorizontalAlignment { Left, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public enum GridUnitType { Auto, Pixel, Star }
    public struct GridLength { public GridLength(double v, GridUnitType t) { } public static GridLength Auto => default; }
    public enum WindowStartupLocation { Manual, CenterOwner, CenterScreen }
    public class ResourceDictionary { public System.Collections.Generic.List<ResourceDictionary> MergedDictionaries { get; } = new System.Collections.Generic.List<ResourceDictionary>(); }
    public enum SizeToContent { Manual, Width, Height, WidthAndHeight }
    public enum ResizeMode { NoResize, CanMinimize, CanResize }
    public static class MessageBox { public static void Show(string text, string caption) { } }
    public class Application { public static Application Current => null; public Window MainWindow => null; }
    public struct Point { public double X, Y; }
    public struct CornerRadius { public CornerRadius(double u) { } }
    public class FrameworkElement { public Thickness Margin { get; set; } public double MinWidth { get; set; } public double MinHeight { get; set; } public double MaxWidth { get; set; }
        public double Width { get; set; } public double Height { get; set; } public bool IsHitTestVisible { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; } public VerticalAlignment VerticalAlignment { get; set; } public event RoutedEventHandler Loaded; public bool Focus() => true;
        public event System.Windows.Input.MouseButtonEventHandler MouseLeftButtonDown, MouseLeftButtonUp, MouseRightButtonDown;
        public event System.Windows.Input.MouseEventHandler MouseMove;
        public bool CaptureMouse() => true; public void ReleaseMouseCapture() { } }
    public class Style { }
    public class Window : FrameworkElement { public string Title { get; set; } public System.Windows.Media.Brush Background { get; set; } public event EventHandler SourceInitialized;
        public WindowStartupLocation WindowStartupLocation { get; set; } public bool ShowInTaskbar { get; set; } public object Content { get; set; }
        public Window Owner { get; set; } public event EventHandler Closed; public void Show() { } public bool Activate() => true; public void Close() { }
        public ResourceDictionary Resources { get; } = new ResourceDictionary();
        public SizeToContent SizeToContent { get; set; } public ResizeMode ResizeMode { get; set; } public bool? ShowDialog() => true; }
}
namespace System.Windows.Markup { public static class XamlReader { public static object Parse(string xaml) => null; } }
namespace System.Windows.Interop { public class WindowInteropHelper { public WindowInteropHelper(System.Windows.Window w) { } public IntPtr Handle => IntPtr.Zero; } }
namespace System.Windows.Threading
{
    public class DispatcherTimer { public TimeSpan Interval { get; set; } public event EventHandler Tick; public void Start() { } public void Stop() { } }
}
namespace System.Windows.Input
{
    public class MouseEventArgs : EventArgs { public System.Windows.Point GetPosition(System.Windows.FrameworkElement relativeTo) => default; }
    public class MouseButtonEventArgs : MouseEventArgs { public int ClickCount { get; set; } public bool Handled { get; set; } }
    public delegate void MouseEventHandler(object sender, MouseEventArgs e);
    public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);
}
namespace System.Windows.Media
{
    public struct Color { public static Color FromRgb(byte r, byte g, byte b) => default; }
    public class SolidColorBrush : Brush { public SolidColorBrush(Color c) { } }
    public class FontFamily { public FontFamily(string n) { } }
    public class Brush { }
    public static class Brushes { public static Brush DimGray => null; public static Brush Gray => null; }
}
namespace System.Windows.Controls.Primitives
{
    public enum PlacementMode { Bottom, Top }
    public class Popup : System.Windows.FrameworkElement { public System.Windows.FrameworkElement PlacementTarget { get; set; } public PlacementMode Placement { get; set; }
        public bool StaysOpen { get; set; } public bool IsOpen { get; set; } public System.Windows.FrameworkElement Child { get; set; } }
}
namespace System.Windows.Controls
{
    using System.Windows.Media;
    public enum Dock { Left, Top, Right, Bottom }
    public enum Orientation { Horizontal, Vertical }
    public enum ScrollBarVisibility { Disabled, Auto }
    public class UIElementCollection { public void Add(FrameworkElement e) { } public void Clear() { } }
    public class Panel : FrameworkElement { public UIElementCollection Children { get; } = new UIElementCollection(); public Brush Background { get; set; } }
    public class RowDefinition { public System.Windows.GridLength Height { get; set; } public double MinHeight { get; set; } }
    public class RowDefinitionCollection { public void Add(RowDefinition r) { } }
    public class Grid : Panel { public RowDefinitionCollection RowDefinitions { get; } = new RowDefinitionCollection(); public static void SetRow(System.Windows.FrameworkElement e, int row) { } }
    public enum GridResizeDirection { Auto, Columns, Rows }
    public enum GridResizeBehavior { BasedOnAlignment, CurrentAndNext, PreviousAndCurrent, PreviousAndNext }
    public class GridSplitter : System.Windows.FrameworkElement { public GridResizeDirection ResizeDirection { get; set; } public GridResizeBehavior ResizeBehavior { get; set; } public Brush Background { get; set; } }
    public class Canvas : Panel { public static void SetLeft(FrameworkElement e, double v) { } public static void SetTop(FrameworkElement e, double v) { } }
    public class ScrollViewer : FrameworkElement { public object Content { get; set; } public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; } }
    public class Border : FrameworkElement { public Thickness Padding { get; set; } public Brush Background { get; set; } public Brush BorderBrush { get; set; } public Thickness BorderThickness { get; set; }
        public CornerRadius CornerRadius { get; set; } public FrameworkElement Child { get; set; } }
    public class DockPanel : Panel { public static void SetDock(FrameworkElement e, Dock d) { } }
    public class StackPanel : Panel { public Orientation Orientation { get; set; } }
    public class TextBlock : FrameworkElement { public string Text { get; set; } public TextWrapping TextWrapping { get; set; } public double FontSize { get; set; }
        public FontFamily FontFamily { get; set; } public Brush Foreground { get; set; } }
    public class Button : FrameworkElement { public object Content { get; set; } public Thickness Padding { get; set; } public event RoutedEventHandler Click;
        public Brush Background { get; set; } public Brush Foreground { get; set; } public Brush BorderBrush { get; set; } public System.Windows.Style Style { get; set; } public bool IsDefault { get; set; } public bool IsCancel { get; set; } }
    public class TextBox : FrameworkElement { public bool AcceptsReturn { get; set; } public bool AcceptsTab { get; set; } public FontFamily FontFamily { get; set; }
        public double FontSize { get; set; } public TextWrapping TextWrapping { get; set; } public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; } public string Text { get; set; } public int CaretIndex { get; set; }
        public event RoutedEventHandler SelectionChanged; public event EventHandler TextChanged;
        public Brush Background { get; set; } public Brush Foreground { get; set; } public Brush BorderBrush { get; set; } public Brush CaretBrush { get; set; } public Brush SelectionBrush { get; set; } }
}
