# ======================== SpellStrike — таинственный трейлер 30 с, 9:16 ========================
# Как начало «Тачек»: ночь, тишина, мяч лежит в центре поля, ветер, далёкий гул трибун, голос комментатора.
# Прожекторы включаются по одному — и быстрая нарезка: огненный удар, сетка, кручёный удар, крюк, телепорт,
# ледяной разлом, Тень растворяется, финальный огненный гол прямо в камеру → чёрный экран → логотип SpellStrike.
# Запуск: Blender 5.2 → File → New → General → Scripting → Open → Tools/Blender/teaser.py → Run Script.
# Сохраняет Builds/Trailer/teaser.blend; рендер Ctrl+F12 → Builds/Trailer/arena_teaser_*.mp4 (со звуком,
# если рядом лежит teaser_audio.wav — его собирает Tools/Blender/teaser_audio.py).
# Использует функции трейлера (trailer.py): стадион, свет, героев и перенос анимаций.
import bpy, bmesh, math, os, sys, random
from mathutils import Vector

ROOT = os.environ.get('ARENA_ROOT') or r'C:\Users\user\ArenaStrikers'
sys.path.insert(0, os.path.join(ROOT, 'Tools', 'Blender'))
for m_ in ('trailer', 'teaser_cues', 'logo'):
    sys.modules.pop(m_, None)   # повторный запуск в том же Blender — свежие версии
import trailer as T
import teaser_cues as Q

T.F_END = Q.F_END
T.G = Q.FINAL_GOAL            # стробоскоп, конфетти, прыгающие трибуны, «ГОЛ!» на бортах — у финального гола
OUT = T.OUT
BR = T.BALL_R
random.seed(11)
# ===== НАСТРОЙКИ ВЕРСИИ (поменяй и запусти скрипт заново) =====
FORMAT = os.environ.get('ARENA_FORMAT') or 'vertical'   # 'vertical' — 1080×1920 (Shorts, TikTok, Reels); 'horizontal' — 1920×1080 (обычное видео YouTube)
LANG = os.environ.get('ARENA_LANG') or 'ru'              # 'ru' — «СКОРО» и русский голос; 'en' — «COMING SOON» и английский голос
# =================================================================
TAG = '%s_%s' % ('v' if FORMAT == 'vertical' else 'h', LANG)   # имя версии в файлах: v_ru, h_en …
AUDIO = os.path.join(OUT, 'teaser_audio.wav' if LANG == 'ru' else 'teaser_audio_%s.wav' % LANG)
BASE_EXPOSURE = -0.4
STILLS = os.environ.get('ARENA_STILLS')   # проверка Claude: только эти кадры (через запятую), без сохранения

# ---------------------------------------------------------------- мелкие помощники
def put(h, f, x, y, face_deg, z=0.0):
    """Герой на поле ступенькой: x, y — м, face — куда смотрит (0 — к воротам +X)."""
    h.arm.location = (x, y, z); h.arm.rotation_euler = (0, 0, math.radians(face_deg + 90))
    h.arm.keyframe_insert('location', frame=f); h.arm.keyframe_insert('rotation_euler', frame=f)
    T.key_interp(h.arm, 'location', f, 'CONSTANT'); T.key_interp(h.arm, 'rotation_euler', f, 'CONSTANT')

def move(h, f0, f1, p0, p1, face=None):
    """Бег по прямой с кадра f0 до f1."""
    ang = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0])) if face is None else face
    h.arm.rotation_euler = (0, 0, math.radians(ang + 90))
    for f, p in ((f0, p0), (f1, p1)):
        h.arm.location = (p[0], p[1], 0)
        h.arm.keyframe_insert('location', frame=f); h.arm.keyframe_insert('rotation_euler', frame=f)
    T.key_interp(h.arm, 'location', f0, 'LINEAR'); T.key_interp(h.arm, 'rotation_euler', f0, 'CONSTANT')
    T.key_interp(h.arm, 'location', f1, 'CONSTANT'); T.key_interp(h.arm, 'rotation_euler', f1, 'CONSTANT')

def hide(h, f):
    """Убрать героя под землю (между своими планами его не видно)."""
    put(h, f, 0, 0, 0, z=-40)

def keys(sock, pairs):
    """Ключи на входе узла (default_value)."""
    for f, v in pairs:
        sock.default_value = v; sock.keyframe_insert('default_value', frame=f)

def emit_mat(name, color, strength, alpha=None):
    return T.mat(name, color, 0.4, emit=color, emit_str=strength, alpha=alpha)

