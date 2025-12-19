import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as nbgv from 'nerdbank-gitversioning';

export const MAX_BROWSER_VERSION_PART = 65535;
const moduleDir = path.dirname(fileURLToPath(import.meta.url));
export const projectRoot = path.resolve(moduleDir, '..');

export async function getVersionInfo(root = projectRoot) {
  const versionInfo = await nbgv.getVersion(root);
  return versionInfo;
}

export function getBrowserExtensionVersion(versionInfo) {
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

  const combined = [...parsedSimple, versionHeight];
  return combined.join('.');
}
