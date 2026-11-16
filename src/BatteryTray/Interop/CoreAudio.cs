using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryTray.Interop;

/// <summary>
/// Windows Core Audio (the API behind the Windows volume mixer): the default
/// playback device's master volume and each app's audio session. Only volume
/// and mute; nothing here touches HID devices or the network.
/// </summary>
internal static class CoreAudio
{
    /// <summary>One app's sessions grouped by executable (Edge plays from several processes).</summary>
    public sealed record AppSession(string Key, string Name, string? ExePath, bool IsSystem,
        float Volume, bool Muted, IReadOnlyList<SessionVolume> Controls);

    public sealed record Master(string DeviceId, string DeviceName, float Volume, bool Muted, IAudioEndpointVolume Endpoint);

    /// <summary>Tags our own changes, so Windows knows where they came from.</summary>
    private static Guid _context = new("4E1F0A52-7A9B-4C61-9E0B-5952C2B9A7A1");

    private const int eRender = 0, eMultimedia = 1;
    private const int CLSCTX_ALL = 0x17;
    private const int StateExpired = 2;

    // All Core Audio calls run on one background MTA thread: from the WPF (STA)
    // thread, Windows refuses ISimpleAudioVolume on other apps' sessions
    // (E_NOINTERFACE). One thread also keeps volume changes in order.
    private static readonly BlockingCollection<Action> _queue = new();
    private static readonly Thread _worker = StartWorker();

