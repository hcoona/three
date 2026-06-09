# Markdown-Aware Translation High-Level Design

Status: **Ready for implementation**

## Audience and Intent

This document is written for AI agents acting as senior software engineers. It
translates the frozen Markdown-aware translation requirements into a high-level
design for the existing `document-translator` C# CLI.

The design preserves the baseline whole-document translation path for existing
non-Markdown formats. It adds a separate Markdown-aware path that parses
Markdown, extracts approved prose text nodes, translates those extracted
segments with Azure Translator Text Translation REST API v3.0, and writes a
validated Markdown output without normalizing protected bytes.

## Design Goals

1. Preserve existing behavior for non-Markdown inputs.
2. Automatically route `.md` and `.markdown` inputs to Markdown-aware translation
   in `auto` mode.
3. Allow users to force either Markdown-aware translation or legacy
   whole-document translation.
4. Preserve protected Markdown bytes by editing only approved source text spans.
5. Fail closed before writing output when parsing, segmentation, translation,
   placeholder restoration, or structural validation cannot prove safety.
6. Keep the implementation testable with deterministic fake translators and no
   live Azure dependency in normal CI.

## Project Shape

Add Markdown-aware implementation files beside the existing CLI code:

```text
src/private/app/document-translator-cli/
  MarkdownMode.cs
  TranslationRoute.cs
  MarkdownTranslationCommand.cs
  MarkdownDocumentParser.cs
  MarkdownUnsupportedConstructDetector.cs
  MarkdownSegmentExtractor.cs
  MarkdownTokenProtector.cs
  MarkdownSourcePatcher.cs
  MarkdownOutputValidator.cs
  ITextSegmentTranslator.cs
  AzureTextSegmentTranslator.cs
  AzureTextTranslationClient.cs
  AzureTextTranslationJsonContext.cs
```

Keep the existing whole-document path and its `IDocumentTranslator` abstraction
for non-Markdown translation and explicit Markdown legacy mode. The Markdown
path should have a separate `ITextSegmentTranslator` abstraction because it
translates text segments, not document streams.

## Dependencies

Add one required application package reference:

- `Markdig`

Use `System.Text.Json` from the .NET shared framework. Do not add a
`System.Text.Json` package reference unless a future implementation explicitly
requires a NuGet-version override. Add `Markdig` to `Directory.Packages.props`
in alphabetical order when implementation begins.

Use `HttpClient` directly for Azure Translator Text Translation REST API v3.0.
Do not introduce a host, dependency injection container, or `IHttpClientFactory`
unless a later design explicitly requires one. The CLI performs one bounded
operation per process, so an owned `HttpClient` per invocation is acceptable.

Continue using `Azure.Identity` for Entra ID authentication and the existing
root custom-domain endpoint validation.

## Command-Line Contract

Extend the existing `translate` command with one option:

```bash
document-translator translate \
  --input <path> \
  --output <path> \
  --target-language <language-code> \
  [--auth-mode <api-key|entra-id>] \
  [--endpoint <uri>] \
  [--key <api-key>] \
  [--force] \
  [--markdown-mode <auto|aware|legacy>]
```

Environment fallback:

| Option            | Environment variable                | Default |
| ----------------- | ----------------------------------- | ------- |
| `--markdown-mode` | `DOCUMENT_TRANSLATOR_MARKDOWN_MODE` | `auto`  |

Resolution rules:

1. Command-line option wins over the environment variable.
2. Environment variable wins over the default.
3. Values are trimmed, normalized case-insensitively, and validated as `auto`,
   `aware`, or `legacy`.
4. Invalid values are validation errors before translation.

## Routing Model

Introduce a `TranslationRoute` value determined after option resolution and
common path validation. Common path validation includes required input path,
input file existence, 10 MB file size, required output path, output directory
shape, same-path rejection, and overwrite validation. It does not include the
legacy extension allowlist. The extension allowlist runs only after route
selection so `.md` and `.markdown` can enter either the Markdown-aware route or
the explicit legacy `text/plain` route.

