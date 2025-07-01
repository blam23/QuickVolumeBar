using System.Collections.Generic;
using System.Diagnostics;

namespace LibW
{
    public static class WinAudioController
    {
        public static IEnumerable<Process> GetProcessesToMonitor()
        {
            foreach (var pid in WinMM.GetProcessesWithAudioSessions())
            {
                Process proc = null;

                try
                {
                    proc = Process.GetProcessById((int)pid);
                }
                catch
                {
                    Logging.WriteLine($"Unable to get process handle for pid {pid}.");
                }

                if (proc is null)
                    continue;

                yield return proc;
            }
        }

        public static float GetVolume(Process proc)
        {
            if (proc is null)
                return WinMM.GetMasterVolumeLevel();

            return WinMM.GetApplicationVolume((uint)proc.Id).GetValueOrDefault(-1);
        }

        public static void SetVolume(Process proc, float value)
        {
            if (proc is null)
            {
                WinMM.SetMasterVolumeLevel(value);
                System.Media.SystemSounds.Exclamation.Play();
            }
            else
                WinMM.SetApplicationVolume((uint)proc.Id, value);
        }
    }
}
