"""Process downloaded music into Unity-ready OGG loops/stingers.
Run: python -I Tools/music/process_music.py [--cue ID ...]
Needs ffmpeg/ffprobe on PATH. Pure stdlib. Writes Tools/music/measurements.json and updates provenance.json post_processing.
Chain: decode -> (stingers: trim edge silence) -> (loops: equal-power qsin crossfade of tail into head baked into the seam)
-> single linear gain to the LUFS target (re-measured, lowered if true peak > limit) -> stinger fades -> libvorbis q5 -> verify.
"""
import argparse
import array
import json
import math
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
D = os.path.join(ROOT, "Tools", "music")
TARGET = {"loop": -19.0, "stinger": -17.0}   # integrated LUFS
TP_LIMIT = -1.5
PRE_TP = -2.3   # pre-encode headroom: lossy Vorbis can overshoot the PCM true peak
SR = 44100


def run(args, capture=False):
    r = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        sys.exit("command failed: " + " ".join(args[:3]) + "\n" + r.stderr[-800:])
    return r.stderr if capture else ""


def ff(*a, capture=False):
    return run(["ffmpeg", "-hide_banner", "-nostdin", "-y"] + list(a), capture)


def duration(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                       capture_output=True, text=True)
    return float(r.stdout.strip())


def stream_info(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
                        "stream=codec_name,sample_rate,channels", "-of", "json", path], capture_output=True, text=True)
    return json.loads(r.stdout)["streams"][0]


def measure(path):
    err = ff("-i", path, "-af", "ebur128=peak=true", "-f", "null", "-", capture=True)
    tail = err[err.rfind("Summary:"):]
    i = float(re.search(r"I:\s+(-?[\d.]+) LUFS", tail).group(1))
    tp = float(re.search(r"Peak:\s+(-?[\d.]+) dBFS", tail).group(1))
    return i, tp


def silences(path):
    err = ff("-i", path, "-af", "silencedetect=noise=-50dB:d=1.5", "-f", "null", "-", capture=True)
    return len(re.findall(r"silence_start", err))


def pcm(path):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2",
                        "-ar", str(SR), "-"], capture_output=True)
    a = array.array("f")
    a.frombytes(r.stdout)
    return a


def seam(path):
    """Wrap-point discontinuity: |last-first| per channel relative to the 99th-percentile |sample step| over the last 2 s,
    plus the 50 ms RMS level step (dB) between the end and the start."""
    a = pcm(path)
    n = len(a) // 2
    worst = 0.0
    for ch in (0, 1):
        x = a[ch::2]
        steps = sorted(abs(x[i] - x[i - 1]) for i in range(n - 2 * SR, n))
        p99 = steps[int(len(steps) * 0.99)] + 1e-9
        worst = max(worst, abs(x[0] - x[-1]) / p99)

    def rms_db(lo, hi):
        seg = a[2 * lo:2 * hi]
        s = sum(v * v for v in seg) / max(1, len(seg))
        return 10 * math.log10(max(s, 1e-12))

    w = SR // 20
    return round(worst, 2), round(abs(rms_db(0, w) - rms_db(n - w, n)), 2)


def level_profile(a):
    """250 ms RMS levels (dB) of interleaved stereo float samples."""
    n = len(a) // 2
    w = SR // 4
    out = []
    for i in range(0, n - w + 1, w):
        seg = a[2 * i:2 * (i + w)]
        out.append(10 * math.log10(max(sum(v * v for v in seg) / len(seg), 1e-12)))
    return out


def body_range(a, drop_db=4.0):
    """Seconds range [t0, t1] that excludes the generated fade-in/fade-out (windows more than drop_db below the 70th percentile)."""
    lv = level_profile(a)
    ref = sorted(lv)[int(len(lv) * 0.7)]
    ok = [i for i, v in enumerate(lv) if v >= ref - drop_db]
    return ok[0] * 0.25, (ok[-1] + 1) * 0.25


def dip_db(path):
    """Quietest 250 ms level in the first/last 2 s relative to the 10th-percentile level of the whole file
    (sparse beds are naturally spiky, so the median would flag normal gaps). Negative = the seam is quieter than the music's own quietest parts."""
    lv = level_profile(pcm(path))
    p10 = sorted(lv)[int(len(lv) * 0.1)]
    edge = lv[:8] + lv[-8:]
    return round(min(edge) - p10, 1)


