# Opis implementacije sistema preporuke GymLink

*Objašnjivi hibridni sistem preporuke teretana i trenera*

## 1. Uvod

U okviru aplikacije GymLink implementiran je kompletan hibridni sistem preporuke koji prijavljenom članu predlaže teretane i trenere. Sistem nije zasnovan na unaprijed definisanim ili ručno dodijeljenim rezultatima, nego rang-listu izračunava na osnovu stvarnih korisničkih preferencija, prethodnih aktivnosti i ukupne popularnosti dostupnih kandidata.

Algoritam nosi verziju `gymlink-hybrid-v1`. Verzija algoritma čuva se uz svaki generisani rezultat, čime je omogućeno naknadno mijenjanje formule bez miješanja preporuka nastalih primjenom različitih verzija sistema.

Sistem se koristi putem endpointa `GET /api/me/preferences`, `PUT /api/me/preferences`, `GET /api/me/recommendations` i `POST /api/me/recommendations/refresh`. Svi endpointi zaštićeni su `MemberSelf` autorizacijskom politikom. Identitet člana određuje se iz validiranog JWT tokena i nije ga moguće odabrati kroz URL ili tijelo zahtjeva.

## 2. Hibridni model preporuke

GymLink koristi deterministički hibridni algoritam sastavljen od tri komponente: podudaranja sa eksplicitnim preferencijama korisnika, personalizacije na osnovu prethodnih aktivnosti i popularnosti kandidata na nivou cijelog sistema.

```text
konačnaOcjena =
    0,50 × preference
  + 0,30 × ličnaAktivnost
  + 0,20 × popularnost
```

Svaka komponenta normalizovana je na interval `[0, 1]`, pa se i konačna ocjena uvijek nalazi između nule i jedan. Ako član još nema preference ili relevantne aktivnosti, nedostajuća komponenta ne tretira se kao rezultat nula. Njena težina uklanja se iz brojioca i nazivnika, a konačna vrijednost ponovo se normalizuje koristeći samo dostupne informacije.

```text
konačnaOcjena =
    zbir dostupnih ponderisanih komponenti
    / zbir njihovih težina
```

Ovakav pristup rješava problem hladnog početka. Novi član i bez historije odmah dobija korisne preporuke na osnovu popularnosti, dok sistem postaje sve personalizovaniji nakon unošenja preferencija i korištenja aplikacije.

## 3. Komponenta korisničkih preferencija

Član može definisati najviše tri rangirane preference. Svaka preferenca sastoji se od željenog grada i željenog tipa treninga. Težine određuje server: glavna preferenca ima težinu `1,0`, druga `0,7`, a treća `0,4`. Mobilna aplikacija šalje samo izabrani grad i tip treninga, pa korisnik ne može samostalno mijenjati težine algoritma.

```text
podudaranjeProfila =
    0,40 × podudaranjeGrada
  + 0,60 × podudaranjeTipaTreninga
```

Podudaranje ima vrijednost jedan ako kandidat odgovara izabranom kriteriju, odnosno nula ako ne odgovara. Tip treninga ima veću težinu jer predstavlja direktniji signal interesovanja člana.

```text
preference =
    Σ(težinaProfila × podudaranjeProfila)
    / Σ(težinaProfila)
```

Kod teretana se koriste grad i svi tipovi treninga koje teretana nudi. Kod trenera se koriste grad pripadajuće teretane i tipovi treninga povezani sa profilom trenera. Prilikom čuvanja server provjerava da su svi gradovi i tipovi treninga aktivni, odbija duplikate i zamjenjuje preference u serijalizovanoj transakciji.

## 4. Personalizacija na osnovu aktivnosti

Sistem evidentira stvarne aktivnosti člana i dodjeljuje im težine prema jačini
namjere: pregled teretane ili trenera ima težinu `1`, zahtjev za članstvo `3`,
kreiranje rezervacije `4`, aktivacija članstva `5`, a završetak rezervacije i
kreiranje recenzije imaju težinu `6`.

- Pregled teretane ili trenera — težina `1`.
- Zahtjev za članstvo — težina `3`.
- Kreiranje rezervacije — težina `4`.
- Aktivacija članstva — težina `5`.
- Završetak rezervacije ili kreiranje recenzije — težina `6`.

Uticaj aktivnosti postepeno opada kako aktivnost postaje starija. Koristi se eksponencijalno vremensko slabljenje sa poluživotom od 60 dana.

```text
vremenskiPonder = osnovnaTežina × 0,5^(starostUDanima / 60)
```

Aktivnost stara 60 dana ima polovinu početne vrijednosti, dok novije aktivnosti imaju veći uticaj. Budući događaji se ignorišu. Aktivnost usmjerena prema treneru doprinosi i njegovoj teretani sa 50% vrijednosti, čime se modelira veza između interesa za trenera i interesa za teretanu u kojoj radi. Rezultati aktivnosti za teretane i trenere normalizuju se odvojeno.

