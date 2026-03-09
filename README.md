# Mini Account & Transfer Service

A production-quality ASP.NET Core 8 Web API implementing a thread-safe, in-memory account and transfer service. Built as a coding exercise for the Backend Developer (.NET) position at DNA Payments.

---

## Task requirements

> **Support two account types:** Current and Savings  
> **Implement endpoints to:** Create an account · Retrieve an account by ID · Freeze/unfreeze an account · Transfer money between accounts  
> **Data storage:** In-memory (no database required)  
> **Business rules:** No transfers from frozen accounts · Cannot exceed available balance · Savings accounts cannot go below minimum balance · Balances must be consistent under concurrency  
> **Optional:** Basic logging or metrics  
> **Deliverables:** Source code · Minimal tests · README covering architecture decisions, concurrency handling, and trade-offs

---

## Getting started

```bash
# Run the API (defaults to InMemory storage)
cd BankingService
dotnet run

# Swagger UI (Development only)
open https://localhost:5001/swagger

# Run all tests
cd BankingService.Tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Switch to EF Core + SQLite (optional)
# In appsettings.json set "Storage": "EfCore"
dotnet run
```

---

## API reference

All routes are versioned. The current version is `v1`.

| Method  | Route                              | Description                         | Success | Errors   |
|---------|------------------------------------|-------------------------------------|---------|----------|
| `POST`  | `/api/v1/accounts`                 | Create a Current or Savings account | 201     | 400      |
| `GET`   | `/api/v1/accounts/{id}`            | Retrieve account by integer ID      | 200     | 404      |
| `PATCH` | `/api/v1/accounts/{id}/freeze`     | Freeze or unfreeze an account       | 200     | 400, 404 |
| `POST`  | `/api/v1/accounts/transfers`       | Transfer funds between accounts     | 200     | 400, 404 |

All error responses use RFC 7807 `ProblemDetails` JSON. `400` responses may come from either FluentValidation (field-level `ValidationProblemDetails`) or a business rule violation (flat `ProblemDetails` with a `detail` string).

### Create account

```json
POST /api/v1/accounts
{
  "ownerId":        "user-42",
  "displayName":    "My Current Account",
  "type":           "Current",
  "initialBalance": 500.00
}
```

Response `201 Created`:
```json
{
  "id":          1,
  "ownerId":     "user-42",
  "displayName": "My Current Account",
  "type":        "Current",
  "balance":     500.00,
  "isFrozen":    false,
  "createdAt":   "2026-03-09T10:00:00Z"
}
```

### Transfer funds

```json
POST /api/v1/accounts/transfers
{
  "fromAccountId": 1,
  "toAccountId":   2,
  "amount":        150.00
}
```

Response `200 OK`:
```json
{
  "transferId":       "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fromAccountId":    1,
  "toAccountId":      2,
  "amount":           150.00,
  "fromBalanceAfter": 350.00,
  "toBalanceAfter":   650.00,
  "executedAt":       "2026-03-09T10:01:00Z"
}
```

### Freeze / unfreeze

```json
PATCH /api/v1/accounts/1/freeze
{ "freeze": true }
```

---

## Architecture decisions

### Layering

```
HTTP Request
    │
    ▼
AccountsController          ← thin HTTP adapter; validates input; converts HTTP ↔ service calls
    │
    ▼
AccountService              ← all business rules; validates state; records metrics
    │
    ▼
IAccountRepository          ← persistence abstraction; hides concurrency strategy
    │
    ├── InMemoryAccountRepository   ← SemaphoreSlim + ConcurrentDictionary
    └── EfCoreAccountRepository     ← EF Core + Serializable transactions
```

Controllers are deliberately thin — they validate the incoming request then delegate entirely to `IAccountService`. Every business rule (minimum balance, freeze guard, balance arithmetic) lives exclusively in `AccountService`. This makes the service independently testable without any HTTP infrastructure.

### Repository pattern with dual implementation

The task required in-memory storage, but real banking services run against relational databases. The `IAccountRepository` interface abstracts the storage backend so both implementations are swappable at startup without changing any service or controller code.

The interface exposes a standard CRUD surface plus one non-standard method:

```csharp
Task<(Account From, Account To)> ExecuteAtomicTransferAsync(
    int fromId,
    int toId,
    Action<Account, Account> operation,
    CancellationToken ct = default);
```

