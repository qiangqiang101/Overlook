"""Small helpers for writing Overlook themes in Python: element builders, background drawing (Pillow) and saving.

Used by generate_themes.py to produce the bundled sample themes. Sensors are portable aliases (see PortableSensors.cs) or
special kinds, so every theme works on any PC.
"""
import base64
import io
import json
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

# ----------------------------------------------------------------------------- colours


def C(color, alpha=255):
    """'#RRGGBB' (+ alpha) or '#AARRGGBB' -> '#AARRGGBB'."""
    h = color.lstrip('#').upper()
    return '#' + h if len(h) == 8 else f'#{alpha:02X}{h}'


def rgb(color):
    h = color.lstrip('#')
    h = h[-6:]
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    ra, rb = rgb(a), rgb(b)
    return '#' + ''.join(f'{round(x + (y - x) * t):02X}' for x, y in zip(ra, rb))


# ----------------------------------------------------------------------------- sensors


def lhm(alias):
    """A portable sensor, e.g. '@CpuLoadPercent'."""
    return {'kind': 'lhm', 'param': alias}


def special(kind, param=None):
    s = {'kind': kind}
    if param is not None:
        s['param'] = param
    return s


# ----------------------------------------------------------------------------- elements


def font(family='Oxanium', size=12, bold=False, italic=False):
    return {'family': family, 'size': size, 'bold': bold, 'italic': italic, 'underline': False, 'strikeout': False}


def _base(kind, name, x, y, w, h, sensor=None):
    e = {'type': kind, 'name': name, 'x': int(x), 'y': int(y), 'width': int(w), 'height': int(h), 'visible': True, 'backColor': '#00000000'}
    if sensor:
        e['sensor'] = sensor
    return e


def text(name, x, y, w, h, txt='', sensor=None, f=None, color='#FFFFFF', align='middleCenter', before=None):
    e = _base('text', name, x, y, w, h, sensor)
    e.update(font=f or font(), foreColor=C(color), text=txt, textAlign=align, autoSize=False, autoEllipsis=False,
             rightToLeft=False, borderStyle='none', imageAlign='middleCenter', allowUserEdit=False)
    if before:
        e['beforeText'] = before
    return e


def label(name, x, y, w, h, txt, f=None, color='#FFFFFF', align='middleLeft'):
    """A fixed caption; the user can retype it in the main window."""
    e = text(name, x, y, w, h, txt, None, f, color, align)
    e['allowUserEdit'] = True
    return e


def metric(name, x, y, w, h, sensor, unit, vfont, ufont, vcolor, ucolor, unit_w=None, value_y=0, unit_y=None):
    """A big number with a small unit to its right (value right-aligned, unit left-aligned)."""
    unit_w = unit_w if unit_w is not None else int(ufont['size'] * 1.33 * max(len(unit), 1) * 0.65) + 6
    if unit_y is None:
        unit_y = value_y + int((vfont['size'] - ufont['size']) * 1.33 * 0.75)
    e = _base('customText', name, x, y, w, h, sensor)
    e.update(title='', value='0', unit=unit, titleColor=C(vcolor), valueColor=C(vcolor), unitColor=C(ucolor),
             titleFont=vfont, valueFont=vfont, unitFont=ufont, titleAlign='middleLeft', valueAlign='middleRight', unitAlign='middleLeft',
             autoWidth=False, titleWidth=0, valueWidth=w - unit_w, unitWidth=unit_w,
             titleOffset={'x': 0, 'y': 0}, valueOffset={'x': 0, 'y': value_y}, unitOffset={'x': 0, 'y': unit_y})
    return e


def number(name, x, y, w, h, sensor, vfont, color, align='middleCenter'):
    """Just the sensor's number (no unit), aligned inside the box."""
    e = _base('customText', name, x, y, w, h, sensor)
    e.update(title='', value='0', unit='', titleColor=C(color), valueColor=C(color), unitColor=C(color),
             titleFont=vfont, valueFont=vfont, unitFont=vfont, titleAlign='middleLeft', valueAlign=align, unitAlign='middleLeft',
             autoWidth=False, titleWidth=0, valueWidth=w, unitWidth=0,
             titleOffset={'x': 0, 'y': 0}, valueOffset={'x': 0, 'y': 0}, unitOffset={'x': 0, 'y': 0})
    return e


