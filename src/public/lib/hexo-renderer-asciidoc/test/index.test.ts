/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import * as entities from 'entities';
import * as hexoUtil from 'hexo-util';
import { afterEach, describe, expect, it, vi } from 'vitest';
import renderer from '../src/core/renderer';
import { CANONICAL_HTML_ALGORITHM, canonicalizeHtml } from './helpers/canonical-html';
import { renderAsciiDoc } from './helpers/render';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('Asciidoc renderer', () => {
  it('header', async () => {
    const body = `
== Test H2 ==
`;
    const result = await renderAsciiDoc(body);

    expect(result).toEqual(`<div class="sect1">
<h2 id="_test_h2">Test H2</h2>
<div class="sectionbody">

</div>
</div>`);
  });

  it('code highlight', async () => {
    const body = `
[source,ruby]
----
require 'sinatra'

get '/hi' do
  "Hello World!"
end
----`;
    const result = await renderAsciiDoc(body);

    expect(entities.decodeHTML(result)).toEqual(
      entities.decodeHTML(`<div class="listingblock">
<div class="content">
<pre><code class="highlight ruby"><span class="keyword">require</span> <span class="string">'sinatra'</span>

get <span class="string">'/hi'</span> <span class="keyword">do</span>
  <span class="string">"Hello World!"</span>
<span class="keyword">end</span></code></pre>
</div>
</div>`),
    );
  });

  it('escapes braces after conversion and syntax highlighting', async () => {
    const body = `
[source,javascript]
----
const value = { nested: true };
----`;

    const result = await renderAsciiDoc(body);

    expect(result).toContain('&#123;');
    expect(result).toContain('&#125;');
    expect(result).not.toContain('{ nested:');
  });

  it('preserves noncanonical passthrough DOM through the full production renderer', async () => {
    const passthroughHtml = `<section id="ac07-raw" data-entity="fish &amp; chips" onclick="window.rawHandler({ event: 'click' })">
<script>window.rawExecutable = { nested: "{script-braces}", entity: "&amp;" };</script>
<pre class="noncanonical"><code data-lang="javascript">const raw = { entity: "&amp;", braces: "{code-braces}" };</code></pre>
<p data-braces="{attribute-braces}">entities: &amp; &lt; &gt;; literal {text-braces}</p>
</section>`;
    const body = `++++
${passthroughHtml}
++++
`;
    const highlightMock = vi
      .spyOn(hexoUtil, 'highlight')
      .mockReturnValue('<figure class="highlight">unexpected rewrite</figure>');

    const renderPromise = renderer({ text: body });

    expect(renderPromise).toBeInstanceOf(Promise);
    const result = await renderPromise;
    const expectedTransformedHtml = passthroughHtml.replaceAll('{', '&#123;').replaceAll('}', '&#125;');
    const expectedTransformedDom = canonicalizeHtml(expectedTransformedHtml);

    expect(CANONICAL_HTML_ALGORITHM).toBe('ac07-dom-v1');
    expect(highlightMock).not.toHaveBeenCalled();
    expect(canonicalizeHtml(result)).toEqual(expectedTransformedDom);
    expect(result.match(/&#123;/g)).toHaveLength(7);
    expect(result.match(/&#125;/g)).toHaveLength(7);
    expect(result).not.toMatch(/[{}]/);
  });
});
