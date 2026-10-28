"""Builds the bean characters: one shared player rig with 6 looks, one manager rig with 4 profile looks (+ the
classic fallback), the face expression atlas, all animation clips, FBX exports and preview contact sheets.

Run:  blender -b --factory-startup -P Art/Scripts/build_characters.py [-- --previews]
Outputs:
  Game/Assets/Game/Art/Models/Characters/{Employee,Manager}.fbx, FaceAtlas.png   (Unity imports these)
  Art/Export/{Employee,Manager}.fbx, FaceAtlas.png                                (archive copies)
  Art/Blender/{employee,manager}.blend                                            (editable sources)
  Art/character_looks.json      looks, material swap groups, clip list (read by ProjectBuilder.Characters.cs)
  Art/Previews/characters/*.png + sheet_*.png                                      (with --previews)

Modules: char_rig (bones, pose solver, actions), char_models (shape math, primitives, face patches),
char_parts (bodies, outline hulls, accessories, props), char_anims (clips), char_face_atlas, char_preview, char_png.
Art direction: Art/UI/portraits (2D portraits by the UI art run) - capsule beans, cream collars, navy outline.
"""
import json
import math
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
for _m in ("char_rig", "char_models", "char_parts", "char_anims", "char_face_atlas", "char_preview", "char_png"):
    sys.modules.pop(_m, None)

import bpy  # noqa: E402

import bk_common as bk  # noqa: E402
import char_anims as ca  # noqa: E402
import char_face_atlas as fa  # noqa: E402
import char_models as cm  # noqa: E402
import char_parts as cp  # noqa: E402
import char_png as png  # noqa: E402
import char_preview as pv  # noqa: E402
import char_rig as cr  # noqa: E402

CHAR_DIR = os.path.join(bk.UNITY_MODELS, "Characters")
ATLAS_PATH = os.path.join(CHAR_DIR, "FaceAtlas.png")
LOOKS_JSON = os.path.join(bk.ROOT, "Art", "character_looks.json")
PREV = os.path.join(bk.PREVIEW_DIR, "characters", "_frames")   # individual renders (git-ignored); sheets go to Art/Previews
PLAYER_TRI_BUDGET = 4000
MANAGER_TRI_BUDGET = 6000

# Player slot order = colour index 0..5. scale is Unity-style (x, height, depth).
PLAYER_LOOKS = [
    {"id": "Mavi", "accessories": ["Acc_Collar", "Acc_Tie"], "swaps": {"skin": "M_BeanBlue", "tie": "M_TieGreenDark"}, "scale": (1.0, 1.08, 1.0)},
    {"id": "Sari", "accessories": ["Acc_Collar", "Acc_GlassesRound"], "swaps": {"skin": "M_BeanYellow"}, "scale": (1.1, 0.95, 1.1)},
    {"id": "Pembe", "accessories": ["Acc_CollarRound", "Acc_HairBun"], "swaps": {"skin": "M_BeanPink"}, "scale": (1.0, 1.0, 1.0)},
    {"id": "Yesil", "accessories": ["Acc_Collar", "Acc_Tie", "Acc_Headset"], "swaps": {"skin": "M_BeanGreen", "tie": "M_TieForest"}, "scale": (0.98, 0.9, 0.98)},
    {"id": "Turuncu", "accessories": ["Acc_Collar", "Acc_Tie", "Acc_Beanie"], "swaps": {"skin": "M_BeanOrange", "tie": "M_TieOrange"}, "scale": (1.0, 1.0, 1.0)},
    {"id": "Mor", "accessories": ["Acc_Curls", "Acc_Scarf"], "swaps": {"skin": "M_BeanPurple"}, "scale": (1.0, 0.98, 1.0)},
]
PLAYER_GROUPS = {"skin": ["M_BeanBlue", "M_BeanYellow", "M_BeanPink", "M_BeanGreen", "M_BeanOrange", "M_BeanPurple", "M_BeanSlate"],
                 "tie": ["M_TieGreenDark", "M_TieForest", "M_TieOrange"]}
