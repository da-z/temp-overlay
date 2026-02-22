# TempOverlay

Windows desktop temperature overlay for CPU/GPU with tray settings.

## Screenshot

![TempOverlay screenshot](TempOverlay/assets/sensor-overlay-preview.png)

## Features

- Always-on-top, click-through overlay
- CPU + GPU temperature readout
- Position presets (corners) with vertical/horizontal offsets
- Theme and font size options
- Tray menu controls and startup toggle

## Build

```powershell
dotnet publish TempOverlay\TempOverlay.csproj -c Release -r win-x64 --self-contained false -o release-small
```

Output is written to `release-small`.

## Credits

- Hardware sensor access is powered by [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor).

## License

This project is licensed under the MIT License. See `LICENSE` for details.
