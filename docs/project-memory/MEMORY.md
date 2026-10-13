# WildCat project memory

Read [the imported open-source plan](wildcat-open-source-plan.md) for the project's origin,
confirmed scope, and handoff history. [AGENTS.md](../../AGENTS.md) is the maintained source for
working rules, architecture decisions, commands, and roadmap order. The decisions were confirmed
on 2026-09-30; do not reopen them unless the user does.

These files preserve project context for agents that can read this checkout. They are not an
import into account-wide ChatGPT saved memory. Keep durable project facts here, with dates and
supporting files, and keep temporary command output in the conversation.

## Password hashing CI fix on 2026-10-13

Two password hashing concurrency tests failed in GitHub Actions because their measured delay
missed the hashing operation. Player and Developer now receive a `PasswordHashing` service
that preserves the existing thread-pool execution and PBKDF2 format. The test fixture replaces
only the synchronous work with a password-scoped gate, then checks that an interleavable call
finishes before releasing the worker. All waits are bounded and cleanup releases the gate.
The four focused tests passed. Temporarily running verification inline made the developer
test fail as intended; the thread-pool implementation was restored before the full test run.

## Customization roadmap added on 2026-10-13

[The developer experience roadmap](../developer-experience-roadmap.md) continues the completed
migration milestones with planned items 10 through 15: serialize/schema support, generated
multilingual RPC contracts, compiled C# modules and grain extensions, typed state subscriptions,
development inspection and tracing, command recovery, and optional simulation/hosting modules.

The goal is to make backend customization feel as natural as working in a game engine.
Grains, sets of grains and ordinary modules take priority over scripting. Proceed directly
with implementation rather than a separate trial or comparative benchmark phase. Normal
implementation verification remains applicable.

## Documentation review on 2026-10-13

Applied UEWF's `product-docs` writing guidance to the project guides: explain behavior through
its purpose and consequences, preserve examples, and keep verification reports in internal
records. User-facing SDK pages now describe platform limits without recounting the Editor
session. Corrected stale admin status and Unity version text, clarified historical surveys,
and documented the SDK's 100-character key limit separately from the gateway's 128-character
limit. Local Markdown links, heading anchors, ASCII, code fences and protocol documentation
consistency checks passed. No application code changed or runtime tests ran for this edit.

## Public release preparation on 2026-10-13

The requested scope is preparation only: no push or repository visibility change. The README
now includes cloning and local panel startup. [Server deployment](../server-deployment.md)
covers Linux, PostgreSQL, a panel build mounted through `compose.server.yaml`, a host-installed
HTTPS proxy, trusted forwarding, account setup, backups and updates.

Compose 2.40.3 accepted the merged server configuration with synthetic validation values;
the host has one loopback-only published port. Markdown checks passed across 34 files.
No live server deployment or HTTPS certificate issuance was performed. A limited pattern scan
of 1,561 blobs reachable through local Git refs found no recognizable AWS access keys, GitHub
or Slack tokens, Google API keys, private keys or AWS secret assignments. This is not a full
secret audit and does not cover remote refs absent locally. The older Apadana credential
rotation follow-up remains unconfirmed. GitHub CLI is not authenticated, so remote visibility
and current CI status were not checked. Changes remain local and uncommitted.

## Merged and verified on 2026-10-02

The owner authorized merging the completed remote branch, running the tests and checking the
Unity Editor with GPT-6.1 Sol at max reasoning, followed by an independent parent review.
Local commit `a4c4b95` preserved the Codex context migration. Merge commit `15a844b` integrated
`origin/claude/elegant-mendel-jr5n9a` at `f3590a4` into `master`. It retains all branch commits,
the canonical AGENTS.md guide, CLAUDE.md's compatibility import and the newer Orleans refs
under `.agents/skills/orleans`.

The current backend is Orleans 10.3.1 on .NET 10, with a shared netstandard2.1 protocol, a
co-hosted gateway and admin API, PostgreSQL providers, a React panel and the Unity 6 UPM SDK.
All nine original roadmap items were already implemented on the merged branch. The older local-state
sections below describe the context-import history, not pending product work.

Release build passed. Backend tests passed 717 with one existing skipped test, including all
eight PostgreSQL integration tests. SDK tests passed 61 and panel tests passed 38; panel lint
and production build, Compose validation, Docker image build and a Production host startup
against PostgreSQL also passed. A Windows newline parsing failure in the gateway route test
was corrected and independently rerun.

Actual Unity 6000.0.80f1 checks covered package/sample import, compilation, settings/client/About
menus, two Basic sample clients in Play mode against the host, matchmaking, relay positions,
server score submission and leaderboard reads. The sample's WebGL IL2CPP player built with zero
errors. Browser execution, mobile players and visual layout remain unverified. See
[the full verification and review record](verification-2026-10-02.md) for commands, evidence,
the final configuration import and limits. Changes are local; nothing was pushed or published.

## Where to look

| Task | Read |
|---|---|
| Grains, hosting, persistence, streams, or tests | [Orleans skill](../../.agents/skills/orleans/SKILL.md), then the relevant reference |
| Understand or remove legacy backend behavior | [Backend inventory, flows, and bugs](../legacy-backend-notes.md) |
| Change client/server framing or messages | [Wire protocol](../wire-protocol.md) |
| Work on the Unity SDK | [SDK API, transport, bugs, and cleanup plan](../unity-sdk-notes.md) |
| Build the admin API or panel | [Legacy panel behavior and expected REST contract](../legacy-admin-panel.md) |
| Port Orleans 3.4.3 code | [Migration checklist](../../.agents/skills/orleans/reference/migration-3x.md) |

