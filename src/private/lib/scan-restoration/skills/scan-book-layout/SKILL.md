---
name: scan-book-layout
description: >-
    Use this skill for the production-layout stage of a restored scan batch:
    remove scanner-edge artifacts without deleting real page content, preserve
    braces and staff endings, normalize every page to a common canvas without
    aspect-ratio distortion, center content, and write a separate output directory.
    Trigger for dirty borders, black scanner edges, inconsistent page dimensions,
    page-size normalization, centering, print preparation, or final image-sequence
    assembly.
compatibility: Designed for GitHub Copilot CLI on Windows. Every invocation requires mise, AzureAuth access to Lucia_PrivatePackages, and network access.
---

# Scan book layout

Treat edge cleanup and canvas normalization as content-aware production tasks,
not fixed-width cropping.

Run all commands below from this skill directory.
The script reads one directory and writes another; the two directory trees must
be separate and non-nested. The output directory must not exist. The command fails
rather than overwrite any prior directory, report, or file.

## Workflow

1. Inspect left and right page edges separately. A left-side music brace is content;
   a long isolated right-side scanner boundary may be contamination. The script
   never cleans the left edge automatically. Right-edge candidates are changed
   only when they are an isolated connected component with sustained physical
   border contact. Thin 1-2px flush-edge components are never deleted.
   Rule-like 3px+ flush-edge components are also preserved because genuine
   marginal and page-frame rules may contain ordinary scan noise; tonal
   variation and slight one-pixel boundary irregularity are never artifact
   evidence. A scanner-boundary cue requires repeated, regular boundary
   recessions at least two pixels deep, which is very unlikely for a real rule,
   and cannot override rule-like elongated geometry. Deletion requires combined
   scanner-strip-specific width, solidness, clean paper-background separation,
   slight boundary irregularity, and attachment-absence cues.
   Foreground is segmented relative to a local paper-background estimate.
   Weak local contrast is retained only when connected to strong local contrast,
   so broad dark or photographic content attached inward remains represented
   without classifying an absolutely dark paper band as ink. Off-white, dark, or
   yellow paper is not mistaken for ink. Wider
   borders require
   large-height (at least 75%), dark solid-strip, low-variance evidence and no
   branches or content extending inward.
   Deletion requires contact with the actual outermost column;
   a component ending even one pixel short is never deletion-eligible and is
   preserved for review. A width-scaled near-edge margin never makes a component
   deletion-eligible. Components with meaningful horizontal branches, short or
   long staff endings, broad page content, or ambiguous attachments are always
   preserved for review, regardless of geometric resemblance.
   Separated marginal rules are preserved.
   Cleanup erases only the detected component and does not dilate the mask into
   neighboring content.
