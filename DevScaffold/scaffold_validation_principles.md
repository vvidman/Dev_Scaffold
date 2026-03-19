# Scaffold Protocol – Output Validáció: Elvek és Megközelítés

> **Státusz:** Elfogadott architektúrális döntés – éles tesztekkel igazolva  
> **Kontextus:** Scaffold Protocol – human-in-the-loop AI fejlesztési eszköz  
> **Utoljára frissítve:** 2026-03-19 – 5 éles futás tapasztalatai alapján

---

## 1. Az alapállítás

Egy LLM kimenet **matematikai értelemben sosem determinisztikus.** A temperature, a sampling és a modell belső állapota miatt ugyanaz az input két futáson két különböző outputot adhat. Ez tény, és semmilyen validációs keretrendszer nem változtat rajta.

A cél ezért nem az output teljes determinizmusa. A cél az, hogy:

- **a biztosan rossz kimenetek automatikusan kiszűrhetők legyenek,**
- **a variancia egy elfogadható tartományon belül maradjon,**
- **a hibák detektálhatók, jelölhetők és célzottan javíthatók legyenek.**

Ez analóg a szoftverteszteléssel: a tesztek nem garantálják, hogy a kód hibamentes – de garantálják, hogy **bizonyos hibaosztályok nem jutnak át.** Ugyanez a logika érvényes itt.

---

## 2. A határvonal – ez a legfontosabb döntés

A validátorok **szükséges feltételeket** ellenőriznek, nem elégséges feltételeket.

| | Automatizálható | Nem automatizálható |
|---|---|---|
| **Példa** | Struktúra, constraint megfelelés, stop token, Roslyn compile | Architektúrális helyesség, business logika, tartalmi illeszkedés |
| **Ha fail** | Output biztosan rossz | – |
| **Ha pass** | Output *lehet* jó | Human dönti el |

> **Aranyszabály:** Ha a validator fail-t ad, az output biztosan rossz.  
> Ha pass-t ad, az output lehet jó – de a human validálja.

A human figyelme így a **valódi kérdésekre** fókuszál, nem a mechanikus ellenőrzésre.

**Éles tapasztalat:** Az első sikeres Accept futáson a human megjegyezte: „én máshogy csinálnám, de nem tudok belekötni." Ez pontosan a helyes működés – a tartalmi ízlésbeli különbség nem validator kérdés.

---

## 3. A veszélyek – amit el kell kerülni

### 3.1 Túl strict validátor → false positive rejectek

Ha a forbidden keyword lista vagy a field order check túl merev, a rendszer folyamatosan visszautasítja az egyébként helyes outputokat. A refinement loop végtelen körökbe kerül. Ez **frusztrálóbb és károsabb mint a laza validáció.**

**Elv:** Csak olyat validálj automatikusan, amire biztosan tudod, hogy rossz.

**Éles példa:** A `forbidden_affected_files` listában szereplő `Repository.cs` substring match alapon tüzelt `CachingRepository.cs`-re is – három egymást követő false positive auto-reject eredményezett, ~15 perc felesleges futási idővel. A fix: negative lookbehind alapú regex (`(?<![A-Za-z])Repository\.cs`) a pontos fájlnév egyezéshez.

### 3.2 LLM-as-judge a tartalomra

Az LLM-as-judge megközelítés csábító, de a tartalom helyességének megítélésére **nem megbízható bíró** – maga is LLM, maga is variáns. Erre a rétegre automatizmust építeni félrevezető biztonságérzetet ad.

**Elv:** Az LLM judge csak az UNCERTAIN kategóriák human elé irányítására használható, nem döntéshozóként.

### 3.3 Korai túlkomplexitás

Ha a validációs infrastruktúra fejlesztése megelőzi a tényleges funkcionalitást, a fejlesztési erőforrás rossz helyre megy. Az első két implementációs lépés (UniversalValidator + TaskBreakdownValidator) már önmagában értéket ad.

**Elv:** Fokozatos építkezés – minden lépés önállóan szállítható és tesztelhető.

### 3.4 Substring alapú fájlnév egyezés (újonnan azonosított)

A forbidden file listák substring match-csel működnek rossz alapértelmezéssel. Minden fájlnév alapú ellenőrzésnél szóhatár-érzékeny (negative lookbehind) regex szükséges, különben a decorator osztályok nevei (`CachingRepository.cs`) false positive-ot generálnak az eredeti osztályok nevére (`Repository.cs`).

**Elv:** Fájlnév egyezésnél mindig teljes névegyezés vagy szóhatár alapú regex.

---

## 4. Error-driven refinement – nem újrapróbálkozás

A hagyományos megközelítés: Reject → human szöveges pontosítás → újrafutás. Ez **újrapróbálkozás**, nem javítás.

Az error-driven refinement ezzel szemben:

```
Validator detektál konkrét violation-t
        ↓
RefinementPromptBuilder → célzott hibaüzenet a következő futás promptjába
        ↓
LLM pontosan azt a hibát kapja vissza amit el kell kerülnie
        ↓
Újrafutás célzott kontextussal
```

