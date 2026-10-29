# Арена Страйкеров — Жар, собранный кодом (без нейросетей).
# Blender: Scripting -> Open -> этот файл -> Run Script (▶).
# Строит модель + скелет (имена костей как у Mixamo, плюс хвост), рендерит превью
# в Art/Heroes/jar_v2/preview_*.png и экспортирует model.fbx для Unity.
import bpy, bmesh, os, math
from mathutils import Vector, Matrix

ROOT = r"C:\Users\user\ArenaStrikers"
OUT = os.path.join(ROOT, "Art", "Heroes", "jar_v2")
os.makedirs(OUT, exist_ok=True)

# ---------------- палитра (sRGB) ----------------
COL = dict(
    Skin=(0.86, 0.38, 0.17),
    Kit=(0.96, 0.96, 0.96), KitShorts=(0.80, 0.80, 0.83),
    Boot=(0.95, 0.95, 0.97), Sole=(0.22, 0.22, 0.26),
    Horn=(0.17, 0.15, 0.18), EyeWhite=(1.0, 0.97, 0.86), Iris=(1.0, 0.80, 0.12), Pupil=(0.06, 0.03, 0.02),
    Mouth=(0.30, 0.06, 0.05), Tooth=(1.0, 0.97, 0.90),
    GlowFlame=(1.0, 0.42, 0.06), GlowFlameCore=(1.0, 0.84, 0.28),
)
P = "mixamorig:"

# ---------------- сцена ----------------
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for coll_ in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.cameras, bpy.data.images):
    for d in list(coll_):
        if d.users == 0:
            coll_.remove(d)
coll = bpy.context.scene.collection


def lin(c):
    return tuple(x ** 2.2 for x in c) + (1.0,)


def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = lin(COL[name])
    m.diffuse_color = c
    try:
        m.use_nodes = True
    except Exception:
        pass
    if m.node_tree:
        b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if b:
            b.inputs['Base Color'].default_value = c
            b.inputs['Roughness'].default_value = 0.65
            if name.startswith('Glow'):
                for key in ('Emission Color', 'Emission'):
                    if key in b.inputs:
                        b.inputs[key].default_value = c
                        break
                if 'Emission Strength' in b.inputs:
                    b.inputs['Emission Strength'].default_value = 1.5
    return m


def new_obj(name, me, mname=None):
    o = bpy.data.objects.new(name, me)
    coll.objects.link(o)
    if mname:
        me.materials.append(mat(mname))
    for p in me.polygons:
        p.use_smooth = True
    return o


def sphere(name, center, radii, mname, seg=32, ring=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=ring, radius=1.0)
    c = Vector(center)
    for v in bm.verts:
        v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2])) + c
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return new_obj(name, me, mname)


def tube(name, pts, radii, mname, seg=14, flat=1.0, steps=6):
    """Гладкая трубка по точкам (сплайн Катмулла-Рома), радиус меняется вдоль; радиус 0 в конце = острый кончик."""
    Pt = [Vector(p) for p in pts]
    path, rad = [], []
    for i in range(len(Pt) - 1):
        p0 = Pt[i - 1] if i > 0 else Pt[i]
        p1, p2 = Pt[i], Pt[i + 1]
        p3 = Pt[i + 2] if i + 2 < len(Pt) else Pt[i + 1]
        for s in range(steps):
            t = s / steps
            path.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
            rad.append(radii[i] + (radii[i + 1] - radii[i]) * t)
    path.append(Pt[-1])
    rad.append(radii[-1])
    pointy = rad[-1] < 1e-4
    bm = bmesh.new()
    rings = []
    t0 = (path[1] - path[0]).normalized()
    nrm = Vector((0, 0, 1)).cross(t0)
    if nrm.length < 1e-4:
        nrm = Vector((1, 0, 0)).cross(t0)
    nrm.normalize()
    prev = t0
    n = len(path) - (1 if pointy else 0)
    for i in range(n):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        axis = prev.cross(t)
        if axis.length > 1e-6:
            nrm = Matrix.Rotation(prev.angle(t), 3, axis.normalized()) @ nrm
        prev = t
        bi = t.cross(nrm).normalized()
        r = rad[i]
        rings.append([bm.verts.new(path[i] + nrm * math.cos(2 * math.pi * k / seg) * r + bi * math.sin(2 * math.pi * k / seg) * r * flat) for k in range(seg)])
    for i in range(len(rings) - 1):
        for k in range(seg):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % seg], rings[i + 1][(k + 1) % seg], rings[i + 1][k]))
    bm.faces.new(list(reversed(rings[0])))
    if pointy:
        tip = bm.verts.new(path[-1])
        for k in range(seg):
            bm.faces.new((rings[-1][k], rings[-1][(k + 1) % seg], tip))
    else:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return new_obj(name, me, mname)


