# ======================== SpellStrike — трейлер «Мяч в центре», 26 с, 16:9 (YouTube) ========================
# Чёрный экран, тишина → прожекторы включаются по одному → в центре поля мяч, камера медленно наезжает под стук сердца.
# «Мяч вспоминает»: на ударах сердца — короткие вспышки-детали (сетка надувается, мяч в огне, трибуны вскакивают,
# табло 0:1, штанга в инее, швы мяча светятся магией) → мяч в центре слабо вспыхивает швами → свет гаснет →
# логотип SpellStrike · ARCANE LEAGUE · COMING SOON.
# Таймлайн — в ball_cues.py, звук — ball_audio.py (запустить первым: python ball_audio.py → Builds/Trailer/ball_audio.wav).
# Запуск: Blender 5.2 → File → New → General → Scripting → Open → Tools/Blender/ball_trailer.py → Run Script.
# Сохраняет Builds/Trailer/ball_trailer_h_en.blend; рендер Ctrl+F12 → Builds/Trailer/spellstrike_ball_trailer_h_en_*.mp4.
# Стадион, свет, мяч и огонь — из trailer.py, логотип — logo.py. Героев в ролике нет.
import bpy, bmesh, math, os, sys, random
from mathutils import Vector

ROOT = os.environ.get('ARENA_ROOT') or r'C:\Users\user\ArenaStrikers'
sys.path.insert(0, os.path.join(ROOT, 'Tools', 'Blender'))
for m_ in ('trailer', 'ball_cues', 'logo', 'emblem'):
    sys.modules.pop(m_, None)   # повторный запуск в том же Blender — свежие версии
import trailer as T
import ball_cues as Q

# ===== НАСТРОЙКИ ВЕРСИИ (поменяй и запусти скрипт заново) =====
FORMAT = os.environ.get('ARENA_FORMAT') or 'horizontal'   # 'horizontal' — 1920×1080 (YouTube); 'vertical' — 1080×1920 (Shorts)
LANG = os.environ.get('ARENA_LANG') or 'en'               # 'en' — «COMING SOON»; 'ru' — «СКОРО»
# =================================================================
TAG = '%s_%s' % ('h' if FORMAT == 'horizontal' else 'v', LANG)
FL_ = Q.FLASH
T.F_END = Q.F_END
T.G = FL_['score']['start'] + 3  # табло 0:0 → 0:1 и «ГОЛ!» на бортах — во вспышке «табло»
SUBTITLE = 'ARCANE LEAGUE'
if LANG == 'en':
    T.ADS = ['SPELLSTRIKE', 'ARCANE LEAGUE', 'COMING SOON', 'PLAY WITH FRIENDS']
else:
    T.ADS = ['SPELLSTRIKE', 'ARCANE LEAGUE', 'СКОРО', 'ИГРАЙ С ДРУЗЬЯМИ']
OUT = T.OUT
BR = T.BALL_R
GX = T.FL / 2                   # линия ворот (+X), где сетка и штанга из вспышек
NETX = GX + T.NET_D - 0.08      # где мяч упирается в заднюю сетку
AUDIO = os.path.join(OUT, 'ball_audio.wav')
BASE_EXPOSURE = -0.4
STILLS = os.environ.get('ARENA_STILLS')   # проверка Claude: сцена без сохранения
random.seed(5)

# ---------------------------------------------------------------- помощники
def keys(sock, pairs, interp=None):
    """Ключи на входе узла (default_value)."""
    for f, v in pairs:
        sock.default_value = v; sock.keyframe_insert('default_value', frame=f)

def const_keys(idblock):
    for fc in T.fcurves_of(idblock):
        for kp in fc.keyframe_points: kp.interpolation = 'CONSTANT'

def emit_mat(name, color, strength, alpha=None):
    return T.mat(name, color, 0.4, emit=color, emit_str=strength, alpha=alpha)

def burst(name, pos, f, color, size=1.0, life=10, n=1, alpha=0.85, glow=8.0, spread=0.9):
    """Вспышка-шар: раздувается и гаснет (телепорт, ледяная пыль)."""
    m = emit_mat(name, color, glow, alpha=0.9)
    b = T.bsdf_of(m.node_tree.nodes)
    keys(b.inputs['Alpha'], [(1, 0.0), (f - 1, 0.0), (f, alpha), (f + life, 0.0)])
    keys(b.inputs['Emission Strength'], [(f, glow), (f + life, 0.0)])
    for i in range(n):
        me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
        for fc in bm.faces: fc.smooth = True
        bm.to_mesh(me); bm.free(); me.materials.append(m)
        o = T.link(bpy.data.objects.new(name, me))
        off = Vector(((random.random() - 0.5) * spread, (random.random() - 0.5) * spread, random.random() * spread * 0.6)) if n > 1 else Vector()
        o.location = Vector(pos) + off
        s = size * (0.6 + 0.6 * random.random()) if n > 1 else size
        for fr, k in ((1, 0.001), (f - 1, 0.001), (f, s * 0.25), (f + life, s), (f + life + 1, 0.001)):
            o.scale = (k, k, k); o.keyframe_insert('scale', frame=fr)
        try: o.visible_shadow = False
        except Exception: pass