def burst(name, pos, f, color, size=1.0, life=10, n=1, alpha=0.85, glow=8.0, rise=0.0, spread=0.9):
    """Вспышка-шар (телепорт, дым Тени): раздувается и гаснет."""
    m = emit_mat(name, color, glow, alpha=0.9)
    b = T.bsdf_of(m.node_tree.nodes)
    keys(b.inputs['Alpha'], [(1, 0.0), (f - 1, 0.0), (f, alpha), (f + life, 0.0)])
    keys(b.inputs['Emission Strength'], [(f, glow), (f + life, 0.0)])
    for i in range(n):
        me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
        for fc in bm.faces: fc.smooth = True
        bm.to_mesh(me); bm.free(); me.materials.append(m)
        o = T.link(bpy.data.objects.new(name, me))
        off = Vector(((random.random() - 0.5) * spread, (random.random() - 0.5) * spread, random.random() * spread)) if n > 1 else Vector()
        o.location = Vector(pos) + off
        if rise:   # дымок всплывает
            o.keyframe_insert('location', frame=f)
            o.location = Vector(pos) + off + Vector((0, 0, rise * (0.6 + 0.8 * random.random()))); o.keyframe_insert('location', frame=f + life)
        s = size * (0.6 + 0.6 * random.random()) if n > 1 else size
        for fr, k in ((1, 0.001), (f - 1, 0.001), (f, s * 0.25), (f + life, s), (f + life + 1, 0.001)):
            o.scale = (k, k, k); o.keyframe_insert('scale', frame=fr)
        try: o.visible_shadow = False
        except Exception: pass

def smoke(name, pos, f, color, life=18, n=7):
    """Объёмный дым (Тень растворяется): клубы с шумом внутри, всплывают, густеют и тают."""
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
        o = T.link(bpy.data.objects.new(name, me))
        p0 = Vector(pos) + Vector(((random.random() - 0.5) * 0.5, (random.random() - 0.5) * 0.5, 0.15 + random.random() * 0.45))
        s0 = 0.15 + random.random() * 0.1
        for fr, loc, sc_ in ((f - 1, p0, 0.001), (f, p0, s0), (f + life, p0 + Vector((0, 0, 0.6 + random.random() * 0.6)), s0 * 1.9), (f + life + 1, p0, 0.001)):
            o.location = loc; o.scale = (sc_,) * 3
            o.keyframe_insert('location', frame=fr); o.keyframe_insert('scale', frame=fr)
        o.scale = (0.001,) * 3; o.keyframe_insert('scale', frame=1)
        try: o.visible_shadow = False
        except Exception: pass

def spark_trail(ball, f0, f1, color, name):
    """След из искр за мячом (кручёный удар) — как огненный след трейлера, но своего цвета."""
    m1 = emit_mat(name + 'A', color, 16); m2 = emit_mat(name + 'B', (1, 1, 1), 20)
    c = bpy.data.collections.new(name + 'Bits'); bpy.context.scene.collection.children.link(c)
    for i, mm in enumerate((m1, m1, m2)):
        me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0); bm.to_mesh(me); bm.free()
        me.materials.append(mm); c.objects.link(bpy.data.objects.new(name + str(i), me))
    T.hide_collection(c)
    ball.modifiers.new(name, 'PARTICLE_SYSTEM')
    ps = ball.particle_systems[-1].settings
    ps.count = 700; ps.frame_start = f0; ps.frame_end = f1; ps.lifetime = 14; ps.lifetime_random = 0.5
    ps.emit_from = 'VOLUME'; ps.normal_factor = 0.0; ps.factor_random = 0.15
    ps.render_type = 'COLLECTION'; ps.instance_collection = c; ps.use_collection_pick_random = True
    ps.particle_size = 0.035; ps.size_random = 0.6; ps.effector_weights.gravity = 0.0

def bar_mesh(name, m, radius=0.03):
    """Цилиндр длиной 1 от начала координат вдоль Z (цепь крюка): масштаб по Z = длина."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=radius, radius2=radius, depth=1.0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 0.5))
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    return T.link(bpy.data.objects.new(name, me))

def wall_mesh(name, m):
    """Брусок длиной 1 от начала координат вдоль X (ледяной разлом): масштаб X = длина."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0.5, 0, 0.5))
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.06, segments=2, affect='EDGES')
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    return T.link(bpy.data.objects.new(name, me))

# ================================================================ сборка
CLIPS = ['idle', 'idle_show', 'run', 'run_ball', 'kick', 'kick_strong', 'throw', 'ground_punch', 'cel_victory']

