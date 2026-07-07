# Document Translator CLI MVP High-Level Design

Status: **Frozen for MVP**

## Overview

`document-translator` is a C# CLI that translates one local document per invocation by calling Azure AI Translator Document Translation synchronous single-document API.

The design intentionally avoids Azure Blob Storage and batch orchestration for the MVP. The CLI reads the local source file, sends it as multipart form data through `SingleDocumentTranslationClient`, receives `Response<BinaryData>`, and writes the response content bytes to the requested output path.

## Reference SDK Behavior

The Azure Document Translation SDK for .NET provides:

- `Azure.AI.Translation.Document` package.
- `SingleDocumentTranslationClient` for synchronous single-document translation.
- `AzureKeyCredential` for API key authentication.
- `TokenCredential` constructors for Entra ID authentication through Azure Identity.
- `MultipartFormFileData` and `DocumentTranslateContent` for sending local document content.
- `TranslateAsync(targetLanguage, content)` returning `Response<BinaryData>`.

The batch `DocumentTranslationClient` requires Azure Blob Storage source and target containers. It is out of scope for the MVP.

The MVP supports both SDK authentication paths: API key authentication through `AzureKeyCredential` and Entra ID authentication through Azure Identity's `DefaultAzureCredential`.

## Proposed Project Location

```text
src/private/app/document-translator-cli/
  docs/
    requirements.md
    high-level-design.md
```

When implementation starts, the expected C# project layout is:

```text
src/private/app/document-translator-cli/
  DocumentTranslatorCli.csproj
  Program.cs
  TranslationOptions.cs
  TranslationCommand.cs
  IDocumentTranslator.cs
  AzureDocumentTranslator.cs
```

## Dependencies

Required:

- `Azure.AI.Translation.Document`
- `Azure.Identity`
- `System.CommandLine`

Optional only if needed by implementation:

- `Microsoft.Extensions.Configuration.EnvironmentVariables`

Repository integration notes:

- Add package versions to root Central Package Management if they are not already present.
- Follow existing private C# app conventions: SDK-style project, `Microsoft.Build.Artifacts`, `OutputType` `Exe`, `TargetFramework` `$(CurrentTargetFramework)`, nullable enabled, implicit usings enabled.

## Command-Line Contract

```bash
document-translator translate \
  --input <path> \
  --output <path> \
  --target-language <language-code> \
  [--auth-mode <api-key|entra-id>] \
  [--endpoint <uri>] \
  [--key <api-key>] \
  [--region <region>] \
  [--force]
```

Environment fallback:

| Option              | Environment variable         | Required                         |
| ------------------- | ---------------------------- | -------------------------------- |
| `--endpoint`        | `AZURE_TRANSLATOR_ENDPOINT`  | Yes                              |
| `--auth-mode`       | `AZURE_TRANSLATOR_AUTH_MODE` | No; defaults to `api-key`        |
| `--key`             | `AZURE_TRANSLATOR_KEY`       | Only when auth mode is `api-key` |
| `--region`          | `AZURE_TRANSLATOR_REGION`    | No; API-key text backend only    |
| `--target-language` | None                         | Yes                              |
| `--input`           | None                         | Yes                              |
| `--output`          | None                         | Yes                              |

`--force` is the only overwrite mechanism. Without it, an existing output file is a validation error.

The endpoint value must be the root custom-domain Document Translation endpoint: `https://<resource-name>.cognitiveservices.azure.com`. The legacy SDK example input with a trailing `/translator` path is accepted for compatibility and normalized internally to the root endpoint.

Authentication mode behavior:

- `api-key` uses `AzureKeyCredential` and requires `--key` or `AZURE_TRANSLATOR_KEY`.
- `api-key` includes `Ocp-Apim-Subscription-Region` for text translation when `--region` or `AZURE_TRANSLATOR_REGION` is configured; legacy Document Translation ignores region.
- `entra-id` uses `DefaultAzureCredential` from Azure Identity and does not require an API key.
- If `--auth-mode` is omitted, the CLI uses `api-key`.

## Runtime Flow

1. Parse command-line options.
2. Resolve endpoint, authentication mode, and API key from options or environment variables.
3. Validate inputs:
    - input file exists,
    - input file is no larger than 10 MB,
    - input extension is in the MVP supported-format allowlist,
    - output path is not a directory,
    - output file does not exist unless `--force` is set,
    - output path is not the same file as the input path,
    - endpoint is an absolute URI,
    - authentication mode is `api-key` or `entra-id`,
    - key is non-empty when authentication mode is `api-key`,
    - target language is non-empty.
