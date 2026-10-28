"""Shared bean rig + pose authoring helpers (players and managers use the same bone names and hierarchy).

Bones (character faces Blender +Y; +X is the character's right):
  Root (ground)
    Body (pivot near the floor; squash/stretch + lean of the whole bean)
      Head (upper bean incl. face)
        Top (hair bun / pompom / curls / hat: secondary wobble)
      Tail (scarf tail / drawstrings: secondary swing)
      Arm.L -> Hand.L, Arm.R -> Hand.R (upper arm, forearm+mitten; 2-bone noodle arms)
    Foot.L, Foot.R

Poses are authored in *character space* and converted to bone-local quaternions here, so clips never depend on
bone roll conventions:
  body/head: lean (deg forward), turn (deg to the character's left), tilt (deg to the character's right),
             sq (vertical scale; horizontal = 1/sqrt(v) keeps volume), up/fwd/side (metres)
  hands: target positions of the mitten centres in the Body's rest frame + an elbow pole hint (2-bone IK)
  feet: offsets (side, fwd, up) + pitch (deg toe up)
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30
BONES = ["Root", "Body", "Head", "Top", "Tail", "Arm.L", "Hand.L", "Arm.R", "Hand.R", "Foot.L", "Foot.R"]

# Rest layout for scale 1 (employee). Managers use the same layout scaled.
LAYOUT = {
    "Root": ((0, 0, 0), (0, 0, 0.10), None),
    "Body": ((0, 0, 0.06), (0, 0, 0.60), "Root"),
    "Head": ((0, 0, 0.60), (0, 0, 1.06), "Body"),
    "Top": ((0, 0, 1.06), (0, 0, 1.20), "Head"),
    "Tail": ((0.14, 0.30, 0.58), (0.15, 0.32, 0.34), "Body"),
    "Arm.L": ((-0.28, 0.04, 0.56), (-0.385, 0.03, 0.40), "Body"),
    "Hand.L": ((-0.385, 0.03, 0.40), (-0.455, 0.02, 0.225), "Arm.L"),
    "Arm.R": ((0.28, 0.04, 0.56), (0.385, 0.03, 0.40), "Body"),
    "Hand.R": ((0.385, 0.03, 0.40), (0.455, 0.02, 0.225), "Arm.R"),
    "Foot.L": ((-0.12, -0.03, 0.05), (-0.12, 0.12, 0.05), "Root"),
    "Foot.R": ((0.12, -0.03, 0.05), (0.12, 0.12, 0.05), "Root"),
}


# Managers are wider beans: shoulders further out (applied before the 1.2 scale).
MANAGER_LAYOUT = {
    "Arm.L": ((-0.31, 0.04, 0.56), (-0.435, 0.03, 0.40), "Body"),
    "Hand.L": ((-0.435, 0.03, 0.40), (-0.505, 0.02, 0.225), "Arm.L"),
    "Arm.R": ((0.31, 0.04, 0.56), (0.435, 0.03, 0.40), "Body"),
    "Hand.R": ((0.435, 0.03, 0.40), (0.505, 0.02, 0.225), "Arm.R"),
}

MAX_STRETCH = 0.14  # rubber-hose: the forearm may slide this far (x scale) along the upper arm to reach a target


class Rig:
    """Wraps the armature object with rest data needed to convert character-space poses."""

    def __init__(self, obj, s):
        self.obj = obj
        self.s = s
        self.rest_q = {}
        self.head = {}
        self.tail = {}
        for b in obj.data.bones:
            self.rest_q[b.name] = b.matrix_local.to_quaternion()
            self.head[b.name] = b.head_local.copy()
            self.tail[b.name] = b.tail_local.copy()
        self.len_up = {k: (self.tail["Arm." + k] - self.head["Arm." + k]).length for k in "LR"}
        self.len_fore = {k: (self.tail["Hand." + k] - self.head["Hand." + k]).length for k in "LR"}

    def rest_hand(self, side):
        return self.tail["Hand." + side].copy()


def make_rig(coll, parent, s=1.0, name="Rig", layout=None):
    data = bpy.data.armatures.new(name + "Data")
    obj = bpy.data.objects.new(name, data)
    coll.objects.link(obj)
    obj.parent = parent
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    lay = dict(LAYOUT)
    lay.update(layout or {})
    for n in BONES:
        h, t, par = lay[n]
        b = data.edit_bones.new(n)
        b.head = Vector(h) * s
        b.tail = Vector(t) * s
        b.roll = 0.0
        if par:
            b.parent = data.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
    obj.show_in_front = False
    return Rig(obj, s)


# ----------------------------------------------------------------------------------------------- skinning

def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def weights_rigid(obj, bone):
    vg = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")


def weights_fn(obj, fn):
    """fn(world_co) -> {bone: weight}. Weights are normalised."""
    mw = obj.matrix_world
    groups = {}
    for v in obj.data.vertices:
        w = fn(mw @ v.co)
        tot = sum(w.values()) or 1.0
        for bone, x in w.items():
            if x <= 1e-4:
                continue
            g = groups.get(bone)
            if g is None:
                g = groups[bone] = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            g.add([v.index], x / tot, "REPLACE")


def body_head_fn(s, z0=0.50, z1=0.70):
    def fn(co):
        t = smoothstep(z0 * s, z1 * s, co.z)
        return {"Body": 1 - t, "Head": t}
    return fn


def head_top_fn(s, z0=0.98, z1=1.16):
    def fn(co):
        t = smoothstep(z0 * s, z1 * s, co.z)
        return {"Head": 1 - t, "Top": t}
    return fn


def bind(obj, rig_obj):
    mw = obj.matrix_world.copy()
    obj.parent = rig_obj
    obj.matrix_world = mw
    m = obj.modifiers.new("Armature", "ARMATURE")
    m.object = rig_obj
    return obj


# ------------------------------------------------------------------------------------------------- poses

def qx(deg):
    return Quaternion((1, 0, 0), math.radians(deg))


def qy(deg):
    return Quaternion((0, 1, 0), math.radians(deg))


def qz(deg):
    return Quaternion((0, 0, 1), math.radians(deg))


def ik2(S, T, L1, L2, pole):
    """Two-bone IK. Returns (upper dir, fore dir, reached target)."""
    d = T - S
    dist = d.length
    lo, hi = abs(L1 - L2) + 1e-3, L1 + L2 - 1e-3
    dist_c = min(max(dist, lo), hi)
    u = d.normalized() if dist > 1e-6 else Vector((0, 0, -1))
    pole = Vector(pole)
    w = pole - u * pole.dot(u)
    if w.length < 1e-5:
        w = Vector((0, 1, 0)) - u * u.y
    w.normalize()
    cos_a = (L1 * L1 + dist_c * dist_c - L2 * L2) / (2 * L1 * dist_c)
    a = math.acos(max(-1.0, min(1.0, cos_a)))
    E = S + (u * math.cos(a) + w * math.sin(a)) * L1
    Tc = S + u * dist_c
    return (E - S).normalized(), (Tc - E).normalized(), Tc


class Pose:
    """Character-space pose -> per-bone (loc, quat, scale) in bone-local space."""

    def __init__(self, body=None, head=None, hands=None, feet=None, root=None, top=None, tail=None):
        self.body = body or {}
        self.head = head or {}
        self.hands = hands or {}
        self.feet = feet or {}
        self.root = root or {}
        self.top = top or {}
        self.tail = tail or {}

    def copy(self, **over):
        p = Pose(dict(self.body), dict(self.head), dict(self.hands), dict(self.feet), dict(self.root), dict(self.top), dict(self.tail))
        for k, v in over.items():
            cur = getattr(p, k)
            cur.update(v)
        return p


def _rot(spec):
    return qz(spec.get("turn", 0)) @ qx(-spec.get("lean", 0)) @ qy(spec.get("tilt", 0))


def _sq(spec):
    v = spec.get("sq", 1.0)
    h = spec.get("sqh", 1.0 / math.sqrt(v))
    return Vector((h, v, h))


def _local(rig, bone, d_arm):
    R = rig.rest_q[bone]
    return R.inverted() @ d_arm @ R


def _loc_local(rig, bone, v_arm):
    return rig.rest_q[bone].inverted() @ Vector(v_arm)


def solve(rig, pose):
    s = rig.s
    out = {}
    one = Vector((1, 1, 1))
    r = pose.root
    out["Root"] = (_loc_local(rig, "Root", (r.get("side", 0) * s, r.get("fwd", 0) * s, r.get("up", 0) * s)), _local(rig, "Root", qz(r.get("turn", 0))), one)
    b = pose.body
    out["Body"] = (_loc_local(rig, "Body", (b.get("side", 0) * s, b.get("fwd", 0) * s, b.get("up", 0) * s)), _local(rig, "Body", _rot(b)), _sq(b))
    h = pose.head
    out["Head"] = (_loc_local(rig, "Head", (h.get("side", 0) * s, h.get("fwd", 0) * s, h.get("up", 0) * s)), _local(rig, "Head", _rot(h)), _sq(h))
    t = pose.top
    out["Top"] = (Vector(), _local(rig, "Top", qx(-t.get("lean", 0)) @ qy(t.get("tilt", 0))), Vector((1, t.get("sq", 1.0), 1)))
    tl = pose.tail
    out["Tail"] = (Vector(), _local(rig, "Tail", qy(tl.get("side", 0)) @ qx(tl.get("swing", 0))), one)

    for side, sx in (("L", -1), ("R", 1)):
        S = rig.head["Arm." + side]
        target = pose.hands.get(side)
        if target is None:
            # hanging arm, optionally swung forward (deg) around the shoulder and raised outward (deg)
            swing = pose.hands.get("swing" + side, 0.0)
            raise_ = pose.hands.get("out" + side, 0.0)
            rest = rig.rest_hand(side) - S
            q = qx(swing) @ qy(-sx * raise_)
            T = S + q @ rest
            bend = pose.hands.get("bend" + side, 12.0)
            pole = Vector((sx * 0.3, -1.0, 0.0))
        else:
            T = Vector(target) * s
            bend = None
            pole = Vector(pose.hands.get("pole" + side, (sx * 1.0, -0.6, -0.6)))
        L1, L2 = rig.len_up[side], rig.len_fore[side]
        if bend is not None:
            # keep a natural elbow bend for hanging arms: pull the target slightly toward the shoulder
            d = T - S
            T = S + d * (math.cos(math.radians(bend / 2)))
        stretch = min(max((T - S).length - (L1 + L2) + 0.004 * s, 0.0), MAX_STRETCH * s)
        du, df, _ = ik2(S, T, L1 + stretch, L2, pole)
        rest_u = (rig.tail["Arm." + side] - S).normalized()
        rest_f = (rig.tail["Hand." + side] - rig.head["Hand." + side]).normalized()
        D_up = rest_u.rotation_difference(du)
        D_fore = (D_up @ rest_f).rotation_difference(df) @ D_up
        twist = pose.hands.get("twist" + side, 0.0)
        if twist:
            D_fore = Quaternion(df, math.radians(twist)) @ D_fore
        out["Arm." + side] = (Vector(), _local(rig, "Arm." + side, D_up), one)
        out["Hand." + side] = (_loc_local(rig, "Hand." + side, rest_u * stretch), _local(rig, "Hand." + side, D_up.inverted() @ D_fore), one)

    for side in ("L", "R"):
        f = pose.feet.get(side, ())
        f = tuple(f) + (0,) * (4 - len(f))
        dx, dy, dz, pitch = f
        out["Foot." + side] = (_loc_local(rig, "Foot." + side, (dx * s, dy * s, dz * s)), _local(rig, "Foot." + side, qx(pitch)), one)
    return out


# --------------------------------------------------------------------------------------------- actions

def make_action(rig, name, keys, loop=True, interp="BEZIER"):
    """keys: list of (frame, Pose). Every bone/channel is keyed on every key frame.
    Looping clips: the last key should repeat the first pose; the exported range stops one frame before it so
    Unity's loop is seamless."""
    obj = rig.obj
    if obj.animation_data is None:
        obj.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    obj.animation_data.action = act
    prev = {}
    for frame, pose in keys:
        solved = solve(rig, pose)
        for bname in BONES:
            loc, q, sc = solved[bname]
            p = prev.get(bname)
            if p is not None and p.dot(q) < 0:
                q = -q
            prev[bname] = q
            pb = obj.pose.bones[bname]
            pb.location = loc
            pb.rotation_quaternion = q
            pb.scale = sc
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
    f0, f1 = keys[0][0], keys[-1][0]
    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = interp
            kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"
        if loop:
            fc.modifiers.new("CYCLES")
        fc.update()
    act.use_frame_range = True
    act.frame_start = f0
    act.frame_end = (f1 - 1) if loop else f1
    act.use_cyclic = loop
    return act


def _fcurves(act):
    """Blender 4.4+ layered actions keep F-curves in channelbags; fall back to the legacy list."""
    try:
        curves = []
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    curves.extend(bag.fcurves)
        if curves:
            return curves
    except AttributeError:
        pass
    return list(getattr(act, "fcurves", []))


def reset_pose(rig):
    obj = rig.obj
    if obj.animation_data:
        obj.animation_data.action = None
    for pb in obj.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def apply_pose(rig, pose):
    """Static pose (previews / tests)."""
    solved = solve(rig, pose)
    for bname, (loc, q, sc) in solved.items():
        pb = rig.obj.pose.bones[bname]
        pb.location, pb.rotation_quaternion, pb.scale = loc, q, sc
    bpy.context.view_layer.update()
