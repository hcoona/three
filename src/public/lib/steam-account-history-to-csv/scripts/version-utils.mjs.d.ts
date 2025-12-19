import type * as nbgv from 'nerdbank-gitversioning';

export const MAX_BROWSER_VERSION_PART: number;
export const projectRoot: string;

export type VersionInfo = Awaited<ReturnType<typeof nbgv.getVersion>>;

/**
 * Reads NBGV version info for the given root directory.
 * If omitted, defaults to this package's project root.
 */
export function getVersionInfo(root?: string): Promise<VersionInfo>;

/**
 * Creates a browser-extension-safe dotted numeric version: SimpleVersion + VersionHeight.
 * Result format: MAJOR.MINOR.PATCH.HEIGHT (all numeric, each <= 65535).
 */
export function getBrowserExtensionVersion(versionInfo: VersionInfo): string;
