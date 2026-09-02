# Scholarly Publication

`scholarly-publication` is a GitHub Copilot CLI plugin for producing one
bounded scholarly publication unit from an authorized born-digital PDF.

The plugin exposes exactly three skills:

| Skill                          | Sole responsibility                                                                                                                    |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| `scholarly-pdf-reconstruction` | Extract selected PDF pages into a translation-facing source package and optionally replay that extraction against the original source. |
| `scholarly-print-assembly`     | Compose approved fragments and consumed source assets into offline HTML/CSS/PDF and record the resulting publication tree.             |
| `scholarly-render-qa`          | Independently observe the assembly tree and final HTML/PDF behavior without reconstructing upstream semantics or repairing output.     |

Translation governance remains outside this plugin. Scan cleanup and OCR
remain outside the reconstruction skill.

Skill guidance defines authority boundaries, required outcomes, decision
points, and failure conditions rather than one universal agent procedure.
Agents may choose an appropriate investigation and reporting approach.
Ordering is fixed only by machine contracts such as manifest bindings,
required render multiplicity, ordered evidence checks, and final publication
renames.

## Trust and threat model

The caller or workspace owner, exclusive writes to declared paths, the
operating system, the Python standard library, and pinned mature PDF, font,
CSS, and browser tooling are trusted. Input manifests, fragments,
stylesheets, PDFs, SVGs, and stale workspace content are untrusted until the
owning stage validates the portion it consumes.

The plugin does not defend against coherent workspace-owner rewrites, hostile
concurrent writers, compromised tools or operating systems, or claims of
authenticity and nonrepudiation. SHA-256 values bind bytes for consistency and
replay; they are not signatures and do not identify an author.

Each downstream stage trusts the validated boundary owned by its upstream
stage. Manifest hashes and retained-tree closure catch ordinary byte
corruption. A coherent rewrite by the trusted caller or workspace owner is
outside scope.

## Handoff concepts

The five named handoff concepts are:

1. `source-package.json`
2. `translation-bundle.json`
3. `assembly-manifest.json`
4. `qa-evidence.json`
5. `release-manifest.json`

`assembly-spec.json` is a caller-authored build recipe. It selects inputs and
layout choices but is not a sixth authority. After a successful build,
`assembly-manifest.json` is the authority for the assembled tree and the
values QA must observe.

The retained guarantees are intentionally local:

- reconstruction owns source extraction, ordered block evidence, canonical
  page SVGs, asset-derived review facts, extraction-time trace observations,
  and optional exact source replay;
- assembly validates upstream schemas, status, manifest hashes, approved
  fragments, and only the blocks, maps, and SVGs it consumes;
- QA validates the assembly manifest, its semantic asset closure, and
  observable HTML/PDF behavior without loading upstream contract schemas;
- publication output retains the input manifest snapshots, approved fragment
  copies, used page SVGs, fonts, stylesheets, HTML, CSS, PDF when rendered, and
  the assembly manifest.

The publication tree is not a complete archival closure of the source package
or translation approval workspace.

After build, standalone Assembly validation treats the three retained input
snapshots as opaque lineage assets bound only by path, hash, and length. It
does not parse them, replay the recipe, or reconstruct manifest values.
The deduplicated union of manifest input, fragment, figure-SVG, font,
stylesheet, and non-null output records is the complete retained regular-file
inventory. Build validates authored HTML/CSS policy and generated
composition. Standalone validation checks manifest consistency, retained-tree
integrity, generated topology and local-resource bindings, and bounded output
parseability without reparsing retained fragments and stylesheets or
reconstructing the composer. Browser rendering and QA determine actual glyph
selection.

Reconstruction publishes only to an absent output path. It validates a sibling
candidate and performs one final rename into place; reruns require a new path
or caller-managed deletion of the prior output. Page content counters are not
emitted. Text volume, replacement characters, and raster-image presence are
recomputed from the bound block JSON and page SVG bytes. Canonical
hidden/nonpainting-text and trace-failure observations remain extraction-time
issues.

Assembly follows the same fresh-output lifecycle: build publishes one validated
sibling candidate to an absent path with one final rename. Render is also
fresh-only: it requires a null manifest PDF output and an absent destination,
then adds only the PDF asset record. Reruns require a new build path or
caller-managed deletion.

QA also publishes only to an absent dedicated review root. It builds one
sibling candidate and performs one final rename. A completed blocking result
contains evidence, both renders, and all rasters without a release manifest;
an operational failure publishes no review root and cleans or explicitly
reports an orphan candidate.

## Publication profiles

Assembly and QA carry byte-identical
`assets/publication-profile.json` files. Assembly owns enforcement of its
authored-fragment/stylesheet policy and generated composition during build.
Standalone validation checks the manifest, retained tree, generated topology,
resource bindings, source-SVG crop geometry, and bounded output parseability;
it does not replay the composer. QA trusts that stage boundary plus manifest
hashes and retained-tree closure. It binds the bundled profile's closed
identity/version/hash without replaying Assembly validators. QA independently
checks narrow CSS/source-SVG resource safety, role-aware offline browser
routing, active print-page rules, effective UTF-8 metadata,
visibility-aware fragment text, normalized raw `aria-label` bindings,
figure/crop geometry and ownership, overflow, bounded complete renders, and
final PDF behavior under the 500-page ceiling.

QA explicitly does not guarantee independent authored-profile conformance,
exact generated markup/CSS reproduction, retained-snapshot or source replay,
visual/editorial/accessibility approval, defense against hostile concurrent
writers, or crash recovery.

Stable QA and release asset bindings state their relative path bases:
`publication-root` for the assembly manifest, `evidence-root` for independent
render PDFs and rasters, and `release-root` for QA evidence in the thin release
index.

## Source and deployment layout

Canonical runtime skills live under `skills/`. Maintainer-only tests and eval
fixtures live under the package-level `tests/` and `evals/` directories
because APM installs each declared skill directory recursively.
`.agents/skills/` is generated deployment output and must contain only
`SKILL.md`, runtime schemas/assets, references, and scripts.

## Validation

From the repository root:

```powershell
mise run scholarly-publication-plugin-source-check
mise run scholarly-publication-plugin-runtime-check
mise run scholarly-publication-plugin-spec
mise run scholarly-publication-plugin-test
mise run scholarly-publication-plugin-lint
```

Contract/distribution validation covers canonical source, runtime includes,
schema validity, shared-file identity, root package registration, lock
bindings, and deployed parity. Assembly owns publication-profile semantics;
QA owns profile identity binding and observable release outcomes.

## Runtime requirements

- `mise`
- `uv`
- A local Chromium-family browser
- Python dependencies declared inline by each PEP 723 runtime script

The plugin does not download or vendor publications, fonts, or browser
binaries.
