import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

interface SavedQrCode {
  filePath: string | null;
  mediaType: string | null;
}

interface DataUriGroups {
  mediaType: string;
  payload: string;
}

export async function saveQrCodeArtifact(
  stateDirectory: string,
  qrcodeId: string | undefined,
  qrContent: string | undefined,
): Promise<SavedQrCode> {
  if (!qrContent) {
    return {
      filePath: null,
      mediaType: null,
    };
  }

  const qrCodeDirectory = path.join(stateDirectory, 'qrcodes');
  await mkdir(qrCodeDirectory, { recursive: true });

  const safeId = sanitizeFilePart(qrcodeId ?? 'latest');

  if (qrContent.startsWith('data:')) {
    const match = /^data:(?<mediaType>[^;]+);base64,(?<payload>.+)$/u.exec(qrContent);

    if (!match?.groups) {
      throw new Error('QR code data URI format is not supported.');
    }

    const groups = match.groups as Partial<DataUriGroups>;
    const mediaType = groups.mediaType ?? 'application/octet-stream';
    const payload = groups.payload ?? '';
    const extension = mediaType.includes('svg') ? 'svg' : mediaType.includes('png') ? 'png' : 'bin';
    const filePath = path.join(qrCodeDirectory, `${safeId}.${extension}`);

    await writeFile(filePath, Buffer.from(payload, 'base64'));

    return {
      filePath,
      mediaType,
    };
  }

  if (looksLikeSvg(qrContent)) {
    const filePath = path.join(qrCodeDirectory, `${safeId}.svg`);
    await writeFile(filePath, qrContent, 'utf8');
    return {
      filePath,
      mediaType: 'image/svg+xml',
    };
  }

  if (looksLikeBase64(qrContent)) {
    const filePath = path.join(qrCodeDirectory, `${safeId}.png`);
    await writeFile(filePath, Buffer.from(qrContent, 'base64'));
    return {
      filePath,
      mediaType: 'image/png',
    };
  }

  const filePath = path.join(qrCodeDirectory, `${safeId}.txt`);
  await writeFile(filePath, qrContent, 'utf8');
  return {
    filePath,
    mediaType: 'text/plain',
  };
}

function sanitizeFilePart(value: string): string {
  return value.replaceAll(/[^A-Za-z0-9._-]/gu, '_');
}

function looksLikeSvg(value: string): boolean {
  const trimmed = value.trimStart();
  return trimmed.startsWith('<svg') || trimmed.startsWith('<?xml');
}

function looksLikeBase64(value: string): boolean {
  const normalized = value.replaceAll(/\s+/gu, '');

  if (normalized.length === 0 || normalized.length % 4 !== 0) {
    return false;
  }

  return /^[A-Za-z0-9+/=]+$/u.test(normalized);
}