    private static Thread StartWorker()
    {
        var t = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable())
                try { work(); } catch (Exception ex) { BatteryTray.Core.Log.Error("Mixer.Worker", ex); }
        }) { IsBackground = true, Name = "YRAWAH audio" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        return t;
    }

    private static void Post(Action work) { _ = _worker; _queue.Add(work); }

    public static (Master? Master, List<AppSession> Apps) Read(Func<string, string> systemSoundsName)
    {
        var done = new TaskCompletionSource<(Master?, List<AppSession>)>();
        Post(() =>
        {
            try { done.SetResult(ReadHere(systemSoundsName)); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task.GetAwaiter().GetResult();
    }

    private static (Master? Master, List<AppSession> Apps) ReadHere(Func<string, string> systemSoundsName)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0 || device is null)
            return (null, new List<AppSession>()); // no speakers or headphones at all

        device.GetId(out string id);
        string name = FriendlyName(device) ?? "";

        var endpointIid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref endpointIid, CLSCTX_ALL, IntPtr.Zero, out object endpointObj);
        var endpoint = (IAudioEndpointVolume)endpointObj;
        endpoint.GetMasterVolumeLevelScalar(out float masterVolume);
        endpoint.GetMute(out bool masterMuted);
        var master = new Master(id, name, masterVolume, masterMuted, endpoint);

        var managerIid = typeof(IAudioSessionManager2).GUID;
        device.Activate(ref managerIid, CLSCTX_ALL, IntPtr.Zero, out object managerObj);
        var manager = (IAudioSessionManager2)managerObj;
        manager.GetSessionEnumerator(out var sessions);
        sessions.GetCount(out int count);

        var groups = new Dictionary<string, (string Name, string? Path, bool System, List<SessionVolume> Controls, float Volume, bool Muted)>();
        var order = new List<string>();
        int self = Environment.ProcessId;
        for (int i = 0; i < count; i++)
        {
            try
            {
                sessions.GetSession(i, out var session);
                session.GetState(out int state);
                if (state == StateExpired) continue;
                session.GetProcessId(out uint pid);
                if (pid == self) continue;

                bool isSystem = session.IsSystemSoundsSession() == 0;
                string? path = isSystem ? null : ProcessPath(pid);
                string key = isSystem ? "system" : path?.ToLowerInvariant() ?? $"pid:{pid}";

                var control = SessionVolume.For(session);
                if (control is null) continue;
                float volume = control.GetVolume();
                bool muted = control.GetMute();

                if (!groups.TryGetValue(key, out var g))
                {
                    session.GetDisplayName(out string? display);
                    string label = isSystem ? systemSoundsName(key)
                        : !string.IsNullOrWhiteSpace(display) && !display.StartsWith('@') ? display
                        : AppName(path, pid);
                    g = (label, path, isSystem, new List<SessionVolume>(), volume, muted);
                    order.Add(key);
                }
                g.Controls.Add(control);
                // A group reads as muted only when every session in it is.
                groups[key] = g with { Volume = Math.Max(g.Volume, volume), Muted = g.Muted && muted };
            }
            catch (Exception ex) { BatteryTray.Core.Log.Error("Mixer.Session", ex); }
        }

        var apps = order.Select(k => groups[k])
            .Select((g, i) => new AppSession(order[i], g.Name, g.Path, g.System, g.Volume, g.Muted, g.Controls))
            .OrderBy(a => a.IsSystem ? 1 : 0).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (master, apps);
    }

    public static void SetVolume(IAudioEndpointVolume endpoint, float level) =>
        Post(() => endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref _context));

    public static void SetMute(IAudioEndpointVolume endpoint, bool muted) =>
        Post(() => endpoint.SetMute(muted, ref _context));

    public static void SetVolume(IReadOnlyList<SessionVolume> controls, float level) => Post(() =>
    {
        foreach (var c in controls)
            try { c.SetVolume(Math.Clamp(level, 0f, 1f)); } catch { /* session ended mid-drag */ }
    });

    public static void SetMute(IReadOnlyList<SessionVolume> controls, bool muted) => Post(() =>
    {
        foreach (var c in controls)
            try { c.SetMute(muted); } catch { /* session ended */ }
    });

    /// <summary>
    /// One app session's volume. Normally ISimpleAudioVolume, the slider the
    /// Windows mixer moves. Some Windows builds (seen on 10.0.26300) refuse it
    /// for other apps' sessions; then the per-channel volume is used instead,
    /// and mute sets the channels to 0 and puts the old level back.
    /// </summary>
    public abstract class SessionVolume
    {
        public abstract float GetVolume();
        public abstract void SetVolume(float level);
        public abstract bool GetMute();
        public abstract void SetMute(bool muted);

        internal static SessionVolume? For(IAudioSessionControl2 session)
        {
            if (session is ISimpleAudioVolume simple) return new Simple(simple);
            if (session is IChannelAudioVolume channels)
            {
                session.GetSessionInstanceIdentifier(out string? id);
                return new Channels(channels, id ?? "");
            }
            return null;
        }

        private sealed class Simple(ISimpleAudioVolume v) : SessionVolume
        {
            public override float GetVolume() { v.GetMasterVolume(out float l); return l; }
            public override void SetVolume(float level) => v.SetMasterVolume(level, ref _context);
            public override bool GetMute() { v.GetMute(out bool m); return m; }
            public override void SetMute(bool muted) => v.SetMute(muted, ref _context);
        }

        private sealed class Channels(IChannelAudioVolume v, string id) : SessionVolume
        {
            /// <summary>Level before mute, per session, for as long as YRAWAH runs.</summary>
            private static readonly ConcurrentDictionary<string, float> Saved = new();

            private float[] Read()
            {
                v.GetChannelCount(out uint n);
                var levels = new float[n];
                if (n > 0) v.GetAllVolumes(n, levels);
                return levels;
            }

            private void Write(float level)
            {
                var levels = Read();
                if (levels.Length == 0) return;
                Array.Fill(levels, level);
                v.SetAllVolumes((uint)levels.Length, levels, ref _context);
            }

            public override float GetVolume()
            {
                if (GetMute()) return Saved[id];
                var levels = Read();
                return levels.Length == 0 ? 1f : levels.Max();
            }

            public override void SetVolume(float level)
            {
                Saved.TryRemove(id, out _);
                Write(level);
            }

            public override bool GetMute()
            {
                if (!Saved.ContainsKey(id)) return false;
                var levels = Read();
                if (levels.Length > 0 && levels.Max() > 0) { Saved.TryRemove(id, out _); return false; } // raised elsewhere
                return true;
            }

            public override void SetMute(bool muted)
            {
                if (muted == GetMute()) return;
                if (muted)
                {
                    var levels = Read();
                    Saved[id] = levels.Length == 0 ? 1f : levels.Max();
                    Write(0f);
                }
                else if (Saved.TryRemove(id, out float level)) Write(level);
            }
        }
    }

    // ---- names ------------------------------------------------------------

    private static string AppName(string? path, uint pid)
    {
        if (path is not null)
        {
            try
            {
                string? description = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(path);
        }
        try
        {
            // No path (a protected or elevated process): the process name still reads.
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            if (!string.IsNullOrWhiteSpace(process.ProcessName)) return process.ProcessName;
        }
        catch { }
        return $"PID {pid}";
    }

    private static string? ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return ImagePathWithoutOpening(pid); // elevated app (e.g. MSI LEDKeeper2): access denied
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : ImagePathWithoutOpening(pid);
        }
        finally { CloseHandle(h); }
    }

    /// <summary>
    /// Image path of any process without opening it: NtQuerySystemInformation
    /// (SystemProcessIdInformation, class 88), the way Task Manager names
    /// elevated processes. It comes back as \Device\HarddiskVolumeN\...; mapped to a drive letter.
    /// </summary>
    private static string? ImagePathWithoutOpening(uint pid)
    {
        const int SystemProcessIdInformation = 88;
        const int BufferBytes = 2048;
        IntPtr buffer = Marshal.AllocHGlobal(BufferBytes);
        try
        {
            var info = new SystemProcessIdInfo
            {
                ProcessId = (IntPtr)pid,
                ImageName = new UnicodeString { Length = 0, MaximumLength = BufferBytes, Buffer = buffer },
            };
            if (NtQuerySystemInformation(SystemProcessIdInformation, ref info, Marshal.SizeOf<SystemProcessIdInfo>(), out _) != 0) return null;
            string nt = Marshal.PtrToStringUni(info.ImageName.Buffer, info.ImageName.Length / 2);
            return DosPath(nt);
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>\Device\HarddiskVolume3\Program Files\x.exe -> C:\Program Files\x.exe</summary>
    private static string? DosPath(string ntPath)
    {
        foreach (var drive in Environment.GetLogicalDrives())
        {
            string letter = drive.TrimEnd('\\');
            var target = new StringBuilder(512);
            if (QueryDosDeviceW(letter, target, target.Capacity) == 0) continue;
            string device = target.ToString();
            if (ntPath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                return letter + ntPath[device.Length..];
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessIdInfo { public IntPtr ProcessId; public UnicodeString ImageName; }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, ref SystemProcessIdInfo info, int length, out int returned);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, StringBuilder target, int max);

    private static string? FriendlyName(IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return null;
            var key = new PropertyKey { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 }; // PKEY_Device_FriendlyName
            store.GetValue(ref key, out var value);
            try { return value.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.p) : null; }
            finally { PropVariantClear(ref value); }
        }
        catch { return null; }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    // ---- COM declarations (mmdeviceapi.h, endpointvolume.h, audiopolicy.h) --

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out int count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(int channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
        void GetChannelVolumeLevel(int channel, out float levelDb);
        void GetChannelVolumeLevelScalar(int channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid group);
        void SetGroupingParam(ref Guid group, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr events);
        void UnregisterAudioSessionNotification(IntPtr events);
        // IAudioSessionControl2
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession(); // S_OK (0) = yes, S_FALSE (1) = no
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IChannelAudioVolume
    {
        void GetChannelCount(out uint count);
        void SetChannelVolume(uint index, float level, ref Guid context);
        void GetChannelVolume(uint index, out float level);
        void SetAllVolumes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] levels, ref Guid context);
        void GetAllVolumes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0), Out] float[] levels);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F7DD2F8D6A6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid context);
        void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
