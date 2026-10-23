"""Builds the shared employee rig (4 appearances via accessories + skin material) and the manager.

Run:  blender -b --factory-startup -P Art/Scripts/build_characters.py [-- --previews]
Outputs: Art/Blender/characters.blend, Game/Assets/Game/Art/Models/Characters/{Employee,Manager}.fbx,
         Art/Previews/characters/*.png

Characters face Blender +Y (= Unity +Z, transform.forward). Bones (all roll 0):
  Root (ground) -> Body (vertical) -> Head (vertical); Body -> Arm.L / Arm.R (horizontal, pointing outward);
  Root -> Foot.L / Foot.R (pointing forward).
Local rotation conventions used by the actions below (degrees, XYZ Euler):
  Body/Head: -X leans forward, +Y turns to the character's left, +Z rolls to the character's left.
  Arm.L: -Y swings forward, +Y backward; Arm.R: +Y swings forward, -Y backward; +X raises either arm outward/up.
  Foot: location +Y steps forward, +Z lifts.
"""
import bpy
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bk_common as bk  # noqa: E402
from mathutils import Vector  # noqa: E402

FPS = 30
BONES = ["Root", "Body", "Head", "Arm.L", "Arm.R", "Foot.L", "Foot.R"]

EMPLOYEE_PROFILE = [(0, 0.06), (0.18, 0.065), (0.28, 0.10), (0.335, 0.18), (0.355, 0.32), (0.35, 0.48),
                    (0.33, 0.64), (0.295, 0.78), (0.24, 0.90), (0.16, 1.0), (0.08, 1.06), (0, 1.08)]
MANAGER_PROFILE = [(0, 0.06), (0.2, 0.065), (0.31, 0.10), (0.37, 0.18), (0.395, 0.32), (0.39, 0.50),
                   (0.37, 0.66), (0.33, 0.80), (0.27, 0.92), (0.18, 1.02), (0.08, 1.08), (0, 1.10)]
DEPTH = 0.88  # front-back flattening


def radius_at(profile, z):
    for (r0, z0), (r1, z1) in zip(profile, profile[1:]):
        if z0 <= z <= z1:
            t = 0 if z1 == z0 else (z - z0) / (z1 - z0)
            return r0 + (r1 - r0) * t
    return 0.0


# ----------------------------------------------------------------------------------------------------- rig

def make_rig(coll, parent, s=1.0):
    data = bpy.data.armatures.new("RigData")
    rig = bpy.data.objects.new("Rig", data)
    coll.objects.link(rig)
    rig.parent = parent
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(n, h, t, par=None):
        b = data.edit_bones.new(n)
        b.head = Vector(h) * s
        b.tail = Vector(t) * s
        b.roll = 0.0
        if par:
            b.parent = data.edit_bones[par]
        return b

    bone("Root", (0, 0, 0), (0, 0, 0.12))
    bone("Body", (0, 0, 0.12), (0, 0, 0.62), "Root")
    bone("Head", (0, 0, 0.62), (0, 0, 1.12), "Body")
    bone("Arm.L", (-0.30, 0, 0.56), (-0.42, 0, 0.56), "Body")
    bone("Arm.R", (0.30, 0, 0.56), (0.42, 0, 0.56), "Body")
    bone("Foot.L", (-0.13, -0.06, 0.05), (-0.13, 0.10, 0.05), "Root")
    bone("Foot.R", (0.13, -0.06, 0.05), (0.13, 0.10, 0.05), "Root")
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
    return rig


def assign_group(obj, bone):
    vg = obj.vertex_groups.new(name=bone)
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")


def assign_body_head_blend(obj, z_body, z_head):
    vb = obj.vertex_groups.new(name="Body")
    vh = obj.vertex_groups.new(name="Head")
    mw = obj.matrix_world
    for v in obj.data.vertices:
        z = (mw @ v.co).z
        t = min(1.0, max(0.0, (z - z_body) / (z_head - z_body)))
        if t < 1.0:
            vb.add([v.index], 1.0 - t, "REPLACE")
        if t > 0.0:
            vh.add([v.index], t, "REPLACE")


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = objs[0]
    o.name = name
    o.data.name = name
    return o