| Markdown mode | Input extension                        | Route                                         |
| ------------- | -------------------------------------- | --------------------------------------------- |
| `auto`        | `.md` or `.markdown`                   | Markdown-aware text-segment route             |
| `auto`        | any other supported baseline extension | Legacy whole-document route                   |
| `aware`       | `.md` or `.markdown`                   | Markdown-aware text-segment route             |
| `aware`       | any other extension                    | Validation error                              |
| `legacy`      | `.md` or `.markdown`                   | Legacy whole-document route with `text/plain` |
| `legacy`      | any supported baseline extension       | Legacy whole-document route                   |

Extension matching uses the final path segment and ordinal case-insensitive
comparison. The Markdown legacy route bypasses the baseline extension allowlist
only for `.md` and `.markdown`; it preserves the original input file name and
uses content type `text/plain`.

## Option Model Changes

Extend the raw and validated option models:

```csharp
internal enum MarkdownMode
{
    Auto,
    Aware,
    Legacy,
}

internal enum TranslationRoute
{
    LegacyDocument,
    MarkdownAware,
}
```

Add these validated properties:

- `MarkdownMode MarkdownMode`
- `TranslationRoute TranslationRoute`
- `bool IsMarkdownExtension`

Keep `LegacyDocumentContentType` populated only for the legacy document route.
The Markdown-aware route does not carry an Azure document content type because
its text-segment backend does not use that Azure document contract.

## Runtime Flow

The top-level command orchestration becomes:

1. Parse command-line options.
2. Resolve options and environment fallback.
3. Validate common inputs without applying the legacy extension allowlist:
    - input path,
    - output path,
    - target language,
    - endpoint,
    - authentication mode,
    - credentials,
    - 10 MB input limit,
    - same-path and overwrite rules.
4. Resolve Markdown mode and route.
5. Validate route-specific extension and content-type rules:
    - `LegacyDocument` with a baseline extension uses the existing content-type
      mapping.
    - `LegacyDocument` with a Markdown extension is valid only when
      `--markdown-mode legacy` or `DOCUMENT_TRANSLATOR_MARKDOWN_MODE=legacy` is
      resolved; it uses `text/plain`.
    - `MarkdownAware` requires `.md` or `.markdown`.
6. Preflight output path using the existing same-directory temporary file probe.
7. Open the input file.
8. Dispatch by route:
    - `LegacyDocument`: call the existing `IDocumentTranslator` path.
    - `MarkdownAware`: call `MarkdownTranslationCommand`.
9. Write translated bytes through the existing atomic output writer.
10. Print the existing short success message.

The Markdown-aware route must return translated output as `BinaryData` or an
equivalent byte container so the existing atomic writer can remain the final
write seam.

## Markdown-Aware Pipeline

`MarkdownTranslationCommand` coordinates the Markdown route:

1. Read the input bytes.
2. Decode as UTF-8 with strict error detection and remember:
    - UTF-8 byte order mark presence,
    - line-ending bytes by source range,
    - final newline presence.
3. Reject JSON front matter before Markdown parsing when the first byte after an
   optional UTF-8 byte order mark is `{`.
4. Run a preliminary parse and source-range collection pass with the frozen
   Markdig pipeline. This pass collects reliable source ranges for later
   detection, extraction, patching, and validation.
5. Build the exact unsupported-detection exclusion ranges for fenced code,
   indented code, inline code, YAML front matter, raw HTML blocks, HTML comments,
   and early brace-like machine tokens. Early brace-like machine tokens are only
   template variables, replacement fields, and braced shell variable references
   inside otherwise translatable parser text nodes. These are the only protected
   ranges that suppress unsupported-construct detection.
6. Separately collect candidate protected ranges for link and image destinations,
   link and image titles, reference labels, reference definitions, autolinks,
   URL literals, email literals, URI fragments, Markdown structural syntax,
   escaped Markdown delimiters, inline HTML tags, and text enclosed by paired
   inline raw HTML markup. These candidate ranges are for extraction,
   reconstruction validation, and later protection only; they must not suppress
   unsupported-construct detection. Inline HTML tags and paired inline raw HTML
   enclosures become protected only after MDX JSX, MDX expression, and MDX
   comment checks prove the full tag text, attributes, and enclosed text are not
   unsupported MDX-like syntax. Do not protect inline link display text or image
   alt text when they are emitted as approved parser text nodes.
7. Run unsupported construct detection outside the exact detector exclusion
   ranges.
