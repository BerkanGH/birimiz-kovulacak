<#
.SYNOPSIS
  Packages the built player into Builds\BirimizKovulacak-Windows-Playtest.zip for sharing with playtesters.
  Excludes Unity's debug-symbol backup folder and adds the Turkish quick-start + hosting guide.
#>
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "Builds\BirimizKovulacak-Windows"
if (-not (Test-Path (Join-Path $build "BirimizKovulacak.exe"))) { throw "No build found. Run Tools\build.ps1 first." }
$stage = Join-Path $root "Builds\package\BirimizKovulacak"
if (Test-Path (Split-Path $stage)) { Remove-Item -Recurse -Force (Split-Path $stage) }
New-Item -ItemType Directory -Force $stage | Out-Null

Get-ChildItem $build | Where-Object { $_.Name -notlike "*_BackUpThisFolder_ButDontShipItWithYourGame" } |
    ForEach-Object { Copy-Item $_.FullName -Destination $stage -Recurse -Force }
Copy-Item (Join-Path $root "docs\HOSTING.md") (Join-Path $stage "KURULUM_VE_BAGLANTI.md")

$buildId = Get-Content (Join-Path $root "Game\Assets\Game\Resources\build_id.txt") -ErrorAction SilentlyContinue
$readme = @"
BİRİMİZ KOVULACAK — Kapalı Oyun Testi (v2: İş Haftası)
======================================================
Ofisi birlikte kurtar. Sonra suçu arkadaşına at.

1) BirimizKovulacak.exe dosyasını çalıştır (SmartScreen uyarısında: Ek bilgi -> Yine de çalıştır).
2) Ayarlar -> Mikrofon: mikrofonunu seç, "Kendini dinle" ile dene. Konuşmak için V'ye basılı tut.
   Sesli sohbet oyunun içinde: Discord KULLANMAYIN (gizli telefon aramaları ve kulak misafirliği oyunun parçası).
3) Biriniz "Lobi Kur" desin, mod seçsin (Hızlı Oyun / İş Haftası); diğerleri ev sahibinin IP adresiyle "Lobiye Katıl".
4) 3-6 kişi. Herkes "Hazırım" deyince ev sahibi "Maçı Başlat". Bir maç yaklaşık 9 dakika; İş Haftası 5 maç.

İnternetten oynamak, bağlantı sorunları ve kontroller: KURULUM_VE_BAGLANTI.md
Sürüm: $buildId
"@
[IO.File]::WriteAllText((Join-Path $stage "OKUBENI.txt"), $readme, (New-Object Text.UTF8Encoding($true)))

$zip = Join-Path $root "Builds\BirimizKovulacak-Windows-Playtest.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Package: $zip ($size MB)"
