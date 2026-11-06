# Фирменные картинки SpellStrike из значка (emblem.py) и надписи (channel_logo.py):
#   spellstrike_avatar.png        — аватар канала 800×800 (только значок, без текста: в круге 98 px текст не читается)
#   spellstrike_lockup_vertical.png   — значок над надписью, прозрачный фон (вертикальные видео, заставки)
#   spellstrike_lockup_horizontal.png — значок слева, надпись справа, прозрачный фон (шапка канала, Steam)
# Запуск: python branding.py  (нужны numpy и Pillow)
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.environ.get('ARENA_ROOT') or os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
D = os.path.join(ROOT, 'Builds', 'Branding')

def blob(S, cx, cy, r, color, k):
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    d = np.sqrt((x - cx * S) ** 2 + (y - cy * S) ** 2) / (r * S)
    return np.exp(-d * d)[..., None] * np.array(color, np.float32)[None, None, :] * k

def night_bg(S):
    """Ночной фон: тёмно-синий центр, магия сверху слева, огонь снизу справа."""
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    r = np.sqrt((x - S / 2) ** 2 + (y - S / 2) ** 2) / (S / 2)
    c0, c1 = np.array([14, 18, 48], np.float32), np.array([3, 4, 10], np.float32)
    bg = c0 + (c1 - c0) * np.clip(r, 0, 1)[..., None] ** 1.2
    bg += blob(S, 0.15, 0.15, 0.4, (70, 60, 220), 0.35) + blob(S, 0.88, 0.88, 0.4, (230, 70, 10), 0.35)
    return Image.fromarray(np.clip(bg, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')

def crop(im):
    return im.crop(im.getbbox())

def halo(im, rad, alpha):
    """Мягкая тёмная тень под фигурой — отделяет её от любого фона."""
    a = im.split()[3].filter(ImageFilter.GaussianBlur(rad))
    sh = Image.new('RGBA', im.size, (2, 3, 10, 0)); sh.putalpha(a.point(lambda v: int(v * alpha)))
    return sh

def place(canvas, im, cx, cy):
    canvas.alpha_composite(im, (int(cx - im.width / 2), int(cy - im.height / 2)))

def main():
    emb = crop(Image.open(os.path.join(D, 'spellstrike_emblem.png')).convert('RGBA'))
    word = crop(Image.open(os.path.join(D, 'spellstrike_logo_transparent.png')).convert('RGBA'))
    pad = 60
    def padded(im):
        c = Image.new('RGBA', (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0)); c.alpha_composite(im, (pad, pad)); return c
    emb, word = padded(emb), padded(word)

    # ---- аватар 800 (рисуем 1600)
    S = 1600
    av = night_bg(S)
    k = S * 0.82 / max(emb.size); e = emb.resize((int(emb.width * k), int(emb.height * k)), Image.LANCZOS)
    place(av, halo(e, 30, 0.7), S / 2, S / 2); place(av, e, S / 2, S / 2)
    av.convert('RGB').resize((800, 800), Image.LANCZOS).save(os.path.join(D, 'spellstrike_avatar.png'))
    av.convert('RGB').save(os.path.join(D, 'spellstrike_avatar_1600.png'))

    # ---- вертикальная связка: значок над надписью
    W = 1600
    e = emb.resize((int(emb.width * W * 0.78 / emb.width), int(emb.height * W * 0.78 / emb.width)), Image.LANCZOS)
    w = word.resize((int(W * 0.8), int(word.height * W * 0.8 / word.width)), Image.LANCZOS)
    H = e.height + w.height - 120
    v = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    place(v, e, W / 2, e.height / 2)
    place(v, halo(w, 18, 0.9), W / 2, e.height - 120 + w.height / 2); place(v, w, W / 2, e.height - 120 + w.height / 2)
    v.save(os.path.join(D, 'spellstrike_lockup_vertical.png'))

    # ---- горизонтальная связка: значок слева, надпись справа
    Hh = 1000
    e = emb.resize((int(emb.width * Hh / emb.height), Hh), Image.LANCZOS)
    w = word.resize((int(word.width * Hh * 0.62 / word.height), int(Hh * 0.62)), Image.LANCZOS)
    Wh = e.width + w.width - 120
    h = Image.new('RGBA', (Wh, Hh), (0, 0, 0, 0))
    place(h, e, e.width / 2, Hh / 2)
    place(h, halo(w, 18, 0.9), e.width - 120 + w.width / 2, Hh / 2); place(h, w, e.width - 120 + w.width / 2, Hh / 2)
    h.save(os.path.join(D, 'spellstrike_lockup_horizontal.png'))
    print('готово:', D)

if __name__ == '__main__':
    main()