def main():
    T.clean()
    sc = bpy.context.scene
    print('[teaser] анимации...')
    clips = {n: T.Clip(os.path.join(T.HEROES, 'Anim', n + '.fbx'), n) for n in CLIPS}
    T.setup_render(sc)
    T.setup_compositor(sc)
    sc.frame_end = Q.F_END
    if FORMAT == 'horizontal':   # кадры строились по высоте (камеры с вертикальной подгонкой) — в 16:9 просто видно шире по бокам
        sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.filepath = os.path.join(OUT, 'spellstrike_teaser_%s_' % TAG)
    st = bpy.data.collections.new('Stadium'); sc.collection.children.link(st)
    hc = bpy.data.collections.new('Heroes'); sc.collection.children.link(hc)

    # с какого места клипа начать, чтобы касание мяча пришлось ровно на нужный кадр
    def t0_for(clip, f_contact, f_start, speed):
        return max(0.0, clips[clip].contact_time() - (f_contact - f_start) / 30.0 * speed)
    C = Q.CUTS

    print('[teaser] герои...')
    H = {f: T.Hero(f, T.TEAM[f], hc) for f in T.FOLDERS}
    J, B, Tn, S, M, V = H['Jar'], H['Bagor'], H['Ten'], H['Stena'], H['Mig'], H['Vihr']
    segs = {
        'Jar': [(1, 'idle', 0, 1, True), (C['step'], 'run', 0.2, 0.75, True), (Q.STEP_FOOT, 'idle_show', 0.6, 1, True),
                (C['fire'], 'kick_strong', t0_for('kick_strong', Q.FIRE_KICK, C['fire'], 0.55), 0.55, False), (C['net'], 'idle', 0, 1, True),
                (C['run'], 'run_ball', 0, 1.15, True),
                (C['final'], 'kick_strong', t0_for('kick_strong', Q.FINAL_KICK, C['final'], 0.75), 0.75, False),
                (Q.FINAL_GOAL + 14, 'cel_victory', 0.3, 1, False)],
        'Vihr': [(1, 'idle', 0.4, 1, True), (C['curve'], 'kick', t0_for('kick', Q.CURVE_KICK, C['curve'], 0.9), 0.9, False), (Q.CURVE_END - 8, 'idle', 0, 1, True)],
        'Bagor': [(1, 'idle', 0.8, 1, True), (C['hook'], 'throw', 0.15, 1.1, False), (Q.HOOK_BACK + 4, 'idle_show', 0.3, 1, True)],
        'Mig': [(1, 'idle', 1.2, 1, True), (C['blink'], 'run', 0.1, 1.15, True)],
        'Stena': [(1, 'idle', 1.6, 1, True), (C['wall'], 'ground_punch', 0.35, 1.2, False), (Q.WALL_HIT + 6, 'idle_show', 0, 1, True)],
        'Ten': [(1, 'idle', 2.0, 1, True), (C['shade'], 'run_ball', 0.2, 1.1, True)],
    }
    for f, h in H.items():
        rig = T.Rig(h.arm, clips['idle'])
        rig.bake(clips, segs[f], 1, Q.F_END, xfade=4)
        h.arm.scale = (h.scale,) * 3
        hide(h, 1)

    # ---- расстановка по планам (вне своего плана герой под землёй)
    move(J, C['step'], Q.STEP_FOOT, (2.6, 1.4), (0.38, 0.05)); put(J, Q.STEP_FOOT, 0.38, 0.05, 180)   # подходит, нога на мяче
    put(J, C['fire'], 6.5, 0.6, 0)                                                                   # огненный удар
    hide(J, C['curve'])
    move(J, C['run'], C['final'] - 1, (-2.0, 1.0), (5.2, 0.5), face=0)                              # бежит с мячом
    put(J, C['final'], 7.2, 0.4, 0)                                                                  # финальный удар
    put(J, Q.FINAL_GOAL + 14, 7.2, 0.4, 20); hide(J, C['black'])
    put(V, C['curve'], -4.0, -3.0, 18); hide(V, C['hook'])
    put(B, C['hook'], -6.0, 3.2, -90); hide(B, C['blink'])
    move(M, C['blink'], Q.BLINK - 1, (2.2, 4.0), (3.0, 4.0), face=0)
    move(M, Q.BLINK, C['wall'] - 1, (5.6, 4.0), (6.6, 4.0), face=0); hide(M, C['wall'])
    put(S, C['wall'], -8.4, -1.5, 0); hide(S, C['shade'])
    move(Tn, C['shade'], Q.SHADE_VANISH, (4.6, -4.0), (2.6, -4.0), face=180); hide(Tn, Q.SHADE_VANISH + 1)
    # свечение Жара: разгорается, когда он ставит ногу на мяч, и на огненных ударах
    if J.glow is not None:
        keys(J.glow, [(1, 2.0), (Q.STEP_FOOT, 2.0), (Q.STEP_FOOT + 20, 9.0), (C['fire'], 6.0), (Q.FIRE_KICK, 12.0),
                      (C['net'], 4.0), (C['run'], 4.0), (C['final'] - 1, 11.0), (Q.FINAL_KICK, 14.0), (C['crowd'], 6.0)])

    print('[teaser] стадион...')
    T.build_pitch(st)
    back_home = T.build_goal(1, T.HOME_L, st)
    T.build_goal(-1, T.AWAY_L, st)
    strips = T.build_boards(st)
    rows = T.build_stands(st)
    T.animate_crowd(rows)
    spots = T.build_lights(st, strips)
    lights_off_until_on(spots, strips)

    print('[teaser] мяч и эффекты...')
    ball, ball_em = T.build_ball(st)
    for n in ball.data.materials[0].node_tree.nodes:   # узор мяча крупнее: на крупных планах он должен читаться
        if n.type == 'TEX_VORONOI': n.inputs['Scale'].default_value = 16.0
        if n.type == 'VALTORGB':
            n.color_ramp.elements[0].position = 0.3; n.color_ramp.elements[1].position = 0.33
    path = ball_path()
    spin = 0.0; prev = None
    for fr in range(1, Q.F_END + 1):
        p = path(fr)
        if prev is not None and (p - prev).length < 1.0: spin += (p - prev).length / BR
        prev = p
        ball.location = p; ball.keyframe_insert('location', frame=fr)
        ball.rotation_euler = (0, spin, 0.3 * spin); ball.keyframe_insert('rotation_euler', frame=fr)
    T.set_interp(ball, 'location', 'LINEAR')
    keys(ball_em, [(1, 0.0), (Q.FIRE_KICK - 1, 0.0), (Q.FIRE_KICK, 9.0), (C['net'] + 20, 3.0), (C['curve'], 0.0),
                   (Q.FINAL_KICK - 1, 0.0), (Q.FINAL_KICK, 10.0), (C['crowd'], 3.0), (C['black'], 0.0)])
    T.fire_trail(ball, Q.FIRE_KICK, Q.FIRE_NET + 2, st)
    T.fire_trail(ball, Q.FINAL_KICK, Q.FINAL_GOAL + 3, st)
    spark_trail(ball, Q.CURVE_KICK, Q.CURVE_END, T.srgb((0.35, 0.95, 1.0)), 'Spin')
    T.net_bulge(back_home, 1, 0.3, 0.38, Q.FIRE_NET)
    sk = back_home.data.shape_keys.key_blocks['Bulge']   # второй «мешок» той же сетки — финальный гол
    for f, v in ((Q.FINAL_GOAL - 1, 0), (Q.FINAL_GOAL + 3, 1.15), (Q.FINAL_GOAL + 9, -0.3), (Q.FINAL_GOAL + 15, 0.5),
                 (Q.FINAL_GOAL + 22, -0.1), (Q.FINAL_GOAL + 32, 0.15), (Q.FINAL_GOAL + 44, 0)):
        sk.value = v; sk.keyframe_insert('value', frame=f)
    T.confetti(st)
    hook_fx()
    blink_fx()
    wall_fx()
    smoke('ShadeSmoke', (2.6, -4.0, 0.0), Q.SHADE_VANISH, T.srgb((0.38, 0.18, 0.75)))

    print('[teaser] камеры...')
    cams = build_cameras()
    flashes_and_fades(sc)
    title(cams['title'], Q.TITLE_POP)
    add_audio(sc)

    if STILLS:
        return
    sc.frame_set(1)
    p = os.path.join(OUT, 'teaser_%s.blend' % TAG)
    try:
        bpy.ops.wm.save_as_mainfile(filepath=p); print('[teaser] сохранено:', p)
    except Exception as e:
        print('[teaser] не сохранилось:', e)
    print('[teaser] готово. Рендер: Ctrl+F12 ->', sc.render.filepath)

