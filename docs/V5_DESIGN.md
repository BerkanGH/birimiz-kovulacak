# v5 "Her Şey Kayıtta": tasarım taslağı

> **Durum: TASLAK, tartışma aşamasında.** Bu dosya henüz normatif değil. v4 kuralları
> `docs/GAMEPLAY_REDESIGN.md` içinde geçerliliğini koruyor. Sayılar hipotezdir ve
> kâğıt prototip ile test edilecek. Her bölüm **[KARAR]**, **[ÖNERİ]** ya da **[AÇIK]** olarak işaretli.

## 0. Neden değişiyoruz?

v4 bir "ofis simülasyonu" haline geldi: 12 görev, kusur, ihtar, para, kariyer puanı, kanıt pazarı, telafi
raporu, 9 gizli hedef. Sorunlar:

- **İhanet karşılıksız kalıyor.** Sabotaj sadece para kazandırıyor, para puana dönmüyor ve yakalanırsan −50
  puan alıyorsun. Mantıklı oynayan herkes işbirliği yapıyor, yani tartışma çıkmıyor.
- **"Birimiz Kovulacak" ama kimse kovulmak zorunda değil.** "Kimseyi kovma" seçeneği var.
- **Yalan mekanik olarak bitiriliyor.** Satın alınan kanıtlar kesin ve sunulduğunda tartışma bitiyor.
- **Bilişsel yük çok fazla.** Oyuncular yalan söylemek yerine fatura topluyor.

Hedef: **arkadaş grupları arasında tartışma, ihanet ve kahkaha çıkaran, kısa ve tekrar oynanmak istenen bir oyun.**
Vampir köylünün bir kopyası olmamalı: iyi/kötü takımı yok, rol kartı yok.

## 1. Oyunun kimliği [KARAR]

> *Herkes biraz kötü. Her telefon kaydedilebilir. Sonunda biri kovulur.*

- **Özgün mekanik:** Özel telefon görüşmeleri gerçekten kaydedilebilir ve toplantıda herkese çalınabilir.
- **Sorulan soru:** "Kim kötü?" değil, **"Kime güvenirim, kimi satarım?"**
- **Neden oynanır?**
  - Arkadaşının seni sattığı an, kendi sesiyle tekrar çalar.
  - Her maç kişisel bir hikâyeyle biter ("Ahmet beni 3. toplantıda sattı") ve rövanş isteği doğurur.
  - Maç kısadır, "bir tane daha" denir.

## 2. Kazanma: prim [KARAR]

- Tek bir para birimi var: **prim**. Kariyer puanı, ayrı para sistemi gibi diğer skorlar kalkar.
- **Maç sonunda en çok primi olan kazanır.**
- **Kovulan kişi tüm primini kaybeder ve bu prim, ona oy verenler arasında paylaştırılır.**
  - Oy vermenin sebebi budur: seni soyan birini kovarak paranı geri alırsın. Adalet ve açgözlülük aynı yöne çeker.
  - Doğal denge: ihanet seni zengin eder, zenginlik seni hedef yapar.
- Prim **harcanabilir** ve her harcama skoru düşürür:
  1. Soruşturma açmak (§6)
  2. Oy satın almak: başka bir oyuncuya prim verip oyunu istemek (§7)
  3. Kriz'de günah keçisine ödeme yapmak (§8.2)

## 3. Bilgi: kim neyi bilir? [KARAR]

- **Bakiyeler gizli.** Herkes sadece kendi primini görür.
- **Prim transferleri gizli.** Sadece gönderen ve alan kişi bilir.
- **Mağdur bilir, diğerleri bilmez.** Ortağın seni soyduysa bunu sadece sen görürsün. Diğerleri en fazla "bu
  turda biri hepsini aldı" bilgisini görür, kimin aldığını görmez.
- **Şans faktörü yok.** Mağdur her zaman kesin bilir. İnkâr payı sadece sözden gelir:
  - Hain "yalan söylüyor, ben paylaştım" der.
  - Masum birine iftira atmak da mümkündür. Dinleyenler kimin yalan söylediğini kendileri çözer.
- **Desen yakalanır.** Ortaklar her blokta değişir. Tek şikâyet sözüne söz kalır, ama farklı iki kişi aynı
  kişiyi suçlarsa ikna edici olur. İtibar maç boyunca rol kartı olmadan oluşur.
- **Zenginlik davranıştan sızar.** Oy satın alan ya da soruşturma açan biri "bu kadar parası nereden var?"
  sorusunu doğurur.

## 4. Suç puanı ve toplantılar [KARAR, detaylar ÖNERİ]

- **Suç puanı** kovulma adaylarını belirler. Herkese açıktır ve suç tahtasında görünür.
- Başarısız olan görevler suç üretir. Toplantı bir mahkemedir: **"Bu kimin suçu?"**
- Kesinleşen bir soruşturma (§6), ihbar (§8.2) ve Kriz (§8.2) de suç puanı yazar.

**Suç oylaması [KARAR]:**
- **Sadece başarısız görev varsa oylama yapılır.** Herkes dürüst davrandıysa suç dağıtılmaz; bu işbirliğinin
  ödülüdür. İhanet varsa suç mutlaka bir yere düşer, belki masum birine.
- **Her başarısız görev için herkesin 1 oyu vardır.** İki görev başarısızsa herkesin 2 oyu olur; ikisini aynı
  kişiye ya da farklı kişilere verebilir.
- **Her oy, alan kişiye 1 suç puanı yazar.** Kendine oy verilemez.
  - Toplu yüklenmeye karşı fren kendiliğinden oluşur: 4 kişide bir oyuncu, başarısız görev başına en fazla 3 suç
    alabilir.
  - Beraberlik sorunu yoktur, çünkü kazanan değil dağılım vardır.
  - Hep ya da hiç değildir: 2 oy alan 2, 1 oy alan 1 suç alır.
- **Çekimser kalmak yok.** Oy vermeyen kişi oyunu kendine vermiş sayılır. Yoksa arkadaşlar "kimse oy vermesin" diye
  anlaşıp suç sistemini öldürebilir.
- **Kesinleşmiş olaylar oylanmaz.** İhbar edilmiş bir kasa hırsızlığı ya da kazanılmış bir soruşturmanın suçu
  doğrudan suçluya yazılır; o görev için oylama yapılmaz.
- **Oylar gizlidir**, maç sonundaki final açılışında açıklanır.
- Reddedilen alternatif: "kazanan hepsini alır" (en çok oyu alan görevin bütün suçunu alır). Daha dramatik ama
  beraberlik kuralı gerektirir ve bir oyuncu tek toplantıda bitirilebilir.

