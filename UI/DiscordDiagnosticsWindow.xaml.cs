using LibW;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace UI
{
    public partial class DiscordDiagnosticsWindow : Window
    {
        public static readonly RoutedUICommand CopyCellCommand = new RoutedUICommand(
            "Copy cell", "CopyCell", typeof(DiscordDiagnosticsWindow));

        private readonly DispatcherTimer _refreshTimer;
        private readonly ObservableCollection<DiscordAudioSessionRow> _audioRows = new ObservableCollection<DiscordAudioSessionRow>();
        private readonly HashSet<int> _captureObservedProcessIds = new HashSet<int>();
        private DateTime _nextProcessRefresh;
        private List<DiscordProcessRow> _processRows = new List<DiscordProcessRow>();
        private string _processMetadataWarning = string.Empty;

        public DiscordDiagnosticsWindow()
        {
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(CopyCellCommand, CopyCell_Executed, CopyCell_CanExecute));
            dgSessions.ItemsSource = _audioRows;
            Loaded += (_, __) => RefreshDiagnostics();
            Closed += (_, __) => _refreshTimer.Stop();

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _refreshTimer.Tick += (_, __) => RefreshDiagnostics();
            _refreshTimer.Start();
        }

        private void RefreshDiagnostics()
        {
            var now = DateTime.Now;
            try
            {
                if (now >= _nextProcessRefresh)
                {
                    RefreshProcessList();
                    _nextProcessRefresh = now.AddSeconds(1);
                }

                var processById = _processRows.ToDictionary(p => p.ProcessId);
                var snapshots = AudioSessionDiagnostics.GetActiveEndpointSessions(out string scanWarnings)
                    .Where(session => processById.ContainsKey((int)session.ProcessId))
                    .ToList();

                foreach (var snapshot in snapshots)
                {
                    if (snapshot.DataFlow == "Capture" && snapshot.State == "Active")
                        _captureObservedProcessIds.Add((int)snapshot.ProcessId);
                }

                foreach (var process in _processRows)
                    process.UpdateRole(_captureObservedProcessIds.Contains(process.ProcessId));

                dgProcesses.Items.Refresh();
                SynchronizeSessionRows(snapshots, processById);
                var updatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
                var summary = snapshots.Count == 0
                    ? $"Updated {updatedAt} | {_processRows.Count} Discord process(es) | No matching sessions on active audio endpoints"
                    : $"Updated {updatedAt} | {_processRows.Count} Discord process(es) | {snapshots.Count} audio session(s) across active playback and capture endpoints";
                var warnings = string.Join(" | ", new[] { _processMetadataWarning, scanWarnings }.Where(w => !string.IsNullOrEmpty(w)));
                txtStatus.Text = string.IsNullOrEmpty(warnings) ? summary : summary + " | Warnings: " + warnings;
            }
            catch (Exception ex)
            {
                var hresult = ex is COMException comException ? " (0x" + comException.ErrorCode.ToString("X8") + ")" : string.Empty;
                txtStatus.Text = "Audio session scan failed: " + ex.Message + hresult;
            }
        }

        private void RefreshProcessList()
        {
            Dictionary<int, string> commandLines;
            try
            {
                commandLines = GetDiscordCommandLines();
                _processMetadataWarning = string.Empty;
            }
            catch (Exception ex)
            {
                commandLines = new Dictionary<int, string>();
                _processMetadataWarning = "Command lines unavailable: " + ex.Message;
            }

            var processes = new List<DiscordProcessRow>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!process.ProcessName.StartsWith("Discord", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var row = new DiscordProcessRow
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        WindowTitle = SafeGet(() => process.MainWindowTitle),
                        StartTime = SafeGet(() => process.StartTime.ToString("yyyy-MM-dd HH:mm:ss")),
                        ExecutablePath = SafeGet(() => process.MainModule.FileName),
                        CommandLine = commandLines.TryGetValue(process.Id, out string commandLine) ? commandLine : string.Empty
                    };

                    if (!string.IsNullOrEmpty(row.ExecutablePath))
                        row.Version = SafeGet(() => FileVersionInfo.GetVersionInfo(row.ExecutablePath).ProductVersion);

                    processes.Add(row);
                }
                finally
                {
                    process.Dispose();
                }
            }

            _processRows = processes.OrderBy(p => p.ProcessId).ToList();
            var liveProcessIds = new HashSet<int>(_processRows.Select(p => p.ProcessId));
            _captureObservedProcessIds.IntersectWith(liveProcessIds);
            foreach (var process in _processRows)
                process.UpdateRole(_captureObservedProcessIds.Contains(process.ProcessId));
            dgProcesses.ItemsSource = _processRows;
        }

        private static Dictionary<int, string> GetDiscordCommandLines()
        {
            var commandLines = new Dictionary<int, string>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'Discord%'"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject process in results)
                {
                    using (process)
                    {
                        if (process["ProcessId"] != null)
                            commandLines[Convert.ToInt32(process["ProcessId"])] = process["CommandLine"] as string ?? string.Empty;
                    }
                }
            }

            return commandLines;
        }

        private void SynchronizeSessionRows(
            List<AudioSessionSnapshot> snapshots,
            IDictionary<int, DiscordProcessRow> processById)
        {
            var existing = _audioRows.ToDictionary(row => row.Key);
            var seen = new HashSet<string>();

            foreach (var snapshot in snapshots)
            {
                var key = DiscordAudioSessionRow.MakeKey(snapshot);
                seen.Add(key);
                if (!existing.TryGetValue(key, out var row))
                {
                    row = new DiscordAudioSessionRow(key);
                    _audioRows.Add(row);
                }

                row.Update(snapshot, processById[(int)snapshot.ProcessId]);
            }

            for (int index = _audioRows.Count - 1; index >= 0; index--)
            {
                if (!seen.Contains(_audioRows[index].Key))
                    _audioRows.RemoveAt(index);
            }
        }

        private static string SafeGet(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CopyCell_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is DataGridCell;
            e.Handled = true;
        }

        private void CopyCell_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!(e.Parameter is DataGridCell cell))
                return;

            var text = GetCellText(cell);
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                txtStatus.Text = "The clipboard is busy. Try copying the cell again.";
            }
        }

        private static string GetCellText(DependencyObject element)
        {
            if (element is TextBlock textBlock)
                return textBlock.Text;
            if (element is TextBox textBox)
                return textBox.Text;

            var parts = new List<string>();
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            {
                var childText = GetCellText(VisualTreeHelper.GetChild(element, index));
                if (!string.IsNullOrEmpty(childText))
                    parts.Add(childText);
            }

            return string.Join(" ", parts);
        }
    }

    internal sealed class DiscordProcessRow : INotifyPropertyChanged
    {
        private string _role;

        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string CommandLine { get; set; }
        public string Role
        {
            get => _role;
            private set
            {
                if (_role == value)
                    return;
                _role = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role)));
            }
        }
        public string WindowTitle { get; set; }
        public string ExecutablePath { get; set; }
        public string Version { get; set; }
        public string StartTime { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void UpdateRole(bool captureObserved)
        {
            if (CommandLine.IndexOf("--utility-sub-type=audio.mojom.AudioService", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Role = "Audio service";
                return;
            }

            if (CommandLine.IndexOf("--type=renderer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var rendererId = GetArgumentValue("--renderer-client-id=");
                var rendererLabel = string.IsNullOrEmpty(rendererId) ? "Renderer" : "Renderer " + rendererId;
                Role = captureObserved ? rendererLabel + " (capture observed)" : rendererLabel;
                return;
            }

            var processType = GetArgumentValue("--type=");
            Role = string.IsNullOrEmpty(processType) ? "Main process" : processType + " process";
        }

        private string GetArgumentValue(string argumentPrefix)
        {
            if (string.IsNullOrEmpty(CommandLine))
                return string.Empty;

            var start = CommandLine.IndexOf(argumentPrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += argumentPrefix.Length;
            var end = CommandLine.IndexOfAny(new[] { ' ', '\t', '"' }, start);
            return end < 0 ? CommandLine.Substring(start) : CommandLine.Substring(start, end - start);
        }
    }

    internal sealed class DiscordAudioSessionRow : INotifyPropertyChanged
    {
        private string _processName;
        private string _sessionName;
        private string _dataFlow;
        private int _endpointNumber;
        private string _state;
        private string _volumeText;
        private bool _isMuted;
        private double _peakPercent;
        private string _peakText;
        private string _sessionIdentifier;
        private string _sessionInstanceIdentifier;
        private string _groupingParameter;

        public DiscordAudioSessionRow(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public uint ProcessId { get; private set; }
        public string ProcessName { get => _processName; private set => Set(ref _processName, value); }
        public string SessionName { get => _sessionName; private set => Set(ref _sessionName, value); }
        public string DataFlow { get => _dataFlow; private set => Set(ref _dataFlow, value); }
        public int EndpointNumber { get => _endpointNumber; private set => Set(ref _endpointNumber, value); }
        public string State { get => _state; private set => Set(ref _state, value); }
        public string VolumeText { get => _volumeText; private set => Set(ref _volumeText, value); }
        public bool IsMuted { get => _isMuted; private set => Set(ref _isMuted, value); }
        public double PeakPercent { get => _peakPercent; private set => Set(ref _peakPercent, value); }
        public string PeakText { get => _peakText; private set => Set(ref _peakText, value); }
        public string SessionIdentifier { get => _sessionIdentifier; private set => Set(ref _sessionIdentifier, value); }
        public string SessionInstanceIdentifier { get => _sessionInstanceIdentifier; private set => Set(ref _sessionInstanceIdentifier, value); }
        public string GroupingParameter { get => _groupingParameter; private set => Set(ref _groupingParameter, value); }

        public static string MakeKey(AudioSessionSnapshot snapshot)
        {
            var identity = string.IsNullOrEmpty(snapshot.SessionInstanceIdentifier)
                ? snapshot.SessionIdentifier + "|" + snapshot.DisplayName
                : snapshot.SessionInstanceIdentifier;
            return snapshot.DataFlow + "|" + snapshot.EndpointNumber + "|" + snapshot.ProcessId + "|" + identity;
        }

        public void Update(AudioSessionSnapshot snapshot, DiscordProcessRow process)
        {
            ProcessId = snapshot.ProcessId;
            ProcessName = process.ProcessName;
            SessionName = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? "(no session display name)" : snapshot.DisplayName;
            DataFlow = snapshot.DataFlow;
            EndpointNumber = snapshot.EndpointNumber;
            State = snapshot.State;
            VolumeText = snapshot.Volume < 0 ? "—" : snapshot.Volume.ToString("0") + "%";
            IsMuted = snapshot.IsMuted;
            PeakPercent = Math.Round(snapshot.PeakLevel * 100, 1);
            PeakText = PeakPercent.ToString("0.0") + "%";
            SessionIdentifier = snapshot.SessionIdentifier;
            SessionInstanceIdentifier = snapshot.SessionInstanceIdentifier;
            GroupingParameter = snapshot.GroupingParameter;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