This **delegate pattern** is the critical design decision. `AccountService` passes a lambda containing its validation and mutation logic; the repository wraps it in whatever atomicity mechanism it owns — semaphores for in-memory, a `Serializable` database transaction for EF Core. The service never needs to know which mechanism is in use.

### DI lifetimes and storage switch

Storage is selected at startup via `appsettings.json`:

```json
{ "Storage": "InMemory" }   // default
{ "Storage": "EfCore"   }   // switches to SQLite / SQL Server
```

The lifetime difference is intentional and important:

| Backend  | Lifetime  | Reason                                                           |
|----------|-----------|------------------------------------------------------------------|
| InMemory | Singleton | The `ConcurrentDictionary` is the data store — must live forever |
| EfCore   | Scoped    | `DbContext` must be scoped per HTTP request                      |

### Clean Program.cs via extension methods

`Program.cs` is 12 lines. All registration logic lives in `ServiceCollectionExtensions` and `WebApplicationExtensions`:

```csharp
builder.Services.AddStorage(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddVersionedSwagger();

app.UseStorageInitialisation();
app.UseVersionedSwagger();
app.UseApplicationMiddleware();
```

### Exception handling middleware

`ExceptionHandlingMiddleware` sits at the outermost layer and converts domain exceptions to RFC 7807 `ProblemDetails` responses. Controllers contain `try/catch` only for logging known exceptions before rethrowing — unhandled exceptions bubble up to the middleware as a final safety net.

```
KeyNotFoundException        →  404 Not Found
InvalidOperationException   →  400 Bad Request
Unhandled exception         →  500 Internal Server Error
```

---

## Concurrency handling

This is the most technically interesting part of the service. Two distinct problems must be solved.

### Problem 1 — Lost updates on a single account

With a plain dictionary and no synchronisation, two threads can both read the same balance, both pass the balance check, and both write — one write is silently lost:

```
Thread 1 reads balance:  £100  ✓ (£100 ≥ £80, proceed)
Thread 2 reads balance:  £100  ✓ (£100 ≥ £80, proceed)
Thread 1 writes balance: £100 - £80 = £20
Thread 2 writes balance: £100 - £80 = £20   ← Thread 1's debit is gone; £160 was sent from £100
```

**Solution:** Each account has a dedicated `SemaphoreSlim(1, 1)` stored in a parallel `ConcurrentDictionary<int, SemaphoreSlim>`. Every mutation — balance change or freeze toggle — acquires the semaphore first. The domain model `Account` stays clean (no lock field on the entity); the lock dictionary lives entirely inside `InMemoryAccountRepository`.

```csharp
await ResolveLock(accountId).WaitAsync(ct);
try   { /* read, validate, write */ }
finally { ResolveLock(accountId).Release(); }
```

This also means freeze and transfer are correctly serialised: if a freeze races with a transfer for the same account, they cannot interleave because both acquire the same semaphore.

### Problem 2 — Deadlock on bidirectional transfers

A transfer between accounts A and B must hold both semaphores simultaneously. If Thread 1 holds `lock(A)` waiting for `lock(B)` while Thread 2 holds `lock(B)` waiting for `lock(A)`, both wait forever.

**Solution — Ordered lock acquisition.** Before acquiring any semaphore, the two account IDs are compared and sorted numerically. Both threads always acquire the lock for the account with the **smaller integer ID** first. This breaks the circular-wait condition by construction — no two threads can ever form a cycle.

```csharp
// InMemoryAccountRepository.ExecuteAtomicTransferAsync
var (first, second) = fromId < toId ? (fromId, toId) : (toId, fromId);

await ResolveLock(first).WaitAsync(ct);
try
{
    await ResolveLock(second).WaitAsync(ct);
    try   { operation(from, to); }
    finally { ResolveLock(second).Release(); }
}
finally { ResolveLock(first).Release(); }
```

`Interlocked.Increment` is used for the auto-increment integer ID so account creation is also thread-safe without a lock.

### EF Core concurrency (alternative backend)

When `"Storage": "EfCore"` is set, the in-memory semaphore strategy is irrelevant — multiple API instances may run behind a load balancer. The EF Core repository uses two mechanisms:

- **Optimistic concurrency with `RowVersion`**: `AccountDbContext` marks the `RowVersion` property as a concurrency token. EF Core appends a `WHERE RowVersion = @original` clause to every `UPDATE`. If the row was modified by another process since it was read, `DbUpdateConcurrencyException` is thrown.
- **Serializable transactions for transfers**: `ExecuteAtomicTransferAsync` opens an `IsolationLevel.Serializable` transaction. The database holds range locks for the duration, preventing phantom reads and concurrent writes to the same rows from any process.

---

## Business rules

| Rule                                                               | Enforced in                          |
|--------------------------------------------------------------------|--------------------------------------|
| Transfers from a frozen account are blocked                        | `AccountService` (transfer delegate) |
| Cannot transfer more than available balance                        | `AccountService` (transfer delegate) |
| Savings accounts cannot drop below `SavingsMinimumBalance` (£10)  | `AccountService` (transfer delegate) |
| Savings accounts must open with ≥ minimum balance                 | `AccountService.CreateAccountAsync`  |
| Source and destination accounts must differ                        | `TransferRequestValidator`           |
| `fromAccountId` and `toAccountId` must be > 0                     | `TransferRequestValidator`           |
| Transfer amount must be > 0                                        | `TransferRequestValidator`           |
| `OwnerId` and `DisplayName` must not be empty or exceed max length | `CreateAccountRequestValidator`      |
| Balances remain consistent under concurrent access                 | Repository semaphores / DB tx        |
| Freeze racing with a transfer never corrupts balance               | Same per-account semaphore guards both |

The minimum savings balance is not hardcoded — it is read from `appsettings.json` via `IOptions<AccountSettings>`:

```json
"AccountSettings": {
  "SavingsMinimumBalance": 10.00
}
```

---

## Validation

`FluentValidation` validators are injected directly into `AccountsController` and called explicitly at the top of each write action. This keeps validation visible and in full controller control rather than relying on the framework pipeline.

```csharp
public AccountsController(
    IAccountService                  service,
    ILogger<AccountsController>      logger,
    IValidator<CreateAccountRequest> createValidator,
    IValidator<FreezeRequest>        freezeValidator,
    IValidator<TransferRequest>      transferValidator)
```

Each write action validates before calling the service:

```csharp
var validation = await _createValidator.ValidateAsync(request, ct);
if (!validation.IsValid)
{
    validation.AddToModelState(ModelState, prefix: null);
    return ValidationProblem();
}
```

`AddToModelState` maps FluentValidation's field-keyed errors into ASP.NET Core's `ModelStateDictionary`. `ValidationProblem()` then serialises those into a standard RFC 7807 `ValidationProblemDetails` response with per-field error arrays:

```json
{
  "type":   "https://tools.ietf.org/html/rfc7807",
  "title":  "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Amount":        ["Amount must be greater than zero."],
    "ToAccountId":   ["Source and destination accounts must differ."]
  }
}
```

`AddFluentValidationAutoValidation()` is **not** registered — auto-validation would run the same validators a second time, invisibly, before the action body is reached. Explicit calling is the single source of truth.

Validators are still scanned and registered with DI via:

```csharp
services.AddValidatorsFromAssemblyContaining<CreateAccountRequestValidator>();
```

This separates format validation (empty strings, range checks, valid enum values) from business rule validation (frozen accounts, balance checks), which lives in `AccountService`.

---

## Observability

### Custom metrics — System.Diagnostics.Metrics

`AccountMetricsService` uses `IMeterFactory` (the .NET 8 DI-friendly factory) to create a `Meter` named `BankingService`. Three `Counter<long>` instruments are registered:

| Instrument name               | Tags                                   | When emitted                       |
|-------------------------------|----------------------------------------|------------------------------------|
| `banking.transfers.succeeded` | `from_account_type`, `to_account_type` | After each successful transfer     |
| `banking.transfers.failed`    | `reason`                               | After each failed transfer attempt |
| `banking.accounts.created`    | `account_type`                         | After each account is created      |

Known failure reasons: `frozen`, `insufficient_balance`, `savings_minimum`, `account_not_found`, `unknown`.

Monitor live with `dotnet-counters`:

```bash
dotnet-counters monitor -n BankingService \
  --counters BankingService,\
             Microsoft.AspNetCore.Hosting,\
             Microsoft.AspNetCore.Server.Kestrel,\
             Microsoft.AspNetCore.Routing,\
             Microsoft.AspNetCore.Diagnostics
```

