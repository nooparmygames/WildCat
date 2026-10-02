# Grain persistence, providers and transactions

All (docs) unless marked.

## API

```csharp
public interface IPersistentState<TState> : IStorage<TState> { }
public interface IStorage<TState> : IStorage { TState State { get; set; } }
public interface IStorage
{
    string Etag { get; }
    bool RecordExists { get; }
    Task ClearStateAsync();
    Task WriteStateAsync();
    Task ReadStateAsync();
}
```

Inject with `[PersistentState("stateName", "providerName")] IPersistentState<T>` in the
constructor. Several named states per grain are fine, each on a different provider if wanted.
State is read during the `SetupState` lifecycle stage, so it is not available in the constructor
but is available in `OnActivateAsync`. `RecordExists` tells a new grain apart from a loaded one.
Writes are explicit: mutate `State`, `await WriteStateAsync()`. `ReadStateAsync()` reloads from
storage (after an external change). `ClearStateAsync()` clears, and depending on the provider
deletes, the record. The legacy `Grain<TState>` + `[StorageProvider(ProviderName = "...")]`
still works and exposes the same three methods, but the docs call it legacy.

Failure modes: a failed initial read fails the activation (no `OnActivateAsync`, caller gets the
exception). A failed write throws from `WriteStateAsync`; if the grain catches it, it must not
rethrow after handling. ETag conflicts surface as `InconsistentStateException`, which also
deactivates the grain so the next call reloads fresh state. A missing or bad provider name
gives `BadProviderConfigException` on every call.

Pattern for the write-after-each-mutation style used throughout this backend:

```csharp
public async Task AddScore(string player, int score)
{
    state.State.Entries.Add(new LeaderboardEntry { PlayerId = player, Score = score });
    await state.WriteStateAsync();
}
```

Batching writes behind a grain timer trades durability for throughput; the docs list it as a
legitimate option for state that tolerates loss.

## Providers

### Memory (dev and tests)

`silo.AddMemoryGrainStorage("Default")`, `AddMemoryGrainStorageAsDefault()`. Data lives in grains
inside the cluster and is lost on restart. Streams also need `AddMemoryGrainStorage("PubSubStore")`.

### ADO.NET (PostgreSQL, MySQL/MariaDB, SQL Server, Oracle)

Packages: `Microsoft.Orleans.Persistence.AdoNet`, `Microsoft.Orleans.Clustering.AdoNet`,
`Microsoft.Orleans.Reminders.AdoNet`, plus the driver (`Npgsql` for PostgreSQL, `MySql.Data`,
`Microsoft.Data.SqlClient` for SQL Server in 10.0, `Oracle.ManagedDataAccess`).

Invariants: `Npgsql`, `MySql.Data.MySqlClient`, `Microsoft.Data.SqlClient` (10.0; older docs
show `System.Data.SqlClient`), `Oracle.DataAccess.Client`.

SQL scripts (run Main first, then the ones you use) live in the dotnet/orleans repo:
`src/AdoNet/Shared/PostgreSQL-Main.sql`,
`src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql`,
`src/AdoNet/Orleans.Persistence.AdoNet/PostgreSQL-Persistence.sql`,
`src/AdoNet/Orleans.Reminders.AdoNet/PostgreSQL-Reminders.sql` (same names for MySQL, SQLServer,
Oracle). Migrations for existing schemas sit in `Migrations` folders next to them. Every silo
must reach the database; missing access is the most common setup error.

```csharp
var invariant = "Npgsql";
var cs = builder.Configuration.GetConnectionString("orleans");
builder.UseOrleans(silo =>
{
    silo.UseAdoNetClustering(o => { o.Invariant = invariant; o.ConnectionString = cs; });
    silo.UseAdoNetReminderService(o => { o.Invariant = invariant; o.ConnectionString = cs; });
    silo.AddAdoNetGrainStorage("Default", o => { o.Invariant = invariant; o.ConnectionString = cs; });
});
// external clients only need clustering:
builder.UseOrleansClient(c => c.UseAdoNetClustering(o => { o.Invariant = invariant; o.ConnectionString = cs; }));
```

