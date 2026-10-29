# Арена Страйкеров — Жар, собранный кодом (v3: единая «вылепленная» сетка + детали).
# Blender: Scripting -> Open -> этот файл -> Run Script (▶). Работает ~30-60 с.
# Результат: Art/Heroes/jar_v2/preview_*.png, model.fbx (и копия сразу в игру).
import bpy, bmesh, os, math, random, shutil
from mathutils import Vector, Matrix

ROOT = r"C:\Users\user\ArenaStrikers"
OUT = os.path.join(ROOT, "Art", "Heroes", "jar_v2")
GAME = os.path.join(ROOT, "Assets", "Arena", "Resources", "Heroes", "Jar")
os.makedirs(OUT, exist_ok=True)
VOXEL = 0.007        # детализация лепки (меньше = детальнее и медленнее)
KEEP = 0.28          # доля полигонов после упрощения

COL = dict(  # sRGB
    Skin=(0.86, 0.38, 0.17), SkinDark=(0.55, 0.17, 0.07),
    Kit=(0.96, 0.96, 0.96), KitShorts=(0.80, 0.80, 0.83), Trim=(0.99, 0.99, 0.99), KitStripe=(0.96, 0.96, 0.96),
    Boot=(0.95, 0.95, 0.97), Sole=(0.20, 0.20, 0.24),
    Horn=(0.17, 0.15, 0.18), EyeWhite=(1.0, 0.97, 0.86), Iris=(1.0, 0.80, 0.12), Pupil=(0.06, 0.03, 0.02),
    Mouth=(0.30, 0.06, 0.05), Tooth=(1.0, 0.97, 0.90),
    GlowFlameDeep=(0.95, 0.24, 0.04), GlowFlame=(1.0, 0.50, 0.08), GlowFlameCore=(1.0, 0.86, 0.32),
    GlowLava=(1.0, 0.62, 0.12), GlowSpark=(1.0, 1.0, 1.0),
)
P = "mixamorig:"
rnd = random.Random(7)

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


def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = tuple(x ** 2.2 for x in COL[name]) + (1.0,)
    m.diffuse_color = c
    try:
        m.use_nodes = True
    except Exception:
        pass
    if m.node_tree:
        b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if b:
            b.inputs['Base Color'].default_value = c
            b.inputs['Roughness'].default_value = 0.6
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


def sphere(name, center, radii, mname='Skin', seg=32, ring=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=ring, radius=1.0)
    c = Vector(center)
    for v in bm.verts:
        v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2])) + c
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return new_obj(name, me, mname)


def tube(name, pts, radii, mname='Skin', seg=14, flat=1.0, steps=6, ridge=(0, 0)):
    """Гладкая трубка по точкам (Катмулл-Ром). Радиус 0 в конце — острый кончик. ridge=(сила, число рёбер)."""
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
    if ridge[0]:
        n_ = len(rad)
        rad = [r * (1 + ridge[0] * max(0.0, math.sin(math.pi * ridge[1] * i / (n_ - 1)))) for i, r in enumerate(rad)]
    pointy = rad[-1] < 1e-4
    bm = bmesh.new()
    rings = []
    t0 = (path[1] - path[0]).normalized()
    nrm = Vector((0, 0, 1)).cross(t0)
    if nrm.length < 1e-4:
        nrm = Vector((1, 0, 0)).cross(t0)
    nrm.normalize()
    prev = t0
    for i in range(len(path) - (1 if pointy else 0)):
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


def bake(o, mods):
    """Применить модификаторы через вычисленную сетку."""
    for kind, props in mods:
        m = o.modifiers.new(kind.lower(), kind)
        for k, v in props.items():
            setattr(m, k, v)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    o.modifiers.clear()
    old = o.data
    o.data = me
    bpy.data.meshes.remove(old)


def join(objs, into):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    into.select_set(True)
    bpy.context.view_layer.objects.active = into
    bpy.ops.object.join()
    return into


