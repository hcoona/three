/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { defineConfig, type ViteUserConfigExport } from 'vitest/config';

const vitestConfig: ViteUserConfigExport = defineConfig({
  test: {
    include: ['test/asciidoctor.coldstart.test.ts'],
    environment: 'node',
  },
});

export default vitestConfig;
