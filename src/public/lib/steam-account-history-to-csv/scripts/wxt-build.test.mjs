import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFile, rm } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';

import { getBrowserExtensionVersion, getVersionInfo, projectRoot } from './version-utils.mjs';

test('WXT build emits the NBGV browser manifest version', { timeout: 60_000 }, async () => {
  const manifestPath = path.join(projectRoot, '.output', 'chrome-mv3', 'manifest.json');
  await rm(manifestPath, { force: true });

  const expectedVersion = getBrowserExtensionVersion(await getVersionInfo(projectRoot));
  const result = spawnSync(process.execPath, ['./scripts/nbgv-version.mjs', 'run', 'wxt', 'build'], {
    cwd: projectRoot,
    encoding: 'utf8',
  });

  assert.equal(result.status, 0, result.stderr || result.stdout);
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  assert.equal(manifest.version, expectedVersion);
});