def rigid(o, bone):
    vg = o.vertex_groups.new(name=bone)
    vg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    return o


def mirror_x(p):
    return (-p[0], p[1], p[2])


# ---------------- тело: «скелет» из точек + Skin-модификатор ----------------
def build_body():
    names, co, rr, edges = {}, [], [], []

    def V(n, c, r):
        names[n] = len(co)
        co.append(c)
        rr.append(r if isinstance(r, tuple) else (r, r))

    def E(a, b):
        edges.append((names[a], names[b]))

    V('pelvis', (0, 0, 0.66), (0.22, 0.24))
    V('waist', (0, 0, 0.80), (0.24, 0.26))
    V('chest', (0, 0, 0.98), (0.33, 0.30))
    V('upchest', (0, 0, 1.12), (0.37, 0.28))
    V('neck', (0, 0, 1.24), (0.12, 0.11))
    V('neck2', (0, 0, 1.32), (0.11, 0.10))
    for a, b in (('pelvis', 'waist'), ('waist', 'chest'), ('chest', 'upchest'), ('upchest', 'neck'), ('neck', 'neck2')):
        E(a, b)
    for side, sx in (('L', 1), ('R', -1)):
        def S(n, c, r):
            V(n + side, (c[0] * sx, c[1], c[2]), r)
        S('sho', (0.31, 0, 1.13), 0.16)
        S('bic', (0.41, 0, 1.03), 0.15)
        S('elb', (0.50, 0, 0.92), 0.12)
        S('fore', (0.57, 0, 0.82), 0.165)
        S('wri', (0.63, 0, 0.72), 0.12)
        S('hip', (0.13, 0, 0.60), 0.15)
        S('thi', (0.145, 0, 0.48), 0.15)
        S('kne', (0.14, 0, 0.36), 0.095)
        S('shin', (0.14, 0, 0.24), 0.095)
        S('ank', (0.14, 0, 0.13), 0.08)
        E('upchest', 'sho' + side)
        for a, b in (('sho', 'bic'), ('bic', 'elb'), ('elb', 'fore'), ('fore', 'wri'), ('hip', 'thi'), ('thi', 'kne'), ('kne', 'shin'), ('shin', 'ank')):
            E(a + side, b + side)
        E('pelvis', 'hip' + side)
    me = bpy.data.meshes.new('jar_body')
    me.from_pydata(co, edges, [])
    o = bpy.data.objects.new('jar_body', me)
    coll.objects.link(o)
    sk = o.modifiers.new('skin', 'SKIN')
    sk.use_smooth_shade = True
    sk.branch_smoothing = 0.6
    for i, r in enumerate(rr):
        me.skin_vertices[0].data[i].radius = r
    me.skin_vertices[0].data[names['pelvis']].use_root = True
    sub = o.modifiers.new('sub', 'SUBSURF')
    sub.levels = sub.render_levels = 3   # мельче сетка — ровнее край майки
    dg = bpy.context.evaluated_depsgraph_get()
    newme = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    o.modifiers.clear()
    o.data = newme
    newme.name = 'jar_body'
    # материалы по зонам: майка, шорты, гетры
    for n in ('Skin', 'Kit', 'KitShorts'):
        newme.materials.append(mat(n))
    def seg_score(c, chain):
        best = 1e9
        for a, b in zip(chain, chain[1:]):
            A, B = Vector(co[names[a]]), Vector(co[names[b]])
            ab = B - A
            t = max(0.0, min(1.0, (c - A).dot(ab) / ab.length_squared))
            r = rr[names[a]][0] + (rr[names[b]][0] - rr[names[a]][0]) * t
            best = min(best, (c - (A + ab * t)).length / r)
        return best

    torso = ['pelvis', 'waist', 'chest', 'upchest', 'neck']

    def torso_score(c):
        z = max(co[names['pelvis']][2], min(co[names['neck']][2], c.z))
        for a, b in zip(torso, torso[1:]):
            za, zb = co[names[a]][2], co[names[b]][2]
            if za <= z <= zb:
                t = (z - za) / (zb - za)
                rx = rr[names[a]][0] + (rr[names[b]][0] - rr[names[a]][0]) * t
                ry = rr[names[a]][1] + (rr[names[b]][1] - rr[names[a]][1]) * t
                return math.sqrt((c.x / rx) ** 2 + (c.y / ry) ** 2)
        return 9

    arms = [['sho' + sd, 'bic' + sd, 'elb' + sd, 'fore' + sd, 'wri' + sd] for sd in 'LR']
    for p in newme.polygons:
        c = p.center
        is_arm = min(seg_score(c, a) for a in arms) < torso_score(c) * 1.05
        m = 0
        if not is_arm and 0.75 < c.z < 1.19:
            m = 1          # майка
        elif not is_arm and 0.44 < c.z <= 0.75:
            m = 2          # шорты
        elif c.z < 0.30:
            m = 1          # гетры
        p.material_index = m
        p.use_smooth = True
    return o


