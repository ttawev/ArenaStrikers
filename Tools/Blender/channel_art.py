# Оформление канала YouTube SpellStrike: шапка 2560×1440, водяной знак 150×150, обложка трейлера 1280×720.
# Фон — настоящий стадион игры (рендер сцены трейлера: Builds/Branding/_work/stadium_bg.png).
# Запуск: python channel_art.py  (numpy, Pillow) → Builds/Branding/
import os
import numpy as np
from PIL import Image, ImageFilter, ImageDraw, ImageFont, ImageEnhance

ROOT = os.environ.get('ARENA_ROOT') or os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
D = os.path.join(ROOT, 'Builds', 'Branding')
FONTS = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts')
RUSSO = os.path.join(FONTS, 'RussoOne-Regular.ttf')
PTB = os.path.join(FONTS, 'PTSans-Bold.ttf')
CYAN, VIOLET, GOLD, FIRE = (120, 225, 255), (150, 110, 255), (255, 200, 70), (255, 95, 30)

def crop(im): return im.crop(im.getbbox())

def fit(im, w=None, h=None):
    k = (w / im.width) if w else (h / im.height)
    return im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)

def halo(im, rad, alpha):
    a = im.split()[3].filter(ImageFilter.GaussianBlur(rad))
    sh = Image.new('RGBA', im.size, (2, 3, 10, 0)); sh.putalpha(a.point(lambda v: int(v * alpha)))
    return sh

def glow(im, rad, k):
    """Свечение фигуры её же цветом."""
    g = im.filter(ImageFilter.GaussianBlur(rad))
    a = np.asarray(g).astype(np.float32); a[..., 3] = np.clip(a[..., 3] * k, 0, 255)
    return Image.fromarray(a.astype(np.uint8), 'RGBA')

def put(canvas, im, x, y, anchor='c'):
    if anchor == 'c': x, y = x - im.width / 2, y - im.height / 2
    elif anchor == 'l': y = y - im.height / 2
    canvas.alpha_composite(im, (int(x), int(y)))

def gradient_text(text, size, c0, c1, font=RUSSO, spacing=0, shear=0.18):
    """Надпись с горизонтальным градиентом и наклоном (как в логотипе)."""
    f = ImageFont.truetype(font, size)
    w = int(f.getlength(text) + spacing * len(text)) + 40; h = int(size * 1.4)
    mask = Image.new('L', (w, h), 0); d = ImageDraw.Draw(mask); x = 20
    for ch in text:
        d.text((x, h * 0.12), ch, font=f, fill=255); x += f.getlength(ch) + spacing
    grad = np.zeros((h, w, 3), np.float32); t = np.linspace(0, 1, w)[None, :, None]
    grad[:] = np.array(c0)[None, None] * (1 - t) + np.array(c1)[None, None] * t
    im = Image.fromarray(grad.astype(np.uint8), 'RGB').convert('RGBA'); im.putalpha(mask)
    if shear:
        im = im.transform((w + int(h * shear), h), Image.AFFINE, (1, shear, -h * shear, 0, 1, 0), Image.BICUBIC)
    return crop(im)

