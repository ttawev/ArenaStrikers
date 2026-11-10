# ======================== SpellStrike — геймплейный трейлер 65 с, 16:9 ========================
# 1) 0–5 с: сразу огненный суперудар Жара в сетку. 2) 5–20 с: розыгрыши 4 на 4 с игровой камеры (сверху под 52°).
# 3) 20–44 с: шесть героев по 4 с — крюк, кручёный, телепорт, ледяной разлом, невидимость, огонь против льда.
# 4) 44–58 с: нарастание, золотой гол в замедлении, празднование, MVP. 5) Титр: значок + SpellStrike + Arcane League.
# Запуск: Blender 5.2 → File → New → General → Scripting → Open → Tools/Blender/gameplay_trailer.py → Run Script.
# Сохраняет Builds/Trailer/gameplay_<версия>.blend; рендер Ctrl+F12 → Builds/Trailer/spellstrike_gameplay_*.mp4.
# Использует trailer.py (стадион, свет, герои, перенос анимаций Mixamo), logo.py и значок из ball_trailer.py / emblem.py.
# Тайминги — в gameplay_cues.py.
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Quaternion

ROOT = os.environ.get('ARENA_ROOT') or r'C:\Users\user\ArenaStrikers'
sys.path.insert(0, os.path.join(ROOT, 'Tools', 'Blender'))
for m_ in ('trailer', 'gameplay_cues', 'logo', 'emblem', 'ball_trailer', 'ball_cues'):
    sys.modules.pop(m_, None)   # повторный запуск в том же Blender — свежие версии
import trailer as T
import gameplay_cues as Q

# ===== НАСТРОЙКИ ВЕРСИИ (поменяй и запусти скрипт заново) =====
LANG = os.environ.get('ARENA_LANG') or 'en'      # 'en' — надписи по-английски (Steam, YouTube); 'ru' — по-русски
TAGLINE = None                                   # None — «COMING SOON» / «СКОРО»; когда будет страница Steam — 'WISHLIST ON STEAM'
PREVIEW = False                                  # True — быстрый черновик: половина разрешения и мало сэмплов
# =================================================================
STILLS = os.environ.get('ARENA_STILLS')          # проверка Claude: сцена без сохранения
STUB = os.environ.get('ARENA_STUB') == '1'       # проверка Claude без моделей: герои — капсулы
F_END = Q.F_END
T.F_END = F_END
T.G = 10 ** 6            # голы и их стробоскоп этот скрипт ставит сам (их шесть)
T.ADS = ['SPELLSTRIKE', 'ARCANE LEAGUE', 'COMING SOON', 'PLAY WITH FRIENDS'] if LANG == 'en' else \
        ['SPELLSTRIKE', 'ARCANE LEAGUE', 'СКОРО', 'ИГРАЙ С ДРУЗЬЯМИ']
OUT = T.OUT
BR = T.BALL_R
GX = T.FL / 2
NETX = GX + T.NET_D - 0.1
BLUE, RED = T.HOME_L, T.AWAY_L
GOLD = T.srgb((1.0, 0.76, 0.22))
BASE_EXP = -0.4
TAG = LANG
random.seed(23)

TXT = {
    'arcade': ('ARCADE 4v4 FOOTBALL', 'АРКАДНЫЙ ФУТБОЛ 4 НА 4'),
    'friends': ('PLAY WITH FRIENDS ONLINE', 'ИГРАЙ С ДРУЗЬЯМИ ОНЛАЙН'),
    'hook': ('HOOK', 'КРЮК'), 'curve': ('CURVE SHOT', 'КРУЧЁНЫЙ УДАР'), 'blink': ('BLINK', 'ТЕЛЕПОРТ'),
    'ice': ('ICE FISSURE', 'ЛЕДЯНОЙ РАЗЛОМ'), 'shadow': ('SHADOW', 'НЕВИДИМОСТЬ'), 'fire': ('FIRE STRIKE', 'ОГНЕННЫЙ УДАР'),
    'golden': ('GOLDEN GOAL', 'ЗОЛОТОЙ ГОЛ'), 'mvp': ('MVP', 'MVP'),
    'heroes': ('6 HEROES · 6 SPELLS', '6 ГЕРОЕВ · 6 ЗАКЛИНАНИЙ'),
}
def tr(k): return TXT[k][0 if LANG == 'en' else 1]

# ---------------------------------------------------------------- анимации
CLIP_NAMES = ['idle', 'idle_show', 'run', 'run_ball', 'kick', 'kick_strong', 'pass', 'throw', 'ground_punch', 'spell',
              'magic', 'dash', 'fall', 'get_up', 'sad', 'defeated', 'cel_victory', 'cel_cheering', 'cel_taunt', 'cel_hip_hop']
CLIPS = {}

def clip_len(n):
    return CLIPS[n].length if n in CLIPS else 1.6

def clip_contact(n):
    if n in CLIPS:
        return CLIPS[n].contact_time()
    return 0.45

def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi

# ---------------------------------------------------------------- герой на поле: путь, действия, взгляд
TURN = 0.42          # рад за кадр (в игре разворот 11 рад/с)
WAIT_STILL = ('cel_', 'sad', 'defeated', 'idle_show')   # эти анимации ждут, пока герой остановится
MOVING_CLIPS = ('dash',)                                 # в этих герой и должен ехать

class Feet:
    """Цель мяча: ноги героя в кадр прилёта (dz — выше, например грудь)."""
    def __init__(self, actor, dz=0.0):
        self.a, self.dz = actor, dz

class Actor:
    def __init__(s, key, folder, team):
        s.key, s.folder, s.team = key, folder, team
        s.wp, s.acts, s.faces, s.vis, s.ghosts = [], [], [], [], []
        s.pos = s.ang = s.hero = None

    def at(s, *pts):
        for p in pts:
            s.wp.append((int(p[0]), float(p[1]), float(p[2])))
        return s

    def show(s, f0, f1):
        s.vis.append((f0, f1)); return s

    def ghost(s, f0, f1):
        s.ghosts.append((f0, f1)); return s

    def look(s, f0, f1, target):
        """Куда смотреть: угол в градусах (0 — к воротам соперника +X) или точка (x, y)."""
        s.faces.append((f0, f1, target)); return s

    def act(s, f, clip, t0=0.0, speed=1.0, dur=None, face=None, loop=False):
        if dur is None:
            dur = int(round(max(0.2, clip_len(clip) - t0) / max(0.05, speed) * 30))
        s.acts.append(dict(f=int(f), clip=clip, t0=t0, speed=speed, dur=int(dur), face=face, loop=loop))
        return s

    def kick(s, f_contact, target, clip='kick_strong', speed=0.8, lead=12, after=20):
        """Удар: касание мяча ровно на кадре f_contact, герой смотрит на цель."""
        ct = clip_contact(clip)
        lead = max(2, min(lead, int(ct / speed * 30)))
        t0 = max(0.0, ct - lead / 30.0 * speed)
        return s.act(f_contact - lead, clip, t0, speed, lead + after, face=target)

    # --- расчёт
    def build(s):
        wp = sorted(s.wp)
        s.pos = [None] * (F_END + 4)
        j = 0
        for f in range(F_END + 4):
            if f <= wp[0][0]:
                p = (wp[0][1], wp[0][2])
            elif f >= wp[-1][0]:
                p = (wp[-1][1], wp[-1][2])
            else:
                while j + 1 < len(wp) and wp[j + 1][0] < f:
                    j += 1
                a, b = wp[j], wp[j + 1]
                t = (f - a[0]) / max(1, b[0] - a[0])
                p = (a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)
            s.pos[f] = Vector(p)
        s._settle()

    def _settle(s):
        """Стоячие анимации без скольжения: празднования ждут остановки, а удары и падения тормозят героя,
        после чего он догоняет свой путь."""
        def spd(f):
            return (s.pos[min(F_END + 3, f + 1)] - s.pos[max(0, f - 1)]).length * 15
        acts = sorted(s.acts, key=lambda x: x['f'])   # сначала удары и падения, потом ожидание остановки
        for ac in [x for x in acts if not x['clip'].startswith(WAIT_STILL)] + [x for x in acts if x['clip'].startswith(WAIT_STILL)]:
            if ac['clip'] in MOVING_CLIPS:
                continue
            r = next(((a, b) for a, b in s.vis if a <= ac['f'] <= b), None)
            if r is None:
                continue
            if ac['clip'].startswith(WAIT_STILL):
                f = ac['f']
                while f < min(r[1], ac['f'] + 90) and spd(f) > 0.3:
                    f += 1
                ac['f'] = f
                continue
            s0 = ac['f']; e = min(r[1], s0 + ac['dur'])
            if e <= s0 or max(spd(f) for f in range(s0, e)) < 0.5:
                continue
            path = list(s.pos)
            hold = path[s0] + (path[min(r[1], s0 + 6)] - path[s0]) * 0.5
            for f in range(s0, min(r[1] + 1, e + 12)):
                if f < s0 + 6:
                    t = (f - s0) / 6; p = path[s0].lerp(hold, 1 - (1 - t) ** 2)
                elif f < e:
                    p = hold
                else:
                    t = (f - e) / 12; p = hold.lerp(path[f], t * t * (3 - 2 * t))
                s.pos[f] = p.copy()

    def vel(s, f):
        """Скорость (м/с); соседние кадры берутся только из того же плана — склейки не дают рывков."""
        lo, hi = 0, F_END + 3
        for a, b in s.vis:
            if a <= f <= b:
                lo, hi = a, b
        f0, f1 = max(lo, f - 2), min(hi, f + 2)
        if f1 <= f0:
            return Vector((0.0, 0.0))
        return (s.pos[f1] - s.pos[f0]) * (30.0 / (f1 - f0))

    def visible(s, f):
        return any(a <= f <= b for a, b in s.vis)

    def ghosted(s, f):
        return any(a <= f <= b for a, b in s.ghosts)

    def _range(s, f):
        return next(((a, b) for a, b in s.vis if a <= f <= b), None)

    def active_act(s, f):
        """Действие в кадре f. Длинный клип (танец) не перетекает в следующий план того же героя."""
        cur = None
        r = s._range(f)
        for a in s.acts:
            if a['f'] <= f < a['f'] + a['dur'] and s._range(a['f']) == r:
                cur = a
        return cur

    def _angle_to(s, tgt, f):
        if isinstance(tgt, (int, float)):
            return math.radians(tgt)
        d = Vector((tgt[0], tgt[1])) - s.pos[f]
        return math.atan2(d.y, d.x) if d.length > 0.05 else None

    def forced(s, f):
        a = s.active_act(f)
        if a and a['face'] is not None:
            return s._angle_to(a['face'], f)
        for f0, f1, t in s.faces:
            if f0 <= f <= f1:
                return s._angle_to(t, f)
        return None

    def solve_facing(s, ball=None, owner=None):
        ang = None
        out = [0.0] * (F_END + 4)
        starts = {v[0] for v in s.vis}
        for f in range(1, F_END + 1):
            tgt = s.forced(f)
            if tgt is None:
                v = s.vel(f)
                if v.length > 0.7:
                    tgt = math.atan2(v.y, v.x)
                elif ball is not None and owner[f] is not s:
                    d = ball[f].xy - s.pos[f]
                    if d.length > 0.3:
                        tgt = math.atan2(d.y, d.x)
            if ang is None:
                ang = tgt if tgt is not None else 0.0
            elif tgt is not None:
                diff = wrap(tgt - ang)
                ang += diff if f in starts else max(-TURN, min(TURN, diff))
            out[f] = ang
        out[0] = out[1]
        for f in range(F_END + 1, F_END + 4):
            out[f] = out[F_END]
        s.ang = out

    def dir(s, f):
        return Vector((math.cos(s.ang[f]), math.sin(s.ang[f])))

    def feet(s, f, dz=0.0):
        p = s.pos[f] + s.dir(f) * 0.42
        return Vector((p.x, p.y, BR + dz))

    def hand(s, f):
        """Правая рука (откуда летит крюк)."""
        d = s.dir(f); r = Vector((d.y, -d.x))
        p = s.pos[f] + d * 0.18 + r * 0.22
        return Vector((p.x, p.y, 0.72))

    def segments(s, owner):
        """Клипы по кадрам: действие → его клип; бежит → бег (с мячом — ведение); стоит → стойка."""
        segs, prev, running = [], None, False
        for f in range(1, F_END + 1):
            sp = s.vel(f).length
            running = sp > (0.45 if running else 0.8)
            a = s.active_act(f)
            if a:
                key = ('A', id(a))
            elif running:
                key = ('run_ball',) if owner[f] is s else ('run',)
            else:
                key = ('idle',)
            if key != prev:
                if a:
                    segs.append((f, a['clip'], a['t0'] + (f - a['f']) / 30.0 * a['speed'], a['speed'], a['loop']))
                elif key[0] == 'idle':
                    segs.append((f, 'idle', (f * 0.37) % 2.0, 1.0, True))
                else:   # темп бега — по скорости героя (в игре 3,7 м/с)
                    v = s.vel(min(F_END, f + 4)).length
                    segs.append((f, key[0], 0.0, max(0.75, min(1.35, v / 3.7 * (1.1 if key[0] == 'run' else 1.15))), True))
                prev = key
        return segs

