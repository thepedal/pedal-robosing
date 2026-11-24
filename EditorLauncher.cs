// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — a double-click on the machine opens the phrase editor.
//
// ReBuzz sends a double-click to a machine's own window when the machine has a
// GUI factory marked PreferWindowedGUI: it makes a host window once, puts the
// GUI the factory returns inside it, and shows it on every double-click
// (MachineCore.OpenWindowedGUI). The phrase editor is a window of its own, so
// the GUI here is only a messenger: each time the host window is shown it opens
// the editor and hides the host again. The host is made invisible before it is
// first shown, so nothing flashes. With this in place a double-click no longer
// opens the parameter window; that is on the right-click menu (Parameters).

using System;
using System.Windows;
using System.Windows.Controls;
using BuzzGUI.Interfaces;

namespace PedalRoboSing
{
    [MachineGUIFactoryDecl(PreferWindowedGUI = true, IsGUIResizable = false, UseThemeStyles = false)]
    public class PedalRoboSingGuiFactory : IMachineGUIFactory
    {
        public IMachineGUI CreateGUI(IMachineGUIHost host) => new EditorLauncher(host as Window);
    }

    public class EditorLauncher : UserControl, IMachineGUI
    {
        readonly Window _hostWindow;
        IMachine _machine;

        public IMachine Machine { get => _machine; set => _machine = value; }

        public EditorLauncher(Window hostWindow)
        {
            _hostWindow = hostWindow;
            Width = 1; Height = 1;
            if (hostWindow == null) return;                 // not the windowed path: stay an empty speck
            try
            {
                // Before the host is first shown: no frame, see-through, no focus, not in the taskbar
                hostWindow.WindowStyle = WindowStyle.None;
                hostWindow.AllowsTransparency = true;
                hostWindow.Opacity = 0;
                hostWindow.ShowActivated = false;
                hostWindow.ShowInTaskbar = false;
            }
            catch { }                                       // if ReBuzz ever makes the window sooner, it just flashes
            hostWindow.IsVisibleChanged += (s, e) =>
            {
                if (!hostWindow.IsVisible) return;
                // After ReBuzz has finished showing it, not in the middle
                hostWindow.Dispatcher.BeginInvoke(new Action(Launch));
            };
        }

        void Launch()
        {
            try { _hostWindow.Hide(); } catch { }
            try { (_machine?.ManagedMachine as PedalRoboSingMachine)?.OpenEditor(); } catch { }
        }
    }
}
