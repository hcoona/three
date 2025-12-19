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
