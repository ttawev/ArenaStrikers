# Арена Страйкеров — скелет для модели героя из Meshy (.glb с текстурой).
# Blender: Scripting -> Open -> этот файл -> поменять HERO -> Run Script (▶).
# Делает: импорт -> чистка -> рост 1.8 м, ступни на полу -> скелет (кости как у Mixamo + хвост)
# -> автоматические веса -> рендеры (обычная поза и тестовая поза) -> model.fbx + текстуры прямо в игру.
import bpy, bmesh, os, math, json, struct, shutil
from mathutils import Vector, Matrix

ROOT = r"C:\Users\user\ArenaStrikers"
HERO = "shadow"   # jar, bagor, mig, vihr, shadow, stena

# Суставы (координаты после нормализации: лицо к -Y, ступни на z=0, рост 1.8 м).
# Для левой стороны (+X); правая зеркальная.
HEROES = {
    "jar": dict(
        game="Jar",
        spine=[(0, 0.0, 0.58), (0, 0, 0.72), (0, 0, 0.86), (0, 0, 1.00), (0, -0.01, 1.15), (0, -0.03, 1.26), (0, -0.03, 1.62)],
        shoulder=(0.06, 0, 1.10), arm=(0.30, 0, 1.08), elbow=(0.52, -0.02, 0.86), wrist=(0.66, -0.08, 0.62), hand_end=(0.76, -0.05, 0.42),
        hip=(0.14, 0, 0.58), knee=(0.17, -0.02, 0.32), ankle=(0.20, -0.02, 0.12), toe=(0.20, -0.18, 0.04), toe_end=(0.20, -0.30, 0.03),
        tail=[(-0.04, 0.17, 0.56), (0.0, 0.22, 0.38), (0.10, 0.27, 0.24), (0.26, 0.26, 0.22), (0.38, 0.26, 0.32), (0.42, 0.26, 0.45)],
    ),
    "bagor": dict(
        game="Bagor",
        spine=[(0, 0.02, 0.58), (0, 0.03, 0.72), (0, 0.04, 0.86), (0, 0.05, 1.00), (0, 0.03, 1.18), (0, -0.02, 1.30), (0, -0.02, 1.70)],
        shoulder=(0.06, 0.06, 1.14), arm=(0.30, 0.10, 1.12), elbow=(0.56, 0.06, 0.88), wrist=(0.70, -0.04, 0.65), hand_end=(0.73, -0.08, 0.40),
        hip=(0.15, 0.02, 0.58), knee=(0.17, 0.04, 0.30), ankle=(0.19, 0.05, 0.12), toe=(0.20, -0.12, 0.04), toe_end=(0.20, -0.22, 0.03),
        rigid_head_above=1.52,   # шляпа и верх головы — целиком на кость головы
    ),
    "shadow": dict(
        game="Ten",
        spine=[(0, -0.04, 0.55), (0, -0.04, 0.70), (0, -0.03, 0.85), (0, -0.02, 1.00), (0, -0.02, 1.18), (0, -0.03, 1.30), (0, -0.03, 1.72)],
        shoulder=(0.06, -0.02, 1.14), arm=(0.30, -0.02, 1.12), elbow=(0.55, -0.06, 0.92), wrist=(0.72, -0.09, 0.77), hand_end=(0.88, -0.12, 0.66),
        hip=(0.13, -0.05, 0.55), knee=(0.16, -0.04, 0.30), ankle=(0.18, -0.04, 0.13), toe=(0.19, -0.20, 0.04), toe_end=(0.19, -0.30, 0.03),
        rigid_head_above=1.38,   # капюшон и длинные уши
        # концы шарфа: цепочки костей (левая, правая — зеркально); тело ближе min_y к спине их не слушается
        chains=[dict(name='ScarfL', mirror='ScarfR', parent='Spine2', min_y=0.2,
                     pts=[(0.10, 0.17, 1.18), (0.17, 0.26, 1.00), (0.31, 0.32, 0.80), (0.37, 0.30, 0.60), (0.32, 0.30, 0.40)])],
        # задняя часть капюшона — «лоскут»: вес плавно растёт к кончику (по оси Y от start до end)
        flaps=[dict(name='ScarfHood', parent='Head', pts=[(0, 0.02, 1.62), (0, 0.15, 1.62), (0, 0.29, 1.61)],
                    region=lambda x, y, z: z > 1.42 and y > 0.02 and abs(x) < 0.2, start=0.02, end=0.29)],
    ),
}
C = HEROES[HERO]
SRC = os.path.join(ROOT, "persons_concepts", HERO, "meshy.glb")
OUT = os.path.join(ROOT, "Art", "Heroes", HERO + "_meshy")
GAME = os.path.join(ROOT, "Assets", "Arena", "Resources", "Heroes", C["game"])
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"