**Implementációs részletek:**
- Az auto-reject clarification `[AUTO]` prefixet kap – a loop megkülönbözteti az automatikus és a human rejectet
- Human reject esetén a human szöveges pontosítása kerül a refinement promptba
- A refinement prompt az eredeti system prompt után van fűzve, `--- REFINEMENT CONTEXT ---` szekciókkal elválasztva
- Maximum kísérletszám (`MaxAttempts = 5`) védi a végtelen loop ellen

**Éles eredmény:** Az első sikeres éles futáson 1 auto-reject + 1 refinement futás után Accept született. A `STOP_TOKEN_LEAKED` violation detektálva, a 2. futás tiszta outputot adott.

---

## 5. Infrastruktúra vs. prompt szintű hibák

Az éles tesztek feltártak egy fontos különbséget a hibaosztályok között:

| Hiba típusa | Példa | Kezelés |
|---|---|---|
| **Infrastruktúra szintű** | Stop token szivárgás, context overflow | Kódban javítandó – prompt nem segít |
| **Prompt szintű** | Constraint sértés, hallucinált fájlok | Refinement prompt + validator yaml |
| **Validator szintű** | False positive (substring match) | Validator szabály pontosítása |

**Kritikus tanulság:** A `STOP_TOKEN_LEAKED` violation refinement promptja nem hatott a tartalomra – a stop token eltűnése a `StatelessExecutor` tiszta kontextusának köszönhető, nem a prompt kiegészítésnek. Infrastruktúra hibát nem lehet prompt finomítással orvosolni.

**LlamaSharp specifikus:** Az `InteractiveExecutor` állapotot tart a `_context`-ben – minden refinement futáson a conversation history akkumulálódik és `InvalidInputBatch` hibát okoz. A `StatelessExecutor` minden híváskor tiszta kontextusból indul – ez a helyes választás step-szintű inference-hez. A multi-turn támogatás `LlamaExecutorMode` enummal van elkülönítve.

---

## 6. Folyamatos finomítás – a kontextus növekszik

Minden futás, minden violation és minden ValidationOutcome **adat**. A rendszer nem statikus – a modell viselkedése alapján iterálható:

```
Futás → Violation log → Validator rule finomítás → Következő futás
```

Aggregált mérés az eddigi futások alapján (task_breakdown, local-reasoning modell):

```
STOP_TOKEN_LEAKED       | 3/5 futás → infrastruktúra szintű, LlamaSharp antiprompt
FORBIDDEN_AFFECTED_FILE | 4/5 futás → részben false positive (substring match bug)
TASK_COUNT_VIOLATION    | 0/5 futás → a 6-10 limit megfelelő
TOKEN_LIMIT_PROXIMITY   | 2/5 futás → 1200 token határán mozog
```

---

## 7. A validációs rétegek összefoglalója

```
Kimenet
  │
  ▼
[Univerzális validator]      ← stop token, truncation, token limit proximity
  │
  ├── Error → Auto Reject + violation log + [AUTO] refinement clarification
  │
  └── Pass
        │
        ▼
  [Per-step validator]        ← struktúra, constraint, forbidden keywords
        │
        ├── Error → Auto Reject + célzott FixHint a refinement-hez
        │
        └── Pass / Warning only
              │
              ▼
        [Human validáció]     ← tartalmi helyesség, architektúra, business logika
              │
              ▼
        ValidationOutcome + teljes audit log (attempts száma is rögzítve)
```

**Implementált komponensek:**
- `CompositeOutputValidator` – belépési pont, IOutputValidator implementáció
- `UniversalOutputValidator` – internal, minden stepre (stop token, truncation, proximity)
- `StepValidatorRegistry` – step_id alapú resolver
- `TaskBreakdownValidator` – per-step, yaml rule set + kódolt logika
- `ValidatorYamlReader` – deklaratív szabályok betöltése
- `RefinementPromptBuilder` – violation → prompt augmentation

---

## 8. Amit ez a rendszer nem old meg

Fontos kimondani, hogy a validációs keretrendszer **nem garantálja** a helyes outputot. Nem helyettesíti a human ítéletet a tartalmi kérdésekben. Nem teszi a rendszert matematikailag determinisztikussá.

Amit megold: **a human figyelmet a valódi kérdésekre irányítja**, és mérhetővé teszi a rendszer minőségét az idő múlásával. Egy projekt, ami csak human intuícióra épít a kimenet megítéléséhez, nem skálázható. Ez a keretrendszer azt oldja meg – nem a determinizmust.

---

## 9. Nyitott kérdések és következő lépések

- **`<|-|>` stop token töredék** – Qwen-specifikus, felvételre vár a `KnownStopTokens` listára
- **`field_order` ellenőrzés** – a `ValidatorRuleSet`-ben definiálható, de a `TaskBreakdownValidator` még nem implementálja
- **`code_generation` step** – következő validator implementáció, Roslyn static analysis integrációval
- **Violation rate aggregáció** – jelenleg manuális; audit log parse-olással automatizálható