### Built-in ASP.NET Core metrics (zero code required)

ASP.NET Core 8 emits these automatically:

| Meter name                              | What it covers                    |
|-----------------------------------------|-----------------------------------|
| `Microsoft.AspNetCore.Hosting`          | Request duration, active requests |
| `Microsoft.AspNetCore.Routing`          | Route match results               |
| `Microsoft.AspNetCore.Diagnostics`      | Exceptions caught by middleware   |
| `Microsoft.AspNetCore.Server.Kestrel`   | Connections, connection duration  |

### Structured logging

All components use `ILogger<T>` injected via DI. Log messages use structured message templates so log aggregators (Seq, Elastic, Datadog) can index and filter on individual fields:

```csharp
_logger.LogInformation(
    "Transfer {TransferId}: {Amount} from {From} (bal={FromBal}) to {To} (bal={ToBal})",
    response.TransferId, request.Amount, from.Id, from.Balance, to.Id, to.Balance);
```

**Try/catch layering strategy** — each layer has a distinct logging responsibility:

| Layer      | Catches                     | Logs at | Action      |
|------------|-----------------------------|---------|-------------|
| Repository | All exceptions              | Error   | Rethrows    |
| Service    | `KeyNotFoundException`      | Warning | Rethrows    |
| Service    | `InvalidOperationException` | Warning | Rethrows    |
| Service    | Unexpected                  | Error   | Rethrows    |
| Controller | `KeyNotFoundException`      | Warning | Returns 404 |
| Controller | `InvalidOperationException` | Warning | Returns 400 |
| Middleware | All unhandled exceptions    | Error   | Returns 500 |

---

## API versioning

Uses the `Asp.Versioning.Mvc` 8.1.0 package. Current version is `v1`. Routes contain the version segment:

```
GET /api/v1/accounts/7
```

Configuration:
- `DefaultApiVersion = new ApiVersion(1)`
- `AssumeDefaultVersionWhenUnspecified = true` — allows `/api/accounts/7` to work as well
- `ReportApiVersions = true` — includes `api-supported-versions` response header
- `SubstituteApiVersionInUrl = true` — resolves `{version:apiVersion}` in route templates

Adding `v2` requires only a new controller annotated with `[ApiVersion(2)]`; all `v1` routes continue to work unchanged.

---

## XML documentation

