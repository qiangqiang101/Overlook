"""Generates the sample themes that ship with Overlook (src/BigBro.App/Themes).

    python tools/generate_themes.py [--only Name ...] [--no-preview]

Needs Pillow. Previews are rendered with the built app: set OVERLOOK_APP to the path of Overlook.dll (or Overlook.exe);
without it the themes are written without a preview image.
Sensors are portable aliases, so the themes work on any PC; the previews use the bundled sample machine.
"""
import io
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(__file__))
from PIL import Image, ImageDraw, ImageFilter  # noqa: E402

from themelib import *  # noqa: E402,F401,F403
from themelib import _hg  # noqa: E402
from themelib import halo as soft_halo  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
THEMES_DIR = os.path.join(ROOT, 'src', 'BigBro.App', 'Themes')
SAMPLE = os.path.join(ROOT, 'src', 'BigBro.App', 'Assets', 'sample-lhm.json')

REGISTRY = {}


def theme(fn):
    REGISTRY[fn.__name__] = fn
    return fn


# =============================================================================== 1. Aurora


@theme
def Aurora():
    t = Theme('Aurora', 1920, 480, bg='#0c2236', tags=['rgb', 'glass', 'cyan', 'purple'], rgb=True)
    img = solid(1920, 480, '#050b14', 120)                       # dims the live RGB lighting, which shows through
    cards = [(30, 30, 610, 450), (650, 30, 1270, 450), (1310, 30, 1890, 450)]
    for box in cards:
        rounded_card(img, box, 32, fill=(255, 255, 255, 26), outline=(255, 255, 255, 70), width=2)
    t.image = img

    cyan, violet, pink, dim, white = '#7DF9FF', '#B388FF', '#FF9DE6', '#9FB4C8', '#FFFFFF'
    F = lambda s, b=True: font('Oxanium', s, b)

    def side(prefix, x0, c1, c2, title, load, temp, clock, third, third_label, third_sensor):
        t.add(label(f'{prefix}Title', x0 + 30, 50, 220, 44, title, F(22), c1))
        t.add(gauge(f'{prefix}Load', x0 + 25, 105, 260, lhm(load), c1, c2, '#33FFFFFF', 16, F(36), white, '%', sub='LOAD'))
        rows = [('TEMP', lhm(temp)), ('CLOCK', lhm(clock)), (third_label, third_sensor)]
        for i, (name, sensor) in enumerate(rows):
            y = 120 + i * 72
            t.add(label(f'{prefix}Row{i}Label', x0 + 305, y, 110, 40, name, F(15), dim))
            t.add(text(f'{prefix}Row{i}Value', x0 + 355, y - 4, 200, 48, '', sensor, F(24), white, 'middleRight'))
        t.add(chart(f'{prefix}Chart', x0 + 30, 385, 520, 48, lhm(load), c1, None, width=2))

    side('Cpu', 30, cyan, violet, 'CPU', '@CpuLoadPercent', '@CpuTemperatureC', '@CpuClockSpeed', None, 'POWER', lhm('@CpuPowerWattage'))
    side('Gpu', 1310, pink, cyan, 'GPU', '@GpuLoadPercent', '@GpuTemperatureC', '@GpuClockSpeed', None, 'VRAM', lhm('@GpuVramUsage'))

    # centre card: clock, date, weather, memory, network
    t.add(text('Time', 670, 52, 580, 150, '', special('customDateTime', 'HH:mm'), font('Oxanium', 104, True), white))
    t.add(text('Date', 670, 196, 580, 40, '', special('customDateTime', 'dddd, d MMMM'), F(21, False), cyan))
    t.add(label('UptimeLabel', 700, 246, 130, 36, 'UPTIME', F(14), dim))
    t.add(text('Uptime', 820, 246, 150, 36, '', special('systemUptime', r'd\.hh\:mm\:ss'), F(17, True), white, 'middleLeft'))
    t.add(label('PingLabel', 960, 246, 70, 36, 'PING', F(14), dim))
    t.add(text('Ping', 1020, 246, 200, 36, '', special('ping', '8.8.8.8'), F(15, True), white, 'middleRight'))
    t.add(label('MemoryLabel', 700, 304, 200, 30, 'MEMORY', F(14), dim))
    t.add(text('MemoryUsed', 950, 304, 270, 30, '', lhm('@RamUsage'), F(15), white, 'middleRight'))
    t.add(bar('MemoryBar', 700, 338, 520, 12, lhm('@RamLoadPercent'), cyan, '#33FFFFFF'))
    t.add(label('DownLabel', 700, 382, 90, 30, 'DOWN', F(14), dim))
    t.add(text('DownValue', 780, 378, 170, 36, '', lhm('@DownloadSpeed'), F(18), white, 'middleRight'))
    t.add(label('UpLabel', 980, 382, 70, 30, 'UP', F(14), dim))
    t.add(text('UpValue', 1050, 378, 170, 36, '', lhm('@UploadSpeed'), F(18), white, 'middleRight'))
    return t


