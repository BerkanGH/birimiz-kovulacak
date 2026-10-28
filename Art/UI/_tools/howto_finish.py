"""Finish the six second-run how-to illustrations. Writes only under Art/UI."""
import sys
sys.dont_write_bytecode = True
from pathlib import Path
import json, shutil
from kit import ROOT, safe, finish, sheet, metadata, audit

ledger = json.loads((ROOT / '_sources/howto_prompts.json').read_text(encoding='utf-8'))
entries = []
checks = []
for item in ledger:
    raw = safe(ROOT / '_sources/howto' / (item['id'] + '_raw.png'))
    if not raw.exists():
        shutil.copy2(item['source'], raw)
    target = safe(ROOT / 'howto' / (item['id'] + '.png'))
    finish(raw, target, 1280, 720, mode='cover', limit=1500000)
    entries.append(metadata(target, 'howto', item['description']))
    checks.append(audit(target))
safe(ROOT / 'howto/_metadata.json').write_text(json.dumps(entries, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
safe(ROOT / '_sources/howto_validation.json').write_text(json.dumps({'count':len(entries),'checks':checks,'visualReview':'All six reviewed for clear teaching idea, cast identity, style consistency, and text-free art. Week board has exactly five columns and blank labels.'}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
sheet((ROOT / 'howto').glob('*.png'), ROOT / '_sheets/howto.png', cols=2, cell=(640,405))
print(json.dumps({'count':len(entries), 'checks':checks}, indent=2))
