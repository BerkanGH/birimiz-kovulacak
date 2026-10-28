"""Map/prop helpers shared by build_props.py, map_props_rooms.py, build_architecture.py and map_preview.py.

- Prop registry (`@prop`): every registered function builds one prop under a root empty at the origin.
- `merge_prop`: before FBX export, every prop is collapsed to as few mesh objects as possible (one body mesh with
  one submesh per material, plus the named sub-objects the Unity side drives). Fewer renderers = fewer
  GameObjects, less culling work and better static batching.
- Unity <-> Blender transforms: Unity (x, y, z), yaw in degrees (clockwise from +Z seen from above)
  -> Blender (x, z, y), rotation about Z = -yaw.
"""
import math
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402

import bk_common as bk  # noqa: E402

R = math.radians
PROPS = []

# Sub-objects the Unity side finds by name (FindDeep) and drives at runtime. They are never merged into the
# static body of their prop. Keep these names unique across all props (Blender appends .001 to duplicates).
DRIVEN = {
    "Drawer", "Screen", "Coffee", "Stripe", "Handset", "RingLight", "StatusLight", "Door", "Fabric", "Padlock",
    "Leds", "LedsB", "RecLight", "RouterLight", "RouterLight2", "FuseLight", "FuseLever", "EmLamp", "Glow",
    "TvScreen", "Steam", "PcLed",
}


def prop(fn):
    PROPS.append(fn)
    return fn


def root(name, c):
    return bk.empty(name, c)


def base_name(obj):
    n = obj.name
    i = n.rfind(".")
    return n[:i] if i > 0 and n[i + 1:].isdigit() else n


# ------------------------------------------------------------------------------------------------ geometry

def uv_plane(name, w, h, coll, material, parent=None, location=(0, 0, 0), facing="-Y"):
    """Rectangle with 0..1 UVs, facing -Y by default (a prop's front). Used for screens and decals."""
    bm = bmesh.new()
    hw, hh = w / 2, h / 2
    if facing == "-Y":
        pts = [(-hw, 0, -hh), (hw, 0, -hh), (hw, 0, hh), (-hw, 0, hh)]
    elif facing == "+Y":
        pts = [(hw, 0, -hh), (-hw, 0, -hh), (-hw, 0, hh), (hw, 0, hh)]
    else:  # +Z (floor decal)
        pts = [(-hw, -hh, 0), (hw, -hh, 0), (hw, hh, 0), (-hw, hh, 0)]
    vs = [bm.verts.new(p) for p in pts]
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.new("UVMap")
    for loop, co in zip(f.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
        loop[uv].uv = co
    return bk.mesh_obj(name, bm, coll, material, parent, location)


def quad_floor(bm, x0, y0, x1, y1, z, mat_index, mats_layer=None):
    vs = [bm.verts.new((x0, y0, z)), bm.verts.new((x1, y0, z)), bm.verts.new((x1, y1, z)), bm.verts.new((x0, y1, z))]
    f = bm.faces.new(vs)
    f.material_index = mat_index
    return f


def add_box_to_bm(bm, center, size, mat_index):
    """Axis-aligned box (6 quads) appended to an existing bmesh."""
    cx, cy, cz = center
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
    v = [bm.verts.new((cx + dx * sx, cy + dy * sy, cz + dz * sz)) for dx in (-1, 1) for dy in (-1, 1) for dz in (-1, 1)]
    # index = (dx>0)*4 + (dy>0)*2 + (dz>0)
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    out = []
    for f in faces:
        face = bm.faces.new([v[i] for i in f])
        face.material_index = mat_index
        out.append(face)
    return out


def multi_box_obj(name, boxes, coll, parent=None, location=(0, 0, 0)):
    """One mesh object from many boxes: boxes = [(center, size, material_name), ...]. Cheap static geometry."""
    mats = []
    bm = bmesh.new()
    for center, size, m in boxes:
        if m not in mats:
            mats.append(m)
        add_box_to_bm(bm, center, size, mats.index(m))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bk.mesh_obj(name, bm, coll, mats, parent, location)


def cable(name, points, radius, coll, material, parent=None, segments=6):
    """Tube along a polyline (Blender coordinates)."""
    bm = bmesh.new()
    rings = []
    pts = [Vector(p) for p in points]
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
        a = d.cross(up).normalized()
        b = d.cross(a).normalized()
        ring = []
        for k in range(segments):
            t = 2 * math.pi * k / segments
            ring.append(bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * radius))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(segments):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % segments], rings[i + 1][(k + 1) % segments], rings[i + 1][k]))
    return bk.mesh_obj(name, bm, coll, material, parent, smooth=True)