# ---------------------------------------------------------------- мяч: владение, пасы, удары
class BallPlan:
    def __init__(s):
        s.segs = []

    def own(s, f0, f1, a): s.segs.append(('own', f0, f1, a)); return s
    def fly(s, f0, f1, to, h=0.0): s.segs.append(('fly', f0, f1, to, h)); return s
    def curve(s, f0, f1, ctrl, to): s.segs.append(('curve', f0, f1, to, ctrl)); return s
    def roll(s, f0, f1, to): s.segs.append(('roll', f0, f1, to)); return s
    def net(s, f0, f1): s.segs.append(('net', f0, f1)); return s
    def put(s, f, p): s.segs.append(('put', f, f, p)); return s
    def hide(s, f0, f1): s.segs.append(('hide', f0, f1)); return s

    def owner_map(s):
        own = [None] * (F_END + 4)
        for sg in s.segs:
            if sg[0] == 'own':
                for f in range(sg[1], sg[2] + 1):
                    own[f] = sg[3]
        return own

    @staticmethod
    def resolve(to, f):
        if isinstance(to, Feet):
            return to.a.feet(f, to.dz)
        if isinstance(to, Actor):
            return to.feet(f)
        return Vector((to[0], to[1], to[2] if len(to) > 2 else BR))

    def solve(s):
        segs = sorted(s.segs, key=lambda x: x[1])
        pts = [None] * (F_END + 4)
        cur = Vector((0, 0, BR)); active = None; start = cur
        for f in range(1, F_END + 1):
            sg = None
            for x in segs:
                if x[1] <= f <= x[2]:
                    sg = x
            if sg is None:
                pts[f] = cur.copy(); continue
            if sg is not active:
                active = sg; start = cur.copy()
            k, f0, f1 = sg[0], sg[1], sg[2]
            t = (f - f0) / max(1, f1 - f0)
            if k == 'own':
                a = sg[3]; p = a.feet(f)
                if a.vel(f).length > 0.5:
                    p.z = BR + abs(math.sin(f * 0.55)) * 0.035
            elif k == 'fly':
                to = s.resolve(sg[3], f1)
                p = start.lerp(to, t) + Vector((0, 0, 4 * sg[4] * t * (1 - t)))
            elif k == 'curve':
                to = s.resolve(sg[3], f1)
                c = Vector((sg[4][0], sg[4][1], (start.z + to.z) / 2))
                u = 1 - (1 - t) ** 1.4
                p = start * (1 - u) ** 2 + c * 2 * u * (1 - u) + to * u * u
            elif k == 'roll':
                to = s.resolve(sg[3], f1)
                p = start.lerp(to, 1 - (1 - t) ** 2)
                p.z = BR
            elif k == 'net':
                p = Vector((start.x - 0.18 * t, start.y, start.z + (BR - start.z) * t * t + abs(math.sin(t * 7)) * 0.05 * (1 - t)))
            elif k == 'put':
                p = s.resolve(sg[3], f)
            else:
                p = Vector((0, 0, -30))
            pts[f] = p; cur = p
        pts[0] = pts[1]
        for f in range(F_END + 1, F_END + 4):
            pts[f] = pts[F_END]
        return pts

