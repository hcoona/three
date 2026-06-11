# Document Translator CLI MVP Implementation Plan

Status: **Ready for implementation**

## Audience and Intent

This document is written for an AI agent acting as a senior software engineer. It translates the frozen MVP requirements and high-level design into an implementation plan for the currently empty `src/private/app/document-translator-cli` project.

Do not expand the MVP scope while implementing this plan. The first implementation must remain a small C# command-line application that translates exactly one supported local document to exactly one local output path by using Azure AI Translator Document Translation's single-document service operation. The Azure service operation is the synchronous document translation workflow, but the .NET SDK should still be invoked through its cancellation-friendly async API.

## Implementation Principles

1. Keep the implementation small, deterministic, and script-friendly.
2. Validate all user-controlled inputs before opening an Azure SDK client or making a network call.
3. Do not print, log, persist, or otherwise expose API keys or document contents beyond the requested input, output, and same-directory temporary output file.
4. Prefer async I/O and pass cancellation tokens through command execution, file I/O, and Azure SDK calls.
5. Follow the repository's existing C# private application and test project conventions.
6. Keep live Azure tests out of normal CI unless they are explicitly gated and opt-in.

## 1. Project and Dependency Integration

Create the application project at:

```text
src/private/app/document-translator-cli/DocumentTranslatorCli.csproj
```

Use the existing private C# application shape:

- `Sdk="Microsoft.NET.Sdk"`
- `Microsoft.Build.Artifacts`
- `<OutputType>Exe</OutputType>`
- `<TargetFramework>$(CurrentTargetFramework)</TargetFramework>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<Nullable>enable</Nullable>`
- `<AssemblyName>document-translator</AssemblyName>`
- `<RootNamespace>Hcoona.DocumentTranslatorCli</RootNamespace>`
- `InternalsVisibleTo` for `Hcoona.DocumentTranslatorCli.Tests`

Add application package references for:

- `Azure.AI.Translation.Document`
- `Azure.Identity`
- `System.CommandLine`

Add missing root Central Package Management entries for `Azure.AI.Translation.Document` and `Azure.Identity` in `Directory.Packages.props`, preserving the file's alphabetical ordering convention. `System.CommandLine` is already centrally versioned.

Create the test project at:

```text
tests/private/app/document-translator-cli/Hcoona.DocumentTranslatorCli.Tests.csproj
```

Use the newest private app xUnit v3/MTP test shape:

