# Sheet Music Recognition

This project defines how photographed pages of printed piano music become
editable notation. A recognizer reads one printed grand-staff system at a time
into text, and deterministic tools validate, assemble and convert that text to
MusicXML. The records here define the text representation and the evidence its
tools must meet.

**Current state:** experimental; W1 formal measurement is preparing `formal-5`
after `formal-4` stopped on `p0098/rx` (`#1488`). A Python package parses
Text Score 2 documents, validates their grammar and semantics, reports each
error with its rule class and location, lists review markers, writes canonical
text, derives the carry-out state of a system document and assembles system
documents into one score. A forward converter turns a valid assembled score
without review markers into MusicXML 4.0, and a reverse converter turns a
MusicXML piano part on two staves back into a valid assembled score. A
round-trip comparison instrument and execution harness run the
[comparison protocol](docs/comparison-protocol.md) and the auxiliary protocols
in the [validation basis](docs/requirements.md#validation-basis). Formal
attempt `formal-4` completed `preflight`, `select` (`200` PDMX + `50` Lieder;
[Issue #1488](https://github.com/hcoona/three/issues/1488#issuecomment-7070335207))
and `pdmx-x` (`200 / 200` files) on `main` (`5956790d`) before stopping on
`p0098/rx` (`_NumberConflictError`;
[Issue #1488](https://github.com/hcoona/three/issues/1488#issuecomment-7071662321)).
The [requirements](docs/requirements.md), [Text Score 2 format](docs/text-format.md),
[comparison protocol](docs/comparison-protocol.md), converters, comparator and
harness incorporate the Phase 1 and post-`formal-4` owner dispositions
([Issue #1488](https://github.com/hcoona/three/issues/1488#issuecomment-7071944465),
[PR #1704](https://github.com/hcoona/three/pull/1704),
[PR #1705](https://github.com/hcoona/three/pull/1705),
[PR #1711](https://github.com/hcoona/three/pull/1711)) ahead of `formal-5`
execution (see [Agent handoff](docs/agent-handoff.md)).
No formal acceptance run has completed end to end yet, and there is no
recognizer, model, packaging or release.

## Usage

From the repository root:

```shell
uv run --package sheet-music-omr sheet-music-omr validate FILE...
uv run --package sheet-music-omr sheet-music-omr canonicalize FILE
uv run --package sheet-music-omr sheet-music-omr assemble FILE... [-o OUTPUT]
uv run --package sheet-music-omr sheet-music-omr to-musicxml FILE [-o OUT]
uv run --package sheet-music-omr sheet-music-omr from-musicxml FILE [-o OUT]
uv run --package sheet-music-omr --group dev pytest src/private/app/sheet-music-omr/tests
```

`validate` prints one `path:line:column: class/rule: message` line for each
error and one `review:` line for each review marker, and exits with 1 when a
file is invalid. `canonicalize` writes the canonical text of one valid file.
`assemble` writes the assembled score to standard output or `OUTPUT`, and
reports errors and review items on standard error. It exits with 0 on
success, 1 on failure and 3 when review markers or extended trills need a
person.
`to-musicxml` writes the MusicXML 4.0 of one valid assembled score to standard
output or to `OUT`. It exits with 1 and writes no MusicXML when the score is
invalid, holds review markers or holds content that the converter cannot
convert faithfully.
`from-musicxml` reads a `.musicxml` or compressed `.mxl` file and writes the
canonical text of the assembled score to standard output or to `OUT`. It exits
with 1 and writes no score when the file is unreadable, is not one part on two
staves or holds content that the converter cannot convert faithfully.

The W1 round-trip measurement harness runs the
[comparison protocol](docs/comparison-protocol.md) on Windows or from WSL2
using the Windows host's MuseScore Studio 4 installation. Its run and download
directories must be outside every local repository worktree:

```powershell
uv run --package sheet-music-omr --group dev `
  python -m sheet_music_omr.harness COMMAND --run-dir RUN --downloads DOWNLOADS [options]
```

`preflight` and `pilot` invoke `pytest` in the active interpreter, so include
`--group dev` when running the harness.
The commands, in order, are `preflight` (with `--prior-total` and `--basis`
when initializing a fresh shared `DOWNLOADS/download-ledger.json`), `download`,
`select`, `run` (with `--gold-status`, `--gold-cache` and `--jobs`), `report`,
`replay-req012` (with `--manifest` for the frozen `OMR-REQ-012` replay) and
`tokenizer-counts` (for the W1 tokenizer-counts protocol). `preflight`,
`select`, `run` and `pilot` accept optional
`--musescore` and `--settings` path overrides. `view --id ID [--measure N]`
shows one file's differences by measure, and `pilot [--fixtures FILE...]` runs
every phase on repository fixtures. A stop condition is recorded in
`RUN/run.json` and exits with 2.

Each worker call has one 300-second bound, which includes starting a fresh
worker. Some calls hold more than one of the protocol's steps: the content
pass with the supplementary checks, the text-space check's two reverse
conversions, and each gold piece's whole system-assembly test (the baseline's
content and layout passes run in separate 300-second worker calls). A timeout
of one of these calls is counted as a failure under the protocol's failure
rule, but the bound is stricter than the protocol's per-step bound, so the
results carrier lists any such timeout for the owner to review before its
figures are used.

## User and primary journey

The user is the repository owner, who turns photographed pages of piano scores
into editable notation and reviews the result against the page. The intended
journey is:

1. A page photograph is divided into grand-staff systems.
2. A recognizer writes each system as a system document of the text format.
3. Tools validate the documents, assemble them into one score and convert it to
   MusicXML for editing.
4. The owner reviews the score against the page and corrects it.

The records here cover the text format and the requirements its tools must
meet, which is steps 2 and 3 without the recognizer. The recognizer and its
training are later workstreams. Their direction and research context are tracked
in [Issue #686](https://github.com/hcoona/three/issues/686), and the text
representation work in [Issue #1488](https://github.com/hcoona/three/issues/1488).

Text Score 2 is derived from the owner's as-printed text convention but is
specified anew, with a versioned normative grammar. It is the pilot baseline
representation. It is not a finding that it outperforms ABC, MusicXML or
another representation. That comparison needs model evidence that this project
does not produce.

## Records and authority

| Reader question                                                        | Authority                                                     |
| ---------------------------------------------------------------------- | ------------------------------------------------------------- |
| What must the format, its tools and their acceptance evidence achieve? | [Requirements](docs/requirements.md)                          |
| What exactly is a valid system document or assembled score?            | [Text Score 2 format](docs/text-format.md)                    |
| How is a round trip compared and scored?                               | [Comparison protocol](docs/comparison-protocol.md)            |
| How do I route a change or operate W1 formal runs safely?              | [Agent handoff](docs/agent-handoff.md)                        |
| What work is currently authorized?                                     | Repository [Delivery Wave](../../../../docs/delivery-wave.md) |
| Where are progress, reviews and findings?                              | The two Issues above and their linked pull requests           |

The README owns purpose, user context, current status and routing. The
requirements own the obligations of the format, its tools and their acceptance
evidence. The format owns the grammar and semantics. None of these grants work
or replaces the accepted repository policies. The private inputs and accepted
transcriptions that acceptance uses stay private. The requirements state that
boundary in OMR-REQ-015.

## Record admission and maintenance

The project authors produce these records, and the repository owner maintains
them with the authors. Converter, validator and harness implementers and their
independent reviewers consume the requirements before choosing behavior or an
acceptance measure. Without them each implementation would invent its own
coverage and its own meaning of "no difference", and a result could be judged
against the tool that produced it. The requirements need their own carrier
because the Delivery Wave authorizes work only for a bounded time and is
deleted when accepted, and Issues carry progress rather than durable
obligations.

The same implementers and reviewers consume the format as the single
definition of a valid document. Without it the validator, the converters and any
recognizer training data would disagree about validity, and round-trip results
would not be comparable. The format needs a separate carrier because it changes
through versioned grammar and semantic decisions that a reviewer checks against
the grammar, while the requirements change with acceptance obligations.
Merging them would bury the obligations in a reference text.

The round-trip harness implementers, their independent reviewers and W1
acceptance consume the comparison protocol, which fixes the comparison
dependencies, MuseScore 4 provenance bounds, inputs, element measure and
category assignment of every round-trip result. Without it
the harness would choose its own measure after seeing results. It needs its
own carrier because an accepted protocol must be fixed before execution and
changes only under research-evidence review, independently of format and
requirement changes. Issues carry its results, not the protocol.

Replacement coding agents and contributors consume the
[agent handoff](docs/agent-handoff.md) before modifying project files or
operating the harness across sessions. It owns change routing, prior formal-run
and external-state/ledger invariants, and ordered execution gates. Without it a
fresh session must reconstruct operational hazards and stop boundaries from
dozens of Issue comments and risks repeating environment stops, disturbing
preserved external runs, or bypassing required pre-conversion gates. It needs a
distinct carrier so this README stays a concise project overview while normative
requirements, grammar and comparison rules remain separate from operational
handoff routing.

Review execution, findings and chronological progress stay in Issues and pull
requests. When implementation or execution status changes, update this README
and the handoff rather than creating a progress ledger.