`AdoNetGrainStorageOptions`: `ConnectionString`, `Invariant`, `GrainStorageSerializer`
(default Newtonsoft JSON), `InitStage`, `HashPicker`. The 3.x `UseJsonFormat` flag is gone.
The provider is designed to be shardable and the queries live in the `OrleansQuery` table, so a
DBA can tune them without redeploying.

### Redis (stable in 10.0)

`AddRedisGrainStorage("Default", o => o.ConfigurationOptions = new ConfigurationOptions
{ EndPoints = { "localhost:6379" }, AbortOnConnectFail = false })` or
`AddRedisGrainStorageAsDefault(...)`. Options: `DeleteStateOnClear` (false), `EntryExpiry`
(tests only, causes duplicate activations otherwise), `GrainStorageSerializer`,
`CreateMultiplexer`, `GetStorageKey` (default key `{ServiceId}/state/{grainId}/{grainType}`).
Clustering: `UseRedisClustering`. Reminders: `UseRedisReminderService`. Durability depends on
the Redis persistence configuration (AOF/RDB).

### Azure Storage and Cosmos DB

`AddAzureTableGrainStorage` (64 KB per entity), `AddAzureBlobGrainStorage` (large state),
`UseAzureStorageClustering`, `UseAzureTableReminderService`; authenticate with
`options.TableServiceClient = new TableServiceClient(uri, new DefaultAzureCredential())`.
Cosmos: `AddCosmosGrainStorage`, `UseCosmosClustering`, `UseCosmosReminderService`, with
`IsResourceCreationEnabled = true` to auto-create the database and container.

### MongoDB (community, OrleansContrib)

Package `Orleans.Providers.MongoDB` 9.5.0 (2026-01-12), targets net8.0, depends on
`Microsoft.Orleans.Reminders >= 9.0.1` and `MongoDB.Driver >= 3.0.0`. API:
`UseMongoDBClient(connectionString)`, `UseMongoDBClustering(o => o.DatabaseName = ...;
o.Strategy = MongoDBMembershipStrategy.SingleDocument)`, `UseMongoDBReminders(...)`,
`AddMongoDBGrainStorage("Default", o => ...)`. Its README states "migration to Orleans 9.0
completed"; nothing published confirms Orleans 10 support (inferred: the >= 9.0.1 dependency will
let NuGet resolve against 10.x, but binary compatibility is not guaranteed). Verify with the
test cluster before committing to it, or move to an official provider. The legacy code in this
repo used the 3.3.1 version of this package with
`ConfigureJsonSerializerSettings` on the storage options; those settings no longer exist in the
same form.

## Transactions

Opt in on silo and client: `silo.UseTransactions()`, `client.UseTransactions()`. Transactional
state uses its own storage abstraction (`AddAzureTableTransactionalStateStorage("TransactionStore",
...)`; DynamoDB transactional storage arrived in 10.3). Without one, Orleans bridges to a regular
`IGrainStorage` provider, which is documented as development-only.

Interface methods carry `[Transaction(TransactionOption.X)]`: `Create` (always new),
`Join` (must be inside one), `CreateOrJoin`, `Suppress`, `Supported`, `NotAllowed`. The grain
injects `[TransactionalState("balance")] ITransactionalState<Balance>` and reads or writes only
through `PerformRead(s => ...)` / `PerformUpdate(s => ...)`. A transactional grain must be
`[Reentrant]`. `OnActivateAsync` cannot touch transactional state. Clients coordinate with
`ITransactionClient.RunTransaction(TransactionOption.Create, async () => { ... })`. Failures
throw `OrleansTransactionException` (application exception as inner);
`OrleansTransactionAbortedException` means retry is safe, anything else means unknown outcome,
so wait out `SystemResponseTimeout` before retrying. The legacy backend called
`UseTransactions()` but no grain in it used transactional state; drop it unless a real need
appears (the wallet/inventory purchase path is the only plausible candidate).
