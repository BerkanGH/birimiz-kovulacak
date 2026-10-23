"""Extract a .unitypackage (gzipped tar of <guid>/{asset,asset.meta,pathname}) into a Unity project.
Usage: python extract_unitypackage.py <package> <project_dir>"""
import os
import sys
import tarfile

pkg, project = sys.argv[1], sys.argv[2]
entries = {}
with tarfile.open(pkg, "r:gz") as tar:
    for m in tar.getmembers():
        parts = m.name.replace("\\", "/").lstrip("./").split("/")
        if len(parts) != 2 or not m.isfile():
            continue
        guid, kind = parts
        entries.setdefault(guid, {})[kind] = tar.extractfile(m).read()
count = 0
for guid, files in entries.items():
    if "pathname" not in files:
        continue
    rel = files["pathname"].decode("utf-8").splitlines()[0].strip()
    dest = os.path.normpath(os.path.join(project, rel))
    if not dest.startswith(os.path.normpath(project)):
        continue
    if "asset" in files:
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write(files["asset"])
        count += 1
    else:
        os.makedirs(dest, exist_ok=True)
    if "asset.meta" in files:
        with open(dest + ".meta", "wb") as f:
            f.write(files["asset.meta"])
print("extracted", count, "assets")