# =============================================================================== 2. Neon Grid


@theme
def NeonGrid():
    t = Theme('Neon Grid', 1920, 480, bg='#10002b', tags=['synthwave', 'neon', 'retro', 'pink', 'cyan'])
    W, H, horizon = 1920, 480, 300
    img = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    img.paste(vgradient(W, horizon, [(0, '#0d0221'), (0.55, '#2d1b69'), (1, '#ff3d8b')]), (0, 0))
    img.paste(solid(W, H - horizon, '#12002b'), (0, horizon))

    # the sun: a gradient disc cut by horizontal slits, sitting on the horizon
    sun = layer(W, H)
    disc = vgradient(W, H, [(0.2, '#ffe066'), (0.62, '#ff2a9d')])
    mask = Image.new('L', (W, H), 0)
    ImageDraw.Draw(mask).ellipse((960 - 175, horizon - 175, 960 + 175, horizon + 175), fill=255)
    ImageDraw.Draw(mask).rectangle((0, horizon, W, H), fill=0)
    sun.paste(disc, (0, 0), mask)
    sd = ImageDraw.Draw(sun)
    for k in range(9):
        y = horizon - 150 + k * 16
        sd.rectangle((0, y, W, y + 2 + k), fill=(0, 0, 0, 0))
    img.alpha_composite(sun.filter(ImageFilter.GaussianBlur(0.6)))

    # perspective grid on the ground
    def grid(d):
        for i in range(1, 11):
            y = horizon + 180 * (i / 10) ** 2
            d.line([(0, y), (W, y)], fill=(255, 42, 157, 170), width=2)
        for x in range(-1920, 3841, 160):
            d.line([(960, horizon), (x, H)], fill=(0, 229, 255, 150), width=2)
    glow(img, grid, radius=5, strength=0.9)
    ImageDraw.Draw(img).line([(0, horizon), (W, horizon)], fill=(255, 120, 200, 255), width=3)
    t.image = img

    pink, cyan, white, violet = '#FF2A9D', '#00E5FF', '#FFFFFF', '#C9A7FF'
    F = lambda s, b=True: font('Oxanium', s, b)

    def side(prefix, x0, title, load, temp, clock, c1, c2):
        t.add(label(f'{prefix}Title', x0 + 6, 36, 280, 40, title, F(26), c1, 'middleCenter'))
        t.add(gauge(f'{prefix}Load', x0, 80, 290, lhm(load), c1, c2, '#33FFFFFF', 18, F(40), white, '%', sub='LOAD'))
        t.add(label(f'{prefix}TempLabel', x0 + 300, 130, 130, 28, 'TEMP', F(14), violet))
        t.add(text(f'{prefix}Temp', x0 + 300, 156, 190, 46, '', lhm(temp), F(30), white, 'middleLeft'))
        t.add(label(f'{prefix}ClockLabel', x0 + 300, 214, 130, 28, 'CLOCK', F(14), violet))
        t.add(text(f'{prefix}Clock', x0 + 300, 240, 190, 46, '', lhm(clock), F(26), white, 'middleLeft'))
        t.add(chart(f'{prefix}Chart', x0, 385, 490, 72, lhm(load), c1, None, width=3))

    side('Cpu', 70, 'CPU', '@CpuLoadPercent', '@CpuTemperatureC', '@CpuClockSpeed', cyan, pink)
    side('Gpu', 1360, 'GPU', '@GpuLoadPercent', '@GpuTemperatureC', '@GpuClockSpeed', pink, cyan)

    t.add(text('Time', 620, 96, 680, 140, '', special('customDateTime', 'HH:mm'), font('Oxanium', 96, True), white))
    t.add(text('Date', 620, 222, 680, 40, '', special('customDateTime', 'dddd  d MMM'), F(22, False), '#FFE9F6'))
    t.add(label('MemLabel', 720, 330, 140, 30, 'MEMORY', F(14), violet))
    t.add(text('MemValue', 1000, 330, 200, 30, '', lhm('@RamUsage'), F(16), white, 'middleRight'))
    t.add(bar('MemBar', 720, 364, 480, 12, lhm('@RamLoadPercent'), cyan, '#331A0033'))
    t.add(text('Down', 700, 400, 260, 40, '', lhm('@DownloadSpeed'), F(20), cyan, 'middleCenter', before='DOWN  '))
    t.add(text('Up', 960, 400, 260, 40, '', lhm('@UploadSpeed'), F(20), pink, 'middleCenter', before='UP  '))
    return t


