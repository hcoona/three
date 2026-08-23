---
name: scan-page-rectification
description: Use this skill to correct small-angle global rotation and linear vertical convergence using independent horizontal and vertical evidence. Trigger for slightly skewed text, slanted music staves, nonvertical barlines, or mild trapezoidal convergence. It is especially relevant to music books where staves and long barlines provide strong structural references.
compatibility: Designed for GitHub Copilot CLI on Windows. Requires mise, AzureAuth access to Lucia_PrivatePackages, and network access on every invocation.
---

# Scan page rectification

Model horizontal skew and vertical convergence independently. A page is not
rectified merely because its text baselines are horizontal.

## Capability boundary

This tool corrects only:

- small global rotation inferred from near-horizontal lines (within 2 degrees);
- mild, linear left-to-right variation in near-vertical line angle (edge
  convergence components within 0.8 degrees of the common tilt).

It does not detect or correct 90-degree/sideways orientation, arbitrary
perspective, page curl, wavy baselines, or other curved/local dewarp. Use a
dedicated orientation or mesh-dewarp workflow for those cases.

## Workflow

Run commands from this skill directory and invoke the PowerShell runner with
`-NoProfile`. Input and output must be different directories.
The runner provisions exactly Python 3.12.10, NumPy 2.2.6,
opencv-python-headless 4.12.0.88, and Pillow 12.3.0 in a unique ephemeral
runtime for every invocation. It never executes packages from a preexisting
skill runtime.

1. If available, run `/scan-batch-diagnostics` and identify representative and
   outlier pages. Otherwise, choose a standalone preview set: an early, middle,
   and late page; the most visibly rotated page; the strongest convergence
   outlier; a sparse/text-only page; and a dense notation page. Include both
   left- and right-hand pages when applicable.
