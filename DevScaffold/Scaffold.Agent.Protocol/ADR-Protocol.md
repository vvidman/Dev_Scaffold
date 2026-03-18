# ADR – Scaffold.Agent.Protocol

**Dátum:** 2026-03-04  
**Frissítve:** 2026-03-17  
**Státusz:** Elfogadott  
**Érintett projektek:** Scaffold.Agent.Protocol, Scaffold.ServiceHost, Scaffold.CLI

---

## Kontextus

A `Scaffold.Agent.Protocol` projekt tartalmazza a CLI és a ServiceHost közötti kommunikáció teljes protokoll definícióját protobuf formátumban. A `.proto` fájlok egyszerre szolgálnak séma definícióként és kommunikációs dokumentációként. Az itt leírt döntések azt rögzítik, hogy az egyes üzenetek miért azt tartalmazzák amit, és miért úgy vannak strukturálva ahogyan.

---

## Döntések

---

### 1. request_id korrelációs azonosító minden kérésben

**Döntés:** Minden CLI-ből induló kérés (`InferRequest`, `LoadModelRequest`, `UnloadModelRequest`, `ListModelsRequest`, `CancelInferRequest`) tartalmaz `request_id` mezőt.

**Indoklás:**
- A két pipe aszinkron – a kérés és a válasz esemény nem garantáltan egymás után érkezik
- A CLI a `request_id` alapján tudja melyik esemény melyik kéréshez tartozik
- GUID formátum ajánlott – a CLI generálja indításkor
- `CancelInferRequest`-ben azonosítja melyik futó kérést kell megszakítani

**Megjegyzés:** A `ShutdownRequest`-ben nincs `request_id` mert shutdown után nincs korrelációra szükség – a ServiceHost leáll.

---

### 2. CommandEnvelope és EventEnvelope wrapper üzenetek

**Döntés:** A command pipe-on mindig `CommandEnvelope`, az event pipe-on mindig `EventEnvelope` kerül küldésre – nem a konkrét üzenet típusok közvetlenül.

**Indoklás:**
- A Named Pipe stream alapú – a fogadó félnek tudnia kell mit olvas
- A `oneof` mező garantálja hogy egyszerre csak egy parancs/esemény lehet aktív
- C#-ban a `CommandCase` / `EventCase` enum alapján switch-elhető a feldolgozás
- Bővíthetőség: új parancs vagy esemény hozzáadása nem töri a meglévő kódot – csak új `oneof` ág kell

---

### 3. oneof a CommandEnvelope-ban és EventEnvelope-ban

**Döntés:** Mindkét wrapper üzenet `oneof` mezőt használ az altípusok tárolására.

**Indoklás:**
- Bináris szinten csak az aktív mező kerül szerializálásra – kompakt formátum
- Típusbiztos feldolgozás – a `CommandCase` enum megmondja pontosan mit tartalmaz az üzenet
- Alternatív megközelítés lett volna minden üzenettípushoz külön pipe – ez viszont kezelhetetlen számú pipe-ot eredményezett volna

---

### 4. inference.proto – InferRequest tartalom

**Döntés:** Az `InferRequest` a következőket tartalmazza: `request_id`, `step_id`, `model_alias`, `system_prompt`, `user_input`, `max_tokens`, `output_folder`.

**Mezőnkénti indoklás:**

| Mező | Indoklás |
|---|---|
| `request_id` | Korrelációs azonosító az event pipe eseményekhez |
| `step_id` | Naplózáshoz és státusz megjelenítéshez – a CLI tudja melyik lépés fut |
| `model_alias` | A ServiceHost ebből oldja fel a GGUF path-t a ModelCache-en keresztül |
| `system_prompt` | Az agent szerepe – step agent config yaml-ból jön |
| `user_input` | Az InputAssembler kimenete – input yaml + összes hivatkozott fájl tartalma |
| `max_tokens` | 0 esetén ServiceHost alapértelmezett – rugalmas, nem kell mindig megadni |
| `output_folder` | A CLI által meghatározott step output folder – generáció sorszámot tartalmaz |

**Miért nincs benne a modell path közvetlenül:**
- A path a ServiceHost belső implementációs részlete
- Az alias absztrakcióval a CLI nem tud a fájlrendszer struktúráról
- A models.yaml mapping egy helyen él – könnyebb karbantartani

