# v5 uygulama planı (taslak)

> Kaynak tasarım: `docs/V5_DESIGN.md`. Bu plan 2026-11-10'da mevcut kodun incelenmesiyle hazırlandı. Onaylanmadan
> kod yazılmaz. Satır sayıları yaklaşıktır.

## 0. Mevcut durumun özeti

| Katman | Boyut | v4'e bağlılık | Karar |
|---|---|---|---|
| Kural motoru `Core/Line` (sözleşmeler, motor, codec, bot) | ≈ 5.000 satır | Tamamen v4 | **Yeniden yazılır** |
| Motor testleri `Tests/Line` | ≈ 1.600 satır, 140 test | Tamamen v4 | **Yeniden yazılır** |
| Ağ `Scripts/Net` | ≈ 1.900 | ≈ %70'i genel | Korunur, kenarları düzenlenir |
| Ses `Scripts/Voice` | ≈ 4.800 | Yok (motora sadece arayüzle bağlı) | **Korunur** |
| Odalar, müzik, efekt, giriş | ≈ 3.300 | Çok az | Korunur, aşama eşlemesi düzenlenir |
| Arayüz kabuğu (menü, lobi, ayarlar) | ≈ 2.700 | Lobi kısmı | Korunur, lobi düzenlenir |
| Masa arayüzü `UI/Desk` | ≈ 6.100 | Ağır | **Yeniden yazılır** (telefon, bildirim, bilgisayar kabuğu kurtarılır) |
| Toplantı arayüzü `UI/Meeting` | ≈ 4.200 | Ağır | **Çoğu yeniden yazılır** (TapeDeck, projektör, oy pusulası, savunma, sohbet kurtarılır) |
| Telemetri | ≈ 5.200 (Core + Scripts) | Gözlemci katmanı v4 | Altyapı korunur, v4 gözlemcisi devre dışı |
| Editor / build (`ProjectBuilder`, scriptler) | ≈ 2.200 | Sadece config yazımı | Korunur |

Önemli bulgular:
- Motor, oyunun geri kalanına tek bir arayüzle bağlı (`ILineEngine`). Host her karede `Tick` çağırır, her oyuncuya
  ayrı görünüm (`BuildView`) gönderir. Bu model v5 için de doğru: gizli bilgi zaten oyuncu başına görünümle
  korunuyor.
- **Telefon görüşmelerinin durumu motorda tutuluyor**, ses katmanı sadece "kim kiminle bağlı" diye soruyor. Kayıt,
  host belleğinde her bağlı görüşme için zaten yapılıyor; toplantıda neyin çalınacağını motor seçiyor. v5'te sadece
  seçimi "oyuncunun sızdırdığı kayıt" yapacağız; kayıt/ses altyapısı değişmez.
- Ağ mesajlarını kodlayan `LineCodec` (≈ 1.800 satır) her v4 tipini tek tek yazıyor. v5 tipleri için yeniden
  yazılacak, ama v5'in tip sayısı çok daha az.
- v3 → v4 geçişi yerinde değiştirmeyle yapılmıştı; iki kural seti yan yana tutulmamıştı.

## 1. Strateji

1. **Yerinde değiştirme.** v4 yan yana tutulmaz (bakımı iki katına çıkarır, hiçbir zaman ikisini birden
   oynatmayacağız). v4, `main` üzerinde bir etiketle korunur: `v4.0-hybrid` (etiketi push etmek için onay alınır).
2. **Motor önce, arayüz sonra.** Kurallar ucuz ve test edilebilir; pahalı olan arayüz. Önce tüm v5 kuralları saf C#
   olarak yazılıp testlerle ve bot simülasyonuyla doğrulanır. Arayüz, kurallar oturduktan sonra yazılır; böylece
   kural değişiklikleri arayüzü tekrar tekrar bozmaz.
3. **Sınır korunur.** `ILineEngine` arayüzünün şekli (Tick / Apply / BuildView / DrainEvents ve ses kancaları
   `ConnectedCallId`, `CallPartner`, `RecordingPicks`, `ReportRecording`, `MaySpeak`) korunur. Sözleşme tipleri
   (aşamalar, fiiller, görünümler, config) v5'e göre yeniden tasarlanır.
4. **Sayılar config'te.** `V5_DESIGN.md` §12'deki her sayı `LineConfig` içinde olur; test sonuçlarına göre kod
   değişmeden ayarlanır.
