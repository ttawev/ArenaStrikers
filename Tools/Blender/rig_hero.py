# Арена Страйкеров — скелет для модели героя из Meshy (.glb с текстурой).
# Blender: Scripting -> Open -> этот файл -> поменять HERO -> Run Script (▶).
# Делает: импорт -> чистка -> рост 1.8 м, ступни на полу -> скелет (кости как у Mixamo + хвост)
# -> автоматические веса -> рендеры (обычная поза и тестовая поза) -> model.fbx + текстуры прямо в игру.
import bpy, bmesh, os, math, json, struct, shutil
from mathutils import Vector, Matrix

ROOT = r"C:\Users\user\ArenaStrikers"
HERO = "vihr"   # jar, bagor, mig, vihr, shadow, stena
BUILD = ["vihr"]   # пересобрать сразу нескольких (пустой список — только HERO)

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
        chains=[dict(name='ScarfL', mirror='ScarfR', parent='Spine2', min_y=0.2, hue=(0.66, 0.86), max_z=1.10,  # только фиолетовые и ниже шеи (петля вокруг шеи — за телом)
                     pts=[(0.10, 0.17, 1.18), (0.17, 0.26, 1.00), (0.31, 0.32, 0.80), (0.37, 0.30, 0.60), (0.32, 0.30, 0.40)])],
        # задняя часть капюшона — «лоскут»: вес плавно растёт к кончику (по оси Y от start до end)
        flaps=[dict(name='ScarfHood', parent='Head', pts=[(0, 0.02, 1.62), (0, 0.15, 1.62), (0, 0.29, 1.61)],
                    region=lambda x, y, z: z > 1.42 and y > 0.02 and abs(x) < 0.2, start=0.02, end=0.29)],
    ),
    "mig": dict(
        game="Mig",
        spine=[(0, -0.10, 0.52), (0, -0.11, 0.68), (0, -0.12, 0.84), (0, -0.12, 1.00), (0, -0.12, 1.18), (0, -0.15, 1.27), (0, -0.14, 1.70)],
        shoulder=(0.06, -0.10, 1.13), arm=(0.31, -0.09, 1.12), elbow=(0.48, -0.10, 0.86), wrist=(0.62, -0.22, 0.62), hand_end=(0.72, -0.26, 0.46),
        hip=(0.12, -0.08, 0.52), knee=(0.15, -0.10, 0.33), ankle=(0.17, -0.10, 0.14), toe=(0.19, -0.25, 0.05), toe_end=(0.19, -0.37, 0.03),
        tail=[(0.0, 0.08, 0.56), (0.03, 0.20, 0.40), (0.14, 0.26, 0.24), (0.30, 0.30, 0.21), (0.42, 0.33, 0.33), (0.46, 0.34, 0.53)],
        rigid_head_above=1.45,   # верх капюшона и уши
        tail_skip_hue=(0.5, 0.8, 0.2),   # шорты (холодный серый: оттенок 0.5..0.8, насыщенность < 0.2) хвост не слушают
    ),
    "vihr": dict(
        game="Vihr",
        spine=[(0, -0.04, 0.52), (0, -0.04, 0.68), (0, -0.04, 0.84), (0, -0.03, 1.00), (0, -0.03, 1.20), (0, -0.06, 1.32), (0, -0.06, 1.78)],
        shoulder=(0.06, -0.03, 1.20), arm=(0.33, -0.04, 1.21), elbow=(0.50, -0.04, 0.95), wrist=(0.70, -0.15, 0.65), hand_end=(0.80, -0.18, 0.47),
        hip=(0.13, -0.06, 0.52), knee=(0.17, -0.07, 0.34), ankle=(0.19, -0.06, 0.15), toe=(0.20, -0.20, 0.05), toe_end=(0.20, -0.32, 0.03),
        # хвостовые перья — длинное перо сзади, от поясницы до пяток
        tail=[(0, 0.12, 0.74), (0, 0.18, 0.56), (0, 0.24, 0.40), (0, 0.32, 0.26), (0, 0.40, 0.17)],
        tail_skip_hue=(0.5, 0.9, 0.2),   # шорты и футболка (белые) хвост не слушают
        chain_force=True,   # вершины, ближайшие к костям крыльев/пера, — целиком на них
        # сложенные крылья за спиной — цепочки от лопаток вниз; руки (y < 0.10) их не слушают
        chains=[dict(name='TailWingL', mirror='TailWingR', parent='Spine2', min_y=0.10, max_z_all=1.47,   # перья на затылке — голова, не крыло
                     pts=[(0.20, 0.13, 1.44), (0.38, 0.22, 1.25), (0.36, 0.27, 0.95), (0.32, 0.29, 0.65), (0.30, 0.29, 0.38)],
                     # зона крыла (для левой стороны): за спиной, сбоку, ниже затылочных перьев; белая форма — не крыло
                     smooth_region=lambda x, y, z: x > 0.14 and y > 0.10 and z < 1.47 and not (z > 1.38 and x < 0.24),
                     skip_hue=(0.5, 0.9, 0.2))],
    ),
    "stena": dict(
        game="Stena",
        spine=[(0, -0.01, 0.52), (0, 0.02, 0.68), (0, 0.04, 0.84), (0, 0.06, 1.00), (0, 0.03, 1.17), (0, -0.06, 1.28), (0, -0.08, 1.75)],
        shoulder=(0.07, 0.05, 1.15), arm=(0.36, 0.08, 1.15), elbow=(0.54, 0.07, 0.86), wrist=(0.67, -0.07, 0.63), hand_end=(0.77, -0.12, 0.46),
        hip=(0.13, -0.01, 0.52), knee=(0.16, -0.02, 0.33), ankle=(0.18, 0.0, 0.14), toe=(0.20, -0.12, 0.05), toe_end=(0.21, -0.21, 0.03),
        rigid_head_above=1.50,   # шапка, кристаллы на ней, уши
        # ледяные кристаллы на плечах — целиком на ключицу своей стороны (условие задано для левой стороны +X, правая зеркально)
        rigid=[dict(bone='Shoulder', region=lambda x, y, z: x > 0.45 and z > 1.40),   # верхушки внешних кристаллов — без проверки цвета
               dict(bone='Shoulder', region=lambda x, y, z: x > 0.30 and y > 0.15 and z > 1.42),   # верхушки задних (за ухом)
               dict(bone='Shoulder', region=lambda x, y, z: x > 0.22 and 1.08 < z < 1.48,
                    hue=(0.45, 0.555), min_sat=0.12)],   # остальное — только голубой лёд (кожа плеча и уши сиреневее)
    ),
}
import builtins
_LOG = []


