---
name: scan-book-quality-control
description: >-
    Use this skill to validate a processed scan batch before delivery: compare input
    and output counts, verify consistent dimensions and nonempty files, measure
    background/ink/border statistics, identify geometry and edge outliers, require
    representative visual review, and reject transformations that worsen residual
    error. Trigger for scan QA, output verification, regression checks, whole-book
    review, print-readiness checks, or requests to prove that restoration succeeded.
compatibility: Designed for GitHub Copilot CLI on Windows. Requires mise, AzureAuth access to Lucia_PrivatePackages, and network access on every invocation.
---

# Scan book quality control

Validation is a gate, not a final glance. Check the whole batch mechanically and
review representative plus outlier pages visually.

## Workflow

1. Run:

    ```powershell
    Set-Location C:\path\to\scan-book-quality-control
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1 validate_book.py INPUT_DIR OUTPUT_DIR --evidence-report mechanical-evidence.json
    ```

    Conservative safety budgets are enforced before expensive decoding or
    comparison work: at most 500 images per side, 50,000,000 decoded pixels per
    page, 2,000,000,000 total input/output pixel-units for compact-feature
    extraction, 250,000 output-to-input cross-match comparisons, and 125,000
    output duplicate comparisons. Aggregate retained cross-page features are also
    capped at 256 MiB. Override them explicitly with `--max-pages`,
    `--max-decoded-pixels-per-page`,
    `--max-total-compact-feature-pixels`,
    `--max-retained-feature-bytes`,
    `--max-peak-encoded-buffer-bytes`,
    `--max-cross-match-comparisons`, `--max-duplicate-comparisons`,
    `--max-components-per-extraction`, `--max-component-match-comparisons`,
    `--max-inventory-entries`,
    `--max-inventory-depth`, `--max-inventory-total-bytes-hashed`, and
    `--max-inventory-file-bytes`. Directory inventory uses streaming `scandir`
    enumeration and aborts immediately when entry, depth, page-count, or byte
    limits are crossed, before exhaustive sorting or pairing. Oversized entries
    are not content-sniffed or hashed; a rejected tree is not recursively expanded
    or partially hashed beyond its budget.
    Exceeding any budget fails the gate rather than silently sampling or skipping.
    The peak encoded-buffer limit is a conservative estimator: it includes the
    single preallocated encoded source buffer plus one full-size transient decoder
    read. Encoded files are not assembled from chunk lists or copied into
    `BytesIO`; decoders receive a bounded seekable view of the retained buffer.
    Large connected-component masks are preflighted across their complete area
    with bounded streaming horizontal and vertical run counts, so center-localized
    speckles cannot evade a corner sample. Ambiguous overestimates fail closed and
    require review before any full label/statistics allocation.
    The limits, observed workload, rejection status, and indexing strategy are
    recorded in `evidence.safety_budgets`. If any custom budget override is used,
    repeat every override with the identical value on the approval/final run.
    `evidence.approval_run_argument_template` is generated with the exact custom
    override arguments; replace its path placeholders rather than rebuilding the
    command by hand. Omitting or changing an override changes the evidence hash and
    makes approval stale.

    Cross-page identity scores are all evaluated within those comparison limits,
    but the report never serializes the full quadratic score matrix. For each
    output it retains only the top candidates plus the mapped candidate, and each
    pair retains the mapped score, strongest alternate, and corroboration used by
    the decision. Duplicate evidence contains only threshold-matching candidates and is capped
    at 256 records. Duplicate failures and review requirements are emitted as
    bounded batch summaries with counts and a few examples rather than one string
    per pair or per affected page; every mechanically decisive duplicate still
    fails. The report records counts and truncation semantics explicitly.
    Truncation of exact-alternate or decisive duplicate evidence adds a mandatory
    batch-identity review item; omitted routine scores cannot affect the recorded
    decision. This keeps duplicate-heavy 500-page reports and approvals compact.

    Input and output must be distinct, non-nested, real directories. A root that is
    itself a symlink, junction, or other reparse point is rejected. Unique numeric
    filename identities are paired directly. Duplicate, partial, or mismatched
    identities are a mechanical failure and review item; pairing never silently
    falls back to position. Nonnumeric batches require an explicit complete map:

    ```json
    { "pairs": [{ "input": "front.png", "output": "clean-front.png" }] }
    ```

    Pass it with `--pairing-manifest C:\path\to\pairing.json` on both the evidence
    and approval/final commands. Every top-level input and
    output image filename must occur exactly once. Manifest order is authoritative
    for beginning/middle/end and cover selection. The manifest must be a regular
    non-reparse file on a non-reparse path. Its inventory metadata and SHA-256 are
    evidence-hashed, it is included in the filesystem snapshot, and it is
    revalidated immediately before evidence or final publication. Manifest JSON
    is limited to 4 MiB before reading or hashing.

    `--evidence-report` must name a new `.json` file in an already-existing, non-linked
    directory outside both batch trees. Existing targets are never overwritten.
    The report is published atomically only after all evidence is collected and a
    final inventory/stat/SHA-256 pass proves that every paired image and inventory
    entry still matches one stable snapshot. It is mechanical evidence, not final
    approval, and must remain unchanged.