# =============================================================================== 3. Minimal


@theme
def Minimal():
    t = Theme('Minimal', 480, 1920, bg='#000000', tags=['minimal', 'dark', 'mono', 'clean', 'portrait'])
    img = solid(480, 1920, '#000000')
    d = ImageDraw.Draw(img)
    for i in range(1, 5):
        d.line([(40, 384 * i), (440, 384 * i)], fill=(255, 255, 255, 38), width=1)
    t.image = img

    dim, white = '#7A7A7A', '#FFFFFF'
    B = lambda s, b=False: font('Bahnschrift', s, b)

    def row(i, name, caption, sensor, unit, sub_sensor, bar_sensor):
        y = 384 * i
        t.add(label(f'{name}Caption', 40, y + 40, 400, 34, caption, B(16), dim, 'middleRight'))
        t.add(metric(f'{name}Value', 40, y + 90, 400, 160, sensor, unit, B(96), B(28), white, dim, unit_w=76, value_y=0, unit_y=52))
        t.add(text(f'{name}Sub', 40, y + 258, 400, 44, '', sub_sensor, B(24), dim, 'middleRight'))
        t.add(bar(f'{name}Bar', 40, y + 322, 400, 4, bar_sensor, white, '#2A2A2A', direction='rightToLeft'))

    row(0, 'Cpu', 'PROCESSOR', lhm('@CpuLoadPercent'), '%', lhm('@CpuTemperatureC'), lhm('@CpuLoadPercent'))
    row(1, 'Gpu', 'GRAPHICS', lhm('@GpuLoadPercent'), '%', lhm('@GpuTemperatureC'), lhm('@GpuLoadPercent'))
    row(2, 'Ram', 'MEMORY', lhm('@RamLoadPercent'), '%', lhm('@RamUsage'), lhm('@RamLoadPercent'))

    y = 384 * 3
    t.add(label('NetCaption', 40, y + 40, 400, 34, 'NETWORK', B(16), dim, 'middleRight'))
    t.add(text('NetDown', 40, y + 96, 400, 84, '', lhm('@DownloadSpeed'), B(44), white, 'middleRight'))
    t.add(label('NetDownLabel', 40, y + 180, 400, 28, 'DOWNLOAD', B(13), dim, 'middleRight'))
    t.add(text('NetUp', 40, y + 226, 400, 84, '', lhm('@UploadSpeed'), B(44), white, 'middleRight'))
    t.add(label('NetUpLabel', 40, y + 310, 400, 28, 'UPLOAD', B(13), dim, 'middleRight'))

    y = 384 * 4
    t.add(label('TimeCaption', 40, y + 40, 400, 34, 'TIME', B(16), dim, 'middleRight'))
    t.add(text('Time', 40, y + 90, 400, 160, '', special('customDateTime', 'HH:mm'), B(96), white, 'middleRight'))
    t.add(text('Date', 40, y + 258, 400, 44, '', special('customDateTime', 'dddd d MMMM'), B(22), dim, 'middleRight'))
    return t


# =============================================================================== 4. Terminal


