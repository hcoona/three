# Markdown-Aware Translation Requirements

Status: **Frozen for v1**

## Audience and Intent

This document is written for AI agents acting as senior software engineers. It
freezes the requirements for adding Markdown-aware translation to the existing
`document-translator` CLI without changing the previously frozen baseline
document translation behavior for non-Markdown files.

The implementation must preserve Markdown structure, translate only approved
Markdown text nodes, and fail safely when structure preservation cannot be
guaranteed.

## Goal

Extend `document-translator translate` so Markdown files can be translated while
preserving Markdown syntax and protected content.

Markdown-aware translation is automatically selected for local input files whose
final path segment has the `.md` or `.markdown` extension, matched
case-insensitively with ordinal comparison. Users must also have an explicit way
to disable Markdown-aware processing and force the legacy whole-document
translation path.

## Users

- A developer or operator who wants to translate one local Markdown file from a
  terminal.
- A repository maintainer who needs translated Markdown to remain reviewable,
  diff-friendly, and syntactically valid.
- An automation agent that must avoid corrupting code samples, links,
  placeholders, metadata, and other non-prose content embedded in Markdown.

## Primary Use Case

Given:

- a local `.md` or `.markdown` input file,
- a target language code,
- a local output path,
- an Azure Translator endpoint,
- selected authentication credentials, and
- the default Markdown mode,

the CLI parses the Markdown, translates approved text-node segments through a
text translation backend, reconstructs a Markdown document that preserves
protected structure, validates the result, and writes the translated Markdown to
the requested output path.

## Command Shape

The existing command remains the only command:

```bash
document-translator translate \
  --input ./README.md \
  --output ./README.zh-Hans.md \
  --target-language zh-Hans
```

Markdown mode is controlled by a new option:

```bash
document-translator translate \
  --input ./README.md \
  --output ./README.zh-Hans.md \
  --target-language zh-Hans \
  --markdown-mode auto
```

The option accepts these values:

| Value    | Behavior                                                                                                                                                                                                                                                                           |
| -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `auto`   | Default. Use Markdown-aware translation for `.md` and `.markdown`; use the legacy document path for all other supported extensions.                                                                                                                                                |
| `aware`  | Require Markdown-aware translation. The final input path segment must have the `.md` or `.markdown` extension.                                                                                                                                                                     |
| `legacy` | Disable Markdown-aware translation and force the legacy whole-document path. For `.md` and `.markdown`, bypass the baseline extension allowlist and submit the original input file name with content type `text/plain` only because the user explicitly requested legacy handling. |

Command-line values have the same precedence model as the baseline CLI: command
line first, then environment fallback, then documented defaults. The Markdown
mode environment variable is:

- `DOCUMENT_TRANSLATOR_MARKDOWN_MODE`

When neither the command-line option nor the environment variable is provided,
the mode is `auto`.

## Functional Requirements

1. The CLI must continue to accept exactly one input file and one target language
   per invocation.
2. The CLI must determine the input extension from the final path segment with
   `Path.GetExtension`-equivalent behavior and compare `.md` and `.markdown`
   case-insensitively with ordinal comparison.
3. The CLI must automatically use Markdown-aware translation when the resolved
   Markdown mode is `auto` and the input extension is `.md` or `.markdown`.
4. The CLI must use the legacy document translation path when the resolved
   Markdown mode is `auto` and the input extension is not `.md` or `.markdown`.
5. The CLI must require a `.md` or `.markdown` input extension when the resolved
   Markdown mode is `aware`.
6. The CLI must force the legacy whole-document translation path when the
   resolved Markdown mode is `legacy`. For Markdown extensions, this path must
   preserve the original input file name and use `text/plain` as the content
   type.
7. The CLI must fail before translation when the Markdown mode value is not
   `auto`, `aware`, or `legacy`, using case-insensitive comparison and
   deterministic normalization.
8. The CLI must keep the existing endpoint, authentication, target language,
   output overwrite, same-path, output directory, and atomic write requirements.
9. The CLI must keep the existing 10 MB maximum input file size limit.
10. Markdown-aware translation must translate only approved Markdown text nodes.
11. Markdown-aware translation must preserve protected Markdown syntax,
    structure, and non-prose content.
12. Markdown-aware translation must never silently fall back to legacy
    whole-document translation after a Markdown parse, segmentation,
    translation, placeholder, reconstruction, or validation failure.
13. Markdown-aware translation must be all-or-nothing for each input file. A
    partial translated output is not acceptable.
14. Markdown-aware translation must not write the final output file unless the
    reconstructed Markdown passes validation.
15. Error output must be concise, actionable, and must not include secrets or
    full document content.

