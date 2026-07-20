/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { describe, expect, it, vi } from 'vitest';
import renderer from '../src/core/renderer';
import registerRenderer from '../src/hexo/register';
import type { Hexo } from '../src/types';

describe('registerRenderer', () => {
  it('registers exactly ad, adoc, and asciidoc as asynchronous HTML renderers', () => {
    const register = vi.fn();
    const instance = {
      config: {},
      extend: { renderer: { register } },
    } as Hexo;

    const result = registerRenderer(instance);

    expect(result).toBeUndefined();
    expect(register.mock.calls).toEqual([
      ['ad', 'html', renderer, false],
      ['adoc', 'html', renderer, false],
      ['asciidoc', 'html', renderer, false],
    ]);
  });
});
