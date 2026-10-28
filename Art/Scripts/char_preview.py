"""Eevee preview renders + contact sheets for the character pipeline (headless)."""
import math
import os

import bpy
from mathutils import Vector

import bk_common as bk
import char_models as cm
import char_png as png

EXPRESSIONS = {  # name -> (eye cell 0..7, mouth cell 0..7); mirrored in PlayerAvatar.cs (FaceRig.Cells)
    "Neutral": (0, 0), "Happy": (1, 1), "Sneaky": (2, 2), "Shocked": (3, 3), "Angry": (4, 4), "Sad": (5, 5),
    "Talking": (0, 6), "Laugh": (7, 1), "Smug": (2, 1), "Stern": (4, 5), "Cheerful": (0, 1), "Nervous": (5, 4),
}


def setup(res=(640, 640)):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    try:
        sc.eevee.taa_render_samples = 24
    except AttributeError:
        pass
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    world = bpy.data.worlds.get("PreviewWorld") or bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.42, 0.44, 0.5, 1)
    bg.inputs["Strength"].default_value = 0.9
    sc.world = world
    if "PreviewSun" not in bpy.data.objects:
        ld = bpy.data.lights.new("PreviewSun", "SUN")
        ld.energy = 3.2
        ld.angle = math.radians(8)
        sun = bpy.data.objects.new("PreviewSun", ld)
        sc.collection.objects.link(sun)
        sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
        fd = bpy.data.lights.new("PreviewFill", "SUN")
        fd.energy = 0.9
        fill = bpy.data.objects.new("PreviewFill", fd)
        sc.collection.objects.link(fill)
        fill.rotation_euler = (math.radians(60), 0, math.radians(150))
        # floor so shadows ground the characters
        bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0))
        fl = bpy.context.active_object
        fl.name = "PreviewFloor"
        m = bpy.data.materials.new("PreviewFloorMat")
        m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.53, 0.5, 1)
        m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
        fl.data.materials.append(m)


def fix_outline_material():
    m = bpy.data.materials.get("M_Outline")
    if m is None:
        return
    m.use_backface_culling = True
    try:
        m.use_backface_culling_shadow = True
    except AttributeError:
        pass
    b = m.node_tree.nodes.get("Principled BSDF")
    col = b.inputs["Base Color"].default_value
    b.inputs["Base Color"].default_value = (0, 0, 0, 1)
    b.inputs["Emission Color"].default_value = col
    b.inputs["Emission Strength"].default_value = 1.0
    b.inputs["Roughness"].default_value = 1.0
    try:
        b.inputs["Specular IOR Level"].default_value = 0.0
    except KeyError:
        pass


def camera(target, distance, elev, azim, lens=50, ortho=None):
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.lens = lens
    cam.data.type = "ORTHO" if ortho else "PERSP"
    if ortho:
        cam.data.ortho_scale = ortho
    el, az = math.radians(elev), math.radians(azim)
    t = Vector(target)
    cam.location = t + Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * distance
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    return cam


def render(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


class Character:
    """Handle on a built character for look switching in previews."""

    def __init__(self, root, rig, body, eyes, mouth, accessories, props, groups):
        self.root, self.rig, self.body, self.eyes, self.mouth = root, rig, body, eyes, mouth
        self.acc = accessories      # name -> object
        self.props = props          # name -> object
        self.groups = groups        # swap group -> list of candidate material names

    def all_meshes(self):
        return [self.body, self.eyes, self.mouth] + list(self.acc.values()) + list(self.props.values())

    def apply_look(self, look, props=()):
        for name, o in list(self.acc.items()) + list(self.props.items()):
            o.hide_render = not (name in look["accessories"] or name in props)
        for grp, target in look.get("swaps", {}).items():
            cands = set(self.groups.get(grp, []))
            tm = bk.mat(target)
            for o in self.all_meshes():
                for slot in o.material_slots:
                    if slot.material is not None and slot.material.name in cands:
                        slot.material = tm
        sx, sy_up, sz_depth = look.get("scale", (1, 1, 1))
        self.root.scale = (sx, sz_depth, sy_up)
        self.root.rotation_euler = (-math.radians(look.get("tilt", 0)), 0, 0)
        self.expression(look.get("face", "Neutral"))

    def expression(self, name):
        e, m = EXPRESSIONS[name]
        cm.set_face_cell(bpy.data.materials["M_FaceEyes"], e, 0)
        cm.set_face_cell(bpy.data.materials["M_FaceMouth"], 8 + m, 8)

    def pose(self, action, frame):
        self.rig.obj.animation_data.action = bpy.data.actions[action]
        bpy.context.scene.frame_set(frame)

    def rest(self):
        self.rig.obj.animation_data.action = None
        for pb in self.rig.obj.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.scale = (1, 1, 1)
        bpy.context.view_layer.update()


def sheet(out_path, items, cols, scale=0.6):
    img = png.contact_sheet(items, cols)
    if scale != 1.0:
        img = png.resize(img, scale)
    png.write_png(out_path, img)
    return out_path
