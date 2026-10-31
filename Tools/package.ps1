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
BİRİMİZ KOVULACAK — Kapalı Oyun Testi (v4: Hibrit Ofis)
=======================================================
Şirketi birlikte ayakta tutun (hedef %65 verimlilik), ama herkesin gizli bir hedefi var. Son toplantıda biri kovulabilir
— ya da kimse. Hedef tutmazsa şirket kapanır ve herkes kovulur.

1) BirimizKovulacak.exe dosyasını çalıştır (SmartScreen uyarısında: Ek bilgi -> Yine de çalıştır). Oyun ilk açılışta tam
   ekran başlar (Ayarlar -> Görüntü). Dili seç (Türkçe / English).
2) Ayarlar -> Mikrofon: mikrofonunu seç, "Kendini dinle" ile dene. Konuşmak için V'ye basılı tut. Kulaklık önerilir.
   Sesli sohbet oyunun içinde: Discord KULLANMAYIN. Mesai saatinde sadece telefondaki kişi duyar; toplantıda herkes duyar.
   Konuşmak istemeyen herkes yazışarak da oynayabilir: mesaj, e-posta, toplantı sohbeti, anonim şikâyet.
   Görüşmeler kaydedilebilir: toplantıda bir görüşmeden 7 saniye çalınır (kayıtlar diske yazılmaz; lobide kapatılabilir).
3) Biriniz "Lobi Kur" desin (Kısa Maç 3 blok ≈ 20 dk / Tam Maç 5 blok ≈ 33 dk); diğerleri ev sahibinin IP adresiyle
   "Lobiye Katıl". 3-6 kişi. Herkes "Hazırım" deyince ev sahibi maçı başlatır. Her görevin 5 adımlık kısa anlatımı var.

Oyun testi verileri (isteğe bağlı): ilk açılışta sorulur, varsayılan "Hayır". İzin verirsen yalnızca oyun olayları ve
süreler kaydedilir — ses, kayıt, yazdığın metinler ve adın asla. Ayarlar -> Gizlilik: izni geri al, verilerini sil,
kayıtları dışa aktar (test sonunda dosyayı test yürütücüsüne verin). F8: komik/şaşırtıcı bir anı işaretle (sadece test ekibi görür).

İnternetten oynamak, bağlantı sorunları ve kontroller: KURULUM_VE_BAGLANTI.md

English: keep the company above 65% productivity together while each of you chases a secret objective; private phone
calls or texts, meetings with real 7-second call recordings, and a final vote that may fire someone — or nobody.
Optional playtest analytics are asked on first launch (default No); Settings -> Privacy to change, delete or export.
Sürüm / Version: $buildId
"@
[IO.File]::WriteAllText((Join-Path $stage "OKUBENI.txt"), $readme, (New-Object Text.UTF8Encoding($true)))

$zip = Join-Path $root "Builds\BirimizKovulacak-Windows-Playtest.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Package: $zip ($size MB)"
