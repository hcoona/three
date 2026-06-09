# Markdown-Aware Translation Implementation Plan

Status: **Ready for staged implementation**

## Audience and Intent

This document is written for AI agents acting as senior software engineers. It
turns the frozen Markdown-aware translation requirements and the high-level
design into an implementation workflow for the existing
`document-translator` C# CLI.

Do not reinterpret the requirements while implementing this plan. The v1 feature
adds a Markdown-aware route for `.md` and `.markdown` files, keeps the baseline
whole-document route for existing formats, and fails closed whenever Markdown
structure preservation cannot be proven.

## Implementation Principles

1. Preserve baseline behavior for every non-Markdown input unless the frozen
   requirements explicitly say otherwise.
2. Keep Markdown-aware translation separate from the existing
   `IDocumentTranslator` whole-document backend.
3. Validate route, syntax, source ranges, placeholders, reconstruction, and
   structural invariants before writing final output.
4. Prefer small internal components with deterministic fake test seams over a
   large application host or broad dependency injection container.
5. Do not print, log, persist, or expose full source document content,
   translated document content, API keys, or bearer tokens.
6. Treat unsupported Markdown-like constructs as safety failures, not as
   best-effort pass-through cases.
7. Keep live Azure tests out of normal CI; all normal validation must use fake
   translators or injectable HTTP/token seams.

## Current Baseline

The baseline CLI is already implemented under:

```text
src/private/app/document-translator-cli/
```

Key existing seams to preserve and extend:

| Area                             | Existing files                                                                                                                                                                                | Implementation expectation                                                                               |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Command line parsing             | `Program.cs`, `DocumentTranslatorCommandLineParser.cs`, `CommandLineParseResult.cs`, `RawTranslationOptions.cs`                                                                               | Add `--markdown-mode` without changing existing option semantics.                                        |
| Option resolution and validation | `TranslationOptionResolver.cs`, `TranslationOptionsValidator.cs`, `TranslationOptions.cs`, `TranslationValidationResult.cs`                                                                   | Split common validation from route-specific extension/content-type validation.                           |
| Legacy translation               | `IDocumentTranslator.cs`, `AzureDocumentTranslator.cs`, `ISingleDocumentTranslationClient.cs`, `ISingleDocumentTranslationClientFactory.cs`, `AzureSingleDocumentTranslationClientFactory.cs` | Keep as the only whole-document translation path.                                                        |
| Output atomicity                 | `OutputWriter.cs`, `AtomicOutputWriter.cs`                                                                                                                                                    | Reuse as the final write seam for Markdown-aware output bytes.                                           |
| Tests                            | `tests/private/app/document-translator-cli/`                                                                                                                                                  | Extend existing xUnit v3/MTP coverage with deterministic fake Markdown translators and HTTP/token seams. |

## 1. Project and Dependency Integration

Add the Markdown parser dependency through Central Package Management:

```xml
<PackageVersion Include="Markdig" Version="<selected-version>" />
```

Implementation tasks:

1. Add `Markdig` to `Directory.Packages.props` in alphabetical order.
2. Add a `PackageReference` for `Markdig` to
   `src/private/app/document-translator-cli/DocumentTranslatorCli.csproj`.
3. Restore and update lock files when the package graph changes.
4. Do not add a `System.Text.Json` package reference for this feature. Use the
   shared framework assembly unless a later implementation decision explicitly
   requires a NuGet override.
5. Do not add a hosting package, dependency injection container, or
   `IHttpClientFactory`. The CLI performs one bounded operation per process, so
   owned `HttpClient` instances and injectable test seams are sufficient.

## 2. Workstream Groups

Implement the feature through these workstream groups. Each group must leave the
repository buildable and must add tests for the behavior it introduces.

| Group                                          | Primary output                                                             | Depends on | Safe parallel work                                                           |
| ---------------------------------------------- | -------------------------------------------------------------------------- | ---------- | ---------------------------------------------------------------------------- |
| A. Routing and option contract                 | `MarkdownMode`, `TranslationRoute`, route-aware validation, parser option  | None       | B, as long as both agents use the shared type names in this plan.            |
| B. Markdown domain model                       | parser factory, text ranges, segment/token records, failure types          | None       | A, and C only after B publishes the shared range and failure contracts.      |
| C. Encoding and protected range collection     | strict UTF-8 decoding, BOM/newline metadata, preliminary parse ranges      | B          | F service-client work that does not depend on extracted segment shape.       |
| D. Unsupported construct detection             | deterministic fail-closed detector and diagnostics                         | C          | F HTTP, JSON, authentication, and sanitized-diagnostic tests.                |
| E. Segment extraction and token protection     | approved text-node extraction, machine-token placeholder maps              | D          | F backend work after B publishes the request contract.                       |
| F. Text translation backend                    | `ITextSegmentTranslator`, Azure Text Translation REST client, JSON context | A, B       | D/E unit work that does not change `TextSegmentTranslationRequest`.          |
| G. Placeholder restoration and source patching | placeholder invariant checks and descending-span patcher                   | E          | F backend work after the segment ordering and batching contracts are stable. |
| H. Reconstruction validation                   | structural fingerprints and protected-byte validation                      | C, G       | F service tests that do not change Markdown validation inputs.               |
| I. Command orchestration                       | `MarkdownTranslationCommand` and route dispatch                            | A, F, G, H | None.                                                                        |
| J. End-to-end hardening                        | golden files, negative tests, regression tests, validation commands        | All groups | None.                                                                        |

