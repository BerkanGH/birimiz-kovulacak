# Host-loss test: 1 host + 3 bot clients; the host process is force-killed mid-match (no goodbye message).
# Expected: every client detects the loss, tries to reconnect for the grace period, then ends the match clearly.
param([string]$LogDir = "", [int]$KillAfter = 55, [int]$Port = 7790)
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $root "Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe"
if (-not $LogDir) { $LogDir = Join-Path $root "Builds\runs\suite\host_kill" }
if (Test-Path $LogDir) { Remove-Item -Recurse -Force $LogDir }
New-Item -ItemType Directory -Force $LogDir | Out-Null
$common = @("-screen-fullscreen", "0", "-screen-width", "800", "-screen-height", "450", "-durationscale", "0.5", "-logdir", $LogDir, "-mute", "-bot", "-port", $Port.ToString(), "-quitafterseconds", "150")
$hostProc = Start-Process -FilePath $exe -ArgumentList (@("-autohost", "-autostart", "-name", "Mavi", "-logFile", (Join-Path $LogDir "unity_host.log")) + $common) -PassThru
Start-Sleep -Seconds 3
$clients = @()
$names = @("Sari", "Pembe", "Yesil")
for ($i = 0; $i -lt 3; $i++) {
    $clients += Start-Process -FilePath $exe -ArgumentList (@("-autojoin", "127.0.0.1", "-name", $names[$i], "-logFile", (Join-Path $LogDir ("unity_client$i.log"))) + $common) -PassThru
    Start-Sleep -Milliseconds 800
}
Start-Sleep -Seconds $KillAfter
Write-Host "Killing host process $($hostProc.Id) at $(Get-Date -Format HH:mm:ss)"
Stop-Process -Id $hostProc.Id -Force
$clients | Wait-Process
foreach ($f in Get-ChildItem $LogDir -Filter "*.log" | Where-Object { $_.Name -notlike "unity_*" -and $_.Name -notlike "Mavi_*" }) {
    Write-Host "== $($f.Name)"
    Select-String -Path $f.FullName -Pattern "phase |disconnected|conn |reconnect" | Select-Object -Last 6 | ForEach-Object { $_.Line }
}