def spark_trail(ball, f0, f1, color, name, count=700, size=0.035):
    """След из искр за мячом своего цвета."""
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

def bezier(p0, p1, p2, t):
    return p0 * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + p2 * t * t

def ease(a):
    a = min(max(a, 0.0), 1.0); return a * a * (3 - 2 * a)

# ================================================================ сборка
def main():
    T.clean()
    sc = bpy.context.scene
    T.setup_render(sc)
    T.setup_compositor(sc)
    sc.frame_end = Q.F_END
    if FORMAT == 'horizontal':
        sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.filepath = os.path.join(OUT, 'spellstrike_ball_trailer_%s_' % TAG)
    st = bpy.data.collections.new('Stadium'); sc.collection.children.link(st)

    print('[ball] стадион...')
    T.build_pitch(st)
    back_home = T.build_goal(1, T.HOME_L, st)
    T.build_goal(-1, T.AWAY_L, st)
    strips = T.build_boards(st)
    boards_after_goal()
    rows = T.build_stands(st)
    crowd_jump(rows)
    spots = T.build_lights(st, strips)
    relight(spots, strips)

    print('[ball] мяч и вспышки...')
    ball, glow = build_ball(st)
    animate_ball(ball)
    ball_glow(glow)
    effects(ball, st)
    net_bulge(back_home)

    print('[ball] камеры...')
    cams = build_cameras()
    exposure(sc)
    title(cams['title'])
    add_audio(sc)

    if STILLS:
        return
    sc.frame_set(1)
    p = os.path.join(OUT, 'ball_trailer_%s.blend' % TAG)
    try:
        bpy.ops.wm.save_as_mainfile(filepath=p); print('[ball] сохранено:', p)
    except Exception as e:
        print('[ball] не сохранилось:', e)
    print('[ball] готово. Рендер: Ctrl+F12 ->', sc.render.filepath)

# ---------------------------------------------------------------- борта, табло, трибуны
def boards_after_goal():
    """«GOAL!» на бортах и вспышка табло — только во вспышке «табло», потом снова реклама."""
    end = FL_['score']['end']
    for o in bpy.data.objects:
        if o.name.startswith('AdGoal'):
            if LANG == 'en': o.data.body = 'GOAL!'
            T.vis_keys(o, [(end, False)])
        elif o.name.startswith('AdText'):
            T.vis_keys(o, [(end, True)])

def crowd_jump(rows):
    """Трибуны слегка качаются; вскакивают только во вспышке «трибуны» (и чуть-чуть на табло)."""
    wins = [(FL_['crowd']['start'] - 6, FL_['crowd']['end'], 1.0), (FL_['score']['start'], FL_['score']['end'], 0.5)]
    for o, home_share in rows:
        ph = random.random() * 6.28
        base = o.location.z
        frames = sorted(set(list(range(1, Q.F_END + 1, 3)) + [f for a, b, _ in wins for f in range(a - 1, b + 2)]))
        for f in frames:
            jump = 0.0
            for a, b, k in wins:
                if a <= f <= b:
                    jump = abs(math.sin(f * 0.9 + ph)) * (0.08 + 0.2 * (1 - home_share)) * k
            o.location.z = base + math.sin(f * 0.25 + ph) * 0.012 + jump
            o.keyframe_insert('location', index=2, frame=f)

