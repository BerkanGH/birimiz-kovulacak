# v5 Bot konuşmaları

Durum: **[KARAR]** (2026-11-12, kullanıcı taslağı onayladı; `feature/v5-integration` üzerinde uygulandı). Kurallar
(`V5_DESIGN.md`, `V5_ENGINE_SPEC.md`) ve botların kararları (`LineBot`: Dürüst / Açgözlü / Karışık) değişmez. Bu belge
botların **ne söylediğini, ne zaman aradığını ve sözlerini nasıl tuttuğunu** anlatır.

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
| Reddederken | (her zaman açar) | "Şu an çok yoğunum." <br>*"I'm really busy right now."* | "Sonra ararım." <br>*"I'll call you back."* |
| Kapatırken | "Tamam, anlaştık. Kolay gelsin." <br>*"Okay, deal. Take care."* | "Sen hiç merak etme, canım." <br>*"Don't you worry about a thing, dear."* | "Bakalım. Görüşürüz." <br>*"We'll see. Talk later."* |
| Prim gelince | "Geldi, sağ ol. Sözüm söz." <br>*"Got it, thanks. A promise is a promise."* | "Geldi geldi, sen bir tanesin." <br>*"Got it, got it, you're one of a kind."* | "Gördüm. Tamam." <br>*"Seen it. Okay."* |