def stadium(W, H, blur, dark):
    bg = Image.open(os.path.join(D, '_work', 'stadium_bg.png')).convert('RGB')
    k = max(W / bg.width, H / bg.height); bg = bg.resize((int(bg.width * k), int(bg.height * k)), Image.LANCZOS)
    bg = bg.crop(((bg.width - W) // 2, int((bg.height - H) * 0.55), (bg.width - W) // 2 + W, int((bg.height - H) * 0.55) + H))
    bg = bg.filter(ImageFilter.GaussianBlur(blur))
    bg = ImageEnhance.Brightness(bg).enhance(dark)
    arr = np.asarray(bg).astype(np.float32)
    y, x = np.mgrid[0:H, 0:W].astype(np.float32)
    # цветной свет: магия слева, огонь справа; виньетка
    arr += np.exp(-(((x / W - 0.12) / 0.3) ** 2 + ((y / H - 0.45) / 0.5) ** 2))[..., None] * np.array([40, 30, 120]) * 0.9
    arr += np.exp(-(((x / W - 0.88) / 0.3) ** 2 + ((y / H - 0.55) / 0.5) ** 2))[..., None] * np.array([130, 40, 5]) * 0.9
    v = np.clip(1 - (((x / W - 0.5) / 0.75) ** 2 + ((y / H - 0.5) / 0.8) ** 2), 0, 1) ** 0.6
    arr *= (0.35 + 0.65 * v)[..., None]
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')

def banner(tag='ФУТБОЛ СО СПОСОБНОСТЯМИ  •  ИГРАЙ С ДРУЗЬЯМИ', name='spellstrike_banner'):
    W, H = 2560, 1440
    img = stadium(W, H, 7, 0.55)
    # безопасная зона для всех устройств: 1546×423 по центру
    sx0, sy0 = (W - 1546) // 2, (H - 423) // 2
    emb = crop(Image.open(os.path.join(D, 'spellstrike_emblem.png')).convert('RGBA'))
    word = crop(Image.open(os.path.join(D, 'spellstrike_logo_transparent.png')).convert('RGBA'))
    e = fit(emb, h=470); w = fit(word, h=250)
    f = ImageFont.truetype(PTB, 38)
    gap = -30; total = e.width + gap + max(w.width, f.getlength(tag) + 10); x0 = W / 2 - total / 2   # весь блок — в зоне 1546 по центру
    ey = H / 2 + 5
    put(img, halo(e, 30, 0.7), x0, ey - e.height / 2, 'tl' if False else 'l')
    put(img, e, x0, ey, 'l')
    wx = x0 + e.width + gap; wy = H / 2 - 50
    put(img, halo(w, 20, 0.9), wx, wy, 'l'); put(img, glow(w, 25, 0.6), wx, wy, 'l'); put(img, w, wx, wy, 'l')
    # слоган под надписью
    ty = wy + w.height / 2 + 30
    lay = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(lay)
    d.text((wx + 8, ty), tag, font=f, fill=(225, 232, 255, 235))
    img.alpha_composite(halo(lay, 6, 1.0)); img.alpha_composite(lay)
    # тонкие светящиеся линии сверху и снизу безопасной зоны (видны на ТВ и ПК — рамка «арены»)
    ln = Image.new('RGBA', (W, H), (0, 0, 0, 0)); dl = ImageDraw.Draw(ln)
    for yy in (sy0 - 70, sy0 + 423 + 70):
        for i in range(W):
            t = i / W; c = tuple(int(a * (1 - t) + b * t) for a, b in zip(CYAN, FIRE))
            fade = min(1, (1 - abs(t - 0.5) * 2) * 2.2)
            dl.point((i, yy), fill=c + (int(220 * fade),)); dl.point((i, yy + 1), fill=c + (int(220 * fade),)); dl.point((i, yy + 2), fill=c + (int(120 * fade),))
    img.alpha_composite(ln.filter(ImageFilter.GaussianBlur(8))); img.alpha_composite(ln)
    out = img.convert('RGB')
    out.save(os.path.join(D, name + '.png'), optimize=True)
    out.save(os.path.join(D, name + '.jpg'), quality=92)   # YouTube принимает до 6 МБ
    return out, (sx0, sy0)

def watermark():
    emb = crop(Image.open(os.path.join(D, 'spellstrike_emblem.png')).convert('RGBA'))
    S = 600; c = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    e = fit(emb, w=int(S * 0.94)) if emb.width >= emb.height else fit(emb, h=int(S * 0.94))
    put(c, halo(e, 14, 0.8), S / 2, S / 2); put(c, e, S / 2, S / 2)
    c.resize((150, 150), Image.LANCZOS).save(os.path.join(D, 'spellstrike_watermark.png'))

def thumbnail(title, sub, frame, name):
    """Обложка видео 1280×720: кадр слева-сзади, значок, крупный заголовок с обводкой."""
    W, H = 1280, 720
    if frame and os.path.exists(frame):
        bg = Image.open(frame).convert('RGB'); k = max(W / bg.width, H / bg.height)
        bg = bg.resize((int(bg.width * k), int(bg.height * k)), Image.LANCZOS)
        bg = bg.crop(((bg.width - W) // 2, (bg.height - H) // 2, (bg.width - W) // 2 + W, (bg.height - H) // 2 + H)).convert('RGBA')
    else:
        bg = stadium(W, H, 3, 0.7)
    img = bg
    sh = np.zeros((H, W, 4), np.uint8); sh[..., 3] = (np.linspace(0, 200, W)[None, :] * np.ones((H, 1))).astype(np.uint8)  # затемнение справа под текст
    img.alpha_composite(Image.fromarray(sh, 'RGBA'))
    emb = crop(Image.open(os.path.join(D, 'spellstrike_emblem.png')).convert('RGBA'))
    e = fit(emb, h=560); put(img, halo(e, 20, 0.7), 330, H / 2); put(img, e, 330, H / 2)
    t1 = gradient_text(title, 118, GOLD, FIRE); t2 = gradient_text(sub, 84, CYAN, VIOLET)
    for t, y in ((t2, 230), (t1, 360)):
        x = W - 50 - t.width
        st = Image.new('RGBA', (t.width + 40, t.height + 40), (0, 0, 0, 0)); st.paste((5, 6, 16, 255), (20, 20), t)
        st = st.filter(ImageFilter.MaxFilter(13)).filter(ImageFilter.GaussianBlur(2))
        img.alpha_composite(st, (int(x - 20), int(y - 20))); img.alpha_composite(t, (int(x), int(y)))
    wm = crop(Image.open(os.path.join(D, 'spellstrike_logo_transparent.png')).convert('RGBA')); wm = fit(wm, h=110)
    put(img, halo(wm, 10, 0.9), W - 50 - wm.width, H - 90, 'l'); put(img, wm, W - 50 - wm.width, H - 90, 'l')
    img.convert('RGB').save(os.path.join(D, name), quality=92)

def check(banner_img, safe, name='banner_check.jpg'):
    """Как шапка выглядит на ТВ, ПК и телефоне (контрольный лист)."""
    sx0, sy0 = safe; W, H = banner_img.size
    tv = banner_img.resize((640, 360))
    desk = banner_img.crop((0, (H - 423) // 2, W, (H + 423) // 2)).resize((1280, 212))
    mob = banner_img.crop((sx0, sy0, sx0 + 1546, sy0 + 423)).resize((640, 175))
    sheet = Image.new('RGB', (1300, 360 + 212 + 175 + 60), (240, 240, 240))
    sheet.paste(tv, (0, 0)); sheet.paste(desk, (0, 370)); sheet.paste(mob, (0, 370 + 222))
    d = ImageDraw.Draw(sheet); f = ImageFont.truetype(PTB, 22)
    d.text((660, 10), 'ТВ: вся шапка', font=f, fill=(0, 0, 0)); d.text((660, 340), 'Компьютер: полоса 2560×423', font=f, fill=(0, 0, 0))
    d.text((660, 600), 'Телефон: 1546×423', font=f, fill=(0, 0, 0))
    os.makedirs(os.path.join(ROOT, 'Builds', 'Trailer', '_check'), exist_ok=True)
    sheet.save(os.path.join(ROOT, 'Builds', 'Trailer', '_check', name), quality=88)

if __name__ == '__main__':
    b, safe = banner(); check(b, safe)
    b, safe = banner('FOOTBALL WITH SPELLS  •  PLAY WITH FRIENDS', 'spellstrike_banner_en'); check(b, safe, 'banner_check_en.jpg')
    watermark()
    thumbnail('ТРЕЙЛЕР', 'ПЕРВЫЙ', os.environ.get('THUMB_FRAME'), 'spellstrike_thumb_trailer.jpg')
    thumbnail('TRAILER', 'FIRST', os.environ.get('THUMB_FRAME'), 'spellstrike_thumb_trailer_en.jpg')
    print('готово:', D)
