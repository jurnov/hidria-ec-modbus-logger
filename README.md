# Hidria EC — Modbus RTU zapisovalnik

Windows namizna aplikacija in konzolno orodje za beleženje podatkov iz Hidria EC
ventilatorjev in poljubnih drugih Modbus RTU naprav na isti RS485 liniji.

Nastala je kot generična nadgradnja obstoječe LabVIEW rešitve, ki je bila
vezana izključno na fiksno register mapo Hidria ventilatorjev. Tu register
mape niso del kode, ampak JSON profili — nova naprava na Modbus liniji se doda
brez spreminjanja ali ponovnega prevajanja aplikacije.

## Funkcionalnosti

- **Modbus RTU prek USB-RS485**, poljubna hitrost (1200–115200 baud) in format
  (8E1/8O1/8N1/8N2), nastavljiv timeout in število ponovitev.
- **Konfiguracijsko vodena register mapa** — vsaka naprava ima svoj profil
  (`profiles/*.json`) z naslovi, tipi (`uint16/int16/uint32/int32/float32`),
  skalo, vrstnim redom besed in Modbus funkcijo (input/holding) **za vsak
  register posebej**. Aplikacija registre samodejno združi v čim manj
  Modbus branj, ne glede na to, kako so razpršeni po naslovnem prostoru.
- **Odpornost na izpade** — nedosegljiva naprava (timeout) ne prekine branja
  preostalih naprav na liniji; USB-RS485 pretvornik se po izpadu samodejno
  znova poveže.
- **Dodajanje/urejanje/odstranjevanje naprav** neposredno v vmesniku — brez
  ročnega urejanja JSON datotek. Nov profil (nova vrsta naprave) se lahko
  ustvari kar v istem dialogu, z urejevalnikom register mape.
- **CSV beleženje**, ena datoteka na napravo na dan, z jasno vidnimi izpadi
  komunikacije v podatkih.
- **Poimenovane konfiguracije** — trenutno stanje (naprave, povezava,
  interval, beleženje) se lahko shrani pod poljubnim imenom in kasneje znova
  naloži prek izbirnika.
- **Grafični prikaz v živo** — poljubno število parametrov hkrati, ločena
  leva/desna Y-os (npr. hitrosti in temperature na različnih skalah),
  izbirno časovno okno (1 min – vse).
- **Pregledovalnik Modbus prometka (HEX)** — surovi poslani zahtevki in
  prejeti odgovori, z natančnim časom pošiljanja in prejema; uporabno za
  diagnostiko komunikacijskih težav.
- Celostna podoba Hidria (barve in logotip s hidria.com).

## Struktura projekta

```
src/
  ModbusLogger.Core/   knjižnica: Modbus komunikacija, konfiguracija, CSV, HEX promet
  ModbusLogger.App/    WPF namizna aplikacija (glavni uporabniški vmesnik)
  ModbusLogger.Cli/    konzolno orodje — isto jedro, brez GUI (za headless/strežniški zagon)
config/
  devices.json         povezava, naprave, interval, beleženje
  profiles/*.json       register mape posameznih naprav
logs/                  CSV izhod (ni v git repozitoriju, nastane ob teku)
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

## Konfiguracija

Ob prvem zagonu aplikacija poišče `config/devices.json` v mapi ob sebi (ali
nadrejenih mapah). Naprave, profile in nastavitve povezave lahko urejaš prek
vmesnika (Dodaj/Uredi/Odstrani napravo, Shrani/Naloži konfiguracijo) — ročno
urejanje JSON datotek ni potrebno.

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

## Znane omejitve

- Shranjevanje konfiguracije (`ConfigLoader.Save`) v celoti prepiše
  `devices.json` iz objektnega modela — ročno vpisani `//` komentarji v
  datoteki se ob tem izgubijo.
- Ob zelo hitrem vzorčenju (npr. 1 s) in dolgem prikazanem časovnem oknu na
  grafu lahko oznake na časovni osi postanejo gosto natrpane — čitljivost se
  izboljša z izbiro ožjega časovnega okna.

## Zgodovina razvoja

Podrobna kronologija vseh faz je v [CHANGELOG.md](CHANGELOG.md).
