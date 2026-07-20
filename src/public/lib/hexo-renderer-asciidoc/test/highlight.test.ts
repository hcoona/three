/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import * as hexoUtil from 'hexo-util';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyStaticHighlighting } from '../src/core/highlight';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('applyStaticHighlighting', () => {
  it('decodes escaped code text before invoking hexo highlighter', () => {
    const highlightResult = '<figure class="highlight"></figure>';
    const highlightMock = vi.spyOn(hexoUtil, 'highlight').mockReturnValue(highlightResult);

    const html = `<div class="listingblock">
<div class="content">
<pre class="highlight"><code data-lang="xml">&lt;div class=&quot;test&quot;&gt;AT&amp;T&lt;/div&gt;</code></pre>
</div>
</div>`;

    const result = applyStaticHighlighting(html);

    expect(highlightMock).toHaveBeenCalledTimes(1);
    const callArgs = highlightMock.mock.calls[0];
    expect(callArgs).toBeDefined();
    if (!callArgs) {
      throw new Error('Expected hexoUtil.highlight to be called at least once');
    }

    const [source, options] = callArgs;
    expect(source).toBe('<div class="test">AT&T</div>');
    expect(options).toBeDefined();
    expect(options?.lang).toBe('xml');
    expect(result).toContain(highlightResult);
  });

  it('uses plaintext for a language-less code block and fixed highlighting options', () => {
    const highlightMock = vi.spyOn(hexoUtil, 'highlight').mockReturnValue('<figure class="highlight">plain</figure>');
    const html = `<div class="listingblock"><div class="content">
<pre class="highlight"><code>plain &amp; text</code></pre>
</div></div>`;

    const result = applyStaticHighlighting(html);

    expect(highlightMock).toHaveBeenCalledWith('plain & text', {
      autoDetect: false,
      gutter: false,
      lang: 'plaintext',
      wrap: false,
    });
    expect(result).toContain('<figure class="highlight">plain</figure>');
  });

  it('rewrites a passthrough pre.highlight block outside the canonical listing-block boundary', () => {
    const highlightResult = '<pre><code class="highlight javascript">rewritten</code></pre>';
    const highlightMock = vi.spyOn(hexoUtil, 'highlight').mockReturnValue(highlightResult);
    const html = `<article class="raw-boundary">
<pre class="highlight"><code data-lang="javascript">const raw = 1;</code></pre>
</article>`;

    const result = applyStaticHighlighting(html);

    expect(highlightMock).toHaveBeenCalledWith('const raw = 1;', {
      autoDetect: false,
      gutter: false,
      lang: 'javascript',
      wrap: false,
    });
    expect(result).toContain(highlightResult);
    expect(result).not.toContain('<pre class="highlight"><code data-lang="javascript">const raw = 1;</code></pre>');
  });

  it('highlights multiple blocks in order with independent languages and entity decoding', () => {
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockImplementation(
        (source, options) => `<figure data-language="${options?.lang}" data-source-length="${source.length}"></figure>`,
      );
    const html = `<div class="listingblock"><div class="content">
<pre class="highlight"><code data-lang="xml">&lt;one&gt;&amp;amp;</code></pre>
</div></div>
<div class="listingblock"><div class="content">
<pre class="highlight"><code>two &amp; three</code></pre>
</div></div>`;

    const result = applyStaticHighlighting(html);

    expect(highlightMock.mock.calls).toEqual([
      ['<one>&amp;', { autoDetect: false, gutter: false, lang: 'xml', wrap: false }],
      ['two & three', { autoDetect: false, gutter: false, lang: 'plaintext', wrap: false }],
    ]);
    expect(result.indexOf('data-language="xml"')).toBeLessThan(result.indexOf('data-language="plaintext"'));
  });
});
