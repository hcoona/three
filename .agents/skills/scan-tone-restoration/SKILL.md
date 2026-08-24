---
name: scan-tone-restoration
description: Use this skill to remove yellow or uneven paper tone from scanned book pages while preserving fine text, anti-aliased glyph edges, music staff lines, accidentals, and note details. Trigger for whitening paper, reducing printing ink, background normalization, contrast restoration, faded text, or preparing scans for clear grayscale printing. Do not use it as a substitute for geometric correction.
compatibility: Designed for GitHub Copilot CLI on Windows. Requires trusted mise and network access to PyPI on every run.
---

# Scan tone restoration

Restore paper and ink conservatively. Prefer continuous-tone grayscale over
aggressive binary thresholding unless the user explicitly requests bilevel output.

## Workflow

Run all commands below from this skill directory. Use the single PowerShell
entrypoint with `-NoProfile`.

1. Prefer `/scan-batch-diagnostics` to identify representative pages and
   outliers. If that companion skill is unavailable, select at least one page
   near the beginning, middle, and end, plus visible outliers such as unusually
   yellow, faint, dark, dense, illustrated, or music-heavy pages.
2. Run a preview on selected pages:

    ```powershell
    powershell.exe -NoProfile -File ".\scripts\run.ps1" "restore_tone.py" "INPUT_DIR" "PREVIEW_DIR" "--pages" "1" "10" "50"
    ```

    `--pages` values come from the digits in the final underscore-delimited
    filename segment: `scan_001.png` is page 1 and `book_010_recto.tif` has no
    page number. Values must be positive and unique. Every requested number must
    identify exactly one input; missing or ambiguous numbers fail the run.
    Inputs are processed in deterministic natural filename order (`page_2`
    before `page_10`), independently of page selection.

