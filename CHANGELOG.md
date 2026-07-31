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