8. Parse with the frozen Markdig-compatible pipeline for extraction. The parse
   result may reuse the preliminary parse only if no mutation occurred and all
   required precise source spans are available.
9. Extract approved text-node segments and source spans.
10. Apply inline machine-token protection and placeholder replacement.
11. Validate per-segment and per-batch 50,000 Unicode scalar value limits.
12. If no segments were extracted, skip `ITextSegmentTranslator`, keep the
    original decoded text as the patched text for validation, and return the
    original input bytes through the atomic output writer after validation
    succeeds. Do not re-encode protected-only input on the zero-segment path.
13. Translate non-empty segment batches with `ITextSegmentTranslator`.
14. Restore placeholders and validate placeholder integrity.
15. Patch translated text into the original source text by source span.
16. Re-parse the patched Markdown with the same pipeline.
17. Validate structural invariants and protected bytes.
18. Encode the patched text back to UTF-8, preserving byte order mark and final
    newline presence.

If any step fails, the command returns a validation, service, file I/O, or
cancellation result according to the baseline exit-code taxonomy and writes no
final output.

## Source-Patching Strategy

Do not render Markdown from an AST. Full AST rendering would normalize whitespace,
line endings, table formatting, escapes, and other protected bytes.

Instead, use source spans:

1. Store the original UTF-8 bytes.
2. Decode to a UTF-8 string with strict validation.
3. Extract translatable spans as character ranges in the decoded string.
4. Sort spans by descending start offset.
5. Replace only those ranges with translated text.
6. Leave every byte outside translated ranges unchanged after re-encoding.

The design assumes translated prose may have different byte length. Therefore,
validation must compare protected content by stable source slices and semantic
locations, not by absolute post-patch offsets alone.

## Markdig Parser Pipeline

Use one central parser factory, such as `MarkdownParserFactory.CreateV1Pipeline`.
The pipeline must be reused for extraction and validation.

Required parser features:

- CommonMark-compatible core syntax.
- Pipe tables.
- Task lists.
- Strikethrough.
- Footnotes.
- YAML front matter.
- Raw HTML blocks, inline HTML, and HTML comments.

Do not enable broad extension bundles such as `UseAdvancedExtensions()` because
they can accidentally enable unsupported constructs. Add only the frozen
extensions explicitly.

All parser calls must enable precise source position tracking required by
extraction and validation, such as Markdig's precise source-location option. If
Markdig cannot produce a reliable source span for a non-empty text node inside an
approved translatable container, the file fails validation before translation.
The implementation must not silently skip that text node.

## Approved Text Containers

`MarkdownSegmentExtractor` may extract text nodes only from the frozen approved
containers:

1. heading visible text,
2. paragraph prose,
3. list item prose,
4. block quote prose,
5. table cell prose,
6. inline link display text and full reference link display text,
7. inline image alt text and full reference image alt text,
8. footnote body prose,
9. text inside emphasis, strong emphasis, or strikethrough.

The extractor must not use language-detection heuristics to discover additional
translatable regions. Shortcut and collapsed reference links are unsupported and
fail before translation because their visible text is also the reference label.

## Unsupported Construct Detection

Run `MarkdownUnsupportedConstructDetector` after the preliminary parse/source
range collection pass and before segment extraction.

Detection input:

- decoded UTF-8 text,
- excluded ranges for fenced code,
- excluded ranges for indented code,
- excluded ranges for inline code,
- excluded ranges for YAML front matter,
- excluded ranges for raw HTML,
- excluded ranges for HTML comments,
- excluded ranges for early brace-like machine-token matches only: template
  variables, replacement fields, and braced shell variable references inside
  otherwise translatable parser text nodes.

Do not exclude arbitrary inline HTML before unsupported construct detection.
Inline HTML that matches the frozen MDX JSX patterns, such as `<MyComponent />`,
must fail before translation. Inline HTML tags and paired inline raw HTML
enclosures are protected only after unsupported construct detection proves the
full tag text, attributes, and enclosed text are not unsupported MDX-like
constructs. If a paired inline raw HTML enclosure cannot be scanned safely, the
file fails closed before translation.

The detector applies exactly the frozen line-based and inline patterns from the
requirements. A match outside excluded ranges produces a validation error. The
detector must report the category and line number without echoing the matched
source text.

