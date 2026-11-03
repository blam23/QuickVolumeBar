using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LibW
{
    public sealed class AudioSessionSnapshot
    {
        public uint ProcessId { get; set; }
        public string DataFlow { get; set; }
        public int EndpointNumber { get; set; }
        public string DisplayName { get; set; }
        public string SessionIdentifier { get; set; }
        public string SessionInstanceIdentifier { get; set; }
        public string GroupingParameter { get; set; }
        public string State { get; set; }
        public float Volume { get; set; }
        public bool IsMuted { get; set; }
        public float PeakLevel { get; set; }
    }

    public static class AudioSessionDiagnostics
    {
        private const int DeviceStateActive = 0x00000001;
        private const int ClsctxAll = 0x17;

        public static List<AudioSessionSnapshot> GetActiveEndpointSessions(out string scanWarnings)
        {
            var results = new List<AudioSessionSnapshot>();
            var warnings = new List<string>();
            IMMDeviceEnumeratorDiagnostics deviceEnumerator = null;

            try
            {
                deviceEnumerator = (IMMDeviceEnumeratorDiagnostics)(new MMDeviceEnumerator());

                // Include capture and render endpoints so Discord's communication and playback
                // sessions are both visible, even when they use different default devices.
                foreach (var flow in new[] { EDataFlow.eRender, EDataFlow.eCapture })
                {
                    IMMDeviceCollectionDiagnostics devices = null;
                    int deviceCount;
                    try
                    {
                        Check(deviceEnumerator.EnumAudioEndpoints(flow, DeviceStateActive, out devices), "enumerate " + FlowName(flow) + " endpoints");
                        Check(devices.GetCount(out deviceCount), "get " + FlowName(flow) + " endpoint count");
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(FlowName(flow) + ": " + ex.Message);
                        Release(devices);
                        continue;
                    }

                    for (int endpointIndex = 0; endpointIndex < deviceCount; endpointIndex++)
                    {
                        IMMDeviceDiagnostics device = null;
                        IAudioSessionManager2Diagnostics manager = null;
                        IAudioSessionEnumeratorDiagnostics sessions = null;
                        object managerObject = null;

                        try
                        {
                            Check(devices.Item(endpointIndex, out device), "get audio endpoint");
                            var managerId = typeof(IAudioSessionManager2Diagnostics).GUID;
                            Check(device.Activate(ref managerId, ClsctxAll, IntPtr.Zero, out managerObject), "activate audio session manager");
                            manager = (IAudioSessionManager2Diagnostics)managerObject;
                            Check(manager.GetSessionEnumerator(out sessions), "enumerate audio sessions");
                            Check(sessions.GetCount(out int sessionCount), "get audio session count");

                            for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                            {
                                IAudioSessionControlDiagnostics control = null;
                                try
                                {
                                    Check(sessions.GetSession(sessionIndex, out control), "get audio session");
                                    if (!(control is IAudioSessionControl2Diagnostics control2))
                                        continue;

                                    var snapshot = ReadSession(control, control2, flow, endpointIndex + 1);
                                    results.Add(snapshot);
                                }
                                finally
                                {
                                    Release(control);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add(FlowName(flow) + " endpoint " + (endpointIndex + 1) + ": " + ex.Message);
                        }
                        finally
                        {
                            Release(sessions);
                            Release(managerObject);
                            Release(device);
                        }
                    }

                    Release(devices);
                }

                scanWarnings = string.Join(" | ", warnings);
                return results;
            }
            finally
            {
                Release(deviceEnumerator);
            }
        }

        private static string FlowName(EDataFlow flow)
        {
            return flow == EDataFlow.eRender ? "Playback" : "Capture";
        }

        private static AudioSessionSnapshot ReadSession(
            IAudioSessionControlDiagnostics control,
            IAudioSessionControl2Diagnostics control2,
            EDataFlow flow,
            int endpointNumber)
        {
            var result = new AudioSessionSnapshot
            {
                DataFlow = flow == EDataFlow.eRender ? "Playback" : "Capture",
                EndpointNumber = endpointNumber,
                DisplayName = string.Empty,
                SessionIdentifier = string.Empty,
                SessionInstanceIdentifier = string.Empty,
                GroupingParameter = string.Empty,
                State = "Unknown",
                Volume = -1f
            };

            if (control2.GetProcessId(out uint processId) >= 0)
                result.ProcessId = processId;
            if (control2.GetState(out AudioSessionState state) >= 0)
                result.State = state.ToString().Replace("AudioSessionState", string.Empty);
            if (control.GetDisplayName(out string displayName) >= 0)
                result.DisplayName = displayName ?? string.Empty;
            if (control2.GetSessionIdentifier(out string identifier) >= 0)
                result.SessionIdentifier = identifier ?? string.Empty;
            if (control2.GetSessionInstanceIdentifier(out string instanceIdentifier) >= 0)
                result.SessionInstanceIdentifier = instanceIdentifier ?? string.Empty;
            if (control2.GetGroupingParam(out Guid grouping) >= 0)
                result.GroupingParameter = grouping.ToString("D");

            if (control is ISimpleAudioVolumeDiagnostics volume)
            {
                if (volume.GetMasterVolume(out float level) >= 0)
                    result.Volume = level * 100f;
                if (volume.GetMute(out bool muted) >= 0)
                    result.IsMuted = muted;
            }

            if (control is IAudioMeterInformationDiagnostics meter && meter.GetPeakValue(out float peak) >= 0)
                result.PeakLevel = Math.Max(0f, Math.Min(1f, peak));

            return result;
        }

        private static void Check(int hresult, string operation)
        {
            if (hresult < 0)
                throw new COMException("Failed to " + operation + " (HRESULT 0x" + hresult.ToString("X8") + ").", hresult);
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.ReleaseComObject(value);
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumeratorDiagnostics
        {
            [PreserveSig]
            int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollectionDiagnostics devices);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollectionDiagnostics
        {
            [PreserveSig]
            int GetCount(out int count);

            [PreserveSig]
            int Item(int index, out IMMDeviceDiagnostics device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceDiagnostics
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2Diagnostics
        {
            [PreserveSig] int GetAudioSessionControl(ref Guid sessionGuid, uint streamFlags, out IAudioSessionControlDiagnostics sessionControl);
            [PreserveSig] int GetSimpleAudioVolume(ref Guid sessionGuid, uint streamFlags, out ISimpleAudioVolumeDiagnostics audioVolume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumeratorDiagnostics sessionEnumerator);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumeratorDiagnostics
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControlDiagnostics session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControlDiagnostics
        {
            [PreserveSig] int GetState(out AudioSessionState state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingParameter);
            [PreserveSig] int SetGroupingParam(ref Guid groupingParameter, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2Diagnostics
        {
            [PreserveSig] int GetState(out AudioSessionState state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingParameter);
            [PreserveSig] int SetGroupingParam(ref Guid groupingParameter, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
            [PreserveSig] int GetProcessId(out uint processId);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolumeDiagnostics
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformationDiagnostics
        {
            [PreserveSig] int GetPeakValue(out float peak);
            [PreserveSig] int GetMeteringChannelCount(out int channelCount);
            [PreserveSig] int GetChannelsPeakValues(int channelCount, IntPtr peakValues);
            [PreserveSig] int QueryHardwareSupport(out int hardwareSupportMask);
        }
    }
}
