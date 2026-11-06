# Звук таинственного трейлера (30 с): ветер, гул ночного стадиона, «клац» прожекторов, сердцебиение,
# нарезка с ударами, «вжухами» и рёвом трибун, тишина перед титром, низкий удар на титре.
# Голос комментатора: положи записи в Builds/Trailer/voice/ (v1.wav … v6.wav, реплики — в teaser_cues.py);
# под голосом остальной звук автоматически приглушается. Потом запусти этот скрипт снова и перерендерь ролик.
# Запуск: python teaser_audio.py   (нужны numpy, scipy и ffmpeg) → Builds/Trailer/teaser_audio.wav
# Трибуны: Gregor Quendel, «Free Crowd Cheering Sounds» (CC BY 4.0) и Quistard, freesound.org/s/237684 (CC BY 3.0) —
# указать в описании видео.
import os, sys, subprocess, zipfile, tempfile
import numpy as np
from scipy.signal import butter, sosfilt

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.environ.get('ARENA_ROOT') or os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import teaser_cues as Q

SR = 48000
DUR = Q.F_END / Q.FPS
N = int(SR * DUR)
OUTDIR = os.path.join(ROOT, 'Builds', 'Trailer')
ZIP = os.path.join(ROOT, 'sounds', 'Gregor Quendel - Free Crowd Cheering Sounds.zip')
OOH = os.path.join(ROOT, 'sounds', '237684__quistard__football-crowd-near-miss-ooh-2-southampton-vs-hull-at-saint-marys-stadium.wav')
rng = np.random.default_rng(7)
LANG = os.environ.get('ARENA_LANG') or (sys.argv[1] if len(sys.argv) > 1 else 'ru')   # python teaser_audio.py en — английский голос

def sec(frame): return frame / Q.FPS
def at(t): return int(t * SR)

# ---------------------------------------------------------------- загрузка и фильтры
_tmp = tempfile.mkdtemp(prefix='teaser_snd_')
def gregor(num):
    with zipfile.ZipFile(ZIP) as z:
        name = next(n for n in z.namelist() if (' - %02d - ' % num) in n and not n.startswith('__MACOSX') and n.endswith('.wav'))
        p = os.path.join(_tmp, '%02d.wav' % num)
        if not os.path.exists(p):
            with open(p, 'wb') as f: f.write(z.read(name))
        return p

def load(path, start=0.0, dur=None):
    cmd = ['ffmpeg', '-v', 'error', '-ss', str(start)] + (['-t', str(dur)] if dur else []) + ['-i', path, '-ac', '2', '-ar', str(SR), '-f', 'f32le', '-']
    a = np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype=np.float32).reshape(-1, 2).astype(np.float64)
    return a / max(1e-9, np.abs(a).max())

def filt(x, kind, f, order=2):
    sos = butter(order, f, btype=kind, fs=SR, output='sos')
    return sosfilt(sos, x, axis=0)

def env(points, n=N):
    """Огибающая по точкам [(секунда, уровень)], линейно."""
    ts, vs = zip(*sorted(points))
    return np.interp(np.arange(n) / SR, ts, vs)[:, None]

def db(v): return 10 ** (v / 20)

mix = np.zeros((N, 2))
def add(sig, t, gain=1.0):
    """Положить звук в микс с секунды t."""
    i = at(t)
    if i >= N: return
    s = sig if sig.ndim == 2 else np.stack([sig, sig], 1)
    s = s[:N - i]
    mix[i:i + len(s)] += s * gain

def noise(dur, color='white'):
    n = int(dur * SR); w = rng.standard_normal((n, 2))
    if color == 'brown':
        w = np.cumsum(w, axis=0); w = filt(w, 'highpass', 20); w /= np.abs(w).max()
    return w

def decay(dur, tau):
    t = np.arange(int(dur * SR)) / SR
    return np.exp(-t / tau)[:, None]

def tone(freq0, freq1, dur, tau=None):
    n = int(dur * SR); t = np.arange(n) / SR
    f = np.linspace(freq0, freq1, n); ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.sin(ph)[:, None] * np.ones((1, 2))
    return s * (decay(dur, tau) if tau else 1)

# ---------------------------------------------------------------- готовые звуки
def thump(freq=55, dur=0.6, tau=0.16):            # низкий удар (прожектор, шаг, удар по мячу)
    return tone(freq * 1.8, freq, dur, tau) + filt(noise(dur), 'lowpass', 180) * decay(dur, tau * 0.4) * 0.6

