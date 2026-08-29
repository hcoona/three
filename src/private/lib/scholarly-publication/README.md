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
  page SVGs, source status, and optional exact source replay;
- assembly validates upstream schemas, status, manifest hashes, approved
  fragments, and only the blocks, maps, and SVGs it consumes;
- QA validates the assembly manifest, its declared tree, and observable
  HTML/PDF behavior without loading upstream contract schemas;
- publication output retains the input manifest snapshots, approved fragment
  copies, used page SVGs, fonts, stylesheets, HTML, CSS, PDF when rendered, and
  the assembly manifest.

The publication tree is not a complete archival closure of the source package
or translation approval workspace.

## Publication profiles

Assembly and QA carry byte-identical
`assets/publication-profile.json` files. This compact profile contains only
policy identity, HTML/attribute allowlists, CSS property/at-rule/selector
allowlists, and explicit global prohibitions. The profile may only narrow the
fixed element/local-attribute, global-attribute, and CSS-property ceilings
implemented independently by package validation, Assembly, and QA. The
runtimes also implement fixed selector and CSS value semantics and exercise
shared test-only conformance corpora. The assembler-generated document wrapper,
figure/crop markup, font rules, and page CSS use a separate closed
generated-output profile documented by the assembly and QA contracts.

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
mise run scholarly-publication-plugin-check
mise run scholarly-publication-plugin-spec
mise run scholarly-publication-plugin-test
mise run scholarly-publication-plugin-lint
```

Contract/distribution validation covers canonical source, runtime includes,
schema validity, shared-file identity, and deployed parity.

## Runtime requirements

- `mise`
- `uv`
- A local Chromium-family browser
- Python dependencies declared inline by each PEP 723 runtime script

The plugin does not download or vendor publications, fonts, or browser
binaries.
