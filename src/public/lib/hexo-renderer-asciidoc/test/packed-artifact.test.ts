/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

const packageRoot = path.resolve(import.meta.dirname, '..');
const distDirectory = path.join(packageRoot, 'dist');
const expectedDistFiles = ['index.cjs', 'index.d.cts', 'index.d.cts.map', 'index.d.ts', 'index.d.ts.map', 'index.js'];
const expectedHexoVersion = '8.1.2';
const expectedPnpmVersion = execFileSync('pnpm', ['--version'], { encoding: 'utf8' }).trim();
const sourceManifest = JSON.parse(readFileSync(path.join(packageRoot, 'package.json'), 'utf8')) as {
  dependencies?: { '@asciidoctor/core'?: string };
};
const expectedAsciidoctorCoreVersion = sourceManifest.dependencies?.['@asciidoctor/core'];

if (typeof expectedAsciidoctorCoreVersion !== 'string') {
  throw new Error('Source package must declare an exact @asciidoctor/core dependency.');
}

const probeText = `
== Packed Artifact ==

[source,javascript]
----
const value = { nested: true };
----
`;
const expectedHtml = `<div class="sect1">
<h2 id="_packed_artifact">Packed Artifact</h2>
<div class="sectionbody">
<div class="listingblock">
<div class="content">
<pre><code class="highlight javascript"><span class="keyword">const</span> value = &#123; <span class="attr">nested</span>: <span class="literal">true</span> &#125;;</code></pre>
</div>
</div>
</div>
</div>`;

let temporaryDirectory: string;
let artifactDirectory: string;
let consumerDirectory: string;
let distWasAbsentBeforeBuild = false;
let builtDistFiles: string[] = [];
let packedManifest: {
  dependencies?: { '@asciidoctor/core'?: string };
  exports?: {
    '.': {
      import?: { default?: string; types?: string };
      require?: { default?: string; types?: string };
    };
  };
  main?: string;
  name?: string;
  types?: string;
  version?: string;
};
let packedArtifact: { inventory: string[]; sha256: string; version: string };
let consumerManifest: {
  dependencies: { hexo: string; 'hexo-renderer-asciidoc': string };
  name: string;
  packageManager: string;
  private: boolean;
};

const runNodeProbe = (scriptName: string, source: string): Record<string, unknown> => {
  const scriptPath = path.join(consumerDirectory, scriptName);
  writeFileSync(scriptPath, source);
  const stdout = execFileSync(process.execPath, [scriptPath], {
    cwd: consumerDirectory,
    encoding: 'utf8',
  });
  return JSON.parse(stdout) as Record<string, unknown>;
};

