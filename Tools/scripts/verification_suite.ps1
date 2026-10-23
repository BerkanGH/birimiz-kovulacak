# Sequential multi-process verification runs (each = 1 host + 3 clients on localhost, bot-driven).
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner = Join-Path $root "Tools\run_local4.ps1"
$out = Join-Path $root "Builds\runs\suite"
New-Item -ItemType Directory -Force $out | Out-Null
$summary = Join-Path $out "summary.txt"
"Verification suite " + (Get-Date) | Set-Content $summary
function Run($name, [hashtable]$p) {
    $dir = Join-Path $out $name
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    "== $name ==" | Add-Content $summary
    $res = & powershell -ExecutionPolicy Bypass -File $runner -Bots -LogDir $dir @p 2>&1
    $res | Add-Content $summary
}
Run "reconnect" @{ DurationScale = 0.5; Matches = 1; Seed = 21; Port = 7781; Client2Extra = "-simulatedrop 50 -dropdowntime 6" }
Run "grace_expiry" @{ DurationScale = 0.5; Matches = 1; Seed = 22; Port = 7782; Client3Extra = "-simulatedrop 50 -droprejoin 0"; ExtraAll = "-quitafterseconds 170" }
Run "duck" @{ DurationScale = 0.6; Matches = 1; Seed = 23; Port = 7783; Scenario = "duck" }
Run "full_rematch" @{ DurationScale = 1.0; Matches = 2; Seed = 24; Port = 7784 }
"DONE " + (Get-Date) | Add-Content $summary