2. Fail delivery if:
    - page counts differ;
    - any supported image is nested, any image-like candidate (including
      PSD/SVG/PDF content) has an unsupported format, any accepted image decodes
      to other than exactly one frame, or an image is unreadable/empty;
    - output dimensions are inconsistent;
    - identity checks confidently detect a duplicate, substitution, rotation,
      horizontal/vertical mirror, transpose/transverse reflection, or severe
      structural mismatch;
    - any input file and output file resolve to the same filesystem identity
      (including hardlinks);
    - a page has materially less ink than its source without being intentionally blank;
    - absolute pre-normalization paper/background measurements prove objective
      extreme darkening, unevenness, or introduced color-cast corruption;
    - a geometry transform increases measured residual error; vertical
      convergence uses separate left/right positional slope models rather than
      absolute line tilt, while insufficient two-sided evidence remains review.
3. Visually inspect every page/reason in `visual_review_pages`. The report
   represents beginning/middle/end, first/last or covers, likely blanks,
   text-heavy candidates, music/dense-structure candidates when detected, and
   every metric flag in `visual_review_categories`.
4. Mechanical success sets `mechanical_pass`, but the evidence report always has
   `passed=false`. Its `evidence_hash` covers the complete canonical immutable
   mechanical report body (including status and the single nested `evidence`
   object), not merely a nested subset; the approval template is derived from that
   hash. There are no duplicate top-level evidence fields. Report target paths are
   excluded. Copy
   `approval_template` to a separate `approval.json`, fill reviewer and note,
   and copy each inspected page's complete `required_reasons` list into that
   page's `acknowledged_reasons`. After inspecting every listed page for every listed
   reason, run the approval stage and choose a separate new final report path:

    ```powershell
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1 validate_book.py INPUT_DIR OUTPUT_DIR --pairing-manifest C:\path\to\pairing.json --evidence-report mechanical-evidence.json --approval approval.json --final-report final-quality-report.json
    ```

    Append the same custom budget arguments used in step 1, in the same values, to
    this command. Prefer the report's generated
    `evidence.approval_run_argument_template`, which already includes them.

    The second run verifies that the preserved report is internally intact, that
    current mechanical evidence has the same hash, and that approval contains the
    exact whole-report hash, nonempty reviewer identity and note, and every required
    page/reason acknowledgement. It never overwrites the evidence report. Any
    status, image, manifest, inventory, metric, threshold, failure, or review-reason
    change makes approval stale. Immediately before final publication it rehashes
    and restats every paired input/output and every inventory entry, regenerates
    both inventories, and aborts without a final report on mutation. Final
    publication is atomic and refuses existing targets.
    Evidence and approval JSON are parsed with duplicate-key rejection and are
    limited to 64 MiB and 4 MiB respectively before reading or hashing. Final
    JSON is limited to 4 MiB while it is streamed to its atomic temporary file.
    Bounded snapshots re-check size before hashing and stop if a file grows across
    its limit. Retained manifest, evidence, and approval handles are re-read and
    SHA-256 verified immediately before publication, so same-size in-place
    rewrites remain detectable even if timestamps are restored. Their identities
    are also revalidated along with both batch trees.

