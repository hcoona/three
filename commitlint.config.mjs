import conventionalConfig from '@commitlint/config-conventional';
import { apStyleTitleCase } from 'ap-style-title-case';

const baseTypeEnumRule = conventionalConfig?.rules?.['type-enum'] ?? [];
const baseTypes = Array.isArray(baseTypeEnumRule[2]) ? baseTypeEnumRule[2] : [];
const extendedTypes = Array.from(new Set([...baseTypes, 'security']));

export default {
    extends: ['@commitlint/config-conventional'],
    formatter: '@commitlint/format',
    defaultIgnores: true,

    plugins: [
        {
            rules: {
                'subject-apa-title-case': (parsed) => {
                    const subject = parsed.subject || '';

                    if (subject.length === 0) {
                        return [false, 'Subject is empty'];
                    }

                    const expected = apStyleTitleCase(subject);
                    const isValid = subject === expected;

                    return [isValid, isValid ? '' : `Subject must be in AP style title case: "${expected}"`];
                },
            },
        },
    ],

    rules: {
        'subject-case': [0],
        'subject-apa-title-case': [2, 'always'],
        'type-enum': [2, 'always', extendedTypes],
    },
};