PLAYER_PROPS = ["Prop_Handset", "Prop_Mobile", "Prop_Box"]

MANAGER_LOOKS = [
    {"id": "default", "name": "Müdür", "accessories": ["Acc_MgrTie", "Acc_Moustache"],
     "swaps": {"skin": "M_BeanBrown", "suit": "M_SuitDefault", "tie": "M_TieRed"}, "face": "Stern", "idle": "Idle"},
    {"id": "paragoz", "name": "Hasan Bey", "accessories": ["Acc_MgrTie", "Acc_Handlebar", "Acc_Watch"],
     "swaps": {"skin": "M_BeanGold", "suit": "M_SuitNavy", "tie": "M_TieGold"}, "face": "Smug", "idle": "IdleCrossed", "scale": (1.08, 0.98, 1.08)},
    {"id": "disiplinci", "name": "Nermin Hanım", "accessories": ["Acc_GlassesCatEye", "Acc_TightBun", "Prop_Clipboard"],
     "swaps": {"skin": "M_BeanTeal", "suit": "M_SuitGreen"}, "face": "Stern", "idle": "IdleClipboard", "scale": (0.95, 1.05, 0.95)},
    {"id": "kayirmaci", "name": "Selin Hanım", "accessories": ["Acc_SunglassesUp", "Acc_SilkScarf", "Acc_Earrings"],
     "swaps": {"skin": "M_BeanCoral", "suit": "M_SuitGreen"}, "face": "Cheerful", "idle": "IdleHips"},
    {"id": "paranoyak", "name": "Kemal Bey", "accessories": ["Acc_MgrTie", "Acc_DarkGlasses", "Acc_Trench"],
     "swaps": {"skin": "M_BeanViolet", "suit": "M_SuitTrench", "tie": "M_TieGreenDark"}, "face": "Sneaky", "idle": "IdleSteeple",
     "scale": (1.03, 0.95, 1.03), "tilt": 7},
]
MANAGER_GROUPS = {"skin": ["M_BeanBrown", "M_BeanGold", "M_BeanTeal", "M_BeanCoral", "M_BeanViolet"],
                  "suit": ["M_SuitDefault", "M_SuitNavy", "M_SuitGreen", "M_SuitTrench"],
                  "tie": ["M_TieRed", "M_TieGold", "M_TieGreenDark"]}
MANAGER_PROPS = ["Prop_Clipboard"]


def load_palettes():
    with open(os.path.join(bk.ROOT, "Art", "palette_characters.json"), encoding="utf-8") as f:
        extra = json.load(f)["materials"]
    bk.palette()["materials"].update(extra)


def build_atlas():
    os.makedirs(CHAR_DIR, exist_ok=True)
    page = fa.build_atlas()
    png.write_png(ATLAS_PATH, page)
    os.makedirs(bk.EXPORT_DIR, exist_ok=True)
    shutil.copy2(ATLAS_PATH, os.path.join(bk.EXPORT_DIR, "FaceAtlas.png"))
    os.makedirs(PREV, exist_ok=True)
    png.write_png(os.path.join(bk.PREVIEW_DIR, "characters", "face_atlas_cells.png"), fa.preview_sheet(page, ["#2F8FDE", "#F6CF55", "#A872D0", "#33BEC4"], 1))


def tri(o):
    return cm.tri_count(o)


def look_tris(ch, look):
    base = tri(ch.body) + tri(ch.eyes) + tri(ch.mouth)
    return base + sum(tri(ch.acc[a]) for a in look["accessories"] if a in ch.acc) + sum(tri(ch.props[a]) for a in look["accessories"] if a in ch.props)


def export(root, name):
    path = os.path.join(CHAR_DIR, name + ".fbx")
    bk.export_fbx(root, path, animations=True)
    shutil.copy2(path, os.path.join(bk.EXPORT_DIR, name + ".fbx"))


# ------------------------------------------------------------------------------------------------ build

