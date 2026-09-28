# Python TestPyPI Normal-Live Evidence

## Completion and scope

`hcoona-release-smoke-python` version `0.1.0b26` completed the TestPyPI
normal publication and fresh wheel/sdist consumer journey on September 28, 2026. The [independent final audit][final-audit] joins original Approval,
publication and terminal replay with freshly downloaded exact distributions
and separate clean consumers. Production PyPI remains blocked; this result
completes only the TestPyPI portion of `WD-PY-004`.

This record owns the runtime completion evidence consumed by the project
README, document index and agent handoff. V3 maintainers retain the evidence
and update those consumers when the claim changes. The
[requirements](../requirements.md#python-smoke-slice) and
[migration policy](../migration-strategy.md#python-smoke-delivery) retain their
normative roles. A separate validation carrier preserves exact provenance and
limits without duplicating it across status pages or the service-source study.

| Requirement              | Evidence                                                                                                                                              |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WD-PY-002`, `WD-PY-003` | Full-history NBGV version, original qualified wheel/sdist, metadata and target-bound witness admitted by the [pre-Approval audit][preapproval-audit]. |
| `WD-PY-004`              | TestPyPI-only publication, fresh exact bytes and separate clean wheel/sdist consumers in the [final audit][final-audit]; no PyPI completion claim.    |
| `WD-PY-005`, `WD-PY-007` | Current-run two-file Approval, ordered upload/readback and original authoritative `published` Outcome independently [replayed][terminal-audit].       |
| `WD-PY-006`, `WD-PY-008` | Accepted platform reliance, separately approved bounded campaign, retained failed Attempts and first-success stop; no duplicate/race probes.          |

## Source and publication

[PR #901](https://github.com/hcoona/three/pull/901) delivered exact target/tooling
`adfdfe8ef51d9fbbc4db0dfdea3bd21927d8c54e`, with reviewed/merged tree
`d9d1300637d4f4e5012f4c1649f012eba6a5ffce`. Exact-merge
[CI](https://github.com/hcoona/three/actions/runs/36461235279) and
[CodeQL](https://github.com/hcoona/three/actions/runs/36461235213) passed.
The [execution admission][admission] bound that source to complete-history
public-main NBGV `0.1.0-beta.26`, height 26, and PEP 440 version `0.1.0b26`.

[Run `36462600325`, attempt 1][run] used the stable
`workflow-delivery-v3-python-smoke.yml` entry and its own original Build,
Qualification, Snapshot, Bundle, Approval and Authorization. The destination
Environment was `workflow-delivery-v3-python-testpypi`, ID `22765954016`,
deployment `6717106265`. The original ordered wheel and sdist uploads each
consumed one POST. Readback observed two pending wheel reads then exact state,
and three pending sdist reads then exact state; the independent terminal audit
checked the ten-second spacing and sixty-second windows.

The original Outcome is `published`, SHA-256
`b27631c3e33163137b9bf460d801b7a6e531d7289dbc1b3202a07d23fa62e6d4`.
Offline replay from the original artifacts and verbatim finalizer controls
matched it exactly. `possibly-mutated: false` means a resolved successful
outcome, not absence of mutation. The Result artifact is
[`10989110379`](https://github.com/hcoona/three/actions/runs/36462600325/artifacts/10989110379),
SHA-256 `ffec1707a3d0e819480d447faaed80d4013e019fe485c7b449671010ab122c2a`.

| Original distribution                                   | Artifact ID   | SHA-256                                                            |
| ------------------------------------------------------- | ------------- | ------------------------------------------------------------------ |
| `hcoona_release_smoke_python-0.1.0b26-py3-none-any.whl` | `10987973985` | `ac0f8f0c00b6442fd38e9bddc23e82b5818e8990a304c7af5cf9bae76fe56204` |
| `hcoona_release_smoke_python-0.1.0b26.tar.gz`           | `10987724273` | `a5727ba0a8b86267666eabfdf5aa83340fc95e336b53a09e870006759a561984` |

## Fresh bytes and consumers

The final audit capture ran from `2026-09-28T18:19:43.100073Z` through
`2026-09-28T18:19:44.096758Z`: one Simple Index GET and two file GETs, all HTTP 200. The index response SHA-256 is
`ac3ac7484a0ace519c69e7b242158209d1718fa9fffdccbdb9dc1f13a9b81de2`.
Both fresh distributions exactly matched the original approved hashes above.
Independent offline typed-reader replay reproduced the retained observation.

Wheel installation and sdist rebuild/installation used separate newly created
environments outside the checkout. Archive installations disabled index,
dependency, source and cache lookup; imports used Python isolated mode.
Installed version, `project_id()` and source witness matched the approved
package, and imports resolved to each environment's `site-packages`. The sdist
used ordinary PEP 517 isolation with permitted public PyPI build dependencies;
its rebuilt wheel was byte-identical to the original. All retained consumer
commands exited successfully.

This observation is scoped to the admitted CPython 3.14.3/OpenSSL 3.5.5
environment and locked tools, including UV 0.10.9 and Hatchling 1.32.4. The
operator selected the existing system CA store per process; host trust and
TLS verification were unchanged. It is not universal consumer support,
indefinite TestPyPI retention, atomic two-file publication, unique insertion
or isolation from the accepted sole writer.

## Closure and retained evidence

The [approved campaign][approval] used only slot 01, starting at
`2026-09-28T18:04:16.038968Z`. The original four-hour lifetime and Governance
expiry were unchanged. Its final independent gate closed the slot as
`audited-success`; the campaign is stopped. First audited success ends all
remaining slots. No further dispatch, Approval, OIDC, upload, registry audit,
cleanup or Governance renewal follows from this result.

The final original ledger SHA-256 is
`88a63a9ab8fd5c9cdd2d66d0c2d3ca654d8bfe3dcb1c4b89d8ef68e9b3aebc4f`.
It retains 18 operator JSON GETs, 16 log HTTP transfers and 40 artifact HTTP
transfers, including redirects; the latter cover twenty distinct artifacts.
There was one dispatch, one Environment approval and one ordered upload pair.
Runtime plus final-audit smoke reads total ten index GETs and five file GETs.
Hosted native-control reads and build-dependency downloads have their separate
protocol bounds; operator totals are not all network traffic.

The selected replay packet is retained in five numbered Issue comments:
[1](https://github.com/hcoona/three/issues/843#issuecomment-5876056267),
[2](https://github.com/hcoona/three/issues/843#issuecomment-5876056545),
[3](https://github.com/hcoona/three/issues/843#issuecomment-5876056970),
[4](https://github.com/hcoona/three/issues/843#issuecomment-5876057318), and
[5](https://github.com/hcoona/three/issues/843#issuecomment-5876057688).
Concatenate their Base64 blocks in order, decode and gunzip; the resulting
`sha256-file-map-v1` JSON maps paths to hashes and hashes to exact UTF-8 or
Base64 bytes. Verify every reconstructed file against its SHA-256.

| Carrier                  | SHA-256                                                            |
| ------------------------ | ------------------------------------------------------------------ |
| Compressed replay packet | `768fce9d891a1e2e79b3a5944af4e1763ded81a7b3ca473d63d6aab9bd5e0d2d` |
| Decompressed JSON        | `78005acca4a44a20a1347a32631af7b8546571941026c6c9d717dfa4d1217ab6` |

The 69 selected files preserve original twenty-role payloads, selected platform
controls, verbatim finalizer-step evidence, fresh registry responses/consumers,
independent reports/gates, final ledger and stopped campaign. This is a selected
replay packet, not the full raw transfer archive. Full raw logs/ZIP and private
headers remain locally retained; signed URLs, credentials and private headers
are excluded from the public packet. Native approval API-response-digest and
platform-provenance limits in the independent audits remain unchanged. Public
packet retention avoids relying solely on expiring Actions artifacts; neither
GitHub nor TestPyPI is claimed to provide unlimited retention.

The earlier [failed normal run](https://github.com/hcoona/three/actions/runs/36383200978)
and [original audit](https://github.com/hcoona/three/issues/843#issuecomment-5864320684)
remain failed. The [TLS-stopped operation](https://github.com/hcoona/three/issues/843#issuecomment-5862680211),
failed bootstrap `0.1.0b6` and [native counterexample](../research/python-smoke-evidence.md#retained-identical-duplicate-counterexample)
remain untouched. This success neither repairs nor relabels their evidence.
Production PyPI requires its own authorized configuration/admission and real
publication journey; Issue #843 remains its work carrier.

[run]: https://github.com/hcoona/three/actions/runs/36462600325
[admission]: https://github.com/hcoona/three/issues/843#issuecomment-5875702720
[approval]: https://github.com/hcoona/three/issues/843#issuecomment-5874807332
[preapproval-audit]: https://github.com/hcoona/three/issues/843#issuecomment-5875842238
[terminal-audit]: https://github.com/hcoona/three/issues/843#issuecomment-5875938852
[final-audit]: https://github.com/hcoona/three/issues/843#issuecomment-5875991538