def print(*a):   # пишем и в консоль, и в Logs/rig_<герой>.log — Claude читает его
    builtins.print(*a)
    _LOG.append(' '.join(str(x) for x in a))


def build(HERO):
    _LOG.clear()
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
    # колени чуть вперёд, локти чуть назад: Unity по этому изгибу понимает, куда сгибаются суставы
    C = dict(C)
    hp, kn, an = C['hip'], C['knee'], C['ankle']
    C['knee'] = (kn[0], min(kn[1], (hp[1] + an[1]) / 2 - 0.04), kn[2])
    ar, el, wr = C['arm'], C['elbow'], C['wrist']
    C['elbow'] = (el[0], max(el[1], (ar[1] + wr[1]) / 2 + 0.03), el[2])
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
    chain_maxz = {}    # имя кости -> выше этой высоты вершины цепочку не слушаются
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
                if 'max_z_all' in ch: chain_maxz[n] = ch['max_z_all']   # для всех костей цепочки
            chain_maxz[n] = ch.get('max_z', ch.get('max_z_all', 9))
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

    # Торс не должен слушаться костей рук: иначе при махе руками вздувается спина («горб») и грудь.
    arm_x = abs(C['arm'][0]) - 0.04
    arm_groups = {}
    for side, sx in (('Left', 1), ('Right', -1)):
        for nm in ('Arm', 'ForeArm', 'Hand'):
            g = body.vertex_groups.get(P + side + nm)
            if g: arm_groups[g.index] = sx
    sp2 = body.vertex_groups.get(P + 'Spine2')
    fixed = 0
    for v in me.vertices:
        if v.co.z < 0.6 or abs(v.co.x) >= arm_x:
            continue
        bad = [g.group for g in v.groups if g.group in arm_groups]
        if bad:
            w_bad = sum(g.weight for g in v.groups if g.group in arm_groups)
            for gi in bad:
                body.vertex_groups[gi].remove([v.index])
            sp2.add([v.index], w_bad, 'ADD')
            fixed += 1
    print('[rig] торс отвязан от рук, вершин:', fixed)

    # Спина и плечи не должны слушаться головы и шеи: голова на бегу держится прямо, а корпус наклоняется —
    # иначе верх спины остаётся с головой и встаёт горбом.
    neck_z, head_z = C['spine'][4][2], C['spine'][5][2]
    g_head, g_neck = body.vertex_groups.get(P + 'Head'), body.vertex_groups.get(P + 'Neck')
    fixed = 0
    for v in me.vertices:
        drop = []
        if g_head and v.co.z < head_z - 0.03: drop.append(g_head.index)
        if g_neck and v.co.z < neck_z - 0.04: drop.append(g_neck.index)
        gs = [g for g in v.groups if g.group in drop]
        if gs and len(gs) < len(v.groups):
            w = sum(g.weight for g in gs)
            for g in gs:
                body.vertex_groups[g.group].remove([v.index])
            sp2.add([v.index], w, 'ADD')
            fixed += 1
    print('[rig] спина отвязана от головы/шеи, вершин:', fixed)

    # Разделить зоны: каждая вершина слушает либо кости тела, либо цепочку (хвост/шарф) —
    # в зависимости от того, какая кость к ней ближе. Иначе хвост тянет ногу и наоборот.
    segs = {}
    for b in arm_data.bones:
        segs[b.name] = (Vector(b.head_local), Vector(b.tail_local))
    is_chain = {n: not n.startswith(P) for n in segs}
    grp_of = {g.index: g.name for g in body.vertex_groups}


    def seg_dist(c, a, b):
        ab = b - a
        t = max(0.0, min(1.0, (c - a).dot(ab) / max(ab.length_squared, 1e-9)))
        return (c - (a + ab * t)).length


    if any(is_chain.values()):
        moved = 0
        for v in me.vertices:
            near = min(segs, key=lambda n: seg_dist(v.co, *segs[n]))
            want_chain = is_chain[near] and v.co.z <= chain_maxz.get(near, 9)
            bad = [g.group for g in v.groups if grp_of.get(g.group) in is_chain and is_chain[grp_of[g.group]] != want_chain]
            if bad and len(bad) < len(v.groups):
                for gi in bad:
                    body.vertex_groups[gi].remove([v.index])
                moved += 1
            elif bad and not want_chain:   # слушала только цепочку, а должна тело: ближайшая кость тела
                for gi in bad:
                    body.vertex_groups[gi].remove([v.index])
                nb_ = min((n for n in segs if not is_chain[n]), key=lambda n: seg_dist(v.co, *segs[n]))
                (body.vertex_groups.get(nb_) or body.vertex_groups.new(name=nb_)).add([v.index], 1.0, 'REPLACE')
                moved += 1
            elif want_chain and C.get('chain_force') and v.co.y >= chain_bones.get(near, -9) and not any(is_chain.get(grp_of.get(g.group), False) for g in v.groups):
                # слушала только тело, а ближе всего кость цепочки (тонкие перья: автовеса их не находят) — на эту кость
                for g in list(v.groups):
                    body.vertex_groups[g.group].remove([v.index])
                (body.vertex_groups.get(near) or body.vertex_groups.new(name=near)).add([v.index], 1.0, 'REPLACE')
                moved += 1
        print('[rig] разделено вершин тело/цепочки:', moved)
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
    # цепочки с цветом: двигаем только вершины этого цвета (фиолетовый шарф), спину не трогаем
    hue_rules = {}
    for ch in C.get('chains', []):
        if ch.get('hue'):
            for nm in (ch['name'], ch.get('mirror')):
                if nm: hue_rules[nm] = ch['hue']
    if hue_rules:
        import numpy as np, colorsys
        img = None
        for mt in me.materials:
            if mt and mt.node_tree:
                for nd in mt.node_tree.nodes:
                    if nd.type == 'TEX_IMAGE' and nd.image and any(l.to_socket.name == 'Base Color' for l in nd.outputs[0].links):
                        img = nd.image
        if img is not None:
            W_, H_ = img.size
            px = np.empty(W_ * H_ * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H_, W_, 4)
            uvd = me.uv_layers.active.data
            hits = {}
            for lp in me.loops:
                u, vv = uvd[lp.index].uv
                c = px[min(H_ - 1, max(0, int(vv * H_))), min(W_ - 1, max(0, int(u * W_)))]
                h_, s_, v_ = colorsys.rgb_to_hsv(float(c[0]), float(c[1]), float(c[2]))
                rec = hits.setdefault(lp.vertex_index, [0, 0]); rec[1] += 1
                if any(lo <= h_ <= hi for lo, hi in hue_rules.values()) and s_ > 0.3: rec[0] += 1
            spine_vg = body.vertex_groups.get(P + 'Spine2')
            cleared = 0
            for v in me.vertices:
                hit = hits.get(v.index, [0, 1])
                if hit[0] * 2 >= hit[1]:
                    continue   # вершина шарфа — пусть слушается цепочку
                gs = [g.group for g in v.groups if any(grp_of.get(g.group, '').startswith(nm) for nm in hue_rules)]
                if gs:
                    for gi in gs:
                        body.vertex_groups[gi].remove([v.index])
                    if len(v.groups) == 0:
                        spine_vg.add([v.index], 1.0, 'REPLACE')
                    cleared += 1
            print('[rig] не-шарфовых вершин отвязано от шарфа:', cleared)
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
    # Жёсткие части (кристаллы и т.п.): вершины в зоне (и нужного цвета) — целиком на одну кость
    if C.get('rigid') or C.get('tail_skip_hue'):
        import numpy as np, colorsys
        vhsv = {}
        img = None
        for mt in me.materials:
            if mt and mt.node_tree:
                for nd in mt.node_tree.nodes:
                    if nd.type == 'TEX_IMAGE' and nd.image and any(l.to_socket.name == 'Base Color' for l in nd.outputs[0].links):
                        img = nd.image
        if img is not None:
            W_, H_ = img.size
            px = np.empty(W_ * H_ * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H_, W_, 4)
            uvd = me.uv_layers.active.data
            for lp in me.loops:
                if lp.vertex_index in vhsv: continue
                u, vv = uvd[lp.index].uv
                c = px[min(H_ - 1, max(0, int(vv * H_))), min(W_ - 1, max(0, int(u * W_)))]
                vhsv[lp.vertex_index] = colorsys.rgb_to_hsv(float(c[0]), float(c[1]), float(c[2]))
        for rr in C.get('rigid', []):
            for side, sx in (('Left', 1), ('Right', -1)):
                bn = P + side + rr['bone'] if rr['bone'] in ('Shoulder', 'Arm', 'ForeArm', 'Hand', 'UpLeg', 'Leg', 'Foot') else P + rr['bone']
                vg = body.vertex_groups.get(bn) or body.vertex_groups.new(name=bn)
                cnt = 0
                for v in me.vertices:
                    if not rr['region'](v.co.x * sx, v.co.y, v.co.z):
                        continue
                    if rr.get('hue'):
                        h_, s_, _ = vhsv.get(v.index, (0, 0, 0))
                        if not (rr['hue'][0] <= h_ <= rr['hue'][1] and s_ >= rr.get('min_sat', 0)):
                            continue
                    for g in list(v.groups):
                        body.vertex_groups[g.group].remove([v.index])
                    vg.add([v.index], 1.0, 'REPLACE')
                    cnt += 1
                print('[rig] жёстко на', bn, 'вершин:', cnt)
        # хвост не тянет за собой одежду: вершины цвета формы (шорты) отвязываем от костей хвоста
        th = C.get('tail_skip_hue')
        if th:
            tgs = {g.index for g in body.vertex_groups if g.name.startswith('Tail')}
            freed = 0
            for v in me.vertices:
                h_, s_, _ = vhsv.get(v.index, (0, 1, 0))
                if not (th[0] <= h_ <= th[1] and s_ < th[2]):
                    continue
                gs = [g.group for g in v.groups if g.group in tgs]
                if not gs: continue
                for gi in gs:
                    body.vertex_groups[gi].remove([v.index])
                if not any(g.weight > 0.01 for g in v.groups):
                    nb_ = min((n for n in segs if not is_chain[n]), key=lambda n: seg_dist(v.co, *segs[n]))
                    (body.vertex_groups.get(nb_) or body.vertex_groups.new(name=nb_)).add([v.index], 1.0, 'REPLACE')
                freed += 1
            print('[rig] вершин формы отвязано от хвоста:', freed)
    # Плавные цепочки (крылья): вершины зоны получают веса, плавно перетекающие вдоль цепочки
    # (родитель -> кость 1 -> кость 2 ...), а не «ближайшая кость = 1». Иначе на изгибе перья рвутся ступеньками.
    for ch in C.get('chains', []):
        reg = ch.get('smooth_region')
        if not reg: continue
        kit = ch.get('skip_hue')
        hs = locals().get('vhsv') or {}
        for nm, sx in ((ch['name'], 1), (ch.get('mirror'), -1)):
            if not nm: continue
            pts = [Vector((p[0] * sx, p[1], p[2])) for p in ch['pts']]
            nb = len(pts) - 1
            seg_len = [(pts[i + 1] - pts[i]).length for i in range(nb)]
            cum = [0.0]
            for L in seg_len: cum.append(cum[-1] + L)
            nodes = [0.0] + [cum[i] + seg_len[i] / 2 for i in range(nb)]   # родитель в корне, кости — в серединах
            names_ = [P + ch['parent']] + ['%s%d' % (nm, i + 1) for i in range(nb)]
            vgs = [body.vertex_groups.get(n_) or body.vertex_groups.new(name=n_) for n_ in names_]
            cnt = 0
            for v in me.vertices:
                if not reg(v.co.x * sx, v.co.y, v.co.z): continue
                if kit and v.index in hs:
                    h_, s_, _ = hs[v.index]
                    if kit[0] <= h_ <= kit[1] and s_ < kit[2]: continue
                best = None
                for i in range(nb):   # ближайшая точка на ломаной -> расстояние вдоль цепочки
                    a_, b_ = pts[i], pts[i + 1]; ab = b_ - a_
                    t = max(0.0, min(1.0, (v.co - a_).dot(ab) / max(ab.length_squared, 1e-9)))
                    d = (v.co - (a_ + ab * t)).length
                    if best is None or d < best[0]: best = (d, cum[i] + t * seg_len[i])
                sp = best[1]
                if sp >= nodes[-1]: w = {nb: 1.0}
                else:
                    j = max(k for k in range(len(nodes)) if nodes[k] <= sp)
                    t = (sp - nodes[j]) / max(nodes[j + 1] - nodes[j], 1e-9)
                    w = {j: 1 - t, j + 1: t}
                for g in list(v.groups):
                    body.vertex_groups[g.group].remove([v.index])
                for k, wk in w.items():
                    if wk > 0.001: vgs[k].add([v.index], wk, 'REPLACE')
                cnt += 1
            print('[rig] плавная цепочка', nm, 'вершин:', cnt)
    # Ключицы — только у плеч: середина спины и груди за ними не тянется
    for side in ('Left', 'Right'):
        gsh = body.vertex_groups.get(P + side + 'Shoulder')
        if not gsh: continue
        lim = abs(C['shoulder'][0]) + 0.06
        for v in me.vertices:
            if abs(v.co.x) < lim:
                g = next((g for g in v.groups if g.group == gsh.index), None)
                if g and len(v.groups) > 1:
                    w = g.weight; gsh.remove([v.index]); sp2.add([v.index], w, 'ADD')
    # Вершины, оставшиеся совсем без костей (иначе они стоят на месте и рвут модель) — к ближайшей кости
    orphans = 0
    for v in me.vertices:
        if not any(g.weight > 0.01 for g in v.groups):
            nb_ = min((n for n in segs if v.co.y >= chain_bones.get(n, -9) and v.co.z <= chain_maxz.get(n, 9)), key=lambda n: seg_dist(v.co, *segs[n]))
            (body.vertex_groups.get(nb_) or body.vertex_groups.new(name=nb_)).add([v.index], 1.0, 'REPLACE')
            orphans += 1
    print('[rig] вершин-сирот привязано к ближайшей кости:', orphans)
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


for h in (BUILD or [HERO]):
    try:
        build(h)
    except Exception as ex:
        import traceback
        print('[rig] ОШИБКА', h, ex, traceback.format_exc())
    stats = {}
    obj = bpy.data.objects.get(h)
    if obj is not None:
        for v in obj.data.vertices:
            for g in v.groups:
                n = obj.vertex_groups[g.group].name
                stats[n] = stats.get(n, 0) + (1 if g.weight > 0.05 else 0)
    _LOG.append('веса (вершин с весом > 0.05): ' + ', '.join('%s=%d' % kv for kv in sorted(stats.items())))
    os.makedirs(os.path.join(ROOT, 'Logs'), exist_ok=True)
    with open(os.path.join(ROOT, 'Logs', 'rig_%s.log' % h), 'w', encoding='utf-8') as f:
        f.write('\n'.join(_LOG))