# ---------------------------------------------------------------- свет: ночь → прожекторы по одному
def lights_off_until_on(spots, strips):
    on_all = Q.LIGHTS[-1]
    for i, (L, lc) in enumerate(spots):   # мачта i включается на Q.LIGHTS[i]; ключи стробоскопа у гола остаются
        f_on = Q.LIGHTS[i % len(Q.LIGHTS)]
        for f, e in ((1, 0.0), (f_on - 1, 0.0), (f_on, 26000.0)):
            L.energy = e; L.keyframe_insert('energy', frame=f)
        T.key_interp(L, 'energy', f_on - 1, 'CONSTANT')
    panels = [m for m in bpy.data.materials if m.name.startswith('FloodPanel')]
    for i, m in enumerate(panels):
        s = T.bsdf_of(m.node_tree.nodes).inputs['Emission Strength']; v = s.default_value
        f_on = Q.LIGHTS[i % len(Q.LIGHTS)]
        keys(s, [(1, 0.0), (f_on - 1, 0.0), (f_on, v * 1.6), (f_on + 8, v)])
    # неон, лампы трибун, реклама, линии поля — загораются вместе с последней мачтой
    names = ('StandNeon', 'Lamp', 'AdText', 'StandEdge', 'Lines', 'AdGoal', 'Post_')
    mats = [m for m in bpy.data.materials if m.name.startswith(names)] + [m for m in strips if m.name not in [x.name for x in bpy.data.materials if x.name.startswith(names)]]
    for m in mats:
        s = T.bsdf_of(m.node_tree.nodes).inputs['Emission Strength']; v = s.default_value
        keys(s, [(1, 0.0), (on_all - 1, 0.0), (on_all, v)])
    crowd = bpy.data.materials.get('Crowd')
    if crowd:
        keys(T.bsdf_of(crowd.node_tree.nodes).inputs['Emission Strength'], [(1, 0.02), (on_all - 1, 0.02), (on_all, 0.35)])
    for o in bpy.data.objects:
        if o.type == 'LIGHT' and o.data.name.startswith('Fill'):
            e = o.data.energy
            for f, v in ((1, 0.0), (on_all - 1, 0.0), (on_all, e)):
                o.data.energy = v; o.data.keyframe_insert('energy', frame=f)
        if o.type == 'LIGHT' and o.data.name.startswith('Moon'):
            o.data.energy = 0.3
    # узкий луч сверху на мяч в темноте — как в начале «Тачек»: в кадре только мяч
    kl = bpy.data.lights.new('BallKey', 'SPOT'); kl.energy = 0; kl.color = T.hexc('#a9bcff')
    kl.spot_size = math.radians(16); kl.spot_blend = 0.85; kl.shadow_soft_size = 0.3
    ko = T.link(bpy.data.objects.new('BallKey', kl)); ko.location = (-0.8, 0.5, 7.0)
    ko.rotation_euler = (Vector((0, 0, BR)) - ko.location).to_track_quat('-Z', 'Y').to_euler()
    for f, e in ((1, 0), (30, 0), (110, 260), (Q.LIGHTS[0] - 1, 300), (Q.LIGHTS[0], 0), (Q.CUTS['title'] - 1, 0), (Q.CUTS['title'], 300)):
        kl.energy = e; kl.keyframe_insert('energy', frame=f)
    # к титру стадион притихает: прожекторы в четверть силы, в центре — снова луч на мяч
    for L, lc in spots:
        for f, e in ((Q.CUTS['title'] - 1, 26000.0), (Q.CUTS['title'], 6000.0)):
            L.energy = e; L.color = lc; L.keyframe_insert('energy', frame=f); L.keyframe_insert('color', frame=f)