**Miért kerül az output_folder a CLI-ből a ServiceHost-nak (és nem fordítva):**
- A generáció sorszám kiszámítása CLI felelősség – a CLI ismeri a projekt output struktúrát
- A CLI számolja meg hány `{stepId}_*` folder létezik már, és létrehozza a következőt
- A ServiceHost-nak nem kell tudnia a generációkról – csak azt a foldert használja amit kap
- Ha az `output_folder` üres, a ServiceHost fallback-ként a saját `--output` paraméteréből számít path-t (visszafelé kompatibilitás)
- A CLI az `InferenceCompletedEvent`-ből kapja vissza a tényleges output fájl path-ját

---

### 5. inference.proto – CancelInferRequest tartalom

**Döntés:** A `CancelInferRequest` csak `request_id`-t tartalmaz.

**Indoklás:**
- Minimális üzenet – csak azonosítani kell melyik kérést kell megszakítani
- Nincs szükség más adatra a cancel végrehajtásához

---

### 6. inference.proto – ShutdownRequest tartalom

**Döntés:** A `ShutdownRequest` csak `force` bool mezőt tartalmaz.

**Indoklás:**
- `force = false`: graceful shutdown – megvárja az aktív inference befejezését
- `force = true`: azonnali leállás – aktív inference megszakítása
- Nincs `request_id` – shutdown után nincs korrelációra szükség

---

### 7. models.proto – csak három parancs

**Döntés:** A `models.proto` három parancsot tartalmaz: `LoadModelRequest`, `UnloadModelRequest`, `ListModelsRequest`.

**Indoklás:**
- `LoadModelRequest`: opcionális előzetes betöltés – lazy betöltés esetén nem kötelező, de hasznos ha előre tudod hogy szükséged lesz a modellre
- `UnloadModelRequest`: memória felszabadítás – ha egy modell hosszabb ideig nem kell
- `ListModelsRequest`: csak betöltött modellek listája – a human tudja milyen modellek érhetők el, csak a betöltött állapot az érdekes

**Miért nincs több modell parancs:**
- A models.yaml kezelése (hozzáadás, törlés) human feladat – nem protokoll szintű
- A ServiceHost restart nélkül nem tud új modellt regisztrálni – ez szándékos egyszerűsítés

---

### 8. events.proto – enum konvenciók

**Döntés:** Az enumok értékei nagybetűvel és a típus nevével prefixálva: `MODEL_STATUS_LOADING`, `INFERENCE_STATUS_COMPLETED` stb.

**Indoklás:**
- Proto3 iparági standard – megakadályozza a névütközéseket különböző enum típusok között
- A `Grpc.Tools` a prefixet levágja a generált C# kódban – `ModelStatus.Loading`, `InferenceStatus.Completed`
- Proto3 kötelező szabály: minden enum első értéke 0 értékű `_UNSPECIFIED` – ez az alapértelmezett ha a mező nincs beállítva

---

### 9. events.proto – google.protobuf.Timestamp minden eseményben

**Döntés:** Minden esemény tartalmaz `occurred_at` vagy `started_at` / `completed_at` `google.protobuf.Timestamp` mezőt.

**Indoklás:**
- Az esemény bekövetkezésének ideje diagnosztikailag értékes – naplózáshoz és teljesítményméréshez
- A `google.protobuf.Timestamp` timezone-független UTC időbélyeg – nem kell string konverzióval foglalkozni
- C#-ban egyszerűen konvertálható: `timestamp.ToDateTime()`

---

### 10. events.proto – ServiceReadyEvent mint indulás jelzője

**Döntés:** A `ServiceReadyEvent` tartalmaz `version` és `started_at` mezőt, és ez az esemény jelzi a CLI-nek hogy a ServiceHost kész fogadni parancsokat.

**Indoklás:**
- A CLI ezt az eseményt várja az auto-indítás során – ez a pipe-ready timeout alapja
- A `version` mező jövőbeli kompatibilitás ellenőrzéshez hasznos – CLI és ServiceHost verzió összehasonlítható
- `started_at` diagnosztikailag értékes – megmutatja mennyi ideig tartott az indulás

---

### 11. events.proto – ModelStatusChangedEvent egy üzenet minden modell állapothoz

**Döntés:** Egyetlen `ModelStatusChangedEvent` kezeli a betöltés, betöltöttség, kiürítés és hiba állapotokat a `ModelStatus` enum segítségével – nem külön üzenettípus minden állapothoz.

**Indoklás:**
- Kevesebb üzenettípus – egyszerűbb a CLI oldali feldolgozás
- Az állapot változás természetesen egy esemény sorozat – `Loading` → `Loaded` vagy `Failed`
- A `message` mező opcionális szöveges részletet ad hiba esetén

---

### 12. events.proto – LoadedModelsListEvent repeated mező