# ---------------------------------------------------------------- постановка
def stage(A, B):
    J, V, M, Tn = A['J'], A['V'], A['M'], A['Tn']
    rB, rS, rV, rM = A['rB'], A['rS'], A['rV'], A['rM']
    goal = lambda y, z=0.33: (NETX, y, z)

    # ================= 1. Холодное открытие: огненный суперудар (1–149)
    for a in (J, rB, rS): a.show(1, 149)
    J.at((1, 3.0, 0.9), (30, 6.6, 0.5), (149, 6.6, 0.5))
    J.kick(Q.OPEN_KICK, goal(-0.35), speed=0.7, lead=16)
    J.act(Q.GOAL_OPEN + 8, 'cel_victory', 0.2)
    J.look(Q.GOAL_OPEN + 8, 149, -95)
    rB.at((1, 0.4, -1.9), (46, 5.5, -0.8), (52, 5.7, -0.6), (64, 5.7, -0.6), (100, 4.6, 2.3), (149, 4.6, 2.3))
    rB.act(Q.GOAL_OPEN + 6, 'sad', 0.2)
    rS.at((1, 10.0, -2.0), (40, 10.0, -2.0), (56, 10.4, -1.3), (149, 10.4, -1.3))
    rS.act(Q.GOAL_OPEN + 4, 'defeated', 0.2)
    B.own(1, Q.OPEN_KICK - 1, J).fly(Q.OPEN_KICK, Q.GOAL_OPEN, goal(-0.35), 0.12).net(Q.GOAL_OPEN + 1, Q.GOAL_OPEN + 18)

    # ================= 2a. Розыгрыш 1: пасы, рывок, удар в штангу (150–369)
    P = Q.PLAY1
    for a in A.values(): a.show(P, 369)
    rV.at((P, 2.5, 1.0), (172, 0.2, 0.6), (180, 0.0, 0.5), (230, -1.5, 0.4), (300, 1.5, -0.5), (369, 4.5, -1.5))
    rV.kick(172, (-0.5, -2.8), 'pass', speed=1.0, lead=8)
    rM.at((P, 3.5, -3.0), (188, -0.4, -2.8), (212, -2.8, -2.6), (275, -2.8, -2.6), (369, 1.0, -2.0))
    rM.act(212, 'fall', 0.0, 1.0, dur=38).act(250, 'get_up', 0.0, 1.1)
    V.at((P, -3.0, 3.5), (205, -3.2, -0.9), (212, -3.0, -2.2), (226, -2.4, -1.7), (244, -0.6, -1.4), (300, 2.5, -2.5), (369, 6.0, -3.0))
    V.act(205, 'dash', 0.1, 1.2, dur=14, face=(-2.9, -2.6))
    V.kick(244, (1.5, 3.2), 'pass', speed=1.0, lead=8)
    M.at((P, -4.0, -3.0), (230, -1.5, 2.5), (262, 1.5, 3.2), (290, 4.5, 2.8), (369, 7.0, 3.6))
    M.kick(290, (6.4, -1.0), 'pass', speed=1.0, lead=8)
    J.at((P, -1.5, 0.5), (172, -0.6, 0.6), (230, 1.0, 0.0), (304, 6.4, -1.0), (330, 6.4, -1.0), (369, 7.2, 0.6))
    J.kick(320, (GX, 2.0), 'kick_strong', speed=0.9, lead=10)
    Tn.at((P, -8.5, 0.0), (260, -5.5, 0.5), (369, -1.0, 1.0))
    rB.at((P, 6.0, 3.0), (262, 3.0, 1.3), (300, 4.4, 1.2), (369, 8.6, 4.4))
    rS.at((P, 9.5, 0.0), (320, 10.6, -0.6), (369, 10.3, 0.2))
    B.put(P, (2.5 + 0.42 * -1, 1.0, BR))
    B.own(P, 171, rV).fly(172, 188, Feet(rM)).own(189, 211, rM).roll(212, 226, (-2.2, -1.55))
    B.own(227, 243, V).fly(244, 262, Feet(M)).own(263, 289, M).fly(290, 305, Feet(J)).own(306, 319, J)
    B.fly(320, Q.PLAY1_POST, (GX - 0.05, 1.86, 0.3), 0.08).fly(Q.PLAY1_POST + 1, 350, (9.5, 4.2, BR), 0.35).roll(351, 369, (8.7, 4.8))

    # ================= 2b. Розыгрыш 2: навес, грудь, гол (370–519), команда празднует (520–599)
    P = Q.PLAY2
    for a in A.values(): a.show(P, 599)
    Tn.at((P, 2.5, 5.5), (405, 6.0, 5.3), (415, 6.1, 5.2), (470, 6.4, 4.8), (528, 9.2, -4.0), (599, 9.2, -4.0))
    Tn.kick(408, (9.4, -1.3), 'kick', speed=1.0, lead=9)
    J.at((P, 3.5, 0.5), (444, 9.2, -1.2), (462, 9.2, -1.2), (472, 9.4, -1.6), (505, 10.05, -4.1), (599, 10.05, -4.1))
    J.kick(458, goal(-1.3), 'kick_strong', speed=1.0, lead=10)
    J.act(505, 'cel_victory', 0.1)
    J.look(505, 599, -90)
    V.at((P, 1.0, -3.5), (470, 6.5, -3.4), (525, 10.9, -4.0), (599, 10.9, -4.0))
    V.act(525, 'cel_cheering', 0.0, 1.0, dur=74, loop=True).look(525, 599, -95)
    M.at((P, 2.0, -1.0), (440, 4.0, -1.0), (530, 11.7, -3.8), (599, 11.7, -3.8))
    M.act(531, 'cel_hip_hop', 0.2).look(531, 599, -100)
    Tn.act(528, 'cel_taunt', 0.1).look(528, 599, -80)
    rB.at((P, 8.5, 4.6), (405, 6.8, 5.0), (430, 6.9, 4.4), (599, 6.0, 3.0))
    rS.at((P, 9.8, 0.6), (440, 10.5, 0.5), (599, 10.5, 0.5))
    rS.act(472, 'sad', 0.1)
    rV.at((P, 6.5, -1.5), (450, 8.6, -2.2), (599, 8.0, -2.4))
    rV.act(476, 'defeated', 0.0)
    rM.at((P, 2.0, 2.0), (450, 5.5, 1.6), (599, 6.0, 1.5))
    B.put(P, (2.9, 5.5, BR))
    B.own(P, 407, Tn).fly(408, 446, Feet(J, 0.55), 2.6).fly(447, 452, Feet(J), 0.02).own(453, 457, J)
    B.fly(458, Q.GOAL_PLAY2, goal(-1.3), 0.06).net(Q.GOAL_PLAY2 + 1, Q.GOAL_PLAY2 + 20)

    # ================= 3.1 Крюк Багора (600–719)
    f0 = Q.V_HOOK
    V.show(f0, 719); rB.show(f0, 719)
    V.at((f0, -4.4, 1.7), (640, -0.6, 1.4), (719, -0.6, 1.4))
    V.act(640, 'sad', 0.3)
    rB.at((f0, -1.0, -3.2), (650, -1.0, -3.2), (705, -6.0, -3.8), (719, -6.0, -3.8))
    rB.kick(632, (-1.2, 1.5), 'throw', speed=1.2, lead=8, after=16)
    B.put(f0, (-4.0, 1.7, BR)).own(f0, 631, V).fly(632, 648, Feet(rB), 0.3).own(649, 719, rB)

    # ================= 3.2 Кручёный удар Вихря (720–839)
    f0 = Q.V_CURVE
    V.show(f0, 839); rS.show(f0, 839)
    V.at((f0, 4.6, 4.2), (839, 4.6, 4.2))
    V.kick(748, (9.0, 2.6), 'kick', speed=0.9, lead=10)
    V.act(792, 'cel_cheering', 0.0, 1.0, dur=47, loop=True)
    rS.at((f0, 8.2, 1.6), (748, 8.2, 1.6), (762, 8.0, 2.4), (839, 8.0, 2.4))
    rS.act(790, 'sad', 0.1)
    B.put(f0, (4.4, 4.3, BR)).own(f0, 747, V).curve(748, Q.GOAL_CURVE, (10.8, 4.9), goal(-0.8, 0.3)).net(Q.GOAL_CURVE + 1, Q.GOAL_CURVE + 20)

    # ================= 3.3 Телепорт Мига (840–959)
    f0 = Q.V_BLINK
    M.show(f0, 959); rV.show(f0, 959)
    M.at((f0, -2.5, -2.0), (866, 0.6, -2.0), (867, 0.72, -2.0), (868, 3.4, -2.0), (894, 6.2, -1.7), (910, 7.9, -1.25), (959, 7.9, -1.25))
    M.kick(918, goal(-0.6), 'kick_strong', speed=1.0, lead=8)
    M.act(936, 'cel_hip_hop', 0.2)
    rV.at((f0, 2.2, -2.0), (866, 1.8, -2.0), (880, 1.8, -2.0), (930, 7.0, -2.0), (959, 7.0, -2.0))
    rV.look(868, 880, 0).act(932, 'sad', 0.2)
    B.hide(f0, 877).put(878, (0.6, 2.6, BR)).fly(879, 894, Feet(M)).own(895, 917, M)
    B.fly(918, Q.GOAL_BLINK, goal(-0.6), 0.05).net(Q.GOAL_BLINK + 1, Q.GOAL_BLINK + 18)

    # ================= 3.4 Ледяной разлом Стены (960–1079)
    f0 = Q.V_ICE
    J.show(f0, 1079); rS.show(f0, 1079)
    J.at((f0, 3.0, 0.75), (980, 5.6, 0.6), (1079, 5.6, 0.6))
    J.kick(992, goal(0.4), 'kick_strong', speed=0.9, lead=10)
    J.act(1012, 'sad', 0.1)
    rS.at((f0, 9.4, -3.0), (1079, 9.4, -3.0))
    rS.look(f0, 990, 90)
    rS.act(972, 'ground_punch', 0.35, 1.3, face=90)
    B.put(f0, (3.4, 0.75, BR)).own(f0, 991, J).fly(992, 999, (9.15, 0.5, 0.3), 0.05)
    B.fly(1000, 1018, (7.4, 2.3, BR), 0.45).roll(1019, 1040, (6.9, 2.7))

    # ================= 3.5 Невидимость Тени (1080–1199)
    f0 = Q.V_SHADOW
    Tn.show(f0, 1199); rM.show(f0, 1199)
    Tn.at((f0, -2.0, 3.0), (1104, 0.4, 3.0), (1128, 2.6, 3.9), (1150, 4.8, 3.2), (1158, 5.6, 2.9), (1199, 5.6, 2.9))
    Tn.act(1098, 'spell', 0.3, 1.5, dur=10)
    Tn.ghost(1106, 1159)
    Tn.kick(1164, goal(-0.5), 'kick_strong', speed=1.0, lead=6)
    rM.at((f0, 2.6, 2.9), (1199, 2.6, 2.9))
    rM.look(f0, 1110, 180).look(1111, 1130, -100).look(1131, 1150, 130).look(1151, 1199, 20)
    B.put(f0, (-1.6, 3.0, BR)).own(f0, 1163, Tn).fly(1164, Q.GOAL_SHADOW, goal(-0.5), 0.05).net(Q.GOAL_SHADOW + 1, Q.GOAL_SHADOW + 18)

    # ================= 3.6 Огненный суперудар издалека (1200–1319)
    f0 = Q.V_FIRE
    J.show(f0, 1319); rS.show(f0, 1319)
    J.at((f0, -1.5, 0.5), (1220, 1.0, 0.4), (1319, 1.0, 0.4))
    J.kick(1236, goal(0.15), 'kick_strong', speed=0.6, lead=16)
    J.act(1268, 'cel_victory', 0.15).look(1268, 1319, -90)
    rS.at((f0, 10.6, 1.9), (1319, 10.6, 1.9))
    rS.act(1266, 'sad', 0.1)
    B.put(f0, (-1.1, 0.5, BR)).own(f0, 1235, J).fly(1236, Q.GOAL_FIRE, goal(0.15), 0.18).net(Q.GOAL_FIRE + 1, Q.GOAL_FIRE + 18)

    # ================= 4. Розыгрыш 3 и золотой гол (1320–1599), MVP (1600–1721)
    P = Q.PLAY3
    for a in A.values(): a.show(P, 1735)
    rV.at((P, 1.0, -1.0), (1342, -0.8, -0.9), (1384, -0.8, -0.9), (1404, -0.8, -0.9), (1735, 0.5, -1.5))
    rV.act(1344, 'fall', 0.0, 1.0, dur=40).act(1384, 'get_up', 0.0, 1.1).act(1500, 'sad', 0.0)
    Tn.at((P, -1.5, -2.2), (1336, -1.6, -1.6), (1344, -1.0, -1.05), (1360, -0.6, -0.4), (1440, 4.0, 1.8), (1562, 6.6, 1.5), (1735, 6.6, 1.5))
    Tn.act(1337, 'dash', 0.1, 1.2, dur=12, face=(-0.8, -0.9))
    Tn.act(1562, 'cel_taunt', 0.1).look(1562, 1735, -70)
    M.at((P, -3.5, 1.5), (1356, -2.5, -0.15), (1369, -2.3, -0.2), (1460, 3.0, 2.2), (1578, 8.6, 1.5), (1735, 8.6, 1.5))
    M.kick(1370, (5.8, -5.2), 'kick', speed=1.0, lead=9)
    M.act(1578, 'cel_hip_hop', 0.2).look(1578, 1735, -110)
    V.at((P, 2.5, -4.8), (1400, 5.8, -5.2), (1418, 7.0, -4.6), (1424, 7.1, -4.5), (1532, 8.0, 2.2), (1735, 8.0, 2.2))
    V.kick(1416, (9.0, -1.0), 'kick', speed=1.1, lead=8)
    V.act(1534, 'cel_cheering', 0.0, 1.0, dur=200, loop=True).look(1534, 1735, -100)
    J.at((P, 3.0, 1.5), (1400, 5.5, 1.0), (1440, 7.6, 0.6), (1735, 7.6, 0.6))
    J.kick(Q.GOLDEN_KICK, goal(-0.5), 'kick_strong', speed=0.35, lead=28, after=12)
    J.act(1500, 'cel_victory', 0.1).look(1500, 1599, -95)
    J.act(1600, 'idle_show', 0.0, 1.0, dur=46, loop=True).act(1646, 'cel_victory', 0.25)
    J.look(1600, 1735, -90)
    rB.at((P, 4.0, -3.5), (1412, 6.0, -3.9), (1414, 6.1, -3.9), (1421, 7.7, -4.3), (1735, 7.6, -4.3))
    rB.act(1413, 'dash', 0.1, 1.2, dur=12, face=(7.6, -4.4)).act(1430, 'sad', 0.2)
    rS.at((P, 8.5, -0.5), (1440, 10.4, -0.8), (1466, 10.4, -0.8), (1478, 8.7, -0.6), (1505, 8.7, -0.6), (1545, 10.4, -2.4), (1735, 10.4, -2.4))
    rS.act(1466, 'dash', 0.1, 0.9, dur=14, face=(7.6, 0.6)).act(1546, 'defeated', 0.0)
    rM.at((P, -2.5, 2.8), (1450, 2.0, 2.5), (1735, 3.0, 2.2))
    rM.act(1505, 'sad', 0.2)
    B.put(P, (0.6, -1.0, BR)).own(P, 1343, rV).roll(1344, 1358, (-2.2, -0.2)).own(1359, 1369, M)
    B.fly(1370, 1400, Feet(V), 2.6).own(1401, 1415, V).curve(1416, 1446, (10.8, -3.4), Feet(J)).own(1447, Q.GOLDEN_KICK - 1, J)
    B.fly(Q.GOLDEN_KICK, Q.GOAL_GOLDEN, goal(-0.5), 0.08).net(Q.GOAL_GOLDEN + 1, Q.GOAL_GOLDEN + 22)
    # ================= 5. Титр: мяч в центре
    B.put(Q.TITLE - 2, (0, 0, BR)).put(F_END, (0, 0, BR))

