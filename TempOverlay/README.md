# TempOverlay (C# WinForms)

Always-on-top, click-through desktop overlay that shows CPU and GPU temperatures.

## Requirements

- Windows 10/11
- Visual Studio 2022 (or MSBuild with .NET Framework 4.8 targeting pack)

## Build

1. Open `TempOverlay/TempOverlay.csproj` in Visual Studio.
2. Build and run (`F5` or `Ctrl+F5`).

## Notes

- Temperature data is read from WMI namespace: `root\LibreHardwareMonitor`.
- Overlay refresh interval is 2 seconds.
