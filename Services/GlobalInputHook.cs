using System.ComponentModel;
using System.Runtime.InteropServices;
using DpiMusicControl.Core;

namespace DpiMusicControl.Services;

public sealed class GlobalInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;

    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly LowLevelMouseProc _mouseHookProc;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;

    public GlobalInputHook()
    {
        _mouseHookProc = MouseHookCallback;
        _keyboardHookProc = KeyboardHookCallback;
    }

    public event EventHandler<TriggerPressedEventArgs>? TriggerPressed;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        var moduleHandle = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, moduleHandle, 0);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, moduleHandle, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            Stop();
            throw new Win32Exception(errorCode, "Windows не дала приложению доступ к глобальному вводу.");
        }

        IsRunning = true;
    }

    public void Stop()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        IsRunning = false;
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && TryGetMouseTrigger(wParam, lParam, out var trigger))
        {
            var args = new TriggerPressedEventArgs(trigger);
            TriggerPressed?.Invoke(this, args);

            if (args.Suppress)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && TryGetKeyboardTrigger(wParam, lParam, out var trigger))
        {
            var args = new TriggerPressedEventArgs(trigger);
            TriggerPressed?.Invoke(this, args);

            if (args.Suppress)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static bool TryGetMouseTrigger(IntPtr wParam, IntPtr lParam, out InputTrigger trigger)
    {
        trigger = default;
        var message = wParam.ToInt32();

        if (message == WmMButtonDown)
        {
            trigger = InputTrigger.MiddleButton;
            return true;
        }

        if (message != WmXButtonDown)
        {
            return false;
        }

        var mouseInfo = Marshal.PtrToStructure<MsllHookStruct>(lParam);
        var button = (ushort)((mouseInfo.MouseData >> 16) & 0xFFFF);

        switch (button)
        {
            case 1:
                trigger = InputTrigger.XButton1;
                return true;
            case 2:
                trigger = InputTrigger.XButton2;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetKeyboardTrigger(IntPtr wParam, IntPtr lParam, out InputTrigger trigger)
    {
        trigger = default;
        var message = wParam.ToInt32();
        if (message is not WmKeyDown and not WmSysKeyDown)
        {
            return false;
        }

        var keyboardInfo = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
        const uint f13 = 0x7C;
        const uint f24 = 0x87;

        if (keyboardInfo.VirtualKey is < f13 or > f24)
        {
            return false;
        }

        trigger = (InputTrigger)((int)InputTrigger.F13 + (int)(keyboardInfo.VirtualKey - f13));
        return true;
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        Delegate callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}

public sealed class TriggerPressedEventArgs(InputTrigger trigger) : EventArgs
{
    public InputTrigger Trigger { get; } = trigger;

    public bool Suppress { get; set; }
}