body = build_body()
COL['GlowLava'] = (1.0, 0.62, 0.12)


def on_surface(axis_a, axis_b, t, ang):
    """Точка на поверхности тела: луч снаружи к оси сегмента руки."""
    A, B = Vector(axis_a), Vector(axis_b)
    c = A + (B - A) * t
    d = (B - A).normalized()
    u = d.cross(Vector((0, 1, 0))).normalized()
    v = d.cross(u).normalized()
    out = u * math.cos(ang) + v * math.sin(ang)
    hit, loc, nor, _ = body.ray_cast(c + out * 0.35, -out, distance=0.7)
    if not hit or (loc - c).length > 0.22:   # попали не в эту руку (в торс) — пропускаем
        return None
    return loc + nor * 0.004


cracks = []
for sx, side in ((1, 'Left'), (-1, 'Right')):
    # углы вокруг руки: 0 — наружу, pi/2 — вперёд, 5.0 — назад-наружу
    for a, b, bone, angs in (((0.50, 0, 0.92), (0.63, 0, 0.72), 'ForeArm', (0.5, 1.5, 5.0)), ((0.31, 0, 1.13), (0.50, 0, 0.92), 'Arm', (0.7, 5.2))):
        a, b = (a[0] * sx, a[1], a[2]), (b[0] * sx, b[1], b[2])
        for ang in angs:
            ang0 = ang if sx > 0 else math.pi - ang
            pts = []
            for j in range(5):
                t = 0.15 + 0.7 * j / 4
                q = on_surface(a, b, t, ang0 + (0.25 if j % 2 else -0.25))
                if q is not None:
                    pts.append(q)
            if len(pts) >= 3:
                cracks.append(rigid(tube('crack', pts, [0.009] * (len(pts) - 1) + [0.0], 'GlowLava', seg=6, steps=3), P + side + bone))

# хвост (часть кожи, гнётся костями Tail1..3)
TAIL = [(0, 0.14, 0.66), (0.03, 0.30, 0.56), (0.10, 0.40, 0.38), (0.22, 0.38, 0.20), (0.36, 0.28, 0.13), (0.44, 0.16, 0.18)]
tail = tube('jar_tail', TAIL, [0.065, 0.06, 0.055, 0.05, 0.045, 0.04], 'Skin')

# ---------------- скелет ----------------
BONES = [  # имя, начало, конец, родитель
    ('Hips', (0, 0, 0.62), (0, 0, 0.74), None),
    ('Spine', (0, 0, 0.74), (0, 0, 0.88), 'Hips'),
    ('Spine1', (0, 0, 0.88), (0, 0, 1.02), 'Spine'),
    ('Spine2', (0, 0, 1.02), (0, 0, 1.18), 'Spine1'),
    ('Neck', (0, 0, 1.18), (0, 0, 1.29), 'Spine2'),
    ('Head', (0, 0, 1.29), (0, 0, 1.62), 'Neck'),
]
for side, sx in (('Left', 1), ('Right', -1)):
    def m(c):
        return (c[0] * sx, c[1], c[2])
    BONES += [
        (side + 'Shoulder', m((0.06, 0, 1.14)), m((0.27, 0, 1.15)), 'Spine2'),
        (side + 'Arm', m((0.27, 0, 1.15)), m((0.50, 0, 0.92)), side + 'Shoulder'),
        (side + 'ForeArm', m((0.50, 0, 0.92)), m((0.63, 0, 0.72)), side + 'Arm'),
        (side + 'Hand', m((0.63, 0, 0.72)), m((0.68, 0, 0.56)), side + 'ForeArm'),
        (side + 'UpLeg', m((0.13, 0, 0.62)), m((0.14, 0, 0.36)), 'Hips'),
        (side + 'Leg', m((0.14, 0, 0.36)), m((0.14, 0, 0.12)), side + 'UpLeg'),
        (side + 'Foot', m((0.14, 0, 0.12)), m((0.14, -0.13, 0.05)), side + 'Leg'),
        (side + 'ToeBase', m((0.14, -0.13, 0.05)), m((0.14, -0.22, 0.04)), side + 'Foot'),
    ]