5. **Her aşama ayrı branch.** `feature/v5-engine`, `feature/v5-integration`, … Merge kararı kullanıcınındır.

Önceki konuşmada söylenen aşama sırası (Aşama 1: sadece bir kısım kural) şu sebeple değişti: motorun tamamı küçük ve
Unity'siz test edilebiliyor; kuralları parça parça arayüze bağlamak yerine hepsini bir kerede doğrulamak daha ucuz.

## 2. Aşamalar

### Aşama 1: v5 kural motoru (Unity gerekmez) — branch `feature/v5-engine`

Hedef: v5 kurallarının tamamı saf C#'ta, testlerle ve botlarla doğrulanmış halde. Oyun bu aşamada derlenmez (Unity
tarafı hâlâ v4 tiplerini kullanır); bu bilinçli bir ara durumdur.

| İş | Dosyalar |
|---|---|
| Sözleşmeler: aşamalar, fiiller, görev türleri, olaylar | `Contracts/LineEnums.cs` (yeniden) |
| Config: §12 sayıları, süreler, oyuncu sayısına göre tablolar | `Contracts/LineConfig.cs` (yeniden) |
| Görünümler: herkese açık (suç tahtası, hat panosu, oylama durumu), kişisel (prim, telefon, görev, tanıklık, kayıt cihazı, soruşturma hakkı), toplantı, final, sonuç (tüm bakiyeler, tüm oylar, kim kimi soydu) | `Contracts/LineViews.cs` (yeniden) |
| API: `ILineEngine` şekli korunur, v4'e özel üyeler çıkar | `Contracts/LineApi.cs` (düzenleme) |
| Motor çekirdeği: durum, saat, aşama makinesi | `Engine/LineEngine.cs` |
| Telefon: v4 arama/cevap/meşgul mantığı korunur; hat panosu ve kayıt işaretleme eklenir | `Engine/LineEngine.Phone.cs` (v4 `Office` kısmından) |
| Görevler: Müşteri Bonusu, Küçük Kasa (ihbar/şantaj penceresi dahil), Hafta Sonu Nöbeti; tekrarsız eşleşme ve üçlü kuralı | `Engine/LineEngine.Tasks.cs` |
| Toplantı: dosya, suç oylaması (başarısız görev başına 1 oy), soruşturma, kayıt sızdırma | `Engine/LineEngine.Meeting.cs` |
| Kriz: telefon pazarlığı, gizli oy, beraberlikte paylaşım | `Engine/LineEngine.Crisis.cs` |
| Final: adaylar, savunma, gizli oy, beraberlik zinciri, sıralı ikinci kovulma (5–6 kişi), masa açılışı, primin oy verenlere dağıtımı, kazanan | `Engine/LineEngine.Final.cs` |
| Prim transferi (oy satın alma, şantaj, rüşvet) | motor içinde |
| Codec: v5 tipleri; v4'ün ilkel yardımcıları (başlık, LEB128, doğrulanmış enum) kullanılır | `Engine/LineCodec.cs` (yeniden) |
| Botlar: Dürüst / Açgözlü / Karışık politikalar | `Engine/LineBot.cs` (yeniden) |
| Testler: her kural, gizlilik (kimin neyi gördüğü), determinizm, codec | `Tests/Line/*` (yeniden) |
| Simülasyon: §12 "sağlama" hesabını botlarla doğrular (ortalama prim, suç dağılımı, kovulanın kim olduğu) | `Tools/LineSim` (düzenleme) |
| Derleme tüketicileri: telemetri testlerindeki sahte motor, derleme stub'ları | `Tools/TelemetryTests`, `Tools/stubs` (uyarlama ya da geçici devre dışı) |

Bitti sayılma ölçütü: `dotnet test` geçer; LineSim 3–6 oyuncuyla binlerce maç oynatır, hiçbir maç takılmaz ve
sonuçlar §12'deki sağlamayla tutarlıdır; gizlilik testleri hiçbir oyuncunun görmemesi gereken bilgiyi görmediğini
doğrular.

### Aşama 2: Unity entegrasyonu ve sade arayüz — branch `feature/v5-integration`

Hedef: oyun derlenir ve **çirkin ama oynanabilir** bir v5 maçı baştan sona oynanır.

