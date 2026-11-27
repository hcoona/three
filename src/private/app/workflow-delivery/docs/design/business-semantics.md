# Workflow Delivery v3: business decisions

A contributor asks whether a particular change meets its quality requirements.
A release operator asks what may be published to one destination and what the
outcome means. These are different questions, even when they use the same
source revision and native build definitions.

This design candidate starts from the [five user journeys](../requirements.md#mission).
The [requirements](../requirements.md) own product behavior. The models below
are an independent specification for comparing designs, not a description of
the current code, a new support commitment or permission to publish.

## Three decisions

| Decision                                           | What establishes the answer                                                                                                                                                                                 | What does not                                                                           |
| -------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| Does this CI candidate pass?                       | Known scope and satisfied results for every required check and variant of that candidate.                                                                                                                   | All the checks that happened to run passed.                                             |
| May this Release request perform this write now?   | Independently qualified originals, a live request eligible for its channel, native authorization, and a still-permitted operation on the bound destination.                                                 | A green CI result, approval alone, or an earlier observation that a file was missing.   |
| What may the operator conclude about this Release? | The destination's completion evidence for the whole request and the required evidence about this execution. Report satisfaction, attempted mutation, command outcome and any unresolved effects separately. | An upload exit code, or a later observation offered as proof of who created the object. |

Git and the native ecosystem tools supply revision, dependency, version and
build facts. Actions owns execution and immutable artifact transport;
Environments and destination permissions own authorization. The destination
owns its actual state and what it lets a caller observe. Workflow Delivery owns
the choice and binding of inputs, the operations it requests and the conclusions
it draws. It cannot use unobservable remote state to make a decision.
Successful transport tells the receiver that it got the selected artifact,
not that Workflow Delivery selected the right artifact for this request.

## The five journeys

| Journey                                           | An outcome the model must allow                                                                                    | A distinction that changes the decision                                                                                                                |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Selective or explicitly full CI                   | Pass a known empty project scope, or pass when the exact candidate satisfies its complete plan.                    | An unresolved comparison is not an empty scope. Deleting an input or edge does not erase a surviving consumer.                                         |
| Development dry run                               | Qualify the selected development revision and describe hypothetical operations without a live-eligible ref.        | A browser-store simulation cannot acquire even Chrome's observation credential or claim it saw the store.                                              |
| Buddy                                             | Publish independently qualified originals to an admitted preview coordinate from an allowed development ref.       | The same product version does not make Buddy outputs, approval or completion transferable to Official.                                                 |
| Official                                          | Verify an already exact ordinary destination without write access, or enter the native gate for still-needed work. | AMO has to enter the gate to look; Edge has no version lookup even there. A grant alone permits neither a different file nor an ineligible ref.        |
| Existing, partial, conflicting or uncertain state | Keep exact objects; in a fresh request complete a supported, verified subset.                                      | A failed command, a missing publisher record and a proven non-start have different reports. A later success cannot change the earlier terminal result. |

## CI: determine the obligation before judging the result

For [selective CI](../requirements.md#ci-qualification), use the comparison
basis **and** the tested candidate to find affected projects, surviving
consumers and their required checks and variants. Removing a dependency in the
candidate does not erase the former consumer from the impact analysis. A
native workspace-wide check can be necessary; explain why it applies. Explicit
full validation is a separate request, not a fallback for unknown scope.

Consider two changes for which no project check ran. For one, complete facts
establish that the changed path has no applicable project check. For the other,
native evaluation failed before it could identify the consumers. The first has
no project obligation for that path; the second cannot pass planning. Likewise,
once the scope is known, a passing result for the same tree against a
different comparison basis or plan cannot fill a missing required result.
At a multi-job join, validate each planned producer's result set and each
required check exactly once; two identical passes are still two receipts.
An advisory failure remains visible without becoming a required failure.
CI has no publication capability, regardless of its verdict.

## Release: bind the request, limit the write, qualify the conclusion

A request names one release unit, immutable source revision, channel,
destination and live or dry-run mode. Release builds and qualifies its
**complete** set of originals, including clean native consumption, without
publication capability. A dry run can exercise a development revision without
writing or claiming live satisfaction. Buddy may publish from an allowed
development ref; Official requires an eligible reviewed ref. These are channel
choices, not successive stages: byte-identical Buddy output is not Official
qualification or authority.

For ordinary destinations, observed absence can permit creation without
overwrite; a verified exact subset can permit missing-file completion where the
destination supports it. A conflict, an unknown required observation or a
missing non-overwrite guarantee permits no such write. The isolated publisher
acts only on the bound originals; it does not run product code.

Fresh observation may shrink the set of intended writes, not substitute one
write for another. If another writer supplied the last missing file while
approval was pending, verify the complete set rather than uploading it again.
If only the wheel was missing before the gate and only the sdist is missing
now, stop: one write is still needed, but it is not the write presented for
authorization. Serializing this repository's releases by registry and package
does not lock out other writers or make an observation and a write atomic.
Invoke the native publisher once per still-needed operation. After failure,
ambiguity or interruption, make no further destination mutations in that
execution; bounded verification reads may still establish the outcome.

The [destination contract](../requirements.md#release-delivery) answers two
independent questions:

- **What can this actor observe before writing?** For a live Firefox AMO
  request, only the gated publisher can read the version; it can discover that
  no write is needed. For Edge Add-ons, even the publisher cannot read the
  version. The explicit first-submission exception permits an operation, not
  an assertion of absence. After an unknown outcome the owner must establish
  in Partner Center that the version was not received before a fresh live
  request. A browser-store dry run acquires no store credential.
- **What counts as completion?** Ordinary destinations require authoritative
  observation of each intended coordinate and its required metadata, plus
  downloaded bytes matching every qualified original. For nuget.org, observing
  the exact package ID and normalized version as available suffices; this says
  nothing about byte equality. The three browser stores
  require the store to report the submitted version received, in review or
  published; receipt is not a claim that end users can install it. Two commits
  can share a product version, so version availability cannot replace byte
  equality at a destination that requires the latter.

## When equal observations require different answers

**Same final bytes, different conclusions.** In one request the destination
already served all qualified originals. No publisher was needed; final
verification can establish satisfaction without a write. In another, a write
was initially necessary, the required publisher result is missing, nothing
establishes whether the publisher started, and final verification finds the
same bytes. Report the match, but do not claim that this execution satisfied
the request or that it made no attempt. If the verifier instead downloads
**conflicting bytes**, it knows the target is unsatisfied even though the
attempt and its outcome remain unknown. A _recorded_ command failure is
different: if bounded verification establishes the complete target while the
request is active and all required evidence is present, the request can be
satisfied. Keep the command failure; do not claim this execution created the
files.

**Same index listing, different next actions.** A Python request qualifies a
wheel and sdist. The public index does not yet list the wheel. Under the
[Python index contract](../integration/python.md#concrete-python-release-integration),
an acknowledged wheel upload permits the still-intended sdist upload without
waiting for immediate readback. An ambiguous wheel response does not: stop
application writes, even if a later read finds the wheel. The native client's
bounded transport retries are part of one invocation, not permission to resend.
A [fresh manual request](../requirements.md#observation-retry-and-recovery)
can reproduce and qualify both originals from the original revision and inputs,
observe an exact wheel and missing sdist, and obtain current authorization to
upload only the sdist. Its success does not revise the first request's terminal
result. If the rebuilt sdist is known to differ, the request cannot recover
the original release, even though no remote sdist exists to expose the
difference. Complete-set reproducibility is a premise of ordinary recovery,
not a requirement to retrieve an earlier run or build twice.

## Fresh recovery: can a redundant invocation always be avoided?

No. Consider one wheel: all other originals already match. A first request
invoked the native publisher and lost the response. A fresh manual request
uses the original revision and inputs, qualifies its own originals and has
current authorization for that wheel. Its index reads succeed, but do not
list the wheel. There is no conflicting metadata or other blocking condition.

| What actually happened to the first upload                | If the fresh request invokes native creation                                               | If it defers on the same observation      |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------ | ----------------------------------------- |
| The index never committed the wheel.                      | Gives this request a way to supply the missing wheel.                                      | The file remains missing in this request. |
| The index committed the wheel, but it is not visible yet. | Makes a redundant invocation. Native active-file non-overwrite protects the existing file. | Avoids the redundant invocation.          |

The fresh publisher knows what its index read returned, not which row it is
in. Another read can distinguish the rows **if it finds an exact match**. A
second unlisted read still cannot establish non-commit without a guaranteed
visibility deadline. At the end of the read window the request either invokes
creation or it does not. Handing the decision back to the operator defers the
write; it supplies no new fact about the file.

**Design choice:** allow the separately authorized, non-overwriting native
create on a valid unlisted observation. This preserves a recovery path in the
first row without promising never to invoke the publisher in the second.
This is neither an automatic retry of the original request nor evidence that
the fresh request created an object. A failed lookup is not a valid unlisted
observation, and a destination without the native guarantee cannot use this
rule. Completion still requires the destination evidence described above.

The [TLA+ comparison](models/FreshRecovery.tla) represents the two histories
together so the decision can use only observations available to the operator.
It lets the index catch up before, between or after two successful reads. In
this example no one deletes or changes the file; once visible, it matches
the qualified original. These are bounds on the example, not extra platform
guarantees. The two reads are not a proposed polling policy.

## What the finite models check

Use a decision table where order cannot change the answer, and a sequence of
observations and operations where it can. The models share no production
predicates or test fixtures. The Release models share [business rules](models/ReleaseRules.tla)
so they agree on what it means to enter a publisher or claim satisfaction.

| Model and configuration                                                                                          | Finite boundary and checked behavior                                                                                                                                                                                                                                                                                                                 | Distinct states |
| ---------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------: |
| [CandidateQualification](models/CandidateQualification.tla) ([configuration](models/CandidateQualification.cfg)) | One graph pair with old and new consumers, a removed input, a two-variant library, a prerequisite and an unrelated project. Check every direct-impact set in both modes; vary one receipt across wrong tree, comparison or plan, duplicates, missing and non-passing results, including advisory work.                                               |          26,880 |
| [ReleaseDecisions](models/ReleaseDecisions.tla) ([configuration](models/ReleaseDecisions.cfg))                   | Two modes, two channels and five destination profiles. Within each combination check changed request fields, control, preparation, native access, reader, evidence expiry and completion predicates. Include positive routes as well as refusals.                                                                                                    |              20 |
| [PublicationWindow](models/PublicationWindow.tla) ([Python](models/PublicationWindow.cfg))                       | Wheel then sdist. Vary the initial, gated and final reads independently; allow denial, external changes, acknowledged, failed or ambiguous commands, and loss of the publisher result. Check operation identity, order, stopping, progress under favorable inputs and truthful conclusions. Compose two requests at one ambiguous-wheel boundary.    |           4,846 |
| [PublicationWindow](models/PublicationWindow.tla) ([GitHub Release assets](models/ReleaseAssets.cfg))            | Create the release if missing, then upload missing installer and portable assets. A missing container has no existing assets. An exact container includes the tag, revision and channel metadata; an exact asset includes its uploaded state, size and downloaded bytes. Check all three operations against the reviewed set, not just file uploads. |          25,317 |
| [FreshRecovery](models/FreshRecovery.tla) ([configuration](models/FreshRecovery.cfg))                            | One file, two possible commit histories, two successful reads. Check when an observation-based decision can both offer recovery and avoid a redundant invocation.                                                                                                                                                                                    |               8 |

These are separate local TLC 2.19 checks with the repository's locked TLA+
tools (1.7.4). Each normal configuration exhausted its reachable states with
no invariant violation. The first two enumerate decisions; their state counts
are not counts of workflow executions. In the temporal models, the additional
writer need not be scheduled: its observable effects can appear in the next
read. A read is not a lock. Progress means a suitable continuation is
**permitted**, not that Actions, a registry or an operator will take it.

Four separate configurations deliberately produce traces. The first three
refute tempting shortcuts. The last asks TLC to refute the claim that a useful
recovery is **unreachable**:

| Configuration                                                                           | Module                   | Expected violated invariant and what the trace establishes                                                                                                                                                                                                            |
| --------------------------------------------------------------------------------------- | ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [CandidateQualificationCounterexample](models/CandidateQualificationCounterexample.cfg) | `CandidateQualification` | `GreenReceiptsSuffice`: all received results belong to the tested candidate and passed; a required library variant is missing.                                                                                                                                        |
| [OperationCountCounterexample](models/OperationCountCounterexample.cfg)                 | `PublicationWindow`      | `CountBoundsAuthority`: only the wheel was missing before the gate; only the sdist is missing afterwards. The count still fits. The request issues neither operation.                                                                                                 |
| [FreshRecoveryCounterexample](models/FreshRecoveryCounterexample.cfg)                   | `FreshRecovery`          | `AlwaysPossibleToDoBoth`: two unlisted reads do not distinguish a lost upload from a committed upload that is not yet visible.                                                                                                                                        |
| [SubsetRecoveryWitness](models/SubsetRecoveryWitness.cfg)                               | `PublicationWindow`      | `NoSuccessfulSubsetRecovery`: the wheel response is ambiguous, the request stops, and verification sees neither file. A new manual request sees the exact wheel, obtains a new grant, uploads only the sdist and verifies both files. The first report stays unknown. |

To reproduce any row, use the [repository TLA+ toolchain](../../../../../../docs/engineering/workspaces.md#tla)
from the repository root. Supply the configuration and module from the same
row. For example:

```sh
mise run tla:tlc -- -workers 1 -config src/private/app/workflow-delivery/docs/design/models/ReleaseAssets.cfg src/private/app/workflow-delivery/docs/design/models/PublicationWindow.tla
```

Normal runs should exit successfully. For a deliberate trace, look for its
**named invariant** and TLC exit code 12; a parse or configuration error is
not the expected result. These checks model decisions, not native services.

## Where the model relies on native contracts

- **CI:** Git and native evaluators supply the comparison, candidate and
  resolved input/consumer facts. The [selection contract](repository-and-selection.md#selection-algorithm)
  owns path-to-consumer reasons, legitimate workspace-wide work and supported
  scope. At collection the model checks individual receipts against their
  original plan, assuming the [multi-job boundary](ci.md#result-collection)
  has validated the planned producer sets. It does not prove every parser,
  arbitrary graph, project policy or runner correct.
- **Release authority:** The request binds immutable source/control, frozen
  inputs, native coordinate, channel, destination and complete originals.
  A fresh dispatch must build and qualify its own set. Actions supplies the
  actual isolated contexts, artifact identity, native concurrency and current
  grant; a Boolean grant in the model is not a replayable approval. The
  `originals` profile means an already admitted channel-specific route, not
  support for every unit on both channels. The exception profiles select their
  native output sets: one `.nupkg`, one Chrome or Edge ZIP, or the Firefox
  package and its sources. A version receipt cannot qualify an extra output.
- **Destination facts:** [Python](../integration/python.md#concrete-python-release-integration)
  supplies active-file non-overwrite and eventual indexing;
  [GitHub Releases](../integration/github-release.md#github-release-destination-completion)
  supplies tag, release and asset observations. Changed rebuilt bytes conflict
  with nonmatching served bytes. An acknowledged GitHub asset absent from a
  subsequent listing is unverified, not conclusively absent. The model assumes
  neither immediate visibility, reproducible bytes for every product, nor
  permanent coordinate reservation.
  [nuget.org](../requirements.md#release-delivery) and the
  [three stores](../integration/browser-store.md#destinations-and-completion)
  have different positive evidence. The store API protocols, Edge owner check,
  native client retries and the effective reach of credentials are not
  simulated or certified by TLC.
- **Reporting and recovery:** Unresolved calls name possible effects, not
  authorship. Missing required transfers or expired evidence cannot be filled
  from a cache or a green job. The saved first report in the two-request
  example is for checking that it never changes, not a required release
  database. Unsupported partial state, a conflict or a necessary administrative
  correction goes back to the owner; no rollback or repair API is assumed.

## What the abstraction can omit

For the same subject, two histories may share a business state when no allowed
continuation distinguishes their permitted actions, required report or recovery
options. Always refusing a write or a success claim would lose legitimate
subset recovery or no-write completion. A command exit code cannot distinguish
a complete release from an acknowledged upload that has yet to become visible.
By contrast, splitting an unprivileged build across runners need not change
the business decision if the subject,
qualification, permissions and transferred originals remain the same. No
arrangement of jobs or permanent ledger can reconstruct a remote effect that
neither the publisher nor the destination can establish.

A write outside the intended set or after an ambiguous result in the same
request is forbidden. A _redundant invocation_ on a valid unlisted read in a
fresh request need not be forbidden: the destination protects the active file
and the new request still has to establish its own result. Safe recovery is a
conditional capability, not a promise that every request eventually publishes.

To compare an implementation, give it the same candidate or release request,
native facts and sequence of observable outcomes. Compare its selected
obligations, permitted operations and operator report, including paths that
**should** succeed. A different job graph is immaterial; passing a missing
variant, substituting a write, suppressing a command failure or blocking a
legal recovery is not. If a difference rests on a native assumption, resolve
it against the owning contract rather than changing the model to match the
code. This is a criterion for a useful abstraction, not a proof that it is
globally minimal or that the implementation already conforms.