## Markdown Scope

Markdown-aware v1 must use a Markdig-compatible parser pipeline with a frozen
extension set. The parser contract is part of the requirements because different
Markdown engines disagree on edge cases.

The supported v1 parser feature set is:

- CommonMark-style headings, paragraphs, thematic breaks, hard breaks, soft
  breaks, lists, block quotes, code fences, indented code blocks, inline code,
  emphasis, strong emphasis, links, images, autolinks, and reference
  definitions.
- GitHub Flavored Markdown pipe tables, task list markers, and strikethrough.
- Footnote definitions and references.
- YAML front matter delimited by `---` as protected metadata.
- Raw HTML blocks, inline HTML tags, and HTML comments as protected content.

Markdown-aware v1 does not support MDX, Markdown directives, custom admonition
syntax, embedded imports or exports, JSX expressions, TOML front matter, JSON
front matter, or other parser extensions outside the frozen feature set. These
constructs must fail closed before translation according to the detection rules
in the next section instead of being treated as plain prose or preserved by
heuristic pass-through.

## Unsupported Construct Detection

The implementation must detect unsupported constructs after excluding fenced code
blocks, indented code blocks, inline code spans, YAML front matter, raw HTML
blocks, and frozen machine token matches. Frozen machine token matches are
protected before MDX expression detection so template variables, replacement
fields, and shell variable references are not misclassified as MDX expressions.
The file must fail before translation when any of the following line-based or
inline patterns are found outside those excluded regions:

1. MDX import/export: a line that matches
   `^\s*(import|export)\s+`.
2. MDX JSX block or element: a line that matches
   `^\s*</?[A-Z][A-Za-z0-9.:-]*(\s|/?>)`.
3. MDX expression: inline text that matches
   `\{[A-Za-z_$][A-Za-z0-9_$]*(?:[.()[\]\w\s+\-*/?:'"]*)?\}`.
4. Markdown directive: a line that matches
   `^\s*:{2,3}[A-Za-z][A-Za-z0-9_-]*\b`.
5. Custom admonition: a line that matches `^\s*!!!\s+\w+`.
6. TOML front matter: the file starts with `+++` before any other byte except a
   UTF-8 byte order mark.
7. JSON front matter: the file starts with `{` before any other byte except a
   UTF-8 byte order mark and the first non-whitespace line appears before any
   Markdown block.

The detection rules intentionally prefer false-positive rejection over unsafe
translation. A rejected file can still be translated through
`--markdown-mode legacy` when the user explicitly accepts whole-document
translation risk.

## Translatable Content

Markdown-aware translation may translate text nodes in these regions:

1. Heading visible text.
2. Paragraph prose.
3. List item prose, excluding list markers, indentation, numbering, and task
   checkbox markers.
4. Block quote prose, excluding quote markers and nested protected content.
5. Table cell prose, excluding table delimiters and alignment markers.
6. Link display text for inline links and full reference links such as
   `[text](destination)` and `[text][id]`, excluding link destination, title,
   reference label, and reference definition.
7. Image alt text for inline images and full reference images such as
   `![alt](destination)` and `![alt][id]`, excluding image destination, title,
   reference label, and reference definition.
8. Footnote body prose, excluding footnote identifiers and reference syntax.
9. Text inside emphasis, strong emphasis, or strikethrough, excluding the
   formatting delimiters.

The implementation must not use language-detection heuristics to discover extra
translatable regions. If text is not emitted by the frozen parser as a text node
inside one of the approved container types above, it is not translatable in v1.
Shortcut reference links such as `[id]` and collapsed reference links such as
`[id][]` are unsupported in v1 because their visible text is also the reference
label. They must fail before translation instead of being translated or silently
preserved.

## Protected Content

Markdown-aware translation must not translate or mutate these regions:

1. Fenced code blocks, including fence markers, fence length, info strings, and
   code content.
2. Indented code blocks.
3. Inline code spans, including delimiters and code content.
4. Link destinations, image destinations, autolinks, URL literals, email
   literals, and URI fragments.
5. Link titles and image titles.
6. Reference labels, reference destinations, and reference titles.
7. Front matter fences, keys, values, comments, and formatting.
8. Raw HTML blocks, inline HTML tags, comments, attributes, and text enclosed by
   raw HTML markup.
9. MDX-like JSX, imports, exports, expressions, directives, and comments, which
   are unsupported in v1 and must fail before translation when detected outside
   raw HTML.
10. Markdown structural delimiters, including heading markers, list markers,
    block quote markers, table pipes, table alignment rows, emphasis markers,
    thematic breaks, and escape characters.