**Döntés:** A `LoadedModelsListEvent` `repeated string loaded_aliases` mezőt tartalmaz a betöltött modellek listájához.

**Indoklás:**
- `repeated` a protobuf lista típusa – C#-ban `RepeatedField<string>` ami `IList<string>`-ként használható
- Csak betöltött aliasok kerülnek bele – a human tudja milyen modellek érhetők el, csak a betöltött állapot az érdekes
- Nincs szükség komplex modell objektumra – az alias elegendő azonosító

---

### 13. events.proto – InferenceProgressEvent tartalom

**Döntés:** Az `InferenceProgressEvent` tartalmaz `request_id`, `step_id`, `elapsed_seconds`, `status_message` mezőket.

**Indoklás:**
- `elapsed_seconds`: egyszerű haladásjelző – a CLI kiírja "Generálás folyamatban... (Xs)"
- `status_message`: szöveges státusz – első körben egyszerű üzenet mint "Generálás folyamatban..."
- Nem token-enkénti streaming – a Scaffold Protocol input fájlokból dolgozik, nincs konzol gépelés élmény igény
- 10 másodperces periódus – elegendő visszajelzés anélkül hogy elárasztaná az event pipe-ot

---

### 14. events.proto – InferenceCompletedEvent output_file_path mezője

**Döntés:** Az `InferenceCompletedEvent` tartalmazza az `output_file_path` mezőt.

**Indoklás:**
- A CLI-nek tudnia kell hol van a generált kimenet fájl a human validációhoz
- A ServiceHost határozza meg a konkrét fájlnevet (`{stepId}_{requestId[..8]}.md`) az `output_folder`-en belül
- A CLI ezt a path-t adja át a `ConsoleHumanValidationService`-nek a fájl megnyitáshoz
- Az audit logba is ez a path kerül bejegyzésre

---

### 15. events.proto – ServiceErrorEvent általános hiba esemény

**Döntés:** Külön `ServiceErrorEvent` típus a kéréshez nem köthető hibákhoz, `error_code` és `error_message` mezőkkel.

**Indoklás:**
- Vannak hibák amik nem köthetők konkrét `request_id`-hoz – pl. models.yaml parse hiba, pipe hiba
- `error_code` gépi feldolgozáshoz – a CLI dönthet hogy bizonyos hibakódoknál leáll vagy folytatja
- `error_message` human readable – konzolra kiírható
- Az inference specifikus hibák az `InferenceFailedEvent`-be kerülnek – a `ServiceErrorEvent` csak infrastruktúra szintű hibákhoz

---

### 16. commands.proto – különálló fájl a CommandEnvelope-nak

**Döntés:** A `CommandEnvelope` egy dedikált `commands.proto` fájlban él, ami importálja az `inference.proto`-t és a `models.proto`-t.

**Indoklás:**
- A `CommandEnvelope` egyetlen felelőssége: összefogni az összes parancs típust
- Ha az `inference.proto`-ban lenne, az importálná a `models.proto`-t – keveredne a két fájl felelőssége
- Tiszta függőségi gráf: `commands.proto` importál, de senki nem importálja – körkörös függőség nincs

---

### 17. Mezőszámok konzisztenciája

**Döntés:** A mezőszámok minden üzenettípusban 1-től indulnak és folyamatosan növekszenek. Nincs "stratégiai" mezőszám kiosztás.

**Indoklás:**
- Az aktuális protokoll nem igényel visszafelé kompatibilitást régi verziókkal – v1.0 induló állapot
- Ha mező törlés szükséges, `reserved` kulcsszóval jelölendő a szám és a név is:
  ```protobuf
  reserved 3;
  reserved "old_field_name";
  ```
- Ez megakadályozza hogy törölt mezőszám véletlenül újra felhasználásra kerüljön

---

## Összefoglaló – proto fájl felelősségek

| Fájl | Tartalom | Irány |
|---|---|---|
| `inference.proto` | InferRequest, CancelInferRequest, ShutdownRequest | CLI → ServiceHost |
| `models.proto` | LoadModelRequest, UnloadModelRequest, ListModelsRequest | CLI → ServiceHost |
| `commands.proto` | CommandEnvelope (importálja inference + models) | CLI → ServiceHost |
| `events.proto` | EventEnvelope + 10 esemény típus + 2 enum | ServiceHost → CLI |

## Függőségi gráf

```
commands.proto
    ├── imports inference.proto
    └── imports models.proto

events.proto
    └── imports google/protobuf/timestamp.proto

inference.proto   ← független
models.proto      ← független
```