def process(cue, tmp):
    cid = cue["id"]
    src = os.path.join(ROOT, cue["source"])
    dst = os.path.join(ROOT, cue["integrated"])
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    w0, w1, w2 = (os.path.join(tmp, cid + s + ".wav") for s in ("_0", "_1", "_2"))
    steps = ["decode to 44.1 kHz stereo float"]
    af = []
    af.append("silenceremove=start_periods=1:start_threshold=-55dB:stop_periods=1:stop_threshold=-55dB:stop_duration=0.3")
    steps.append("trim leading/trailing silence (<-55 dB)")
    ff("-i", src, "-ar", str(SR), "-ac", "2", *(["-af", ",".join(af)] if af else []), "-c:a", "pcm_f32le", w0)
    n = duration(w0)
    raw_seam = seam(w0) if cue["loop"] else None
    if cue["loop"]:
        t0, t1 = body_range(pcm(w0))
        w0b = os.path.join(tmp, cid + "_0b.wav")
        ff("-i", w0, "-af", f"atrim=start={t0:.3f}:end={t1:.3f},asetpts=PTS-STARTPTS", "-c:a", "pcm_f32le", w0b)
        w0 = w0b
        n = duration(w0)
        steps.append(f"cut generated fade-in/out: keep {t0:.2f}-{t1:.2f} s of the decoded source (levels within 4 dB of the 70th percentile)")
        c = cue["crossfade_ms"] / 1000.0
        flt = (f"[0:a]atrim=start={n - c:.6f},asetpts=PTS-STARTPTS[t];[0:a]atrim=end={n - c:.6f},asetpts=PTS-STARTPTS[b];"
               f"[t][b]acrossfade=d={c}:c1=qsin:c2=qsin[o]")
        ff("-i", w0, "-filter_complex", flt, "-map", "[o]", "-c:a", "pcm_f32le", w1)
        steps.append(f"equal-power (qsin) {cue['crossfade_ms']} ms tail->head crossfade baked into the loop seam")
    else:
        w1 = w0
    cur, _ = measure(w1)
    target = TARGET[cue["kind"]]
    gain = target - cur
    fade = []
    if cue["kind"] == "stinger":
        d = duration(w1)
        fade = ["afade=t=in:d=0.01", f"afade=t=out:st={max(0, d - 0.35):.3f}:d=0.35"]
        steps.append("10 ms fade-in, 350 ms fade-out")
    for _ in range(4):
        ff("-i", w1, "-af", ",".join([f"volume={gain:.3f}dB"] + fade), "-c:a", "pcm_f32le", w2)
        i2, tp2 = measure(w2)
        if tp2 > PRE_TP:
            gain -= (tp2 - PRE_TP) + 0.1
            continue
        if abs(i2 - target) > 0.3 and tp2 + (target - i2) <= PRE_TP:
            gain += target - i2
            continue
        break
    steps.append(f"linear gain {gain:+.2f} dB toward {target} LUFS (true peak limit {TP_LIMIT} dBTP, no limiter/compression)")
    ff("-i", w2, "-c:a", "libvorbis", "-q:a", "5", "-ar", str(SR), dst)
    steps.append("encode OGG Vorbis q5 44.1 kHz stereo")
    li, ltp = measure(dst)
    si = stream_info(dst)
    res = {"cue": cid, "kind": cue["kind"], "source_duration_s": round(n, 3), "duration_s": round(duration(dst), 3),
           "codec": si["codec_name"], "sample_rate": int(si["sample_rate"]), "channels": si["channels"],
           "integrated_lufs": li, "true_peak_dbtp": ltp, "target_lufs": target, "gain_db": round(gain, 2),
           "silences_over_1_5s_below_-50dB": silences(dst), "bytes": os.path.getsize(dst)}
    if cue["loop"]:
        j, lv = seam(dst)
        res.update({"seam_jump_ratio": j, "seam_level_step_db": lv, "seam_jump_ratio_unprocessed": raw_seam[0],
                    "seam_level_step_unprocessed_db": raw_seam[1], "crossfade_ms": cue["crossfade_ms"]})
        res["edge_dip_db"] = dip_db(dst)
        res["seam_ok"] = j <= 1.5 and lv <= 6.0 and res["edge_dip_db"] >= -3.0
    res["checks_ok"] = (res["codec"] == "vorbis" and res["sample_rate"] == SR and ltp <= TP_LIMIT
                        and abs(li - target) <= (1.0 if cue["loop"] else 3.0) and (not cue["loop"] or res["seam_ok"]))
    return res, steps


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cue", action="append")
    a = ap.parse_args()
    m = json.load(open(os.path.join(D, "music_manifest.json"), encoding="utf-8"))
    pp = os.path.join(D, "provenance.json")
    prov = json.load(open(pp, encoding="utf-8"))
    mp = os.path.join(D, "measurements.json")
    meas = json.load(open(mp, encoding="utf-8")) if os.path.exists(mp) else {}
    bad = []
    with tempfile.TemporaryDirectory() as tmp:
        for cue in m["cues"]:
            if a.cue and cue["id"] not in a.cue:
                continue
            if not os.path.exists(os.path.join(ROOT, cue["source"])):
                print("missing source", cue["id"])
                bad.append(cue["id"])
                continue
            res, steps = process(cue, tmp)
            meas[cue["id"]] = res
            if cue["id"] in prov["assets"]:
                pa = prov["assets"][cue["id"]]
                pa["post_processing"] = steps
                pa["integrated_duration_s"] = res["duration_s"]
                pa["integrated_format"] = "ogg vorbis q5 44.1kHz stereo"
            print(cue["id"], res["duration_s"], "s", res["integrated_lufs"], "LUFS", res["true_peak_dbtp"], "dBTP",
                  "seam", res.get("seam_jump_ratio"), "/", res.get("seam_jump_ratio_unprocessed"),
                  "OK" if res["checks_ok"] else "CHECK FAILED")
            if not res["checks_ok"]:
                bad.append(cue["id"])
            json.dump(meas, open(mp, "w", encoding="utf-8"), indent=2)
            json.dump(prov, open(pp, "w", encoding="utf-8"), indent=2)
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