11. Task list checkbox markers such as `[ ]`, `[x]`, and `[X]`.
12. Footnote identifiers and reference syntax.
13. Placeholders, template variables, replacement fields, and format tokens such
    as `{{name}}`, `${name}`, `%NAME%`, and `{0}`.
14. CLI flags, environment variable names, package names, file names, file paths,
    and identifiers that match the frozen token patterns in this document.

Protected content must be preserved byte-for-byte. The v1 requirement is
preservation, not normalization.

## Machine Token Patterns

The following inline tokens are protected when they appear inside otherwise
translatable text nodes:

1. CLI flag: `(?<!\w)--[A-Za-z][A-Za-z0-9-]*(?:=[^\s]+)?`.
2. Short CLI flag group: `(?<!\w)-[A-Za-z](?:[A-Za-z0-9])*\b`.
3. Environment variable: `\b[A-Z][A-Z0-9_]{2,}\b`.
4. Windows environment variable reference: `%[A-Za-z_][A-Za-z0-9_]*%`.
5. Shell variable reference: `\$\{?[A-Za-z_][A-Za-z0-9_]*\}?`.
6. Replacement field: `\{[0-9]+\}`.
7. Template variable: `\{\{[^{}\r\n]+\}\}`.
8. Absolute URL: `https?://[^\s<>)]+`.
9. Windows absolute path: `[A-Za-z]:\\[^\s:*?"<>|]+`.
10. UNC path: `\\\\[^\s\\/:*?"<>|]+\\[^\s:*?"<>|]+`.
11. POSIX absolute path: `/(?:[A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+`.
12. Relative path: `(?:\.{1,2}/)?(?:[A-Za-z0-9._-]+/)+[A-Za-z0-9._-]+`.
13. File name with extension: `\b[A-Za-z0-9._-]+\.[A-Za-z0-9]{1,8}\b`.
14. .NET identifier:
    `\b[A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)+\b`.
15. Package-like identifier:
    `\b[@A-Za-z0-9][A-Za-z0-9._/-]*[A-Za-z0-9]\b` when one of the
    previous three case-insensitive words is `package`, `module`, `namespace`,
    `dependency`, `NuGet`, `npm`, or `Python`.

The implementation must apply these patterns before sending a segment to the
text translator. If overlapping token matches occur, the longest match wins; ties
are resolved by the earliest start offset.

## Segmentation Requirements

1. Markdown-aware translation must segment by Markdown structure, not by raw file
   chunks.
2. A translation segment must not cross a block boundary.
3. A translation segment must not cross a table cell boundary.
4. A translation segment must not cross into code, links, images, front matter,
   raw HTML, MDX-like syntax, reference definitions, or any other protected
   region.
5. Protected inline spans inside otherwise translatable prose must be replaced
   with reversible placeholders before translation.
6. Placeholders must be stable, collision-resistant within the document, and
   unlikely to be interpreted as natural language by a translator.
7. Placeholder restoration must verify that every placeholder appears exactly
   where required. Missing, duplicated, modified, or illegally reordered
   placeholders must cause failure.
8. Segment batching is allowed for efficiency only when reconstruction remains
   deterministic and no protected boundary is crossed.
9. A single extracted segment must not exceed 50,000 Unicode scalar values after
   placeholders are inserted. A segment that exceeds this limit fails validation
   before translation.
10. A batched text translation request must not exceed 50,000 Unicode scalar
    values across all segment texts in that request.

## Translation Backend Requirements

Markdown-aware mode must not submit the full Markdown document to Azure Document
Translation. It must translate extracted prose segments through a text-segment
translator abstraction backed by Azure Translator text translation capability.

The text-segment translator must:

1. Accept one or more extracted text segments plus their placeholder maps.
2. Return exactly one translated result for each input segment.
3. Preserve placeholder tokens in each returned segment so the reconstruction
   layer can validate them.
4. Avoid writing segment text to temporary files.
5. Use Azure Translator Text Translation REST API v3.0 `translate`, not Azure
   Document Translation.
6. Derive the text translation request URI from the validated root custom-domain
   endpoint by appending `/translator/text/v3.0/translate` and adding query
   parameters `api-version=3.0` and `to=<target-language>`.
7. Submit a UTF-8 JSON request body shaped as an array of objects:
   `[{ "Text": "<segment>" }]`.
8. Set `Content-Type` to `application/json; charset=utf-8`.
9. For API key authentication, send the configured key in the
   `Ocp-Apim-Subscription-Key` header.
10. For Entra ID authentication, request a token for
    `https://cognitiveservices.azure.com/.default` through the existing
    Azure Identity credential flow and send it with the `Authorization: Bearer`
    header.
