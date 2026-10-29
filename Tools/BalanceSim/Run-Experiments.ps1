param([int] $Threads = 8, [switch] $SkipBaseline)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = $PSScriptRoot

function Invoke-Sim([string[]] $Arguments) {
    & dotnet run -c Release --no-build --project $project -- @Arguments --threads $Threads
    if ($LASTEXITCODE -ne 0) { throw "BalanceSim failed (exit $LASTEXITCODE)" }
}

& dotnet build -c Release $project
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (-not $SkipBaseline) { Invoke-Sim @('--experiment', 'all', '--out', 'baseline') }

# Quota-difficulty alternative. These are base values BEFORE ApplyHeadcount.
foreach ($pair in @(@(3,54),@(4,44),@(5,43),@(6,39))) {
    $name = if ($pair[0] -eq 4 -or $pair[0] -eq 6) { 'quota_final_' } else { 'quota_' }
    Invoke-Sim @('--experiment','quick','--players',"$($pair[0])",'--out',($name + $pair[0]),'--set',"QuotaTarget=$($pair[1])")
}

# Winner-coverage alternative: retain default quota, change objective thresholds
# and remove three observation penalties. Full rationale/tradeoffs are in the doc.
$shared = @('--set','Objectives.SCAPEGOAT.Count=1', '--set','Objectives.CHAOS_COORDINATOR.Count=1',
    '--set','Risk.MovingDuringInspection=0', '--set','Risk.PersonalCall=0', '--set','Risk.NotAtDesk=0')
foreach ($pair in @(@(3,12),@(4,11),@(5,10),@(6,10))) {
    Invoke-Sim (@('--experiment','quick','--players',"$($pair[0])",'--out',('balance_' + $pair[0]),
        '--set',"Objectives.CLEAN_HANDS.Count=$($pair[1])") + $shared)
}
Invoke-Sim (@('--experiment','profiles','--out','balance_profiles','--set','Objectives.CLEAN_HANDS.Count=11') + $shared)
foreach ($pair in @(@(4,11),@(6,10))) {
    Invoke-Sim (@('--experiment','week','--players',"$($pair[0])",'--out',('balance_week_' + $pair[0]),
        '--set',"Objectives.CLEAN_HANDS.Count=$($pair[1])") + $shared)
}
