# Значок SpellStrike: футбольный мяч, разрубленный светящимся разрезом. Сверху слева — лёд и магия
# (бирюза, кристаллы, сияние), снизу справа — огонь (обугленные панели с лавовыми трещинами, пламя, искры).
# Запуск: Blender → File → New → General → Scripting → Open → этот файл → Run Script.
# Результат: Builds/Branding/spellstrike_emblem.png (прозрачный фон). Аватар и связки с надписью — branding.py.
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix

ROOT = os.environ.get('ARENA_ROOT') or r'C:\Users\user\ArenaStrikers'
OUT = os.path.join(ROOT, 'Builds', 'Branding')
RES = int(os.environ.get('ARENA_RES', '1600'))
SAMPLES = int(os.environ.get('ARENA_SAMPLES', '128'))
random.seed(3)
N_CUT = Vector((1, 0, -1)).normalized()     # разрез: огонь там, куда смотрит N_CUT (вправо-вниз на экране)
T_CUT = Vector((1, 0, 1)).normalized()      # вдоль разреза (снизу слева → вверх вправо)
GAP = 0.05                                   # половинки разведены на столько от разреза

def srgb(c): return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def clean():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for c in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras, bpy.data.node_groups):
        for d in list(c):
            try: c.remove(d)
            except Exception: pass

def link(o): bpy.context.scene.collection.objects.link(o); return o

# ---------------------------------------------------------------- геометрия мяча
def ball_mesh():
    """Усечённый икосаэдр (12 пятиугольников, 20 шестиугольников), швы, скругление до шара."""
    p = (1 + 5 ** 0.5) / 2
    base = [(0, 1, 3 * p), (1, 2 + p, 2 * p), (p, 2, 2 * p + 1)]
    pts = set()
    for a, b, c in base:
        for sa in ((1, -1) if a else (1,)):
            for sb in ((1, -1) if b else (1,)):
                for sc in ((1, -1) if c else (1,)):
                    v = (a * sa, b * sb, c * sc)
                    for k in range(3): pts.add((v[k % 3], v[(k + 1) % 3], v[(k + 2) % 3]))
    bm = bmesh.new()
    for q in pts: bm.verts.new(Vector(q).normalized())
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(1), verts=bm.verts, edges=bm.edges)
    for f in bm.faces: f.material_index = 1 if len(f.verts) == 5 else 0
    panels = list(bm.faces)
    r = bmesh.ops.inset_individual(bm, faces=panels, thickness=0.022, depth=0.0)
    for f in r['faces']: f.material_index = 2   # швы
    me = bpy.data.meshes.new('Ball'); bm.to_mesh(me); bm.free()
    for i in range(4): me.materials.append(None)   # слоты нужны заранее, иначе при применении модификаторов номера панелей сбросятся
    o = link(bpy.data.objects.new('Ball', me))
    m = o.modifiers.new('sub', 'SUBSURF'); m.subdivision_type = 'SIMPLE'; m.levels = 4; m.render_levels = 4   # без стягивания панелей
    c = o.modifiers.new('cast', 'CAST'); c.factor = 1.0; c.use_radius_as_size = True; c.radius = 1.0
    bpy.context.view_layer.objects.active = o
    for md in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
    for p_ in o.data.polygons: p_.use_smooth = True
    return o

