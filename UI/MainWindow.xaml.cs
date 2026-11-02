using LibW;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;

namespace UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int WcaAccentPolicy = 19;
        private const int AccentEnableAcrylicBlurBehind = 4;
        private const int AcrylicTintColor = unchecked((int)0xA0302033);
        private const int DwmwaSystemBackdropType = 38;
        private const int DwmSystemBackdropTransientWindow = 3;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private readonly Timer _refreshTimer;
        static ImageSource _volumeIcon = GetIcon(Assembly.GetEntryAssembly().Location);
        Dictionary<int, ProcVolumeInfo> _processLookup;
        ObservableSet<ProcVolumeInfo> _processes;

        private void RefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            RefreshList();
        }

        static ImageSource GetIcon(string path)
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);

            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); ;
        }

        private void RefreshList()
        {
            var procs = WinAudioController.GetProcessesToMonitor();

            Dispatcher.Invoke(() =>
            {
                List<int> existing = new List<int>();
                foreach (var proc in procs)
                {
                    // Idle
                    if (proc.Id == 0)
                        continue;

                    existing.Add(proc.Id);
                    if (!_processLookup.ContainsKey(proc.Id))
                    {
                        var icon = Icon;
                        try
                        {
                            icon = GetIcon(proc.MainModule.FileName);
                        }
                        catch (Exception)
                        {
                            // can't enumerate modules (privilege error)
                        }

                        var pvi = new ProcVolumeInfo
                        {
                            Process = proc,
                            Icon = icon
                        };

                        _processes.Add(pvi);
                        _processLookup.Add(proc.Id, pvi);
                    }
                }

                List<ProcVolumeInfo> toRemove = new List<ProcVolumeInfo>();
                foreach (var kvp in _processLookup)
                {
                    if (!existing.Contains(kvp.Key))
                    {
                        toRemove.Add(kvp.Value);
                    }
                    else
                    {
                        kvp.Value.Refresh();
                    }
                }

                foreach (var pvi in toRemove)
                {
                    _processLookup.Remove(pvi.Process.Id);
                    _processes.Remove(pvi);
                }

                if (lvApplications.SelectedIndex == -1)
                {
                    lvApplications.SelectedIndex = 0;
                    lvApplications.Focusable = true;
                    lvApplications.Focus();
                    Keyboard.Focus(lvApplications);
                }
            });
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
            KeyUp += WindowKeyUp;
            _refreshTimer = new Timer(300);
            _refreshTimer.Elapsed += RefreshTimer_Elapsed;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 2,
                GradientColor = AcrylicTintColor
            };
            var accentSize = Marshal.SizeOf(typeof(AccentPolicy));
            var accentPointer = Marshal.AllocHGlobal(accentSize);

            try
            {
                if (source != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;

                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                var cornerPreference = DwmWindowCornerRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

                // Windows 11 22H2+ exposes Desktop Acrylic as a supported DWM backdrop.
                var backdropType = DwmSystemBackdropTransientWindow;
                if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdropType, sizeof(int)) == 0)
                {
                    RootGrid.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x3A, 0x16, 0x4A));
                    Background = Brushes.Transparent;
                    return;
                }

                // Older Windows builds can still provide Acrylic through the legacy accent policy.
                Marshal.StructureToPtr(accent, accentPointer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = accentPointer,
                    SizeOfData = accentSize
                };

                if (SetWindowCompositionAttribute(hwnd, ref data))
                {
                    RootGrid.Background = Brushes.Transparent;
                    Background = Brushes.Transparent;
                }
            }
            catch (DllNotFoundException)
            {
                RestoreOpaqueBackground(source);
            }
            catch (EntryPointNotFoundException)
            {
                RestoreOpaqueBackground(source);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPointer);
            }
        }

        private void RestoreOpaqueBackground(HwndSource source)
        {
            if (source != null)
                source.CompositionTarget.BackgroundColor = Color.FromRgb(24, 20, 32);

            RootGrid.Background = new SolidColorBrush(Color.FromRgb(24, 20, 32));
            Background = RootGrid.Background;
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            _refreshTimer?.Stop();
            Hide();
        }

        public new void Show()
        {
            base.Show();
            _refreshTimer?.Stop();
            _refreshTimer?.Start();
            RefreshList();
        }

        private void WindowKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Enter:
                    Hide();
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (BackgroundWindow.Exiting)
            {
                _refreshTimer.Dispose();
                return;
            }

            e.Cancel = true;
            Hide();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _processes = new ObservableSet<ProcVolumeInfo>();
            _processLookup = new Dictionary<int, ProcVolumeInfo>();
            lvApplications.ItemsSource = _processes;

            var globalVolume = new ProcVolumeInfo
            {
                Process = null,
                Icon = _volumeIcon
            };
            _processes.Add(globalVolume);
        }

        private static readonly float ADJUSTMENT_AMOUNT = 10f;
        private static readonly float SMALL_ADJUSTMENT_AMOUNT = 2f;

        private void AppsKeyDown(object sender, KeyEventArgs e)
        {
            if (!(lvApplications.SelectedItem is ProcVolumeInfo currentProc))
                return;

            bool shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            switch (e.Key)
            {
                case Key.Left:
                    {
                        var amount = shiftDown ? SMALL_ADJUSTMENT_AMOUNT : ADJUSTMENT_AMOUNT;
                        currentProc.LowerVolume(amount);
                        e.Handled = true;
                        break;
                    }
                case Key.Right:
                    {
                        var amount = shiftDown ? SMALL_ADJUSTMENT_AMOUNT : ADJUSTMENT_AMOUNT;
                        currentProc.RaiseVolume(amount);
                        lvApplications.UpdateLayout();
                        e.Handled = true;
                        break;
                    }
                case Key.Escape:
                case Key.Enter:
                    Hide();
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        public void Dispose()
        {
        }
    }
}
