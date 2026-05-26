---
name: capture-overlay-multimonitor
description: "Use when: changing DegrandeScreenShot capture overlay, multi-monitor capture, virtual desktop bounds, screenshot DPI, editor 1:1 zoom, or monitor/DPI bugs."
---

# Capture Overlay Multi-Monitor

Use this skill before changing capture overlay behavior, virtual desktop capture bounds, screenshot DPI handling, or editor 1:1 rendering.

## Verified Fixes

- Starting capture from a secondary monitor can make a single WPF overlay window get born on that monitor and behave as if only that monitor is capturable.
- Preserve the working primary-monitor path by creating the overlay on the primary monitor first, briefly anchoring the cursor on primary during overlay creation, then restoring the cursor before interaction.
- Use Win32 `GetCursorPos` physical screen coordinates for overlay pointer tracking, translated by `CaptureFrame.VirtualLeft` and `CaptureFrame.VirtualTop`.
- Use Win32 `GetSystemMetrics(SM_XVIRTUALSCREEN / SM_YVIRTUALSCREEN / SM_CXVIRTUALSCREEN / SM_CYVIRTUALSCREEN)` for virtual desktop bounds instead of `System.Windows.Forms.Screen.AllScreens` when capture bounds must be physical and DPI-safe.
- Keep captured screenshot bitmaps normalized to 96 DPI. Do not rewrite captured images to the current monitor DPI before preview, editor, clipboard, or scrolling-capture display.
- In the editor, `1:1` must mean one image pixel maps to one physical screen pixel. Keep device-pixel snapping enabled and use nearest-neighbor bitmap rendering at the actual-size zoom level.

## Files To Check

- `src/DegrandeScreenShot.App/CaptureOverlayWindow.xaml.cs`
- `src/DegrandeScreenShot.App/Services/ScreenCaptureService.cs`
- `src/DegrandeScreenShot.App/EditorWindow.xaml`
- `src/DegrandeScreenShot.App/EditorWindow.xaml.cs`
- `src/DegrandeScreenShot.App/MainWindow.xaml.cs`

## Avoid Regressions

- Do not go back to WPF `Mouse.GetPosition` as the primary source for cross-monitor overlay coordinates.
- Do not derive virtual desktop capture size from WPF logical units.
- Do not apply monitor DPI metadata to screenshot bitmaps just to make them look correct in WPF; fix the viewer transform/rendering instead.
- After edits, run focused tests and restart the latest Release app before asking the user to test.
