# Blog Writing Context – Scaffold Protocol

> Ez a dokumentum egy új chat számára készült, hogy kontextust adjon egy blog cikk megírásához.
> A blog két nyelven jelenik meg: angol és magyar.

---

## A blog célja

Bemutatni a Scaffold Protocol rendszert – hogyan épült, miért így épült, és mi a szerepe a human orchestratornak. A blog nem egy eszköz reklámja, hanem egy mérnöki gondolatmenet: hogyan lehet AI-t bevonni a fejlesztési folyamatba úgy, hogy a kontroll végig az embernél marad.

---

## Célközönség

Széles technikai közönség – nem csak .NET fejlesztők. Olyan olvasók akik hallottak az LLM-alapú fejlesztési eszközökről, de kíváncsiak hogy a gyakorlatban hogyan néz ki egy jól megtervezett, kontrollált rendszer.

---

## A három pillér amit a blognak fel kell építenie

### 1. AI mint transzformációs motor, nem döntéshozó

Az AI egyetlen feladata: adott inputból adott formátumú outputot generálni. Nem tervez, nem dönt, nem orchestrál. Ez tudatos döntés – nem a modell képességeinek hiányából fakad, hanem az irányíthatóság elvéből.

A human szerepe: orchestrator. Ő dönti el mi lesz a következő lépés, ő értékeli az outputot, ő adja a kontextust. Az AI gyorsítja a végrehajtást, de nem vezeti a folyamatot.

### 2. A validációs réteg – biztosan rossz kimenetek kiszűrése

A kulcsgondolat: nem a tökéletes outputot keressük, hanem a biztosan rosszakat szűrjük ki.

Ez a határvonal a legfontosabb tervezési döntés:
- **Automatizálható:** struktúra, constraint megfelelés, stop token, compile error
- **Nem automatizálható:** architektúrális helyesség, business logika, tartalmi ízlés

Ha a validator fail-t ad → az output biztosan rossz.
Ha pass-t ad → az output *lehet* jó, a human dönti el.

A human figyelme így a valódi kérdésekre fókuszál.

### 3. Error-driven refinement – nem újrapróbálkozás

Ha az output hibás, a rendszer nem egyszerűen újrafuttat. A konkrét violation → célzott hibaüzenet → refinement prompt. Az LLM pontosan azt a hibát kapja vissza amit javítani kell.

Ez a különbség a retry és a refinement között:
- **Retry:** ugyanaz a kérés, véletlen másik output
- **Refinement:** konkrét hiba + célzott javítási utasítás

---

## A rendszer neve és kontextusa

**Scaffold Protocol** – human-in-the-loop, YAML-alapú, lépésenkénti fejlesztési eszköz .NET projektekhez.

Főbb komponensek amiket érdemes megemlíteni (de nem technikai mélységben):
- Step agent config YAML – minden lépés saját system prompttal és szabályokkal
- Validator réteg – univerzális + per-step, deklaratív yaml szabályok + kód
- Refinement loop – auto-reject → célzott prompt → újrafutás
- Human validáció – Accept / Edit / Reject, minden lépés után

**Nem pipeline** – ezt fontos kiemelni. Nem egy input folyik végig egy láncon. Minden step önálló, egymásra hatással vannak, de a human dönti el mikor mi következik.

---

## Valós adatok a bloghoz

Az éles tesztelés során mért adatok (task_breakdown step, Qwen2.5-7B-Instruct lokális modell):

| Futás | Auto-reject oka | Refinement | Eredmény |
|---|---|---|---|
| 1. | – (validator még nem volt) | Human reject | Tanulság: prompt finomítás |
| 2. | – | Human reject | Truncation + constraint sértés |
| 3. | – | Human (Edit javasolt) | Stop token szivárgás |
| 4. | FORBIDDEN_AFFECTED_FILE (false positive) | Auto × 3 | Validator bug azonosítva |
| 5. | STOP_TOKEN_LEAKED | Auto × 1 | **Accept** – 2. futáson sikeres |

Az 5. futáson a human megjegyzése az elfogadott outputra: *„én máshogy csinálnám, de nem tudok belekötni."* – Ez pontosan a helyes működés. A tartalmi ízlésbeli különbség nem validator kérdés.

---

## Hangnem és stílus

- Első személyű, mérnöki hangnem – egy fejlesztő meséli el hogyan építette a rendszert
- Nem marketing szöveg – az önazonos mérnöki döntések és a tanulságok a fontosak
- A kudarcok és a false positive-ok is bele kell kerüljenek – ezek a legértékesebb tanulságok
- Kerülendő: „forradalmi", „game-changer", „AI-powered" buzzwordök

---

## Javasolt cikkstruktúra

1. **Nyitó:** Mi a probléma amit megoldani akartam? (AI bevonása fejlesztésbe, kontroll megtartása)
2. **Az alapelv:** Human orchestrator, AI transzformációs motor
3. **Hogyan épül fel egy step?** – YAML config, system prompt, input, output
4. **A validáció gondolata:** Nem tökéletes outputot keresünk, biztosan rosszat szűrünk
5. **Error-driven refinement:** Retry vs. refinement – a különbség
6. **Valós számok:** A tesztelés tanulságai, a false positive eset
7. **Zárás:** Hol tart most a rendszer, mi következik

---

## Amit az új chat-ben NEM kell újra elmagyarázni

- A Scaffold Protocol technikai részletei (Named Pipes, LLamaSharp, protobuf) – ezek nem a blog témái
- A validator implementáció részletei – csak az elv számít a bloghoz
- A .NET specifikus kód – kerülendő a blogban

---

## Amit az új chat-ben el kell dönteni

- A blog egységes kétnyelvű dokumentum legyen (EN + HU egymás után), vagy két külön fájl?
- Legyen-e code snippet a blogban? Ha igen, milyen szintű?
- Mi legyen a cím? (mindkét nyelven)
