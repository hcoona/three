# LiteAgensh

LiteAgensh is a C# single-machine adaptation of
[microsoft/Agensh](https://github.com/microsoft/Agensh). It keeps the upstream
peer-cooperation approach while replacing hosted collaboration infrastructure
with SQLite, local Git worktrees and existing native agents.

**Current state:** project establishment and design records only. There is no
application, installed command, successful ACP handshake or runtime acceptance
claim yet. Examples of operations in the records describe intended behavior,
not commands available today.

## User and primary journey

The user is a developer who wants several coding agents to cooperate on one
local repository without deploying a Git server, chat server, database server
or container stack. The developer supplies a goal, a base revision, workers,
checks and a finite run budget; agents select work, communicate findings and
submit changes; the developer inspects a checked local delivery branch and can
intervene, pause or resume without losing partial work.

This context motivates the [requirements](docs/requirements.md). Their
acceptance evidence belongs in the [validation basis](docs/validation.md), not
in this overview.

## Records and authority

| Reader question                                                     | Authority                                                                     |
| ------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| What must the local product do, and what is unsupported?            | [Requirements](docs/requirements.md)                                          |
| How do C#, SQLite, native processes, MCP and Git fit together?      | [Architecture and integration boundaries](docs/architecture.md)               |
| What replaces each upstream responsibility and collaboration tool?  | [Complete migration map and source evidence](docs/upstream-migration.md)      |
| What evidence permits implementation and an eventual support claim? | [Validation basis](docs/validation.md)                                        |
| Where is upstream attribution retained?                             | [Third-party notice](THIRD-PARTY-NOTICES.txt)                                 |
| What work is currently authorized?                                  | Repository [Delivery Wave](../../../../docs/delivery-wave.md)                 |
| Where are progress, reviews and delivery dependencies?              | [Issue #1526](https://github.com/hcoona/three/issues/1526) and its linked PRs |

The README owns purpose, user context, current implementation status and routing.
Requirements own behavior; architecture owns realization; the migration map owns
source findings and treatment rationale; validation owns evidence obligations.
None of these grants work or replaces the accepted repository policies.

## Record admission and maintenance

The repository owner maintains these records with project authors. Implementers
and independent reviewers consume requirements before selecting behavior,
architecture before changing dependency/recovery boundaries, the migration map
before translating upstream mechanisms, and validation before claiming a journey
works. Missing any of those would respectively permit scope drift, an
incompatible platform substitution, an untracked semantic omission or an
unsupported acceptance claim. They need distinct carriers because their
questions and change/review points differ; this portal cannot serve as all four
authorities.

The source baseline and license stay with the map and notice. Review execution,
checker reports, mutable-source retrievals, findings and progress stay in Issues
and PRs. When implementation changes status, update this README rather than
creating a second progress ledger.
