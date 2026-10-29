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
BİRİMİZ KOVULACAK — Kapalı Oyun Testi (v3: Ofis Hattı)
======================================================
Herkes kendi odasında. Tek silahın masa telefonun. Cuma biri kovulacak.

1) BirimizKovulacak.exe dosyasını çalıştır (SmartScreen uyarısında: Ek bilgi -> Yine de çalıştır). Dili seç (Türkçe / English).
2) Ayarlar -> Mikrofon: mikrofonunu seç, "Kendini dinle" ile dene. Konuşmak için V'ye basılı tut. Kulaklık önerilir.
   Sesli sohbet oyunun içinde: Discord KULLANMAYIN. Gündüz sadece telefondaki kişi duyar; toplantıda herkes herkesi duyar.
   Görüşmeler kaydedilebilir: toplantıda bir görüşmeden 7 saniye çalınır (kayıtlar diske yazılmaz; lobide kapatılabilir).
3) Biriniz "Lobi Kur" desin (Kısa Hafta 3 gün ≈ 30 dk / Tam Hafta 5 gün ≈ 50 dk); diğerleri ev sahibinin IP adresiyle "Lobiye Katıl".
4) 3-6 kişi. Herkes "Hazırım" deyince ev sahibi "Haftayı Başlat". İlk gün oyun sizi adım adım tanıştırır.
   Ofiste: fareyle bak, monitöre (E) ya da telefona (Tab) tıkla, Y/N aramayı aç/reddet, Esc geri.

İnternetten oynamak, bağlantı sorunları ve kontroller: KURULUM_VE_BAGLANTI.md

English: everyone sits alone in their own office; the desk phone is the only channel; someone is fired on the
last day. Pick English on first launch. Host: Host -> Short/Full Week. Others: Join with the host's IP, port 7777.
Sürüm / Version: $buildId
"@
[IO.File]::WriteAllText((Join-Path $stage "OKUBENI.txt"), $readme, (New-Object Text.UTF8Encoding($true)))

$zip = Join-Path $root "Builds\BirimizKovulacak-Windows-Playtest.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Package: $zip ($size MB)"