# ---------------------------------------------------------------- материалы и меши для эффектов
def emit_mat(name, color, strength, alpha=None):
    return T.mat(name, color, 0.4, emit=color, emit_str=strength, alpha=alpha)

def keys(sock, pairs, interp=None):
    for f, v in pairs:
        sock.default_value = v; sock.keyframe_insert('default_value', frame=f)

def no_shadow(o):
    try: o.visible_shadow = False
    except Exception: pass

def sphere(name, m, r=1.0, sub=2, coll=None):
    me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    for fc in bm.faces: fc.smooth = True
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    o = T.link(bpy.data.objects.new(name, me), coll); no_shadow(o); return o

def pop_scale(o, pairs):
    for f, k in pairs:
        o.scale = (max(1e-3, k),) * 3; o.keyframe_insert('scale', frame=f)

def burst(name, pos, f, color, size=1.0, life=10, n=1, alpha=0.85, glow=8.0, rise=0.0, spread=0.9):
    """Вспышка-шар (телепорт, искры удара): раздувается и гаснет."""
    m = emit_mat(name, color, glow, alpha=0.9)
    b = T.bsdf_of(m.node_tree.nodes)
    keys(b.inputs['Alpha'], [(1, 0.0), (f - 1, 0.0), (f, alpha), (f + life, 0.0)])
    keys(b.inputs['Emission Strength'], [(f, glow), (f + life, 0.0)])
    for i in range(n):
        o = sphere(name, m, 1.0, 3 if n == 1 else 1)
        off = Vector(((random.random() - 0.5) * spread, (random.random() - 0.5) * spread, random.random() * spread * 0.6)) if n > 1 else Vector()
        o.location = Vector(pos); o.keyframe_insert('location', frame=f)
        o.location = Vector(pos) + off * 1.6 + Vector((0, 0, rise)); o.keyframe_insert('location', frame=f + life)
        s = size * (0.6 + 0.6 * random.random()) if n > 1 else size
        pop_scale(o, [(1, 0), (f - 1, 0), (f, s * 0.25), (f + life, s), (f + life + 1, 0)])

def smoke(name, pos, f, color, life=18, n=7):
    """Объёмный дым (Тень растворяется и появляется)."""
    m = bpy.data.materials.new(name); m.use_nodes = True
    N = m.node_tree.nodes; L = m.node_tree.links
    for x in list(N):
        if x.type != 'OUTPUT_MATERIAL': N.remove(x)
    out = next(x for x in N if x.type == 'OUTPUT_MATERIAL')
    vol = N.new('ShaderNodeVolumePrincipled')
    vol.inputs['Color'].default_value = (*color, 1)
    vol.inputs['Emission Color'].default_value = (*color, 1); vol.inputs['Emission Strength'].default_value = 0.6
    tc = N.new('ShaderNodeTexCoord'); nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 2.2; nz.inputs['Detail'].default_value = 6
    L.new(tc.outputs['Object'], nz.inputs['Vector'])
    grad = N.new('ShaderNodeTexGradient'); grad.gradient_type = 'SPHERICAL'; L.new(tc.outputs['Object'], grad.inputs['Vector'])
    mul = N.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; L.new(nz.outputs['Fac'], mul.inputs[0]); L.new(grad.outputs['Fac'], mul.inputs[1])
    k = N.new('ShaderNodeMath'); k.operation = 'MULTIPLY'; L.new(mul.outputs[0], k.inputs[0])
    L.new(k.outputs[0], vol.inputs['Density']); L.new(vol.outputs[0], out.inputs['Volume'])
    keys(k.inputs[1], [(1, 0.0), (f - 1, 0.0), (f + 2, 7.0), (f + life // 2, 4.0), (f + life, 0.0)])
    for i in range(n):
        me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0); bm.to_mesh(me); bm.free()
        me.materials.append(m)
        o = T.link(bpy.data.objects.new(name, me)); no_shadow(o)
        p0 = Vector(pos) + Vector(((random.random() - 0.5) * 0.5, (random.random() - 0.5) * 0.5, 0.15 + random.random() * 0.45))
        s0 = 0.15 + random.random() * 0.1
        o.scale = (0.001,) * 3; o.keyframe_insert('scale', frame=1)
        for fr, loc, sc_ in ((f - 1, p0, 0.001), (f, p0, s0), (f + life, p0 + Vector((0, 0, 0.6 + random.random() * 0.6)), s0 * 1.9), (f + life + 1, p0, 0.001)):
            o.location = loc; o.scale = (sc_,) * 3
            o.keyframe_insert('location', frame=fr); o.keyframe_insert('scale', frame=fr)

def spark_trail(ball, f0, f1, color, name, count=700, size=0.035):
    """След из искр за мячом (кручёный удар)."""
    m1 = emit_mat(name + 'A', color, 16); m2 = emit_mat(name + 'B', (1, 1, 1), 20)
    c = bpy.data.collections.new(name + 'Bits'); bpy.context.scene.collection.children.link(c)
    for i, mm in enumerate((m1, m1, m2)):
        me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0); bm.to_mesh(me); bm.free()
        me.materials.append(mm); c.objects.link(bpy.data.objects.new(name + str(i), me))
    T.hide_collection(c)
    ball.modifiers.new(name, 'PARTICLE_SYSTEM')
    ps = ball.particle_systems[-1].settings
    ps.count = count; ps.frame_start = f0; ps.frame_end = f1; ps.lifetime = 14; ps.lifetime_random = 0.5
    ps.emit_from = 'VOLUME'; ps.normal_factor = 0.0; ps.factor_random = 0.15
    ps.render_type = 'COLLECTION'; ps.instance_collection = c; ps.use_collection_pick_random = True
    ps.particle_size = size; ps.size_random = 0.6; ps.effector_weights.gravity = 0.0

def bar_mesh(name, m, radius=0.03):
    """Цилиндр длиной 1 от начала координат вдоль Z (верёвка крюка): масштаб Z = длина."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=radius, radius2=radius, depth=1.0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 0.5))
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    o = T.link(bpy.data.objects.new(name, me)); no_shadow(o); return o

def cone_mesh(name, m, r=1.0, h=1.0, seg=6):
    """Конус от основания (z=0) вверх — шип льда."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=0.0, depth=h)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, h / 2))
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    return T.link(bpy.data.objects.new(name, me))

def ice_mat(name):
    m = T.mat(name, T.srgb((0.5, 0.85, 1.0)), 0.08, emit=T.srgb((0.3, 0.75, 1.0)), emit_str=0.6, alpha=0.8)
    try: T.bsdf_of(m.node_tree.nodes).inputs['Transmission Weight'].default_value = 0.6
    except Exception: pass
    return m

# ---------------------------------------------------------------- мяч (классический, как в игре)
def build_ball(coll):
    """12 чёрных пятиугольников и 20 белых шестиугольников со швами-канавками (как ball_trailer.py).
    Возвращает мяч и входы свечения: GlowBase (весь мяч), GlowColor, Arcane (только швы)."""
    t = (1 + 5 ** 0.5) / 2
    penta = [Vector(v).normalized() for v in ((-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t),
                                              (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1))]
    edge = (penta[0] - penta[1]).length * 1.01
    centers = list(penta)
    for i in range(12):
        for j in range(i + 1, 12):
            for k in range(j + 1, 12):
                if (penta[i] - penta[j]).length < edge and (penta[j] - penta[k]).length < edge and (penta[i] - penta[k]).length < edge:
                    centers.append((penta[i] + penta[j] + penta[k]).normalized())
    me = bpy.data.meshes.new('Ball'); bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=5, radius=1.0)
    info = {}
    for v in bm.verts:
        d = v.co.normalized()
        dots = sorted(((d.dot(c), i) for i, c in enumerate(centers)), reverse=True)
        g = dots[0][0] - dots[1][0]
        seam = max(0.0, 1 - g / 0.022)
        puff = min(1.0, g / 0.12)
        v.co = d * BR * (1 - 0.022 * seam ** 1.5 + 0.006 * puff)
        info[v.index] = (dots[0][1] < 12, seam)
    for f in bm.faces: f.smooth = True
    col = bm.loops.layers.float_color.new('Panel'); seam_l = bm.loops.layers.float_color.new('Seam')
    dark, white, line = (0.012, 0.012, 0.014, 1), (0.9, 0.9, 0.88, 1), (0.05, 0.05, 0.055, 1)
    for f in bm.faces:
        for l in f.loops:
            is_p, sm = info[l.vert.index]
            base = dark if is_p else white
            k = sm * (0.3 if is_p else 0.9)
            l[col] = tuple(base[i] * (1 - k) + line[i] * k for i in range(4))
            l[seam_l] = (sm, sm, sm, 1)
    bm.to_mesh(me); bm.free()
    ball = T.link(bpy.data.objects.new('Ball', me), coll)
    m = bpy.data.materials.new('Ball'); m.use_nodes = True
    N = m.node_tree.nodes; L = m.node_tree.links; b = T.bsdf_of(N)
    at = N.new('ShaderNodeVertexColor'); at.layer_name = 'Panel'
    L.new(at.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.32
    for k, v in (('Coat Weight', 0.35), ('Coat Roughness', 0.15)):
        try: b.inputs[k].default_value = v
        except Exception: pass
    sa = N.new('ShaderNodeVertexColor'); sa.layer_name = 'Seam'
    gb = N.new('ShaderNodeValue'); gb.name = gb.label = 'GlowBase'; gb.outputs[0].default_value = 0.0
    gc = N.new('ShaderNodeRGB'); gc.name = gc.label = 'GlowColor'; gc.outputs[0].default_value = (1.0, 0.35, 0.05, 1)
    ar = N.new('ShaderNodeValue'); ar.name = ar.label = 'Arcane'; ar.outputs[0].default_value = 0.0
    sxa = N.new('ShaderNodeMath'); sxa.operation = 'MULTIPLY'; L.new(sa.outputs['Color'], sxa.inputs[0]); L.new(ar.outputs[0], sxa.inputs[1])
    stren = N.new('ShaderNodeMath'); stren.operation = 'MULTIPLY_ADD'; L.new(sxa.outputs[0], stren.inputs[0])
    stren.inputs[1].default_value = 45.0; L.new(gb.outputs[0], stren.inputs[2])
    L.new(stren.outputs[0], b.inputs['Emission Strength'])
    fac = N.new('ShaderNodeMath'); fac.operation = 'MULTIPLY'; fac.use_clamp = True; L.new(sxa.outputs[0], fac.inputs[0]); fac.inputs[1].default_value = 6.0
    mx = N.new('ShaderNodeMix'); mx.data_type = 'RGBA'; L.new(fac.outputs[0], mx.inputs['Factor'])
    L.new(gc.outputs[0], mx.inputs['A']); mx.inputs['B'].default_value = (*T.srgb((0.45, 0.85, 1.0)), 1)
    L.new(mx.outputs['Result'], b.inputs['Emission Color'])
    me.materials.append(m)
    return ball, (gb.outputs[0], gc.outputs[0], ar.outputs[0])

def key_ball(ball, pts):
    ball.rotation_mode = 'QUATERNION'
    q = Quaternion()
    jumps = []
    for f in range(1, F_END + 1):
        d = pts[f] - pts[f - 1]
        if (d.length > 1.5):
            jumps.append(f - 1)
        d.z = 0
        L = d.length
        if 1e-5 < L < 1.5:
            q = Quaternion(Vector((-d.y, d.x, 0)).normalized(), L / BR) @ q
        ball.location = pts[f]; ball.keyframe_insert('location', frame=f)
        ball.rotation_quaternion = q; ball.keyframe_insert('rotation_quaternion', frame=f)
    T.set_interp(ball, 'location', 'LINEAR'); T.set_interp(ball, 'rotation_quaternion', 'LINEAR')
    for f in jumps:
        if f >= 1:
            T.key_interp(ball, 'location', f, 'CONSTANT')

# ---------------------------------------------------------------- герои в сцене
class Capsule:
    """Заменитель героя для проверки без моделей (ARENA_STUB=1)."""
    def __init__(self, a, coll):
        m = T.mat('Stub_' + a.key, a.team, 0.5)
        me = bpy.data.meshes.new(a.key); bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.28, radius2=0.2, depth=1.35)
        bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 0.675))
        nose = bmesh.ops.create_cube(bm, size=0.18)
        bmesh.ops.translate(bm, verts=nose['verts'], vec=(0, -0.3, 1.0))
        bm.to_mesh(me); bm.free(); me.materials.append(m)
        self.arm = T.link(bpy.data.objects.new('Hero_' + a.key, me), coll)
        self.glow = None; self.scale = 1.0

