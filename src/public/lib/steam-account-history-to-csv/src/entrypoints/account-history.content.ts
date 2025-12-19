/**
 * Copyright 2017 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { defineContentScript } from 'wxt/utils/define-content-script';

const WALLET_HISTORY_SELECTOR = '.wallet_history_table';
const WALLET_ROW_CLASS = 'wallet_table_row';
const PAYMENT_CLASS = 'wth_payment';
const EXPORT_BUTTON_CLASS = 'btnv6_blue_hoverfade btn_small';
const EXPORT_FILENAME = 'wallet_history.csv';

function normalizeContent(content: string | null | undefined): string {
  if (!content || content.length === 0) {
    return '';
  }

  return content[0] === '\u0008' ? content.slice(1) : content;
}

function getTrimmedText(node: Element): string {
  return normalizeContent(node.textContent).trim();
}

function parseWalletHistoryTableHeader(table: HTMLTableElement): string[] {
  const head = table.tHead;
  if (!head) {
    throw new Error('Wallet history table header is missing.');
  }

  const [firstRow, secondRow] = Array.from(head.rows);
  if (!firstRow || !secondRow) {
    throw new Error('Wallet history header must have two rows.');
  }

  const titleRowValues = Array.from(firstRow.cells).map((cell) => normalizeContent(cell.textContent ?? ''));
  const lastTitle = titleRowValues.at(-1) ?? '';
  const nestedTitles = Array.from(secondRow.cells).map(
    (cell) => `${lastTitle}:${normalizeContent(cell.textContent ?? '')}`,
  );

  return titleRowValues.slice(0, -1).concat(nestedTitles);
}

function parseWalletHistoryTableBody(table: HTMLTableElement): string[][] {
  const body = table.tBodies.item(0);
  if (!body) {
    throw new Error('Wallet history table body is missing.');
  }

  const rows = Array.from(body.rows).filter((row) => row.classList.contains(WALLET_ROW_CLASS));
  return rows.map(parseWalletHistoryTableRow);
}

function parseWalletHistoryTableRow(row: HTMLTableRowElement): string[] {
  const cells = Array.from(row.cells);
  if (cells.length < 6) {
    throw new Error('Wallet history row has insufficient cells.');
  }

  try {
    const values = [
      parseDateCell(cells[0]),
      parseMultiValueCell(cells[1]).join('|'),
      parseMultiValueCell(cells[2]).join('|'),
      parseMultiValueCell(cells[3]).join('|'),
      parseSimpleCell(cells[4]),
      parseSimpleCell(cells[5]),
    ];
    return values.map(quoteCsvValue);
  } catch (error) {
    console.error('Failed to parse wallet history row.', { error, row });
    throw error;
  }
}

function getCellChildrenWithoutPayment(cell: HTMLTableCellElement): HTMLElement[] {
  return Array.from(cell.children).filter(
    (child): child is HTMLElement => child instanceof HTMLElement && !child.classList.contains(PAYMENT_CLASS),
  );
}

function parseDateCell(cell: HTMLTableCellElement): string {
  return getTrimmedText(cell);
}

function parseMultiValueCell(cell: HTMLTableCellElement): string[] {
  if (cell.children.length === 0) {
    return [getTrimmedText(cell)];
  }

  return getCellChildrenWithoutPayment(cell).map((child) => getTrimmedText(child));
}

function parseSimpleCell(cell: HTMLTableCellElement): string {
  return getTrimmedText(cell);
}

function quoteCsvValue(value: string): string {
  const sanitized = value.replace(/"/g, '""');
  return `"${sanitized}"`;
}

function generateCsv(table: HTMLTableElement): string {
  const header = parseWalletHistoryTableHeader(table);
  const rows = parseWalletHistoryTableBody(table);
  const csvLines = [header.join(',')].concat(rows.map((row) => row.join(',')));
  return csvLines.join('\n');
}

function downloadCsv(csvContents: string): void {
  const blob = new Blob(['\ufeff', csvContents], { type: 'text/csv' });
  const anchor = document.createElement('a');
  anchor.href = window.URL.createObjectURL(blob);
  anchor.download = EXPORT_FILENAME;

  const body = document.body;
  if (!body) {
    console.error('Document body is unavailable.');
    return;
  }

  body.appendChild(anchor);
  anchor.click();
  anchor.remove();
}

function handleExportClick(): void {
  const table = document.querySelector<HTMLTableElement>(WALLET_HISTORY_SELECTOR);
  if (!table) {
    console.error('Steam wallet history table not found.');
    return;
  }

  try {
    const csvContents = generateCsv(table);
    downloadCsv(csvContents);
  } catch (error) {
    console.error('Failed to export wallet history.', error);
  }
}

function insertExportButton(): void {
  const exportBtn = document.createElement('span');
  exportBtn.className = EXPORT_BUTTON_CLASS;
  exportBtn.addEventListener('click', handleExportClick);

  const exportLabel = document.createElement('span');
  exportLabel.textContent = 'Export CSV';
  exportBtn.appendChild(exportLabel);

  const mainContent = document.getElementById('main_content');
  if (!mainContent) {
    console.error('Steam wallet main content container not found.');
    return;
  }

  mainContent.insertBefore(exportBtn, mainContent.firstChild);
}

export default defineContentScript({
  matches: ['https://store.steampowered.com/account/history/'],
  runAt: 'document_idle',

  main() {
    insertExportButton();
  },
});
