"""
Upper GI Bleed — art generated in Blender from code.

Builds and exports, into the output folder (default UpperGIBleed/Assets/Art):

  tract.fbx            the upper GI tract as one tube mesh: oesophagus (collapsed, star-shaped
                       lumen), gastro-oesophageal junction, stomach with rugae and the incisura,
                       antrum, pyloric rosette, duodenal bulb (D1). Axis: the tube runs along +Y in
                       Blender, which Unity imports as +Z. w (depth along the tract) = Y.
  tract_albedo.png     baked mucosa colour (u = angle round the wall, v = depth / END)
  props.fbx            ulcer crater, varix cord, banded nodule, wash nozzle, cautery probe,
                       banding cap, scope shaft — each a separate object at the origin. Lathed props
                       are built round Blender's Z, which Unity imports as +Y (ProcedureView.Prop
                       re-orients them), and Blender +X arrives mirrored (Tract mirrors the mesh).
  avatars/avatar_N.png six clinician portraits (flat style) for the player profile

Run from the project folder:

  /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
      --python art/blender/tract.py -- UpperGIBleed/Assets/Art

The anatomy tables here mirror web/config/anatomy.js and UpperGIBleed/Assets/Scripts/Anatomy.cs.
"""
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGS[0] if ARGS else "UpperGIBleed/Assets/Art")
ONLY = ARGS[1] if len(ARGS) > 1 else None          # "tract" | "props" | "avatars" to build one part
os.makedirs(OUT, exist_ok=True)
os.makedirs(os.path.join(OUT, "avatars"), exist_ok=True)

# ----------------------------------------------------------------------------- anatomy
END = 22.8
ZLINE = 7.0
RADIUS = [(0, 0.58), (5.5, 0.60), (6.7, 0.68), (7.15, 1.25), (8.3, 1.75), (13.5, 1.8), (16.5, 1.3), (17.9, 1.0), (18.4, 0.74), (18.9, 0.96), (19.6, 0.98), (22.8, 0.76)]
PATH_X = [(0, 0), (14.5, 0), (16.2, 0.35), (17.5, 0.55), (18.4, 0.1), (19.0, 0), (21.5, 0), (22.8, 1.3)]
PATH_Y = [(0, 0), (14.5, 0), (16.2, -0.25), (17.5, -0.15), (18.4, 0), (22.0, 0), (22.8, 0.25)]
PALETTE = [(7.0, (214, 160, 152)), (14.6, (206, 92, 74)), (18.2, (218, 114, 94)), (22.9, (226, 152, 120))]
FOLDS = [  # (w, a0 deg, sweep deg, prot, uniform)
    (7.35, 95, 150, 0.20, False), (9.6, 200, 130, 0.14, False), (11.6, -40, 120, 0.14, False),
    (14.9, -85, 135, 0.50, False), (18.4, 0, 360, 0.58, True),
]
TAU = math.tau


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def tab(t, w):
    if w <= t[0][0]:
        return t[0][1]
    for i in range(1, len(t)):
        if w <= t[i][0]:
            f = smooth(t[i - 1][0], t[i][0], w)
            return t[i - 1][1] + (t[i][1] - t[i - 1][1]) * f
    return t[-1][1]


def R0(w):
    return tab(RADIUS, w)


def px(w):
    return tab(PATH_X, w)


def py(w):
    return tab(PATH_Y, w)


def star_amp(w):
    return 0.24 * (1 - smooth(ZLINE - 1.0, ZLINE + 0.1, w))


def hash01(a, b):
    h = (a * 374761393 + b * 668265263) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFFFF) / 4294967295.0


def relief(th, w):
    """Wall radius multiplier (1 = base radius): folds, rugae, ridges, rosette."""
    m = 1.0 - star_amp(w) * 0.5 * (1 + math.cos(5 * th))
    if w < ZLINE - 0.2:                                  # oesophageal longitudinal ridges
        m -= 0.025 * (1 + math.cos(5 * th + 0.4 * math.sin(w * 2.1)))
    if ZLINE < w < 14.6:                                 # gastric rugae: wavy longitudinal folds
        f = smooth(ZLINE, ZLINE + 0.8, w) * (1 - smooth(13.8, 14.6, w))
        m -= 0.06 * f * (0.5 + 0.5 * math.sin(7 * th + 0.6 * math.sin(w * 0.9) + w * 0.15)) ** 2
    if 14.6 <= w < 18.3:                                 # antral folds, fainter
        m -= 0.025 * (0.5 + 0.5 * math.sin(6 * th + w))
    for (fw, a0, sweep, prot, uniform) in FOLDS:        # circular folds and the pylorus
        dw = w - fw
        if abs(dw) > 0.35:
            continue
        prof = math.exp(-(dw / 0.14) ** 2)
        if uniform:
            ang = 1.0
            ridge = 0.5 + 0.5 * math.cos(9 * th)       # rosette ridges converging on the opening
            m -= prot * prof * (0.75 + 0.25 * ridge)
        else:
            d = ((th - math.radians(a0)) % TAU)
            sw = math.radians(sweep)
            if d > sw:
                continue
            u = d / sw
            ang = math.sin(math.pi * u) ** 0.7
            m -= prot * prof * ang
    return max(0.08, m)


