# Аватар канала YouTube SpellStrike 800×800 из логотипа на прозрачном фоне (его рендерит channel_logo.py).
# Ночной фон с магическим (сверху слева) и огненным (снизу справа) сиянием, свечение букв, кольцо по краю, искры.
# Запуск: python channel_avatar.py  (нужны numpy и Pillow) → Builds/Branding/spellstrike_avatar.png
import os, math
import numpy as np
from PIL import Image, ImageFilter, ImageDraw

ROOT = os.environ.get('ARENA_ROOT') or os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
D = os.path.join(ROOT, 'Builds', 'Branding')
S = 1600                                   # рисуем в 2× и уменьшаем — края гладкие

def blob(cx, cy, r, color, k):
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    d = np.sqrt((x - cx * S) ** 2 + (y - cy * S) ** 2) / (r * S)
    a = np.exp(-d * d) * k
    return a[..., None] * np.array(color, np.float32)[None, None, :]

def main():
    logo = Image.open(os.path.join(D, 'spellstrike_logo_transparent.png')).convert('RGBA')
    logo = logo.crop(logo.getbbox())
    # фон: тёмно-синий центр → почти чёрный край, сияния магии и огня
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    r = np.sqrt((x - S / 2) ** 2 + (y - S / 2) ** 2) / (S / 2)
    c0, c1 = np.array([12, 16, 44], np.float32), np.array([3, 4, 10], np.float32)
    bg = c0[None, None] + (c1 - c0)[None, None] * np.clip(r, 0, 1)[..., None] ** 1.3
    bg += blob(0.12, 0.1, 0.36, (90, 60, 230), 0.32) + blob(0.05, 0.4, 0.22, (30, 160, 230), 0.14)
    bg += blob(0.9, 0.92, 0.36, (240, 80, 15), 0.36) + blob(0.98, 0.65, 0.22, (255, 150, 30), 0.12)
    img = Image.fromarray(np.clip(bg, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')
    # логотип: ширина ~74% круга, по центру (чуть выше — под ним черта)
    w = int(S * 0.74); h = int(logo.height * w / logo.width)
    lg = logo.resize((w, h), Image.LANCZOS)
    ox, oy = (S - w) // 2, (S - h) // 2 - int(S * 0.01)
    # свечение букв: размытая копия своего цвета, два радиуса
    glow_layer = Image.new('RGBA', (S, S), (0, 0, 0, 0)); glow_layer.paste(lg, (ox, oy), lg)
    for rad, k in ((40, 0.9), (14, 0.8)):
        g = glow_layer.filter(ImageFilter.GaussianBlur(rad))
        ga = np.asarray(g).astype(np.float32); ba = np.asarray(img).astype(np.float32)
        ba[..., :3] += ga[..., :3] * (ga[..., 3:4] / 255) * k
        img = Image.fromarray(np.clip(ba, 0, 255).astype(np.uint8), 'RGBA')
    # тёмная обводка-тень под буквами — читаются на сиянии
    sh = Image.new('RGBA', (S, S), (0, 0, 0, 0)); a = lg.split()[3]
    sh.paste(Image.new('RGBA', lg.size, (3, 4, 12, 210)), (ox + 6, oy + 10), a)
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(6)))
    img.alpha_composite(glow_layer)
    # кольцо по краю: бирюза → фиолет сверху, огонь снизу; со свечением
    ring = Image.new('RGBA', (S, S), (0, 0, 0, 0)); dr = ImageDraw.Draw(ring)
    R, W = S * 0.465, int(S * 0.009)
    for i in range(720):
        t = i / 720; a0 = t * 360 - 90
        mix = (1 - math.cos(math.radians(a0 - 225))) / 2         # 0 — магия (сверху слева), 1 — огонь (снизу справа)
        c = tuple(int(v) for v in np.array((90, 225, 255)) * (1 - mix) + np.array((255, 120, 30)) * mix)
        dr.arc((S / 2 - R, S / 2 - R, S / 2 + R, S / 2 + R), a0, a0 + 0.6, fill=c + (255,), width=W)
    img.alpha_composite(ring.filter(ImageFilter.GaussianBlur(18))); img.alpha_composite(ring.filter(ImageFilter.GaussianBlur(5))); img.alpha_composite(ring)
    # искры
    sp = Image.new('RGBA', (S, S), (0, 0, 0, 0)); ds = ImageDraw.Draw(sp)
    for (cx, cy, rr, c) in ((0.18, 0.3, 7, (150, 230, 255)), (0.26, 0.2, 4, (190, 160, 255)), (0.13, 0.52, 4, (120, 220, 255)),
                            (0.8, 0.72, 8, (255, 190, 90)), (0.88, 0.62, 4, (255, 140, 50)), (0.72, 0.84, 5, (255, 170, 70))):
        ds.ellipse((cx * S - rr, cy * S - rr, cx * S + rr, cy * S + rr), fill=c + (255,))
    img.alpha_composite(sp.filter(ImageFilter.GaussianBlur(10))); img.alpha_composite(sp)
    out = img.convert('RGB').resize((800, 800), Image.LANCZOS)
    out.save(os.path.join(D, 'spellstrike_avatar.png'))
    # то же в высоком разрешении (для других площадок)
    img.convert('RGB').save(os.path.join(D, 'spellstrike_avatar_1600.png'))
    print('аватар:', os.path.join(D, 'spellstrike_avatar.png'))

if __name__ == '__main__':
    main()
