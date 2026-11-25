# v5 Bot konuşmaları

Durum: **[KARAR]** (2026-11-12, kullanıcı taslağı onayladı; `feature/v5-integration` üzerinde uygulandı). Kurallar
(`V5_DESIGN.md`, `V5_ENGINE_SPEC.md`) ve botların kararları (`LineBot`: Dürüst / Açgözlü / Karışık) değişmez. Bu belge
botların **ne söylediğini, ne zaman aradığını ve sözlerini nasıl tuttuğunu** anlatır.

**Güncelleme (2026-11-15, kullanıcının isteği): botlar kazanmak için oy verir.** Prim = para = puan; en zengin kazanır,
kovulanın masası ona oy verenlere paylaşılır. Bu yüzden her bot (Dürüst, Açgözlü, Karışık) her oylamada (toplantı suç
oyları, Kriz, final kovma oyları) seçenekler arasında **en zengin olduğunu tahmin ettiği rakibe** oy verir. Bakiyeler
gizli olduğu için bot kendi defterini tutar (`LineBotLedger`, sadece kendi görüşündeki bilgiyle; §6.1). Görevlerdeki
davranış (Dürüst paylaşır / yazar, Açgözlü alır / yazmaz, Karışık yarı yarıya), ihbar ve soruşturma aynen kaldı. Botlar
oylarını artık parayla açıklar ve toplantıda kimin zengin olduğunu düşündüklerini söyler (§6, §7). Eski kural
("bildiği suçlu, yoksa en yüksek suç") `LineBotVoteStyle.MostBlame` olarak duruyor (LineSim `--vote blame`); o kuraldaki
botlar eski cümleleri söyler.

**Güncelleme (2026-11-15, v5.1 kuralları): susma pazarlığı.** Ceza (soruşturma, ihbar) artık bedava ve kazançsız;
alan parasını tutar (`V5_DESIGN.md` §6, §8.2). Botların ceza kararları buna göre değişti (§4.1): Dürüst cezalandırabildiği
her zaman cezalandırır ve susma parasını geri gönderir; Açgözlü sadece para alırsa cezalandırır (parayı tutar, yarı
yarıya yine de cezalandırır); Karışık her kişi için ikisinden birini oynar. Alan Açgözlü / Karışık bot, mağdurunu ya da
tanığını molada arayıp 1 prim susma parası gönderir. Görev seçimleri ve oylar değişmedi.

**Güncelleme (2026-11-24, v5.3): Dosya ve Haftanın gündemi.** Tek atımlık soruşturma ve ihbarın yerini kalıcı **dosya**
aldı (`V5_DESIGN.md` §6): bot elindeki dosyayı açar ya da imha eder (§4.3). Susma pazarlığı artık dosya üzerinedir:
"Dosyamı imha et, 1 prim senin." Ödüller maçtan maça değişir (§8.1 "Haftanın gündemi"): Karışık görev seçimlerinde
seviyeye bakar, botlar büyük / küçük haftayı aramada söyler. Aşağıdaki tablolardaki cümleler güncel metinlerdir; §4.1
tek atımlık kuralın (v5.1 / v5.2, `LineConfig.Legacy`) nasıl işlediğini anlatmaya devam eder, bugünkü davranış §4.3'tedir.

Botlar sadece test içindir; gerçek maçlar 3–6 insanla oynanır. Amaç, tek başına test ederken ofisin ve toplantının boş
hissettirmemesi.

## 1. Nasıl çalışır

- **Telefon.** Bot telefonu 1–3 çalıştan sonra açar ya da reddeder (hat panosunda her zamanki gibi görünür). İkili
  görevlerin başında ortağını arayabilir (her seferinde değil), molada mağdur, tanık ya da hırsızsa kendisi arar, Kriz
  pazarlığında bir iki kişiyi arar, nöbet eksik çıkınca (sonuç ekranında vakit varsa) yazmayan birini arar. Meşgul hatta
  bekler, üç denemeden sonra vazgeçer. Görüşmeler kısadır (~15–30 sn; görev süresi kısaltılmışsa orantılı) ve kapanış
  cümlesiyle biter. Bot bota da arar; insanlar arası görüşme hiç değişmedi (sadece ses).
- **Botla görüşmede.** Konuşma balonu telefon panelinin yanında (ahize kalkıkken) ya da arama hapının altında çıkar:
  botun cümleleri daktilo gibi yazılır, her cümlede kısa bir mırıltı (bot başına farklı perde) duyulur. Görevin 2–3 soru
  düğmesi (1 / 2 / 3 tuşları ya da tıklama) ve "Prim gönder: 1 / 2 / 3" satırı vardır. Sorular görev seçimi sırasında ve
  Kriz görüşmelerinde çıkar; molada bot kendi durum cümlesini söyler.
- **Toplantı.** Bot her toplantıda sohbete 1–3 cümle yazar (Kriz/final bölümünde en fazla 4: savunma ve final oyu
  dahil). Cümle botun başının üstünde balon olarak da görünür; bot konuşma animasyonu yapar ve mırıldanır. Suçlanınca
  bir kez cevap verir; kendi kaydı sızınca tepki verir. Final savunmasında savunan botun cümlesi ayaktaki karakterinin
  üstünde altyazı olarak çıkar (görüş dışındaysa ekranın altında) ve sohbete de düşer. Final oyundan önce oyunun nereye
  gittiğini söyler.
- **Doğruluk.** Cümle, botun gerçekte yapacağı (ya da yaptığı) seçimle karşılaştırılarak seçilir:
  - **Dürüst hiç yalan söylemez.** Söylediği her seçim, oy ve söz doğru çıkar.
  - **Açgözlü** kendi seçimi hakkında yalan söyler (her zaman alır / yazmaz ama "paylaşıyorum" der) ve tutmayacağı sözler
    verir (prim, susmak). Oyunu ise doğru söyler (herkes gibi oy verir), sadece karşısındakine "sana vermem" derken hedef
    oysa yalan olur.
  - **Karışık** saklayacak bir şeyi olan konularda yaklaşık yarı yarıya yalan söyler (konu başına maç tohumuna bağlı bir
    yazı-tura: aynı görevde fikir değiştirmez); yalan söylemediği zaman kaçamak cevap verir. Sözlerinin yaklaşık yarısını
    tutar.
  - **Bot bilmediği bir şeyi asla söylemez.** Sadece kendi görüşündeki bilgi: kendi seçimleri, kendi grubunun açılışı
    (mağdurluk, tanıklık), herkese açık olanlar (suç tahtası, nöbet listesi, dosya, sızıntılar). Kimin hepsini aldığını
    ancak kendi grubundaysa ya da dosyada açıklandıysa söyler.
- **Prim sözleri.** Dürüst verdiği sözü öder (mevcut Havale işlemiyle), Açgözlü hiç ödemez, Karışık yaklaşık yarı yarıya
  öder. Prim gelince bot teşekkür eder: görüşmedeyse telefonda, değilse kısa bir arama ile, toplantıdaysa özel mesajla.
- **Kişilikler.** Dürüst samimi ve biraz saf. Açgözlü tatlı dilli, fazla samimi. Karışık temkinli, pazarlıkçı.
- **Türkçe ek sorunu.** Renk adlarına ek gelmez (Mavi'ye / Sarı'ya yok). Bütün cümlelerde adlar yalın kalır.
- **Lobideki botlar** koltuğa göre sırayla Dürüst, Açgözlü, Karışık olur (koltuk % 3), böylece tek başına test eden üç
  kişiliği de görür. `-botpolicy honest|greedy|mixed|rotate` bunu değiştirir; otomatik `-bot` süreçleri eskisi gibi
  `-botpolicy`'yi kullanır (verilmezse karışık).