def key_actor(a):
    arm = a.hero.arm
    arm.rotation_mode = 'XYZ'
    seen = []
    for f in range(1, F_END + 1):
        v = a.visible(f) and not a.ghosted(f)
        p = a.pos[f]
        arm.location = (p.x, p.y, 0.0 if v else -40.0)
        arm.rotation_euler = (0, 0, a.ang[f] + math.pi / 2)
        arm.keyframe_insert('location', frame=f); arm.keyframe_insert('rotation_euler', frame=f)
        seen.append(v)
    T.set_interp(arm, 'location', 'LINEAR'); T.set_interp(arm, 'rotation_euler', 'LINEAR')
    for i in range(len(seen) - 1):
        if seen[i] != seen[i + 1]:
            T.key_interp(arm, 'location', i + 1, 'CONSTANT'); T.key_interp(arm, 'rotation_euler', i + 1, 'CONSTANT')

def bake_actor(a, owner):
    if STUB:
        return
    rig = T.Rig(a.hero.arm, CLIPS['idle'])
    segs = a.segments(owner)
    for f0, f1 in sorted(a.vis):
        rig.bake(CLIPS, segs, max(1, f0 - 1), min(F_END, f1 + 1), xfade=5)
    a.hero.arm.scale = (a.hero.scale,) * 3

# ---------------------------------------------------------------- голы: стробоскоп, сетка, конфетти, трибуны
EXPO = {}
GOAL_LOG = []     # (кадр, цвет, большой?)

