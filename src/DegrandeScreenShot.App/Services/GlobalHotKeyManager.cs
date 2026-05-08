using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DegrandeScreenShot.App.Services;

public sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private static int _nextHotKeyId = 0x1208;

    private readonly Window _window;
    private readonly ModifierKeys _modifierKeys;
    private readonly Key _key;
    private readonly Action _callback;
    private readonly int _hotKeyId;
    private HwndSource? _source;

    public GlobalHotKeyManager(Window window, ModifierKeys modifierKeys, Key key, Action callback)
    {
        _window = window;
        _modifierKeys = modifierKeys;
        _key = key;
        _callback = callback;
        _hotKeyId = _nextHotKeyId++;
    }

    public void Register()
    {
        var handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        if (!RegisterHotKey(handle, _hotKeyId, (uint)_modifierKeys, (uint)KeyInterop.VirtualKeyFromKey(_key)))
        {
            throw new InvalidOperationException($"Could not register the global hotkey {_modifierKeys} + {_key}. Another app may already be using it.");
        }
    }

    public void Dispose()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle != IntPtr.Zero)
        {
            UnregisterHotKey(handle, _hotKeyId);
        }

        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _hotKeyId)
        {
            _callback();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}