Signali običnog pregleda dedupliciraju se u periodu od 15 minuta kako često otvaranje istog ekrana ne bi nepravedno povećavalo rezultat kandidata. Poslovni događaji koriste izvorni identifikator i jedinstveno ograničenje u bazi, čime su kreiranje članstva, rezervacije i recenzije idempotentni signali.

## 5. Komponenta popularnosti

Popularnost kandidata sastoji se od kvaliteta ocjena, broja rezervacija i ukupne aktivnosti svih članova.

```text
popularnost =
    0,50 × kvalitetOcjena
  + 0,30 × brojRezervacija
  + 0,20 × ukupnaAktivnostKorisnika
```

### 5.1. Bayesova korekcija kvaliteta ocjena

Običan prosjek može nepravedno favorizovati kandidata koji ima samo jednu visoku ocjenu. GymLink zato koristi Bayesovu korekciju sa početnim prioritetom od pet ocjena i ponderisanim globalnim prosjekom svih aktivnih kandidata.

```text
korigovanaOcjena =
    ((prosjek × brojOcjena) + (globalniProsjek × 5))
    / (brojOcjena + 5)

kvalitetOcjena = korigovanaOcjena / 5
```

Kandidat sa malim brojem recenzija ostaje bliže globalnom prosjeku, dok kandidati sa većim brojem ocjena postepeno dobijaju rezultat koji više odgovara njihovom stvarnom prosjeku.

### 5.2. Broj rezervacija

Posmatraju se potvrđene i završene rezervacije u prethodnih 180 dana. Broj rezervacija normalizuje se logaritamski, čime se sprečava da nekoliko najpopularnijih kandidata potpuno dominira rang-listom.

```text
brojRezervacija =
    ln(1 + rezervacijeKandidata)
    / ln(1 + najvećiBrojRezervacija)
```

### 5.3. Ukupna aktivnost korisnika

Sistem koristi ciljane aktivnosti svih članova kao kolektivni signal interesovanja. Primjenjuju se iste težine događaja i isto vremensko slabljenje kao kod lične aktivnosti. Rezultat se logaritamski normalizuje odvojeno za teretane i trenere. Na ovaj način dobija se signal opće popularnosti bez izlaganja privatnih podataka drugih korisnika.

## 6. Izbor kandidata i sigurnost podataka

Sistem razmatra samo trenutno dostupne kandidate. Teretana mora pripadati aktivnom tenantu i biti javno vidljiva. Trener mora imati aktivan profil i aktivan korisnički račun te pripadati javno vidljivoj teretani aktivnog tenanta.

Preporuke mogu obuhvatiti teretane iz različitih tenant prostora, ali se koriste isključivo javni podaci: naziv, grad, slika, tipovi treninga i agregirane ocjene. Privatni podaci članova, trenera i administracije nikada se ne uključuju u rezultat. Kandidati se ponovo provjeravaju i prilikom čitanja sačuvanih rezultata, pa se deaktivirana teretana ili trener neće prikazati ni ako je ranije bio preporučen.

## 7. Rangiranje, balansiranje i čuvanje rezultata

Kandidati se sortiraju prema konačnoj ocjeni opadajuće, zatim prema nazivu i identifikatoru rastuće. Dodatni kriteriji omogućavaju potpuno determinističan poredak kada dva kandidata imaju isti rezultat.

Sistem čuva najviše 20 teretana i 20 trenera po članu. Prilikom vraćanja rezultata pokušava se ostvariti približno jednak broj obje kategorije. Ako u jednoj kategoriji nema dovoljno kandidata, preostala mjesta popunjavaju se najboljim kandidatima druge kategorije.

Generisani rezultat sadrži korisnika, tenant kandidata, tip i identifikator cilja, konačnu ocjenu, verziju algoritma, vrijeme generisanja i objašnjenje. Zamjena rezultata izvršava se u serijalizovanoj transakciji, uz zaključavanje generisanja po korisniku. Time se sprečavaju dupli rezultati i djelimično ažurirane rang-liste.

Baza podataka dodatno osigurava jedinstven rezultat po kombinaciji korisnika, tipa i identifikatora cilja te ograničava ocjenu na interval `[0, 1]`.

## 8. Automatsko osvježavanje preporuka

Preporuke se automatski ponovo generišu kada korisnik još nema rezultate, kada su rezultati stariji od 24 sata, kada pripadaju starijoj verziji algoritma ili kada su preference ili aktivnosti novije od posljednjeg generisanja.

Korisnik može ručno osvježiti ekran povlačenjem prema dolje. Mobilna aplikacija tada poziva endpoint za prisilno regenerisanje rezultata. Nakon uspješnog čuvanja novih preferencija postojeće preporuke se uklanjaju i odmah se učitava nova personalizovana rang-lista.