# ---------- сцена ----------
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for coll_ in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.cameras, bpy.data.images):
    for d in list(coll_):
        if d.users == 0:
            coll_.remove(d)
coll = bpy.context.scene.collection

# ---------- импорт ----------
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
body.name = HERO
me = body.data

# чистка: склеить швы, убрать мелкий мусор
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.verts.ensure_lookup_table()
seen, islands = set(), []
for v in bm.verts:
    if v.index in seen: continue
    st, isl = [v], []; seen.add(v.index)
    while st:
        x = st.pop(); isl.append(x)
        for e in x.link_edges:
            w = e.other_vert(x)
            if w.index not in seen: seen.add(w.index); st.append(w)
    islands.append(isl)
junk = [i for i in islands if len(i) < max(20, 0.003 * len(bm.verts))]
bmesh.ops.delete(bm, geom=[v for i in junk for v in i], context='VERTS')
bm.to_mesh(me); bm.free()
print(f"[rig] кусков {len(islands)}, удалено мусора {len(junk)}")

# рост 1.8, ступни на 0, центр
co = [v.co for v in me.vertices]
mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
me.transform(Matrix.Translation(-Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))))
me.transform(Matrix.Scale(1.8 / (mx.z - mn.z), 4))
for p in me.polygons:
    p.use_smooth = True
me.update()

# ---------- скелет ----------
names = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head']
BONES = [(names[i], C['spine'][i], C['spine'][i + 1], names[i - 1] if i else None) for i in range(6)]
for side, sx in (('Left', 1), ('Right', -1)):
    def m(c):
        return (c[0] * sx, c[1], c[2])
    BONES += [
        (side + 'Shoulder', m(C['shoulder']), m(C['arm']), 'Spine2'),
        (side + 'Arm', m(C['arm']), m(C['elbow']), side + 'Shoulder'),
        (side + 'ForeArm', m(C['elbow']), m(C['wrist']), side + 'Arm'),
        (side + 'Hand', m(C['wrist']), m(C['hand_end']), side + 'ForeArm'),
        (side + 'UpLeg', m(C['hip']), m(C['knee']), 'Hips'),
        (side + 'Leg', m(C['knee']), m(C['ankle']), side + 'UpLeg'),
        (side + 'Foot', m(C['ankle']), m(C['toe']), side + 'Leg'),
        (side + 'ToeBase', m(C['toe']), m(C['toe_end']), side + 'Foot'),
    ]
arm_data = bpy.data.armatures.new(HERO + '_rig')
rig = bpy.data.objects.new(HERO + '_rig', arm_data)
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
tail_names = []
T = C.get('tail') or []
for i in range(len(T) - 1):
    n = 'Tail%d' % (i + 1)
    b = arm_data.edit_bones.new(n)
    b.head, b.tail = T[i], T[i + 1]
    b.parent = eb['Hips'] if i == 0 else eb['Tail%d' % i]
    eb[n] = b
    tail_names.append(n)
chain_bones = {}   # имя кости -> min_y
for ch in C.get('chains', []):
    for nm, sx in ((ch['name'], 1), (ch.get('mirror'), -1)):
        if not nm: continue
        pts = [(p[0] * sx, p[1], p[2]) for p in ch['pts']]
        for i in range(len(pts) - 1):
            n = '%s%d' % (nm, i + 1)
            b = arm_data.edit_bones.new(n)
            b.head, b.tail = pts[i], pts[i + 1]
            b.parent = eb[ch['parent']] if i == 0 else eb['%s%d' % (nm, i)]
            eb[n] = b
            chain_bones[n] = ch.get('min_y', -9)
for fl in C.get('flaps', []):
    pts = fl['pts']
    for i in range(len(pts) - 1):
        n = '%s%d' % (fl['name'], i + 1)
        b = arm_data.edit_bones.new(n)
        b.head, b.tail = pts[i], pts[i + 1]
        b.parent = eb[fl['parent']] if i == 0 else eb['%s%d' % (fl['name'], i)]
        eb[n] = b
bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
thr = C.get('rigid_head_above')
if thr:
    head_vg = body.vertex_groups.get(P + 'Head')
    for v in me.vertices:
        if v.co.z > thr:
            for g in list(v.groups):
                body.vertex_groups[g.group].remove([v.index])
            head_vg.add([v.index], 1.0, 'REPLACE')
for n, min_y in chain_bones.items():   # тело не должно болтаться вместе с шарфом
    vg = body.vertex_groups.get(n)
    if vg:
        vg.remove([v.index for v in me.vertices if v.co.y < min_y])
