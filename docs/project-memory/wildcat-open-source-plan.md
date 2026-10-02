---
name: wildcat-open-source-plan
description: "WildCat's origin, confirmed modernization decisions, credential rotation follow-up, and handoff from Claude to Codex."
metadata:
  node_type: memory
  type: project
  originSessionId: 424c1010-238b-4923-9511-2a9b99a924e4
  source_modified: 2026-09-30T11:58:58.576Z
  imported: 2026-10-02
---

# WildCat open-source plan

WildCat (C:\Users\ashka\projects\WildCat) is where the user is open-sourcing their old Orleans
game backend "Apadana" (legacy source: C:\Users\ashka\projects\apadana, two repos: "Apadana 2"
with backend + Unity SDK, and "apadana-fronteers-v2" the AngularJS panel). On 2026-09-30 the
backend and the Unity SDK Backend folder were copied into WildCat with secrets scrubbed; the
panel was not copied (commercial Fuse theme) and is rewritten from docs/legacy-admin-panel.md.
An Orleans 10 skill now lives at `.agents/skills/orleans`.

Decisions the user confirmed on 2026-09-30 (now maintained in [AGENTS.md](../../AGENTS.md)): product name
WildCat everywhere; Orleans 10 on .NET 10; PostgreSQL via official ADO.NET providers; remove
PlayFab and CafeBazaar entirely; keep tenants (developers/games) but drop billing and invoices;
keep Roslyn script events and harden later; keep the binary WebSocket protocol for now but fix
it (MessagePack rejected as too large a payload; a later format change is expected); admin =
REST minimal API + React/TypeScript SPA; Unity SDK targets Unity 6 LTS only as a UPM package;
keep email login/activation/reset with configurable SMTP; no legacy data to migrate; Docker
Compose first; port the old tests as a safety net then replace them.

The user wants to understand, clean up and release the project. The original handoff planned
to continue in a cloud-based Claude session once local setup was committed and pushed. That is
historical context. On 2026-10-02 the user requested this project's memories, instructions and
skills be moved into formats usable by ChatGPT/Codex in the WildCat checkout.

Treat [AGENTS.md](../../AGENTS.md) as the plan; do not reopen the decisions above unless
the user does. Remind the user, if it comes up, that the original apadana repo's git history
still holds an AWS key, a PlayFab secret and an SMTP password that need rotating.

The migration imported saved context; it did not carry out the product roadmap. The current
code and the verification notes in [MEMORY.md](MEMORY.md) determine what has actually been done.
