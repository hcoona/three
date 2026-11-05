# Sheet Music Recognition

This project defines how photographed pages of printed piano music become
editable notation. A recognizer reads one printed grand-staff system at a time
into text, and deterministic tools validate, assemble and convert that text to
MusicXML. The records here define the text representation and the evidence its
tools must meet.

**Current state:** experimental. A standard-library Python package parses
Text Score 2 documents, reports lexical and grammar errors with their rule class
and location, lists review markers and writes canonical text. There is no
semantic validator, assembler, converter, recognizer or model yet, so a
document that this package accepts can still be invalid. No packaging or
release is selected.

## Usage

From the repository root:

```shell
uv run sheet-music-omr validate FILE...
uv run sheet-music-omr canonicalize FILE
uv run pytest src/private/app/sheet-music-omr/tests
```

`validate` prints one `path:line:column: class/rule: message` line for each
error and one `review:` line for each review marker, and exits with 1 when a
file is invalid. `canonicalize` writes the canonical text of one valid file.

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

Review execution, findings and progress stay in Issues and pull requests. When
implementation changes status, update this README rather than creating a second
progress ledger.
