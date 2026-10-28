// Deterministic ZIP32 (stored entries). No external tools or dependencies.
import { crc32 } from './privacy.mjs';
export const PACKAGE_ID = 'com.unlibra.mururu-blink-lipsync';
export function validateManifest(bytes, version) {
  const manifest = JSON.parse(bytes.toString('utf8'));
  if (manifest.name !== PACKAGE_ID || manifest.version !== version ||
      manifest.url !== `https://github.com/unlibra/mururu-blink-lipsync/releases/download/v${version}/${PACKAGE_ID}-${version}.zip` ||
      manifest.unity !== '2022.3' || manifest.license !== 'SEE LICENSE IN LICENSE.txt' ||
      manifest.author?.name !== 'unlibra' || !manifest.author?.email ||
      Object.keys(manifest.vpmDependencies ?? {}).sort().join(',') !== 'com.vrchat.avatars,nadena.dev.modular-avatar,nadena.dev.ndmf' ||
      Object.values(manifest.vpmDependencies).some(value => typeof value !== 'string' || value.length === 0) ||
      ['legacyFolders', 'legacyFiles', 'legacyPackages', 'zipSHA256'].some(key => key in manifest))
    throw Error('Invalid VPM manifest or unverified automatic legacy removal.');
  return manifest;
}
export function zipBytes(files) {
  if (!(files instanceof Map) || files.size === 0 || files.size > 65535) throw Error('Invalid ZIP file inventory.');
  const chunks = [], directory = [];
  let offset = 0;
  for (const [name, bytes] of [...files].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)) {
    if (!/^[A-Za-z0-9_.\/-]+$/.test(name) || name.startsWith('/') || name.split('/').some(p => !p || p === '.' || p === '..') ||
        !Buffer.isBuffer(bytes) || bytes.length > 64 * 1024 * 1024) throw Error('Invalid ZIP entry.');
    const encoded = Buffer.from(name, 'ascii'), checksum = crc32(bytes);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4);
    local.writeUInt16LE(33, 12); // 1980-01-01; fixed, no local clock/timezone
    local.writeUInt32LE(checksum, 14); local.writeUInt32LE(bytes.length, 18); local.writeUInt32LE(bytes.length, 22);
    local.writeUInt16LE(encoded.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0); central.writeUInt16LE(0x0314, 4); central.writeUInt16LE(20, 6);
    central.writeUInt16LE(33, 14); central.writeUInt32LE(checksum, 16);
    central.writeUInt32LE(bytes.length, 20); central.writeUInt32LE(bytes.length, 24);
    central.writeUInt16LE(encoded.length, 28); central.writeUInt32LE((0o100644 << 16) >>> 0, 38);
    central.writeUInt32LE(offset, 42);
    chunks.push(local, encoded, bytes); directory.push(central, encoded);
    offset += local.length + encoded.length + bytes.length;
    if (offset > 64 * 1024 * 1024) throw Error('ZIP exceeds reviewed size limit.');
  }
  const central = Buffer.concat(directory), end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(files.size, 8); end.writeUInt16LE(files.size, 10);
  end.writeUInt32LE(central.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...chunks, central, end]);
}
export function verifyVpm(bytes, files) {
  if (!bytes.equals(zipBytes(files))) throw Error('VPM ZIP content or metadata mismatch.');
}
