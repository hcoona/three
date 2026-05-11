import { readFileSync } from 'node:fs';
import { defineConfig } from 'wxt';

const packageJson = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as {
  version?: string;
};

function toBrowserManifestVersion(version: string): string {
  const [numericCore] = version.split(/[+-]/, 1);
  const parts = numericCore.split('.');
  if (parts.length < 2 || parts.length > 4) {
    throw new Error(`Version "${version}" must have 2 to 4 numeric parts for browser manifest stamping.`);
  }

  const parsed = parts.map((part) => {
    if (!/^(0|[1-9]\d*)$/.test(part)) {
      throw new Error(`Version part "${part}" is not a browser-compatible integer.`);
    }
    const value = Number.parseInt(part, 10);
    if (value > 65535) {
      throw new Error(`Version part "${part}" exceeds the browser manifest limit 65535.`);
    }
    return value;
  });

  while (parsed.length < 3) {
    parsed.push(0);
  }

  return parsed.join('.');
}

const manifestVersion = toBrowserManifestVersion(packageJson.version ?? '0.0.0');

export default defineConfig({
  manifest: {
    name: 'hcoona-release-smoke-wxt',
    version: manifestVersion,
  },
});