## 3. Component Map

Use the high-level design component names so implementation agents can coordinate
without inventing duplicate abstractions.

| Component                              | Owner phase | Responsibility                                                                                         |
| -------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------------ |
| `MarkdownMode`                         | Phase 1     | User-selected Markdown routing mode.                                                                   |
| `TranslationRoute`                     | Phase 1     | Validated dispatch route.                                                                              |
| `MarkdownDocumentParser`               | Phases 2-3  | Central Markdig pipeline usage, strict UTF-8 document model, and source metadata.                      |
| `MarkdownUnsupportedConstructDetector` | Phase 4     | Fail-closed unsupported syntax detection.                                                              |
| `MarkdownSegmentExtractor`             | Phase 5     | Approved text-node extraction from a document that has already passed unsupported-construct detection. |
| `MarkdownTokenProtector`               | Phases 3, 5 | Frozen machine-token matching, early protected-range scanning, and placeholder insertion.              |
| `MarkdownSourcePatcher`                | Phase 7     | Placeholder restoration, translated source-span replacement, and source patch mapping.                 |
| `MarkdownOutputValidator`              | Phase 8     | Re-parse, structural fingerprint, protected-slice, BOM, newline, and line-ending validation.           |
| `ITextSegmentTranslator`               | Phase 6     | Testable segment translation abstraction.                                                              |
| `AzureTextSegmentTranslator`           | Phase 6     | Batching adapter from segment requests to Azure Text Translation REST calls.                           |
| `AzureTextTranslationClient`           | Phase 6     | Low-level HTTP client for Azure Translator Text Translation REST API v3.0.                             |
| `AzureTextTranslationJsonContext`      | Phase 6     | Source-generated JSON metadata for request and response payloads.                                      |
| `MarkdownTranslationCommand`           | Phase 9     | Markdown-aware pipeline orchestration.                                                                 |

## 4. Phase 0: Baseline Characterization

Before changing behavior, capture the current baseline with tests or existing
test runs.

Tasks:

1. Run the existing document-translator test project.
2. Identify existing tests that prove non-Markdown extension routing, content
   type selection, output overwrite behavior, same-path rejection, endpoint
   validation, authentication validation, Azure error mapping, cancellation, and
   atomic output behavior.
3. Add missing baseline regression tests before refactoring shared validation or
   command orchestration seams.
4. Keep these tests focused on current behavior; do not mix Markdown assertions
   into baseline characterization tests.

Exit criteria:

1. The current non-Markdown route is protected by tests before route selection is
   refactored.
2. The team can identify whether a later failure is caused by Markdown work or a
   baseline regression.

## 5. Phase 1: CLI Option and Route Selection

Implement the user-visible routing contract first, without parsing Markdown.

Tasks:

1. Add:

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

2. Add `MarkdownMode` to the raw and validated option models.
3. Add `TranslationRoute` and `IsMarkdownExtension` to validated options.
4. Add `--markdown-mode <auto|aware|legacy>` to the `translate` command.
5. Add `DOCUMENT_TRANSLATOR_MARKDOWN_MODE` environment fallback with the same
   precedence model as the baseline CLI.
6. Validate Markdown mode values case-insensitively after trimming; invalid
   values return exit code `2` before translation.
7. Determine Markdown extensions from the final path segment with
   `Path.GetExtension`-equivalent behavior and ordinal case-insensitive
   comparison.
8. Refactor validation into common validation followed by route-specific
   extension/content-type validation:
    - common validation keeps input path, output path, target language, endpoint,
      authentication, 10 MB limit, same-path, overwrite, and output directory
      rules;
    - route-specific validation applies the legacy allowlist, Markdown-aware
      extension requirement, or explicit Markdown legacy `text/plain` rule.
9. Keep `.md` and `.markdown` unsupported in the baseline allowlist except when
   `legacy` explicitly routes them through `text/plain`.

Tests:

1. Command-line `--markdown-mode` wins over
   `DOCUMENT_TRANSLATOR_MARKDOWN_MODE`.
2. Environment value wins over the default.
3. Default mode is `auto`.
4. Lowercase, uppercase, and mixed-case `.md` and `.markdown` route to
   `MarkdownAware` in `auto`.
5. Non-Markdown supported formats route to `LegacyDocument` in `auto`.
6. `aware` rejects non-Markdown extensions.
7. `legacy` routes Markdown extensions to `LegacyDocument` with content type
   `text/plain` and original file name preservation.
8. Invalid Markdown mode values fail before either translator is called.
9. `legacy` bypasses Markdown-aware validation for an invalid Markdown fixture
   and calls only the legacy translator.

Exit criteria:

1. Route selection is fully testable without Markdown parser dependencies.
2. Existing non-Markdown tests still pass.

## 6. Phase 2: Markdown Core Models and Parser Factory

Introduce small internal models before implementing extraction or backend logic.

Tasks:

1. Add immutable source range and segment models:

    ```csharp
    internal readonly record struct TextRange(int Start, int Length);

    internal sealed record ProtectedToken(
        TextRange SourceRange,
        string Placeholder,
        string OriginalText);

    internal sealed record MarkdownTranslationSegment(
        int SegmentIndex,
        TextRange SourceRange,
        string OriginalText,
        string ProtectedText,
        IReadOnlyList<ProtectedToken> ProtectedTokens);

    internal sealed record TextSegmentTranslationRequest(
        int SegmentIndex,
        string ProtectedText,
        IReadOnlyList<ProtectedToken> ProtectedTokens);

    internal sealed record ProtectedSlice(
        string SliceId,
        string Kind,
        TextRange SourceRange,
        string OriginalText);

    internal sealed record SourcePatchMap(
        int SegmentIndex,
        TextRange OriginalRange,
        TextRange PatchedRange,
        int LengthDelta);
    ```

2. Add typed Markdown failure categories for unsupported syntax, invalid UTF-8,
   unreliable source spans, segment-size violations, placeholder corruption,
   reconstruction changes, and structural changes detected during validation.
3. Define the segment request handoff contract:
    - `SegmentIndex` is unique, zero-based, and stable for the whole document;
    - requests preserve extraction order;
    - `ProtectedText` is the only text sent to Azure;
    - `ProtectedTokens` is per-segment metadata for fake translators and
      placeholder validation;
    - `AzureTextSegmentTranslator` owns request batching, but batching must not
      reorder returned results.
4. Define the protected-slice validation contract:
    - `ProtectedSlice` instances identify protected content by stable semantic
      kind and source slice, not by post-patch absolute offsets;
    - `MarkdownSourcePatcher` must pass patch mapping metadata that lets
      `MarkdownOutputValidator` locate each protected slice after translated spans
      change length;
    - `SourcePatchMap` instances are ordered by `OriginalRange`, include the
      original translated range, patched translated range, segment index, and
      length delta, and use decoded-string offsets in their respective source and
      patched texts;
    - validators must fail closed if a protected slice cannot be uniquely located
      in the patched document.
5. Define Unicode length rules:
    - segment and batch limits count Unicode scalar values, not UTF-16 code units;
    - use `System.Text.Rune` or `string.EnumerateRunes()`-equivalent logic for
      limit checks;
    - `TextRange` and patch maps still use decoded-string offsets, but no range
      may split a surrogate pair.
6. Add a single parser factory, such as
   `MarkdownParserFactory.CreateV1Pipeline()`, that enables only:
    - CommonMark-compatible core syntax,
    - pipe tables,
    - task lists,
    - strikethrough,
    - footnotes,
    - YAML front matter,
    - raw HTML blocks, inline HTML, and HTML comments,
    - precise source position tracking.
7. Do not enable broad Markdig extension bundles such as
   `UseAdvancedExtensions()`.
8. Add tests proving the factory recognizes required syntax and does not enable
   unsupported extension bundles accidentally.

Exit criteria:

1. All downstream Markdown components use the same parser factory.
2. Failure categories can map deterministically to the baseline exit-code
   taxonomy.

## 7. Phase 3: Strict UTF-8 and Protected Range Collection

Implement byte-level input handling before extracting translatable text.

Tasks:

1. Decode input bytes as UTF-8 with strict error detection.
2. Preserve UTF-8 byte order mark presence.
3. Record final newline presence.
4. Record original line-ending bytes for source ranges that must remain
   protected.
5. Reject JSON front matter before any Markdig parse when the first byte after an
   optional UTF-8 byte order mark is `{`.
6. Run a preliminary Markdig parse to collect source ranges for:
    - fenced code blocks,
    - indented code blocks,
    - inline code spans,
    - YAML front matter,
    - raw HTML blocks,
    - HTML comments,
    - text enclosed by raw HTML markup,
    - link and image destinations,
    - link and image titles,
    - reference labels,
    - reference definitions,
    - autolinks,
    - URL literals,
    - email literals,
    - URI fragments,
    - Markdown structural syntax,
    - escaped Markdown delimiters.
7. Run an early `MarkdownTokenProtector` machine-token scan without generating
   placeholders. This scan may produce detector exclusion ranges only for
   template variables, replacement fields, and braced shell variable references
   such as `${name}` inside otherwise translatable parser text nodes. It must not
   exclude URL literals, file paths, link destinations, titles, reference
   metadata, URI fragments, or other candidate protected ranges from
   unsupported-construct detection. Phase 5 reuses the same matcher to generate
   placeholders inside extracted segments.
8. Do not exclude arbitrary inline HTML before unsupported-construct detection;
   JSX-like uppercase tags must still be detectable.
9. Identify text enclosed by inline raw HTML markup, such as
   `<span>do not translate</span>`, as candidate protected content. Phase 4 must
   scan the full tag text, attributes, and enclosed text for frozen unsupported
   inline patterns before the enclosure becomes protected. Fail closed if paired
   inline HTML ranges are nested, malformed, or otherwise cannot be proven safe
   under the frozen parser scope.
10. Treat malformed Markdown operationally as a safety failure when parsing fails,
    a required source span is unavailable, the parser emits an unsupported node
    for this feature, inline raw HTML enclosure pairing is ambiguous, or later
    structural fingerprinting cannot prove equivalence. Do not add broad
    heuristic malformed-Markdown repair.
11. Fail validation if a required source range is unavailable for a non-empty
    node that must be protected or translated.

