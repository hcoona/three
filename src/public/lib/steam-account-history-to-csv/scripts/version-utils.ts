/**
 * SPDX-FileCopyrightText: 2017 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import type * as nbgv from 'nerdbank-gitversioning';

import {
  getBrowserExtensionVersion as getBrowserExtensionVersionImpl,
  getVersionInfo as getVersionInfoImpl,
  MAX_BROWSER_VERSION_PART,
  projectRoot,
} from './version-utils.mjs';

export { MAX_BROWSER_VERSION_PART, projectRoot };

export type VersionInfo = Awaited<ReturnType<typeof nbgv.getVersion>>;

export async function getVersionInfo(root?: string): Promise<VersionInfo> {
  return (await getVersionInfoImpl(root)) as VersionInfo;
}

export function getBrowserExtensionVersion(versionInfo: VersionInfo): string {
  return getBrowserExtensionVersionImpl(versionInfo) as string;
}
