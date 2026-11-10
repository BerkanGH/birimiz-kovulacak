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
- Başarısız olan görevler suç puanı üretir. Her toplantı bir mahkemedir: **"Bu kimin suçu?"** Oylanır ve suç o
  kişiye yazılır.
- Kesinleşen bir soruşturma (§6) ve Kriz (§8.2) de suç puanı yazar.
- [ÖNERİ] Toplu yüklenmeye karşı fren: bir oyuncu tek toplantıda en fazla X suç puanı alabilir.
- Suç puanı "kim aday olacak" sorusunu, prim ise "kime oy vereceğim" sorusunu belirler. Masum ama zengin biri
  ile suçlu ama fakir biri aday olunca oy verenler gerçek bir ikilemde kalır.

## 4.1 Maç yapısı [ÖNERİ]

Oyuncu sayısı ne olursa olsun **3 normal görev + 1 Kriz**. Oyuncu sayısı arttıkça görev değil, toplantı ve final
süresi uzar.

```
Görev 1 → Toplantı (suç dağıtılır)
Görev 2 → Toplantı (suç dağıtılır)
Görev 3 → Toplantı (suç dağıtılır)
KRİZ   → büyük suç gizli oyla birine yazılır, suç tahtası sarsılır
FİNAL  → en çok suçu olanlar oylamaya çıkar → kovulma
```

| Oyuncu | Normal görev | Kriz | Kovulan | Tahmini süre |
|---|---|---|---|---|
| 4 | 3 | 1 | 1 | ≈ 13–14 dk |
| 5 | 3 | 1 | 2 | ≈ 15–16 dk |
| 6 | 3 | 1 | 2 | ≈ 17–18 dk |

- Bir blok ≈ 3 dk: ≈ 90 sn görev ve telefon, ≈ 90 sn suç toplantısı.
- 4 oyuncuda 3 ikili tur ile herkes herkesle tam bir kez eşleşir. 6 oyuncuda herkes 5 kişiden 3'üyle eşleşir;
  eksik birinci el bilgi dedikoduyu artırır.
- 3 görev farklı türlerden seçilir: bir **ikili**, bir **herkesin katıldığı**, bir **rol** görevi. Görev havuzunda
  en az 5–6 görev olmalı. Kriz her maçta sabittir.
- İleride: 5 görev + Kriz'lik bir "Uzun mod".

## 5. Final ve kovulma [ÖNERİ]

- **Maç ortasında eleme yok.** Erken elenen oyuncu 15 dakikalık bir parti oyununda boş boş izler. Maç blok
  sayısı dolunca biter.
- Final toplantısı: herkes kısa bir savunma yapar, ardından **gizli oylama** olur.
- Oylamaya en çok suçu olan oyuncular çıkar. "Kimseyi kovma" seçeneği **yok**.

| Oyuncu | Oylamaya çıkan | Kovulan |
|---|---|---|
| 3–4 | En çok suçu olan 2 kişi | 1 |
| 5–6 | En çok suçu olan 3 kişi | 2, sırayla |

- **Sıralı kovulma (5–6 oyuncu):** İlk kovulanın masası açılır ve primi dağıtılır. Bu açılış ikinci oylamayı
  etkiler.
- **Masa açılışı:** "Ahmet'in çekmecesinden 23 prim çıktı!" Herkes doğru kişiyi kovup kovmadığını o an görür.
- **Final açılışı:** Tüm bakiyeler, **tüm oylar** ve kim kimi soymuş bilgisi açıklanır. Örnek an: "Ahmet benden
  4 prim aldı ve yine de beni kovdu!"
- [AÇIK] Oylamada beraberlik nasıl bozulur? Seçenekler: daha çok suçu olan kovulur ya da müdür karar verir.

## 6. Soruşturma Hakkı [ÖNERİ]

- Her oyuncunun maç başına **1 soruşturma hakkı** vardır ve kullanımı **≈5 prime** mal olur.
- Toplantıda "Ahmet beni Müşteri Bonusu'nda soydu, soruşturma açıyorum" denir. Sistem o görevdeki seçimi
  **herkese** açıklar.
  - Haklıysan: Ahmet büyük suç puanı alır ve kaybettiğin prim iade edilir.
  - İftira attıysan: **suç puanı sana yazılır.**
