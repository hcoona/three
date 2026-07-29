/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { existsSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const packageRoot = path.resolve(import.meta.dirname, '..');
const distDirectory = path.join(packageRoot, 'dist');

const escapeRegExp = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const renameIfPresent = (fromRelativePath, toRelativePath) => {
  const fromPath = path.join(distDirectory, fromRelativePath);
  if (!existsSync(fromPath)) {
    return;
  }
  renameSync(fromPath, path.join(distDirectory, toRelativePath));
};

const rewriteSourceMapComment = (relativePath, fromValue, toValue) => {
  const filePath = path.join(distDirectory, relativePath);
  if (!existsSync(filePath)) {
    return;
  }
  const contents = readFileSync(filePath, 'utf8').replace(fromValue, toValue);
  writeFileSync(filePath, contents, 'utf8');
};

const rewriteSourceMapMetadata = (relativePath, fromValue, toValue) => {
  const filePath = path.join(distDirectory, relativePath);
  if (!existsSync(filePath)) {
    return;
  }
  const contents = readFileSync(filePath, 'utf8');
  const sourceMap = JSON.parse(contents);
  if (sourceMap.file !== fromValue && sourceMap.file !== toValue) {
    throw new Error(`${relativePath} unexpectedly targets ${sourceMap.file}.`);
  }
  sourceMap.file = toValue;
  writeFileSync(filePath, `${JSON.stringify(sourceMap)}${contents.endsWith('\n') ? '\n' : ''}`, 'utf8');
};

const removeRuntimeSourceMap = (relativePath) => {
  const filePath = path.join(distDirectory, relativePath);
  if (!existsSync(filePath)) {
    return;
  }
  rmSync(filePath, { force: true });
};

renameIfPresent('index.mjs', 'index.js');
renameIfPresent('index.d.mts', 'index.d.ts');
renameIfPresent('index.d.mts.map', 'index.d.ts.map');

rewriteSourceMapComment(
  'index.js',
  new RegExp(`(?:\\r\\n|\\r|\\n)?${escapeRegExp('//# sourceMappingURL=index.mjs.map')}(?:\\r\\n|\\r|\\n)?$`, 'u'),
  '',
);
rewriteSourceMapComment('index.d.ts', '//# sourceMappingURL=index.d.mts.map', '//# sourceMappingURL=index.d.ts.map');
rewriteSourceMapMetadata('index.d.ts.map', 'index.d.mts', 'index.d.ts');

removeRuntimeSourceMap('index.mjs.map');
