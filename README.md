# Overlook

A sensor-panel app for small displays, rewritten for **.NET 10** and **Avalonia UI** supports **Windows and Linux**.

* Sensors are addressed by their LibreHardwareMonitor path, e.g. `ROGPC2/Intel Core i5-14600K/Load/CPU Total`, and come from a remote **LibreHardwareMonitor** (Options > Remote Web Server), default `http://127.0.0.1:8085/data.json`.
* Themes are **JSON** (`theme.json` + `assets/`). 1.x XML themes convert with the built-in importer or `bigbro-convert`.

## Run
    dotnet run --project src/BigBro.App
Pick a theme, press **Show panel**. Drag the panel to move it: it stays on screen and snaps flush to screen edges; right-click for options.
Open **Settings** for the LibreHardwareMonitor address (with a connection test), weather coordinates, language, start-with-system, tray and other options.

## Where things are stored

* **Your files** (settings, your own themes, fonts, languages) are in `%AppData%\Overlook` on Windows and `~/.config/Overlook` on Linux.
* **Sample themes** (NVIDIA) come with the program and are read from the `Themes` folder next to the executable. Overlook never copies them into your folder, so deleting one there removes it for good.
* **Themes from both places** show up in the list. If both have a theme with the same name, yours is used.
* **Editing a sample theme** and saving puts the edited copy in your own `Themes` folder, and the original stays untouched.
* **Portable Windows zip**: it contains a `portable.txt` file, so everything is kept in a `Data` folder beside the program instead of `%AppData%`. Delete `portable.txt` to use `%AppData%` again.

## Downloads

[Release](https://github.com/qiangqiang101/Overlook/releases)

## Theme editor
**Theme editor...** on the main window opens it. Click or drag elements on the canvas (handles resize), edit every property in the side panel,
the **File / Edit / Add / View / Tools** menus: add, duplicate, delete and reorder elements, nest elements under a parent, snap to a grid (toolbar: Snap to grid / Show grid / size), undo/redo (Ctrl+Z / Ctrl+Y), save (Ctrl+S) or save as a copy.
It previews with live data when the panel is connected, otherwise with a bundled sample PC.
**Importing 1.x themes lives here** (File menu: *Import 1.x themes* / *Import a 1.x Themes folder*). 1.x themes name hardware sensors ("CPU load"), so the import shows a window where you confirm or change the LibreHardwareMonitor sensor for each one; suggestions come from your PC when it can be reached. From a terminal:

    dotnet run --project src/BigBro.ThemeConverter -- "<old>\Themes" -o Themes --sensors http://192.168.0.8:8085/data.json

`--sensors` (a data.json file or URL of your PC) binds the old sensors to the matching ones there; without it they are left unbound.

Fonts a theme needs must be installed or placed in the `Fonts` folder (the importer copies them from a 1.x install when it finds one).
Format details: [docs/theme-format.md](docs/theme-format.md).

## Extra requirements
* **Audio visualizer** - Windows: nothing. Linux: `parec` (package `pulseaudio-utils`, or `pipewire-pulse`).
* **YouTube element** - Windows: WebView2 runtime. Linux: WebKitGTK (`libwebkit2gtk-4.1`). The video must allow embedding. The player is hosted on a tiny local web server (127.0.0.1) because YouTube rejects embeds without a referrer.
* **FPS / frametime** - MSI Afterburner + RivaTuner running on the *same* Windows PC as Overlook, with the Framerate counter enabled.

## Command-line helpers
    Overlook --render <theme folder> --out shot.png --sensors data.json [--samples 60] [--audio-demo] [--wait 500]
    Overlook --screenshot main|settings|editor --out shot.png [--theme NAME] [--select ELEMENT] [--data <data dir>]

## Limits
* Date/time, ping, uptime, drive sizes, screen resolution, refresh rate and FPS describe the machine the app runs on, not the monitored PC.
* RAM speed is derived from the DIMMs' SPD timings. Display refresh rate on Linux needs `xrandr`.
* Not ported: the HTTP broadcast server and the second-screen window (dropped on purpose).
* YouTube players are native web views, so they always sit on top of the drawn elements.