@theme
def Terminal():
    t = Theme('Terminal', 1024, 600, bg='#020a04', text='#33FF66', tags=['terminal', 'green', 'retro', 'crt'], interval=1000)
    W, H = 1024, 600
    img = solid(W, H, '#020a04')
    green_dim = (51, 255, 102, 70)
    d = ImageDraw.Draw(img)
    d.rectangle((16, 16, W - 17, H - 17), outline=green_dim, width=2)
    d.line([(16, 70), (W - 17, 70)], fill=green_dim, width=2)
    rows = [100 + 62 * i for i in range(6)]
    for y in rows:                                      # frames around each bar
        d.rectangle((234, y + 10, 646, y + 40), outline=green_dim, width=2)
    d.line([(760, 70), (760, H - 17)], fill=green_dim, width=2)
    vignette(img, 150)
    scanlines(img, 3, 55)
    t.image = img

    g, dim, amber = '#33FF66', '#1F9940', '#FFB000'
    O = lambda s, b=False: font('OCR A Extended', s, b)
    t.add(label('Title', 36, 24, 700, 40, '> OVERLOOK TERMINAL v2.0', O(18, True), g))
    t.add(text('Clock', 760, 20, 244, 44, '', special('customDateTime', 'HH:mm:ss'), O(20, True), amber, 'middleRight'))

    items = [('CPU LOAD', '@CpuLoadPercent', '%'), ('CPU TEMP', '@CpuTemperatureC', None), ('GPU LOAD', '@GpuLoadPercent', '%'),
             ('GPU TEMP', '@GpuTemperatureC', None), ('MEM USED', '@RamLoadPercent', '%'), ('VRAM', '@GpuMemoryPercent', '%')]
    for (name, alias, unit), y in zip(items, rows):
        key = name.replace(' ', '')
        t.add(label(f'{key}Label', 36, y + 4, 190, 40, name, O(16), g))
        t.add(bar(f'{key}Bar', 238, y + 14, 404, 22, lhm(alias), g, '#0A2A12'))
        t.add(text(f'{key}Value', 650, y + 4, 100, 40, '', lhm(alias), O(16, True), amber, 'middleRight'))

    t.add(label('ClockLabel', 780, 100, 220, 30, 'CLOCK', O(13), dim))
    t.add(text('CpuClock', 780, 126, 224, 40, '', lhm('@CpuClockSpeed'), O(17), g, 'middleLeft'))
    t.add(label('PowerLabel', 780, 186, 220, 30, 'CPU POWER', O(13), dim))
    t.add(text('CpuPower', 780, 212, 224, 40, '', lhm('@CpuPowerWattage'), O(17), g, 'middleLeft'))
    t.add(label('NetLabel', 780, 272, 220, 30, 'NET DOWN', O(13), dim))
    t.add(text('NetDown', 780, 298, 224, 40, '', lhm('@DownloadSpeed'), O(15), g, 'middleLeft'))
    t.add(label('NetUpLabel', 780, 358, 220, 30, 'NET UP', O(13), dim))
    t.add(text('NetUp', 780, 384, 224, 40, '', lhm('@UploadSpeed'), O(15), g, 'middleLeft'))
    t.add(label('UptimeLabel', 780, 444, 220, 30, 'UPTIME', O(13), dim))
    t.add(text('Uptime', 780, 470, 224, 40, '', special('systemUptime', r'd\.hh\:mm\:ss'), O(17), g, 'middleLeft'))

    t.add(chart('LoadChart', 36, 478, 706, 80, lhm('@CpuLoadPercent'), g, '#0F4D1F', width=2))
    t.add(label('PingLabel', 780, 524, 220, 24, 'PING 8.8.8.8', O(12), dim))
    t.add(text('Ping', 772, 548, 232, 32, '', special('ping', '8.8.8.8'), O(14), amber, 'middleLeft'))
    return t


# =============================================================================== 5. Carbon


