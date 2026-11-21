"""
Hero render: the distal oesophagus seen through an endoscope, with one small oozing variceal bleed.
Everything is procedural (no textures): run headless with

  Blender --background --factory-startup --python art/blender/oesophagus_render.py -- <out.png> [quick]

Units: 1 = about 2 cm. The tube runs from the camera (z = 0) down -Z towards the gastro-oesophageal junction.
"""
import bpy, bmesh, math, sys, random
from mathutils import Vector, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp/oesophagus.png"
QUICK = "quick" in argv
random.seed(11)

# ----------------------------------------------------------------------------- scene / render
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
try:
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "METAL"
    prefs.get_devices()
    for d in prefs.devices: d.use = True
    scene.cycles.device = "GPU"
except Exception as e:
    print("GPU setup failed, CPU:", e)
scene.cycles.samples = 48 if QUICK else 256
scene.cycles.use_adaptive_sampling = True; scene.cycles.adaptive_threshold = 0.02
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 8
scene.cycles.transparent_max_bounces = 4
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
scene.render.resolution_percentage = 50 if QUICK else 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = OUT
try:
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
except Exception: pass
scene.view_settings.exposure = 0.15
world = bpy.data.worlds.new("World"); scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)

# ----------------------------------------------------------------------------- the lumen geometry
L = 7.0                      # length down -Z
NZ, NT = 220, 128            # rings, verts per ring

def axis(s):
    """Centre line: gentle curve to the left and drooping distally (so blood runs away from the camera)."""
    return Vector((0.42 * math.sin(max(s, 0) * 2.3), -0.30 * max(s, 0) ** 2, -s * L))

def base_radius(s):
    r = 1.0 - 0.32 * max(s, 0)
    if s > 0.86:
        k = (s - 0.86) / 0.14
        r *= 1 - 0.96 * k * k        # the junction closes to a puckered rosette
    return r

def wall_radius(th, s):
    """Lumen radius at angle th (0 = +X, pi/2 = +Y which is up in the image) and depth s."""
    R = base_radius(s)
    A = 0.13 + 0.20 * max(s, 0) ** 1.4                # longitudinal folds, more prominent distally
    ph = 0.6 * s
    f = 0.5 * (1 + math.cos(4 * th + ph)); f = f ** 2.2
    f2 = 0.5 * (1 + math.cos(9 * th - 1.3 + 2 * s)) ** 3 * 0.25   # smaller secondary ridges
    n = noise.noise(Vector((math.cos(th) * 2.2, math.sin(th) * 2.2, s * 7.0))) * 0.035
    n2 = noise.noise(Vector((math.cos(th) * 6, math.sin(th) * 6, s * 18.0))) * 0.012
    return R * (1 - A * (f + f2)) + n + n2

def wall(th, s):
    r = wall_radius(th, s)
    return axis(s) + Vector((math.cos(th) * r, math.sin(th) * r, 0))

def inward(th, s):
    """Approximate inward normal of the wall."""
    p = wall(th, s)
    e = 0.01
    du = wall(th + e, s) - wall(th - e, s)
    dv = wall(th, s + e / 4) - wall(th, s - e / 4)
    n = du.cross(dv).normalized()
    if n.dot(axis(s) - p) < 0: n = -n
    return n

bm = bmesh.new()
rings = []
for i in range(NZ + 1):
    s = -0.10 + 1.10 * i / NZ
    ring = []
    for j in range(NT):
        th = 2 * math.pi * j / NT
        ring.append(bm.verts.new(wall(th, s)))
    rings.append(ring)
for i in range(NZ):
    for j in range(NT):
        a, b = rings[i][j], rings[i][(j + 1) % NT]
        c, d = rings[i + 1][(j + 1) % NT], rings[i + 1][j]
        bm.faces.new((a, b, c, d))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bmesh.ops.reverse_faces(bm, faces=bm.faces)          # the camera is inside: normals point into the lumen
mesh = bpy.data.meshes.new("Oesophagus"); bm.to_mesh(mesh); bm.free()
mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
tube = bpy.data.objects.new("Oesophagus", mesh); scene.collection.objects.link(tube)
sub = tube.modifiers.new("Subdivision", "SUBSURF"); sub.levels = 1; sub.render_levels = 2

# ----------------------------------------------------------------------------- materials
def new_mat(name):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    return m, nt, bsdf

def node(nt, kind, **kw):
    n = nt.nodes.new(kind)
    for k, v in kw.items(): setattr(n, k, v)
    return n

