# Shorts из настоящих записей игры: хайлайты ботов (F8 в меню) записаны Unity Recorder в 4K (Recordings/Movie_*.mp4),
# рядом — трек мяча по кадрам (track_*.csv: frame;highlight;angle;gap;bx;by — bx,by — мяч на экране, 0..1).
# Вертикальный кадр 1080x1920 вырезается из 4K на всю высоту и плавно ведёт мяч. Звук — из записи (игра и трибуны).
# Запуск: python reel_shorts.py [имя ролика ...]   Результат: Builds/Shorts/*.mp4
import csv, os, subprocess, sys, tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REC = os.path.join(ROOT, 'Recordings')
OUT = os.path.join(ROOT, 'Builds', 'Shorts')
FONT = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts', 'RussoOne-Regular.ttf')
SW, SH, FPS = 3840, 2160, 60          # запись
CW = 1215                             # ширина вырезки 9:16 на всю высоту 2160

# Запись -> трек и сдвиг (видео_с = кадр_трека/60 + OFFSET; сдвиг находим по первому чёрному кадру между моментами)
SOURCES = {'m5': ('Movie_005.mp4', 'track_20261104_0002.csv', 151)}

# Планы в записи: 0 — трансляция (гол на 5-й секунде), 1 — «за мячом» в замедлении, 2 — облёт забившего.
# Кусок: (момент, план, с какой секунды плана, по какую; None — до конца плана)
SHORTS = {
    'reel1_fire': ('m5', 'FOOTBALL WITH SPELLS', [(1, 0, 2.2, 5.6), (1, 1, 2.6, None), (1, 2, 0, None)]),
    'reel2_top3': ('m5', 'WHICH GOAL IS BEST? 1, 2 OR 3', [(1, 0, 2.6, 5.7), (1, 2, 0, 1.2),
                                                          (3, 0, 2.6, 5.7), (3, 2, 0, 1.2),
                                                          (5, 0, 2.6, 5.7), (5, 2, 0, 1.4)]),
    'reel3_shadow': ('m5', 'HE WAS INVISIBLE', [(3, 0, 1.8, 5.6), (3, 1, 2.4, None), (3, 2, 0, None)]),
}

def load_track(name):
    rows = list(csv.DictReader(open(os.path.join(REC, name), encoding='utf-8'), delimiter=';'))
    plans = {}
    for r in rows:
        key = (int(r['highlight']), int(r['angle']))
        if r['gap'] == '1': continue
        plans.setdefault(key, []).append((int(r['frame']), float(r['bx'])))
    return plans

def esc(s): return s.replace('\\', '\\\\').replace(':', '\\:').replace("'", "\\'")

def render_piece(src, plans, offset, hid, angle, a, b, path, first, hook, total_before):
    pts = plans[(hid, angle)]
    f0, f1 = pts[0][0], pts[-1][0]
    s0 = f0 + int(a * FPS); s1 = f1 if b is None else min(f1, f0 + int(b * FPS))
    # плавное ведение мяча (только «трансляция»; в замедлении и облёте камера и так на мяче/герое)
    cmds, x = [], None
    for fr, bx in pts:
        if fr < s0 - 120 or fr > s1: continue
        tx = bx if (angle == 0 and 0.02 < bx < 0.98) else (x if x is not None else 0.5)
        x = tx if x is None else x + (tx - x) * 0.06
        if fr >= s0:
            px = int(min(max(x * SW - CW / 2, 0), SW - CW))
            cmds.append(f'{(fr - s0) / FPS:.4f} crop x {px};')
    cf = path + '.cmd'
    open(cf, 'w').write('\n'.join(cmds) + '\n')
    x0 = cmds[0].split()[-1].rstrip(';')
    font = FONT.replace('\\', '/').replace(':', '\\:')
    vf = f"sendcmd=f='{cf}',crop={CW}:{SH}:{x0}:0,scale=1080:1920:flags=lanczos,fps=30"
    if first and hook:   # одна строка-крючок, первые 2 с
        vf += (f",drawtext=fontfile='{font}':text='{esc(hook)}':fontsize=64:fontcolor=white:borderw=5:bordercolor=black@0.8"
               f":x=(w-text_w)/2:y=230:enable='lt(t,2.0)'")
    t0 = s0 / FPS + offset / FPS; dur = (s1 - s0) / FPS
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-ss', f'{t0:.3f}', '-t', f'{dur:.3f}', '-i', os.path.join(REC, src),
                    '-vf', vf, '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p',
                    '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', path], check=True)
    return dur

def build(name):
    sid, hook, pieces = SHORTS[name]
    src, track, offset = SOURCES[sid]
    plans = load_track(track)
    tmp = tempfile.mkdtemp()
    parts, total = [], 0
    for i, (hid, angle, a, b) in enumerate(pieces):
        p = os.path.join(tmp, f'p{i:02d}.mp4')
        total += render_piece(src, plans, offset, hid, angle, a, b, p, i == 0, hook, total)
        parts.append(p)
    lst = os.path.join(tmp, 'list.txt')
    open(lst, 'w').write(''.join(f"file '{p}'\n" for p in parts))
    out = os.path.join(OUT, name + '.mp4')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'concat', '-safe', '0', '-i', lst,
                    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', out], check=True)
    print('ok', out, f'{total:.1f} с')

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for n in (sys.argv[1:] or SHORTS):
        build(n)
