/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import * as cheerio from 'cheerio';

export const CANONICAL_HTML_ALGORITHM = 'ac07-dom-v1';

interface HtmlNode {
  attribs?: Record<string, string>;
  children?: HtmlNode[];
  data?: string;
  name?: string;
  type: string;
}

interface CanonicalHtmlNode {
  attributes?: [string, string][];
  children?: CanonicalHtmlNode[];
  data?: string;
  name?: string;
  type: string;
}

/**
 * AC-07 canonical DOM algorithm (`ac07-dom-v1`):
 *
 * 1. Parse as an HTML5 document with Cheerio 1.2.0's default parse5 parser.
 * 2. Preserve document order, node types, element names, text, comments, raw
 *    script contents, and attribute values after HTML parsing/entity decoding.
 * 3. Sort attributes lexicographically by name; ignore source attribute order
 *    and serializer spelling (quotes, void-tag syntax, and entity references).
 * 4. Compare the resulting JSON-compatible trees for deep equality.
 */
export const canonicalizeHtml = (html: string): CanonicalHtmlNode[] => {
  const $ = cheerio.load(html);

  const canonicalizeNode = (node: HtmlNode): CanonicalHtmlNode => {
    const canonical: CanonicalHtmlNode = { type: node.type };

    if (node.name !== undefined) {
      canonical.name = node.name;
    }
    if (node.attribs !== undefined) {
      canonical.attributes = Object.entries(node.attribs).sort(([left], [right]) => left.localeCompare(right));
    }
    if (node.data !== undefined) {
      canonical.data = node.data;
    }
    if (node.children !== undefined) {
      canonical.children = node.children.map(canonicalizeNode);
    }

    return canonical;
  };

  return ($.root().get(0)?.children as HtmlNode[] | undefined)?.map(canonicalizeNode) ?? [];
};