@theme
def Carbon():
    t = Theme('Carbon', 1920, 480, bg='#0a0a0c', tags=['carbon', 'automotive', 'orange', 'dashboard'])
    W, H = 1920, 480
    tile = Image.new('RGBA', (8, 8))
    td = ImageDraw.Draw(tile)
    td.rectangle((0, 0, 7, 7), fill=(12, 12, 14, 255))
    td.rectangle((0, 0, 3, 3), fill=(30, 30, 34, 255))
    td.rectangle((4, 4, 7, 7), fill=(30, 30, 34, 255))
    td.rectangle((4, 0, 7, 3), fill=(18, 18, 21, 255))
    img = Image.new('RGBA', (W, H))
    for y in range(0, H, 8):
        for x in range(0, W, 8):
            img.paste(tile, (x, y))
    sheen = hgradient(W, H, [(0, '#FFFFFF'), (0.5, '#000000'), (1, '#FFFFFF')]) if False else _hg(W, H, [(0, '#FFFFFF'), (0.5, '#000000'), (1, '#FFFFFF')])
    sheen.putalpha(Image.new('L', (W, H), 14))
    img.alpha_composite(sheen)
    orange, blue = (255, 106, 0, 255), (0, 190, 255, 255)
    # tick rings around both big gauges (centre x, y, radius match the gauge elements below)
    for cx, col in ((250, orange), (1670, blue)):
        ticks(img, (cx, 250), 214, 14, 135, 270, 54, col[:3] + (200,), 2, major_every=6, major_extra=10)
    d = ImageDraw.Draw(img)
    d.line([(560, 60), (560, 420)], fill=(255, 106, 0, 70), width=2)
    d.line([(1360, 60), (1360, 420)], fill=(0, 190, 255, 70), width=2)
    t.image = img

    white, dim = '#FFFFFF', '#8D8D95'
    F = lambda s, b=True: font('Oxanium', s, b)
    t.add(gauge('CpuLoad', 60, 60, 380, lhm('@CpuLoadPercent'), '#FF8A00', '#FF2D00', '#26FFFFFF', 26, F(78), white, '%', sub='CPU', shape='flat'))
    t.add(gauge('GpuLoad', 1480, 60, 380, lhm('@GpuLoadPercent'), '#00D4FF', '#0066FF', '#26FFFFFF', 26, F(78), white, '%', sub='GPU', shape='flat'))

    t.add(text('Time', 600, 30, 720, 130, '', special('customDateTime', 'HH:mm'), F(100), white))
    t.add(text('Date', 600, 150, 720, 36, '', special('customDateTime', 'dddd  d MMMM yyyy'), F(18, False), dim))

    def mini(i, name, caption, alias, color, maximum=100):
        y = 215 + i * 62
        t.add(label(f'{name}Label', 620, y, 210, 30, caption, F(15), dim))
        t.add(text(f'{name}Value', 1100, y - 4, 200, 38, '', lhm(alias), F(20), white, 'middleRight'))
        t.add(bar(f'{name}Bar', 620, y + 34, 680, 10, lhm(alias), color, '#26FFFFFF', maximum=maximum))
    mini(0, 'CpuTemp', 'CPU TEMPERATURE', '@CpuTemperatureC', '#FF8A00', 110)
    mini(1, 'GpuTemp', 'GPU TEMPERATURE', '@GpuTemperatureC', '#00D4FF', 110)
    mini(2, 'Mem', 'MEMORY', '@RamLoadPercent', '#C9C9D0', 100)
    return t


# =============================================================================== 6. Glacier


@theme
def Glacier():
    t = Theme('Glacier', 1024, 600, bg='#E6F0FF', text='#14325C', tags=['light', 'blue', 'clean', 'cards'])
    W, H = 1024, 600
    img = vgradient(W, H, [(0, '#F1F7FF'), (1, '#CFE2FF')])
    cards = [(28, 110, 506, 340), (518, 110, 996, 340), (28, 358, 506, 588), (518, 358, 996, 588)]
    for box in cards:
        rounded_card(img, box, 26, fill=(255, 255, 255, 255), shadow=(0, 10, 20, '#3B6AA8', 60))
    t.image = img

    navy, mute, blue, cyan = '#14325C', '#6B84A8', '#2D7DFF', '#00C2FF'
    F = lambda s, b=True: font('Oxanium', s, b)
    t.add(text('Time', 28, 14, 300, 80, '', special('customDateTime', 'HH:mm'), F(54), navy, 'middleLeft'))
    t.add(text('Date', 560, 20, 436, 70, '', special('customDateTime', 'dddd, d MMMM'), F(20, False), mute, 'middleRight'))

    def card(i, name, caption, load, line1, line2):
        x0, y0, x1, y1 = cards[i]
        t.add(label(f'{name}Caption', x0 + 26, y0 + 16, 200, 34, caption, F(17), blue))
        t.add(gauge(f'{name}Load', x0 + 24, y0 + 54, 160, lhm(load), blue, cyan, '#E3ECFA', 14, F(26), navy, '%', style='style1', start_angle=90))
        for k, (lab, sensor) in enumerate((line1, line2)):
            yy = y0 + 70 + k * 74
            t.add(label(f'{name}Stat{k}Label', x0 + 210, yy, 240, 28, lab, F(13), mute))
            t.add(text(f'{name}Stat{k}', x0 + 210, yy + 24, 250, 44, '', sensor, F(26), navy, 'middleLeft'))

    card(0, 'Cpu', 'PROCESSOR', '@CpuLoadPercent', ('TEMPERATURE', lhm('@CpuTemperatureC')), ('CLOCK', lhm('@CpuClockSpeed')))
    card(1, 'Gpu', 'GRAPHICS', '@GpuLoadPercent', ('TEMPERATURE', lhm('@GpuTemperatureC')), ('CLOCK', lhm('@GpuClockSpeed')))
    card(2, 'Mem', 'MEMORY', '@RamLoadPercent', ('IN USE', lhm('@RamUsage')), ('AVAILABLE', lhm('@RamAvailable')))
    x0, y0, x1, y1 = cards[3]
    t.add(label('NetCaption', x0 + 26, y0 + 16, 200, 34, 'NETWORK', F(17), blue))
    t.add(label('NetDownLabel', x0 + 26, y0 + 56, 200, 26, 'DOWNLOAD', F(13), mute))
    t.add(text('NetDown', x0 + 26, y0 + 78, 210, 40, '', lhm('@DownloadSpeed'), F(24), navy, 'middleLeft'))
    t.add(label('NetUpLabel', x0 + 250, y0 + 56, 200, 26, 'UPLOAD', F(13), mute))
    t.add(text('NetUp', x0 + 250, y0 + 78, 210, 40, '', lhm('@UploadSpeed'), F(24), navy, 'middleLeft'))
    t.add(chart('NetChart', x0 + 26, y0 + 134, 426, 80, lhm('@DownloadSpeed'), blue, '#E3ECFA', relative=True, width=3, top='#F3F8FF', bottom='#FFFFFF'))
    return t


