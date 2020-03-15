using LibW;
using System;
using System.Collections.Generic;
using System.Timers;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Timer _refreshTimer;

        private void RefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            RefreshList();
        }

        ImageSource GetIcon(string path)
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);

            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); ;
        }

        Dictionary<int, ProcVolumeInfo> processLookup;
        ObservableSet<ProcVolumeInfo> processes;

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
                    if (!processLookup.ContainsKey(proc.Id))
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

                        processes.Add(pvi);
                        processLookup.Add(proc.Id, pvi);
                    }
                }

                foreach (var kvp in processLookup)
                {
                    if (!existing.Contains(kvp.Key))
                    {
                        processLookup.Remove(kvp.Key);
                        processes.Remove(kvp.Value);
                    }
                    else
                    {
                        kvp.Value.Refresh();
                    }
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
            processes = new ObservableSet<ProcVolumeInfo>();
            processLookup = new Dictionary<int, ProcVolumeInfo>();
            lvApplications.ItemsSource = processes;
            //
            //_refreshTimer.Start();
            //RefreshList();
        }

        private static readonly float ADJUSTMENT_AMOUNT = 10f;

        private void appsKeyDown(object sender, KeyEventArgs e)
        {
            if (!(lvApplications.SelectedItem is ProcVolumeInfo currentProc))
                return;

            switch (e.Key)
            {
                case Key.Left:
                    currentProc.LowerVolume(ADJUSTMENT_AMOUNT);
                    e.Handled = true;
                    break;
                case Key.Right:
                    currentProc.RaiseVolume(ADJUSTMENT_AMOUNT);
                    lvApplications.UpdateLayout();
                    e.Handled = true;
                    break;
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
