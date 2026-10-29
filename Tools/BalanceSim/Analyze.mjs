// Optional offline analysis; simulator itself has no Node dependency.
import fs from 'node:fs';
const report = JSON.parse(fs.readFileSync(process.argv[2] ?? 'Tools/BalanceSim/results/baseline.json', 'utf8'));
for (const group of report.Groups) {
    const points = group.Matches.map(m => m.Result.QuotaPoints - (m.Result.ManagerProfileId === 'paragoz' ? 2 : 0)).sort((a,b) => a-b);
    console.log(group.Label, 'points minus default profile delta quantiles:', JSON.stringify(Object.fromEntries([.1,.25,.3,.35].map(q => [q,points[Math.floor(q*points.length)]]))));
    const outcomes = group.Matches.flatMap(m => m.Result.Players).filter(p => p.Role === 'Employee');
    console.log(Object.entries(Object.groupBy(outcomes,p => p.ObjectiveCardId)).sort().map(([id,ps]) => `${id}: ${(100*ps.filter(p=>p.Won).length/ps.length).toFixed(1)}% (N=${ps.length})`).join('\n'));
    const clean = outcomes.filter(p => p.ObjectiveCardId === 'CLEAN_HANDS');
    console.log('CleanHands final win if task count threshold changed:', Object.fromEntries([8,9,10,11,12].map(t=>[t,(100*clean.filter(p=>p.Won && p.TasksCompleted>=t).length/clean.length).toFixed(1)])));
    const penaltyCounts = {};
    for (const match of group.Matches) for (const kind of match.UnderPressureFinalPenalties ?? []) penaltyCounts[kind]=(penaltyCounts[kind]??0)+1;
    if (Object.keys(penaltyCounts).length) console.log('UnderPressure final penalty frequencies:', penaltyCounts);
}
