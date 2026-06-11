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
- selected authentication credentials: either an Azure API key or an available Entra ID credential,

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
- `AZURE_TRANSLATOR_AUTH_MODE`, optional, either `api-key` or `entra-id`, defaulting to `api-key`
- `AZURE_TRANSLATOR_KEY`, required only when using `api-key` authentication
- `AZURE_TRANSLATOR_REGION`, optional, used only by the API-key text translation backend when provided

The endpoint must be the root custom-domain Document Translation endpoint: `https://<resource-name>.cognitiveservices.azure.com`. The legacy SDK example input with a trailing `/translator` path is accepted for compatibility and normalized internally to the root endpoint.

Command-line overrides are included in the MVP:

API key authentication:

```bash
document-translator translate \
  --input ./source.docx \
  --output ./source.zh-Hans.docx \
  --target-language zh-Hans \
  --auth-mode api-key \
  --endpoint https://<resource-name>.cognitiveservices.azure.com \
  --region <region> \
  --key <api-key>
```

Entra ID authentication:

```bash
document-translator translate \
  --input ./source.docx \
  --output ./source.zh-Hans.docx \
  --target-language zh-Hans \
  --auth-mode entra-id \
  --endpoint https://<resource-name>.cognitiveservices.azure.com
```

## Functional Requirements

1. The CLI accepts exactly one input file per invocation.
2. The CLI accepts one target language per invocation.
3. The CLI writes the translated document to the requested output path.
4. The CLI creates the output directory when it does not exist.
5. The CLI fails before calling Azure when the input file does not exist.
6. The CLI fails before calling Azure when the output path points to an existing directory.
7. The CLI fails before calling Azure when endpoint, authentication mode, required credentials, or target language is missing or invalid.
8. The CLI passes the original input file name and detected content type to Azure.
9. The CLI returns exit code `0` on success and non-zero on validation, service, or file I/O failure.
10. Error output is concise and actionable.
11. The CLI fails before calling Azure when the input file is larger than the synchronous Document Translation limit of 10 MB.
12. The CLI fails before calling Azure when the input extension is not in the MVP supported-format allowlist.
13. Existing output files fail validation unless `--force` is provided.
14. The output path must not be the same file as the input path.
15. The CLI supports both API key authentication and Entra ID authentication in the MVP.
16. API key authentication requires an API key from `--key` or `AZURE_TRANSLATOR_KEY`.
17. Entra ID authentication uses Azure Identity's default credential chain and does not require an API key.
18. API key text translation sends `Ocp-Apim-Subscription-Region` when `--region` or `AZURE_TRANSLATOR_REGION` is configured; legacy Document Translation ignores region.

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
- Entra ID authentication through Azure Identity.
- Endpoint, authentication mode, and key command-line overrides with environment-variable fallback.
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
- Custom Entra ID credential selection beyond Azure Identity defaults.

## Verified Assumptions and Evidence

These assumptions were checked against official Microsoft documentation.

1. **Endpoint.** Use a Translator resource custom domain endpoint accepted by `SingleDocumentTranslationClient`.
    - Evidence: The Document Translation overview lists a synchronous prerequisite as "A Translator resource with a custom domain endpoint" ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/overview)).
    - Evidence: The `SingleDocumentTranslationClient(Uri, AzureKeyCredential)` and `SingleDocumentTranslationClient(Uri, TokenCredential)` constructor docs describe the endpoint parameter as "Supported document Translation endpoint" and give `https://{TranslatorResourceName}.cognitiveservices.azure.com/translator` as the example ([.NET API reference](https://learn.microsoft.com/en-us/dotnet/api/azure.ai.translation.document.singledocumenttranslationclient.-ctor?view=azure-dotnet)).
    - Evidence: Microsoft Entra authentication docs state that regional endpoints do not support Microsoft Entra authentication ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/how-to/microsoft-entra-id-auth)).
2. **Format and language support.** The service accepts only supported synchronous document formats and supported target language codes.
    - Evidence: The synchronous REST guide says `targetLanguage` "must be one of the supported languages included in the translation scope" and the `document` body must be "Any one of the supported document formats" ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/reference/translate-document)).
    - Evidence: The Document Translation overview lists the synchronous supported formats used by the MVP allowlist, including `.txt`, `.csv`, `.html`, `.docx`, `.pptx`, `.xlsx`, `.msg`, and `.xlf` ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/overview#supported-document-and-glossary-formats)).
    - Evidence: The language support page says "Cloud translation is available in all languages for the `Translate` operation of Text translation and for Document translation" ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/language-support)).
3. **Format preservation.** Azure Document Translation preserves layout and format for supported documents, but this is not a pixel-perfect guarantee and only applies within service-supported formats.
    - Evidence: The overview says Document Translation translates supported documents "while preserving original document structure and data format" and lists "Preserve source file presentation" for synchronous translation as preserving "the original layout and format" ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/overview)).
    - Evidence: The synchronous REST guide says the final response "contains the translated document and is returned directly to the calling client" ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/reference/translate-document)).
    - Caveat: The overview documents legacy file type conversion exceptions for batch translation; those legacy formats are outside this synchronous MVP allowlist ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/overview#supported-document-and-glossary-formats)).
4. **Credential management.** The CLI consumes externally supplied credentials or an externally configured Azure Identity context; it does not store, rotate, or provision credentials itself.
    - Evidence: The overview advises storing subscription keys in a secure location such as Azure Key Vault and avoiding source control ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/overview)).
    - Evidence: .NET authentication guidance says most Foundry tools support key-based authentication and Microsoft Entra ID, and `DefaultAzureCredential` "automatically discovers available Azure credentials based on the current environment and tooling available" ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/azure-ai-services-authentication)).
    - Evidence: Azure SDK authentication guidance says "The Azure Identity library acquires and manages Microsoft Entra tokens for you" and that managed identity avoids needing to manage credentials ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/)).
5. **Source-language detection.** The MVP omits a source-language option and relies on Azure automatic source-language detection, with a documented quality caveat.
    - Evidence: The synchronous REST guide says that if `sourceLanguage` is not specified, "automatic language detection is applied to determine the source language" ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/reference/translate-document)).
    - Evidence: The same guide says Microsoft "strongly recommend[s] specifying it explicitly" because providing source language produces better quality translations than relying on automatic detection ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/ai-services/translator/document-translation/reference/translate-document)).

## Open Questions Deferred Past MVP

1. Whether to add batch mode with Blob Storage.
2. Whether to add a Markdown-aware mode that parses Markdown structure and translates only human-readable prose.
3. Whether to add an explicit `--source-language` option.
4. Whether to infer output file names when `--output` is omitted.
5. Whether to add glossary and custom model support.
6. Whether to package as a .NET tool.

## Acceptance Criteria

1. `translate` with a valid file, endpoint, target language, and selected authentication credentials writes a translated file to the output path.
2. Missing input file exits non-zero without calling Azure.
3. Missing endpoint or required authentication credentials exits non-zero without printing secrets.
4. Service errors show Azure error code and message when available.
5. Existing output files are overwritten only when `--force` is provided.
6. Files over 10 MB fail validation without calling Azure.
7. Unsupported extensions fail validation without calling Azure.
8. API key authentication succeeds when `--auth-mode api-key` and a valid key are provided.
9. Entra ID authentication succeeds when `--auth-mode entra-id` and a valid Azure Identity credential are available.
