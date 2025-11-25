/**
 * Copyright 2017 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { defineConfig } from 'tsdown';

export default defineConfig({
  entry: 'src/index.ts',
  format: 'iife',
  globalName: 'SteamAccountHistoryToCsv',
  platform: 'browser',
  target: 'es2020',
  outDir: 'dist',
  hash: false,
  sourcemap: true,
  outputOptions(options, format) {
    if (format === 'iife') {
      options.entryFileNames = '[name].js';
      options.chunkFileNames = '[name]-[hash].js';
    }

    return options;
  },
});
