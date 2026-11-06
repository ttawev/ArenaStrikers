
# ======================== SpellStrike — трейлер 15 с, 9:16 ========================
# Запуск: Blender 5.2 → вкладка Scripting → Open → Tools/Blender/trailer.py → Run Script.
# Скрипт сам строит сцену (стадион, свет, герои с анимациями, мяч, эффекты, камеры, титры)
# и сохраняет её в Builds/Trailer/trailer.blend. Рендер: Render → Render Animation (Ctrl+F12),
# видео появится в Builds/Trailer/arena_trailer.mp4.
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix, Quaternion, Euler

ROOT = os.environ.get('ARENA_ROOT') or r'C:\Users\user\ArenaStrikers'
PREVIEW = False   # True — быстрый черновик: половина разрешения и мало сэмплов (проверить ролик за несколько минут)
PREVIEW = PREVIEW or os.environ.get('ARENA_PREVIEW') == '1'
HEROES = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'Heroes')
FONT = os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts', 'RussoOne-Regular.ttf')
OUT = os.path.join(ROOT, 'Builds', 'Trailer')

FPS, F_END = 30, 450
FL, FWD = 24.0, 15.0                    # поле: длина по X, ширина по Y (м)
GOAL_W, GOAL_H, NET_D = 4.0, 0.62, 0.36
BALL_R = 0.13
HERO_H = 1.35                           # рост героя на поле (как в игре)
HOME = (0.180, 0.490, 1.000)            # синие #2e7dff
AWAY = (1.000, 0.310, 0.447)            # красные #ff4f72
ACCENT = (1.0, 0.70, 0.12)
G = 310                                 # кадр, когда мяч влетает в сетку
random.seed(7)

def srgb(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def hexc(h):
    h = h.lstrip('#'); return srgb(tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)))

HOME_L, AWAY_L, ACCENT_L = srgb(HOME), srgb(AWAY), srgb(ACCENT)

def node_of(nodes, typ):
    """Узел по типу: имена узлов зависят от языка Blender (в русском — «Принципиальный BSDF»)."""
    for n in nodes:
        if n.type == typ: return n
    raise KeyError(typ)

def bsdf_of(nodes):
    return node_of(nodes, 'BSDF_PRINCIPLED')

# ---------------------------------------------------------------- очистка и настройки
def clean():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights, bpy.data.curves,
                 bpy.data.armatures, bpy.data.actions, bpy.data.images, bpy.data.particles, bpy.data.node_groups):
        for d in list(coll):
            try: coll.remove(d)
            except Exception: pass
    for c in list(bpy.data.collections): bpy.data.collections.remove(c)

def setup_render(sc):
    sc.render.fps = FPS
    sc.frame_start, sc.frame_end = 1, F_END
    sc.render.resolution_x, sc.render.resolution_y = 1080, 1920
    sc.render.resolution_percentage = 50 if PREVIEW else 100
    sc.render.engine = 'BLENDER_EEVEE'
    ee = sc.eevee
    for k, v in (('taa_render_samples', 16 if PREVIEW else 96), ('use_raytracing', True), ('use_shadows', True),
                 ('shadow_resolution_scale', 1.0), ('volumetric_tile_size', '8'), ('volumetric_samples', 64),
                 ('use_volumetric_shadows', True), ('volumetric_end', 60.0), ('motion_blur_steps', 1)):
        try: setattr(ee, k, v)
        except Exception: pass
    sc.render.use_motion_blur = True
    sc.render.motion_blur_shutter = 0.45
    try:
        sc.view_settings.view_transform = 'AgX'
        sc.view_settings.look = 'AgX - Punchy'
    except Exception:
        pass
    sc.view_settings.exposure = -0.4
    os.makedirs(OUT, exist_ok=True)
    ims = sc.render.image_settings
    try:
        ims.media_type = 'VIDEO'
        ims.file_format = 'FFMPEG'
        sc.render.ffmpeg.format = 'MPEG4'
        sc.render.ffmpeg.codec = 'H264'
        sc.render.ffmpeg.constant_rate_factor = 'HIGH'
        sc.render.ffmpeg.ffmpeg_preset = 'GOOD'
        sc.render.filepath = os.path.join(OUT, 'arena_trailer_')
    except Exception:
        ims.file_format = 'PNG'
        sc.render.filepath = os.path.join(OUT, 'frames', 'f_')

def setup_compositor(sc):
    """Свечение ярких мест (огонь, неон, прожекторы) и лёгкая виньетка."""
    try:
        ng = bpy.data.node_groups.new('Trailer Comp', 'CompositorNodeTree')
        sc.compositing_node_group = ng
        ng.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')
        rl = ng.nodes.new('CompositorNodeRLayers')
        gl = ng.nodes.new('CompositorNodeGlare')
        for name, val in (('Type', 'Bloom'), ('Quality', 'High'), ('Threshold', 0.9), ('Strength', 0.55), ('Size', 0.6)):
            try: gl.inputs[name].default_value = val
            except Exception: pass
        out = ng.nodes.new('NodeGroupOutput')
        ng.links.new(rl.outputs['Image'], gl.inputs['Image'])
        ng.links.new(gl.outputs['Image'], out.inputs[0])
    except Exception as e:
        print('compositor:', e)

# ---------------------------------------------------------------- геометрия и материалы
def link(o, coll=None):
    (coll or bpy.context.scene.collection).objects.link(o); return o

def mat(name, color=(0.5, 0.5, 0.5), rough=0.5, metal=0.0, emit=None, emit_str=0.0, alpha=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = bsdf_of(m.node_tree.nodes)
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if emit is not None:
        b.inputs['Emission Color'].default_value = (*emit, 1)
        b.inputs['Emission Strength'].default_value = emit_str
    if alpha is not None:
        b.inputs['Alpha'].default_value = alpha
        try: m.surface_render_method = 'BLENDED'
        except Exception: pass
    return m

def box(name, center, size, m, coll=None, shadow=True):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); o.location = center; o.scale = size
    me.materials.append(m); link(o, coll)
    if not shadow:
        try: o.visible_shadow = False
        except Exception: pass
    return o

def cyl(name, p0, p1, r, m, seg=16, coll=None):
    d = Vector(p1) - Vector(p0)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length)
    for f in bm.faces: f.smooth = True
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    o.location = (Vector(p0) + Vector(p1)) / 2
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    me.materials.append(m); link(o, coll); return o

def ring(name, r_in, r_out, m, start=0.0, sweep=2 * math.pi, seg=96, z=0.006, loc=(0, 0, 0), coll=None):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    vs = []
    for i in range(seg + 1):
        a = start + sweep * i / seg
        vs.append((bm.verts.new((math.cos(a) * r_in, math.sin(a) * r_in, z)), bm.verts.new((math.cos(a) * r_out, math.sin(a) * r_out, z))))
    for i in range(seg):
        bm.faces.new((vs[i][0], vs[i][1], vs[i + 1][1], vs[i + 1][0]))
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); o.location = loc; me.materials.append(m); link(o, coll); return o

def plane(name, size, loc, m, rot=(0, 0, 0), coll=None):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); o.location = loc; o.scale = (size[0], size[1], 1); o.rotation_euler = rot
    me.materials.append(m); link(o, coll); return o

def text(name, body, size, m, loc=(0, 0, 0), rot=(0, 0, 0), align='CENTER', extrude=0.0, coll=None):
    cu = bpy.data.curves.new(name, 'FONT'); cu.body = body
    try: cu.font = bpy.data.fonts.load(FONT, check_existing=True)
    except Exception: pass
    cu.size = size; cu.align_x = align; cu.align_y = 'CENTER'; cu.extrude = extrude
    o = bpy.data.objects.new(name, cu); o.location = loc; o.rotation_euler = rot
    cu.materials.append(m); link(o, coll); return o

def key(o, path, frame, value=None, interp=None):
    if value is not None:
        setattr(o, path, value)
    o.keyframe_insert(path, frame=frame)
    if interp:
        set_interp(o, path, interp)