def build_employee():
    bk.reset_scene()
    bpy.context.scene.render.fps = cr.FPS
    coll = bk.collection("Employee")
    root = bk.empty("Employee", coll)
    rig = cr.make_rig(coll, root, 1.0)
    shape = cm.BeanShape(cm.EMPLOYEE_CTRL, 0.9, 1.0)
    body, eyes, mouth = cp.build_base(coll, rig, shape, "employee", "M_BeanBlue", "M_ShoeNavy", cm.EMP_FACE, ATLAS_PATH)
    ctx = cp.Ctx(coll, rig, shape, cm.EMP_FACE)
    acc = {
        "Acc_Collar": cp.acc_collar(ctx),
        "Acc_CollarRound": cp.acc_collar(ctx, round_tips=True, name="Acc_CollarRound"),
        "Acc_Tie": cp.acc_tie(ctx),
        "Acc_GlassesRound": cp.acc_glasses_round(ctx),
        "Acc_HairBun": cp.acc_hair_bun(ctx),
        "Acc_Headset": cp.acc_headset(ctx),
        "Acc_Beanie": cp.acc_beanie(ctx),
        "Acc_Curls": cp.acc_curls(ctx),
        "Acc_Scarf": cp.acc_scarf(ctx),
    }
    props = {}
    cr.apply_pose(rig, ca.pose_phone())
    props["Prop_Handset"] = cp.prop_handset(ctx, coll)
    props["Prop_Mobile"] = cp.prop_mobile(ctx, coll)
    cr.apply_pose(rig, ca.pose_fired())
    props["Prop_Box"] = cp.prop_box(ctx, coll)
    cr.reset_pose(rig)
    clips = ca.player_clips(rig)
    cr.reset_pose(rig)
    pv.fix_outline_material()
    ch = pv.Character(root, rig, body, eyes, mouth, acc, props, PLAYER_GROUPS)
    return ch, clips


def build_manager():
    bk.reset_scene()
    bpy.context.scene.render.fps = cr.FPS
    s = 1.2
    coll = bk.collection("Manager")
    root = bk.empty("Manager", coll)
    rig = cr.make_rig(coll, root, s, layout=cr.MANAGER_LAYOUT)
    shape = cm.BeanShape(cm.MANAGER_CTRL, 0.9, s)
    ctx = cp.Ctx(coll, rig, shape, cm.MGR_FACE)
    suit = cp.mgr_suit_parts(ctx)
    body, eyes, mouth = cp.build_base(coll, rig, shape, "manager", "M_BeanBrown", "M_ShoeBlack", cm.MGR_FACE, ATLAS_PATH,
                                      sleeve_mat="M_SuitDefault", body_extra=suit)
    acc = {
        "Acc_MgrTie": cp.acc_mgr_tie(ctx),
        "Acc_Moustache": cp.acc_moustache(ctx),
        "Acc_Handlebar": cp.acc_moustache(ctx, "Acc_Handlebar", big=True),
        "Acc_Watch": cp.acc_watch(ctx, "L"),
        "Acc_GlassesCatEye": cp.acc_glasses_cateye(ctx),
        "Acc_TightBun": cp.acc_tight_bun(ctx),
        "Acc_SunglassesUp": cp.acc_sunglasses_up(ctx),
        "Acc_SilkScarf": cp.acc_silk_scarf(ctx),
        "Acc_Earrings": cp.acc_earrings(ctx),
        "Acc_DarkGlasses": cp.acc_dark_glasses(ctx),
        "Acc_Trench": cp.acc_trench(ctx),
    }
    props = {}
    cr.apply_pose(rig, ca.pose_clipboard())
    props["Prop_Clipboard"] = cp.prop_clipboard(ctx, coll, "L")
    cr.reset_pose(rig)
    clips = ca.manager_clips(rig)
    cr.reset_pose(rig)
    pv.fix_outline_material()
    ch = pv.Character(root, rig, body, eyes, mouth, acc, props, MANAGER_GROUPS)
    return ch, clips


# --------------------------------------------------------------------------------------------- previews