def clunk():                                       # «клац» мощного рубильника прожектора + гул
    d = 1.4
    s = thump(48, d, 0.22) * 1.0
    s += filt(noise(d), 'bandpass', [900, 3200]) * decay(d, 0.018) * 0.7       # металлический щелчок
    s += filt(noise(d), 'lowpass', 400) * decay(d, 0.5) * 0.25                  # гулкий хвост
    s += tone(100, 100, d) * decay(d, 0.6) * 0.06                               # гудение лампы
    return s

def whoosh(dur=0.35, up=True):                     # «вжух» на склейке
    n = noise(dur); t = np.linspace(0, 1, len(n))[:, None]
    lo = filt(n, 'bandpass', [300, 1500]); hi = filt(n, 'bandpass', [1500, 6000])
    k = t if up else 1 - t
    e = np.sin(np.pi * np.linspace(0, 1, len(n))) ** 2
    return (lo * (1 - k) + hi * k) * e[:, None]

def kick():                                        # удар по мячу
    d = 0.5
    return thump(70, d, 0.09) * 1.2 + filt(noise(d), 'bandpass', [600, 2500]) * decay(d, 0.012) * 0.8

def fire_roar(dur):                                # рёв огня: низкий шум + треск
    n = filt(noise(dur, 'brown'), 'lowpass', 900) * 1.6
    crack = (rng.random((int(dur * SR), 1)) > 0.9993) * rng.standard_normal((int(dur * SR), 2))
    crack = filt(crack, 'highpass', 2000) * 3
    e = env([(0, 0), (0.05, 1), (dur * 0.7, 0.8), (dur, 0)], len(n))
    return (n + crack) * e

def net_hit():                                     # мяч в сетке: шелест + глухой удар
    d = 0.9
    return filt(noise(d), 'bandpass', [2000, 7000]) * decay(d, 0.12) * 0.5 + thump(60, d, 0.12) * 0.7

def spin_whistle(dur):                             # свист закрученного мяча
    n = int(dur * SR); t = np.arange(n) / SR
    f = 950 + 400 * t / dur; trem = 0.6 + 0.4 * np.sin(2 * np.pi * 14 * t)
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) * trem * np.sin(np.pi * t / dur)
    return s[:, None] * np.ones((1, 2)) * 0.35 + filt(noise(dur), 'bandpass', [3000, 8000]) * 0.08

def chain(dur, ticks=26):                          # звенья цепи крюка
    s = np.zeros((int(dur * SR), 2))
    for i in range(ticks):
        p = int(i / ticks * len(s)); g = filt(noise(0.03), 'bandpass', [2500 + rng.random() * 2500, 9000]) * decay(0.03, 0.006)
        s[p:p + len(g)] += g[:len(s) - p] * (0.5 + 0.5 * rng.random())
    return s

def zap():                                         # телепорт
    return tone(2400, 180, 0.35, 0.12) * 0.6 + filt(noise(0.35), 'highpass', 3000) * decay(0.35, 0.05) * 0.4

def ice():                                         # лёд вырастает: хруст + звон
    d = 1.2
    crunch = filt(noise(d), 'bandpass', [1200, 6000]) * env([(0, 0), (0.02, 1), (0.25, 0.5), (0.5, 0)], int(d * SR))
    chime = sum(tone(f, f, d, 0.35) * a for f, a in ((2093, 0.12), (2637, 0.08), (3136, 0.06)))
    return crunch * 0.8 + chime + thump(50, d, 0.18) * 0.8

def poof():                                        # Тень растворяется: втягивающийся шёпот
    d = 0.7
    n = filt(noise(d), 'bandpass', [400, 3000]); e = env([(0, 0), (0.45, 1), (0.5, 0.3), (0.7, 0)], len(n))
    return n * e * 0.6

def heartbeat():                                   # «тук-тук»
    return thump(45, 0.5, 0.08) + np.pad(thump(42, 0.4, 0.07) * 0.7, ((int(0.18 * SR), 0), (0, 0)))[:int(0.5 * SR)]

def boom(dur=4.0):                                 # низкий кинематографичный удар на титре
    return tone(70, 32, dur, 0.9) * 1.1 + filt(noise(dur, 'brown'), 'lowpass', 300) * decay(dur, 0.6) * 0.8

