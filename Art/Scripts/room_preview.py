"""EEVEE previews of the v3 rooms from the first-person camera (approximation of the Unity look; no detail textures).
Called by build_rooms.py --previews. Writes Art/Previews/rooms/*.png. Characters are placeholder beans."""
import math
import os

import bpy
from mathutils import Euler, Vector

import bk_common as bk
import build_rooms as br

R = math.radians


def u2b(p):
    return Vector((p[0], p[2], p[1]))


def instance(lib, model, p, yaw, origin=(0, 0, 0), coll=None):
    src = lib.get(model)
    if src is None:
        print("PREVIEW missing", model)
        return None
    e = bpy.data.objects.new("I_" + model, None)
    e.instance_type = "COLLECTION"
    e.instance_collection = src
    e.location = u2b((p[0] + origin[0], p[1] + origin[1], p[2] + origin[2]))
    e.rotation_euler = (0, 0, -R(yaw))
    (coll or bpy.context.scene.collection).objects.link(e)
    return e


def light(spec, origin=(0, 0, 0)):
    t = spec["t"]
    ld = bpy.data.lights.new(spec["name"], "SPOT" if t == "spot" else "POINT")
    h = spec["color"].lstrip("#")
    ld.color = tuple(bk.hex_to_linear("#" + h))
    ld.energy = spec["i"] * 60.0
    ld.shadow_soft_size = 0.15
    if hasattr(ld, "use_shadow"):
        ld.use_shadow = False
    if t == "spot":
        ld.spot_size = R(spec.get("angle", 60))
    o = bpy.data.objects.new(spec["name"], ld)
    p = spec["p"]
    o.location = u2b((p[0] + origin[0], p[1] + origin[1], p[2] + origin[2]))
    if t == "spot":
        o.rotation_euler = (0, 0, 0)  # points down (-Z) by default
    bpy.context.scene.collection.objects.link(o)


def camera(name, eye, yaw, pitch, fov=60):
    cd = bpy.data.cameras.new(name)
    cd.sensor_fit = "VERTICAL"
    cd.angle_y = R(fov)
    cd.clip_start = 0.03
    cd.clip_end = 400
    cam = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = u2b(eye)
    # Unity: yaw clockwise from +Z, pitch positive = down. Blender camera looks down -Z local.
    fwd = Vector((math.sin(R(yaw)) * math.cos(R(pitch)), -math.sin(R(pitch)), math.cos(R(yaw)) * math.cos(R(pitch))))
    cam.rotation_euler = u2b(fwd).to_track_quat("-Z", "Y").to_euler()
    return cam


def bean(p, color, origin, yaw):
    m = bpy.data.materials.new("Bean" + color)
    m.diffuse_color = tuple(bk.hex_to_linear(color)) + (1,)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = tuple(bk.hex_to_linear(color)) + (1,)
    c = bpy.context.scene.collection
    loc = u2b((p[0] + origin[0], 0.44 + 0.6, p[2] + origin[2]))
    b = bk.sphere("Bean", 0.33, c, None, None, loc, scale=(1, 1, 1.75), segments=20, rings=12)
    b.data.materials.append(m)
    f = Vector((math.sin(R(yaw)), math.cos(R(yaw)), 0))
    for sx in (-1, 1):
        side = Vector((f.y, -f.x, 0)) * 0.11 * sx
        e = bk.sphere("Eye", 0.06, c, None, None, loc + f * 0.3 + side + Vector((0, 0, 0.22)), segments=10, rings=6)
        e.data.materials.append(bk.mat("M_Eye"))


