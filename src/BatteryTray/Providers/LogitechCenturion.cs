using System.Collections.Concurrent;
using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Logitech "Centurion" headsets (G PRO X 2 LIGHTSPEED, PID 0x0AF7): not HID++,
/// but 64-byte frames with report id 0x51 on the dongle's 0xFFA0 collection.
/// The dongle bridges requests to the headset over the radio.
///
/// Protocol from HeadsetControl (lib/devices/logitech_gpro_x2_lightspeed.hpp and
/// protocols/logitech_centurion_protocol.hpp). Only read questions:
///   1. Root.getFeature(0x0003) on the dongle  -> index of the bridge feature
///   2. through the bridge, Root.getFeature(0x0104) on the headset -> battery feature index
///   3. through the bridge, battery function 0  -> [percent, ?, charging state]
/// The two indexes are cached per dongle, so later readings ask once.
/// Older firmware without feature 0x0104 answers a fixed "legacy" battery question.
/// </summary>
internal static class LogitechCenturion
{
    private const ushort Vid = 0x046D;
    private const byte ReportId = 0x51;
    private const byte SoftwareId = 0x01;
    private const byte BridgeSend = 0x10 | SoftwareId;
    private const ushort BridgeFeature = 0x0003;
    private const ushort BatteryFeature = 0x0104;
    private static readonly TimeSpan DirectTimeout = TimeSpan.FromMilliseconds(1500);
    // The bridge answers after the headset does, over the radio: give it longer.
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromMilliseconds(2500);

    public static readonly Dictionary<ushort, string> Models = new()
    {
        [0x0AF7] = "Logitech G PRO X 2 LIGHTSPEED",
    };

    /// <summary>Dongle path -> (bridge index, battery index on the headset; 0 = legacy battery).</summary>
    private static readonly ConcurrentDictionary<string, (byte Bridge, byte Battery)> Indexes = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<DeviceEntry> Read(IReadOnlyList<HidCollection> collections)
    {
        foreach (var group in collections.Where(c => c.Vid == Vid && Models.ContainsKey(c.Pid)).GroupBy(c => c.Pid))
        {
            var target = group.FirstOrDefault(c => HidRequest.CanAsk(c) && c.UsagePage == 0xFFA0 && c.Usage == 0x0001
                                                   && c.OutputLen >= 64 && c.InputLen >= 64);
            BatteryReading reading;
            if (target is null)
                reading = BatteryReading.NoData(L.T("não achei o canal de bateria deste fone", "couldn’t find this headset’s battery channel"));
            else
            {
                using var session = HidRequest.Open(new[] { target });
                reading = session is null
                    ? BatteryReading.NoData(L.T("não consegui abrir o receptor", "couldn’t open the receiver"))
                    : ReadBattery(session, target);
            }

            yield return new DeviceEntry
            {
                Key = $"logitech:{group.Key:X4}",
                DisplayName = Models[group.Key],
                HardwareId = Known.Id(Vid, group.Key),
                ProviderName = "Logitech",
                Reading = reading,
                IsConnected = true,
                Kind = DeviceKind.Headset,
                Origin = "Dongle",
            };
        }
    }

    private static BatteryReading ReadBattery(HidRequest session, HidCollection target)
    {
        var off = BatteryReading.NoData(L.T("fone desligado ou longe do receptor; ligue e atualize", "headset off or far from the receiver; turn it on and refresh"));
        var silent = BatteryReading.NoData(L.T("não respondeu; ligue o fone e atualize", "didn’t answer; turn the headset on and refresh"));

        if (!Indexes.TryGetValue(target.Path, out var idx))
        {
            // 1. Where is the bridge on the dongle?
            var reply = session.Ask(target, DirectFrame(0x00, 0x00, Hi(BridgeFeature), Lo(BridgeFeature)), IsRootReply, DirectTimeout);
            if (reply is null) return silent;
            byte bridge = reply[5];
            if (reply[3] == 0xFF || bridge == 0) return BatteryReading.NoData(L.T("receptor sem a ponte para o fone", "receiver without the headset bridge"));

            // 2. Where is the battery feature on the headset?
            var lookup = AskBridge(session, target, bridge, 0x00, 0x00, Hi(BatteryFeature), Lo(BatteryFeature));
            if (lookup.Offline) return off;
            if (lookup.Params is null) return silent;
            idx = (bridge, lookup.Params.Length > 0 ? lookup.Params[0] : (byte)0);
            Indexes[target.Path] = idx;
        }

        if (idx.Battery == 0) return Legacy(session, target) ?? off;

        // 3. The battery.
        var battery = AskBridge(session, target, idx.Bridge, idx.Battery, 0x00);
        if (battery.Offline) return off;
        if (battery.Params is null) { Indexes.TryRemove(target.Path, out _); return silent; } // look the indexes up again next time
        return ParseBattery(battery.Params) ?? off;
    }

    // ---------------- frames ----------------

    private static byte Hi(ushort v) => (byte)(v >> 8);
    private static byte Lo(ushort v) => (byte)(v & 0xFF);

    /// <summary>[0x51, length, flags, payload...] padded to 64; length counts the flags byte.</summary>
    internal static byte[] Frame(params byte[] payload)
    {
        var frame = new byte[64];
        frame[0] = ReportId;
        frame[1] = (byte)(payload.Length + 1);
        Array.Copy(payload, 0, frame, 3, Math.Min(payload.Length, 61));
        return frame;
    }

