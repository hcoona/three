/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import * as hexoUtil from 'hexo-util';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyStaticHighlighting } from '../src/core/highlight';

const FIXED_HIGHLIGHT_OPTIONS = {
  autoDetect: false,
  gutter: false,
  wrap: false,
};

const wrapCanonicalListingBlock = (preHtml: string): string => `<div class="listingblock">
<div class="content">
${preHtml}
</div>
</div>`;

afterEach(() => {
  vi.restoreAllMocks();
});

describe('applyStaticHighlighting', () => {
  it.each([
    {
      name: 'pre.highlight > code[data-lang]',
      preHtml: '<pre class="highlight"><code data-lang="xml">&lt;node /&gt;</code></pre>',
      expectedSource: '<node />',
      expectedLanguage: 'xml',
    },
    {
      name: 'pre.highlight > code',
      preHtml: '<pre class="highlight"><code>plain &amp; text</code></pre>',
      expectedSource: 'plain & text',
      expectedLanguage: 'plaintext',
    },
    {
      name: 'pre[lang] > code',
      preHtml: '<pre lang="python"><code>value = 1</code></pre>',
      expectedSource: 'value = 1',
      expectedLanguage: 'python',
    },
    {
      name: 'pre > code',
      preHtml: '<pre><code>bare &amp; code</code></pre>',
      expectedSource: 'bare & code',
      expectedLanguage: 'plaintext',
    },
  ])('highlights the canonical $name shape', ({ preHtml, expectedSource, expectedLanguage }) => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockImplementation((_source, options) => `<figure data-language="${options?.lang}"></figure>`);

    const result = applyStaticHighlighting(wrapCanonicalListingBlock(preHtml));

    expect(highlightMock).toHaveBeenCalledTimes(1);
    expect(highlightMock).toHaveBeenCalledWith(expectedSource, {
      ...FIXED_HIGHLIGHT_OPTIONS,
      lang: expectedLanguage,
    });
    expect(result).toContain(`<figure data-language="${expectedLanguage}"></figure>`);
  });

  it('prefers code[data-lang] over pre[lang]', () => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockReturnValue('<figure class="highlight">precedence</figure>');

    applyStaticHighlighting(wrapCanonicalListingBlock('<pre lang="python"><code data-lang="ruby">puts 1</code></pre>'));

    expect(highlightMock).toHaveBeenCalledWith('puts 1', {
      ...FIXED_HIGHLIGHT_OPTIONS,
      lang: 'ruby',
    });
  });

  it('falls back to pre[lang] when code[data-lang] is empty', () => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockReturnValue('<figure class="highlight">precedence-empty</figure>');

    applyStaticHighlighting(wrapCanonicalListingBlock('<pre lang="python"><code data-lang="">value = 1</code></pre>'));

    expect(highlightMock).toHaveBeenCalledWith('value = 1', {
      ...FIXED_HIGHLIGHT_OPTIONS,
      lang: 'python',
    });
  });

  it('falls back to pre[lang] when code[data-lang] is whitespace only', () => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockReturnValue('<figure class="highlight">precedence-whitespace</figure>');

    applyStaticHighlighting(
      wrapCanonicalListingBlock('<pre lang="python"><code data-lang="   ">value = 1</code></pre>'),
    );

    expect(highlightMock).toHaveBeenCalledWith('value = 1', {
      ...FIXED_HIGHLIGHT_OPTIONS,
      lang: 'python',
    });
  });

  it('decodes XML entities exactly once before highlighting', () => {
    const highlightMock = vi.spyOn(hexoUtil, 'highlight').mockReturnValue('<figure class="highlight">decoded</figure>');

    applyStaticHighlighting(
      wrapCanonicalListingBlock(
        '<pre class="highlight"><code data-lang="xml">&lt;one&gt;&amp;amp;notit;&lt;/one&gt;</code></pre>',
      ),
    );

    expect(highlightMock).toHaveBeenCalledWith('<one>&amp;notit;</one>', {
      ...FIXED_HIGHLIGHT_OPTIONS,
      lang: 'xml',
    });
  });

  it('preserves the nowrap option on highlighted source blocks', () => {
    vi.spyOn(hexoUtil, 'highlight').mockReturnValue('<pre><code class="highlight plaintext">plain text</code></pre>');

    const result = applyStaticHighlighting(
      wrapCanonicalListingBlock('<pre class="highlight nowrap"><code data-lang="plaintext">plain text</code></pre>'),
    );

    expect(result).toContain('<pre class="nowrap"><code class="highlight plaintext">plain text</code></pre>');
  });

  it('highlights multiple canonical blocks in document order with isolated language detection', () => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockImplementation(
        (source, options) => `<figure data-language="${options?.lang}" data-source="${source}"></figure>`,
      );

    const html = `${wrapCanonicalListingBlock('<pre class="highlight"><code data-lang="xml">&lt;one&gt;&amp;amp;</code></pre>')}
${wrapCanonicalListingBlock('<pre lang="python"><code>value = 1</code></pre>')}
${wrapCanonicalListingBlock('<pre><code>three &amp; four</code></pre>')}`;

    const result = applyStaticHighlighting(html);

    expect(highlightMock.mock.calls).toEqual([
      ['<one>&amp;', { ...FIXED_HIGHLIGHT_OPTIONS, lang: 'xml' }],
      ['value = 1', { ...FIXED_HIGHLIGHT_OPTIONS, lang: 'python' }],
      ['three & four', { ...FIXED_HIGHLIGHT_OPTIONS, lang: 'plaintext' }],
    ]);
    expect(result.indexOf('data-language="xml"')).toBeLessThan(result.indexOf('data-language="python"'));
    expect(result.indexOf('data-language="python"')).toBeLessThan(result.indexOf('data-language="plaintext"'));
  });

  it.each([
    {
      name: 'arbitrary outer boundary',
      html: '<article><pre class="highlight"><code data-lang="javascript">const raw = 1;</code></pre></article>',
    },
    {
      name: 'nested wrapper under content',
      html: '<div class="listingblock"><div class="content"><div><pre class="highlight"><code data-lang="javascript">const nested = 1;</code></pre></div></div></div>',
    },
    {
      name: 'nested non-direct code child',
      html: '<div class="listingblock"><div class="content"><pre class="highlight"><span><code data-lang="javascript">const wrapped = 1;</code></span></pre></div></div>',
    },
    {
      name: 'passthrough-like boundary',
      html: '<div class="pass"><div class="content"><pre class="highlight"><code data-lang="javascript">const passthrough = 1;</code></pre></div></div>',
    },
    {
      name: 'canonical listing block without direct code child',
      html: '<div class="listingblock"><div class="content"><pre>plain &amp; text</pre></div></div>',
    },
  ])('leaves $name untouched', ({ html }) => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockReturnValue('<figure class="highlight">rewritten</figure>');

    const result = applyStaticHighlighting(html);

    expect(highlightMock).not.toHaveBeenCalled();
    expect(result).toContain(html);
  });
});
