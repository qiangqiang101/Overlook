# Theme format (v2)

`theme.json` sits in its own folder (an optional `fonts/` subfolder can carry the theme's fonts). **Images are embedded in the JSON** as `data:image/png;base64,...` strings (PNG, JPEG, GIF - animated GIFs play -, BMP, WebP); relative paths from older v2 files are still read. Colours are `#AARRGGBB` (or `#RRGGBB`). Fonts are in points at 96 dpi.
`elements` is ordered **back to front**; `children` are drawn above their parent, clipped to it, positioned relative to it.

Top level: `formatVersion`, `name`, `author`, `version`, `tags`, `width`, `height`, `backgroundColor`, `textColor`, `backgroundImage`, `preview`, `updateInterval` (ms), `opacity`, `rgb {enabled}`, `elements`.

Every element: `type`, `name` (unique), `x`, `y`, `width`, `height`, `visible`, `backColor`, `sensor {kind, param}`, `children`.

| type | extra fields |
|---|---|
| `text` | font, foreColor, text, beforeText, textAlign, autoSize, autoEllipsis, rightToLeft, borderStyle, image, imageAlign, allowUserEdit |
| `customText` | title/value/unit, their colors, fonts, aligns, widths, offsets, autoWidth |
| `image` | image, sizeMode, backgroundImage, backgroundImageLayout, borderStyle, enableDynamicImages, dynamicImages[], allowUserEdit |
| `bar` | text, font, foreColor, foregroundColor, texture*, minimum, maximum, fillDirection, showValue, unit |
| `circularBar` | style, progressShape, trackColor, progressColor(2), gradientMode, thickness, startAngle, barWidth, textMode, subText, unit |
| `chart` | scaleMode, borderStyle, style {grid/line pens, gradients, showAverageLine...} |
| `detail` | group, font, foreColor, drawBorder |
| `youtube` | youtubeId, play |
| `audio` | barCount, barSpacing, scalingStrategy (Decibel/Linear/Sqrt), direction (Top/Bottom/Center), barStyle (Bar/Line), lineCap, colorStyle (Solid/Rainbow/Gradient), speed, colors[7], backgroundImage |

### Sensors
A binding is `{ "kind": "lhm", "param": "ROGPC2/Intel Core i5-14600K/Load/CPU Total" }`: the sensor's path as LibreHardwareMonitor's tree shows it
(`Computer/Hardware/[Chip/]Group/Name`). A theme made on another PC still works if the hardware names match (the computer name is ignored when it doesn't).
An LHM sensor id such as `/intelcpu/0/load/0` is accepted as well.

A few values don't come from one LibreHardwareMonitor sensor and keep a `kind` (`param` as noted): `longDate`, `shortDate`, `longTime`, `shortTime`,
`customDateTime` (.NET format), `systemUptime`, `ping` (host), `hddTotalSize` / `hddTotalFreeSpace` / `hddUsage` (drive), `displayScreenResolution`,
`displayRefreshRate`, `displayFramerate`, `displayFrametime`, `cpuCoreCount`, `ramTotal`, `ramClockSpeed`, `ramVirtualTotal` and the `weather*` kinds.

The 1.x sensor names (`cpuTemperatureC`, `gpuLoadPercent`, ...) no longer exist. Importing a 1.x theme asks you to choose the LibreHardwareMonitor
sensor for each one (with a suggestion taken from your PC); a theme file that still contains a 1.x name is refused with a message saying so.

## RGB background

`"rgb": {"enabled": true}` puts the live RGB lighting behind the theme, using the UDP stream of RGB Wallpaper (SignalRGB / OpenRGB / RGBJunkie plugin, default port 8133). The theme's `backgroundImage` is drawn over it as a mask, so make the areas that should glow transparent. How the lighting looks (LED matrix, shape, blur, offline effect, cover image) is an app setting, not part of the theme, and is overwritten by the plugin's own settings.

The port is opened with address reuse, but Windows only lets two programs share a UDP port when both do that. RGB Wallpaper currently opens it exclusively, so run only one of them on the same port until it is changed to reuse the address (set `ExclusiveAddressUse = false` and `SocketOptionName.ReuseAddress` before binding).

