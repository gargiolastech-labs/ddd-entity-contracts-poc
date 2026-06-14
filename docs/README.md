# Documentazione

Questa cartella raccoglie tutta la documentazione tecnica di `ddd-entity-contracts-poc`.

## Percorso consigliato di lettura

Per chi si avvicina al progetto per la prima volta, seguire l'ordine numerico in `guida/`:

| # | Documento | Quando leggerlo |
|---|---|---|
| 01 | [Introduzione](guida/01-introduzione.md) | Sempre. Spiega cosa fa il PoC e perché esiste. |
| 02 | [Architettura](guida/02-architettura.md) | Per capire come sono organizzati i progetti e i confini tra layer. |
| 03 | [Shared Kernel](guida/03-shared-kernel.md) | Per padroneggiare le primitive (`Result`, `Validation`, `Delta`, `Entity`, `Error`). |
| 04 | [Aggregate Customer — walkthrough](guida/04-customer-walkthrough.md) | Per vedere in pratica come si compone un aggregate. |
| 05 | [Application — eventi e outbox](guida/05-application-events-outbox.md) | Per capire come gli eventi di dominio escono dal Domain. |
| 06 | [API — pipeline ProblemDetails](guida/06-api-problemdetails.md) | Per capire come `Result` viene tradotto in HTTP. |
| 07 | [Strategia di test](guida/07-testing.md) | Prima di scrivere nuovi test. |
| 08 | [Glossario DDD](guida/08-glossario.md) | Riferimento per la terminologia. |

## Documentazione di riferimento

Convenzioni di authoring (regole obbligatorie, non opinabili):

- [Decide / Apply Pattern](conventions/decide-apply.md) — fase di validazione vs fase di mutazione.
- [Value Object Authoring](conventions/value-object-authoring.md) — come scrivere un VO corretto.

Decisioni architetturali:

- [ADR-001 — Domain Create / Update Convention](adr/ADR-001-domain-create-update-convention.md) — convenzione formale per Create/Update degli aggregate.

Template operativi:

- [Aggregate Authoring Template](templates/aggregate-authoring-template.md) — scheletro pronto da copiare per un nuovo aggregate.

## Per ruolo

**Sei un nuovo collaboratore?** Leggi 01 → 02 → 04 nell'ordine.

**Stai aggiungendo un nuovo aggregate?** Studia 03, 04 e 05; poi parti dal [template](templates/aggregate-authoring-template.md).

**Stai esponendo un nuovo endpoint?** 06 + i test in `tests/DddEntityContracts.Api.Tests/`.

**Stai aggiungendo un side-effect su evento di dominio?** 05 + esempi in `src/DddEntityContracts.Application/Customers/EventHandlers/`.

**Stai facendo code review?** Tieni aperti 03 (per le primitive) e [ADR-001](adr/ADR-001-domain-create-update-convention.md).