# ---------------------------------------------------------------- путь мяча по всем планам
def bezier(p0, p1, p2, t):
    return p0 * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + p2 * t * t

def ball_path():
    C = Q.CUTS
    ctr = Vector((0, 0, BR))
    j_fire = Vector((6.5 + 0.42, 0.6, BR))
    net_pt = Vector((T.FL / 2 + T.NET_D - 0.08, 0.3, 0.38))
    v_ball = Vector((-4.0 + 0.40 * math.cos(math.radians(18)), -3.0 + 0.40 * math.sin(math.radians(18)), BR))
    v_ctrl, v_end = Vector((-0.4, -6.4, BR)), Vector((3.2, -1.2, BR))
    hook_ball = Vector((-6.0, -0.4, BR)); bagor_feet = Vector((-6.0, 2.78, BR))
    wall_y = -1.5
    sh0, sh1 = Vector((4.6 - 0.42, -4.0, BR)), Vector((2.6 - 0.42, -4.0, BR))
    j_run0, j_run1 = Vector((-2.0 + 0.42, 1.0, BR)), Vector((5.2 + 0.42, 0.5, BR))
    j_final = Vector((7.2 + 0.42, 0.4, BR))
    goal_pt = Vector((T.FL / 2 + T.NET_D - 0.08, 0.12, 0.36))
    under = Vector((0, 0, -30))

    def path(f):
        if f < C['fire']:
            return ctr                                                   # лежит в центре; Жар ставит на него ногу
        if f < Q.FIRE_KICK:
            return j_fire
        if f <= Q.FIRE_NET:
            a = (f - Q.FIRE_KICK) / (Q.FIRE_NET - Q.FIRE_KICK)
            return j_fire.lerp(net_pt, a) + Vector((0, 0, math.sin(a * math.pi) * 0.25))
        if f < C['curve']:
            a = min(1.0, (f - Q.FIRE_NET) / 18)
            return Vector((net_pt.x - 0.15 * a, net_pt.y, 0.38 - (0.38 - BR) * a * a + abs(math.sin(a * 7)) * 0.06 * (1 - a)))
        if f < Q.CURVE_KICK:
            return v_ball
        if f <= Q.CURVE_END:
            t = (f - Q.CURVE_KICK) / (Q.CURVE_END - Q.CURVE_KICK)
            return bezier(v_ball, v_ctrl, v_end, 1 - (1 - t) ** 1.6)     # дуга: сначала быстро, к концу медленнее
        if f < C['hook']:
            return v_end
        if f < Q.HOOK_GRAB:
            return hook_ball
        if f <= Q.HOOK_BACK:
            a = (f - Q.HOOK_GRAB) / (Q.HOOK_BACK - Q.HOOK_GRAB)
            return hook_ball.lerp(bagor_feet, a * a * (3 - 2 * a)) + Vector((0, 0, math.sin(a * math.pi) * 0.35))
        if f < C['wall']:
            return under if f >= C['blink'] else bagor_feet
        if f < C['shade']:                                               # мяч катится на разлом и отскакивает
            p0, hit, p2 = Vector((-5.6, 2.6, BR)), Vector((-5.6, wall_y + 0.24, BR)), Vector((-4.9, 1.2, BR))
            if f < C['wall'] + 4: return p0
            if f <= Q.WALL_HIT:
                return p0.lerp(hit, (f - C['wall'] - 4) / (Q.WALL_HIT - C['wall'] - 4))
            a = (f - Q.WALL_HIT) / (C['shade'] - Q.WALL_HIT)
            return hit.lerp(p2, 1 - (1 - a) ** 2) + Vector((0, 0, math.sin(a * math.pi) * 0.2))
        if f < C['run']:                                                 # Тень ведёт мяч, исчезает — мяч катится сам
            if f <= Q.SHADE_VANISH:
                return sh0.lerp(sh1, (f - C['shade']) / (Q.SHADE_VANISH - C['shade']))
            a = (f - Q.SHADE_VANISH) / (C['run'] - Q.SHADE_VANISH)
            return sh1 + Vector((-1.6 * (1 - (1 - a) ** 2), 0, 0))
        if f < C['final']:
            a = (f - C['run']) / (C['final'] - 1 - C['run'])
            return j_run0.lerp(j_run1, a) + Vector((0, 0, abs(math.sin(f * 0.5)) * 0.04))
        if f < Q.FINAL_KICK:
            return j_final
        if f <= Q.FINAL_GOAL:
            a = (f - Q.FINAL_KICK) / (Q.FINAL_GOAL - Q.FINAL_KICK)
            return j_final.lerp(goal_pt, a) + Vector((0, 0, math.sin(a * math.pi) * 0.18))
        if f < C['title']:
            a = min(1.0, (f - Q.FINAL_GOAL) / 18)
            return Vector((goal_pt.x - 0.15 * a, goal_pt.y, 0.36 - (0.36 - BR) * a * a))
        return ctr                                                       # титр: снова один мяч в центре
    return path