All public request/response types and all controller actions carry XML doc comments. Swagger picks these up automatically when `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is set in the `.csproj`:

```csharp
/// <summary>Transfers funds between two accounts.</summary>
/// <param name="request">Transfer details including source, destination and amount.</param>
/// <returns>Transfer receipt with balances after transfer.</returns>
/// <response code="200">Transfer completed successfully.</response>
/// <response code="400">Validation failed or business rule violation.</response>
/// <response code="404">One or both accounts not found.</response>
[HttpPost("transfers")]
public async Task<IActionResult> Transfer([FromBody] TransferRequest request, CancellationToken ct)
```

---

## Trade-offs considered

| Decision | Alternative considered | Reason for this choice |
|----------|------------------------|------------------------|
| Per-account `SemaphoreSlim` | Single global lock | Better throughput: transfers between unrelated account pairs never contend with each other |
| Ordered integer ID lock acquisition | Try-lock with exponential back-off | Simpler, provably deadlock-free without spin loops or retry complexity |
| Integer `Id` (auto-increment via `Interlocked.Increment`) | `Guid` | More natural for a banking API; Guid ordering for ordered locks requires string comparison overhead |
| `ExecuteAtomicTransferAsync` delegate pattern | Repository returns raw accounts; service locks externally | Each repository knows its own concurrency model; keeping locking inside the repository prevents service code from needing to know about semaphores or transactions |
| `InMemoryAccountRepository` as Singleton | Scoped | It IS the data store; scoped would create a new empty dictionary per request |
| `EfCoreAccountRepository` as Scoped | Singleton | `DbContext` is not thread-safe and must be scoped per HTTP request |
| Explicit `IValidator<T>` injection in controller | `AddFluentValidationAutoValidation()` | Validation is visible in the controller, easy to read, easy to test, and not dependent on pipeline magic; auto-validation also cannot pass a `CancellationToken` |
| Exception → HTTP mapping in middleware | `ActionFilter` or `IExceptionHandler` | Middleware runs for all requests, is simpler to test in isolation, and keeps controllers free of cross-cutting concern code |
| `System.Diagnostics.Metrics` with `IMeterFactory` | OpenTelemetry SDK | Proportionate to scope; .NET's built-in metrics API is first-class in .NET 8 and plugs into OpenTelemetry exporters with zero code change later |
| `AutoMapper` with explicit profile | Manual mapping in service | Eliminates repetitive property-copy code; the profile serves as a single documented place where field renames (e.g. `InitialBalance` → `Balance`) are declared |
| `IOptions<AccountSettings>` for minimum balance | Constant in service | Configuration is testable (pass different `IOptions` to unit tests), changeable without recompilation, and follows ASP.NET Core conventions |
| `TimeProvider` injected into `AccountService` | `DateTime.UtcNow` directly | Makes timestamps deterministic in tests — the mock returns a fixed `DateTimeOffset`, allowing `Assert.Equal` on `ExecutedAt` instead of a fragile `>= before` range check |
| `Asp.Versioning.Mvc` for API versioning | URL segment `/v1/` in route string | Proper versioning support including header reporting, default version negotiation, and future multi-version Swagger docs |
| Receiving into a frozen account is allowed | Block both sides | The spec mentions only that frozen accounts cannot *send* funds; blocking receipts is not required and would prevent salary deposits landing on a locked account |
| EF Core 8 (not 9) | EF Core 9 | Matches the target framework; EF Core 9 would add built-in metrics but requires a major version bump |

---

## Test strategy

### Mock-based unit tests — `AccountServiceTests`

`IAccountRepository` and `IAccountMetricsService` are substituted using **NSubstitute** so each test verifies exactly one unit of behaviour in isolation from I/O. `TimeProvider` is also mocked to return a fixed timestamp (`2026-03-09T12:00:00Z`), making `ExecutedAt` assertions exact rather than range-based.

```csharp
mockTimeProvider = Substitute.For<TimeProvider>();
mockTimeProvider.GetUtcNow().Returns(FixedUtcNow);
```

Note the two-statement form: calling `.GetUtcNow()` in the same expression as `Substitute.For<>()` executes the method on the new mock immediately and chains `.Returns()` onto the resulting `DateTimeOffset` value rather than registering a stub.

Every interaction test has exactly **two assertion blocks**:

```csharp
// Assert — method was called
await repo.Received(1).AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());

// Assert — method was called with exact parameters
await repo.Received(1).AddAsync(
    Arg.Is<Account>(a => a.OwnerId == "owner-1" && a.Balance == 300m),
    Arg.Any<CancellationToken>());
```

`SetupAtomicTransfer()` is a shared helper that wires `ExecuteAtomicTransferAsync` to actually invoke the `Action<Account, Account>` delegate the service passes in. This lets the service's own business rule code run under mock conditions without needing a real repository.

### Concurrency tests — `AccountServiceConcurrencyTests`

Concurrency tests live in a dedicated class because they require a real `InMemoryAccountRepository`. Threading behaviour — lock ordering, balance conservation, deadlock prevention — cannot be meaningfully verified through mocks. `TimeProvider.System` is passed directly; no pinned timestamp is needed.

Three tests cover the key scenarios:

| Test | What it proves |
|------|----------------|
| `SimultaneousTransfers_TotalBalanceIsConserved` | 100 concurrent transfers of £10 from a £1,000 account — total money across both accounts equals £1,000 regardless of how many succeed |
| `OppositeDirectionTransfers_NoDeadlock` | A→B and B→A run simultaneously 50 times — test completes (no deadlock), total balance conserved |
| `FreezeInterleavedWithTransfers_BalanceAlwaysConsistent` | Freeze/unfreeze toggles racing against 50 concurrent transfers — no money is created or destroyed |

### Assert.Equivalent vs Assert.Equal

`Assert.Equivalent` is used when comparing a full response object against a known expected value. It performs deep structural comparison in one statement, replacing multiple individual `Assert.Equal` calls:

```csharp
Assert.Equivalent(
    new AccountResponse(stored.Id, stored.OwnerId, stored.DisplayName,
        stored.Type, stored.Balance, stored.IsFrozen, stored.CreatedAt),
    result);
```

`Assert.Equivalent(expected, actual, strict: false)` is used when the expected value is an anonymous type with a subset of fields (e.g. when `CreatedAt` is assigned at runtime and cannot be predicted):

```csharp
Assert.Equivalent(
    new { OwnerId = "user-42", Balance = 250m, Type = AccountType.Savings },
    account,
    strict: false);
