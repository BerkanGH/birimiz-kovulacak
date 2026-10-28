"""Animation clips for the bean rig. Hand targets are in the Body's rest frame (unit scale; managers are scaled
automatically). Frame numbers are at 30 fps. Looping clips repeat the first pose on the last key.

Player clip names are the Animator state names used by PlayerAvatar; manager names by ManagerVisual.
"""
from char_rig import Pose, make_action

# ------------------------------------------------------------------------------------------------ helpers


def P(body=None, head=None, hands=None, feet=None, root=None, top=None, tail=None):
    return Pose(body, head, hands, feet, root, top, tail)


def mirror_hands(h):
    out = {}
    for k, v in h.items():
        if k in ("L", "R"):
            out["R" if k == "L" else "L"] = (-v[0], v[1], v[2])
        elif k[-1] in "LR" and k[:-1] in ("pole", "twist", "swing", "out", "bend"):
            nk = k[:-1] + ("R" if k[-1] == "L" else "L")
            if k.startswith("pole"):
                out[nk] = (-v[0], v[1], v[2])
            else:
                out[nk] = v
        else:
            out[k] = v
    return out


def hands(L=None, R=None, poleL=(-1, -0.4, -0.7), poleR=(1, -0.4, -0.7), **kw):
    d = dict(kw)
    if L is not None:
        d["L"] = L
        d["poleL"] = poleL
    if R is not None:
        d["R"] = R
        d["poleR"] = poleR
    return d


def merge(*ds):
    out = {}
    for d in ds:
        out.update(d)
    return out


def gait(period, stride, lift, bob, lean, sq, swing=25, bend=12, arms=None, roll=3, toe=18, head=None, extra_body=None,
         top=6, tail=10, sneak=False):
    """Generic 4-phase gait. arms: fixed hand dict (carry) or None for swinging arms."""
    f = [1 + round(period * i / 4) for i in range(5)]
    keys = []
    for i in range(5):
        ph = i % 4
        left_fwd = ph == 0
        contact = ph in (0, 2)
        sgn = 1 if ph in (0, 1) else -1          # which foot leads this half
        if contact:
            fl = (0, stride * (1 if left_fwd else -1), 0.0, toe * (1 if left_fwd else -0.8))
            fr = (0, stride * (-1 if left_fwd else 1), 0.01, toe * (-0.8 if left_fwd else 1))
            body = dict(lean=lean, up=-bob * 0.35, sq=1 - sq, tilt=-roll * sgn, turn=-3 * sgn)
            tp = dict(lean=-top)
        else:
            planted = (0, 0.0, 0.0, 0)
            passing = (0, 0.0, lift, 4 if not sneak else -25)
            fl, fr = (planted, passing) if ph == 1 else (passing, planted)
            body = dict(lean=lean, up=bob, sq=1 + sq * 0.6, tilt=0, turn=0)
            tp = dict(lean=top)
        if extra_body:
            body.update(extra_body)
        if arms is None:
            sw = swing if contact else 0
            h = {"swingL": -sw * sgn, "swingR": sw * sgn, "bendL": bend, "bendR": bend, "outL": 6, "outR": 6}
        else:
            h = arms(ph) if callable(arms) else arms
        hd = dict(lean=-lean * 0.4, tilt=roll * 0.5 * sgn if contact else 0)
        if head:
            hd.update(head(ph) if callable(head) else head)
        keys.append((f[i], P(body=body, head=hd, hands=h, feet={"L": fl, "R": fr}, top=tp,
                             tail=dict(swing=-tail if contact else tail * 0.6))))
    return keys


def loop(keys):
    """Append the first pose at the end frame (last key's frame) if not already closed."""
    return keys


# --------------------------------------------------------------------------------------- player poses

CARRY = hands(L=(-0.15, 0.3, 0.58), R=(0.15, 0.3, 0.58), poleL=(-1, -0.2, -0.9), poleR=(1, -0.2, -0.9))
FIRED_BOX = hands(L=(-0.215, 0.37, 0.44), R=(0.215, 0.37, 0.44), poleL=(-1, -0.2, -0.6), poleR=(1, -0.2, -0.6))
PHONE_R = dict(R=(0.37, 0.09, 0.84), poleR=(1, -0.1, -1.0))
TYPE_L, TYPE_R = (-0.16, 0.34, 0.69), (0.16, 0.34, 0.69)
SIT_UP = 0.44


