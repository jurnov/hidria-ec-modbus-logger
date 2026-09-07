# Hidria EC — Modbus zapisovalnik

Windows namizna aplikacija in konzolno orodje za beleženje podatkov iz Hidria EC
ventilatorjev in poljubnih drugih Modbus naprav — prek serijske RS-485 povezave
ali Modbus TCP.

Nastala je kot generična nadgradnja obstoječe LabVIEW rešitve, ki je bila
vezana izključno na fiksno register mapo Hidria ventilatorjev. Tu register
mape niso del kode, ampak JSON profili — nova naprava na Modbus liniji (ali
oddaljena naprava prek TCP) se doda brez spreminjanja ali ponovnega
prevajanja aplikacije.

## Funkcionalnosti

- **Modbus RTU (USB-RS485) ali Modbus TCP** — izbirnik načina povezave v
  vmesniku; za RTU nastavljiva hitrost (1200–115200 baud) in format
  (8E1/8O1/8N1/8N2), za TCP IP naslov/vrata; oboje z nastavljivim timeoutom
  in številom ponovitev.
- **Konfiguracijsko vodena register mapa** — vsaka naprava ima svoj profil
  (`profiles/*.json`) z naslovi, tipi (`uint16/int16/uint32/int32/float32`),
  skalo, vrstnim redom besed in Modbus funkcijo (input/holding) **za vsak
  register posebej**. Aplikacija registre samodejno združi v čim manj
  Modbus branj, ne glede na to, kako so razpršeni po naslovnem prostoru.
- **Odpornost na izpade** — nedosegljiva naprava (timeout) ne prekine branja
  preostalih naprav na liniji; USB-RS485 pretvornik oz. TCP povezava se po
  izpadu samodejno znova poveže.
- **Dodajanje/urejanje/odstranjevanje naprav** neposredno v vmesniku — brez
  ročnega urejanja JSON datotek. Urejanje register mape ene naprave ne
  vpliva na druge naprave, ki uporabljajo isti profil (samodejno se
  razcepi v zasebno kopijo); na voljo so tudi eksplicitni ukazi Naloži
  profil / Shrani kot profil / Izbriši profil za namensko ponovno uporabo.
- **Ločena hitrost komunikacije in beleženja** — "Čas vzorčenja" določa,
  kako pogosto aplikacija dejansko bere naprave (urejljiv tudi med tekom),
  "Interval zapisa" pa, kako pogosto se zadnji vzorec zapiše v CSV/MySQL.
- **CSV beleženje**, ena datoteka na napravo na dan, z jasno vidnimi izpadi
  komunikacije v podatkih.
- **MySQL beleženje** (dodatno k CSV, lahko obe hkrati) — poveže se na
  obstoječo tabelo, prebere njene stolpce in omogoči, da uporabnik vsak
  stolpec poveže s poljubnim podatkom (čas, ime naprave, vrednost
  poljubnega registra, zastavica napake komunikacije, ali fiksna
  konstanta).
- Nastavitve beleženja (CSV/MySQL/interval zapisa) so med tekom
  zapisovalnika zaklenjene, da spremembe niso videti uveljavljene, a se v
  resnici ne uporabijo — za spremembo je treba ustaviti in znova zagnati.
- **Poimenovani profili** — trenutno stanje (naprave, povezava, beleženje)
  se lahko shrani pod poljubnim imenom in kasneje znova naloži prek
  izbirnika; aplikacija si zapomni nazadnje naloženega med zagoni (ob
  povsem prvem zagonu se ne naloži nič samodejno).
- **Pregledovalnik Modbus prometka (HEX)** — surovi poslani zahtevki in
  prejeti odgovori (RTU ali TCP), z natančnim časom pošiljanja in prejema;
  uporabno za diagnostiko komunikacijskih težav.
- **Premikanje naprav v seznamu** — gumba ▲/▼ premakneta izbrano napravo
  za eno mesto navzgor/navzdol; vrstni red se trajno shrani.
- **Večjezični vmesnik** — slovenščina, angleščina, nemščina, italijanščina,
  španščina; izbirnik jezika v orodni vrstici, popolnoma prevedeno
  (vključno z dnevnikom dogodkov in sporočili o napakah).
- **Navodila za uporabo** — gumb "?" poleg izbirnika jezika odpre HTML
  navodila v privzetem brskalniku, v trenutno izbranem jeziku.