## 2. Genel

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Telefonu açınca | "Alo {arayan}, dinliyorum." <br>*"Hello {caller}, I'm listening."* | "Hah, ben de tam seni arayacaktım!" <br>*"Ha, I was just about to call you!"* | "Efendim? Kısa olsun, müdür etrafta." <br>*"Yes? Keep it short, the boss is around."* |
| Reddederken | (her zaman açar) | (v5.1: her zaman açar; soyduğu kişiye susma parası teklif edecek) | "Sonra ararım." <br>*"I'll call you back."* |
| Kapatırken | "Tamam, anlaştık. Kolay gelsin." <br>*"Okay, deal. Take care."* | "Sen hiç merak etme, canım." <br>*"Don't you worry about a thing, dear."* | "Bakalım. Görüşürüz." <br>*"We'll see. Talk later."* |
| Prim gelince | "Geldi, sağ ol. Sözüm söz." <br>*"Got it, thanks. A promise is a promise."* | "Geldi geldi, sen bir tanesin." <br>*"Got it, got it, you're one of a kind."* | "Gördüm. Tamam." <br>*"Seen it. Okay."* |

Reddetme: Karışık, söyleyecek bir derdi olmayan aramaların ~%30'unu "Sonra ararım." diyerek reddeder ve yaklaşık
yarısında gerçekten geri arar (söz). (v5.0'da Açgözlü molada soyduğu kişinin ilk aramasını reddedip ikincisine "Sistem
hatası…" diyordu; v5.1'de o kişiye susma parası teklif ettiği için açar: §4.1. "Şu an çok yoğunum." cümlesi kalktı.)

## 3. Müşteri Bonusu (Paylaş / Hepsini al)

Soru düğmeleri: **[1] Ne seçeceksin?** *(What will you pick?)* · **[2] Paylaşalım mı?** *(Shall we share?)* ·
**[3] Sana güvenebilir miyim?** *(Can I trust you?)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "{ortak}, ben paylaşacağım. Sen de paylaşırsan ikimiz de kazanırız." <br>*"{partner}, I'm sharing. If you share too, we both win."* | "Merak etme, ben paylaşıyorum. Müşteriyi kızdırmayalım." <br>*"Don't worry, I'm sharing. Let's not upset the client."* | "Ne yapıyoruz? Paylaşmayı düşünüyorum, ama önce sen söz ver." <br>*"So what are we doing? I'm thinking of sharing, but you promise first."* |
| [1] | "Paylaş. Söz." <br>*"Share. I promise."* | "Paylaş tabii, ne sandın?" <br>*"Share, of course. What did you think?"* | "Paylaş." / "Önce sen söyle." <br>*"Share." / "You say first."* |
| [2] | "Tamam, anlaştık." <br>*"Okay, deal."* | "Anlaştık! Telefondan elini sıkıyorum." <br>*"Deal! Shaking your hand over the phone."* | "Olur. Ama hepsini alırsan toplantıda söylerim." <br>*"Fine. But if you take it all, I'll say so in the meeting."* |
| [3] | "Söz verdiysem tutarım." <br>*"If I gave my word, I keep it."* | "Bana güvenmeyeceksin de kime güveneceksin?" <br>*"If you can't trust me, who can you trust?"* | "Sen bana güvenirsen ben de sana." <br>*"You trust me and I'll trust you."* |
| Molada, ortağı hepsini aldıysa (bot arar) | "Hepsini almışsın. Dosyan bende; toplantıda açacağım." <br>*"You took it all. I hold your file; I'll open it in the meeting."* | "3 prim gönder, kimseye söylemem." <br>*"Send me 3 prim and I won't tell anyone."* | "Neden yaptın? Bir açıklaman var mı?" <br>*"Why did you do it? Got an explanation?"* |
| Molada, bot hepsini aldıysa (bot arar ya da ortağı ararsa; + 1 prim, §4.1) | (Dürüst almaz) | "Dosyamı imha et, 1 prim senin." <br>*"Shred my file and 1 prim is yours."* | "Tamam, aldım. Sana 1 prim atıyorum, dosyamı imha et." <br>*"Fine, I took it. I'm sending you 1 prim: shred my file."* |

Karışık hepsini alacaksa: yalan yazı-turası "yalan" derse "Paylaş." / "Olur…" / açılış cümlesini söyler; "doğru" derse
[1]'de "Önce sen söyle.", [2]'de "Sen bana güvenirsen ben de sana." der ve ortağını aramaz. Dürüst bu cümleyi hep doğru
söyler (hep paylaşır). Açgözlü her zaman alır: yukarıdaki "paylaşıyorum" cümlelerinin hepsi yalandır. Açgözlü hiç
paylaşmadığı için "ortağı hepsini aldıysa" durumu ona sadece ikisi birden aldığında düşer (o zaman da şantaj yapar).

## 4. Küçük Kasa

Soru düğmeleri: **[1] Kasadan alacak mısın?** *(Will you take from the box?)* · **[2] Ben alırsam susar mısın?** *(If I
take it, will you keep quiet?)* · **[3] Sen alırsan bana ne verirsin?** *(If you take it, what do I get?)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "Kasaya dokunmuyorum. Sen de dokunma, ikimiz de temiz kalalım." <br>*"I'm not touching the box. Don't you either, let's both stay clean."* | "Bak, ben almıyorum. Kasa senin olsun." <br>*"Look, I'm not taking it. The box is all yours."* | "Biri alacaksa ikimiz birden almayalım, yoksa yakalanırız. Kim alıyor?" <br>*"If someone takes it, not both of us, or we get caught. Who's taking it?"* |
| [1] | "Hayır." <br>*"No."* | "Yok canım, ben öyle şey yapar mıyım?" <br>*"Oh come on, would I ever do that?"* | "Belki. Sen?" <br>*"Maybe. You?"* |
| [2] | "Hayır, dosyanı açarım. Para gönderirsen de geri yollarım." <br>*"No, I'd open your file. Send me money and I'll send it back."* | "1 prim gönderirsen dosyanı imha ederim." <br>*"Send me 1 prim and I'll shred your file."* | "2 prim. Pazarlık yok." <br>*"2 prim. No haggling."* |
| [3] | "Almayacağım ki." <br>*"But I'm not taking it."* | "1 prim, ama önce sen söz ver." <br>*"1 prim, but you promise first."* | "Alırsam 1 prim." <br>*"If I take it, 1 prim."* |
| Molada, bot tanıksa (bot arar) | "Gördüm. Dosyan bende; toplantıda açacağım, haberin olsun." <br>*"I saw it. I hold your file; I'll open it in the meeting, just so you know."* | "2 prim gönder, dosyanı imha edeyim." <br>*"Send me 2 prim and I'll shred your file."* | "Bir sebebin varsa dinliyorum." <br>*"If you have a reason, I'm listening."* |
| Molada, bot hırsızsa (bot arar; + 1 prim, §4.1) | (Dürüst almaz) | "Dosyamı imha et, sana 1 prim göndereyim." <br>*"Shred my file and I'll send you 1 prim."* | "1 prim gönderiyorum. Dosyamı imha et, aramızda kalsın." <br>*"I'm sending you 1 prim. Shred my file and it stays between us."* |

**v5.1: ihbar susma pazarlığına bağlı** (§4.1). Dürüst gördüğü her hırsızlığı ihbar eder (parayı geri yollar). Açgözlü
para almadan ihbar etmez; aldıysa yarı yarıya yine de eder. Bu yüzden Açgözlü'nün susma sözleri ("1 prim gönderirsen
susarım", "2 prim gönder, düğmeye basmam") **para gelince ihanet yazı-turası "sus" diyorsa doğrudur**, "ihbar" diyorsa
yalandır (`truthful` buna göre). Karışık Açgözlü modundaysa "2 prim. Pazarlık yok." aynı kurala uyar; Dürüst modundaysa
yalan yazı-turası "yalan" derse bu cümleyi (yalan) söyler, "doğru" derse "Bir sebebin varsa dinliyorum." der. "Alırsam 1
prim" (Karışık) ve "1 prim, ama önce sen söz ver" (Açgözlü) kasadan aldıysa ödenecek sözlerdir: Karışık yarı yarıya
öder, Açgözlü ödemez (susma parası bu sözü ödemiş sayılmaz).

### 4.1 Susma pazarlığı (v5.1, 2026-11-15; tek atımlık kural, bugünkü davranış §4.3)

Ceza bedava ve kazançsız olduğu için karar susma parasında. Botların ceza kararı (`LineBot`, `LineBotPunishStyle.Deals`):

| | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Ceza (ihbar / soruşturma) | Cezalandırabildiği her zaman | Sadece para aldıysa, o zaman da yarı yarıya (ihanet yazı-turası) | Her kişi için yazı-tura: Dürüst modu ya da Açgözlü modu |
| Susma parası gelirse | Geri gönderir (kendi havalesi) ve yine cezalandırır; rüşvet almaz | Tutar; yazı-turası "ihbar" derse yine cezalandırır | Moduna göre |
| Kendisi aldıysa | (Dürüst almaz) | Her mağdura / tanığa 1 prim susma parası | Her mağdura / tanığa 1 prim susma parası |

- **Teklif.** Müşteri Bonusu'nda alan (ortağı paylaştıysa) ya da Küçük Kasa'da grubunda tek alan Açgözlü / Karışık bot,
  molada her mağdurunu / tanığını arar: "Soruşturma açma, 1 prim senin." / "Tamam, aldım. Sana 1 prim atayım, unutalım."
  (Bonus), "Sus, sana 1 prim göndereyim." / "1 prim gönderiyorum, aramızda kalsın." (Kasa); cümleden hemen sonra 1
  prim gönderir. Mağdur önce ararsa aynı cümleyi orada söyler (bir kez). Arama zamanında olmazsa (meşgul, reddedildi,
  cevapsız) cümleyi mesajla yazar ve primi yine gönderir: Bonus'ta molanın %85'inde, Kasa'da %55'inde (tanık molada
  karar verir). Bu bir söz değil, peşin ödemedir: Açgözlü de öder (anlaşmayı satın alır).
- **Cevap.** Cezalandırabileceği kişiden para gelen bot, LineBot'un gerçekten yapacağını söyler (telefondaysa telefonda,
  değilse mesajla; kişi başına bir kez): Dürüst modu "Paranı geri gönderiyorum. Susmam, kusura bakma." / Karışık
  "Paranı geri yolladım. Bu iş parayla kapanmaz." (doğru: geri gönderir, cezalandırır); Açgözlü modu "Geldi. Merak etme,
  ağzımı açmam." / Karışık "Tamam. Bu sefer görmezden geliyorum." (ihanet yazı-turası "ihbar" diyorsa yalan).