Tests:

1. Valid UTF-8 with and without BOM succeeds.
2. Invalid UTF-8 fails before translation.
3. LF, CRLF, mixed line endings, final newline, and no-final-newline cases are
   represented without normalization.
4. Leading-`{` JSON front matter fails before preliminary Markdown parsing and
   before either translator can be called.
5. Protected range collection excludes code, YAML front matter, raw HTML, inline
   code, references, inline raw HTML enclosure text, and structural delimiters
   from later translation.
6. Inline JSX-like HTML remains visible to unsupported-construct detection.
7. Lowercase inline HTML can be protected after MDX/JSX detection, while
   uppercase JSX-like tags are rejected.
8. Bare URL literals, email literals, and URI fragments are protected before
   extraction.

Exit criteria:

1. The Markdown pipeline has reliable text, byte metadata, and protected ranges.
2. Later components do not need to inspect raw bytes directly except for final
   validation and encoding.

## 8. Phase 4: Unsupported Construct Detection

Implement the fail-closed detector exactly from the frozen requirements.

Tasks:

1. Run detection after protected range collection and before segment extraction.
2. Apply line-based and inline patterns for:
    - MDX import/export,
    - MDX JSX block or element,
    - MDX expression,
    - MDX comment,
    - Markdown directive,
    - custom admonition,
    - TOML front matter,
    - JSON front matter.
3. Use exactly the detector exclusion set from the requirements and high-level
   design: fenced code blocks, indented code blocks, inline code spans, YAML
   front matter, raw HTML blocks, HTML comments, and early brace-like machine
   token matches. Early brace-like machine-token matches are limited to template
   variables, replacement fields, and braced shell variable references inside
   otherwise translatable parser text nodes. Other protected ranges collected in
   Phase 3, such as link destinations, titles, reference metadata, URL literals,
   email literals, URI fragments, structural delimiters, inline HTML tags, and
   text enclosed by inline raw HTML markup, are for extraction and reconstruction
   validation only; they must not suppress unsupported-construct detection.
4. Do not exclude inline HTML tags until all frozen unsupported inline patterns
   have been applied to the tag text. Inline tags matching the frozen
   uppercase/MDX-like JSX pattern, MDX expression pattern, or MDX comment pattern
   must fail. Parser-classified inline HTML tags may be protected from
   translation only after those checks prove the full tag text is not an
   unsupported MDX-like construct.
5. Do not protect text enclosed by paired inline raw HTML markup until all
   frozen unsupported inline patterns have been applied to the full enclosure,
   including opening tags, attributes, enclosed text, and closing tags.
   Enclosures containing MDX JSX, MDX expressions, or MDX comments must fail.
6. Report category and line number without echoing matched source text.
7. Detect shortcut and collapsed reference links during link-node inspection and
   fail before translation.
8. Prefer false-positive rejection over unsafe translation when a construct is
   ambiguous.

Tests:

1. Each unsupported pattern fails outside excluded ranges.
2. Equivalent text inside code, raw HTML blocks, HTML comments, front matter, or
   allowed early brace-like machine tokens does not fail incorrectly.
3. Lowercase inline HTML tags are allowed and protected only after MDX JSX,
   expression, and comment checks run across the full tag text.
4. Uppercase JSX-like tags, MDX expressions in inline HTML attributes, and MDX
   comments in inline HTML attributes fail before translation.
5. MDX expressions, MDX comments, and JSX-like tags inside paired inline raw HTML
   enclosures fail before translation, including `<span>{foo}</span>`,
   `<span>{/* comment */}</span>`, `<span><MyComponent /></span>`, and multiline
   inline raw HTML enclosures that contain those constructs.
6. Shortcut reference links and collapsed reference links fail before
   translation.
7. Error messages include stable categories and line numbers but not document
   excerpts.
8. MDX expressions, MDX comments, and JSX-like text fail outside the allowed
   detector exclusion set, including when they appear in link destinations,
   titles, or reference metadata.
9. MDX expressions, MDX comments, and JSX-like text inside URL literals, file
   paths, URI fragments, link destinations, link titles, and reference metadata
   fail before translation; those machine-token or candidate protected ranges do
   not suppress unsupported-construct detection.
10. Spaced and non-identifier single-brace MDX expressions, including `{ foo }`,
    `{1 + 2}`, and `{"text"}`, fail before translation unless they are an
    allowed early brace-like machine token.

Exit criteria:

1. Unsupported constructs cannot reach the translator.
2. Diagnostics are stable enough for tests without exposing source content.

## 9. Phase 5: Segment Extraction and Machine-Token Protection

Extract only approved text nodes from a document that has already passed
unsupported-construct detection, then protect inline machine tokens.

Tasks:

1. Extract text nodes only from:
    - heading visible text,
    - paragraph prose,
    - list item prose,
    - block quote prose,
    - table cell prose,
    - inline link display text and full reference link display text,
    - inline image alt text and full reference image alt text,
    - footnote body prose,
    - text inside emphasis, strong emphasis, or strikethrough.
2. Ensure a segment never crosses a block boundary, table cell boundary, or
   protected range.
3. Fail if Markdig cannot provide a reliable source span for a non-empty
   approved text node.