# ---------------------------------------------------------------- способности
def hook_fx():
    gold = T.mat('HookChain', T.hexc('#e2c48a'), 0.3, 0.9, emit=T.hexc('#e2c48a'), emit_str=1.5)
    chain = bar_mesh('HookChain', gold, 0.022)
    head = bar_mesh('HookHead', gold, 0.07)
    start = Vector((-6.0 - 0.18, 3.2 - 0.25, 0.55)); ball = Vector((-6.0, -0.4, BR))
    d = ball - start; L0 = d.length; dn = d.normalized()
    rot = dn.to_track_quat('Z', 'Y').to_euler()
    chain.rotation_euler = rot; head.rotation_euler = rot
    def at(f, length):
        chain.location = start; chain.scale = (1, 1, max(0.001, length))
        chain.keyframe_insert('location', frame=f); chain.keyframe_insert('scale', frame=f)
        head.location = start + dn * max(0.0, length - 0.06); head.keyframe_insert('location', frame=f)
        hs = 1.0 if length > 0.05 else 0.001
        head.scale = (hs, hs, 0.12 * hs); head.keyframe_insert('scale', frame=f)
    at(1, 0); at(Q.HOOK_THROW - 1, 0)
    for f in range(Q.HOOK_THROW, Q.HOOK_GRAB + 1):
        at(f, L0 * (f - Q.HOOK_THROW) / (Q.HOOK_GRAB - Q.HOOK_THROW))
    for f in range(Q.HOOK_GRAB + 1, Q.HOOK_BACK + 1):
        a = (f - Q.HOOK_GRAB) / (Q.HOOK_BACK - Q.HOOK_GRAB); a = a * a * (3 - 2 * a)
        at(f, L0 * (1 - a))   # цепь сматывается вместе с мячом
    at(Q.HOOK_BACK + 1, 0)

def blink_fx():
    cyan = T.srgb((0.45, 0.95, 1.0))
    burst('BlinkOut', (3.0, 4.0, 0.65), Q.BLINK, cyan, size=0.45, life=6, alpha=0.5)
    burst('BlinkIn', (5.6, 4.0, 0.65), Q.BLINK, cyan, size=0.55, life=7, alpha=0.45)
    burst('BlinkSpark', (5.6, 4.0, 0.6), Q.BLINK, (1, 1, 1), size=0.18, life=9, n=6)

