"""Finish built-in v4 generations using the existing v3 card geometry.

No scene content is drawn here. This performs requested deterministic framing,
alpha cleanup, proportional scaling, metadata, review sheets and Resources copies.
Run with the Blender Python documented in README.md; Pillow comes from kit.py.
"""
import sys
sys.dont_write_bytecode = True
import hashlib
import json
import shutil
from kit import ROOT, Image, ImageOps, metadata, save, sheet
from v3_cards_finish import frame
from v4_spec import EXPECTED, CATEGORIES, SAFE_AREA, DESCRIPTIONS

REPO = ROOT.parents[1]
TOOLS = ROOT / '_tools'
LEDGER = TOOLS / 'v4_generation.json'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    ledger = json.loads(LEDGER.read_text(encoding='utf-8'))
    baseline_path = TOOLS / 'v4_baseline.json'
    manifest_path = ROOT / 'manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    if not baseline_path.exists():
        assert len(manifest) == 308, 'Capture baseline before adding v4 assets'
        old_paths = {r['path'] for r in manifest}
        inventory = [p for p in ROOT.rglob('*') if p.suffix.lower() in ('.png', '.ico')
                     and not any(part.startswith('_') for part in p.relative_to(ROOT).parts)]
        assert {'Art/UI/' + p.relative_to(ROOT).as_posix() for p in inventory} == old_paths
        meta = {str(p.relative_to(REPO)).replace('\\', '/'): digest(p)
                for p in ROOT.rglob('_metadata.json')}
        unity = REPO / 'Game/Assets/Game/UI/Resources/ui'
        write(baseline_path, dict(manifest=manifest,
            assetSha256={r['path']: digest(REPO / r['path']) for r in manifest},
            metadataSha256=meta,
            unitySha256={p.relative_to(REPO).as_posix(): digest(p)
                         for p in unity.rglob('*') if p.is_file()}))
    baseline = json.loads(baseline_path.read_text(encoding='utf-8'))
    assert all(digest(REPO / rel) == sha for rel, sha in baseline['assetSha256'].items())
    assert len(ledger['records']) == len(EXPECTED) == 23
    assert {f"{r['category']}/{r['id']}.png" for r in ledger['records']} == set(EXPECTED)
    rows = {c: [] for c in CATEGORIES}
    for rec in ledger['records']:
        rel = f"{rec['category']}/{rec['id']}.png"
        src_path = REPO / rec['source']
        source = Image.open(src_path).convert('RGBA')
        rec['sourceSha256'] = digest(src_path)
        rec['sourceSize'] = list(source.size)
        rec['sourceAlphaExtrema'] = list(source.getchannel('A').getextrema())
        rec['referenceSha256'] = {p: digest(REPO / p) for p in rec['refs']}
        rec['referenceArchive'] = {'Art/UI/_sheets/v4_bean_references.png':
                                  'Art/UI/_tools/v4_sources/bean_references.png'} if any('_sheets' in p for p in rec['refs']) else {}
        for attempt in rec.get('attempts', []):
            attempt['sourceSha256'] = digest(REPO / attempt['source'])
        if rec.get('attempts'):
            rec['retryReferenceSha256'] = {p: digest(REPO / (rec['attempts'][0]['source'] if p == rec['source'] else p)) for p in rec['retryRefs']}
            rec['retryReferenceArchive'] = {rec['source']: rec['attempts'][0]['source']}
        if rec['category'] in ('tasks', 'evidence'):
            alpha = source.getchannel('A')
            assert alpha.getextrema() == (0, 255), 'Source needs true alpha: ' + rel
            alpha = alpha.point(lambda v: 0 if v < 24 else v)
            source.putalpha(alpha)
            crop = alpha.point(lambda v: 255 if v > 100 else 0).getbbox()
            source = source.crop(crop)
            source.thumbnail((410, 448), Image.Resampling.LANCZOS)
            offset = ((512 - source.width) // 2, 62 + (448 - source.height) // 2)
            out = frame()
            out.alpha_composite(source, offset)
            rec['postProcessing'] = dict(pipeline='v3_cards_finish.frame + v4_finish.py',
                alphaBelow24Cleared=True, trimBounds=list(crop), maxIllustrationSize=[410,448],
                placedSize=list(source.size), offset=list(offset), safeArea=list(SAFE_AREA),
                canvas=[512,716], sceneContentAltered=False, encoding='lossless RGBA PNG, optimize=True')
        else:
            assert source.getchannel('A').getextrema() == (255, 255), 'Wide source must be opaque'
            # The source aspect is within 0.1% of 16:9; fit only trims that tiny edge difference.
            out = ImageOps.fit(source, (1280, 720), Image.Resampling.LANCZOS, centering=(0.5,0.5))
            rec['postProcessing'] = dict(pipeline='v4_finish.py', proportionalFit=[1280,720],
                sourceAspect=source.width/source.height, canvas=[1280,720],
                sceneContentAltered=False, encoding='lossless RGBA PNG, optimize=True')
        path = ROOT / rel
        save(out, path)
        desc = DESCRIPTIONS[rec['id']]
        if rec['category'] in ('tasks', 'evidence'):
            desc += ' Exact v3 rounded manila frame; blank runtime title/body safe area [52,552,408,104].'
        rows[rec['category']].append(metadata(path, rec['category'], desc, text=rec['id']=='task_server_code'))
        rec['finalPath'] = 'Art/UI/' + rel
        rec['finalSha256'] = digest(path)
        target = REPO / 'Game/Assets/Game/UI/Resources/ui' / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        rec['resourcesPath'] = target.relative_to(REPO).as_posix()
    new_entries = []
    for category, entries in rows.items():
        entries.sort(key=lambda r: r['id'])
        write(ROOT / category / '_metadata.json', entries)
        new_entries.extend(entries)
    # Append to the original sequence; never rebuild or reorder the existing kit.
    write(manifest_path, baseline['manifest'] + new_entries)
    write(LEDGER, ledger)
    sheet((ROOT/'tasks').glob('*.png'), ROOT/'_sheets/v4_tasks.png', cols=6, cell=(160,235))
    sheet((ROOT/'evidence').glob('*.png'), ROOT/'_sheets/v4_evidence.png', cols=6, cell=(160,235))
    for category in ('tasks', 'evidence'):
        paths = sorted((ROOT/category).glob('*.png'))
        for start in range(0,len(paths),6):
            sheet(paths[start:start+6], ROOT/f'_sheets/v4_{category}_detail_{start//6+1}.png', cols=3, cell=(300,450))
    wide = [ROOT/p for p in EXPECTED if p.startswith(('onboarding_v4/', 'results_v4/'))]
    sheet(wide, ROOT/'_sheets/v4_wide.png', cols=2, cell=(640,415))
    print(json.dumps(dict(finished=len(new_entries), counts={c:len(r) for c,r in rows.items()},
                         unityCopies=len(new_entries), retries=sum(r['retries'] for r in ledger['records'])),indent=2))


if __name__ == '__main__':
    main()