4. Apply every frozen machine-token pattern to each candidate text node.
5. Resolve overlapping token matches by longest match, then earliest start
   offset.
6. Reuse the same `MarkdownTokenProtector` matcher used by Phase 3 early
   scanning; Phase 5 is the only phase that creates translation placeholders.
7. Replace protected inline tokens with placeholders of this exact form:

    ```text
    __DTCLI_PH_<segment-index>_<token-index>__
    ```

8. Verify generated placeholders do not already appear anywhere in the decoded
   document.
9. Enforce the 50,000 Unicode scalar value limit for each protected segment by
   counting Unicode scalar values with `System.Text.Rune` or equivalent logic.
10. Emit `TextSegmentTranslationRequest` instances in `SegmentIndex` order for
    the backend adapter. Do not batch inside the extractor.
11. Return an empty segment list for valid protected-only Markdown instead of
    treating the absence of translatable nodes as a validation failure.

Tests:

1. Approved containers produce expected segments.
2. Code, raw HTML, front matter, link destinations, titles, references,
   autolinks, URL literals, email literals, URI fragments, task markers,
   Markdown delimiters, and identifiers are not translated.
3. Text enclosed by paired inline raw HTML tags is not translated.
4. Ambiguous, nested, or malformed inline raw HTML enclosure fails closed.
5. Each frozen machine-token pattern is protected.
6. Overlap resolution uses longest match, then earliest start offset.
7. Placeholder collision fails before translation.
8. Segment size limits fail before translation.
9. Protected-only Markdown produces zero segment requests.
10. Package-like identifier protection respects the previous-three-word,
    same-text-node context window, treats ASCII punctuation and Markdown
    delimiters as separators, and does not cross protected-token boundaries.

Exit criteria:

1. The backend receives only protected prose segments.
2. No protected syntax or machine token is present in a translator-editable span.

## 10. Phase 6: Text Translation Backend

Add the segment translator abstraction and Azure Text Translation REST client.

Tasks:

1. Add:

    ```csharp
    internal interface ITextSegmentTranslator
    {
        ValueTask<IReadOnlyList<string>> TranslateAsync(
            TranslationOptions options,
            IReadOnlyList<TextSegmentTranslationRequest> segments,
            CancellationToken cancellationToken);
    }
    ```

2. Use the Phase 2 `TextSegmentTranslationRequest` contract without changing its
   fields or ordering semantics.
3. Implement deterministic fake translators for tests:
    - success variant returns `TRANSLATED[n]` followed by one ASCII space and the
      original protected segment text;
    - failure variants drop, duplicate, mutate, and reorder placeholders.
4. Implement `AzureTextSegmentTranslator` as the adapter that batches ordered
   segment requests, calls `AzureTextTranslationClient`, and restores result
   ordering by `SegmentIndex`.
5. Implement `AzureTextTranslationClient` around `HttpClient`.
6. Add `AzureTextTranslationJsonContext` for source-generated request and
   response JSON metadata.
7. Provide injectable `HttpClient` or `HttpMessageHandler` and injectable token
   provider seams.
8. Build request URIs from the validated root custom-domain endpoint by
   appending `/translator/text/v3.0/translate` and query parameters
   `api-version=3.0` and `to=<target-language>`.
9. Send a UTF-8 JSON body shaped as `[{ "Text": "<segment>" }]`.
10. Set `Content-Type` to `application/json; charset=utf-8`.
11. For API key authentication, send `Ocp-Apim-Subscription-Key`.
12. For Entra ID authentication, request
    `https://cognitiveservices.azure.com/.default` through the existing Azure
    Identity credential flow and send `Authorization: Bearer <token>`.
13. Treat token acquisition failures as service errors that map to exit code `3`.
14. Treat non-success HTTP status, malformed JSON, missing result entries, extra
    result entries, and empty translation values as service errors.
15. Enforce batching by both service limits:
    - at most 100 text array elements per request;
    - at most 50,000 Unicode scalar values across all protected segment texts in
      a request, counted with `System.Text.Rune` or equivalent logic. Do not use
      `string.Length` as a proxy for this limit.
16. Sanitize service diagnostics. Do not include request bodies, response bodies,
    malformed JSON snippets, source segment text, translated segment text, API
    keys, or bearer tokens in exceptions, logs, stderr, or test failure messages.
17. Return translated strings only; do not expose raw service JSON to callers.

Tests:

1. URI path and query construction are exact.
2. JSON request body contains only protected segment text.
3. API key and Entra ID authentication headers are correct.
4. Token acquisition is testable without live Azure credentials.
5. Result count and malformed response validation are deterministic.
6. Token acquisition failures map to service errors.
7. Segment batching respects both the 100-item request limit and the 50,000
   Unicode scalar value request limit while preserving result order.
8. Empty translated text values map to service errors.
9. Segment and batch scalar limits count Unicode scalar values rather than UTF-16 code
   units, including cases with surrogate pairs.
10. Batching splits many short segments by the 100-item request limit.
11. Non-success responses and malformed JSON diagnostics do not include response
    bodies, request bodies, segment text, keys, or tokens.

Exit criteria:

1. Azure Text Translation behavior is fully covered without live network calls.
2. The whole-document Azure Document Translation path remains untouched for
   legacy routes.

## 11. Phase 7: Placeholder Restoration and Source Patching