def preview_players(ch):
    pv.setup((560, 640))
    items_front, items_game = [], []
    for look in PLAYER_LOOKS:
        ch.apply_look(look)
        ch.rest()
        pv.camera((0, 0, 0.62), 3.3, 8, 202, 50)
        items_front.append((pv.render(os.path.join(PREV, "player_%s.png" % look["id"])), look["id"]))
        pv.camera((0, 0, 0.5), 6.0, 52, 205, 45)
        items_game.append((pv.render(os.path.join(PREV, "player_%s_game.png" % look["id"])), look["id"] + " (game cam 6m)"))
    pv.sheet(os.path.join(bk.PREVIEW_DIR, "sheet_players.png"), items_front + items_game, 6)

    # expressions on two looks
    items = []
    for look_i in (0, 4):
        ch.apply_look(PLAYER_LOOKS[look_i])
        ch.rest()
        for name in pv.EXPRESSIONS:
            ch.expression(name)
            pv.camera((0, 0, 0.8), 1.9, 6, 195, 50)
            items.append((pv.render(os.path.join(PREV, "expr_%s_%s.png" % (PLAYER_LOOKS[look_i]["id"], name))), name))
    pv.sheet(os.path.join(bk.PREVIEW_DIR, "sheet_expressions.png"), items, 10)

    # key poses (props shown where they belong)
    poses = [("Idle", 31, "Neutral", ()), ("Idle2", 30, "Happy", ()), ("Walk", 1, "Neutral", ()), ("Run", 1, "Neutral", ()),
             ("Run", 4, "Neutral", ()), ("CrouchIdle", 1, "Sneaky", ()), ("Sneak", 9, "Sneaky", ()), ("CarryWalk", 1, "Neutral", ()),
             ("Typing", 1, "Neutral", ()), ("WorkPose", 4, "Shocked", ()), ("PhoneStand", 30, "Talking", ("Prop_Handset",)),
             ("PhoneArm", 1, "Talking", ("Prop_Mobile",)), ("Cleaning", 7, "Neutral", ()), ("Rummaging", 9, "Sneaky", ()),
             ("Fiddling", 7, "Sneaky", ()), ("Brewing", 6, "Neutral", ()), ("Delivering", 16, "Happy", ()), ("Placing", 13, "Neutral", ()),
             ("Spilling", 21, "Sneaky", ()), ("Sit", 1, "Neutral", ()), ("SitTalk", 14, "Talking", ()), ("Talk", 14, "Talking", ()),
             ("Wave", 12, "Happy", ()), ("Point", 14, "Angry", ()), ("Facepalm", 20, "Sad", ()), ("Shrug", 22, "Smug", ()),
             ("Laugh", 11, "Laugh", ()), ("Shocked", 8, "Shocked", ()), ("Panic", 5, "Shocked", ()), ("Cheer", 6, "Laugh", ()),
             ("FiredIdle", 30, "Sad", ("Prop_Box",)), ("FiredWalk", 1, "Sad", ("Prop_Box",))]
    items = []
    looks_cycle = [0, 1, 2, 3, 4, 5]
    for i, (act, fr, face, props) in enumerate(poses):
        look = PLAYER_LOOKS[looks_cycle[i % 6]]
        ch.apply_look(look, props)
        ch.expression(face)
        ch.pose(act, fr)
        pv.camera((0, 0, 0.62), 3.6, 14, 215, 50)
        items.append((pv.render(os.path.join(PREV, "pose_%s_%d.png" % (act, fr))), "%s %d" % (act, fr)))
    pv.sheet(os.path.join(bk.PREVIEW_DIR, "sheet_poses.png"), items, 8)
    ch.rest()


