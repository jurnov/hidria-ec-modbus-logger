# Changelog

Kronološki povzetek razvoja aplikacije, od prvega prototipa do trenutnega stanja.

## Faza 1 — Prototip Modbus RTU komunikacije

- Konzolna aplikacija (`ModbusLogger.Cli`) z knjižnico **NModbus** prek
  USB-RS485 vodila.
- Branje bloka input registrov `0xD100`–`0xD119` z enega Hidria EC
  ventilatorja, dekodiranje tipov (uint16/int16, skaliranje), izpis v
  konzolo.
- Register mapa je bila v tej fazi še statična tabela v kodi.

## Faza 2 — Konfiguracijsko vodena register mapa

- Register mape preseljene iz kode v JSON profile naprav
  (`config/profiles/*.json`) — nova naprava se doda brez spreminjanja kode.
- `devices.json`: nastavitve povezave + seznam naprav na Modbus liniji.
- `ConfigLoader` z validacijo (naslovni razponi, Modbus omejitev 125
  registrov na branje, pokritost registrov s poll skupinami, jasna
  sporočila o napakah z navedbo vrstice).

## Faza 3 — CSV beleženje in odpornost

- `PollService`: ciklično branje vseh naprav, z ločenim try/catch na
  napravo — ena nedosegljiva naprava ne prekine branja ostalih.
- Samodejno ponovno odpiranje porta po izpadu USB-RS485 pretvornika.
- CSV zapisovalnik (`CsvLogSink`), ena datoteka na napravo na dan; izpadi
  komunikacije se v CSV vidijo kot vrstice z opisom napake.

## Faza 4 — WPF grafični vmesnik

- Namizna aplikacija (`ModbusLogger.App`, MVVM) z seznamom naprav, tabelo
  registrov v živo, zagon/ustavitev beleženja.
- **Popravek**: sesutje ob zagonu zaradi `ScrollIntoView` klicanega
  neposredno znotraj `CollectionChanged` (nekonsistenten
  `ItemContainerGenerator` pri več hitro zaporednih vrsticah dnevnika) —
  popravljeno z odlogom prek `Dispatcher.BeginInvoke`.

## Nastavitve povezave in dinamika

- Interval vzorčenja se lahko spremeni **med tekom** in začne veljati
  takoj, brez ustavitve.
- Izbirnik standardnih hitrosti (1200–115200 baud) in Modbus okvirjev
  (8E1/8O1/8N1/8N2) namesto ločenih polj za pariteto/stop bite.

## Celostna podoba

- Barve in logotip prevzeti z uradne strani www.hidria.com (temno modra →
  tirkizna, poudarjena modra povezav).
- Ikona okna in `.exe`, stilizirani gumbi, naslovi skupin v barvi znamke.

## Urejanje naprav v vmesniku

- Dialog **Dodaj napravo**: izbira obstoječega profila ali ustvarjanje
  novega profila (urejevalnik register mape) neposredno v vmesniku.
- **Popravek**: napaka "cyclical dependency" pri dveh povezanih
  `RadioButton` v isti `GroupName` (WPF posebnost) — zamenjano z enim
  `CheckBox`.
- **Popravek**: `ItemsSource` stolpcev `DataGridComboBoxColumn` (Tip, Word
  order, Funkcija) vezan prek `ElementName`/`x:Reference` se ni zanesljivo
  razrešil (stolpci niso del vizualnega drevesa) — zamenjano s statičnimi
  seznami prek `x:Static`.
- **Uredi** in **Odstrani** napravo, z ohranjanjem profilnih datotek na
  disku ob odstranitvi naprave.
- Preverjena odpornost: nedosegljiva naprava (pred ali za delujočo v
  seznamu) ne prepreči branja ostalih naprav.

## Poimenovane konfiguracije

- **Shrani konfiguracijo** (z izbiro imena) in **Naloži konfiguracijo**
  (izbirnik med shranjenimi) namesto enega samega `devices.json`.
- Vse poimenovane konfiguracije si delijo isto mapo `profiles/`.

## Register mapa: več blokov in funkcija na register

- Samodejno združevanje registrov v **čim manj Modbus branj**, ne glede na
  to, kako so razpršeni po naslovnem prostoru (prej: en blok od
  najmanjšega do najvišjega naslova, kar je hitro preseglo omejitev 125
  registrov).
