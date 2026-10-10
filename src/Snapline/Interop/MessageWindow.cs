using System.Windows.Interop;
using System.Windows.Input;
using System.Runtime.InteropServices;
using static Snapline.Interop.Native;

namespace Snapline.Interop;

/// <summary>A hidden message-only window for hot keys and clipboard notifications.</summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    public IntPtr Handle => _source.Handle;

    public event Action<int, IntPtr, IntPtr>? Message;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("Snapline.Messages")
        {
            Width = 0, Height = 0, PositionX = 0, PositionY = 0,
            WindowStyle = 0,
            ParentWindow = HWND_MESSAGE,
        };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        Message?.Invoke(msg, wParam, lParam);
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}

/// <summary>A single global shortcut, through RegisterHotKey. No hooks, no permissions.</summary>
public sealed class HotKey : IDisposable
{
    private readonly MessageWindow _window;
    private readonly int _id;
    private static int _nextId = 0x4C49; // "LI"
    private Action<int, IntPtr, IntPtr>? _handler;
    public bool Registered { get; private set; }
    public int ErrorCode { get; private set; }
    public string Text { get; }
    private readonly uint _modifiers;
    public uint VirtualKey { get; }
    public bool IsSingleKey => _modifiers == 0;
    private readonly Action _action;
    private readonly bool _valid;
    private bool _disposed;

    public HotKey(MessageWindow window, string text, Action action)
    {
        _window = window;
        _id = _nextId++;
        Text = text;
        _action = action;
        // Do not override Windows-reserved combinations or Print Screen defaults.
        _valid = Parse(text, out var mods, out var vk) && (mods & MOD_WIN) == 0 && vk is not (0x2C or 0x7B);
        _modifiers = mods; VirtualKey = vk;
        SetActive(true);
    }

    /// <summary>Reuse the same registration ID as a contextual key enters/leaves its hover zone.</summary>
    public void SetActive(bool active)
    {
        if (_disposed) return;
        if (!active)
        {
            if (_handler is not null) { _window.Message -= _handler; _handler = null; }
            if (Registered) { UnregisterHotKey(_window.Handle, _id); Registered = false; }
            return;
        }
        if (Registered) return;
        if (!_valid) { ErrorCode = 87; return; }
        Registered = RegisterHotKey(_window.Handle, _id, _modifiers | MOD_NOREPEAT, VirtualKey);
        ErrorCode = Registered ? 0 : Marshal.GetLastWin32Error();
        if (!Registered) return;
        _handler = (msg, w, _) =>
        {
            if (msg == WM_HOTKEY && w.ToInt32() == _id) _action();
        };
        _window.Message += _handler;
    }

    /// <summary>Use virtual-key names so letters, digits, navigation and OEM keys round-trip alike.</summary>
    public static string? FromKey(Key key, ModifierKeys modifiers)
    {
        if (key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return null;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(((System.Windows.Forms.Keys)vk).ToString());
        return string.Join("+", parts);
    }

    public static bool Matches(string text, Key key, ModifierKeys modifiers)
    {
        var actual = FromKey(key, modifiers);
        return actual is not null && Parse(text, out var m, out var v) && Parse(actual, out var am, out var av) && m == am && v == av;
    }

    /// <summary>Parses "Ctrl+Alt+T", "Space", "A" and so on.</summary>
    public static bool Parse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "windows" or "meta": modifiers |= MOD_WIN; break;
                default:
                    if (Enum.TryParse<System.Windows.Forms.Keys>(raw, true, out var key)) vk = (uint)key;
                    else if (raw.Length == 1) vk = (uint)char.ToUpperInvariant(raw[0]);
                    else return false;
                    break;
            }
        }
        return vk != 0;
    }

    public void Dispose()
    {
        SetActive(false);
        _disposed = true;
    }
}
