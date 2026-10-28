"""Keep script inputs in the kit rather than depending on the generator cache."""
import sys
sys.dont_write_bytecode=True
from kit import *
import shutil
for ledger in ['root_decals.json','root_hud_prompts.json']:
    file=ROOT/'_sources'/ledger
    rows=json.loads(file.read_text(encoding='utf-8'))
    for row in rows:
        src=Path(row['source'])
        dst=ROOT/'_sources/root_generated'/(row['id']+'.png')
        if src.resolve()!=dst.resolve():
            safe(dst);shutil.copyfile(src,dst)
            row['generatorCacheSource']=str(src)
            row['source']='Art/UI/_sources/root_generated/'+dst.name
    file.write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
print('Archived generated inputs for deterministic root compositing.')