```

`Assert.Equal` is retained for single primitive checks (`decimal`, `bool`, `int`) where constructing an expected object would add noise.

### Constructor null guard tests — `ConstructorNullGuardTests`

Every nullable constructor parameter across all components is tested. Each test captures the thrown exception and asserts the correct `ParamName`, not just the exception type:

```csharp
var ex = Assert.Throws<ArgumentNullException>(() =>
    new AccountService(null!, ValidMetrics, ValidMapper,
        ValidSettings, ValidLogger, ValidTimeProvider));

Assert.Equal("repository", ex.ParamName);
```

One test per parameter keeps failures precisely pinpointed. The full set covers `AccountService` (6 params), `AccountsController` (5 params), `InMemoryAccountRepository`, `EfCoreAccountRepository`, `AccountMetricsService`, and `ExceptionHandlingMiddleware`.

### MeterListener-based metrics tests

`AccountMetricsServiceTests` subscribes a `MeterListener` to the service's meter, routes measurements to thread-safe counters, and asserts the counter values and tags after each operation. This is the standard in-process approach for testing `System.Diagnostics.Metrics` code without a real exporter.

---

## NuGet packages

### BankingService

| Package | Version | Purpose |
|---------|---------|---------|
| `Asp.Versioning.Mvc` | 8.1.0 | API versioning infrastructure |
| `Asp.Versioning.Mvc.ApiExplorer` | 8.1.0 | Versioned Swagger document generation |
| `AutoMapper` | 13.0.1 | Object-to-object mapping profiles |
| `FluentValidation` | 11.3.0 | Input validation rules and `IValidator<T>` |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.0 | SQLite provider for EfCore backend |
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.0.0 | SQL Server provider for EfCore backend |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.0 | EF Core tooling (migrations) |
| `Swashbuckle.AspNetCore` | 6.6.2 | Swagger / OpenAPI UI |

Note: `FluentValidation.AspNetCore` is no longer required — validators are injected directly into the controller and called explicitly, so the auto-validation middleware integration is not used.

### BankingService.Tests

| Package | Version | Purpose |
|---------|---------|---------|
| `NSubstitute` | 5.1.0 | Mock/stub framework |
| `AutoMapper` | 13.0.1 | Mapper configuration in mapping tests |
| `FluentValidation` | 11.3.0 | Validator instantiation in validator tests |
| `Microsoft.AspNetCore.Mvc.Testing` | 8.0.0 | `WebApplicationFactory` for integration tests |
| `Microsoft.EntityFrameworkCore.InMemory` | 8.0.0 | Real `DbContext` for `EfCoreAccountRepository` null guard tests |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.0 | `NullLogger<T>` for unit tests |
| `xunit` | 2.9.0 | Test framework |
| `coverlet.collector` | 6.0.2 | Code coverage collection |

---

## Configuration reference

```json
{
  "Storage": "InMemory",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=banking.db"
  },
  "AccountSettings": {
    "SavingsMinimumBalance": 10.00
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Set `"Storage": "EfCore"` to switch to SQLite. The database file `banking.db` is created automatically on first startup via `EnsureCreated()`.

---

## What is not included

The following are deliberate omissions, proportionate to the exercise scope:

- **Authentication / authorisation** — no JWT or API key validation; any caller can access any account
- **Retry logic** — `EfCoreAccountRepository` throws `InvalidOperationException` on concurrency conflict; a real service would retry with exponential back-off
- **Migrations** — `EnsureCreated()` is used instead of `dotnet ef migrations`; suitable for prototypes, not production
- **Serilog / structured log sinks** — the service uses the built-in `ILogger<T>` abstraction; replacing the provider with Serilog requires only a one-line change in `Program.cs`
- **OpenTelemetry exporter** — the `System.Diagnostics.Metrics` API integrates with any OTLP exporter; adding one requires only a NuGet package and `builder.Services.AddOpenTelemetry(...)`
- **EF Core 9 metrics** — `Microsoft.EntityFrameworkCore` built-in metrics are only available in EF Core 9+
- **Pagination** — `GET /api/v1/accounts` (list all) is not implemented
- **Persistence across restarts** — InMemory data is lost on restart by design; switch to EfCore for durability
