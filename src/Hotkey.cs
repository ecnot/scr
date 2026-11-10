using System.Collections.Generic;
using System.Windows.Input;
using static Scr.Native;

namespace Scr;

public static class Hotkey
{
    public static string Format(int mods, int vk)
    {
        var parts = new List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int vk)
    {
        if (vk == 0x2C) return "Print Screen";
        var k = KeyInterop.KeyFromVirtualKey(vk);
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num " + (k - Key.NumPad0);
        return k switch
        {
            Key.Pause => "Pause",
            Key.Scroll => "Scroll Lock",
            Key.Insert => "Insert",
            Key.OemTilde => "§",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            _ => k.ToString(),
        };
    }

    public static int ModsFrom(ModifierKeys m)
    {
        int r = 0;
        if (m.HasFlag(ModifierKeys.Control)) r |= MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) r |= MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) r |= MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) r |= MOD_WIN;
        return r;
    }

    public static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>Keys that are fine on their own (they don't type anything).</summary>
    public static bool WorksAlone(Key k) => k is Key.Snapshot or Key.Pause or Key.Scroll or Key.Insert
        || (k >= Key.F1 && k <= Key.F24);
}