# =============================================================================== 7. Sunset


@theme
def Sunset():
    t = Theme('Sunset', 800, 480, bg='#7A2A7A', tags=['warm', 'gradient', 'sunset', 'compact'])
    W, H = 800, 480
    img = vgradient(W, H, [(0, '#1B1464'), (0.5, '#B02D7A'), (0.8, '#FF6A4D'), (1, '#FFC15A')])

    def sun(d, s):
        d.ellipse(((W / 2 - 230) * s, (H - 90) * s, (W / 2 + 230) * s, (H + 370) * s), fill=(255, 236, 160, 150))
    draw_shapes(img, sun)
    for box in ((40, 206, 270, 404), (285, 206, 515, 404), (530, 206, 760, 404)):
        rounded_card(img, box, 28, fill=(20, 8, 50, 110), outline=(255, 255, 255, 60), width=2)
    t.image = img

    white, soft, gold = '#FFFFFF', '#FFD9C2', '#FFD36E'
    F = lambda s, b=True: font('Oxanium', s, b)
    t.add(text('Time', 100, 10, 600, 150, '', special('customDateTime', 'HH:mm'), F(100), white))
    t.add(text('Date', 100, 150, 600, 40, '', special('customDateTime', 'dddd, d MMMM'), F(21, False), soft))
    for i, (name, caption, alias, sub) in enumerate((('Cpu', 'CPU', '@CpuLoadPercent', lhm('@CpuTemperatureC')),
                                                      ('Gpu', 'GPU', '@GpuLoadPercent', lhm('@GpuTemperatureC')),
                                                      ('Mem', 'MEMORY', '@RamLoadPercent', lhm('@RamUsage')))):
        x = 40 + i * 245
        t.add(label(f'{name}Caption', x + 10, 214, 210, 30, caption, F(15), soft, 'middleCenter'))
        t.add(gauge(f'{name}Load', x + 35, 240, 160, lhm(alias), '#FFFFFF', gold, '#33FFFFFF', 12, F(28), white, '%', style='style1', start_angle=90))
        t.add(text(f'{name}Sub', x + 10, 360, 210, 34, '', sub, F(18), white, 'middleCenter'))
    t.add(label('UptimeLabel', 50, 424, 90, 36, 'UP FOR', F(13), soft))
    t.add(text('Uptime', 125, 424, 160, 36, '', special('systemUptime', r'd\.hh\:mm\:ss'), F(17), white, 'middleLeft'))
    t.add(text('Down', 300, 424, 230, 36, '', lhm('@DownloadSpeed'), F(17), white, 'middleCenter', before='DOWN '))
    t.add(text('Up', 540, 424, 220, 36, '', lhm('@UploadSpeed'), F(17), white, 'middleCenter', before='UP '))
    return t


# =============================================================================== 8. Amber LCD


