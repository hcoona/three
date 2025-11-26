/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import type { Config } from 'prettier';

const config: Config = {
  semi: true,
  trailingComma: 'all',
  singleQuote: true,
  printWidth: 120,
  tabWidth: 2,
  useTabs: false,
  endOfLine: 'lf',

  // Override for specific file types
  overrides: [
    {
      files: '*.md',
      options: {
        proseWrap: 'preserve',
        tabWidth: 4,
      },
    },
    {
      files: '*.ps1',
      options: {
        tabWidth: 4,
      },
    },
    {
      files: '*.cs',
      options: {
        tabWidth: 4,
      },
    },
  ],
};

export default config;