- `Sdk="Microsoft.NET.Sdk"`
- `<TargetFramework>$(CurrentTargetFramework)</TargetFramework>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<Nullable>enable</Nullable>`
- `<RootNamespace>Hcoona.DocumentTranslatorCli.Tests</RootNamespace>`
- `<MSTestAnalysisMode>None</MSTestAnalysisMode>`
- package references for `Microsoft.NET.Test.Sdk`, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Testing.Extensions.TrxReport`, `xunit.v3.mtp-v2`, `xunit.runner.visualstudio`, and `coverlet.collector`
- `PrivateAssets="all"` and the standard `IncludeAssets` list for `xunit.runner.visualstudio` and `coverlet.collector`
- a project reference to `../../../../src/private/app/document-translator-cli/DocumentTranslatorCli.csproj`

No `dirs.proj` edit should be required because the repository already glob-includes projects under `src` and `tests`.

## 2. CLI Surface and Option Resolution

Implement a thin async `Program.cs` entry point that builds a `System.CommandLine` root command with one subcommand:

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

Define the documented options exactly:

| Option              | Purpose                                              |
| ------------------- | ---------------------------------------------------- |
| `--input`           | Local source document path.                          |
| `--output`          | Local translated document path.                      |
| `--target-language` | Azure target language code.                          |
| `--auth-mode`       | `api-key` or `entra-id`; defaults to `api-key`.      |
| `--endpoint`        | Azure Document Translation endpoint.                 |
| `--key`             | Azure Translator API key for API key authentication. |
| `--region`          | API-key text translation resource region.            |
| `--force`           | Allows replacing an existing output file.            |

Resolve configuration with this precedence:

1. Command-line option value.
2. Environment variable fallback.
3. Documented default, where applicable.

Use these environment variables:

| Option        | Environment variable         | Required                          |
| ------------- | ---------------------------- | --------------------------------- |
| `--endpoint`  | `AZURE_TRANSLATOR_ENDPOINT`  | Yes                               |
| `--auth-mode` | `AZURE_TRANSLATOR_AUTH_MODE` | No; default is `api-key`          |
| `--key`       | `AZURE_TRANSLATOR_KEY`       | Only for `api-key` authentication |
| `--region`    | `AZURE_TRANSLATOR_REGION`    | No; API-key text backend only     |

Keep command parsing separate from command execution so tests can cover parse failures, option resolution, validation, and execution independently.

Treat malformed command lines as user errors. Bare root invocation without `translate`, unknown commands, unknown options, invalid option value types, and missing or invalid required arguments must write concise stderr and return exit code `2`. `--help` for the root command or `translate` command should remain a normal help request and return exit code `0`. The root command must not silently display help with success when the user omitted the required `translate` subcommand. Normalize parse and dispatch failures independently of `System.CommandLine` defaults so the frozen exit-code contract is stable.

## 3. Validation

Create a resolved options model, such as `TranslationOptions`, and a validator that returns structured validation errors for expected user mistakes. Do not throw for normal validation failures.

Run validation before constructing an Azure SDK client, opening a network connection, or creating output files. Validate all of the following:

1. Input path is provided.
2. Input path exists and is a file.
3. Input file length is no larger than `10 * 1024 * 1024` bytes.
4. Input extension is in the MVP allowlist.
5. Output path is provided.
6. Output path does not point to an existing directory.
7. Output file does not already exist unless `--force` is set.
8. Canonical full input and output paths do not refer to the same path.
9. Endpoint is provided and has a supported Document Translation endpoint shape.
10. Authentication mode is either `api-key` or `entra-id`, case-insensitively.
11. API key is provided and non-empty when authentication mode is `api-key`.
12. API key is not required when authentication mode is `entra-id`.
13. Target language is provided, non-empty, and syntactically valid as a language tag.

Normalize the input extension with invariant lowercasing before allowlist and content-type lookup. Preserve the original input file name when passing the file to Azure.

Use `Path.GetFullPath` and a platform-aware path comparison for same-path detection. Do not rely on file identity APIs that require the output file to already exist.

Validate the endpoint locally against the root custom-domain shape used by this MVP: `https://<resource-name>.cognitiveservices.azure.com`. Also accept the legacy SDK example input `https://<resource-name>.cognitiveservices.azure.com/translator`, then normalize the internal endpoint to the root custom-domain endpoint. Require HTTPS, a non-empty resource-name host label, the `cognitiveservices.azure.com` host suffix, no userinfo, no non-default port, no query, and no fragment. Reject unrelated HTTPS URLs, hosts outside the `cognitiveservices.azure.com` domain, and any path other than root or `/translator`. Do not attempt a network probe during validation, and do not hard-code a specific resource name.

Validate the target language with a small local syntax check, not a live Azure language lookup. Accept common Azure language tags such as `en`, `fr`, `zh-Hans`, and `pt-BR`; reject empty values, whitespace-only values, values with spaces, and values with punctuation outside language-tag syntax. Do not attempt to prove that the tag is currently supported by Azure during MVP validation.

## 4. Content-Type Mapping

Use a small static mapping for the supported synchronous single-document formats:

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

Unknown extensions must fail validation before Azure is called.

Do not include PDF in the MVP because the MVP uses synchronous single-document translation. Do not include Markdown because Markdown requires a future structure-aware mode.

## 5. Translation Abstraction and Azure Implementation

Define a small `IDocumentTranslator` abstraction for testability. It should represent one translation request and accept:

- target language
- input stream
- original input file name
- detected content type
- resolved endpoint
- resolved authentication settings
- cancellation token

Return the translated content as `BinaryData` or equivalent byte content.

Implement `AzureDocumentTranslator` with one SDK client per invocation:

- use `SingleDocumentTranslationClient(endpoint, new AzureKeyCredential(key))` for `api-key`
- use `SingleDocumentTranslationClient(endpoint, new DefaultAzureCredential())` for `entra-id`
- create `MultipartFormFileData` with the original file name, input stream, and detected content type
- wrap it in `DocumentTranslateContent`
- call `TranslateAsync` with the target language, `DocumentTranslateContent`, default optional translation parameters, and a named `cancellationToken` argument so the SDK overload is unambiguous

Do not introduce a dependency injection host or large service container for the MVP unless implementation constraints require it. A small composition root is sufficient.

## 6. File Output and Atomicity

Create output directories only after validation succeeds and before writing translated bytes.

Write translated content through a small output persistence abstraction, such as an internal output writer interface plus a file-system implementation. The abstraction should cover the whole temporary-write workflow, including temporary path creation, byte writing, final move or replace, cleanup, and failure injection for tests. This seam keeps command tests deterministic without relying on brittle real-disk failure scenarios.