4. Create the output directory if needed.
5. Detect content type from file extension using a small static mapping.
6. Open the input file as a read-only stream.
7. Create `SingleDocumentTranslationClient` with `AzureKeyCredential` for `api-key` mode or `DefaultAzureCredential` for `entra-id` mode.
8. Build `MultipartFormFileData` with original file name, stream, and content type.
9. Call `TranslateAsync(targetLanguage, content, cancellationToken)`.
10. Write response content bytes to a temporary file in the output directory, then move it to the output path.
11. Print a short success message to stdout.

## Content-Type Strategy

Use a small allowlist for document types natively supported by Azure synchronous single-document translation:

| Extension        | Content type                                                                |
| ---------------- | --------------------------------------------------------------------------- |
| `.txt`           | `text/plain`                                                                |
| `.tsv`, `.tab`   | `text/tab-separated-values`                                                 |
| `.csv`           | `text/csv`                                                                  |
| `.html`, `.htm`  | `text/html`                                                                 |
| `.mhtml`, `.mht` | `message/rfc822`                                                            |
| `.docx`          | `application/vnd.openxmlformats-officedocument.wordprocessingml.document`   |
| `.pptx`          | `application/vnd.openxmlformats-officedocument.presentationml.presentation` |
| `.xlsx`          | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`         |
| `.msg`           | `application/vnd.ms-outlook`                                                |
| `.xlf`           | `application/xliff+xml`                                                     |

Unknown extensions fail validation in the MVP. This keeps behavior explicit and avoids submitting files with incorrect content types.

PDF is not included because the MVP uses synchronous single-document translation; PDF support belongs to a future batch/Blob Storage workflow.

Markdown is not included in the MVP. Do not submit `.md` files as `text/plain` by default because that would translate code fences, front matter, links, MDX directives, and placeholders. Markdown support should be added later as a Markdown-aware mode that preserves structure and translates only human-readable prose.

## Error Handling

The CLI writes errors to stderr and returns non-zero exit codes.

Suggested exit codes:

| Code | Meaning                          |
| ---- | -------------------------------- |
| `0`  | Success                          |
| `2`  | Command-line or validation error |
| `3`  | Azure service error              |
| `4`  | File I/O error                   |
| `1`  | Unexpected error                 |

Handling rules:

- Validation errors are reported before network calls.
- `RequestFailedException` reports Azure status, error code, and message.
- The API key is never printed.
- Partial output files are avoided by writing to a temporary file in the output directory and moving it into place after the SDK call succeeds.
- Temporary output files use the same directory as the final output path and are deleted on failure or cancellation on a best-effort basis.

## Security Considerations

- Prefer environment variables for credentials in normal usage.
- Do not include keys in logs, exceptions, or success messages.
- If command-line `--key` is implemented, document that shells may persist command history.
- Entra ID mode must not require or log an API key.
- Do not send files other than the explicit input path.
- Do not create telemetry for document content in the MVP.
- Do not use a global temporary directory for translated content.

## Test Strategy

Unit tests:

- option parsing,
- environment fallback,
- authentication mode selection,
- validation failures,
- content-type mapping,
- output overwrite behavior,
- temp-file move behavior.

Integration tests:

- keep live Azure translation tests manual or opt-in because they require credentials and may incur cost.
- use a tiny `IDocumentTranslator` abstraction around `SingleDocumentTranslationClient` for automated command tests so CI does not require Azure credentials.

## Implementation Notes

The Azure SDK client can be created per invocation for the MVP. If future batch or daemon modes are added, reuse a client instance because Azure SDK clients are thread-safe.

Use a small `IDocumentTranslator` abstraction for testability, but avoid a large service container or hosted application pattern unless future requirements justify it.

Set `<AssemblyName>document-translator</AssemblyName>` so the produced executable matches the documented command name.

## Future Extensions

- Batch mode using `DocumentTranslationClient` and Azure Blob Storage.
- Multiple input files.
- Multiple target languages.
- Markdown-aware mode.
- Glossary support.
- Custom translation model support.
- Packaging as a local or published .NET tool.
