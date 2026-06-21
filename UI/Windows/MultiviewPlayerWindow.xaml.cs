using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HTPC.Core.Interop;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Windows;

public partial class MultiviewPlayerWindow : Window
{
    // --- WIN32 INTEROP: Z-ORDER & CLICK-THROUGH ---
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    public enum LayoutMode { Quad, ThreeScreen, PiP }
    
    private readonly List<Channel> _channels;
    private readonly ServerManagerService _serverManager;
    private readonly IntPtr[] _mpvContexts = new IntPtr[4];
    private readonly Border[] _borders;
    
    private int _activeIndex = 0;
    private LayoutMode _currentLayout = LayoutMode.Quad;
    private bool _isClosing = false; 

    private List<int> _displayOrder = new List<int>();

    private DispatcherTimer _idleTimer;
    private bool _isControlsVisible = true;
    private Point _lastMousePosition;

    public MultiviewPlayerWindow(List<Channel> channels, ServerManagerService serverManager)
    {
        InitializeComponent();
        _channels = channels;
        _serverManager = serverManager;
        
        _borders = new Border[] { Border0, Border1, Border2, Border3 };

        int activeCount = _channels.Count(c => c != null);
        if (activeCount == 2) _currentLayout = LayoutMode.PiP;
        else if (activeCount == 3) _currentLayout = LayoutMode.ThreeScreen;
        else _currentLayout = LayoutMode.Quad;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();

        this.SizeChanged += (s, e) => {
            PopupRoot.Width = e.NewSize.Width;
            PopupRoot.Height = e.NewSize.Height;
            var offset = OverlayPopup.HorizontalOffset;
            OverlayPopup.HorizontalOffset = offset + 0.1;
            OverlayPopup.HorizontalOffset = offset;
        };

        this.PreviewMouseLeftButtonDown += Overlay_MouseLeftButtonDown;
        this.PreviewMouseMove += Overlay_MouseMove;

        this.Loaded += OnLoaded;
        this.Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopupRoot.Width = this.ActualWidth;
        PopupRoot.Height = this.ActualHeight;

        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null)
        {
            MessageBox.Show("No active server configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _isClosing = true;
            Close();
            return;
        }

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";

        for (int i = 0; i < 4; i++)
        {
            if (i < _channels.Count && _channels[i] != null)
            {
                _displayOrder.Add(i);

                var channel = _channels[i];
                _mpvContexts[i] = Libmpv.mpv_create();
                
                Libmpv.mpv_set_option_string(_mpvContexts[i], "vo", "gpu-next");
                Libmpv.mpv_set_option_string(_mpvContexts[i], "hwdec", "auto-copy");
                Libmpv.mpv_set_option_string(_mpvContexts[i], "profile", "fast"); 
                Libmpv.mpv_set_option_string(_mpvContexts[i], "demuxer-lavf-o", "fflags=+genpts+igndts");

                Libmpv.mpv_initialize(_mpvContexts[i]);

                long hwndLong = GetHostHwnd(i);
                IntPtr hwnd = (IntPtr)hwndLong;
                Libmpv.mpv_set_property_string(_mpvContexts[i], "wid", hwndLong.ToString());

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);

                string streamUrl = "";
                if (channel.Id != null && channel.Id.StartsWith("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    var currentAiring = channel.CurrentAirings?.Count > 0 ? channel.CurrentAirings[0] : null;
                    if (currentAiring != null && !string.IsNullOrWhiteSpace(currentAiring.Source))
                    {
                        string fileId = currentAiring.Source.Split('/').Last();
                        streamUrl = $"{baseUrl}/dvr/files/{fileId}/hls/master.m3u8";

                        int offsetSeconds = (int)(DateTime.Now - currentAiring.StartTime).TotalSeconds;
                        if (offsetSeconds > 0)
                            Libmpv.mpv_set_option_string(_mpvContexts[i], "start", offsetSeconds.ToString());
                    }
                    else streamUrl = $"{baseUrl}/devices/ANY/channels/{channel.Number}/hls/master.m3u8";
                }
                else streamUrl = $"{baseUrl}/devices/ANY/channels/{channel.Number}/stream.mpg?format=ts";

                Libmpv.mpv_command_string(_mpvContexts[i], $"loadfile \"{streamUrl}\"");
            }
        }

        if (_displayOrder.Count > 0)
        {
            SwitchFocus(_displayOrder[0]);
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point pos = e.GetPosition(this); 
        double w = this.ActualWidth;
        double h = this.ActualHeight;
        
        if (_isControlsVisible && pos.Y <= 80 && pos.X <= 120) return; 
        
        int clickedIndex = -1;

        if (_displayOrder.Count == 0) return;

        if (_currentLayout == LayoutMode.PiP && _displayOrder.Count > 1)
        {
            double pipW = 426, pipH = 240, margin = 40;
            double pipLeft = w - pipW - margin;
            double pipTop = h - pipH - margin;

            if (pos.X >= pipLeft && pos.X <= pipLeft + pipW && pos.Y >= pipTop && pos.Y <= pipTop + pipH)
                clickedIndex = _displayOrder[1]; 
            else
                clickedIndex = _displayOrder[0]; 
        }
        else if (_currentLayout == LayoutMode.ThreeScreen && _displayOrder.Count > 1)
        {
            if (pos.X < w * (2.0 / 3.0)) clickedIndex = _displayOrder[0]; 
            else if (pos.Y < h / 2) clickedIndex = _displayOrder[1]; 
            else if (_displayOrder.Count > 2) clickedIndex = _displayOrder[2]; 
        }
        else if (_currentLayout == LayoutMode.Quad)
        {
            bool isRight = pos.X > w / 2;
            bool isBottom = pos.Y > h / 2;
            int slot = (isBottom ? 2 : 0) + (isRight ? 1 : 0); 
            if (slot < _channels.Count && _channels[slot] != null) clickedIndex = slot;
        }

        if (clickedIndex != -1 && _activeIndex != clickedIndex)
        {
            SwitchFocus(clickedIndex);
            // THE FIX: Immediately kill the event so it cannot double-fire and undo the swap!
            e.Handled = true; 
        }
    }

    private void SwitchFocus(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _channels.Count || _channels[newIndex] == null)
            return;

        _activeIndex = newIndex;

        // THE UX UPGRADE: Perform a pure 1-to-1 swap to keep unclicked videos perfectly in place
        int indexInRoster = _displayOrder.IndexOf(newIndex);
        if (indexInRoster > 0)
        {
            int temp = _displayOrder[0];
            _displayOrder[0] = _displayOrder[indexInRoster];
            _displayOrder[indexInRoster] = temp;
        }

        // Force all to mute, except the newly active video
        for (int i = 0; i < 4; i++)
        {
            if (_mpvContexts[i] != IntPtr.Zero)
            {
                string muteState = (i == _activeIndex) ? "no" : "yes";
                Libmpv.mpv_set_property_string(_mpvContexts[i], "mute", muteState);
            }
        }

        ApplyLayout();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        WakeUpUi();

        if (_isClosing) return;

        // --- NEW: Fetch the mapped remote command ---
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);

