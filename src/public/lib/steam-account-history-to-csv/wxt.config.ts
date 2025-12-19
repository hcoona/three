/**
 * Copyright 2017 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { access, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as nbgv from 'nerdbank-gitversioning';
import type { ResolvedPublicFile, Wxt } from 'wxt';
import { defineConfig } from 'wxt';

const MAX_BROWSER_VERSION_PART = 65535;
const projectRoot = path.dirname(fileURLToPath(import.meta.url));

type NbgvVersionInfo = Awaited<ReturnType<typeof nbgv.getVersion>>;

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

function getBrowserExtensionVersion(versionInfo: NbgvVersionInfo): string {
  const simpleVersion = versionInfo?.simpleVersion;
  const versionHeightRaw = versionInfo?.versionHeight;

  if (typeof simpleVersion !== 'string' || simpleVersion.length === 0) {
    throw new Error('nbgv did not return a SimpleVersion string. Ensure version.json is configured correctly.');
  }

  const simpleParts = simpleVersion.split('.');
  if (simpleParts.length < 2 || simpleParts.length > 3) {
    throw new Error(`SimpleVersion "${simpleVersion}" must contain 2 or 3 numeric segments.`);
  }

  const parsedSimple = simpleParts.map((segment) => {
    if (!/^\d+$/.test(segment)) {
      throw new Error(`SimpleVersion segment "${segment}" is not numeric.`);
    }

    const value = Number.parseInt(segment, 10);
    if (!Number.isFinite(value) || value < 0 || value > MAX_BROWSER_VERSION_PART) {
      throw new Error(`SimpleVersion segment "${segment}" must be between 0 and ${MAX_BROWSER_VERSION_PART}.`);
    }

    return value;
  });

  while (parsedSimple.length < 3) {
    parsedSimple.push(0);
  }

  const versionHeight = Number.parseInt(`${versionHeightRaw ?? 0}`, 10);
  if (!Number.isFinite(versionHeight) || versionHeight < 0 || versionHeight > MAX_BROWSER_VERSION_PART) {
    throw new Error(
      `VersionHeight "${versionHeightRaw}" must be a non-negative integer not exceeding ${MAX_BROWSER_VERSION_PART}.`,
    );
  }

  return [...parsedSimple, versionHeight].join('.');
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
      const versionInfo = await nbgv.getVersion(projectRoot);
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

      // Keep license artifacts in the final build output for compliance.
      assets.push(
        { absoluteSrc: path.join(projectRoot, 'LICENSE'), relativeDest: 'LICENSE' },
        { absoluteSrc: path.join(monorepoRoot, 'COPYING'), relativeDest: 'COPYING' },
        { absoluteSrc: path.join(monorepoRoot, 'COPYING.LESSER'), relativeDest: 'COPYING.LESSER' },
      );
    },
  },
});
