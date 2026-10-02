# WildCat project memory

Read [the imported open-source plan](wildcat-open-source-plan.md) for the project's origin,
confirmed scope, and handoff history. [AGENTS.md](../../AGENTS.md) is the maintained source for
working rules, architecture decisions, commands, and roadmap order. The decisions were confirmed
on 2026-09-30; do not reopen them unless the user does.

These files preserve project context for agents that can read this checkout. They are not an
import into account-wide ChatGPT saved memory. Keep durable project facts here, with dates and
supporting files, and keep temporary command output in the conversation.

## Where to look

| Task | Read |
|---|---|
| Grains, hosting, persistence, streams, or tests | [Orleans skill](../../.agents/skills/orleans/SKILL.md), then the relevant reference |
| Understand or remove legacy backend behavior | [Backend inventory, flows, and bugs](../legacy-backend-notes.md) |
| Change client/server framing or messages | [Wire protocol](../wire-protocol.md) |
| Work on the Unity SDK | [SDK API, transport, bugs, and cleanup plan](../unity-sdk-notes.md) |
| Build the admin API or panel | [Legacy panel behavior and expected REST contract](../legacy-admin-panel.md) |
| Port Orleans 3.4.3 code | [Migration checklist](../../.agents/skills/orleans/reference/migration-3x.md) |

## Remote progress discovered on 2026-10-02

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
