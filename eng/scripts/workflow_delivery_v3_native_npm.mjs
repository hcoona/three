import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const npa = require('npm-package-arg');
const PackageJson = require('@npmcli/package-json');

const request = JSON.parse(readFileSync(0, 'utf8'));
if (
  request === null ||
  typeof request !== 'object' ||
  Array.isArray(request) ||
  Object.keys(request).sort().join(',') !== 'package,version' ||
  typeof request.package !== 'string' ||
  typeof request.version !== 'string'
) {
  throw new TypeError('invalid fixture coordinate request');
}

const parsed = npa.resolve(request.package, request.version);
const manifest = new PackageJson().fromJSON(JSON.stringify({ name: request.package, version: request.version }));
await manifest.normalize({ strict: true, steps: ['fixName', 'fixVersionField'] });
if (
  parsed.scope !== '@hcoona' ||
  parsed.name !== request.package ||
  parsed.type !== 'version' ||
  manifest.content.name !== request.package ||
  manifest.content.version !== request.version
) {
  throw new Error('fixture coordinates must be canonical exact @hcoona npm coordinates');
}
process.stdout.write(JSON.stringify({ package: parsed.name, version: manifest.content.version }));
