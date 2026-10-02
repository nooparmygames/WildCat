# Serialization (Orleans 7+)

All (docs). Two serializers exist: grain call serialization (the Orleans.Serialization framework,
binary, version tolerant) and grain storage serialization (Newtonsoft.Json by default, swappable
through `IGrainStorageSerializer`).

## Marking types

```csharp
[GenerateSerializer, Alias("wildcat.leaderboard.entry")]
public sealed class LeaderboardEntry
{
    [Id(0)] public string PlayerId { get; set; } = "";
    [Id(1)] public string Name { get; set; } = "";
    [Id(2)] public int Score { get; set; }
    [Id(3)] public DateTime EntryTime { get; set; }
}
```

- `[GenerateSerializer]` on every class, struct and record that is passed to a grain method,
  returned from one, stored in grain state, published on a stream, or thrown as an exception.
- `[Id(n)]` on every member to serialize, including private and readonly fields. Unmarked members
  are skipped silently. Ids are scoped per class in an inheritance chain, so base and derived
  can both start at 0.
- `[Alias("...")]` gives a stable wire name so the class can be renamed or moved. Aliases are
  global and unique; generic types append the arity: `[Alias("mytype`2")]`.
- Records: primary constructor parameters get implicit ids in declaration order (do not reorder
  them after deployment); body members need explicit `[Id]` and do not clash with the
  constructor ids. `[GenerateSerializer(IncludePrimaryConstructorParameters = false)]` opts out.
- Grain references and `IGrainObserver` references inside state are fine; Orleans serializes them.
- Collections, dictionaries, arrays, tuples, Guid, DateTime, TimeSpan, enums and the usual BCL
  types are supported out of the box. `Dictionary<IPlayer, ...>` keyed by a grain reference
  works but string keys are friendlier for JSON storage.
- The IDE code fix "add [Id] attributes" exists for `[GenerateSerializer]` types.

## Versioning rules

Allowed: add or remove members at any level; add a subclass; widen numerics (int to long, float
to double); narrow numerics only when values fit (runtime exception otherwise); rename a type
that has an `[Alias]`.

Forbidden: change a field id; change a field type (other than numeric width); change signedness
(int to uint); add, remove or change a base class of a deployed type; turn a record into a class
or the reverse.

## Surrogates for foreign types

For a type you do not own (third-party value type, a Unity struct mirrored on the server), write
a `[GenerateSerializer]` surrogate struct with plain fields and a `[RegisterConverter]` class
implementing `IConverter<TForeign, TSurrogate>`. If the foreign type can be a base class of your
own types, also implement `IPopulator<TForeign, TSurrogate>`.

## External serializers

`Microsoft.Orleans.Serialization.SystemTextJson`, `.NewtonsoftJson`, `.MessagePack` (8.2+) and
`.Protobuf` can take over selected types: `siloBuilder.Services.AddSerializer(sb =>
sb.AddMessagePackSerializer(isSerializable: t => ..., isCopyable: t => false))`. The native
serializer keeps object identity and full type fidelity; the others do not. Prefer native unless
a type already carries the other library's attributes.

## Copying and immutability

Orleans deep-copies arguments and return values (and stream items) so that caller and callee
cannot share mutable objects. `[GenerateSerializer]` also generates the copier. `[Immutable]` on
a type, a member (`[Id(0), Immutable] public byte[] Payload`), or a parameter
(`Task Relay([Immutable] byte[] data)`), or wrapping in `Immutable<T>`, skips the copy. That is
a two-sided promise: nobody mutates the object after the call.

## Grain storage serializers

State persisted by a storage provider is serialized by the provider's
`IGrainStorageSerializer`, default Newtonsoft.Json. Swap it per provider:

```csharp
silo.AddAdoNetGrainStorage("Default", (OptionsBuilder<AdoNetGrainStorageOptions> ob) =>
    ob.Configure<IGrainStorageSerializer>((o, s) => { o.Invariant = "Npgsql"; o.ConnectionString = cs; o.GrainStorageSerializer = s; }));
```

Register a `SystemTextJsonGrainStorageSerializer` (from the SystemTextJson package) or your own
implementation in DI first. JSON state survives adding and removing properties; keep property
names stable or map them.

## Exceptions

Custom exceptions crossing grain boundaries need `[GenerateSerializer]` and `[Id]` on their
extra members like any other type (the 3.x `ISerializable` pattern is no longer the mechanism).
If the receiving side lacks the exception assembly it gets `UnavailableExceptionFallbackException`
carrying the original type name, message and stack trace.