def strip_color(f, i):
    return BLUE if ((f // 30) + i) % 2 == 0 else RED

def goal_lights(spots, strips, f, col, big):
    span = 66 if big else 24
    for i, (L, lc) in enumerate(spots):
        L.color = lc; L.energy = 26000; L.keyframe_insert('color', frame=f - 1); L.keyframe_insert('energy', frame=f - 1)
        for k in range(0, span, 3):
            on = (k // 3 + i) % 2 == 0
            L.color = col if on else lc; L.energy = (70000 if big else 45000) if on else 16000
            L.keyframe_insert('color', frame=f + k); L.keyframe_insert('energy', frame=f + k)
        L.color = lc; L.energy = 26000; L.keyframe_insert('color', frame=f + span + 3); L.keyframe_insert('energy', frame=f + span + 3)
    for i, sm in enumerate(strips):
        n = T.bsdf_of(sm.node_tree.nodes).inputs['Emission Color']
        n.default_value = (*strip_color(f - 1, i), 1); n.keyframe_insert('default_value', frame=f - 1)
        for k in range(0, span + 6, 4):
            n.default_value = (*(col if (k // 4) % 2 == 0 else (1, 1, 1)), 1); n.keyframe_insert('default_value', frame=f + k)
        n.default_value = (*strip_color(f + span + 8, i), 1); n.keyframe_insert('default_value', frame=f + span + 8)

def net_hit(back, f, y, z, strength=1.0):
    if not back.data.shape_keys:
        T.net_bulge(back, 1, y, z, f)
        return
    sk = back.data.shape_keys.key_blocks['Bulge']
    for fr, v in ((f - 1, 0), (f + 3, 1.0 * strength), (f + 9, -0.25 * strength), (f + 15, 0.45 * strength),
                  (f + 22, -0.1 * strength), (f + 30, 0.15 * strength), (f + 40, 0)):
        sk.value = v; sk.keyframe_insert('value', frame=fr)

def confetti(f, cols, name):
    cc = bpy.data.collections.new(name + 'Bits'); bpy.context.scene.collection.children.link(cc)
    for i, c in enumerate(cols):
        m = T.mat(name + str(i), c, 0.4, emit=c, emit_str=1.2)
        me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5); bm.to_mesh(me); bm.free()
        me.materials.append(m); o = bpy.data.objects.new(name + str(i), me); o.scale = (1, 0.6, 1); cc.objects.link(o)
    T.hide_collection(cc)
    for y in (-T.GOAL_W / 2, T.GOAL_W / 2):
        em = T.plane(name + 'Gun', (0.25, 0.25), (GX + 0.05, y, T.GOAL_H + 0.1), T.mat(name + 'Hidden', (0, 0, 0), 1), rot=(0, math.radians(-35), 0))
        em.modifiers.new('conf', 'PARTICLE_SYSTEM')
        ps = em.particle_systems[-1].settings
        ps.count = 700; ps.frame_start = f; ps.frame_end = f + 8; ps.lifetime = 120; ps.lifetime_random = 0.3
        ps.emit_from = 'FACE'; ps.normal_factor = 7.0; ps.factor_random = 2.5
        ps.render_type = 'COLLECTION'; ps.instance_collection = cc; ps.use_collection_pick_random = True
        ps.particle_size = 0.09; ps.size_random = 0.5
        ps.effector_weights.gravity = 0.3; ps.damping = 0.05
        ps.use_rotations = True; ps.rotation_mode = 'NONE'; ps.rotation_factor_random = 1.0
        ps.angular_velocity_mode = 'RAND'; ps.angular_velocity_factor = 8.0
        em.show_instancer_for_render = False

def animate_crowd(rows):
    for o, home_share in rows:
        ph = random.random() * 6.28
        base = o.location.z
        for f in range(1, F_END + 1, 4):
            jump = 0.0
            for g, col, big in GOAL_LOG:
                if g <= f <= g + 100:
                    k = 1 - (f - g) / 100
                    amp = (0.06 + 0.16 * home_share) * (1.0 if big else 0.5)
                    jump = max(jump, abs(math.sin(f * 0.55 + ph)) * amp * k)
            o.location.z = base + math.sin(f * 0.25 + ph) * 0.012 + jump
            o.keyframe_insert('location', index=2, frame=f)

def goal(f, back, spots, strips, y, z, col=None, big=True, conf_cols=None, name='Goal'):
    col = col or BLUE
    GOAL_LOG.append((f, col, big))
    net_hit(back, f, y, z, 1.0 if big else 0.8)
    goal_lights(spots, strips, f, col, big)
    if big:
        confetti(f, conf_cols or [BLUE, BLUE, (1, 1, 1), T.ACCENT_L, T.srgb((0.55, 0.8, 1.0))], name + 'Conf%d' % f)
    EXPO[f] = BASE_EXP + (1.3 if big else 0.8)
    EXPO[f + 4] = BASE_EXP

# ---------------------------------------------------------------- способности
def hook_fx(rB, pts, f_throw, f_grab, f_back):
    gold = T.mat('HookRope', T.hexc('#c9a46a'), 0.6, 0.2)
    metal = T.mat('HookHead', T.hexc('#d8dde3'), 0.25, 0.95, emit=T.hexc('#e2c48a'), emit_str=0.8)
    rope = bar_mesh('HookRope', gold, 0.018)
    head = bar_mesh('HookHead', metal, 0.06)
    rope.rotation_mode = head.rotation_mode = 'QUATERNION'
    prev = None
    for f in range(f_throw - 1, f_back + 2):
        start = rB.hand(f)
        if f < f_throw or f > f_back:
            tip = start; L = 0.0
        elif f <= f_grab:
            tip = start.lerp(pts[f_grab], (f - f_throw) / max(1, f_grab - f_throw))
        else:
            tip = pts[f]
        d = tip - start; L = d.length
        q = (d.normalized() if L > 1e-4 else Vector((0, 0, 1))).to_track_quat('Z', 'Y')
        if prev is not None and prev.dot(q) < 0: q = -q
        prev = q
        rope.location = start; rope.rotation_quaternion = q; rope.scale = (1, 1, max(1e-3, L))
        head.location = start + d * max(0.0, 1 - 0.08 / max(L, 0.08)); head.rotation_quaternion = q
        hs = 1.0 if L > 0.05 else 1e-3
        head.scale = (hs, hs, 0.14 * hs)
        for o in (rope, head):
            o.keyframe_insert('location', frame=f); o.keyframe_insert('rotation_quaternion', frame=f); o.keyframe_insert('scale', frame=f)
    for o in (rope, head):
        o.scale = (1e-3,) * 3; o.keyframe_insert('scale', frame=1)
        T.set_interp(o, 'scale', 'CONSTANT')
    burst('HookGrab', pts[f_grab], f_grab, T.hexc('#ffe2a0'), size=0.22, life=6, glow=10)

def blink_fx(p_out, p_in, f):
    cyan = T.srgb((0.55, 0.85, 1.0)); violet = T.srgb((0.7, 0.45, 1.0))
    burst('BlinkOut', (p_out.x, p_out.y, 0.65), f, violet, size=0.42, life=6, alpha=0.3)
    burst('BlinkIn', (p_in.x, p_in.y, 0.65), f, cyan, size=0.5, life=7, alpha=0.28)
    burst('BlinkSpark', (p_in.x, p_in.y, 0.6), f, (1, 1, 1), size=0.05, life=10, n=9)
    # метка на старте: можно вернуться 1 с
    m = emit_mat('BlinkMark', violet, 6.0, alpha=0.9)
    b = T.bsdf_of(m.node_tree.nodes)
    keys(b.inputs['Alpha'], [(1, 0.0), (f - 1, 0.0), (f, 0.9), (f + 24, 0.7), (f + 30, 0.0)])
    T.ring('BlinkMark', 0.28, 0.36, m, loc=(p_out.x, p_out.y, 0.012))

def ice_wall(base, dir_deg, length, f0, f1, f_end, shatter=None, name='Ice'):
    """Ледяная гряда из-под ног вперёд: растёт за f0..f1, стоит до f_end (или разбивается в кадр shatter)."""
    m = ice_mat(name)
    a = math.radians(dir_deg); d = Vector((math.cos(a), math.sin(a))); nrm = Vector((-d.y, d.x))
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0.5, 0, 0.5))
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.06, segments=2, affect='EDGES')
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    w = T.link(bpy.data.objects.new(name, me))
    w.location = (base[0], base[1], 0); w.rotation_euler = (0, 0, a)
    gone = shatter or f_end
    for f, sx, sz in ((1, 1e-3, 1e-3), (f0 - 1, 1e-3, 1e-3), (f0, 0.4, 0.45), (f1, length, 0.62),
                      (gone - 1, length, 0.62), (gone, 1e-3, 1e-3)):
        w.scale = (sx, 0.32, sz); w.keyframe_insert('scale', frame=f)
    T.set_interp(w, 'scale', 'LINEAR'); T.key_interp(w, 'scale', gone - 1, 'CONSTANT')
    spikes = []
    n = int(length / 0.42)
    for i in range(n):
        u = (i + 0.5) / n
        p = Vector(base) + d * (u * length) + nrm * ((random.random() - 0.5) * 0.25)
        h = 0.55 + random.random() * 0.55
        s = cone_mesh(name + 'Spike', m, 0.16 + random.random() * 0.08, h)
        s.location = (p.x, p.y, 0.25)
        s.rotation_euler = ((random.random() - 0.5) * 0.6, (random.random() - 0.5) * 0.6, random.random() * 6.28)
        fa = int(f0 + (f1 - f0) * u)
        pop_scale(s, [(1, 0), (fa - 1, 0), (fa, 0.3), (fa + 2, 1.2), (fa + 4, 1.0), (gone - 1, 1.0), (gone, 0)])
        T.key_interp(s, 'scale', gone - 1, 'CONSTANT')
        spikes.append(s)
    for k in range(4):
        u = (k + 0.5) / 4
        p = Vector(base) + d * (u * length)
        burst(name + 'Frost', (p.x, p.y, 0.25), int(f0 + (f1 - f0) * u) + 1, T.srgb((0.8, 0.95, 1.0)), size=0.18, life=10, n=5, alpha=0.6, glow=3.0)
    if shatter:
        chunks(name, base, d, length, shatter, m)
    return w

def chunks(name, base, d, length, f, m):
    """Огонь разбивает лёд: осколки разлетаются по дуге и тают."""
    for i in range(18):
        u = random.random()
        p0 = Vector((*(Vector(base) + d * (u * length)), 0.3 + random.random() * 0.4))
        v = Vector(((1.2 + random.random() * 2.5), (random.random() - 0.5) * 3.0, 2.0 + random.random() * 2.5))
        me = bpy.data.meshes.new(name + 'Chunk'); bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
        bm.to_mesh(me); bm.free(); me.materials.append(m)
        o = T.link(bpy.data.objects.new(name + 'Chunk', me)); no_shadow(o)
        s = 0.08 + random.random() * 0.12
        o.scale = (1e-3,) * 3; o.keyframe_insert('scale', frame=1); o.keyframe_insert('scale', frame=f - 1)
        rot0 = Vector((random.random(), random.random(), random.random())) * 6
        for k in range(0, 31, 2):
            t = k / 30.0
            pos = p0 + v * t + Vector((0, 0, -4.9 * t * t))
            pos.z = max(0.05, pos.z)
            o.location = pos; o.keyframe_insert('location', frame=f + k)
            o.rotation_euler = rot0 * (1 + t * 3); o.keyframe_insert('rotation_euler', frame=f + k)
            ss = s if k < 22 else s * (30 - k) / 8
            o.scale = (ss, ss * 0.7, ss * 1.2); o.keyframe_insert('scale', frame=f + k)
    burst(name + 'Shatter', (base[0], base[1] + d.y * length / 2, 0.35), f, T.srgb((0.85, 0.95, 1.0)), size=0.5, life=6, alpha=0.3, glow=6)
    burst(name + 'FireHit', (base[0], base[1] + d.y * length / 2, 0.35), f, T.srgb((1.0, 0.5, 0.1)), size=0.45, life=6, alpha=0.4, glow=14)

def curve_dots(pts, f_show, f_kick, color):
    """Траектория кручёного удара точками (как в игре, пока заряжаешь)."""
    m = emit_mat('CurveDot', color, 9.0)
    n = 16
    for i in range(1, n + 1):
        p = pts[f_kick + int(i * (Q.GOAL_CURVE - f_kick) / (n + 1))]
        o = sphere('CurveDot', m, 1.0, 1)
        o.location = (p.x, p.y, 0.05)
        fa = f_show + int(i * (f_kick - f_show) / n * 0.6)
        pop_scale(o, [(1, 0), (fa - 1, 0), (fa, 0.04), (f_kick + 1, 0.04), (f_kick + 2, 0)])

def landing_mark(p, f0, f1, color):
    """Метка приземления навеса на газоне."""
    m = emit_mat('LandMark', color, 5.0, alpha=0.85)
    b = T.bsdf_of(m.node_tree.nodes)
    keys(b.inputs['Alpha'], [(1, 0.0), (f0 - 1, 0.0), (f0 + 3, 0.85), (f1, 0.85), (f1 + 4, 0.0)])
    o = T.ring('LandMark', 0.34, 0.44, m, loc=(p.x, p.y, 0.012))
    pop_scale(o, [(f0, 0.3), (f0 + 5, 1.0)] + [(f, 1.0 + 0.12 * (k % 2)) for k, f in enumerate(range(f0 + 8, f1, 8))])

def dash_fx(a, f_dash, f_hit):
    p = a.pos[f_dash]
    burst('DashDust', (p.x, p.y, 0.15), f_dash, T.srgb((0.85, 0.9, 1.0)), size=0.14, life=8, n=5, alpha=0.25, glow=3.0, spread=0.5)
    if f_hit:
        q = a.feet(f_hit)
        burst('HitSpark', (q.x, q.y, 0.7), f_hit, T.srgb((1.0, 0.85, 0.4)), size=0.045, life=8, n=10, glow=14, spread=0.9)
        burst('HitFlash', (q.x, q.y, 0.7), f_hit, (1, 1, 1), size=0.35, life=4, alpha=0.35, glow=10)

# ---------------------------------------------------------------- камеры
CAMS = []

def cam(name, lens, fstop, kk, ease=True):
    c, t = T.camera('Cam_' + name, lens, fstop)
    c.data.sensor_fit = 'HORIZONTAL'; c.data.sensor_width = 36
    T.cam_keys(c, t, kk)
    if ease:
        T.ease_all(c); T.ease_all(t)
    CAMS.append(c)
    return c

def follow_cam(name, f0, f1, track, dist=17.0, tilt=52.0, lens=30.0, smooth=0.07, clamp_x=6.5, clamp_y=2.2, lead=None):
    """Игровая камера: сверху под углом, поперёк поля, плавно следует за мячом (как в игре)."""
    tg = track[f0].copy(); tg.z = 0
    kk = []
    ta = math.radians(tilt)
    for f in range(f0, f1 + 1):
        p = track[f].copy(); p.z = 0
        tg = tg.lerp(p, smooth)
        tx = max(-clamp_x, min(clamp_x, tg.x)); ty = max(-clamp_y, min(clamp_y, tg.y))
        if (f - f0) % 2 == 0 or f == f1:
            kk.append((f, (tx, ty - dist * math.cos(ta), dist * math.sin(ta)), (tx, ty, 0.3), lens))
    return cam(name, lens, 16.0, kk, ease=False)

def cuts(pairs):
    for f, c in pairs:
        T.cut(f, c)

def orbit_keys(center, r, h0, h1, a0, a1, f0, f1, look_z=0.85, step=6):
    kk = []
    for f in range(f0, f1 + 1, step):
        u = (f - f0) / max(1, f1 - f0)
        a = math.radians(a0 + (a1 - a0) * u)
        kk.append((f, (center[0] + math.cos(a) * r, center[1] + math.sin(a) * r, h0 + (h1 - h0) * u), (center[0], center[1], look_z), None))
    return kk

def build_cameras(A, pts):
    J = A['J']
    track = pts
    c = {}
    # 1. открытие
    jp = J.pos
    c['open1'] = cam('open1', 34, 2.4, [(1, (jp[1].x - 1.4, jp[1].y - 2.6, 0.55), (jp[1].x + 0.9, jp[1].y, 0.55), None),
                                        (30, (jp[30].x - 1.7, jp[30].y - 2.4, 0.5), (jp[30].x + 0.7, jp[30].y, 0.6), None),
                                        (45, (jp[45].x - 1.3, jp[45].y - 1.9, 0.45), (jp[45].x + 0.6, jp[45].y, 0.6), 30)])
    c['open2'] = cam('open2', 22, 3.2, [(46, (12.22, 0.95, 0.36), (8.2, -0.2, 0.4), None), (77, (12.22, 0.9, 0.38), (8.8, -0.3, 0.38), None)])
    c['open3'] = cam('open3', 30, 3.5, orbit_keys((6.6, 0.5), 3.2, 0.75, 1.3, -70, -120, 78, 149))
    # 2. игровая камера
    c['play1'] = follow_cam('play1', Q.PLAY1, 369, track)
    c['play2'] = follow_cam('play2', Q.PLAY2, 519, track, dist=16.0)
    c['team'] = cam('team', 26, 11.0, [(Q.TEAM_CEL, (10.5, -7.1, 0.75), (10.45, -4.0, 0.72), None), (599, (10.45, -6.9, 0.8), (10.45, -4.0, 0.74), None)])
    # 3. герои
    c['hook'] = cam('hook', 30, 11.0, [(Q.V_HOOK, (-7.6, -0.6, 1.6), (-0.9, -0.8, 0.45), None), (719, (-8.2, -1.4, 1.7), (-2.0, -1.6, 0.45), None)])
    c['curve'] = cam('curve', 24, 11.0, [(Q.V_CURVE, (1.6, 7.0, 3.3), (8.0, 2.0, 0.2), None), (839, (2.4, 6.8, 3.0), (9.4, 1.0, 0.2), None)])
    c['blink'] = cam('blink', 30, 11.0, [(Q.V_BLINK, (-1.0, -6.6, 1.15), (-1.0, -2.0, 0.55), None), (868, (1.4, -6.5, 1.15), (1.6, -2.0, 0.55), None),
                                        (915, (6.6, -6.4, 1.2), (8.0, -1.2, 0.5), None), (959, (8.4, -6.0, 1.3), (11.2, -0.6, 0.45), None)])
    c['ice'] = cam('ice', 26, 11.0, [(Q.V_ICE, (2.6, -3.9, 2.4), (8.6, 0.4, 0.45), None), (1079, (3.2, -3.6, 2.2), (8.8, 0.6, 0.45), None)])
    c['shadow'] = cam('shadow', 28, 11.0, [(Q.V_SHADOW, (-0.6, -2.4, 0.95), (0.0, 3.0, 0.5), None), (1160, (3.4, -2.1, 1.0), (5.8, 2.8, 0.45), None),
                                          (1186, (6.4, -2.8, 1.3), (11.4, -0.3, 0.4), None), (1199, (6.6, -2.8, 1.3), (11.6, -0.4, 0.4), None)])
    c['fire1'] = cam('fire1', 30, 11.0, [(Q.V_FIRE, (-3.6, -1.4, 0.9), (4.0, 0.4, 0.5), None), (1236, (-1.6, -1.1, 0.75), (6.0, 0.3, 0.45), None)])
    c['fire2'] = cam('fire2', 26, 11.0, [(1237, (2.6, -3.7, 0.75), (3.0, 0.3, 0.4), None), (Q.GOAL_FIRE, (9.6, -3.6, 0.8), (11.6, 0.2, 0.35), None),
                                         (1271, (9.9, -3.5, 0.85), (11.8, 0.2, 0.35), None)])
    c['fire3'] = cam('fire3', 30, 11.0, orbit_keys((1.0, 0.4), 3.0, 0.8, 1.2, -60, -115, 1272, 1319))
    # 4. нарастание и золотой гол — монтаж на разные камеры
    c['g1'] = follow_cam('g1', Q.PLAY3, 1343, track, dist=15.0)
    c['g2'] = cam('g2', 30, 4.0, [(1344, (-0.6, -4.9, 0.75), (-0.9, -0.9, 0.5), None), (1369, (-1.2, -4.7, 0.8), (-1.9, -0.4, 0.45), None)])
    c['g3'] = follow_cam('g3', 1370, 1400, track, dist=18.0, smooth=0.12)
    c['g4'] = cam('g4', 28, 4.0, [(1401, (3.6, -7.0, 1.4), (8.2, -2.4, 0.4), None), (1445, (4.2, -6.9, 1.4), (8.6, -1.2, 0.4), None)])
    c['g5'] = cam('g5', 32, 3.5, [(1446, (6.5, -2.7, 0.6), (7.6, 0.6, 0.6), None), (1473, (6.8, -2.3, 0.5), (7.6, 0.6, 0.55), None)])
    c['g6'] = cam('g6', 22, 11.0, [(1474, (12.22, 0.95, 0.36), (8.2, -0.1, 0.42), None), (1519, (12.22, 0.9, 0.4), (8.6, -0.2, 0.45), None)])
    c['g7'] = cam('g7', 26, 4.5, orbit_keys((7.9, 0.4), 4.4, 1.4, 1.9, -50, -130, 1520, 1599, look_z=0.8))
    c['mvp'] = cam('mvp', 40, 11.0, [(Q.MVP, (7.6, -3.0, 0.9), (7.6, 0.6, 0.8), None), (Q.BLACK, (7.6, -2.3, 0.85), (7.6, 0.6, 0.82), None)])
    # 5. титр (как в ball_trailer.py: вертикальная подгонка — под неё сделан макет значка и надписи)
    ct, tt = T.camera('Cam_title', 40, 1.6)
    T.cam_keys(ct, tt, [(Q.TITLE, (-2.4, -0.3, 0.16), (0, 0, 0.45), None), (F_END, (-2.0, -0.25, 0.17), (0, 0, 0.47), None)])
    T.ease_all(ct); T.ease_all(tt)
    td = ct.data.dof; td.focus_object = None; td.focus_distance = 3.2; td.aperture_fstop = 11.0
    c['title'] = ct
    cuts([(1, c['open1']), (46, c['open2']), (78, c['open3']), (Q.PLAY1, c['play1']), (Q.PLAY2, c['play2']), (Q.TEAM_CEL, c['team']),
          (Q.V_HOOK, c['hook']), (Q.V_CURVE, c['curve']), (Q.V_BLINK, c['blink']), (Q.V_ICE, c['ice']), (Q.V_SHADOW, c['shadow']),
          (Q.V_FIRE, c['fire1']), (1237, c['fire2']), (1272, c['fire3']), (Q.PLAY3, c['g1']), (1344, c['g2']), (1370, c['g3']), (1401, c['g4']),
          (1446, c['g5']), (1474, c['g6']), (1520, c['g7']), (Q.MVP, c['mvp']), (Q.TITLE, c['title'])])
    bpy.context.scene.camera = c['open1']
    return c

# ---------------------------------------------------------------- надписи в кадре
def screen_text(cam, body, f0, f1, color, size=0.055, where='bottom', d=1.2, glow=4.0, pop=False, bar=True):
    """Надпись, прикреплённая к камере: появляется и исчезает (альфа), чуть выезжает."""
    lens = cam.data.lens
    hw = 18.0 / lens * d; hh = hw * 9 / 16
    m = T.mat('Txt', color, 0.4, emit=color, emit_str=glow, alpha=1.0)
    b = T.bsdf_of(m.node_tree.nodes)
    keys(b.inputs['Alpha'], [(1, 0.0), (f0 - 1, 0.0), (f0 + 6, 1.0), (f1 - 6, 1.0), (f1, 0.0)])
    if where == 'bottom':
        pos = Vector((-0.84 * hw, -0.66 * hh, -d)); align = 'LEFT'
    else:
        pos = Vector((0.0, 0.5 * hh, -d)); align = 'CENTER'
    t = T.text('Txt', body, size, m, tuple(pos), (0, 0, 0), align=align, extrude=0.004 if pop else 0.0)
    t.parent = cam; no_shadow(t)
    if pop:
        pop_scale(t, [(1, 0), (f0 - 1, 0), (f0, 0.2), (f0 + 5, 1.25), (f0 + 9, 1.0)])
    else:
        for fr, dx in ((f0, -0.06 * hw), (f0 + 10, 0.0)):
            t.location = pos + Vector((dx, 0, 0)); t.keyframe_insert('location', frame=fr)
    if bar and where == 'bottom':
        bm_ = T.mat('TxtBar', color, 0.4, emit=color, emit_str=glow * 1.5, alpha=1.0)
        keys(T.bsdf_of(bm_.node_tree.nodes).inputs['Alpha'], [(1, 0.0), (f0 - 1, 0.0), (f0 + 6, 1.0), (f1 - 6, 1.0), (f1, 0.0)])
        br = T.box('TxtBar', (0, 0, 0), (1, 1, 1), bm_, shadow=False)
        br.parent = cam
        bw = len(body) * size * 0.62
        for fr, k in ((f0, 0.05), (f0 + 10, 1.0)):
            br.location = pos + Vector((bw * k / 2, -size * 0.75, 0)); br.scale = (bw * k, size * 0.08, 0.002)
            br.keyframe_insert('location', frame=fr); br.keyframe_insert('scale', frame=fr)
    return t

def captions(c):
    W = (1, 1, 1)
    screen_text(c['play1'], tr('arcade'), 165, 255, W)
    screen_text(c['team'], tr('friends'), 530, 596, W)
    for k, cm, f0, col in (('hook', 'hook', Q.V_HOOK, T.hexc('#ffd28a')), ('curve', 'curve', Q.V_CURVE, T.srgb((0.45, 0.95, 1.0))),
                           ('blink', 'blink', Q.V_BLINK, T.srgb((0.75, 0.6, 1.0))), ('ice', 'ice', Q.V_ICE, T.srgb((0.6, 0.9, 1.0))),
                           ('shadow', 'shadow', Q.V_SHADOW, T.srgb((0.72, 0.5, 1.0))), ('fire', 'fire1', Q.V_FIRE, T.srgb((1.0, 0.55, 0.15)))):
        screen_text(c[cm], tr(k), f0 + 4, f0 + (36 if k == 'fire' else 70), col, size=0.07)
    screen_text(c['g6'], tr('golden'), Q.GOAL_GOLDEN + 2, 1519, GOLD, size=0.15, where='center', glow=6.0, pop=True)
    screen_text(c['mvp'], tr('mvp'), Q.MVP + 12, Q.BLACK, GOLD, size=0.13, where='center', glow=5.0, pop=True)

# ---------------------------------------------------------------- свет: MVP, титр
def special_lights(spots, A):
    J = A['J']
    for L, lc in spots:   # MVP: стадион притихает, на героя — луч; титр — в четверть силы
        for f, e in ((Q.MVP - 1, 26000.0), (Q.MVP + 8, 9000.0), (Q.BLACK, 9000.0), (Q.TITLE, 6000.0)):
            L.energy = e; L.color = lc; L.keyframe_insert('energy', frame=f); L.keyframe_insert('color', frame=f)
    k = bpy.data.lights.new('MVPKey', 'SPOT'); k.color = T.srgb((1.0, 0.9, 0.7))
    k.spot_size = math.radians(22); k.spot_blend = 0.6; k.shadow_soft_size = 0.4
    p = J.pos[Q.MVP]
    ko = T.link(bpy.data.objects.new('MVPKey', k)); ko.location = (p.x - 0.6, p.y - 1.8, 6.0)
    ko.rotation_euler = (Vector((p.x, p.y, 0.6)) - ko.location).to_track_quat('-Z', 'Y').to_euler()
    for f, e in ((1, 0), (Q.MVP - 1, 0), (Q.MVP + 8, 900), (Q.BLACK, 900), (Q.TITLE, 0)):
        k.energy = e; k.keyframe_insert('energy', frame=f)
    kl = bpy.data.lights.new('BallKey', 'SPOT'); kl.color = T.hexc('#a9bcff')
    kl.spot_size = math.radians(16); kl.spot_blend = 0.85; kl.shadow_soft_size = 0.3
    bo = T.link(bpy.data.objects.new('BallKey', kl)); bo.location = (-0.8, 0.5, 7.0)
    bo.rotation_euler = (Vector((0, 0, BR)) - bo.location).to_track_quat('-Z', 'Y').to_euler()
    for f, e in ((1, 0), (Q.TITLE - 1, 0), (Q.TITLE, 300)):
        kl.energy = e; kl.keyframe_insert('energy', frame=f)

def exposure(sc):
    vs = sc.view_settings; B = BASE_EXP
    pts = {1: -7.0, 8: B}
    for f in (Q.PLAY1, Q.V_HOOK, Q.V_CURVE, Q.V_BLINK, Q.V_ICE, Q.V_SHADOW, Q.V_FIRE, Q.PLAY3, 1344, 1370, 1401, 1446):
        pts[f - 1] = B; pts[f] = B + 1.1; pts[f + 3] = B
    pts.update(EXPO)
    pts.update({Q.BLACK: B, Q.BLACK + 14: -14.0, Q.TITLE - 1: -14.0, Q.TITLE: -6.0, Q.TITLE + 24: B - 0.3, F_END - 12: B - 0.3, F_END: -10.0})
    for f in sorted(pts):
        vs.exposure = pts[f]; vs.keyframe_insert('exposure', frame=f)
    hard = (Q.TITLE - 1,)
    for fc in T.fcurves_of(sc):
        if fc.data_path == 'view_settings.exposure':
            for kp in fc.keyframe_points:
                kp.interpolation = 'CONSTANT' if int(round(kp.co[0])) in hard else 'LINEAR'

def title(cam):
    """Титр как в трейлере «Мяч в центре»: слева значок (лёд и огонь), справа SpellStrike + Arcane League."""
    tag = TAGLINE or ('COMING SOON' if LANG == 'en' else 'СКОРО')
    try:
        import logo
        logo.spellstrike(cam, Q.TITLE_POP, ROOT, tagline=tag, size=0.56, y0=1.42, subtitle='ARCANE LEAGUE', x0=0.78, bold=3.0, glow=1.5, turn=-0.22)
    except Exception as e:
        print('[gameplay] логотип:', e)
    try:
        import ball_trailer as BT
        BT.Q.F_END = F_END
        BT.emblem_title(cam, Q.TITLE_POP - 6, (-1.32, 0.78, -4.3), 0.5)
    except Exception as e:
        print('[gameplay] значок:', e)

def add_audio(sc):
    """Звук из gameplay_audio.py (Builds/Trailer/gameplay_audio.wav), если он уже собран."""
    path = os.path.join(OUT, 'gameplay_audio.wav')
    if not os.path.exists(path):
        print('[gameplay] звука нет (', path, ') — запусти gameplay_audio.py'); return
    sc.sequence_editor_create()
    se = sc.sequence_editor
    coll = getattr(se, 'strips', None)
    if coll is None: coll = se.sequences
    coll.new_sound('gameplay_audio', path, 1, 1)
    ff = sc.render.ffmpeg
    try:
        ff.audio_codec = 'AAC'; ff.audio_bitrate = 256; ff.audio_channels = 'STEREO'; ff.audio_mixrate = 48000
    except Exception as e:
        print('[gameplay] звук в видео:', e)
    print('[gameplay] звук добавлен:', path)

# ================================================================ сборка
def main():
    T.clean()
    sc = bpy.context.scene
    T.PREVIEW = getattr(T, 'PREVIEW', False) or PREVIEW
    T.setup_render(sc)
    T.setup_compositor(sc)
    sc.frame_start, sc.frame_end = 1, F_END
    sc.render.fps, sc.render.fps_base = Q.FPS, 1.0
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.filepath = os.path.join(OUT, 'spellstrike_gameplay_%s_' % TAG)
    st = bpy.data.collections.new('Stadium'); sc.collection.children.link(st)
    hc = bpy.data.collections.new('Heroes'); sc.collection.children.link(hc)

    if not STUB:
        print('[gameplay] анимации...')
        for n in CLIP_NAMES:
            CLIPS[n] = T.Clip(os.path.join(T.HEROES, 'Anim', n + '.fbx'), n)

    print('[gameplay] постановка...')
    A = {'J': Actor('J', 'Jar', BLUE), 'V': Actor('V', 'Vihr', BLUE), 'M': Actor('M', 'Mig', BLUE), 'Tn': Actor('Tn', 'Ten', BLUE),
         'rB': Actor('rB', 'Bagor', RED), 'rS': Actor('rS', 'Stena', RED), 'rV': Actor('rV', 'Vihr', RED), 'rM': Actor('rM', 'Mig', RED)}
    plan = BallPlan()
    stage(A, plan)
    owner = plan.owner_map()
    for a in A.values():
        a.build()
    pts = None
    for it in range(3):
        for a in A.values():
            a.solve_facing(pts, owner)
        pts = plan.solve()

    print('[gameplay] герои...')
    for a in A.values():
        a.hero = Capsule(a, hc) if STUB else T.Hero(a.folder, a.team, hc)
        bake_actor(a, owner)
        key_actor(a)
    J = A['J']
    if J.hero.glow is not None:   # Жар разгорается на замахе огненного удара
        gk = [(1, 3.0), (25, 3.0), (Q.OPEN_KICK, 12.0), (Q.GOAL_OPEN + 20, 5.0), (149, 4.0), (1222, 4.0), (1236, 14.0), (1270, 6.0),
              (1446, 4.0), (Q.GOLDEN_KICK, 14.0), (1530, 6.0), (Q.MVP + 20, 8.0)]
        keys(J.hero.glow, gk)

    print('[gameplay] стадион...')
    T.build_pitch(st)
    back = T.build_goal(1, RED, st)
    T.build_goal(-1, BLUE, st)
    strips = T.build_boards(st)
    rows = T.build_stands(st)
    spots = T.build_lights(st, strips)

    print('[gameplay] мяч и эффекты...')
    ball, (gbase, gcol, arcane) = build_ball(st)
    key_ball(ball, pts)
    fire = (1.0, 0.35, 0.05, 1); cyan = (*T.srgb((0.35, 0.9, 1.0)), 1)
    keys(gcol, [(1, fire), (700, fire), (720, cyan), (800, cyan), (820, fire)])
    keys(gbase, [(1, 0.0), (Q.OPEN_KICK - 1, 0.0), (Q.OPEN_KICK, 9.0), (Q.GOAL_OPEN + 25, 2.0), (148, 0.0),
                 (747, 0.0), (748, 4.0), (Q.GOAL_CURVE + 20, 0.0),
                 (1235, 0.0), (1236, 10.0), (Q.GOAL_FIRE + 25, 2.0), (1310, 0.0),
                 (Q.GOLDEN_KICK - 1, 0.0), (Q.GOLDEN_KICK, 12.0), (Q.GOAL_GOLDEN + 30, 3.0), (1600, 0.0)])
    # полный заряд — швы мяча светятся магией («можно бить»)
    keys(arcane, [(1, 0.0), (28, 0.0), (40, 0.25), (Q.OPEN_KICK, 0.3), (Q.OPEN_KICK + 3, 0.0),
                  (1222, 0.0), (1232, 0.25), (1236, 0.3), (1239, 0.0), (1450, 0.0), (1466, 0.3), (Q.GOLDEN_KICK, 0.35), (Q.GOLDEN_KICK + 3, 0.0),
                  (Q.TITLE + 30, 0.0), (Q.TITLE + 60, 0.12), (F_END, 0.12)])
    T.fire_trail(ball, Q.OPEN_KICK, Q.GOAL_OPEN + 2, st)
    T.fire_trail(ball, 1236, Q.GOAL_FIRE + 2, st)
    T.fire_trail(ball, Q.GOLDEN_KICK, Q.GOAL_GOLDEN + 2, st)
    spark_trail(ball, 748, Q.GOAL_CURVE, T.srgb((0.35, 0.95, 1.0)), 'Spin')
    spark_trail(ball, 1416, 1446, T.srgb((0.35, 0.95, 1.0)), 'Spin2', count=400)

    goal(Q.GOAL_OPEN, back, spots, strips, -0.35, 0.33)
    goal(Q.GOAL_PLAY2, back, spots, strips, 0.6, 0.33)
    goal(Q.GOAL_CURVE, back, spots, strips, -0.8, 0.3, big=False)
    goal(Q.GOAL_BLINK, back, spots, strips, -0.6, 0.33, big=False)
    goal(Q.GOAL_SHADOW, back, spots, strips, -0.5, 0.33, big=False)
    goal(Q.GOAL_FIRE, back, spots, strips, 0.15, 0.33)
    goal(Q.GOAL_GOLDEN, back, spots, strips, -0.5, 0.33, col=GOLD, conf_cols=[GOLD, GOLD, (1, 1, 1), BLUE, T.srgb((1.0, 0.9, 0.5))], name='Golden')
    animate_crowd(rows)
    burst('PostHit', pts[Q.PLAY1_POST], Q.PLAY1_POST, (1, 1, 1), size=0.05, life=6, n=6, glow=10, spread=0.5)

    # способности
    hook_fx(A['rB'], pts, 624, 632, 648)
    curve_dots(pts, 726, 748, T.srgb((0.45, 0.95, 1.0)))
    M = A['M']
    blink_fx(M.pos[867], M.pos[868], 868)
    ice_wall((9.4, -2.6), 90, 6.0, 980, 988, 1078, name='IceA')
    Tn = A['Tn']
    violet = T.srgb((0.38, 0.18, 0.75))
    smoke('ShadeOut', (Tn.pos[1106].x, Tn.pos[1106].y, 0.0), 1106, violet)
    smoke('ShadeIn', (Tn.pos[1159].x, Tn.pos[1159].y, 0.0), 1159, violet, life=14, n=5)
    landing_mark(pts[446], 400, 446, BLUE)
    landing_mark(pts[1400], 1362, 1400, BLUE)
    dash_fx(A['V'], 205, 212)
    dash_fx(A['Tn'], 1337, 1344)
    dash_fx(A['rB'], 1413, None)
    dash_fx(A['rS'], 1466, None)
    special_lights(spots, A)

    print('[gameplay] камеры...')
    c = build_cameras(A, pts)
    captions(c)
    exposure(sc)
    title(c['title'])
    add_audio(sc)
    sc.render.fps, sc.render.fps_base = Q.FPS, 1.0   # ещё раз в конце: импорт моделей (FBX) сбрасывает сцену на 24 кадра/с

    if STILLS:
        return
    sc.frame_set(1)
    p = os.path.join(OUT, 'gameplay_%s.blend' % TAG)
    try:
        bpy.ops.wm.save_as_mainfile(filepath=p); print('[gameplay] сохранено:', p)
    except Exception as e:
        print('[gameplay] не сохранилось:', e)
    print('[gameplay] готово. Рендер: Ctrl+F12 ->', sc.render.filepath)

if __name__ == '__main__' or os.environ.get('ARENA_GAMEPLAY_RUN') == '1':
    main()