# ---------------------------------------------------------------- свет: темно → по одному включаются → по одному гаснут
def relight(spots, strips):
    on_all, off_all = Q.LIGHTS_ON[-1], Q.LIGHTS_OFF[-1]
    cr = FL_['crowd']
    for i, (L, lc) in enumerate(spots):
        L.animation_data_clear()                       # стробоскоп trailer.py не нужен — свой ниже
        f_on, f_off = Q.LIGHTS_ON[i], Q.LIGHTS_OFF[i]
        seq = [(1, 0.0, lc), (f_on, 26000.0, lc)]
        for k in range(0, cr['end'] - cr['start'], 2):  # стробоскоп в цвет синих — во вспышке «трибуны»
            on = (k // 2 + i) % 2 == 0
            seq.append((cr['start'] + k, 80000.0 if on else 12000.0, T.HOME_L if on else lc))
        seq += [(cr['end'], 26000.0, lc), (f_off, 0.0, lc)]
        for f, e, c in seq:
            L.energy = e; L.color = c
            L.keyframe_insert('energy', frame=f); L.keyframe_insert('color', frame=f)
        const_keys(L)
    panels = [m for m in bpy.data.materials if m.name.startswith('FloodPanel')]
    for i, m in enumerate(panels):
        s = T.bsdf_of(m.node_tree.nodes).inputs['Emission Strength']; v = s.default_value
        f_on, f_off = Q.LIGHTS_ON[i % 4], Q.LIGHTS_OFF[i % 4]
        keys(s, [(1, 0.0), (f_on - 1, 0.0), (f_on, v * 1.6), (f_on + 8, v), (f_off - 1, v), (f_off, 0.0)])
    # неон, лампы, реклама, линии поля, табло — загораются с последней мачтой и гаснут с последней
    names = ('StandNeon', 'Lamp', 'AdText', 'StandEdge', 'Lines', 'AdGoal', 'Post_', 'AdStrip', 'Score', 'Net_')
    for m in bpy.data.materials:
        if not m.name.startswith(names): continue
        s = T.bsdf_of(m.node_tree.nodes).inputs['Emission Strength']; v = s.default_value
        keys(s, [(1, 0.0), (on_all - 1, 0.0), (on_all, v), (off_all - 1, v), (off_all, 0.0)])
    crowd = bpy.data.materials.get('Crowd')
    if crowd:
        keys(T.bsdf_of(crowd.node_tree.nodes).inputs['Emission Strength'],
             [(1, 0.02), (on_all - 1, 0.02), (on_all, 0.35), (off_all - 1, 0.35), (off_all, 0.02)])
    for o in bpy.data.objects:
        if o.type == 'LIGHT' and o.data.name.startswith('Fill'):
            e = o.data.energy
            for f, v in ((1, 0.0), (on_all - 1, 0.0), (on_all, e), (off_all - 1, e), (off_all, 0.0)):
                o.data.energy = v; o.data.keyframe_insert('energy', frame=f)
        if o.type == 'LIGHT' and o.data.name.startswith('Moon'):
            o.data.energy = 0.3
    # под титром: узкий холодный луч сверху на мяч в темноте
    kl = bpy.data.lights.new('BallKey', 'SPOT'); kl.energy = 0; kl.color = T.hexc('#a9bcff')
    kl.spot_size = math.radians(16); kl.spot_blend = 0.85; kl.shadow_soft_size = 0.3
    ko = T.link(bpy.data.objects.new('BallKey', kl)); ko.location = (-0.8, 0.5, 7.0)
    ko.rotation_euler = (Vector((0, 0, BR)) - ko.location).to_track_quat('-Z', 'Y').to_euler()
    for f, e in ((1, 0), (Q.TITLE - 1, 0), (Q.TITLE, 2600)):
        kl.energy = e; kl.keyframe_insert('energy', frame=f)
    const_keys(kl)
    # под титром: контровой свет сзади — у мяча светлый ободок, он отделяется от темноты
    rl = bpy.data.lights.new('BallRim', 'SPOT'); rl.energy = 0; rl.color = T.hexc('#ffd7a8')
    rl.spot_size = math.radians(22); rl.spot_blend = 0.8; rl.shadow_soft_size = 0.2
    ro = T.link(bpy.data.objects.new('BallRim', rl)); ro.location = (2.2, 0.9, 1.4)
    ro.rotation_euler = (Vector((0, 0, BR)) - ro.location).to_track_quat('-Z', 'Y').to_euler()
    for f, e in ((1, 0), (Q.TITLE - 1, 0), (Q.TITLE, 700)):
        rl.energy = e; rl.keyframe_insert('energy', frame=f)
    const_keys(rl)
    # и мягкий тёплый свет спереди-снизу — мяч читается целиком
    fl = bpy.data.lights.new('BallFill', 'POINT'); fl.energy = 0; fl.color = T.hexc('#ffe2c4'); fl.shadow_soft_size = 0.4
    fo = T.link(bpy.data.objects.new('BallFill', fl)); fo.location = (-2.1, -1.1, 0.35)   # сбоку от камеры, вне кадра
    for f, e in ((1, 0), (Q.TITLE - 1, 0), (Q.TITLE, 50)):
        fl.energy = e; fl.keyframe_insert('energy', frame=f)
    const_keys(fl)
    for o_ in (ko, ro, fo):                          # сами лампы в кадре не видны (только их свет)
        try: o_.visible_camera = False
        except Exception: pass

# ---------------------------------------------------------------- мяч как в игре
def build_ball(coll):
    """Классический мяч (как в игре, WorldView.BallMaterial): 12 чёрных пятиугольников, 20 белых шестиугольников.
    Швы — настоящие канавки в геометрии, панели чуть выпуклые; цвет панелей — атрибут 'Panel', швы — 'Seam'.
    Свечение: GlowBase × GlowColor (огонь/лёд — светится весь мяч) + Arcane × шов (магия — светятся только швы).
    Возвращает мяч и три ключуемых входа (GlowBase, GlowColor, Arcane)."""
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
    bmesh.ops.create_icosphere(bm, subdivisions=6, radius=1.0)
    info = {}
    for v in bm.verts:
        d = v.co.normalized()
        dots = sorted(((d.dot(c), i) for i, c in enumerate(centers)), reverse=True)
        g = dots[0][0] - dots[1][0]                       # у шва две панели почти равны
        seam = max(0.0, 1 - g / 0.022)                    # ширина канавки
        puff = min(1.0, g / 0.12)                         # панель чуть выпуклая к центру
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
    b.inputs['Roughness'].default_value = 0.32                  # гладкая кожа с бликом
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
    L.new(gc.outputs[0], mx.inputs['A']); mx.inputs['B'].default_value = (*T.srgb((0.45, 0.85, 1.0)), 1)   # магия: бирюза
    L.new(mx.outputs['Result'], b.inputs['Emission Color'])
    me.materials.append(m)
    return ball, (gb.outputs[0], gc.outputs[0], ar.outputs[0])

# ---------------------------------------------------------------- путь мяча
P_NET0, P_NET = Vector((10.3, 0.35, 0.42)), Vector((NETX, 0.35, 0.32))
# макро-вспышки — за спиной камеры главного плана, чтобы их искры не попадали в кадр с мячом в центре
P_FIRE0, P_FIRE1 = Vector((8.6, 3.0, 0.9)), Vector((9.2, 3.0, 0.97))
P_ICE0, P_POST, P_ICE1 = Vector((10.5, -0.95, 0.33)), Vector((GX - 0.15, -T.GOAL_W / 2 + 0.14, 0.31)), Vector((11.2, -1.15, 0.24))
P_ARC0, P_ARC1 = Vector((9.0, -3.2, 0.56)), Vector((9.0, -3.2, 0.66))
NET_HIT = FL_['net']['start'] + 4
POST_HIT = FL_['ice']['start'] + 3

def settle(pt, f, hit):
    """После удара в сетку мяч сползает по ней на газон."""
    a = min(1.0, (f - hit) / 14)
    return Vector((pt.x - 0.15 * a, pt.y, pt.z - (pt.z - BR) * a * a + abs(math.sin(a * 7)) * 0.05 * (1 - a)))

def ball_at(f):
    ctr = Vector((0, 0, BR))
    def inside(k): return FL_[k]['start'] <= f < FL_[k]['end']
    def u(k): return (f - FL_[k]['start']) / (FL_[k]['end'] - FL_[k]['start'])
    if inside('net'):
        if f <= NET_HIT: return P_NET0.lerp(P_NET, (f - FL_['net']['start']) / (NET_HIT - FL_['net']['start']))
        return settle(P_NET, f, NET_HIT)
    if inside('fire'):                                   # замедленно летит, крутится в огне
        return P_FIRE0.lerp(P_FIRE1, u('fire'))
    if inside('ice'):                                    # в штангу и отскок
        if f <= POST_HIT: return P_ICE0.lerp(P_POST, (f - FL_['ice']['start']) / (POST_HIT - FL_['ice']['start']))
        a = (f - POST_HIT) / (FL_['ice']['end'] - POST_HIT)
        return P_POST.lerp(P_ICE1, a) + Vector((0, 0, math.sin(a * math.pi) * 0.12))
    if inside('arcane'):                                 # парит и медленно поднимается
        return P_ARC0.lerp(P_ARC1, u('arcane'))
    return ctr                                           # мяч в центре поля (и во вспышках «трибуны», «табло» — его там не видно)

def animate_ball(ball):
    spin = 0.0; prev = None
    for fr in range(1, Q.F_END + 1):
        p = ball_at(fr)
        if prev is not None and (p - prev).length < 1.0: spin += (p - prev).length / BR
        if FL_['fire']['start'] <= fr < FL_['fire']['end']: spin += 0.32          # закрученный мяч в огне
        if FL_['arcane']['start'] <= fr < FL_['arcane']['end']: spin += 0.06      # медленно поворачивается
        prev = p
        ball.location = p; ball.keyframe_insert('location', frame=fr)
        ball.rotation_euler = (0.25 * spin, spin, 0.3 * spin); ball.keyframe_insert('rotation_euler', frame=fr)
    T.set_interp(ball, 'location', 'LINEAR')
    T.set_interp(ball, 'rotation_euler', 'LINEAR')
    # на склейках мяч «телепортируется» — без размытия движения и без искр по пути
    for k in Q.ORDER:
        for f in (FL_[k]['start'] - 1, FL_[k]['end'] - 1):
            T.key_interp(ball, 'location', f, 'CONSTANT'); T.key_interp(ball, 'rotation_euler', f, 'CONSTANT')

def ball_glow(glow):
    """Огонь — оранжевое свечение мяча; лёд — голубое; магия — светятся только швы."""
    base, col, arc = glow
    fire, ice_c = (1.0, 0.35, 0.05, 1), (*T.srgb((0.4, 0.85, 1.0)), 1)
    fr, ic, ar = FL_['fire'], FL_['ice'], FL_['arcane']
    keys(base, [(1, 0.0), (fr['start'] - 1, 0.0), (fr['start'], 2.2), (fr['end'] - 1, 2.2), (fr['end'], 0.0),
                (ic['start'] - 1, 0.0), (ic['start'], 1.6), (ic['end'] - 1, 1.0), (ic['end'], 0.0)])
    keys(col, [(1, fire), (ic['start'], ice_c), (ic['end'], fire)])
    seq = [(1, 0.0), (ar['start'] - 1, 0.0), (ar['start'], 1.0), (ar['end'] - 1, 1.0), (ar['end'], 0.0)]
    for p in Q.PULSES:                                   # мяч в центре «помнит»: швы вспыхивают в такт сердцу
        seq += [(p - 1, 0.0), (p + 2, 0.32), (p + 14, 0.0)]
    keys(arc, seq)
    for fc in T.fcurves_of(base.node.id_data):
        for kp in fc.keyframe_points:
            kp.interpolation = 'CONSTANT' if 'GlowColor' in fc.data_path else 'LINEAR'

# ---------------------------------------------------------------- эффекты вспышек
def effects(ball, st):
    fr, ic, ar = FL_['fire'], FL_['ice'], FL_['arcane']
    ps = T.fire_trail(ball, fr['start'], fr['end'] - 2, st)
    ps.count = 320; ps.particle_size = 0.026; ps.lifetime = 8      # в макро огонь — язычками за мячом, мяч должен читаться
    spark_trail(ball, ic['start'], POST_HIT + 1, T.srgb((0.7, 0.92, 1.0)), 'Frost', count=220, size=0.018)
    spark_trail(ball, ar['start'], ar['end'] - 2, T.srgb((0.62, 0.38, 1.0)), 'Arcane', count=260, size=0.012)
    # штанга в инее: вспыхивает голубым, разлетаются ледяные осколки
    pm = bpy.data.materials.get('Post_1')
    if pm:
        b = T.bsdf_of(pm.node_tree.nodes)
        c0 = tuple(b.inputs['Emission Color'].default_value); frost = (*T.srgb((0.6, 0.92, 1.0)), 1)
        keys(b.inputs['Emission Color'], [(1, c0), (POST_HIT, frost), (ic['end'], c0)])
        for fc in T.fcurves_of(pm.node_tree):
            if 'Emission Color' in fc.data_path:
                for kp in fc.keyframe_points: kp.interpolation = 'CONSTANT'
        keys(b.inputs['Emission Strength'], [(POST_HIT - 1, 0.6), (POST_HIT, 4.0), (ic['end'] - 1, 2.0), (ic['end'], 0.6)])
    post_pt = (GX, -T.GOAL_W / 2, 0.32)
    burst('IceShard', post_pt, POST_HIT, T.srgb((0.75, 0.95, 1.0)), size=0.05, life=8, n=14, alpha=0.8, glow=6.0, spread=0.55)
    burst('IceMist', post_pt, POST_HIT, T.srgb((0.6, 0.9, 1.0)), size=0.2, life=8, alpha=0.14, glow=0.6)
    # табло: цифры меняются со вспышкой
    sm = bpy.data.materials.get('Score')
    if sm:
        keys(T.bsdf_of(sm.node_tree.nodes).inputs['Emission Strength'], [(T.G - 1, 3.0), (T.G, 10.0), (T.G + 5, 3.0)])

def net_bulge(back):
    """Задняя сетка «мешком» у самого объектива."""
    back.shape_key_add(name='Basis')
    sk = back.shape_key_add(name='Bulge')
    cy, cz = P_NET.y, P_NET.z
    for i, v in enumerate(back.data.vertices):
        y, z = v.co.y, v.co.z
        uu = (y + T.GOAL_W / 2) / T.GOAL_W; w = z / T.GOAL_H
        g = math.exp(-((y - cy) ** 2) / (2 * 0.6 ** 2) - ((z - cz) ** 2) / (2 * 0.22 ** 2))
        d = 0.5 * g * math.sin(math.pi * uu) * math.cos(math.pi * w * 0.5)
        sk.data[i].co = v.co + Vector((d, 0, 0))
    h, e = NET_HIT, FL_['net']['end']
    for f, val in ((1, 0), (h - 1, 0), (h + 2, 1.0), (h + 5, 0.75), (e, 0.5), (e + 1, 0)):
        sk.value = val; sk.keyframe_insert('value', frame=f)

# ---------------------------------------------------------------- камеры
def build_cameras():
    cams = {}
    def cam(k, lens, fstop, kk):
        c, t = T.camera('Cam_' + k, lens, fstop)
        T.cam_keys(c, t, kk); T.ease_all(c); T.ease_all(t); cams[k] = c
    # главный план: камера низко со стороны «наших» ворот, мяч в центре, медленный наезд — к концу быстрее
    cam('center', 32, 2.2, [(1, (7.6, 1.25, 1.35), (0, 0, 0.13), None), (300, (3.9, 0.65, 0.62), (0, 0, 0.13), None),
                            (Q.LIGHTS_OFF[0], (1.35, 0.22, 0.24), (0, 0, 0.135), None), (Q.DARK, (0.95, 0.15, 0.17), (0, 0, 0.14), None)])
    F = FL_
    # 1. сетка: камера за сеткой, мяч бьёт в неё прямо перед объективом
    cam('net', 26, 2.4, [(F['net']['start'], (12.62, 0.22, 0.3), (9.0, 0.4, 0.36), None), (F['net']['end'] - 1, (12.62, 0.26, 0.31), (9.2, 0.4, 0.35), None)])
    # 2. макро: мяч в огне крутится в воздухе, камера летит рядом, стадион размыт
    cam('fire', 55, 2.0, [(F['fire']['start'], P_FIRE0 + Vector((0.12, -0.72, 0.02)), P_FIRE0, None),
                          (F['fire']['end'] - 1, P_FIRE1 + Vector((0.08, -0.7, 0.02)), P_FIRE1, None)])
    # 3. трибуны снизу: вскакивают, стробоскоп
    cam('crowd', 28, 5.6, [(F['crowd']['start'], (1.4, 5.5, 0.45), (2.6, 9.6, 1.9), None), (F['crowd']['end'] - 1, (1.5, 5.6, 0.48), (2.7, 9.6, 2.0), None)])
    # 4. табло крупно: 0 : 0 → 0 : 1
    cam('score', 40, 2.8, [(F['score']['start'], (0.6, -5.95, 0.48), (0.8, -7.84, 0.36), None), (F['score']['end'] - 1, (0.65, -6.15, 0.45), (0.8, -7.84, 0.36), None)])
    # 5. штанга: мяч в штангу, иней и осколки
    cam('ice', 32, 2.4, [(F['ice']['start'], (11.0, -3.05, 0.34), (11.75, -1.75, 0.3), None), (F['ice']['end'] - 1, (11.08, -3.0, 0.35), (11.8, -1.8, 0.3), None)])
    # 6. макро: мяч парит, швы светятся магией
    cam('arcane', 60, 1.8, [(F['arcane']['start'], P_ARC0 + Vector((-0.5, -0.42, 0.04)), P_ARC0, None),
                            (F['arcane']['end'] - 1, P_ARC1 + Vector((-0.44, -0.4, 0.02)), P_ARC1, None)])
    # титр: мяч в темноте под лучом, камера у травы
    cam('title', 40, 1.6, [(Q.TITLE, (-2.4, -0.3, 0.16), (0, 0, 0.45), None), (Q.F_END, (-2.0, -0.25, 0.17), (0, 0, 0.47), None)])
    # в титре резкими должны быть и мяч (~2,3 м), и значок с надписью (~4,2 м): фокус между ними, диафрагма прикрыта
    td = cams['title'].data.dof
    td.focus_object = None; td.focus_distance = 3.2; td.aperture_fstop = 11.0
    order = [(1, 'center')]
    for k in Q.ORDER:
        order += [(F[k]['start'], k), (F[k]['end'], 'center')]
    order.append((Q.TITLE, 'title'))
    for f, k in order: T.cut(f, cams[k])
    bpy.context.scene.camera = cams['center']
    return cams

# ---------------------------------------------------------------- экспозиция: чёрный экран, вспышки, затемнения
def exposure(sc):
    vs = sc.view_settings; B = BASE_EXPOSURE
    pts = {1: -14.0, Q.LIGHTS_ON[0] - 1: -14.0, Q.LIGHTS_ON[0]: B}
    for k in Q.ORDER:                  # вспышка на входе, чуть пересвеченное «воспоминание», вспышка на выходе
        s, e = FL_[k]['start'], FL_[k]['end']
        pts.update({s - 1: B, s: B + 2.4, s + 2: B + 0.45, e - 1: B + 0.45, e: B + 1.6, e + 2: B})
    for i, f in enumerate(Q.LIGHTS_OFF):   # каждая погасшая мачта — заметно темнее (ступенькой)
        pts[f - 1] = B - 0.7 * i; pts[f] = B - 0.7 * (i + 1)
    pts.update({Q.DARK: -14.0, Q.TITLE - 1: -14.0, Q.TITLE: -6.0,
                Q.TITLE + 24: B - 0.3, Q.F_END - 12: B - 0.3, Q.F_END: -10.0})
    for f in sorted(pts):
        vs.exposure = pts[f]; vs.keyframe_insert('exposure', frame=f)
    hard = (Q.LIGHTS_ON[0] - 1, Q.TITLE - 1) + tuple(f - 1 for f in Q.LIGHTS_OFF)
    for fc in T.fcurves_of(sc):
        if fc.data_path == 'view_settings.exposure':
            for kp in fc.keyframe_points:
                kp.interpolation = 'CONSTANT' if int(round(kp.co[0])) in hard else 'LINEAR'

# ---------------------------------------------------------------- титр и звук
def title(cam):
    """Титр: слева — значок SpellStrike (мяч лёд/огонь, из emblem.py) с анимацией, справа — объёмное название."""
    import logo
    if FORMAT == 'horizontal':
        logo.spellstrike(cam, Q.TITLE_POP, ROOT, tagline='COMING SOON' if LANG == 'en' else 'СКОРО', size=0.56,
                         y0=1.42, subtitle=SUBTITLE, x0=0.78, bold=3.0, glow=1.5, turn=-0.22)
        emblem_title(cam, Q.TITLE_POP - 6, (-1.32, 0.78, -4.3), 0.5)
    else:
        logo.spellstrike(cam, Q.TITLE_POP, ROOT, tagline='COMING SOON' if LANG == 'en' else 'СКОРО', size=0.37,
                         y0=0.6, subtitle=SUBTITLE, bold=3.0, glow=1.5)
        emblem_title(cam, Q.TITLE_POP - 6, (0, 1.45, -4.3), 0.42)

def emblem_title(cam, f0, pos, scale):
    """Значок оживает: половинки (лёд и огонь) влетают с двух сторон и сталкиваются → вспыхивает разрез →
    вырастают пламя, сияние и кристаллы, разлетаются искры → половинки медленно вращаются, пламя течёт вверх."""
    import emblem as E
    from mathutils import Quaternion
    before = set(bpy.data.objects)
    parts = E.build()
    rig = T.link(bpy.data.objects.new('EmblemRig', None)); rig.parent = cam
    rig.location = pos; rig.rotation_euler = (-math.pi / 2, 0, 0); rig.scale = (scale,) * 3
    ec = bpy.data.collections.new('EmblemParts'); bpy.context.scene.collection.children.link(ec)
    for o in [o for o in bpy.data.objects if o not in before and o is not rig]:
        o.parent = rig; ec.objects.link(o)
        try: o.visible_shadow = False
        except Exception: pass
    # свет значка (как в emblem.py), в его системе координат
    for name, loc, col, e, size in (('EmbCool', (-3.5, -4.0, 3.5), (0.6, 0.85, 1.0), 900, 3), ('EmbWarm', (3.5, -3.0, -2.5), (1.0, 0.55, 0.25), 500, 3),
                                    ('EmbRim', (0.5, 4.0, 2.0), (0.8, 0.8, 1.0), 700, 2), ('EmbFront', (0, -6, 0.5), (1, 1, 1), 150, 4)):
        L_ = bpy.data.lights.new(name, 'AREA'); L_.color = T.srgb(col); L_.size = size
        lo = T.link(bpy.data.objects.new(name, L_)); lo.parent = rig; lo.location = loc
        lo.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        try: lo.light_linking.receiver_collection = ec     # светит только на значок, стадион за ним остаётся тёмным
        except Exception as ex: print('[ball] light linking:', ex)
        k = e * scale * scale
        for f, v in ((1, 0.0), (f0 - 1, 0.0), (f0 + 8, k * 1.6), (f0 + 20, k)):
            L_.energy = v; L_.keyframe_insert('energy', frame=f)

    def pop(o, f, over=1.18, dur=6):
        s0 = tuple(o.scale)
        for fr, k in ((1, 0.0), (f - 1, 0.0), (f, 0.2), (f + dur, over), (f + dur + 4, 1.0)):
            o.scale = tuple(max(1e-3, c * k) for c in s0); o.keyframe_insert('scale', frame=fr)

    hit = f0 + 8                                   # половинки сталкиваются
    for o, side in ((parts['ice'], -1), (parts['fire'], 1)):
        n = E.N_CUT * side
        end = Vector(o.location); q0 = o.rotation_euler.to_quaternion(); o.rotation_mode = 'QUATERNION'
        for fr, d, ang in ((1, 3.2, 0.0), (f0 - 1, 3.2, 0.0), (f0, 3.2, -1.2 * side), (hit - 2, 0.35, -0.25 * side), (hit, -0.04, 0.0), (hit + 3, 0.0, 0.0)):
            o.location = end + n * d; o.rotation_quaternion = Quaternion(E.T_CUT, ang) @ q0
            o.keyframe_insert('location', frame=fr); o.keyframe_insert('rotation_quaternion', frame=fr)
        for fr, s_ in ((1, 0.001), (f0 - 1, 0.001), (f0, 0.7), (hit, 1.0)):
            o.scale = (s_,) * 3; o.keyframe_insert('scale', frame=fr)
        # потом медленно вращаются вокруг оси разреза (в разные стороны): трещины и панели «живут»
        o.rotation_quaternion = Quaternion(E.N_CUT, 0.9 * side) @ q0; o.keyframe_insert('rotation_quaternion', frame=Q.F_END)
        T.key_interp(o, 'rotation_quaternion', hit + 3, 'LINEAR')
    # разрез: вспышка и вытягивание вдоль клинка
    bs = E.nodes(parts['blade_mat'])[2].inputs['Emission Strength']
    for fr, v in ((1, 0.0), (hit - 1, 0.0), (hit, 90.0), (hit + 10, 22.0)):
        bs.default_value = v; bs.keyframe_insert('default_value', frame=fr)
    for o in parts['blades']: pop(o, hit, over=1.25, dur=4)
    # пламя и сияние разгораются и текут вверх
    for i, o in enumerate(parts['flames'] + parts['magic']):
        pop(o, hit + 1 + i % 3, over=1.12, dur=7)
        nt = o.data.materials[0].node_tree
        mp = next((n for n in nt.nodes if n.type == 'MAPPING'), None)
        if mp:
            loc0 = tuple(mp.inputs['Location'].default_value)
            for fr, dz in ((hit, 0.0), (Q.F_END, -2.2)):
                mp.inputs['Location'].default_value = (loc0[0], loc0[1], loc0[2] + dz)
                mp.inputs['Location'].keyframe_insert('default_value', frame=fr)
            for fc in T.fcurves_of(nt):
                for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    # кристаллы растут по очереди
    for i, o in enumerate(parts['shards']):
        pop(o, hit + 3 + 2 * i, over=1.3, dur=4)
    # искры и блёстки: вылетают от разреза и медленно плывут наружу
    for o in parts['embers'] + parts['frosts']:
        end = Vector(o.location)
        for fr, k, s_ in ((1, 0.0, 0.001), (hit, 0.0, 0.001), (hit + 1, 0.15, 1.0), (hit + 10, 1.0, 1.0), (Q.F_END, 1.25, 0.6)):
            o.location = end * k; o.scale = (s_,) * 3
            o.keyframe_insert('location', frame=fr); o.keyframe_insert('scale', frame=fr)
    # весь значок едва заметно покачивается
    for fr, rz in ((hit, 0.0), ((hit + Q.F_END) // 2, 0.06), (Q.F_END, 0.0)):
        rig.rotation_euler = (-math.pi / 2, 0, rz); rig.keyframe_insert('rotation_euler', frame=fr)

def add_audio(sc):
    if not os.path.exists(AUDIO):
        print('[ball] звука нет (', AUDIO, ') — сначала запусти ball_audio.py'); return
    sc.sequence_editor_create()
    se = sc.sequence_editor
    coll = getattr(se, 'strips', None)
    if coll is None: coll = se.sequences
    coll.new_sound('ball_audio', AUDIO, 1, 1)
    ff = sc.render.ffmpeg
    try:
        ff.audio_codec = 'AAC'; ff.audio_bitrate = 256; ff.audio_channels = 'STEREO'; ff.audio_mixrate = 48000
    except Exception as e:
        print('[ball] звук в видео:', e)
    print('[ball] звук добавлен:', AUDIO)

if __name__ == '__main__' or os.environ.get('ARENA_RUN') == '1':
    main()
