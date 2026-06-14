# Value Object Authoring Convention

## Obiettivo

I Value Object (VO) rappresentano concetti del dominio definiti interamente dal loro valore, non dalla loro identità.
Devono essere:

- **immutabili**: nessun cambiamento di stato dopo la creazione;
- **validati alla nascita**: un'istanza VO che esiste è per definizione valida;
- **confrontabili per valore**: due VO con gli stessi dati sono uguali.

Senza questi tre requisiti un VO diventa un plain data container senza garanzie, difficile da testare e da ragionare.

---

## Regole obbligatorie

| Regola | Motivazione |
|---|---|
| Preferire `record` o `readonly record struct` | Value equality gratuita, immutabilità strutturale |
| Se si usa `class`, implementare `Equals` / `GetHashCode` / `==` / `!=` in modo esplicito | `class` usa reference equality di default: due istanze con gli stessi dati risultano disuguali |
| Nessun setter pubblico | Un VO mutabile non è un VO |
| Nessun costruttore pubblico che bypassa la validazione | La validazione deve essere l'unico ingresso al tipo |
| Obbligatorio un metodo `Create(...)` come unico punto di ingresso | Garantisce che ogni istanza sia valida per costruzione |
| `Create(...)` deve ritornare `Validation<TValueObject>` | Permette l'accumulo degli errori; il chiamante decide quando collassare a `Result<T>` |
| I guard devono ritornare `Validation<T>`, non `Result<T>` | `Validation<T>` è applicativo (accumula); `Result<T>` è monadico (short-circuit) |
| Il mapping verso `Result<T>` avviene solo a livello di entità o use case tramite `ToResult()` | Separa la logica di validazione (VO) dalla logica di esecuzione (Entity/Application) |
| Nessuna eccezione per validazioni di business attese | Le eccezioni sono per errori di programmazione, non per input utente invalidi |

---

## Esempio valido — campo singolo con più guard

Pattern: cortocircuito sul guard fondamentale, poi accumulo parallelo sui rimanenti.

```csharp
public sealed record Email
{
    public string Value { get; }

    private Email(string value) { Value = value; }

    public static Validation<Email> Create(string? value)
    {
        // Prima regola: cortocircuito immediato se vuoto.
        var notEmpty = ValueObjectGuards.NotNullOrWhiteSpace(
            value, "Email.Empty", "L'email non può essere vuota.");

        if (notEmpty.IsFailure)
            return Validation<Email>.Failure(notEmpty.ToResult().Errors);

        var v = notEmpty.ToResult().Value;

        // Guard successivi: accumulo parallelo degli errori.
        var maxLen = ValueObjectGuards.MaxLength(v, 254, "Email.TooLong", "L'email supera 254 caratteri.");
        var format  = ValueObjectGuards.Matches(
            v, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", "Email.InvalidFormat", "Formato email non valido.");

        return Validation<Email>.Combine(maxLen, format, (_, validated) => new Email(validated));
    }
}
```

---

## Esempio valido — più campi con accumulo cross-field

Pattern: un guard per campo, `Combine` per accumulare tutti gli errori di tutti i campi.

```csharp
public sealed record PersonName
{
    public string First { get; }
    public string Last { get; }

    private PersonName(string first, string last) { First = first; Last = last; }

    public static Validation<PersonName> Create(string? first, string? last)
    {
        var vFirst = ValueObjectGuards.NotNullOrWhiteSpace(
            first, "Name.FirstEmpty", "Il nome è obbligatorio.");
        var vLast  = ValueObjectGuards.NotNullOrWhiteSpace(
            last,  "Name.LastEmpty",  "Il cognome è obbligatorio.");

        // Accumula gli errori di entrambi i campi in parallelo.
        return Validation<PersonName>.Combine(vFirst, vLast, (f, l) => new PersonName(f, l));
    }
}
```

---

## Esempio pericoloso / non valido

```csharp
// SBAGLIATO: class senza value equality, setter pubblici, costruttore pubblico che bypassa la validazione.
public class Money
{
    public decimal Amount { get; set; }   // setter pubblico: mutabile
    public string Currency { get; set; }  // setter pubblico: mutabile

    // costruttore pubblico: bypassa ogni controllo
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }
}

var m1 = new Money(100, "EUR");
var m2 = new Money(100, "EUR");
bool equal = m1 == m2; // false! reference equality — bug silenzioso nelle collection e nei confronti
```

Problemi:
- `m1 == m2` è `false` anche se rappresentano lo stesso concetto di dominio;
- chiunque può scrivere `money.Amount = -1` dopo la creazione;
- è possibile costruire un `Money` con `Amount = -9999` senza alcuna validazione.

---

## Pattern per ID tipizzati

`readonly record struct` non può essere ereditato. Ogni ID di dominio replica il pattern con il proprio nome:

```csharp
public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());
    public static CustomerId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

`StronglyTypedId` nel `SharedKernel` è il tipo di riferimento/documentazione di questo pattern.
Non estenderlo — copiarlo.

---

## Nota per Analyzer futuro

Questa convenzione sarà la base per le regole di analisi statica pianificate:

| Regola | Descrizione |
|---|---|
| `VO001` | VO senza metodo `Create` statico |
| `VO002` | VO `class` senza override di `Equals` / `GetHashCode` |
| `VO003` | VO con setter pubblico |
| `VO004` | VO con `Create` che ritorna `Result<T>` invece di `Validation<T>` |
| `VO005` | VO con costruttore pubblico che bypassa la validazione |
