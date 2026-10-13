# WildCat customization and developer experience roadmap

WildCat should make backend customization feel as natural as working in a game engine.
Developers should be able to add or replace game-specific behavior, inspect its state, and
step through its code without maintaining a fork of the backend.

Features can be implemented as a grain, a set of grains, an ordinary C# service, or a module
combining them. Provide useful defaults and clear extension points while letting developers
choose the implementation that fits their game. Prioritize compiled C# extensions over runtime
scripting.

The following milestones continue the completed migration roadmap, in implementation order.

## 10. Add multilingual serialization and contract support

- Add support for Glenn Fiedler's `serialize` family, including `serialize.cs`, at the
  client/server serialization boundary. Use the companion `schema` project for shared data
  definitions and generated serializers across languages.
- Keep one authoritative contract definition. Generate language-specific types rather than
  maintaining separate handwritten definitions for the server and each client.
- Integrate generation into the build and SDK packaging workflow. Support Unity's C# 9,
  netstandard2.1 and IL2CPP constraints. Provide additional language bindings as supported
  SDK targets; a serializer port alone is not a complete client SDK.
- Pin dependency and generated format versions. Define a protocol migration and compatibility
  policy, with explicit handling for clients that speak an incompatible format.
- Preserve authentication, message bounds and the separation of relay traffic from server
  commands. Keep client-facing serialization separate from Orleans' internal serialization
  and PostgreSQL state persistence.

The relevant upstream projects are [serialize](https://github.com/mas-bandwidth/serialize),
[serialize.cs](https://github.com/mas-bandwidth/serialize.cs) and
[schema](https://github.com/mas-bandwidth/schema). Serialization describes data on the wire;
WildCat remains responsible for RPC routing, identity, request lifetime and subscriptions.

## 11. Support compiled game modules and generated RPCs

- Let developers reference WildCat from their own C# projects and register modules through
  public hosting and dependency-injection APIs. Adding a feature must not require editing
  WildCat's gateway assembly or forking the framework.
- Provide explicit extension points for custom commands and policies, including rewards and
  matchmaking eligibility. Document ordering, allowed effects and failure behavior.
- Generate typed client calls, server dispatch adapters and serialization bindings from
  declared public operations. Include stable operation identities, consistent errors,
  request/response correlation, deadlines and tracing metadata.
- Use a language-neutral contract model underneath the tooling. C# declarations may provide
  an authoring interface, but must not create a second manually maintained source of truth.
- Keep public operations separate from internal grain methods. Expose only intentionally
  declared client APIs; derive player and game identity from authenticated server context.
- Let an operation delegate to one grain, coordinate several grains, or invoke another
  service. Support straightforward explicit bindings where a separate adapter adds no value.
- Emit inspectable source with useful build diagnostics. Developers should be able to step
  through a generated call and put a breakpoint in their own feature implementation.

A typical project has shared contracts, a server module and a Unity client. A crafting
operation would become a typed `client.Crafting.CraftAsync(request)` call, while its inventory
rules and persistence remain ordinary C# code in the game project. The call above illustrates the planned API.

## 12. Add typed state subscriptions and replicated views

- Pair command APIs with typed subscriptions so developers do not manually send every state
  update after every operation.
- Declare which views a client may observe. Apply player, game and visibility permissions
  before sending data; never replicate an entire grain's private state by default.
- Define initial snapshots, subsequent updates, ordering, revision tracking, reconnection
  and resynchronization behavior. Generate bindings for supported client languages.
- Provide an optional room/match module with lifecycle hooks and replicated views. Allow a
  grain to own room state without exposing Orleans details to the client.
- Allow shared pure C# rules and data models where useful. Shared deterministic actions and
  client prediction are an optional capability, not a requirement for all backend features.
  Server validation remains authoritative.

## 13. Make local development and debugging feel like engine development

- Provide one development command and IDE launch configuration for the host, local dependencies,
  seeded development data and generated client bindings.
- Support direct breakpoint debugging of compiled game modules from Unity-initiated requests.
  Keep source, generated contracts and runtime diagnostics connected in the tooling.
- Add a development inspector for players, guilds, matches and custom entities. Show declared
  state, recent commands, inputs, results, errors and state changes.
- Trace a client request through gateway dispatch, game code, grain calls and persistence.
  Distinguish execution time from queueing, downstream calls and storage time.
- Support local snapshots and recorded command replay for debugging. Capture the code/config
  version, time, randomness and external results required to reproduce an operation.
- Make code-change restart and state restoration predictable. Document which edits require
  restart or state migration.

## 14. Provide clear state ownership and command failure semantics

- Give feature authors a documented command pattern with request identities, duplicate
  handling and explicit persistence boundaries.
- Keep state that must change atomically in one grain or transactional storage boundary where practical.
  Define coordination and recovery for operations spanning several grains or services.
- Make retry behavior explicit. A timeout does not establish whether a server operation
  completed, so generated clients must not blindly retry mutating operations.
- Expose enough diagnostics to explain a partial failure and its recovery. Preserve tenancy
  and authorization throughout module, command and subscription APIs.

## 15. Support simulation and hosting through composable modules

- Keep persistent account, inventory, progression, guild and lobby services distinct from
  high-frequency simulation execution.
- Provide an optional authoritative match lifecycle and execution interface. Keep frequently
  updated match state together rather than requiring remote grain calls per entity per frame.
- Allow dedicated game servers to integrate with WildCat's identity, matchmaking and persistent
  services. Define hosting adapters so external orchestration can be used without building a
  proprietary fleet manager into the core.
- Keep prediction, rollback and deterministic simulation optional integrations. Existing relay
  multiplayer remains a supported mode.
