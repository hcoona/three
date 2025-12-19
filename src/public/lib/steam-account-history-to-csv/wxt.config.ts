/**
 * Copyright 2017 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { access, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { ResolvedPublicFile, Wxt } from 'wxt';
import { defineConfig } from 'wxt';

import { getBrowserExtensionVersion, getVersionInfo } from './scripts/version-utils';

const projectRoot = path.dirname(fileURLToPath(import.meta.url));

async function pathExists(candidate: string): Promise<boolean> {
  try {
    await access(candidate);
    return true;
  } catch {
    return false;
  }
}

async function findMonorepoRoot(startDir: string): Promise<string> {
  let current = startDir;
  const { root } = path.parse(current);

  while (true) {
    for (const marker of ['pnpm-workspace.yaml', '.git']) {
      if (await pathExists(path.join(current, marker))) {
        return current;
      }
    }

    if (current === root) {
      break;
    }

    current = path.dirname(current);
  }

  throw new Error(`Unable to locate pnpm workspace root when starting from ${startDir}.`);
}

export default defineConfig({
  srcDir: 'src',
  targetBrowsers: ['chrome', 'firefox', 'edge'],

  manifest: {
    name: 'Steam Account History to CSV',
  },

  zip: {
    artifactTemplate: '{{name}}-{{packageVersion}}-{{browser}}.zip',
    sourcesTemplate: '{{name}}-{{packageVersion}}-sources.zip',
  },

  hooks: {
    'build:manifestGenerated': async (_wxt: Wxt, manifest) => {
      // Chrome/Edge require a numeric dotted version. Use NBGV's SimpleVersion + VersionHeight.
      const versionInfo = await getVersionInfo(projectRoot);
      manifest.version = getBrowserExtensionVersion(versionInfo);
    },

    'build:publicAssets': async (_wxt: Wxt, assets: ResolvedPublicFile[]) => {
      const monorepoRoot = await findMonorepoRoot(projectRoot);
      const licensesDir = path.join(monorepoRoot, 'LICENSES');

      if (await pathExists(licensesDir)) {
        const entries = await readdir(licensesDir, { withFileTypes: true });
        for (const entry of entries) {
          if (!entry.isFile()) {
            continue;
          }

          assets.push({
            absoluteSrc: path.join(licensesDir, entry.name),
            relativeDest: `LICENSES/${entry.name}`,
          });
        }
      }

      assets.push(
        { absoluteSrc: path.join(projectRoot, 'PRIVACY.md'), relativeDest: 'PRIVACY.md' },
        { absoluteSrc: path.join(projectRoot, 'README.user.md'), relativeDest: 'README.md' },
        { absoluteSrc: path.join(projectRoot, 'LICENSE'), relativeDest: 'LICENSE' },
        { absoluteSrc: path.join(monorepoRoot, 'COPYING'), relativeDest: 'COPYING' },
        { absoluteSrc: path.join(monorepoRoot, 'COPYING.LESSER'), relativeDest: 'COPYING.LESSER' },
      );
    },
  },

  modules: ['@wxt-dev/auto-icons'],
});
