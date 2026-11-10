# Вертикальные ролики 1080x1920 для YouTube Shorts / TikTok из геймплейного трейлера.
# Фон — размытый кадр, по центру — игра (обрезка 4:3, крупнее), сверху крючок, снизу название.
# Запуск: python make_shorts.py  (нужен ffmpeg). Результат — Builds/Shorts/*.mp4
import os, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Builds', 'Trailer', 'spellstrike_gameplay_en_sound.mp4')
OUT = os.path.join(ROOT, 'Builds', 'Shorts')
AUDIO = os.path.join(ROOT, 'Builds', 'Trailer', 'gameplay_audio_nomusic.wav')  # звук игры и трибуны, без музыки
FONT = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts', 'RussoOne-Regular.ttf')

# (файл, начало с, конец с, крючок сверху (строки), подписи [(с, по, текст)] — время от начала ролика)
SHORTS = [
    ('short1_spells', 20.0, 44.0, ['FOOTBALL...', 'BUT EVERY PLAYER', 'HAS A SPELL'],
     [(0, 4, 'HOOK'), (4, 8, 'CURVE SHOT'), (8, 12, 'TELEPORT'), (12, 16, 'ICE WALL'),
      (16, 20, 'INVISIBILITY'), (20, 24, 'FIRE STRIKE')]),
    ('short2_golden_goal', 44.0, 58.0, ['LAST MINUTE.', 'GOLDEN GOAL.'], []),
    ('short3_with_friends', 0.0, 20.0, ['4v4 FOOTBALL', 'WITH MAGIC'],
     [(5, 20, 'PLAY WITH FRIENDS')]),
]

def esc(s):
    return s.replace('\\', '\\\\').replace(':', '\\:').replace("'", "\\'").replace('%', '\\%')

def build(name, t0, t1, hook, caps):
    font = FONT.replace('\\', '/').replace(':', '\\:')
    dur = t1 - t0
    f = []
    f.append('[0:v]split[a][b]')
    f.append('[a]scale=-2:1920,crop=1080:1920,boxblur=28:2,eq=brightness=-0.18:saturation=1.2[bg]')
    f.append('[b]crop=1440:1080,scale=1080:810[fg]')
    f.append('[bg][fg]overlay=0:555[v0]')
    cur = 'v0'
    n = 0
    def text(txt, y, size, color='white', extra=''):
        nonlocal cur, n
        n += 1
        f.append(f"[{cur}]drawtext=fontfile='{font}':text='{esc(txt)}':fontsize={size}:fontcolor={color}"
                 f":borderw=6:bordercolor=black@0.85:x=(w-text_w)/2:y={y}{extra}[t{n}]")
        cur = f't{n}'
    # крючок сверху
    lh = 96
    y0 = 520 - len(hook) * lh
    for i, line in enumerate(hook):
        text(line, y0 + i * lh, 84, 'white' if i < len(hook) - 1 else '#FFD24A')
    # подписи способностей под картинкой
    for a, b, txt in caps:
        text(txt, 1395, 72, '#7FE7FF', f":enable='between(t,{a},{b - 0.05})'")
    # название снизу
    text('SPELLSTRIKE', 1560, 110, '#FFB43A')
    text('COMING SOON TO STEAM', 1690, 46, 'white@0.9')
    fc = ';'.join(f)
    out = os.path.join(OUT, name + '.mp4')
    cmd = ['ffmpeg', '-v', 'error', '-y', '-ss', str(t0), '-t', str(dur), '-i', SRC, '-ss', str(t0), '-t', str(dur), '-i', AUDIO,
           '-filter_complex', fc, '-map', f'[{cur}]', '-map', '1:a',
           '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', '-r', '30',
           '-c:a', 'aac', '-b:a', '192k', '-af', f'afade=t=out:st={dur - 0.6}:d=0.6',
           '-movflags', '+faststart', out]
    subprocess.run(cmd, check=True)
    print('ok', out)

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    only = sys.argv[1:]
    for s in SHORTS:
        if not only or s[0] in only:
            build(*s)