beforeAll(() => {
  temporaryDirectory = mkdtempSync(path.join(tmpdir(), 'hexo-renderer-asciidoc-packed-v4-'));
  artifactDirectory = path.join(temporaryDirectory, 'artifact');
  consumerDirectory = path.join(temporaryDirectory, 'consumer');
  mkdirSync(artifactDirectory);
  mkdirSync(consumerDirectory);

  if (existsSync(distDirectory)) {
    rmSync(distDirectory, { recursive: true, force: true });
  }
  distWasAbsentBeforeBuild = !existsSync(distDirectory);
  if (!distWasAbsentBeforeBuild) {
    throw new Error('dist/ must be absent before the packed-artifact build');
  }

  execFileSync('pnpm', ['run', 'build'], { cwd: packageRoot, stdio: 'pipe' });
  builtDistFiles = readdirSync(distDirectory).sort();

  execFileSync('pnpm', ['pack', '--pack-destination', artifactDirectory], { cwd: packageRoot, stdio: 'pipe' });
  const archiveEntries = readdirSync(artifactDirectory)
    .filter((entry) => entry.endsWith('.tgz'))
    .sort();
  const archivePath = path.join(artifactDirectory, archiveEntries[0] ?? '');
  if (!existsSync(archivePath)) {
    throw new Error('npm pack did not produce the expected v4 artifact');
  }

  packedManifest = JSON.parse(
    execFileSync('tar', ['-xOzf', archivePath, 'package/package.json'], { encoding: 'utf8' }),
  ) as typeof packedManifest;
  const version = packedManifest.version;
  if (typeof version !== 'string') {
    throw new Error('packed artifact does not contain a package version');
  }
  packedArtifact = {
    inventory: execFileSync('tar', ['-tzf', archivePath], { encoding: 'utf8' }).trim().split('\n'),
    sha256: createHash('sha256').update(readFileSync(archivePath)).digest('hex'),
    version,
  };

  consumerManifest = {
    name: 'hexo-renderer-asciidoc-packed-v4-consumer',
    private: true,
    packageManager: `pnpm@${expectedPnpmVersion}`,
    dependencies: {
      hexo: expectedHexoVersion,
      'hexo-renderer-asciidoc': `file:${archivePath}`,
    },
  };
  writeFileSync(path.join(consumerDirectory, 'package.json'), `${JSON.stringify(consumerManifest, undefined, 2)}\n`);
  execFileSync('pnpm', ['install', '--lockfile-only', '--ignore-scripts'], {
    cwd: consumerDirectory,
    stdio: 'pipe',
  });
  execFileSync('pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], {
    cwd: consumerDirectory,
    stdio: 'pipe',
  });
}, 60_000);

afterAll(() => {
  rmSync(temporaryDirectory, { recursive: true, force: true });
  rmSync(distDirectory, { recursive: true, force: true });
});