arm_data = bpy.data.armatures.new('jar_rig')
rig = bpy.data.objects.new('jar_rig', arm_data)
coll.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for n, h, t, par in BONES:
    b = arm_data.edit_bones.new(P + n)
    b.head, b.tail = h, t
    if par:
        b.parent = eb[par]
    eb[n] = b
for i, (a, i0, i1) in enumerate((('Tail1', 0, 2), ('Tail2', 2, 4), ('Tail3', 4, 5))):
    b = arm_data.edit_bones.new(a)
    b.head, b.tail = TAIL[i0], TAIL[i1]
    b.parent = eb['Hips'] if i == 0 else eb['Tail%d' % i]
    eb[a] = b
bpy.ops.object.mode_set(mode='OBJECT')

# кожа + хвост -> автоматические веса
bpy.ops.object.select_all(action='DESELECT')
tail.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')

# ---------------- жёсткие детали (голова, кулаки, бутсы, огонь) ----------------
parts = list(cracks)
H = P + 'Head'
parts += [
    rigid(sphere('head', (0, 0, 1.42), (0.25, 0.23, 0.24), 'Skin'), H),
    rigid(sphere('jaw', (0, -0.06, 1.32), (0.235, 0.17, 0.13), 'Skin'), H),
    rigid(sphere('brow', (0, -0.17, 1.50), (0.20, 0.075, 0.055), 'Skin'), H),
    rigid(sphere('nose', (0, -0.245, 1.41), (0.042, 0.036, 0.036), 'Skin'), H),
    rigid(sphere('mouth', (0, -0.225, 1.335), (0.10, 0.025, 0.02), 'Mouth'), H),
]
for sx in (1, -1):
    parts += [
        rigid(sphere('eyeW', (0.085 * sx, -0.205, 1.44), (0.055, 0.03, 0.045), 'EyeWhite'), H),
        rigid(sphere('iris', (0.085 * sx, -0.228, 1.44), (0.03, 0.012, 0.03), 'Iris'), H),
        rigid(sphere('pupil', (0.085 * sx, -0.238, 1.44), (0.012, 0.006, 0.017), 'Pupil'), H),
        rigid(tube('browHair', [(0.035 * sx, -0.24, 1.49), (0.09 * sx, -0.235, 1.51), (0.15 * sx, -0.215, 1.535)], [0.024, 0.022, 0.012], 'Horn'), H),
        rigid(tube('tusk', [(0.07 * sx, -0.215, 1.30), (0.075 * sx, -0.232, 1.335), (0.082 * sx, -0.24, 1.37)], [0.022, 0.016, 0.0], 'Tooth'), H),
        rigid(tube('ear', [(0.20 * sx, 0.0, 1.44), (0.30 * sx, 0.02, 1.48), (0.37 * sx, 0.05, 1.55)], [0.07, 0.045, 0.0], 'Skin', flat=0.45), H),
        rigid(tube('horn', [(0.12 * sx, -0.02, 1.58), (0.19 * sx, -0.01, 1.68), (0.23 * sx, 0.02, 1.79), (0.20 * sx, 0.05, 1.87)], [0.05, 0.042, 0.027, 0.0], 'Horn'), H),
    ]
