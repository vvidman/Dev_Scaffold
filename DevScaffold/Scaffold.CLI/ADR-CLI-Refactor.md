# ADR – Scaffold CLI Refaktor

**Dátum:** 2026-03-05  
**Frissítve:** 2026-03-17  
**Státusz:** Elfogadott  
**Érintett projektek:** Scaffold.CLI

---

## Kontextus

Az eredeti CLI implementáció (`PipelineRunner` alapú) a teljes pipeline orchestrációt egyetlen folyamatos processzen belül kezelte – beleértve a modell lifecycle-t is. Ez a megközelítés nem volt összeegyeztethető a human-in-the-loop validációs modellel, ahol:

- A lépések között tetszőleges idő telhet el
- Egy lépés outputja a következő lépés inputja – ezek előre nem ismertek
- A taszkokat az 1. lépés generálja – számuk és tartalmuk futásidőben derül ki
- A 2-4. lépés taszkonként fut – nem egyszer, hanem annyiszor ahány taszk van

A human az orchestrátor – ő dönti el mikor fut a következő lépés és mivel.

A refaktor második köre (2026-03-17) bevezette az audit log rendszert és a strukturált konzol kimenetet, mert:
- A `Console.WriteLine` hívások szétszórtak és nem archivált – egy session után elveszett minden nyom
- A human validáció döntése (különösen a Reject pontosítása) nem volt rögzítve sehol
- A különböző szintű üzenetek (infrastruktúra, inference progress, validáció) vizuálisan nem különültek el

---

## Döntések

---

### 1. CLI lépés szintű, nem pipeline szintű

**Döntés:** A CLI nem pipeline-t futtat, hanem egyetlen lépést. Minden CLI hívás egy lépést hajt végre, majd a CLI kilép. A ServiceHost fut tovább.

**Indoklás:**
- A pipeline lépések inputjai egymás outputjaitól függnek – nem tudhatók előre
- A human validáció és az orchestráció a human felelőssége – a CLI ezt nem veszi át
- A `pipeline.yaml` fogalma megszűnik – felesleges komplexitás lett volna

**CLI parancsok:**
```bash
DevScaffold run --config <step_agent_config.yaml>
                --input  <input.yaml>
                --model  <model_alias>
               [--output <output mappa>]

DevScaffold shutdown
```

---

### 2. CLI session lifecycle – egy lépés, majd kilép

**Döntés:** A CLI session addig él amíg a CLI process fut. Egy `run` hívás lefuttatja a lépést human validációval, majd a CLI kilép. A ServiceHost lifecycle ettől független.

**Indoklás:**
- A human dönti el mikor jön a következő lépés – a CLI-nek nem kell bent maradnia
- Egyszerűbb és előre kiszámítható lifecycle
- A Reject → újragenerálás iteráció a session életidején belül marad

**Lifecycle:**
```
DevScaffold run      → ServiceHost indul (ha még nem fut) + lépés fut → CLI kilép
DevScaffold run      → ServiceHost már fut + lépés fut → CLI kilép
DevScaffold run      → ServiceHost már fut + lépés fut → CLI kilép
DevScaffold shutdown → ServiceHost leáll → CLI kilép
```

---

### 3. ServiceHost lifecycle – független a CLI-től

**Döntés:** A ServiceHost az első `run` híváskor indul, és addig fut amíg explicit `shutdown` parancs nem érkezik. Nem áll le amikor a CLI kilép.

**Indoklás:**
- A modellek memóriában maradnak a lépések között – nem kell minden híváskor újratölteni
- A human munkamenet több CLI hívásból áll – a ServiceHost ezeket szolgálja ki
- Explicit shutdown parancs egyértelmű és szándékos leállítást jelent

---

### 4. pipeline.yaml fogalma megszűnik

**Döntés:** Nincs `pipeline.yaml`, `IPipelineConfigReader`, `PipelineConfig`, `PipelineStepRef` a CLI-ben. Ezek feleslegessé váltak.

