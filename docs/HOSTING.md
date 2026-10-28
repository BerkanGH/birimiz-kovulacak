# Birimiz Kovulacak — Kurulum, Lobi Kurma ve Katılma

Bu rehber kapalı oyun testi (3–6 arkadaş) içindir. **Sesli sohbet oyunun içindedir — Discord'a gerek yok, hatta
kullanmayın:** ofiste yakınındakileri duyarsın (duvarlar sesi boğar), telefonla birini arayınca yalnızca o duyar
(yanında duran biri seni duyabilir!), toplantıda herkes herkesi duyar. Kulaklık önerilir.

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
4. Mod seç: **Hızlı Oyun** (tek maç, her şey açık, rastgele müdür) veya **İş Haftası** (Pazartesi→Cuma 5 maç,
   kariyer puanı, Cuma finali ve "Ayın Elemanı"). İş Haftasında yeni sistemler gün gün açılır.
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

- Oyun **30 saniye** boyunca otomatik olarak yeniden bağlanmayı dener. Aynı koltuk, rol, hedef ve notlar geri gelir.
- Oyun çöktüyse: hemen yeniden aç → ana menüde **Maça geri dön** düğmesi (yaklaşık 1 dakika görünür).
- 30 saniye içinde dönülmezse ya da ev sahibi ayrılırsa maç **İPTAL** edilir (kazanan yok).

## 5. Kontroller

| Tuş | İş |
|---|---|
| WASD | Yürü |
| Fare | Kamerayı çevir · tekerlek: yakınlaştır |
| E / F | Bakılan şeyle etkileşim (birincil / ikincil) |
| Q | Elindekini yere bırak |
| R | Çalışıyormuş gibi yap (müdür baskınında hayat kurtarır) |
| Sol Ctrl (basılı) / C | Eğil, sinsi yürü — tanıklar seni daha yakından fark eder, bölmelerin arkasında görünmezsin |
| V (basılı) | Bas-konuş (Ayarlar'dan açık mikrofona geçilebilir) |
| Y / N | Gelen aramayı aç / reddet · aramayı kapat |
| G | Elindekini kullan (ör. kahvenin tadına bak) |
| T (basılı) + fare · 1–5 | Mimik çarkı / hızlı mimik |
| Tab | Telefon: görev, gizli kartlar, rehber (arama + **ihbar hattı**), gelen kutusu, notlar, pano, İK (muhbir) |
| Esc | Menü, ayarlar (ses, fare hassasiyeti, tam ekran, **tuş atamaları**) |

Bir işlem sürerken hareket edersen işlem iptal olur.

## 6. Test için botlar

Lobi kurucusu **Bot ekle** ile boş koltuklara bot koyabilir (3 kişiyseniz 4. koltuğu doldurmak için de işe yarar).
Botlar konuşmaz; görevleri yapar, sabotaj yapar, arar ve oy kullanır.

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
`Player.log` ve `logs\` klasörü.

---

### English summary
Unzip, run `BirimizKovulacak.exe`. Voice chat is built in (proximity in the office, private phone calls, everyone in
the meeting; push-to-talk V) — do not use Discord. Host: *Lobi Kur* (allow the firewall prompt), pick Quick Play or
Work Week, share the shown IP + UDP port 7777. Clients: enter IP/port → *Lobiye Katıl* → *Hazırım*; 3–6 players
(bots can fill seats); the host starts when everyone is ready. For internet play use
Tailscale/ZeroTier (no router changes) or forward UDP 7777 to the host. Drops auto-reconnect for 30 s; a crashed client
can restart and use *Maça geri dön*. Host leaving or a player missing the grace period abandons the match.