def wall_fx():
    ice = T.mat('IceWall', T.srgb((0.45, 0.82, 1.0)), 0.08, emit=T.srgb((0.3, 0.75, 1.0)), emit_str=0.45, alpha=0.75)
    try: T.bsdf_of(ice.node_tree.nodes).inputs['Transmission Weight'].default_value = 0.6
    except Exception: pass
    w = wall_mesh('IceWall', ice)
    w.location = (-8.0, -1.5, 0.0)
    for f, sx, sz in ((1, 0.001, 0.001), (Q.WALL_PUNCH - 1, 0.001, 0.001), (Q.WALL_PUNCH, 0.6, 0.55), (Q.WALL_PUNCH + 4, 4.6, 0.85),
                      (Q.WALL_PUNCH + 6, 4.2, 0.78), (Q.CUTS['shade'] - 1, 4.2, 0.78), (Q.CUTS['shade'], 0.001, 0.001)):
        w.scale = (sx, 0.3, sz); w.keyframe_insert('scale', frame=f)
    T.set_interp(w, 'scale', 'LINEAR')
    burst('IceDust', (-6.0, -1.5, 0.3), Q.WALL_PUNCH + 2, T.srgb((0.75, 0.95, 1.0)), size=0.16, life=9, n=8, alpha=0.6, glow=3.0)

# ---------------------------------------------------------------- камеры
def build_cameras():
    C = Q.CUTS; cams = {}
    def cam(k, lens, fstop, kk):
        c, t = T.camera('Cam_' + k, lens, fstop)
        T.cam_keys(c, t, kk); T.ease_all(c); T.ease_all(t); cams[k] = c
    # 1. мяч в темноте: камера у самой травы медленно подъезжает
    cam('intro', 55, 1.2, [(1, (-2.3, -0.55, 0.09), (0, 0, 0.16), None), (C['step'] - 1, (-1.15, -0.28, 0.1), (0, 0, 0.17), None)])
    # 2. подходит Жар — низко, видно ноги и мяч
    cam('step', 32, 1.8, [(C['step'], (-1.3, -1.0, 0.22), (0.3, 0.2, 0.25), None), (C['fire'] - 1, (-1.05, -0.8, 0.3), (0.25, 0.1, 0.55), None)])
    # 3. огненный удар — сбоку, едем за мячом
    cam('fire', 26, 2.8, [(C['fire'], (5.0, -2.9, 0.55), (6.8, 0.6, 0.55), None), (Q.FIRE_KICK, (5.4, -2.9, 0.5), (7.0, 0.6, 0.45), None),
                          (C['net'] - 1, (8.4, -2.5, 0.5), (10.6, 0.4, 0.35), None)])
    # 4. сетка — из-за ворот, мяч летит в лицо
    cam('net', 18, 2.8, [(C['net'], (12.6, 0.75, 0.42), (8.5, 0.4, 0.35), None), (C['curve'] - 1, (12.62, 0.65, 0.48), (9.0, 0.35, 0.3), None)])   # между сеткой и бортом
    # 5. кручёный — сверху-сбоку, видна дуга
    cam('curve', 24, 4.0, [(C['curve'], (-6.4, -0.9, 2.4), (0.4, -4.2, 0.0), None), (C['hook'] - 1, (-5.6, -1.1, 2.0), (1.6, -3.4, 0.0), None)])   # слева-сзади и выше Вихря: дуга уходит вглубь
    # 6. крюк — сбоку, низко
    cam('hook', 24, 2.8, [(C['hook'], (-11.6, 1.5, 0.8), (-6.0, 1.4, 0.5), None), (C['blink'] - 1, (-11.3, 1.4, 0.9), (-6.0, 1.6, 0.5), None)])
    # 7. телепорт — спереди
    cam('blink', 30, 2.8, [(C['blink'], (8.8, 2.6, 0.75), (4.0, 4.0, 0.65), None), (C['wall'] - 1, (8.6, 2.7, 0.7), (5.2, 4.0, 0.65), None)])
    # 8. разлом — низко, лёд на камеру
    cam('wall', 26, 2.8, [(C['wall'], (-3.4, 3.2, 1.25), (-6.8, -1.4, 0.4), None), (C['shade'] - 1, (-3.7, 3.0, 1.1), (-6.4, -1.3, 0.4), None)])   # со стороны мяча: видно Стену, лёд и отскок
    # 9. Тень бежит на камеру и исчезает
    cam('shade', 45, 2.0, [(C['shade'], (-0.4, -3.6, 0.45), (3.6, -4.0, 0.55), None), (C['run'] - 1, (-0.2, -3.7, 0.4), (2.0, -4.0, 0.3), None)])
    # 10. Жар бежит с мячом — камера едет спиной вперёд
    cam('run', 28, 2.8, [(C['run'], (0.4, 0.85, 0.42), (-2.0, 1.0, 0.65), None), (C['final'] - 1, (7.4, 0.45, 0.5), (5.2, 0.5, 0.7), None)])
    # 11. финальный удар — из-за ворот, мяч прямо в объектив
    cam('final', 18, 3.2, [(C['final'], (12.62, 0.3, 0.45), (7.6, 0.4, 0.55), None), (Q.FINAL_GOAL, (12.6, 0.25, 0.43), (8.0, 0.35, 0.5), None),
                           (C['crowd'] - 1, (12.62, 0.7, 0.6), (7.6, 0.3, 0.7), None)])
    # 12. стадион ревёт — общий план со стробоскопом
    cam('crowd', 22, 8.0, [(C['crowd'], (-3.5, -15.5, 8.5), (5.0, 0.0, 1.2), None), (C['black'] - 1, (-1.0, -16.0, 9.5), (5.0, 0.0, 1.0), None)])
    # 13. титр: снова мяч в центре поля, камера у травы
    cam('title', 40, 1.6, [(C['title'], (-2.4, -0.3, 0.16), (0, 0, 0.45), None), (Q.F_END, (-2.0, -0.25, 0.17), (0, 0, 0.47), None)])
    order = [(1, 'intro'), (C['step'], 'step'), (C['fire'], 'fire'), (C['net'], 'net'), (C['curve'], 'curve'), (C['hook'], 'hook'),
             (C['blink'], 'blink'), (C['wall'], 'wall'), (C['shade'], 'shade'), (C['run'], 'run'), (C['final'], 'final'),
             (C['crowd'], 'crowd'), (C['title'], 'title')]
    for f, k in order: T.cut(f, cams[k])
    bpy.context.scene.camera = cams['intro']
    return cams

