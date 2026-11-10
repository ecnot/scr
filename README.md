<p align="center">
  <img src="docs/icon.png" width="96" alt="Scr icon">
</p>

<h1 align="center">Scr</h1>

<p align="center">
  A small, fast screenshot app for Windows that makes your captures look good.<br>
  Named after the <kbd>Prt Scr</kbd> key.
</p>

<p align="center">
  <a href="https://github.com/ecnot/scr/releases/latest"><b>Download the latest version</b></a>
</p>

<p align="center">
  <img src="docs/screenshots/framed-ocean-linear.png" width="760" alt="A screenshot framed by Scr, with rounded corners, a soft shadow and a blue gradient backdrop">
</p>

Press <kbd>Print Screen</kbd> or <kbd>Win</kbd> + <kbd>Shift</kbd> + <kbd>S</kbd>, pick an area or a window, and the
screenshot is on your clipboard straight away, just like Snipping Tool. If you want it to look nice, frame it in the
editor: rounded corners, a soft shadow, some room around it and a backdrop. Then press Copy (or Enter) to put the framed one
on the clipboard instead.

Scr is meant to replace Snipping Tool for everyday use. It waits in the tray using only a few MB of memory, and taking a
capture is instant.

## The editor

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/editor-dark.png">
  <img src="docs/screenshots/editor-light.png" alt="The Scr editor: captures as tabs at the top, the framed screenshot with the tool bar, and the frame controls on the right">
</picture>

Every capture opens as a **tab** in one editor window, so you can take a few and frame them one after another.
Each tab keeps its own frame and edits. <kbd>Ctrl</kbd> + <kbd>Tab</kbd> switches tabs, and <kbd>Ctrl</kbd> + <kbd>W</kbd> or the tab's × closes one.