for n in chain_bones:
    tail_names.append(n)   # и их тоже гнём в тестовой позе
for fl in C.get('flaps', []):   # лоскут: голова -> кость 1 -> кость 2 по мере удаления
    nb = len(fl['pts']) - 1
    vgs = [body.vertex_groups.get(P + fl['parent']) or body.vertex_groups.new(name=P + fl['parent'])]
    vgs += [body.vertex_groups.get('%s%d' % (fl['name'], i + 1)) or body.vertex_groups.new(name='%s%d' % (fl['name'], i + 1)) for i in range(nb)]
    for v in me.vertices:
        if not fl['region'](*v.co):
            continue
        t = max(0.0, min(1.0, (v.co.y - fl['start']) / (fl['end'] - fl['start'])))
        w = t * t
        seg = min(nb - 1, int(t * nb))
        for g in list(v.groups):
            body.vertex_groups[g.group].remove([v.index])
        vgs[0].add([v.index], 1 - w, 'REPLACE')
        vgs[1 + seg].add([v.index], w, 'REPLACE')
    tail_names.extend('%s%d' % (fl['name'], i + 1) for i in range(nb))
unweighted = sum(1 for v in me.vertices if not any(g.weight > 0.01 for g in v.groups))
print('[rig] вершин без весов:', unweighted)

# ---------- рендеры ----------
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'
sh.color_type = 'TEXTURE'
try:
    sh.show_cavity = False
    sh.show_object_outline = True
except Exception:
    pass
sc.view_settings.view_transform = 'Standard'
if sc.world is None:
    sc.world = bpy.data.worlds.new('World')
sc.world.color = (0.18, 0.2, 0.22)
sc.render.resolution_x, sc.render.resolution_y = 600, 700
camd = bpy.data.cameras.new('cam'); camd.type = 'ORTHO'; camd.ortho_scale = 2.2
cam = bpy.data.objects.new('cam', camd); coll.objects.link(cam); sc.camera = cam


def shoot(tag, views):
    for name, d in views:
        d = Vector(d).normalized()
        cam.location = Vector((0, 0, 0.95)) + d * 6
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(OUT, 'preview_%s_%s.png' % (tag, name))
        bpy.ops.render.render(write_still=True)


VIEWS = [('front', (0, -1, 0.05)), ('side', (1, 0, 0.05)), ('back', (0, 1, 0.05))]
shoot('rest', VIEWS)
# тестовая поза: проверяем, как кожа гнётся за костями
POSE = {P + 'LeftArm': (0, 0, 50), P + 'LeftForeArm': (70, 0, 0), P + 'RightArm': (60, 0, 0), P + 'RightForeArm': (0, 0, -60),
        P + 'LeftUpLeg': (-45, 0, 0), P + 'LeftLeg': (70, 0, 0), P + 'RightUpLeg': (30, 0, 0), P + 'Spine1': (0, 20, 0), P + 'Head': (0, 30, 15)}
for n in tail_names:
    POSE[n] = (0, 0, 25)
for n, r in POSE.items():
    pb = rig.pose.bones.get(n)
    if pb:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = [math.radians(a) for a in r]
bpy.context.view_layer.update()
shoot('pose', VIEWS)
for pb in rig.pose.bones:
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (0, 0, 0)
bpy.data.objects.remove(cam, do_unlink=True)

# ---------- экспорт ----------
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); body.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT, 'model.fbx')
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=False, apply_scale_options='FBX_SCALE_ALL', use_armature_deform_only=True, path_mode='STRIP')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, HERO + '.blend'), copy=True)

# текстуры прямо из .glb: цвет -> albedo.png, нормали -> normal.png
raw = open(SRC, 'rb').read()
jl = struct.unpack('<I', raw[12:16])[0]
gj = json.loads(raw[20:20 + jl]); binc = raw[20 + jl + 8:]
mat0 = gj['materials'][0]
tex_of = {'albedo': mat0.get('pbrMetallicRoughness', {}).get('baseColorTexture'), 'normal': mat0.get('normalTexture')}
os.makedirs(GAME, exist_ok=True)
for name, ref in tex_of.items():
    if not ref: continue
    img = gj['images'][gj['textures'][ref['index']]['source']]
    bv = gj['bufferViews'][img['bufferView']]; o = bv.get('byteOffset', 0)
    data = binc[o:o + bv['byteLength']]
    for d in (OUT, GAME):
        with open(os.path.join(d, name + '.png'), 'wb') as f:
            f.write(data)
shutil.copy(fbx, os.path.join(GAME, 'model.fbx'))
print('[rig] готово:', OUT, '->', GAME)