Restore protected tokens and patch translated text into the original source.

Tasks:

1. For each translated segment, verify every expected placeholder appears
   exactly once.
2. Verify no unexpected placeholder-shaped token appears.
3. Verify placeholders appear in the same relative order as in the protected
   input segment.
4. Replace placeholders with original protected token text.
5. Sort source spans by descending start offset.
6. Replace only extracted text ranges in the decoded string.
7. Keep every character outside translated ranges unchanged.
8. Produce one `SourcePatchMap` per patched segment. Each map records
   `SegmentIndex`, `OriginalRange`, `PatchedRange`, and `LengthDelta`, and lets
   `MarkdownOutputValidator` locate every `ProtectedSlice` after
   length-changing replacements.
9. Re-encode only after reconstruction validation has passed.

Tests:

1. Placeholder drop, duplication, mutation, reorder, and unexpected placeholder
   cases fail validation.
2. Descending-span patching handles translated text with different length.
3. Adjacent segments do not corrupt each other.
4. Protected text outside translated ranges remains unchanged.
5. Patch mapping metadata remains correct when translated text is shorter,
   longer, empty, or contains Unicode scalar values outside ASCII.
6. Patch mapping rejects ranges that split surrogate pairs.

Exit criteria:

1. Translator output cannot move or corrupt protected tokens silently.
2. Patching is deterministic and independent of translated text length.

## 12. Phase 8: Reconstruction and Structural Validation

Validate the patched Markdown before returning output bytes to the atomic writer.

Tasks:

1. Re-parse the patched Markdown with the same parser pipeline.
2. Compare source and output structural fingerprints for:
    - node type order,
    - node nesting and parent-child relationships,
    - opening and closing delimiter kinds where source-supported,
    - inline delimiter placement for emphasis, strong emphasis, strikethrough,
      links, images, code spans, autolinks, and HTML,
    - block order and nesting,
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
3. Use Phase 7 patch mapping metadata and structural fingerprints to locate
   protected slices after translated text changes length. Do not compare
   protected bytes by original absolute offsets alone.
4. Compare protected source slices against their corresponding output slices.
5. Fail closed if a protected slice cannot be located uniquely in the patched
   document.
6. Recompute detector exclusion ranges from the patched parse and patched text,
   then run unsupported construct detection on the patched output outside those
   exact output-side exclusion ranges. If translated prose introduces MDX
   import/export, MDX JSX, MDX expression, MDX comment, Markdown directive,
   custom admonition, TOML front matter, or JSON front matter syntax, fail
   validation before output bytes are returned. Do not reuse source-document
   exclusion offsets after translation changes text lengths.
7. Verify BOM presence and final newline presence.
8. Verify original line-ending bytes remain unchanged outside translated prose.
9. Map validation failures to exit code `2` unless caused by service, file I/O,
   or cancellation failures.

Tests:

1. Golden files pass byte-for-byte comparison for successful cases.
2. Structural mutations fail for table shape, link destination/title, image
   destination/title, reference definitions, block order, nesting, code fences,
   front matter, raw HTML, task markers, and footnote identifiers.
3. Inline delimiter placement mutations fail for emphasis, strong emphasis,
   strikethrough, links, images, code spans, autolinks, and HTML.
4. BOM, line endings, and final newline behavior are preserved.
5. Validation failure leaves no final output file.
6. Protected slices are validated correctly when earlier translated spans change
   length.
7. Fake translator variants that inject MDX expression, MDX comment, JSX,
   directive, custom admonition, TOML front matter, JSON front matter, or
   import/export-like text fail output validation before writing.
8. Fake translator variants that inject spaced or non-identifier single-brace
   MDX expressions such as `{ foo }`, `{1 + 2}`, and `{"text"}` fail output
   validation before writing.

Exit criteria:

1. Markdown-aware output is not returned unless structure and protected bytes are
   proven safe.
2. Output byte encoding preserves the required input byte properties.

## 13. Phase 9: Command Orchestration and Error Mapping

Wire the Markdown-aware pipeline into the existing command execution flow.

Tasks:

1. Add `MarkdownTranslationCommand` to coordinate:
    - input byte reading,
    - strict UTF-8 decoding,
    - protected range collection,
    - unsupported construct detection,
    - segment extraction,
    - token protection,
    - invoking `AzureTextSegmentTranslator`, which owns segment batching and text
      translation,
    - placeholder restoration,
    - source patching,
    - reconstruction validation,
    - UTF-8 output byte encoding.
2. If segment extraction returns zero segments, skip `ITextSegmentTranslator`,
   keep the original decoded text as the patched text, still run reconstruction
   and protected-byte validation, and write the original bytes through the
   existing atomic output path after validation succeeds.
3. Keep the existing same-directory temporary-file output preflight before
   opening the input file, invoking `MarkdownTranslationCommand`, or calling
   either translator. If output preflight fails, return the appropriate file I/O
   or validation error without submitting any document content to a translation
   backend.
4. Dispatch by `TranslationRoute`:
    - `LegacyDocument` calls the existing `IDocumentTranslator` path;
    - `MarkdownAware` calls `MarkdownTranslationCommand`.
5. Return translated Markdown as `BinaryData` or equivalent byte content so
   `AtomicOutputWriter` remains the final output seam.
