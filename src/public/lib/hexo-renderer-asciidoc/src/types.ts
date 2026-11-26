/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

/**
 * Input payload passed to the AsciiDoc renderer.
 */
export interface RendererData {
  /** Absolute or relative path to the file being rendered, if available. */
  path?: string | null;
  /** Raw AsciiDoc content provided by Hexo. */
  text: string;
}

/**
 * Optional per-render template or context variables supplied by Hexo.
 */
export type RendererLocals = Record<string, unknown>;

/**
 * Asynchronous Hexo renderer function converting AsciiDoc input into HTML.
 */
export type Renderer = (data: RendererData, locals?: RendererLocals) => Promise<string>;

/**
 * Hexo renderer extension registry contract used to wire AsciiDoc file extensions.
 */
export interface RendererExtension {
  /**
   * Register a renderer function for a source extension and target output format.
   *
   * @param name - Source file extension without a leading dot (for example, `'adoc'`).
   * @param output - Target output extension (for example, `'html'`).
   * @param fn - Asynchronous renderer implementation.
   * @param sync - Whether the renderer executes synchronously (`false` for AsciiDoc).
   */
  register(name: string, output: string, fn: Renderer, sync?: boolean): void;
}

/**
 * Minimal Hexo instance surface required to register the AsciiDoc renderer plugin.
 */
export interface Hexo {
  /** Hexo extension registries exposed to plugins. */
  extend: {
    /** Renderer extension registry. */
    renderer: RendererExtension;
  };
  /** Hexo site configuration dictionary. */
  config: Record<string, unknown>;
}
