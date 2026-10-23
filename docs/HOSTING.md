# Birimiz Kovulacak — Kurulum, Lobi Kurma ve Katılma

Bu rehber kapalı oyun testi (4 arkadaş) içindir. Sesli iletişim için **Discord** kullanılır; oyunun içinde sesli
sohbet yoktur.

## 1. Kurulum

1. `BirimizKovulacak-Windows-Playtest.zip` dosyasını bir klasöre çıkar (masaüstü olabilir).
2. `BirimizKovulacak.exe` dosyasını çalıştır.
   - Windows "Bilgisayarınız korundu" (SmartScreen) uyarısı verirse: **Ek bilgi → Yine de çalıştır**. Oyun imzasız bir
     test sürümüdür.
3. Herkes **aynı ZIP** dosyasını kullanmalı. Farklı sürümler birbirine bağlanamaz ("Sürüm uyuşmuyor").

Gereksinimler: Windows 10/11 64-bit, DirectX 12 destekli ekran kartı, klavye ve fare.

## 2. Lobi kurmak (ev sahibi)

1. Ana menüde adını yaz → **Lobi Kur**.
2. Windows Güvenlik Duvarı izin isterse **İzin ver** (en azından "Özel ağlar").
3. Lobi ekranının üst kısmında arkadaşlarının bağlanacağı **IP adres(ler)i ve port (7777, UDP)** yazar.
4. Dört kişi lobiye girip **Hazırım** dediğinde **Maçı Başlat**'a bas.

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
| Sol Ctrl | Yavaş yürü (müdür hızlı yürüyenleri fark eder) |
| Tab | Telefon: hedefin, notların, delil panosu (İK Muhbiri için tarama/mühür) |
| Esc | Menü, ayarlar (ses, fare hassasiyeti, tam ekran, **tuş atamaları**) |

Bir işlem sürerken hareket edersen işlem iptal olur.

## 6. Test için botlar

Lobi kurucusu "Test için boş koltukları botlarla doldur" düğmeleriyle boş koltuklara bot koyabilir. Botlar yalnızca
oyunu tek başına denemek içindir; gerçek oyun dört insanla oynanır.

## 7. Sorun giderme

| Mesaj | Çözüm |
|---|---|
| Ev sahibine bağlanılamadı | IP/port doğru mu? Ev sahibinin güvenlik duvarı izni verildi mi? İnternetten oynuyorsanız Tailscale ya da port yönlendirme gerekir. |
| Sürüm uyuşmuyor | Herkes aynı ZIP'i kullanmalı. |
| Lobi dolu | Dört koltuk dolu (bot koltukları dahil). |
| Maç zaten başladı | Maç başladıktan sonra yalnızca bağlantısı kopan oyuncu geri girebilir. |
| Lobi kurulamadı | 7777 portunu kullanan başka bir program olabilir; farklı bir port seçip arkadaşlarına söyle. |

Kayıtlar (hata bildirirken ekleyin): `%USERPROFILE%\AppData\LocalLow\Leonidas Studio\Birimiz Kovulacak\` altındaki
`Player.log` ve `logs\` klasörü.

---

### English summary
Unzip, run `BirimizKovulacak.exe`. Host: *Lobi Kur* (allow the firewall prompt), share the shown IP + UDP port 7777.
Clients: enter IP/port → *Lobiye Katıl* → *Hazırım*; the host starts when four players are ready. For internet play use
Tailscale/ZeroTier (no router changes) or forward UDP 7777 to the host. Drops auto-reconnect for 30 s; a crashed client
can restart and use *Maça geri dön*. Host leaving or a player missing the grace period abandons the match.