# ================= 1. «Глина»: всё, что сольётся в одну гладкую кожу =================
SK = {}   # точки скин-скелета: имя -> (координата, (rx, ry))


def build_skin_core():
    names, co, rr, edges = {}, [], [], []

    def V(n, c, r):
        names[n] = len(co)
        co.append(c)
        rr.append(r if isinstance(r, tuple) else (r, r))
        SK[n] = (Vector(c), rr[-1])

    def E(a, b):
        edges.append((names[a], names[b]))

    V('pelvis', (0, 0, 0.66), (0.22, 0.24))
    V('waist', (0, 0, 0.80), (0.24, 0.26))
    V('chest', (0, 0, 0.98), (0.33, 0.30))
    V('upchest', (0, 0, 1.12), (0.37, 0.28))
    V('neck', (0, 0, 1.24), (0.13, 0.12))
    V('neck2', (0, 0, 1.32), (0.12, 0.11))
    for a, b in (('pelvis', 'waist'), ('waist', 'chest'), ('chest', 'upchest'), ('upchest', 'neck'), ('neck', 'neck2')):
        E(a, b)
    for side, sx in (('L', 1), ('R', -1)):
        def S(n, c, r):
            V(n + side, (c[0] * sx, c[1], c[2]), r)
        S('sho', (0.31, 0, 1.13), 0.16)
        S('bic', (0.41, 0, 1.03), 0.15)
        S('elb', (0.50, 0, 0.92), 0.12)
        S('fore', (0.57, 0, 0.82), 0.165)
        S('wri', (0.63, 0, 0.72), 0.11)
        S('hip', (0.13, 0, 0.60), 0.15)
        S('thi', (0.145, 0, 0.48), 0.15)
        S('kne', (0.14, 0, 0.36), 0.10)
        S('shin', (0.14, 0, 0.24), 0.10)
        S('ank', (0.14, 0, 0.13), 0.08)
        E('upchest', 'sho' + side)
        for a, b in (('sho', 'bic'), ('bic', 'elb'), ('elb', 'fore'), ('fore', 'wri'), ('hip', 'thi'), ('thi', 'kne'), ('kne', 'shin'), ('shin', 'ank')):
            E(a + side, b + side)
        E('pelvis', 'hip' + side)
    me = bpy.data.meshes.new('core')
    me.from_pydata(co, edges, [])
    o = bpy.data.objects.new('core', me)
    coll.objects.link(o)
    sk = o.modifiers.new('skin', 'SKIN')
    sk.branch_smoothing = 0.6
    for i, r in enumerate(rr):
        me.skin_vertices[0].data[i].radius = r
    me.skin_vertices[0].data[names['pelvis']].use_root = True
    bake(o, [('SUBSURF', dict(levels=2, render_levels=2))])
    return o


