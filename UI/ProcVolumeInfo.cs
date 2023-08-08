using LibW;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;

namespace UI
{
    class ProcVolumeInfo : INotifyPropertyChanged
    {
        private float _oldVolume;
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

        public string Title => Process.MainWindowTitle;
        public string Name => Process.ProcessName;

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

        public void Refresh()
        {
            Process.Refresh();
            OnPropertyChanged(new PropertyChangedEventArgs("Name"));
            OnPropertyChanged(new PropertyChangedEventArgs("Title"));
        }
    }
}
