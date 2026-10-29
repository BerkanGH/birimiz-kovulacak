<#
.SYNOPSIS
  Launches one host and N-1 clients on this PC (N = -Players, 3..6, default 4) for local multiplayer testing.
  With -Bots every process is driven by the automated test player; without it you control the host window.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\run_local4.ps1 -Bots -DurationScale 0.5 -Matches 1
#>
param(
    [switch]$Bots,
    [double]$DurationScale = 1.0,
    [int]$Matches = 1,
    [int]$Seed = 0,
    [string]$Exe = "",
    [string]$LogDir = "",
    [int]$Port = 7777,
    [string]$Client2Extra = "",
    [string]$Client3Extra = "",
    [string]$ExtraAll = "",
    [string]$HostExtra = "",
    [int]$Players = 4,
    [switch]$Week,
    [string]$Manager = "",
    [string]$VoiceTest = ""
)
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root "Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe" }
if (-not $LogDir) { $LogDir = Join-Path $root ("Builds\runs\" + (Get-Date -Format "yyyyMMdd_HHmmss")) }
New-Item -ItemType Directory -Force $LogDir | Out-Null
$inv = [Globalization.CultureInfo]::InvariantCulture
$common = @("-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540", "-durationscale", $DurationScale.ToString($inv),
            "-logdir", $LogDir, "-screenshots", "-mute", "-port", $Port.ToString(), "-rematches", ($Matches - 1).ToString(), "-quitaftermatches", $Matches.ToString(), "-expectplayers", $Players.ToString())
if ($Seed -ne 0) { $common += @("-seed", $Seed.ToString()) }
if ($ExtraAll) { $common += $ExtraAll.Split(" ") }
if ($VoiceTest) { $common += @("-voicetest", $VoiceTest) }
$names = @("Mavi", "Sari", "Pembe", "Yesil", "Turuncu", "Mor")
$procs = @()
$hostArgs = @("-autohost", "-autostart", "-name", $names[0], "-logFile", (Join-Path $LogDir "unity_host.log")) + $common
if ($Bots) { $hostArgs += "-bot" }
if ($Week) { $hostArgs += "-week" }
if ($Manager) { $hostArgs += @("-manager", $Manager) }
if ($HostExtra) { $hostArgs += $HostExtra.Split(" ") }
$procs += Start-Process -FilePath $Exe -ArgumentList $hostArgs -PassThru
Start-Sleep -Seconds 3
for ($i = 1; $i -lt $Players; $i++) {
    $a = @("-autojoin", "127.0.0.1", "-bot", "-name", $names[$i], "-logFile", (Join-Path $LogDir ("unity_client$i.log"))) + $common
    if ($i -eq 2 -and $Client2Extra) { $a += $Client2Extra.Split(" ") }
    if ($i -eq 3 -and $Client3Extra) { $a += $Client3Extra.Split(" ") }
    $procs += Start-Process -FilePath $Exe -ArgumentList $a -PassThru
    Start-Sleep -Milliseconds 800
}
Write-Host "Launched $Players processes; logs in $LogDir"
$procs | Wait-Process
python (Join-Path $PSScriptRoot "scripts\verify_logs.py") $LogDir
