# Арена Страйкеров — подготовка модели героя из TRELLIS для Mixamo.
# Blender: вкладка Scripting -> Open -> этот файл -> Run Script (▶).
# Что делает: импорт .glb -> склейка швов -> удаление мусорных кусочков ->
# поворот лицом вперёд (-Y) -> рост HEIGHT, ноги на полу -> гладкое затенение ->
# текстура в png -> экспорт FBX для Mixamo + сохранение .blend.
import bpy, bmesh, os, math
from mathutils import Matrix, Vector

# ======== НАСТРОЙКИ (меняйте для каждого героя) ========
ROOT   = r"C:\Users\user\ArenaStrikers"
HERO   = "jar"          # папка героя в persons_concepts
SRC    = "sample3.glb"  # файл из TRELLIS
FACING = "+X"           # куда смотрит лицо после импорта: +X, -X, +Y, -Y
HEIGHT = 1.8            # рост в метрах (Mixamo любит человеческий масштаб)
# Хвост отрезается в отдельный объект, иначе Mixamo приклеит его к ноге.
# Правило в готовых координатах (лицо к -Y, спина к +Y, ноги на z=0). None — хвоста нет.
# Жар: трубка хвоста за ногами ниже пояса + огонёк на кончике справа.
TAIL   = lambda x, y, z: (y > 0.11 and z < 0.58) or (y > 0.15 and x > 0.3 and z < 0.8)
# ======================================================

src = os.path.join(ROOT, "persons_concepts", HERO, SRC)
out_dir = os.path.join(ROOT, "Art", "Heroes", HERO)
os.makedirs(out_dir, exist_ok=True)

# 1. пустая сцена
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
    for d in list(coll):
        if d.users == 0:
            coll.remove(d)

# 2. импорт
bpy.ops.import_scene.gltf(filepath=src)
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
obj = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
obj.name = HERO; obj.data.name = HERO + "_mesh"
me = obj.data

# 3. склеить швы и удалить мусорные кусочки
bm = bmesh.new(); bm.from_mesh(me)
before = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
bm.verts.ensure_lookup_table()
seen, islands = set(), []
for v in bm.verts:
    if v.index in seen: continue
    stack, isl = [v], []; seen.add(v.index)
    while stack:
        x = stack.pop(); isl.append(x)
        for e in x.link_edges:
            w = e.other_vert(x)
            if w.index not in seen:
                seen.add(w.index); stack.append(w)
    islands.append(isl)
limit = max(20, int(0.005 * len(bm.verts)))
junk = [i for i in islands if len(i) < limit]
bmesh.ops.delete(bm, geom=[v for i in junk for v in i], context='VERTS')
bm.to_mesh(me); bm.free()
print(f"[prep] вершин {before} -> {len(me.vertices)}, кусков {len(islands)}, удалено мусора {len(junk)}")

# 4. лицом к -Y (вид спереди Numpad 1), рост HEIGHT, ступни на z=0, центр в 0
ang = {"+X": -math.pi/2, "-X": math.pi/2, "+Y": math.pi, "-Y": 0.0}[FACING]
me.transform(Matrix.Rotation(ang, 4, 'Z'))
co = [v.co for v in me.vertices]
mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
me.transform(Matrix.Translation(-Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))))
me.transform(Matrix.Scale(HEIGHT / (mx.z - mn.z), 4))
for p in me.polygons:
    p.use_smooth = True
me.update()

# 5. текстура: достаём png прямо из .glb и собираем простой материал
import json, struct
raw = open(src, 'rb').read()
jlen = struct.unpack('<I', raw[12:16])[0]
gj = json.loads(raw[20:20 + jlen]); binc = raw[20 + jlen + 8:]
tex_path = None
if gj.get('images'):
    bv = gj['bufferViews'][gj['images'][0]['bufferView']]
    o = bv.get('byteOffset', 0)
    tex_path = os.path.join(out_dir, f"{HERO}_albedo.png")
    with open(tex_path, 'wb') as f:
        f.write(binc[o:o + bv['byteLength']])
    print("[prep] текстура ->", tex_path)
    mat = bpy.data.materials.new(HERO + "_mat")
    try: mat.use_nodes = True
    except Exception: pass
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None) or nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 1.0
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(tex_path, check_existing=True)
    tex.location = (-400, 200)
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    me.materials.clear(); me.materials.append(mat)

# 5b. отделить хвост, а дырку в теле залатать
tail_obj = None
if TAIL:
    tail_obj = obj.copy(); tail_obj.data = me.copy()
    bpy.context.collection.objects.link(tail_obj)
    tail_obj.name = HERO + "_tail"; tail_obj.data.name = HERO + "_tail_mesh"

    # тело: убрать все грани, задевающие хвост
    bm = bmesh.new(); bm.from_mesh(me)
    uvl = bm.loops.layers.uv.active
    tv = {v for v in bm.verts if TAIL(*v.co)}
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(v in tv for v in f.verts)], context='FACES')
    edge_uv = {}
    for v in bm.verts:
        if v.link_loops and uvl: edge_uv[v] = v.link_loops[0][uvl].uv.copy()
    border = [e for e in bm.edges if e.is_boundary]
    filled = bmesh.ops.holes_fill(bm, edges=border, sides=0)["faces"]
    poked = bmesh.ops.poke(bm, faces=filled)["faces"] if filled else []
    if uvl:
        for f in poked:
            known = [edge_uv[l.vert] for l in f.loops if l.vert in edge_uv]
            for l in f.loops:
                l[uvl].uv = edge_uv.get(l.vert, known[0] if known else l[uvl].uv)
    bm.normal_update(); bm.to_mesh(me); bm.free(); me.update()
    print(f"[prep] тело: залатано дыр {len(filled)}")

    # хвост: оставить только хвостовые грани, пивот — в точке крепления
    tm = tail_obj.data
    bm = bmesh.new(); bm.from_mesh(tm)
    tv = {v for v in bm.verts if TAIL(*v.co)}
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not any(v in tv for v in f.verts)], context='FACES')
    bm.to_mesh(tm); bm.free()
    zs = sorted(v.co.z for v in tm.vertices); ztop = zs[int(len(zs) * 0.9)]
    top = [v.co.copy() for v in tm.vertices if v.co.z >= ztop]
    root = sum(top, Vector()) / len(top)
    tm.transform(Matrix.Translation(-root)); tail_obj.location = root
    print(f"[prep] хвост: {len(tm.vertices)} вершин, крепление {tuple(round(c, 3) for c in root)}")

# 6. экспорт
def export(o, name):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    bpy.context.view_layer.objects.active = o
    path = os.path.join(out_dir, name)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                             path_mode='COPY', embed_textures=True)
    print("[prep] ->", path)
export(obj, f"{HERO}_mixamo.fbx")
if tail_obj: export(tail_obj, f"{HERO}_tail.fbx")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, f"{HERO}.blend"), copy=True)
print("[prep] готово")
