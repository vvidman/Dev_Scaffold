# reference-readiness – task sorrend

Cél: a repó bemutatása rendszertervezési referenciaként egy álláspályázat mellé.

| # | Task | Típus | Függ | Becsült méret |
|---|---|---|---|---|
| 01 | Konfigurációs higiénia, `DevScaffold` assembly név | takarítás | – | S |
| 02 | Artifact path guard (path traversal) | **biztonsági javítás** | – | S |
| 03 | Fire-and-forget hibaág + CLI liveness timeout | **hibajavítás** | – | M |
| 04 | GenerationCalculator / ArtifactApplier kiemelése | refaktor | 02 | S |
| 05 | Tesztprojektek (MSTest + NSubstitute) | teszt | 02, 03, 04 | L |
| 06 | GitHub Actions CI + badge | CI | 05 | S |
| 07 | Validációs elvek → angol ADR, elnevezés, doksi pontosítás | doksi | 01, 05 | M |
| 08 | Reviewer guide, szekvenciadiagram, Trade-offs, specs/README | doksi | 03, 07 | M |
| 09 | (opcionális) Kódszövegek angolra — az A) rész javasolt | nyelv | 05, 08 | L |
| 10 | Utóellenőrzés javításai — README, követett lokális fájlok, liveness heartbeat | javítás | 01, 03, 05–08 | M |

Futtatási sorrend: 01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → (09) → 10

Minden task után: `dotnet build DevScaffold/DevScaffold.slnx`, a 05-től `dotnet test` is,
és a meglévő end-to-end folyamat (`--step`, Accept, `--apply --dry-run`) egy élő futtatással.

A 02 és 03 valódi hibákat javít, amelyeket az átnézés során találtam:
- 02: `Path.Combine(root, llmPath)` abszolút `llmPath` esetén eldobja a rootot → az LLM kimenet bárhová írhat.
- 03: a `HandleInferAsync` catch ága nem kapja el a `Task.Run` kivételeit → pl. egy párhuzamos kérésnél a CLI esemény nélkül, végtelenül vár.