def half(src, name, fire):
    """Половинка мяча: отрезать по плоскости разреза, закрыть срез «сердцевиной» (материал 3), отодвинуть."""
    me = src.data.copy(); o = link(bpy.data.objects.new(name, me))
    bm = bmesh.new(); bm.from_mesh(me)
    n = N_CUT if fire else -N_CUT
    r = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=n, clear_inner=True)
    cut = [e for e in r['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
    f = bmesh.ops.edgeloop_fill(bm, edges=cut)
    for fc in f['faces']: fc.material_index = 3; fc.smooth = False
    bm.to_mesh(me); bm.free()
    o.location = n * GAP
    o.rotation_euler = (0, (0.05 if fire else -0.04), 0)
    return o

# ---------------------------------------------------------------- материалы
def nodes(m):
    m.use_nodes = True; N = m.node_tree.nodes; L = m.node_tree.links
    return N, L, next(n for n in N if n.type == 'BSDF_PRINCIPLED')

def cracks(N, L, scale, width):
    """Сетка тонких трещин (Voronoi: расстояние до края ячейки) → 1 на трещине, 0 вне."""
    tc = N.new('ShaderNodeTexCoord'); v = N.new('ShaderNodeTexVoronoi'); v.feature = 'DISTANCE_TO_EDGE'; v.inputs['Scale'].default_value = scale
    nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 4; L.new(tc.outputs['Object'], nz.inputs['Vector'])
    mx = N.new('ShaderNodeMix'); mx.data_type = 'VECTOR'; mx.inputs['Factor'].default_value = 0.12
    L.new(tc.outputs['Object'], mx.inputs['A']); L.new(nz.outputs['Color'], mx.inputs['B']); L.new(mx.outputs['Result'], v.inputs['Vector'])
    mr = N.new('ShaderNodeMapRange'); mr.inputs['From Min'].default_value = 0; mr.inputs['From Max'].default_value = width
    mr.inputs['To Min'].default_value = 1; mr.inputs['To Max'].default_value = 0; L.new(v.outputs['Distance'], mr.inputs['Value'])
    return mr.outputs['Result']

def toward_cut(N, L, k=3.0):
    """1 у разреза, к краю мяча — 0 (свечение сильнее у раны)."""
    tc = N.new('ShaderNodeTexCoord'); dp = N.new('ShaderNodeVectorMath'); dp.operation = 'DOT_PRODUCT'
    dp.inputs[1].default_value = tuple(N_CUT); L.new(tc.outputs['Object'], dp.inputs[0])
    ab = N.new('ShaderNodeMath'); ab.operation = 'ABSOLUTE'; L.new(dp.outputs['Value'], ab.inputs[0])
    ex = N.new('ShaderNodeMath'); ex.operation = 'MULTIPLY'; ex.inputs[1].default_value = -k; L.new(ab.outputs[0], ex.inputs[0])
    e = N.new('ShaderNodeMath'); e.operation = 'EXPONENT'; L.new(ex.outputs[0], e.inputs[0])
    return e.outputs[0]

def mat_ice_panel(name, base, crack_col, crack_k):
    m = bpy.data.materials.new(name); N, L, b = nodes(m)
    b.inputs['Base Color'].default_value = (*base, 1); b.inputs['Roughness'].default_value = 0.18
    b.inputs['Coat Weight'].default_value = 1.0; b.inputs['Coat Roughness'].default_value = 0.05
    try: b.inputs['Subsurface Weight'].default_value = 0.25; b.inputs['Subsurface Radius'].default_value = (0.3, 0.6, 1.0)
    except Exception: pass
    c = cracks(N, L, 6.0, 0.016); t = toward_cut(N, L, 2.5)
    s = N.new('ShaderNodeMath'); s.operation = 'MULTIPLY_ADD'; L.new(t, s.inputs[0]); s.inputs[1].default_value = 0.6; s.inputs[2].default_value = 0.25
    k = N.new('ShaderNodeMath'); k.operation = 'MULTIPLY'; L.new(c, k.inputs[0]); L.new(s.outputs[0], k.inputs[1])
    k2 = N.new('ShaderNodeMath'); k2.operation = 'MULTIPLY'; k2.inputs[1].default_value = crack_k; L.new(k.outputs[0], k2.inputs[0])
    b.inputs['Emission Color'].default_value = (*crack_col, 1); L.new(k2.outputs[0], b.inputs['Emission Strength'])
    return m

def mat_fire_panel(name, base, lava_k):
    m = bpy.data.materials.new(name); N, L, b = nodes(m)
    b.inputs['Base Color'].default_value = (*base, 1); b.inputs['Roughness'].default_value = 0.55
    c = cracks(N, L, 3.6, 0.02); t = toward_cut(N, L, 2.0)
    s = N.new('ShaderNodeMath'); s.operation = 'MULTIPLY_ADD'; L.new(t, s.inputs[0]); s.inputs[1].default_value = 0.8; s.inputs[2].default_value = 0.35
    k = N.new('ShaderNodeMath'); k.operation = 'MULTIPLY'; L.new(c, k.inputs[0]); L.new(s.outputs[0], k.inputs[1])
    ramp = N.new('ShaderNodeValToRGB'); L.new(k.outputs[0], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].color = (*srgb((0.9, 0.12, 0.02)), 1); ramp.color_ramp.elements[1].color = (*srgb((1.0, 0.8, 0.3)), 1)
    k2 = N.new('ShaderNodeMath'); k2.operation = 'MULTIPLY'; k2.inputs[1].default_value = lava_k; L.new(k.outputs[0], k2.inputs[0])
    L.new(ramp.outputs['Color'], b.inputs['Emission Color']); L.new(k2.outputs[0], b.inputs['Emission Strength'])
    return m

def mat_flat(name, col, rough=0.5, emit=None, k=0.0):
    m = bpy.data.materials.new(name); N, L, b = nodes(m)
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = k
    return m

def mat_glass(name, col, emit_k):
    m = bpy.data.materials.new(name); N, L, b = nodes(m)
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = 0.04
    b.inputs['Transmission Weight'].default_value = 0.85; b.inputs['IOR'].default_value = 1.31
    lw = N.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.35
    k = N.new('ShaderNodeMath'); k.operation = 'MULTIPLY'; k.inputs[1].default_value = emit_k; L.new(lw.outputs['Facing'], k.inputs[0])
    b.inputs['Emission Color'].default_value = (*col, 1); L.new(k.outputs[0], b.inputs['Emission Strength'])
    return m

def mat_volume(name, color0, color1, k, side, rise):
    """Светящийся объём вокруг половинки мяча (пламя или магическое сияние): шум, вытянутый вверх, только со своей стороны."""
    m = bpy.data.materials.new(name); m.use_nodes = True; N = m.node_tree.nodes; L = m.node_tree.links
    for x in list(N):
        if x.type != 'OUTPUT_MATERIAL': N.remove(x)
    out = next(x for x in N if x.type == 'OUTPUT_MATERIAL')
    tc = N.new('ShaderNodeTexCoord')
    mp = N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1.0, 1.0, rise); L.new(tc.outputs['Object'], mp.inputs['Vector'])
    nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 2.6; nz.inputs['Detail'].default_value = 8; nz.inputs['Roughness'].default_value = 0.62
    L.new(mp.outputs['Vector'], nz.inputs['Vector'])
    sh = N.new('ShaderNodeMapRange'); sh.inputs['From Min'].default_value = 0.54; sh.inputs['From Max'].default_value = 0.72; L.new(nz.outputs['Fac'], sh.inputs['Value'])
    # только своя сторона
    dp = N.new('ShaderNodeVectorMath'); dp.operation = 'DOT_PRODUCT'; dp.inputs[1].default_value = tuple(N_CUT * side); L.new(tc.outputs['Object'], dp.inputs[0])
    sd = N.new('ShaderNodeMapRange'); sd.inputs['From Min'].default_value = -0.1; sd.inputs['From Max'].default_value = 0.45; L.new(dp.outputs['Value'], sd.inputs['Value'])
    # затухание от поверхности мяча наружу
    ln = N.new('ShaderNodeVectorMath'); ln.operation = 'LENGTH'; L.new(tc.outputs['Object'], ln.inputs[0])
    fo = N.new('ShaderNodeMapRange'); fo.inputs['From Min'].default_value = 0.95; fo.inputs['From Max'].default_value = 1.55
    fo.inputs['To Min'].default_value = 1; fo.inputs['To Max'].default_value = 0; L.new(ln.outputs['Value'], fo.inputs['Value'])
    a = N.new('ShaderNodeMath'); a.operation = 'MULTIPLY'; L.new(sh.outputs['Result'], a.inputs[0]); L.new(sd.outputs['Result'], a.inputs[1])
    a2 = N.new('ShaderNodeMath'); a2.operation = 'MULTIPLY'; L.new(a.outputs[0], a2.inputs[0]); L.new(fo.outputs['Result'], a2.inputs[1])
    ramp = N.new('ShaderNodeValToRGB'); L.new(fo.outputs['Result'], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].color = (*color1, 1); ramp.color_ramp.elements[1].color = (*color0, 1)
    vol = N.new('ShaderNodeVolumePrincipled')
    dn = N.new('ShaderNodeMath'); dn.operation = 'MULTIPLY'; dn.inputs[1].default_value = 0.8; L.new(a2.outputs[0], dn.inputs[0])
    L.new(dn.outputs[0], vol.inputs['Density']); L.new(ramp.outputs['Color'], vol.inputs['Color'])
    L.new(ramp.outputs['Color'], vol.inputs['Emission Color'])
    k_ = N.new('ShaderNodeMath'); k_.operation = 'MULTIPLY'; k_.inputs[1].default_value = k; L.new(a2.outputs[0], k_.inputs[0])
    L.new(k_.outputs[0], vol.inputs['Emission Strength'])
    L.new(vol.outputs[0], out.inputs['Volume'])
    return m

def mat_flame(name, c_in, c_out, side, rise, thresh, seed, k):
    """Оболочка-пламя: по контуру (под углом к камере), только своя сторона, рваный шум, вытянутый вверх."""
    m = bpy.data.materials.new(name); N, L, b = nodes(m)
    b.inputs['Base Color'].default_value = (0, 0, 0, 1); b.inputs['Roughness'].default_value = 1.0
    try: b.inputs['Specular IOR Level'].default_value = 0.0
    except Exception: pass
    tc = N.new('ShaderNodeTexCoord')
    mp = N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1.0, 1.0, rise); mp.inputs['Location'].default_value = (seed, seed * 0.7, 0)
    L.new(tc.outputs['Object'], mp.inputs['Vector'])
    nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 2.4; nz.inputs['Detail'].default_value = 5; nz.inputs['Roughness'].default_value = 0.55
    L.new(mp.outputs['Vector'], nz.inputs['Vector'])
    lw = N.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.5
    rim = N.new('ShaderNodeMapRange'); rim.inputs['From Min'].default_value = 0.35; rim.inputs['From Max'].default_value = 0.85; L.new(lw.outputs['Facing'], rim.inputs['Value'])
    sum_ = N.new('ShaderNodeMath'); sum_.operation = 'MULTIPLY_ADD'; L.new(rim.outputs['Result'], sum_.inputs[0]); sum_.inputs[1].default_value = 0.35; L.new(nz.outputs['Fac'], sum_.inputs[2])
    cut = N.new('ShaderNodeMapRange'); cut.inputs['From Min'].default_value = thresh + 0.25; cut.inputs['From Max'].default_value = thresh + 0.33; L.new(sum_.outputs[0], cut.inputs['Value'])
    dp = N.new('ShaderNodeVectorMath'); dp.operation = 'DOT_PRODUCT'; dp.inputs[1].default_value = tuple(N_CUT * side); L.new(tc.outputs['Object'], dp.inputs[0])
    sd = N.new('ShaderNodeMapRange'); sd.inputs['From Min'].default_value = 0.0; sd.inputs['From Max'].default_value = 0.35; L.new(dp.outputs['Value'], sd.inputs['Value'])
    a0 = N.new('ShaderNodeMath'); a0.operation = 'MULTIPLY'; L.new(cut.outputs['Result'], a0.inputs[0]); L.new(sd.outputs['Result'], a0.inputs[1])
    edge = N.new('ShaderNodeMapRange'); edge.inputs['From Min'].default_value = 0.3; edge.inputs['From Max'].default_value = 0.55; L.new(lw.outputs['Facing'], edge.inputs['Value'])
    a = N.new('ShaderNodeMath'); a.operation = 'MULTIPLY'; L.new(a0.outputs[0], a.inputs[0]); L.new(edge.outputs['Result'], a.inputs[1])   # лицом к камере — прозрачно
    ramp = N.new('ShaderNodeValToRGB'); L.new(nz.outputs['Fac'], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].position = 0.45; ramp.color_ramp.elements[0].color = (*c_out, 1)
    ramp.color_ramp.elements[1].position = 0.75; ramp.color_ramp.elements[1].color = (*c_in, 1)
    b.inputs['Emission Color'].default_value = (1, 1, 1, 1); L.new(ramp.outputs['Color'], b.inputs['Emission Color'])
    b.inputs['Emission Strength'].default_value = k
    L.new(a.outputs[0], b.inputs['Alpha'])
    try: m.surface_render_method = 'BLENDED'
    except Exception: pass
    return m

