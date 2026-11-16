using System.Runtime.InteropServices;
using System.Windows.Interop;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// YRAWAH: a system-wide shortcut (Ctrl+Alt+B) that opens and closes the panel,
/// like clicking the tray icon. RegisterHotKey on a hidden message window; if
/// another app already owns the combination, it is logged and nothing else happens.
/// </summary>
internal sealed class HotKey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_B = 0x42;
    private const int Id = 0xB0B;

    public const string Label = "Ctrl+Alt+B";

    private readonly HwndSource _window;
    private readonly Action _pressed;
    private readonly bool _registered;

    public HotKey(Action pressed)
    {
        _pressed = pressed;
        _window = new HwndSource(new HwndSourceParameters("YRAWAHHotKey") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
        _registered = RegisterHotKey(_window.Handle, Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_B);
        if (!_registered) Log.Write($"Atalho {Label} nao registrado (outro app ja usa?), erro {Marshal.GetLastWin32Error()}.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            try { _pressed(); } catch (Exception ex) { Log.Error("Atalho", ex); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_window.Handle, Id);
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