clay = [build_skin_core()]
TAIL = [(0, 0.14, 0.66), (0.03, 0.30, 0.56), (0.10, 0.40, 0.38), (0.22, 0.38, 0.20), (0.36, 0.28, 0.13), (0.44, 0.16, 0.18)]
clay.append(tube('tail', TAIL, [0.07, 0.062, 0.056, 0.05, 0.046, 0.042]))
# голова: череп, челюсть, надбровье, нос, скулы, веки, уши
clay += [
    sphere('head', (0, 0, 1.42), (0.25, 0.23, 0.24)),
    sphere('jaw', (0, -0.06, 1.32), (0.24, 0.175, 0.135)),
    sphere('chin', (0, -0.15, 1.27), (0.12, 0.08, 0.07)),
    sphere('brow', (0, -0.17, 1.50), (0.20, 0.075, 0.055)),
    sphere('nose', (0, -0.245, 1.41), (0.045, 0.04, 0.038)),
    tube('noseBridge', [(0, -0.215, 1.48), (0, -0.24, 1.43)], [0.025, 0.03]),
]
for sx in (1, -1):
    clay += [
        sphere('cheek', (0.13 * sx, -0.175, 1.37), (0.075, 0.06, 0.06)),
        sphere('lid', (0.085 * sx, -0.20, 1.468), (0.062, 0.036, 0.024)),
        tube('ear', [(0.20 * sx, 0.0, 1.44), (0.30 * sx, 0.02, 1.48), (0.37 * sx, 0.05, 1.555)], [0.07, 0.045, 0.0], flat=0.45),
        # мышцы
        sphere('delt', (0.34 * sx, 0.0, 1.15), (0.15, 0.15, 0.14)),
        sphere('trap', (0.15 * sx, 0.04, 1.23), (0.15, 0.12, 0.08)),
        sphere('pec', (0.13 * sx, -0.20, 1.06), (0.15, 0.10, 0.11)),
        sphere('calf', (0.14 * sx, 0.035, 0.27), (0.085, 0.085, 0.09)),
        sphere('forearmTop', (0.56 * sx, -0.02, 0.85), (0.15, 0.16, 0.12)),
    ]
    # кулак: ладонь, четыре согнутых пальца, большой палец
    clay.append(sphere('palm', (0.665 * sx, -0.01, 0.63), (0.11, 0.105, 0.12)))
    for i in range(4):
        y = -0.075 + i * 0.045
        clay.append(tube('finger', [(0.70 * sx, y, 0.585), (0.705 * sx, y, 0.525), (0.665 * sx, y, 0.495), (0.625 * sx, y, 0.52)], [0.034, 0.032, 0.03, 0.028]))
    clay.append(tube('thumb', [(0.62 * sx, -0.08, 0.67), (0.635 * sx, -0.12, 0.61), (0.66 * sx, -0.11, 0.565)], [0.04, 0.035, 0.03]))

body = join(clay[1:], clay[0])
body.name = 'jar'
bake(body, [('REMESH', dict(mode='VOXEL', voxel_size=VOXEL, adaptivity=0.0)),
            ('SMOOTH', dict(factor=0.5, iterations=4)),
            ('DECIMATE', dict(ratio=KEEP))])
for p in body.data.polygons:
    p.use_smooth = True
print('[jar] кожа: полигонов', len(body.data.polygons))

# ================= 2. Зоны формы: майка, шорты, гетры, белый кант =================


def seg_score(c, chain):
    best = 1e9
    for a, b in zip(chain, chain[1:]):
        (A, ra), (B, rb) = SK[a], SK[b]
        ab = B - A
        t = max(0.0, min(1.0, (c - A).dot(ab) / ab.length_squared))
        best = min(best, (c - (A + ab * t)).length / (ra[0] + (rb[0] - ra[0]) * t))
    return best


TORSO = ['pelvis', 'waist', 'chest', 'upchest', 'neck']


def torso_score(c):
    z = max(SK['pelvis'][0].z, min(SK['neck'][0].z, c.z))
    for a, b in zip(TORSO, TORSO[1:]):
        (A, ra), (B, rb) = SK[a], SK[b]
        if A.z <= z <= B.z:
            t = (z - A.z) / (B.z - A.z)
            rx = ra[0] + (rb[0] - ra[0]) * t
            ry = ra[1] + (rb[1] - ra[1]) * t
            return math.sqrt((c.x / rx) ** 2 + (c.y / ry) ** 2)
    return 9


def tail_dist(c):
    best = 1e9
    for a, b in zip(TAIL, TAIL[1:]):
        A, B = Vector(a), Vector(b)
        ab = B - A
        t = max(0.0, min(1.0, (c - A).dot(ab) / ab.length_squared))
        best = min(best, (c - (A + ab * t)).length)
    return best


ARMS = [['sho' + s, 'bic' + s, 'elb' + s, 'fore' + s, 'wri' + s] for s in 'LR']
ZONES = ['Skin', 'Kit', 'KitShorts', 'Trim']
me = body.data
me.materials.clear()
for n in ZONES:
    me.materials.append(mat(n))