5. Fix and rerun until final `passed` is true.

PNG `pHYs`, JPEG EXIF/JFIF density, and TIFF resolution tags are captured and
normalized to oriented X/Y DPI and physical page dimensions. Missing or
unreliable metadata requires review; losing reliable source DPI or materially
changing DPI/physical size fails at the authoritative thresholds. If a known
legacy workflow cannot preserve reliable DPI, document it on both runs with
`--dpi-workflow-note "specific workflow and external physical-size check"`.
This keeps missing metadata review-safe, but does not excuse a severe measurable
change when both sides have reliable DPI. The note, metadata, comparisons, and
thresholds are evidence-hashed.

## Reported thresholds

The JSON report contains the authoritative `thresholds` object. Foreground is
measured relative to each page's estimated global and local paper background,
with an Otsu-constrained contrast threshold and connected-component speck cleanup.
Likely blank includes both pages below a conservative 0.02% source foreground and
pages below 0.2% whose foreground consists only of small dirt-like components.
Blank intent is classified before structural and ink hard-failure decisions, so
cleaning small dirt remains a mechanical pass while still requiring representative
blank review. Nonblank retention below 70% fails with a small 0.02% absolute-loss
safeguard, and near complete output erasure is checked explicitly; blank added ink
above 1% fails. Ink retention uses connected foreground mass normalized by an
independently estimated source-to-output feature or component registration, with
the canvas transform as a fail-closed fallback. Output foreground bounds never set
this scale, because deleting content can shrink those bounds. Thus deleting half
the content reports about 50% retention and fails, while proportional scaling and
white padding retain about 100%. All
outer 2% and internal 2–8% edge bands are measured. Bands above 4% require visual
review. In addition, connected components are searched symmetrically through the
outer 12% of the top, bottom, left, and right sides, so long thin horizontal or
vertical scanner strips are detected even when inset from the physical edge or
diluted inside a wide band. Every qualifying component is retained per side and
matched to source components by normalized position, extent, and thickness, with
a second match under the measured crop/padding/content-registration transform.
That transform also checks qualifying source components which originated outside
the source outer-12% zone but are expected inside the output edge zone.
Conversely, source edge components mapped inward beyond the output outer-12%
zone are matched against full-canvas candidates. Uniform physical scaling plus
contained white-padding canvas evidence remains reliable even when its normalized
scale is small.
Components explained only by the transform are uncertain and review-only. A
genuinely unmatched qualifying output strip, or a materially enlarged matched
strip, fails mechanically and requires review even when a larger unchanged source
border or staff component is present. Side projections independently retain long
dark runs whose corner-connected rectangular component has less than 60% bounding-box
fill. Perpendicular endpoint agreement and dark support at all four corners must
form a top/bottom/left/right frame before this evidence can hard-fail. A new or
materially enlarged four-sided frame fails; a source-consistent page border is
review-only, a match requiring the measured content transform is uncertain and
review-only, and an unmatched removed source frame/border requires review. This
paired four-side geometry prevents isolated staves, braces, and page-border
fragments from being classified as scanner frames. Broad edge
content already present in the source, including staves, page numbers, borders,
and cover art, remains a mandatory review item rather than a false hard failure;
uncertain shifts and crops are reviewed too. Unmatched source-side broad
components are also retained across the full outer 12% and compared both directly
and under content registration. They require review as potential removed content;
an inset component that should remain in the registered edge zone is a
high-confidence removal failure, while physical-edge crop/padding or a component
registered outside that zone remains review-only. A dark-fraction decrease of 8
percentage points in an edge band is also mandatory review for possible crop or
content loss.
Horizontal skew and vertical-convergence/barline residuals are measured
independently. Either fails when residual grows by over 0.30 degrees and 1.5x.
Every measurable output residual over 0.50 degrees requires review regardless of
adaptive statistics; any unmeasurable source/output comparison also requires
review. Source/output canvas aspect ratios and robust 0.5%-99.5% foreground quantile
bounds are compared explicitly, so isolated specks do not determine geometry.
Horizontal and vertical physical content scale are estimated from registered
content bounds and internal row/column projection landmarks, so same-canvas
anisotropic stretch is not dependent only on the outer foreground box. Canvas
aspect changes by themselves indicate blank-margin crop or padding and require
review rather than a hard stretch failure. Normalized bbox edges and foreground
centroids are also compared, with isotropic content scale and crop/shift residuals;
changes above 2% require review. Registered-content anisotropic residual above 2%
requires review and above 5% fails. Compact global histograms plus 4x4 spatial Lab/chroma region signatures require
review for regional hue swaps, shifts, or color loss even when luminance
structure and the global color histogram remain equal. A grayscale or
near-zero-chroma source that gains material chroma is independently flagged as
introduced color; this check is not gated on the source already being colorful.