def radius(th, w):
    return R0(w) * relief(th, w)


def wall_point(th, w):
    r = radius(th, w)
    # along +Y; Blender z is negated because Unity's FBX import (plus the tube's 90° turn in Tract.Setup) maps Blender z to Unity -Y
    return Vector((px(w) + r * math.cos(th), w, -(py(w) + r * math.sin(th))))


# ----------------------------------------------------------------------------- helpers
def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.engine = "CYCLES"
    bpy.context.scene.cycles.samples = 1
    bpy.context.scene.cycles.device = "CPU"


def new_mesh_object(name, verts, faces, uvs=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if uvs:
        uv = me.uv_layers.new(name="UVMap")
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                uv.data[li].uv = uvs[vi]
    return ob


def smooth_shade(ob):
    for p in ob.data.polygons:
        p.use_smooth = True


def mat_simple(name, rgb, rough=0.5, emit=0.0, alpha=1.0, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit:
        bsdf.inputs["Emission Color"].default_value = (rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, 1)
        bsdf.inputs["Emission Strength"].default_value = emit
    if alpha < 1:
        bsdf.inputs["Alpha"].default_value = alpha
        m.blend_method = "BLEND"
    return m


def export_fbx(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False,
                             bake_anim=False, path_mode="COPY", embed_textures=False)
    print("wrote", path)


# ----------------------------------------------------------------------------- tract
def build_tract():
    clear_scene()
    NA, STEP = 96, 0.05
    nb = int(END / STEP) + 1
    verts, uvs = [], []
    for j in range(nb):
        w = min(END, j * STEP)
        for a in range(NA):
            th = a * TAU / NA
            verts.append(wall_point(th, w))
            uvs.append((a / NA, w / END))
    # duplicate the seam column so the UV wraps cleanly
    faces = []
    for j in range(nb - 1):
        for a in range(NA):
            a1 = (a + 1) % NA
            i0, i1, i2, i3 = j * NA + a, j * NA + a1, (j + 1) * NA + a1, (j + 1) * NA + a
            faces.append((i0, i3, i2, i1))      # inward-facing normals: the camera is inside the tube
    ob = new_mesh_object("Tract", verts, faces)
    me = ob.data
    # per-loop UVs so the seam column maps to u=1 rather than u=0
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            j, a = divmod(vi, NA)
            u = a / NA
            # a face that straddles the seam (contains a == 0 and a == NA-1) maps its a==0 corner to 1.0
            corners = [me.loops[x].vertex_index % NA for x in poly.loop_indices]
            if 0 in corners and (NA - 1) in corners and a == 0:
                u = 1.0
            uv.data[li].uv = (u, min(END, j * STEP) / END)
    smooth_shade(ob)

    # ---- procedural mucosa material (baked to a texture below)
    m = bpy.data.materials.new("Mucosa")
    m.use_nodes = True
    nt = m.node_tree
    nodes, links = nt.nodes, nt.links
    for n in list(nodes):
        nodes.remove(n)
    out = nodes.new("ShaderNodeOutputMaterial")
    emit = nodes.new("ShaderNodeEmission")
    links.new(emit.outputs[0], out.inputs[0])
    texco = nodes.new("ShaderNodeTexCoord")
    sep = nodes.new("ShaderNodeSeparateXYZ")
    links.new(texco.outputs["UV"], sep.inputs[0])
    # region colour ramp along v (depth)
    ramp = nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.interpolation = "LINEAR"
    stops = [(0.0, (214, 160, 152)), (ZLINE / END - 0.004, (214, 160, 152)), (ZLINE / END + 0.004, (206, 92, 74)),
             (14.2 / END, (206, 92, 74)), (14.9 / END, (218, 114, 94)), (18.2 / END, (218, 114, 94)),
             (18.6 / END, (226, 152, 120)), (1.0, (226, 152, 120))]
    while len(cr.elements) < len(stops):
        cr.elements.new(0.5)
    for el, (pos, rgb) in zip(cr.elements, stops):
        el.position = pos
        el.color = (rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, 1)
    zj = nodes.new("ShaderNodeTexNoise")                       # jagged Z-line: perturb the depth that feeds the ramp
    zj.inputs["Scale"].default_value = 18.0
    zj.inputs["Detail"].default_value = 2.0
    links.new(texco.outputs["UV"], zj.inputs["Vector"])
    zjm = nodes.new("ShaderNodeMath")
    zjm.operation = "MULTIPLY_ADD"
    zjm.inputs[1].default_value = 0.022
    zjm.inputs[2].default_value = -0.011
    links.new(zj.outputs["Fac"], zjm.inputs[0])
    # only near the Z-line: weight = exp(-((v - zline)/0.02)^2)
    zd = nodes.new("ShaderNodeMath"); zd.operation = "SUBTRACT"; zd.inputs[1].default_value = ZLINE / END
    links.new(sep.outputs["Y"], zd.inputs[0])
    zd2 = nodes.new("ShaderNodeMath"); zd2.operation = "DIVIDE"; zd2.inputs[1].default_value = 0.02
    links.new(zd.outputs[0], zd2.inputs[0])
    zd3 = nodes.new("ShaderNodeMath"); zd3.operation = "MULTIPLY"
    links.new(zd2.outputs[0], zd3.inputs[0]); links.new(zd2.outputs[0], zd3.inputs[1])
    zd4 = nodes.new("ShaderNodeMath"); zd4.operation = "MULTIPLY"; zd4.inputs[1].default_value = -1.0
    links.new(zd3.outputs[0], zd4.inputs[0])
    zd5 = nodes.new("ShaderNodeMath"); zd5.operation = "EXPONENT"
    links.new(zd4.outputs[0], zd5.inputs[0])
    zw = nodes.new("ShaderNodeMath"); zw.operation = "MULTIPLY"
    links.new(zjm.outputs[0], zw.inputs[0]); links.new(zd5.outputs[0], zw.inputs[1])
    vfac = nodes.new("ShaderNodeMath"); vfac.operation = "ADD"
    links.new(sep.outputs["Y"], vfac.inputs[0]); links.new(zw.outputs[0], vfac.inputs[1])
    links.new(vfac.outputs[0], ramp.inputs["Fac"])
    # jagged Z-line: perturb v near the junction with noise along u
    # (approximated by the ramp step width above plus the mottling)
    # mottling noise
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (1.0, END / (TAU * 1.05), 1.0)   # v spans the whole length; scale it so noise is isotropic on the wall
    links.new(texco.outputs["UV"], mapping.inputs["Vector"])
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 34.0
    noise.inputs["Detail"].default_value = 6.0
    noise.inputs["Roughness"].default_value = 0.6
    links.new(mapping.outputs[0], noise.inputs["Vector"])
    noise2 = nodes.new("ShaderNodeTexNoise")
    noise2.inputs["Scale"].default_value = 140.0
    noise2.inputs["Detail"].default_value = 3.0
    links.new(mapping.outputs[0], noise2.inputs["Vector"])
    # vessels: thin dark red lines from a voronoi edge
    vor = nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "DISTANCE_TO_EDGE"
    vor.inputs["Scale"].default_value = 40.0
    links.new(mapping.outputs[0], vor.inputs["Vector"])
    vmap = nodes.new("ShaderNodeMapRange")
    vmap.inputs["From Min"].default_value = 0.0
    vmap.inputs["From Max"].default_value = 0.025
    vmap.inputs["To Min"].default_value = 0.0
    vmap.inputs["To Max"].default_value = 1.0
    links.new(vor.outputs["Distance"], vmap.inputs["Value"])
    # combine: colour * (0.86 + 0.28*noise) mixed with vessel red
    mult = nodes.new("ShaderNodeMath")
    mult.operation = "MULTIPLY_ADD"
    mult.inputs[1].default_value = 0.30
    mult.inputs[2].default_value = 0.84
    links.new(noise.outputs["Fac"], mult.inputs[0])
    mult2 = nodes.new("ShaderNodeMath")
    mult2.operation = "MULTIPLY_ADD"
    mult2.inputs[1].default_value = 0.10
    mult2.inputs[2].default_value = 0.95
    links.new(noise2.outputs["Fac"], mult2.inputs[0])
    m1 = nodes.new("ShaderNodeMath")
    m1.operation = "MULTIPLY"
    links.new(mult.outputs[0], m1.inputs[0])
    links.new(mult2.outputs[0], m1.inputs[1])
    shade = nodes.new("ShaderNodeMixRGB")
    shade.blend_type = "MULTIPLY"
    shade.inputs["Fac"].default_value = 1.0
    links.new(ramp.outputs["Color"], shade.inputs["Color1"])
    # grey from m1
    grey = nodes.new("ShaderNodeCombineColor")
    links.new(m1.outputs[0], grey.inputs[0])
    links.new(m1.outputs[0], grey.inputs[1])
    links.new(m1.outputs[0], grey.inputs[2])
    links.new(grey.outputs[0], shade.inputs["Color2"])
    vessel = nodes.new("ShaderNodeMixRGB")
    vessel.blend_type = "MIX"
    vessel.inputs["Color2"].default_value = (0.42, 0.10, 0.11, 1)
    links.new(shade.outputs[0], vessel.inputs["Color1"])
    # vessel strength: stronger in the oesophagus (v < zline)
    vstr = nodes.new("ShaderNodeMath")
    vstr.operation = "MULTIPLY"
    inv = nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    links.new(vmap.outputs[0], inv.inputs[1])
    regionv = nodes.new("ShaderNodeMath")       # 0.55 in oesophagus, 0.25 elsewhere
    regionv.operation = "LESS_THAN"
    regionv.inputs[1].default_value = ZLINE / END
    links.new(sep.outputs["Y"], regionv.inputs[0])
    rv2 = nodes.new("ShaderNodeMath")
    rv2.operation = "MULTIPLY_ADD"
    rv2.inputs[1].default_value = 0.30
    rv2.inputs[2].default_value = 0.22
    links.new(regionv.outputs[0], rv2.inputs[0])
    links.new(inv.outputs[0], vstr.inputs[0])
    links.new(rv2.outputs[0], vstr.inputs[1])
    links.new(vstr.outputs[0], vessel.inputs["Fac"])
    links.new(vessel.outputs[0], emit.inputs["Color"])
    ob.data.materials.append(m)

    # ---- bake the emission to a texture
    W, H = 1024, 4096
    img = bpy.data.images.new("tract_albedo", W, H)
    texnode = nodes.new("ShaderNodeTexImage")
    texnode.image = img
    nodes.active = texnode
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.context.scene.render.bake.use_selected_to_active = False
    bpy.context.scene.render.bake.margin = 8
    bpy.ops.object.bake(type="EMIT")
    img.filepath_raw = os.path.join(OUT, "tract_albedo.png")
    img.file_format = "PNG"
    img.save()
    print("wrote", img.filepath_raw)

    # Unity will use its own material; swap to a plain one referencing the baked image for the FBX
    m2 = bpy.data.materials.new("Tract")
    m2.use_nodes = True
    bsdf = m2.node_tree.nodes["Principled BSDF"]
    ti = m2.node_tree.nodes.new("ShaderNodeTexImage")
    ti.image = img
    m2.node_tree.links.new(ti.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.25
    ob.data.materials.clear()
    ob.data.materials.append(m2)
    export_fbx(os.path.join(OUT, "tract.fbx"), [ob])


# ----------------------------------------------------------------------------- props
def ring_profile_object(name, profile, segs=32, color=(200, 160, 120), rough=0.5, alpha=1.0, emit=0.0):
    """Lathe a (radius, height) profile round the Z axis."""
    verts, faces = [], []
    n = len(profile)
    for i, (r, h) in enumerate(profile):
        for s in range(segs):
            th = s * TAU / segs
            verts.append((r * math.cos(th), r * math.sin(th), h))
    for i in range(n - 1):
        for s in range(segs):
            s1 = (s + 1) % segs
            faces.append((i * segs + s, i * segs + s1, (i + 1) * segs + s1, (i + 1) * segs + s))
    ob = new_mesh_object(name, verts, faces)
    smooth_shade(ob)
    ob.data.materials.append(mat_simple(name + "Mat", color, rough, emit, alpha))
    return ob


def build_props():
    clear_scene()
    objs = []
    # Ulcer: unit-radius crater, lying in the XY plane, Z up (= wall normal pointing into the lumen).
    prof = [(1.75, 0.0), (1.35, 0.03), (1.0, 0.09), (0.85, 0.07), (0.55, -0.04), (0.3, -0.09), (0.0, -0.11)]
    ulcer = ring_profile_object("Ulcer", prof, 48, (236, 212, 160), 0.45)
    ob = ulcer
    # vertex colours: red rim, cream crater, brown centre (Unity reads them via a vertex-colour shader if needed)
    objs.append(ob)
    # Varix cord: a bulging tube along Y, radius 1 (scaled in Unity), with a rupture point at the origin
    verts, faces = [], []
    L, segs, n = 6.0, 20, 40
    for i in range(n + 1):
        y = -L / 2 + L * i / n
        bulge = 1.0 + 0.35 * math.exp(-(y / 0.9) ** 2) + 0.12 * math.sin(y * 2.3)
        wig = 0.25 * math.sin(y * 1.3)
        for s in range(segs):
            th = s * TAU / segs
            r = bulge
            verts.append((wig + r * math.cos(th), y, r * 0.55 * (1 + math.sin(th)) / 2 + 0.15 * r * math.sin(th)))
    for i in range(n):
        for s in range(segs):
            s1 = (s + 1) % segs
            faces.append((i * segs + s, (i + 1) * segs + s, (i + 1) * segs + s1, i * segs + s1))
    varix = new_mesh_object("Varix", verts, faces)
    smooth_shade(varix)
    varix.data.materials.append(mat_simple("VarixMat", (78, 84, 160), 0.35))
    objs.append(varix)
    # Banded nodule: a strangulated bulb with an elastic band at its base
    nod = ring_profile_object("BandNodule", [(0.0, 1.6), (0.6, 1.45), (0.95, 1.0), (0.9, 0.5), (0.55, 0.25), (0.6, 0.12), (0.0, 0.0)], 32, (70, 28, 66), 0.5)
    objs.append(nod)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.62, minor_radius=0.1, major_segments=32, minor_segments=10, location=(0, 0, 0.2))
    band = bpy.context.active_object
    band.name = "Band"
    band.data.materials.append(mat_simple("BandMat", (196, 172, 120), 0.4))
    objs.append(band)
    # Tools: built along +Z with the working tip at the origin and the shaft going -Z
    wash = ring_profile_object("WashNozzle", [(0.0, 0.0), (0.05, 0.0), (0.09, -0.12), (0.11, -0.3), (0.11, -1.0)], 24, (170, 195, 210), 0.3)   # open-ended: the shaft continues it
    objs.append(wash)
    probe = ring_profile_object("CauteryProbe", [(0.0, 0.0), (0.07, -0.02), (0.085, -0.08), (0.06, -0.15), (0.09, -0.2), (0.09, -1.0)], 24, (36, 40, 44), 0.35)
    # metallic tip
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.075, segments=20, ring_count=12, location=(0, 0, 0))
    tip = bpy.context.active_object
    tip.name = "CauteryTip"
    tip.data.materials.append(mat_simple("CauteryTipMat", (220, 222, 225), 0.2, 0.0, 1.0, 0.8))
    objs += [probe, tip]
    cap = ring_profile_object("BandingCap", [(0.0, 0.0), (0.42, 0.0), (0.42, -0.55), (0.36, -0.55), (0.36, -0.03), (0.0, -0.03)], 32, (200, 190, 255), 0.15, 0.3)
    objs.append(cap)
    shaft = ring_profile_object("ScopeShaft", [(0.11, -2.6), (0.11, -0.2)], 24, (120, 125, 135), 0.4)
    objs.append(shaft)
    export_fbx(os.path.join(OUT, "props.fbx"), objs)


# ----------------------------------------------------------------------------- avatars
AVATARS = [  # (skin, scrub colour, cap, hair)
    ((233, 190, 160), (60, 120, 160), True, (60, 40, 30)),
    ((160, 110, 80), (40, 140, 110), False, (20, 15, 12)),
    ((210, 165, 140), (120, 70, 150), True, (120, 80, 40)),
    ((95, 65, 45), (200, 90, 70), False, (15, 12, 10)),
    ((245, 215, 195), (70, 90, 130), True, (210, 170, 90)),
    ((190, 140, 110), (210, 150, 60), False, (40, 30, 25)),
]


def build_avatars():
    for i, (skin, scrub, cap, hair) in enumerate(AVATARS, 1):
        clear_scene()
        sc = bpy.context.scene
        sc.render.engine = "BLENDER_EEVEE"
        sc.render.resolution_x = sc.render.resolution_y = 256
        sc.render.film_transparent = True
        bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, segments=48, ring_count=24, location=(0, 0, 1.2))
        head = bpy.context.active_object
        bpy.ops.object.shade_smooth()
        head.data.materials.append(mat_simple("Skin", skin, 0.6))
        bpy.ops.mesh.primitive_cylinder_add(radius=1.3, depth=1.4, vertices=48, location=(0, 0, -0.55))
        torso = bpy.context.active_object
        bpy.ops.object.shade_smooth()
        torso.data.materials.append(mat_simple("Scrub", scrub, 0.7))
        bpy.ops.mesh.primitive_cylinder_add(radius=0.32, depth=0.5, vertices=24, location=(0, 0, 0.3))
        neck = bpy.context.active_object
        neck.data.materials.append(mat_simple("Skin2", skin, 0.6))

        def cut(ob, loc, scale):
            bpy.ops.mesh.primitive_cube_add(size=2, location=loc)
            cutter = bpy.context.active_object
            cutter.scale = scale
            cutter.hide_render = True
            cutter.hide_viewport = True
            mod = ob.modifiers.new("cut", "BOOLEAN")
            mod.operation = "DIFFERENCE"
            mod.object = cutter
            mod.solver = "EXACT"

        if cap:                                                  # scrub cap: a dome that stops above the eyes
            bpy.ops.mesh.primitive_uv_sphere_add(radius=1.07, segments=48, ring_count=24, location=(0, 0, 1.2))
            c = bpy.context.active_object
            bpy.ops.object.shade_smooth()
            c.data.materials.append(mat_simple("Cap", (max(0, scrub[0] - 20), max(0, scrub[1] - 20), max(0, scrub[2] - 20)), 0.7))
            cut(c, (0, 0, -0.5), (3, 3, 2.0))                    # remove everything below z = 1.5
        else:                                                    # hair: a cap over the back and top, clear of the face
            bpy.ops.mesh.primitive_uv_sphere_add(radius=1.07, segments=48, ring_count=24, location=(0, 0, 1.2))
            h = bpy.context.active_object
            bpy.ops.object.shade_smooth()
            h.data.materials.append(mat_simple("Hair", hair, 0.8))
            cut(h, (0, 0, -1.2), (3, 3, 2.2))                    # below z = 1.0
            cut(h, (0, -2.4, 0.4), (3, 2.0, 1.25))               # the face: y < -0.4 and z < 1.65
        for sx in (-0.36, 0.36):                                 # eyes on the surface of the head
            d = Vector((sx, -0.9, 0.12)).normalized()
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.11, segments=16, ring_count=8, location=(Vector((0, 0, 1.2)) + d * 0.98))
            e = bpy.context.active_object
            e.data.materials.append(mat_simple("Eye", (25, 25, 30), 0.3))
        bpy.ops.mesh.primitive_torus_add(major_radius=0.22, minor_radius=0.035, major_segments=24, minor_segments=8,
                                         location=(0, -0.93, 0.9), rotation=(math.radians(90), 0, 0))
        mouth = bpy.context.active_object                        # a smile: the lower half of a thin ring
        mouth.data.materials.append(mat_simple("Mouth", (120, 50, 55), 0.5))
        cut(mouth, (0, -0.93, 1.9), (0.6, 0.6, 1.0))
        bpy.ops.mesh.primitive_torus_add(major_radius=0.95, minor_radius=0.06, major_segments=32, minor_segments=8,
                                         location=(0, -0.75, 0.0), rotation=(math.radians(75), 0, 0))
        st = bpy.context.active_object                           # stethoscope round the neck
        st.data.materials.append(mat_simple("Steth", (40, 40, 48), 0.4))
        bpy.ops.object.light_add(type="SUN", location=(2, -3, 5), rotation=(math.radians(50), math.radians(10), math.radians(20)))
        bpy.context.active_object.data.energy = 3.0
        bpy.ops.object.light_add(type="AREA", location=(-3, -3, 3))
        bpy.context.active_object.data.energy = 400
        bpy.ops.object.camera_add(location=(0, -6.2, 0.75), rotation=(math.radians(88), 0, 0))
        cam = bpy.context.active_object
        cam.data.lens = 60
        sc.camera = cam
        sc.view_settings.view_transform = "Standard"
        path = os.path.join(OUT, "avatars", f"avatar_{i}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("wrote", path)


if ONLY in (None, "tract"):
    build_tract()
if ONLY in (None, "props"):
    build_props()
if ONLY in (None, "avatars"):
    build_avatars()
print("DONE")