11. Reuse the resolved endpoint, authentication mode, and target language from
    the baseline command. No separate text endpoint, region option, source
    language option, glossary option, or custom model option is in scope for v1.

The existing whole-document `SingleDocumentTranslationClient` path remains the
backend for non-Markdown files and for Markdown files only when the resolved
Markdown mode is `legacy`.

## Reconstruction and Validation Requirements

After translation, the CLI must reconstruct Markdown from the original structure
and translated prose. Before writing the final output, it must validate all of
the following:

1. The output can be parsed as supported Markdown.
2. Protected regions are unchanged.
3. Placeholder restoration is complete.
4. Markdown node ordering is unchanged.
5. Markdown block nesting is unchanged.
6. Link and image destinations are unchanged.
7. Reference definitions still resolve to the same labels and destinations.
8. Table row and column counts are unchanged.
9. Code fences, inline code spans, front matter, raw HTML, task markers, and
   footnote identifiers are unchanged.
10. The output path is not written after any validation failure.

The implementation may allow translated prose to differ in wording, punctuation,
Unicode normalization, or internal spacing. It must not allow those differences
to change Markdown structure or protected content.

## Failure Semantics

Markdown-aware translation must fail closed for these conditions:

1. Markdown parsing fails.
2. The file contains syntax outside the frozen parser feature set.
3. A translatable segment cannot be extracted without crossing a protected
   boundary.
4. The translation service drops, duplicates, mutates, or illegally reorders a
   placeholder.
5. Reconstruction changes protected content.
6. Re-parsing the reconstructed Markdown fails.
7. Structural validation detects changed block order, nesting, table shape, link
   destinations, reference definitions, front matter, code, raw HTML, or
   footnote identifiers.
8. Any segment translation fails.
9. The input contains any shortcut or collapsed reference link.
10. A segment or batched request exceeds the frozen 50,000 Unicode scalar value
    limit.
11. File I/O, authentication, or service errors occur.

Failure must return a non-zero exit code consistent with the baseline CLI error
taxonomy. Markdown validation and reconstruction failures are validation errors
unless they are caused by a lower-level service or file I/O failure.

## Non-Functional Requirements

1. Keep Markdown-aware behavior deterministic and script-friendly.
2. Markdown-aware v1 supports UTF-8 input with or without a UTF-8 byte order
   mark. Invalid UTF-8 and other encodings must fail validation before
   translation.
3. Markdown-aware output must preserve the input's UTF-8 byte order mark
   presence, final newline presence, and original line-ending bytes for
   structural delimiters and protected regions. The implementation must not
   normalize LF to CRLF, CRLF to LF, or mixed line endings.
4. Do not log or print source document content, translated document content,
   secrets, access tokens, or API keys.
5. Do not persist intermediate source or translated prose outside the requested
   input and output paths, except for same-directory temporary output files used
   by the existing atomic write pattern.
6. Prefer async I/O and cancellation-friendly translation calls.
7. Keep the implementation testable without live Azure credentials by using
   translator abstractions and deterministic fake translators.
8. Keep non-Markdown behavior backward-compatible except for the addition of the
   new Markdown mode option.

## Explicit Non-Goals

Markdown-aware v1 does not include:

- Batch translation.
- Multiple input files.
- Multiple target languages.
- Source language override controls.
- Glossary or custom terminology controls.
- Custom translation models.
- Interactive prompts.
- Persistent configuration files.
- A separate text translation endpoint option.
- Full MDX support.
- Translating raw HTML content.
- Translating front matter values.
- TOML or JSON front matter support.
- Translating link or image titles.
- Automatic content-based Markdown detection for extensionless files.
- Best-effort repair of malformed Markdown.
- Silent partial translation.
- Unsafe fallback to whole-document translation.

## Acceptance Criteria

1. A valid `.md`, `.MD`, `.markdown`, or `.MarkDown` input with default settings
   uses Markdown-aware translation and writes a valid translated Markdown output.
2. Extension routing is based on the final input path segment only.
3. A supported non-Markdown input with default settings continues to use the
   legacy document translation path.
4. `--markdown-mode aware` succeeds only for `.md` and `.markdown` inputs.
5. `--markdown-mode legacy` bypasses Markdown-aware translation, bypasses the
   baseline extension allowlist for Markdown extensions, preserves the original
   input file name, and uses content type `text/plain`.
6. `DOCUMENT_TRANSLATOR_MARKDOWN_MODE` provides the same values as the command
   line option, with command-line values taking precedence.
7. Invalid Markdown mode values fail validation before translation.
8. Headings, paragraphs, list prose, block quote prose, table cell prose, link
   text, image alt text, and footnote body prose are translated when they are
   emitted by the frozen parser as text nodes in approved containers.
