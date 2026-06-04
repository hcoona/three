# Document Translator CLI MVP Requirements

Status: **Frozen for MVP**

## Goal

Build a small C# command-line application that translates one specified local document and writes the translated document to a specified local output path.

The MVP uses Azure AI Translator Document Translation SDK for .NET, specifically the synchronous single-document API. This avoids Azure Blob Storage setup for the first version.

## Users

- A developer or operator who wants to translate one local document from a terminal.
- The user already has access to an Azure AI Translator resource that supports Document Translation.

## Primary Use Case

Given:

- a local input file path,
- a target language code,
- a local output file path,
- an Azure Document Translation endpoint, and
- an Azure API key,

the CLI sends the file to Azure Translator, waits for the synchronous response, and writes the translated bytes to the output path.

## Command Shape

The MVP exposes one command:

```bash
document-translator translate \
  --input ./source.docx \
  --output ./source.zh-Hans.docx \
  --target-language zh-Hans
```

Configuration comes from environment variables by default:

- `AZURE_TRANSLATOR_ENDPOINT`
- `AZURE_TRANSLATOR_KEY`

The endpoint must be the Document Translation custom domain endpoint shown on the Azure Translator resource page, for example `https://<resource-name>.cognitiveservices.azure.com`.

Command-line overrides are included in the MVP:

```bash
document-translator translate \
  --input ./source.docx \
  --output ./source.zh-Hans.docx \
  --target-language zh-Hans \
  --endpoint https://<resource-name>.cognitiveservices.azure.com \
  --key <api-key>
```

## Functional Requirements

1. The CLI accepts exactly one input file per invocation.
2. The CLI accepts one target language per invocation.
3. The CLI writes the translated document to the requested output path.
4. The CLI creates the output directory when it does not exist.
5. The CLI fails before calling Azure when the input file does not exist.
6. The CLI fails before calling Azure when the output path points to an existing directory.
7. The CLI fails before calling Azure when endpoint, key, or target language is missing.
8. The CLI passes the original input file name and detected content type to Azure.
9. The CLI returns exit code `0` on success and non-zero on validation, service, or file I/O failure.
10. Error output is concise and actionable.
11. The CLI fails before calling Azure when the input file is larger than the synchronous Document Translation limit of 10 MB.
12. The CLI fails before calling Azure when the input extension is not in the MVP supported-format allowlist.
13. Existing output files fail validation unless `--force` is provided.
14. The output path must not be the same file as the input path.

## Non-Functional Requirements

1. Keep the MVP implementation small and dependency-light.
2. Use nullable annotations and repository C# defaults.
3. Avoid logging secrets or echoing the API key.
4. Do not persist source or translated document content outside the requested input and output paths, except for a transient same-directory temporary output file used for atomic writes.
5. Prefer async I/O and cancellation-friendly SDK calls.
6. Keep behavior deterministic and script-friendly.

## MVP Scope

Included:

- C# CLI application.
- One local input file.
- One target language.
- Local output file.
- Azure API key authentication.
- Endpoint and key command-line overrides with environment-variable fallback.
- Synchronous single-document translation through `SingleDocumentTranslationClient`.
- `--force` overwrite control.
- Basic validation and user-facing errors.

Excluded:

- Batch translation.
- Azure Blob Storage source or target containers.
- Multiple input files.
- Multiple target languages.
- Markdown files.
- Glossaries.
- Custom translation models.
- Automatic language detection override controls.
- Progress bars.
- Retry policy customization beyond Azure SDK defaults.
- Interactive prompts.
- Persistent configuration files.
- Secret storage integration.

## Assumptions

1. The Azure resource endpoint is a custom Translator endpoint accepted by the Document Translation SDK.
2. The Azure SDK supports the input document format and the selected target language.
3. The service response preserves the translated document format when the service supports that format.
4. The caller is responsible for managing Azure credentials outside the CLI.
5. The MVP relies on Azure automatic source-language detection.

## Open Questions Deferred Past MVP

1. Whether to add batch mode with Blob Storage.
2. Whether to support Azure Identity authentication.
3. Whether to add a Markdown-aware mode that parses Markdown structure and translates only human-readable prose.
4. Whether to add an explicit `--source-language` option.
5. Whether to infer output file names when `--output` is omitted.
6. Whether to add glossary and custom model support.
7. Whether to package as a .NET tool.

## Acceptance Criteria

1. `translate` with a valid file, endpoint, key, and target language writes a translated file to the output path.
2. Missing input file exits non-zero without calling Azure.
3. Missing endpoint or key exits non-zero without printing the secret.
4. Service errors show Azure error code and message when available.
5. Existing output files are overwritten only when `--force` is provided.
6. Files over 10 MB fail validation without calling Azure.
7. Unsupported extensions fail validation without calling Azure.
