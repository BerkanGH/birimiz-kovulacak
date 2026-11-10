# Birimiz Kovulacak — Kurulum, Lobi Kurma ve Katılma

Bu rehber kapalı oyun testi (3–6 arkadaş) içindir. **Sesli sohbet oyunun içindedir — Discord'a gerek yok, hatta
kullanmayın:** ofis aşamalarında sadece telefonla 1'e 1 konuşulur (seni yalnızca aradığın kişi duyar), toplantıda
herkes herkesi duyar. Kulaklık önerilir. Kurallar oyunun içinde: ana menü, lobi ve oyun içi menüde **Nasıl oynanır**
(v5, 11 kısa sayfa); maç ekranındaki "Şimdi ne yapıyorsun?" kartı her aşamada ne yapılacağını söyler.

## 1. Kurulum

1. `BirimizKovulacak-Windows-Playtest.zip` dosyasını bir klasöre çıkar (masaüstü olabilir).
2. `BirimizKovulacak.exe` dosyasını çalıştır.
   - Windows "Bilgisayarınız korundu" (SmartScreen) uyarısı verirse: **Ek bilgi → Yine de çalıştır**. Oyun imzasız bir
     test sürümüdür.
3. Herkes **aynı ZIP** dosyasını kullanmalı. Farklı sürümler birbirine bağlanamaz ("Sürüm uyuşmuyor").

Gereksinimler: Windows 10/11 64-bit, DirectX 11 destekli ekran kartı, klavye, fare ve mikrofon (kulaklık önerilir).
Oyun ~450 MB RAM kullanır.

İlk açılışta **Ayarlar → Mikrofon** bölümünden mikrofonunu seç ve "Kendini dinle" ile dene. Varsayılan: **V'ye basılı
tut, konuş** (bas-konuş). İstersen açık mikrofon moduna geçebilirsin.

## 2. Lobi kurmak (ev sahibi)

1. Ana menüde adını yaz → **Lobi Kur**.
2. Windows Güvenlik Duvarı izin isterse **İzin ver** (en azından "Özel ağlar").
3. Lobi ekranının üst kısmında arkadaşlarının bağlanacağı **IP adres(ler)i ve port (7777, UDP)** yazar.
4. Maç ayarı yok: her maç 3 görev, 1–2 toplantı, kriz ve final (≈ 12–15 dk); finalde biri (5–6 kişide iki kişi)
   kovulur. "Görüşmeler kaydedilebilir" kutusu kayıt cihazını ve toplantıda 7 saniyelik sızıntıların dinletilmesini
   açar/kapatır. Oyun testi yürütüyorsanız "Deneme grubu kodu" alanına grubunuzun kodunu (ör. G07) yazın.
5. En az **3**, en çok **6** kişi. Eksik koltuklara **Bot ekle** ile bot koyabilirsin.
6. Herkes **Hazırım** dediğinde **Maçı Başlat**'a bas.

Ev sahibinin bilgisayarı maçın "hakemi"dir: oyunu kapatırsa maç iptal olur.

## 3. Lobiye katılmak

1. Ana menüde adını yaz, **Adres** kutusuna ev sahibinin verdiği IP'yi, **Port** kutusuna `7777` yaz.
2. **Lobiye Katıl** → lobide **Hazırım**.

### Aynı ev / aynı ağ (LAN)
Ev sahibinin lobi ekranında gösterilen yerel IP'yi (ör. `192.168.1.23`) kullanın.

### İnternet üzerinden (farklı evler)
Üç seçenek var; en kolayı **Tailscale**:

| Yöntem | Ne yapılır? |
|---|---|
| **Tailscale (önerilen)** | Herkes ücretsiz [Tailscale](https://tailscale.com) kurar ve ev sahibinin paylaştığı ağa (tailnet) katılır. Katılanlar ev sahibinin `100.x.y.z` Tailscale IP'sini girer. Modem ayarı gerekmez. |
| ZeroTier / Radmin VPN | Aynı mantık: herkes aynı sanal ağa girer, ev sahibinin sanal ağ IP'si kullanılır. |
| Port yönlendirme | Ev sahibi modeminde **UDP 7777**'yi kendi bilgisayarına yönlendirir; katılanlar ev sahibinin genel IP'sini girer. |

## 4. Bağlantı koparsa

- Oyun **30 saniye** boyunca otomatik olarak yeniden bağlanmayı dener. Aynı koltuk, primin ve maçtaki durumun geri gelir.
- Oyun çöktüyse: hemen yeniden aç → ana menüde **Maça geri dön** düğmesi (yaklaşık 1 dakika görünür).
- 30 saniye içinde dönülmezse ya da ev sahibi ayrılırsa maç **İPTAL** edilir (kazanan yok).

## 5. Kontroller (v5 — tek oyun ekranı)

Maç tek bir ekrandan oynanır: solda ekip ve suç tahtası, ortada "Şimdi ne yapıyorsun?" kartı (seçim, oy, ihbar
düğmeleri burada), sağda telefon, prim transferi ve mesajlar. Her şey fareyle tıklanır.

| Tuş | İş |
|---|---|
| Fare | Ekrandaki düğmeler: ara, seç, oy ver, prim gönder, mesaj yaz |
| Y / N | Gelen aramayı aç / reddet · aramayı kapat |
| V (basılı) | Bas-konuş (Ayarlar'dan açık mikrofona geçilebilir). Ofiste sadece telefondaki kişi duyar; toplantıda herkes |
| Enter | Yazdığın mesajı gönder |
| Esc | Menü: Nasıl oynanır, Ayarlar, çıkış |
| F8 | (Oyun testi izni varsa) komik/şaşırtıcı bir anı işaretle — sadece test ekibi görür |

Ayarlar → Görüntü ve Dil: tam ekran, Türkçe / English. Ayarlar → Ses: müzik sesi ve müziği kapat. Ayarlar → Gizlilik:
oyun testi verisi izni.

## 6. Test için botlar

Lobi kurucusu **Bot ekle** ile boş koltuklara bot koyabilir (3 kişiyseniz 4. koltuğu doldurmak için de işe yarar).
Botlar konuşmaz ve telefon açmaz; görevde seçim yapar, ihbar eder, soruşturma açar ve oy kullanır.

## 7. Sorun giderme

| Mesaj | Çözüm |
|---|---|
| Ev sahibine bağlanılamadı | IP/port doğru mu? Ev sahibinin güvenlik duvarı izni verildi mi? İnternetten oynuyorsanız Tailscale ya da port yönlendirme gerekir. |
| Sürüm uyuşmuyor | Herkes aynı ZIP'i kullanmalı. |
| Lobi dolu | Altı koltuk dolu (bot koltukları dahil). |
| Kimse beni duymuyor | Ayarlar → Mikrofon: doğru cihaz mı, seviye çubuğu oynuyor mu? Windows Ayarları → Gizlilik → Mikrofon'da masaüstü uygulamalarına izin verilmiş olmalı. Bas-konuş modundaysan V'ye basılı tut. |
| Sesler yankılanıyor | Kulaklık kullanın veya açık mikrofon yerine bas-konuş kullanın. |
| Maç zaten başladı | Maç başladıktan sonra yalnızca bağlantısı kopan oyuncu geri girebilir. |
| Lobi kurulamadı | 7777 portunu kullanan başka bir program olabilir; farklı bir port seçip arkadaşlarına söyle. |

Kayıtlar (hata bildirirken ekleyin): `%USERPROFILE%\AppData\LocalLow\Leonidas Studio\Birimiz Kovulacak\` altındaki
`Player.log` ve `logs\` klasörü. Bu dosyalarda kendi oyun hamlelerin bulunur; yalnızca geliştiriciyle paylaş.

Oyun testi verileri (yalnızca izin verdiysen): aynı klasördeki `telemetry\` altında. En kolayı Ayarlar → Gizlilik →
"Kayıtları dışa aktar": oluşan zip dosyasını test yürütücüsüne verin. Ses, kayıt, yazdığın metinler ve adın bu
dosyalarda yoktur. İzni istediğin an geri alabilir ve verilerini silebilirsin (ev sahibinin bilgisayarına daha önce
yazılmış maç olayları için test yürütücüsünden silinmesini isteyin).

---

### English summary
Unzip, run `BirimizKovulacak.exe`. Voice chat is built in (private 1:1 phone calls in the office phases, everyone in
meetings; push-to-talk V) — do not use Discord; private texts and meeting chat are an alternative to voice. Rules: *How
to play* in the menu, lobby and pause menu. A match is 3 tasks, 1–2 meetings, a crisis and the final (≈ 12–15 min).
Host: *Lobi Kur* (allow the firewall prompt), optionally type the playtest group code, share the shown IP + UDP port 7777. Optional playtest analytics: Settings → Privacy (export the zip at the
end of a test). Clients: enter IP/port → *Lobiye Katıl* → *Hazırım*; 3–6 players
(bots can fill seats); the host starts when everyone is ready. For internet play use
Tailscale/ZeroTier (no router changes) or forward UDP 7777 to the host. Drops auto-reconnect for 30 s; a crashed client
can restart and use *Maça geri dön*. Host leaving or a player missing the grace period abandons the match.
