<#
.SYNOPSIS
  Regenerates all Unity content (materials, prefabs, Office scene) and builds the Windows x64 player.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\build.ps1            # assets + scene + player
  powershell -ExecutionPolicy Bypass -File Tools\build.ps1 -NoPlayer  # assets + scene only
#>
param(
    [switch]$NoPlayer,
    [switch]$Art,
    [string]$Unity = $(if ($env:UNITY_EXE) { $env:UNITY_EXE } else { "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe" }),
    [string]$Blender = $(if ($env:BLENDER_EXE) { $env:BLENDER_EXE } else { "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" })
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Game"
$logs = Join-Path $root "Builds\logs"
New-Item -ItemType Directory -Force $logs | Out-Null

if ($Art) {
    foreach ($s in "build_characters.py", "build_props.py", "build_architecture.py", "build_rooms.py") {
        Write-Host "Blender: $s"
        & $Blender -b --factory-startup -P (Join-Path $root "Art\Scripts\$s") | Select-String "DONE|Error|Traceback"
    }
}

# TMP essential resources must exist before the builder runs (batchmode package import is asynchronous).
if (-not (Test-Path (Join-Path $project "Assets\TextMesh Pro\Resources\TMP Settings.asset"))) {
    $pkg = Get-ChildItem -Recurse -Filter "TMP Essential Resources.unitypackage" (Join-Path $project "Library\PackageCache") -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pkg) { python -I (Join-Path $PSScriptRoot "scripts\extract_unitypackage.py") $pkg.FullName $project }
    else { Write-Host "Package cache not ready yet; the first Unity run will populate it." }
}

$uargs = @("-batchmode", "-nographics", "-projectPath", $project, "-executeMethod", "BirimizKovulacak.EditorTools.ProjectBuilder.BuildAllCli", "-logFile", (Join-Path $logs "build.log"))
if ($NoPlayer) { $uargs += "-noPlayer" }
# Windows PowerShell does not quote array arguments: paths with spaces must be quoted here.
$uargLine = ($uargs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join " "
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $p = Start-Process -FilePath $Unity -ArgumentList $uargLine -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 3) { break }
    Write-Host "Builder requested a rerun (first-time imports)."
}
Select-String -Path (Join-Path $logs "build.log") -Pattern "\[Builder\]|error CS" | ForEach-Object { $_.Line }
if ($p.ExitCode -ne 0) { throw "Build failed (exit $($p.ExitCode)). See Builds\logs\build.log" }
Write-Host "Build OK -> Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe"