MDX comments are unsupported in v1. Detect inline MDX comment forms such as
`{/* comment */}` outside excluded ranges and fail before translation without
echoing the comment text.

Shortcut and collapsed reference links are unsupported even though they are not
MDX-like constructs. Detect them during link node inspection and fail before
translation.

## Machine Token Protection

`MarkdownTokenProtector` applies the frozen machine-token patterns to each
candidate text node before translation.

Rules:

1. Evaluate all frozen patterns.
2. Resolve overlaps by longest match, then earliest start offset.
3. Replace each protected span with a generated placeholder.
4. Store placeholder-to-original mappings per segment.
5. Validate that no generated placeholder already appears anywhere in the decoded
   source document.

Placeholder format:

```text
__DTCLI_PH_<segment-index>_<token-index>__
```

The placeholder format is fixed for v1 and intentionally non-natural-language.
The implementation must generate placeholders with document-scoped uniqueness,
keep the whole-document collision check, and keep all restoration invariants.

## Segment Model

Use a small immutable model for extracted segments:

```csharp
internal sealed record MarkdownTranslationSegment(
    int SegmentIndex,
    TextRange SourceRange,
    string OriginalText,
    string ProtectedText,
    IReadOnlyList<ProtectedToken> ProtectedTokens);
```

`TextRange` uses decoded-string offsets, not byte offsets. The source patcher
converts patched text back to bytes only after reconstruction validation.

Each segment must:

- belong to one approved Markdown container,
- stay inside one block or one table cell,
- exclude protected Markdown syntax,
- exclude escaped Markdown delimiters and their escape characters,
- contain no unsupported shortcut or collapsed reference link,
- contain no more than 50,000 Unicode scalar values after placeholder insertion.

Batching may group multiple protected segment strings as long as the batch total
does not exceed 50,000 Unicode scalar values.

## Text Translation Backend

`ITextSegmentTranslator` isolates tests from Azure:

```csharp
internal interface ITextSegmentTranslator
{
    ValueTask<IReadOnlyList<string>> TranslateAsync(
        TranslationOptions options,
        IReadOnlyList<TextSegmentTranslationRequest> segments,
        CancellationToken cancellationToken);
}
```

`AzureTextSegmentTranslator` uses `AzureTextTranslationClient`, an internal REST
client around `HttpClient`. The REST client must accept an injectable
`HttpMessageHandler` or `HttpClient` test seam and an injectable token provider
seam for Entra ID tests. Unit tests must be able to verify request URI, headers,
body, batching, and token usage without live Azure credentials or network calls.

`TextSegmentTranslationRequest` carries the segment index, protected segment
text, and placeholder map. The Azure REST client sends only the protected segment
text to the service, while fake translators and placeholder validation can use
the map to verify segment-level invariants.

Request construction:

1. Start from the validated root custom-domain endpoint.
2. Append `/translator/text/v3.0/translate`.
3. Add `api-version=3.0` and `to=<target-language>`.
4. Send UTF-8 JSON body: `[{ "Text": "<segment>" }]`.
5. Keep each request at or below 100 text array elements and 50,000 Unicode
   scalar values across all segment texts.
6. Set `Content-Type: application/json; charset=utf-8`.
7. For API key mode, send `Ocp-Apim-Subscription-Key`.
8. For Entra ID mode, request
   `https://cognitiveservices.azure.com/.default` with the existing Azure
   Identity credential flow and send `Authorization: Bearer <token>`.

Response handling:

1. Non-success HTTP responses are service errors.
2. The response must contain exactly one translation result per input segment.
3. Each result must contain at least one translated text value.
4. Missing, extra, malformed, or empty result entries are service errors.
5. The translator abstraction returns only translated strings, never raw response
   JSON.

## Placeholder Restoration

After translation:

1. Verify every expected placeholder appears exactly once.
2. Verify no unexpected placeholder-shaped token appears.
3. Verify placeholders appear in the same relative order as the protected input
   segment.
4. Replace each placeholder with its original protected token text.
5. Fail validation if any placeholder is missing, duplicated, mutated, or
   illegally reordered.

The restoration step operates per segment. One corrupted segment fails the whole
file.

## Reconstruction Validation