Absolute tonal measurements are taken from decoded samples before any min-max
normalization. Each page reports full luminance and robust paper/background
percentiles, paper highlight range and clipping fractions, a 4x4 local-paper
unevenness profile, and (for color-capable pages) median background RGB and Lab
color-cast chroma. A grayscale or zero-chroma source uses a zero cast baseline, so
a tinted color output is still detected. Source/output comparisons require review when paper materially
darkens or turns gray, loses highlight range, clips to a dark ceiling, becomes
locally uneven, or gains a color cast even when thresholded ink and normalized
structure remain similar. Legitimate whitening is not flagged merely for raising
paper brightness or clipping clean paper to white. Only conservative, objectively
extreme source-relative corruption thresholds fail mechanically; lesser changes
remain mandatory visual-review reasons. These metrics, comparisons, thresholds,
and reasons are part of the full evidence hash and approval acknowledgements.

## Gotchas

- A clean outer 12-pixel border does not prove that internal edge dirt is gone.
- One good sample does not validate a batch.
- Mechanical metrics cannot reliably distinguish music braces, dense notation, or
  cover art from dirt, especially after crop or registration shifts. Suspicious
  edge changes therefore trigger mandatory review. Only a source-relative,
  broad, connected, filled strip with a material thickness increase is classified
  as a high-confidence scanner-strip failure; unchanged legitimate edge content
  stays review-only.
- Actual decoded format is restricted to JPEG, PNG, TIFF, or WebP as appropriate
  for the accepted extension; renamed or decoder-supported alternative formats fail.
  Frame count is decoded from content for every accepted extension; multi-frame
  PNG/WebP/TIFF or any other accepted format is rejected.
- Each image is read once into an immutable byte buffer used for header/frame
  inspection, Pillow decoding, compact metrics, encoded SHA-256, and canonical
  decoded-content identity. Pillow applies EXIF orientation before every metric and decoded-content
  comparison. The original mode, orientation, oriented dimensions, sample dtype,
  depth, frame count, and alpha facts are retained in evidence. Applied EXIF
  orientation and greater-than-8-bit samples require explicit review.
- Alpha images, including PNG `tRNS`, palette transparency, and `LA`, retain alpha
  evidence and are composited over white for metrics.
  Any nonopaque pixels force explicit review, so transparent or hidden content
  cannot silently pass.
- Encoded sample depth and channel count come from PNG IHDR/chunks or TIFF
  BitsPerSample/SamplesPerPixel metadata before conversion. Bilevel mode `1` is
  valid 1-bit input. Supported 16-bit grayscale and `tRNS` are composited without
  integer overflow; greater-than-8-bit multi-channel input is rejected before
  Pillow can silently down-convert it.