@theme
def AmberLCD():
    t = Theme('Amber LCD', 600, 1024, bg='#0E0803', text='#FFB000', tags=['lcd', 'amber', '7-segment', 'retro', 'portrait'])
    W, H = 600, 1024
    img = solid(W, H, '#0E0803')
    amber_dim = (255, 176, 0, 55)
    d = ImageDraw.Draw(img)
    for box in ((24, 24, 576, 266), (24, 278, 576, 520), (24, 532, 292, 740), (308, 532, 576, 740), (24, 752, 576, 1000)):
        d.rounded_rectangle(box, 14, outline=amber_dim, width=2, fill=(20, 12, 2, 255))
    vignette(img, 120)
    scanlines(img, 4, 35)
    t.image = img

    amber, ghost, dim = '#FFB000', '#3A2600', '#A87400'
    D = lambda s: font("Let's go Digital", s)
    B = lambda s: font('Bahnschrift', s)

    def big(name, y, caption, alias, unit):
        x, w = 24, 552
        t.add(label(f'{name}Caption', x + 24, y + 14, w - 48, 30, caption, B(16), dim))
        unit_w = 90
        t.add(text(f'{name}Ghost', x + 8, y + 40, w - unit_w - 8, 190, '88', None, D(150), ghost, 'middleRight'))
        t.add(metric(f'{name}Value', x + 8, y + 40, w - 8, 190, lhm(alias), unit, D(150), B(34), amber, amber, unit_w=unit_w, value_y=0, unit_y=84))

    big('CpuTemp', 24, 'CPU TEMPERATURE', '@CpuTemperatureC', '°C')
    big('GpuTemp', 278, 'GPU TEMPERATURE', '@GpuTemperatureC', '°C')

    def small(name, x, caption, alias):
        w, y = 268, 532
        t.add(label(f'{name}Caption', x + 20, y + 12, w - 40, 28, caption, B(14), dim))
        unit_w = 56
        t.add(text(f'{name}Ghost', x + 4, y + 44, w - unit_w - 4, 140, '88', None, D(110), ghost, 'middleRight'))
        t.add(metric(f'{name}Value', x + 4, y + 44, w - 4, 140, lhm(alias), '%', D(110), B(24), amber, amber, unit_w=unit_w, value_y=0, unit_y=64))
    small('CpuLoad', 24, 'CPU LOAD', '@CpuLoadPercent')
    small('GpuLoad', 308, 'GPU LOAD', '@GpuLoadPercent')

    t.add(label('TimeCaption', 48, 764, 500, 28, 'TIME', B(14), dim))
    t.add(text('TimeGhost', 36, 800, 528, 190, '88:88', None, D(170), ghost, 'middleCenter'))
    t.add(text('Time', 36, 800, 528, 190, '', special('customDateTime', 'HH:mm'), D(170), amber, 'middleCenter'))
    return t


# =============================================================================== 9. Pulse


@theme
def Pulse():
    t = Theme('Pulse', 1920, 480, bg='#07070F', tags=['audio', 'visualizer', 'music', 'rainbow'])
    W, H = 1920, 480
    img = vgradient(W, H, [(0, '#05050C'), (1, '#160A2C')])

    def halo_shape(d):
        d.ellipse((260, 330, 1660, 620), fill=(140, 60, 255, 120))
    soft_halo(img, halo_shape, radius=70, strength=0.9)
    ImageDraw.Draw(img).line([(60, 112), (W - 60, 112)], fill=(255, 255, 255, 30), width=1)
    t.image = img

    white, dim = '#FFFFFF', '#8A86A8'
    F = lambda s, b=True: font('Oxanium', s, b)
    t.add(text('Time', 50, 14, 330, 90, '', special('customDateTime', 'HH:mm'), F(60), white, 'middleLeft'))
    t.add(text('Date', 380, 30, 460, 60, '', special('customDateTime', 'dddd, d MMMM'), F(20, False), dim, 'middleLeft'))
    for i, (name, caption, alias) in enumerate((('Cpu', 'CPU', '@CpuLoadPercent'), ('Gpu', 'GPU', '@GpuLoadPercent'), ('Mem', 'MEM', '@RamLoadPercent'))):
        x = 1130 + i * 250
        t.add(label(f'{name}Caption', x, 22, 90, 30, caption, F(14), dim))
        t.add(text(f'{name}Value', x + 60, 14, 160, 50, '', lhm(alias), F(28), white, 'middleRight'))
        t.add(bar(f'{name}Bar', x, 70, 220, 5, lhm(alias), '#B388FF', '#26FFFFFF'))
    t.add(audio('Visualizer', 60, 130, 1800, 330, bars=84, spacing=5, direction='Bottom', style='Bar', color_style='Rainbow', speed=0.015, cap='Round'))
    return t


# =============================================================================== 10. Gamer HUD