def fcurves_of(idblock):
    ad = idblock.animation_data
    if not ad or not ad.action: return []
    act = ad.action
    try:
        return list(act.fcurves)
    except Exception:
        out = []
        for layer in act.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    out += list(cb.fcurves)
        return out

def set_interp(idblock, path, interp):
    for fc in fcurves_of(idblock):
        if fc.data_path == path:
            for kp in fc.keyframe_points: kp.interpolation = interp

def ease_all(idblock):
    for fc in fcurves_of(idblock):
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'; kp.easing = 'AUTO'

# ---------------------------------------------------------------- поле
def build_pitch(coll):
    m = bpy.data.materials.new('Grass'); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; L = nt.links
    b = bsdf_of(N); b.inputs['Roughness'].default_value = 0.85
    tc = N.new('ShaderNodeTexCoord'); sep = N.new('ShaderNodeSeparateXYZ'); L.new(tc.outputs['Object'], sep.inputs[0])
    div = N.new('ShaderNodeMath'); div.operation = 'DIVIDE'; div.inputs[1].default_value = 2.0; L.new(sep.outputs['X'], div.inputs[0])
    fl = N.new('ShaderNodeMath'); fl.operation = 'FLOOR'; L.new(div.outputs[0], fl.inputs[0])
    mod = N.new('ShaderNodeMath'); mod.operation = 'FLOORED_MODULO'; mod.inputs[1].default_value = 2.0; L.new(fl.outputs[0], mod.inputs[0])
    mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    mix.inputs['A'].default_value = (*hexc('#2f7a40'), 1); mix.inputs['B'].default_value = (*hexc('#286b37'), 1)
    L.new(mod.outputs[0], mix.inputs['Factor'])
    nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 1.5; L.new(tc.outputs['Object'], nz.inputs['Vector'])
    mul = N.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 0.25
    L.new(mix.outputs['Result'], mul.inputs['A']); L.new(nz.outputs['Color'], mul.inputs['B'])
    L.new(mul.outputs['Result'], b.inputs['Base Color'])
    bump = N.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.15
    nz2 = N.new('ShaderNodeTexNoise'); nz2.inputs['Scale'].default_value = 180.0; L.new(tc.outputs['Object'], nz2.inputs['Vector'])
    L.new(nz2.outputs['Fac'], bump.inputs['Height']); L.new(bump.outputs['Normal'], b.inputs['Normal'])
    plane('Pitch', (FL + 1.6, FWD + 1.6), (0, 0, 0), m, coll=coll)
    plane('Gravel', (70, 60), (0, 0, -0.02), mat('Gravel', hexc('#161c28'), 0.95), coll=coll)
    line = mat('Lines', hexc('#e6f2ec'), 0.6, emit=hexc('#e6f2ec'), emit_str=0.35)
    W = 0.08
    def seg(x0, y0, x1, y1):
        c = ((x0 + x1) / 2, (y0 + y1) / 2, 0.004)
        sx = max(abs(x1 - x0), W); sy = max(abs(y1 - y0), W)
        box('Line', c, (sx, sy, 0.006), line, coll, shadow=False)
    hx, hy = FL / 2, FWD / 2
    seg(-hx, -hy, hx, -hy); seg(-hx, hy, hx, hy); seg(-hx, -hy, -hx, hy); seg(hx, -hy, hx, hy); seg(0, -hy, 0, hy)
    ring('CenterCircle', 2.5, 2.5 + W, line, coll=coll)
    for s in (-1, 1):
        x0 = s * hx; x1 = s * (hx - 4.5)
        seg(x0, -4.5, x1, -4.5); seg(x0, 4.5, x1, 4.5); seg(x1, -4.5, x1, 4.5)
        a0 = math.pi / 2 if s > 0 else -math.pi / 2
        ring('BoxArc', 2.2, 2.2 + W, line, start=a0, sweep=math.pi, loc=(x1, 0, 0), coll=coll)
    # мягкие пятна света прожекторов на газоне — «живее» травы
    return m