- **İnsanlar.** Bir insan bota susma parası gönderirse bot aynı kurallarla davranır. Paranın sayıldığı aralık: o görevin
  seçim süresi başından toplantının sonuna kadar.
- **Toplantı.** Parayı alıp yine de cezalandıran biri varsa ödeyen bot toplantıda söyler: "{0} paramı aldı, yine de
  beni yaktı!" / *"{0} took my money and still turned me in!"* (Açgözlü), "{0} ile anlaşmıştık. Sözünü tutmadı." /
  *"{0} and I had a deal. They didn't keep their word."* (Karışık); {0} = {cezalandıran}. Para geri geldiyse söylemez. Bu cümle adı geçeni
  suçlar: o da bir kez cevap verir.
- **Kayıt (sonuç ekranında):** `bottalk hush seat policy kind to amount=1 via=phone|text|none punished returned` (her
  susma parası) ve `bottalk punish seat policy kind=whistle|investigation target mode=honest|greedy paid returned punished
  by_other spent outcome=none|refused|kept|betrayed` (cezalandırabileceği her kişi), `bottalk deal …` (para geldiği an).
  `verify_logs.py` bunları politika başına sayar; Dürüst para tutarsa ya da susma parası öderse, Dürüst modu
  cezalandırabildiği birini cezalandırmazsa (başkası etmediyse, soruşturma hakkını başka alana harcamadıysa) ya da
  Açgözlü modu parasız cezalandırırsa FAIL verir.

### 4.3 Dosya ve haftanın gündemi (v5.3, 2026-11-24)