`MarkdownOutputValidator` validates the patched Markdown before output bytes are
returned:

1. Parse the patched text with the same Markdig pipeline.
2. Compare source and output structural fingerprints for the full supported
   block and inline tree:
    - node type order,
    - node nesting and parent-child relationships,
    - source-supported opening and closing delimiter kinds,
    - inline delimiter placement for emphasis, strong emphasis, strikethrough,
      links, images, code spans, autolinks, and HTML,
    - block order,
    - block nesting,
    - heading levels,
    - list structure,
    - table row and column counts,
    - link and image destinations,
    - link and image titles,
    - reference labels, destinations, and titles,
    - autolinks, URL literals, email literals, and URI fragments,
    - code fence metadata,
    - front matter presence and byte content,
    - raw HTML byte content, including comments and attributes,
    - task markers,
    - footnote identifiers.
3. Recompute detector exclusion ranges from the patched parse and patched text,
   then run unsupported construct detection against the patched output outside
   those exact output-side exclusion ranges. This catches translated prose that
   introduces unsupported MDX import/export, MDX JSX, MDX expression, MDX
   comment, Markdown directive, custom admonition, TOML front matter, or JSON
   front matter syntax.
4. Compare protected source slices against their corresponding output slices.
5. Verify byte order mark presence and final newline presence.
6. Verify original line-ending bytes remain unchanged outside translated prose.

Validation failures are command validation errors unless caused by service, file
I/O, or cancellation failures.

## Encoding and Newline Handling

Markdown-aware v1 supports UTF-8 only.

Implementation rules:

1. Detect and preserve a UTF-8 byte order mark.
2. Decode with strict UTF-8 validation.
3. Reject invalid UTF-8 before translation.
4. Keep original line-ending bytes for structural delimiters and protected
   regions.
5. Preserve final newline presence exactly.
6. Do not normalize mixed line endings.

Translated prose may contain translator-provided line endings only inside
translated text ranges. Those line endings must not alter surrounding Markdown
structure.

## Error Mapping

Use the baseline exit-code taxonomy:

| Condition                                                                                                                             | Exit code |
| ------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| Successful translation                                                                                                                | `0`       |
| Command-line, validation, Markdown parse, unsupported construct, placeholder, reconstruction, or structural validation error          | `2`       |
| Azure Text Translation HTTP error, malformed service response, Azure Document Translation error, or Azure Identity credential failure | `3`       |
| File I/O or path error                                                                                                                | `4`       |
| Cancellation or unexpected error                                                                                                      | `1`       |

Diagnostics must be actionable and must not include source text, translated text,
secrets, access tokens, or API keys. Diagnostics may include file path, line
number, error category, and option name.

Implementation should use typed internal failures rather than generic exceptions
for expected Markdown and service outcomes:

- `MarkdownValidationException` or an equivalent result type maps to exit code
  `2`.
- `TextTranslationServiceException` or an equivalent result type maps to exit
  code `3`.
- Existing `RequestFailedException`, `AuthenticationFailedException`, and
  `CredentialUnavailableException` continue to map to exit code `3`.

Do not let expected Markdown validation failures fall through to the existing
unexpected-error catch path.

## Security Considerations

1. Do not log document content or translated segment content.
2. Do not persist extracted segments or translated segments outside memory.
3. Continue using same-directory temporary files only for final atomic output.
4. Do not print API keys or bearer tokens.
5. Prefer environment variables for secrets, as in the baseline CLI.
6. Treat unsupported syntax rejection as a safety feature, not a parser
   limitation to work around.

## Test Strategy

Use deterministic fake translators for normal CI.

Unit-level coverage:

- command-line parsing for `--markdown-mode`,
- environment fallback for `DOCUMENT_TRANSLATOR_MARKDOWN_MODE`,
- route selection,
- existing non-Markdown formats staying on the current Document Translation
  route,
- Markdown legacy override validator bypass, original file name preservation, and
  `text/plain` content type,
- extension matching,
- validation errors,
- unsupported construct detection,
- machine-token protection,
- placeholder restoration,
- source patching,
- structural fingerprint comparison,
- Azure Text Translation REST request construction,
- Azure Text Translation response parsing,
- injectable HTTP handler/client behavior,
- injectable Entra ID token provider behavior,
- same-path rejection,
- `--force` overwrite validation.

