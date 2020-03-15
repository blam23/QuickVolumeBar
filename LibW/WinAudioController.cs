using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace LibW
{
    public static class WinAudioController
    {
        public static IEnumerable<Process> GetProcessesToMonitor()
        {
            foreach(var pid in WinMM.GetProcessesWithAudioSessions())
            {
                yield return Process.GetProcessById((int)pid);
            }
        }

        public static float GetVolume(Process proc)
        {
            return WinMM.GetApplicationVolume((uint)proc.Id).GetValueOrDefault(-1);
        }

        public static void SetVolume(Process proc, float value)
        {
            WinMM.SetApplicationVolume((uint)proc.Id, value);
        }
    }
}