def pose_phone():
    """Pose used to model the handset / mobile props (must match PhoneStand / PhoneArm)."""
    return P(head=dict(tilt=9, turn=-4), hands=merge(PHONE_R, {"swingL": 0}))


def pose_fired():
    return P(body=dict(lean=6, sq=0.95), head=dict(lean=18), hands=FIRED_BOX)


def player_clips(rig):
    clips = []

    def add(name, keys, loop=True):
        make_action(rig, name, keys, loop=loop)
        clips.append((name, loop, keys[-1][0] - keys[0][0] + (0 if loop else 1)))

    rest_arms = {"swingL": 0, "swingR": 0, "outL": 4, "outR": 4}
    # ---------------------------------------------------------------- idles
    add("Idle", [
        (1, P(body=dict(sq=0.99, tilt=-1), head=dict(tilt=1), hands=rest_arms, top=dict(lean=0))),
        (31, P(body=dict(sq=1.03, tilt=1, up=0.005), head=dict(tilt=-1.5, turn=4, lean=-2), hands=merge(rest_arms, {"outL": 8, "outR": 8, "swingL": 4, "swingR": 4}),
               top=dict(lean=3))),
        (61, P(body=dict(sq=0.99, tilt=-1), head=dict(tilt=1), hands=rest_arms, top=dict(lean=0))),
    ])
    stretch_h = hands(L=(-0.4, 0.06, 0.94), R=(0.4, 0.06, 0.94), poleL=(-1, 0, -0.3), poleR=(1, 0, -0.3))
    add("Idle2", [  # bored stretch + look around
        (1, P(hands=rest_arms)),
        (14, P(body=dict(sq=1.1, lean=-6), head=dict(lean=-14), hands=stretch_h, feet={"L": (0, 0, 0.0, -20), "R": (0, 0, 0.0, -20)}, top=dict(lean=-6))),
        (30, P(body=dict(sq=1.12, lean=-8), head=dict(lean=-16, tilt=6), hands=stretch_h, feet={"L": (0, 0, 0.0, -25), "R": (0, 0, 0.0, -25)})),
        (40, P(body=dict(sq=0.93), hands=rest_arms, top=dict(lean=8))),
        (48, P(body=dict(sq=1.0), hands=rest_arms)),
        (60, P(head=dict(turn=40, lean=-4), hands=rest_arms)),
        (75, P(head=dict(turn=-40, lean=-4), hands=rest_arms)),
        (90, P(hands=rest_arms)),
    ], loop=False)

    # ---------------------------------------------------------------- locomotion
    add("Walk", gait(20, 0.11, 0.05, 0.025, 3, 0.03, swing=24))
    add("Run", gait(12, 0.13, 0.09, 0.05, 13, 0.07, swing=42, bend=75, toe=24, top=9, tail=16))
    sneak_arms = lambda ph: hands(L=(-0.2, 0.25 + (0.03 if ph in (1, 2) else 0), 0.66), R=(0.2, 0.25 + (0.03 if ph in (0, 3) else 0), 0.66),
                                  poleL=(-1, -0.3, -0.6), poleR=(1, -0.3, -0.6))
    crouch = dict(sq=0.8, lean=14)
    add("CrouchIdle", [
        (1, P(body=crouch, head=dict(turn=30, lean=-8), hands=sneak_arms(0))),
        (20, P(body=dict(sq=0.82, lean=14, up=0.01), head=dict(turn=-5, lean=-8), hands=sneak_arms(1))),
        (40, P(body=crouch, head=dict(turn=-32, lean=-8), hands=sneak_arms(2))),
        (61, P(body=crouch, head=dict(turn=30, lean=-8), hands=sneak_arms(0))),
    ])
    add("Sneak", gait(32, 0.11, 0.09, 0.02, 14, 0.02, arms=sneak_arms, roll=4, toe=-10, sneak=True, extra_body=dict(sq=0.82),
                      head=lambda ph: dict(turn=(30, 0, -30, 0)[ph], lean=-8)))
    add("CarryIdle", [
        (1, P(body=dict(sq=0.99), hands=CARRY)),
        (21, P(body=dict(sq=1.025, up=0.004), head=dict(turn=3), hands=CARRY, top=dict(lean=3))),
        (41, P(body=dict(sq=0.99), hands=CARRY)),
    ])
    add("CarryWalk", gait(12, 0.12, 0.07, 0.04, 6, 0.05, arms=CARRY, toe=20, top=8))
    sad = dict(lean=22, tilt=0)
    add("FiredIdle", [
        (1, P(body=dict(lean=6, sq=0.95), head=dict(sad), hands=FIRED_BOX)),
        (30, P(body=dict(lean=8, sq=0.93, up=-0.005), head=dict(lean=26, tilt=4), hands=FIRED_BOX, top=dict(lean=4))),
        (61, P(body=dict(lean=6, sq=0.95), head=dict(sad), hands=FIRED_BOX)),
    ])
    add("FiredWalk", gait(26, 0.08, 0.03, 0.015, 6, 0.02, arms=FIRED_BOX, roll=5, toe=10, head=dict(lean=22), extra_body=dict(sq=0.94)))

    # ---------------------------------------------------------------- work / activities
    def typing(period, amp, body_lean, head_fn=None, fast=False):
        f = [1 + round(period * i / 4) for i in range(5)]
        keys = []
        for i, fr in enumerate(f):
            ph = i % 4
            up_l = amp if ph in (0, 2) and ph == 0 else 0
            up_r = amp if ph == 2 else 0
            L = (TYPE_L[0] + (0.02 if ph == 1 else 0), TYPE_L[1], TYPE_L[2] + up_l)
            R = (TYPE_R[0] - (0.02 if ph == 3 else 0), TYPE_R[1], TYPE_R[2] + up_r)
            hd = dict(lean=8 + (2 if ph % 2 else 0))
            if head_fn:
                hd.update(head_fn(ph))
            keys.append((fr, P(body=dict(lean=body_lean, sq=1.0 + (0.01 if ph % 2 else 0)), head=hd,
                               hands=hands(L=L, R=R, poleL=(-1, -0.5, -0.4), poleR=(1, -0.5, -0.4)), top=dict(lean=2 if ph % 2 else -2))))
        return keys
    add("Typing", typing(16, 0.035, 6))
    add("WorkPose", typing(10, 0.05, 10, head_fn=lambda ph: dict(turn=(18, 0, -18, 0)[ph], lean=4)))

    phone_body = lambda t: P(body=dict(tilt=(1.5, -1.5)[t], sq=(1.0, 1.02)[t]), head=dict(tilt=9 + 2 * t, turn=-4 + 6 * t, lean=-3 * t),
                             hands=merge(PHONE_R, hands(L=((-0.36, 0.28, 0.6), (-0.42, 0.12, 0.42))[t], poleL=(-1, -0.4, -0.6))),
                             feet={"L": (0, 0, 0.0, (0, 12)[t])}, top=dict(lean=(0, 3)[t]))
    add("PhoneStand", [(1, phone_body(0)), (30, phone_body(1)), (61, phone_body(0))])
    add("PhoneArm", [
        (1, P(head=dict(tilt=9), hands=PHONE_R)),
        (30, P(head=dict(tilt=11, lean=-3), hands=dict(R=(0.37, 0.1, 0.85), poleR=(1, -0.1, -1.0)))),
        (61, P(head=dict(tilt=9), hands=PHONE_R)),
    ])

    def wipe(i):
        import math
        a = i * math.pi / 2
        return (0.14 + 0.07 * math.cos(a), 0.36 + 0.05 * math.sin(a), 0.58)
    clean_keys = []
    for i in range(5):
        clean_keys.append((1 + i * 6, P(body=dict(lean=16, tilt=(2, 0, -2, 0, 2)[i]), head=dict(lean=12, turn=(-8, 0, 8, 0, -8)[i]),
                                        hands=hands(L=(-0.16, 0.36, 0.66 + (0.02 if i % 2 else 0)), R=wipe(i % 4), poleL=(-1, -0.5, -0.5), poleR=(1, -0.5, -0.5)),
                                        top=dict(lean=(3, -3, 3, -3, 3)[i]))))
    add("Cleaning", clean_keys)

    dig = lambda ph: hands(L=(-0.16, 0.36, 0.28 + (0.06 if ph % 2 else 0)), R=(0.16, 0.38, 0.34 - (0.06 if ph % 2 else 0)),
                           poleL=(-1, -0.2, -0.3), poleR=(1, -0.2, -0.3))
    add("Rummaging", [(1 + 8 * i, P(body=dict(lean=36, sq=0.95), head=dict(lean=-6, turn=(25, 0, -25, 0, 25)[i]), hands=dig(i), top=dict(lean=(4, -4)[i % 2])))
                      for i in range(5)])
    fid = lambda ph: hands(L=(-0.07 - (0.02 if ph % 2 else 0), 0.34, 0.68), R=(0.07 + (0.02 if ph % 2 else 0), 0.35, 0.7 + (0.02 if ph % 2 else 0)),
                           poleL=(-1, -0.5, -0.5), poleR=(1, -0.5, -0.5))
    add("Fiddling", [(1 + 6 * i, P(body=dict(lean=10), head=dict(lean=8, turn=(0, 28, 0, -28, 0)[i]), hands=fid(i))) for i in range(5)])

    mug = (0.1, 0.36, 0.7)
    brew = lambda press, tap: P(body=dict(lean=4), head=dict(lean=-8 if not press else 4, turn=-6),
                                hands=hands(L=(-0.14, 0.33, 0.8 if press else 0.62), R=mug, poleL=(-1, -0.5, -0.6), poleR=(1, -0.5, -0.6)),
                                feet={"R": (0, 0.02, 0.0, 22 if tap else 0)})
    add("Brewing", [(1, brew(False, False)), (6, brew(True, False)), (11, brew(False, False)), (16, brew(True, False)), (21, brew(False, True)),
                    (26, brew(False, False)), (31, brew(False, True)), (36, brew(False, False)), (41, brew(False, False))])
    offer = lambda t: P(body=dict(lean=(4, 16)[t], sq=(1.0, 0.98)[t]), head=dict(lean=(0, 10)[t], tilt=(0, 6)[t]),
                        hands=hands(L=(-0.1, 0.42 + 0.04 * t, 0.62), R=(0.1, 0.42 + 0.04 * t, 0.62), poleL=(-1, -0.3, -0.8), poleR=(1, -0.3, -0.8)))
    add("Delivering", [(1, offer(0)), (16, offer(1)), (31, offer(0))])
    reach = lambda t: P(body=dict(lean=(10, 30)[t], sq=(1.0, 0.94)[t]), head=dict(lean=(4, -8)[t]),
                        hands=hands(L=(-0.13, 0.36, (0.6, 0.38)[t]), R=(0.13, 0.36, (0.6, 0.38)[t]), poleL=(-1, -0.3, -0.5), poleR=(1, -0.3, -0.5)))
    add("Placing", [(1, reach(0)), (13, reach(1)), (25, reach(0))])
    pour = lambda t: P(body=dict(lean=8), head=dict(turn=(30, -30, 0)[t], lean=-4),
                       hands=hands(L=(-0.38, -0.12, 0.38), R=(0.12, 0.38, 0.68), poleL=(-1, 0.4, -0.6), poleR=(1, -0.4, -0.7), twistR=(-20, -75, -60)[t]))
    add("Spilling", [(1, pour(0)), (11, pour(2)), (21, pour(1)), (31, pour(0))])

    # ---------------------------------------------------------------- meeting
    sit_feet = {"L": (0, 0.2, SIT_UP - 0.14, 25), "R": (0, 0.2, SIT_UP - 0.14, 25)}
    lap = hands(L=(-0.2, 0.26, 0.42), R=(0.2, 0.26, 0.42), poleL=(-1, -0.6, -0.3), poleR=(1, -0.6, -0.3))
    add("Sit", [
        (1, P(body=dict(up=SIT_UP, lean=-4, sq=0.98), hands=lap, feet=sit_feet)),
        (31, P(body=dict(up=SIT_UP, lean=-4, sq=1.01), head=dict(turn=5, tilt=-2), hands=lap,
               feet={"L": (0, 0.22, SIT_UP - 0.13, 30), "R": (0, 0.2, SIT_UP - 0.14, 22)})),
        (61, P(body=dict(up=SIT_UP, lean=-4, sq=0.98), hands=lap, feet=sit_feet)),
    ])
    gest = lambda t: hands(L=(-0.2, 0.26, 0.42), R=((0.3, 0.38, 0.66), (0.24, 0.42, 0.74), (0.34, 0.34, 0.62))[t], poleL=(-1, -0.6, -0.3), poleR=(1, -0.4, -0.7))
    add("SitTalk", [(1, P(body=dict(up=SIT_UP, lean=4), head=dict(lean=-4, turn=-6), hands=gest(0), feet=sit_feet)),
                    (14, P(body=dict(up=SIT_UP, lean=8, sq=0.98), head=dict(lean=6, turn=4, tilt=4), hands=gest(1), feet=sit_feet)),
                    (27, P(body=dict(up=SIT_UP, lean=5), head=dict(lean=-2, turn=-2), hands=gest(2), feet=sit_feet)),
                    (41, P(body=dict(up=SIT_UP, lean=4), head=dict(lean=-4, turn=-6), hands=gest(0), feet=sit_feet))])
    tgest = lambda t: hands(L=((-0.4, 0.2, 0.5), (-0.36, 0.3, 0.6), (-0.42, 0.18, 0.46))[t], R=((0.3, 0.38, 0.66), (0.26, 0.42, 0.76), (0.4, 0.3, 0.6))[t],
                            poleL=(-1, -0.4, -0.7), poleR=(1, -0.4, -0.7))
    add("Talk", [(1, P(body=dict(lean=2), head=dict(lean=-4, turn=-6), hands=tgest(0))),
                 (14, P(body=dict(lean=6, sq=0.98), head=dict(lean=6, turn=5, tilt=4), hands=tgest(1), top=dict(lean=4))),
                 (27, P(body=dict(lean=3, sq=1.02), head=dict(lean=-2), hands=tgest(2))),
                 (41, P(body=dict(lean=2), head=dict(lean=-4, turn=-6), hands=tgest(0)))])

    # ---------------------------------------------------------------- emotes (one-shots)
    def wave_h(x):
        return merge(hands(R=(0.46 + x, 0.12, 0.88), poleR=(1, -0.2, -0.8)), {"swingL": 0, "outL": 6})
    add("Wave", [
        (1, P(hands=rest_arms)),
        (7, P(body=dict(tilt=-6, sq=1.04), head=dict(tilt=-6), hands=wave_h(0.0), top=dict(lean=-4))),
        (12, P(body=dict(tilt=-6), head=dict(tilt=-4), hands=wave_h(0.08))),
        (17, P(body=dict(tilt=-6), head=dict(tilt=-6), hands=wave_h(-0.04))),
        (22, P(body=dict(tilt=-6), head=dict(tilt=-4), hands=wave_h(0.08))),
        (27, P(body=dict(tilt=-6), head=dict(tilt=-6), hands=wave_h(-0.04))),
        (32, P(body=dict(tilt=-5), head=dict(tilt=-4), hands=wave_h(0.06))),
        (45, P(hands=rest_arms)),
    ], loop=False)
    point_h = hands(L=(-0.42, 0.06, 0.36), R=(0.26, 0.5, 0.66), poleL=(-1, 0.3, -0.3), poleR=(1, -0.2, -0.3))
    add("Point", [
        (1, P(hands=rest_arms)),
        (6, P(body=dict(lean=-4, sq=1.04), head=dict(lean=-6), hands=hands(R=(0.36, 0.2, 0.74), poleR=(1, -0.3, -0.6)))),
        (10, P(body=dict(lean=10, sq=0.97, turn=-8), head=dict(lean=4, turn=-4), hands=point_h, top=dict(lean=8))),
        (14, P(body=dict(lean=12, sq=0.96, turn=-8), head=dict(lean=4, turn=-4), hands=point_h, top=dict(lean=-4))),
        (30, P(body=dict(lean=11, turn=-8), head=dict(lean=2, turn=-4), hands=point_h)),
        (40, P(hands=rest_arms)),
    ], loop=False)
    palm = hands(R=(0.06, 0.38, 0.76), poleR=(1, -0.5, -0.8), L=(-0.44, 0.04, 0.3), poleL=(-1, 0, -0.5))
    add("Facepalm", [
        (1, P(hands=rest_arms)),
        (8, P(body=dict(lean=4), head=dict(lean=8), hands=hands(R=(0.28, 0.34, 0.78), poleR=(1, -0.4, -0.8)))),
        (12, P(body=dict(lean=8, sq=0.94), head=dict(lean=22), hands=palm, top=dict(lean=8))),
        (20, P(body=dict(lean=8, sq=0.94), head=dict(lean=22, turn=12), hands=palm)),
        (28, P(body=dict(lean=8, sq=0.94), head=dict(lean=22, turn=-12), hands=palm)),
        (38, P(body=dict(lean=7, sq=0.95), head=dict(lean=20), hands=palm)),
        (50, P(hands=rest_arms)),
    ], loop=False)
    shrug_h = hands(L=(-0.5, 0.2, 0.56), R=(0.5, 0.2, 0.56), poleL=(-0.4, -0.2, -1), poleR=(0.4, -0.2, -1), twistL=40, twistR=-40)
    add("Shrug", [
        (1, P(hands=rest_arms)),
        (8, P(body=dict(sq=0.92), head=dict(tilt=10, lean=-6, up=-0.02), hands=shrug_h, top=dict(lean=-6))),
        (22, P(body=dict(sq=0.93), head=dict(tilt=12, lean=-8, up=-0.02), hands=shrug_h)),
        (28, P(body=dict(sq=1.03), hands=rest_arms, top=dict(lean=6))),
        (40, P(hands=rest_arms)),
    ], loop=False)
    belly = hands(L=(-0.22, 0.34, 0.36), R=(0.22, 0.34, 0.36), poleL=(-1, -0.3, -0.3), poleR=(1, -0.3, -0.3))
    laugh = []
    for i in range(9):
        laugh.append((1 + i * 5 + (5 if i == 8 else 0), P(body=dict(lean=-10 if i < 8 else 0, sq=(0.94, 1.05)[i % 2] if i < 8 else 1.0, up=(0, 0.02)[i % 2] if i < 8 else 0),
                                                        head=dict(lean=-12 if i < 8 else 0, tilt=(4, -4)[i % 2] if i < 8 else 0),
                                                        hands=belly if 0 < i < 8 else rest_arms, top=dict(lean=(6, -6)[i % 2]))))
    add("Laugh", laugh, loop=False)
    up_h = hands(L=(-0.46, 0.08, 0.84), R=(0.46, 0.08, 0.84), poleL=(-1, -0.2, -0.4), poleR=(1, -0.2, -0.4))
    add("Shocked", [
        (1, P(hands=rest_arms)),
        (4, P(body=dict(sq=0.86), head=dict(lean=4), hands=merge(rest_arms, {"outL": 20, "outR": 20}))),
        (8, P(root=dict(up=0.14, fwd=-0.06), body=dict(sq=1.16, lean=-10), head=dict(lean=-16, up=0.02), hands=up_h,
              feet={"L": (0, -0.02, 0.0, -30), "R": (0, -0.02, 0.0, -30)}, top=dict(lean=-12))),
        (13, P(root=dict(up=0.0, fwd=-0.12), body=dict(sq=0.88, lean=-6), head=dict(lean=-8), hands=up_h, top=dict(lean=10))),
        (18, P(root=dict(fwd=-0.12), body=dict(sq=1.04, lean=-6), head=dict(lean=-10), hands=up_h)),
        (30, P(root=dict(fwd=-0.12), body=dict(sq=1.0, lean=-3), head=dict(lean=-4), hands=hands(L=(-0.42, 0.14, 0.62), R=(0.42, 0.14, 0.62), poleL=(-1, -0.3, -0.5), poleR=(1, -0.3, -0.5)))),
    ], loop=False)
    flail = lambda t: hands(L=(-0.44, 0.1 + 0.1 * t, 0.8 - 0.1 * t), R=(0.44, 0.2 - 0.1 * t, 0.7 + 0.1 * t), poleL=(-1, -0.2, -0.4), poleR=(1, -0.2, -0.4))
    add("Panic", [
        (1, P(body=dict(sq=1.04, tilt=-4), head=dict(turn=-15, lean=-8), hands=flail(0), feet={"L": (0, 0.05, 0.07, 10)}, top=dict(lean=8))),
        (5, P(body=dict(sq=0.95, up=0.03), head=dict(turn=0, lean=-8), hands=flail(1))),
        (9, P(body=dict(sq=1.04, tilt=4), head=dict(turn=15, lean=-8), hands=flail(0), feet={"R": (0, 0.05, 0.07, 10)}, top=dict(lean=-8))),
        (13, P(body=dict(sq=0.95, up=0.03), head=dict(turn=0, lean=-8), hands=flail(1))),
        (17, P(body=dict(sq=1.04, tilt=-4), head=dict(turn=-15, lean=-8), hands=flail(0), feet={"L": (0, 0.05, 0.07, 10)}, top=dict(lean=8))),
    ])
    add("Cheer", [
        (1, P(body=dict(sq=0.88), hands=up_h, top=dict(lean=-6))),
        (6, P(root=dict(up=0.16), body=dict(sq=1.12), head=dict(lean=-10), hands=hands(L=(-0.38, 0.06, 0.9), R=(0.38, 0.06, 0.9), poleL=(-1, 0, -0.3), poleR=(1, 0, -0.3)),
              feet={"L": (0, -0.03, 0, -30), "R": (0, -0.03, 0, -30)}, top=dict(lean=10))),
        (11, P(root=dict(up=0.0), body=dict(sq=0.9), hands=up_h, top=dict(lean=-8))),
        (15, P(body=dict(sq=1.02), hands=up_h)),
        (21, P(body=dict(sq=0.88), hands=up_h, top=dict(lean=-6))),
    ])
    return clips