# ---------------------------------------------------------------- детали
def shell(name, r, m, shift):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=r); o = bpy.context.active_object; o.name = name
    o.data.materials.append(m); o.location = shift
    return o

def shard(name, base, tip, w, m):
    """Ледяной кристалл: вытянутая шестигранная призма с острым концом."""
    d = Vector(tip) - Vector(base); L_ = d.length
    bm = bmesh.new()
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=w, radius2=w * 0.75, depth=L_ * 0.72)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, L_ * 0.36))
    top = [v for v in bm.verts if v.co.z > L_ * 0.7]
    c = sum((v.co for v in top), Vector()) / len(top)
    tipv = bm.verts.new((0, 0, L_))
    for i in range(len(top)):
        a, b_ = top[i], top[(i + 1) % len(top)]
    bmesh.ops.pointmerge(bm, verts=top + [tipv], merge_co=Vector((0, 0, L_)))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(m)
    o = link(bpy.data.objects.new(name, me)); o.location = base
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return o

def dot(name, loc, r, m):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=loc); o = bpy.context.active_object; o.name = name
    o.data.materials.append(m); return o

def blade(name, length, width, m):
    """Светящийся разрез: тонкий ромб вдоль разреза, перед мячом."""
    a = T_CUT * (length / 2); w = Vector((0, 0, 0)) + N_CUT * (width / 2)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(-a), tuple(w), tuple(a), tuple(-w)], [], [(0, 1, 2, 3)]); me.materials.append(m)
    return link(bpy.data.objects.new(name, me))

