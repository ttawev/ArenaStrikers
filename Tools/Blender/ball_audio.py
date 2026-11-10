# Звук трейлера «Мяч в центре» (26 с), вариант «Мяч вспоминает»: тишина → «клац» прожекторов → сердце под наездом
# на мяч; на ударах сердца — обрывки воспоминаний по 0,3 с (сетка, огонь, рёв трибун, сирена табло, звон штанги
# со льдом, магический звон) → швы мяча звенят в такт сердцу → прожекторы гаснут → тишина → низкий удар на логотипе.
# Запуск: python ball_audio.py   (нужны numpy, scipy и ffmpeg) → Builds/Trailer/ball_audio.wav
# Трибуны: Gregor Quendel, «Free Crowd Cheering Sounds» (CC BY 4.0) — указать в описании видео.
import os, sys, subprocess, zipfile, tempfile
import numpy as np
from scipy.signal import butter, sosfilt

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.environ.get('ARENA_ROOT') or os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import ball_cues as Q

SR = 48000
DUR = Q.F_END / Q.FPS
N = int(SR * DUR)
OUTDIR = os.path.join(ROOT, 'Builds', 'Trailer')
ZIP = os.path.join(ROOT, 'sounds', 'Gregor Quendel - Free Crowd Cheering Sounds.zip')
OOH = os.path.join(ROOT, 'sounds', '237684__quistard__football-crowd-near-miss-ooh-2-southampton-vs-hull-at-saint-marys-stadium.wav')
rng = np.random.default_rng(7)

def sec(frame): return frame / Q.FPS
def at(t): return int(t * SR)

# ---------------------------------------------------------------- загрузка и фильтры
_tmp = tempfile.mkdtemp(prefix='ball_snd_')
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

# ---------------------------------------------------------------- свои звуки
def clang():                                       # мяч в штангу: металлический «дзынь»
    d = 1.6
    s = sum(tone(f, f * 0.995, d, tau) * a for f, a, tau in ((1480, 0.5, 0.5), (2350, 0.3, 0.35), (3720, 0.18, 0.25), (5100, 0.08, 0.15)))
    s[:int(0.4 * SR)] += thump(90, 0.4, 0.04) * 0.6
    return s

def buzzer(d=0.6):                                 # сирена табло
    n = int(d * SR); t = np.arange(n) / SR
    s = np.sign(np.sin(2 * np.pi * 220 * t)) * 0.5 + np.sign(np.sin(2 * np.pi * 330 * t)) * 0.3
    return filt(s[:, None] * np.ones((1, 2)), 'lowpass', 2500) * 0.5

def shimmer(d=1.2, gain=1.0):                      # магия: звенящий аккорд с дрожанием
    n = int(d * SR); t = np.arange(n) / SR
    s = np.zeros(n)
    for f, a in ((880, 0.5), (1318.5, 0.4), (1760, 0.3), (2637, 0.2), (3520, 0.1)):
        s += np.sin(2 * np.pi * f * t + 3 * np.sin(2 * np.pi * 6 * t)) * a
    s *= np.exp(-t / (d * 0.35)) * (1 - np.exp(-t / 0.01))
    return s[:, None] * np.ones((1, 2)) * 0.35 * gain + filt(noise(d), 'highpass', 6000) * decay(d, d * 0.2) * 0.05 * gain

