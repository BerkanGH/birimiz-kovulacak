"""Dev-time music generation via the official ElevenLabs Music API (POST /v1/music). Run: python -I Tools/music/generate_music.py [--cue ID ...] [--force] [--max-requests N]
Reads ELEVENLABS_API_KEY from the environment only; never prints or stores it. Not run by builds."""
import argparse, json, os, sys, time, datetime, urllib.request, urllib.error

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
MANIFEST = os.path.join(ROOT, "Tools", "music", "music_manifest.json")
PROV = os.path.join(ROOT, "Tools", "music", "provenance.json")
SAFE_HEADERS = ("song-id", "request-id", "content-type", "content-length", "x-model-id", "date")

def load(path, default):
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    return default

def request_once(m, cue, key):
    body = {"prompt": cue["prompt"], "music_length_ms": int(cue["target_duration_s"] * 1000),
            "model_id": m["model_id"], "force_instrumental": m["force_instrumental"]}
    url = m["endpoint"] + "?output_format=" + m["output_format"]
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST",
                                 headers={"xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg"})
    with urllib.request.urlopen(req, timeout=600) as r:
        return r.read(), {k.lower(): v for k, v in r.headers.items()}

def generate(m, cue, key, retries):
    delay = 5
    for attempt in range(retries + 1):
        try:
            return request_once(m, cue, key)
        except urllib.error.HTTPError as e:
            detail = e.read()[:400].decode("utf-8", "replace").replace(key, "***")
            print(f"  HTTP {e.code} for {cue['id']}: {detail}", file=sys.stderr)
            if e.code in (429, 500, 502, 503, 504) and attempt < retries:
                time.sleep(delay); delay *= 3; continue
            raise
        except (urllib.error.URLError, TimeoutError) as e:
            print(f"  network error for {cue['id']}: {type(e).__name__}", file=sys.stderr)
            if attempt < retries:
                time.sleep(delay); delay *= 3; continue
            raise

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cue", action="append")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--max-requests", type=int, default=12, help="hard cap on HTTP generations this run")
    ap.add_argument("--retries", type=int, default=0, help="retries on 429/5xx (each counts toward the cap)")
    a = ap.parse_args()
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set (generation is a dev-time tool; gameplay does not need it).")
    m = load(MANIFEST, None)
    prov = load(PROV, {"assets": {}})
    used = 0; failed = []
    for cue in m["cues"]:
        if a.cue and cue["id"] not in a.cue: continue
        src = os.path.join(ROOT, cue["source"])
        if os.path.exists(src) and not a.force and cue["id"] in prov["assets"]:
            print(f"skip {cue['id']} (done)"); continue
        if used >= a.max_requests:
            print("request cap reached"); break
        os.makedirs(os.path.dirname(src), exist_ok=True)
        print(f"generate {cue['id']} ({cue['target_duration_s']}s)")
        try:
            used += 1
            data, hdr = generate(m, cue, key, min(a.retries, max(0, a.max_requests - used)))
        except Exception as e:
            failed.append(cue["id"]); print(f"  FAILED {cue['id']}: {type(e).__name__}", file=sys.stderr); continue
        with open(src, "wb") as f: f.write(data)
        prov["assets"][cue["id"]] = {
            "cue_id": cue["id"], "intended_use": cue["use"], "prompt": cue["prompt"], "provider": m["provider"],
            "endpoint": m["endpoint"], "model_id_requested": m["model_id"], "output_format": m["output_format"],
            "force_instrumental": m["force_instrumental"], "requested_length_ms": int(cue["target_duration_s"] * 1000),
            "generated_utc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "response_headers": {k: v for k, v in hdr.items() if k in SAFE_HEADERS},
            "song_id": hdr.get("song-id"), "source_path": cue["source"], "integrated_path": cue["integrated"],
            "bytes": len(data), "loop": cue["loop"], "crossfade_ms": cue["crossfade_ms"], "post_processing": None}
        with open(PROV, "w", encoding="utf-8") as f: json.dump(prov, f, indent=2)  # persist after each success
    print(f"requests used: {used}; failed: {failed}")
    sys.exit(1 if failed else 0)

if __name__ == "__main__":
    main()