FLAMES = [  # x, y, высота, наклон назад, радиус, материал
    (0.00, -0.03, 0.42, 0.16, 0.13, 'GlowFlame'),
    (-0.12, -0.04, 0.34, 0.14, 0.115, 'GlowFlame'), (0.12, -0.04, 0.34, 0.14, 0.115, 'GlowFlame'),
    (-0.20, 0.03, 0.24, 0.12, 0.09, 'GlowFlame'), (0.20, 0.03, 0.24, 0.12, 0.09, 'GlowFlame'),
    (-0.08, 0.10, 0.30, 0.22, 0.11, 'GlowFlame'), (0.08, 0.10, 0.30, 0.22, 0.11, 'GlowFlame'),
    (0.00, 0.15, 0.24, 0.26, 0.10, 'GlowFlame'),
    (-0.06, -0.11, 0.24, 0.10, 0.075, 'GlowFlameCore'), (0.06, -0.11, 0.24, 0.10, 0.075, 'GlowFlameCore'),
    (0.00, -0.10, 0.32, 0.12, 0.08, 'GlowFlameCore'),
]
for i, (x, y, h, ly, r, mn) in enumerate(FLAMES):
    b = Vector((x, y, 1.56 + (0.03 if abs(x) < 0.15 else 0)))
    lx = x * 0.6
    wig = 0.02 * (1 if i % 2 else -1)
    # язык пламени: вверх, потом загиб назад; «волна» вбок
    pts = [b, b + Vector((lx * 0.2 + wig, ly * 0.1, h * 0.35)), b + Vector((lx * 0.6 - wig, ly * 0.35, h * 0.72)), b + Vector((lx + wig, ly, h))]
    parts.append(rigid(tube('flame', pts, [r, r * 1.1, r * 0.65, 0.0], mn, flat=0.55), H))
for sx, side in ((1, 'Left'), (-1, 'Right')):
    hb, fb = P + side + 'Hand', P + side + 'Foot'
    parts += [
        rigid(sphere('fist', (0.665 * sx, -0.01, 0.62), (0.13, 0.125, 0.14), 'Skin'), hb),
        rigid(sphere('knuckles', (0.68 * sx, -0.07, 0.58), (0.10, 0.08, 0.075), 'Skin'), hb),
        rigid(sphere('thumb', (0.60 * sx, -0.10, 0.665), (0.05, 0.055, 0.05), 'Skin'), hb),
    ]
    boot = sphere('boot', (0.14 * sx, -0.05, 0.085), (0.105, 0.17, 0.085), 'Boot')
    for v in boot.data.vertices:
        v.co.z = max(v.co.z, 0.03)
    parts += [rigid(boot, fb), rigid(sphere('sole', (0.14 * sx, -0.05, 0.025), (0.11, 0.175, 0.028), 'Sole'), fb)]
# огонёк на кончике хвоста
tip = Vector(TAIL[-1])
for h, lx, ly, r, mn in ((0.26, 0.04, 0.03, 0.085, 'GlowFlame'), (0.19, -0.06, 0.05, 0.065, 'GlowFlame'), (0.16, 0.0, -0.04, 0.055, 'GlowFlameCore')):
    pts = [tip, tip + Vector((lx * 0.3, ly * 0.3, h * 0.4)), tip + Vector((lx, ly, h))]
    parts.append(rigid(tube('tailFlame', pts, [r, r * 1.05, 0.0], mn, flat=0.6), 'Tail3'))

bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body.name = 'jar'

# ---------------- превью ----------------
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
try:
    sh.show_cavity = True
    sh.show_object_outline = True
except Exception:
    pass
sc.view_settings.view_transform = 'Standard'
if sc.world is None:
    sc.world = bpy.data.worlds.new('World')
sc.world.color = (0.18, 0.2, 0.22)
sc.render.resolution_x, sc.render.resolution_y = 700, 800
sc.render.film_transparent = False
camd = bpy.data.cameras.new('preview_cam')
camd.type = 'ORTHO'
camd.ortho_scale = 2.4
cam = bpy.data.objects.new('preview_cam', camd)
coll.objects.link(cam)
sc.camera = cam
center = Vector((0, 0, 1.0))
for name, d in (('front', (0, -1, 0.05)), ('side', (1, 0, 0.05)), ('back', (0, 1, 0.05)), ('game', (0, -0.62, 0.79))):
    d = Vector(d).normalized()
    cam.location = center + d * 6
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, 'preview_%s.png' % name)
    bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam, do_unlink=True)

# ---------------- экспорт ----------------
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'model.fbx'), use_selection=True,
                         object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False,
                         apply_scale_options='FBX_SCALE_ALL', use_armature_deform_only=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'jar_v2.blend'), copy=True)
import shutil
shutil.copy(os.path.join(OUT, 'model.fbx'), os.path.join(ROOT, 'Assets', 'Arena', 'Resources', 'Heroes', 'Jar', 'model.fbx'))  # сразу в игру
print('[jar] готово:', OUT)
