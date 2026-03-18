# Scaffold Protocol – Output Validáció: Elvek és Megközelítés

> **Státusz:** Elfogadott architektúrális döntés  
> **Kontextus:** Scaffold Protocol – human-in-the-loop AI fejlesztési pipeline

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

---

## 3. A veszélyek – amit el kell kerülni

### 3.1 Túl strict validátor → false positive rejectek

Ha a forbidden keyword lista vagy a field order check túl merev, a rendszer folyamatosan visszautasítja az egyébként helyes outputokat. A refinement loop végtelen körökbe kerül. Ez **frusztrálóbb és károsabb mint a laza validáció.**

**Elv:** Csak olyat validálj automatikusan, amire biztosan tudod, hogy rossz.

### 3.2 LLM-as-judge a tartalomra

Az LLM-as-judge megközelítés csábító, de a tartalom helyességének megítélésére **nem megbízható bíró** – maga is LLM, maga is variáns. Erre a rétegre automatizmust építeni félrevezető biztonságérzetet ad.

**Elv:** Az LLM judge csak az UNCERTAIN kategóriák human elé irányítására használható, nem döntéshozóként.

### 3.3 Korai túlkomplexitás

Ha a validációs infrastruktúra fejlesztése megelőzi a tényleges funkcionalitást, a fejlesztési erőforrás rossz helyre megy. Az első két implementációs lépés (UniversalValidator + TaskBreakdownValidator) már önmagában értéket ad.

**Elv:** Fokozatos építkezés – minden lépés önállóan szállítható és tesztelhető.

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

**Példa refinement prompt kiegészítés:**
```
Previous attempt violations:
  - [FORBIDDEN_AFFECTED_FILE] Task 3 lists IRepository.cs as affected file.
    Fix: IRepository<T> is closed for modification. Remove this task or
    redirect the change to CachingRepository.cs.
  - [CACHE_INVALIDATION_IN_SERVICE] Task 7 places cache logic in ProductService.cs.
    Fix: Cache invalidation must be in CachingRepository<T> Add/Update/Delete methods.
```

A human pontosítás megmarad azokra a hibákra ahol az automatikus validator nem tud violation-t generálni – de a mechanikus hibákat a rendszer önállóan kezeli.

---

## 5. Folyamatos finomítás – a kontextus növekszik

Minden futás, minden violation és minden ValidationOutcome **adat**. A rendszer nem statikus – a modell viselkedése alapján iterálható:

```
Futás → Violation log → Validator rule finomítás → Következő futás
```

Aggregált mérés több futás után:

```
task_breakdown | STOP_TOKEN_LEAKED      | 3/5 futás → infrastruktúra bug
task_breakdown | FORBIDDEN_KEYWORD      | 2/5 futás → prompt finomítás szükséges  
task_breakdown | TOKEN_LIMIT_PROXIMITY  | 5/5 futás → max_tokens emelés szükséges
```

Ez az a mérési alap, ami alapján a prompt iterációk hatása **számszerűsíthetővé válik** – nem érzés alapon mondod, hogy „jobb lett", hanem a violation rate csökkent.

Az adott LLM kontextusa ezáltal futásról futásra gazdagodik: a validator szabályok és a refinement promptok együtt alkotják azt a tudásbázist, ami a modellt egyre szorosabb tartományba tereli – anélkül, hogy a modellt magát módosítanád.

---

## 6. A validációs rétegek összefoglalója

```
Kimenet
  │
  ▼
[Univerzális validator]      ← stop token, truncation, token limit proximity
  │
  ├── Error → Auto Reject + violation log + refinement prompt
  │
  └── Pass
        │
        ▼
  [Per-step validator]        ← struktúra, constraint, forbidden keywords, Roslyn
        │
        ├── Error → Auto Reject + célzott FixHint a refinement-hez
        │
        └── Pass / Warning only
              │
              ▼
        [Human validáció]     ← tartalmi helyesség, architektúra, business logika
              │
              ▼
        ValidationOutcome + teljes log
```

---

## 7. Amit ez a rendszer nem old meg

Fontos kimondani, hogy a validációs keretrendszer **nem garantálja** a helyes outputot. Nem helyettesíti a human ítéletet a tartalmi kérdésekben. Nem teszi a rendszert matematikailag determinisztikussá.

Amit megold: **a human figyelmet a valódi kérdésekre irányítja**, és mérhetővé teszi a rendszer minőségét az idő múlásával. Egy projekt, ami csak human intuícióra épít a kimenet megítéléséhez, nem skálázható. Ez a keretrendszer azt oldja meg – nem a determinizmust.
