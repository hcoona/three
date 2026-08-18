/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import type { CheerioOptions } from 'cheerio';
import * as cheerio from 'cheerio';
import { decodeXML } from 'entities';
import hexoUtil from 'hexo-util';

const CHEERIO_LOAD_OPTIONS: CheerioOptions = Object.freeze({
  // Cheerio 1 defaults to parse5, which eagerly decodes entities using HTML
  // rules. That destroys strings like `&amp;notit;` by resolving them into
  // partial entities (e.g., `¬it;`). We therefore force the htmlparser2 path
  // and keep entity decoding disabled so that we can read the exact escaped
  // payload from Asciidoctor and run `decodeXML` (with XML rules) ourselves
  // before handing the plain code to Hexo's highlighter.
  xml: {
    xmlMode: false,
    decodeEntities: false,
  },
});

const DEFAULT_LANGUAGE = 'plaintext';

interface HighlightBaseOptions {
  autoDetect: boolean;
  gutter: boolean;
  wrap: boolean;
}

const BASE_HIGHLIGHT_OPTIONS: HighlightBaseOptions = Object.freeze({
  autoDetect: false,
  gutter: false,
  wrap: false,
});

const toHighlightLanguage = (language?: string): string => {
  if (typeof language !== 'string' || language.trim().length === 0) {
    return DEFAULT_LANGUAGE;
  }

  return language;
};

const getPreferredLanguage = (codeLanguage: string | undefined, preLanguage: string | undefined): string => {
  if (typeof codeLanguage === 'string' && codeLanguage.trim().length > 0) {
    return codeLanguage;
  }

  return toHighlightLanguage(preLanguage);
};

/**
 * Replace Asciidoctor's placeholder highlight blocks with Hexo's static highlighter output.
 * Only the canonical `div.listingblock > div.content > pre > code` chain is rewritten so
 * that passthrough or unrelated `pre` elements elsewhere in the document are left untouched.
 *
 * Within the selected `pre`, all four shapes emitted by Asciidoctor 4 and the legacy
 * html-pipeline highlighter are recognized:
 *   - `pre.highlight > code[data-lang]` (Asciidoctor 4 default, with a language)
 *   - `pre.highlight > code` (Asciidoctor 4 default, without a language)
 *   - `pre[lang] > code` (html-pipeline, with a language)
 *   - `pre > code` (html-pipeline, without a language)
 * Language precedence is `code[data-lang]` > `pre[lang]` > `plaintext`.
 *
 * @param html - HTML string generated directly from Asciidoctor.
 * @returns HTML with code blocks rendered using Hexo's static highlighter.
 */
export const applyStaticHighlighting = (html: string): string => {
  const $ = cheerio.load(html, CHEERIO_LOAD_OPTIONS);

  $('div.listingblock > div.content > pre > code').each((_index, codeElement) => {
    const preElement = codeElement.parent;
    if (preElement?.type !== 'tag') {
      return;
    }

    // biome-ignore lint/complexity/useLiteralKeys: noPropertyAccessFromIndexSignature requires bracket notation for index-signature types
    const lang = getPreferredLanguage(codeElement.attribs?.['data-lang'], preElement.attribs?.['lang']);
    const sourceCodeText = decodeXML($(codeElement).text());
    const highlightOptions = { ...BASE_HIGHLIGHT_OPTIONS, lang };
    let rendered = hexoUtil.highlight(sourceCodeText, highlightOptions);

    if ($(preElement).hasClass('nowrap')) {
      const renderedFragment = cheerio.load(rendered, CHEERIO_LOAD_OPTIONS, false);
      renderedFragment('pre').first().addClass('nowrap');
      rendered = renderedFragment.html();
    }

    $(preElement).replaceWith(rendered);
  });

  return $.html();
};
