using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Ui;

/// <summary>
/// The panel's volume mixer: the default device's master volume plus one
/// slider per app playing sound, like the Windows volume mixer. Reads Windows
/// only while the panel is open; it never touches HID devices.
/// </summary>
public sealed class VolumeMixer
{
    public ObservableCollection<MixerRow> Rows { get; } = new();

    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-reads Windows and updates the rows in place (a slider being dragged keeps its row).</summary>
    public void Refresh()
    {
        CoreAudio.Master? master;
        List<CoreAudio.AppSession> apps;
        try { (master, apps) = CoreAudio.Read(_ => L.T("Sons do sistema", "System sounds")); }
        catch (Exception ex) { Log.Error("Mixer.Read", ex); Rows.Clear(); return; }

        if (master is null) { Rows.Clear(); return; }

        var wanted = new List<MixerRow>();
        wanted.Add(Upsert("master", r =>
        {
            r.Name = master.DeviceName.Length > 0 ? master.DeviceName : L.T("Volume geral", "Master volume");
            r.IsMaster = true;
            r.Glyph = ""; // Segoe Fluent/MDL2 "Volume"
            r.Bind(master.Volume, master.Muted,
                v => CoreAudio.SetVolume(master.Endpoint, v),
                m => CoreAudio.SetMute(master.Endpoint, m));
        }));
        foreach (var app in apps)
        {
            wanted.Add(Upsert(app.Key, r =>
            {
                r.Name = app.Name;
                r.Glyph = app.IsSystem ? "" : ""; // "Ringer" for system sounds
                r.Icon = app.ExePath is null ? null : IconFor(app.ExePath);
                var controls = app.Controls;
                r.Bind(app.Volume, app.Muted,
                    v => CoreAudio.SetVolume(controls, v),
                    m => CoreAudio.SetMute(controls, m));
            }));
        }

        // Same order as Windows: master first, then apps by name, system sounds last.
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Rows[i])) Rows.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = Rows.IndexOf(wanted[i]);
            if (at < 0) Rows.Insert(i, wanted[i]);
            else if (at != i) Rows.Move(at, i);
        }
    }

    private MixerRow Upsert(string key, Action<MixerRow> fill)
    {
        var row = Rows.FirstOrDefault(r => r.Key == key) ?? new MixerRow(key);
        fill(row);
        return row;
    }

    private ImageSource? IconFor(string exePath)
    {
        if (_icons.TryGetValue(exePath, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                image = source;
            }
        }
        catch (Exception ex) { Log.Error("Mixer.Icon", ex); }
        _icons[exePath] = image;
        return image;
    }
}

/// <summary>One slider of the mixer.</summary>
public sealed class MixerRow : INotifyPropertyChanged
{
    public MixerRow(string key) => Key = key;

    public string Key { get; }

    private Action<float>? _setVolume;
    private Action<bool>? _setMute;
    private DateTime _touchedAt;
    private string _name = "";
    private string _glyph = "";
    private ImageSource? _icon;
    private double _volume;
    private bool _muted;

    public bool IsMaster { get; set; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Glyph { get => _glyph; set => Set(ref _glyph, value); }

    public ImageSource? Icon
    {
        get => _icon;
        set { if (Set(ref _icon, value)) { Raise(nameof(HasIcon)); Raise(nameof(GlyphVisibility)); } }
    }

    public bool HasIcon => _icon is not null;
    public Visibility GlyphVisibility => HasIcon ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>0 to 100, as the slider shows it. Setting it changes the volume in Windows.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (!Set(ref _volume, value)) return;
            Raise(nameof(VolumeText));
            _touchedAt = DateTime.UtcNow;
            try { _setVolume?.Invoke((float)(value / 100)); } catch (Exception ex) { Log.Error("Mixer.SetVolume", ex); }
            // Like Windows: moving the slider of a muted app unmutes it.
            if (_muted && value > 0) Muted = false;
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            RaiseMuteLooks();
            _touchedAt = DateTime.UtcNow;
            try { _setMute?.Invoke(value); } catch (Exception ex) { Log.Error("Mixer.SetMute", ex); }
        }
    }

    public string VolumeText => $"{_volume:0}";
    public double ContentOpacity => _muted ? 0.45 : 1.0;
    public Visibility MutedBadge => _muted ? Visibility.Visible : Visibility.Collapsed;
    public string MuteTip => _muted ? L.T("Ativar som", "Unmute") : L.T("Silenciar", "Mute");

    /// <summary>Takes the values Windows reports, except right after you moved this slider yourself.</summary>
    internal void Bind(float volume, bool muted, Action<float> setVolume, Action<bool> setMute)
    {
        _setVolume = setVolume;
        _setMute = setMute;
        if (DateTime.UtcNow - _touchedAt < TimeSpan.FromSeconds(1.5)) return;

        double v = Math.Round(volume * 100);
        if (v != _volume) { _volume = v; Raise(nameof(Volume)); Raise(nameof(VolumeText)); }
        if (muted != _muted) { _muted = muted; RaiseMuteLooks(); }
    }

    private void RaiseMuteLooks()
    {
        Raise(nameof(Muted)); Raise(nameof(ContentOpacity)); Raise(nameof(MutedBadge)); Raise(nameof(MuteTip));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