# ---------------------------------------------------------------- сборка
def build():
    FL = Q.FLASH
    t_lit = sec(Q.LIGHTS_ON[-1]); t_off0 = sec(Q.LIGHTS_OFF[0]); t_off = sec(Q.LIGHTS_OFF[-1])
    t_dark = sec(Q.DARK); t_title = sec(Q.TITLE)

    # ветер — тихо в начале, чуть слышно под наездом, почти пропадает в темноте
    w = filt(noise(DUR, 'brown'), 'lowpass', 600) * 1.2
    gust = 0.55 + 0.45 * np.sin(2 * np.pi * 0.13 * np.arange(N) / SR + 1.0) * np.sin(2 * np.pi * 0.07 * np.arange(N) / SR)
    add(w * gust[:, None] * env([(0, 0), (1.2, 0.12), (t_lit, 0.3), (t_off, 0.25), (t_dark, 0.12), (t_title, 0.1), (DUR - 0.5, 0.15), (DUR, 0)]), 0)
    # низкий гул-напряжение: нарастает под наездом
    drone = (tone(41, 41, DUR) + tone(55, 55, DUR) * 0.7 + tone(82.5, 82.5, DUR) * 0.2) * 0.2
    add(drone * env([(0, 0), (t_lit, 0.15), (t_off0, 1.0), (t_off, 0.5), (t_dark, 0), (t_title, 0), (t_title + 1.5, 0.5), (DUR, 0)]), 0)
    # прожекторы: клац-клац-клац-КЛАЦ
    for i, f in enumerate(Q.LIGHTS_ON):
        add(clunk(), sec(f), 0.45 + 0.1 * i)
    # далёкие трибуны — приглушённо (стадион пуст, всё громкое — только в воспоминаниях)
    amb = filt(load(gregor(10), 2.0, DUR), 'lowpass', 500)
    add(amb * env([(0, 0), (t_lit, 0), (t_lit + 1.0, 0.16), (t_off0, 0.16), (t_off, 0), (DUR, 0)], len(amb)), 0)
    # сердце
    starts = {FL[k]['start']: k for k in Q.ORDER}
    for i, f in enumerate(Q.BEATS):
        g = 0.45 + 0.35 * i / len(Q.BEATS)
        add(heartbeat(), sec(f), g * (1.35 if f in starts else 1.0))
    # вспышки-воспоминания: звук идёт отдельно и резко обрывается на склейке
    for k in Q.ORDER:
        s0, s1 = sec(FL[k]['start']), sec(FL[k]['end'])
        fb = np.zeros_like(mix)
        def put(sig, t, g=1.0):
            i = at(t)
            if i >= N: return
            s_ = sig if sig.ndim == 2 else np.stack([sig, sig], 1); s_ = s_[:N - i]
            fb[i:i + len(s_)] += s_ * g
        dur = s1 - s0 + 0.05
        if k == 'net':
            put(whoosh(0.12, up=True), s0, 0.6); put(net_hit(), s0 + 4 / Q.FPS, 1.2)
            put(load(gregor(4), 0.5, dur), s0, 0.7)
        if k == 'fire':
            put(fire_roar(dur), s0, 1.1); put(whoosh(dur, up=True), s0, 0.5)
        if k == 'crowd':
            put(load(gregor(1), 4.5, dur), s0, 1.2)
        if k == 'score':
            put(buzzer(dur), s0 + 3 / Q.FPS, 0.9); put(load(gregor(4), 1.0, dur), s0, 0.6)
        if k == 'ice':
            put(clang(), s0 + 3 / Q.FPS, 0.9); put(ice(), s0 + 3 / Q.FPS, 0.8)
        if k == 'arcane':
            put(shimmer(dur + 0.1, 1.3), s0, 1.0)
        i0, i1 = at(s0), at(s1); fade = int(0.008 * SR)
        fb[:i0] = 0
        fb[i1:i1 + fade] *= np.linspace(1, 0, fade)[:, None]; fb[i1 + fade:] = 0
        mix[:] += fb
        # «эхо» воспоминания после обрыва — тихий хвост в реверберации, тает за 0,6 с
        tail = filt(fb[i0:i1], 'lowpass', 1800)
        if len(tail):
            e = np.exp(-np.arange(int(0.6 * SR)) / SR / 0.25)[:, None]
            add(np.resize(tail, (len(e), 2)) * e * 0.12, s1)
    # швы мяча в центре звенят в такт сердцу
    for p in Q.PULSES:
        add(shimmer(1.0, 0.45), sec(p))
    # прожекторы гаснут: тише и глуше, чем включались
    for i, f in enumerate(Q.LIGHTS_OFF):
        add(filt(clunk(), 'lowpass', 2500), sec(f), 0.5 - 0.05 * i)
    # темнота — почти тишина; титр — низкий удар и звон по буквам
    i0, i1 = at(t_dark), at(t_title)
    mix[i0:i1] *= np.linspace(1, 0.15, i1 - i0)[:, None]
    add(boom(DUR - sec(Q.TITLE_POP)), sec(Q.TITLE_POP) - 0.05, 0.9)
    add(shimmer(2.5, 0.5), sec(Q.TITLE_POP) + 0.6)

    out = np.tanh(mix * 0.9) / np.tanh(0.9)
    out *= db(-1) / max(1e-9, np.abs(out).max())
    os.makedirs(OUTDIR, exist_ok=True)
    pcm = (out * 32767).astype(np.int16)
    path = os.path.join(OUTDIR, 'ball_audio.wav')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 's16le', '-ar', str(SR), '-ac', '2', '-i', '-', path], input=pcm.tobytes(), check=True)
    print('звук:', path)

if __name__ == '__main__':
    build()