## Remote progress discovered before the merge on 2026-10-02

`git fetch --all --prune` and `git pull --ff-only` completed. The pull reported "Already up to
date" because local `master` and `origin/master` both remain at `703ed09`. There is one remote
branch not merged into `origin/master`: `origin/claude/elegant-mendel-jr5n9a`, tip `f3590a4`.
It is 48 commits ahead with no commits behind master. The branch comparison spans 914 files.

The branch's `CLAUDE.md` marks all nine original roadmap items done. Its history and file
inventory include the Orleans 10/.NET 10 port, feature removals, WildCat rename, security fixes,
PostgreSQL and Docker Compose, gateway and shared protocol refactoring, the admin REST API and
React panel, the Unity 6 UPM package, script hardening, CI, and subsequent review fixes.
This is a branch inspection, not a fresh verification of those implementations. Its handoff
still reports that the Unity engine layer, editor tools, sample and WebGL bridge have not been
verified in a real Unity editor or player.

The branch was not merged or checked out. The local agent-context migration remains
uncommitted. Before further implementation, reconcile that migration with the newer branch's
`CLAUDE.md` and its updated Orleans `hosting.md` and `migration-3x.md` references. The earlier
claim that the project still needed every roadmap item was based only on stale local master.

Pull request and CI status could not be checked: GitHub CLI is not authenticated, and the
unauthenticated GitHub API returned 404. Do not infer that there is no pull request or that CI
passed. [Remote branch](https://github.com/nooparmygames/WildCat/tree/claude/elegant-mendel-jr5n9a).

## Local handoff state before the remote check

The backend and Unity SDK were copied from Apadana on 2026-09-30 with a fresh history and
secrets removed, according to the original handoff. The latest commit at context import was
`703ed09`, "Import the legacy Apadana backend and Unity SDK, add the Orleans 10 skill and
planning docs". The working tree was clean before this migration.

Source inspection still shows `net5.0`, Orleans 3.4.3, legacy namespaces, a separate silo and
WebSocket gateway, and `TestCluster` tests. The copied Unity settings specify 2020.3.18f1.
The old admin panel is absent. Orleans 10, PostgreSQL, the WildCat rename, the new REST API and
React panel, and the Unity 6 UPM package are absent from this local checkout. They are present
on the remote branch described above; do not restart that work from the old local roadmap.

The original Claude handoff reported a successful backend build with 24 warnings. This context
migration did not rerun backend tests, build the game code, or compile Unity. The installed
.NET SDKs were checked: 10.0.401, 9.0.318 and 6.0.419. Other toolchain details in the original
notes are dated reports and need checking when used on another machine.

The original Apadana Git history was reported to contain an AWS access key, a PlayFab developer
secret and an SMTP password. Rotation remains an owner follow-up unless separately confirmed;
do not copy their values or the old history into WildCat.

## Import coverage and provenance

Imported on 2026-10-02 from the following local sources. This records the inspected locations,
not a dependency on those paths existing on another machine.

| Source | Destination or disposition |
|---|---|
| `WildCat/CLAUDE.md` | All project guidance moved into [AGENTS.md](../../AGENTS.md); `CLAUDE.md` now imports that guide |
| `WildCat/.claude/skills/orleans/SKILL.md` | Moved to [.agents/skills/orleans/SKILL.md](../../.agents/skills/orleans/SKILL.md); shortened discovery description, dated version claims, and made the confirmed PostgreSQL choice explicit |
| The skill's `reference/*.md` | All nine files moved without content changes: grains, hosting, messaging, migration-3x, persistence, scheduling, serialization, sources, and testing |
| Claude project memory `MEMORY.md` | Its single plan entry is carried into this index, with ASCII punctuation and the current handoff |
| Claude project memory `wildcat-open-source-plan.md` | [Imported plan](wildcat-open-source-plan.md), retaining the source session and modification time; paths and the cloud-session note adapted for Codex |
| `WildCat/docs/*.md` | All four existing technical surveys retained and linked above |
| `C:/Users/ashka/.claude/CLAUDE.md` | Writing and documentation rules were already present in `C:/Users/ashka/.codex/AGENTS.md`; no global settings changed. Key rules and the user's recap requirement are also in the project guide |

The Claude memory directory was
`C:/Users/ashka/.claude/projects/C--Users-ashka-projects-WildCat/memory/`.
It contained exactly the two memory files listed above. No other project memory Markdown files
under Claude's local project store mentioned WildCat or Apadana. The user messages in Claude
session `424c1010-238b-4923-9511-2a9b99a924e4` were also checked for the original request;
the raw transcript was not copied into the repository.

The legacy source is `C:/Users/ashka/projects/apadana/`: `Apadana 2` contains the backend and
Unity SDK, and `apadana-fronteers-v2` contains the AngularJS panel. Neither source tree contained
additional `CLAUDE.md`, `AGENTS.md`, `SKILL.md`, saved memory, or agent configuration directories
at import time. Those repositories and the original Claude memory files were left unchanged.
The commercial Fuse panel remains a reference for behavior; its files were not imported.

The target layout follows the official [AGENTS.md discovery guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
and [repository skill discovery guidance](https://learn.chatgpt.com/docs/build-skills).

Validation at import passed: the Skill Creator validator accepted the Orleans skill, YAML
frontmatter parsed, all local links and skill reference paths resolved, and imported Markdown
was ASCII with clean whitespace. The nine reference files matched their original Git content.
The confirmed decisions, roadmap, ten Orleans rules, recipes, and reference index were checked
for preservation. Application code and the four existing technical surveys were unchanged.