# ---------------------------------------------------------------- вспышки на склейках, затемнения
def flashes_and_fades(sc):
    vs = sc.view_settings; C = Q.CUTS
    pts = {1: -9.0, 50: BASE_EXPOSURE}
    for k in ('fire', 'net', 'curve', 'hook', 'blink', 'wall', 'shade', 'run', 'final', 'crowd'):
        f = C[k]; pts[f - 1] = BASE_EXPOSURE; pts[f] = BASE_EXPOSURE + 2.2; pts[f + 3] = BASE_EXPOSURE
    pts[Q.FINAL_GOAL] = BASE_EXPOSURE + 1.4; pts[Q.FINAL_GOAL + 4] = BASE_EXPOSURE
    pts.update({C['black'] - 1: BASE_EXPOSURE, C['black']: -14.0, C['title'] - 1: -14.0, C['title']: -6.0,
                C['title'] + 24: BASE_EXPOSURE - 0.3, Q.F_END - 12: BASE_EXPOSURE - 0.3, Q.F_END: -10.0})
    for f in sorted(pts):
        vs.exposure = pts[f]; vs.keyframe_insert('exposure', frame=f)
    for fc in T.fcurves_of(sc):
        if fc.data_path == 'view_settings.exposure':
            for kp in fc.keyframe_points:
                kp.interpolation = 'CONSTANT' if int(round(kp.co[0])) in (C['black'] - 1, C['title'] - 1) else 'LINEAR'

# ---------------------------------------------------------------- титр: логотип SpellStrike (logo.py)
def title(cam, f0):
    import logo
    sz = 0.52 if FORMAT == 'horizontal' else 0.37   # в широком кадре места по бокам больше — логотип крупнее
    logo.spellstrike(cam, f0, ROOT, tagline='СКОРО' if LANG == 'ru' else 'COMING SOON', size=sz, y0=0.95 if FORMAT != 'horizontal' else 1.05)

# ---------------------------------------------------------------- звук
def add_audio(sc):
    if not os.path.exists(AUDIO):
        print('[teaser] звука нет (', AUDIO, ') — ролик будет без звука'); return
    sc.sequence_editor_create()
    se = sc.sequence_editor
    coll = getattr(se, 'strips', None)
    if coll is None: coll = se.sequences
    coll.new_sound('teaser_audio', AUDIO, 1, 1)
    ff = sc.render.ffmpeg
    try:
        ff.audio_codec = 'AAC'; ff.audio_bitrate = 256; ff.audio_channels = 'STEREO'; ff.audio_mixrate = 48000
    except Exception as e:
        print('[teaser] звук в видео:', e)
    print('[teaser] звук добавлен:', AUDIO)

if __name__ == '__main__' or os.environ.get('ARENA_RUN') == '1':
    main()
