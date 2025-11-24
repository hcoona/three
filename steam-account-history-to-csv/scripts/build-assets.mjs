import { cp, mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { getBrowserExtensionVersion, getVersionInfo, projectRoot } from './version-utils.mjs';

const rootDir = projectRoot;
const assetsDir = path.join(rootDir, 'assets');
const distDir = path.join(rootDir, 'dist');
const monorepoRoot = path.resolve(rootDir, '..');
const SHARED_LICENSE_ENTRIES = ['COPYING', 'COPYING.LESSER', 'LICENSES'];

async function loadJson(filePath) {
  const content = await readFile(filePath, 'utf8');
  return JSON.parse(content);
}

async function buildManifest() {
  const [pkg, manifest, versionInfo] = await Promise.all([
    loadJson(path.join(rootDir, 'package.json')),
    loadJson(path.join(assetsDir, 'manifest.json')),
    getVersionInfo(),
  ]);

  const browserVersion = getBrowserExtensionVersion(versionInfo);

  const nextManifest = {
    ...manifest,
    name: pkg.displayName ?? pkg.name ?? manifest.name,
    version: browserVersion,
    description: pkg.description ?? manifest.description,
  };

  await mkdir(distDir, { recursive: true });
  await writeFile(path.join(distDir, 'manifest.json'), `${JSON.stringify(nextManifest, null, 2)}\n`);
}

async function copyIcons() {
  const icons = ['icon-32.png', 'icon-48.png'];
  await Promise.all(
    icons.map(async (icon) => {
      const source = path.join(assetsDir, icon);
      const target = path.join(distDir, icon);
      await cp(source, target);
    }),
  );
}

async function copyEntry(source, target) {
  const info = await stat(source).catch(() => null);
  if (!info) {
    throw new Error(`Missing license artifact at ${source}`);
  }

  await rm(target, { recursive: true, force: true });
  if (info.isDirectory()) {
    await cp(source, target, { recursive: true });
    return;
  }

  await mkdir(path.dirname(target), { recursive: true });
  await cp(source, target);
}

async function copyProjectLicense() {
  const source = path.join(rootDir, 'LICENSE');
  const target = path.join(distDir, 'LICENSE');
  await copyEntry(source, target);
}

async function copySharedLicenses() {
  for (const entry of SHARED_LICENSE_ENTRIES) {
    const source = path.join(monorepoRoot, entry);
    const target = path.join(distDir, entry);
    await copyEntry(source, target);
  }
}

async function main() {
  await buildManifest();
  await copyIcons();
  await copyProjectLicense();
  await copySharedLicenses();
}

await main();
