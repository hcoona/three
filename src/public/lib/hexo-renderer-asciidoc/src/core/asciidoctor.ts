/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { convert, Logger } from '@asciidoctor/core';

type ControlledAsciiDocOptions = Readonly<{
  doctype: 'article';
  safe: 'server';
  to_file: false;
}>;

const CONTROLLED_OPTIONS: ControlledAsciiDocOptions = Object.freeze({
  doctype: 'article',
  safe: 'server',
  to_file: false,
});

const toAsciiDocText = (value: string): string => {
  if (typeof value !== 'string') {
    throw new TypeError(`Asciidoctor conversion requires string input: ${typeof value}`);
  }

  return value;
};

/**
 * Convert a chunk of AsciiDoc text into HTML using the stateless `@asciidoctor/core` v4 API.
 * Each call is independent; there is no shared singleton runtime.
 *
 * @param text - Raw AsciiDoc document body.
 * @returns A promise resolving to the rendered HTML string produced by Asciidoctor.
 */
export const convertAsciiDoc = async (text: string): Promise<string> => {
  const result = await convert(toAsciiDocText(text), {
    ...CONTROLLED_OPTIONS,
    logger: new Logger(),
  });
  if (typeof result !== 'string') {
    throw new TypeError(`Asciidoctor conversion did not return a string: ${typeof result}`);
  }
  return result;
};