3. Inspect small text, anti-aliased edges, staff lines, accidentals, dots, and
   faint print. Iterate with conservative tuning options. Record the exact
   approved tuning options; the full-batch command does not inherit preview
   options:

    ```powershell
    powershell.exe -NoProfile -File ".\scripts\run.ps1" `
      "restore_tone.py" "INPUT_DIR" "PREVIEW_DIR_2" `
      "--pages" "1" "10" "50" `
      "--paper-level" "244" "--whiten-start" "188" "--whiten-width" "62" "--white-clip" "253"
    ```

    Lower `--paper-level` or raise `--whiten-start`/`--white-clip` to retain more
    paper and faint detail. Raise `--whiten-width` for a gentler transition.
    `--background-scale` (default 34) and `--min-background-sigma` (default 24)
    control local background estimation. Smooth, boundary-connected
    low-frequency ramps, vignettes, and page shadows remain eligible as paper
    even when they are more than 40 levels below the bright paper reference.
    Residual and local-edge evidence separates internal text, rectangles, and
    illustration detail from that smooth shading, including content connected
    to a page edge through a shadow. A full-bleed or edge-connected dark region
    with a sharp boundary or substantial internal contrast is content, not
    shading. Bounded components, coherent faint edges, and locally textured
    illustration/photograph regions are excluded from that estimate and
    normalized against a conservative page-paper reference instead; change
    these options only when broad paper shading remains.
    Smooth bounded illustration gradients are protected when they vary
    meaningfully along either axis. This includes full-width illustration bands
    bounded vertically and full-height panels bounded horizontally. Paper-wide
    low-frequency ramps spanning both axes remain eligible for normalization.
    `--black-percentile`
    (default 0.35) and `--white-percentile`
    (default 88) select contrast endpoints. The advanced `--black-floor`,
    `--black-ceiling`, `--white-floor`, and `--min-tone-range` options constrain
    those endpoints. Run `restore_tone.py --help` through the entrypoint for all
    ranges and defaults.

4. After approving the preview, process the whole batch:

    ```powershell
    powershell.exe -NoProfile -File ".\scripts\run.ps1" `
      "restore_tone.py" "INPUT_DIR" "OUTPUT_DIR" `
      "--paper-level" "244" "--whiten-start" "188" "--whiten-width" "62" "--white-clip" "253"
    ```

    Explicitly repeat every exact option approved in preview (including
    advanced options not shown above). Omitting one restores its default and can
    make the full batch differ from the approved preview.

5. Input and output must be separate, non-nested directories. The output path
   must not exist, even as an empty directory. Any same-stem input collision
   (case-insensitive, across all supported inputs) fails the run.

## Output

Every accepted input image produces a same-stem PNG in the output directory.
Pages with a usable paper background are reported as `normalized` or
`foreground_protected`. If most of a page is illustration/filled foreground,
if a continuous-tone page has no reliable paper background, or if the dominant
upper-tone cluster does not behave like paper relative to the page foreground,
the page is reported as
`copied_unchanged` and its grayscale pixels are preserved rather than risking
content erasure. Bright photographs and illustrations are treated the same as
dark ones: broad multi-level texture, local variance, gradients, and changing
color/chroma are content even when they cross above and below the estimated
paper tone. Large continuous-tone pages are copied unchanged, while embedded
textured regions are excluded from paper estimation. Their protection mask is
refined at full resolution so surrounding paper is not restored with the
photograph; original tones are retained through the content core and blended
inward at the detected boundary rather than clipping highlights to white. Uniform
yellow or cream paper remains eligible for whitening. Broad, nearly uniform low-contrast regions whose separation
from paper is ambiguous are likewise copied unchanged for review, regardless
of how much of the page they cover. Coherent region boundaries and internal
structure also prevent a dominant pale illustration or other low-contrast
content from being inferred to be paper solely from coverage. Whitening is
weighted by continuous background confidence; protected and faint foreground
pixels retain their ordering and a minimum contrast from surrounding paper,
and only confidently classified background may be clipped to paper white.
Coherent bounded components remain protected even when they are only one or
two gray levels below paper; component extent, shape, and consistent boundary
contrast distinguish them from isolated near-paper noise.
This background-relative check keeps solid dark pages, dark covers with light
lettering, and low-key illustrations from being mistaken for yellow paper and
whitened while allowing uniformly underexposed paper with darker print.
Majority-dark pages are also copied when their dark
coverage, protected content, and tonal spread indicate a cover or
filled-content page rather than relying on a single protected-area cutoff.
Ordinary cream or yellow paper remains suitable
when its broadly covered upper tones are light enough.
Output is continuous-tone grayscale: supported 1-, 2-, 4-, and 8-bit sources
produce 8-bit PNGs, while 16-bit grayscale sources produce 16-bit grayscale
PNGs. The JSON summary reports both the encoded source bit depth and the output
bit depth. Unsigned grayscale TIFFs are accepted only at 1, 8, or 16 bits with
WhiteIsZero or BlackIsZero PhotometricInterpretation and FillOrder 1 or 2.
Their native samples are decoded with the pinned tifffile/imagecodecs fallback;
WhiteIsZero is inverted exactly once at native depth so both photometric
interpretations reach restoration with black-zero polarity. Unsupported TIFF
tag combinations fail closed. Pillow cannot
preserve 16-bit multichannel samples in its supported image modes, so 16-bit
color or alpha inputs are rejected rather than silently reduced to 8-bit.
Supported 8-bit PNG and WebP alpha is composited onto white as straight
(unassociated) alpha. TIFF images carrying alpha or extra samples are rejected
because associated versus unassociated alpha cannot be established reliably
enough to avoid applying alpha twice. Unsupported modes such as CMYK, LAB,
floating point, signed integer, and other high-depth/color modes are rejected.
Decoding retains source depth, and restoration uses floating-point values
without an intermediate 8-bit conversion. This preserves anti-aliasing, faint
print, and fine music details; it does not produce bilevel images.

Pillow is invoked with an explicit JPEG, PNG, TIFF, and WebP decoder allowlist;
renamed BMP, JPEG 2000, and every other parser are rejected. The encoded format
is identified from content, never from the filename suffix, and must contain
exactly one frame. Multi-page TIFF, animated PNG, and animated WebP are
rejected when the pinned Pillow codec detects those animations; frame-count
inspection failures are also rejected. Inventory performs header-only
classification, then each accepted file is read once into one immutable byte
buffer used for authoritative inspection and decoding. Format, frame, size,
and pixel budgets are revalidated from that buffer before pixel allocation.
`ImageOps.exif_transpose` applies EXIF orientation exactly once before
conversion to NumPy; OpenCV is not used for input decoding or orientation.
Dimensions in the JSON summary describe the visually oriented output.

Every top-level ordinary file is inventoried before page selection. Inventory
enforces the entry-count limit while enumerating, before retaining or sorting
the complete directory, then applies the aggregate-byte preflight and uses only
a stable bounded header read to dismiss non-image files. It does not fully read
or hash ignored files. Supported filename suffixes are `.jpe`, `.jfif`, `.jpg`,
`.jpeg`, `.png`, `.tif`, `.tiff`, and `.webp` (case-insensitive), while Pillow identifies the actual
encoded format.
Unsupported image formats, corrupt image-like files, and valid images with an
odd or unsupported suffix fail clearly instead of being silently omitted.
Modern and specialist image suffixes such as JXL and EXR, along with the common
image-like suffixes recognized by sibling scan skills, are candidates and
always fail rather than being skipped. PSD, SVG, and PDF candidates also fail,
including when renamed with a non-image suffix. Other recognized unsupported
image signatures fail. Only files whose bounded header is not recognized as an
image candidate and whose suffix is not image-like are ignored.

Default safety budgets reject an input above 268435456 encoded bytes, a page
above 100000000 pixels, more than 10000 selected pages, or more than
1000000000 selected pixels before pixel decoding/allocation. Inventory is
also limited to 20000 ordinary entries and 4294967296 aggregate bytes.
Per-page preflight conservatively accounts for the decoded image,
full-resolution float32 analysis planes, masks, labels, background-blur
kernels, and repeated full-resolution passes; its default limits are
8589934592 estimated working bytes and 500000000000 estimated work units.
The
`--max-encoded-bytes`, `--max-pixels-per-page`, `--max-page-count`, and
`--max-total-pixels`, `--max-inventory-entries`, `--max-inventory-bytes`,
`--max-working-bytes-per-page`, and `--max-work-units-per-page` options may
set lower site-specific limits; budget failures identify the exceeded limit.
Pillow's decompression-bomb threshold is set to the active per-page pixel
budget for every inspection and decode, so it does not impose a lower hidden
limit. Background estimation preserves the requested full-resolution Gaussian scale
by area-downsampling, blurring with a correspondingly scaled sigma, and
linearly upsampling when the direct kernel would exceed its dimension cap.
The same transform is applied independently to weighted pixels and validity
masks so protected faint content remains excluded. If a requested scale cannot
be represented within the cap, the page is copied unchanged rather than using
a silently truncated blur. Full-resolution morphology iterations remain capped
by both page geometry and a fixed ceiling, preventing pathological aspect
ratios from creating unbounded loops without changing ordinary page behavior.

Processing is streaming: only one decoded input and its working arrays are
held at a time, avoiding batch-sized memory growth. Pages are safely written
to a uniquely named staging directory beside the requested output. Only after
all pages succeed is that whole directory atomically renamed to the previously
nonexistent output path. Decode, frame-count, processing, write, and
out-of-memory failures remove staging and leave no output directory.

The final stdout line is a JSON summary with this schema:

```json
{
    "processed": 1,
    "output": "C:\\absolute\\output",
    "output_format": "PNG",
    "color_space": "grayscale",
    "pages": [
        {
            "input": "page_001.tif",
            "output": "page_001.png",
            "source_bit_depth": 16,
            "output_bit_depth": 16,
            "width": 2400,
            "height": 3200,
            "status": "foreground_protected",
            "reason": "large_or_textured_foreground_excluded_from_background_estimation",
            "protected_fraction": 0.1842
        }
    ]
}
```

## Gotchas

- Yellow paper is not the same as low contrast. Normalize local background before stretching contrast.
- Dark content is not yellow paper. A dark cover, low-key illustration, or
  uniformly dark page is unsuitable for paper whitening and is copied unchanged.
- Do not choose parameters from a cover or blank page and apply them blindly to dense pages.
- Hard thresholding can erase thin staff lines and punctuation.
- Whitening should reduce toner use without turning weak ink into white.
- Preserve grayscale antialiasing in the archival/working output.
- Keep the pinned mise Python 3.12.10 and PyPI dependency bootstrap in
  `scripts\run.ps1`.
- Invoke the skill only through `scripts\run.ps1` with `-NoProfile`, as shown
  above. The practical trust boundary is the normal Windows user/process
  isolation plus the installed PowerShell, mise, and Python.
- Every invocation creates a uniquely named, previously nonexistent runtime
  under `scripts`, installs hash-locked binary wheels without a pip cache,
  validates NumPy, OpenCV, and Pillow versions, providers, and origins, and
  deletes the runtime in `finally`. A runtime left by any earlier invocation
  is never executed.
- The runner uses Python isolated mode, isolated pip configuration, pinned
  versions, an isolated mise configuration, and module-path checks so inherited
  Python or pip configuration cannot supply runtime dependencies. It
  temporarily clears `PIP_*` and `PYTHON*` variables, sets
  `PIP_CONFIG_FILE=nul`, uses the PyPI source declared in
  `requirements.lock`, and restores the environment afterward.
