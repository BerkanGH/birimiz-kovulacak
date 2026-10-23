# Second round: scripted rubber-duck story, human input path, host loss.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner = Join-Path $root "Tools\run_local4.ps1"
$out = Join-Path $root "Builds\runs\suite2"
New-Item -ItemType Directory -Force $out | Out-Null
$summary = Join-Path $out "summary.txt"
"Verification suite 2 " + (Get-Date) | Set-Content $summary
function Run($name, [hashtable]$p, [switch]$NoBotHost) {
    $dir = Join-Path $out $name
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    "== $name ==" | Add-Content $summary
    if ($NoBotHost) { $res = & powershell -ExecutionPolicy Bypass -File $runner -LogDir $dir @p 2>&1 }
    else { $res = & powershell -ExecutionPolicy Bypass -File $runner -Bots -LogDir $dir @p 2>&1 }
    $res | Add-Content $summary
}
Run "duck" @{ DurationScale = 0.6; Matches = 1; Seed = 23; Port = 7791; Scenario = "duck" }
Run "inputsim" @{ DurationScale = 0.5; Matches = 1; Seed = 31; Port = 7792; HostExtra = "-inputsim -quitafterseconds 300" } -NoBotHost
"== host_kill ==" | Add-Content $summary
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "host_loss_test.ps1") -LogDir (Join-Path $out "host_kill") -Port 7793 2>&1 | Add-Content $summary
"DONE " + (Get-Date) | Add-Content $summary