2. Preview geometry correction:

    ```powershell
    powershell.exe -NoProfile -File ".\scripts\run.ps1" "rectify_pages.py" "C:\absolute\INPUT_DIR" "C:\absolute\PREVIEW_DIR" "--pages" "5" "20" "40" "--report" "C:\absolute\PREVIEW_DIR\report.json"
    ```

    `--pages` selects page numbers derived from the input filename stem's final
    underscore-delimited segment: all digits in that segment are combined
    (`scan_005` selects page `5`). Inputs are processed in natural filename order
    (`page_2` before `page_10`). Page selectors must be positive and unique;
    every requested number must match exactly one input. Missing or ambiguous
    selectors fail the batch. Every output is a grayscale PNG named from its
    input stem. Accepted 16-bit grayscale PNG/TIFF inputs remain 16-bit through
    rectification and output. Native 16-bit grayscale PNG `tRNS` transparency
    is composited onto white at uint16 depth before rectification; other 16-bit
    PNG transparency modes fail closed rather than downconverting. Other
    accepted inputs produce 8-bit output. The
    report records `input_bit_depth` and `output_bit_depth`.
    Unsigned 16-bit grayscale TIFF WhiteIsZero samples are inverted once at
    native depth after decoding and orientation, so geometry always sees the
    same black-ink/white-paper polarity as BlackIsZero TIFF.
    Inputs larger than 512 MiB encoded, 80 million decoded pixels, or an
    estimated 4 GiB working set (128 bytes per pixel for the rectification
    pipeline's simultaneous image, mask, coordinate, and detector arrays) fail
    before the corresponding allocation. Because OpenCV geometric operations
    require each dimension to be below 32767, a page at or above that boundary
    is left unchanged and reported as `review_required` rather than entering an
    OpenCV operation that can assert. Floating-point TIFF/Pillow `F` images
    are rejected; they are never normalized or converted to 8-bit grayscale.
    Every input is read once into an immutable byte buffer. Frame inspection and
    decoding use that same buffer, which must contain exactly one decodable
    frame; zero-frame, animated, and multipage inputs fail closed. The report
    records its SHA-256 hash. EXIF orientation is applied during decoding.
    Standard straight-alpha PNG/WebP transparency is composited onto white
    before grayscale conversion; unsupported premultiplied alpha modes are
    rejected rather than converted into black ink. Immediately before atomic
    publication, every input path is rehashed and any change aborts the batch.
    The output directory must not exist, and its parent must already exist
    without symlink/reparse traversal. There is no overwrite mode. Inputs with
    duplicate stems (including different extensions or letter case) are
    rejected because they would map to the same PNG.
    The complete output tree, including its report, is encoded in a unique
    sibling staging directory. Only after every artifact succeeds is that whole
    directory atomically renamed to the requested output. No files are promoted
    individually, and no replacement, backup, or partial-output path exists.

3. Inspect:
    - text baselines and staff lines for horizontal residual;
    - long barlines, system dividers, and frame lines for vertical residual;
    - top-versus-bottom width and left-versus-right convergence;
    - interpolation damage to thin notation.
      The mandatory visual-review list is the representative/outlier set plus
      every report page whose `review_required` field is `true`. Inspect every
      page on that list before approving the preview or batch.
4. Process the batch only after preview review:

    ```powershell
    powershell.exe -NoProfile -File ".\scripts\run.ps1" "rectify_pages.py" "C:\absolute\INPUT_DIR" "C:\absolute\OUTPUT_DIR" "--report" "C:\absolute\OUTPUT_DIR\report.json"
    ```

5. If available, run `/scan-book-quality-control`. Otherwise, review every
   report page whose `review_required` field is `true`, every
   `low_confidence`, `reverted`, and `partially_applied` report entry, and the
   representative/outlier set, comparing input and output at high zoom. Do not
   sample or omit any `review_required: true` page. Keep low-confidence or
   reverted pages unchanged rather than forcing transforms.

The JSON report gives overall and independent `horizontal_status` and
`vertical_status` values, explicit reasons, and a page-level
`review_required` boolean used to build the mandatory review list. Applied
candidates are always re-measured; a candidate that lacks post-transform
evidence, fails to reduce its own residual by both 0.02 degrees and 10%,
worsens a measurable independent-axis residual, or risks clipping ink is
reverted. The independent vertical validation metric is left-right
convergence differential only;
common vertical tilt is reported separately and can require review, but can
never hide or substitute for convergence. Final statuses are recomputed from
the final image: a measurable horizontal residual above 0.04 degrees or
convergence differential above 0.20 degrees always sets `review_required`,
even after a materially improved transform. Horizontal validation retains its
before/after vertical evidence in the `horizontal_validation_vertical_*`
fields rather than replacing it with later vertical-correction measurements.

After all accepted steps, original-to-final cumulative source-to-destination
coordinates and foreground loss are validated. Foreground is measured relative
to a smooth, locally estimated paper background rather than a fixed gray
threshold, so yellow or gray paper is not mistaken for ink. Isolated one- and
two-pixel specks are excluded while connected thin text and staff lines remain
tracked. Every source foreground pixel is projected through the composed
forward transform and must have output foreground at its destination, with
only a one-pixel rasterization/interpolation tolerance. Unrelated foreground
elsewhere cannot compensate for missing source-specific edge or page content.
A cumulative clipping failure reverts the page to the original and is recorded
in the `cumulative_*` report fields.

`--report` must resolve to exactly `OUTPUT_DIR\report.json`. No alternate
filename or nested report path is supported. Keeping that fixed root report in
the atomically published output tree is the only supported report-publication
design.

## Gotchas

- Ordinary note stems are semantic exclusions: vertical candidates must have
  continuous barline/divider-scale support or span a complete five-line staff
  system. Plausible near-vertical LSD fragments are first assembled
  by common line, center intercept, angle, and gap-tolerant support; only the
  resulting physical structure is then tested for length, ink continuity,
  complete-system staff crossings, and compact attached-blob topology.
  This reconnects barlines split at staff-line gaps without promoting each
  fragment to a vote. Staff references are likewise assembled from collinear
  horizontal fragments, so an LSD break at a staff/barline crossing does not
  erase the intersection. Short assembled candidates are accepted only when
  they cross five regularly spaced staff lines; candidates without complete
  staff support must reach the 12% divider/reference scale. Candidates with an
  attached compact notehead-like bulge, short-stem topology, or discontinuous
  support are rejected. Accepted evidence must also be grouped or
  system-spanning across multiple y regions on both sides of the page.
  Fit and held-out vertical structures retain their staff-system identities
  through correction. Held-out evidence must preserve distinct, vertically
  distributed systems on each side; several barlines from one staff system
  cannot validate a page-wide warp. Braces
  are curved, and page borders/frame edges may describe cropping rather than
  page geometry. Visually verify that none dominates the accepted model.
  Line width and attached-blob topology use the same smooth, locally estimated
  paper background segmentation as ink/clipping checks, not a fixed grayscale
  cutoff. Yellow, gray, or uneven paper therefore cannot inflate a stem or
  barline's measured width merely because its background is below 245.
- Use many long staff/baseline and barline/system-divider candidates with
  robust outlier rejection; never trust a single text line. Collinear
  horizontal fragments are clustered into one structural vote only when their
  center intercept, angle, and x support are physically compatible. Opposing
  angle modes or spatially disconnected fragments in the same center band stay
  separate so conflict detection can see them. A transform requires at least
  70% count and length-weighted consensus across independent clusters.
  Consensus structures are split by alternating occupied y bands:
  fit and holdout must each contain at least five independent structures in at
  least two bands. Fit/holdout identities are assigned once before correction;
  candidate validation tracks those same structures by transformed position,
  x support, and length. Missing bands fail closed and can never renumber the
  remaining bands or swap their roles. The fit never falls back to all
  structures. Missing or insufficient holdout evidence is low confidence,
  leaves the page unchanged, and cannot validate a correction. Competing coherent modes are left
  unchanged and marked `review_required`; an adequate holdout must
  independently improve after correction.
- Pages without enough evidence should remain unchanged and be reported.
- Low-confidence horizontal measurements never create a transform candidate,
  regardless of the angle value returned with that insufficient sample set.
- Horizontal and vertical evidence remain independent. Re-measure the output;
  a transform that fails its residual gate or worsens the other measured axis
  must be reverted.
- Vertical correction requires at least 0.20 degrees of left-to-right
  differential. Similar left/right angles are treated as common global tilt,
  not convergence. Global tilt is governed by independent horizontal
  orientation evidence; unresolved common vertical tilt is marked
  `review_required` rather than sheared away.
- Vertical evidence is normalized to a center-crossing line model and clustered
  by physical intercept before fitting, without splitting one structure merely
  because its overlapping fragments have small angle disagreements. Y-disjoint
  barlines in separate systems remain independent votes, while fragments from
  one staff crossing cannot multiply their influence.
  Confidence requires balanced independent structures with meaningful spread
  and multiple occupied x bands on both sides of the page. Accepted convergence
  fits require at least 70% count and length-weighted inlier consensus with low
  angular MAD. Bilateral counts and balance, per-side x spread, occupied x
  bands, and independent system/y-band distribution are all checked again on
  the consensus-filtered structures and after every later outlier-filtering
  stage before any fit or transform is allowed. Conflict analysis runs on all
  independent physical structures before any same-x filtering could discard a
  minority mode, and is repeated after each filtering stage.
  Competing coherent vertical-angle or convergence modes are
  reported as `conflicting_evidence`, require review, and produce no vertical
  transform rather than being averaged. The meaningful-minority threshold is
  inclusive: a coherent alternative with at least 30% of both independent
  structure count and length weight conflicts, including an exact 70/30 split;
  an alternative below either threshold does not.
  Accepted convergence
  correction must also materially improve a distinct held-out left/right
  structural estimator. Alternating x bands are reserved from the LSD fit for
  this validation. The held-out estimator applies the same barline/divider
  length, staff-intersection, grouping, and bilateral system-span criteria as
  the fit. Its combined evidence and each side independently repeat the same
  consensus, coherent-minority conflict, balance, and distribution analysis;
  holdout mode assignments remain tied to their coherent modes rather than
  switching to whichever mode improves after correction. Every material
  mode's residual must not worsen beyond tolerance; otherwise the evidence is
  conflicting and no transform is attempted. An even multimode holdout cannot
  select one mode by its median. Raw short
  vertical gradients cannot validate a correction. Fitted
  corridors are excluded using observed thickness and position uncertainty, so
  fitted fragments cannot validate themselves.
- Fixed-canvas transforms are rejected when transformed-out source foreground
  exceeds 0.0005 of source foreground (0.05%) or measured candidate foreground
  loss exceeds 0.005 (0.5%). The same two limits apply to original-to-final
  cumulative clipping validation. Paper tone and isolated scan specks do not
  count as foreground, but thin connected text and staff structures do.
- The runner requires mise 2026.8.8 and provisions exact Python 3.12.10 in a
  unique, isolated mise session. It creates a fresh virtual environment,
  installs only the exact hash-locked binary packages in `requirements.lock`,
  validates their versions, and deletes the complete session on exit.
- AzureAuth 0.9.5 obtains the Lucia feed token only inside a `-NoProfile`
  dependency-install child. The token is held only in that child's memory and
  `PIP_INDEX_URL`, is never placed on a command line, logged, or persisted, and
  is cleared before the child exits. Missing prerequisites, version mismatches,
  failed installs, and failed dependency checks stop the run.
- Network access to Lucia_PrivatePackages is required on every invocation.