Reddetme: Açgözlü molada soyduğu kişinin (Müşteri Bonusu'nda paylaşan ortağı) **ilk** aramasını reddeder; tekrar ararsa
açar ve "Sistem hatası…" der. Karışık, söyleyecek bir derdi olmayan aramaların ~%30'unu "Sonra ararım." diyerek
reddeder ve yaklaşık yarısında gerçekten geri arar (söz).

## 3. Müşteri Bonusu (Paylaş / Hepsini al)

Soru düğmeleri: **[1] Ne seçeceksin?** *(What will you pick?)* · **[2] Paylaşalım mı?** *(Shall we share?)* ·
**[3] Sana güvenebilir miyim?** *(Can I trust you?)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "{ortak}, ben paylaşacağım. Sen de paylaşırsan ikimiz de kazanırız." <br>*"{partner}, I'm sharing. If you share too, we both win."* | "Merak etme, ben paylaşıyorum. Müşteriyi kızdırmayalım." <br>*"Don't worry, I'm sharing. Let's not upset the client."* | "Ne yapıyoruz? Paylaşmayı düşünüyorum, ama önce sen söz ver." <br>*"So what are we doing? I'm thinking of sharing, but you promise first."* |
| [1] | "Paylaş. Söz." <br>*"Share. I promise."* | "Paylaş tabii, ne sandın?" <br>*"Share, of course. What did you think?"* | "Paylaş." / "Önce sen söyle." <br>*"Share." / "You say first."* |
| [2] | "Tamam, anlaştık." <br>*"Okay, deal."* | "Anlaştık! Telefondan elini sıkıyorum." <br>*"Deal! Shaking your hand over the phone."* | "Olur. Ama hepsini alırsan toplantıda söylerim." <br>*"Fine. But if you take it all, I'll say so in the meeting."* |
| [3] | "Söz verdiysem tutarım." <br>*"If I gave my word, I keep it."* | "Bana güvenmeyeceksin de kime güveneceksin?" <br>*"If you can't trust me, who can you trust?"* | "Sen bana güvenirsen ben de sana." <br>*"You trust me and I'll trust you."* |
| Molada, ortağı hepsini aldıysa (bot arar) | "Hepsini almışsın. Toplantıda söyleyeceğim." <br>*"You took it all. I'll say so in the meeting."* | "3 prim gönder, kimseye söylemem." <br>*"Send me 3 prim and I won't tell anyone."* | "Neden yaptın? Bir açıklaman var mı?" <br>*"Why did you do it? Got an explanation?"* |
| Molada, bot hepsini aldıysa ve ortağı ararsa | (Dürüst almaz) | "Sistem hatası olmalı, ben paylaşa bastım." <br>*"Must be a system error, I pressed share."* | "Tamam, aldım. Sana 1 prim atayım, unutalım." <br>*"Fine, I took it. Let me send you 1 prim and we forget it."* |

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
| [2] | "Hayır, ihbar ederim. Kusura bakma." <br>*"No, I'd report it. Sorry."* | "1 prim gönderirsen susarım." <br>*"Send me 1 prim and I'll keep quiet."* | "2 prim. Pazarlık yok." <br>*"2 prim. No haggling."* |
| [3] | "Almayacağım ki." <br>*"But I'm not taking it."* | "1 prim, ama önce sen söz ver." <br>*"1 prim, but you promise first."* | "Alırsam 1 prim." <br>*"If I take it, 1 prim."* |
| Molada, bot tanıksa (bot arar) | "Gördüm. İhbar ediyorum, haberin olsun." <br>*"I saw it. I'm reporting it, just so you know."* | "2 prim gönder, düğmeye basmam." <br>*"Send me 2 prim and I won't press the button."* | "Bir sebebin varsa dinliyorum." <br>*"If you have a reason, I'm listening."* |
| Molada, bot hırsızsa (bot arar) | (Dürüst almaz) | "Sus, sana 1 prim gönderiyorum." <br>*"Keep quiet, I'm sending you 1 prim."* | "1 prim gönderiyorum, aramızda kalsın." <br>*"I'm sending you 1 prim, let's keep this between us."* |

**Her bot gördüğü hırsızlığı ihbar eder** (`LineBot`: tanık her politikada molada ihbar eder). Bu yüzden susma sözleri
("1 prim gönderirsen susarım", "2 prim. Pazarlık yok.", "2 prim gönder, düğmeye basmam") hiç tutulmaz: Açgözlü'de hep
yalan, Karışık'ta ise yalan yazı-turası "doğru" derse bunun yerine "Bir sebebin varsa dinliyorum." der. "Alırsam 1 prim"
(Karışık) ve "1 prim, ama önce sen söz ver" (Açgözlü) kasadan aldıysa ödenecek sözlerdir: Karışık yarı yarıya öder,
Açgözlü ödemez.

## 5. Hafta Sonu Nöbeti

Soru düğmeleri: **[1] Listeye yazacak mısın?** *(Will you sign the roster?)* · **[2] Ben yazmasam olur mu?** *(Is it OK if
I don't sign?)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "Ben yazıyorum. {K} kişi lazım, sen de yaz." <br>*"I'm signing up. We need {K}, sign up too."* <br>Yazmayacaksa: "Bu sefer ben yazmıyorum, {K} kişi lazım. Sen yazarsan sana 1 prim gönderirim." <br>*"I'm not signing up this time; we need {K}. Sign up and I'll send you 1 prim."* | "Ben yazdım bile, sıra sende." <br>*"I've already signed, your turn."* | "Kaç kişi yazdı acaba? Ben son dakikaya bakacağım." <br>*"I wonder how many signed up. I'll check at the last minute."* |
| [1] | "Evet, yazacağım." <br>*"Yes, I'll sign up."* <br>Yazmayacaksa: "Hayır, bu sefer yazmıyorum." <br>*"No, I'm not signing up this time."* | "Tabii, hafta sonu zaten boşum." <br>*"Of course, my weekend is free anyway."* | "Sen yazarsan ben de yazarım." <br>*"If you sign up, I'll sign up."* |
| [2] | "Olmaz, {K} kişi lazım. Yazmazsan görev batar." <br>*"No, we need {K}. If you don't sign, the task fails."* | "Yazma yazma, biz hallederiz." <br>*"Don't, don't, we'll handle it."* | "1 prim gönder, senin yerine ben yazarım." <br>*"Send me 1 prim and I'll sign in your place."* |
| Sonuç eksikse (bot arar) | "Yazmayanlar belli. Toplantıda konuşuruz." <br>*"We can see who didn't sign. Let's talk in the meeting."* | "Ben yazmıştım, liste mi bozuk?" <br>*"I signed it, is the roster broken?"* | "Bu sefer kim kaçtı acaba?" <br>*"I wonder who ran off this time?"* |

Dürüst, `LineBot`'un basit sayımına göre yazar: suçu en az olan Gerekli+1 kişiden biriyse yazar, değilse yazmaz. Yazmadığı
durumda "ben yazıyorum" diyemez; yazmadığını söyler ve yazan kişiye 1 prim sözü verir, liste açılınca o kişi yazdıysa
öder. Karışık yazmayacaksa ve yalan yazı-turası "doğru" derse [1] ve [2]'ye "Kaç kişi yazdı acaba?…" ile kaçamak cevap
verir. "Sonuç eksikse" araması sadece sonuç ekranında bir görüşmeye yetecek süre kaldıysa yapılır.

## 6. Kriz

Soru düğmeleri: **[1] Kime oy vereceksin?** *(Who are you voting for?)* · **[2] Bana oy verme.** *(Don't vote for me.)*

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Bot arar | "Suç tahtasında en önde {lider} var. Oyum ona." <br>*"{leader} is ahead on the suç board. My vote goes there."* <br>Oyu tahtanın liderine değilse: "Ben {hedef} diyorum. Ne yaptığını biliyorum." <br>*"I'm saying {target}. I know what they did."* | "Dinle, ikimiz de {hedef} dersek iş biter." <br>*"Listen, if we both say {target}, it's done."* | "Henüz karar vermedim. İkna et beni." <br>*"I haven't decided yet. Convince me."* |
| [1] | "{lider}. Tahtaya bak." <br>*"{leader}. Look at the board."* <br>Lider değilse: "{hedef}. Ne yaptığını biliyorum." <br>*"{target}. I know what they did."* | "{hedef}. Kesin." <br>*"{target}. Definitely."* | "Daha bilmiyorum." <br>*"I don't know yet."* |
| [2] | "Tahta ne derse o. Önde değilsen merak etme." <br>*"Whatever the board says. If you're not ahead, don't worry."* <br>Soran kişi hedefiyse ve önde değilse: "Kusura bakma, oyum sana." <br>*"Sorry, my vote is you."* | "Sana vermem, söz. 2 prim gönderirsen yemin ederim." <br>*"I won't, promise. Send me 2 prim and I'll swear on it."* | "1 prim gönder, düşünürüm." <br>*"Send me 1 prim, I'll think about it."* |

Bütün botlar aynı kuralla oy verir: bildiği bir suçlu varsa ona, yoksa suçu en yüksek olana (eşitlikte botun tohumu).
Dürüst bu yüzden bazen tahtanın liderine değil, kendi grubunda aldığını gördüğü birine oy verir; cümlesi buna göre
değişir. Açgözlü'nün {hedef}'i gerçek oyudur; hedefi karşısındaki kişiyse başka bir ad söyler (yalan) ve kendi hedefini
asla aramaz.

## 7. Toplantı (sohbet ve savunma)

| Durum | Dürüst | Açgözlü | Karışık |
|---|---|---|---|
| Mağdurken | "{hırsız} Müşteri Bonusu'nda hepsini aldı. Ben paylaşmıştım." <br>*"{thief} took the whole Client Bonus. I had shared."* | Para aldıysa: "Benim bir şikâyetim yok." <br>*"I have no complaints."* <br>Almadıysa: "{hırsız} beni soydu, herkes bilsin!" <br>*"{thief} robbed me, everyone should know!"* | "{hırsız} ile bir sorunum var. Soruşturma açabilirim." <br>*"I have a problem with {thief}. I might open an investigation."* |
| Tanıkken | "Kasadan {hırsız} aldı. Gördüm." <br>*"{thief} took from the petty cash. I saw it."* | İhbar ettiyse ve para aldıysa: "Kasadan {hırsız} aldı, ihbar ettim. Benimle bir pazarlık falan olmadı." <br>*"{thief} took from the petty cash and I reported it. There was no deal with me or anything."* <br>İhbar ettiyse, para almadıysa: "Kasadan {hırsız} aldı. İhbar ettim, herkes bilsin." <br>*"{thief} took from the petty cash. I reported it, everyone should know."* <br>İhbar etmediyse ve para aldıysa: "Ben bir şey görmedim." <br>*"I didn't see anything."* | "Kasayla ilgili bildiğim bir şey var. Soran olursa." <br>*"I know something about the petty cash. If anyone asks."* |
| Suçluyken (dikkat dağıtma) | — | "{masum} çok sessiz. Bence asıl o bir şey saklıyor." <br>*"{innocent} is very quiet. I think they're the one hiding something."* | "Hepimizin bir hatası var, değil mi?" <br>*"We all make mistakes, right?"* |
| Suçlanınca | "Ben bir şey yapmadım. Soruşturma açın, görürsünüz." <br>*"I didn't do anything. Open an investigation and you'll see."* | "Yalan! {suçlayan} benden prim istedi, vermedim diye konuşuyor." <br>*"Lies! {accuser} asked me for prim and is talking because I said no."* | Aldıysa: "Evet aldım, ama {başkası} daha fazlasını yaptı." <br>*"Yes, I took it, but {someone else} did worse."* |
| Kendi kaydı sızınca | "Evet, o benim. Söylediğimin arkasındayım." <br>*"Yes, that's me. I stand by what I said."* | "Bu kayıt bağlamından koparılmış!" <br>*"That recording is taken out of context!"* | "Hmm. Bunu açıklayabilirim." <br>*"Hmm. I can explain that."* |
| Final oyu | "Suç tahtasında lider {lider}. Oyum ona." <br>*"{leader} leads the suç board. My vote goes there."* <br>Lider değilse: "Ben {hedef} diyorum. Ne yaptığını biliyorum." | "Çekmecesi en dolu olanı kovalım, primi bize kalır." <br>*"Let's fire the one with the fullest drawer, the prim stays with us."* | "Bu sefer {hedef} diyorum." <br>*"This time I'm saying {target}."* |

- **Sıra ve tempo.** Toplantı 1: mağdur > tanık > suçlu (dikkat dağıtma). Toplantı 2 (nöbet eksikse): yazmayan Açgözlü /
  Karışık dikkat dağıtır. Final tartışması: aday olan ve almış bir bot dikkat dağıtır; yoksa toplantı 1'de söyleyemediği
  mağdur / tanık cümlesini söyler. Cümleler tartışmanın içine koltuk sırasına göre yayılır (aynı anda düşmez; sızan bir
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

Yer tutucular: {arayan} arayan kişi, {ortak} ortak, {hırsız} çalan, {masum} suçsuz biri, {suçlayan} botu suçlayan,
{başkası} başka bir suçlu, {lider} suç tahtası lideri (aynı zamanda botun gerçek oyu), {hedef} botun oy hedefi, {K}
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

## 9. Teknik

| Parça | Dosya |
|---|---|
| Cümle kataloğu, soru düğmeleri, sohbet jetonu | `Core/Line/Engine/LineBotTalk.cs` (`LineBotLines`, `BotLine`, `BotAsk`) |
| Konuşma beyni (arama, cevap, toplantı, söz, kayıt) | `Core/Line/Engine/LineBotTalker.cs` (saf C#, testli) |
| Kararların önizlemesi | `LineBot.PlannedTaskChoice`, `LineBot.PlannedVote`: aynı politika, botun rastgele durumunun kopyasıyla; kararı değiştirmez (LineSim sonuçları birebir aynı) |
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
  `Tools/scripts/verify_logs.py` bunları toplar; Dürüst bir yalan ya da `ok=no` bulursa FAIL verir.
- **-calltest:** Intro ve Kriz görüşmelerini test kancası yapar (hemen açar, kaydeder, sızdırır); bot o evrelerde kendi
  araması yapmaz, kancanın görüşmesini kapatmaz, sadece konuşur. Diğer evrelerde aramaları bot açar.
- **Testler:** `Tests/Line/LineBotTalkTests.cs`: önizlemeler kararı değiştirmez ve gerçek seçimi / oyu bilir; Dürüst hiç
  yalan söylemez ve her iddia sonuçla tutarlıdır; Açgözlü aldığında iddiası hep yalandır, Karışık bazen; cümleler sadece
  botun görebildiği kişileri suçlar, yer tutucular hep dolu; motor bot işlemlerini hatasız alır; tohum aynıysa konuşma
  aynıdır; her soru düğmesine cevap gelir.
- **Mock'lar:** `-uimock "v5.office_botcall*"` (gelen arama, sorular, Açgözlü yalanı, Dürüst'ün ödenen sözü, reddetme,
  Kriz soruları), `"v5.meeting_botchat*"` (suçlamalar, cevap, sohbet, savunma altyazısı, final oyu), `_en` sürümleri.
