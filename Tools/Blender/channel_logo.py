# Логотип SpellStrike на прозрачном фоне (для заставок и превью; из него channel_avatar.py делает аватар канала). Запуск: Blender → File → New → General → Scripting → Open → этот файл → Run Script.
# Результат: Builds/Branding/spellstrike_logo_transparent.png
import bpy, os, sys, math
from mathutils import Vector

ROOT = os.environ.get('ARENA_ROOT') or r'C:\Users\user\ArenaStrikers'
sys.path.insert(0, os.path.join(ROOT, 'Tools', 'Blender')); sys.modules.pop('logo', None)
import logo
OUT = os.path.join(ROOT, 'Builds', 'Branding')
SAMPLES = int(os.environ.get('ARENA_SAMPLES', '96'))
F = 60   # кадр, когда логотип полностью собран (влетает с кадра 10)

def clean():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for c in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.cameras, bpy.data.lights, bpy.data.node_groups):
        for d in list(c):
            try: c.remove(d)
            except Exception: pass

def emit_plane(name, size, loc, mat, parent):
    me = bpy.data.meshes.new(name); s = size / 2
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    me.uv_layers.new(); me.materials.append(mat)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.parent = parent; o.location = loc
    return o

def background_mat():
    """Ночное небо стадиона: тёмно-синий центр, к краям почти чёрный; снизу — тёплый отсвет, сверху-слева — холодный."""
    m = bpy.data.materials.new('AvatarBG'); m.use_nodes = True
    N = m.node_tree.nodes; L = m.node_tree.links
    for x in list(N):
        if x.type != 'OUTPUT_MATERIAL': N.remove(x)
    out = next(x for x in N if x.type == 'OUTPUT_MATERIAL')
    tc = N.new('ShaderNodeTexCoord')
    def radial(cx, cy, scale):
        mp = N.new('ShaderNodeMapping'); mp.inputs['Location'].default_value = (-cx * scale, -cy * scale, 0); mp.inputs['Scale'].default_value = (scale, scale, 1)
        L.new(tc.outputs['UV'], mp.inputs['Vector'])
        g = N.new('ShaderNodeTexGradient'); g.gradient_type = 'SPHERICAL'; L.new(mp.outputs['Vector'], g.inputs['Vector'])
        return g.outputs['Fac']
    base = N.new('ShaderNodeValToRGB'); L.new(radial(0.5, 0.5, 1.6), base.inputs['Fac'])
    base.color_ramp.elements[0].color = logo.srgb((0.02, 0.025, 0.06)) + (1,)
    base.color_ramp.elements[1].color = logo.srgb((0.09, 0.12, 0.3)) + (1,)
    def glow(col, cx, cy, sc, k):
        mx = N.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'ADD'
        f = N.new('ShaderNodeMath'); f.operation = 'MULTIPLY'; f.inputs[1].default_value = k; L.new(radial(cx, cy, sc), f.inputs[0])
        L.new(f.outputs[0], mx.inputs['Factor']); mx.inputs['B'].default_value = logo.srgb(col) + (1,)
        return mx
    g1 = glow((0.35, 0.25, 1.0), 0.22, 0.8, 2.6, 0.5)     # магия — сверху слева
    g2 = glow((1.0, 0.35, 0.08), 0.8, 0.18, 2.6, 0.45)   # огонь — снизу справа
    L.new(base.outputs['Color'], g1.inputs['A']); L.new(g1.outputs['Result'], g2.inputs['A'])
    em = N.new('ShaderNodeEmission'); L.new(g2.outputs['Result'], em.inputs['Color']); em.inputs['Strength'].default_value = 1.0
    L.new(em.outputs[0], out.inputs['Surface'])
    return m

