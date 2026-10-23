# Final regression round on the release candidate build.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner = Join-Path $root "Tools\run_local4.ps1"
$out = Join-Path $root "Builds\runs\final"
New-Item -ItemType Directory -Force $out | Out-Null
$summary = Join-Path $out "summary.txt"
"Final regression " + (Get-Date) | Set-Content $summary
"== host_kill ==" | Add-Content $summary
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "host_loss_test.ps1") -LogDir (Join-Path $out "host_kill") -Port 7801 2>&1 | Add-Content $summary
"== inputsim ==" | Add-Content $summary
& powershell -ExecutionPolicy Bypass -File $runner -LogDir (Join-Path $out "inputsim") -DurationScale 0.5 -Matches 1 -Seed 41 -Port 7802 -HostExtra "-inputsim -quitafterseconds 300" 2>&1 | Add-Content $summary
"== two_matches ==" | Add-Content $summary
& powershell -ExecutionPolicy Bypass -File $runner -Bots -LogDir (Join-Path $out "two_matches") -DurationScale 0.5 -Matches 2 -Seed 42 -Port 7803 2>&1 | Add-Content $summary
"DONE " + (Get-Date) | Add-Content $summary