describe('packed Asciidoctor v4 package import shapes', () => {
  it('records the initial dist state, builds a fresh dist, and pins the v4 runtime dependency', () => {
    expect(distWasAbsentBeforeBuild).toBe(true);
    expect(builtDistFiles).toEqual(expectedDistFiles);
    for (const declarationName of ['index.d.ts', 'index.d.cts']) {
      const mapName = `${declarationName}.map`;
      const declarationMap = JSON.parse(readFileSync(path.join(distDirectory, mapName), 'utf8')) as {
        file?: string;
      };
      expect(declarationMap.file).toBe(declarationName);
      expect(readFileSync(path.join(distDirectory, declarationName), 'utf8')).toContain(
        `//# sourceMappingURL=${mapName}`,
      );
    }

    expect(expectedHtml).toContain('<h2 id="_packed_artifact">Packed Artifact</h2>');
    expect(expectedHtml).toContain('&#123;');
    expect(expectedHtml).toContain('&#125;');
    expect(expectedHtml).toContain('<code class="highlight javascript">');

    expect(packedManifest.name).toBe('hexo-renderer-asciidoc');
    expect(packedManifest.dependencies?.['@asciidoctor/core']).toBe(expectedAsciidoctorCoreVersion);
    expect(packedManifest.main).toBe('./dist/index.cjs');
    expect(packedManifest.types).toBe('./dist/index.d.ts');
    expect(packedManifest.exports?.['.']?.import?.default).toBe('./dist/index.js');
    expect(packedManifest.exports?.['.']?.import?.types).toBe('./dist/index.d.ts');
    expect(packedManifest.exports?.['.']?.require?.default).toBe('./dist/index.cjs');
    expect(packedManifest.exports?.['.']?.require?.types).toBe('./dist/index.d.cts');
    expect(packedArtifact.version).toBe(packedManifest.version);
    expect(packedArtifact.inventory).toEqual(
      expect.arrayContaining(['package/package.json', ...expectedDistFiles.map((file) => `package/dist/${file}`)]),
    );
    expect(packedArtifact.sha256).toMatch(/^[\da-f]{64}$/);
    expect(consumerManifest.dependencies.hexo).toBe(expectedHexoVersion);
    expect(existsSync(path.join(consumerDirectory, 'pnpm-lock.yaml'))).toBe(true);
  });

  it('invokes CommonJS default, named, and registered renderers with identical HTML', () => {
    const probe = runNodeProbe(
      'commonjs.cjs',
      `const sample = ${JSON.stringify(probeText)};
const packageRoot = require('hexo-renderer-asciidoc');
const registrations = [];
const registerResult = packageRoot.registerRenderer({
  extend: {
    renderer: {
      register(...args) {
        registrations.push(args);
      },
    },
  },
});
const render = (fn) => fn({ text: sample });
(async () => {
  process.stdout.write(JSON.stringify({
    rootType: typeof packageRoot,
    keys: Object.keys(packageRoot).sort(),
    defaultType: typeof packageRoot.default,
    rendererType: typeof packageRoot.renderer,
    registerRendererType: typeof packageRoot.registerRenderer,
    defaultEqualsRenderer: packageRoot.default === packageRoot.renderer,
    registerResultIsUndefined: registerResult === undefined,
    outputs: {
      default: await render(packageRoot.default),
      named: await render(packageRoot.renderer),
      registered: await Promise.all(registrations.map(async ([extension, outputFormat, registeredRenderer, sync]) => ({
        extension,
        outputFormat,
        rendererIsPublic: registeredRenderer === packageRoot.renderer,
        sync,
        html: await render(registeredRenderer),
      }))),
    },
  }));
})();`,
    );

    expect(probe).toEqual({
      rootType: 'object',
      keys: ['default', 'registerRenderer', 'renderer'],
      defaultType: 'function',
      rendererType: 'function',
      registerRendererType: 'function',
      defaultEqualsRenderer: true,
      registerResultIsUndefined: true,
      outputs: {
        default: expectedHtml,
        named: expectedHtml,
        registered: [
          { extension: 'ad', outputFormat: 'html', rendererIsPublic: true, sync: false, html: expectedHtml },
          { extension: 'adoc', outputFormat: 'html', rendererIsPublic: true, sync: false, html: expectedHtml },
          { extension: 'asciidoc', outputFormat: 'html', rendererIsPublic: true, sync: false, html: expectedHtml },
        ],
      },
    });
  });

  it('invokes the ESM nested default, named, namespace, and registered renderers with identical HTML', () => {
    const probe = runNodeProbe(
      'module.mjs',
      `const sample = ${JSON.stringify(probeText)};
import packageDefault, { registerRenderer, renderer } from 'hexo-renderer-asciidoc';
const registrations = [];
const registerResult = registerRenderer({
  extend: {
    renderer: {
      register(...args) {
        registrations.push(args);
      },
    },
  },
});
const render = (fn) => fn({ text: sample });
process.stdout.write(JSON.stringify({
  defaultType: typeof packageDefault,
  rendererType: typeof renderer,
  registerRendererType: typeof registerRenderer,
  defaultEqualsRenderer: packageDefault === renderer,
  registerResultIsUndefined: registerResult === undefined,
  outputs: {
    default: await render(packageDefault),
    named: await render(renderer),
    registered: await Promise.all(registrations.map(async ([extension, outputFormat, registeredRenderer, sync]) => ({
      extension,
      outputFormat,
      rendererIsPublic: registeredRenderer === renderer,
      sync,
      html: await render(registeredRenderer),
    }))),
  },
}));`,
    );

    expect(probe).toEqual({
      defaultType: 'function',
      rendererType: 'function',
      registerRendererType: 'function',
      defaultEqualsRenderer: true,
      registerResultIsUndefined: true,
      outputs: {
        default: expectedHtml,
        named: expectedHtml,
        registered: [
          { extension: 'ad', outputFormat: 'html', rendererIsPublic: true, sync: false, html: expectedHtml },
          { extension: 'adoc', outputFormat: 'html', rendererIsPublic: true, sync: false, html: expectedHtml },
          { extension: 'asciidoc', outputFormat: 'html', rendererIsPublic: true, sync: false, html: expectedHtml },
        ],
      },
    });
  });
});
