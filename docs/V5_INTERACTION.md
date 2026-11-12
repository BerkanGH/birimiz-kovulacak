# v5 Etkileşim Tasarımı (taslak)

Durum: **[KARAR]** (2026-11-11, kullanıcı onayladı; §6'daki dört soru önerilen şekilde kapandı). Kurallar (`V5_DESIGN.md`, `V5_ENGINE_SPEC.md`) değişmez; bu belge
sadece oyuncunun kuralları **nasıl** oynadığını, yani sunumu anlatır. Şu anki tek pencerelik arayüzün yerini alır.

## 1. İlkeler

1. **Hareket iş çıkarmaz.** v2'de ortak ofiste yürüme, gizlenme ve eşya taşıma vardı. Oyun kalabalıklaştı, yalan
   merkeze geçemedi (`V3_DESIGN.md`). v5'te yürümek "orada olma" hissi içindir; koşturmaca ya da gizlenme yok.
2. **Her eylemin bir nesnesi var.** Görevi bilgisayardan, aramayı telefondan, oyu kâğıttan yaparsın.
3. **Hiçbir şey iki adımdan uzak değil.** Her nesnenin bir kısayolu da var; yürümek istemeyen oturduğu yerden oynar.
4. **Ekranda kalan az.** Sadece ince bir şerit kalır: aşama, süre, primin ve tek satırlık "Şimdi: …" ipucu.
   Bildirimler (mesaj geldi, prim geldi, telefon çalıyor) köşede kısa baloncuklar olarak çıkar.
5. **Ağ yükü artmaz.** Herkes kendi ofisinde olduğu için yürüme sadece yerelde hesaplanır. Toplantıda yalnızca
   bakış yönü paylaşılır (bkz. §3).

## 2. Ofis (kendi odan)

Fare ile bakılır, WASD ile oda içinde yürünür. Ofis birkaç adım genişletilebilir (şu an 3,6 × 3,2 m).
Bir nesneye bakınca adı çıkar; **E / sol tık** ile kullanılır, **Esc** ile bırakılır.

| Nesne | Ne yapar | Kısayol |
|---|---|---|
| **Bilgisayar** | Kamera monitöre yaklaşır. İçinde uygulamalar: **Görev** (Müşteri Bonusu seçimi), **Havale** (prim gönder), **Mesajlar**, **Arama geçmişi** | Q |
| **Masa telefonu** | Ahize kulağa kalkar (animasyon hazır). Telefonun ekranında rehber var: kim müsait, kim kiminle konuşuyor (hat panosu). Ara, cevapla (Y), reddet (N), kapat | Tab |
| **Kasetçalar** (yeni, masada) | Görüşmedeyken kırmızı düğme kaydı başlatır. Kalan kayıt hakkı yanındaki kaset sayısıyla görünür | R |
| **Kasa kutusu** (Küçük Kasa'da masada belirir) | Kapağını açıp parayı alırsın ya da dokunmazsın. Görev bitene kadar geri koyabilirsin. Molada tanıksan kutunun üstünde **İhbar** düğmesi yanar | — |
| **Nöbet çizelgesi** (Hafta Sonu Nöbeti'nde duvarda) | Kalemle adını yazarsın (gönüllü) ya da yazmazsın; silebilirsin. Kimin yazdığı süre bitince görünür | — |
| **Mantar pano** | Suç tahtası: herkesin suçu, aday/mühürlü etiketleri. Yakınlaşınca okunur | — |
| **Çekmece** | Primin burada deste olarak durur; destenin kalınlığı primini gösterir. Finaldeki "masa açılışı" bu çekmeceden yapılır | — |
| **Kapı** | Toplantı yaklaşınca üstündeki ışık yanar. Süre dolunca ekran kararır ve toplantı odasına geçilir (koridor yok) | — |

Görev başına bir nesne (bilgisayar, kasa, çizelge) olması bilinçli bir seçim: üç görev üç farklı hareketle
oynanır, hepsi aynı ekranda düğmeye basmaya dönüşmez.

## 3. Toplantı odası

Oturarak oynanır. Fare ile serbest bakılır (sağa sola yaklaşık 110°).

- **Karakterler:** Konuşanın karakteri konuşma animasyonu yapar. Bir karaktere bakınca adı çıkar.
- **Bakış yönü paylaşılır:** Her oyuncunun karakterinin başı, oyuncunun baktığı yöne döner. "Neden bana
  bakıyorsun?" anları için ucuz ama güçlü bir sosyal ipucu.
- **Projektör:** Dosya slaytları (başarısız görevler, ihbarlar, soruşturmalar). Sızan kayıt çalarken kasetçalar
  görüntüsü ve dalga formu burada.
- **Beyaz tahta:** Canlı suç tahtası.
- **Önündeki klasör (tablet):** Kendi eylemlerin: Soruştur, Kaydı sızdır, toplantı sohbeti, mesajlar, havale. **E** ile alınır.
- **Oy pusulası:** Sadece oylamada önüne kâğıt ve kalem gelir. İsmi seçersin, kâğıt katlanıp masadaki kutuya
  atılır (animasyon). Oyunu süre bitene kadar değiştirebilirsin.
- **Kriz:** Kriz görüşmeleri telefonla olduğu için ofiste yapılır. Kriz oylaması toplantı odasında, pusulayla.
- **Savunma:** Savunan ayağa kalkar ve üstüne spot düşer (spot hazır). Diğer karakterler ona döner.
- **Masa açılışı:** Projektörde kovulanın çekmecesi açılır ve para destesi sayılır. Paylar oy verenlerin önüne
  kayar. Kovulan karakter kutusunu alır (kutu hazır).
- **Sonuç:** Projektörde kazanan ve sıralama. Ayrıntılar (tüm oylar, havaleler, seçimler) klasörde.

## 4. Kontroller

| Tuş | Ofis | Toplantı |
|---|---|---|
| Fare | Bak | Bak |
| WASD | Yürü | — |
| E / sol tık | Baktığın nesneyi kullan | Klasörü al, pusulada seç |
| Q / Tab / R | Bilgisayar / telefon / kayıt | — |
| V | Bas-konuş | Bas-konuş |
| Y / N | Cevapla / reddet | — |
| Esc | Bırak / menü (Kurallar, Ayarlar) | Bırak / menü |

## 5. Uygulama sırası

1. **Ofis:** Bakma ve yürüme, bilgisayar (görev, havale, mesajlar), telefon ve rehber, kasetçalar, mantar pano,
   ince şerit. Şimdiki arayüzün parçaları monitörün ve telefonun içine taşınır.
   *Uygulandı (`feature/v5-integration`):* ofis 4,6 × 4,0 m; dosyalar ve ayrıntılar `UI.md` (v5 → Office).
2. **Görev nesneleri:** Kasa kutusu, nöbet çizelgesi, çekmecedeki prim destesi.
   *Uygulandı (`feature/v5-integration`):* kasa kutusu masanın solunda (Küçük Kasa'dan molasının sonuna kadar),
   nöbet çizelgesi mantar panonun sağında duvarda (koltuktan erişilir), çekmecede beşlik desteler ve tam sayıyı
   yazan etiket. Bilgisayardaki Görev uygulaması bu iki görev ve ihbar için tek düğmeyle nesneye götürür. Ayrıntılar
   `UI.md` (v5 → Task objects).
3. **Toplantı:** Bakış ve paylaşılan bakış yönü, projektör, beyaz tahta, klasör, pusula ve kutu, savunma, masa açılışı.
   İki parçaya bölündü:
   - *3a, uygulandı (`feature/v5-integration`):* toplantı odasında oturarak bakış (sağa sola yaklaşık 55°, toplam
     ~110°), herkesin karakteri kendi renginde, bakınca isim etiketi, konuşanın konuşma animasyonu, paylaşılan bakış
     yönü (8 Hz, sadece sunum; botlar konuşana, en çok suçu olana ya da masaya bakar), iki beyaz tahtada canlı suç
     tahtası (sağ sıra için tekerlekli tahta; E ile yakınlaşır), önündeki klasör (E: Soruştur, Kaydı sızdır, sohbet,
     mesajlar, havale, dosya, kurallar), oylamada kâğıt ve kalem (isim işaretlenir, kâğıt katlanıp masadaki kutuya
     atılır, süre bitene kadar yeni kâğıtla değiştirilir; finalde boş oy satırı), kutuya düşen her kâğıt herkese görünür
     (içeriği değil). Ofiste ortak arama: ikili/üçlü görevde ortak rehberin başında, Tab ile seçili açılır, E/Enter
     arar; ilk üç maçta 15 sn sonra ipucu. Ayrıntılar `UI.md` (Meeting room). Projektörün işi (dosya, kararlar, savunma
     sırası, masa açılışı, sonuçlar) ve kasetçalar şimdilik odanın üstünde kart olarak duruyor.
   - *3b, sırada:* projektör slaytları (dosya: başarısız görevler, ihbarlar, soruşturmalar), sızan kaydın projektörde
     çalınması, savunmada ayağa kalkma ve spot, masa açılışı projektörde, sonuçlar projektörde ve ayrıntılar klasörde.
4. **Sonra, ayrı karar:** Yüz yüze görüşme için ortak mola alanı (kayda girmez ama herkes görür). Oyun testinden sonra.

Her adımdan sonra oyun derlenir ve botlarla doğrulanır; kullanıcı oyuna girip dener, sonra bir sonraki adıma geçilir.

## 6. Kararlar

1. **[KARAR]** Görevler nesnelere dağılır: Müşteri Bonusu bilgisayarda, Küçük Kasa kasa kutusunda, Hafta Sonu
   Nöbeti duvardaki çizelgede.
2. **[KARAR]** Ofis birkaç adım yürünebilecek kadar büyütülür.
3. **[KARAR]** Kısayollar kalır; yürümek zorunlu değildir.
4. **[KARAR]** Toplantıda bakış yönü paylaşılır; karakterin başı oyuncunun baktığı yöne döner.
5. **[KARAR]** Yeni nesneler mevcut Blender betikleriyle, mevcut tarzda üretilir (Blender 5.2.2).
6. **[KARAR]** Animasyon: nesne hareketleri (çekmece, pusula, para destesi, kamera) ve basit karakter hareketleri
   (ayağa kalkma, eğilme) kodla yapılır; görünüşe kullanıcı denedikçe ince ayar yapılır.
