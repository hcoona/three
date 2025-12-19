# Privacy Policy — Steam Account History to CSV

**Last updated:** 2025-12-19

This page describes how the browser extension **Steam Account History to CSV** (the **“Extension”**) handles user data.

> Note: Chrome Web Store requires that privacy policies be accessible via a **public URL** in the Chrome Web Store Developer Dashboard. This Markdown file is intended to be published as a publicly accessible web page (for example, via GitHub Pages).

## Summary

- The Extension runs **only** on: `https://store.steampowered.com/account/history/`.
- The Extension reads the account history table displayed on that page and converts it into a CSV file.
- The CSV is generated **locally on your device** and downloaded to your computer when you click **“Export CSV”**.
- The Extension **does not** transmit your data to the developer or to any third party.
- The Extension **does not** sell or rent user data.

## What data the Extension processes

When you click **“Export CSV”**, the Extension accesses and processes the **content visible on the Steam account history page**, including (depending on what Steam displays):

- Transaction dates/times
- Descriptions / item names
- Amounts / debits / credits
- Balance / totals
- Any other text shown in the account history table

This information may be considered **personal or sensitive user data** (for example, financial/payment-related information) because it reflects your account activity.

## How the Extension uses the data

The Extension uses the page content **only** to:

1. Parse the account history table in the page.
2. Generate a CSV representation of that table.
3. Trigger a file download (`wallet_history.csv`) to your device.

The Extension does **not** use the data for analytics, advertising, profiling, marketing, or any unrelated purpose.

## Data transmission and sharing

- **No external transmission:** The Extension does not send account history data over the network (no upload to servers).
- **No third-party sharing:** The Extension does not share your account history data with any third party.

## Data storage and retention

- The Extension does not store your account history data in extension storage (such as `chrome.storage`) or in a remote database.
- The only persistent copy is the CSV file that **you choose to download**. Storage, retention, and deletion of the downloaded CSV are under your control.

## Permissions and scope

The production build is designed to:

- Inject a content script only on `https://store.steampowered.com/account/history/`.
- Avoid broad permissions not required for the Extension’s single purpose.

## Security

- The Extension’s functionality is implemented locally in the browser.
- Since the Extension does not transmit account history data to servers, there is no server-side storage of your account history by the developer.

## Children’s privacy

The Extension is not directed to children and does not knowingly collect personal information from children.

## Changes to this policy

If the Extension’s data practices change, this policy will be updated accordingly. The “Last updated” date at the top of this page will reflect the most recent revision.

## Contact

If you have questions about this policy, contact:

- **Developer:** Shuai Zhang
- **Email:** zhangshuai.ustc@gmail.com

## Compliance statement (Chrome Web Store User Data Policy)

The Extension’s use of data is limited to its **single purpose** (exporting the Steam account history table to CSV) and is intended to comply with the Chrome Web Store User Data Policy, including the **Limited Use** requirements.
