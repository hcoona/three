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
  assert.equal(manifest.browser_specific_settings, undefined);
});

test('Firefox build declares the AMO add-on and accepts the reviewer version', { timeout: 60_000 }, async () => {
  const manifestPath = path.join(projectRoot, '.output', 'firefox-mv2', 'manifest.json');
  await rm(manifestPath, { force: true });

  const result = spawnSync(process.execPath, ['./scripts/nbgv-version.mjs', 'run', 'wxt', 'build', '-b', 'firefox'], {
    cwd: projectRoot,
    encoding: 'utf8',
    env: { ...process.env, BROWSER_EXTENSION_VERSION: '1.2.0.65535' },
  });

  assert.equal(result.status, 0, result.stderr || result.stdout);
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  assert.equal(manifest.version, '1.2.0.65535');
  assert.deepEqual(manifest.browser_specific_settings, {
    gecko: {
      id: '{ec925b44-57c3-4340-92a8-3fd7d6fe6043}',
      data_collection_permissions: { required: ['none'] },
    },
  });
});
