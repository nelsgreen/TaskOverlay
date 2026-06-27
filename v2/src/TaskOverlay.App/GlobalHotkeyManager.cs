using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace TaskOverlay.App;

internal enum GlobalHotkeyAction
{
    CreateTasksFromLines,
    CreateSingleTask,
    CreateTaskWithDescription,
    QuickAddTask,
    ToggleOverlay,
    CycleOverlayMode,
    SelectWorkingMode,
    SelectPinnedMode,
    SelectCollapsedHandleMode
}

internal sealed class GlobalHotkeyManager : Forms.NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyWindow = new(-3);

    private readonly Dictionary<int, HotkeyRegistration> _registrations = new();
    private bool _disposed;

    public GlobalHotkeyManager()
    {
        CreateHandle(new Forms.CreateParams
        {
            Caption = "TaskOverlayV2Hotkeys",
            Parent = MessageOnlyWindow
        });
    }

    public event Action<GlobalHotkeyAction>? HotkeyPressed;

    public bool Register(
        int id,
        Forms.Keys key,
        GlobalHotkeyAction action,
        out string? error)
    {
        error = null;

        if (_disposed)
        {
            error = "The hotkey manager is already disposed.";
            return false;
        }

        if (_registrations.ContainsKey(id))
        {
            error = $"Hotkey ID {id} is already registered by TaskOverlay.";
            return false;
        }

        var modifiers = ModControl | ModAlt | ModNoRepeat;
        if (!RegisterHotKey(Handle, id, modifiers, (uint)key))
        {
            var win32Error = Marshal.GetLastWin32Error();
            error = new Win32Exception(win32Error).Message;
            return false;
        }

        _registrations.Add(
            id,
            new HotkeyRegistration(id, action));
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var registration in _registrations.Values)
        {
            UnregisterHotKey(Handle, registration.Id);
        }

        _registrations.Clear();
        HotkeyPressed = null;
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == WmHotkey &&
            _registrations.TryGetValue(message.WParam.ToInt32(), out var registration))
        {
            HotkeyPressed?.Invoke(registration.Action);
            return;
        }

        base.WndProc(ref message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    private sealed record HotkeyRegistration(
        int Id,
        GlobalHotkeyAction Action);
}
