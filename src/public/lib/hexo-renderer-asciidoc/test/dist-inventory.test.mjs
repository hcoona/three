/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  EXPECTED_ROOT_DIST_FILES,
  listDistEntries,
  OPTIONAL_ROOT_DIST_FILES,
  verifyDistInventory,
  verifyExactTarEntries,
  verifyExactTarInventory,
} from '../scripts/validation-utils.mjs';

let dist;

const populateRequired = () => {
  for (const file of EXPECTED_ROOT_DIST_FILES) {
    writeFileSync(path.join(dist, file), `// ${file}\n`);
  }
};

beforeEach(() => {
  dist = mkdtempSync(path.join(tmpdir(), 'dist-inventory-test-'));
  populateRequired();
});

afterEach(() => {
  rmSync(dist, { force: true, recursive: true });
});

describe('recursive dist inventory', () => {
  it('accepts exactly the required root files', () => {
    expect(verifyDistInventory(dist).sort()).toEqual([...EXPECTED_ROOT_DIST_FILES].sort());
  });

  describe('required tarball inventory', () => {
    const required = ['package/README.md', 'package/CHANGELOG.md', 'package/LICENSE'];

    it('accepts exactly one of every required entry', () => {
      expect(() => verifyExactTarEntries(required, required)).not.toThrow();
    });

    it('rejects a missing or duplicate changelog', () => {
      expect(() =>
        verifyExactTarEntries(
          required.filter((entry) => !entry.endsWith('CHANGELOG.md')),
          required,
        ),
      ).toThrow(/CHANGELOG\.md entry, found 0/);
      expect(() => verifyExactTarEntries([...required, 'package/CHANGELOG.md'], required)).toThrow(
        /CHANGELOG\.md entry, found 2/,
      );
    });

    it('rejects unexpected and duplicate optional entries in a complete inventory', () => {
      expect(() => verifyExactTarInventory([...required, 'package/UNEXPECTED'], required)).toThrow(
        /package\/UNEXPECTED/,
      );
      expect(() =>
        verifyExactTarInventory([...required, 'package/dist/index.d.ts.map', 'package/dist/index.d.ts.map'], required, [
          'package/dist/index.d.ts.map',
        ]),
      ).toThrow(/found 2/);
    });
  });

  it('accepts only the permitted declaration maps', () => {
    for (const mapName of OPTIONAL_ROOT_DIST_FILES) {
      const declarationName = mapName.slice(0, -'.map'.length);
      writeFileSync(path.join(dist, declarationName), `//# sourceMappingURL=${mapName}\n`);
      writeFileSync(path.join(dist, mapName), JSON.stringify({ file: declarationName }));
    }
    expect(verifyDistInventory(dist).sort()).toEqual([...EXPECTED_ROOT_DIST_FILES, ...OPTIONAL_ROOT_DIST_FILES].sort());
  });

  it('recursively reports and rejects an unexpected nested artifact', () => {
    mkdirSync(path.join(dist, 'nested', 'deeper'), { recursive: true });
    writeFileSync(path.join(dist, 'nested', 'deeper', 'runtime.js'), 'export {};\n');

    expect(listDistEntries(dist)).toEqual(
      expect.arrayContaining(['nested/', 'nested/deeper/', 'nested/deeper/runtime.js']),
    );
    expect(() => verifyDistInventory(dist)).toThrow(/nested\/deeper\/runtime\.js/);
  });

  it('rejects an unexpected root artifact', () => {
    writeFileSync(path.join(dist, 'index.js.map'), '{}\n');
    expect(() => verifyDistInventory(dist)).toThrow(/index\.js\.map/);
  });
});
