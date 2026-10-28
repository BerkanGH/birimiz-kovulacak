<#
.SYNOPSIS
  Runs a local bot match (Tools/run_local4.ps1) and samples the memory of every game process while it runs.
  Writes <LogDir>/memory.csv and prints peak/average working set and private bytes per process.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\scripts\measure_memory.ps1 -Players 6 -DurationScale 0.4
  powershell -ExecutionPolicy Bypass -File Tools\scripts\measure_memory.ps1 -Exe Builds\baseline_rc\BirimizKovulacak.exe -Players 4
#>
param(
    [int]$Players = 4,
    [double]$DurationScale = 0.4,
    [string]$Exe = "",
    [string]$LogDir = "",
    [int]$IntervalMs = 2000,
    [string]$Extra = ""
)
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $LogDir) { $LogDir = Join-Path $root ("Builds\runs\mem_" + (Get-Date -Format "yyyyMMdd_HHmmss")) }
New-Item -ItemType Directory -Force $LogDir | Out-Null
$runner = Join-Path $root "Tools\run_local4.ps1"
$runArgs = @("-ExecutionPolicy", "Bypass", "-File", $runner, "-Bots", "-DurationScale", $DurationScale, "-Matches", 1, "-LogDir", $LogDir)
if ($Exe) { $runArgs += @("-Exe", (Resolve-Path $Exe).Path) }
# The v1 runner has no -Players; only pass it when it differs from the v1 default.
if ($Players -ne 4) { $runArgs += @("-Players", $Players) }
if ($Extra) { $runArgs += $Extra.Split(" ") }
$job = Start-Process powershell -ArgumentList $runArgs -PassThru -WindowStyle Hidden
$csv = Join-Path $LogDir "memory.csv"
"time,pid,working_set_mb,private_mb" | Set-Content $csv
$stats = @{}
Start-Sleep -Seconds 5
while (-not $job.HasExited) {
    $t = (Get-Date).ToString("HH:mm:ss")
    foreach ($p in Get-Process -Name "BirimizKovulacak" -ErrorAction SilentlyContinue) {
        try {
            $ws = [math]::Round($p.WorkingSet64 / 1MB, 1); $pr = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
            "$t,$($p.Id),$ws,$pr" | Add-Content $csv
            if (-not $stats.ContainsKey($p.Id)) { $stats[$p.Id] = @{ Peak = 0.0; PeakPrivate = 0.0; Sum = 0.0; N = 0 } }
            $s = $stats[$p.Id]; $s.Peak = [math]::Max($s.Peak, $ws); $s.PeakPrivate = [math]::Max($s.PeakPrivate, $pr); $s.Sum += $ws; $s.N++
        } catch { }
    }
    Start-Sleep -Milliseconds $IntervalMs
}
$lines = @("pid,peak_working_set_mb,avg_working_set_mb,peak_private_mb,samples")
foreach ($k in $stats.Keys) { $s = $stats[$k]; $lines += "$k,$($s.Peak),$([math]::Round($s.Sum / [math]::Max(1, $s.N), 1)),$($s.PeakPrivate),$($s.N)" }
$lines | Set-Content (Join-Path $LogDir "memory_summary.csv")
$lines | ForEach-Object { Write-Host $_ }
Write-Host "Logs: $LogDir"
