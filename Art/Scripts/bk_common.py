"""Shared helpers for the Birimiz Kovulacak Blender asset scripts (run headless: blender -b -P <script>).

Conventions
- Units are metres. Blender (x, y, z) corresponds to Unity (x, z, y); a prop's usable *front* faces Blender -Y
  (Unity -Z), so the Unity builder rotates each prop by the yaw of the person using it.
- Pivots are at the floor/bottom centre unless stated otherwise.
- Materials are named after Art/palette.json; Unity remaps imported materials to project materials by name.
- Modifiers stay live in the .blend (editable sources); the FBX exporter applies them.
"""
import bpy
import bmesh
import json
import math
import os
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PALETTE_PATH = os.path.join(ROOT, "Art", "palette.json")
BLEND_DIR = os.path.join(ROOT, "Art", "Blender")
EXPORT_DIR = os.path.join(ROOT, "Art", "Export")
UNITY_MODELS = os.path.join(ROOT, "Game", "Assets", "Game", "Art", "Models")
PREVIEW_DIR = os.path.join(ROOT, "Art", "Previews")

_palette = None


def palette():
    global _palette
    if _palette is None:
        with open(PALETTE_PATH, "r", encoding="utf-8") as f:
            _palette = json.load(f)
    return _palette


def hex_to_linear(hex_str):
    h = hex_str.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]

    def lin(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return [lin(c) for c in srgb]


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)