# ---------------------------------------------------------------- сцена
def main():
    clean(); sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        sc.cycles.device = 'GPU'
    except Exception: pass
    sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = True
    sc.cycles.transparent_max_bounces = 64; sc.cycles.max_bounces = 16   # оболочек пламени много — иначе лучи чернеют
    sc.render.resolution_x = sc.render.resolution_y = RES; sc.render.film_transparent = True
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Punchy'
    except Exception: pass
    sc.view_settings.exposure = 0.0
    ims = sc.render.image_settings
    try: ims.media_type = 'IMAGE'
    except Exception: pass
    ims.file_format = 'PNG'; ims.color_mode = 'RGBA'
    sc.world = sc.world or bpy.data.worlds.new('W'); sc.world.use_nodes = True
    bg = next(n for n in sc.world.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs['Color'].default_value = (0.01, 0.012, 0.03, 1); bg.inputs['Strength'].default_value = 0.4

    src = ball_mesh()
    ice_m = [mat_ice_panel('IceHex', srgb((0.55, 0.86, 1.0)), srgb((0.3, 0.85, 1.0)), 3.5),
             mat_flat('IcePent', srgb((0.02, 0.05, 0.16)), 0.15),
             mat_flat('IceSeam', srgb((0.02, 0.05, 0.12)), 0.4),
             mat_flat('IceCore', srgb((0.5, 0.95, 1.0)), 0.3, srgb((0.45, 0.95, 1.0)), 9.0)]
    fire_m = [mat_fire_panel('FireHex', srgb((0.42, 0.13, 0.04)), 9.0),
              mat_flat('FirePent', srgb((0.015, 0.008, 0.006)), 0.35),
              mat_flat('FireSeam', srgb((0.02, 0.01, 0.01)), 0.6),
              mat_flat('FireCore', srgb((1.0, 0.7, 0.25)), 0.3, srgb((1.0, 0.62, 0.18)), 16.0)]
    ice = half(src, 'IceHalf', False); fire = half(src, 'FireHalf', True)
    bpy.data.objects.remove(src, do_unlink=True)
    for o, ms in ((ice, ice_m), (fire, fire_m)):
        for i, m in enumerate(ms): o.data.materials[i] = m   # по местам: clear() сбросил бы номера панелей

    # сияние и пламя — светящиеся объёмы вокруг своих половинок
    # языки пламени и магическое сияние — светящиеся оболочки с рваным краем, видны по контуру своей половины
    NOFX = os.environ.get('ARENA_NOFX') == '1'
    for i, (r, k, th) in enumerate(((1.05, 1.0, 0.36), (1.14, 0.9, 0.4), (1.26, 0.75, 0.46), (1.4, 0.55, 0.52), (1.56, 0.4, 0.58))):
        if NOFX: break
        shell('Flame%d' % i, r, mat_flame('FlameM%d' % i, srgb((1.0, 0.72, 0.18)), srgb((0.95, 0.16, 0.01)), 1, 0.3, th, i * 7.3, 3.2 * k), N_CUT * GAP)
    for i, (r, k, th) in enumerate(((1.05, 1.0, 0.42), (1.15, 0.8, 0.48), (1.28, 0.6, 0.55))):
        if NOFX: break
        shell('Magic%d' % i, r, mat_flame('MagicM%d' % i, srgb((0.35, 0.92, 1.0)), srgb((0.45, 0.25, 1.0)), -1, 0.55, th, 3.1 + i * 5.7, 2.6 * k), -N_CUT * GAP)

    # ледяные кристаллы по краю ледяной половины (вверх-влево)
    crys = mat_glass('Crystal', srgb((0.55, 0.92, 1.0)), 2.5)
    for i, (ang, L_, w) in enumerate(((112, 0.38, 0.12), (130, 0.62, 0.17), (147, 0.48, 0.14), (163, 0.66, 0.18), (181, 0.42, 0.13), (198, 0.3, 0.1))):
        a = math.radians(ang); d = Vector((math.cos(a), -0.25, math.sin(a))).normalized()
        shard('Shard%d' % i, d * 0.8 - N_CUT * GAP, d * (0.8 + L_) - N_CUT * GAP, w, crys)
    # искры огня и ледяные блёстки
    ember = mat_flat('Ember', (1, 0.4, 0.1), 0.5, srgb((1.0, 0.42, 0.08)), 7)
    frost = mat_flat('Frost', (0.6, 0.9, 1), 0.5, srgb((0.45, 0.9, 1.0)), 6)
    for i in range(16):
        a = math.radians(random.uniform(-80, 10)); r = random.uniform(1.15, 1.65)
        dot('Ember%d' % i, (math.cos(a) * r, -random.uniform(0.2, 0.9), math.sin(a) * r), random.uniform(0.012, 0.03), ember)
    for i in range(10):
        a = math.radians(random.uniform(95, 215)); r = random.uniform(1.2, 1.6)
        dot('Frost%d' % i, (math.cos(a) * r, -random.uniform(0.2, 0.9), math.sin(a) * r), random.uniform(0.01, 0.022), frost)

    # светящийся разрез (как удар клинка) — перед мячом и за ним
    bm_ = bpy.data.materials.new('Blade'); N, L, b = nodes(bm_)
    tc = N.new('ShaderNodeTexCoord'); sep = N.new('ShaderNodeSeparateXYZ'); L.new(tc.outputs['Object'], sep.inputs[0])
    ramp = N.new('ShaderNodeValToRGB'); L.new(sep.outputs['X'], ramp.inputs['Fac'])
    mapr = N.new('ShaderNodeMapRange'); mapr.inputs['From Min'].default_value = -1.4; mapr.inputs['From Max'].default_value = 1.4
    L.new(sep.outputs['X'], mapr.inputs['Value']); L.new(mapr.outputs['Result'], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].color = (*srgb((0.55, 0.95, 1.0)), 1); ramp.color_ramp.elements[1].color = (*srgb((1.0, 0.7, 0.3)), 1)
    mid = ramp.color_ramp.elements.new(0.5); mid.color = (1, 1, 1, 1)
    L.new(ramp.outputs['Color'], b.inputs['Base Color']); L.new(ramp.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 22
    bl = blade('Blade', 3.3, 0.07, bm_); bl.location = (0, -1.08, 0)
    bl2 = blade('BladeBack', 2.3, 0.05, bm_); bl2.location = (0, 0.0, 0)

    # свет: холодный сверху слева (магия), тёплый снизу справа (огонь), контровой сзади
    def area(name, loc, col, e, size):
        L_ = bpy.data.lights.new(name, 'AREA'); L_.energy = e; L_.color = col; L_.size = size
        o = link(bpy.data.objects.new(name, L_)); o.location = loc
        o.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    area('Cool', (-3.5, -4.0, 3.5), srgb((0.6, 0.85, 1.0)), 900, 3)
    area('Warm', (3.5, -3.0, -2.5), srgb((1.0, 0.55, 0.25)), 500, 3)
    area('Rim', (0.5, 4.0, 2.0), srgb((0.8, 0.8, 1.0)), 700, 2)
    area('Front', (0, -6, 0.5), (1, 1, 1), 150, 4)

    cd = bpy.data.cameras.new('Cam'); cd.lens = 50; cd.sensor_fit = 'AUTO'; cd.sensor_width = 36
    cam = link(bpy.data.objects.new('Cam', cd)); cam.location = (0, -7.2, 0); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    # свечение ярких мест
    try:
        ng = bpy.data.node_groups.new('EmComp', 'CompositorNodeTree'); sc.compositing_node_group = ng
        ng.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')
        rl = ng.nodes.new('CompositorNodeRLayers'); gl = ng.nodes.new('CompositorNodeGlare'); out = ng.nodes.new('NodeGroupOutput')
        for k, v in (('Type', 'Bloom'), ('Quality', 'High'), ('Threshold', 1.0), ('Strength', 0.45), ('Size', 0.5)):
            try: gl.inputs[k].default_value = v
            except Exception: pass
        ng.links.new(rl.outputs['Image'], gl.inputs['Image']); ng.links.new(gl.outputs['Image'], out.inputs[0])
    except Exception as e:
        print('compositor:', e)
    os.makedirs(OUT, exist_ok=True)
    border = os.environ.get('ARENA_BORDER')   # рендер полосой (y_min,y_max в долях) — для медленных машин; потом склеить
    if border:
        y0, y1 = (float(x) for x in border.split(','))
        sc.render.use_border = True; sc.render.use_crop_to_border = False
        sc.render.border_min_x, sc.render.border_max_x, sc.render.border_min_y, sc.render.border_max_y = 0, 1, y0, y1
    sc.render.filepath = os.path.join(OUT, os.environ.get('ARENA_OUTNAME', 'spellstrike_emblem.png'))
    bpy.ops.render.render(write_still=True)
    print('[emblem]', sc.render.filepath)

if __name__ == '__main__' or os.environ.get('ARENA_RUN') == '1':
    main()