def bind(obj, rig):
    mw = obj.matrix_world.copy()
    obj.parent = rig
    obj.matrix_world = mw
    m = obj.modifiers.new("Armature", "ARMATURE")
    m.object = rig
    return obj


# ----------------------------------------------------------------------------------------------- meshes

def build_employee(coll):
    root = bk.empty("Employee", coll)
    rig = make_rig(coll, root)
    parts = []

    body = bk.lathe("BodyMesh", EMPLOYEE_PROFILE, coll, "M_SkinBlue", segments=28, scale=(1, DEPTH, 1))
    assign_body_head_blend(body, 0.56, 0.70)
    parts.append(body)

    face = []
    for sx in (-1, 1):
        face.append(bk.sphere("EyeWhite", 0.075, coll, "M_Eye", location=(0.095 * sx, 0.235, 0.80), scale=(1, 0.6, 1.15), segments=16, rings=10))
        face.append(bk.sphere("Pupil", 0.036, coll, "M_Pupil", location=(0.095 * sx, 0.276, 0.79), scale=(1, 0.5, 1.1), segments=12, rings=8))
        brow = bk.box("Brow", (0.075, 0.018, 0.016), coll, "M_Brow", location=(0.095 * sx, 0.215, 0.925))
        brow.rotation_euler = (0, math.radians(-10 * sx), 0)
        face.append(brow)
    face.append(bk.torus("Mouth", 0.035, 0.009, coll, "M_Mouth", location=(0, 0.275, 0.68),
                         rotation=(math.radians(-90), 0, 0), arc=math.pi, major_seg=10, minor_seg=6))
    for o in face:
        assign_group(o, "Head")
    parts.extend(face)

    for sx, bone in ((-1, "Arm.L"), (1, "Arm.R")):
        arm = bk.cylinder("Arm", 0.042, 0.29, coll, "M_SkinBlue", location=(0.35 * sx, 0.0, 0.42), segments=12)
        shoulder = bk.sphere("Shoulder", 0.046, coll, "M_SkinBlue", location=(0.33 * sx, 0, 0.56), segments=12, rings=8)
        hand = bk.sphere("Hand", 0.064, coll, "M_SkinBlue", location=(0.36 * sx, 0.01, 0.255), segments=14, rings=10)
        for o in (arm, shoulder, hand):
            assign_group(o, bone)
            parts.append(o)
    for sx, bone in ((-1, "Foot.L"), (1, "Foot.R")):
        foot = bk.sphere("Shoe", 0.085, coll, "M_Shoe", location=(0.13 * sx, 0.04, 0.042), scale=(0.8, 1.25, 0.5), segments=14, rings=8)
        assign_group(foot, bone)
        parts.append(foot)

    body = join(parts, "Body")
    bind(body, rig)

    # Accessories (toggled per appearance in Unity). Each is a separate rigidly skinned mesh.
    acc = {}
    # Collar + tie (Mavi red, Sarı green via material swap in Unity)
    collar_parts = []
    for sx in (-1, 1):
        c = bk.box("CollarFlap", (0.09, 0.03, 0.055), coll, "M_Collar", location=(0.055 * sx, 0.29, 0.6))
        c.rotation_euler = (math.radians(-10), math.radians(28 * sx), 0)
        collar_parts.append(c)
    for o in collar_parts:
        assign_group(o, "Body")
    acc["Acc_Collar"] = join(collar_parts, "Acc_Collar")

    import bmesh
    bm = bmesh.new()
    pts = [(0, 0.555), (-0.038, 0.50), (0, 0.36), (0.038, 0.50)]
    front = [bm.verts.new((x, 0.0, z)) for x, z in pts]
    back = [bm.verts.new((x, -0.016, z)) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((back[i], back[j], front[j], front[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    blade = bk.mesh_obj("TieBlade", bm, coll, "M_TieRed", location=(0, 0.322, 0))
    knot = bk.box("TieKnot", (0.05, 0.03, 0.04), coll, "M_TieRed", location=(0, 0.30, 0.575))
    for o in (blade, knot):
        assign_group(o, "Body")
    acc["Acc_Tie"] = join([blade, knot], "Acc_Tie")

    tuft = [
        bk.sphere("Tuft", 0.09, coll, "M_HairDark", location=(0, 0.05, 1.05), scale=(1.35, 0.9, 0.42)),
        bk.sphere("Tuft2", 0.06, coll, "M_HairDark", location=(0.07, 0.13, 1.02), scale=(1.2, 0.8, 0.45)),
        bk.sphere("Tuft3", 0.05, coll, "M_HairDark", location=(-0.06, 0.12, 1.035), scale=(1.1, 0.8, 0.5)),
    ]
    tuft[0].rotation_euler = (math.radians(12), 0, math.radians(10))
    for o in tuft:
        assign_group(o, "Head")
    acc["Acc_HairTuft"] = join(tuft, "Acc_HairTuft")

    glasses = []
    for sx in (-1, 1):
        glasses.append(bk.torus("Rim", 0.078, 0.011, coll, "M_Glasses", location=(0.095 * sx, 0.29, 0.80),
                                rotation=(math.radians(90), 0, 0), major_seg=20, minor_seg=6))
        t = bk.box("Temple", (0.012, 0.2, 0.012), coll, "M_Glasses", location=(0.215 * sx, 0.18, 0.815))
        t.rotation_euler = (0, 0, math.radians(-28 * sx))
        glasses.append(t)
    glasses.append(bk.box("Bridge", (0.04, 0.012, 0.012), coll, "M_Glasses", location=(0, 0.295, 0.815)))
    for o in glasses:
        assign_group(o, "Head")
    acc["Acc_Glasses"] = join(glasses, "Acc_Glasses")

    bun = [
        bk.sphere("Bun", 0.105, coll, "M_Hair", location=(0, -0.05, 1.10)),
        bk.sphere("Cap", 0.2, coll, "M_Hair", location=(0, 0.02, 0.97), scale=(1.22, 1.08, 0.55)),
        bk.sphere("BowL", 0.045, coll, "M_Bow", location=(-0.055, -0.04, 1.2), scale=(1.25, 0.55, 0.8)),
        bk.sphere("BowR", 0.045, coll, "M_Bow", location=(0.055, -0.04, 1.2), scale=(1.25, 0.55, 0.8)),
        bk.sphere("BowC", 0.024, coll, "M_Bow", location=(0, -0.035, 1.2)),
    ]
    for o in bun:
        assign_group(o, "Head")
    acc["Acc_HairBun"] = join(bun, "Acc_HairBun")

    headset = [
        bk.torus("Band", 0.285, 0.016, coll, "M_Headset", location=(0, 0, 0.84), rotation=(math.radians(90), 0, 0),
                 arc=math.pi, major_seg=18, minor_seg=6),
    ]
    for sx in (-1, 1):
        headset.append(bk.cylinder("Cup", 0.058, 0.045, coll, "M_Headset", location=(0.29 * sx, 0, 0.83),
                                   rotation=(0, math.radians(90), 0), segments=14))
    boom = bk.cylinder("Boom", 0.008, 0.2, coll, "M_Headset", location=(0.25, 0.12, 0.74),
                       rotation=(math.radians(70), 0, math.radians(35)), segments=6)
    headset.append(boom)
    headset.append(bk.sphere("Mic", 0.022, coll, "M_PlasticDark", location=(0.17, 0.235, 0.69)))
    for o in headset:
        assign_group(o, "Head")
    acc["Acc_Headset"] = join(headset, "Acc_Headset")

    for o in acc.values():
        bind(o, rig)
    return root, rig, body, acc


def build_manager(coll):
    s = 1.2
    root = bk.empty("Manager", coll)
    rig = make_rig(coll, root, s)
    parts = []
    prof = [(r * s, z * s) for r, z in MANAGER_PROFILE]
    body = bk.lathe("BodyMesh", prof, coll, "M_SkinManager", segments=28, scale=(1, DEPTH, 1))
    assign_body_head_blend(body, 0.56 * s, 0.70 * s)
    parts.append(body)

    # Suit shell over the lower body, open at the front by a shirt V.
    suit_prof = [(0, 0.055 * s), (0.21 * s, 0.06 * s)] + [(radius_at(MANAGER_PROFILE, z) * s + 0.014, z * s) for z in (0.1, 0.18, 0.32, 0.5, 0.62)] + [(0.3 * s, 0.68 * s), (0.0, 0.70 * s)]
    suit = bk.lathe("Suit", suit_prof, coll, "M_Suit", segments=28, scale=(1, DEPTH, 1))
    assign_group(suit, "Body")
    parts.append(suit)
    zf = 0.5 * s
    yf = radius_at(MANAGER_PROFILE, 0.5) * s * DEPTH + 0.02
    shirt = bk.box("Shirt", (0.16, 0.02, 0.3), coll, "M_Collar", location=(0, yf, 0.62 * s))
    assign_group(shirt, "Body")
    parts.append(shirt)
    for sx in (-1, 1):
        lapel = bk.box("Lapel", (0.08, 0.025, 0.3), coll, "M_Suit", location=(0.09 * sx, yf + 0.008, 0.6 * s))
        lapel.rotation_euler = (0, math.radians(18 * sx), 0)
        assign_group(lapel, "Body")
        parts.append(lapel)
    tie = bk.box("Tie", (0.06, 0.02, 0.26), coll, "M_TieRed", location=(0, yf + 0.012, 0.6 * s))
    knot = bk.box("Knot", (0.065, 0.03, 0.05), coll, "M_TieRed", location=(0, yf + 0.012, 0.74 * s))
    for o in (tie, knot):
        assign_group(o, "Body")
        parts.append(o)

    face = []
    ez = 0.82 * s
    ey = radius_at(MANAGER_PROFILE, 0.82) * s * DEPTH - 0.04
    for sx in (-1, 1):
        face.append(bk.sphere("EyeWhite", 0.068, coll, "M_Eye", location=(0.105 * sx, ey, ez), scale=(1, 0.6, 1.0)))
        face.append(bk.sphere("Pupil", 0.032, coll, "M_Pupil", location=(0.1 * sx, ey + 0.038, ez - 0.005), scale=(1, 0.5, 1.0)))
        brow = bk.box("Brow", (0.12, 0.03, 0.035), coll, "M_Brow", location=(0.1 * sx, ey + 0.02, ez + 0.085))
        brow.rotation_euler = (0, math.radians(22 * sx), 0)
        face.append(brow)
        mus = bk.sphere("Mustache", 0.07, coll, "M_Mustache", location=(0.065 * sx, ey + 0.035, ez - 0.13), scale=(1.45, 0.55, 0.45))
        mus.rotation_euler = (0, math.radians(-14 * sx), 0)
        face.append(mus)
    face.append(bk.torus("Mouth", 0.04, 0.01, coll, "M_Mouth", location=(0, ey + 0.03, ez - 0.21),
                         rotation=(math.radians(90), 0, 0), arc=math.pi, major_seg=10, minor_seg=6))
    for o in face:
        assign_group(o, "Head")
    parts.extend(face)

    for sx, bone in ((-1, "Arm.L"), (1, "Arm.R")):
        sleeve = bk.cylinder("Sleeve", 0.06, 0.27, coll, "M_Suit", location=(0.4 * sx, 0.0, 0.52), segments=12)
        shoulder = bk.sphere("Shoulder", 0.065, coll, "M_Suit", location=(0.38 * sx, 0, 0.67), segments=12, rings=8)
        hand = bk.sphere("Hand", 0.07, coll, "M_SkinManager", location=(0.41 * sx, 0.01, 0.36))
        for o in (sleeve, shoulder, hand):
            assign_group(o, bone)
            parts.append(o)
    for sx, bone in ((-1, "Foot.L"), (1, "Foot.R")):
        foot = bk.sphere("Shoe", 0.1, coll, "M_Shoe", location=(0.16 * sx, 0.05, 0.05), scale=(0.8, 1.3, 0.5))
        assign_group(foot, bone)
        parts.append(foot)
    body = join(parts, "Body")
    bind(body, rig)
    return root, rig, body


# ------------------------------------------------------------------------------------------- animation

REST = {"loc": (0, 0, 0), "rot": (0, 0, 0), "scale": (1, 1, 1)}


def make_action(rig, name, keys):
    """keys: list of (frame, {bone: {"loc":..,"rot":deg..,"scale":..}}). Every bone/channel is keyed at every key frame."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for frame, pose in keys:
        for bname in BONES:
            pb = rig.pose.bones[bname]
            spec = pose.get(bname, {})
            pb.location = spec.get("loc", REST["loc"])
            pb.rotation_euler = [math.radians(a) for a in spec.get("rot", REST["rot"])]
            pb.scale = spec.get("scale", REST["scale"])
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("rotation_euler", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
    return act


def arms(l, r, lx=8, rx=8):
    """Forward swing angles (positive = forward for both) and outward raise."""
    return {"Arm.L": {"rot": (lx, -l, 0)}, "Arm.R": {"rot": (rx, r, 0)}}


def merge(*dicts):
    out = {}
    for d in dicts:
        for k, v in d.items():
            out.setdefault(k, {}).update(v)
    return out


def walk_cycle(frames, stride=0.12, lift=0.06, bob=0.035, arm_swing=25, arm_pose=None, lean=-4, roll=3, extra=None):
    f0, f1, f2, f3, f4 = frames
    def pose(fl, fr, lz, rz, y, swing, r):
        p = {"Foot.L": {"loc": (0, fl, lz)}, "Foot.R": {"loc": (0, fr, rz)},
             "Body": {"loc": (0, y, 0), "rot": (lean, 0, r)}}
        p = merge(p, arm_pose if arm_pose is not None else arms(-swing, swing))
        if extra:
            p = merge(p, extra)
        return p
    return [
        (f0, pose(stride, -stride, 0, 0, 0, arm_swing, roll)),
        (f1, pose(0, 0, 0, lift, bob, 0, 0)),
        (f2, pose(-stride, stride, 0, 0, 0, -arm_swing, -roll)),
        (f3, pose(0, 0, lift, 0, bob, 0, 0)),
        (f4, pose(stride, -stride, 0, 0, 0, arm_swing, roll)),
    ]


def employee_actions(rig):
    rig.animation_data_create()
    breathe = {"Body": {"scale": (1.015, 1.025, 1.015)}}
    make_action(rig, "Idle", [
        (1, arms(0, 0)),
        (31, merge(arms(0, 0, 12, 12), breathe, {"Head": {"rot": (0, 3, 0)}})),
        (61, arms(0, 0)),
    ])
    make_action(rig, "Walk", walk_cycle((1, 4, 7, 10, 13)))
    carry = arms(70, 70, -12, -12)
    make_action(rig, "CarryIdle", [
        (1, carry), (21, merge(arms(66, 66, -12, -12), breathe)), (41, carry)])
    make_action(rig, "CarryWalk", walk_cycle((1, 4, 7, 10, 13), arm_pose=carry, lean=-2))
    make_action(rig, "Work", [
        (1, merge(arms(50, 58, -6, -6), {"Body": {"rot": (-6, 0, 0)}, "Head": {"rot": (-5, 0, 0)}})),
        (5, merge(arms(58, 50, -6, -6), {"Body": {"rot": (-6, 0, 0)}, "Head": {"rot": (-7, 0, 0)}})),
        (9, merge(arms(50, 58, -6, -6), {"Body": {"rot": (-6, 0, 0)}, "Head": {"rot": (-5, 0, 0)}})),
        (13, merge(arms(58, 50, -6, -6), {"Body": {"rot": (-6, 0, 0)}, "Head": {"rot": (-7, 0, 0)}})),
        (17, merge(arms(50, 58, -6, -6), {"Body": {"rot": (-6, 0, 0)}, "Head": {"rot": (-5, 0, 0)}})),
    ])
    make_action(rig, "WorkPose", [
        (1, merge(arms(45, 78, -8, -8), {"Body": {"loc": (0, 0, 0), "rot": (-10, 0, 3)}, "Head": {"rot": (-8, 6, 0)}})),
        (4, merge(arms(78, 45, -8, -8), {"Body": {"loc": (0, 0.03, 0), "rot": (-10, 0, -3)}, "Head": {"rot": (2, -6, 0)}})),
        (7, merge(arms(45, 78, -8, -8), {"Body": {"loc": (0, 0, 0), "rot": (-10, 0, 3)}, "Head": {"rot": (-8, 6, 0)}})),
        (10, merge(arms(78, 45, -8, -8), {"Body": {"loc": (0, 0.03, 0), "rot": (-10, 0, -3)}, "Head": {"rot": (2, -6, 0)}})),
        (13, merge(arms(45, 78, -8, -8), {"Body": {"loc": (0, 0, 0), "rot": (-10, 0, 3)}, "Head": {"rot": (-8, 6, 0)}})),
    ])
    make_action(rig, "Interact", [
        (1, merge(arms(10, 30), {"Body": {"rot": (-3, 0, 0)}})),
        (11, merge(arms(15, 82, 8, 4), {"Body": {"rot": (-12, 0, 0)}})),
        (21, merge(arms(10, 40), {"Body": {"rot": (-6, 0, 0)}})),
        (31, merge(arms(10, 30), {"Body": {"rot": (-3, 0, 0)}})),
    ])
    crouch = {"Body": {"scale": (1.05, 0.82, 1.05), "rot": (-14, 0, 0)}}
    make_action(rig, "Sneak", [
        (1, merge(crouch, arms(35, 40, -4, -4), {"Head": {"rot": (0, -25, 0)}})),
        (10, merge(crouch, arms(42, 33, -4, -4), {"Head": {"rot": (0, 0, 0)}})),
        (19, merge(crouch, arms(35, 40, -4, -4), {"Head": {"rot": (0, 25, 0)}})),
        (28, merge(crouch, arms(42, 33, -4, -4), {"Head": {"rot": (0, 0, 0)}})),
        (37, merge(crouch, arms(35, 40, -4, -4), {"Head": {"rot": (0, -25, 0)}})),
    ])
    make_action(rig, "Phone", [
        (1, merge({"Arm.L": {"rot": (8, 0, 0)}, "Arm.R": {"rot": (165, 40, 0)}}, {"Head": {"rot": (0, 0, -15)}, "Body": {"rot": (0, 0, -2)}})),
        (16, merge({"Arm.L": {"rot": (12, 10, 0)}, "Arm.R": {"rot": (160, 45, 0)}}, {"Head": {"rot": (-4, 4, -17)}, "Body": {"rot": (0, 0, 2)}})),
        (31, merge({"Arm.L": {"rot": (8, 0, 0)}, "Arm.R": {"rot": (165, 40, 0)}}, {"Head": {"rot": (0, 0, -15)}, "Body": {"rot": (0, 0, -2)}})),
    ])
    make_action(rig, "Panic", [
        (1, merge(arms(0, 0, 118, 118), {"Body": {"loc": (0, 0, 0), "scale": (1.08, 0.9, 1.08)}, "Head": {"rot": (0, -10, 0)}})),
        (7, merge(arms(-10, -10, 135, 135), {"Body": {"loc": (0, 0.14, 0), "scale": (0.95, 1.08, 0.95)}, "Head": {"rot": (0, 10, 0)}, "Foot.L": {"loc": (0, 0, 0.08)}, "Foot.R": {"loc": (0, 0, 0.08)}})),
        (13, merge(arms(0, 0, 118, 118), {"Body": {"loc": (0, 0, 0), "scale": (1.08, 0.9, 1.08)}, "Head": {"rot": (0, -10, 0)}})),
    ])
    sad = {"Head": {"rot": (-25, 0, 0)}, "Body": {"scale": (1.0, 0.92, 1.0)}}
    make_action(rig, "Fired", [
        (1, merge(sad, arms(65, 65, -14, -14), {"Body": {"rot": (-6, 0, 3)}})),
        (31, merge(sad, arms(62, 62, -14, -14), {"Body": {"rot": (-6, 0, -3)}})),
        (61, merge(sad, arms(65, 65, -14, -14), {"Body": {"rot": (-6, 0, 3)}})),
    ])
    make_action(rig, "Cheer", [
        (1, merge(arms(0, 0, 115, 140), {"Body": {"loc": (0, 0, 0), "scale": (1.05, 0.95, 1.05)}})),
        (7, merge(arms(0, 0, 140, 115), {"Body": {"loc": (0, 0.08, 0)}, "Foot.L": {"loc": (0, 0, 0.05)}, "Foot.R": {"loc": (0, 0, 0.05)}})),
        (13, merge(arms(0, 0, 115, 140), {"Body": {"loc": (0, 0, 0), "scale": (1.05, 0.95, 1.05)}})),
    ])
    make_action(rig, "Talk", [
        (1, merge(arms(5, 40, 8, 20), {"Head": {"rot": (-3, 4, 0)}})),
        (11, merge(arms(5, 75, 8, 30), {"Head": {"rot": (3, -2, 0)}, "Body": {"rot": (-4, 0, 0)}})),
        (21, merge(arms(5, 50, 8, 15), {"Head": {"rot": (-3, 6, 0)}})),
        (31, merge(arms(5, 40, 8, 20), {"Head": {"rot": (-3, 4, 0)}})),
    ])
    rig.animation_data.action = None


def manager_actions(rig):
    rig.animation_data_create()
    behind = {"Arm.L": {"rot": (-8, 40, 0)}, "Arm.R": {"rot": (-8, -40, 0)}}
    make_action(rig, "Idle", [
        (1, behind), (31, merge(behind, {"Body": {"scale": (1.015, 1.025, 1.015)}, "Head": {"rot": (0, 4, 0)}})), (61, behind)])
    make_action(rig, "Walk", walk_cycle((1, 5, 9, 13, 17), stride=0.15, lift=0.05, bob=0.03, arm_pose=behind, lean=-6, roll=4))
    lean = {"Body": {"rot": (-15, 0, 0), "scale": (1.02, 0.95, 1.02)}}
    make_action(rig, "Inspect", [
        (1, merge(behind, lean, {"Head": {"rot": (-10, -30, 0)}})),
        (21, merge(behind, lean, {"Head": {"rot": (-10, 30, 0)}})),
        (41, merge(behind, lean, {"Head": {"rot": (-10, -30, 0)}})),
    ])
    make_action(rig, "Angry", [
        (1, merge(arms(100, 100, 20, 20), {"Body": {"loc": (0, 0, 0), "rot": (-5, 0, 5)}, "Head": {"rot": (-5, 0, 0)}})),
        (5, merge(arms(90, 112, 25, 15), {"Body": {"loc": (0, 0.04, 0), "rot": (-5, 0, -5)}, "Head": {"rot": (2, 0, 0)}})),
        (9, merge(arms(112, 90, 15, 25), {"Body": {"loc": (0, 0, 0), "rot": (-5, 0, 5)}, "Head": {"rot": (-5, 0, 0)}})),
        (13, merge(arms(90, 112, 25, 15), {"Body": {"loc": (0, 0.04, 0), "rot": (-5, 0, -5)}, "Head": {"rot": (2, 0, 0)}})),
        (17, merge(arms(100, 100, 20, 20), {"Body": {"loc": (0, 0, 0), "rot": (-5, 0, 5)}, "Head": {"rot": (-5, 0, 0)}})),
    ])
    make_action(rig, "Talk", [
        (1, merge({"Arm.L": {"rot": (-8, 40, 0)}}, {"Arm.R": {"rot": (10, 60, 0)}}, {"Head": {"rot": (0, 3, 0)}})),
        (16, merge({"Arm.L": {"rot": (-8, 40, 0)}}, {"Arm.R": {"rot": (20, 85, 0)}}, {"Head": {"rot": (-6, -3, 0)}})),
        (31, merge({"Arm.L": {"rot": (-8, 40, 0)}}, {"Arm.R": {"rot": (10, 60, 0)}}, {"Head": {"rot": (0, 3, 0)}})),
    ])
    make_action(rig, "Point", [
        (1, merge({"Arm.L": {"rot": (-8, 40, 0)}}, {"Arm.R": {"rot": (5, 90, 0)}}, {"Body": {"rot": (-8, 0, 0)}})),
        (8, merge({"Arm.L": {"rot": (-8, 40, 0)}}, {"Arm.R": {"rot": (5, 98, 0)}}, {"Body": {"rot": (-11, 0, 0)}})),
        (16, merge({"Arm.L": {"rot": (-8, 40, 0)}}, {"Arm.R": {"rot": (5, 90, 0)}}, {"Body": {"rot": (-8, 0, 0)}})),
    ])
    rig.animation_data.action = None


# ------------------------------------------------------------------------------------------------ main

VARIANTS = [
    ("Mavi", "M_SkinBlue", ["Acc_Collar", "Acc_Tie", "Acc_HairTuft"], "M_TieRed"),
    ("Sari", "M_SkinYellow", ["Acc_Collar", "Acc_Tie", "Acc_Glasses"], "M_TieGreen"),
    ("Pembe", "M_SkinPink", ["Acc_HairBun"], None),
    ("Yesil", "M_SkinGreen", ["Acc_Collar", "Acc_Headset"], None),
]


def pose_at(rig, action_name, frame):
    rig.animation_data.action = bpy.data.actions[action_name]
    bpy.context.scene.frame_set(frame)


def preview_employee(emp_rig, emp_body, acc):
    bk.setup_preview_render((720, 720))
    out = os.path.join(bk.PREVIEW_DIR, "characters")
    for name, skin, accs, tie in VARIANTS:
        emp_body.material_slots[0].material = bk.mat(skin)
        for k, o in acc.items():
            o.hide_render = k not in accs
        if tie:
            acc["Acc_Tie"].material_slots[0].material = bk.mat(tie)
        pose_at(emp_rig, "Idle", 1)
        bk.render_preview(os.path.join(out, "employee_%s.png" % name), target=(0, 0, 0.6), distance=3.2, elevation_deg=12, azimuth_deg=200)
    emp_body.material_slots[0].material = bk.mat("M_SkinBlue")
    for k, o in acc.items():
        o.hide_render = k not in VARIANTS[0][2]
        o.hide_set(False)
    acc["Acc_Tie"].material_slots[0].material = bk.mat("M_TieRed")
    for act, frame in (("Walk", 1), ("CarryIdle", 1), ("Sneak", 1), ("WorkPose", 1), ("Phone", 1), ("Panic", 7), ("Fired", 1), ("Cheer", 7), ("Interact", 11)):
        pose_at(emp_rig, act, frame)
        bk.render_preview(os.path.join(out, "pose_%s.png" % act), target=(0, 0, 0.6), distance=3.2, elevation_deg=10, azimuth_deg=(180 if act in ("Cheer", "Panic", "Phone", "WorkPose") else 235))
    for o in acc.values():
        o.hide_render = False
    emp_rig.animation_data.action = None


def preview_manager(mgr_rig):
    bk.setup_preview_render((720, 720))
    out = os.path.join(bk.PREVIEW_DIR, "characters")
    for act, frame in (("Idle", 1), ("Walk", 1), ("Inspect", 1), ("Angry", 5), ("Point", 1)):
        pose_at(mgr_rig, act, frame)
        bk.render_preview(os.path.join(out, "manager_%s.png" % act), target=(0, 0, 0.75), distance=3.8, elevation_deg=10, azimuth_deg=215)
    mgr_rig.animation_data.action = None


def export_copy(root, name):
    import shutil
    out_dir = os.path.join(bk.UNITY_MODELS, "Characters")
    path = os.path.join(out_dir, name + ".fbx")
    bk.export_fbx(root, path, animations=True)
    os.makedirs(bk.EXPORT_DIR, exist_ok=True)
    shutil.copy2(path, os.path.join(bk.EXPORT_DIR, name + ".fbx"))


def main():
    want_previews = "--previews" in sys.argv
    # Employee (own session so action names stay clean)
    bk.reset_scene()
    bpy.context.scene.render.fps = FPS
    emp_root, emp_rig, emp_body, acc = build_employee(bk.collection("Employee"))
    employee_actions(emp_rig)
    export_copy(emp_root, "Employee")
    bk.save_blend("employee.blend")
    if want_previews:
        preview_employee(emp_rig, emp_body, acc)

    # Manager
    bk.reset_scene()
    bpy.context.scene.render.fps = FPS
    mgr_root, mgr_rig, mgr_body = build_manager(bk.collection("Manager"))
    manager_actions(mgr_rig)
    export_copy(mgr_root, "Manager")
    bk.save_blend("manager.blend")
    if want_previews:
        preview_manager(mgr_rig)
    print("CHARACTERS_DONE")


main()
