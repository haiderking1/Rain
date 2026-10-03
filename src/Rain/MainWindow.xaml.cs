using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Rain;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        SourceInitialized += (_, _) => UseDarkTitleBar();
        Closed += (_, _) => _vm.Dispose();
        _vm.PropertyChanged += OnViewModelChanged;
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentTrack) && _vm.CurrentTrack is { } track)
            TrackList.ScrollIntoView(track);
        else if (e.PropertyName == nameof(MainViewModel.SelectedTrack) && _vm.SelectedTrack is { } selected)
            TrackList.ScrollIntoView(selected);
        else if (e.PropertyName == nameof(MainViewModel.IsDownloadOpen) && _vm.IsDownloadOpen)
            Dispatcher.BeginInvoke(() => { UrlBox.Focus(); UrlBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_vm.IsDownloading)
        {
            _vm.DownloadCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.ToggleDownloadCommand.Execute(null);
            TrackList.Focus();
            e.Handled = true;
        }
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;

        if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (SearchBox.IsKeyboardFocused)
        {
            if (e.Key == Key.Escape)
            {
                _vm.SearchText = "";
                TrackList.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                TrackList.Focus();
                e.Handled = true;
            }
            return;
        }

        // Typing in any other box (like the download link) shouldn't trigger shortcuts.
        if (Keyboard.FocusedElement is TextBox) return;

        switch (e.Key)
        {
            case Key.Space:
            case Key.MediaPlayPause:
                _vm.PlayPauseCommand.Execute(null);
                break;
            case Key.MediaNextTrack:
            case Key.Right when ctrl:
                _vm.NextCommand.Execute(null);
                break;
            case Key.MediaPreviousTrack:
            case Key.Left when ctrl:
                _vm.PreviousCommand.Execute(null);
                break;
            case Key.Enter when _vm.SelectedTrack is { } selected:
                _vm.Play(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void TrackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(TrackList, source) is ListBoxItem { DataContext: Track track })
        {
            _vm.Play(track);
        }
    }

    void Seek_DragStarted(object sender, DragStartedEventArgs e) => _vm.IsSeeking = true;

    void Seek_DragCompleted(object sender, DragCompletedEventArgs e) => _vm.IsSeeking = false;

    // ---- Dark title bar + acrylic blur (Windows 11 22H2+, solid fallback elsewhere) ----

    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaCaptionColor = 35;
    const int DwmwaSystemBackdropType = 38;
    const int DwmsbtTransientWindow = 3; // acrylic

    [StructLayout(LayoutKind.Sequential)]
    struct Margins(int all)
    {
        public int Left = all, Right = all, Top = all, Bottom = all;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        if (Environment.OSVersion.Version.Build >= 22621)
        {
            // Let DWM paint a blurred backdrop behind the whole window, title bar included.
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Margins(-1);
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var backdrop = DwmsbtTransientWindow;
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0)
            {
                Background = (Brush)FindResource("BackdropTint");
                return;
            }
        }

        var caption = 0x0015110F; // #0F1115 as COLORREF (0x00BBGGRR), matches the window background
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
    }
}
