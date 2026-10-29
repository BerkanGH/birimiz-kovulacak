param(
    [Parameter(Mandatory = $true)][string] $Path,
    [string] $Compare
)

# Checks exported outcomes, and optionally exact schedule-independent replay.
# Example: ./Tools/BalanceSim/Verify-Results.ps1 -Path .../smoke.json -Compare .../smoke_serial.json
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Result([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-ResultDigest([string[]] $Hashes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes(($Hashes -join "`n"))
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    }
    finally { $sha.Dispose() }
}

function Read-VerifiedReport([string] $File) {
    $report = Get-Content -LiteralPath $File -Raw | ConvertFrom-Json
    $matchCount = 0
    $weekCount = 0
    foreach ($group in $report.Groups) {
        $index = 0
        foreach ($match in $group.Matches) {
            $result = $match.Result
            $context = "$($group.Label), seed $($result.MatchSeed)"
            Assert-Result ($result.MatchSeed -eq $group.Seed + $index) "${context}: seed sequence"
            Assert-Result ($result.Players.Count -eq $group.Players) "${context}: player count"
            $fired = @($result.Players | Where-Object { $_.Fired })
            Assert-Result ($fired.Count -eq 1 -and $fired[0].Player -eq $result.FiredPlayer) "${context}: fired player"
            Assert-Result ($result.CompanySucceeded -eq ($result.QuotaPoints -ge $result.QuotaTarget)) "${context}: quota result"
            foreach ($player in $result.Players) {
                $expectedWin = $result.CompanySucceeded -and -not $player.Fired -and $player.ObjectiveCompleted
                Assert-Result ($player.Won -eq $expectedWin) "${context}: victory gates for player $($player.Player)"
                $incidents = @($result.Incidents | Where-Object { $_.Actor -eq $player.Player })
                $misconduct = 0
                if ($incidents.Count -gt 0) { $misconduct = ($incidents | Measure-Object -Property Misconduct -Sum).Sum }
                Assert-Result ([Math]::Abs($player.TrueMisconduct - $misconduct) -lt 0.001) "${context}: incident misconduct total"
                # The game's objective sabotage counter excludes evidence tampering.
                $sabotage = @($incidents | Where-Object { $_.Type -ne 'EvidenceTampering' }).Count
                Assert-Result ($sabotage -eq $player.SabotageCount) "${context}: sabotage count"
            }
            $informant = @($result.Players | Where-Object { $_.Role -eq 'Informant' })
            Assert-Result ($informant.Count -eq 1 -and $informant[0].Won -eq $result.InformantWon) "${context}: Informant outcome"
            $category = if ($fired[0].TrueMisconduct -le 0) { 'zero' }
                elseif ($result.TopMisconductTier -contains $fired[0].Player) { 'top tier' } else { 'other' }
            Assert-Result ($match.FiredCategory -eq $category) "${context}: misconduct category"
            $rank = 1 + @($result.Players | Where-Object { $_.FinalPerformance -gt $fired[0].FinalPerformance }).Count
            Assert-Result ($match.FiredPerformanceRank -eq $rank) "${context}: performance rank"
            foreach ($field in @('RaidInspected', 'FinalInspected', 'SurpriseInspected')) {
                Assert-Result ($match.$field -ge 0 -and $match.$field -le $group.Players) "${context}: $field bounds"
            }
            Assert-Result ($match.FootageActorHits -le $match.FootageWithIncident -and $match.FootageWithIncident -le $match.FootageRequests) "${context}: footage denominators"
            Assert-Result ($match.TipHits -le $match.TipInspections) "${context}: tip denominator"
            Assert-Result ($match.RejectedRequests -le $match.ProcessedRequests) "${context}: rejection denominator"
            Assert-Result ($match.Calls -eq @($result.Calls | Where-Object { -not $_.Tip }).Count) "${context}: call count"
            Assert-Result ($match.Tips -eq @($result.Calls | Where-Object { $_.Tip }).Count) "${context}: tip count"
            $index++
            $matchCount++
        }
        $digest = Get-ResultDigest @($group.Matches | ForEach-Object { $_.Result.ResultHash })
        Assert-Result ($digest -ceq $group.Digest) "$($group.Label): digest"
    }
    foreach ($group in $report.WeekGroups) {
        $index = 0
        foreach ($week in $group.Weeks) {
            Assert-Result ($week.Seed -eq $group.Seed + 5 * $index) 'Week seed sequence'
            Assert-Result ($week.DailyHashes.Count -eq 5) 'Week must include five completed days'
            Assert-Result ($week.CareerTotals.Count -eq $group.Players) 'Week player count'
            $extremes = $week.CareerTotals | Measure-Object -Minimum -Maximum
            Assert-Result ($week.CareerSpread -eq $extremes.Maximum - $extremes.Minimum) 'Week career spread'
            $leaders = @($week.CareerTotals | Where-Object { $_ -eq $extremes.Maximum })
            Assert-Result ($week.EmployeeTie -eq ($leaders.Count -gt 1)) 'Employee of month tie'
            Assert-Result (-not $week.AllFridayLeadersAlreadyLeading -or $week.FridayLeaderAlreadyLeading) 'Friday leader implication'
            $index++
            $weekCount++
        }
        $digest = Get-ResultDigest @($group.Weeks | ForEach-Object { $_.DailyHashes })
        Assert-Result ($digest -ceq $group.Digest) "Week $($group.Players)p: digest"
    }
    Write-Host "Verified $matchCount match records and $weekCount five-day weeks: $File"
    return $report
}

$first = Read-VerifiedReport $Path
if ($Compare) {
    $second = Read-VerifiedReport $Compare
    Assert-Result ($first.Groups.Count -eq $second.Groups.Count -and $first.WeekGroups.Count -eq $second.WeekGroups.Count) 'Comparison experiment groups differ'
    for ($i = 0; $i -lt $first.Groups.Count; $i++) {
        $left = $first.Groups[$i]
        $right = $second.Groups[$i]
        Assert-Result ($left.Label -ceq $right.Label -and $left.Seed -eq $right.Seed) 'Comparison group identity differs'
        # Include every diagnostic and result, not just the core result hash.
        $a = ConvertTo-Json -InputObject $left.Matches -Depth 100 -Compress
        $b = ConvertTo-Json -InputObject $right.Matches -Depth 100 -Compress
        Assert-Result ($a -ceq $b) "Schedule-independent replay failed: $($left.Label)"
    }
    for ($i = 0; $i -lt $first.WeekGroups.Count; $i++) {
        $left = $first.WeekGroups[$i]
        $right = $second.WeekGroups[$i]
        Assert-Result ($left.Players -eq $right.Players -and $left.Seed -eq $right.Seed) 'Comparison week identity differs'
        $a = ConvertTo-Json -InputObject $left.Weeks -Depth 100 -Compress
        $b = ConvertTo-Json -InputObject $right.Weeks -Depth 100 -Compress
        Assert-Result ($a -ceq $b) "Schedule-independent replay failed: Week $($left.Players)p"
    }
    Write-Host 'All exported outcomes and metrics match exactly across the two runs.'
}