def preview_managers(ch):
    pv.setup((560, 640))
    front, game, poses = [], [], []
    for look in MANAGER_LOOKS:
        ch.apply_look(look)
        ch.pose(look.get("idle", "Idle"), 1)
        pv.camera((0, 0, 0.78), 4.0, 8, 202, 50)
        front.append((pv.render(os.path.join(PREV, "manager_%s.png" % look["id"])), look["id"]))
        pv.camera((0, 0, 0.6), 6.5, 52, 205, 45)
        game.append((pv.render(os.path.join(PREV, "manager_%s_game.png" % look["id"])), look["id"] + " (game cam)"))
    pv.sheet(os.path.join(bk.PREVIEW_DIR, "sheet_managers.png"), front + game, 5)
    items = []
    acts = [("Walk", 1, "Stern", 1, ()), ("Inspect", 1, "Sneaky", 2, ("Prop_Clipboard",)), ("Angry", 7, "Angry", 3, ()),
            ("Pleased", 8, "Happy", 1, ()), ("Talk", 14, "Talking", 4, ()), ("Point", 8, "Angry", 2, ()), ("Sit", 1, "Stern", 3, ()),
            ("SitTalk", 14, "Talking", 4, ())]
    for act, fr, face, li, props in acts:
        ch.apply_look(MANAGER_LOOKS[li], props)
        ch.expression(face)
        ch.pose(act, fr)
        pv.camera((0, 0, 0.75), 4.3, 14, 215, 50)
        items.append((pv.render(os.path.join(PREV, "manager_pose_%s.png" % act)), "%s (%s)" % (act, MANAGER_LOOKS[li]["id"])))
    pv.sheet(os.path.join(bk.PREVIEW_DIR, "sheet_manager_poses.png"), items, 4)
    ch.rest()


# ------------------------------------------------------------------------------------------------ main

def look_json(look, ch, kind):
    d = {"id": look["id"], "accessories": look["accessories"], "swaps": look.get("swaps", {}),
         "scale": list(look.get("scale", (1, 1, 1))), "tilt": look.get("tilt", 0), "face": look.get("face", "Neutral"),
         "tris": look_tris(ch, look)}
    if kind == "manager":
        d["name"] = look["name"]
        d["idle"] = look.get("idle", "Idle")
    return d


def main():
    want_previews = "--previews" in sys.argv
    load_palettes()
    build_atlas()
    report = {}

    emp, emp_clips = build_employee()
    export(emp.root, "Employee")
    bk.save_blend("employee.blend")
    players = [look_json(l, emp, "player") for l in PLAYER_LOOKS]
    report["players"] = players
    report["playerAccessories"] = sorted(emp.acc.keys())
    report["playerProps"] = {k: tri(v) for k, v in emp.props.items()}
    report["playerGroups"] = PLAYER_GROUPS
    report["playerClips"] = [{"name": n, "loop": l, "frames": f} for n, l, f in emp_clips]
    if want_previews:
        preview_players(emp)

    mgr, mgr_clips = build_manager()
    export(mgr.root, "Manager")
    bk.save_blend("manager.blend")
    report["managers"] = [look_json(l, mgr, "manager") for l in MANAGER_LOOKS]
    report["managerAccessories"] = sorted(mgr.acc.keys())
    report["managerProps"] = {k: tri(v) for k, v in mgr.props.items()}
    report["managerGroups"] = MANAGER_GROUPS
    report["managerClips"] = [{"name": n, "loop": l, "frames": f} for n, l, f in mgr_clips]
    if want_previews:
        preview_managers(mgr)

    report["atlas"] = {"file": "FaceAtlas.png", "size": fa.SIZE, "cells": fa.COLS, "eyes": fa.EYES, "mouths": fa.MOUTHS}
    report["expressions"] = {k: list(v) for k, v in pv.EXPRESSIONS.items()}
    with open(LOOKS_JSON, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1, ensure_ascii=False)

    ok = True
    for l in report["players"]:
        flag = "OK" if l["tris"] <= PLAYER_TRI_BUDGET else "OVER"
        ok &= flag == "OK"
        print("TRIS player %-8s %5d %s" % (l["id"], l["tris"], flag))
    for l in report["managers"]:
        flag = "OK" if l["tris"] <= MANAGER_TRI_BUDGET else "OVER"
        ok &= flag == "OK"
        print("TRIS manager %-10s %5d %s" % (l["id"], l["tris"], flag))
    print("CLIPS player %d manager %d" % (len(emp_clips), len(mgr_clips)))
    print("CHARACTERS_DONE" if ok else "CHARACTERS_DONE (budget exceeded)")


main()