def gauge(name, x, y, size, sensor, c1, c2, track, thickness=14, f=None, color='#FFFFFF', unit='%', maximum=100,
          style='style2', sub='', shape='round', text_mode='value', gradient='forwardDiagonal', start_angle=90, sweep=360, caption=''):
    e = _base('circularBar', name, x, y, size, size, sensor)
    e.update(text=caption, subText=sub, font=f or font(size=int(size / 9), bold=True), foreColor=C(color),
             trackColor=C(track), progressColor=C(c1), progressColor2=C(c2), gradientMode=gradient, style=style,
             progressShape=shape, barWidth=sweep, thickness=thickness, startAngle=start_angle, minimum=0, maximum=maximum,
             unit=unit, textMode=text_mode)
    return e


def bar(name, x, y, w, h, sensor, fg, back='#00000000', direction='leftToRight', maximum=100, show=False, f=None, color='#FFFFFF', unit='%'):
    e = _base('bar', name, x, y, w, h, sensor)
    e.update(backColor=C(back), text='', font=f or font(size=9), foreColor=C(color), foregroundColor=C(fg), minimum=0, maximum=maximum,
             fillDirection=direction, showValue=show, unit=unit, useTexture=False, textureWidth=0, textureHeight=0)
    return e


def chart(name, x, y, w, h, sensor, line, grid=None, relative=False, width=2, avg=None, top='#00000000', bottom='#00000000'):
    e = _base('chart', name, x, y, w, h, sensor)
    pen = lambda col, wd=1: {'color': C(col), 'dashStyle': 'solid', 'width': wd}
    grid_on = grid is not None
    e.update(text='', font=font(size=8), foreColor=C(line), scaleMode='relative' if relative else 'absolute', borderStyle='none',
             style={'showVerticalGridLines': grid_on, 'showHorizontalGridLines': grid_on, 'showCurMax': False, 'showAverageLine': avg is not None,
                    'verticalGridPen': pen(grid or '#202020'), 'horizontalGridPen': pen(grid or '#202020'),
                    'avgLinePen': pen(avg or '#FFFFFF'), 'chartLinePen': pen(line, width), 'antiAliasing': True,
                    'backgroundColorTop': C(top), 'backgroundColorBottom': C(bottom)})
    return e


def audio(name, x, y, w, h, bars=48, spacing=3, direction='Bottom', style='Bar', color_style='Rainbow', colors=None, speed=0.02, cap='Round'):
    e = _base('audio', name, x, y, w, h)
    e.update(useAverage=False, barCount=bars, barSpacing=spacing, scalingStrategy='Decibel', direction=direction, lineCap=cap,
             barStyle=style, colorStyle=color_style, speed=speed, colors=[C(c) for c in (colors or [])])
    return e


def panel(name, x, y, w, h, color, children=None):
    """A plain coloured rectangle (a bar element without a sensor is just its back colour)."""
    e = _base('image', name, x, y, w, h)
    e.update(backColor=C(color), backgroundImageLayout='none', sizeMode='normal', borderStyle='none')
    if children:
        e['children'] = children
    return e


# ----------------------------------------------------------------------------- backgrounds


def vgradient(w, h, stops):
    """Vertical gradient from [(position 0..1, '#RRGGBB'), ...]."""
    img = Image.new('RGB', (w, h))
    px = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(h - 1, 1)
        for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
            if p0 <= t <= p1:
                px.line([(0, y), (w, y)], fill=rgb(mix(c0, c1, (t - p0) / max(p1 - p0, 1e-6))))
                break
    return img.convert('RGBA')


def hgradient(w, h, stops):
    return vgradient(h, w, stops).rotate(90, expand=True).transpose(Image.FLIP_TOP_BOTTOM) if False else _hg(w, h, stops)


def _hg(w, h, stops):
    img = Image.new('RGB', (w, h))
    px = ImageDraw.Draw(img)
    for x in range(w):
        t = x / max(w - 1, 1)
        for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
            if p0 <= t <= p1:
                px.line([(x, 0), (x, h)], fill=rgb(mix(c0, c1, (t - p0) / max(p1 - p0, 1e-6))))
                break
    return img.convert('RGBA')


def solid(w, h, color, alpha=255):
    return Image.new('RGBA', (w, h), rgb(color) + (alpha,))


def layer(w, h):
    return Image.new('RGBA', (w, h), (0, 0, 0, 0))


SS = 3   # supersampling for smooth shapes


def draw_shapes(base, fn):
    """Runs fn(draw, s) on a supersampled transparent layer (s = scale factor) and composites it onto base."""
    w, h = base.size
    big = Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0))
    fn(ImageDraw.Draw(big), SS)
    small = big.resize((w, h), Image.LANCZOS)
    base.alpha_composite(small)
    return base


