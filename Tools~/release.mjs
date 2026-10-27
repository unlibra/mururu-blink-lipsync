// Node.js 24+, Git. Reads committed blobs so checkout line endings cannot alter a release.
import fs from 'node:fs';
import path from 'node:path';
import { gzipSync, gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const prefix = 'Assets/unlibra/MururuBlinkLipSync';
const parentFolders = [
  ['Assets/unlibra', '8fabaddd6533aa34194eef49072b513a'],
  [prefix, '8d3ad7bcfce8603439764df07cee3dfc'],
];
const documentFiles = ['README.md', 'CHANGELOG.md', 'LICENSE.txt', 'NOTICE.txt'];
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const git = (root, ...args) => execFileSync('git', ['-C', root, ...args], { maxBuffer: 64 * 1024 * 1024 });
const requireThat = (condition, message) => { if (!condition) throw new Error(message); };

function folderMeta(guid) {
  return Buffer.from(`fileFormatVersion: 2\nguid: ${guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
}

export function releaseSnapshot(root) {
  const commit = git(root, 'rev-parse', '--verify', 'HEAD^{commit}').toString().trim();
  const blobs = new Map(git(root, 'ls-tree', '-r', '-z', commit).toString().split('\0').filter(Boolean).map(line => {
    const [header, name] = line.split('\t');
    const [mode, type, oid] = header.split(' ');
    return [name, { mode, type, oid }];
  }));
  const read = name => {
    const entry = blobs.get(name);
    requireThat(entry?.type === 'blob' && ['100644', '100755'].includes(entry.mode), `Missing or linked release source: ${name}`);
    return git(root, 'cat-file', 'blob', entry.oid);
  };
  for (const name of documentFiles) requireThat(read(name).toString().trim().length > 0, `Empty release document: ${name}`);
  const versionMatches = [...read('Editor/Window/MururuBlinkLipSyncSetupWindow.cs').toString().matchAll(/private const string ToolVersion = "((?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))";/g)];
  requireThat(versionMatches.length === 1, 'Expected one valid ToolVersion.');
  const version = versionMatches[0][1];
  const changelog = read('CHANGELOG.md').toString().replaceAll('\r\n', '\n');
  const escapedVersion = version.replaceAll('.', '\\.');
  const sections = [...changelog.matchAll(new RegExp(`^## \\[${escapedVersion}\\] - (\\d{4}-\\d{2}-\\d{2})[ \\t]*\\n([\\s\\S]*?)(?=^## |$(?![\\s\\S]))`, 'gm'))];
  requireThat(sections.length === 1, 'Expected one matching CHANGELOG version/date.');
  const releaseDate = sections[0][1];
  const date = new Date(`${releaseDate}T00:00:00Z`);
  requireThat(!Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === releaseDate, 'Invalid release date.');
  const releaseNotes = sections[0][2].trim();
  requireThat(releaseNotes.length > 0, 'Release notes must not be empty.');
  const assets = new Set(['Editor', 'Runtime', ...documentFiles]);
  for (const name of blobs.keys()) {
    if (!/^(Editor|Runtime)\//.test(name)) continue;
    const parts = name.split('/');
    for (let count = 1; count < parts.length; count++) assets.add(parts.slice(0, count).join('/'));
    if (!name.endsWith('.meta')) assets.add(name);
  }
  // Reject orphan metadata instead of silently dropping an incomplete asset.
  for (const name of blobs.keys()) {
    if (/^(Editor|Runtime)\/.+\.meta$/.test(name)) requireThat(assets.has(name.slice(0, -5)), `Orphan asset metadata: ${name}`);
  }
  const entries = new Map();
  const guids = new Set();
  const addAsset = (assetPath, guid, meta, bytes) => {
    requireThat(/^[0-9a-f]{32}$/.test(guid) && !guids.has(guid), `Invalid or duplicate GUID: ${assetPath}`);
    guids.add(guid);
    entries.set(`${guid}/`, { type: '5', bytes: Buffer.alloc(0) });
    entries.set(`${guid}/pathname`, { type: '0', bytes: Buffer.from(assetPath) });
    entries.set(`${guid}/asset.meta`, { type: '0', bytes: meta });
    if (bytes !== undefined) entries.set(`${guid}/asset`, { type: '0', bytes });
  };
  for (const [name, guid] of parentFolders) addAsset(name, guid, folderMeta(guid));
  for (const name of [...assets].sort()) {
    requireThat(/^[A-Za-z0-9_.\/-]+$/.test(name), `Unsupported release source path: ${name}`);
    const meta = read(`${name}.meta`);
    const guidMatches = [...meta.toString().matchAll(/^guid: ([0-9a-f]{32})\s*$/gm)];
    requireThat(guidMatches.length === 1, `Missing or invalid GUID: ${name}`);
    const isFile = blobs.has(name);
    requireThat(isFile || /^folderAsset: yes\s*$/m.test(meta.toString()), `Missing folder metadata: ${name}`);
    addAsset(`${prefix}/${name}`, guidMatches[0][1], meta, isFile ? read(name) : undefined);
  }
  return { commit, version, releaseDate, releaseNotes, assetCount: assets.size, entries };
}

// A bounded USTAR writer: only our GUID directories and three regular entry names.
// No filesystem extraction, user names, source paths, host timestamps or external tar executable.
function tarBytes(entries) {
  const chunks = [];
  for (const [name, { type, bytes }] of [...entries].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)) {
    requireThat(/^[0-9a-f]{32}\/(?:asset|asset\.meta|pathname)?$/.test(name), 'Unsupported archive entry.');
    const header = Buffer.alloc(512);
    header.write(name, 0, 100, 'ascii');
    const octal = (value, offset, width) => {
      const digits = value.toString(8);
      requireThat(digits.length < width, 'Archive size exceeds USTAR limits.');
      header.write(digits.padStart(width - 1, '0') + '\0', offset, width, 'ascii');
    };
    octal(type === '5' ? 0o755 : 0o644, 100, 8);
    octal(0, 108, 8); octal(0, 116, 8);
    octal(bytes.length, 124, 12); octal(0, 136, 12);
    header.fill(32, 148, 156);
    header.write(type, 156, 1, 'ascii');
    header.write('ustar\0', 257, 6, 'ascii'); header.write('00', 263, 2, 'ascii');
    const checksum = header.reduce((sum, byte) => sum + byte, 0);
    header.write(checksum.toString(8).padStart(6, '0') + '\0 ', 148, 8, 'ascii');
    chunks.push(header, bytes, Buffer.alloc((512 - bytes.length % 512) % 512));
  }
  return Buffer.concat([...chunks, Buffer.alloc(1024)]);
}

export function verifyPackage(packageBytes, entries) {
  const expected = tarBytes(entries);
  requireThat(expected.length <= 64 * 1024 * 1024, 'Release exceeds the reviewed package size limit.');
  const actual = gunzipSync(packageBytes, { maxOutputLength: expected.length });
  requireThat(actual.equals(expected), 'Package content or archive metadata mismatch.');
}

function metadata(snapshot, bytes) {
  return {
    version: snapshot.version, releaseDate: snapshot.releaseDate,
    sourceCommit: snapshot.commit, sourceDirty: false, packageAssetCount: snapshot.assetCount,
    files: [{ name: `MururuBlinkLipSync_v${snapshot.version}.unitypackage`, bytes: bytes.length, sha256: hash(bytes) }],
  };
}

function notes(snapshot, sha256) {
  return `${snapshot.releaseNotes}\n\n---\n\n- マニュアル: https://docs.unlibra.com/mururu-blink-lipsync/\n- BOOTH: https://unlibra.booth.pm/items/9056145\n- Commit: ${snapshot.commit}\n- SHA-256: \`${sha256}\`\n`;
}

export function buildRelease(root) {
  requireThat(git(root, 'status', '--porcelain', '--untracked-files=all').length === 0, 'Release builds require a clean committed checkout.');
  const snapshot = releaseSnapshot(root);
  const bytes = gzipSync(tarBytes(snapshot.entries), { level: 9 });
  // zlib otherwise writes the host OS here (Windows=10, Unix=3).
  bytes[9] = 255;
  verifyPackage(bytes, snapshot.entries);
  const manifest = metadata(snapshot, bytes);
  const dist = path.join(root, 'dist~');
  const distStat = fs.lstatSync(dist, { throwIfNoEntry: false });
  requireThat(distStat === undefined || distStat.isDirectory() && !distStat.isSymbolicLink(), 'Invalid release output directory.');
  fs.mkdirSync(dist, { recursive: true });
  for (const [name, data] of [
    [manifest.files[0].name, bytes],
    ['release-manifest.json', JSON.stringify(manifest, null, 2) + '\n'],
    ['release-notes.md', notes(snapshot, manifest.files[0].sha256)],
  ]) {
    const destination = path.join(dist, name);
    const stat = fs.lstatSync(destination, { throwIfNoEntry: false });
    requireThat(stat === undefined || stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1, 'Linked release output is not allowed.');
    fs.writeFileSync(destination, data);
  }
  return manifest;
}

export function verifyBuilds(root, directories, tag, commit) {
  const snapshot = releaseSnapshot(root);
  requireThat(tag === `v${snapshot.version}`, 'Tag does not match ToolVersion and CHANGELOG.');
  requireThat(commit === snapshot.commit, 'Release commit mismatch.');
  requireThat(directories.length === 2, 'Both Linux and Windows builds are required.');
  let firstPackage;
  for (const directory of directories) {
    const name = `MururuBlinkLipSync_v${snapshot.version}.unitypackage`;
    requireThat(JSON.stringify(fs.readdirSync(directory).sort()) === JSON.stringify([name, 'release-manifest.json', 'release-notes.md'].sort()), 'Unexpected release artifact files.');
    const bytes = fs.readFileSync(path.join(directory, name));
    verifyPackage(bytes, snapshot.entries);
    const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'release-manifest.json')));
    requireThat(JSON.stringify(manifest) === JSON.stringify(metadata(snapshot, bytes)), 'Release manifest mismatch.');
    requireThat(fs.readFileSync(path.join(directory, 'release-notes.md'), 'utf8') === notes(snapshot, hash(bytes)), 'Release notes mismatch.');
    requireThat(firstPackage === undefined || firstPackage.equals(bytes), 'Linux and Windows packages differ.');
    firstPackage = bytes;
  }
  return { packagePath: path.resolve(directories[0], `MururuBlinkLipSync_v${snapshot.version}.unitypackage`), notesPath: path.resolve(directories[0], 'release-notes.md') };
}

function main(args) {
  const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
  if (args.length === 1 && args[0] === 'build') {
    console.log(JSON.stringify(buildRelease(root), null, 2));
  } else if (args.length === 5 && args[0] === 'verify') {
    const result = verifyBuilds(root, [args[1], args[2]], args[3], args[4]);
    if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `package_path=${result.packagePath}\nnotes_path=${result.notesPath}\n`);
    console.log('Release artifacts verified against committed sources on both platforms.');
  } else throw new Error('Usage: node Tools~/release.mjs build | verify LINUX_DIR WINDOWS_DIR TAG COMMIT');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { main(process.argv.slice(2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
