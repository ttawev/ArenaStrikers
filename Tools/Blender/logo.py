# Логотип SpellStrike для роликов: «SPELL» — холодное магическое сияние (бирюза → фиолет),
# «STRIKE» — огонь (золото → оранжево-красный). Буквы с наклоном и фаской, по ним пробегает блик,
# под ними вырастает светящаяся черта, ниже — подпись (например «СКОРО»).
# Логотип прикрепляется к камере (в 4 м перед ней) и проявляется с кадра f0.
import bpy, os, math

def _font(root):
    p = os.path.join(root, 'Assets', 'Arena', 'Resources', 'UI', 'Fonts', 'RussoOne-Regular.ttf')
    return bpy.data.fonts.load(p, check_existing=True)

def _gradient_mat(name, c0, c1, glow, f_sweep, depth=False):
    """Градиент слева направо по букве + бегущий блик (Generated X), всё светится.
    depth=True — лицевая сторона букв светится, а борта темнее и блестят: буквы выглядят объёмными."""
    m = bpy.data.materials.new(name); m.use_nodes = True
    N = m.node_tree.nodes; L = m.node_tree.links
    b = next(n for n in N if n.type == 'BSDF_PRINCIPLED')
    tc = N.new('ShaderNodeTexCoord'); sep = N.new('ShaderNodeSeparateXYZ'); L.new(tc.outputs['Generated'], sep.inputs[0])
    ramp = N.new('ShaderNodeValToRGB'); L.new(sep.outputs['X'], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].color = (*c0, 1); ramp.color_ramp.elements[1].color = (*c1, 1)
    # блик: полоса по X, которая едет слева направо (ключи на sweep.default_value)
    sweep = N.new('ShaderNodeValue'); sweep.outputs[0].default_value = -1.0
    d = N.new('ShaderNodeMath'); d.operation = 'SUBTRACT'; L.new(sep.outputs['X'], d.inputs[0]); L.new(sweep.outputs[0], d.inputs[1])
    tilt = N.new('ShaderNodeMath'); tilt.operation = 'MULTIPLY_ADD'; L.new(sep.outputs['Y'], tilt.inputs[0]); tilt.inputs[1].default_value = -0.25; L.new(d.outputs[0], tilt.inputs[2])
    a = N.new('ShaderNodeMath'); a.operation = 'ABSOLUTE'; L.new(tilt.outputs[0], a.inputs[0])
    band = N.new('ShaderNodeMapRange'); band.clamp = True; band.inputs['From Min'].default_value = 0.0; band.inputs['From Max'].default_value = 0.07
    band.inputs['To Min'].default_value = 1.0; band.inputs['To Max'].default_value = 0.0; L.new(a.outputs[0], band.inputs['Value'])
    mix = N.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.inputs['B'].default_value = (1, 1, 1, 1)
    L.new(band.outputs['Result'], mix.inputs['Factor']); L.new(ramp.outputs['Color'], mix.inputs['A'])
    L.new(ramp.outputs['Color'], b.inputs['Base Color']); L.new(mix.outputs['Result'], b.inputs['Emission Color'])
    st = N.new('ShaderNodeMath'); st.operation = 'MULTIPLY_ADD'; L.new(band.outputs['Result'], st.inputs[0]); st.inputs[1].default_value = glow * 1.6; st.inputs[2].default_value = glow
    if depth:
        # лицо буквы — нормаль к камере (локальная Z); борта и фаска — темнее, металлические
        sz = N.new('ShaderNodeSeparateXYZ'); L.new(tc.outputs['Normal'], sz.inputs[0])
        fr = N.new('ShaderNodeMapRange'); fr.clamp = True; fr.inputs['From Min'].default_value = 0.55; fr.inputs['From Max'].default_value = 0.95
        L.new(sz.outputs['Z'], fr.inputs['Value'])
        k = N.new('ShaderNodeMapRange'); k.inputs['To Min'].default_value = 0.12; k.inputs['To Max'].default_value = 1.0; L.new(fr.outputs['Result'], k.inputs['Value'])
        st2 = N.new('ShaderNodeMath'); st2.operation = 'MULTIPLY'; L.new(st.outputs[0], st2.inputs[0]); L.new(k.outputs['Result'], st2.inputs[1])
        L.new(st2.outputs[0], b.inputs['Emission Strength'])
        dk = N.new('ShaderNodeMix'); dk.data_type = 'RGBA'; L.new(fr.outputs['Result'], dk.inputs['Factor'])
        sd = N.new('ShaderNodeMix'); sd.data_type = 'RGBA'; sd.blend_type = 'MULTIPLY'; sd.inputs['Factor'].default_value = 1.0
        L.new(ramp.outputs['Color'], sd.inputs['A']); sd.inputs['B'].default_value = (0.25, 0.25, 0.3, 1)
        L.new(sd.outputs['Result'], dk.inputs['A']); L.new(ramp.outputs['Color'], dk.inputs['B'])
        L.new(dk.outputs['Result'], b.inputs['Base Color'])
        b.inputs['Roughness'].default_value = 0.18; b.inputs['Metallic'].default_value = 0.85
    else:
        L.new(st.outputs[0], b.inputs['Emission Strength'])
        b.inputs['Roughness'].default_value = 0.3; b.inputs['Metallic'].default_value = 0.4
    for f, v in ((1, -1.0), (f_sweep, -0.3), (f_sweep + 16, 1.3)):
        sweep.outputs[0].default_value = v; sweep.outputs[0].keyframe_insert('default_value', frame=f)
    return m

