# Steam Account History to CSV (User Guide)

This browser extension adds an **“Export CSV”** button to Steam’s **Account History** page and lets you download the table as a CSV file for analysis in Excel, Google Sheets, Numbers, etc.

## What it works on

- **Only this page:** `https://store.steampowered.com/account/history/`
- The extension does **not** run on other Steam pages.

## How to use

1. Sign in to Steam in your browser.
2. Open **Account History**:
    - `https://store.steampowered.com/account/history/`
3. Wait for the page to load.
4. Click **Export CSV**.
5. Your browser downloads a file named: `wallet_history.csv`.

## What’s inside the CSV

- The first line is a header row.
- Each subsequent line represents one account history row from Steam.
- Values are **quoted** (wrapped in `"..."`) to keep commas safe.
- The file includes a UTF-8 BOM so Excel is more likely to open it with correct encoding.

### Columns

Steam’s Account History table layout can change over time. The extension exports whatever Steam shows on the page. In general, the CSV includes:

- Date
- Type / Action
- Details / Description
- Change (debit/credit)
- Balance

### Multiple values in one cell

Some Steam cells contain multiple lines (for example, multiple items). The extension exports them joined with a pipe character:

- Example: `"Item A|Item B|Item C"`

## Tips for Excel / Sheets

- If Excel asks, import as **UTF-8**.
- If your locale uses `;` as a CSV separator, you may need to use Excel’s **Import** wizard rather than double-clicking the file.

## Troubleshooting

### I don’t see the “Export CSV” button

- Confirm you are on: `https://store.steampowered.com/account/history/` (exact page).
- Refresh the page.
- Make sure the extension is enabled.
- Try disabling other Steam-related extensions that might modify the page.

### The exported data looks wrong

- Steam sometimes loads content dynamically; wait until the history table is visible before exporting.
- If Steam changes the table structure, the extension may need an update.

## Privacy

- The export happens **locally in your browser**.
- The extension does **not** send your account history anywhere.

Read the full policy in `PRIVACY.md`.

## Permissions

The extension is designed to run only on the Steam Account History page to read the visible table and trigger a download.

## License

See `LICENSE` and the bundled `COPYING` / `COPYING.LESSER` files for licensing details.