- **Frame:** Backdrop (the room around it), Corners, Inset and Inner corners. The inset can be **Glass** (see-through and
  frosted over the backdrop, with a bright rim) or **Edge** (the screenshot's own edge colour). **Auto** picks Edge when the
  screenshot's edge is one plain colour, and Glass when it's colourful. With Glass, the **Frost** slider goes from clear glass to milky, blurred frosted glass.
- **Shadow:** Strength and Size, from a short close shadow to a big soft one.
- **Backdrop:** a solid colour (or see-through), a gradient drawn as Mesh, Linear or Radial (with Swap and Shuffle), or a picture: a blurred copy of the screenshot itself, or one of your own.
  The first gradient, with the little sparkle, is **From the screenshot**: colours picked from your capture so the backdrop always suits it.
- **Size:** Fit, 16:9, 4:3 or 1:1, for posts and slides.
- **Plain** takes the frame off, and clicking it again puts the frame back. Scr remembers your last frame for next time.
- **Copy** (<kbd>Enter</kbd> or <kbd>Ctrl</kbd> + <kbd>C</kbd>) and **Save…** (<kbd>Ctrl</kbd> + <kbd>S</kbd>, PNG).
- **Closed it too soon?** The history button next to Save lists your last 10 closed captures with the time they were taken.
  Pick one to get it back with its frame and edits. <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>T</kbd> and the tray menu bring back the most recent one.

### Tools

The small bar on the picture has four tools. Nothing happens until you click one; click it again or press <kbd>Esc</kbd> to switch it off.

- **Redact** (<kbd>R</kbd>): drag over names, numbers or faces to **pixelate** or **blur** them. It's baked into the pixels, so it can't be undone by whoever you send it to.
- **Crop** (<kbd>C</kbd>): drag the part to keep. Click Crop again to change it, or Reset to get the whole capture back.
- **Highlight** (<kbd>H</kbd>): a **box** outline or a see-through **marker**, in red, yellow, green or blue.
- **Spotlight** (<kbd>S</kbd>): dims everything except the parts you drag.

**Undo** (<kbd>Ctrl</kbd> + <kbd>Z</kbd>) and **Redo** (<kbd>Ctrl</kbd> + <kbd>Y</kbd>) step through all of them.

<p align="center">
  <img src="docs/screenshots/framed-spotlight-crop.png" width="49%" alt="A cropped capture with a spotlight and a yellow marker">
  <img src="docs/screenshots/framed-auto-linear.png" width="49%" alt="A backdrop with colours picked from the screenshot">
</p>
<p align="center">
  <img src="docs/screenshots/framed-glass-dusk.png" width="49%" alt="A glass inset over a dusk gradient">
  <img src="docs/screenshots/framed-sunset-wide.png" width="49%" alt="Framed on a sunset gradient at 16:9">
</p>

### Pictures you already have

Frame any picture, not just captures: press <kbd>Ctrl</kbd> + <kbd>O</kbd> or drop picture files on the editor, or paste
one with <kbd>Ctrl</kbd> + <kbd>V</kbd>. The tray menu has **Open a picture…** and **Frame the clipboard picture** too.
Phone photos come in the right way up, and pictures with see-through parts keep them.

## Capturing

When you press the shortcut, the screen freezes and dims. Then:

- **drag** to pick an area,
- **click** a window to take just that window: the whole window, without the border or anything in front of it,
- press <kbd>Enter</kbd> to take the whole screen,
- or press <kbd>Esc</kbd> or right-click to cancel.

It works across several monitors, each with its own scaling, and captures in real pixels.

**Full-screen games and videos are left alone.** A full-screen game minimises if another window takes focus, so when
something fills the screen, Scr quietly takes the whole screen instead. The game keeps running, the screenshot is on your
clipboard, and a small notice lets you open it in the editor whenever you like.

Click the tray icon to capture too. Right-click it to capture the whole screen, open the editor, open or paste a picture,
open your screenshots folder, change Settings or quit. Starting Scr again while it's running also takes a capture, so a
pinned Scr icon works as a capture button.

## Settings

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/settings-dark.png">
  <img src="docs/screenshots/settings-light.png" width="420" alt="Scr settings">
</picture>

- **Shortcut:** <kbd>Print Screen</kbd> to start with. Click it and press the keys you'd like instead.
- **Win + Shift + S opens Scr:** takes the Snipping Tool shortcut over from Windows (on to start with).
- **Copy the screenshot straight away**, **Close the editor after Copy** and **Leave full-screen games alone.**
- **Appearance:** the same eight themes as Kalendar (Blossom, Peach, Honey, Meadow, Glacier, Lavender, Dusk, Slate),
  Same as Windows, Light or Dark, and a five-step **Transparency** slider from Clear to Solid.
- **Check for updates:** see [Updates](#updates).
- Where **Save…** starts (Pictures\Screenshots to start with), and **Start with Windows**.

### Print Screen and Snipping Tool

Windows 11 gives the Print Screen key to Snipping Tool. To use it for Scr, turn off
**Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture"**.
Scr's Settings has a link straight there.

Windows keeps <kbd>Win</kbd> + <kbd>Shift</kbd> + <kbd>S</kbd> for itself, so Scr catches it with a low-level keyboard
hook that sees those keys before Windows does. The hook ignores every other key, and it only runs while there's
something to catch. One limit: while an app running as administrator is in front, Windows doesn't pass keys to Scr,
so Snipping Tool may open instead.

## Install

1. Scr needs the free **.NET 10 Desktop Runtime**. If you don't have it:
   ```
   winget install Microsoft.DotNet.DesktopRuntime.10
   ```
2. Download **Scr-x.y.z.zip** from [the latest release](https://github.com/ecnot/scr/releases/latest), unzip it
   anywhere you like (for example `C:\Apps\Scr`), and run **Scr.exe**.

The first time, Scr opens Settings with a short welcome. After that it waits in the tray, by the clock.
Turn on Start with Windows to have it there every day.

- `Scr.exe --tray` starts it quietly in the tray (this is what Start with Windows uses).
- `Scr.exe --quit` closes a running Scr.

### Updates

Scr updates itself. About once a day it asks GitHub whether there's a newer release, and if there is, a small notice
says so. Click it (or **Update** in the tray menu or Settings): Scr downloads the zip from this project's releases,
checks it against GitHub's checksum, swaps in the new files and starts again, with your settings just as they were.
Turn **Check for updates** off in Settings if you'd rather update by hand. Versions before 0.3.0 need to be updated by
hand once.

### Private

Your screenshots stay on your PC and on your clipboard; Scr never sends them anywhere. The only thing it ever contacts
is GitHub, to check for a new version and download it, and that request contains nothing about you. Its settings are in
`%APPDATA%\Scr\settings.json`, and Start with Windows is a value named "Scr" under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Build it yourself

You need the free .NET 10 SDK:

```
winget install Microsoft.DotNet.SDK.10
```

Then run **`build.bat`**. It builds into `dist\` (about half a megabyte) and closes a running Scr first.

Scr is written in C# with WPF on .NET 10, with no extra packages. The tray icon and the shortcuts use Win32 directly.

| File | What it does |
|---|---|
| `src/App.xaml.cs` | Startup, one Scr at a time, tray actions, giving memory back after a capture |
| `src/CaptureSession.cs`, `src/OverlayWindow.cs` | The frozen, dimmed screen: one overlay per monitor, in real pixels |
| `src/ScreenCapture.cs` | The screen copy, monitors and window outlines |
| `src/FrameRenderer.cs`, `src/Pixels.cs` | The frame: backdrop, shadow, corners, inset. The same drawing is shown live and rendered for Copy and Save |
| `src/EditorWindow.xaml(.cs)` | The editor: tabs, tools and frame controls |
| `src/SettingsWindow.xaml(.cs)` | Settings and the shortcut recorder |
| `src/TrayIcon.cs`, `src/Hotkey.cs` | Tray icon, menu and global shortcut |
| `src/KeyboardHook.cs` | Takes over Win + Shift + S, or a shortcut another app already has |
| `src/Redact.cs` | Pixelate and blur, highlights and spotlights, and the colours for "From the screenshot" |
| `src/Import.cs` | Opening, dropping and pasting pictures |
| `src/Theme.xaml`, `src/ThemeColors.cs`, `src/GlassWindow.cs` | The look: Kalendar's themes, frosted window, light and dark |
| `tools/make-icon.ps1` | Draws `src/Assets/scr.ico` |

A Debug build has `Scr.exe --preview <folder> <picture>`, which draws the editor, Settings and some framed examples
to PNG files without opening any windows. That's how the pictures on this page were made.

Scr is a sibling of [Kalendar](https://github.com/ecnot/kalendar), and it borrows Kalendar's Lavender colours.
