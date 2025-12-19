# Steam Account History to CSV

This is a lightweight browser extension that converts the Steam account history table into CSV for further analysis in Excel.

## Browser store publishing checklist

### Chrome Web Store

- Before submission, verify the packed extension works locally and double-check the `manifest.json` fields such as `name`, `version`, `icons`, and `description`. Every release must bump the manifest version, and the manifest has to live at the root of the ZIP archive. See [“Prepare to publish”](https://developer.chrome.com/docs/webstore/prepare?hl=zh-cn).
- The developer console accepts ZIP uploads only. Once the package is uploaded, the manifest metadata shown in the “Package” tab becomes read-only—you must update the manifest locally and re-upload to change it. See [“Publish in the Chrome Web Store”](https://developer.chrome.com/docs/webstore/publish?hl=zh-cn).

### Firefox Add-ons (AMO)

- AMO expects all extension files (`manifest.json`, scripts, icons, etc.) to be zipped directly into `.zip/.xpi/.crx` without wrapping the parent directory. Mozilla recommends `web-ext build`, which ignores common junk such as `.git`. See [“Package your extension”](https://extensionworkshop.com/documentation/publish/package-your-extension/).
- Each upload is automatically validated (200 MB maximum). If the submission contains minified or obfuscated code, you must attach a readable source package for review. See [“Submitting an add-on”](https://extensionworkshop.com/documentation/publish/submitting-an-add-on/).

### Microsoft Edge Add-ons

- Publishing to the Microsoft Edge Add-ons store requires a Partner Center developer account and a working prototype of the extension. See [“Publish Microsoft Edge extensions”](https://learn.microsoft.com/zh-cn/microsoft-edge/extensions/publish/publish-extension).
- The uploaded ZIP must include `manifest.json`, icons, and every runtime asset. Make sure manifest fields (`name`, `description`, `short_description`, etc.) match what you intend to display in the store listing. The submission wizard walks through uploading the package, selecting markets, filling out properties/listing texts, and providing testing notes. See [the same article](https://learn.microsoft.com/zh-cn/microsoft-edge/extensions/publish/publish-extension).

All three storefronts require thorough local testing before submission and generally expect a clear privacy disclosure plus demo credentials when applicable.

## Build & package

This package is built with [WXT](https://wxt.dev). WXT generates `manifest.json` from `wxt.config.ts` and the `src/entrypoints/*` files.

There is no checked-in `manifest.json` file. Icons are discovered from `public/` (for example `public/icon-32.png`, `public/icon-48.png`).

Versioning is driven by Nerdbank.GitVersioning (NBGV):

- `manifest.version` is a browser-safe numeric dotted version derived from NBGV's `SimpleVersion` + `VersionHeight`.
- `manifest.version_name` keeps the full stamped NBGV version string (for display in extension UIs).

### Install

Run `pnpm install` from the repo root (recommended). This package also has a `postinstall` hook that runs `wxt prepare` to generate local type definitions.

### Dev

- `pnpm dev` (Chrome)
- `pnpm dev:firefox`
- `pnpm dev:edge`

### Build

- `pnpm build` (Chrome MV3)
- `pnpm build:firefox` (Firefox MV2)
- `pnpm build:edge` (Edge MV3)

Build outputs are written to `.output/`.

### Package (ZIP)

- `pnpm zip` (Chrome)
- `pnpm zip:firefox`
- `pnpm zip:edge`

Zips are created under `.output/` and are ready to upload to the respective stores.