    /// <summary>A request to a dongle feature: [feature index, function | software id, params...].</summary>
    internal static byte[] DirectFrame(byte feature, byte function, params byte[] args) =>
        Frame(new[] { feature, (byte)((function & 0xF0) | SoftwareId) }.Concat(args).ToArray());

    /// <summary>A request to a headset feature, wrapped for the dongle's bridge.</summary>
    internal static byte[] BridgeFrame(byte bridge, byte subFeature, byte function, params byte[] args)
    {
        var sub = new[] { (byte)0x00, subFeature, (byte)((function & 0xF0) | SoftwareId) }.Concat(args).ToArray();
        return Frame(new[] { bridge, BridgeSend, (byte)((sub.Length >> 8) & 0x0F), (byte)(sub.Length & 0xFF) }.Concat(sub).ToArray());
    }

    /// <summary>Root.getFeature reply on the dongle (or its error frame, feature byte 0xFF).</summary>
    internal static bool IsRootReply(byte[] r) =>
        r.Length >= 6 && r[0] == ReportId && ((r[3] == 0x00 && r[4] == SoftwareId) || (r[3] == 0xFF && r[4] == 0x00));

    internal enum BridgeKind { Other, Ack, Answer, Rejected, Disconnected }

    /// <summary>What a frame from the dongle means for a bridged request to <paramref name="subFeature"/>.</summary>
    internal static BridgeKind Classify(byte[] r, byte bridge, byte subFeature)
    {
        if (r.Length < 8 || r[0] != ReportId) return BridgeKind.Other;
        // p = payload: frame bytes from index 3.
        byte p0 = r[3], p1 = r[4], p2 = r[5], p3 = r[6];
        if (p0 == 0xFF && p1 == bridge && p2 == BridgeSend) return BridgeKind.Rejected;   // no headset behind the bridge
        if (p0 != bridge) return BridgeKind.Other;
        if (p1 == 0x00) return p3 == 0x00 ? BridgeKind.Disconnected : BridgeKind.Other;  // connection event
        if (p1 >> 4 != 0x1) return BridgeKind.Other;
        if ((p1 & 0x0F) == SoftwareId) return BridgeKind.Ack;
        if ((p1 & 0x0F) != 0x00 || r.Length < 10 || r[7] != 0x00) return BridgeKind.Other;
        if (r[8] == subFeature) return BridgeKind.Answer;
        return r[8] == 0xFF && r[9] == subFeature ? BridgeKind.Rejected : BridgeKind.Other;
    }

    private sealed record BridgeResult(byte[]? Params, bool Offline);

    private static BridgeResult AskBridge(HidRequest session, HidCollection target, byte bridge, byte subFeature, byte function, params byte[] args)
    {
        var reply = session.Ask(target, BridgeFrame(bridge, subFeature, function, args),
            r => Classify(r, bridge, subFeature) is BridgeKind.Answer or BridgeKind.Rejected or BridgeKind.Disconnected, BridgeTimeout);
        if (reply is null) return new(null, false);
        return Classify(reply, bridge, subFeature) switch
        {
            BridgeKind.Answer => new(BridgeParams(reply), false),
            _ => new(null, true),
        };
    }

    /// <summary>Parameters of a bridged answer: after [bridge, fn, size, size, 0, sub feature, fn].</summary>
    internal static byte[] BridgeParams(byte[] r)
    {
        int length = r[1] - 1;                 // payload bytes after the flags byte
        int end = Math.Min(r.Length, 3 + length);
        return end > 10 ? r[10..end] : Array.Empty<byte>();
    }

    /// <summary>[percent, ?, state]: state 1 or 2 = charging.</summary>
    internal static BatteryReading? ParseBattery(byte[] p)
    {
        if (p.Length == 0 || p[0] > 100) return null;
        bool charging = p.Length >= 3 && p[2] is 1 or 2;
        return BatteryReading.Percentage(p[0], chargeText: charging ? L.T("carregando", "charging") : null);
    }

    // ---------------- older firmware ----------------

    internal static readonly byte[] LegacyRequest = Pad(ReportId, 0x08, 0x00, 0x03, 0x1A, 0x00, 0x03, 0x00, 0x04, 0x0A);

    private static byte[] Pad(params byte[] b) { var f = new byte[64]; b.CopyTo(f, 0); return f; }

    internal static bool IsLegacyBattery(byte[] r) => r.Length >= 13 && r[0] == ReportId && r[1] == 0x0B && r[8] == 0x04;
    internal static bool IsLegacyPowerOff(byte[] r) => r.Length >= 7 && r[0] == ReportId && r[1] == 0x05 && r[6] == 0x00;

    private static BatteryReading? Legacy(HidRequest session, HidCollection target)
    {
        var reply = session.Ask(target, LegacyRequest, r => IsLegacyBattery(r) || IsLegacyPowerOff(r), BridgeTimeout);
        return reply is null || !IsLegacyBattery(reply) ? null : ParseLegacy(reply);
    }

    internal static BatteryReading? ParseLegacy(byte[] r) =>
        r[10] > 100 ? null : BatteryReading.Percentage(r[10], chargeText: r[12] == 0x02 ? L.T("carregando", "charging") : null);
}
