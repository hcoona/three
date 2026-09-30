# Hosted TestPyPI Partial-Publication Recovery Evidence

## Conclusion and boundary

The real wheel-present/sdist-missing recovery of `hcoona-release-smoke-python`
`0.1.0b39` completed on 2026-09-29 and passed independent audit. A fresh normal
Attempt independently rebuilt and qualified both archives byte-identically,
retained the exact existing wheel and uploaded only the missing sdist. Fresh
native downloads and separate clean wheel/sdist consumers passed; the sdist
consumer rebuilt an identical wheel. The original seed remains failed.

The [success carrier](https://github.com/hcoona/three/issues/843#issuecomment-5883838961)
and [independent public-packet/replay review](https://github.com/hcoona/three/issues/843#issuecomment-5883881292)
retain the joined evidence. The V3 maintainer maintains this validation record;
reviewers and future maintainers use it to interpret the result and historical
artifacts. The [LLD](../hcoona-release-smoke-python-lld.md#temporary-hosted-recovery-proof)
owns retirement and ordinary runtime behavior.

The campaign stopped at its first independently audited success. Temporary
interruption controls and operators are retired; the completed Wave entry is
removed. This record grants no operation. The [handoff](../agent-handoff.md#starting-a-new-session)
routes completed production PyPI evidence and current operation limits.
The inverse sdist-present/wheel-missing path has local integration evidence
only. This intentionally interrupted client operation proves neither a native
TestPyPI outage nor generic reproducibility across changed build inputs.

## Exact subject and observations

Both runs used repository `hcoona/three`, actor `hcoona`, the stable workflow
`workflow-delivery-v3-python-smoke.yml` and Environment
`workflow-delivery-v3-python-testpypi`. Publisher registrations, Governance,
transport profile and host trust were unchanged.

- Source/tooling: `b7b29e5f43fae031c0bee3b2f6aad89475a78ec4`.
- Git tree: `a49f71089d35c8176b6691300e4d43771e542ea8`.
- Frozen 154-input map SHA-256:
  `823894f552c35f90c21e81983927ee52c70cdd8c4fd014c4b692a99b875e74a2`.
- Executed protocol SHA-256:
  `514450f9c8ac30a1f5e27111794e02234e302751ce671ecd77cc98b85a77b574`.

| Attempt          | Original run                                                            | Observed result                                                                                                                                        |
| ---------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Scenario 03 / 01 | [36520490650](https://github.com/hcoona/three/actions/runs/36520490650) | Disclosed stop after successful wheel upload/readback; sdist not attempted; `publication-failed`, `possibly-mutated: true`                             |
| Scenario 03 / 02 | [36521837680](https://github.com/hcoona/three/actions/runs/36521837680) | Fresh builds, Qualification, artifacts and Approval; wheel `already-present` without POST; only sdist uploaded; `published`, `possibly-mutated: false` |

The success Outcome's `possibly-mutated: false` is its terminal contract
classification, not a claim that no upload occurred. Recovery artifact IDs
are disjoint from the seed's; original evidence is compared, never adopted as
current-run authority. Both independently built pairs have these exact bytes:

| Archive                                                 | Bytes | SHA-256                                                            |
| ------------------------------------------------------- | ----: | ------------------------------------------------------------------ |
| `hcoona_release_smoke_python-0.1.0b39-py3-none-any.whl` |  4088 | `0a02fe7784e3f0d6e240ea3e3495c2cf8bf5613adc48d0f897c082890dce8677` |
| `hcoona_release_smoke_python-0.1.0b39.tar.gz`           |  2929 | `bc484c617daab587f9ca94637dc856da1278716eb1a5033211ff7a148d4ee6d7` |

The independent seed audit used two real GETs (index and wheel) and established
exact wheel-only state. Recovery used three (index, wheel and sdist); one
pending wait respected the 30-second pacing without resending successful
requests. The final native audit completed at `2026-09-29T04:44:29.626862+00:00`.
Both separate clean consumers passed on those downloaded files.

| Retained object               | SHA-256                                                            |
| ----------------------------- | ------------------------------------------------------------------ |
| Seed original Outcome         | `e5ae470fa951d9321a9c34dcf2ca0f1de571bcacbf959bf5694ab216394978b1` |
| Recovery original Outcome     | `d65fdf3212f9bab5cfc0b7d361ac14656d082d8ffa733d1858fde1bc6a5e75d7` |
| Seed closure gate             | `171226308d8aa4892aaf308d8f358d362395a2f666a8fee91f36d31f0168fd90` |
| Recovery audited-success gate | `00bff0af193bef00beaf20f088d851c356ae1845642a0078ec688013a4c9baff` |
| Final stopped campaign        | `73f82cc06787124ea45e4bfd874e5e3b088900fd64fbfbc0150dbc2be31ad305` |

Final campaign accounting retains three scenarios, four spent dispatch
reservations and three spent file-upload reservations. The successful b39
scenario consumed two dispatches and two uploads. Unused ceilings create no
remaining authority after success.

## Durable selected packet and offline replay

The selected public packet contains 114 paths and 104 unique content blobs.
It uses `sha256-file-map-v1`: `files` maps relative paths to content hashes;
`blobs` maps hashes to UTF-8 or base64-encoded content. The 395754-byte gzip has
SHA-256 `cdfd6bf8ae4a7d8a1e829998c7680dcc62d234ff9215621f0702943c7ea0b444`;
the decompressed JSON has SHA-256
`71c8f30eecd9ef67a26a21269b666f5db9db9e36cb38a35b7b72f6eebd250bf3`.

Retrieve the numbered packet parts in order:

1. [Part 1](https://github.com/hcoona/three/issues/843#issuecomment-5883877360)
2. [Part 2](https://github.com/hcoona/three/issues/843#issuecomment-5883877532)
3. [Part 3](https://github.com/hcoona/three/issues/843#issuecomment-5883877739)
4. [Part 4](https://github.com/hcoona/three/issues/843#issuecomment-5883877884)
5. [Part 5](https://github.com/hcoona/three/issues/843#issuecomment-5883878068)
6. [Part 6](https://github.com/hcoona/three/issues/843#issuecomment-5883878471)
7. [Part 7](https://github.com/hcoona/three/issues/843#issuecomment-5883878752)
8. [Part 8](https://github.com/hcoona/three/issues/843#issuecomment-5883878969)
9. [Part 9](https://github.com/hcoona/three/issues/843#issuecomment-5883879134)
10. [Part 10](https://github.com/hcoona/three/issues/843#issuecomment-5883879298)
11. [Part 11](https://github.com/hcoona/three/issues/843#issuecomment-5883879547)
12. [Part 12](https://github.com/hcoona/three/issues/843#issuecomment-5883879704)

Concatenate the base64 code-block contents in numeric order and decode once;
verify the gzip and JSON hashes before extraction. Decode each blob according
to its declared encoding, verify its SHA-256 and write only manifest-listed
relative paths inside a new local directory. All twelve published bodies were
read back and reconstructed to the exact original packet bytes. The independent
reviewer also reconstructed the selected packet and executed both replays.

The packet includes original archives, typed DAG payloads, reviewed terminal
scalar extracts, recorded native/consumer results, independent reviews,
original campaign evidence and `replay_recovery_evidence.py`. The wrapper's
SHA-256 is `c88269ca6db0279e7205b1f59b1a99e8de6b8035ebb3a3bf6b489af9bdeb885e`.
Its packaged README is the original pre-execution proposal; the later execution
reports and independent review establish the completed result.

Prepare the pinned source and its locked CPython 3.14.3 environment before
network isolation. Required reader dependencies are PyYAML 6.0.3, rfc8785 0.1.4,
packaging 26.3, tomli-w 1.2.0 and nbgv-python 2.1.0.dev1. Use the wrapper's
packaged invocation contract with `--stage terminal --recompile-model`:

- Replay seed package `frozen-replay/03-01` with manifest SHA-256
  `24fadfadc000a74557c6454499c9cf01afb0816cf96bd1106a7f853206d67277`.
- Replay recovery package `frozen-replay/03-02` with manifest SHA-256
  `a799f143f3af177dc5c7dd32cb5f94d6e55f583d94a4ccebd7c0bc99e5222bd7`,
  also passing the seed package and its manifest hash.

Use a separate fresh output leaf for each invocation, OS-denied networking and
read-only source, evidence and Git-object mounts. The demonstrated Linux
containment used `unshare --user --map-root-user --mount --net`, private mounts
and read-only bind mounts; Windows operators need an equivalent Linux/WSL
environment. Both executions recompiled Model and reproduced the original
Outcome bytes exactly. They invoked pinned pure readers/Finalizer, never live
operators, registry reads, credential acquisition or installation consumers.

This public replay uses **reviewed scalar extracts**. It does not re-extract
all raw job logs or prove a publicly self-contained closure of every gate hash.
Full original logs, ZIPs, private transfer headers and some gate dependencies
remain in the retained original archive. Recorded native/consumer results are
reviewable historical observations; replay does not make fresh registry or
consumer observations. No token, authorization header or signed transfer URL
belongs in the public packet. GitHub/TestPyPI retention is not unlimited.

## Historical sources and preserved failures

The executed [protocol][historical-protocol], [LLD][historical-lld] and
[temporary callers][historical-callers] remain available at the exact b39
source. Historical artifacts use those pinned readers; current strict readers
reject the retired proof-bearing shape. Historical source is interpretation,
not permission to re-execute an operator.

The earlier `0.1.0b31` failed partial Attempt and its complete original archive
remain retained. Its [known-state closure](https://github.com/hcoona/three/issues/843#issuecomment-5883055780)
and [independent review](https://github.com/hcoona/three/issues/843#issuecomment-5883055904)
are separate from b39 success. Its publication reader is pinned to
`86b63fd09fcf3d20ee2615b35e37f4a541d0dc27`; the later GET-only terminal
continuation caller is pinned separately to
`3faff7653efbde37487d381577fed9cc72e16d04`. Their original bindings, distinct
publisher/audit deadlines, protocol hashes and spent reservations remain
unchanged. Successful supplementary readback did not rewrite its failure.

The [b38 cancellation/no-upload closure](https://github.com/hcoona/three/issues/843#issuecomment-5883197568)
also remains spent. It had no original Outcome; none is invented. Preserve
all earlier versions, including partial bootstrap b6 and the independently
completed [normal b26 publication](./python-normal-live-evidence.md).
No deletion, rollback, old-version recovery, duplicate/race experiment or
production PyPI operation is authorized by this completion.

[historical-protocol]: https://github.com/hcoona/three/blob/b7b29e5f43fae031c0bee3b2f6aad89475a78ec4/src/public/lib/three-workflow-delivery-v3/docs/validation/python-hosted-recovery.md
[historical-lld]: https://github.com/hcoona/three/blob/b7b29e5f43fae031c0bee3b2f6aad89475a78ec4/src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-python-lld.md#temporary-hosted-recovery-proof
[historical-callers]: https://github.com/hcoona/three/tree/b7b29e5f43fae031c0bee3b2f6aad89475a78ec4/eng/workflow-delivery/v3/python-recovery-proof
