using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using static Scr.Native;

namespace Scr;

/// <summary>
/// Catches shortcuts that Windows keeps for itself (like Win + Shift + S, which opens Snipping Tool) with a
/// low-level keyboard hook, and swallows them so Windows never sees them. It runs on its own thread with its
/// own message loop, so a busy window can never make Windows drop the hook for being slow.
/// It is only installed while there is something to catch.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    readonly Dispatcher ui;
    readonly Action fire;
    volatile (int Mods, int Vk)[] targets = Array.Empty<(int, int)>();
    volatile bool paused;
    readonly HashSet<int> held = new(); // keys we swallowed on the way down, so we swallow them on the way up too
    Thread? thread;
    uint threadId;
    HookProc? proc; // kept alive while the hook is installed

    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_QUIT = 0x12;
    const int LLKHF_INJECTED = 0x10;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const ushort MaskKey = 0xE8; // an unassigned key, pressed after a swallowed Win shortcut so Windows doesn't open Start

    public KeyboardHook(Dispatcher ui, Action fire)
    {
        this.ui = ui;
        this.fire = fire;
    }

    public void SetTargets(IEnumerable<(int Mods, int Vk)> list)
    {
        var arr = new List<(int, int)>();
        foreach (var t in list) if (!arr.Contains(t)) arr.Add(t);
        targets = arr.ToArray();
        if (targets.Length > 0) Start(); else Stop();
    }

    public void Pause() => paused = true;
    public void Resume() => paused = false;

    void Start()
    {
        if (thread != null) return;
        var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            proc = Callback;
            IntPtr hook;
            using (var me = Process.GetCurrentProcess())
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(me.MainModule?.ModuleName), 0);
            ready.Set();
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        })
        { IsBackground = true, Name = "Scr keyboard hook" };
        thread.Start();
        ready.Wait(2000);
    }

    void Stop()
    {
        if (thread == null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
        thread = null;
        held.Clear();
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !paused)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            int vk = (int)k.vkCode;
            if ((k.flags & LLKHF_INJECTED) == 0)
            {
                if (up && held.Remove(vk)) return new IntPtr(1);
                if (down)
                {
                    foreach (var t in targets)
                    {
                        if (t.Vk != vk || CurrentMods() != t.Mods) continue;
                        if (held.Add(vk)) ui.BeginInvoke(fire); // key repeat only fires once
                        if ((t.Mods & MOD_WIN) != 0) PressMaskKey();
                        return new IntPtr(1);
                    }
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    static int CurrentMods()
    {
        int m = 0;
        if (Down(VK_CONTROL)) m |= MOD_CONTROL;
        if (Down(VK_MENU)) m |= MOD_ALT;
        if (Down(VK_SHIFT)) m |= MOD_SHIFT;
        if (Down(VK_LWIN) || Down(VK_RWIN)) m |= MOD_WIN;
        return m;
    }

    static void PressMaskKey()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1; inputs[0].u.ki.wVk = MaskKey;
        inputs[1].type = 1; inputs[1].u.ki.wVk = MaskKey; inputs[1].u.ki.dwFlags = 2; // key up
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => Stop();
}