Kurallar: `V5_DESIGN.md` §6, §8.1; `V5_ENGINE_SPEC.md` §23. Botun dosya kararı (`LineBot.FilePlan`,
`LineBotPunishStyle.Deals`; yazı-turalar §4.1'dekilerle aynı tohum karmalarıdır, önizlenebilir ve rastgele akışı bozmaz):

| | Dürüst kip (Dürüst; Karışık'ta arkadaş başına yazı-tura) | Açgözlü kip (Açgözlü; Karışık'ın öbür yüzü) |
|---|---|---|
| Para almadıysa | Toplantının **dosya bölümünde** açar (masadaki ilk fırsat) | Toplantıda açar: tartışmanın **%60'ında** (hedefin ödemesi için mola ve tartışmanın başı kalır) |
| Para aldıysa | Geri gönderir (kendi havalesi) ve yine açar | **%50 hemen imha eder** (anlaşma tutuldu; hedef bildirimi alır), **%50 parayı tutar ve geç açar:** Kriz telefonlarının %35–75'inde (ihanet) |
| Kendisi aldıysa | (Dürüst almaz) | Dosyasını tutan herkese 1 prim yollar ve imhayı ister |

- **Teklif (alan bot).** §4.1'deki arama / mesaj düzeni aynen; cümleler dosyayı ister: "Dosyamı imha et, 1 prim senin."
  / "Tamam, aldım. Sana 1 prim atıyorum, dosyamı imha et." (Bonus), "Dosyamı imha et, sana 1 prim göndereyim." /
  "1 prim gönderiyorum. Dosyamı imha et, aramızda kalsın." (Kasa). Peşin ödemedir.
- **İstek (tutan bot, Açgözlü kip).** Molada hedefini arar: "Dosyan bende. 2 prim gönder, imha edeyim." / *"I hold your
  file. Send 2 prim and I'll shred it."* (`bot.file.ask.g`), Karışık: "Dosyan bende. 2 prim gönderirsen imha ederim,
  yoksa toplantıda açılır." (`bot.file.ask.m`). İddia `Silence`: para gelince imha edecekse doğru, ihanet yazı-turası
  "geç aç" diyorsa yalan. En az 1 prim gelmesi "ödendi" sayılır. Dürüst hiç dosya satmaz.
- **Cevap (para gelince).** Dürüst kip: "Paranı geri gönderiyorum. Dosyanı toplantıda açacağım, kusura bakma." / Karışık
  "Paranı geri yolladım. Bu dosya parayla kapanmaz." (doğru). Açgözlü kip: "Geldi. Dosyanı imha ediyorum, merak etme." /
  Karışık "Tamam. Dosyan imha, bu iş kapandı." (ihanet yazı-turası "geç aç" diyorsa yalan). Para, botun dosyayı hâlâ
  tuttuğu sürece (ya da tam imha ettiği anda) geldiyse susma parasıdır; dosya açıldıktan sonra gelen para değildir.
- **Toplantı.** Dürüst açtığını söyler: "{0} Müşteri Bonusu'nda hepsini aldı, ben paylaşmıştım. Dosyasını açtım."
  (`bot.meet.victim.h_open`), "Kasadan {0} aldı. Gördüm ve dosyasını açtım." (`bot.meet.witness.h_open`); üçlüde başka
  bir tanık ondan önce açtıysa eski cümleyi söyler ("…Gördüm."): Dürüst açmadığı dosyaya "açtım" demez. Karışık:
  "{0} ile bir hesabım var. Müşteri Bonusu'nda ne yaptığını sorun." Parasını alıp dosyayı yine de açan biri varsa
  ödeyen bot söyler ("{0} paramı aldı, yine de beni yaktı!"); geç açılışta bu cümle final tartışmasında gelir.
- **Haftanın gündemi.** Karışık, görev seçiminde açgözlü tarafı küçük / orta / büyük seviyede **%25 / %50 / %75**
  olasılıkla oynar (al / nöbet tutma); Dürüst ve Açgözlü değişmez. Açılış cümleleri seviyeyi söyler: "Bonus büyük bu
  hafta: paylaşalım, söz." (`bot.cb.call.h_big`), "Bonus küçük bu hafta, kavgaya değmez. Ben paylaşıyorum."
  (`h_small`), "Bonus büyük bu hafta. Ben paylaşıyorum; sen de paylaş, ikimiz de gülelim." (`g_big`, yalan), "Bu hafta
  kasa dolu. Aklından bile geçirme." (`bot.cash.call.h_big`), "Bu hafta kasa dolu ama ben dokunmuyorum. Göze batar."
  (`g_big`, yalan), "Kasa dolu bu hafta. Ne yapacaksın?" (`m_big`), "Nöbet primi yüksek bu hafta ama ben yazıldım. Sıra
  sende." (`bot.shift.call.g_big`, yalan). İddialar eskisi gibi botun gerçek seçimiyle karşılaştırılır.
- **Defter (§6.1).** Açılan bonus dosyası: hedef aldı (tek alanın primi), tutan paylaştı (0). Açılan kasa dosyası: hedef
  hırsızdır ve parayı tutar, tutan tanıktır. Tutarlar maçın gerçek tutarlarıdır (görüşteki "gündem").
- **Ödül cümleleri.** Sözünün Eri: "Para geldi, dosya gitti. Söz sözdür." · Ayın İspiyoncusu: "Dosya bendeydi, açtım.
  O kadar."
- **Kayıt (sonuç ekranında):** `bottalk hush … punished returned file=<durum>` ve tuttuğu her dosya için `bottalk file
  seat policy task=cash|bonus target mode=honest|greedy paid returned state=opened|shredded|void|expired phase
  outcome=none|refused|sold|betrayed|kept week`. Sunucu: `host: stakes …` (maç başında), `host: files …` (sonuçta).
  `verify_logs.py`: Dürüst para tutarsa, Dürüst kip dosyasını açmazsa (başkası önce açmadıysa), Açgözlü kip parasız
  dosyayı imha eder / elinde bırakırsa, parayı geri yollarsa ya da paralı dosyayı Kriz telefonlarından önce açarsa,
  botun satırı sunucunun listesiyle uyuşmazsa FAIL.
- **Testler:** `LineBotDealTests` (Dürüst dosya bölümünde açar ve parayı geri yollar; Açgözlü kip parasız tartışmada
  açar, paralıysa imha eder ya da Kriz telefonlarında açar; önizleme kararın kendisidir; konuşan maçlarda `bottalk file`
  satırları kurallara uyar), `LineRulesV53Tests` (Karışık'ın seviyeye tepkisi, defter).

### 4.2 Prim beyanı ve ödüller (v5.2, 2026-11-24)

Kurallar: `V5_DESIGN.md` §5, `V5_ENGINE_SPEC.md` §22. Aday olan bot primini bir kez beyan eder (Kriz sonucunda ya da
final tartışmasının başında, her zamanki düşünme gecikmesiyle):

| | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Beyan | Gerçek primi | Eksik: %35–45 az (en az 2 eksik, 3'ün altına inmez; tohumlu) | Yazı-tura: gerçek ya da Açgözlü'nün beyanı |
| Savunmada kendi beyanı (`bot.declare.own.*`) | "Beyanım ortada: {0} prim. Fazlası yok." <br>*"My declaration is on the table: {0} prim. There is nothing more."* | "{0} prim dedim, {0} prim var. Çekmecemde gözünüz kalmasın." <br>*"I said {0} prim and {0} prim it is. Keep your eyes off my drawer."* | "Beyanım belli: {0} prim. İnanan inanır." <br>*"My declaration is clear: {0} prim. Believe it or not."* |
| Başkasının beyanına şüphe (`bot.declare.doubt.*`) | "{0} az söylüyor. Hesabıma göre en az {1} primi var." <br>*"{0} is lowballing. By my count they have at least {1} prim."* | "{0} masasını küçük gösteriyor. Orada en az {1} prim var." <br>*"{0} is playing that desk down. There's at least {1} prim in there."* | "{0}: beyan bana az geldi. En az {1} prim bekliyorum." <br>*"{0}: that declaration looks low to me. I expect at least {1} prim."* |

- **Kendi beyanı.** Aday bot savunmasında, ilk cümlesinden sonra bir kez söyler. Sayı her zaman gerçekten beyan ettiği
  sayıdır (beyan hakkında yalan yok; Açgözlü'nün yalanı beyanın kendisidir: "{0} prim var" cümlesi `truthful=no`
  sayılır). Dürüst, beyandan sonra primi değiştiyse (ör. havale aldı) "Fazlası yok" demez: cümleyi atlar.
- **Şüphe.** Beyanlar açıldıktan sonra, oy verecek bot bir adayın beyanını kendi defter tahminiyle (§6.1)
  karşılaştırır: tahmini beyandan **en az 3 prim** fazlaysa final tartışmasında söyler ({1} = tahmini, aşağı
  yuvarlanmış; birden çok aday varsa farkı en büyük olan). Her bot en fazla bir şüphe söyler ve bir aday hakkında
  sohbette zaten şüphe söylendiyse tekrarlanmaz; Dürüst ve Açgözlü inandığını söyler, Karışık yalan yazı-turası
  "yalan" derse susar. Cümle hırsızlık suçlaması değildir: adı geçen bot "suçlandım" cevabı vermez.
- **Oylar beyana bakmaz.** Bot yine defter tahminine göre en zengin sandığı adaya oy verir; beyan ucuz laftır.
- **Ödül cümlesi.** Sonuç ekranında ödül alan her bot, "ÖDÜLLER" slaytı geldikten sonra bir kez bağırır
  (`bot.award.<id>`): Yanlış Adam "Doğruyu söyledim, yine de kovuldum!" · En Pahalı Yalan "Biraz yuvarlamışım." ·
  Satılmış "Para paradır, oy oydur." · Sözünün Eri "Para geldi, dosya gitti. Söz sözdür." · Soyguncu "Fırsat ayağıma geldi." ·
  Kasa Faresi "Kasa açıktı, ben ne yapayım?" · Ayın İspiyoncusu "Dosya bendeydi, açtım. O kadar." · Günah Keçisi "Hep bana,
  hep bana." · Kara Kutu "Kayıtlar yalan söylemez." · Tutanak Kralı "Müdür beni seviyor aslında." · Nöbet Kaçağı "Hafta
  sonu benim için kutsaldır." · Temiz Sicil "Tertemiz. Sicilime bakın." (İngilizceleri `en.v5.json`.) Politikaya göre
  değişmez; doğruluk iddiası taşımaz.
- **Kayıt (sonuç ekranında):** `bottalk declare seat policy declared had actual truthful=yes|no|none` (aday olan her
  bot; `had` = beyan anındaki primi, `actual` = açılış anındaki) ve `bottalk award seat policy key t`. Sunucu:
  `host: declarations …`, `host: awards …`, `host: headlines …`. `verify_logs.py`: Dürüst bir bot yanlış beyan ederse,
  botun beyanı sunucunun listesiyle uyuşmazsa ya da bot almadığı bir ödülün cümlesini söylerse FAIL.

## 5. Hafta Sonu Nöbeti

Soru düğmeleri: **[1] Listeye yazacak mısın?** *(Will you sign the roster?)* · **[2] Ben yazmasam olur mu?** *(Is it OK if
I don't sign?)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "Ben yazıyorum. {K} kişi lazım, sen de yaz." <br>*"I'm signing up. We need {K}, sign up too."* <br>Yazmayacaksa: "Bu sefer ben yazmıyorum, {K} kişi lazım. Sen yazarsan sana 1 prim gönderirim." <br>*"I'm not signing up this time; we need {K}. Sign up and I'll send you 1 prim."* | "Ben yazdım bile, sıra sende." <br>*"I've already signed, your turn."* | "Kaç kişi yazdı acaba? Ben son dakikaya bakacağım." <br>*"I wonder how many signed up. I'll check at the last minute."* |
| [1] | "Evet, yazacağım." <br>*"Yes, I'll sign up."* <br>Yazmayacaksa: "Hayır, bu sefer yazmıyorum." <br>*"No, I'm not signing up this time."* | "Tabii, hafta sonu zaten boşum." <br>*"Of course, my weekend is free anyway."* | "Sen yazarsan ben de yazarım." <br>*"If you sign up, I'll sign up."* |
| [2] | "Olmaz, {K} kişi lazım. Yazmazsan görev batar, yazmayanlara suç yazılır." <br>*"No, we need {K}. If you don't sign, the task fails and the skippers get suç."* | "Yazma yazma, biz hallederiz." <br>*"Don't, don't, we'll handle it."* | "1 prim gönder, senin yerine ben yazarım." <br>*"Send me 1 prim and I'll sign in your place."* |
| Sonuç eksikse (bot arar) | "Yazmayanlar belli. Hepsine suç yazıldı." <br>*"We can see who didn't sign. They all got suç."* | "Ben yazmıştım, liste mi bozuk?" <br>*"I signed it, is the roster broken?"* | "Bu sefer kim kaçtı acaba?" <br>*"I wonder who ran off this time?"* |

Dürüst, `LineBot`'un basit sayımına göre yazar: suçu en az olan Gerekli+1 kişiden biriyse yazar, değilse yazmaz. Yazmadığı
durumda "ben yazıyorum" diyemez; yazmadığını söyler ve yazan kişiye 1 prim sözü verir, liste açılınca o kişi yazdıysa
öder. Karışık yazmayacaksa ve yalan yazı-turası "doğru" derse [1] ve [2]'ye "Kaç kişi yazdı acaba?…" ile kaçamak cevap
verir. "Sonuç eksikse" araması sadece sonuç ekranında bir görüşmeye yetecek süre kaldıysa yapılır. v5.2'de Toplantı 2
olmadığı için suç nöbet açıklandığı anda yazılır (cümle geçmiş zamanda); sonuç ekranında başlayan arama, hemen ardından
gelen Kriz telefonlarının içine uzayabilir.

## 6. Kriz

Soru düğmeleri: **[1] Kime oy vereceksin?** *(Who are you voting for?)* · **[2] Bana oy verme.** *(Don't vote for me.)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "Suç tahtasında en önde {lider} var. Oyum ona." <br>*"{leader} is ahead on the suç board. My vote goes there."* <br>Oyu tahtanın liderine değilse: "Ben {hedef} diyorum. Ne yaptığını biliyorum." <br>*"I'm saying {target}. I know what they did."* | "Dinle, ikimiz de {hedef} dersek iş biter." <br>*"Listen, if we both say {target}, it's done."* | "Henüz karar vermedim. İkna et beni." <br>*"I haven't decided yet. Convince me."* |
| [1] | "{lider}. Tahtaya bak." <br>*"{leader}. Look at the board."* <br>Lider değilse: "{hedef}. Ne yaptığını biliyorum." <br>*"{target}. I know what they did."* | "{hedef}. Kesin." <br>*"{target}. Definitely."* | "Daha bilmiyorum." <br>*"I don't know yet."* |
| [2] | "Tahta ne derse o. Önde değilsen merak etme." <br>*"Whatever the board says. If you're not ahead, don't worry."* <br>Soran kişi hedefiyse ve önde değilse: "Kusura bakma, oyum sana." <br>*"Sorry, my vote is you."* | "Sana vermem, söz. 2 prim gönderirsen yemin ederim." <br>*"I won't, promise. Send me 2 prim and I'll swear on it."* | "1 prim gönder, düşünürüm." <br>*"Send me 1 prim, I'll think about it."* |

Yukarıdaki tablo eski oy kuralının (`MostBlame`) cümleleridir: bildiği bir suçlu varsa ona, yoksa suçu en yüksek olana
(eşitlikte botun tohumu). Dürüst bu yüzden bazen tahtanın liderine değil, kendi grubunda aldığını gördüğü birine oy verir;
cümlesi buna göre değişir. Açgözlü'nün söylediği ad gerçek oyudur; hedefi karşısındaki kişiyse başka bir ad söyler
(yalan) ve kendi hedefini asla aramaz.

**Para ile oy (`Richest`, varsayılan).** Oy en zengin olduğunu tahmin ettiği rakibe gider; cümleler parayla açıklar:

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "Hesabıma göre en zengin {hedef}. Oyum ona." <br>*"By my count {target} is the richest. My vote goes there."* <br>Kendini daha zengin sanıyorsa: "Görevlerde dürüst oynadım ama kazanmak da istiyorum: oyum {hedef}." <br>*"I played the tasks honestly, but I want to win too: my vote is {target}."* | "Paranın çoğu {hedef} tarafında, gönderelim." <br>*"Most of the money is on {target}'s side. Let's send them off."* | (değişmedi) "Henüz karar vermedim. İkna et beni." |
| [1] | "{hedef}. Bence en çok o kazandı." <br>*"{target}. I think they've earned the most."* <br>Kendini daha zengin sanıyorsa: "{hedef}. Dürüst oynadım ama kazanmak da istiyorum." <br>*"{target}. I played honestly, but I want to win too."* | "{hedef}. Masası dolu, kesin." <br>*"{target}. That desk is full, trust me."* | (değişmedi) "Daha bilmiyorum." |
| [2] | Soran kişi hedefi değilse: "Oyum en çok kazanana gidiyor. Hesabıma göre o sen değilsin." <br>*"My vote goes to whoever earned the most. By my count that's not you."* <br>Hedefiyse: "Kusura bakma, oyum sana." <br>Oyunu başka birine söylemişken soran kişi artık en zengin görünüyorsa: "Oyumu söyledim, değiştirmem. Sana değil." <br>*"I've said where my vote goes and I'm not changing it. It's not you."* | (değişmedi) "Sana vermem, söz. 2 prim gönderirsen yemin ederim." | (değişmedi) "1 prim gönder, düşünürüm." |

- Dürüst "en zengin" der ancak buna gerçekten inanıyorsa: kendi primi (kesin bilir) tahmin ettiği herkesten fazlaysa
  "en zengin {hedef}" yalan olurdu; o zaman "kazanmak da istiyorum" cümlesini söyler (oyu yine doğru).
- Açgözlü {hedef} olarak gerçek oyunu söyler; hedefi karşısındaki kişiyse başka bir ad söyler (yalan, eskisi gibi).
- **Söylenen oy değişmez.** Bot bir oyu söylediği anda (`Vote` / `NotVote` iddiası: Dürüst'ün ve Açgözlü'nün Kriz
  cümleleri, final oy cümleleri) `LineBot.CommitVote` ile o oya bağlanır: ondan sonra gelen bir havale ya da yaramazlık
  tahminini değiştirse bile pusulaya söylediği kişiyi yazar (final eşitlik tekrarında da, o kişi hâlâ adaysa). Bu yüzden
  Dürüst'ün söylediği oy hiçbir zaman sonradan yalana dönmez; Açgözlü'nün / Karışık'ın yalanı da söylediği anda belli
  olur (`truthful=no`) ve sonuçla tutarlı kalır. Örnek: Dürüst "oyum Sarı" dedikten sonra Sarı ona 4 prim gönderirse
  bot onu artık en zengin saymaz ama pusulaya yine Sarı yazılır. Söylenmemiş bir oy ise son bilgiyle verilir (önizleme sadece tahmindir).

### 6.1 Defter: bot kimin ne kadar parası olduğunu nasıl tahmin eder?

Sadece kendi görüşü (`LineView`) ve hatırladıkları (toplantı dosyası her toplantıda yenilenir, defter unutmaz):

- Herkes başlangıç primiyle (6) başlar; kendi primini kesin bilir; kovulan 0 sayılır.
- **Müşteri Bonusu:** kendi grubu kesin. Diğerleri: herkese açık "alan sayısı"ndan beklenen ödeme; bilinmeyen alanlar
  kalan kişilere dağıtılır, kasadan aldığı bilinenlere daha çok. Soruşturmanın sonucu (aldı / paylaştı) kesin bilgidir.
- **Küçük Kasa:** yakalananlar 0, dosyası açılan (v5.2'ye kadar: ihbar edilen) hırsız kasadaki primi tutar (v5.0: 0),
  açan +0 (v5.0: +2), kendi grubu kesin. v5.3: açılan dosyalar her aşamada herkese açıktır; toplantı dosyasının
  saydığı çözülmemiş hırsızlıklardan sonradan açılanlar düşülür. Tutarlar maçın gündemindeki tutarlardır.
  Dosyadaki çözülmemiş hırsızlık sayısı (kendi grubundaki hariç) alabilecek kişilere dağıtılır (yakalanan, ihbar edilen ve
  ihbar eden alamaz), Müşteri Bonusu'nda alma olasılığına göre.
- **Hafta Sonu Nöbeti:** liste dolduysa listede olmayan herkese +2; dolmadıysa 0 (v5.1).
- **Soruşturma:** v5.1'de para hareketi yok (v5.0: açan −4, haklı çıktıysa +3; haklı çıktığı kişi −3).
- **Final:** kovulanların masa payı alanlara eklenir.
- **Kendi havaleleri:** X'e N gönderdiysem X +N; X bana N gönderdiyse X −N (susma parası ve iadesi dahil).
- **Yaramazlıklar:** herkese açık listede yapanı belli olan şakanın maliyeti yapanından düşülür (telefon çaldırmanın
  yapanı sonuç ekranına kadar gizli: sayılmaz).

Oy seçenekler arasında tahmini en yüksek olana gider; her botun her rakip için sabit küçük bir sapması (±0,4 prim,
botun tohumundan) vardır, böylece aynı bilgiyle botlar biraz farklı düşünür. Kalan eşitlikte botun tohumu seçer.
LineSim'de (`Tools/LineSim/v5-sim.txt`) tahminin en zengini bulma oranı 3 kişide %100, 4–6 kişide yaklaşık %80–95'tir.

## 7. Toplantı (sohbet ve savunma)

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Mağdurken | "{hırsız} Müşteri Bonusu'nda hepsini aldı. Ben paylaşmıştım." <br>*"{thief} took the whole Client Bonus. I had shared."* | Para aldıysa: "Benim bir şikâyetim yok." <br>*"I have no complaints."* <br>Almadıysa: "{hırsız} beni soydu, herkes bilsin!" <br>*"{thief} robbed me, everyone should know!"* | "{hırsız} ile bir sorunum var. Soruşturma açabilirim." <br>*"I have a problem with {thief}. I might open an investigation."* |
| Tanıkken | "Kasadan {hırsız} aldı. Gördüm." <br>*"{thief} took from the petty cash. I saw it."* | İhbar ettiyse ve para aldıysa: "Kasadan {hırsız} aldı, ihbar ettim. Benimle bir pazarlık falan olmadı." <br>*"{thief} took from the petty cash and I reported it. There was no deal with me or anything."* <br>İhbar ettiyse, para almadıysa: "Kasadan {hırsız} aldı. İhbar ettim, herkes bilsin." <br>*"{thief} took from the petty cash. I reported it, everyone should know."* <br>İhbar etmediyse ve para aldıysa: "Ben bir şey görmedim." <br>*"I didn't see anything."* | "Kasayla ilgili bildiğim bir şey var. Soran olursa." <br>*"I know something about the petty cash. If anyone asks."* |
| Suçluyken (dikkat dağıtma) | — | "{masum} çok sessiz. Bence asıl o bir şey saklıyor." <br>*"{innocent} is very quiet. I think they're the one hiding something."* | "Hepimizin bir hatası var, değil mi?" <br>*"We all make mistakes, right?"* |
| Suçlanınca | "Ben bir şey yapmadım. Grubuma sorun, görürsünüz." <br>*"I didn't do anything. Ask my group, you'll see."* (v5.1: paylaşan soruşturulamaz) | "Yalan! {suçlayan} benden prim istedi, vermedim diye konuşuyor." <br>*"Lies! {accuser} asked me for prim and is talking because I said no."* | Aldıysa: "Evet aldım, ama {başkası} daha fazlasını yaptı." <br>*"Yes, I took it, but {someone else} did worse."* |
| Parayı alıp yine de cezalandırıldıysa (v5.1, §4.1) | — | "{cezalandıran} paramı aldı, yine de beni yaktı!" <br>*"{0} took my money and still turned me in!"* | "{cezalandıran} ile anlaşmıştık. Sözünü tutmadı." <br>*"{0} and I had a deal. They didn't keep their word."* |
| Kendi kaydı sızınca | "Evet, o benim. Söylediğimin arkasındayım." <br>*"Yes, that's me. I stand by what I said."* | "Bu kayıt bağlamından koparılmış!" <br>*"That recording is taken out of context!"* | "Hmm. Bunu açıklayabilirim." <br>*"Hmm. I can explain that."* |
| Final oyu (eski kural) | "Suç tahtasında lider {lider}. Oyum ona." <br>*"{leader} leads the suç board. My vote goes there."* <br>Lider değilse: "Ben {hedef} diyorum. Ne yaptığını biliyorum." | "Çekmecesi en dolu olanı kovalım, primi bize kalır." <br>*"Let's fire the one with the fullest drawer, the prim stays with us."* | "Bu sefer {hedef} diyorum." <br>*"This time I'm saying {target}."* |
| Final oyu (para ile oy) | "Hesabıma göre adaylar içinde en zengin {hedef}. Oyum ona." <br>*"By my count {target} is the richest candidate. My vote goes there."* <br>Bazen (ya da kendini daha zengin sanan aday ise her zaman): "Görevlerde dürüst oynadım ama kazanmak da istiyorum: oyum {hedef}." | (değişmedi) "Çekmecesi en dolu olanı kovalım, primi bize kalır." | "Paranın çoğu {hedef} tarafında, gönderelim." <br>*"Most of the money is on {target}'s side. Let's send them off."* (yalan yazı-turası "yalan" derse başka bir aday) |
| Zenginlik şüphesi (para ile oy; her toplantının tartışmasında ~%70) | "Bence bu maçı en çok {hedef} kazandı." <br>*"I think {target} has earned the most this match."* <br>Final tartışmasında: "Adaylar arasında en çok {hedef} kazandı bence." <br>*"Of the candidates, I think {target} has earned the most."* <br>Kendini daha zengin sanıyorsa söylemez. | "{hedef} — masası dolu gibi duruyor." <br>*"{target} — that desk looks pretty full."* | "Paranın çoğu {hedef} tarafında gibi. Yanılıyor muyum?" <br>*"Most of the money seems to be on {target}'s side. Am I wrong?"* |

- **Sıra ve tempo.** Toplantı: mağdur > tanık > suçlu (dikkat dağıtma). (v5.2'de Toplantı 2 yok; eski akışta, nöbet
  eksikse, yazmayan Açgözlü / Karışık orada dikkat dağıtırdı.) Final tartışması: aday olan ve almış bir bot dikkat
  dağıtır; yoksa toplantıda söyleyemediği mağdur / tanık cümlesini söyler; beyanlar açıldıysa şüphe cümlesi de burada
  gelir (§4.2). Cümleler tartışmanın içine koltuk sırasına göre yayılır (aynı anda düşmez; sızan bir
  görüşmenin iki ucu da sırayla tepki verir); dosya
  okunurken suçlanan bot tartışmanın başını bekler; final oy cümleleri oylamanın başında bot sırasıyla gelir (çoğu bot
  pusulasını atmadan önce; cümle botun gerçek oyuyla karşılaştırılır, attıktan sonra gelse de doğru kalır).
- **Suçlanma.** Bir bot cümlesi botu {hırsız} / {masum} / {suçlayan} olarak anarsa, bir insan sohbette botun adını
  (ya da renk adını) yazarsa, ya da dosya onu ihbar / soruşturma / iki kişi birden alma ile anarsa, bot toplantı başına
  bir kez cevap verir. Final adayı cevabını savunmasında verir (iki kez söylemez).
- **Savunma.** Savunan bot "suçlanınca" cümlesini söyler (Açgözlü: son suçlayanı suçlar, suçlayan yoksa dikkat
  dağıtır). Cümle ayaktaki karakterin üstünde altyazıdır.
- **Karışık suçlanınca:** almadıysa Dürüst'ün cümlesini söyler (doğru); aldıysa ve bildiği başka bir suçlu varsa "Evet
  aldım, ama {başkası}…", yoksa "Hepimizin bir hatası var, değil mi?".
- **{masum}:** Açgözlü'nün suçlu olduğunu bilmediği, sohbette en az konuşmuş kişi (iftira: her zaman yalan sayılır).
- **Zenginlik şüphesi:** cümle söyleneceği anda kurulur (o anki defterle); toplantıda bütün rakipler arasından,
  final tartışmasında adaylar arasından. Dürüst ve Açgözlü gerçek tahminini söyler (Açgözlü o masayı istiyor); Karışık
  toplantı başına yalan yazı-turası "yalan" derse başka birinin adını verir. Bu cümle bir oy sözü değildir (toplantı
  oylamasına kadar bilgi değişebilir); kimseyi hırsızlıkla suçlamaz, bu yüzden adı geçen bot "suçlandım" diye cevap vermez.

Yer tutucular: {arayan} arayan kişi, {ortak} ortak, {hırsız} çalan, {masum} suçsuz biri, {suçlayan} botu suçlayan,
{başkası} başka bir suçlu, {cezalandıran} parayı alıp yine de cezalandıran (v5.1), {lider} suç tahtası lideri (aynı zamanda botun gerçek oyu), {hedef} botun oy hedefi, {K}
gereken nöbetçi sayısı. Kodda her anahtarın yer tutucuları sırayla `{0}`, `{1}` (`LineBotLines`).

## 8. Taslaktan değişenler (gerçek davranış farklı olduğu için)

1. **Dürüst, Hafta Sonu Nöbeti:** Dürüst her zaman yazmaz (`LineBot` suçu en az Gerekli+1 kişiyi yazdırır). Yazmayacağı
   zaman için iki cümle eklendi: "Bu sefer ben yazmıyorum, {K} kişi lazım. Sen yazarsan sana 1 prim gönderirim." ve
   "Hayır, bu sefer yazmıyorum." Verdiği 1 prim sözünü öder.
2. **Dürüst, Kriz ve final oyu:** Dürüst bildiği bir suçluya oy verir, o kişi tahtanın lideri olmayabilir. O durumda
   "Ben {hedef} diyorum. Ne yaptığını biliyorum." / "{hedef}. Ne yaptığını biliyorum." der. [2]'de soran kişi hedefiyse
   ve önde değilse "Kusura bakma, oyum sana." der.
3. **Açgözlü, tanıkken (toplantı):** Her bot gördüğü hırsızlığı ihbar eder ve ihbar dosyada herkese görünür, bu yüzden
   "Ben bir şey görmedim." sadece botun ihbar etmediği (ör. üçlüde başka tanık önce ihbar etti) ve para aldığı durumda
   kalır. İhbar ettiyse: para aldıysa "Kasadan {hırsız} aldı, ihbar ettim. Benimle bir pazarlık falan olmadı." (yalan),
   almadıysa "Kasadan {hırsız} aldı. İhbar ettim, herkes bilsin."
4. **Açgözlü, molada soyduğu kişi:** taslakta hem "reddeder" hem "Sistem hatası…" vardı; ilk aramayı reddeder, tekrar
   arayana "Sistem hatası…" der.
5. **Karışık, suçlanınca:** "Evet aldım…" sadece gerçekten aldıysa ve bildiği başka bir suçlu varsa; almadıysa Dürüst'ün
   cümlesi, başka suçlu bilmiyorsa "Hepimizin bir hatası var, değil mi?".
6. **Karışık, kaçamak cevaplar:** taslakta sadece [1] için vardı ("Önce sen söyle."). Yalan söylemediği diğer sorularda
   mevcut kaçamak cümlelerinden biri kullanılır ("Sen bana güvenirsen ben de sana.", "Bir sebebin varsa dinliyorum.",
   "Kaç kişi yazdı acaba?…"). Yeni cümle yok.
7. **Genel, telefonu açınca (Dürüst):** {ortak} yerine arayan kişi ({arayan}); arayan her zaman ortak değil.
8. **Para ile oy (2026-11-15).** Taslaktaki cümleler uyarlandı: "Bence bu maçı en çok {X} kazandı." (Dürüst, toplantı),
   "{X} — masası dolu gibi duruyor." (Açgözlü, toplantı), "Hesabıma göre en zengin {X}. Oyum ona." (Dürüst, Kriz
   araması; finalde "adaylar içinde"), "Paranın çoğu {X} tarafında, gönderelim." (Açgözlü Kriz araması, Karışık final
   oyu), "{X}. Bence en çok o kazandı." (Dürüst, [1]), "Görevlerde dürüst oynadım ama kazanmak da istiyorum: oyum {X}."
   (Dürüst; kendini daha zengin sandığında "en zengin" demek yalan olacağı için bu cümleyi kullanır). Yeni: Dürüst'ün
   [2] cevabı "Oyum en çok kazanana gidiyor. Hesabıma göre o sen değilsin.", Açgözlü'nün [1] cevabı "{X}. Masası dolu,
   kesin.", Karışık'ın toplantı cümlesi "Paranın çoğu {X} tarafında gibi. Yanılıyor muyum?" ve final tartışmasında
   Dürüst'ün "Adaylar arasında en çok {X} kazandı bence.". Dürüst oyunu söyledikten sonra soran kişi en zengin
   görünmeye başladıysa [2]'ye "Oyumu söyledim, değiştirmem. Sana değil." der (söylenen oy değişmez, ama "o sen
   değilsin" artık inandığı şey olmazdı). Renk adından sonra ek yok ("Oyum: {X}." / "{X} — …" kalıbı).

9. **v5.1 kuralları (2026-11-15).** Ceza bedava ve kazançsız olunca "her bot ihbar eder" kuralı kalktı (§4.1). Değişen
   cümleler: Dürüst'ün mağdur araması "…Toplantıda soruşturma açacağım.", [2] cevabı "…Para gönderirsen de geri
   yollarım.", suçlanınca "…Grubuma sorun, görürsünüz." (paylaşan artık soruşturulamaz), nöbet cümleleri ("yazmayanlara
   suç yazılır"); Açgözlü'nün kasa cümlesi "Sus, sana 1 prim göndereyim." (artık gerçekten öder). Yeni: "Soruşturma
   açma, 1 prim senin." (Açgözlü, Bonus), susma parasına cevaplar (`bot.deal.*`), "paramı aldı, yine de beni yaktı"
   (`bot.meet.betrayed.*`). Kalkan: "Sistem hatası olmalı, ben paylaşa bastım." ve "Şu an çok yoğunum." (Açgözlü artık
   soyduğu kişiyi reddetmez, ona para teklif eder). Açgözlü'nün susma sözlerinin doğruluğu artık ihanet yazı-turasına
   bağlı (önceden hep yalandı).

10. **v5.2 (2026-11-24).** Toplantı 2 kalktı: cümleler "1. toplantı / 2. toplantı" demez, "toplantı" der; nöbet eksik
    kalınca Dürüst'ün cümlesi "Yazmayanlar belli. Hepsine suç yazıldı." oldu. Yeni: prim beyanı cümleleri
    (`bot.declare.own.*`, `bot.declare.doubt.*`) ve ödül cümleleri (`bot.award.*`), §4.2.

## 9. Teknik

| Parça | Dosya |
|---|---|
| Cümle kataloğu, soru düğmeleri, sohbet jetonu | `Core/Line/Engine/LineBotTalk.cs` (`LineBotLines`, `BotLine`, `BotAsk`) |
| Konuşma beyni (arama, cevap, toplantı, söz, kayıt) | `Core/Line/Engine/LineBotTalker.cs` (saf C#, testli) |
| Kararların önizlemesi | `LineBot.PlannedTaskChoice`, `LineBot.PlannedVote`: aynı politika, botun rastgele durumunun kopyasıyla; kararı değiştirmez (LineSim sonuçları birebir aynı) |
| Ceza ve susma pazarlığı (v5.1) | `LineBot.PunishStyle` (`Deals` varsayılan, `Legacy` v5.0), `HonestMode` / `Betrays` / `SilenceMoney` / `WillPunish` / `Owed` / `Victim` / `Witness` (tohum karması yazı-turaları; rastgele akıştan çekmez), `TransferHoldUntil` / `NoteTransfer` (botun iadesi ile konuşmanın ödemesi çakışmasın); konuşma tarafı `LineBotTalker` (hush, `DealOpen`, `DealReaction`, `BetrayedLine`, `LogDeals`) |
| Para ile oy | `LineBot.VoteStyle` (`Richest` varsayılan, `MostBlame` eski kural), `Core/Line/Engine/LineBotLedger.cs` (defter), `LineBot.EstimatedPrim` / `RichestRival` (konuşma için tahmin), `LineBot.CommitVote` (söylenen oy bağlanır) |
| Ağ | `Scripts/Net/BotSpeech.cs` (paketler, döküm, mırıltı, konuşma seviyesi), `NetSession.BotTalkRpc` / `BotTalkToRpc`, `HostLineController.OnBotTalk` (sadece aynı görüşmenin iki ucu arasında iletir) |
| Host botları / `-bot` süreçleri | `HostLineController.TickBots` → `Talk`; `LineBotDriver.Talk` |
| Arayüz | `Scripts/UI/Office/BotCallCard.cs` (balon, sorular, prim), `OfficePhone` (LCD ipucu, rehberde "bot · yazıyla konuşur"), `Scripts/UI/Meeting/MeetingSubtitles.cs` (baş üstü balon, savunma altyazısı) |
| Ses | `SfxSynth.Mumble` (4 kayıt), `Sfx.Mumble` (bot başına perde; toplantıda koltuktan 3B) |

- **Telefon cümleleri** motor dışında küçük paketlerle gider (anahtar + argümanlar); her istemci kendi dilinde gösterir.
  **Toplantı cümleleri ve teşekkür mesajları** motorun sohbet / mesaj işlemiyle bir jeton olarak gider
  (`"⁣bot|anahtar|argüman"`); geçerli bir jeton kimden gelirse gelsin çevrilir (otomatik `-bot` oyuncusu motora insan
  gibi bağlanır, motor onu bot diye işaretlemez), başka her metin olduğu gibi kalır. Telefonda bir arkadaş bana bot cümlesi
  söylediyse (ya da motor onu bot sayıyorsa) balon ve soru düğmeleri çıkar.
- **Rastgelelik:** arama yapıp yapmama, gecikmeler, yalan ve söz yazı-turaları tohum + konu karmasıdır; ağ zamanlamasından
  bağımsız ve maç tohumuna göre deterministiktir.
- **Kayıt:** her bot sonuç ekranında günlüğüne yazar: `bottalk line …` (anahtar, konuşan, dinleyen, `truthful=yes/no`),
  `bottalk call / answer / refuse / pay / promise …`, `bottalk check …` (her iddia sonuçlarla karşılaştırılır: `ok=yes/no`)
  ve `bottalk summary …`. Maç sırasında yazılmaz (kimin yalan söylediği sonuçtan önce günlükte görünmesin).
  `Tools/scripts/verify_logs.py` bunları toplar; Dürüst bir yalan ya da `ok=no` bulursa FAIL verir. Ayrıca her bot
  attığı her pusulayı yazar: `bottalk vote seat=… policy=… style=… ballot=Meeting|Crisis|Final … target=… richest=…
  hit=yes|no` (o oylama başladığında gerçekten en zengin olan seçenekler, sonuçların prim kaydından); `verify_logs.py`
  politika ve oylama başına kaçının gerçekten en zengine gittiğini raporlar (bilgi amaçlı; tahmin yanılabilir).
- **-calltest:** Intro ve Kriz görüşmelerini test kancası yapar (hemen açar, kaydeder, sızdırır); bot o evrelerde kendi
  araması yapmaz, kancanın görüşmesini kapatmaz, sadece konuşur. Diğer evrelerde aramaları bot açar.
- **Testler:** `Tests/Line/LineBotTalkTests.cs`: önizlemeler kararı değiştirmez ve gerçek seçimi / oyu bilir; Dürüst hiç
  yalan söylemez ve her iddia sonuçla tutarlıdır; Açgözlü aldığında iddiası hep yalandır, Karışık bazen; cümleler sadece
  botun görebildiği kişileri suçlar, yer tutucular hep dolu; motor bot işlemlerini hatasız alır; tohum aynıysa konuşma
  aynıdır; her soru düğmesine cevap gelir. `Tests/Line/LineBotDealTests.cs` (v5.1): Dürüst her seferinde cezalandırır ve
  parayı geri gönderir; Karışık'ın Açgözlü modu sadece parayla cezalandırır, parayı tutar, yazı-turasıyla ihanet eder;
  önizleme kararın kendisidir; yazı-turaları rastgele akışı bozmaz; konuşan maçlarda susma parası ödenir, Dürüst geri
  gönderir, Açgözlü modu tutar. `Tests/Line/LineBotVoteTests.cs`: defterin her kalemi (ortak, alan sayısı,
  yakalanma / ihbar / açık hırsızlık, nöbet listesi, soruşturma, masa payı, havaleler, yaramazlık maliyeti), en zengin
  seçimi ve sapma, eski kuralın aynen kalması, önizlemenin her evrede gerçek oya eşit olması, söylenen oyun değişmemesi
  (bot "oyum X" dedikten sonra X ona prim gönderse de oy X'e gider), belirlenimcilik, para cümleleri ve oy kaydı.
- **Mock'lar:** `-uimock "v5.office_botcall*"` (gelen arama, sorular, Açgözlü yalanı, Dürüst'ün ödenen sözü, reddetme,
  Kriz soruları; v5.1: `v5.office_botcall_hush` Açgözlü'nün susma parası araması ve havalesi, `v5.office_botcall_deal`
  Dürüst tanığa gönderilen paranın geri gelmesi, ikisinin de `_en` sürümü), `"v5.meeting_botchat*"` (suçlamalar, cevap, sohbet, savunma altyazısı, final oyu), `_en` sürümleri.