def _flat_mat(name, color, glow, alpha=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (*color, 1); b.inputs['Emission Color'].default_value = (*color, 1)
    b.inputs['Emission Strength'].default_value = glow
    if alpha is not None:
        b.inputs['Alpha'].default_value = alpha
        try: m.surface_render_method = 'BLENDED'
        except Exception: pass
    return m

def _text(name, body, size, m, font, coll, bold=1.0):
    cu = bpy.data.curves.new(name, 'FONT'); cu.body = body; cu.font = font
    cu.size = size; cu.align_x = 'CENTER'; cu.align_y = 'CENTER'
    cu.shear = 0.18; cu.extrude = 0.03 * bold; cu.bevel_depth = 0.006 * bold; cu.bevel_resolution = 3 if bold > 1 else 2
    cu.materials.append(m)
    o = bpy.data.objects.new(name, cu); coll.objects.link(o)
    return o

def srgb(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def spellstrike(cam, f0, root, tagline='СКОРО', size=0.37, y0=0.95, subtitle=None, x0=0.0, bold=1.0, glow=1.0, turn=0.0):
    """Логотип у камеры cam. f0 — кадр, когда буквы влетают. subtitle — строка под чертой (например «ARCANE LEAGUE»),
    золотом, выезжает с бликом; tagline тогда ниже. x0 — сдвиг вправо (место под значок слева),
    bold — толщина букв (×), glow — яркость (×), turn — поворот букв вокруг вертикали (рад), чтобы был виден объём.
    Возвращает объекты."""
    depth = bold > 1.0
    coll = bpy.context.scene.collection
    font = _font(root)
    spell_m = _gradient_mat('LogoSpell', srgb((0.45, 0.95, 1.0)), srgb((0.62, 0.38, 1.0)), 2.2 * glow, f0 + 12, depth)
    strike_m = _gradient_mat('LogoStrike', srgb((1.0, 0.82, 0.25)), srgb((1.0, 0.3, 0.1)), 2.6 * glow, f0 + 17, depth)
    spell = _text('LogoSPELL', 'SPELL', size, spell_m, font, coll, bold)
    strike = _text('LogoSTRIKE', 'STRIKE', size, strike_m, font, coll, bold)
    # светящаяся черта-«разрез» под словами: бирюза → огонь, растёт из центра
    bar_m = _gradient_mat('LogoBar', srgb((0.45, 0.95, 1.0)), srgb((1.0, 0.5, 0.15)), 5.0, f0 + 20)
    me = bpy.data.meshes.new('LogoBar')
    w, h, sk = 0.78, 0.016, 0.05
    me.from_pydata([(-w, -h, 0), (w - sk, -h, 0), (w, h, 0), (-w + sk, h, 0)], [], [(0, 1, 2, 3)]); me.materials.append(bar_m)
    bar = bpy.data.objects.new('LogoBar', me); coll.objects.link(bar)
    tag = None
    items = [(spell, (x0 - 0.1, y0, -4.0), (x0 - 1.4, y0, -4.0)), (strike, (x0 + 0.08, y0 - size * 1.02, -4.0), (x0 + 1.4, y0 - size * 1.02, -4.0))]
    # мягкая тёмная подложка: логотип читается поверх светлых трибун
    bm_ = bpy.data.materials.new('LogoShade'); bm_.use_nodes = True
    N = bm_.node_tree.nodes; L = bm_.node_tree.links
    for x in list(N):
        if x.type != 'OUTPUT_MATERIAL': N.remove(x)
    outn = next(x for x in N if x.type == 'OUTPUT_MATERIAL')
    tc = N.new('ShaderNodeTexCoord'); gr = N.new('ShaderNodeTexGradient'); gr.gradient_type = 'SPHERICAL'
    mp = N.new('ShaderNodeMapping'); mp.inputs['Location'].default_value = (-0.5, -0.5, 0); mp.inputs['Scale'].default_value = (2.0, 2.0, 1)
    L.new(tc.outputs['Generated'], mp.inputs['Vector']); L.new(mp.outputs['Vector'], gr.inputs['Vector'])
    fac = N.new('ShaderNodeMath'); fac.operation = 'MULTIPLY'; L.new(gr.outputs['Fac'], fac.inputs[0])
    tr = N.new('ShaderNodeBsdfTransparent'); bl = N.new('ShaderNodeEmission'); bl.inputs['Color'].default_value = (0, 0, 0, 1)
    mx = N.new('ShaderNodeMixShader'); L.new(fac.outputs[0], mx.inputs['Fac']); L.new(tr.outputs[0], mx.inputs[1]); L.new(bl.outputs[0], mx.inputs[2])
    L.new(mx.outputs[0], outn.inputs['Surface'])
    try: bm_.surface_render_method = 'BLENDED'
    except Exception: pass
    for f, v in ((1, 0.0), (f0 - 2, 0.0), (f0 + 8, 1.0)):
        fac.inputs[1].default_value = v; fac.inputs[1].keyframe_insert('default_value', frame=f)
    me_s = bpy.data.meshes.new('LogoShade'); me_s.from_pydata([(-1.6, -0.75, 0), (1.6, -0.75, 0), (1.6, 0.75, 0), (-1.6, 0.75, 0)], [], [(0, 1, 2, 3)])
    me_s.materials.append(bm_)
    shade = bpy.data.objects.new('LogoShade', me_s); coll.objects.link(shade); shade.parent = cam
    shade.location = (x0 * 0.6, y0 - size * 1.15, -4.4); shade.scale = (1.0 + abs(x0) * 0.6, 1.15, 1)
    for o, end, start in items:
        o.parent = cam; o.rotation_euler = (0, turn, 0)
        # влетают с разных сторон, с перелётом и растяжкой
        for f, loc, s in ((1, start, 0.0), (f0 - 1, start, 0.0), (f0, start, 0.9), (f0 + 6, (end[0] + (0.05 if start[0] < 0 else -0.05), end[1], end[2]), 1.06),
                          (f0 + 10, end, 0.98), (f0 + 14, end, 1.0)):
            o.location = loc; o.scale = (s, s, s)
            o.keyframe_insert('location', frame=f); o.keyframe_insert('scale', frame=f)
    bar.parent = cam
    bar.location = (x0, y0 - size * 1.02 - size * 0.62, -4.0); bar.rotation_euler = (0, turn, 0)
    for f, sx in ((1, 0.0), (f0 + 7, 0.0), (f0 + 15, 1.0)):
        bar.scale = (max(sx, 0.001), 1, 1); bar.keyframe_insert('scale', frame=f)
    sub = None
    if subtitle:
        cs = bpy.data.curves.new('LogoSub', 'FONT'); cs.body = subtitle; cs.font = font
        cs.size = size * 0.4; cs.align_x = 'CENTER'; cs.align_y = 'CENTER'; cs.space_character = 1.35
        cs.shear = 0.18; cs.extrude = 0.012 * bold; cs.bevel_depth = 0.003 * bold
        sm = _gradient_mat('LogoSub', srgb((1.0, 0.86, 0.45)), srgb((0.95, 0.62, 0.22)), 2.0 * glow, f0 + 30, depth)
        cs.materials.append(sm)
        sub = bpy.data.objects.new('LogoSub', cs); coll.objects.link(sub); sub.parent = cam
        y_sub = bar.location.y - size * 0.42
        for f, y, s in ((1, y_sub - 0.05, 0.001), (f0 + 17, y_sub - 0.05, 0.001), (f0 + 18, y_sub - 0.05, 0.92), (f0 + 26, y_sub, 1.0)):
            sub.location = (x0, y, -4.0); sub.scale = (s, s, s); sub.rotation_euler = (0, turn, 0)
            sub.keyframe_insert('location', frame=f); sub.keyframe_insert('scale', frame=f)
    if tagline:
        cu = bpy.data.curves.new('LogoTag', 'FONT'); cu.body = tagline; cu.font = font
        cu.size = size * 0.33; cu.align_x = 'CENTER'; cu.align_y = 'CENTER'; cu.space_character = 1.7
        tm = _flat_mat('LogoTag', srgb((0.86, 0.9, 1.0)), 2.2 * glow, alpha=1.0)
        cu.materials.append(tm)
        tag = bpy.data.objects.new('LogoTag', cu); coll.objects.link(tag); tag.parent = cam
        tag.location = (x0, bar.location.y - size * (0.92 if subtitle else 0.42), -4.0); tag.rotation_euler = (0, turn, 0)
        a = next(n for n in tm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Alpha']
        for f, v in ((1, 0.0), (f0 + 24, 0.0), (f0 + 36, 1.0)):
            a.default_value = v; a.keyframe_insert('default_value', frame=f)
        for f, y in ((f0 + 24, tag.location.y - 0.06), (f0 + 36, tag.location.y)):
            tag.location.y = y; tag.keyframe_insert('location', frame=f)
    for o in (spell, strike, bar) + ((tag,) if tag else ()) + ((sub,) if sub else ()):
        try: o.visible_shadow = False
        except Exception: pass
    return spell, strike, bar, tag
