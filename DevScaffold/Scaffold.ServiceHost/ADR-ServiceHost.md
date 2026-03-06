# ADR – Scaffold ServiceHost

**Dátum:** 2026-03-06  
**Státusz:** Elfogadott  
**Érintett projektek:** Scaffold.ServiceHost

---

## Kontextus

A Scaffold Protocol human-in-the-loop pipeline modelljében a CLI lépés szintű – minden `DevScaffold run` hívás egy önálló process. A modellek betöltése CPU-n 30–60 másodpercet vesz igénybe, ezért a modell lifecycle-t nem szabad a CLI lifecycle-hoz kötni. A ServiceHost ezt oldja meg: háttérben fut, a modelleket memóriában tartja, és Named Pipe-on keresztül fogadja a CLI parancsait.

---

## Döntések

---

### 1. ServiceHost mint önálló háttérprocess

**Döntés:** A ServiceHost önálló .NET consolos process, amelyet az első `DevScaffold run` híváskor a CLI indít el, és explicit `DevScaffold shutdown` parancsig fut.

**Indoklás:**
- A CLI process rövid életű – a modell betöltési idő a CLI számára elfogadhatatlan lenne
- A modellek memóriában tartása a lépések között a ServiceHost felelőssége
- A ServiceHost lifecycle független a CLI lifecycle-tól – explicit shutdown szándékos döntés

---

### 2. Két egyirányú Named Pipe – nem egy kétirányú

**Döntés:** Két külön `NamedPipeServerStream` – egy parancs pipe (CLI → ServiceHost) és egy esemény pipe (ServiceHost → CLI) – ahelyett hogy egy kétirányú pipe lenne.

**Indoklás:**
- Két egyirányú pipe egyértelműbb ownership-et ad: a `PipeServer` olvassa a parancs pipe-ot, az `EventPublisher` írja az esemény pipe-ot
- A kétirányú pipe esetén az olvasási és írási műveletek interfereálhatnak egymással egy szál esetén
- A két pipe aszinkron, egymástól független élettartamot tesz lehetővé

**Pipe nevek:** `{pipeName}-commands` és `{pipeName}-events`

---

### 3. Protobuf framing – WriteDelimitedTo / ParseDelimitedFrom

**Döntés:** Az üzenetek varint hossz prefix framing-gel utaznak a pipe-on (`WriteDelimitedTo` / `ParseDelimitedFrom`), nem fix hosszú fejléccel vagy newline delimiter-rel.

**Indoklás:**
- A Protobuf natívan támogatja a delimited framing-et – nincs saját protokoll implementáció
- A varint prefix hatékony: kis üzeneteknél 1 bájt overhead
- A `CommandEnvelope` és `EventEnvelope` wrapper típusok egységes keretbe foglalják az összes üzenettípust – a pipe-on mindig ugyanaz a típus utazik

---

### 4. Indulási sorrend: event pipe előbb, command pipe később

**Döntés:** A ServiceHost először az event pipe-on várja a CLI csatlakozását, elküldi a `ServiceReadyEvent`-et, és csak ezután nyitja meg a command pipe-ot.

**Indoklás:**
- A CLI oldalon a `WaitForReadyAsync` közvetlenül olvassa az event pipe-ot, az event loop megkerülésével – ezt garantálja a sorrend
- Ha a command pipe előbb nyílna meg, a CLI parancsot küldhetne mielőtt a ServiceHost felkészül az olvasásra
- A `ServiceReadyEvent` az egyetlen jelzés a CLI felé hogy a ServiceHost készen áll – a sorrend ezt teszi determinisztikussá

---

### 5. Multi-session support – session loop a PipeServer-ben

**Döntés:** A `PipeServer.RunAsync` egy külső session loop-ot tartalmaz. Minden CLI session után az `EventPublisher` reseteli az event pipe-ot és a következő CLI kapcsolatot várja.

**Indoklás:**
- A `NamedPipeServerStream` nem reusable egy lezárt kapcsolat után – új instance kell
- A ServiceHost a CLI lépés szintű lifecycle-ja miatt több CLI processt szolgál ki egymás után
- A `ModelCache` és `InferenceWorker` session-független – a reset csak a pipe réteget érinti

**Session lifecycle:**
```
[session loop start]
  EventPublisher.WaitForConnectionAsync()  → CLI csatlakozik
  EventPublisher.PublishServiceReadyAsync()
  PipeServer: command pipe nyitása          → CLI csatlakozik
  RunCommandLoopAsync()                     → CLI kilép → pipe lezárul
  commandPipe.DisposeAsync()
  EventPublisher.ResetForNewConnectionAsync() → új pipe instance
[session loop újraindul]
```

---

### 6. EventPublisher – SemaphoreSlim thread safety

**Döntés:** Az `EventPublisher` egy `SemaphoreSlim(1,1)` lockkal biztosítja hogy egyszerre csak egy `EventEnvelope` kerül a pipe-ra.

**Indoklás:**
- Az `InferenceWorker` progress timerje és a `ModelCache` státusz eseményei párhuzamosan futhatnak
- A pipe stream nem thread-safe – a lock nélkül az üzenetek keveredhetnek
- A `SemaphoreSlim` aszinkron lock – nem blokkolja a szálat, csak a async folyamatot