**Indoklás:**
- A pipeline sorrend a human fejében és a protokoll specifikációban él – nem egy konfigurációs fájlban
- Az előre definiált lépéslista ellentmond a human-in-the-loop elvnek
- A `PipelineRunner` és a kapcsolódó domain modellek a CLI-ből kikerülnek

---

### 5. CLI mint vékony kliens

**Döntés:** A CLI nem tud LLamaSharp-ról, modell kezelésről, vagy inference részletekről. Kizárólag `CommandEnvelope` üzeneteket küld és `EventEnvelope` üzeneteket fogad.

**Indoklás:**
- A modell lifecycle a ServiceHost felelőssége
- A CLI könnyű és gyorsan indul – nem kell megvárni a modell betöltését
- Az `Scaffold.Infrastructure.Inference` project reference teljesen kikerült a CLI-ből

**Mi maradt a CLI-ben:**
- `IStepAgentConfigReader` – agent system prompt betöltése
- `IInputAssembler` – input yaml + path referencia feloldás
- `ConsoleHumanValidationService` – human validáció
- `FileAuditLogger` – audit log írás
- Generáció számítás – step output folder meghatározása

---

### 6. Komponens struktúra

**Döntés:** Öt komponens, Single Responsibility elvvel:

| Komponens | Egyetlen felelősség |
|---|---|
| `PipeClient` | Named Pipe kapcsolat kezelés |
| `ServiceHostLauncher` | ServiceHost auto-indítás és ready várakozás |
| `ScaffoldSession` | Egyetlen lépés futtatása human validációval + audit log |
| `FileAuditLogger` | Audit log írás fájlba, auto-flush |
| `Program.cs` | Parancs értelmezés, generáció számítás, összerakás |

---

### 7. PipeClient – EventReceived callback

**Döntés:** A `PipeClient` `Func<EventEnvelope, Task>` delegate-et (`EventReceived`) használ az események továbbítására, nem `IObservable<EventEnvelope>`-ot vagy channel-t.

**Indoklás:**
- Egyszerűbb mint Rx vagy `System.Threading.Channels` – nincs extra függőség
- A use case nem igényel backpressure-t vagy több subscriber-t
- A `ScaffoldSession` egy callback-kel feliratkozik és leiratkozik

---

### 8. WaitForReadyAsync az event loop előtt

**Döntés:** A `PipeClient.WaitForReadyAsync` közvetlenül olvassa az event pipe-ot, az event loop megkerülésével. Az event loop csak a `ServiceReadyEvent` megérkezése után indul.

**Indoklás:**
- A `ServiceReadyEvent` speciális – a `ServiceHostLauncher` ezt szinkronban várja az indítás során
- Ha az event loop előbb indulna, race condition adódhatna a ready esemény feldolgozásánál
- Garantált sorrend: WaitForReady → event loop indul → command pipe csatlakozás

---

### 9. ServiceHostLauncher – pipe existence check process check helyett

**Döntés:** A `ServiceHostLauncher` nem process név alapján ellenőrzi hogy fut-e a ServiceHost, hanem a Named Pipe létezését teszteli (`Connect(timeout: 0)`).

**Indoklás:**
- Process név alapú keresés elevated jogosultságot igényelhet és platform-függő
- Ha a pipe él, a ServiceHost fut és fogad kapcsolatokat – ez a releváns információ

**Két eset:**
- Pipe nem létezik → új ServiceHost process indítása
- Pipe létezik de ServiceReadyEvent nem érkezik → újracsatlakozás kísérlet

---

### 10. Retry policy – 3 kísérlet, 60s timeout, azonnali újraindulás

**Döntés:** Maximum 3 kísérlet, kísérletenként 60 másodperces timeout a `ServiceReadyEvent`-re. Kísérletek között nincs extra várakozás.

**Indoklás:**
- 60 másodperc elegendő a ServiceHost indulásához (modell betöltés nélkül – lazy)
- A timeout lejárta maga a várakozás – felesleges lenne utána még várni
- 3 kísérlet elegendő: ha háromszor sem sikerül, konfigurációs probléma valószínűsíthető

