// Optional report assembly after Run-Experiments.ps1; no simulation or policy changes.
import fs from 'node:fs';
const dir = 'Tools/BalanceSim/results/';
const read = name => JSON.parse(fs.readFileSync(dir + name + '.json', 'utf8'));
const baseline = read('baseline');
const ns = [3,4,5,6];
const quotaNames = ['quota_3','quota_final_4','quota_5','quota_final_6'];
const quotas = quotaNames.map(read);
const balances = ns.map(n=>read('balance_'+n));
const profiles = read('balance_profiles');
const weeks = [4,6].map(n=>read('balance_week_'+n));
const rate = (a,f) => (100*a.filter(f).length/a.length).toFixed(1)+'%';
const mean = (a,f) => (a.reduce((s,x)=>s+f(x),0)/a.length).toFixed(2);
const change = (before,after,f) => rate(before,f)+' → '+rate(after,f);
const cards = baseline.Groups[0].Matches.flatMap(m=>m.Result.Players).filter(p=>p.Role==='Employee').map(p=>p.ObjectiveCardId);
const allCards = [...new Set(cards)].sort();
const lines = [
'## Recommendations', '',
'**No configuration satisfies all four requested bands.** Every victory requires company success, so a 75% company-success ceiling imposes a 75% ceiling on matches with winners. The following are two measured alternatives, not a claim that the conflicting targets have been met. All reruns use the same baseline seeds and policy; values were passed with `--set`, without changing `CreateDefault`.', '',
'For a quota-difficulty target, use the following **pre-headcount** `QuotaTarget` values. The old headcount multipliers cannot produce the desired success band with one shared base value: this workload generates about 41 points with three players and 54 with six. These values are headcount-specific presets; the simulator applies the ordinary `ApplyHeadcount` afterward. They achieve the quota band but fail the winner-coverage goal, and Informant wins fall below 30% at 4–6 players.', '',
'| Players | `QuotaTarget` old → override | Effective base after headcount | Quota success before → after | Informant before → after | ≥1 winner before → after | N, engine seeds |',
'|---|---|---:|---|---|---|---|'
];
for(let i=0;i<4;i++) {
    const b=baseline.Groups[i],a=quotas[i].Groups[0],q=[54,44,43,39][i],effective=[39,44,51,53][i];
    lines.push(`| ${ns[i]} | 22 → ${q} | ${effective} (+2 for Paragoz) | ${change(b.Matches,a.Matches,m=>m.Result.CompanySucceeded)} | ${change(b.Matches,a.Matches,m=>m.Result.InformantWon)} | ${change(b.Matches,a.Matches,m=>m.Result.Players.some(p=>p.Won))} | 1000, ${a.Seed}–${a.Seed+999} |`);
}
lines.push('', 'For winner coverage, the stronger config-only candidate keeps `QuotaTarget=22` and uses this pack:', '',
'| Key | Default | Override | Reason / cost |', '|---|---:|---|---|',
'| `Objectives.SCAPEGOAT.Count` | 2 | 1 | One discovered framing success is achievable under this policy. |',
'| `Objectives.CHAOS_COORDINATOR.Count` | 2 | 1 | One discovered planted desk; two distinct discovered desks was too rare. |',
'| `Objectives.CLEAN_HANDS.Count` | 4 | 3p: 12; 4p: 11; 5p/6p: 10 | Raises the task burden of the strongest card; task-type requirement stays 2. |',
'| `Risk.NotAtDesk` | 1 | 0 | Removes absence from observed risk and Under Pressure’s positive-penalty gate. |',
'| `Risk.MovingDuringInspection` | 1.5 | 0 | Removes movement penalties, including for legitimate travel. |',
'| `Risk.PersonalCall` | 1 | 0 | Removes personal-call penalties. |', '',
'The three zero-risk changes are a substantial design tradeoff: absence, movement, and personal calls no longer increase firing risk. Other evidence, being caught, suspicious items, mess, and incorrect desk presence still matter. The pack improves the measured outcomes below, but should be playtested before adoption. It intentionally retains easy quota to protect winner coverage. Lowering only `Objectives.UNDER_PRESSURE.Count` cannot address that card’s hardcoded “no positive final-inspection observation” condition; this pack leaves its count at 2.', '',
'| Players | N | Quota before → after | Informant before → after | ≥1 winner before → after | Fired top / other / zero after |',
'|---|---:|---|---|---|---|');
for(let i=0;i<4;i++) {
    const b=baseline.Groups[i].Matches,a=balances[i].Groups[0].Matches;
    lines.push(`| ${ns[i]} | ${a.length} | ${change(b,a,m=>m.Result.CompanySucceeded)} | ${change(b,a,m=>m.Result.InformantWon)} | ${change(b,a,m=>m.Result.Players.some(p=>p.Won))} | ${['top tier','other','zero'].map(c=>rate(a,m=>m.FiredCategory===c)).join(' / ')} |`);
}
lines.push('', 'Card-level paired rerun, final win % before → after (card-holder N; identical assignments in each pair):', '',
'| Card | 3 players | 4 players | 5 players | 6 players |', '|---|---:|---:|---:|---:|');
for(const card of allCards) {
    const cells=ns.map((n,i)=>{
        const owners = g=>g.Matches.flatMap(m=>m.Result.Players).filter(p=>p.Role==='Employee' && p.ObjectiveCardId===card);
        const b=owners(baseline.Groups[i]),a=owners(balances[i].Groups[0]);
        return `${change(b,a,p=>p.Won)} (${a.length})`;
    });
    lines.push(`| ${card} | ${cells.join(' | ')} |`);
}
lines.push('', 'The pack puts most cards in or near 20–60%, but does **not** meet “every card”: six-player Protection Deal is 72.8%, Unnoticed 63.5%, Desk Guardian 62.7%, and Quiet Star 17.6%; five-player Quiet Star is 17.1%. These predicates have no configurable count threshold. Four-player Desk Guardian is 61.1% and three-player Fixer is 19.6%, both close to the boundary relative to sampling uncertainty. These remaining outliers need further measured tuning or a predicate/policy review; this experiment did not establish a config-only solution for every card. Achieving both quota and winner bands requires changing the victory gate or the targets. Engine changes remain outside this task.', '',
'The affected profile and week experiments were also rerun with this pack (4p Clean Hands count 11, 6p count 10):', '',
'| Profile, 4p | N | Quota before → after | Informant before → after | ≥1 winner before → after | Fired mean performance rank before → after |',
'|---|---:|---|---|---|---|');
for(let i=0;i<4;i++) {
    const b=baseline.Groups[4+i],a=profiles.Groups[i];
    lines.push(`| ${a.Label} | 500 | ${change(b.Matches,a.Matches,m=>m.Result.CompanySucceeded)} | ${change(b.Matches,a.Matches,m=>m.Result.InformantWon)} | ${change(b.Matches,a.Matches,m=>m.Result.Players.some(p=>p.Won))} | ${mean(b.Matches,m=>m.FiredPerformanceRank)} → ${mean(a.Matches,m=>m.FiredPerformanceRank)} |`);
}
lines.push('', '| Week players | Weeks N | Mean spread before → after | Friday leader led Wednesday (any) before → after | Ayın Elemanı tie before → after |', '|---|---:|---|---|---|');
for(let i=0;i<2;i++) {
    const b=baseline.WeekGroups[i].Weeks,a=weeks[i].WeekGroups[0].Weeks;
    lines.push(`| ${[4,6][i]} | 300 | ${mean(b,w=>w.CareerSpread)} → ${mean(a,w=>w.CareerSpread)} | ${change(b,a,w=>w.FridayLeaderAlreadyLeading)} | ${change(b,a,w=>w.EmployeeTie)} |`);
}
lines.push('', 'Quick-play points, incident counts/discovery, calls, tips, and inspection coverage stayed unchanged in the winner-coverage rerun. Meeting changes altered footage actor visibility (3p: 2.9% → 2.8%; 4p: 3.7% → 4.0%; 5p: 3.8% → 4.0%; 6p: 4.6% → 4.5%) and some FakeEmail consequence misconduct, which depends on the fired player. Full per-run tables remain in the generated Markdown files.', '',
'Example quota-only rerun at four players:', '', '```powershell',
'dotnet run -c Release --project Tools/BalanceSim -- --experiment quick --players 4 --matches 1000 --seed 220000 --out quota_final_4 --set QuotaTarget=44', '```', '',
'Example winner-coverage rerun at four players:', '', '```powershell',
'dotnet run -c Release --project Tools/BalanceSim -- --experiment quick --players 4 --matches 1000 --seed 220000 --out balance_4 --set Objectives.SCAPEGOAT.Count=1 --set Objectives.CHAOS_COORDINATOR.Count=1 --set Objectives.CLEAN_HANDS.Count=11 --set Risk.NotAtDesk=0 --set Risk.MovingDuringInspection=0 --set Risk.PersonalCall=0', '```', '',
'Run [Run-Experiments.ps1](../Tools/BalanceSim/Run-Experiments.ps1) to reproduce the baseline plus both final alternatives (22,000 matches total); `-SkipBaseline` reuses an existing baseline. It passes every override on the command line. Generated JSON/Markdown are ignored by Git; source and this report are the review artifacts. Optional `node Tools/BalanceSim/Write-TuningReport.mjs` regenerates this recommendations section from the exports; Node is not required to build or run the simulator.', '',
'| Rerun | N | Seed range | Ordered result digest (SHA-256) |', '|---|---:|---|---|');
for(const [name,r] of [...quotaNames.map((n,i)=>[n,quotas[i]]),...ns.map((n,i)=>['balance_'+n,balances[i]]),['balance_profiles',profiles],...weeks.map((w,i)=>['balance_week_'+[4,6][i],w])]) {
    for(const g of r.Groups) lines.push(`| ${name}: ${g.Label} | ${g.Matches.length} matches | ${g.Seed}–${g.Seed+g.Matches.length-1} | \`${g.Digest}\` |`);
    for(const g of r.WeekGroups) lines.push(`| ${name} | ${g.Weeks.length} weeks | ${g.Seed}–${g.Seed+5*g.Weeks.length-1} | \`${g.Digest}\` |`);
}
const recommendedSeconds=[baseline,...quotas,...balances,profiles,...weeks].reduce((s,r)=>s+r.ElapsedSeconds,0);
const allFiles=fs.readdirSync(dir).filter(x=>x.endsWith('.json'));
// Read only the short metadata prefix before Groups to avoid reparsing large result arrays.
let allSeconds=0;
for(const f of allFiles) {
    const fd=fs.openSync(dir+f,'r'),buf=Buffer.alloc(8192); const count=fs.readSync(fd,buf,0,buf.length,0);fs.closeSync(fd);
    const match=buf.subarray(0,count).toString('utf8').match(/"ElapsedSeconds":([\d.eE+-]+)/);
    if(match) allSeconds+=Number(match[1]);
}
lines.push('', `Measured simulation time for the baseline and final alternatives was **${recommendedSeconds.toFixed(1)} seconds (${(recommendedSeconds/60).toFixed(2)} minutes)** summed across processes on this machine. Including the 300-match pilot, two superseded quota trials, and the two 32-match determinism smoke runs, all recorded simulation runs totaled **${allSeconds.toFixed(1)} seconds (${(allSeconds/60).toFixed(2)} minutes)**. Compilation and offline export validation are excluded; two rerun batches briefly overlapped, so summed process time exceeds their wall-clock time. The 10-minute budget is hardware dependent.`, '',
'Validation: Release build passed with zero warnings/errors. [Verify-Results.ps1](../Tools/BalanceSim/Verify-Results.ps1) checked all 6,000 baseline quick/profile records and all 600 weeks for victory gates, exactly one fired player, misconduct totals, ranks, denominators, career spreads/ties, seed ordering, and digests. The same 32 seeds with one and eight workers produced identical complete exported metrics. CLI validation also rejects duration overrides.', '',
'```powershell', './Tools/BalanceSim/Verify-Results.ps1 -Path Tools/BalanceSim/results/smoke.json -Compare Tools/BalanceSim/results/smoke_serial.json', '```', '');
const path='docs/BALANCE_SIM.md';
const existing=fs.readFileSync(path,'utf8').split('## Recommendations')[0].trimEnd();
fs.writeFileSync(path,existing+'\n\n'+lines.join('\n'));
console.log(`Updated ${path}; final suite ${recommendedSeconds.toFixed(1)}s; all runs ${allSeconds.toFixed(1)}s`);
