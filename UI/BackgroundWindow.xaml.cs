using LibW;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UI
{
    /// <summary>
    /// Interaction logic for BackgroundWindow.xaml
    /// </summary>
    public partial class BackgroundWindow : Window
    {
        private static readonly string Startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        private static readonly string Shortcut = Path.Combine(Startup, "Quick Volume Bar.url");
        public static bool Exiting { get; private set; }

        private WndProcBus _bus;
        private Win32HotKeyController _hotKeyBus;

        private MainWindow _controllerWindow;

        private Mutex _runningMutex;
        private static readonly string UniqueName = "QuickVolumeBarWPFApp";

        public BackgroundWindow()
        {
            InitializeComponent();
            Loaded += OnLoad;

            Closed += (_, __) =>
            {
                Dispose();
            };
        }

        private void ShowController()
        {
            Logging.WriteLine("Displaying Controller Window");
            _controllerWindow.Show();
            _controllerWindow.Activate();
            _controllerWindow.Topmost = true;
        }

        private void OnLoad(object sender, RoutedEventArgs e)
        {
            // Make sure this is hidden.
            Hide();

            // Check to see if we're already running
            Logging.WriteLine($"Checking to see if we're already running");
            _runningMutex = new Mutex(true, UniqueName, out var createdRunningMutex);
            if (!createdRunningMutex)
            {
                Logging.WriteLine($"Application is already running, exiting this instance.");
                Application.Current.Shutdown();
                return;
            }

            // Show the tray icon now that we know we're not already running
            TrayIcon.Visibility = Visibility.Visible;

            // Setup Controller Window
            Logging.WriteLine($"Setting up Controller Window");
            _controllerWindow = new MainWindow();

            Logging.WriteLine($"Setting up WndProcBus for {(GetType().Name)}");
            _bus = new WndProcBus();
            _bus.Init(this);

            Logging.WriteLine("Setting up Win32HotKeyController");
            _hotKeyBus = new Win32HotKeyController();
            _hotKeyBus.RegisterForEvents(_bus);

            Logging.WriteLine("Registering Global Audio Controller Hotkey (Shift+Win+G)");
            var registered = _hotKeyBus.Register(new Win32HotKey(
                Win32HotKeyController.Modifiers.WinKey | Win32HotKeyController.Modifiers.Shift,
                Key.G,
                ShowController
            ));
            Logging.WriteLine($"Registered Sync HotKey, success=[{registered}]");

            StartupToggle.IsChecked = File.Exists(Shortcut);
        }

        private static void RemoveFromStartup()
        {
            if (File.Exists(Shortcut))
            {
                File.Delete(Shortcut);
            }
        }

        private static void AddToStartup()
        {
            using (var writer = new StreamWriter(Shortcut))
            {
                var app = Assembly.GetExecutingAssembly().Location;
                if (app.EndsWith(".dll"))
                {
                    app = app.Substring(0, app.Length - 4) + ".exe";
                }

                writer.WriteLine("[InternetShortcut]");
                writer.WriteLine("URL=file:///" + app);
                writer.WriteLine("IconIndex=0");
                var icon = app.Replace('\\', '/');
                writer.WriteLine("IconFile=" + icon);
                writer.Flush();
            }
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {

        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Exiting = true;
            Application.Current.Shutdown();
        }

        private void Startup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi)
            {
                if (mi.IsChecked)
                    AddToStartup();
                else
                    RemoveFromStartup();
            }
        }

        public void Dispose()
        {
            _runningMutex.Dispose();
            _hotKeyBus?.Dispose();
            TrayIcon?.Dispose();
        }
    }
}