9. Code fences, indented code blocks, inline code, URLs, link destinations,
   image destinations, reference definitions, front matter, raw HTML, task
   markers, table structure, and footnote identifiers remain unchanged.
10. Placeholder corruption causes a non-zero exit and no final output file.
11. Markdown parse, reconstruction, and structural validation failures cause a
    non-zero exit and no final output file.
12. The output overwrite behavior still requires `--force` when the output file
    already exists.
13. The output path must still differ from the input path.
14. Invalid UTF-8 input causes a non-zero exit before translation.
15. Unsupported MDX, directive, custom admonition, TOML front matter, or JSON
    front matter patterns cause a non-zero exit before translation.
16. Shortcut and collapsed reference links cause a non-zero exit before
    translation.
17. Segment and batched request sizes over 50,000 Unicode scalar values cause a
    non-zero exit before translation.
18. Missing endpoint, invalid endpoint, missing target language, missing API key
    for API key authentication, unsupported authentication mode, and service
    errors keep the baseline behavior and do not print secrets.

## Required Test Coverage

Automated tests must use deterministic fake translators for normal CI. Live
Azure tests, if any are added later, must be opt-in and excluded from normal CI.

The required fake text translator contract is:

1. For segment index `n`, return the marker `TRANSLATED[n]` followed by one
   ASCII space and then the original segment text.
2. Preserve every placeholder token in the original order.
3. Support test variants that deliberately drop, duplicate, mutate, and reorder
   placeholders.

Successful golden-file tests compare the entire output byte-for-byte against the
expected file. Negative tests compare stable error categories and must not assert
full source or translated document content in diagnostics.

At minimum, tests must cover:

1. Markdown mode resolution from command line, environment variables, and
   defaults.
2. Extension routing for lowercase, uppercase, and mixed-case `.md` and
   `.markdown` final path extensions, plus non-Markdown files.
3. Legacy override behavior for Markdown files, including validator bypass,
   original file name preservation, and `text/plain` content type.
4. Parser failures and unsupported syntax failures, including MDX
   import/export, JSX elements, MDX expressions, Markdown directives, custom
   admonitions, TOML front matter, and JSON front matter.
5. Placeholder insertion, restoration, and corruption detection.
6. Golden-file output for headings, paragraphs, lists, block quotes, tables,
   links, images, code fences, inline code, front matter, raw HTML, task lists,
   footnotes, reference links, placeholders, paths, CLI flags, and environment
   variables.
7. Machine token protection for each frozen token pattern, including overlap
   resolution by longest match and earliest start offset.
8. Byte-for-byte preservation of protected regions.
9. Structural validation for table shape, link destinations, reference
   definitions, block ordering, nesting, code fences, front matter, raw HTML,
   task markers, and footnote identifiers.
10. Text translation backend request construction for URI path, query
    parameters, JSON body, content type, API key header, Entra ID bearer token,
    target language, and request batching limits.
11. Shortcut and collapsed reference links fail before translation.
12. Single extracted segments over 50,000 Unicode scalar values fail before
    translation.
13. Batched requests over 50,000 Unicode scalar values fail before translation.
14. Atomic write behavior for success, validation failure, service failure, file
    I/O failure, and cancellation.
15. UTF-8 without byte order mark, UTF-8 with byte order mark, LF, CRLF, mixed
    line endings, final newline, and no-final-newline cases.
16. Backward-compatible behavior for existing non-Markdown supported formats.

## Frozen Decisions

1. Markdown-aware mode is automatically selected for `.md` and `.markdown` by
   default.
2. Users can explicitly force legacy whole-document translation with
   `--markdown-mode legacy`.
3. Users can explicitly require Markdown-aware translation with
   `--markdown-mode aware`.
4. Front matter is protected in v1.
5. Raw HTML is protected in v1; MDX-like syntax is unsupported in v1.
6. Link display text and image alt text are translatable in v1.
7. Link titles and image titles are protected in v1.
8. Markdown-aware translation is all-or-nothing per file.
9. Unsafe fallback to whole-document translation is forbidden unless the user
   explicitly selected `legacy`.
10. Markdown-aware v1 supports UTF-8 only.
11. Markdown-aware v1 uses a text-segment translation backend, not Azure Document
    Translation over the full Markdown file.
12. Markdown-aware v1 uses Azure Translator Text Translation REST API v3.0
    `translate` through the existing Translator resource endpoint.
13. Shortcut and collapsed reference links are unsupported in v1.
14. A single segment or batch may contain at most 50,000 Unicode scalar values.