# ---------------------------------------------------------------- сборка
def build():
    C = Q.CUTS
    t_lit = sec(Q.LIGHTS[-1]); t_black = sec(C['black']); t_title = sec(C['title']); t_mont = sec(C['fire'])

    # ветер — весь ролик, громче в тишине
    w = filt(noise(DUR, 'brown'), 'lowpass', 700) * 1.2
    gust = 0.55 + 0.45 * np.sin(2 * np.pi * 0.13 * np.arange(N) / SR + 1.0) * np.sin(2 * np.pi * 0.07 * np.arange(N) / SR)
    w = w * gust[:, None]
    add(w * env([(0, 0), (1.5, 0.55), (t_lit, 0.45), (t_mont, 0.15), (t_black, 0.08), (t_black + 0.4, 0.3), (DUR - 0.5, 0.35), (DUR, 0)]), 0)
    # низкий гул-напряжение в темноте
    drone = (tone(41, 41, DUR) + tone(55, 55, DUR) * 0.7 + tone(82.5, 82.5, DUR) * 0.15) * 0.18
    add(drone * env([(0, 0), (5, 1), (sec(Q.LIGHTS[0]), 1.0), (t_lit + 0.3, 0), (t_title, 0), (t_title + 1.5, 0.6), (DUR, 0)]), 0)
    # сердцебиение в тишине
    for t in (1.6, 2.7, 3.7, 4.6):
        add(heartbeat(), t, 0.55)
    # ночной стадион: далёкий приглушённый гул → после света открывается
    amb = load(gregor(10), 2.0, DUR)
    amb_far = filt(amb, 'lowpass', 500)
    add(amb_far * env([(0, 0), (2, 0.35), (t_lit, 0.35), (t_lit + 0.6, 0), (DUR, 0)], len(amb_far)), 0)
    add(amb * env([(0, 0), (t_lit, 0), (t_lit + 0.8, 0.8), (t_mont, 0.5), (t_black, 0.4), (t_black + 0.03, 0), (DUR, 0)], len(amb)), 0)
    wake = load(gregor(5), 1.0, t_mont - t_lit + 0.5)                      # стадион просыпается: трибуны оживают
    add(wake * env([(0, 0), (0.6, 0.25), (t_mont - t_lit, 0.6), (t_mont - t_lit + 0.5, 0)], len(wake)), t_lit)
    # прожекторы: клац-клац-клац-КЛАЦ
    for i, f in enumerate(Q.LIGHTS):
        add(clunk(), sec(f), 0.45 + 0.1 * i)
    # Жар подходит и ставит ногу на мяч; разгорается огонь
    add(thump(60, 0.4, 0.06), sec(Q.STEP_FOOT), 0.7)
    add(fire_roar(sec(C['fire']) - sec(Q.STEP_FOOT) + 0.2), sec(Q.STEP_FOOT), 0.35)
    add(whoosh(1.1, up=True), sec(C['fire']) - 1.1, 0.4)

    # нарезка: трибуны скандируют, нарастая
    chant = load(gregor(8), 0.0, t_black - t_mont + 0.1)
    add(chant * env([(0, 0.0), (0.4, 0.45), (sec(C['final']) - t_mont, 0.6), (sec(Q.FINAL_GOAL) - t_mont, 0.3), (t_black - t_mont, 0.3), (t_black - t_mont + 0.03, 0)], len(chant)), t_mont)
    for k in ('fire', 'net', 'curve', 'hook', 'blink', 'wall', 'shade', 'run', 'final', 'crowd'):
        add(whoosh(0.3, up=True), sec(C[k]) - 0.22, 0.55)
    add(kick(), sec(Q.FIRE_KICK), 1.0); add(fire_roar(sec(Q.FIRE_NET) - sec(Q.FIRE_KICK) + 0.3), sec(Q.FIRE_KICK), 0.7)
    add(net_hit(), sec(Q.FIRE_NET), 1.0)
    cheer = load(gregor(4), 0.3, 3.0)
    add(cheer * env([(0, 0), (0.15, 1), (1.8, 0.6), (3.0, 0)], len(cheer)), sec(Q.FIRE_NET), 0.75)
    add(kick() * 0.8, sec(Q.CURVE_KICK), 0.9); add(spin_whistle(sec(Q.CURVE_END) - sec(Q.CURVE_KICK)), sec(Q.CURVE_KICK), 0.8)
    if os.path.exists(OOH):
        ooh = load(OOH, 0.2, 2.6)
        add(ooh * env([(0, 0), (0.2, 1), (2.0, 0.6), (2.6, 0)], len(ooh)), sec(Q.CURVE_KICK) + 0.5, 0.55)
    add(chain(sec(Q.HOOK_GRAB) - sec(Q.HOOK_THROW)), sec(Q.HOOK_THROW), 0.8)
    add(thump(90, 0.3, 0.05), sec(Q.HOOK_GRAB), 0.9); add(filt(noise(0.3), 'bandpass', [1500, 5000]) * decay(0.3, 0.03), sec(Q.HOOK_GRAB), 0.9)
    add(chain(sec(Q.HOOK_BACK) - sec(Q.HOOK_GRAB), 18), sec(Q.HOOK_GRAB), 0.6)
    add(zap(), sec(Q.BLINK) - 0.05, 0.9)
    add(ice(), sec(Q.WALL_PUNCH), 0.9); add(thump(80, 0.3, 0.05), sec(Q.WALL_HIT), 0.7)
    add(poof(), sec(Q.SHADE_VANISH) - 0.45, 1.0)
    add(fire_roar(sec(C['final']) - sec(C['run'])) * env([(0, 0.2), (sec(C['final']) - sec(C['run']), 1.0)], int((sec(C['final']) - sec(C['run'])) * SR)), sec(C['run']), 0.6)
    for t in np.arange(sec(C['run']), sec(C['final']), 0.42):
        add(heartbeat(), t, 0.5)                                        # сердце бьётся чаще перед финальным ударом
    # финальный удар и гол
    add(kick(), sec(Q.FINAL_KICK), 1.3); add(thump(38, 1.0, 0.3), sec(Q.FINAL_KICK), 1.0)
    add(fire_roar(sec(Q.FINAL_GOAL) - sec(Q.FINAL_KICK) + 0.4), sec(Q.FINAL_KICK), 0.9)
    add(net_hit(), sec(Q.FINAL_GOAL), 1.3); add(boom(1.6), sec(Q.FINAL_GOAL), 0.6)
    roar = load(gregor(1), 4.0, t_black - sec(Q.FINAL_GOAL) + 0.1)
    add(roar * env([(0, 0), (0.12, 1.0), (t_black - sec(Q.FINAL_GOAL), 0.9), (t_black - sec(Q.FINAL_GOAL) + 0.03, 0)], len(roar)), sec(Q.FINAL_GOAL), 0.9)

    # чёрный экран — тишина; титр — низкий удар и далёкие трибуны
    mix[at(t_black):at(t_title)] *= np.linspace(1, 0, at(t_title) - at(t_black))[:, None] ** 40   # резкий обрыв
    add(boom(DUR - sec(Q.TITLE_POP)), sec(Q.TITLE_POP) - 0.05, 0.9)
    tail = load(gregor(10), 20.0, DUR - t_title)
    add(filt(tail, 'lowpass', 900) * env([(0, 0), (1.0, 0.3), (DUR - t_title - 0.6, 0.3), (DUR - t_title, 0)], len(tail)), t_title)

    # голос комментатора (если записан): приглушить всё под ним
    vdir = os.path.join(OUTDIR, 'voice' if LANG == 'ru' else 'voice_' + LANG); duck = np.ones((N, 1)); voice = np.zeros((N, 2)); found = []
    for fn, t, text, how in (Q.VOICE if LANG == 'ru' else Q.VOICE_EN):
        p = os.path.join(vdir, fn)
        if not os.path.exists(p): continue
        v = load(p) * 0.9; i = at(t); v = v[:N - i]
        voice[i:i + len(v)] += v; found.append(fn)
        e = np.zeros(N); e[i:i + len(v)] = 1
        k = int(0.15 * SR); e = np.convolve(e, np.ones(k) / k, mode='same')
        duck = np.minimum(duck, 1 - 0.55 * e[:, None])
    out = mix * duck + voice
    # мастер: мягкое ограничение и нормировка до -1 дБ
    out = np.tanh(out * 0.9) / np.tanh(0.9)
    out *= db(-1) / max(1e-9, np.abs(out).max())
    os.makedirs(OUTDIR, exist_ok=True)
    pcm = (out * 32767).astype(np.int16)
    path = os.path.join(OUTDIR, 'teaser_audio.wav' if LANG == 'ru' else 'teaser_audio_%s.wav' % LANG)
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 's16le', '-ar', str(SR), '-ac', '2', '-i', '-', path], input=pcm.tobytes(), check=True)
    print('звук:', path, '| голос:', ', '.join(found) if found else 'нет (положи v1.wav… в ' + vdir + ')')

if __name__ == '__main__':
    build()
