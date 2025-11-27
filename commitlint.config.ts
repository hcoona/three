import conventionalConfig from '@commitlint/config-conventional';
import type { Plugin, UserConfig } from '@commitlint/types';
import { RuleConfigSeverity } from '@commitlint/types';
import { apStyleTitleCase } from 'ap-style-title-case';

const typeEnumRule = conventionalConfig?.rules?.['type-enum'] ?? [];
const baseTypes = Array.isArray(typeEnumRule[2]) ? typeEnumRule[2] : [];
const extendedTypes = Array.from(new Set([...baseTypes, 'security']));

const apaTitleCasePlugin: Plugin = {
  rules: {
    'subject-apa-title-case': (parsed) => {
      const subject = parsed.subject ?? '';

      if (subject.length === 0) {
        return [false, 'Subject is empty'];
      }

      const expected = apStyleTitleCase(subject);
      const isValid = subject === expected;

      return [isValid, isValid ? '' : `Subject must be in AP style title case: "${expected}"`];
    },
  },
};

const Configuration: UserConfig = {
  extends: ['@commitlint/config-conventional'],
  formatter: '@commitlint/format',
  defaultIgnores: true,
  plugins: [apaTitleCasePlugin],
  rules: {
    'subject-case': [RuleConfigSeverity.Disabled],
    'subject-apa-title-case': [RuleConfigSeverity.Error, 'always'],
    'type-enum': [RuleConfigSeverity.Error, 'always', extendedTypes],
  },
};

export default Configuration;