- Vsak register ima svojo Modbus funkcijo (input/holding), ne le profil kot
  celota — podpora napravam z mešanim branjem.

## Grafični prikaz

- Preklop med tabelo in grafom registrov (knjižnica **OxyPlot**).
- Več parametrov hkrati, z ločeno levo/desno Y-osjo (npr. hitrosti in
  temperature na različnih skalah) in legendo.
- Izbirno časovno okno (1 min – 1 h – vse).

## Pregledovalnik Modbus prometka (HEX)

- Surovi poslani zahtevki in prejeti odgovori v HEX zapisu, z ločenima
  časoma pošiljanja in prejema.
- Implementirano z ovojnico okoli `IStreamResource` (`LoggingStreamResource`),
  ki prestreže natanko tiste bajte, ki jih NModbus dejansko pošlje/prejme —
  brez spremembe dejanske komunikacije.
- Večkosovni odgovori (NModbus jih lahko prebere v več `Read` klicih) se
  pravilno sestavijo v en zapis; brez odgovora (timeout) je prikazano
  ločeno.

## Popravek zaznavanja izpada USB-RS485 pretvornika

- **Popravek**: po fizičnem izklopu pretvornika je `SerialPort.IsOpen`
  ostajal `true` (dokumentirana omejitev .NET/Windows), zato so lučke
  naprav dlje časa ostajale zelene kljub timeoutom v HEX pregledu —
  dodano dodatno preverjanje dejanskega stanja vodila (`BytesToRead`
  v `try/catch`), ki na "mrtvem" portu zanesljivo vrže izjemo.

## Urejanje profilov: ločeno od naprav, ki jih delijo

- Urejanje register mape ene naprave ne spreminja več tiho profila, ki ga
  uporablja tudi druga naprava — če je profil deljen, se sprememba samodejno
  "razcepi" v zasebno kopijo za urejeno napravo.
- Eksplicitni ukazi **Naloži profil**, **Shrani kot profil** (pravi Windows
  dialog za shranjevanje, ukoreninjen v mapi `profiles/`) in **Izbriši
  profil** (z opozorilom, če ga uporablja še katera druga naprava).

## Nastavitve beleženja v ločenem oknu

- Interval vzorčenja, "Beleži v CSV", mapa/ločilo/decimalno ločilo CSV
  datotek premaknjeni iz glavnega okna v ločeno okno **Nastavitve
  beleženja**, dostopno prek gumba v orodni vrstici.