def mat(name):
    """Get or create a Principled material from the palette."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = palette()["materials"].get(name)
    if spec is None:
        raise KeyError("Material not in palette: " + name)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    r, g, b = hex_to_linear(spec["color"])
    bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0 - spec.get("smooth", 0.3)
    bsdf.inputs["Metallic"].default_value = spec.get("metal", 0.0)
    if spec.get("emit", 0) > 0:
        bsdf.inputs["Emission Color"].default_value = (r, g, b, 1.0)
        bsdf.inputs["Emission Strength"].default_value = spec["emit"]
    if spec.get("alpha", 1.0) < 1.0:
        bsdf.inputs["Alpha"].default_value = spec["alpha"]
    m.diffuse_color = (r, g, b, spec.get("alpha", 1.0))
    return m


def collection(name, parent=None):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(c)
    return c


def _link(obj, coll):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)


def empty(name, coll, location=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.2
    e.location = location
    coll.objects.link(e)
    if parent is not None:
        e.parent = parent
    return e


def mesh_obj(name, bm, coll, material=None, parent=None, location=(0, 0, 0), smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    coll.objects.link(obj)
    obj.location = location
    if material is not None:
        if isinstance(material, (list, tuple)):
            for m in material:
                me.materials.append(mat(m))
        else:
            me.materials.append(mat(material))
    for p in me.polygons:
        p.use_smooth = smooth
    if parent is not None:
        obj.parent = parent
    return obj


def _bm_box(size, center=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    sx, sy, sz = size
    for v in bm.verts:
        v.co = Vector((v.co.x * sx + center[0], v.co.y * sy + center[1], v.co.z * sz + center[2]))
    return bm


def box(name, size, coll, material, parent=None, location=(0, 0, 0), bevel=0.0, segments=2, smooth=None):
    """Box of `size` centred at `location` (relative to parent). Optional live bevel + weighted normals."""
    bm = _bm_box(size)
    obj = mesh_obj(name, bm, coll, material, parent, location, smooth=bool(bevel) if smooth is None else smooth)
    if bevel > 0:
        add_bevel(obj, bevel, segments)
    return obj


def add_bevel(obj, width, segments=2, angle_deg=40):
    b = obj.modifiers.new("Bevel", "BEVEL")
    b.width = width
    b.segments = segments
    b.limit_method = "ANGLE"
    b.angle_limit = math.radians(angle_deg)
    b.harden_normals = False
    for p in obj.data.polygons:
        p.use_smooth = True
    wn = obj.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    wn.keep_sharp = True
    wn.mode = "FACE_AREA"
    return b


def cylinder(name, radius, depth, coll, material, parent=None, location=(0, 0, 0), segments=20, smooth=True,
             rotation=(0, 0, 0), cap_bevel=0.0, radius_top=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                          radius1=radius, radius2=radius if radius_top is None else radius_top, depth=depth)
    obj = mesh_obj(name, bm, coll, material, parent, location, smooth=smooth)
    obj.rotation_euler = rotation
    if cap_bevel > 0:
        add_bevel(obj, cap_bevel, 2, 50)
    return obj


def sphere(name, radius, coll, material, parent=None, location=(0, 0, 0), scale=(1, 1, 1), segments=20, rings=12,
           smooth=True):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    for v in bm.verts:
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2]))
    return mesh_obj(name, bm, coll, material, parent, location, smooth=smooth)


def ico(name, radius, coll, material, parent=None, location=(0, 0, 0), scale=(1, 1, 1), subdiv=1, smooth=False, jitter=0.0, seed=1):
    import random
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    for v in bm.verts:
        j = 1.0 + (rnd.uniform(-jitter, jitter) if jitter else 0.0)
        v.co = Vector((v.co.x * scale[0] * j, v.co.y * scale[1] * j, v.co.z * scale[2] * j))
    return mesh_obj(name, bm, coll, material, parent, location, smooth=smooth)


def torus(name, major, minor, coll, material, parent=None, location=(0, 0, 0), rotation=(0, 0, 0), major_seg=24,
          minor_seg=10, arc=2 * math.pi, smooth=True):
    """Torus (or partial torus when arc < 2*pi) in the XY plane."""
    bm = bmesh.new()
    rings = []
    steps = major_seg if arc >= 2 * math.pi - 1e-4 else major_seg + 1
    for i in range(steps):
        a = arc * i / major_seg
        ring = []
        for j in range(minor_seg):
            b = 2 * math.pi * j / minor_seg
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        rings.append(ring)
    closed = arc >= 2 * math.pi - 1e-4
    count = len(rings) if closed else len(rings) - 1
    for i in range(count):
        r0 = rings[i]
        r1 = rings[(i + 1) % len(rings)]
        for j in range(minor_seg):
            bm.faces.new((r0[j], r0[(j + 1) % minor_seg], r1[(j + 1) % minor_seg], r1[j]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    obj = mesh_obj(name, bm, coll, material, parent, location, smooth=smooth)
    obj.rotation_euler = rotation
    return obj


def lathe(name, profile, coll, material, parent=None, location=(0, 0, 0), segments=24, smooth=True, scale=(1, 1, 1)):
    """Revolve a (radius, z) profile around Z. Profile should start and end at radius 0 for a closed solid."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        ring = []
        if r <= 1e-5:
            ring = [bm.verts.new((0, 0, z * scale[2]))]
        else:
            for i in range(segments):
                a = 2 * math.pi * i / segments
                ring.append(bm.verts.new((r * math.cos(a) * scale[0], r * math.sin(a) * scale[1], z * scale[2])))
        rings.append(ring)
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for i in range(segments):
                bm.faces.new((a[0], b[(i + 1) % segments], b[i]))
        elif len(b) == 1:
            for i in range(segments):
                bm.faces.new((a[i], a[(i + 1) % segments], b[0]))
        else:
            for i in range(segments):
                bm.faces.new((a[i], a[(i + 1) % segments], b[(i + 1) % segments], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, coll, material, parent, location, smooth=smooth)


def prism_nameplate(name, length, depth, height, coll, material, parent=None, location=(0, 0, 0)):
    """Triangular desk sign: ridge along X, slanted faces toward -Y and +Y."""
    bm = bmesh.new()
    hl, hd = length / 2, depth / 2
    v = [bm.verts.new(p) for p in [(-hl, -hd, 0), (hl, -hd, 0), (hl, hd, 0), (-hl, hd, 0), (-hl, 0, height), (hl, 0, height)]]
    bm.faces.new((v[0], v[1], v[5], v[4]))
    bm.faces.new((v[2], v[3], v[4], v[5]))
    bm.faces.new((v[0], v[4], v[3]))
    bm.faces.new((v[1], v[2], v[5]))
    bm.faces.new((v[0], v[3], v[2], v[1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, coll, material, parent, location)


def flat_blob(name, rx, ry, thickness, coll, material, parent=None, location=(0, 0, 0), points=14, seed=3, wobble=0.25):
    """Irregular flat puddle/stain."""
    import random
    rnd = random.Random(seed)
    bm = bmesh.new()
    top, bottom = [], []
    for i in range(points):
        a = 2 * math.pi * i / points
        k = 1.0 + rnd.uniform(-wobble, wobble)
        x, y = math.cos(a) * rx * k, math.sin(a) * ry * k
        top.append(bm.verts.new((x, y, thickness)))
        bottom.append(bm.verts.new((x, y, 0)))
    bm.faces.new(top)
    bm.faces.new(list(reversed(bottom)))
    for i in range(points):
        j = (i + 1) % points
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, coll, material, parent, location, smooth=False)


def select_hierarchy(root):
    bpy.ops.object.select_all(action="DESELECT")
    stack = [root]
    while stack:
        o = stack.pop()
        o.hide_set(False)
        o.select_set(True)
        stack.extend(o.children)
    bpy.context.view_layer.objects.active = root


def export_fbx(root, path, animations=False):
    """Export root and its children. Axis conversion is baked on the Unity side (bakeAxisConversion)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_hierarchy(root)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"EMPTY", "MESH", "ARMATURE"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_space_transform=not animations,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        bake_anim=animations,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=animations,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
    )


def save_blend(name):
    os.makedirs(BLEND_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND_DIR, name), compress=True)


def setup_preview_render(resolution=(900, 700)):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("PreviewWorld")
    world.color = (0.82, 0.84, 0.88)
    scene.world = world
    scene.display.shading.background_type = "WORLD"


def render_preview(path, target=(0, 0, 0.5), distance=4.0, elevation_deg=25, azimuth_deg=-35, lens=50, ortho_scale=None):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = lens
    if ortho_scale:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho_scale
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    el, az = math.radians(elevation_deg), math.radians(azimuth_deg)
    t = Vector(target)
    offset = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * distance
    cam.location = t + offset
    direction = t - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
