"""Fast Unity-free compile check for the Game and Editor assemblies (seconds instead of a Unity import).

Reuses the csproj files Unity generated in the *main* checkout (references to UnityEngine, packages, analyzers)
but compiles the sources of the checkout this script lives in, so it also works inside agent worktrees that have
no Library folder. Generated projects go to <checkout>/Builds/compilecheck (ignored).

    python -I Tools/scripts/compile_check.py            # Core + Game + Editor
    python -I Tools/scripts/compile_check.py --player   # also drop UNITY_EDITOR defines for the Game assembly
    python -I Tools/scripts/compile_check.py --engine-stubs   # compile Core/Line/Engine as Tools/stubs (contracts only)

Exit code 0 = compiled. Warnings are not printed unless --warnings is given.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ASSEMBLIES = [("Core", r"Assets\Game\Core"), ("Game", r"Assets\Game\Scripts"), ("Editor", r"Assets\Game\Editor")]


def main_checkout():
    common = subprocess.run(["git", "-C", ROOT, "rev-parse", "--git-common-dir"], capture_output=True, text=True).stdout.strip()
    common = os.path.abspath(os.path.join(ROOT, common)) if common else os.path.join(ROOT, ".git")
    return os.path.dirname(common)


def generate(main_game, out_dir, name, src_dir, player, stubs=False):
    src = os.path.join(main_game, "BirimizKovulacak.%s.csproj" % name)
    if not os.path.exists(src):
        sys.exit("Missing %s - open the project in Unity once (or run Tools/build.ps1) so it generates csproj files." % src)
    text = open(src, encoding="utf-8-sig").read()
    text = re.sub(r"\s*<Compile Include=\"[^\"]*\"\s*/>", "", text)
    text = re.sub(r"\s*<None Include=\"[^\"]*\"\s*/>", "", text)
    glob = os.path.join(ROOT, "Game", src_dir, "**", "*.cs")
    extra = "<ItemGroup>\n    <Compile Include=\"%s\" />" % glob
    if stubs and name == "Core":
        # Parallel tracks compile against the contracts while the engine is being rewritten.
        engine = os.path.join(ROOT, "Game", src_dir, "Line", "Engine", "**", "*.cs")
        extra = "<ItemGroup>\n    <Compile Include=\"%s\" Exclude=\"%s\" />" % (glob, engine)
        extra += "\n    <Compile Include=\"%s\" />" % os.path.join(ROOT, "Tools", "stubs", "LineEngineStubs.cs")
    # Managed plugins added in this checkout (e.g. Assets/Plugins/Concentus) are not in Unity's csproj yet.
    if name != "Core":
        for dirpath, _, files in os.walk(os.path.join(ROOT, "Game", "Assets", "Plugins")):
            for f in files:
                if f.lower().endswith(".dll") and ("<Reference Include=\"%s\"" % f[:-4]) not in text:
                    extra += "\n    <Reference Include=\"%s\"><HintPath>%s</HintPath><Private>False</Private></Reference>" % (f[:-4], os.path.join(dirpath, f))
    text = text.replace("<ItemGroup>", extra, 1)
    text = re.sub(r"<HintPath>(?![A-Za-z]:\\)", "<HintPath>" + main_game.replace("\\", "\\\\") + r"\\", text)
    text = re.sub(r"<ProjectReference Include=\"BirimizKovulacak\.(\w+)\.csproj\"",
                  lambda m: "<ProjectReference Include=\"..\\%s\\BirimizKovulacak.%s.csproj\"" % (m.group(1), m.group(1)), text)
    text = re.sub(r"<OutputPath>[^<]*</OutputPath>", "<OutputPath>bin\\\\</OutputPath>", text)
    text = re.sub(r"<BaseIntermediateOutputPath>[^<]*</BaseIntermediateOutputPath>", "", text)
    if player and name == "Game":
        text = re.sub(r";UNITY_EDITOR[A-Z0-9_]*", "", text)
        text = text.replace(";UNITY_INCLUDE_TESTS", "")
    d = os.path.join(out_dir, name)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, "BirimizKovulacak.%s.csproj" % name), "w", encoding="utf-8") as f:
        f.write(text)
    return os.path.join(d, "BirimizKovulacak.%s.csproj" % name)


def main():
    player = "--player" in sys.argv
    warnings = "--warnings" in sys.argv
    stubs = "--engine-stubs" in sys.argv
    main_game = os.path.join(main_checkout(), "Game")
    out_dir = os.path.join(ROOT, "Builds", "compilecheck")
    paths = [generate(main_game, out_dir, n, s, player, stubs) for n, s in ASSEMBLIES]
    proc = subprocess.run(["dotnet", "build", paths[-1], "-nologo", "-v", "q", "-clp:NoSummary"], capture_output=True, text=True)
    lines = [l for l in proc.stdout.splitlines() + proc.stderr.splitlines() if l.strip()]
    errors = sorted(set(l.strip() for l in lines if ": error " in l))
    shown = errors + (sorted(set(l.strip() for l in lines if ": warning " in l)) if warnings else [])
    for l in shown:
        print(l.replace(ROOT + os.sep, ""))
    if proc.returncode != 0 and not errors:
        print("\n".join(lines[-30:]))
    print("COMPILE %s (%d errors)" % ("OK" if proc.returncode == 0 else "FAILED", len(errors)))
    sys.exit(proc.returncode)


if __name__ == "__main__":
    main()