- **Namestitveni program** (`ModbusLoggerSetup.exe`) — namestitev brez
  skrbniških pravic, z že pripravljenim začetnim profilom.
- Celostna podoba Hidria (barve in logotip s hidria.com).

## Struktura projekta

```
src/
  ModbusLogger.Core/   knjižnica: Modbus komunikacija (RTU/TCP), konfiguracija, CSV/MySQL, HEX promet,
                       Strings.resx (sl) + Strings.{en,de,it,es}.resx (prevodi vmesnika)
  ModbusLogger.App/    WPF namizna aplikacija (glavni uporabniški vmesnik), docs/help-*.html (navodila)
  ModbusLogger.Cli/    konzolno orodje — isto jedro, brez GUI (za headless/strežniški zagon)
config/
  devices.json         povezava, naprave, čas vzorčenja/interval zapisa, beleženje
  profiles/*.json       register mape posameznih naprav (v profiles/_private/ tiste, ki so last ene naprave)
logs/                  CSV izhod (ni v git repozitoriju, nastane ob teku)
dist/                  pripravljen paket za deljenje (exe + začetni config + navodila), izhodišče za installer
installer/             izvorna skripta namestitvenega programa (Inno Setup)
installer-output/      zgrajen ModbusLoggerSetup.exe (ni v git repozitoriju)
```

## Gradnja in zagon

Potreben je [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build ModbusLogger.sln
```

**GUI aplikacija:**
```bash
dotnet run --project src/ModbusLogger.App
```

**Konzolno orodje** (npr. za strežnik brez grafičnega vmesnika):
```bash
dotnet run --project src/ModbusLogger.Cli -- --once
```

**Samostojen .exe** (brez potrebe po .NET SDK na tarčnem računalniku, le
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)):
```bash
dotnet publish src/ModbusLogger.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish-app
```

**Namestitveni program** (potreben [Inno Setup 6](https://jrsoftware.org/isinfo.php)):
```bash
# najprej osveži dist/ModbusLogger.App.exe iz zgornjega publish koraka, nato:
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ModbusLogger.iss
```
Rezultat je `installer-output\ModbusLoggerSetup.exe` — namestitev je
per-user (brez skrbniških pravic), ker aplikacija piše nastavitve in CSV
loge neposredno ob sebi.

## Konfiguracija

Ob povsem prvem zagonu aplikacija ne naloži ničesar samodejno — profil
izbereš sam prek gumba **Naloži profil**. Naprave, profile in nastavitve
povezave/beleženja lahko urejaš prek vmesnika — ročno urejanje JSON
datotek ni potrebno.

Primer profila naprave (`config/profiles/*.json`):

```json
{
  "name": "moja-naprava",
  "pollGroups": [
    { "function": "input", "startAddress": "0x0000", "count": 4 }
  ],
  "registers": [
    { "address": "0x0000", "name": "Napetost", "unit": "V", "type": "float32", "function": "input" }
  ]
}
```

Izsek nastavitev povezave in beleženja v `devices.json` (Modbus TCP + MySQL):

```json
{
  "connectionType": "tcp",
  "tcp": { "host": "192.168.1.50", "port": 502, "timeoutMs": 1000, "retries": 2 },
  "sampleIntervalSeconds": 5,
  "writeIntervalSeconds": 60,
  "mySql": {
    "enabled": true,
    "host": "localhost", "port": 3306, "database": "meritve", "table": "ventilatorji",
    "columnMapping": { "ts": "__timestamp__", "device": "__device__", "temp": "reg:Ambient Temp" }
  }
}
```

## Znane omejitve

- Shranjevanje konfiguracije (`ConfigLoader.Save`) v celoti prepiše
  `devices.json` iz objektnega modela — ročno vpisani `//` komentarji v
  datoteki se ob tem izgubijo.
- MySQL beleženje piše v **obstoječo** tabelo (aplikacija je ne ustvari) in
  vedno z enim INSERT-om na napravo na cikel zapisa — brez agregacije
  (povprečja, min/max) med cikli.
- Namestitveni program (čarovnik Inno Setup) je trenutno samo v
  angleščini; sama aplikacija je večjezična (glej zgoraj).

## Zgodovina razvoja

Podrobna kronologija vseh faz je v [CHANGELOG.md](CHANGELOG.md).