- Prikaz polno razrešene poti do CSV mape v živo, z gumbom **Odpri mapo**.
- Glavna gumba v orodni vrstici preimenovana **Naloži profil**/**Shrani
  profil** (bilo "konfiguracijo"), enotno po celotnem vmesniku.

## Samodejno pomnjenje nazadnje naložene konfiguracije

- Ob prvem zagonu aplikacija ne naloži ničesar samodejno — uporabnik izbere
  profil sam prek **Naloži profil** (prej se je vedno poskusila naložiti
  privzeta `config/devices.json`).
- Pot do nazadnje uspešno naložene/shranjene konfiguracije se zapomni med
  zagoni (`%AppData%\ModbusLogger\last-config.txt`, ločeno od samih
  konfiguracijskih datotek) — ob naslednjem zagonu se naloži samodejno.
- Gumb "Naloži profil" je zdaj vedno na voljo, tudi preden je karkoli
  naloženo.

## Modbus TCP, poleg RTU

- Izbirnik načina povezave (**Serijska (RS-485)** / **Modbus TCP**) nad
  nastavitvami povezave; glede na izbiro se prikažejo ustrezna polja
  (COM port/hitrost/format oz. IP naslov/vrata).
- Enotna arhitektura: `DeviceReader.Read` in vsa logika dekodiranja
  registrov delujeta nespremenjeno na obeh povezavah (`IModbusMaster`),
  Slave ID pri TCP ustreza Unit ID.
- Zaznavanje prekinjene TCP povezave (Poll/Available trik na golem
  socketu) in samodejno ponovno vzpostavljanje, analogno USB-RS485.
- HEX pregled prometka prikazuje tudi Modbus TCP pakete (z MBAP glavo).

## Odstranitev grafičnega prikaza

- Preklop tabela/graf, izbirnik parametrov in časovnega okna ter knjižnica
  **OxyPlot** odstranjeni — na voljo ostane samo tabela s trenutnimi
  vrednostmi registrov izbrane naprave.

## MySQL beleženje (dodatno k CSV)

- V "Nastavitve beleženja" nova možnost **Beleži v MySQL**, neodvisna od
  CSV (obe sta lahko omogočeni hkrati).
- Gumb **Preberi stolpce iz tabele** prebere imena stolpcev obstoječe
  tabele (`INFORMATION_SCHEMA.COLUMNS`); za vsak stolpec uporabnik izbere
  vir podatka: čas meritve, ime naprave, Slave ID, status/napaka, čas
  odziva, zastavica napake komunikacije (1/0), vrednost poljubnega
  registra (po imenu) ali poljubna **konstantna vrednost**.
- En INSERT na napravo na cikel zapisa (enaka zrnatost kot CSV) v skupno
  tabelo; stolpci brez razpoložljivega vira za trenutno napravo ostanejo
  prazni (NULL).
- **Popravek**: tiha izpustitev vrstice, kadar noben mapiran stolpec ni
  imel razpoložljive vrednosti, zdaj vidno javi v dnevnik dogodkov.

## Namestitveni paket

- Namestitveni program (Inno Setup) `ModbusLoggerSetup.exe` — namestitev
  brez skrbniških pravic (per-user, `%LocalAppData%\Programs\ModbusLogger`),
  ker aplikacija piše nastavitve/loge neposredno ob sebi.
- Ob namestitvi doda že pripravljen `devices.json` z eno napravo ("Hidria
  ventilator", profil `hidria-ec-fan") — samo, če taka datoteka še ne
  obstaja, tako da nadgradnja nikoli ne prepiše žive konfiguracije; te
  datoteke tudi niso odstranjene ob deinstalaciji.
- Bližnjica v start meniju (in po želji na namizju), preverjanje
  prisotnosti .NET 8 Desktop Runtime ob zagonu namestitve.

## Ločen čas vzorčenja in interval zapisa; zaklepanje med tekom

- Prejšnji enotni "interval vzorčenja" razdeljen na dva pojma: **Čas
  vzorčenja** (kako pogosto se dejansko komunicira z napravami; ostaja
  urejljiv tudi med tekom, v panelu "Povezava") in **Interval zapisa**
  (kako pogosto se zadnji vzorec zapiše v CSV/MySQL; v "Nastavitve
  beleženja").
- Celotno okno "Nastavitve beleženja" (CSV, MySQL, interval zapisa) je
  zdaj zaklenjeno, dokler zapisovalnik teče, z vidnim opozorilom — prej so
  spremembe med tekom tiho ostale brez učinka do naslednjega zagona.

## Popravek statusa naprav ob ustavitvi

- **Popravek**: ob kliku "USTAVI" je lučka naprave ostala zelena z zadnjim
  "OK (x ms)" stanjem, kar je dajalo vtis, da naprava še vedno komunicira
  — zdaj se ob ustavitvi lučka obarva sivo, besedilo pa spremeni v "brez
  povezave — ustavljeno".

## MySQL beleženje: konstanta po napravi in oznaka napake

- Dodana možnost "Konstantna vrednost" pri mapiranju MySQL stolpca —
  fiksno besedilo, vpisano neposredno v nastavitvah beleženja, na voljo v
  vsaki vrstici ne glede na napravo.
- Dodano polje "Napaka komunikacije (1/0)" med viri podatka za MySQL
  stolpec — 1 ob neuspešnem branju, 0 ob uspešnem.

## Konstantni "register" v profilu naprave

- V urejevalniku register mape nova funkcija **constant**: register, ki
  se nikoli ne bere z Modbus vodila, ampak ima fiksno, ročno vpisano
  vrednost (nov stolpec "Konstanta"). Ker je vsak profil last ene naprave,
  ima lahko vsaka naprava svojo vrednost (npr. št. linije/lokacije), ki se
  nato v MySQL nastavitvah poveže na poljuben stolpec enako kot pravi
  register.
- Naslov takega registra se nikjer ne izpiše (ni pravega naslova) — polje
  je sivo, tako v urejevalniku naprave kot v tabeli trenutnih vrednosti.

## Popravek tipa registrov hitrosti

- **Popravek**: registri "Target Speed", "Actual Speed" in "Calculated
  Speed" v profilu Hidria EC ventilatorja so bili `uint16`, spremenjeni v
  `int16` (vsi obstoječi profili ventilatorjev na disku).
