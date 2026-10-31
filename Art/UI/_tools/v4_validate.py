"""Read-only delivery audit; writes only v4_validation.json. Exit 1 on errors.

Checks the exact 23-file brief, hashes of every prior kit asset and Unity file,
new metadata, blank localization regions, exact v3 frame, genuine alpha,
source provenance, bounded retries, visual review hashes and Resources copies.
Historical v3 ignored archives are not needed or re-created.
"""
import sys
sys.dont_write_bytecode = True
import argparse
import collections
import datetime
import hashlib
import json
from kit import ROOT, Image
from v3_cards_finish import frame
from v4_spec import EXPECTED, CATEGORIES, FIELDS, SAFE_AREA

REPO = ROOT.parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main(strict=True):
    errors, assets = [], []
    check_count = 0

    def check(ok, message):
        nonlocal check_count
        check_count += 1
        if not ok:
            errors.append(message)

    def local(rel):
        path = (REPO / rel).resolve()
        if REPO not in path.parents:
            raise ValueError('Path outside worktree: ' + rel)
        return path

    try:
        manifest = read(ROOT/'manifest.json')
        baseline = read(ROOT/'_tools/v4_baseline.json')
        ledger = read(ROOT/'_tools/v4_generation.json')
        review = read(ROOT/'_tools/v4_review.json')
        check(len(baseline['manifest']) == 308, 'Prior manifest must have 308 entries')
        check(len(manifest) == 331, 'Combined manifest must have 331 entries')
        check(manifest[:308] == baseline['manifest'], 'Original manifest entries changed or reordered')
        check(len({r['id'] for r in manifest}) == len(manifest), 'Duplicate manifest IDs')
        check(len({r['path'] for r in manifest}) == len(manifest), 'Duplicate manifest paths')
        check(all(set(r) == FIELDS for r in manifest), 'Manifest nine-field schema differs')
        verified = collections.Counter()
        for section in ('assetSha256', 'metadataSha256', 'unitySha256'):
            for rel, expected_hash in baseline[section].items():
                path = local(rel)
                ok = path.is_file() and digest(path) == expected_hash
                check(ok, 'Pre-v4 file modified/missing: ' + rel)
                verified[section] += int(ok)
        exact_paths = {'Art/UI/' + rel for rel in EXPECTED}
        check({r['path'] for r in manifest[308:]} == exact_paths, 'New manifest inventory differs from brief')
        records = ledger['records']
        check(len(records) == 23 and {r['finalPath'] for r in records} == exact_paths, 'Generation ledger inventory differs')
        records = {r['finalPath']: r for r in records}
        metadata = {}
        for category in CATEGORIES:
            rows = read(ROOT/category/'_metadata.json')
            check(all(set(r) == FIELDS for r in rows), 'Metadata schema differs: ' + category)
            requested = {p for p in EXPECTED if p.startswith(category+'/')}
            actual = {category+'/'+p.name for p in (ROOT/category).glob('*.png')}
            check(actual == requested, 'Final PNG inventory differs: ' + category)
            check(len(rows) == len(requested) and {r['path'] for r in rows} == {'Art/UI/'+p for p in requested}, 'Metadata inventory differs: '+category)
            metadata.update({r['path']: r for r in rows})
            unity = REPO/'Game/Assets/Game/UI/Resources/ui'/category
            check({category+'/'+p.name for p in unity.glob('*.png')} == requested, 'Resources inventory differs: '+category)
            check(not list(unity.glob('*.meta')), 'V4 .meta files must be created by Unity, not this delivery: '+category)
        template = frame().convert('RGBA')
        safe_box = (SAFE_AREA[0],SAFE_AREA[1],SAFE_AREA[0]+SAFE_AREA[2],SAFE_AREA[1]+SAFE_AREA[3])
        for rel, size in EXPECTED.items():
            full = 'Art/UI/' + rel
            try:
                path = ROOT/rel
                with Image.open(path) as opened:
                    check(opened.format == 'PNG', 'Not PNG: '+rel)
                    check(opened.size == size, 'Wrong dimensions: '+rel)
                    check(opened.mode == 'RGBA', 'Expected real RGBA: '+rel)
                    im = opened.convert('RGBA')
                alpha = im.getchannel('A')
                corners = [alpha.getpixel(p) for p in [(0,0),(size[0]-1,0),(0,size[1]-1),(size[0]-1,size[1]-1)]]
                card = rel.startswith(('tasks/','evidence/'))
                if card:
                    check(alpha.getextrema() == (0,255) and corners == [0]*4, 'Bad card alpha: '+rel)
                    check(alpha.tobytes() == template.getchannel('A').tobytes(), 'Card silhouette differs from v3: '+rel)
                    check(im.crop(safe_box).tobytes() == template.crop(safe_box).tobytes(), 'Runtime text area is not blank: '+rel)
                    for box in ((0,0,512,62),(0,510,512,716),(0,62,51,510),(461,62,512,510)):
                        check(im.crop(box).tobytes() == template.crop(box).tobytes(), 'Frame or protected region differs: '+rel)
                else:
                    check(alpha.getextrema() == (255,255), 'Wide illustration not fully opaque: '+rel)
                entry = metadata[full]
                check(entry in manifest, 'Metadata missing from manifest: '+rel)
                check((entry['w'],entry['h']) == size and entry['category'] == rel.split('/')[0] and entry['id'] == path.stem, 'Metadata identity/size mismatch: '+rel)
                check(entry['nineSlice'] is None and entry['screenRect'] is None, 'Unexpected slice/aperture: '+rel)
                check(entry['hasText'] is (path.stem == 'task_server_code'), 'Wrong lettering flag: '+rel)
                check(bool(entry['description'].strip()), 'Empty metadata description: '+rel)
                rec = records[full]
                check(bool(rec['prompt']) and bool(rec['refs']) and bool(rec['review']), 'Prompt/reference/review absent: '+rel)
                check(rec['tool'] == 'image_gen.imagegen' and bool(rec['model']) and bool(rec['date']), 'Missing tool provenance: '+rel)
                check(0 <= rec['retries'] <= 2 and len(rec.get('attempts',[])) == rec['retries'], 'Retry bound/history mismatch: '+rel)
                check(rec['finalSha256'] == digest(path), 'Final differs from finishing ledger: '+rel)
                check(rec['postProcessing']['canvas'] == list(size) and rec['postProcessing']['sceneContentAltered'] is False, 'Finishing provenance differs: '+rel)
                source = local(rec['source'])
                check(ROOT/'_tools/v4_sources' in source.parents, 'Source outside designated archive: '+rel)
                check(digest(source) == rec['sourceSha256'], 'Source hash differs: '+rel)
                with Image.open(source) as src:
                    src.load()
                    ext = src.convert('RGBA').getchannel('A').getextrema()
                    check(ext == ((0,255) if card else (255,255)), 'Raw transparency invalid: '+rel)
                for ref, expected_hash in rec['referenceSha256'].items():
                    archived = rec.get('referenceArchive',{}).get(ref,ref)
                    check(digest(local(archived)) == expected_hash, 'Reference archive differs: '+ref)
                for ref, expected_hash in rec.get('retryReferenceSha256',{}).items():
                    archived = rec.get('retryReferenceArchive',{}).get(ref,ref)
                    check(digest(local(archived)) == expected_hash, 'Retry reference archive differs: '+ref)
                for attempt in rec.get('attempts',[]):
                    check(bool(attempt['prompt']) and bool(attempt['refs']) and bool(attempt['reason']), 'Incomplete retry history: '+rel)
                    check(digest(local(attempt['source'])) == attempt['sourceSha256'], 'Rejected source differs: '+rel)
                target = local(rec['resourcesPath'])
                expected_target = REPO/'Game/Assets/Game/UI/Resources/ui'/rel
                check(target == expected_target and target.is_file() and digest(target) == digest(path), 'Unity copy differs: '+rel)
                check(review['finalSha256'].get(full) == digest(path), 'Final changed since visual review: '+rel)
                check(path.stat().st_size < (400000 if card else 1500000), 'Exceeded established kit size budget: '+rel)
                assets.append(dict(path=full,w=size[0],h=size[1],bytes=path.stat().st_size,
                    alphaExtrema=list(alpha.getextrema()),cornerAlpha=corners,sha256=digest(path),
                    safeAreaBlank=True if card else None,resourcesCopyMatches=True))
            except Exception as exc:
                errors.append(rel+': '+str(exc))
        check(set(review['finalSha256']) == exact_paths, 'Visual review must cover all 23 finals')
        check(review['allFinalsVisuallyReviewed'] and review['allSourcesVisuallyReviewed'], 'Incomplete visual review')
        check(review['textReview']['onlyLetteringAsset'] == 'task_server_code', 'Lettering review differs')
        check(review['textReview']['digits'] == ['4','1','7','2'], 'Digit review differs')
        for sheet_rel, expected_hash in review['sheetSha256'].items():
            check(digest(local(sheet_rel)) == expected_hash, 'Review sheet changed: '+sheet_rel)
    except Exception as exc:
        errors.append('Audit inputs: '+str(exc))
    report = dict(generatedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        passed=not errors,expectedV4Count=23,presentV4Count=len(assets),manifestAssetCount=len(manifest) if 'manifest' in locals() else 0,
        countsByCategory=dict(collections.Counter(p['path'].split('/')[2] for p in assets)),
        checkCount=check_count,priorFilesVerified=dict(verified) if 'verified' in locals() else {},
        retries=sum(r['retries'] for r in records.values()) if isinstance(locals().get('records'),dict) else None,
        errorCount=len(errors),errors=errors,warnings=[],assets=assets,
        unityIntegrationTested=False,limitations=['Unity editor import and in-game camera/compression checks require integration.',
            'Text absence and scene/identity semantics are visually reviewed, not OCR assertions.',
            'Historical ignored v1-v3 raw archives are unavailable; all 308 prior final assets and prior metadata are hash-checked.'])
    (ROOT/'v4_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('passed','expectedV4Count','presentV4Count','manifestAssetCount','countsByCategory','checkCount','priorFilesVerified','retries','errorCount','errors','warnings')},indent=2))
    return int(bool(errors))


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--strict',action='store_true',help='Compatibility flag; all validation failures always return exit code 1.')
    parser.parse_args()
    raise SystemExit(main())
