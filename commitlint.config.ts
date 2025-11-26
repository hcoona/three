/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import conventionalConfig from '@commitlint/config-conventional';
import type { Plugin, UserConfig } from '@commitlint/types';
import { RuleConfigSeverity } from '@commitlint/types';
import { apStyleTitleCase } from 'ap-style-title-case';
import type { Commit } from 'conventional-commits-parser';

const baseTypeEnumRule = conventionalConfig?.rules?.['type-enum'] ?? [];
const baseTypes = Array.isArray(baseTypeEnumRule[2]) ? baseTypeEnumRule[2] : [];
const extendedTypes = Array.from(new Set([...baseTypes, 'security']));

const subjectApaTitleCasePlugin: Plugin = {
  rules: {
    'subject-apa-title-case': (parsed: Commit) => {
      const subject = parsed.subject || '';

      if (subject.length === 0) {
        return [false, 'Subject is empty'];
      }

      const expected = apStyleTitleCase(subject);
      const isValid = subject === expected;

      return [isValid, isValid ? '' : `Subject must be in AP style title case: "${expected}"`];
    },
  },
};

const config: UserConfig = {
  extends: ['@commitlint/config-conventional'],
  formatter: '@commitlint/format',
  defaultIgnores: true,
  plugins: [subjectApaTitleCasePlugin],
  rules: {
    'subject-case': [RuleConfigSeverity.Disabled],
    'subject-apa-title-case': [RuleConfigSeverity.Error, 'always'],
    'type-enum': [RuleConfigSeverity.Error, 'always', extendedTypes],
  },
};

export default config;