# --------------------------------------------------------------------------------------- manager clips

M_BEHIND = hands(L=(-0.24, -0.4, 0.4), R=(0.24, -0.4, 0.4), poleL=(-1, 0.6, -0.4), poleR=(1, 0.6, -0.4))
M_BELLY = hands(L=(-0.13, 0.42, 0.44), R=(0.13, 0.43, 0.48), poleL=(-1, -0.2, -0.6), poleR=(1, -0.2, -0.6))
M_CLIP = hands(L=(-0.16, 0.36, 0.62), R=(0.14, 0.4, 0.58), poleL=(-1, -0.4, -0.8), poleR=(1, -0.4, -0.8))
M_STEEPLE = hands(L=(-0.09, 0.43, 0.56), R=(0.09, 0.43, 0.56), poleL=(-1, -0.3, -0.7), poleR=(1, -0.3, -0.7))
M_HIPS = hands(L=(-0.5, 0.08, 0.4), R=(0.5, 0.08, 0.4), poleL=(-0.6, -1, 0.2), poleR=(0.6, -1, 0.2))
M_SIT_UP = 0.36


def pose_clipboard():
    return P(hands=M_CLIP, head=dict(lean=10))


def manager_clips(rig):
    clips = []

    def add(name, keys, loop=True):
        make_action(rig, name, keys, loop=loop)
        clips.append((name, loop, keys[-1][0] - keys[0][0] + (0 if loop else 1)))

    def idle(name, h, head_turn=5, extra=None):
        b0 = dict(sq=0.99, lean=-2)
        b1 = dict(sq=1.02, lean=-3, up=0.004)
        if extra:
            b0.update(extra)
            b1.update(extra)
        add(name, [(1, P(body=b0, hands=h)), (31, P(body=b1, head=dict(turn=head_turn, lean=-2), hands=h, top=dict(lean=3))), (61, P(body=b0, hands=h))])

    idle("Idle", M_BEHIND)
    idle("IdleCrossed", M_BELLY, head_turn=-4, extra=dict(lean=-6))
    idle("IdleClipboard", M_CLIP, head_turn=6)
    idle("IdleSteeple", M_STEEPLE, head_turn=-12, extra=dict(lean=10))
    idle("IdleHips", M_HIPS, head_turn=8)
    add("Walk", gait(22, 0.13, 0.05, 0.025, -3, 0.03, arms=M_BEHIND, roll=4, toe=16, top=5))
    insp = lambda t: P(body=dict(lean=14, sq=0.97), head=dict(lean=12, turn=(-30, 30)[t]), hands=M_CLIP, top=dict(lean=(4, -4)[t]))
    add("Inspect", [(1, insp(0)), (31, insp(1)), (61, insp(0))])
    fists = lambda t: hands(L=(-0.36, 0.24, 0.78 + 0.05 * t), R=(0.36, 0.24, 0.83 - 0.05 * t), poleL=(-1, -0.3, -0.8), poleR=(1, -0.3, -0.8))
    add("Angry", [
        (1, P(body=dict(sq=0.95, lean=6, tilt=4), head=dict(lean=8), hands=fists(0), feet={"L": (0, 0.02, 0.06, 0)}, top=dict(lean=6))),
        (7, P(body=dict(sq=1.05, lean=8, tilt=-4, up=0.02), head=dict(lean=4), hands=fists(1), top=dict(lean=-6))),
        (13, P(body=dict(sq=0.95, lean=6, tilt=4), head=dict(lean=8), hands=fists(0), feet={"R": (0, 0.02, 0.06, 0)}, top=dict(lean=6))),
        (19, P(body=dict(sq=1.05, lean=8, tilt=-4, up=0.02), head=dict(lean=4), hands=fists(1), top=dict(lean=-6))),
        (25, P(body=dict(sq=0.95, lean=6, tilt=4), head=dict(lean=8), hands=fists(0), feet={"L": (0, 0.02, 0.06, 0)}, top=dict(lean=6))),
    ])
    add("Pleased", [
        (1, P(body=dict(sq=1.0, lean=-4), head=dict(lean=-4), hands=M_BELLY)),
        (8, P(body=dict(sq=0.97, lean=-2), head=dict(lean=10), hands=M_BELLY, top=dict(lean=5))),
        (15, P(body=dict(sq=1.03, lean=-5, up=0.01), head=dict(lean=-6), hands=M_BELLY)),
        (22, P(body=dict(sq=0.97, lean=-2), head=dict(lean=10), hands=M_BELLY, top=dict(lean=5))),
        (41, P(body=dict(sq=1.0, lean=-4), head=dict(lean=-4), hands=M_BELLY)),
    ])
    tg = lambda t: hands(L=(-0.24, -0.4, 0.4), R=((0.36, 0.4, 0.68), (0.3, 0.46, 0.8), (0.42, 0.34, 0.62))[t], poleL=(-1, 0.6, -0.4), poleR=(1, -0.4, -0.7))
    add("Talk", [(1, P(body=dict(lean=2), head=dict(turn=-6), hands=tg(0))), (14, P(body=dict(lean=6, sq=0.98), head=dict(lean=6, turn=5), hands=tg(1))),
                 (27, P(body=dict(lean=3), head=dict(lean=-2), hands=tg(2))), (41, P(body=dict(lean=2), head=dict(turn=-6), hands=tg(0)))])
    pt = hands(L=(-0.5, 0.08, 0.4), R=(0.24, 0.56, 0.7), poleL=(-0.6, -1, 0.2), poleR=(1, -0.2, -0.3))
    add("Point", [(1, P(body=dict(lean=10, turn=-8), head=dict(lean=4), hands=pt)),
                  (8, P(body=dict(lean=13, turn=-8, sq=0.97), head=dict(lean=6), hands=pt, top=dict(lean=6))),
                  (16, P(body=dict(lean=10, turn=-8), head=dict(lean=4), hands=pt))])
    sit_feet = {"L": (0, 0.2, M_SIT_UP - 0.12, 25), "R": (0, 0.2, M_SIT_UP - 0.12, 25)}
    add("Sit", [(1, P(body=dict(up=M_SIT_UP, lean=-6), hands=M_BELLY, feet=sit_feet)),
                (31, P(body=dict(up=M_SIT_UP, lean=-6, sq=1.01), head=dict(turn=6), hands=M_BELLY, feet=sit_feet)),
                (61, P(body=dict(up=M_SIT_UP, lean=-6), hands=M_BELLY, feet=sit_feet))])
    sg = lambda t: hands(L=(-0.13, 0.42, 0.44), R=((0.36, 0.4, 0.68), (0.3, 0.46, 0.8), (0.42, 0.34, 0.62))[t], poleL=(-1, -0.2, -0.6), poleR=(1, -0.4, -0.7))
    add("SitTalk", [(1, P(body=dict(up=M_SIT_UP, lean=2), head=dict(turn=-6), hands=sg(0), feet=sit_feet)),
                    (14, P(body=dict(up=M_SIT_UP, lean=6), head=dict(lean=6, turn=5), hands=sg(1), feet=sit_feet)),
                    (27, P(body=dict(up=M_SIT_UP, lean=3), head=dict(lean=-2), hands=sg(2), feet=sit_feet)),
                    (41, P(body=dict(up=M_SIT_UP, lean=2), head=dict(turn=-6), hands=sg(0), feet=sit_feet))])
    return clips