6. Never fall back to `LegacyDocument` after any Markdown-aware failure.
7. Map expected Markdown failures to validation exit code `2`.
8. Map Azure Text Translation HTTP and response failures to service exit code
   `3`.
9. Map Azure Identity token acquisition failures to service exit code `3`.
10. Map file I/O and path failures to exit code `4`.
11. Keep cancellation mapped to exit code `1`.
12. Keep success stdout consistent with the baseline CLI.

Tests:

1. Markdown-aware success writes through the existing atomic writer.
2. Markdown-aware success for `.md` and `.markdown` in `auto` and `aware` calls
   only `ITextSegmentTranslator` with extracted protected segments and never
   calls `IDocumentTranslator` or Azure Document Translation.
3. Markdown validation failure calls no legacy translator and writes no output.
4. Protected-only Markdown with zero extracted segments skips
   `ITextSegmentTranslator`, still validates, and writes byte-for-byte original
   content through the atomic writer.
5. Text translation service failure writes no output.
6. Token acquisition failure maps to exit code `3` and writes no output.
7. Output preflight failure occurs before `MarkdownTranslationCommand`,
   `ITextSegmentTranslator`, `IDocumentTranslator`, or Azure clients are called.
8. File I/O failure and cancellation preserve existing cleanup behavior.
9. Non-Markdown inputs still call only the legacy translator.
10. Explicit Markdown legacy inputs call only the legacy translator with
    `text/plain`.

Exit criteria:

1. The feature is integrated end-to-end with route-specific test coverage.
2. No safety failure can produce partial final output.

## 14. Phase 10: Test Matrix and Golden Fixtures

Complete the required test coverage before declaring the implementation ready.

Golden-file fixture groups:

1. Headings, paragraphs, nested lists, block quotes, and thematic breaks.
2. Soft line breaks, hard breaks using two trailing spaces, and backslash hard
   breaks with surrounding line-ending bytes preserved.
3. Pipe tables, including alignment rows.
4. Emphasis, strong emphasis, and strikethrough.
5. Inline links, full reference links, images, image alt text, and protected
   titles.
6. Autolinks, URL literals, email literals, and URI fragments.
7. Fenced code blocks, indented code blocks, and inline code.
8. Escaped Markdown delimiters.
9. YAML front matter.
10. Raw HTML blocks, inline HTML tags, inline raw HTML enclosure text, and HTML
    comments.
11. Task lists.
12. Footnotes.
13. Each frozen machine-token class and overlap resolution.
14. Placeholder-bearing prose and protected spans.
15. Protected-only Markdown with zero extracted segments.
16. UTF-8 BOM, no BOM, LF, CRLF, mixed line endings, final newline, and
    no-final-newline.

Negative fixture groups:

1. Invalid UTF-8.
2. Malformed Markdown that prevents safe parsing, emits unavailable required
   source spans, creates unsupported parser nodes, makes inline raw HTML
   enclosure ambiguous, or prevents structural fingerprint equivalence.
3. MDX import/export, JSX element, MDX expression, spaced and non-identifier
   single-brace MDX expression, and MDX comment.
4. Markdown directive and custom admonition.
5. TOML and JSON front matter.
6. Leading-`{` JSON front matter reaches no Markdig parser or translator seam.
7. Shortcut and collapsed reference links.
8. Segment over 50,000 Unicode scalar values.
9. Batch over 50,000 Unicode scalar values.
10. Batch over 100 text array elements.
11. Placeholder drop, duplication, mutation, reorder, and unexpected placeholder.
12. Structural validation mismatch for each protected invariant.
13. Translator-injected unsupported MDX expression, MDX comment, JSX, directive,
    custom admonition, TOML front matter, JSON front matter, and
    import/export-like text in prose.
14. Azure service error, Azure Identity token acquisition failure, empty
    translated text value, and malformed Azure response.

Command outcome fixture groups:

1. Atomic output success.
2. Validation failure with no final output.
3. Service failure with no final output.
4. File I/O failure with no final output.
5. Cancellation with temporary output cleanup.

Test hygiene rules:

1. Use deterministic fake translators for normal command and golden-file tests.
2. Assert stable error categories, option names, and line numbers; do not assert
   complete source or translated content in diagnostics.
3. Keep live Azure smoke tests out of normal CI. If added later, gate them with
   an explicit opt-in environment variable and a test trait excluded by default.
4. Prefer byte-for-byte output assertions for controlled success fixtures. This
   exception applies only to in-repository test fixtures and does not permit
   printing document content in diagnostics, logs, stderr, or service errors.
5. Keep fixture names descriptive enough that future agents can identify the
   protected invariant being tested.

## 15. Agent Assignment Model

Use independent implementation agents only when their scopes do not overlap.
Each agent must receive the requirements, high-level design, this plan, and the
current code paths for its workstream.

Recommended groups:

| Agent group           | Owned phases   | Handoff output                                                                                 |
| --------------------- | -------------- | ---------------------------------------------------------------------------------------------- |
| CLI routing agent     | Phases 0-1     | Route-aware options, tests, and unchanged baseline behavior.                                   |
| Markdown parser agent | Phases 2-4     | Parser factory, source metadata, protected ranges, and unsupported detector.                   |
| Segment safety agent  | Phases 5 and 7 | Extraction, token protection, placeholder restoration, source patching, and patch maps.        |
| Backend agent         | Phase 6        | Segment translator adapter, Azure REST client, JSON context, fake translators, and tests.      |
| Validation agent      | Phase 8        | Structural fingerprints, protected-byte validation, encoding preservation, and negative tests. |
| Integration agent     | Phases 9-10    | End-to-end command orchestration, fixture matrix, error mapping, and validation command runs.  |

