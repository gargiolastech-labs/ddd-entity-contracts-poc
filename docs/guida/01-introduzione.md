# 01 — Introduzione

## Cos'è questo progetto

`ddd-entity-contracts-poc` è un **Proof of Concept** in .NET 9 / C# 13 che dimostra come standardizzare in modo idiomatico la creazione e l'aggiornamento atomico di entità in un contesto Domain-Driven Design.

Non è un framework. Non è un template di riferimento per applicazioni reali. È una **specifica eseguibile**: ogni decisione architetturale è incarnata in codice compilabile, testato, e versionato; ogni convenzione ha test che la dimostrano.

## Il problema che risolve

In molti codebase DDD si vedono ricorrere gli stessi anti-pattern:

- **Validazione sparpagliata**: ogni metodo dell'aggregate fa controlli a modo suo, e gli errori vengono lanciati come eccezioni.
- **Stato intermedio invalido**: l'aggregate viene mutato durante la validazione; se la validazione fallisce a metà, l'oggetto resta corrotto.
- **Eventi sollevati anche quando nulla cambia**: ogni `Update` solleva `Updated`, anche se l'input era identico allo stato corrente.
- **`null` confuso con "non cambiare"**: non si distingue tra "il client vuole svuotare questo campo" e "il client non ha specificato questo campo".
- **Eccezioni come canale di errore di business**: si lanciano `ArgumentException` per email malformate, intrecciando bug di programmazione e regole di dominio.
- **Generic abstractions premature**: si scrive `Repository<T>`, `Validator<T>`, `Handler<T>` prima ancora di avere due aggregate.

Questo PoC propone una risposta sistematica a ognuno di questi problemi, dimostrata su due aggregate reali:

- **`Customer`** — aggregate pilota originario, copre `Create`, `Update`, behavioral operations (`Activate`/`Deactivate`) e targeted edits (`ChangeEmail`/`ChangePhone`).
- **`Product`** — secondo aggregate, introdotto seguendo letteralmente la procedura della [guida 09](09-aggiungere-nuova-entita.md). Dimostra che la convenzione è replicabile su un dominio diverso (lifecycle a 3 stati `Draft → Published → Archived`, VO composto `Money(Amount, Currency)`, state-machine guards più ricchi).

Avere due aggregate non identici è importante: dimostra che il pattern non è "fitted" su Customer ma è davvero un'astrazione utile.

## Le idee guida

### 1. Result vs Validation

`Result<T>` è **monadico**: short-circuit. Il primo errore interrompe la catena. Usato per gli step della pipeline applicativa, dove un fallimento rende inutile continuare.

`Validation<T>` è **applicativo**: accumula. Tutti gli errori dei field paralleli vengono raccolti prima di decidere se procedere. Usato dentro l'aggregate, dove vogliamo dire al client *tutti* i campi sbagliati in una volta sola.

Il confine tra i due mondi è esplicito: il metodo `ToResult()` di `Validation<T>` è l'unico ponte. Approfondimento: [Shared Kernel — Result vs Validation](03-shared-kernel.md#result-vs-validation).

### 2. Decide → Apply

Ogni operazione che modifica un aggregate è divisa in due fasi non sovrapponibili:

- **Decide**: pura. Costruisce uno `ValidatedState` candidato. Non muta nulla.
- **Apply**: imperativa. Muta l'aggregate e solleva eventi. Si fida dello stato già validato.

Se Decide fallisce, Apply non viene mai eseguita. L'aggregate non può mai essere in uno stato parzialmente valido. Approfondimento: [conventions/decide-apply.md](../conventions/decide-apply.md).

### 3. Delta<T> per il tracking esplicito del cambiamento

`null` è un valore legittimo, non sinonimo di "non cambiare". Per distinguere "il client vuole azzerare questo campo" da "il client non ha modificato questo campo", esiste `Delta<T>`: un value object che porta sia il valore sia il flag `IsChanged`. Approfondimento: [Shared Kernel — Delta<T>](03-shared-kernel.md#delta).

### 4. Eventi di dominio solo per cambiamenti effettivi

Il pattern Diff su tutta la `ValidatedState` permette di sollevare `Updated` solo se almeno un campo è davvero cambiato. Le transizioni di stato (`Activate`, `Deactivate`) sono behaviorally distinte dagli `Update` di attributi e sollevano eventi semanticamente intention-revealing (`CustomerActivated`, non solo `CustomerUpdated`).

### 5. Boundary tra Domain e infrastructure

Il Domain solleva solo **domain events**. L'Application li intercetta e produce due cose, fisicamente separate:

- **Side-effect** interni al boundary (welcome email).
- **Integration events** verso il mondo esterno, accodati in Outbox.

Gli integration events sono DTO stabili, versionabili, e non contengono mai tipi di dominio. Approfondimento: [05 — Application, eventi e outbox](05-application-events-outbox.md).

## Cosa NON è (ancora)

- Non c'è persistenza (niente EF Core, niente repository).
- Non c'è un broker reale (RabbitMQ, Kafka, Service Bus).
- Non c'è SMTP reale.
- Non c'è un Analyzer/Source Generator che enforci le convenzioni a compile time (è la fase 2 prevista nell'ADR).
- Non c'è MediatR né alcun mediator pattern.

Tutti questi assenti sono **intenzionali**: il PoC esiste per validare le convenzioni, non per essere uno scaffold produttivo.

## Numeri attuali

Al momento della redazione di questa documentazione:

| Layer | LOC sorgenti | Test |
|---|---|---|
| `DddEntityContracts.Domain` | ~850 | 123 |
| `DddEntityContracts.Application` | ~190 | 10 |
| `DddEntityContracts.Api` | ~80 | 3 |
| **Totale** | **~1.120** | **136** |

`dotnet build` e `dotnet test` devono passare a 0 errori, 0 warning su ogni branch.

## Prossimo passo

Per capire come i progetti si parlano tra loro: [02 — Architettura](02-architettura.md).