- Unity projesi 6000.6.3f1 → **6000.6.8f1** (ayrı commit; arkadaşa haber verilir).
- `HostLineController`, `LineClient`, `LineDrivers`, `GameConfig`, `LaunchArgs`, müzik aşama eşlemesi v5 aşamalarına
  uyarlanır. Lobi: Kısa/Uzun ayarı kalkar.
- Telemetri: v4 gözlemcisi devre dışı, altyapı (onay, gizlilik) korunur.
- Masa: görev ekranı (3 görev), prim, suç tahtası, telefon (mevcut telefon bileşenleri), transfer düğmeleri.
- Toplantı: dosya, oy pusulası (mevcut bileşen), Kriz, final, sonuç ekranı. Sade görünüm.
- Botlarla çoklu pencere testi (`run_local4.ps1`) ve log doğrulayıcı v5 aşamalarına uyarlanır.

### Aşama 3: Kayıt cihazı, hat panosu, masa açılışı

- Görüşme sırasında "kaydet" düğmesi; toplantıda "sızdır" seçimi; kayıt mevcut TapeDeck ile çalınır.
- Hat panosu: kim kiminle konuşuyor, ne kadar süredir.
- Final ve masa açılışı sahneleri: "çekmeceden 23 prim çıktı", tüm oyların açıklanması.
- Sentetik sesli botlarla (`-voicetest`) kayıt ve sızıntı akışı doğrulanır.

### Aşama 4: Cila

- Arayüzün görsel tasarımı, Türkçe/İngilizce metinler, görev öğreticileri.
- Arayüz mock senaryoları (`-uimock`) v5 için yeniden yazılır; ekran görüntüleriyle kontrol.
- Telemetri gözlemcisi v5 olaylarına göre (playtest analizi için).
- Eski, yanıltıcı dokümanların (ARCHITECTURE.md, UI.md) güncellenmesi.

## 3. Kod yazmadan önce netleşmesi gereken kural detayları

Tasarım dokümanında açık kalan ve motoru yazarken karar gerektiren noktalar. Her birinin yanında önerilen
varsayılan var; kullanıcı onaylarsa böyle uygulanır.

1. **Soruşturma neyi kapsar?** Öneri: sadece Müşteri Bonusu'nda ortağın seçimi (Kasa'nın ihbarı var, Nöbet zaten
   açık). Soruşturma toplantı dosyası ya da tartışma aşamasında açılır.
2. **Müşteri Bonusu'nda herkese açık bilgi.** Öneri: "Bu turda N kişi hepsini aldı" (grup belirtilmeden).
3. **Kasa'da iki kişi alırsa** tanıklık yoktur; ikisi de otomatik +2 suç alır ve bu herkese açıklanır.
4. **Kriz pazarlığı.** Öneri: 75 sn sadece 1'e 1 telefon (toplantı sesi kapalı), ardından 20 sn gizli oy; kendine oy
   verilemez, çekimser kalan oyunu kendine vermiş sayılır.
5. **Final oylaması.** Öneri: sadece adaylara oy verilir; adaylar da oy kullanır (kendine veremez); çekimser yok.
6. **Kovulanın primi** ona oy verenlere eşit bölünür; artan kalan yanar. Kimse oy vermediyse (beraberlik zinciriyle
   kovulduysa) prim yanar.
7. **Kayıt cihazı.** Öneri: görüşme sırasında "kaydet"e basılınca o görüşme işaretlenir (karşı taraf bunu görmez).
   Toplantıda en fazla bir sızıntı; 7 sn'lik kesit mevcut seçiciyle (en yoğun konuşma) alınır.
8. **Transferler.** Öneri: ofis aşamalarında, molada, Kriz'de ve final oylamasından önce açık; her transfer sadece
   gönderen ve alıcıya görünür; maç sonunda hepsi açıklanır.
9. **Hat panosu toplantı özeti.** Öneri: prototipte yok (Aşama 3'te değerlendirilir).

## 4. Riskler

- **Aşama 1 sonunda oyun derlenmez.** Bu yüzden Aşama 1 ile 2 arka arkaya yapılmalı; `main`'e Aşama 2 bitmeden
  merge edilmez.
- **Arayüz işi büyük** (≈ 10.000 satırlık v4 arayüzünün yerine). Aşama 2'de bilinçli olarak sade tutulur.
- **Gerçek ağ ve ses** testi botlarla yereldir; internet üzerinden gerçek oyuncu testi kullanıcıların işidir.
- **Arkadaşın Unity sürümü.** 6.8f1 yükseltmesi önceden haber verilmeli.