def ring(name, r, w, parent, z):
    """Тонкое светящееся кольцо по краю аватара: бирюза слева → огонь справа."""
    m = logo._gradient_mat(name, logo.srgb((0.45, 0.95, 1.0)), logo.srgb((1.0, 0.45, 0.12)), 4.0, -100)
    me = bpy.data.meshes.new(name); vs, fs = [], []; n = 160
    for i in range(n):
        a = 2 * math.pi * i / n
        vs += [(math.cos(a) * (r - w), math.sin(a) * (r - w), 0), (math.cos(a) * r, math.sin(a) * r, 0)]
    for i in range(n):
        j = (i + 1) % n; fs.append((2 * i, 2 * i + 1, 2 * j + 1, 2 * j))
    me.from_pydata(vs, [], fs); me.materials.append(m)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.parent = parent; o.location = (0, 0, z)
    return o

def spark(name, loc, size, col, parent):
    m = logo._flat_mat(name, logo.srgb(col), 12.0)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=size)
    o = bpy.context.active_object; o.name = name; o.data.materials.append(m); o.parent = parent; o.location = loc
    return o

def setup(sc, res, transparent):
    sc.render.engine = 'CYCLES'
    try: sc.cycles.device = 'GPU'
    except Exception: pass
    sc.cycles.samples = SAMPLES; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res; sc.render.resolution_percentage = 100
    sc.render.film_transparent = transparent
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Punchy'
    except Exception: pass
    sc.view_settings.exposure = -0.3
    ims = sc.render.image_settings
    try: ims.media_type = 'IMAGE'
    except Exception: pass
    ims.file_format = 'PNG'; ims.color_mode = 'RGBA' if transparent else 'RGB'
    sc.world = sc.world or bpy.data.worlds.new('W'); sc.world.use_nodes = True
    bg = next(n for n in sc.world.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs['Strength'].default_value = 0.0
    # свечение ярких букв
    try:
        ng = bpy.data.node_groups.new('LogoComp', 'CompositorNodeTree'); sc.compositing_node_group = ng
        ng.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')
        rl = ng.nodes.new('CompositorNodeRLayers'); gl = ng.nodes.new('CompositorNodeGlare'); out = ng.nodes.new('NodeGroupOutput')
        for k, v in (('Type', 'Bloom'), ('Quality', 'High'), ('Threshold', 0.85), ('Strength', 0.5), ('Size', 0.55)):
            try: gl.inputs[k].default_value = v
            except Exception: pass
        ng.links.new(rl.outputs['Image'], gl.inputs['Image']); ng.links.new(gl.outputs['Image'], out.inputs[0])
        try: ng.links.new(rl.outputs['Alpha'], out.inputs[0]) if False else None
        except Exception: pass
    except Exception as e:
        print('compositor:', e)

def camera(lens):
    cd = bpy.data.cameras.new('LogoCam'); cd.lens = lens; cd.sensor_fit = 'AUTO'; cd.sensor_width = 36
    cam = bpy.data.objects.new('LogoCam', cd); bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam

def render(path):
    sc = bpy.context.scene; sc.frame_set(F)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('[logo]', path)

def main():
    """Логотип на прозрачном фоне (насыщенные цвета, без свечения — свечение и фон добавляет channel_avatar.py)."""
    os.makedirs(OUT, exist_ok=True)
    sc = bpy.context.scene
    clean(); setup(sc, (2400, 1500), True)
    sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'; sc.view_settings.exposure = -0.6
    sc.compositing_node_group = None
    cam = camera(50)
    logo.spellstrike(cam, 10, ROOT, tagline='', size=0.37, y0=0.37 * 0.86)
    for o in bpy.data.objects:
        if o.name.startswith('LogoShade'): bpy.data.objects.remove(o, do_unlink=True)
    for m in bpy.data.materials:   # без переэкспонирования: цвет букв остаётся насыщенным
        b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m.use_nodes else None
        if b is None: continue
        for n in m.node_tree.nodes:
            if n.type == 'MATH' and n.operation == 'MULTIPLY_ADD' and n.inputs[2].default_value > 1.5:
                n.inputs[2].default_value = 1.0; n.inputs[1].default_value = 0.0
    render(os.path.join(OUT, 'spellstrike_logo_transparent.png'))

if __name__ == '__main__' or os.environ.get('ARENA_RUN') == '1':
    main()
