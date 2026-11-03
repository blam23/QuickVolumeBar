using LibW;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace UI
{
    class ProcVolumeInfo : INotifyPropertyChanged
    {
        private float _oldVolume;
        private float _peakLevel;
        private string _discordNameTag = string.Empty;

        public float PeakPercent => _peakLevel * 100f;
        public Visibility AudioLevelVisibility => Process is null ? Visibility.Collapsed : Visibility.Visible;
        public float Volume
        {
            get
            {
                var v = WinAudioController.GetVolume(Process);
                if (v != _oldVolume)
                {
                    _oldVolume = v;
                    OnPropertyChanged(new PropertyChangedEventArgs("Volume"));
                }
                return v;
            }
            set
            {
                WinAudioController.SetVolume(Process, value);
                OnPropertyChanged(new PropertyChangedEventArgs("Volume"));
            }
        }

        public Process Process { get; set; }
        public ImageSource Icon { get; set; }

        public string Title => Process is null ? "Global Volume" : Process.MainWindowTitle;
        public string Name => Process is null
            ? "volume"
            : ProcessNicknames.GetDisplayName(Process.ProcessName)
                + (string.IsNullOrEmpty(_discordNameTag) ? string.Empty : " (" + _discordNameTag + ")");

        public void SetDiscordNameTag(string nameTag)
        {
            nameTag = nameTag ?? string.Empty;
            if (_discordNameTag == nameTag)
                return;

            _discordNameTag = nameTag;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }

        internal void LowerVolume(float amount)
        {
            float vol = Volume;
            if (vol < amount) Volume = 0f; // floor
            else Volume = vol - amount;
        }

        internal void RaiseVolume(float amount)
        {
            float vol = Volume;
            if (vol + amount > 100) Volume = 100f; // ceil
            else Volume = vol + amount;
        }

        public void Refresh(float peakLevel = 0f)
        {
            Process.Refresh();
            OnPropertyChanged(new PropertyChangedEventArgs("Name"));
            OnPropertyChanged(new PropertyChangedEventArgs("Title"));

            peakLevel = Math.Max(0f, Math.Min(1f, peakLevel));
            if (Math.Abs(_peakLevel - peakLevel) > 0.001f)
            {
                _peakLevel = peakLevel;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(PeakPercent)));
            }
        }
    }
}