Golden-file coverage:

- headings,
- paragraphs,
- nested lists,
- block quotes,
- pipe tables,
- emphasis, strong emphasis, and strikethrough text,
- inline links,
- full reference links,
- images,
- link titles, image titles, and reference titles,
- autolinks, URL literals, email literals, and URI fragments,
- code fences,
- indented code blocks,
- inline code,
- escaped Markdown delimiters,
- YAML front matter,
- raw HTML,
- HTML comments,
- task lists,
- footnotes,
- each frozen machine-token class,
- machine-token overlap resolution,
- placeholder-bearing prose and protected spans,
- protected-only Markdown with zero extracted segments,
- UTF-8 byte order mark,
- LF, CRLF, and mixed line endings,
- final newline and no-final-newline.

Negative coverage:

- invalid UTF-8,
- malformed Markdown,
- unsupported MDX import/export,
- unsupported JSX element,
- unsupported MDX expression,
- unsupported spaced and non-identifier single-brace MDX expression,
- unsupported MDX comment,
- unsupported Markdown directive,
- unsupported custom admonition,
- unsupported TOML front matter,
- unsupported JSON front matter,
- leading-`{` JSON front matter rejected before preliminary Markdown parsing,
- shortcut reference link,
- collapsed reference link,
- single segment over 50,000 Unicode scalar values,
- batch over 50,000 Unicode scalar values,
- batch over 100 text array elements,
- placeholder drop, duplication, mutation, and reorder,
- fake translator injection of unsupported MDX expression, MDX comment, JSX,
  spaced or non-identifier single-brace MDX expression, directive, custom
  admonition, TOML front matter, JSON front matter, and import/export-like text,
- structural validation mismatch for table shape,
- structural validation mismatch for link destination or title,
- structural validation mismatch for image destination or title,
- structural validation mismatch for reference label, destination, or title,
- structural validation mismatch for block order or nesting,
- structural validation mismatch for code fences,
- structural validation mismatch for front matter,
- structural validation mismatch for raw HTML or HTML comments,
- structural validation mismatch for task markers,
- structural validation mismatch for footnote identifiers,
- Azure service error,
- malformed Azure response,
- atomic output success,
- validation failure with no final output,
- service failure with no final output,
- file I/O failure with no final output,
- cancellation with no final output.

The fake text translator must follow the frozen requirements contract:

1. For segment index `n`, return `TRANSLATED[n]` followed by one ASCII space and
   the original protected segment text.
2. Preserve placeholders in order for success fixtures.
3. Provide variants that drop, duplicate, mutate, and reorder placeholders for
   failure fixtures.

Successful golden-file tests compare the full output byte-for-byte. Negative
tests assert stable error categories, not full source or translated content.

## Implementation Notes

1. Keep Markdown parsing, extraction, translation, patching, and validation as
   separate internal components. This keeps tests narrow and makes safety
   invariants explicit.
2. Avoid a large application host. The CLI has a small composition root and one
   command.
3. Add Markdig through Central Package Management.
4. Reuse existing endpoint validation and authentication mode resolution.
5. Do not change the existing success message or baseline non-Markdown behavior.
6. Keep live Azure tests out of normal CI. If live smoke tests are added later,
   require an explicit opt-in environment variable and exclude them by default.

## Requirement Traceability

| Requirement area                      | Design element                                                  |
| ------------------------------------- | --------------------------------------------------------------- |
| Automatic `.md` / `.markdown` routing | Routing model and option model changes                          |
| Explicit legacy override              | `MarkdownMode.Legacy` and legacy route behavior                 |
| UTF-8 only                            | Encoding and newline handling                                   |
| Frozen parser scope                   | Markdig parser pipeline                                         |
| Unsupported syntax fail-closed        | Unsupported construct detector                                  |
| Protected machine tokens              | Machine token protection                                        |
| Segment translation backend           | `ITextSegmentTranslator` and Azure Text Translation REST client |
| Placeholder safety                    | Placeholder restoration                                         |
| Byte preservation                     | Source-patching strategy and reconstruction validation          |
| All-or-nothing output                 | Runtime flow and existing atomic output writer                  |
| Deterministic testing                 | Test strategy and fake translator contract                      |