# ------------------------------------------------------------------------------------------------ merging

def _bake_modifiers(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if not o.modifiers:
            continue
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        o.modifiers.clear()
        old = o.data
        o.data = me
        if old.users == 0:
            bpy.data.meshes.remove(old)


def _join(objs, name, owner):
    if not objs:
        return None
    _bake_modifiers(objs)
    target = objs[0]
    if len(objs) > 1:
        with bpy.context.temp_override(active_object=target, object=target, selected_objects=objs, selected_editable_objects=objs):
            bpy.ops.object.join()
    target.name = name
    target.data.name = name
    mw = target.matrix_world.copy()
    target.parent = owner
    target.matrix_world = mw
    return target


def merge_prop(r):
    """Collapse the hierarchy under root `r`: non-driven meshes merge into '<Root>Body' (or '<Driven>Body' inside a
    driven empty); driven mesh nodes are kept as they are (their mesh children merge into them)."""
    bpy.context.view_layer.update()
    groups = []  # (owner, [meshes])

    def collect(node, bucket):
        for ch in list(node.children):
            if base_name(ch) in DRIVEN:
                if ch.type == "MESH":
                    sub = [ch]
                    collect(ch, sub)
                    groups.append((ch, sub))
                else:
                    sub = []
                    collect(ch, sub)
                    groups.append((ch, sub))
            else:
                if ch.type == "MESH":
                    bucket.append(ch)
                collect(ch, bucket)

    body = []
    collect(r, body)
    groups.append((r, body))
    for owner, objs in groups:
        if owner.type == "MESH":
            # driven mesh: merge children into it, keep its name/transform
            if len(objs) > 1:
                keep_name = owner.name
                _bake_modifiers(objs)
                with bpy.context.temp_override(active_object=owner, object=owner, selected_objects=objs, selected_editable_objects=objs):
                    bpy.ops.object.join()
                owner.name = keep_name
            else:
                _bake_modifiers(objs)
        elif objs:
            _join(objs, base_name(owner) + "Body", owner)


def export_prop(r, path):
    """FBX-export one prop with exact driven names (temporarily taking a name from another prop if Blender had to
    suffix it, e.g. the Stripe of MailEnvelope vs Nameplate)."""
    swaps = []
    stack = [r]
    while stack:
        o = stack.pop()
        stack.extend(o.children)
        b = base_name(o)
        if b != o.name and b in DRIVEN:
            holder = bpy.data.objects.get(b)
            if holder is not None:
                holder.name = b + "__swap"
                swaps.append((holder, b))
            old = o.name
            o.name = b
            swaps.append((o, old))
    try:
        bk.export_fbx(r, path)
    finally:
        for obj, name in reversed(swaps):
            obj.name = name


def tri_count(r):
    n = 0
    dg = bpy.context.evaluated_depsgraph_get()
    stack = [r]
    while stack:
        o = stack.pop()
        stack.extend(o.children)
        if o.type == "MESH":
            me = o.evaluated_get(dg).to_mesh()
            n += sum(len(p.vertices) - 2 for p in me.polygons)
            o.evaluated_get(dg).to_mesh_clear()
    return n


# ------------------------------------------------------------------------------------------------ transforms

def u2b(p):
    """Unity position (x, y, z) -> Blender (x, z, y)."""
    return (p[0], p[2], p[1])


def yaw2rot(yaw_deg):
    return (0.0, 0.0, -math.radians(yaw_deg))