def setup(res, fast):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    ee = sc.eevee
    for attr, val in (("taa_render_samples", 16 if fast else 48), ("use_shadows", True), ("use_raytracing", False)):
        if hasattr(ee, attr):
            try:
                setattr(ee, attr, val)
            except Exception:
                pass
    if hasattr(ee, "use_fast_gi"):
        ee.use_fast_gi = True
    vs = sc.view_settings
    for vt in ("Khronos PBR Neutral", "AgX", "Standard"):
        try:
            vs.view_transform = vt
            break
        except TypeError:
            continue
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.55, 0.7, 0.9, 1)
    bg.inputs["Strength"].default_value = 0.6
    sc.world = world
    for m in bpy.data.materials:
        spec = bk.palette()["materials"].get(m.name)
        if spec and spec.get("alpha", 1.0) < 1.0 and hasattr(m, "surface_render_method"):
            m.surface_render_method = "BLENDED"
    tex = os.path.join(bk.ROOT, "Game", "Assets", "Game", "Art", "Textures", "Rooms", "ScreenDesktop.png")
    m = bpy.data.materials.get("M_RmScreenOff")
    if m is not None and os.path.exists(tex):
        nt = m.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = bpy.data.images.load(tex)
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 1.0


def render(path, cam):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def render_all(roots, fast=False):
    lib = {}
    for r in roots:
        lib[r.name] = r.users_collection[0]
    # hide the source collections (instances still render)
    vl = bpy.context.view_layer
    for lc in vl.layer_collection.children:
        lc.exclude = True
    setup((1280, 720), fast)
    inst_c = bk.collection("PreviewInstances")
    instance(lib, "RmOfficeShell", (0, 0, 0), 0, coll=inst_c)
    instance(lib, "RmSkyline", (0, 0, 0), 0, coll=inst_c)
    for pid, model, p, yaw in br.OFFICE_PROPS:
        instance(lib, model, p, yaw, coll=inst_c)
    for l in br.OFFICE_LIGHTS:
        light(l)
    mo = br.MEET_ORIGIN
    instance(lib, "RmMeetingShell", (0, 0, 0), 0, mo, coll=inst_c)
    for pid, model, p, yaw in br.MEETING_PROPS:
        if model:
            instance(lib, model, p, yaw, mo, coll=inst_c)
    colors = ["#2F8FDE", "#F6CF55", "#F06B88", "#3C9A6C", "#F4853C", "#A872D0"]
    for i, s in enumerate(br.SEATS):
        instance(lib, "RmMeetingChair", s["p"], s["yaw"], mo, coll=inst_c)
        if i != 2:
            bean(s["p"], colors[i], mo, s["yaw"])
    for l in br.MEETING_LIGHTS:
        light(l, mo)
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 4.0
    sun.color = (1.0, 0.9, 0.78)
    sun.angle = R(2)
    so = bpy.data.objects.new("Sun", sun)
    d_u = Vector((math.cos(R(25)) * math.cos(R(28)), -math.sin(R(28)), math.sin(R(25)) * math.cos(R(28))))  # light travels +x, down
    so.rotation_euler = u2b(-d_u).to_track_quat("Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(so)
    out = br.PREV_DIR
    a = br.OFFICE_ANCHORS
    shots = [
        ("office_seat", a["eye"], a["eyeYaw"], a["eyePitch"]),
        ("office_left", a["eye"], -75, 5),
        ("office_right", a["eye"], 80, 8),
        ("office_down", a["eye"], 20, 45),
    ]
    s2 = br.SEATS[2]
    yaw2 = s2["yaw"]
    f2 = (math.sin(R(yaw2)) * 0.06, 0, math.cos(R(yaw2)) * 0.06)
    eye2 = (s2["p"][0] + f2[0] + mo[0], 1.24, s2["p"][2] + f2[2] + mo[2])
    shots += [("meeting_seat", eye2, yaw2, 8), ("meeting_head", eye2, yaw2 - 55, -2),
              ("meeting_overview", (0.0 + mo[0], 2.3, -2.7 + mo[2]), 0, 22)]
    for name, eye, yaw, pitch in shots:
        cam = camera("Cam_" + name, eye, yaw, pitch)
        render(os.path.join(out, name + ".png"), cam)
    print("ROOM_PREVIEWS_DONE", out)