# ---------------------------------------------------------------- ворота с сеткой (сетка прогибается при голе)
def build_goal(sx, team_col, coll):
    x0 = sx * FL / 2
    post = mat('Post_%d' % sx, tuple(min(1, c * 0.85 + 0.15) for c in team_col), 0.35, emit=team_col, emit_str=0.6)
    for y in (-GOAL_W / 2, GOAL_W / 2):
        cyl('Post', (x0, y, 0), (x0, y, GOAL_H), 0.07, post, coll=coll)
    cyl('Bar', (x0, -GOAL_W / 2 - 0.07, GOAL_H), (x0, GOAL_W / 2 + 0.07, GOAL_H), 0.07, post, coll=coll)
    netm = mat('Net_%d' % sx, (0.92, 0.95, 1.0), 0.6, emit=tuple(0.5 + 0.5 * c for c in team_col), emit_str=0.1)
    xb = x0 + sx * NET_D
    def grid(name, corner, ua, va, nu, nv):
        me = bpy.data.meshes.new(name); bm = bmesh.new()
        vv = [[bm.verts.new(Vector(corner) + Vector(ua) * (i / nu) + Vector(va) * (j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
        for i in range(nu):
            for j in range(nv):
                bm.faces.new((vv[i][j], vv[i + 1][j], vv[i + 1][j + 1], vv[i][j + 1]))
        bm.to_mesh(me); bm.free()
        o = bpy.data.objects.new(name, me); me.materials.append(netm); link(o, coll)
        wf = o.modifiers.new('wire', 'WIREFRAME'); wf.thickness = 0.008; wf.use_replace = True
        return o
    back = grid('NetBack', (xb, -GOAL_W / 2, 0), (0, GOAL_W, 0), (0, 0, GOAL_H), 50, 8)
    grid('NetTop', (x0, -GOAL_W / 2, GOAL_H), (0, GOAL_W, 0), (sx * NET_D, 0, 0), 50, 5)
    grid('NetSideA', (x0, -GOAL_W / 2, 0), (sx * NET_D, 0, 0), (0, 0, GOAL_H), 5, 8)
    grid('NetSideB', (x0, GOAL_W / 2, 0), (sx * NET_D, 0, 0), (0, 0, GOAL_H), 5, 8)
    return back

def net_bulge(back, sx, cy, cz, frame):
    """Задняя сетка «мешком»: ключ формы, пружина после удара мяча."""
    back.shape_key_add(name='Basis')
    sk = back.shape_key_add(name='Bulge')
    for i, v in enumerate(back.data.vertices):
        y, z = v.co.y, v.co.z
        u = (y + GOAL_W / 2) / GOAL_W; w = z / GOAL_H
        g = math.exp(-((y - cy) ** 2) / (2 * 0.8 ** 2) - ((z - cz) ** 2) / (2 * 0.25 ** 2))
        d = 0.42 * g * math.sin(math.pi * u) * math.cos(math.pi * w * 0.5)
        sk.data[i].co = v.co + Vector((sx * d, 0, 0))
    for f, v in ((frame - 1, 0), (frame + 3, 1.0), (frame + 9, -0.25), (frame + 15, 0.45), (frame + 22, -0.1), (frame + 30, 0.15), (frame + 40, 0)):
        sk.value = v; sk.keyframe_insert('value', frame=f)

# ---------------------------------------------------------------- борта, реклама, табло
ADS = ['SPELLSTRIKE', 'ЖАР • БАГОР • МИГ', 'ВИХРЬ • ТЕНЬ • СТЕНА', 'ИГРАЙ С ДРУЗЬЯМИ']

def build_boards(coll):
    low = mat('BoardLow', hexc('#16353a'), 0.5)
    hx, hy = FL / 2, FWD / 2
    box('BoardL', (0, -hy - 0.05, 0.08), (FL + 0.2, 0.1, 0.16), low, coll)
    box('BoardR', (0, hy + 0.05, 0.08), (FL + 0.2, 0.1, 0.16), low, coll)
    for sx, c in ((-1, '#8a2c42'), (1, '#1d3f8a')):
        m = mat('BoardEnd', hexc(c), 0.5)
        for sy in (-1, 1):
            w = (FWD - GOAL_W) / 2
            box('BoardEnd', (sx * (hx + 0.05), sy * (GOAL_W / 2 + w / 2), 0.08), (0.1, w, 0.16), m, coll)
    panel = mat('AdPanel', hexc('#081216'), 0.4)
    strips, texts, goal_texts = [], [], []
    txt_m = mat('AdText', (1, 1, 1), 0.5, emit=(1, 1, 1), emit_str=2.2)
    goal_m = mat('AdGoal', AWAY_L, 0.5, emit=AWAY_L, emit_str=5.0)
    def ad_strip(cx, cy, length, along_x, face_sign, scoreboard=False):
        if along_x:
            box('Ad', (cx, cy, 0.35), (length, 0.08, 0.7), panel, coll)
        else:
            box('Ad', (cx, cy, 0.35), (0.08, length, 0.7), panel, coll)
        for z in (0.04, 0.66):
            sm = mat('AdStrip', (1, 1, 1), 0.3, emit=HOME_L, emit_str=4.0)
            strips.append(sm)
            if along_x: box('AdNeon', (cx, cy + face_sign * 0.045, z), (length, 0.015, 0.04), sm, coll, shadow=False)
            else: box('AdNeon', (cx + face_sign * 0.045, cy, z), (0.015, length, 0.04), sm, coll, shadow=False)
        n = max(1, round(length / 7.0))
        rot = (math.pi / 2, 0, math.pi if face_sign > 0 else 0) if along_x else (math.pi / 2, 0, math.pi / 2 if face_sign > 0 else -math.pi / 2)  # лицом к полю
        for i in range(n):
            a = (i + 0.5) / n
            if scoreboard and abs(a - 0.5) < 0.6 / n: continue
            p = (cx - length / 2 + length * a, cy + face_sign * 0.05, 0.35) if along_x else (cx + face_sign * 0.05, cy - length / 2 + length * a, 0.35)
            texts.append(text('AdText', ADS[(i + len(texts)) % len(ADS)], 0.32, txt_m, p, rot, coll=coll))
            goal_texts.append(text('AdGoal', 'ГОЛ!', 0.4, goal_m, p, rot, coll=coll))
        if scoreboard:
            p = (cx, cy + face_sign * 0.045, 0.35)
            box('ScoreScreen', (cx, cy + face_sign * 0.03, 0.35), (5.2, 0.02, 0.6), mat('Screen', (0.01, 0.015, 0.02), 0.2), coll)
            sb_m = mat('Score', (1, 1, 1), 0.5, emit=(1, 1, 1), emit_str=3.0)
            s0 = text('Score00', '0 : 0     2:51', 0.42, sb_m, (cx, cy + face_sign * 0.06, 0.35), rot, coll=coll)
            s1 = text('Score10', '0 : 1     2:49', 0.42, sb_m, (cx, cy + face_sign * 0.06, 0.35), rot, coll=coll)
            swap(s0, s1, G)
    D = 0.3 + 0.1
    ad_strip(0, -hy - D, FL + 1.4, True, 1, scoreboard=True)
    ad_strip(0, hy + D, FL + 1.4, True, -1)
    ad_strip(-hx - NET_D - D, 0, FWD + 1.4, False, 1)
    ad_strip(hx + NET_D + D, 0, FWD + 1.4, False, -1)
    for t, gt in zip(texts, goal_texts):    # «ГОЛ!» на бортах — только после гола
        vis_keys(t, [(1, True), (G, False), (G + 75, True)])
        vis_keys(gt, [(1, False), (G, True), (G + 75, False)])
    return strips

def vis_keys(o, keys):
    """Видимость по кадрам: [(кадр, видно?)]"""
    for f, v in keys:
        o.hide_render = not v; o.hide_viewport = not v
        o.keyframe_insert('hide_render', frame=f); o.keyframe_insert('hide_viewport', frame=f)

def swap(a, b, frame):
    """С кадра frame вместо a показывать b."""
    for o, vis in ((a, (False, True)), (b, (True, False))):
        o.hide_render = vis[0]; o.hide_viewport = vis[0]
        o.keyframe_insert('hide_render', frame=1); o.keyframe_insert('hide_viewport', frame=1)
        o.hide_render = vis[1]; o.hide_viewport = vis[1]
        o.keyframe_insert('hide_render', frame=frame); o.keyframe_insert('hide_viewport', frame=frame)

# ---------------------------------------------------------------- трибуны и болельщики
SKIN = ['#f1c9a5', '#d9a47a', '#b07850', '#7a4e30', '#e8b892']
HAIR = ['#1c1410', '#3b2a1e', '#6b4a2a', '#c9a46a', '#2a2a2a']

def build_stands(coll):
    rows = []
    riser = [mat('Riser0', hexc('#2b313d'), 0.9), mat('Riser1', hexc('#323947'), 0.9)]
    edge = mat('StandEdge', (0.6, 0.65, 0.75), 0.5, emit=(0.55, 0.6, 0.7), emit_str=0.8)
    neon = mat('StandNeon', (0.2, 0.8, 1.0), 0.3, emit=srgb((0.25, 0.85, 1.0)), emit_str=6.0)
    wall = mat('BackWall', hexc('#0e1428'), 0.8)
    crowd = bpy.data.materials.new('Crowd'); crowd.use_nodes = True
    nt = crowd.node_tree; bsdf = bsdf_of(nt.nodes)
    ca = nt.nodes.new('ShaderNodeVertexColor'); ca.layer_name = 'Col'
    nt.links.new(ca.outputs['Color'], bsdf.inputs['Base Color'])
    nt.links.new(ca.outputs['Color'], bsdf.inputs['Emission Color'])
    bsdf.inputs['Emission Strength'].default_value = 0.35; bsdf.inputs['Roughness'].default_value = 0.8
    hx, hy = FL / 2, FWD / 2
    # стороны: (центр линии, направление вдоль, наружу, длина, доля синих, рядов)
    sides = [((0, -hy), (1, 0), (0, -1), FL + 1.2, 0.5, 5), ((0, hy), (1, 0), (0, 1), FL + 1.2, 0.5, 3),
             ((-hx - NET_D, 0), (0, 1), (-1, 0), FWD, 0.25, 5), ((hx + NET_D, 0), (0, 1), (1, 0), FWD, 0.75, 5)]
    D0, DEP, RISE, H0 = 1.1, 0.8, 0.45, 0.85
    for (c, al, out, ln, home_share, nrows) in sides:
        c, al, out = Vector((*c, 0)), Vector((*al, 0)), Vector((*out, 0))
        for r in range(5):
            o = D0 + r * DEP; h = H0 + r * RISE
            p = c + out * (o + DEP / 2)
            size = Vector((abs(al.x) * ln + abs(out.x) * DEP, abs(al.y) * ln + abs(out.y) * DEP, h))
            box('Riser', p + Vector((0, 0, h / 2)), size, riser[r % 2], coll, shadow=False)
            pe = c + out * (o + 0.02)
            box('Edge', pe + Vector((0, 0, h + 0.005)), Vector((abs(al.x) * ln + abs(out.x) * 0.03, abs(al.y) * ln + abs(out.y) * 0.03, 0.01)), edge, coll, shadow=False)
            if r >= nrows: continue
            rows.append((crowd_row(c, al, out, ln, o + DEP * 0.55, h, home_share, crowd, coll, 1 - r * 0.07), home_share))
        top = H0 + 5 * RISE
        box('Neon', c + out * (D0 + 0.01) + Vector((0, 0, H0 + 0.02)), Vector((abs(al.x) * ln + abs(out.x) * 0.04, abs(al.y) * ln + abs(out.y) * 0.04, 0.04)), neon, coll, shadow=False)
        wb = D0 + 5 * DEP + 0.1
        box('Wall', c + out * wb + Vector((0, 0, (top + 1.4) / 2)), Vector((abs(al.x) * ln + abs(out.x) * 0.2, abs(al.y) * ln + abs(out.y) * 0.2, top + 1.4)), wall, coll, shadow=False)
        lamp = mat('Lamp', (1, 0.9, 0.8), 0.3, emit=(1, 0.85, 0.65), emit_str=12)
        for i in range(int(ln // 3)):
            a = -ln / 2 + 1.5 + i * 3
            box('Lamp', c + al * a + out * (wb - 0.15) + Vector((0, 0, top + 1.45)), (0.28, 0.28, 0.12), lamp, coll, shadow=False)
    return rows

def crowd_row(c, al, out, ln, off, base_h, home_share, m, coll, dim):
    me = bpy.data.meshes.new('CrowdRow'); bm = bmesh.new()
    col = bm.loops.layers.float_color.new('Col')
    face = -out
    side = al
    def cuboid(ctr, sx, sy, sz, rgb):
        # оси: side (вдоль ряда), face (к полю), up
        up = Vector((0, 0, 1))
        corners = []
        for dz in (-1, 1):
            for df in (-1, 1):
                for ds in (-1, 1):
                    corners.append(bm.verts.new(ctr + side * (ds * sx / 2) + face * (df * sy / 2) + up * (dz * sz / 2)))
        idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        for k, q in enumerate(idx):
            f = bm.faces.new([corners[i] for i in q])
            shade = (0.75, 1.0, 0.7, 0.85, 0.8, 0.8)[k] * dim
            for l in f.loops: l[col] = (rgb[0] * shade, rgb[1] * shade, rgb[2] * shade, 1)
    a = -ln / 2 + 0.3
    while a < ln / 2 - 0.3:
        a += 0.42 + random.random() * 0.16
        if random.random() < 0.08: continue
        home = random.random() < home_share
        shirt = HOME_L if home else AWAY_L
        if random.random() < 0.15: shirt = hexc(random.choice(['#e8eef0', '#3d8a5a', '#222a30']))
        p = c + al * a + out * (off + (random.random() - 0.5) * 0.12) + Vector((0, 0, base_h))
        hgt = 0.9 + random.random() * 0.12
        cuboid(p + Vector((0, 0, 0.18)), 0.2, 0.16, 0.36, hexc('#26324a'))         # ноги (сидит/стоит)
        cuboid(p + Vector((0, 0, 0.52)), 0.3, 0.2, 0.36, shirt)                  # туловище
        cuboid(p + Vector((0, 0, 0.82)), 0.18, 0.18, 0.2, hexc(random.choice(SKIN)))  # голова
        cuboid(p + Vector((0, 0, 0.93)), 0.19, 0.19, 0.05, hexc(random.choice(HAIR)))
        if random.random() < 0.35:                                                # руки вверх
            for s in (-1, 1):
                cuboid(p + side * (s * 0.17) + Vector((0, 0, 0.88)), 0.07, 0.07, 0.34, shirt)
        else:
            for s in (-1, 1):
                cuboid(p + side * (s * 0.18) + Vector((0, 0, 0.5)), 0.07, 0.07, 0.3, shirt)
    bm.to_mesh(me); bm.free()
    me.materials.append(m)
    o = bpy.data.objects.new('CrowdRow', me); link(o, coll)
    try: o.visible_shadow = False
    except Exception: pass
    return o

def animate_crowd(rows):
    for o, home_share in rows:
        ph = random.random() * 6.28
        base = o.location.z
        for f in range(1, F_END + 1, 5):
            jump = 0.0
            if G <= f <= G + 100:
                k = 1 - (f - G) / 100
                jump = abs(math.sin(f * 0.55 + ph)) * (0.06 + 0.16 * (1 - home_share)) * k
            o.location.z = base + math.sin(f * 0.25 + ph) * 0.012 + jump
            o.keyframe_insert('location', index=2, frame=f)

# ---------------------------------------------------------------- прожекторы и свет
def build_lights(coll, strips):
    sc = bpy.context.scene
    world = bpy.data.worlds.new('Night'); sc.world = world; world.use_nodes = True
    wn = world.node_tree.nodes; wl = world.node_tree.links
    bg = node_of(wn, 'BACKGROUND'); bg.inputs['Color'].default_value = (*hexc('#0a1026'), 1); bg.inputs['Strength'].default_value = 0.6
    if not os.environ.get('ARENA_NO_VOLUME'):
        vol = wn.new('ShaderNodeVolumePrincipled'); vol.inputs['Density'].default_value = 0.006
        vol.inputs['Color'].default_value = (0.7, 0.75, 0.9, 1)
        wl.new(vol.outputs[0], node_of(wn, 'OUTPUT_WORLD').inputs['Volume'])
    moon = bpy.data.lights.new('Moon', 'SUN'); moon.energy = 0.6; moon.color = hexc('#b8c8ff'); moon.angle = 0.05
    mo = link(bpy.data.objects.new('Moon', moon), coll); mo.rotation_euler = (math.radians(50), 0, math.radians(-30))
    pole = mat('Mast', hexc('#8a96a0'), 0.4, 0.6)
    frame_m = mat('MastHead', hexc('#1b2328'), 0.5)
    spots, heads = [], []
    hx, hy = FL / 2 + NET_D + 0.95, FWD / 2 + 0.95
    for sx in (-1, 1):
        for sy in (-1, 1):
            warm = sx > 0
            lc = hexc('#ffd9a6') if warm else hexc('#d2e4ff')
            base = Vector((sx * hx, sy * hy, 0)); top = base + Vector((0, 0, 14))
            cyl('Mast', base, top, 0.09, pole, coll=coll)
            tgt = Vector((sx * FL * 0.08, sy * FWD * 0.08, 0))
            d = (tgt - top).normalized()
            hm = mat('FloodPanel', (1, 1, 1), 0.3, emit=lc, emit_str=40)
            heads.append(hm)
            h = box('FloodHead', top + Vector((0, 0, 0.3)), (1.5, 0.3, 1.1), frame_m, coll, shadow=False)
            h.rotation_mode = 'QUATERNION'; h.rotation_quaternion = Vector((0, 1, 0)).rotation_difference(d)
            pnl = box('FloodLamp', top + Vector((0, 0, 0.3)) + d * 0.17, (1.35, 0.02, 0.95), hm, coll, shadow=False)
            pnl.rotation_mode = 'QUATERNION'; pnl.rotation_quaternion = h.rotation_quaternion
            L = bpy.data.lights.new('Flood', 'SPOT'); L.energy = 26000; L.color = lc
            L.spot_size = math.radians(95); L.spot_blend = 0.9; L.shadow_soft_size = 1.2
            try: L.use_shadow = True
            except Exception: pass
            lo = link(bpy.data.objects.new('Flood', L), coll); lo.location = top + Vector((0, 0, 0.3)) + d * 0.3
            lo.rotation_mode = 'QUATERNION'; lo.rotation_quaternion = Vector((0, 0, -1)).rotation_difference(d)
            spots.append((L, lc))
    # заполняющий мягкий свет сверху — герои читаются в тени
    fill = bpy.data.lights.new('Fill', 'AREA'); fill.energy = 1200; fill.size = 20; fill.color = hexc('#c9d6ff')
    fo = link(bpy.data.objects.new('Fill', fill), coll); fo.location = (0, 0, 16)
    # стробоскоп после гола в цвет забившей команды (красные), остальное время — неон переливается
    for i, (L, lc) in enumerate(spots):
        L.color = lc; L.energy = 26000; L.keyframe_insert('color', frame=G - 1); L.keyframe_insert('energy', frame=G - 1)
        for k in range(0, 66, 3):
            on = (k // 3 + i) % 2 == 0
            L.color = AWAY_L if on else lc; L.energy = 70000 if on else 16000
            L.keyframe_insert('color', frame=G + k); L.keyframe_insert('energy', frame=G + k)
        L.color = lc; L.energy = 26000; L.keyframe_insert('color', frame=G + 70); L.keyframe_insert('energy', frame=G + 70)
    for i, sm in enumerate(strips):
        n = bsdf_of(sm.node_tree.nodes).inputs['Emission Color']
        for f in range(1, F_END + 1, 30):
            if G - 2 <= f <= G + 75: continue
            c = HOME_L if ((f // 30) + i) % 2 == 0 else AWAY_L
            n.default_value = (*c, 1); n.keyframe_insert('default_value', frame=f)
        for k in range(0, 72, 4):
            n.default_value = (*(AWAY_L if (k // 4) % 2 == 0 else (1, 1, 1)), 1); n.keyframe_insert('default_value', frame=G + k)
    return spots

# ---------------------------------------------------------------- герои
FOLDERS = {'Jar': 0, 'Bagor': 1, 'Mig': 2, 'Vihr': 3, 'Ten': 4, 'Stena': 5}
KIT_HUE_MIN = [0, 0, 0.55, 0, 0, 0.64]
GLOW_RULE = [(0.045, 0.19, 0.3, 0.72), None, None, None, (0.44, 0.58, 0.2, 0.75), (0.48, 0.555, 0.12, 0.8)]

def hero_material(folder, team_col):
    i = FOLDERS[folder]
    m = bpy.data.materials.new('Hero_' + folder); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; Ln = nt.links
    b = bsdf_of(N); b.inputs['Roughness'].default_value = 0.55
    try: b.inputs['Specular IOR Level'].default_value = 0.35
    except Exception: pass
    tex = N.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(os.path.join(HEROES, folder, 'albedo.png'), check_existing=True)
    hsv = N.new('ShaderNodeSeparateColor'); hsv.mode = 'HSV'; Ln.new(tex.outputs['Color'], hsv.inputs[0])
    H, S, V = hsv.outputs[0], hsv.outputs[1], hsv.outputs[2]
    def mr(src, a, b_):  # clamp((src-a)/(b-a))
        n = N.new('ShaderNodeMapRange'); n.clamp = True
        n.inputs['From Min'].default_value = a; n.inputs['From Max'].default_value = b_
        Ln.new(src, n.inputs['Value']); return n.outputs['Result']
    def mul(x, y):
        n = N.new('ShaderNodeMath'); n.operation = 'MULTIPLY'; Ln.new(x, n.inputs[0]); Ln.new(y, n.inputs[1]); return n.outputs[0]
    # форма в цвет команды: белые/серые пиксели (как в игре, HeroModel.MakeMat)
    k = mul(mr(S, 0.2, 0.12), mr(V, 0.4, 0.6))
    if KIT_HUE_MIN[i] > 0:
        k = mul(k, mr(H, KIT_HUE_MIN[i], KIT_HUE_MIN[i] + 0.02))
    team = N.new('ShaderNodeMix'); team.data_type = 'RGBA'; team.blend_type = 'MULTIPLY'; team.inputs['Factor'].default_value = 1
    team.inputs['B'].default_value = (*team_col, 1)
    vv = N.new('ShaderNodeMath'); vv.operation = 'MULTIPLY'; vv.inputs[1].default_value = 1.08; Ln.new(V, vv.inputs[0])
    vc = N.new('ShaderNodeCombineColor'); Ln.new(vv.outputs[0], vc.inputs[0]); Ln.new(vv.outputs[0], vc.inputs[1]); Ln.new(vv.outputs[0], vc.inputs[2])
    Ln.new(vc.outputs[0], team.inputs['A'])
    mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    Ln.new(k, mix.inputs['Factor']); Ln.new(tex.outputs['Color'], mix.inputs['A']); Ln.new(team.outputs['Result'], mix.inputs['B'])
    Ln.new(mix.outputs['Result'], b.inputs['Base Color'])
    npath = os.path.join(HEROES, folder, 'normal.png')
    if os.path.exists(npath):
        nimg = N.new('ShaderNodeTexImage'); nimg.image = bpy.data.images.load(npath, check_existing=True)
        nimg.image.colorspace_settings.name = 'Non-Color'
        nm = N.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value = 0.7
        Ln.new(nimg.outputs['Color'], nm.inputs['Color']); Ln.new(nm.outputs['Normal'], b.inputs['Normal'])
    glow = None
    r = GLOW_RULE[i]
    if r:
        w = mul(mul(mr(H, r[0], r[0] + 0.02), mr(H, r[1], r[1] - 0.03)), mul(mr(S, r[2], r[2] + 0.15), mr(V, r[3], r[3] + 0.12)))
        Ln.new(tex.outputs['Color'], b.inputs['Emission Color'])
        em = N.new('ShaderNodeMath'); em.operation = 'MULTIPLY'; em.inputs[1].default_value = 3.0
        Ln.new(w, em.inputs[0]); Ln.new(em.outputs[0], b.inputs['Emission Strength'])
        glow = em.inputs[1]
    return m, glow

class Hero:
    def __init__(self, folder, team_col, coll):
        objs = import_fbx(os.path.join(HEROES, folder, 'model.fbx'))
        for o in objs:
            for c in o.users_collection: c.objects.unlink(o)
            coll.objects.link(o)
        self.arm = next(o for o in objs if o.type == 'ARMATURE')
        self.mesh = next(o for o in objs if o.type == 'MESH')
        self.folder = folder
        m, self.glow = hero_material(folder, team_col)
        self.mesh.data.materials.clear(); self.mesh.data.materials.append(m)
        for p in self.mesh.data.polygons: p.use_smooth = True
        h = max(0.01, self.mesh.dimensions.z)
        self.scale = HERO_H / h
        self.arm.rotation_mode = 'XYZ'

    def place(self, frame, x, y, face_deg, interp='CONSTANT'):
        """Положение на поле (м) и куда смотрит (градусы: 0 — к +X)."""
        self.arm.location = (x, y, 0)
        self.arm.rotation_euler = (0, 0, math.radians(face_deg + 90))
        self.arm.keyframe_insert('location', frame=frame); self.arm.keyframe_insert('rotation_euler', frame=frame)
        set_interp(self.arm, 'location', interp); set_interp(self.arm, 'rotation_euler', interp)

def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]

# ---------------- перенос анимаций Mixamo (Y Bot) на скелеты героев ----------------
# Позы покоя разные (у героев руки опущены ~45°, у Y Bot — T-поза), поэтому кости переносятся по направлениям:
# поворот кости героя = поворот кости Y Bot × (поза покоя Y Bot)⁻¹ × (поза покоя героя, повёрнутая к направлению Y Bot).
import bpy, math
from mathutils import Matrix, Quaternion, Vector

def _rot(m):
    return m.to_3x3().normalized().to_quaternion()

def _import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]

class Clip:
    """Анимация, заранее «просэмплированная» по кадрам: мировые повороты костей и положение бёдер."""
    def __init__(self, path, name):
        self.name = name
        objs = _import_fbx(path)
        arm = next(o for o in objs if o.type == 'ARMATURE')
        act = arm.animation_data.action
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        self.fps = 30.0
        self.f0 = f0
        self.n = f1 - f0 + 1
        self.length = (self.n - 1) / self.fps
        mw = arm.matrix_world
        self.bones = [b.name for b in arm.data.bones if b.name.startswith('mixamorig:')]
        self.rest = {n: _rot(mw @ arm.data.bones[n].matrix_local) for n in self.bones}
        self.rest_dir = {n: (mw.to_3x3().normalized() @ arm.data.bones[n].matrix_local.to_3x3()).col[1].normalized() for n in self.bones}
        hb = arm.data.bones['mixamorig:Hips']
        self.hips_rest = (mw @ hb.matrix_local).translation.copy()
        self.rot = []   # [кадр] -> {кость: Quaternion}
        self.hips = []  # [кадр] -> Vector
        self.rfoot = [] # для поиска момента удара
        sc = bpy.context.scene
        for f in range(f0, f1 + 1):
            sc.frame_set(f)
            d = {}
            for n in self.bones:
                d[n] = _rot(mw @ arm.pose.bones[n].matrix)
            self.rot.append(d)
            self.hips.append((mw @ arm.pose.bones['mixamorig:Hips'].matrix).translation.copy())
            rf = arm.pose.bones.get('mixamorig:RightFoot')
            self.rfoot.append((mw @ rf.matrix).translation.copy() if rf else Vector())
        for o in objs:
            bpy.data.objects.remove(o, do_unlink=True)
        if act.users == 0:
            bpy.data.actions.remove(act)

    def sample(self, t, loop):
        """Поза в момент t (с): {кость: Quaternion}, бёдра."""
        x = t * self.fps
        if loop and self.n > 1:
            x = x % (self.n - 1)
        x = max(0.0, min(self.n - 1.0, x))
        i = int(math.floor(x)); j = min(i + 1, self.n - 1); a = x - i
        ra, rb = self.rot[i], self.rot[j]
        rot = {n: ra[n].slerp(rb[n], a) for n in self.bones}
        hips = self.hips[i].lerp(self.hips[j], a)
        return rot, hips

    def contact_time(self):
        """Момент удара: наибольшая скорость правой стопы."""
        best, bt = 0, 0
        for i in range(1, self.n):
            v = (self.rfoot[i] - self.rfoot[i - 1]).length
            if v > best:
                best, bt = v, i / self.fps
        return bt


class Rig:
    """Скелет героя: перенос поз и запись ключей."""
    def __init__(self, arm, any_clip):
        self.arm = arm
        bones = list(arm.data.bones)
        depth = lambda b: 0 if b.parent is None else 1 + depth(b.parent)
        self.order = sorted(bones, key=depth)
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        # поза покоя героя, развёрнутая к направлению костей Y Bot (Т-поза)
        self.restT = {}
        for b in bones:
            hr = b.matrix_local.to_3x3().normalized().to_quaternion()
            if b.name in any_clip.rest_dir:
                hd = b.matrix_local.to_3x3().col[1].normalized()
                align = hd.rotation_difference(any_clip.rest_dir[b.name])
                self.restT[b.name] = align @ hr
        hb = arm.data.bones['mixamorig:Hips']
        self.hips_rest = hb.matrix_local.translation.copy()
        self.k = self.hips_rest.z / max(0.01, any_clip.hips_rest.z)
        self.prev = {}
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'

    def targets(self, clip, rot):
        """Повороты костей героя (в пространстве скелета) для позы клипа."""
        out = {}
        for n, q in rot.items():
            if n in self.restT:
                out[n] = q @ clip.rest[n].inverted() @ self.restT[n]
        return out

    def key(self, frame, T, hips_pos, extra=None):
        """Записать позу: T — {кость: поворот в пространстве скелета}, hips_pos — положение бёдер."""
        P = {}
        for b in self.order:
            n = b.name
            pb = self.arm.pose.bones[n]
            if b.parent is None:
                q = T.get(n, self.rest[n].to_quaternion())
                Pm = Matrix.Translation(hips_pos) @ q.to_matrix().to_4x4()
                basis = self.rest[n].inverted() @ Pm
                loc = basis.translation
                qb = basis.to_quaternion()
                pb.location = loc
                pb.keyframe_insert('location', frame=frame)
            else:
                L = self.rest[b.parent.name].inverted() @ self.rest[n]
                base = P[b.parent.name] @ L
                if n in T:
                    qb = _rot(base).inverted() @ T[n]
                else:
                    qb = Quaternion()
                    if extra and n in extra:
                        qb = extra[n]
                Pm = base @ qb.to_matrix().to_4x4()
            P[n] = Pm if b.parent is not None else Pm
            pv = self.prev.get(n)
            if pv is not None and pv.dot(qb) < 0:
                qb = -qb
            self.prev[n] = qb.copy()
            pb.rotation_quaternion = qb
            pb.keyframe_insert('rotation_quaternion', frame=frame)

    def bake(self, clips, segments, f_start, f_end, xfade=6, sway=True):
        """segments: [(кадр_начала, имя_клипа, время_старта_с, скорость, по_кругу)] — с кадра начала до следующего сегмента.
        Между сегментами — плавный переход xfade кадров."""
        segs = sorted(segments, key=lambda s: s[0])
        def pose(seg, f):
            fs, cn, t0, sp, loop = seg
            c = clips[cn]
            t = t0 + (f - fs) / 30.0 * sp
            rot, hips = c.sample(t, loop)
            T = self.targets(c, rot)
            dh = (hips - c.hips_rest) * self.k
            dh.x = 0.0; dh.y = 0.0          # героя по полю двигает сцена, клип — только вверх-вниз (как в игре)
            hp = self.hips_rest + dh
            return T, hp
        for f in range(f_start, f_end + 1):
            idx = max(i for i, s in enumerate(segs) if s[0] <= f) if f >= segs[0][0] else 0
            T, hp = pose(segs[idx], f)
            if idx > 0 and f - segs[idx][0] < xfade:
                w = (f - segs[idx][0] + 1) / (xfade + 1)
                w = w * w * (3 - 2 * w)
                T0, hp0 = pose(segs[idx - 1], f)
                T = {n: T0[n].slerp(T[n], w) if n in T0 else T[n] for n in T}
                hp = hp0.lerp(hp, w)
            extra = None
            if sway:   # хвост и шарф: лёгкое покачивание (в игре — «ветер» кодом)
                extra = {}
                for b in self.order:
                    if b.name.startswith('Tail') or b.name.startswith('Scarf'):
                        i = int(''.join(ch for ch in b.name if ch.isdigit()) or 1)
                        a = math.sin(f * 0.21 - i * 0.7) * 0.18 + math.sin(f * 0.13 + i) * 0.08
                        extra[b.name] = Quaternion((0, 0, 1), a) @ Quaternion((1, 0, 0), math.sin(f * 0.17 - i * 0.5) * 0.08)
            self.key(f, T, hp, extra)

# ---------------------------------------------------------------- ключи с нужной интерполяцией
def key_interp(idblock, path, frame, interp):
    for fc in fcurves_of(idblock):
        if fc.data_path == path:
            for kp in fc.keyframe_points:
                if abs(kp.co[0] - frame) < 0.01: kp.interpolation = interp

def place(h, frame, x, y, face_deg, interp='CONSTANT'):
    h.arm.location = (x, y, 0)
    h.arm.rotation_euler = (0, 0, math.radians(face_deg + 90))
    h.arm.keyframe_insert('location', frame=frame); h.arm.keyframe_insert('rotation_euler', frame=frame)
    key_interp(h.arm, 'location', frame, interp); key_interp(h.arm, 'rotation_euler', frame, interp)

# ---------------------------------------------------------------- мяч, огонь, конфетти
def build_ball(coll):
    me = bpy.data.meshes.new('Ball'); bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=BALL_R)
    for f in bm.faces: f.smooth = True
    bm.to_mesh(me); bm.free()
    ball = link(bpy.data.objects.new('Ball', me), coll)
    m = bpy.data.materials.new('Ball'); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; L = nt.links; b = bsdf_of(N)
    b.inputs['Roughness'].default_value = 0.35
    vor = N.new('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 3.2
    tc = N.new('ShaderNodeTexCoord'); L.new(tc.outputs['Object'], vor.inputs['Vector'])
    ramp = N.new('ShaderNodeValToRGB'); L.new(vor.outputs['Distance'], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].position = 0.18; ramp.color_ramp.elements[0].color = (0.03, 0.03, 0.04, 1)
    ramp.color_ramp.elements[1].position = 0.2; ramp.color_ramp.elements[1].color = (0.92, 0.93, 0.95, 1)
    L.new(ramp.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Emission Color'].default_value = (1.0, 0.35, 0.05, 1)
    em = b.inputs['Emission Strength']
    me.materials.append(m)
    return ball, em

def hide_collection(c):
    """Заготовки для частиц: в кадре не видны (коллекция выключена), но частицы их копируют."""
    def find(lc):
        if lc.collection == c: return lc
        for ch in lc.children:
            r = find(ch)
            if r: return r
    lc = find(bpy.context.view_layer.layer_collection)
    if lc: lc.exclude = True

def fire_trail(ball, f0, f1, coll):
    """Огненный след мяча: частицы-огоньки, всплывают и гаснут."""
    flame_m = mat('Flame', (1, 0.5, 0.1), 0.5, emit=(1.0, 0.42, 0.06), emit_str=18)
    core_m = mat('FlameCore', (1, 0.9, 0.5), 0.5, emit=(1.0, 0.85, 0.35), emit_str=25)
    fc = bpy.data.collections.new('FlameBits'); bpy.context.scene.collection.children.link(fc)
    for i, mm in enumerate((flame_m, flame_m, core_m)):
        me = bpy.data.meshes.new('Flame'); bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0); bm.to_mesh(me); bm.free()
        me.materials.append(mm); o = bpy.data.objects.new('Flame%d' % i, me); fc.objects.link(o)
    hide_collection(fc)
    ps_mod = ball.modifiers.new('fire', 'PARTICLE_SYSTEM')
    ps = ball.particle_systems[-1].settings
    ps.count = 900; ps.frame_start = f0; ps.frame_end = f1; ps.lifetime = 10; ps.lifetime_random = 0.5
    ps.emit_from = 'VOLUME'; ps.normal_factor = 0.0; ps.factor_random = 0.4
    ps.render_type = 'COLLECTION'; ps.instance_collection = fc; ps.use_collection_pick_random = True
    ps.particle_size = 0.055; ps.size_random = 0.6
    ps.effector_weights.gravity = -0.15
    ps.use_rotations = True
    try: ball.show_instancer_for_render = True
    except Exception: pass
    return ps

def confetti(coll):
    cols = [AWAY_L, AWAY_L, (1, 1, 1), ACCENT_L, srgb((1.0, 0.65, 0.75))]
    cc = bpy.data.collections.new('ConfettiBits'); bpy.context.scene.collection.children.link(cc)
    for i, c in enumerate(cols):
        m = mat('Conf%d' % i, c, 0.4, emit=c, emit_str=1.2)
        me = bpy.data.meshes.new('Conf'); bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5); bm.to_mesh(me); bm.free()
        me.materials.append(m); o = bpy.data.objects.new('Conf%d' % i, me); o.scale = (1, 0.6, 1); cc.objects.link(o)
    hide_collection(cc)
    for y in (-GOAL_W / 2, GOAL_W / 2):
        em = plane('ConfettiGun', (0.25, 0.25), (FL / 2 + 0.05, y, GOAL_H + 0.1), mat('Hidden', (0, 0, 0), 1), rot=(0, math.radians(-35), 0), coll=coll)
        em.modifiers.new('conf', 'PARTICLE_SYSTEM')
        ps = em.particle_systems[-1].settings
        ps.count = 900; ps.frame_start = G; ps.frame_end = G + 8; ps.lifetime = 140; ps.lifetime_random = 0.3
        ps.emit_from = 'FACE'; ps.normal_factor = 7.0; ps.factor_random = 2.5
        ps.render_type = 'COLLECTION'; ps.instance_collection = cc; ps.use_collection_pick_random = True
        ps.particle_size = 0.09; ps.size_random = 0.5
        ps.effector_weights.gravity = 0.3; ps.damping = 0.05   # гасит скорость: взлетают ~4 м и плавно осыпаются
        ps.use_rotations = True; ps.rotation_mode = 'NONE'; ps.rotation_factor_random = 1.0
        ps.angular_velocity_mode = 'RAND'; ps.angular_velocity_factor = 8.0
        em.show_instancer_for_render = False

# ---------------------------------------------------------------- камеры (смена камер — по маркерам)
def camera(name, lens=35, fstop=2.8):
    cd = bpy.data.cameras.new(name); cd.lens = lens; cd.sensor_fit = 'VERTICAL'; cd.sensor_height = 36
    cd.clip_start = 0.05; cd.clip_end = 200
    cd.dof.use_dof = True; cd.dof.aperture_fstop = fstop
    cam = link(bpy.data.objects.new(name, cd))
    tgt = link(bpy.data.objects.new(name + '_target', None)); tgt.empty_display_size = 0.2
    c = cam.constraints.new('TRACK_TO'); c.target = tgt; c.track_axis = 'TRACK_NEGATIVE_Z'; c.up_axis = 'UP_Y'
    cd.dof.focus_object = tgt
    return cam, tgt

def cam_keys(cam, tgt, keys):
    """keys: [(кадр, позиция камеры, точка взгляда, фокусное)]"""
    for f, p, t, lens in keys:
        cam.location = p; cam.keyframe_insert('location', frame=f)
        tgt.location = t; tgt.keyframe_insert('location', frame=f)
        if lens:
            cam.data.lens = lens; cam.data.keyframe_insert('lens', frame=f)

def cut(frame, cam):
    m = bpy.context.scene.timeline_markers.new('shot_%d' % frame, frame=frame); m.camera = cam

# ---------------------------------------------------------------- титры
def title_card(cam, f0):
    """Логотип SpellStrike (Tools/Blender/logo.py) с подписью «ИГРАЙ С ДРУЗЬЯМИ»."""
    import sys
    sys.path.insert(0, os.path.join(ROOT, 'Tools', 'Blender')); sys.modules.pop('logo', None)
    import logo
    logo.spellstrike(cam, f0, ROOT, tagline='ИГРАЙ С ДРУЗЬЯМИ', size=0.4)

# ================================================================ сборка ролика
CLIPS = ['idle_show', 'idle', 'run', 'run_ball', 'kick_strong', 'cel_victory', 'cel_fist_pump', 'cel_taunt', 'cel_cheering', 'sad']
LINEUP = {'Ten': -3.0, 'Vihr': -1.8, 'Jar': -0.6, 'Bagor': 0.6, 'Mig': 1.8, 'Stena': 3.0}
TEAM = {'Jar': AWAY_L, 'Ten': AWAY_L, 'Vihr': AWAY_L, 'Bagor': HOME_L, 'Mig': HOME_L, 'Stena': HOME_L}
KICK_F0, KICK_T0, KICK_SPEED = 262, 0.25, 0.45

def run_path(h, f0, f1, p0, p1, face=None):
    ang = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0])) if face is None else face
    place(h, f0, p0[0], p0[1], ang, 'LINEAR')
    place(h, f1, p1[0], p1[1], ang, 'CONSTANT')

def main():
    clean()
    sc = bpy.context.scene
    print('[trailer] анимации...')
    clips = {n: Clip(os.path.join(HEROES, 'Anim', n + '.fbx'), n) for n in CLIPS}
    contact = KICK_F0 + (clips['kick_strong'].contact_time() - KICK_T0) / KICK_SPEED * 30
    contact = int(round(contact))
    setup_render(sc)
    setup_compositor(sc)
    st = bpy.data.collections.new('Stadium'); sc.collection.children.link(st)
    hc = bpy.data.collections.new('Heroes'); sc.collection.children.link(hc)

    print('[trailer] герои...')
    H = {f: Hero(f, TEAM[f], hc) for f in FOLDERS}
    segs = {
        'Jar':   [(1, 'idle_show', 0.0, 1, True), (211, 'run_ball', 0, 1.2, True), (KICK_F0, 'kick_strong', KICK_T0, KICK_SPEED, False), (contact + 22, 'cel_victory', 0.4, 1.0, False)],
        'Bagor': [(1, 'idle_show', 1.3, 1, True), (135, 'cel_fist_pump', 0.3, 1.0, False), (173, 'idle_show', 2.0, 1, True), (211, 'run', 0, 1.0, True), (296, 'sad', 0.2, 1, False)],
        'Ten':   [(1, 'idle_show', 2.1, 1, True), (173, 'cel_taunt', 0.1, 1, False), (211, 'run', 0.3, 1, True), (298, 'cel_cheering', 0, 1, True)],
        'Stena': [(1, 'idle_show', 0.7, 1, True), (211, 'idle', 0, 1, True), (G + 2, 'sad', 0.3, 1, False)],
        'Mig':   [(1, 'idle_show', 3.0, 1, True), (211, 'run', 0.15, 1, True), (298, 'sad', 0, 1, False)],
        'Vihr':  [(1, 'idle_show', 4.0, 1, True), (211, 'run', 0.45, 1, True), (300, 'cel_cheering', 0.5, 1, True)],
    }
    for f, h in H.items():
        rig = Rig(h.arm, clips['idle_show'])
        rig.bake(clips, segs[f], 1, F_END)
        h.arm.scale = (h.scale,) * 3
    # построение: все в линию у центра, лицом к камере (+Y)
    for f, h in H.items():
        place(h, 1, LINEUP[f], 0.0, 90)
    J, B, T, S, M, V = H['Jar'], H['Bagor'], H['Ten'], H['Stena'], H['Mig'], H['Vihr']
    jar0, jar1 = (3.6, 0.9), (8.15, 0.35)
    run_path(J, 211, 262, jar0, jar1)
    place(J, 330, jar1[0], jar1[1], 115)                     # празднует лицом к камере
    run_path(B, 211, 296, (1.2, 2.6), (6.6, 1.5))
    run_path(T, 211, 296, (1.6, -2.4), (7.0, -2.0))
    place(T, 297, 7.0, -2.0, 60)
    run_path(M, 211, 296, (6.0, -4.2), (9.6, -2.6))
    run_path(V, 211, 296, (0.5, 4.5), (5.6, 3.6))
    place(V, 297, 5.6, 3.6, -20)
    place(S, 211, 11.5, -1.05, 165)
    # свечение Жара: вспыхивает на крупном плане и при огненном ударе
    if J.glow is not None:
        for fr, v in ((1, 3.0), (100, 3.0), (112, 10.0), (130, 5.0), (140, 3.0), (contact - 20, 3.0), (contact - 4, 11.0), (contact + 30, 6.0), (360, 3.0)):
            J.glow.default_value = v; J.glow.keyframe_insert('default_value', frame=fr)

    print('[trailer] стадион...')
    build_pitch(st)
    back_home = build_goal(1, HOME_L, st)
    build_goal(-1, AWAY_L, st)
    strips = build_boards(st)
    rows = build_stands(st)
    animate_crowd(rows)
    build_lights(st, strips)

    print('[trailer] мяч и эффекты...')
    ball, ball_em = build_ball(st)
    d = Vector((jar1[0] - jar0[0], jar1[1] - jar0[1], 0)).normalized()
    spin = 0.0
    for fr in range(1, F_END + 1):
        if fr < 211:
            p = Vector((0.0, 1.1, BALL_R)); spin = 0
        elif fr <= 262:
            a = (fr - 211) / (262 - 211)
            pj = Vector((jar0[0] + (jar1[0] - jar0[0]) * a, jar0[1] + (jar1[1] - jar0[1]) * a, 0))
            p = pj + d * 0.42 + Vector((0, 0, BALL_R + abs(math.sin(fr * 0.5)) * 0.04)); spin += 0.35
        elif fr <= contact:
            p = Vector((jar1[0], jar1[1], 0)) + d * 0.42 + Vector((0, 0, BALL_R))
        elif fr <= G:
            a = (fr - contact) / max(1, G - contact)
            p0 = Vector((jar1[0], jar1[1], 0)) + d * 0.42 + Vector((0, 0, BALL_R))
            p1 = Vector((FL / 2 + NET_D - 0.06, 0.25, 0.38))
            p = p0.lerp(p1, a) + Vector((0, 0, math.sin(a * math.pi) * 0.12)); spin += 0.9
        else:
            a = min(1.0, (fr - G) / 20)
            p = Vector((FL / 2 + NET_D - 0.06 - 0.1 * a, 0.3, 0.38 - (0.38 - BALL_R) * a * a + abs(math.sin(a * 6)) * 0.05 * (1 - a)))
            spin += 0.2 * (1 - a)
        ball.location = p; ball.keyframe_insert('location', frame=fr)
        ball.rotation_euler = (0, spin, 0); ball.keyframe_insert('rotation_euler', frame=fr)
    for fr, v in ((1, 0.0), (contact - 1, 0.0), (contact, 8.0), (G + 25, 5.0), (G + 70, 0.0)):
        ball_em.default_value = v; ball_em.keyframe_insert('default_value', frame=fr)
    fire_trail(ball, contact, G + 4, st)
    net_bulge(back_home, 1, 0.25, 0.38, G)
    confetti(st)

    print('[trailer] камеры...')
    cA, tA = camera('CamA', 24, 4.0)
    cam_keys(cA, tA, [(1, (-2.5, 13.0, 17.0), (0, -1.0, 0.5), 24), (96, (0.2, 5.2, 1.2), (0, 0, 0.8), 30)])
    cB1, tB1 = camera('CamB1', 45, 2.0)
    cam_keys(cB1, tB1, [(97, (-1.3, 1.8, 0.5), (-0.6, 0, 1.0), None), (134, (-0.2, 1.6, 0.55), (-0.6, 0, 0.95), None)])
    cB2, tB2 = camera('CamB2', 45, 2.0)
    cam_keys(cB2, tB2, [(135, (0.15, 1.75, 0.35), (0.6, 0, 0.95), None), (172, (1.05, 1.6, 0.45), (0.6, 0, 0.95), None)])
    cB3, tB3 = camera('CamB3', 45, 2.0)
    cam_keys(cB3, tB3, [(173, (-3.6, 1.65, 0.75), (-3.0, 0, 0.95), None), (210, (-2.55, 1.7, 0.6), (-3.0, 0, 0.95), None)])
    cC1, tC1 = camera('CamC1', 30, 4.0)
    cam_keys(cC1, tC1, [(211, (jar0[0] - 0.9, jar0[1] + 3.3, 0.95), (jar0[0] + 0.9, jar0[1], 0.55), None),
                        (262, (jar1[0] - 0.9, jar1[1] + 3.0, 0.85), (jar1[0] + 0.7, jar1[1], 0.5), None),
                        (contact + 2, (jar1[0] - 0.2, jar1[1] + 2.2, 0.6), (jar1[0] + 0.6, jar1[1], 0.45), None)])
    cC2, tC2 = camera('CamC2', 30, 2.8)
    cam_keys(cC2, tC2, [(contact + 3, (13.15, 0.9, 1.05), (9.0, 0.25, 0.3), 26), (330, (13.1, 0.75, 1.0), (9.6, 0.2, 0.32), 28)])
    cD, tD = camera('CamD', 32, 2.8)
    keysD = []
    for i, fr in enumerate(range(331, 406, 15)):
        a = math.radians(55 + (fr - 331) / 74 * 75)
        keysD.append((fr, (jar1[0] + math.cos(a) * 2.6, jar1[1] + math.sin(a) * 2.6, 0.65 + (fr - 331) / 74 * 0.35), (jar1[0], jar1[1], 0.85), None))
    cam_keys(cD, tD, keysD)
    cE, tE = camera('CamE', 32, 8.0)
    cam_keys(cE, tE, [(406, (6.6, 4.4, 1.5), (8.2, 0.3, 0.9), None), (450, (4.0, 9.5, 7.5), (8.0, 0.0, 0.0), None)])
    for c in (cA, cB1, cB2, cB3, cC1, cC2, cD, cE):
        ease_all(c); ease_all(c.constraints[0].target)
    for fr, c in ((1, cA), (97, cB1), (135, cB2), (173, cB3), (211, cC1), (contact + 3, cC2), (331, cD), (406, cE)):
        cut(fr, c)
    sc.camera = cA
    title_card(cE, 410)

    sc.frame_set(1)
    path = os.path.join(OUT, 'trailer.blend')
    try:
        bpy.ops.wm.save_as_mainfile(filepath=path)
        print('[trailer] сохранено:', path)
    except Exception as e:
        print('[trailer] не сохранилось:', e)
    print('[trailer] готово. Рендер: Render -> Render Animation (Ctrl+F12) ->', sc.render.filepath)

if __name__ == '__main__' or os.environ.get('ARENA_RUN') == '1':
    main()
