# Первый пост для Instagram (1080x1350, 4:5): три героя из геймплейного трейлера (кадр перед надписью MVP),
# сверху логотип с мячом, снизу подпись. Фон — тот же кадр, размытый и затемнённый.
# Запуск: python insta_post.py [секунда_трейлера]   Результат: Builds/Instagram/post01_heroes.jpg
import os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont, ImageEnhance, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Builds', 'Trailer', 'spellstrike_gameplay_en_0001-1950.mp4')
LOGO = os.path.join(ROOT, 'Builds', 'Branding', 'spellstrike_lockup_horizontal.png')
FONT = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts', 'RussoOne-Regular.ttf')
OUT = os.path.join(ROOT, 'Builds', 'Instagram')
T = float(sys.argv[1]) if len(sys.argv) > 1 else 53.60
W, H = 1080, 1350
CROP_X, CROP_W = 268, 1520      # герои в кадре 1920x1080 занимают примерно 280..1780
FRAME_Y = 318                   # где на холсте начинается резкий кадр

def main():
    os.makedirs(os.path.join(OUT, '_work'), exist_ok=True)
    tmp = os.path.join(OUT, '_work', 'frame.png')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-ss', f'{T:.3f}', '-i', SRC, '-frames:v', '1', tmp], check=True)
    src = Image.open(tmp).convert('RGB')
    src = ImageEnhance.Color(ImageEnhance.Contrast(src).enhance(1.06)).enhance(1.15)
    # фон: кадр на всю высоту, размытый и затемнённый
    bw = int(src.width * H / src.height)
    bg = src.resize((bw, H), Image.LANCZOS).crop(((bw - W) // 2, 0, (bw - W) // 2 + W, H))
    bg = ImageEnhance.Brightness(bg.filter(ImageFilter.GaussianBlur(38))).enhance(0.42)
    img = bg.copy()
    # резкий кадр с мягкими краями сверху и снизу
    fh = int(src.height * W / CROP_W)
    fr = src.crop((CROP_X, 0, CROP_X + CROP_W, src.height)).resize((W, fh), Image.LANCZOS)
    mask = Image.new('L', (W, fh), 255); d = ImageDraw.Draw(mask)
    for y in range(fh):
        a = min(1, y / 150, (fh - 1 - y) / 26)
        d.line([(0, y), (W, y)], fill=int(255 * max(0, a)))
    img.paste(fr, (0, FRAME_Y), mask)
    # затемнение сверху (под логотип) и снизу (под подпись)
    shade = Image.new('L', (W, H), 0); d = ImageDraw.Draw(shade)
    for y in range(H):
        top = max(0, 1 - y / 520) ** 1.3 * 235
        bot = max(0, (y - 1045) / 300) ** 0.9 * 225
        d.line([(0, y), (W, y)], fill=int(min(255, max(top, bot))))
    img = Image.composite(Image.new('RGB', (W, H), (7, 6, 18)), img, shade)
    # логотип
    logo = Image.open(LOGO).convert('RGBA'); logo = logo.crop(logo.getbbox())
    lw = 940; logo = logo.resize((lw, int(logo.height * lw / logo.width)), Image.LANCZOS)
    img.paste(logo, ((W - lw) // 2, 46), logo)
    # подпись
    d = ImageDraw.Draw(img)
    def center(text, y, size, fill, stroke=4):
        f = ImageFont.truetype(FONT, size)
        w = d.textlength(text, font=f)
        d.text(((W - w) / 2, y), text, font=f, fill=fill, stroke_width=stroke, stroke_fill=(5, 5, 14))
    center('FOOTBALL WITH SPELLS', 1128, 76, (255, 206, 64))
    center('4v4 ONLINE  ·  PLAY WITH FRIENDS', 1228, 36, (240, 240, 250), 3)
    center('COMING SOON TO STEAM', 1282, 30, (126, 226, 255), 3)
    out = os.path.join(OUT, 'post01_heroes.jpg')
    img.save(out, quality=95, subsampling=0)
    print('ok', out)

if __name__ == '__main__':
    main()