- Unsupported and nested image candidates are reported rather than silently
  excluded from page counts. Every top-level file and every explicit nested entry
  is inventoried. Unrecognized files require visual review and classification;
  recognized unsupported image formats fail mechanically.
- Structural/perceptual comparison is tone-tolerant and records all rotation,
  horizontal/vertical reflection, transpose, and transverse scores plus every
  output-to-source cross-match. Orientation/reflection failure requires
  independent decoded-pixel, local compact-foreground, or color corroboration;
  a lone structural edge signature is mandatory review. Approximate substitution
  likewise requires independent local/pixel support, while exact decoded mapping
  evidence remains conclusive. Repetitive music ambiguity is mandatory review.
  Cross-page substitution and duplicate matching maximizes across all eight
  rotation/reflection transforms. Decoded-content duplicate and
  substitution checks do not depend on encoded SHA-256 and still flag ambiguity
  when source pages are structurally similar. Identical blank signatures compare
  as identical rather than failing because both vectors have zero norm. Exact or
  near-identical outputs mapped to distinct sources fail unless source equality is
  independently established by the canonical native decoded SHA-256. The sole
  additional review-only case is an exact output match where both paired sources
  are independently classified likely blank, remain below the raw-ink and
  fine-component safeguards, and each output has strictly less foreground than
  its source, consistent with dirt cleanup. Perceptual,
  compact-pixel, color, blank-page, and repetitive-layout similarity never
  establishes source equality. Legitimately duplicated source pages remain
  review-required only when their native decoded identities are exactly equal.
- Exact decoded-content matches use a canonical SHA-256 over oriented dimensions,
  decoded mode/sample depth/channel metadata, and the actual decoded channel
  samples. Color channels remain distinct for isoluminant pages; alpha is
  represented in canonical samples and white-composited for metrics. Source paths
  are rehashed against their captured buffers immediately before publication.
  Approximate identity checks
  use precomputed compact rotation/reflection signatures and run only when their explicit
  cross-match and duplicate comparison budgets admit the complete check; the
  validator fails closed instead of performing an unbounded quadratic scan.
- Full-resolution pages are processed one at a time and discarded. Only compact
  thumbnails, projection landmarks, and precomputed eight-transform signatures are
  retained, so duplicate and cross-match loops do not repeatedly decode, rotate,
  or signature full-size pages. Foreground cleanup uses one vectorized label keep
  table after connected-component statistics, and each page reuses its cleaned
  full-resolution and compact masks across metrics and signatures.
- Pairing manifests, evidence reports, and approvals reject duplicate JSON keys
  and are read under explicit 4 MiB/64 MiB/4 MiB byte limits.
- Every entry is inventoried, including directories, other files, symlinks, and
  reparse points, with explicit link/reparse status. Batch links/reparse points fail.
- Decoded-content SHA-256, pixel-level similarity, and structural signatures catch
  duplicate/substituted pages even when identical content was re-encoded.
- Preserve `mechanical-evidence.json`; approval and final reports are separate files.
- `scripts/run.ps1` creates a unique ephemeral Python 3.12.11 virtual
  environment through isolated mise configuration and removes it in `finally`.
  It installs exact NumPy 2.2.6, OpenCV 4.12.0.88, and Pillow 12.3.0 wheels from
  Lucia_PrivatePackages with the checked-in SHA-256 lock, `--require-hashes`,
  no dependency expansion, and no cache. AzureAuth is invoked from its standard
  0.9.5 installation path. The Lucia URL containing its token exists only in
  the pip child process environment; it is not written to disk, logs, or command
  arguments. The runner uses normal PATH resolution for mise, normal filesystem
  snapshots and hashes for evidence binding, explicit process timeouts, and
  invocation-owned cleanup rather than native launchers, ACLs, handle locks, or
  same-user adversarial race defenses.