Coordination rules:

1. Merge agents only through narrow, reviewed seams: `TranslationOptions`,
   `ITextSegmentTranslator`, `MarkdownTranslationSegment`,
   `TextSegmentTranslationRequest`, `TextRange`, `ProtectedSlice`, patch mapping
   metadata, and typed Markdown failure results.
2. Do not allow multiple agents to edit the same component family concurrently
   after implementation begins.
3. Require each agent to add or update tests before handing off.
4. Require each agent to document any requirement ambiguity in the pull request
   discussion instead of silently choosing a broader behavior.
5. Stop implementation if a proposed change conflicts with the frozen
   requirements or high-level design; update the design documents first only
   after explicit approval.

## 16. Review and Iteration Gates

Use these review gates before implementation is accepted:

1. **Requirements traceability review**: verify every functional requirement,
   failure semantics, non-functional requirement, acceptance criterion, and
   required test coverage item is represented by a workstream, task, or test
   group in this plan.
2. **High-level design consistency review**: verify component names, dependency
   direction, route selection, parser pipeline, placeholder format, backend URI,
   error mapping, and test seams match the high-level design.
3. **Adversarial safety review**: actively search for paths that could corrupt
   Markdown, translate protected bytes, bypass validation, write partial output,
   leak content or secrets, or silently fall back to legacy translation.
4. **Implementation sequencing review**: verify the phases are ordered so
   baseline behavior is protected before refactoring, shared models exist before
   consumers, and integration happens only after safety checks are testable.
5. **Writing quality review**: verify the document uses American English,
   professional tone, precise engineering language, and actionable instructions
   for senior AI coding agents.

Review iteration rule:

1. Address all blocking and non-blocking review comments that improve
   correctness, safety, traceability, sequencing, or clarity.
2. Re-run the independent reviews after each material revision.
3. Stop iterating only when reviewers have no remaining actionable comments.

## 17. Risk Register

| Risk                                                                         | Mitigation                                                                                             |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Markdig source spans are unavailable or unreliable for a required text node. | Fail validation for that file and add fixture coverage before considering alternate extraction logic.  |
| AST rendering normalizes protected bytes.                                    | Do not render from AST; patch source spans in the original decoded text.                               |
| Inline HTML exclusion hides MDX-like JSX.                                    | Keep arbitrary inline HTML visible until unsupported-construct detection completes.                    |
| Inline raw HTML enclosure text is translated accidentally.                   | Protect proven paired inline HTML enclosure ranges and fail closed when pairing is ambiguous.          |
| Machine-token patterns conflict with MDX expression detection.               | Let only early brace-like template, replacement, and braced-shell tokens suppress MDX detection.       |
| Placeholder text is translated, moved, or duplicated by the service.         | Validate placeholder presence, uniqueness, and relative order before restoration.                      |
| Markdown validation relies on shifted absolute offsets after translation.    | Compare structural fingerprints and protected semantic slices, not only raw post-patch offsets.        |
| Route refactoring regresses non-Markdown files.                              | Add baseline characterization tests before changing validation flow.                                   |
| Azure Text Translation tests require live credentials.                       | Use injectable HTTP and token seams; keep live smoke tests opt-in only.                                |
| Service diagnostics echo request or response content.                        | Sanitize HTTP, JSON, and token errors and test that bodies, segment text, keys, and tokens are absent. |
| Diagnostics leak source content or secrets.                                  | Test stable categories and line numbers without asserting or printing source excerpts.                 |

## 18. Implementation Validation Commands

After implementation, run targeted validation first:

```powershell
dotnet build .\src\private\app\document-translator-cli\DocumentTranslatorCli.csproj
dotnet test .\tests\private\app\document-translator-cli\Hcoona.DocumentTranslatorCli.Tests.csproj
```

If package references or lock files changed, run restore before the targeted
build and commit the resulting lock file updates:

```powershell
dotnet restore .\src\private\app\document-translator-cli\DocumentTranslatorCli.csproj
dotnet restore .\tests\private\app\document-translator-cli\Hcoona.DocumentTranslatorCli.Tests.csproj
```

If time and environment constraints allow, also run the broader repository gate
that is appropriate for C# changes:

```powershell
dotnet build .\dirs.proj
```

## 19. Explicit Non-Goals

Do not implement these features as part of Markdown-aware v1:

- multi-document translation,
- multiple input files,
- multiple target languages,
- source language overrides,
- glossaries,
- custom translation models,
- interactive prompts,
- persistent configuration files,
- a separate text translation endpoint option,
- full MDX support,
- Markdown directives or custom admonitions,
- translating raw HTML content,
- translating front matter values,
- TOML or JSON front matter support,
- translating link or image titles,
- automatic Markdown detection for extensionless files,
- best-effort malformed Markdown repair,
- partial translation,
- silent fallback to whole-document translation after Markdown-aware failure.
