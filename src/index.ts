function normalizeContent(content: string): string {
  if (content[0] === "\u0008") {
    return content.slice(1)
  } else {
    return content;
  }
}

function parseWalletHistoryTableHeader(table: HTMLTableElement): string[] {
  let tHead = table.tHead;

  let titles0 = tHead.children[0];
  let titles1 = tHead.children[1];

  let arrayTitles0 = <HTMLTableCellElement[]>Array.prototype.slice.call(titles0.children);
  let arrayTitles1 = <HTMLTableCellElement[]>Array.prototype.slice.call(titles1.children);

  let title0Contents = arrayTitles0.map(elem => normalizeContent(elem.textContent));
  let title1Contents = arrayTitles1.map(elem => title0Contents[title0Contents.length - 1] + ":" + normalizeContent(elem.textContent))

  let titleContents = title0Contents.slice(0, title0Contents.length - 1).concat(title1Contents);

  return titleContents;
}

function parseWalletHistoryTableBody(table: HTMLTableElement): string[][] {
  let tBody = table.tBodies[0];
  let rows = (<HTMLTableRowElement[]>Array.prototype.slice.call(tBody.children)).filter(row => row.classList.contains("wallet_table_row"));
  return rows.map(parseWalletHistoryTableRow);
}

function parseWalletHistoryTableRow(row: HTMLTableRowElement): string[] {
  let cells = <HTMLTableCellElement[]>Array.prototype.slice.call(row.children);
  try {
    return [
      parseDate(cells[0]),
      parseItems(cells[1]).join("|"),
      parseType(cells[2]).join("|"),
      parseTotal(cells[3]).join("|"),
      parseWalletChange(cells[4]),
      parseWalletBalance(cells[5])
    ].map(v => '"' + v + '"');
  } catch (e) {
    console.error('Error: "' + (<Error>e).message + '" occurred on row: "' + row + '"');
    throw e
  }
}

function getCellChildrenWithoutPayment(cell: HTMLTableCellElement): HTMLElement[] {
  return (<HTMLElement[]>Array.prototype.slice.call(cell.children))
    .filter(item => !item.classList.contains("wth_payment"));
}

function parseDate(cell: HTMLTableCellElement): string {
  return cell.textContent.trim();
}

function parseItems(cell: HTMLTableCellElement): string[] {
  if (cell.children.length === 0) {
    return [cell.textContent.trim()];
  } else {
    return getCellChildrenWithoutPayment(cell).map(item => item.textContent.trim());
  }
}

function parseType(cell: HTMLTableCellElement): string[] {
  if (cell.children.length === 0) {
    return [cell.textContent.trim()];
  } else {
    return getCellChildrenWithoutPayment(cell).map(item => item.textContent.trim());
  }
}

function parseTotal(cell: HTMLTableCellElement): string[] {
  if (cell.children.length === 0) {
    return [cell.textContent.trim()];
  } else {
    return getCellChildrenWithoutPayment(cell).map(item => item.textContent.trim());
  }
}

function parseWalletChange(cell: HTMLTableCellElement): string {
  return cell.textContent.trim();
}

function parseWalletBalance(cell: HTMLTableCellElement): string {
  return cell.textContent.trim();
}

let exportBtnContent = document.createElement("span");
exportBtnContent.textContent = "Export CSV";

let exportBtn = document.createElement("span");
exportBtn.className = "btnv6_blue_hoverfade btn_small";
exportBtn.appendChild(exportBtnContent);
exportBtn.onclick = () => {
  let table = <HTMLTableElement>document.getElementsByClassName("wallet_history_table")[0];

  try {
    let titleContents = parseWalletHistoryTableHeader(table);
    let bodyContents = parseWalletHistoryTableBody(table);

    let csvContents = [titleContents.join(",")].concat(bodyContents.map(g => g.join(","))).join("\n");
    let csvBlob = new Blob(["\ufeff", csvContents], { type: "text/csv" }); // UTF-8 BOM

    let pom = document.createElement("a");
    pom.href = window.URL.createObjectURL(csvBlob);
    pom.download = "wallet_history.csv";
    document.body.appendChild(pom);
    pom.click();
  } catch (e) {
    console.error(e);
  }
}

let mainContentDiv = document.getElementById("main_content");
mainContentDiv.insertBefore(exportBtn, mainContentDiv.firstChild);