---

### 11. TaskCompletionSource alapú inference eredmény várakozás

**Döntés:** A `ScaffoldSession.WaitForInferenceResultAsync` három `TaskCompletionSource`-ot használ (completed/cancelled/failed), `Task.WhenAny`-val várja amelyik először teljesül.

**Indoklás:**
- Az inference eredménye aszinkron esemény – a TCS a legtermészetesebb C# primitív erre
- `Task.WhenAny` tisztán kifejezi a szándékot: "várd meg az első eredményt"
- Thread safety: TCS-eket `SemaphoreSlim _eventLock` védi

---

### 12. Reject esetén az elutasított kimenet nincs az újragenerálási promptban

**Döntés:** A `BuildRejectionPrompt` az eredeti inputot és a pontosítást tartalmazza – az elutasított kimenet szövegét nem.

**Indoklás:**
- Ha az AI a saját rossz kimenetéből indul ki, hajlamos azt "javítgatni" ahelyett hogy újragondolná
- A pontosítás elegendő iránymutatás az újrageneráláshoz
- Az elutasított kimenet fájlban marad – a human látja, de az AI nem kapja vissza

---

### 13. Shutdown – dedikált CLI parancs

**Döntés:** A ServiceHost leállítása egy dedikált `DevScaffold shutdown` paranccsal történik, ami `ShutdownRequest`-et küld a command pipe-on.

**Indoklás:**
- A ServiceHost lifecycle független a CLI lifecycle-tól – implicit leállítás nem megfelelő
- A human tudatos döntése a leállítás – explicit parancs ezt jobban kifejezi
- Ctrl+C a CLI-n csak a CLI-t állítja le, a ServiceHost fut tovább

---

### 14. ConsoleHumanValidationService – async wrapper szinkron logika felett

**Döntés:** A `ValidateAsync` interfész metódus `Task.FromResult`-ba csomagolja a szinkron validációs logikát egy privát helper felett, elkerülve a CS1998 compiler warningot.

**Indoklás:**
- A konzolos input/output inherensen szinkron – nincs valódi async művelet
- Az interfész async marad – jövőbeli implementációk valódi async-ot használhatnak

---

### 15. Step output folder struktúra – generáció alapú sorszámozás

**Döntés:** Minden `DevScaffold run` hívás egy dedikált step output foldert kap: `{output}/{stepId}_{n}`, ahol `n` az aktuális generáció sorszáma (1-től indul).

```
{output}/
  task_breakdown_1/     ← első futás
    output_abc12345.md
    audit.log
  task_breakdown_2/     ← első Reject után
    output_def67890.md
    audit.log
  code_generation_1/
    output_...md
    audit.log
```

**Indoklás:**
- A generációk nyomon követhetők: hány újragenerálás kellett a megfelelő eredményhez
- A Reject pontosítása és a döntés az audit logban marad – nem vész el
- Minden futás izolált: az output és az audit log egy helyen van
- A human látja az evolúciót – összehasonlíthatja a generációkat

---

### 16. Generáció számítás – filesystem alapú, process újraindulás-biztos

**Döntés:** A CLI a generáció sorszámát futáskor számolja: megnézi hány `{stepId}_*` nevű folder létezik már az output mappában, és a max sorszám + 1-et veszi.

**Indoklás:**
- A CLI process rövid életű – nem tarthat állapotot memóriában a futások között
- Filesystem alapú számítás process újraindulás esetén is helyes marad
- Lyukas sorozat (pl. ha a human töröl egy foldert) esetén is helyes: a max-ot veszi, nem a count-ot
- A generáció sorszámot a CLI adja át a ServiceHost-nak az `InferRequest.OutputFolder` mezőben

---

### 17. Audit log – plain text, fix szélességű tag oszlop, auto-flush