- Bu mekanizmanın ortaya çıkardığı stratejiler:
  - Gerçek mağdur kesin kazanır ama bedel öder: "5 prim harcayayım mı, sessiz mi kalayım?"
  - Hırsız seçici olur: hakkını kullanmış ya da 5 primi olmayan birini soyar.
  - Susma parası: "Sana 3 prim vereyim, soruşturma açma." Bu konuşma kayda girebilir.
  - Blöf: "Soruşturma açacağım" diyen biri gerçekten açacak mı?
- v4'teki kanıt pazarı (satın alınan denetimler) **tamamen kalkar.** Kesin kanıt sadece soruşturmadır, yarı kanıt
  ise kayıt sızıntısıdır.

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
- Sadece ortak, diğerinin ne seçtiğini görür. Herkese sadece "kaç kişinin hepsini aldığı" gösterilir.
- [AÇIK] Ödeme tablosu ve kaç suç puanı üreteceği.

**Kriz: Günah Keçisi (herkes)** [KARAR]
- **Maç başına bir kez**, son blokta, final toplantısından hemen önce gelir. Suç tahtası belliyken dengeleri
  altüst etmek için tasarlandı.
- Müdür: "Büyük müşteri şikâyet etti, biri bunu üstlenecek." İlk sıraları değiştirebilecek kadar **büyük bir suç
  puanı** ortaya çıkar.
- **Suçu kimin alacağına herkes gizli oyla karar verir.** Oylamadan önce 60–90 sn **özel telefon** pazarlığı
  olur: "Sen Ahmet'e oy ver, ben sana 3 prim vereyim."
- Suçu alan kişi kimseden para **almaz**. Suç onu aday yapar ama zengin yapmaz. Kimin kime ne ödediği gizli kalır.
- [AÇIK] Kriz oylamasında beraberlik kuralı.

**Neden böyle? (reddedilen ilk sürüm)** İlk sürümde biri Kriz'i gönüllü üstleniyor, diğerleri ona açıkça prim
ödüyordu. Sorun: herkes üstlenen kişinin hem aday hem zengin olduğunu biliyordu. Kazanan en zengin olduğu için oy
verenler rakibi elemek ister ve bu kişiyi kovmak otomatik karar haline gelirdi. Yani kimse üstlenmezdi.
"Tazminat" (alınan prim kovulunca da kalır) bunu çözmedi, çünkü sorun parayı almak değil rakibi elemek. Kural:
**Kriz, gizli olması gereken zenginliği görünür yapmamalı.**

**Lideri devirme:** Kazanan en zengin olduğu sürece oyuncular en büyük rakibi elemek ister. Bu yok edilemez ve
edilmemeli de. Savunmalar: bakiyeler gizli (§3) ve oylamaya sadece suçu en çok olanlar çıkar (§5). Liderin kim
olduğu belirsiz kaldıkça bu, istediğimiz tartışmayı üretir: "Bence en zengin Ahmet." / "Hayır, Ayşe."

### 8.3 Aday görevler [AÇIK]

En az 3–4 farklı görev gerekiyor, aksi halde maç tekrara düşer.

| Görev | İhanet türü | Kim bilir? |
|---|---|---|
| **Bütçe Paylaşımı** (herkes): Ortak bütçeden iste. Toplam aşarsa herkes kesintiye uğrar | Anonim açgözlülük | Kimse. Sadece toplam görünür |
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
- Kirli oyunlar (kahve dökme, fişi çekme, sahte e-posta, telefon dinleme): hangileri kalır?
- Terfi: kaldırılsın mı, yoksa "Takım Lideri" (sonraki bloğu yönetir, ilk suç ona yazılır) mı olsun?
- Maç süresi ve blok sayısı (hedef: 12–15 dk).
- Ses zorunlu mu, metin alternatifi kalsın mı?

## 10. Açık sorular listesi

1. Kovulma sayısı ve aday sayısı tablosu (§5).
2. Oylamada beraberlik kuralı: final (§5) ve Kriz (§8.2).
3. Soruşturma maliyeti ve kullanım sayısı (§6).
4. Kayıt cihazı kullanım sayısı (§7).
5. Müşteri Bonusu ödeme tablosu ve suç üretimi (§8.2).
6. 2–3 yeni görev (§8.3).
7. §9'daki konuşulmamış konular.

## 11. Doğrulama planı

Kodlamadan önce bu çekirdek, **4 arkadaşla Discord'da kâğıt prototip** olarak oynanacak: görevler DM ile
dağıtılır, prim ve suç bir tabloda tutulur, "kayıt" Discord'un ses kaydıyla taklit edilir. Bakılacaklar:
tartışma çıkıyor mu, oylar neye göre veriliyor, maç sonunda "bir daha" deniyor mu?
