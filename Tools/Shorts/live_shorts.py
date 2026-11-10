# Вертикальные ролики из записи своей игры (Recordings/Movie_*.mp4 + live_*.csv).
# В игре клавиша T отмечает дубль: чёрный кадр в начале — метка, по ней находится начало (MARKERS).
# Кадр 1080x1920 вырезается из 4K и плавно ведёт мяч по треку.
# Запуск: python live_shorts.py [имя ...]   Результат: Builds/Shorts/*.mp4
import csv, os, subprocess, sys, tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REC = os.path.join(ROOT, 'Recordings')
OUT = os.path.join(ROOT, 'Builds', 'Shorts')
FONT = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts', 'RussoOne-Regular.ttf')
SRC = os.path.join(REC, 'Movie_007.mp4')
SW, SH, FPS = 3840, 2160, 60

# Номер дубля -> секунда чёрной метки в записи (найдено blackdetect)
MARKERS = {1: 18.75, 2: 88.38, 3: 147.63, 4: 206.57, 5: 247.00, 6: 283.18, 7: 322.13, 8: 368.05,
           9: 429.67, 10: 535.68, 11: 592.78, 12: 612.97, 13: 641.30, 14: 669.70, 15: 694.72,
           16: 710.87, 17: 772.87, 18: 794.20, 19: 822.13}

# имя: (дубль, от, до — секунды ОТ НАЧАЛА ДУБЛЯ, высота вырезки из 2160 (меньше — крупнее), крючок)
CLIPS = {
    'live1_fire':    (1, 20.5, 29.0, 1500, 'EVERY PLAYER HAS A SPELL'),
    'live2_four':    (2, 29.5, 38.5, 1500, '4v4 FOOTBALL WITH MAGIC'),
    'live3_quick':   (6, 3.0, 11.5, 1450, 'SPELLS INSTEAD OF TACKLES'),
    'live4_comeback':(8, 41.5, 50.5, 1500, 'PLAY WITH FRIENDS'),
    'live5_win':     (12, 0.8, 10.0, 1450, 'LAST GOAL OF THE MATCH'),
    'live6_fifth':   (19, 10.0, 18.0, 1380, 'FIVE GOALS IN ONE MATCH'),
}

def track(take):
    for f in sorted(os.listdir(REC)):
        if not f.startswith('live_') or not f.endswith('.csv'): continue
        rows = list(csv.DictReader(open(os.path.join(REC, f), encoding='utf-8'), delimiter=';'))
        if rows and int(rows[0]['highlight']) == take:
            return [(float(r['bx']), float(r['by'])) for r in rows]
    raise SystemExit('нет трека для дубля %d' % take)

def esc(s): return s.replace('\\', '\\\\').replace(':', '\\:').replace("'", "\\'")

def build(name):
    take, a, b, ch, hook = CLIPS[name]
    pts = track(take)
    cw = int(ch * 9 / 16) // 2 * 2
    f0, f1 = int(a * FPS), min(int(b * FPS), len(pts) - 1)
    cmds, x, y = [], None, None
    for fr in range(max(0, f0 - 90), f1 + 1):
        bx, by = pts[fr]
        tx = bx if 0.02 < bx < 0.98 else (x if x is not None else 0.5)
        ty = by if 0.02 < by < 0.98 else (y if y is not None else 0.5)
        x = tx if x is None else x + (tx - x) * 0.05          # плавно ведём мяч
        y = ty if y is None else y + (ty - y) * 0.03
        if fr >= f0:
            px = int(min(max(x * SW - cw / 2, 0), SW - cw))
            py = int(min(max((1 - y) * SH - ch * 0.52, 300), SH - ch - 90))   # 250 / -90 — чтобы полосы интерфейса остались за кадром
            cmds.append(f'{(fr - f0) / FPS:.4f} crop x {px}, crop y {py};')
    tmp = tempfile.mkdtemp(); cf = os.path.join(tmp, 'c.txt')
    open(cf, 'w').write('\n'.join(cmds) + '\n')
    x0, y0 = cmds[0].split()[3].rstrip(','), cmds[0].split()[-1].rstrip(';')
    font = FONT.replace('\\', '/').replace(':', '\\:')
    vf = (f"sendcmd=f='{cf}',crop={cw}:{ch}:{x0}:{y0},scale=1080:1920:flags=lanczos,fps=30"
          f",drawtext=fontfile='{font}':text='{esc(hook)}':fontsize=58:fontcolor=white:borderw=5:bordercolor=black@0.85"
          f":x=(w-text_w)/2:y=250:enable='lt(t,2.2)'"
          f",drawtext=fontfile='{font}':text='SPELLSTRIKE':fontsize=52:fontcolor=0xFFD24A:borderw=5:bordercolor=black@0.8"
          f":x=(w-text_w)/2:y=1735")
    t0 = MARKERS[take] + a; dur = (f1 - f0) / FPS
    out = os.path.join(OUT, name + '.mp4')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-ss', f'{t0:.3f}', '-t', f'{dur:.3f}', '-i', SRC,
                    '-vf', vf, '-c:v', 'libx264', '-preset', 'medium', '-crf', '19', '-pix_fmt', 'yuv420p',
                    '-c:a', 'aac', '-b:a', '192k', '-af', f'afade=t=out:st={max(0,dur-0.5):.2f}:d=0.5',
                    '-movflags', '+faststart', out], check=True)
    print('ok', name, f'{dur:.1f} с')

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for n in (sys.argv[1:] or CLIPS): build(n)