@theme
def GamerHUD():
    t = Theme('Gamer HUD', 800, 480, bg='#050A14', tags=['gaming', 'fps', 'hud', 'cyan'])
    W, H = 800, 480
    img = solid(W, H, '#050A14')
    d = ImageDraw.Draw(img)
    for x in range(0, W, 40):
        d.line([(x, 0), (x, H)], fill=(0, 229, 255, 14), width=1)
    for y in range(0, H, 40):
        d.line([(0, y), (W, y)], fill=(0, 229, 255, 14), width=1)

    def brackets(dr):
        for (x, y, sx, sy) in ((16, 16, 1, 1), (W - 16, 16, -1, 1), (16, H - 16, 1, -1), (W - 16, H - 16, -1, -1)):
            dr.line([(x, y + 60 * sy), (x, y), (x + 60 * sx, y)], fill=(0, 229, 255, 255), width=4)
    glow(img, brackets, radius=6, strength=0.8)
    t.image = img

    cyan, blue, white, dim, amber = '#00E5FF', '#2D6BFF', '#FFFFFF', '#6F8FAE', '#FFB000'
    F = lambda s, b=True: font('Oxanium', s, b)
    t.add(label('GpuCaption', 40, 40, 240, 34, 'GPU', F(20), cyan, 'middleCenter'))
    t.add(gauge('GpuLoad', 60, 80, 200, lhm('@GpuLoadPercent'), cyan, blue, '#26FFFFFF', 14, F(30), white, '%', sub='LOAD'))
    t.add(text('GpuTemp', 40, 290, 240, 40, '', lhm('@GpuTemperatureC'), F(22), white, 'middleCenter', before='TEMP  '))
    t.add(label('CpuCaption', 520, 40, 240, 34, 'CPU', F(20), cyan, 'middleCenter'))
    t.add(gauge('CpuLoad', 540, 80, 200, lhm('@CpuLoadPercent'), blue, cyan, '#26FFFFFF', 14, F(30), white, '%', sub='LOAD'))
    t.add(text('CpuTemp', 520, 290, 240, 40, '', lhm('@CpuTemperatureC'), F(22), white, 'middleCenter', before='TEMP  '))

    t.add(label('FpsCaption', 280, 56, 240, 30, 'FRAMES PER SECOND', F(13), dim, 'middleCenter'))
    t.add(number('Fps', 275, 84, 250, 150, special('displayFramerate'), F(96), cyan))
    t.add(label('FrametimeCaption', 280, 238, 240, 28, 'FRAME TIME (MS)', F(13), dim, 'middleCenter'))
    t.add(number('Frametime', 275, 262, 250, 60, special('displayFrametime'), F(40), white))

    t.add(label('VramLabel', 40, 388, 120, 28, 'VRAM', F(14), dim))
    t.add(bar('VramBar', 120, 396, 480, 12, lhm('@GpuMemoryPercent'), cyan, '#26FFFFFF'))
    t.add(text('Vram', 610, 384, 150, 36, '', lhm('@GpuVramUsage'), F(18), white, 'middleRight'))
    t.add(label('PingLabel', 40, 430, 70, 34, 'PING', F(14), dim))
    t.add(text('Ping', 100, 430, 300, 34, '', special('ping', '8.8.8.8'), F(16), amber, 'middleLeft'))
    t.add(text('Time', 560, 430, 200, 34, '', special('customDateTime', 'HH:mm'), F(20), white, 'middleRight'))
    return t


# =============================================================================== rendering


def render_preview(folder, has_audio=False):
    app = os.environ.get('OVERLOOK_APP')
    if not app or not os.path.exists(app):
        return None
    out = os.path.join(tempfile.gettempdir(), 'overlook-preview.png')
    cmd = (['dotnet', app] if app.lower().endswith('.dll') else [app]) + ['--render', folder, '--out', out, '--sensors', SAMPLE]
    if has_audio:
        cmd.append('--audio-demo')
    subprocess.run(cmd, check=True, capture_output=True)
    img = Image.open(out).convert('RGBA')
    img.thumbnail((640, 640), Image.LANCZOS)
    return data_uri(img)


def main(argv):
    only = [a for a in argv if not a.startswith('--')]
    names = only or list(REGISTRY)
    for name in names:
        t = REGISTRY[name]()
        path = t.save(THEMES_DIR)
        preview = None if '--no-preview' in argv else render_preview(os.path.dirname(path), any(e['type'] == 'audio' for e in t.elements))
        if preview:
            t.save(THEMES_DIR, preview)
        print(f'{name}: {os.path.getsize(path) // 1024} KB' + (' (with preview)' if preview else ''))


if __name__ == '__main__':
    main(sys.argv[1:])