---

### 7. ModelCache – lazy betöltés, per-alias lock

**Döntés:** A `ModelCache` lazy betöltést alkalmaz – a modell az első `GetOrLoadAsync` hívásakor töltődik be. A thread safety per-alias `SemaphoreSlim`-mel biztosított, nem globális lockkal.

**Indoklás:**
- A ServiceHost indulása azonnali – nem kell megvárni a modell betöltést
- Per-alias lock: ha két különböző modellt kellene párhuzamosan betölteni, nem blokkolják egymást
- Double-check pattern: a per-alias lockon belül ismét ellenőrzi a cache-t – elkerüli a dupla betöltést versenyhelyzet esetén

**Betöltési sorrend (per alias):**
```
_dictionaryLock → cache hit? → return
                → miss: per-alias lock
                  → _dictionaryLock (double-check) → cache hit? → return
                                                    → miss: LoadModelAsync
```

---

### 8. ModelCache esemény – delegate, nem közvetlen függőség

**Döntés:** A `ModelCache` `event Func<string, ModelStatus, string, Task>? ModelStatusChanged` delegate-en keresztül értesíti az `EventPublisher`-t, nem tart közvetlen referenciát rá.

**Indoklás:**
- A `ModelCache` nem függ az `EventPublisher`-től – tesztelhető önállóan
- A `Program.cs` köti össze a két komponenst – a wiring egy helyen van
- Az event delegate opcionális (`?`) – a `ModelCache` tesztekben esemény handler nélkül is használható

---

### 9. InferenceWorker – fire and forget a command loop védelmében

**Döntés:** A `CommandDispatcher` az `InferRequest`-et `Task.Run`-nal fire-and-forget jelleggel indítja, nem `await`-eli.

**Indoklás:**
- Az inference futás percekig tarthat – ha a `CommandDispatcher` await-elné, a command pipe olvasása blokkolt lenne
- A `CancelInferRequest` csak akkor érkezhet meg ha a command loop fut – tehát a command loop nem blokkolható
- Az `InferenceWorker` saját maga küldi az `InferenceCompletedEvent` / `InferenceFailedEvent` eseményeket

---

### 10. InferenceWorker – SemaphoreSlim az egyszerre futó inference limitáláshoz

**Döntés:** Az `InferenceWorker` `SemaphoreSlim(1,1)`-et használ annak biztosítására hogy egyszerre csak egy inference futhat.

**Indoklás:**
- CPU-only hardveren párhuzamos inference nem előny – ellenkezőleg, mindkét futás lassulna
- Az `InvalidOperationException` azonnali jelzést ad ha a CLI hibásan küld párhuzamos kérést
- A per-request cancel (`TODO` komment) a jövőben bevezethetővé válik a jelenlegi struktúra megtartásával

---

### 11. Shutdown – ShutdownToken a CommandDispatcher-ben

**Döntés:** A `CommandDispatcher` egy saját `CancellationTokenSource _shutdownCts`-t tart, amelynek `Token`-jét a `PipeServer` figyeli. A `ShutdownRequest` handler ezt canceli – nem állítja le közvetlenül a processt.

**Indoklás:**
- A `CommandDispatcher` nem tudja és nem kell hogy tudja hogyan áll le a `PipeServer` – csak jelzi a szándékot
- A `PipeServer` a saját linked token-jén keresztül veszi észre a shutdown jelzést és tisztán lép ki
- `Environment.Exit` vagy `Process.Kill` helyett az async cancellation pattern egységes és tesztelhető

---

### 12. Graceful shutdown – Ctrl+C és SIGTERM

**Döntés:** A `Program.cs` `CancelKeyPress` és `ProcessExit` eseményekre iratkozik fel, és a fő `CancellationTokenSource`-ot canceli.

**Indoklás:**
- A `ProcessExit` biztosítja a `ModelCache.DisposeAsync` hívást SIGTERM esetén is
- A `using var cts` és az `await using` dispose chain garantálja a `LLamaWeights` felszabadítását
- A `OperationCanceledException` a legfelső szinten elkapott és normál (0) exit code-dal kezelt

---

## Komponens összefoglaló

| Komponens | Egyetlen felelősség | Függőségei |
|---|---|---|
| `PipeServer` | Pipe lifecycle, session loop, command olvasás | `CommandDispatcher`, `EventPublisher` |
| `EventPublisher` | Event pipe írás, thread-safe küldés | – (csak `NamedPipeServerStream`) |
| `CommandDispatcher` | Parancs routing, shutdown jelzés | `InferenceWorker`, `ModelCache`, `EventPublisher` |
| `InferenceWorker` | Inference futtatás, progress küldés | `ModelCache`, `EventPublisher` |
| `ModelCache` | Lazy modell betöltés, cache kezelés | `ModelRegistryConfig`, `LLamaWeights` |

---

## Kapcsolódó ADR-ek

- **ADR-CLI-Refactor** – A CLI vékony kliens döntés, amely meghatározza hogy a ServiceHost mikor indul és mikor áll le