**Döntés:** Minden step output folder tartalmaz egy `audit.log` fájlt. A formátum:
```
2026-03-17 14:23:01.123 [INFO ] [SESSION_START   ] step=task_breakdown generation=1
2026-03-17 14:23:01.124 [INFO ] [CONFIG          ] model=qwen-7b system_prompt_length=342
2026-03-17 14:25:43.891 [INFO ] [INFERENCE_DONE  ] tokens=847 elapsed=162s tok_s=5.2
2026-03-17 14:25:50.012 [INFO ] [VALIDATION      ] outcome=Reject clarification="Részletesebb bontás kell"
2026-03-17 14:25:50.013 [INFO ] [SESSION_END     ] total_elapsed=169s outcome=Reject
```

A log `AutoFlush = true` módban íródik.

**Indoklás:**
- Human-readable: nincs szükség külső toolra az olvasáshoz
- Custom parser-barát: fix pozíción lévő timestamp (23 kar), level (7 kar), tag (18 kar) – pozíció alapú split elegendő
- A `key=value` formátum statisztikai feldolgozásra is alkalmas (pl. átlagos tok/s per step típus)
- Auto-flush: CLI crash esetén is megmarad a részleges adat – a legfontosabb információ (ki lett adva a feladat, mi lett a döntés) nem veszhet el

**Rögzített események:**
| Tag | Tartalom |
|---|---|
| `SESSION_START` | step, generation |
| `CONFIG` | model, system_prompt_length, max_tokens, output_folder |
| `INFERENCE_START` | request_id, step, model |
| `INFERENCE_DONE` | tokens, elapsed, tok_s |
| `OUTPUT` | path (a generált fájl teljes path-ja) |
| `VALIDATION` | outcome, clarification (Reject esetén) |
| `SESSION_END` | total_elapsed, outcome |
| `ERROR` | reason, message |

---

### 18. IScaffoldConsole – háromszintű konzol kimenet, színkódolással

**Döntés:** A `Console.WriteLine` hívások helyett minden komponens `IScaffoldConsole`-t használ, ami háromszintű, színkódolt kimenetet biztosít.

| Szint | Metódus | Szín | Tartalom |
|---|---|---|---|
| CLI | `WriteCli` | Cyan | Infrastruktúra, ServiceHost indítás, pipe, folder létrehozás |
| Session | `WriteSession` | Gray | Inference progress, ServiceHost események |
| Validation | `WriteValidation` | Yellow | Human validációs interakció, döntés bekérés |
| Error | `WriteError` | Red | Hibák (stderr-re kerül) |

**Indoklás:**
- A különböző szintű üzenetek vizuálisan elkülönülnek – a human egy pillantással látja mi infrastruktúra és mi az AI kimenet állapota
- Az absztrakció tesztelhetővé teszi a konzol kimenetet
- A három szint természetesen leképezi a rendszer három rétegét: CLI infrastruktúra, inference futás, human döntés

---

## Összefoglaló – az eredeti és az új CLI összehasonlítása

| Szempont | Eredeti CLI | Első refaktor | Második refaktor |
|---|---|---|---|
| Granularitás | Pipeline szintű | Lépés szintű | Lépés szintű |
| Orchestráció | PipelineRunner | Human | Human |
| Modell kezelés | CLI-ben | ServiceHost felelőssége | ServiceHost felelőssége |
| Lépések sorrendje | pipeline.yaml | Human dönti el | Human dönti el |
| CLI lifecycle | Pipeline végéig fut | Egy lépés után kilép | Egy lépés után kilép |
| ServiceHost lifecycle | CLI-vel együtt | Független, explicit shutdown | Független, explicit shutdown |
| Kommunikáció | Közvetlen függőség | Named Pipe, CommandEnvelope | Named Pipe, CommandEnvelope |
| Output folder | ServiceHost határozza meg | ServiceHost határozza meg | CLI határozza meg (generáció) |
| Audit log | Nincs | Nincs | Fájlba, auto-flush |
| Konzol kimenet | `Console.WriteLine` | `Console.WriteLine` | Színkódolt, szint szerinti |