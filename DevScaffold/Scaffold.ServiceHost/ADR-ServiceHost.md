# ADR – Scaffold ServiceHost

**Dátum:** 2026-03-04  
**Státusz:** Elfogadott  
**Érintett projektek:** Scaffold.ServiceHost, Scaffold.Agent.Protocol, Scaffold.CLI

---

## Kontextus

A Scaffold Protocol pipeline lépései nem folyamatos futásban zajlanak – a human validáció miatt a lépések között percek, órák vagy akár napok telhetnek el. Az eredeti `PipelineRunner` alapú megközelítés egyetlen folyamatos processzt feltételezett, ami minden lépésnél újratöltötte a modellt. Ez elfogadhatatlan latenciát okozott volna (30-60mp modelltöltés lépésenként).

---

## Döntések

---

### 1. ServiceHost mint különálló process

**Döntés:** A modell lifecycle és az inference futtatás egy különálló `Scaffold.ServiceHost` console app processbe kerül, elválasztva a CLI-től.

**Indoklás:**
- A CLI hívások között a modell memóriában marad – nem kell minden lépésnél újratölteni
- A CLI vékony kliens marad – nem tud LLamaSharp-ról
- A ServiceHost élettartama független a CLI futásától
- Lehetővé teszi hogy a human tetszőleges időt töltsön a validációval anélkül hogy a modellt újra kellene tölteni

**Elutasított alternatíva:** CLI-be integrált modell kezelés – ez a lépések közötti szünet alatt folyamatosan bent tartotta volna a modellt a CLI processzen belül, ami nem életszerű.

---

### 2. Named Pipe kommunikáció HTTP helyett

**Döntés:** A CLI és a ServiceHost közötti kommunikáció Windows Named Pipe-on keresztül történik, nem HTTP Minimal API-n.

**Indoklás:**
- Nem nyit hálózati portot – nincs port ütközés, nincs firewall kérdés
- Lokális gépen a Named Pipe gyorsabb mint a loopback HTTP
- Teljesen összhangban a privacy-first elvvel – kommunikáció gépen kívülre nem szivárog
- Single-user, lokális eszköznél a HTTP overhead indokolatlan

**Elutasított alternatíva:** Minimal API HTTP – felesleges hálózati overhead és biztonsági felület egy lokális eszköznél.

---

### 3. Két egyirányú Named Pipe

**Döntés:** Két különálló, egyirányú pipe:
- `{pipeName}-commands`: CLI → ServiceHost (csak írás a CLI-nek, csak olvasás a ServiceHost-nak)
- `{pipeName}-events`: ServiceHost → CLI (csak írás a ServiceHost-nak, csak olvasás a CLI-nek)

**Indoklás:**
- Egyirányú pipe-ok egyszerűbbek – nincs multiplexálási probléma
- Tiszta felelősség szétválasztás: parancsok és események soha nem keverednek
- A kétirányú pipe kezelése bonyolultabb és race condition-ra hajlamosabb
- Könnyebb debugolni – minden csatornán csak egy típusú üzenet folyik

**Elutasított alternatíva:** Egy kétirányú pipe multiplexált üzenetekkel – komplexebb protokoll, nehezebb hibakeresés.

---

### 4. Protobuf üzenet formátum JSON helyett

**Döntés:** A Named Pipe-on protobuf bináris formátumban kommunikálnak a komponensek (`Google.Protobuf`), nem JSON-ban.

**Indoklás:**
- Tanulási lehetőség: a protobuf iparági standard, más kontextusban is releváns tudás
- Típusbiztos kommunikáció – a séma a `.proto` fájlokban él és dokumentálja a protokollt
- Bináris formátum kompaktabb mint JSON – lokálisan ez nem kritikus, de a szemlélet fontos

**Elutasított alternatíva:** JSON – egyszerűbb lett volna, de nem ad tanulási értéket és a séma dokumentáció hiányzott volna.

---

### 5. WriteDelimitedTo / ParseDelimitedFrom framing

**Döntés:** A Named Pipe stream alapú – az üzenetek határait a `WriteDelimitedTo` és `ParseDelimitedFrom` metódusok kezelik varint hossz prefix encoding-gal.

**Indoklás:**
- A `Google.Protobuf` könyvtár natívan tartalmazza – nem kell manuálisan implementálni
- Kompaktabb mint a fix 4 bájtos hossz prefix – kis üzeneteknél 1-2 bájt elegendő
- Konzisztens a két pipe oldalán – ugyanaz a mechanizmus mindenhol

**Elutasított alternatíva:** Manuális 4 bájtos hossz prefix kezelés – felesleges, amikor a könyvtár megoldja.

---

### 6. Proto fájl struktúra – B verzió (commands.proto különálló)

**Döntés:** A protobuf definíciók négy fájlban:
- `inference.proto` – inference parancsok
- `models.proto` – modell lifecycle parancsok
- `events.proto` – összes esemény típus
- `commands.proto` – `CommandEnvelope`, importálja az inference.proto-t és models.proto-t

**Indoklás:**
- Minden fájlnak egy felelőssége van – konzisztens a Clean Architecture elvvel
- A `CommandEnvelope` feladata összefogni a parancsokat – ezt egy dedikált fájl jobban kifejezi
- Import függőségek körkörösen mentesek – `commands.proto` importál, de senki nem importálja

**Elutasított alternatíva:** A verzió – `CommandEnvelope` az `inference.proto`-ban, importálja a `models.proto`-t. Keveredtek volna a felelősségek.

---

### 7. GrpcServices="None" a Protocol projektben