def zone(c):
    if c.z > 1.19 or (c.y > 0.12 and tail_dist(c) < 0.09 and c.z < 0.62):
        return 0
    if min(seg_score(c, a) for a in ARMS) < torso_score(c) * 1.05:
        return 0
    if c.z > 0.75:
        return 1
    if c.z > 0.44:
        return 2
    if c.z < 0.30:
        return 1
    return 0


for p in me.polygons:
    p.material_index = zone(p.center)
# кант: края майки, шорт и гетр (1-2 полосы полигонов) — белый
bm = bmesh.new()
bm.from_mesh(me)
bm.faces.ensure_lookup_table()


def nb(f):
    return {g for e in f.edges for g in e.link_faces if g is not f}


trim = set()
for f in bm.faces:
    if f.material_index in (1, 2):
        ns = nb(f)
        if any(g.material_index == 0 for g in ns) or (f.material_index == 1 and f.calc_center_median().z > 0.6 and any(g.material_index == 2 for g in ns)):
            trim.add(f)
ring2 = {g for f in trim for g in nb(f) if g.material_index in (1, 2) and g not in trim}
for f in trim | ring2:
    f.material_index = 3
bm.to_mesh(me)
bm.free()

# ================= 3. Скелет и веса =================
BONES = [
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
        (side + 'Hand', m((0.63, 0, 0.72)), m((0.68, 0, 0.54)), side + 'ForeArm'),
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
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')

# ================= 4. Жёсткие детали =================
H = P + 'Head'
parts = []


def on_surface(a, b, t, ang):
    A, B = Vector(a), Vector(b)
    c = A + (B - A) * t
    d = (B - A).normalized()
    u = d.cross(Vector((0, 1, 0))).normalized()
    v = d.cross(u).normalized()
    out = u * math.cos(ang) + v * math.sin(ang)
    hit, loc, nor, _ = body.ray_cast(c + out * 0.35, -out, distance=0.7)
    if not hit or (loc - c).length > 0.24:
        return None
    return loc + nor * 0.004


# трещины лавы: ломаная + короткая ветка
for sx, side in ((1, 'Left'), (-1, 'Right')):
    for a, b, bone, angs in (((0.50, 0, 0.92), (0.63, 0, 0.72), 'ForeArm', (0.4, 1.4, 2.3, 5.0)), ((0.31, 0, 1.13), (0.50, 0, 0.92), 'Arm', (0.6, 1.6, 5.2))):
        a, b = (a[0] * sx, a[1], a[2]), (b[0] * sx, b[1], b[2])
        for ang in angs:
            ang0 = ang if sx > 0 else math.pi - ang
            pts = []
            for j in range(6):
                q = on_surface(a, b, 0.12 + 0.76 * j / 5, ang0 + rnd.uniform(-0.3, 0.3))
                if q is not None:
                    pts.append(q)
            if len(pts) >= 3:
                parts.append(rigid(tube('crack', pts, [0.010] * (len(pts) - 1) + [0.0], 'GlowLava', seg=6, steps=3), P + side + bone))
                q1 = on_surface(a, b, 0.5, ang0 + 0.45)
                if q1 is not None:
                    parts.append(rigid(tube('crackB', [pts[len(pts) // 2], q1], [0.008, 0.0], 'GlowLava', seg=6, steps=3), P + side + bone))

# лицо
parts += [
    rigid(sphere('mouth', (0, -0.228, 1.335), (0.105, 0.025, 0.022), 'Mouth'), H),
    rigid(sphere('lip', (0, -0.222, 1.312), (0.10, 0.03, 0.018), 'SkinDark'), H),
]
for sx in (1, -1):
    parts += [
        rigid(sphere('eyeW', (0.085 * sx, -0.205, 1.44), (0.055, 0.03, 0.045), 'EyeWhite'), H),
        rigid(sphere('iris', (0.08 * sx, -0.229, 1.437), (0.03, 0.012, 0.03), 'Iris'), H),
        rigid(sphere('pupil', (0.08 * sx, -0.239, 1.437), (0.011, 0.006, 0.019), 'Pupil'), H),
        rigid(sphere('spark', (0.07 * sx, -0.241, 1.452), (0.008, 0.004, 0.008), 'GlowSpark'), H),
        rigid(sphere('nostril', (0.017 * sx, -0.282, 1.398), (0.012, 0.008, 0.009), 'Mouth'), H),
        rigid(tube('browHair', [(0.03 * sx, -0.245, 1.49), (0.09 * sx, -0.24, 1.515), (0.155 * sx, -0.215, 1.54)], [0.026, 0.024, 0.0], 'Horn', flat=0.6), H),
        rigid(tube('tusk', [(0.07 * sx, -0.218, 1.30), (0.076 * sx, -0.236, 1.335), (0.085 * sx, -0.243, 1.375)], [0.024, 0.017, 0.0], 'Tooth'), H),
        rigid(tube('fang', [(0.035 * sx, -0.238, 1.345), (0.037 * sx, -0.244, 1.322)], [0.011, 0.0], 'Tooth', seg=8), H),
        rigid(tube('horn', [(0.12 * sx, -0.02, 1.58), (0.19 * sx, -0.01, 1.68), (0.235 * sx, 0.02, 1.79), (0.21 * sx, 0.06, 1.88)],
                   [0.055, 0.045, 0.028, 0.0], 'Horn', ridge=(0.12, 7), steps=10), H),
    ]


# пламя: три слоя языков (тёмно-красный снаружи, оранжевый, жёлтая сердцевина)
def flame(base, h, lx, ly, r, mn, bone, w=0.02):
    b = Vector(base)
    pts = [b, b + Vector((lx * 0.15 + w, ly * 0.05, h * 0.3)), b + Vector((lx * 0.4 - w, ly * 0.25, h * 0.55)),
           b + Vector((lx * 0.7 + w * 0.6, ly * 0.6, h * 0.8)), b + Vector((lx, ly, h))]
    return rigid(tube('flame', pts, [r, r * 1.15, r * 0.95, r * 0.5, 0.0], mn, flat=0.5, steps=5), bone)


for k in range(10):   # внешний венец
    a = math.radians(-160 + 320 * k / 9)
    x, y = 0.19 * math.sin(a), 0.03 - 0.13 * math.cos(a) + 0.06
    parts.append(flame((x, y, 1.55), rnd.uniform(0.2, 0.3), x * 0.9, 0.12 + max(0, y) * 0.8, 0.085, 'GlowFlameDeep', H, rnd.uniform(-0.03, 0.03)))
for k in range(7):    # средний слой
    a = math.radians(-120 + 240 * k / 6)
    x, y = 0.12 * math.sin(a), 0.02 - 0.08 * math.cos(a) + 0.04
    parts.append(flame((x, y, 1.58), rnd.uniform(0.3, 0.42), x * 0.7, 0.16 + max(0, y), 0.105, 'GlowFlame', H, rnd.uniform(-0.03, 0.03)))
for x, y, h in ((-0.06, -0.08, 0.27), (0.06, -0.08, 0.27), (0.0, -0.05, 0.36), (0.0, -0.11, 0.22)):  # сердцевина
    parts.append(flame((x, y, 1.6), h, x * 0.5, 0.12, 0.07, 'GlowFlameCore', H))
tip = TAIL[-1]
for h, lx, ly, r, mn in ((0.24, 0.05, 0.05, 0.075, 'GlowFlameDeep'), (0.30, 0.02, 0.02, 0.07, 'GlowFlame'), (0.2, -0.05, 0.05, 0.055, 'GlowFlame'), (0.18, 0.0, -0.02, 0.045, 'GlowFlameCore')):
    parts.append(flame(tip, h, lx, ly, r, mn, 'Tail3', 0.015))

# бутсы: колодка, подошва, шипы, шнурки, полоска цвета команды
for sx, side in ((1, 'Left'), (-1, 'Right')):
    fb = P + side + 'Foot'
    x = 0.14 * sx
    boot = sphere('boot', (x, -0.05, 0.09), (0.105, 0.175, 0.09), 'Boot')
    for v in boot.data.vertices:
        v.co.z = max(v.co.z, 0.035)
    toe = sphere('toe', (x, -0.16, 0.07), (0.095, 0.08, 0.06), 'Boot')
    for v in toe.data.vertices:
        v.co.z = max(v.co.z, 0.035)
    parts += [rigid(boot, fb), rigid(toe, fb),
              rigid(sphere('sole', (x, -0.06, 0.03), (0.112, 0.19, 0.022), 'Sole'), fb),
              rigid(tube('stripe', [(x + 0.104 * sx, 0.06, 0.07), (x + 0.11 * sx, -0.03, 0.10), (x + 0.10 * sx, -0.13, 0.07)], [0.012, 0.012, 0.012], 'KitStripe', seg=8), fb),
              rigid(tube('stripe2', [(x - 0.104 * sx, 0.06, 0.07), (x - 0.11 * sx, -0.03, 0.10), (x - 0.10 * sx, -0.13, 0.07)], [0.012, 0.012, 0.012], 'KitStripe', seg=8), fb)]
    for i in range(3):
        yy = -0.02 - i * 0.04
        parts.append(rigid(tube('lace', [(x - 0.04, yy, 0.165 - i * 0.012), (x, yy - 0.005, 0.172 - i * 0.012), (x + 0.04, yy, 0.165 - i * 0.012)], [0.008] * 3, 'Sole', seg=6, steps=3), fb))
    for yy in (-0.17, -0.10, -0.03, 0.06):
        for dx in (-0.05, 0.05):
            parts.append(rigid(tube('stud', [(x + dx, yy, 0.012), (x + dx, yy, -0.008)], [0.014, 0.008], 'Sole', seg=8, steps=1), fb))

join(parts, body)

# ================= 5. Превью =================
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
try:
    sh.show_cavity = True
    sh.cavity_type = 'BOTH'
    sh.show_object_outline = True
    sh.show_specular_highlight = True
except Exception:
    pass
sc.view_settings.view_transform = 'Standard'
if sc.world is None:
    sc.world = bpy.data.worlds.new('World')
sc.world.color = (0.18, 0.2, 0.22)
sc.render.resolution_x, sc.render.resolution_y = 700, 800
camd = bpy.data.cameras.new('preview_cam')
camd.type = 'ORTHO'
cam = bpy.data.objects.new('preview_cam', camd)
coll.objects.link(cam)
sc.camera = cam
for name, d, center, scale in (('front', (0, -1, 0.05), (0, 0, 1.0), 2.4), ('side', (1, 0, 0.05), (0, 0, 1.0), 2.4),
                               ('back', (0, 1, 0.05), (0, 0, 1.0), 2.4), ('game', (0, -0.62, 0.79), (0, 0, 1.0), 2.4),
                               ('face', (0, -1, 0.1), (0, -0.1, 1.5), 0.9), ('fist', (0.4, -1, 0.1), (0.66, 0, 0.62), 0.6)):
    d = Vector(d).normalized()
    cam.location = Vector(center) + d * 6
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    camd.ortho_scale = scale
    sc.render.filepath = os.path.join(OUT, 'preview_%s.png' % name)
    bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam, do_unlink=True)

# ================= 6. Экспорт =================
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'model.fbx'), use_selection=True,
                         object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False,
                         apply_scale_options='FBX_SCALE_ALL', use_armature_deform_only=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'jar_v2.blend'), copy=True)
if os.path.isdir(GAME):
    shutil.copy(os.path.join(OUT, 'model.fbx'), os.path.join(GAME, 'model.fbx'))
print('[jar] готово, полигонов всего:', len(body.data.polygons))
