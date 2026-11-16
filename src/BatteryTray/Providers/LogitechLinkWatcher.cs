using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// YRAWAH: listens (never writes) on Logitech dongles for "the device behind
/// me turned on/off" announcements, so a switched-off headset or mouse shows as
/// off without waiting for Refresh. A Lightspeed dongle stays plugged in, so
/// Windows itself never says anything.
///
///  - Centurion dongle (G PRO X 2): the bridge event [bridge, 0x00, 0x00, connected]
///    arrives as frame 51 05 00 bridge 00 00 01|00 on the 0xFFA0 collection.
///  - HID++ receiver: notification 0x41 (device connection) on the 0xFF00
///    collections. The receiver only sends it when wireless notifications are
///    on (Logitech G HUB turns them on); without them the mouse updates on Refresh.
///
/// Opened only during on-demand readings (like the passive listeners), closed on exit.
/// </summary>
internal sealed class LogitechLinkWatcher : IDisposable
{
    private const ushort Vid = 0x046D;

    private readonly object _gate = new();
    private readonly Dictionary<string, HidPassiveReader> _readers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A device behind this dongle (VID, PID) turned on or off. Reader thread.</summary>
    public event Action<ushort, ushort>? LinkChanged;

    public void Ensure(IReadOnlyList<HidCollection> collections)
    {
        var wanted = collections.Where(Watches).ToList();
        lock (_gate)
        {
            foreach (var path in _readers.Keys.ToList())
            {
                if (wanted.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)) && !_readers[path].DeviceLost) continue;
                _readers[path].Dispose();
                _readers.Remove(path);
            }
            foreach (var c in wanted.Where(c => !_readers.ContainsKey(c.Path)))
            {
                ushort pid = c.Pid;
                bool centurion = LogitechCenturion.Models.ContainsKey(pid);
                var reader = HidPassiveReader.Open(c, r =>
                {
                    if (centurion ? IsCenturionLinkEvent(r) : IsReceiverLinkEvent(r))
                    {
                        Log.Write($"Logitech {pid:X4}: aparelho {(LinkUp(r, centurion) ? "ligou" : "desligou")}.");
                        try { LinkChanged?.Invoke(Vid, pid); } catch (Exception ex) { Log.Error("LinkWatcher", ex); }
                    }
                });
                if (reader is not null) _readers[c.Path] = reader;
            }
        }
    }

    private static bool Watches(HidCollection c) =>
        c.Vid == Vid && HidRequest.CanAsk(c) && c.InputLen > 0 &&
        ((LogitechCenturion.Models.ContainsKey(c.Pid) && c.UsagePage == 0xFFA0 && c.Usage == 0x0001)
         || (LogitechProvider.IsReceiver(c.Pid) && c.UsagePage == 0xFF00 && c.Usage is 0x0001 or 0x0002));

    /// <summary>51 05 00 [bridge] 00 00 [0|1]: function 0, software id 0, 4 payload bytes.</summary>
    internal static bool IsCenturionLinkEvent(byte[] r) =>
        r.Length >= 7 && r[0] == 0x51 && r[1] == 0x05 && r[3] != 0x00 && r[3] != 0xFF && r[4] == 0x00 && r[5] == 0x00 && r[6] is 0 or 1;

    /// <summary>HID++ 1.0 notification 0x41: [0x10|0x11, slot, 0x41, protocol, flags...]; bit 6 of flags = link lost.</summary>
    internal static bool IsReceiverLinkEvent(byte[] r) =>
        r.Length >= 5 && r[0] is 0x10 or 0x11 && r[1] is >= 1 and <= 6 && r[2] == 0x41;

    private static bool LinkUp(byte[] r, bool centurion) => centurion ? r[6] == 1 : (r[4] & 0x40) == 0;

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var reader in _readers.Values) reader.Dispose();
            _readers.Clear();
        }
    }
}