2. Preview selected and outlier pages:

    ```powershell
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
      -File .\scripts\run.ps1 normalize_book.py INPUT_DIR PREVIEW_DIR `
      --width 2638 --height 4234 --pages 1 20 40
    ```

    `--pages` uses the last numeric run in each filename stem. Every requested
    number must identify exactly one input file; an absent or ambiguous number
    fails the whole command. For example,
    `scan_002_final.png` is page 2. Files are processed in natural order, so page
    2 precedes page 10.

3. Confirm that braces, clefs, barlines, page numbers, and marginal text remain.
   Review both accepted and rejected candidates in the JSON report. Every dark
   component intersecting the reported right-edge inspection band is listed,
   including internal and near-edge artifacts that cannot be deleted. Each entry
   has a `status`, decision reasons, border gap, and physical-contact fields, so
   report-driven QC does not silently omit preserved edge-band ink.
   `--edge-confidence` is an inclusive threshold: an otherwise eligible isolated
   candidate is removed only when `confidence >= threshold`. Raise it above the
   conservative `0.9` default when genuine edge content is ambiguous; never lower
   it merely to force cleanup. Confidence is continuous evidence strength derived
   from height, sustained border contact, solidity, darkness, and profile
   uniformity, so changing the threshold can change eligible-candidate decisions.
   Structural safeguards remain absolute: thin or rule-like rules, branches,
   clipped edge-band components, and ambiguous attachments stay preserved even
   at a zero threshold. Use `--no-edge-cleanup` to explicitly disable all
   changes while retaining candidate analysis in the report. The legacy
   `--cleanup-confidence` spelling remains an alias.
4. Process the complete batch with an explicit canvas or auto-derived canvas:

    ```powershell
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
      -File .\scripts\run.ps1 normalize_book.py INPUT_DIR OUTPUT_DIR --auto-canvas
    ```

    Use a different, empty directory from the preview run. For print or an
    established series, choose the required pixel dimensions with
    `--width` and `--height`. Otherwise, `--auto-canvas` uses the maximum width and
    maximum height among the selected source pages. Every page is scaled
    proportionally to fit and padded with white; its aspect ratio is never changed.
    Both modes enforce fixed safety limits before any resize or canvas allocation:
    at most 30,000 pixels on either axis, 80,000,000 total canvas pixels, and a
    512 MiB conservative, phase-aware working-memory estimate covering up to
    128 MiB of encoded input, both the decoder's full-stream/internal encoded
    buffers, the source,
    cleaned copy, resized raster, canvas, edge/interpolation workspaces, and PNG
    encoding buffers for the page's actual 8- or 16-bit depth. Buffers from prior
    phases are released before resizing and encoding. Encoded-byte size, frame
    count, displayed dimensions, decoded pixel count, source decode footprint,
    and working-memory estimates are validated from container headers before any
    full raster decode or copy. Pillow decompression-bomb warnings are fatal.
    Auto-canvas validates the
    cross-product of the maximum
    source width and maximum source height, even when those maxima come from
    different pages. These limits have no command-line override; split or
    downsample an exceptional batch instead.

5. Run `/scan-book-quality-control` and inspect every reported edge outlier. If
   that skill is unavailable, use the current run's JSON report as the manifest;
   do not enumerate an output directory that may contain unrelated files. Open
   every `pages[].output` PNG at 100% zoom, compare every accepted candidate with
   its source, inspect the first/last and smallest/largest pages, and verify the
   declared files and dimensions with:

    ```powershell
    Add-Type -AssemblyName System.Drawing
    $reportPath = Resolve-Path OUTPUT_DIR\cleanup.json
    $manifest = Get-Content -Raw $reportPath | ConvertFrom-Json
    $outputRoot = Resolve-Path (Join-Path (Split-Path -Parent $reportPath.Path) $manifest.output_root_from_report)
    $manifest.pages | ForEach-Object {
      $file = Join-Path $outputRoot.Path $_.output
      if (-not (Test-Path -LiteralPath $file)) { throw "Missing declared output: $file" }
      $i=[Drawing.Image]::FromFile($file)
      try { "{0}: {1}x{2}" -f $_.output,$i.Width,$i.Height } finally { $i.Dispose() }
    }
    ```

## Output behavior

- Output is always grayscale PNG named from the input stem, regardless of source
  format. Accepted 16-bit integer grayscale sources remain 16-bit through decode,
  edge analysis, resizing, white padding, and PNG encoding; 8-bit sources remain
  8-bit. TIFF PhotometricInterpretation and FillOrder are honored for unsigned-integer
  1-, 8-, and 16-bit grayscale; SampleFormat is validated at every bit depth, and
  signed or floating-point TIFF samples are rejected before conversion.
  WhiteIsZero samples are converted to the normal black-zero working
  representation exactly once before edge analysis. Valid unsigned 16-bit
  FillOrder 2 TIFFs use the pinned tifffile/imagecodecs fallback when Pillow
  cannot decode them; fallback metadata, raster size, depth, sample format,
  channel count, photometric, fill order, and orientation are validated before
  preserving the native 16-bit samples. EXIF orientation is
  applied exactly once before edge analysis, so
  "right edge" always means the displayed page's physical right edge. Supported
  PNG/WebP alpha and PNG transparency are composited onto white before grayscale
  conversion and cleanup, at the source's supported 8- or 16-bit depth. Pillow
  modes that cannot preserve their alpha or depth (including multichannel 16-bit
  PNG and high-depth color TIFF) are rejected rather than silently
  down-converted. Compression is lossless,
  but source metadata (including DPI tags) is not carried forward. Choosing or
  assigning physical DPI is out of scope.
- Centering places the complete proportionally scaled page image on the target
  canvas. It does not crop margins or recenter a guessed ink bounding box, so
  genuine extreme-edge content remains safe.
- No fixed white strips are painted on any edge. Accepted cleanup changes only
  the detected right-edge component; rejected candidates remain untouched and
  are reported with preservation/removal reasons and connection metrics.
- Every encoded image must contain exactly one Pillow-detected frame. Unknown
  frame counts and multi-frame files, including APNG and MPO, fail closed before
  anything is published. APNG classification uses decoder frame and typed
  animation metadata semantics; ordinary PNG text fields named `loop` or
  `default_image` do not make a static single-frame PNG animated.
- Every top-level entry is explicitly inventoried. Directories and non-image files
  are classified separately. Files are identified by Pillow's actual decoded
  format, frame count, and format metadata through a restricted format allowlist,
  not by raw container substring scanning or suffix alone.
  Supported single-frame JPEG (including `.jpe` and `.jfif` aliases), PNG, TIFF,
  and WebP content is processed even with an unusual suffix. Unknown-suffix files
  within the encoded-byte limit receive bounded Pillow header parsing across the
  complete file, so late JPEG metadata cannot hide a supported image from the
  inventory. Files too large to inspect within that bound fail closed rather than
  being classified as non-images. Recognized formats
  that are not supported (for example BMP, GIF, HEIC, AVIF, JPEG 2000, SVG, APNG,
  MPO, or DCX) fail the whole batch with detected format and frame count listed,
  including when mixed with supported pages. Top-level files using any known image
  extension outside the supported extension allowlist also fail explicitly, even
  when their content cannot be decoded for inventory.
- Duplicate input stems fail even if their extensions differ. Existing content
  in the output directory is not allowed because the directory must not exist.
- PNGs and `cleanup.json` are built together in a sibling staging directory and
  published with one atomic whole-directory rename only after the complete run
  succeeds. Pages are decoded, normalized, and written to staging one at a time;
  the batch is never retained in memory. Auto-canvas performs a one-page-at-a-time
  dimension pass first. The report is always the fixed internal
  `OUTPUT_DIR\cleanup.json`.
  Page paths are relative to the output root and `output_root_from_report` is `.`.
  A failed run removes staging data and leaves no partial deliverable.
- Connected-component labels and masks are allocated only for the right-edge
  inspection band. Any component reaching the band's inner boundary is treated
  as potentially attached page content and preserved, avoiding a full-page mask
  per candidate while remaining conservative.

## Gotchas

- Do not erase both sides with the same vertical-line rule.
- Do not treat long braces or staff endings as scanner dirt.
- Internal or curved artifacts that do not confidently contact the physical
  right edge are reported but intentionally left for review.
- Normalize by proportional scaling and white padding. Never stretch pages independently in X and Y.
- Keep filenames and natural page order stable.
- The runner creates and removes a unique ephemeral runtime for every invocation.
  Invoke the maintainable PowerShell entry point with `powershell.exe -NoProfile`
  as shown above. It isolates mise configuration and state, pins Python 3.12.10,
  and installs only the exact wheel versions and SHA-256 hashes in
  `scripts\requirements.lock`.
- AzureAuth 0.9.5 is used from its standard `%LOCALAPPDATA%` installation to get
  a Lucia package token. The token is passed to pip only through the temporary
  process environment, is never written or placed on a command line, and is
  cleared immediately after dependency installation. The runtime and mise
  session directories are deleted in `finally` cleanup.
- Network access is required on every invocation for authentication, Python
  provisioning when absent, and installation of the ephemeral pinned runtime
  dependencies. Unsupported or extreme images still fail closed in
  `normalize_book.py`; output images and `cleanup.json` remain an atomic
  whole-directory publication.