def glow(base, fn, radius=18, strength=1.0):
    """Draws fn(draw) on a layer, blurs it and adds it under a crisp copy: a neon glow."""
    w, h = base.size
    lay = layer(w, h)
    fn(ImageDraw.Draw(lay))
    blurred = lay.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        a = blurred.getchannel('A').point(lambda v: min(255, int(v * strength)))
        blurred.putalpha(a)
    base.alpha_composite(blurred)
    base.alpha_composite(lay)
    return base


def halo(base, fn, radius=40, strength=1.0):
    """A soft light: fn(draw) on a layer, blurred, with no crisp copy."""
    lay = layer(*base.size)
    fn(ImageDraw.Draw(lay))
    blurred = lay.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        blurred.putalpha(blurred.getchannel('A').point(lambda v: min(255, int(v * strength))))
    base.alpha_composite(blurred)
    return base


def rounded_card(base, box, radius, fill=None, outline=None, width=1, shadow=None):
    """A rounded rectangle; shadow=(dx, dy, blur, '#RRGGBB', alpha) draws a soft drop shadow first."""
    x0, y0, x1, y1 = box
    if shadow:
        dx, dy, blur, col, alpha = shadow
        sh = layer(*base.size)
        ImageDraw.Draw(sh).rounded_rectangle((x0 + dx, y0 + dy, x1 + dx, y1 + dy), radius, fill=rgb(col) + (alpha,))
        base.alpha_composite(sh.filter(ImageFilter.GaussianBlur(blur)))

    def fn(d, s):
        d.rounded_rectangle((x0 * s, y0 * s, x1 * s, y1 * s), radius * s, fill=fill, outline=outline, width=width * s)
    return draw_shapes(base, fn)


def ticks(base, center, radius, length, start, sweep, count, color, width=2, major_every=0, major_extra=8):
    """Gauge tick marks along an arc (angles clockwise from 3 o'clock)."""
    cx, cy = center

    def fn(d, s):
        for i in range(count + 1):
            a = math.radians(start + sweep * i / count)
            ln = length + (major_extra if major_every and i % major_every == 0 else 0)
            x0, y0 = cx + math.cos(a) * radius, cy + math.sin(a) * radius
            x1, y1 = cx + math.cos(a) * (radius - ln), cy + math.sin(a) * (radius - ln)
            d.line([(x0 * s, y0 * s), (x1 * s, y1 * s)], fill=color, width=width * s)
    return draw_shapes(base, fn)


def vignette(base, strength=140):
    w, h = base.size
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).ellipse((-w * 0.25, -h * 0.6, w * 1.25, h * 1.6), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(min(w, h) / 4))
    dark = Image.new('RGBA', (w, h), (0, 0, 0, strength))
    dark.putalpha(ImageChops.invert(mask).point(lambda v: int(v * strength / 255)))
    base.alpha_composite(dark)
    return base


def scanlines(base, spacing=3, alpha=40):
    w, h = base.size

    def fn(d, s):
        for y in range(0, h, spacing):
            d.line([(0, y * s), (w * s, y * s)], fill=(0, 0, 0, alpha), width=s)
    return draw_shapes(base, fn)


# ----------------------------------------------------------------------------- saving


def data_uri(img, optimize=True):
    buf = io.BytesIO()
    img.save(buf, 'PNG', optimize=optimize)
    return 'data:image/png;base64,' + base64.b64encode(buf.getvalue()).decode('ascii')


class Theme:
    def __init__(self, name, w, h, bg='#000000', text='#FFFFFF', tags=(), interval=1000, rgb=False, description=''):
        self.name, self.w, self.h = name, w, h
        self.bg, self.text = bg, text
        self.tags, self.interval, self.rgb = list(tags), interval, rgb
        self.image = None
        self.elements = []

    def add(self, *elements):
        names = {e['name'] for e in self.elements}
        for e in elements:
            assert e['name'] not in names, f"{self.name}: duplicate element name {e['name']}"
            names.add(e['name'])
            self.elements.append(e)
        return self

    def document(self, preview=None):
        doc = {'formatVersion': 2, 'name': self.name, 'author': 'Overlook', 'version': '1.0',
               'tags': [f'{self.w}x{self.h}'] + self.tags, 'width': self.w, 'height': self.h,
               'backgroundColor': C(self.bg), 'textColor': C(self.text), 'updateInterval': self.interval, 'opacity': 1}
        if self.image is not None:
            doc['backgroundImage'] = data_uri(self.image)
        if preview is not None:
            doc['preview'] = preview
        if self.rgb:
            doc['rgb'] = {'enabled': True}
        doc['elements'] = self.elements
        return doc

    def save(self, root, preview=None):
        folder = os.path.join(root, self.name)
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, 'theme.json')
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(self.document(preview), f, ensure_ascii=False, indent=2)
        return path