**Döntés:** A `Scaffold.Agent.Protocol.csproj`-ban minden `<Protobuf>` item `GrpcServices="None"` attribútummal.

**Indoklás:**
- A `Grpc.Tools` alapból gRPC service stubokat is generálna – ezek nem kellenek
- Named Pipe-ot használunk saját transport réteggel, nem gRPC transport-ot
- Csak az üzenet osztályokra van szükség (`*Messages.cs`), nem a service stubokra

---

### 8. ServiceHost automatikus indítás a CLI-ből

**Döntés:** A ServiceHost-ot a CLI indítja el automatikusan az első híváskor – nem kell manuálisan elindítani.

**Indoklás:**
- Egyszerűbb felhasználói élmény – a human csak a CLI-vel dolgozik
- A CLI generálja a pipe nevet (`scaffold-{guid}`) – így több példány sem ütközik

---

### 9. Retry policy a ServiceHost indításhoz

**Döntés:** Maximum 3 kísérlet, kísérletenként 60 másodperces timeout a `ServiceReadyEvent`-re. A kísérletek között nincs várakozás – a timeout lejárta után azonnal indul a következő kísérlet.

**Indoklás:**
- 3 kísérlet elegendő – ha háromszor sem indul el, valószínűleg konfigurációs probléma van
- 60 másodperc elegendő a modell nélküli ServiceHost induláshoz
- Nincs felesleges várakozás a kísérletek között – a 60s maga a timeout, nem előszoba

**Retry logika részletei:**
- Minden kísérlet előtt: process existence check
- Ha process fut de nem válaszol → újracsatlakozás a pipe-ra
- Ha process nem fut → új process indítása
- Minden kísérlet eredménye konzolra kiírva: `[SCAFFOLD] ServiceHost nem válaszolt (N/3)`

---

### 10. Lazy modell betöltés

**Döntés:** A `ModelCache` az első inference kérésnél tölti be a modellt, nem a ServiceHost indulásakor.

**Indoklás:**
- A ServiceHost gyorsabban indul – nem kell várni a modell betöltésre
- Csak a ténylegesen használt modellek töltődnek be
- Explicit `LoadModelRequest` paranccsal előzetes betöltés is lehetséges ha szükséges

**Trade-off:** Az első inference kérés lassabb lesz (30-60mp modell betöltés). Ez elfogadható mert a human úgyis jelen van.

---

### 11. Per-alias lock a ModelCache-ben

**Döntés:** A `ModelCache` per-alias `SemaphoreSlim` lockot használ, nem egy globális lockot.

**Indoklás:**
- Két különböző modell párhuzamosan töltődhet be anélkül hogy blokkolnák egymást
- Double-checked locking pattern – gyors ellenőrzés lock nélkül, pontos ellenőrzés lockkal
- Thread-safe dictionary műveletek globális lockkal, de csak a minimálisan szükséges ideig

---

### 12. Fire and forget inference indítás a CommandDispatcher-ben

**Döntés:** Az `InferRequest` kezelésekor `Task.Run`-nal indítjuk az inference-t és nem várjuk be a `DispatchAsync`-ban.

**Indoklás:**
- Ha megvárnánk, a command pipe olvasása blokkolódna az inference teljes ideje alatt
- `CancelInferRequest` csak akkor érkezhetne be, ha a command loop fut
- A fire and forget minta lehetővé teszi hogy a command loop folyamatosan olvasson

---

### 13. ShutdownToken szétválasztás

**Döntés:** A `CommandDispatcher` nem állítja le a processt közvetlenül – `_shutdownCts.Cancel()`-t hív, a `PipeServer` figyeli ezt a tokent és vezényli le a graceful shutdown-t.

**Indoklás:**
- Tiszta felelősség szétválasztás – a dispatcher parancsokat feldolgoz, nem lifecycle-t kezel
- A `PipeServer` a megfelelő hely a shutdown koordináláshoz mert ő kezeli a pipe-ok életciklusát

---

### 14. Haladásjelzés token streaming helyett

**Döntés:** Az inference futása alatt periodikus `InferenceProgressEvent` üzenetek küldése 10 másodpercenként, nem token-enkénti streaming.

**Indoklás:**
- A Scaffold Protocol input fájlokból dolgozik – nincs konzol "gépelés" élmény igény
- A token-enkénti streaming Named Pipe-on plusz protokoll komplexitást igényelne
- A `PeriodicTimer` alapú megközelítés egyszerű és elegendő információt ad a haladásról
- Megjelenített info: eltelt másodpercek, státusz üzenet

---

### 15. Részleges kimenet törlése cancel esetén

**Döntés:** `CancelInferRequest` vagy `OperationCanceledException` esetén az `InferenceWorker` törli a részlegesen megírt kimenet fájlt.

**Indoklás:**
- Jobb egy hiányzó fájl mint egy csonka kimenet ami félrevezeti a human reviewt
- A CLI `InferenceCancelledEvent`-et kap – tudja mi történt
- Konzisztens az "elfogadom vagy nem fogadom el" validációs modellel

---

## Összefoglaló – komponens felelősségek

| Komponens | Felelősség |
|---|---|
| `EventPublisher` | Event pipe írás, EventEnvelope gyártás |
| `ModelCache` | Lazy betöltés, thread-safe cache, dispose |
| `InferenceWorker` | Inference futtatás, progress timer, kimenet fájl írás |
| `CommandDispatcher` | Parancs routing, shutdown jelzés |
| `PipeServer` | Pipe lifecycle, command loop, indulási sorrend |
| `Program.cs` | Komponensek összerakása, arg parsing, graceful shutdown |