        // FIX: Listen for the mapped remote command AND the hardware BrowserBack signal
        if (command == HTPC.Core.Input.HtpcCommand.Back || e.Key == Key.Escape || e.Key == Key.Back || e.Key == Key.BrowserBack)
        {
            Back_Click(null!, null!);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L)
        {
            _currentLayout = (LayoutMode)(((int)_currentLayout + 1) % 3);
            ApplyLayout();
            e.Handled = true;
            return;
        }

        // Map remote D-pad commands to standard keys for the navigation handler
        Key navKey = e.Key;
        if (command == HTPC.Core.Input.HtpcCommand.Right) navKey = Key.Right;
        else if (command == HTPC.Core.Input.HtpcCommand.Left) navKey = Key.Left;
        else if (command == HTPC.Core.Input.HtpcCommand.Up) navKey = Key.Up;
        else if (command == HTPC.Core.Input.HtpcCommand.Down) navKey = Key.Down;

        if (navKey == Key.Right || navKey == Key.Left || navKey == Key.Up || navKey == Key.Down)
        {
            HandleNavigation(navKey);
            e.Handled = true;
        }
    }

    private void HandleNavigation(Key key)
    {
        try
        {
            if (_currentLayout == LayoutMode.Quad)
            {
                int newIndex = _activeIndex;
                if (key == Key.Right && (_activeIndex == 0 || _activeIndex == 2)) newIndex = _activeIndex + 1;
                else if (key == Key.Left && (_activeIndex == 1 || _activeIndex == 3)) newIndex = _activeIndex - 1;
                else if (key == Key.Down && (_activeIndex == 0 || _activeIndex == 1)) newIndex = _activeIndex + 2;
                else if (key == Key.Up && (_activeIndex == 2 || _activeIndex == 3)) newIndex = _activeIndex - 2;
                SwitchFocus(newIndex);
            }
            else
            {
                if (_displayOrder.Count <= 1) return;
                
                // Rotates the display order using the keyboard
                if (key == Key.Right || key == Key.Down)
                {
                    int last = _displayOrder[_displayOrder.Count - 1];
                    _displayOrder.RemoveAt(_displayOrder.Count - 1);
                    _displayOrder.Insert(0, last);
                }
                else if (key == Key.Left || key == Key.Up)
                {
                    int first = _displayOrder[0];
                    _displayOrder.RemoveAt(0);
                    _displayOrder.Add(first);
                }
                SwitchFocus(_displayOrder[0]);
            }
        }
        catch (Exception) {}
    }
    
    private void ApplyLayout()
    {
        Col0.Width = new GridLength(1, GridUnitType.Star);
        Col1.Width = new GridLength(1, GridUnitType.Star);
        Row0.Height = new GridLength(1, GridUnitType.Star);
        Row1.Height = new GridLength(1, GridUnitType.Star);

        for (int i = 0; i < 4; i++)
        {
            _borders[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            _borders[i].VerticalAlignment = VerticalAlignment.Stretch;
            _borders[i].Margin = new Thickness(0);
            _borders[i].Width = double.NaN; 
            _borders[i].Height = double.NaN;
            _borders[i].Visibility = Visibility.Collapsed;
            Panel.SetZIndex(_borders[i], 0);
        }

        if (_displayOrder.Count == 0) return;

        int secondaryIdx = -1;

        if (_currentLayout == LayoutMode.Quad)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_channels.Count > i && _channels[i] != null)
                {
                    _borders[i].Visibility = Visibility.Visible;
                    int row = i >= 2 ? 1 : 0;
                    int col = i % 2 != 0 ? 1 : 0;
                    SetGridPosition(_borders[i], row, col, 1, 1);
                }
            }
        }
        else if (_currentLayout == LayoutMode.ThreeScreen)
        {
            Col0.Width = new GridLength(2, GridUnitType.Star); 
            Col1.Width = new GridLength(1, GridUnitType.Star); 

            _borders[_displayOrder[0]].Visibility = Visibility.Visible;
            SetGridPosition(_borders[_displayOrder[0]], 0, 0, 2, 1);

            if (_displayOrder.Count > 1) {
                _borders[_displayOrder[1]].Visibility = Visibility.Visible;
                SetGridPosition(_borders[_displayOrder[1]], 0, 1, 1, 1);
            }
            if (_displayOrder.Count > 2) {
                _borders[_displayOrder[2]].Visibility = Visibility.Visible;
                SetGridPosition(_borders[_displayOrder[2]], 1, 1, 1, 1);
            }
        }
        else if (_currentLayout == LayoutMode.PiP)
        {
            _borders[_displayOrder[0]].Visibility = Visibility.Visible;
            SetGridPosition(_borders[_displayOrder[0]], 0, 0, 2, 2);

            if (_displayOrder.Count > 1) {
                secondaryIdx = _displayOrder[1];
                _borders[secondaryIdx].Visibility = Visibility.Visible;
                SetGridPosition(_borders[secondaryIdx], 0, 0, 2, 2); 
                _borders[secondaryIdx].HorizontalAlignment = HorizontalAlignment.Right;
                _borders[secondaryIdx].VerticalAlignment = VerticalAlignment.Bottom;
                _borders[secondaryIdx].Width = 426; 
                _borders[secondaryIdx].Height = 240;
                _borders[secondaryIdx].Margin = new Thickness(0, 0, 40, 40);
                Panel.SetZIndex(_borders[secondaryIdx], 10); 
            }
        }
        
        UpdateFocusGlow();
        PlayerGrid.UpdateLayout(); 

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_currentLayout == LayoutMode.PiP && secondaryIdx != -1)
            {
                IntPtr mainHwnd = (IntPtr)GetHostHwnd(_displayOrder[0]);
                IntPtr pipHwnd = (IntPtr)GetHostHwnd(secondaryIdx);

                if (mainHwnd != IntPtr.Zero) SetWindowPos(mainHwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                if (pipHwnd != IntPtr.Zero) SetWindowPos(pipHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void SetGridPosition(UIElement element, int row, int col, int rowSpan, int colSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, colSpan);
    }

    private void UpdateFocusGlow()
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == _activeIndex)
            {
                _borders[i].BorderBrush = new SolidColorBrush(Colors.White);
                if (_currentLayout != LayoutMode.PiP) Panel.SetZIndex(_borders[i], 1); 
            }
            else
            {
                _borders[i].BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _isClosing = true;
        OverlayPopup.IsOpen = false; 
        Close();
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        Point currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 2 || Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 2)
        {
            _lastMousePosition = currentPosition;
            WakeUpUi();
        }
    }

    private void WakeUpUi()
    {
        Mouse.OverrideCursor = null;
        if (!_isControlsVisible)
        {
            _isControlsVisible = true;
            FadeControls(1.0); 
        }
        _idleTimer?.Stop();
        _idleTimer?.Start();
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        _idleTimer?.Stop();
        Mouse.OverrideCursor = Cursors.None;
        if (_isControlsVisible)
        {
            _isControlsVisible = false;
            FadeControls(0.0); 
        }
    }

    private void FadeControls(double targetOpacity)
    {
        var fadeAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.3),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        ControlsContainer.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        ControlsContainer.IsHitTestVisible = targetOpacity > 0;
    }

    private long GetHostHwnd(int index) => index switch
    {
        0 => View0.Handle.ToInt64(), 1 => View1.Handle.ToInt64(), 2 => View2.Handle.ToInt64(), 3 => View3.Handle.ToInt64(), _ => 0
    };

    private void OnClosed(object? sender, EventArgs e)
    {
        _idleTimer?.Stop();
        Mouse.OverrideCursor = null;
        OverlayPopup.IsOpen = false;

        for (int i = 0; i < 4; i++)
        {
            if (_mpvContexts[i] != IntPtr.Zero)
                Libmpv.mpv_set_property_string(_mpvContexts[i], "wid", "0");
        }

        var contextsToDispose = _mpvContexts.Clone() as IntPtr[];

        System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                if (contextsToDispose != null && contextsToDispose[i] != IntPtr.Zero)
                {
                    Libmpv.mpv_command_string(contextsToDispose[i], "stop");
                    System.Threading.Thread.Sleep(50); 
                    Libmpv.mpv_terminate_destroy(contextsToDispose[i]);
                }
            }
        });
    }
}