def mucosa_material(name, tint=None, tint_amount=0.0, cap_strength=0.6, bump=0.32, sss=0.22):
    m, nt, bsdf = new_mat(name)
    link = nt.links.new
    tc = node(nt, "ShaderNodeTexCoord")
    # slow warp so nothing looks tiled
    warp = node(nt, "ShaderNodeTexNoise"); warp.inputs["Scale"].default_value = 2.6; warp.inputs["Detail"].default_value = 4
    link(tc.outputs["Object"], warp.inputs["Vector"])
    warped = node(nt, "ShaderNodeVectorMath", operation="ADD")
    scale_w = node(nt, "ShaderNodeVectorMath", operation="SCALE"); scale_w.inputs[3].default_value = 0.6
    link(warp.outputs["Color"], scale_w.inputs[0]); link(tc.outputs["Object"], warped.inputs[0]); link(scale_w.outputs[0], warped.inputs[1])

    # colour: salmon-pink mucosa with blotchy variation
    col_noise = node(nt, "ShaderNodeTexNoise"); col_noise.inputs["Scale"].default_value = 2.4; col_noise.inputs["Detail"].default_value = 6; col_noise.inputs["Roughness"].default_value = 0.55
    link(warped.outputs[0], col_noise.inputs["Vector"])
    ramp = node(nt, "ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35; ramp.color_ramp.elements[0].color = (0.66, 0.25, 0.25, 1)
    ramp.color_ramp.elements[1].position = 0.70; ramp.color_ramp.elements[1].color = (0.88, 0.50, 0.44, 1)
    link(col_noise.outputs["Fac"], ramp.inputs["Fac"])

    # capillary network: distance-to-edge voronoi in two scales
    def capillaries(scale, width):
        v = node(nt, "ShaderNodeTexVoronoi"); v.feature = "DISTANCE_TO_EDGE"; v.inputs["Scale"].default_value = scale
        v.inputs["Randomness"].default_value = 1.0
        link(warped.outputs[0], v.inputs["Vector"])
        r = node(nt, "ShaderNodeMapRange"); r.inputs["From Min"].default_value = 0.0; r.inputs["From Max"].default_value = width
        r.inputs["To Min"].default_value = 1.0; r.inputs["To Max"].default_value = 0.0
        link(v.outputs["Distance"], r.inputs["Value"])
        return r
    c1 = capillaries(15.0, 0.016); c2 = capillaries(42.0, 0.009)
    cmix = node(nt, "ShaderNodeMath", operation="MAXIMUM")
    c2s = node(nt, "ShaderNodeMath", operation="MULTIPLY"); c2s.inputs[1].default_value = 0.55
    link(c2.outputs[0], c2s.inputs[0]); link(c1.outputs[0], cmix.inputs[0]); link(c2s.outputs[0], cmix.inputs[1])
    # fade the capillaries in and out in patches
    patch = node(nt, "ShaderNodeTexNoise"); patch.inputs["Scale"].default_value = 1.1; patch.inputs["Detail"].default_value = 2
    link(warped.outputs[0], patch.inputs["Vector"])
    patch_r = node(nt, "ShaderNodeMapRange"); patch_r.inputs["From Min"].default_value = 0.35; patch_r.inputs["From Max"].default_value = 0.7
    link(patch.outputs["Fac"], patch_r.inputs["Value"])
    cap = node(nt, "ShaderNodeMath", operation="MULTIPLY"); link(cmix.outputs[0], cap.inputs[0]); link(patch_r.outputs[0], cap.inputs[1])
    cap_s = node(nt, "ShaderNodeMath", operation="MULTIPLY"); cap_s.inputs[1].default_value = cap_strength
    link(cap.outputs[0], cap_s.inputs[0])
    red = node(nt, "ShaderNodeMixRGB"); red.blend_type = "MIX"; red.inputs["Color2"].default_value = (0.50, 0.10, 0.10, 1)
    link(cap_s.outputs[0], red.inputs["Fac"]); link(ramp.outputs["Color"], red.inputs["Color1"])
    colour_out = red
    if tint is not None:
        t = node(nt, "ShaderNodeMixRGB"); t.inputs["Fac"].default_value = tint_amount; t.inputs["Color2"].default_value = (*tint, 1)
        link(red.outputs["Color"], t.inputs["Color1"]); colour_out = t
    link(colour_out.outputs["Color"], bsdf.inputs["Base Color"])

    # wet, translucent flesh
    bsdf.inputs["Subsurface Weight"].default_value = sss
    bsdf.inputs["Subsurface Radius"].default_value = (0.14, 0.04, 0.02)
    bsdf.inputs["Subsurface Scale"].default_value = 0.15
    bsdf.inputs["Roughness"].default_value = 0.42
    bsdf.inputs["IOR"].default_value = 1.4
    bsdf.inputs["Coat Weight"].default_value = 1.0
    bsdf.inputs["Coat Roughness"].default_value = 0.10
    bsdf.inputs["Coat IOR"].default_value = 1.4

    # surface relief: fine granular texture, the capillaries very slightly sunk, soft undulations
    fine = node(nt, "ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 38; fine.inputs["Detail"].default_value = 7; fine.inputs["Roughness"].default_value = 0.6
    link(warped.outputs[0], fine.inputs["Vector"])
    soft = node(nt, "ShaderNodeTexNoise"); soft.inputs["Scale"].default_value = 5; soft.inputs["Detail"].default_value = 3
    link(warped.outputs[0], soft.inputs["Vector"])
    h1 = node(nt, "ShaderNodeMath", operation="MULTIPLY_ADD"); h1.inputs[1].default_value = 0.6; link(fine.outputs["Fac"], h1.inputs[0]); link(soft.outputs["Fac"], h1.inputs[2])
    h2 = node(nt, "ShaderNodeMath", operation="SUBTRACT"); link(h1.outputs[0], h2.inputs[0])
    capd = node(nt, "ShaderNodeMath", operation="MULTIPLY"); capd.inputs[1].default_value = 0.35; link(cap.outputs[0], capd.inputs[0]); link(capd.outputs[0], h2.inputs[1])
    bmp = node(nt, "ShaderNodeBump"); bmp.inputs["Strength"].default_value = bump; bmp.inputs["Distance"].default_value = 0.03
    link(h2.outputs[0], bmp.inputs["Height"]); link(bmp.outputs["Normal"], bsdf.inputs["Normal"])
    return m

mucosa = mucosa_material("Mucosa", cap_strength=0.65)
tube.data.materials.append(mucosa)
# varices: the same mucosa stretched thin over a blue-violet column, with dusky red wale streaks
varix_mat = mucosa_material("Varix", tint=(0.15, 0.09, 0.30), tint_amount=0.80, cap_strength=0.8, bump=0.15, sss=0.05)

def blood_material():
    m, nt, bsdf = new_mat("Blood")
    link = nt.links.new
    bsdf.inputs["Base Color"].default_value = (0.42, 0.02, 0.012, 1)
    bsdf.inputs["Subsurface Weight"].default_value = 0.5
    bsdf.inputs["Subsurface Radius"].default_value = (0.30, 0.03, 0.02)
    bsdf.inputs["Subsurface Scale"].default_value = 0.08
    bsdf.inputs["Roughness"].default_value = 0.10
    bsdf.inputs["IOR"].default_value = 1.45
    bsdf.inputs["Coat Weight"].default_value = 1.0
    bsdf.inputs["Coat Roughness"].default_value = 0.02
    # thin edges glow brighter red
    lw = node(nt, "ShaderNodeLayerWeight"); lw.inputs["Blend"].default_value = 0.6
    mix = node(nt, "ShaderNodeMixRGB"); mix.inputs["Color1"].default_value = (0.22, 0.008, 0.006, 1); mix.inputs["Color2"].default_value = (0.62, 0.05, 0.03, 1)
    link(lw.outputs["Facing"], mix.inputs["Fac"]); link(mix.outputs["Color"], bsdf.inputs["Base Color"])
    return m
blood = blood_material()

# ----------------------------------------------------------------------------- varices
def poly_curve(name, pts, radii, mat, bevel_res=10, scale_y=1.0):
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"
    cu.bevel_depth = 1.0; cu.bevel_resolution = bevel_res; cu.use_fill_caps = True
    sp = cu.splines.new("NURBS"); sp.points.add(len(pts) - 1)
    for p, (v, r) in zip(sp.points, zip(pts, radii)):
        p.co = (v.x, v.y, v.z, 1.0); p.radius = r
    sp.use_endpoint_u = True; sp.order_u = 4; sp.resolution_u = 6
    ob = bpy.data.objects.new(name, cu); scene.collection.objects.link(ob)
    cu.materials.append(mat)
    return ob

def varix(name, th0, ph, s0=-0.12, s1=0.985, size=0.075):
    """A serpentine submucosal column running the length of the view: full size behind the camera, tapering only distally."""
    pts, radii = [], []
    n = 110
    for i in range(n + 1):
        s = s0 + (s1 - s0) * i / n
        th = th0 + 0.30 * math.sin(s * 5.5 + ph) + 0.09 * math.sin(s * 16 + 2 * ph)
        t = (s - s0) / (s1 - s0)
        taper = min(1.0, ((1 - t) / 0.15)) ** 0.6
        rv = size * base_radius(s) * (1 + 0.40 * math.sin(s * 10.5 + ph)) * max(taper, 0.05)
        p = wall(th, s) + inward(th, s) * (0.22 * rv)      # mostly buried in the wall, bulging into the lumen
        pts.append(p); radii.append(rv)
    return poly_curve(name, pts, radii, varix_mat)

TH_BLEED = math.radians(232)          # lower-left (about 7-8 o'clock in the image)
v_bleed = varix("Varix.bleeding", TH_BLEED, 0.4, size=0.082)
varix("Varix.2", math.radians(318), 2.1, size=0.07)
varix("Varix.3", math.radians(105), 4.0, size=0.062)
varix("Varix.4", math.radians(20), 5.3, size=0.05)

# ----------------------------------------------------------------------------- the bleed
S_B = 0.36
def varix_surface(th0, ph, s, size):
    th = th0 + 0.30 * math.sin(s * 5.5 + ph) + 0.09 * math.sin(s * 16 + 2 * ph)
    rv = size * base_radius(s) * (1 + 0.40 * math.sin(s * 10.5 + ph))
    n = inward(th, s)
    return wall(th, s) + n * (0.22 * rv + rv * 0.92), th, n, rv

bp, th_b, n_b, rv_b = varix_surface(TH_BLEED, 0.4, S_B, 0.082)
# bleeding point: a tiny dark nipple on the varix with a fresh clot
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.04, segments=32, ring_count=16, location=bp - n_b * 0.008)
nip = bpy.context.object; nip.name = "BleedingPoint"; nip.scale = (1, 1, 0.75)
nip.data.materials.append(blood); nip.data.polygons.foreach_set("use_smooth", [True] * len(nip.data.polygons))

# trickle: runs off the varix, down the wall to the dependent (6 o'clock) side, then along the floor towards the camera
pts, radii = [], []
n = 110
TH_DOWN = math.radians(268)
S_END = S_B - 0.11
for i in range(n + 1):
    t = i / n
    if t < 0.40:
        u = t / 0.40; u = u * u * (3 - 2 * u)
        th = th_b + (TH_DOWN - th_b) * u + 0.025 * math.sin(t * 60)
        s = S_B + 0.02 * u
        lift = (1 - u) * (0.22 * rv_b + rv_b * 0.6)           # starts on the varix, settles on the wall
        r = 0.030 * (0.6 + 0.4 * (1 - u)) * (0.4 + 0.6 * min(1, t / 0.05))
    else:
        u = (t - 0.40) / 0.60
        th = TH_DOWN + 0.05 * math.sin(u * 9) + 0.02 * math.sin(u * 31)
        s = S_B + 0.02 + (S_END - S_B - 0.02) * u
        lift = 0
        r = 0.030 * (1 + 0.4 * math.sin(u * 7)) * (1 - 0.5 * u ** 2)
    p = wall(th, s) + inward(th, s) * (lift + r * 0.35)
    pts.append(p); radii.append(max(r, 0.004))
trickle = poly_curve("Trickle", pts, radii, blood, bevel_res=8)
# a small pool where the trickle ends
p_end = wall(TH_DOWN, S_END); n_end = inward(TH_DOWN, S_END)
bpy.ops.mesh.primitive_uv_sphere_add(radius=1, segments=48, ring_count=24, location=p_end + n_end * 0.006)
pool = bpy.context.object; pool.name = "Pool"; pool.scale = (0.09, 0.02, 0.16)
pool.data.materials.append(blood); pool.data.polygons.foreach_set("use_smooth", [True] * len(pool.data.polygons))

# ----------------------------------------------------------------------------- camera and endoscope light
cam_data = bpy.data.cameras.new("Cam"); cam_data.lens = 13.5; cam_data.sensor_width = 36
cam_data.dof.use_dof = True; cam_data.dof.focus_distance = 1.8; cam_data.dof.aperture_fstop = 8.0
cam = bpy.data.objects.new("Camera", cam_data); scene.collection.objects.link(cam); scene.camera = cam
cam.location = (0.02, 0.05, 0.0)
cam.rotation_euler = (math.radians(-7), math.radians(-4), 0)      # a little down and left, towards the bleed

def spot(name, offset, power):
    ld = bpy.data.lights.new(name, "SPOT"); ld.energy = power; ld.spot_size = math.radians(135); ld.spot_blend = 0.35
    ld.shadow_soft_size = 0.09; ld.color = (1.0, 0.93, 0.85)
    lo = bpy.data.objects.new(name, ld); scene.collection.objects.link(lo)
    lo.parent = cam; lo.location = offset; lo.rotation_euler = (0, 0, 0)
    return lo
spot("Light.L", (-0.075, -0.045, -0.02), 24)
spot("Light.R", (0.075, -0.045, -0.02), 24)
spot("Light.Fill", (0, 0.06, 0.05), 4)

# ----------------------------------------------------------------------------- render
bpy.ops.render.render(write_still=True)
print("RENDERED", OUT, "quick" if QUICK else "full")