## 9. Objašnjivost preporuka

Svaka preporuka sadrži kratak razlog na bosanskom jeziku. Razlog se bira prema komponenti koja je dala najveći ponderisani doprinos konačnom rezultatu. Ako dominira lična aktivnost, korisniku se prikazuje razlog vezan za ranije aktivnosti i rezervacije. Ako dominiraju preference, prednost ima podudaranje tipa treninga, a zatim lokacije. Kada je popularnost najvažnija, prikazuje se razlog vezan za kvalitet ocjena ili opću popularnost.

- „Slično vašem preferiranom tipu treninga.“
- „Odgovara vašoj preferiranoj lokaciji.“
- „Preporučeno na osnovu vaših ranijih aktivnosti i rezervacija.“
- „Visoko ocijenjeno među članovima GymLinka.“
- „Popularan izbor na GymLinku.“

Mobilna aplikacija odbacuje neispravne rezultate bez objašnjenja. Time objašnjenje nije opcionalni tekst, nego obavezan dio ugovora između servera i mobilne aplikacije.

## 10. Primjer izračuna

Pretpostavimo da član ima glavnu preferencu Sarajevo/Snaga, a posmatrana teretana nalazi se u Sarajevu i nudi trening snage. Preference rezultat tada iznosi `1,00`. Pretpostavimo i da je normalizovana lična aktivnost `0,50`, kvalitet ocjena `0,80`, rezultat rezervacija `0,60`, a ukupna aktivnost svih korisnika `0,40`.

```text
popularnost =
    0,50 × 0,80
  + 0,30 × 0,60
  + 0,20 × 0,40
  = 0,66

konačnaOcjena =
    0,50 × 1,00
  + 0,30 × 0,50
  + 0,20 × 0,66
  = 0,782
```

Preference komponenta doprinosi sa `0,50`, lična aktivnost sa `0,15`, a popularnost sa `0,132`. Pošto su preference dale najveći doprinos, korisniku će biti prikazan razlog vezan za preferirani tip treninga.

Ako isti član nema unesene preference, dostupne ostaju aktivnosti i popularnost. Njihove težine se ponovo normalizuju:

```text
(0,30 × 0,50 + 0,20 × 0,66) / 0,50 = 0,564.
```

Kandidat zato nije nepravedno kažnjen zbog nedostajuće preference.

## 11. Mobilna aplikacija

Mobilni ekran prikazuje šest najbolje rangiranih preporuka. Svaka kartica sadrži sliku teretane ili trenera, naziv, dodatni opis, prosječnu ocjenu, broj recenzija, objašnjenje preporuke i dugme za otvaranje detalja ili rezervaciju treninga.

Na dnu ekrana prikazuje se sažetak aktivnosti člana: najčešći tip treninga, prosječan broj rezervacija sedmično i preferirani grad. Član može otvoriti editor, dodati do tri preference i promijeniti njihov prioritet povlačenjem stavki. Server nakon čuvanja dodjeljuje fiksne težine prema redoslijedu.

## 12. Testiranje i provjera ispravnosti

Algoritam je pokriven jediničnim, integracijskim i Flutter testovima. Jedinični testovi provjeravaju podudaranje grada i tipa treninga, težine rangiranih preferencija, sve težine događaja, vremensko slabljenje sa poluživotom od 60 dana, Bayesovu korekciju, logaritamsku normalizaciju, hladni početak, balans kategorija i izbor objašnjenja.

Integracijski test koristi stvarnu SQL Server bazu i API. Test prvo generiše početnu rang-listu bez preference, zatim sprema grad i tip treninga koji odgovaraju slabije rangiranoj teretani. Nakon regenerisanja provjerava da su ocjena i rang te teretane povećani, da je vrijeme generisanja novije i da razlog spominje preferirani tip treninga. Time se dokazuje da preference stvarno utiču na rezultat, a ne samo da se čuvaju u bazi.

Flutter testovi provjeravaju API ugovor, prikaz teretana i trenera, obavezno objašnjenje, ručno osvježavanje, rad na uskom ekranu i očuvanje unesenih vrijednosti nakon serverske greške.

## 13. Zaključak

GymLink implementira objašnjiv i determinističan hibridni sistem preporuke koji kombinuje eksplicitne preference, vremenski ponderisanu stvarnu aktivnost i statistički korigovanu popularnost. Sistem pruža kvalitetan hladni početak, reaguje na promjene ponašanja korisnika, sprečava dominaciju kandidata sa malim brojem ocjena, čuva rezultate transakcijski i uz svaku preporuku daje razumljiv razlog.

Pored same formule, implementacija obuhvata sigurnu selekciju kandidata kroz više tenant prostora, idempotentno evidentiranje aktivnosti, automatsko osvježavanje, verzionisanje algoritma, integritet baze podataka i automatizovane testove koji potvrđuju stvarni uticaj signala na rang-listu.