The file-system implementation is responsible for:

1. Creating a unique temporary file in the final output directory.
2. Writing translated bytes asynchronously to the temporary file.
3. Moving the temporary file to the final output path after the write succeeds.
4. Replacing the target only when `--force` is set.
5. Best-effort cleanup of the temporary file after failures or cancellation.

Do not use a global temporary directory for translated content. The temporary file must be in the same directory as the requested output path to preserve atomic move behavior on the target volume.

## 7. Command Orchestration and Error Handling

Implement a command service, such as `TranslationCommand`, that sequences the operation:

1. Parse command-line input.
2. Resolve options and environment fallback.
3. Validate all inputs.
4. Create the output directory if needed.
5. Detect the content type.
6. Open the input stream read-only.
7. Translate through `IDocumentTranslator`.
8. Write through the atomic output helper.
9. Print a short success message to stdout.

Map outcomes to the documented exit codes:

| Exit code | Meaning                                      |
| --------- | -------------------------------------------- |
| `0`       | Success                                      |
| `2`       | Command-line parse error or validation error |
| `3`       | Azure service or Azure authentication error  |
| `4`       | File I/O or path-related error               |
| `1`       | Unexpected error or cancellation             |

For `RequestFailedException`, include Azure status, error code, and message when available. For Azure Identity failures, such as `AuthenticationFailedException` or `CredentialUnavailableException`, print a concise message indicating that Entra ID credential acquisition failed. Do not dump environment details, token details, or secrets.

Treat `IOException`, `UnauthorizedAccessException`, `PathTooLongException`, and similar path or file-access failures as file I/O errors with exit code `4`.

Handle cancellation explicitly. `OperationCanceledException` must trigger best-effort temporary file cleanup and return exit code `1` with a concise cancellation message. Cleanup failures must not override the primary cancellation result. Do not report user cancellation as an unexpected crash.

Keep stderr concise and actionable. Keep stdout short on success, such as reporting the written output path.

## 8. Automated Tests

Use fake translator implementations for automated command tests so CI does not require Azure credentials or incur Azure cost.

Cover at least these test groups:

1. CLI parse errors return exit code `2`.
2. Environment fallback and command-line precedence.
3. Authentication mode defaulting and case-insensitive normalization.
4. API key requirement for `api-key` mode.
5. No API key requirement for `entra-id` mode.
6. Missing input, missing output, missing endpoint, malformed endpoint, and missing target language validation.
7. File too large validation.
8. Unsupported extension validation.
9. Case-insensitive supported extension lookup.
10. Output path is an existing directory.
11. Existing output file requires `--force`.
12. Input and output paths must not be the same path.
13. Invalid target language syntax fails validation.
14. The translator receives the original input file name and detected content type.
15. Validation failures do not call Azure.
16. Successful fake translation writes the requested output.
17. Failed fake translation leaves no final or partial output.
18. Temporary output cleanup is best-effort and does not mask the primary error.
19. The output writer seam supports deterministic tests for atomic replace, cleanup, overwrite handling, and file I/O failures.
20. `RequestFailedException` maps to exit code `3`.
21. Azure Identity credential failures map to exit code `3`.
22. File I/O failures map to exit code `4`.
23. Cancellation cleans up temporary output, suppresses cleanup failures as secondary, and returns exit code `1`.
24. Success maps to exit code `0`.

Keep true live Azure translation tests out of the normal test suite. If a live smoke test is added later, gate it behind an explicit opt-in environment variable such as `DOCUMENT_TRANSLATOR_RUN_LIVE_TESTS=true` and a trait or category that normal CI excludes by default.

## 9. Implementation Validation Commands

After implementation, run targeted validation first:

```powershell
dotnet build .\src\private\app\document-translator-cli\DocumentTranslatorCli.csproj
dotnet test .\tests\private\app\document-translator-cli\Hcoona.DocumentTranslatorCli.Tests.csproj
```

If time and environment constraints allow, also run the broader repository gate that is appropriate for C# changes, such as:

```powershell
dotnet build .\dirs.proj
```

Update generated package lock files when restore changes require it.

## 10. Explicit Non-Goals

Do not implement these features in the MVP:

- batch translation
- Azure Blob Storage source or target containers
- multiple input files
- multiple target languages
- Markdown translation
- PDF translation
- glossaries
- custom translation models
- explicit source-language selection
- progress bars
- interactive prompts
- persistent configuration files
- secret storage integration
- custom Entra ID credential selection
- retry customization beyond Azure SDK defaults
- packaging as a published .NET tool
