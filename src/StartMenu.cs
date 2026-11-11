using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Scr;

/// <summary>
/// Keeps a "Scr" shortcut in your own Start menu folder, so Scr can be found by pressing Win and typing its name
/// (and pinned). Nothing else is installed, and no admin rights are needed.
/// </summary>
public static class StartMenu
{
    /// <summary>A test copy (SCR_DATA set) keeps its shortcut in its own folder instead of the real Start menu.</summary>
    static string Folder => Environment.GetEnvironmentVariable("SCR_DATA") is { Length: > 0 } test
        ? Path.Combine(test, "StartMenu")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

    public static string ShortcutPath => Path.Combine(Folder, "Scr.lnk");

    /// <summary>Adds the shortcut, or points it at this Scr.exe if the folder has moved. Removes it when switched off.</summary>
    public static void Sync(bool wanted)
    {
        try
        {
            if (!wanted)
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                return;
            }
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Scr.exe");
            if (File.Exists(ShortcutPath) && string.Equals(TargetOf(ShortcutPath), exe, StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(Folder);
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetDescription("Take a screenshot and frame it");
            link.SetIconLocation(exe, 0);
            ((IPersistFile)link).Save(ShortcutPath, true);
            Marshal.ReleaseComObject(link);
        }
        catch { /* the Start menu folder isn't essential */ }
    }

    public static string? TargetOf(string lnk)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            Marshal.ReleaseComObject(link);
            return sb.ToString();
        }
        catch { return null; }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