**Toplantı akışı (≈ 90 sn) [ÖNERİ]:**
1. Dosya (≈ 15 sn): başarısız görevler, ihbarlar, soruşturmalar, sızdırılan kayıtlar.
2. Tartışma (≈ 50 sn): herkes birbirini duyar.
3. Oylama (≈ 20 sn): gizli oy, ardından suç tahtası güncellenir.
- Suç puanı "kim aday olacak" sorusunu, prim ise "kime oy vereceğim" sorusunu belirler. Masum ama zengin biri
  ile suçlu ama fakir biri aday olunca oy verenler gerçek bir ikilemde kalır.

## 4.1 Maç yapısı [ÖNERİ]

Oyuncu sayısı ne olursa olsun **2 ikili görev + 1 grup görevi + Kriz**. Oyuncu sayısı arttıkça görev değil,
toplantı ve final süresi uzar. **[KARAR 2026-11-24]** Finalden önce **tek toplantı** var (Toplantı 2 kalktı), maç **en az
4 oyuncuyla** başlar ve lobide bir **konuşma süresi** seçeneği bulunur; ayrıntılar bu bölümün sonunda.

- **İkili görev:** oyuncular ikişer eşleşir (Müşteri Bonusu, Küçük Kasa). "Sadece mağdur/tanık bilir" mantığı en
  iyi burada işlediği için maçta iki tane var.
- **Grup görevi:** bütün oyuncular birlikte oynar. *Herkes* türü (ör. Bütçe) ya da bir oyuncunun özel rolü olan
  *rol* türü (ör. Masraf Onayı).

```
Görev 1: İkili görev (ör. Müşteri Bonusu)
   ↓ mola (telefonlar)
Görev 2: İkili görev (ör. Küçük Kasa, yeni ortakla)
   ↓ mola (ihbar kararı, şantaj)
TOPLANTI: iki görevin hesabı
   ↓
Görev 3: Grup görevi (ör. Hafta Sonu Nöbeti); sonucu açıklandığı anda işler (v5.2: Toplantı 2 yok, §8.2)
   ↓
KRİZ  → telefon pazarlığı, sonra büyük suç gizli oyla birine yazılır, suç tahtası sarsılır
FİNAL → en çok suçu olanlar aday olur → prim beyanı (§5) → savunma → kovulma
SONUÇ → tam açılış, ödüller ve manşetler (§5)
```

**Toplantı sayısı [KARAR]:** Her görevden sonra değil, **iki ikili görevden sonra bir** ve grup görevinden sonra
bir toplantı. Sebepler:
- Her görevden sonra toplantı tekrara düşer ve tek görevden sonra konuşacak malzeme azdır.
- Toplantı 1'de iki görevden ve iki farklı ortaktan gelen bilgiler aynı masaya gelir; çapraz kontrol tartışmanın
  kendisi olur: "Ahmet beni Bonus'ta soydu." / "Ben de onunla kasadaydım, almadı."
- Görevler arası mola entrika alanıdır: Bonus'ta soyulan biri, kasa turunda yeni ortağıyla ittifak kurar.
- Toplantı 2'nin grup görevi başarısız olmadığında atlanması dürüstlüğü ödüllendirir ve maçı kısaltır.

