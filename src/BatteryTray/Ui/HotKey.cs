using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// YRAWAH: a system-wide shortcut that opens and closes the panel, like
/// clicking the tray icon. The combination is chosen in Settings and stored as
/// text ("Ctrl+Alt+B"; empty = off). RegisterHotKey on a hidden message window.
/// </summary>
internal sealed class HotKey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    private const int Id = 0xB0B;

    public const string Default = "Ctrl+Alt+B";

    private readonly HwndSource _window;
    private readonly Action _pressed;
    private bool _registered;
    private bool _paused;

    /// <summary>What is registered now ("" = nothing).</summary>
    public string Current { get; private set; } = "";

    public HotKey(Action pressed)
    {
        _pressed = pressed;
        _window = new HwndSource(new HwndSourceParameters("YRAWAHHotKey") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    /// <summary>Registers <paramref name="combo"/> in place of the current one. False = invalid or taken by another app (the old one stays).</summary>
    public bool Set(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) { Unregister(); Current = ""; return true; }
        if (Parse(combo) is not { } parsed) return false;
        if (string.Equals(combo, Current, StringComparison.OrdinalIgnoreCase) && _registered) return true;

        string previous = Current;
        Unregister();
        if (RegisterHotKey(_window.Handle, Id, parsed.Mods | MOD_NOREPEAT, parsed.Vk))
        {
            _registered = true;
            Current = combo;
            return true;
        }
        Log.Write($"Atalho {combo} nao registrado (outro app ja usa?), erro {Marshal.GetLastWin32Error()}.");
        if (previous.Length > 0 && Parse(previous) is { } old && RegisterHotKey(_window.Handle, Id, old.Mods | MOD_NOREPEAT, old.Vk)) _registered = true;
        return false;
    }

    /// <summary>While the shortcut is being recorded in Settings, pressing the old one must not close the panel.</summary>
    public void Pause(bool paused) => _paused = paused;

    private void Unregister()
    {
        if (_registered) UnregisterHotKey(_window.Handle, Id);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            if (_paused) return IntPtr.Zero;
            try { _pressed(); } catch (Exception ex) { Log.Error("Atalho", ex); }
        }
        return IntPtr.Zero;
    }

    // ---------------- text <-> keys ----------------

    private static readonly Key[] Modifiers =
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin, Key.System,
    };

    public static bool IsModifier(Key key) => Modifiers.Contains(key);

    /// <summary>"Ctrl+Alt+B" from what was pressed. Null for a lone modifier (still waiting for the key).</summary>
    public static string? Format(ModifierKeys mods, Key key, bool winDown)
    {
        if (IsModifier(key) || key == Key.None) return null;
        var parts = new List<string>();
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift");
        if (winDown || mods.HasFlag(System.Windows.Input.ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Readable form: "Ctrl+Alt+1" instead of "Ctrl+Alt+D1".</summary>
    public static string Display(string combo) =>
        string.IsNullOrWhiteSpace(combo) ? "" : string.Join("+", combo.Split('+').Select(p => p switch
        {
            { Length: 2 } when p[0] == 'D' && char.IsDigit(p[1]) => p[1..],
            "OemPlus" => "=", "OemMinus" => "-", "OemComma" => ",", "OemPeriod" => ".",
            _ => p,
        }));

    /// <summary>True when the combination has no Ctrl/Alt/Shift/Win: that key stops working in other apps.</summary>
    public static bool IsBare(string combo) => !combo.Contains('+');

    internal static (uint Mods, uint Vk)? Parse(string combo)
    {
        uint mods = 0;
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        foreach (var m in parts[..^1])
        {
            switch (m.ToLowerInvariant())
            {
                case "ctrl": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: return null;
            }
        }
        if (!Enum.TryParse<Key>(parts[^1], true, out var key) || IsModifier(key) || key == Key.None) return null;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        return vk == 0 ? null : (mods, (uint)vk);
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
