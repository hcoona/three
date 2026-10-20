/**
 * Copyright 2017 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { existsSync } from 'node:fs';
import { readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import type { ResolvedPublicFile, Wxt } from 'wxt';
import { defineConfig } from 'wxt';

import { getBrowserExtensionVersion, getVersionInfo } from './scripts/version-utils';

const projectRoot = path.dirname(fileURLToPath(import.meta.url));

function findMonorepoRoot(startDir: string): string {
  let current = startDir;
  const { root } = path.parse(current);

  while (true) {
    for (const marker of ['pnpm-workspace.yaml', '.git']) {
      if (existsSync(path.join(current, marker))) {
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

const monorepoRoot = findMonorepoRoot(projectRoot);
const productDir = path.relative(monorepoRoot, projectRoot).split(path.sep).join('/');

// The AMO sources archive has no Git history for NBGV, so a reviewer passes the submitted
// manifest version explicitly when rebuilding it.
const versionOverride = process.env.BROWSER_EXTENSION_VERSION;

export default defineConfig({
  srcDir: 'src',
  targetBrowsers: ['chrome', 'firefox', 'edge'],

  manifest: ({ browser }) => ({
    name: 'Steam Account History to CSV',
    ...(browser === 'firefox' && {
      browser_specific_settings: {
        gecko: {
          id: '{ec925b44-57c3-4340-92a8-3fd7d6fe6043}',
          data_collection_permissions: { required: ['none'] },
        },
      },
    }),
  }),

  zip: {
    artifactTemplate: '{{name}}-{{packageVersion}}-{{browser}}.zip',
    sourcesTemplate: '{{name}}-{{packageVersion}}-sources.zip',
    // AMO reviewers rebuild from the workspace root with its lockfile; see README.md.
    sourcesRoot: monorepoRoot,
    includeSources: [
      'package.json',
      'pnpm-lock.yaml',
      'pnpm-workspace.yaml',
      'mise.toml',
      'COPYING',
      'COPYING.LESSER',
      'LICENSES/**',
      `${productDir}/**`,
    ],
  },

  hooks: {
    'build:manifestGenerated': async (_wxt: Wxt, manifest) => {
      if (versionOverride !== undefined) {
        if (!/^\d+\.\d+\.\d+\.\d+$/.test(versionOverride)) {
          throw new Error(`BROWSER_EXTENSION_VERSION "${versionOverride}" is not a dotted four-part version.`);
        }
        manifest.version = versionOverride;
        return;
      }

      const versionInfo = await getVersionInfo(projectRoot);
      manifest.version = getBrowserExtensionVersion(versionInfo);
    },

    'build:publicAssets': async (_wxt: Wxt, assets: ResolvedPublicFile[]) => {
      const licensesDir = path.join(monorepoRoot, 'LICENSES');

      if (existsSync(licensesDir)) {
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
        { absoluteSrc: path.join(projectRoot, 'CHANGELOG.md'), relativeDest: 'CHANGELOG.md' },
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