**[KARAR 2026-11-24] Toplantı 2 kaldırıldı.** Hafta Sonu Nöbeti açıklanınca maç doğrudan Kriz telefonlarına geçer.
Nöbetin bütün sonuçları açıklandığı anda işler: nöbet tutanın suçu 1 azalır; liste eksikse tutmayan prim almaz ve
**hemen +2 suç** alır. Oyuncular bunu üç yerde görür: ofiste nöbet çizelgesinde ("Nöbet tutulmadı: tutmayan N kişiye
+2 suç yazıldı"), suç tahtasında ve Kriz oylaması açılırken toplantı odasındaki projektörde ("NÖBET CEZASI" slaytı).
- *Neden:* v5.1'de Toplantı 2'nin oylaması kalkmıştı; geriye dosya + tartışmadan ibaret, oylamasız **ölü bir bölüm**
  kalmıştı (ceza zaten otomatikti, konuşulacak karar yoktu). Kaldırınca maç kısalır ve nöbetin sonucu, sıcağı sıcağına
  Kriz telefonlarının pazarlık malzemesi olur.
- Artık finalden önce tek toplantı olduğu için arayüz, "Nasıl oynanır" sayfaları ve bot cümleleri "1. toplantı /
  2. toplantı" demez, sadece **"Toplantı"** der.
- Eski akış bir ayar anahtarının arkasında duruyor (`LineConfig.ShiftMeeting`, varsayılan kapalı): LineSim
  `--rules old` ve eski testler onu kullanır.

**Eşleşme [KARAR]:**
- Rastgele ama tekrarsız: ikinci ikili görevde kimse ilk ortağıyla eşleşmez.
- Tek sayıda oyuncuda bir grup **üçlü** olur (5 kişi: bir ikili + bir üçlü; 3 kişi: tek üçlü). Üçlü her turda
  farklı kişilerden oluşur. Dışarıda kalan ya da bot ortak yok: biri boş kalır, bottan çalmak da drama üretmez.

| Oyuncu | Normal görev | Kriz | Kovulan | Tahmini süre |
|---|---|---|---|---|
| 4 | 3 | 1 | 1 | ≈ 10 dk |
| 5 | 3 | 1 | 2 | ≈ 11 dk |
| 6 | 3 | 1 | 2 | ≈ 11–12 dk |

- Süreler kaba tahmindir (v5.2, "Normal" konuşma süresi): görev ≈ 60–70 sn, mola 30 sn, toplantı ≈ 90 sn, Kriz ≈ 1,5–2 dk,
  final ≈ 2–2,5 dk. Hızlı seçen botlarla maç ≈ 7–8,5 dk sürüyor (`Tools/LineSim/v5-sim.txt`).
- 4 oyuncuda herkes 3 kişiden 2'siyle eşleşir, 6 oyuncuda 5 kişiden 2'siyle. Kimse herkes hakkında birinci elden
  bilgi sahibi olmaz; bu dedikoduyu artırır ("Seni de soydu mu?").
- Kriz her maçta sabittir. Maçlar birbirine benzemesin diye her türde en az 2 görev olmalı.
- İleride: 5 görev + Kriz'lik bir "Uzun mod".

**[KARAR 2026-11-24] En az 4 oyuncu.** Lobide maç, insan + bot toplamı en az 4 dolu koltukla başlar; daha azsa
"Maçı Başlat" düğmesi kapalıdır ve "En az 4 kişi gerekli; bot ekleyebilirsin." yazar.
- *Neden:* 3 kişilik maçta iki ikili görevi de **aynı üçlü** oynar: ortak değişmez, "yeni ortakla ittifak" ve çapraz
  kontrol ("Seni de soydu mu?") ortadan kalkar; herkes her şeyi görür.
- Motor, testler ve LineSim 3 oyuncuyu desteklemeye devam eder (otomatik koşular 3 kişilik maç başlatabilir).

**[KARAR 2026-11-24] Konuşma süresi (lobi seçeneği): Kısa / Normal / Uzun.** Kurucu seçer, varsayılan Normal.
Toplantı tartışmasını, Kriz telefonlarını, final tartışmasını ve savunmayı **×0,7 / ×1 / ×1,5** ile çarpar; başka hiçbir
süreyi ve kuralı değiştirmez.
- *Neden:* aynı süreler her gruba uymaz: tanışık, hızlı konuşan bir grup süreyi doldurmaz; yeni ya da kalabalık bir
  grup pazarlığa yetişemez. Tek bir seçenek, kural eklemeden ikisine de yer açar.

## 5. Final ve kovulma [ÖNERİ]

- **Maç ortasında eleme yok.** Erken elenen oyuncu 15 dakikalık bir parti oyununda boş boş izler. Maç blok
  sayısı dolunca biter.
- Final toplantısı: herkes kısa bir savunma yapar, ardından **gizli oylama** olur.
- Oylamaya en çok suçu olan oyuncular çıkar. "Kimseyi kovma" seçeneği **yok**.
- **[KARAR 2026-11-15] 0 suç olan aday olmaz.** Daha az kişi bu şartı sağlıyorsa adaylar onlardır; kovulan sayısı
  aday sayısını geçmez. Hiç aday yoksa (Kriz 4 suç dağıttığı için olmamalı) kimse kovulmaz ve maç doğrudan sonuçlara
  geçer.

| Oyuncu | Oylamaya çıkan | Kovulan |
|---|---|---|
| 3–4 | En çok suçu olan 2 kişi | 1 |
| 5–6 | En çok suçu olan 3 kişi | 2, sırayla |

- **Sıralı kovulma (5–6 oyuncu):** İlk kovulanın masası açılır ve primi dağıtılır. Bu açılış ikinci oylamayı
  etkiler.
- **Masa mühürü [KARAR]:** Adaylar belli olduğu anda (Kriz sonucu) adayların masası mühürlenir: prim gönderemezler, ama prim alabilirler (oy satın alma mümkün kalır). Yoksa kovulacağını anlayan aday tüm primini bir arkadaşına aktarıp oy verenleri eli boş bırakabilirdi.
- **Masa açılışı:** "Ahmet'in çekmecesinden 23 prim çıktı!" Herkes doğru kişiyi kovup kovmadığını o an görür.
- **[KARAR 2026-11-24] Prim beyanı.** Adaylar belli olunca (Kriz sonucu) her aday **bir kez**, kaç primi olduğunu
  beyan eder (0–99); **yalan söyleyebilir**. Beyan, Kriz sonucundan final tartışmasının ilk 12 saniyesinin sonuna kadar
  verilir ve **mühürlüdür**: aday sadece kendi beyanını görür. Bütün beyanlar **aynı anda** açılır: final tartışması
  başlarken (herkes beyan ettiyse), son aday beyan ettiği an ya da en geç süre dolunca. Beyan etmeyen "BEYAN YOK"
  görünür. Beyanlar maçın sonuna kadar (5–6 kişide ikinci kovma turu dahil) herkese açık kalır; değiştirilemez.
  - Kovulan adayın masası açılırken beyanı çıkan primin yanına yazılır ve damgalanır: "16 PRİM · Beyanı: 7 prim" ve
    üstüne **YALAN** ya da **DOĞRU** damgası.
  - **Prim, suç ve oylar üzerinde mekanik etkisi yoktur.** Beyan sadece bilgidir (ya da dezenformasyondur).
  - *Neden:* gizli bakiye oyunun temel belirsizliğiydi ama oynanabilir bir hamlesi yoktu: "Bence en zengin Ahmet"
    tartışması tahminden ibaretti. Beyan, gizli bakiyeyi **defter tutmadan oynanabilen kısa bir yalan-ve-açığa-çıkma
    döngüsüne** çevirir: aday az söyleyerek hedef olmaktan kaçmayı dener, oy verenler inanıp inanmamaya karar verir,
    masa açılınca herkes gerçeği görür. Maçlar arası itibar da buradan doğar ("Geçen maç da 7 demiştin!").
  - Açılış anında her adayın gerçek primi kaydedilir; damga **o kayda göre** vurulur ve sonuç ekranında bütün adaylar
    için "beyan → gerçek" gösterilir. Beyanlar açıldıktan sonra gelen prim (5–6 kişide ilk kovulanın masasından pay,
    bir havale) doğru beyanı yalana çevirmez: masa o durumda "Beyanlar açılırken: 8 prim" notunu da gösterir.
    Aksi halde ilk kovulana oy verip pay alan dürüst bir aday ikinci turda "YALAN" damgası yerdi; biri de dürüst
    adaya prim gönderip onu yalancı çıkarabilirdi.
- **Final açılışı:** Tüm bakiyeler, **tüm oylar** ve kim kimi soymuş bilgisi açıklanır. Örnek an: "Ahmet benden
  4 prim aldı ve yine de beni kovdu!"
- **[KARAR 2026-11-24] Ödüller ve manşetler (final açılışı).** Sonuç ekranı maçın hikâyesini anlatır; hepsi zaten
  açıklanmış veriden, kural değiştirmeden türetilir (`LineStory`).
  - **Manşetler** (en çok 5; önce her türden bir tane): bozulan anlaşmalar (parayı aldı, yine de cezalandırdı ya da
    aleyhte oy verdi), Müşteri Bonusu ihanetleri, kovulmalar ("Sarı kovuldu: masasından 15 prim çıktı, beyanı 8.
    Yalan!"), Kriz ve maçın en büyük havalesi.
  - **Ödüller** (en çok 4; sabit öncelik sırası; mümkün oldukça kişi başı bir tane): **Yanlış Adam** (beyanı doğruydu,
    yine de kovuldu) · **En Pahalı Yalan** (primini en çok, en az 3 eksik beyan eden aday) · **Satılmış** (parayı
    tuttu, sonra ödeyeni ihbar etti / soruşturdu ya da Kriz'de ya da finalde ona oy verdi) · **Sözünün Eri** (sus
    payını aldı ve gerçekten sustu) · **Soyguncu** (Müşteri Bonusu'nda paylaşan ortağından hepsini aldı) ·
    **Kasa Faresi** (kasadan tek başına aldı, ihbar edilmedi) · **Ayın İspiyoncusu** (en çok ihbar + soruşturma) ·
    **Günah Keçisi** (Kriz suçunu tek başına aldı) · **Kara Kutu** (kayıt sızdırdı) · **Tutanak Kralı** (yaramazlıktan
    tutanak yedi) · **Nöbet Kaçağı** (dolu nöbeti tutmadı, parayı cebe attı, kovulmadı; birden çoksa en zengini) ·
    **Temiz Sicil** (0 suçla bitirdi, kovulmadı; birden çoksa en zengini).
  - Projektörde sıralamadan sonra "ÖDÜLLER" slaytı gelir; klasörün Dosya sekmesi "Manşetler" ile açılır; herkes kendi
    unvanını sonuç çubuğunda görür ("Senin unvanın: SATILMIŞ"). Botlar ödüllerine birer cümleyle tepki verir.
  - *Neden:* §1'deki hedef "her maç kişisel bir hikâyeyle biter ve rövanş isteği doğurur". Tam açılış bunu veriyordu
    ama okunması gereken bir tabloydu. Unvan ve manşet, o tabloyu **rövanş hikâyesine** çevirir ("Bir daha oynayalım,
    bu sefer Satılmış sen ol").
- **Beraberlik kuralları [KARAR, prototip]:**
  - Adaylıkta: suçta son aday sırasını paylaşan herkes aday olur (ör. 4 kişide 3 aday).
  - Final oylamasında: berabere kalanlar arasında 15 sn yeniden oylama → yine berabereyse suçu çok olan kovulur →
    suçlar da eşitse primi çok olan kovulur ("Müdür en yüksek maaşı keser"). Şans yok, her zaman biri kovulur.
  - Kazanmada: primi eşit olanlar birlikte kazanır.

## 6. Soruşturma Hakkı [KARAR 2026-11-15]

**Tasarım fikri (v5.1).** Prim = para = puan, en zengin kazanır. Suç = risk: kovulma adayları en çok suçu olanlardır,
kovma oylamasıyla olur ve kovulanın masası ona oy verenlere gider. Açgözlü hamle parasını tutar; bedeli suç riskidir.
Cezalandırmak (soruşturma, ihbar) **bedava ve kazançsızdır**: cezalandıranın kazancı dolaylıdır (rakip aday olur,
kovulursa masasını oy verenler paylaşır). Cezalandırmayı gerçek bir karar yapan **susma pazarlığıdır**: "Sus, sana 1
prim göndereyim." Telefonda yapılır, havaleler bağlayıcı değildir.

- Her oyuncunun maç başına **1 soruşturma hakkı** vardır; 1. toplantının dosya ve tartışma bölümünde açıktır.
- **Sadece paylaşan soruşturabilir**, o da sadece Müşteri Bonusu grubunda **hepsini alanı**. Alan kimseyi
  soruşturamaz; iki alan birbirini de soruşturamaz. **Haksız soruşturma yoktur:** arayüz sadece geçerli hedefleri
  gösterir, motor diğerlerini reddeder (`NotEligible`).
- **Bedava:** maliyet yok, iade yok. Alan 5 primi tutar.
- Alan **+4 suç** alır. Dosya satırı herkese açıktır (kim kimi soruşturdu, almış).
- Üçlüde iki paylaşan aynı alanı soruşturamaz (ilk soruşturma geçer); iki alanlı bir üçlüde paylaşan birini seçer.
- Ortaya çıkan stratejiler:
  - Mağdurun kararı: "Soruşturayım mı, susma parasını mı alayım?" Soruşturma bedava ama para getirmez; susmak
    1 prim getirir ama rakibi temiz bırakır.
  - Alanın kararı: "Ona 1 prim göndereyim mi?" Söz bağlayıcı değil: parayı alıp yine de soruşturabilir.
  - Blöf: "Soruşturma açacağım" diyen biri gerçekten açacak mı? Kayıt cihazı bu konuşmayı kanıta çevirebilir.
- v4'teki kanıt pazarı (satın alınan denetimler) **tamamen kalkar.** Kesin kanıt sadece soruşturmadır, yarı kanıt
  ise kayıt sızıntısıdır.

**Neden böyle? (reddedilen seçenekler)**
- **Eski ihbar (+2 ödül, hırsız parayı kaybeder):** ihbar bedava kâr, almak her zaman kayıptı; tanık için tek doğru
  hamle vardı, ortada karar kalmıyordu.
- **"Hırsız parayı tutar + ihbara +1":** hâlâ otomatik; ihbar etmemek için sebep yok.
- **İhbar edene +1 suç:** yüzeysel bir yama; ihbar etmek yine her zaman doğru hamle olarak kalıyor.
- **Cezalandırmanın 1 prim bedeli:** mağdurun zaten düşük olan şansını öldürüyor. Kararı yaratan bedel değil, susma
  pazarlığıdır.
- **Yakalanan alana kısmi ceza (para kesme):** paylaşmak baskın strateji olur, almak hiç denenmez.
- **Nöbet tutana +1 prim:** herkes nöbet tutar, görev seçim olmaktan çıkar.
- **Alan sayısını gizlemek:** reddedildi; "kaç kişi aldı" toplantının tartışma malzemesidir.
- **Simülasyon kanıtı** (karar öncesi karalama simülasyonu, durum hesaplayan "akıllı" botlarla): zamanlı ya da karışık
  oynayan bot 4–6 kişide adil payın ≈ 2 katı kazanır; saf Açgözlü %81–98 kovulur. Herkesin hesap yaptığı masalarda
  kovulan oyuncu 4/5/6 kişide %88/%87/%63 oranında suç lideridir (eski kurallarda %100/%95/%91: kovulan hep belliydi).
  Nöbette gönüllülerin −1 suç temizliği seçimi otomatik olmaktan çıkarır (eski kurallarda akıllı botların %74–88'i nöbet
  tutuyordu). Oyunun kendi botlarıyla güncel sayılar: `Tools/LineSim/v5-sim.txt` (`V5_ENGINE_SPEC.md` §21).

## 7. Kayıt cihazı ve oy satın alma [KARAR, sayılar ÖNERİ]

- **Kayıt cihazı:** Her oyuncunun gizli bir ses kayıt cihazı vardır, maç başına 1–2 kullanımlık. Yaptığın bir
  görüşmeyi gizlice kaydedip toplantıda 7 saniyesini **sızdırırsın**.
  - Her telefonda "Bu adam beni kaydediyor mu?" sorusu oluşur.
  - Bağlamından koparılmış kayıt, "Ben onu öyle demedim!" tartışmasını garanti eder.
  - Kayıt hem saldırı ("Bu işi ben hallederim" diyen ses) hem savunma ("Ben sana söylemiştim") silahıdır.
  - v4'teki kayıt altyapısı (sadece host belleğinde, diske yazılmaz, lobi seçeneği) korunur.
- **Oy satın alma:** Oyuncular prim vererek başkasının oyunu isteyebilir. Oylama gizli olduğu için rüşveti
  alanın sözünü tutup tutmadığı ancak final açılışında ortaya çıkar.

## 7.1 İletişim, sözler ve hat panosu

**Sözler bağlayıcı değil [KARAR].** Sistem hiçbir anlaşmayı zorlamaz. "Oy veririm", "öderim" yalan olabilir.
Güvenceyi oyuncular kendileri arar:
- **Kim önce öder?** İki taraf da riskte, bu başlı başına bir pazarlık.
- **Kayıt cihazı:** "Sözünü tutmazsan bu kaydı toplantıda çalarım."
- **Final açılışı:** Tüm oylar ve transferler açıklanır. Yalan maç içinde işe yarayabilir ama maç sonunda ortaya
  çıkar ve maçlar arası itibar oluşur.
- [İLERİDE] **Emanet:** "Ahmet Kriz suçunu alırsa 3 primim Zeynep'e geçsin." Sistem sonucu kontrol eder, oyları
  kontrol edemez. İlk prototipte yok.

**Telefon [KARAR]:**
- Sadece 1'e 1 görüşme. Aynı anda tek kişiyle konuşulur. Kimi arayacağını seçmek de bir stratejidir.
- Meşgul hattı: bekle ya da başkasını ara.
- Görüşme ekranında hızlı transfer: "Prim gönder: 1 / 2 / 3 / 5".

**Hat panosu [KARAR, kapsam ÖNERİ]:**
- Kimin kiminle konuştuğu ve ne kadar süredir konuştuğu herkese görünür, ne konuştukları görünmez. Örnek:
  "Ayşe ↔ Mehmet, 40 sn".
- [ÖNERİ] Sadece Kriz'de değil, **bütün ofis aşamalarında** açık. Kurallar tutarlı kalır ve görevlerde de şüphe
  üretir.
- [ÖNERİ] Toplantı dosyasında o bloğun görüşme özeti: "Ayşe → Mehmet: 3 görüşme".

**Mesajlar [ÖNERİ]:** Gizli kalır, panoda görünmez. Bu bir ikilem yaratır:
- Telefon: hızlı ve ikna edici, ama herkes konuştuğunu görür.
- Mesaj: kimse görmez, ama yavaştır.

## 8. Görevler

### 8.1 Tasarım ilkeleri [KARAR]

- Kısa olmalı (60–90 sn) ve **tek bir karar** içermeli. Hesap ya da bulmaca olmamalı.
- Her görevde bir **ayartma** olmalı: bencil seçim prim kazandırır ve belirli bir kişiye zarar verir.
- Sadece mağdur bilmeli. Diğerleri ya hiçbir şey bilmemeli ya da "biri yaptı" kadarını bilmeli.
- Konuşmadan iyi sonuç alınamamalı.
- Her görev ihanetin **farklı bir türünü** temsil etmeli.

### 8.2 Kabul edilen görevler

**Müşteri Bonusu (ikili)** [KARAR]: v4'teki `bonus_pool` görevinin yeni hali.
- Her ikili gizlice **Paylaş** ya da **Hepsini al** seçer.
- **[KARAR 2026-11-15]** İkisi paylaşırsa **3 / 3** (eskiden 2 / 2); biri alırsa alan 5, paylaşan 0; ikisi alırsa
  1 / 1. Üçlüde kimse almazsa herkes **3** (eskiden 2); tek alan 5, diğerleri 0; iki ya da daha fazla alan varsa herkes
  1. Herkese açık "N kişi hepsini aldı" satırı ve görevin başarısız sayılması değişmedi. Paylaşan mağdur, alanı 1.
  toplantıda bedavaya soruşturabilir (§6).
- Sadece ortak, diğerinin ne seçtiğini görür. Herkese sadece "kaç kişinin hepsini aldığı" gösterilir.
- Başarısızlık: biri "hepsini al" dediyse müşteri ilişkisi zarar görür ve suç oluşur.
- Üçlüde biri alırsa **iki mağdur** olur; iki kişinin aynı kişiyi suçlaması daha ikna edicidir, yani üçlüde çalmak
  daha riskli. Ödemeler: §12.

**Küçük Kasa (ikili)** [KARAR]: ihbar ve şantaj görevi.
- İkiliye ortak bir küçük kasa emanet edilir. Gruptaki **herkes** gizlice kasadan **+3 prim** alabilir. Hak tek
  kişiye verilmez: verilirse diğeri sadece bekler ve hak sahibi baştan şüpheli olur.
- Görev sonunda grup içinde açılış olur: herkes ortağının alıp almadığını görür. Ortak, mağdur değil **tanıktır**,
  çünkü çalınan şirketin parasıdır.
- **İki ya da daha fazla kişi birden alırsa** kasa tamamen boşalır, müdür hemen fark eder ve hepsine suç yazılır.
  Bu kural olmasa herkes çalar ve birbirini ihbar edemez. Kuralın sonuçları:
  - Konuşmak zorunlu olur: "Sen mi alacaksın, ben mi?"
  - Ortaklık doğar: "Ben alayım, sana 1 prim veririm, sen de sus."
  - Ortaklık ihanete açıktır: parayı alıp yine de ihbar etmek ya da anlaşmaya rağmen gizlice çalmak.
- **[KARAR 2026-11-15]** Grubunda tek alan **+3 prim alır ve ihbar edilse bile tutar** (eskiden ihbar edilince
  kaybediyordu). **İhbar bedava ve ödülsüzdür** (eskiden +2): hırsız +3 suç alır, ilk ihbar geçerlidir. İki ya da daha
  fazla alan yakalanır: prim yok, kişi başı +2 suç (değişmedi). Çözülmemiş hırsızlık: kasa açığı satırı, görev
  başarısız, 1. toplantıda suç oylaması (değişmedi).
- Tanığın 3 seçeneği:

| Seçenek | Kazanç | Bedel |
|---|---|---|
| **Sus** | Suçlu sana borçlu kalır | Hiçbir şey kazanmazsın; rakip temiz kalır |
| **İhbar et** | Rakip +3 suç alır: aday olabilir, kovulursa masası oy verenlere | Para kazandırmaz; herkes ihbar ettiğini görür, düşman kazanırsın |
| **Susma parası al** | Telefonda gizli prim ("Sus, sana 1 prim göndereyim.") | Söz bağlayıcı değil; parayı alıp yine de ihbar edebilirsin, suçlu da seni kaydedebilir |

- **İhbar kesin kanıttır.** Sistem gerçeği bilir: suç hırsıza yazılır; parası onda kalır, tanık bir şey kazanmaz.
- İhbarın görünür olması Kriz'deki "görünür zenginlik" sorunu değildir: Kriz'de bedel herkese zorla geliyordu,
  burada tanık bu bedeli kendisi seçer. Gizli kalmak isteyen susar ya da şantaj yapar.
- Toplantıda müdür sadece toplamı açıklar: "Kasalardan 3 prim eksik çıktı." İhbar yoksa suç oylanır ve çalan,
  suçu masum birine atmaya çalışabilir.
- Üçlüde tek bir kişi alabilir (iki kişi alırsa yakalanırlar). Diğer ikisi hem tanık hem de pay isteyen konumdadır.
  Sadece ilk ihbar sayılır.
- Akış: görev (≈ 60 sn, seçim) → grup içi açılış → mola (≈ 30 sn, ihbar/sus kararı ve şantaj telefonları) →
  toplantı.
- [ÖNERİ] İhbar kararı prototipte görev sonundaki molada verilir. [İLERİDE] **İhbar kozu:** hak maç sonuna kadar
  elde durur, her tur şantaj yapılabilir, finalden hemen önce kullanılabilir. Suç bu durumda masum birinden gerçek
  suçluya aktarılır.
- Ödül ve suç miktarları: §12.

**Kriz: Günah Keçisi (herkes)** [KARAR]
- **Maç başına bir kez**, son blokta, final toplantısından hemen önce gelir. Suç tahtası belliyken dengeleri
  altüst etmek için tasarlandı.
- Müdür: "Büyük müşteri şikâyet etti, biri bunu üstlenecek." İlk sıraları değiştirebilecek kadar **büyük bir suç
  puanı** ortaya çıkar.
- **Suçu kimin alacağına herkes gizli oyla karar verir.** Oylamadan önce 60–90 sn **özel telefon** pazarlığı
  olur: "Sen Ahmet'e oy ver, ben sana 3 prim vereyim."
- Suçu alan kişi kimseden para **almaz**. Suç onu aday yapar ama zengin yapmaz. Kimin kime ne ödediği gizli kalır.
- Kriz suçunun tamamı en çok oyu alana yazılır. Beraberlikte suç berabere kalanlar arasında eşit paylaşılır
  [KARAR, prototip].

**Neden böyle? (reddedilen ilk sürüm)** İlk sürümde biri Kriz'i gönüllü üstleniyor, diğerleri ona açıkça prim
ödüyordu. Sorun: herkes üstlenen kişinin hem aday hem zengin olduğunu biliyordu. Kazanan en zengin olduğu için oy
verenler rakibi elemek ister ve bu kişiyi kovmak otomatik karar haline gelirdi. Yani kimse üstlenmezdi.
"Tazminat" (alınan prim kovulunca da kalır) bunu çözmedi, çünkü sorun parayı almak değil rakibi elemek. Kural:
**Kriz, gizli olması gereken zenginliği görünür yapmamalı.**

**Lideri devirme:** Kazanan en zengin olduğu sürece oyuncular en büyük rakibi elemek ister. Bu yok edilemez ve
edilmemeli de. Savunmalar: bakiyeler gizli (§3) ve oylamaya sadece suçu en çok olanlar çıkar (§5). Liderin kim
olduğu belirsiz kaldıkça bu, istediğimiz tartışmayı üretir: "Bence en zengin Ahmet." / "Hayır, Ayşe."

**Hafta Sonu Nöbeti (grup, herkes)** [KARAR]: prototipin grup görevi.
- Oyuncuların yarısı kadar nöbetçi lazım (4 kişide 2, 5 kişide 3, 6 kişide 3).
- Herkes gizlice **"Nöbet tutarım"** ya da **"Tutmam"** seçer.
- Nöbet tutmayan **+2 prim** alır (hafta sonu ek iş). Nöbet tutan bir şey almaz.
- **Yeterli nöbetçi çıkmazsa** görev başarısız olur ve Toplantı 2'de suç oylaması yapılır. Yeterliyse Toplantı 2
  atlanır.
- **[KARAR 2026-11-15, yukarıdaki iki maddenin yerine]** Nöbet tutan prim almaz ama **suçu 1 azalır** (0'ın altına
  inmez), liste dolsa da dolmasa da; görev açıklanınca uygulanır. Liste dolarsa (nöbetçi ≥ gereken) tutmayan +2 prim
  alır ve Toplantı 2 atlanır. **Liste dolmazsa tutmayan prim almaz ve +2 suç alır**; suç Toplantı 2 açılınca
  kendiliğinden yazılır. Toplantı 2'de bu yüzden **oylama yoktur**: dosya ve tartışma olur ("Nöbet tutmayan N kişiye
  otomatik +2 suç yazıldı"). Gereken nöbetçi ceil(N/2) (değişmedi).
- **[KARAR 2026-11-24, Toplantı 2 kaldırıldı]** Yukarıdaki "Toplantı 2" cümlelerinin yerine: liste dolmazsa tutmayanın
  +2 suçu **nöbet açıklandığı anda** yazılır ve maç doğrudan Kriz'e geçer (§4.1). Nöbet tutanın −1 suçu da aynı anda
  işler. Ceza ofiste nöbet çizelgesinde, suç tahtasında ve Kriz oylaması açılırken projektörde ("NÖBET CEZASI") görünür.
- **Nöbet listesi herkese açık.** İhanet açıkta yapılır.
- Neden işe yarar:
  - Ayartma net: tutmayan +2 kazanır, herkes başkasının tutmasını ister.
  - Risk net: nöbetçi yetmezse listede görünen tutmayanlar suç alır (v5.1: oylamasız, hepsi birden).
  - Temizlik net (v5.1): suçu olan, nöbet tutarak 1 suç siler; seçim otomatik olmaktan çıkar.
  - Pazarlık: "Kim tutacak?" Sözünü tutmayan herkesin önünde belli olur: "Bana söz vermiştin!"
  - Satın alma: "Benim yerime nöbet tut, sana 2 prim veririm."
  - Fazla nöbetçi çıkarsa tutanlar boşuna fedakârlık etmiş, diğerleri +2'yi almış olur.
- Üç görev üç farklı bilgi durumu sunar: Müşteri Bonusu'nda sadece mağdur, Küçük Kasa'da sadece tanık, Nöbet'te
  herkes bilir.

### 8.3 Aday görevler [AÇIK]

İkili görevler için Müşteri Bonusu ve Küçük Kasa, grup görevi için Nöbet prototipe yeterli. Maçların birbirine
benzememesi için ileride her türde 3–4 görev hedefleniyor (§4.1). Grup görevi adayları:

| Görev | İhanet türü | Kim bilir? |
|---|---|---|
| **Bütçe Paylaşımı** (herkes): Ortak bütçeden iste. Toplam aşarsa herkes kesintiye uğrar | Anonim açgözlülük | Kimse. Sadece toplam görünür |

> **Bütçe'nin teşvik sorunu (düzeltilmeli):** Sadeleştirilmiş hali (bütçe = 2N, herkes 0–5 ister, aşarsa herkes
> 0 alır) çalışmıyor: bütçe tam adil paya eşit olduğu için tek bir fazla istek herkesi, isteyeni de, sıfırlar.
> Fazla istemek ancak başkası az isterse işe yarar; yani gerçek bir ayartma yok. v4'te bunu gizli ihtiyaçlar ve 2
> birimlik pay boşluğu çözüyordu. Tekrar denenecekse önce bu düzeltilmeli.
| **Zam Listesi** (herkes): Oyuncuların yarısı zam (+3) alacak. Herkes gizlice kendisi hariç 2 kişiye oy verir | Koalisyon, oy ticareti | Sadece kimin zam aldığı ve oy sayıları. "Ben sana oy verdim!" kanıtlanamaz. Final oylamasının provası |
| **Çekmece Araması** (herkes): Herkes ya birinin çekmecesine gizlice yasak eşya koyar ya da kendi çekmecesini kilitler. Kilitsiz çekmecede eşya çıkarsa sahibi suç alır; kilitliye koymaya çalışan yakalanır | Komplo kurmak | Mağdur yapmadığını bilir ama kimin koyduğunu bilmez. Masum suç almaz değil, ama bunu bir oyuncu yapar ve mağdur kilitleyerek korunabilirdi |
| **Kayırmacı Şef** (rol): Şef 2 iyi projeyi (+3) dağıtır, diğerleri kötü projeyi alır. Herkes lobi yapar, rüşvet teklif eder | Torpil | Kimin iyi proje aldığı açık, şefin nedeni gizli |
| **Müfettiş** (rol): Müfettiş bir kişinin önceki görevdeki seçimini gizlice görür; toplantıda doğruyu ya da yalanı söyler veya bilgiyi satar | Söylenti gücü | Sadece müfettiş. Yalanı çürütmenin tek yolu soruşturma hakkı |

İkili görev için ek aday:

| Görev | İhanet türü | Not |
|---|---|---|
| **Ortak Sunum** (ikili): Müdür ikiliye ayrı ayrı "Bu işi kim yaptı?" diye sorar. İkisi "ikimiz" derse 2/2, biri "ben" derse 4/0, ikisi "ben" derse ikisi de suç alır | Emek hırsızlığı | Yapısı Müşteri Bonusu'na çok benziyor; eklenecekse farklılaştırılmalı |

Yukarıdaki son dört grup görevi ve Ortak Sunum 2026-11-10 tartışmasında yeni öneri olarak eklendi, henüz
değerlendirilmedi.
| **Masraf Onayı** (onaylayıcı): Herkes masraf beyan eder ve şişirebilir. Onaylayıcı hepsini görür | Bilgi gücü, şantaj | Sadece onaylayıcı |
| **İçeriden Bilgi** (bilgi sahibi): En iyi tedarikçiyi biri bilir. Rakip firma ona yalan söylemesi için rüşvet verir | Yalan söyleyerek yanıltma | Ona soranlar, sonuç gelince |

Yeni görev fikirleri buraya eklenecek.

## 9. v4'ten kalkanlar ve henüz konuşulmayanlar

**Kalkıyor:** kariyer puanı, Ayın Çalışanı, kanıt pazarı, telafi raporu, ara toplantılardaki ihtar oylaması,
"Kimseyi kovma" seçeneği, 12 görevlik havuz.

**Korunuyor:** birinci şahıs ofisler, 1'e 1 telefon, 7 saniyelik kayıt altyapısı, metin alternatifi, anonim
şikâyetler, müdürler, ofis komedisi.

**Henüz konuşulmadı [AÇIK]:**
- Şirket hedefi: kalsın mı? Herkes açgözlü olursa herkes kaybetsin mi?
- Gizli hedefler/gündemler: kalsın mı, sadeleşsin mi? Kovulup gizli gündemini tamamlayana tazminat?
- ~~Kirli oyunlar (kahve dökme, fişi çekme, sahte e-posta, telefon dinleme): hangileri kalır?~~ Cevaplandı
  (2026-11-13): toplantı odasında **Yaramazlıklar**, bkz. `V5_YARAMAZLIKLAR.md`.
- Terfi: kaldırılsın mı, yoksa "Takım Lideri" (sonraki bloğu yönetir, ilk suç ona yazılır) mı olsun?
- Maç süresi ve blok sayısı (hedef: 12–15 dk).
- Ses zorunlu mu, metin alternatifi kalsın mı?

## 10. Açık sorular listesi

Prototip için gereken eksikler (toplantıda suç dağıtımı, grup görevi, sayılar, beraberlik kuralları) 2026-11-10'da
kapatıldı. Kalanlar:

1. Kovulma sayısı ve aday sayısı tablosu (§5) hâlâ [ÖNERİ]; prototipte doğrulanacak.
2. Prototipten sonra ek görevler: §8.3'teki adaylar ve 3. bir ikili görev (yeni bir ihanet türü getirmeli).
3. §9'daki konuşulmamış konular.

## 11. Doğrulama planı

Kodlamadan önce bu çekirdek, **4 arkadaşla Discord'da kâğıt prototip** olarak oynanacak: görevler DM ile
dağıtılır, prim ve suç bir tabloda tutulur, "kayıt" Discord'un ses kaydıyla taklit edilir. Bakılacaklar:
tartışma çıkıyor mu, oylar neye göre veriliyor, maç sonunda "bir daha" deniyor mu?

## 12. Prototip sayıları [KARAR 2026-11-15: v5.1, test edilecek]

İlk değerler (v0) kâğıt prototip ve bot simülasyonuna göre değişti; 2026-11-15'te v5.1 kuralları karara bağlandı
(§6, §8.2; reddedilen seçenekler §6 "Neden böyle?"). **[KARAR 2026-11-24] v5.2:** prim beyanı (§5), Toplantı 2'nin
kalkması, en az 4 oyuncu ve konuşma süresi seçeneği (§4.1), ödüller ve manşetler (§5); prim ve suç sayıları değişmedi.
Sayıların hepsi `LineConfig`'te (`StreamingAssets/Config/line_config.json`, `ConfigVersion` 8); her alanın yorumu eski
(v5.0) değerini de söyler. Kural kimliği `v5.2-kayit` (v5.1 ve v5.0 verileriyle birleştirilmez).

### Prim

| Kalem | Değer (v5.1) | v5.0 |
|---|---|---|
| Başlangıç primi | **6** | 6 |
| Müşteri Bonusu (ikili) | İkisi paylaşırsa **3 / 3**. Biri alırsa alan 5, diğeri 0. İkisi alırsa 1 / 1 | 2 / 2 … |
| Müşteri Bonusu (üçlü) | Kimse almazsa herkes **3**. Biri alırsa o 5, diğerleri 0. İki ya da daha fazla kişi alırsa herkes 1 | herkes 2 … |
| Küçük Kasa | Grubunda tek alan +3 ve **ihbar edilse de tutar**. İki kişi alırsa ikisi de yakalanır ve hiçbir şey kazanmaz | ihbar edilen 3'ü kaybeder |
| Kasa ihbar ödülü | **0** (ihbar bedava ve ödülsüz) | +2 |
| Hafta Sonu Nöbeti | Liste dolarsa tutmayan +2; **dolmazsa tutmayan 0**; nöbet tutan 0 | tutmayan hep +2 |
| Soruşturma maliyeti / iadesi | **0 / 0** (bedava; alan 5'i tutar) | 4 / haklıysa 3 |

### Suç

| Kalem | Değer (v5.1) | v5.0 |
|---|---|---|
| Toplantı oylaması | Başarısız görev başına herkesin 1 oyu, her oy 1 suç (tek toplantı; v5.2'de Toplantı 2 yok) | 2. toplantıda da |
| İhbar edilen kasa hırsızı | +3 suç, çaldığı 3 prim **onda kalır** | +3, 3'ü geri verir |
| İki kişi birden kasadan alırsa | İkisine de +2 suç (oylama yapılmaz) | aynı |
| Soruşturma | Sadece paylaşan, hepsini alanı: alan **+4 suç** (haksız soruşturma yok) | +3 ve 3 prim öder; haksızsa açana +3 |
| Nöbet tutan | **−1 suç** (0'ın altına inmez), liste dolsa da dolmasa da | yok |
| Eksik nöbette tutmayan | **+2 suç**, nöbet açıklandığı anda, oylamasız (v5.1: Toplantı 2 açılınca) | oylama |
| Kriz | 4 suç, gizli oyla en çok oyu alana; beraberlikte eşit paylaşılır | aynı |

### Diğer

| Kalem | Değer |
|---|---|
| Soruşturma hakkı | Maç başına 1 |
| Kayıt cihazı | Maç başına 1 kullanım (4 kişide en fazla 4 sızıntı; az olduğu için her biri değerli) |
| Aday olmak | En az **1 suç** (v5.1; eskiden 0 suç da aday olabiliyordu). Kovulan sayısı ≤ aday sayısı; aday yoksa kovulan yok |
| Prim beyanı (v5.2) | Aday başına 1 kez, 0–99; Kriz sonucundan final tartışmasının 12. saniyesine kadar; etkisi yok (`DeclarationsEnabled`, `DeclareSeconds`, `DeclareMax`) |
| Toplantı 2 (v5.2) | Yok (`ShiftMeeting` kapalı); eski akış için açılabilir |
| Konuşma süresi (v5.2) | Kısa ×0,7 · Normal ×1 · Uzun ×1,5: toplantı tartışması, Kriz telefonları, final tartışması, savunma (`TalkTimeShort`, `TalkTimeLong`) |
| En az oyuncu (v5.2) | Lobide 4 dolu koltuk (insan + bot); motor 3'ü destekler |
| Ödüller / manşetler (v5.2) | En çok 4 ödül, en çok 5 manşet; beyan yalanı en az 3 prim eksikse "En Pahalı Yalan" |

### Sağlama (4 oyuncu, kaba, v5.1)

- Dürüst oyuncu: 6 + 3 (Bonus) + 0–2 (Nöbet) ≈ **9–11 prim**; nöbet tutarsa 1 suç siler.
- Açgözlü oyuncu: 6 + 5 + 3 + 2 ≈ **16 prim**, ihbar ve soruşturmadan sonra da (para onda kalır); bedeli suçtur:
  soruşturma +4, ihbar +3, eksik nöbet +2.
- Açgözlü oyuncu kovulursa ona oy veren 3 kişi kişi başı ≈ **5 prim** alır. Kovmak için güçlü bir sebep; kovmak için
  de önce aday olması (suç) gerekir.
- Mağdur ve tanık için ceza bedava ama kazançsız; asıl soru susma parasıdır ("Sus, sana 1 prim göndereyim.").

Bot simülasyonunun güncel sayıları: `Tools/LineSim/v5-sim.txt` (açıklaması `V5_ENGINE_SPEC.md` §20, v5.1 için §21, v5.2
için §22: beyan ve ödül satırları).
2026-11-15'ten beri botlar her oylamada kazanmak için en zengin olduğunu tahmin ettikleri rakibe oy verir (eski "bildiği
suçlu, yoksa en yüksek suç" kuralının sayıları için LineSim `--vote blame`); cezalandırmayı susma pazarlığına göre
yaparlar (`V5_BOT_TALK.md` §4.1). v5.0 kurallarıyla sayılar: LineSim `--rules old`.
