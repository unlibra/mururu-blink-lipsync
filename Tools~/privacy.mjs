// No third-party dependencies. Node.js 24+.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import zlib from 'node:zlib';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const POLICY = 'Tools~/public-files.json';
const PNG = Buffer.from('89504e470d0a1a0a', 'hex');
const pngRendering = new Set(['IHDR', 'PLTE', 'IDAT', 'IEND', 'tRNS', 'cHRM', 'gAMA', 'iCCP', 'sRGB', 'sBIT', 'cICP', 'mDCV', 'cLLI', 'acTL', 'fcTL', 'fdAT']);
export const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function requireThat(condition, message) {
  if (!condition) throw new Error(message);
}

export function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function checkProfile(profile, allowedProfiles) {
  requireThat(allowedProfiles.includes(sha256(profile)), 'Unreviewed ICC profile; review it before adding its SHA-256 to the policy.');
}

function cleanPng(bytes, profiles) {
  requireThat(bytes.subarray(0, 8).equals(PNG), 'Invalid PNG signature.');
  const output = [PNG];
  let offset = 8, ended = false, hasPixels = false, count = 0;
  while (offset < bytes.length) {
    requireThat(offset + 12 <= bytes.length, 'Truncated PNG chunk.');
    const length = bytes.readUInt32BE(offset);
    const end = offset + 12 + length;
    requireThat(end <= bytes.length, 'Invalid PNG chunk size.');
    const type = bytes.toString('ascii', offset + 4, offset + 8);
    requireThat(/^[A-Za-z]{4}$/.test(type), 'Invalid PNG chunk type.');
    requireThat(crc32(bytes.subarray(offset + 4, end - 4)) === bytes.readUInt32BE(end - 4), 'Invalid PNG checksum.');
    requireThat(count !== 0 || (type === 'IHDR' && length === 13), 'Missing PNG header.');
    requireThat(count === 0 || type !== 'IHDR', 'Duplicate PNG header.');
    if (!pngRendering.has(type)) {
      requireThat(type[0] === type[0].toLowerCase(), 'Unsupported critical PNG chunk.');
    } else {
      let chunk = bytes.subarray(offset, end);
      if (type === 'iCCP') {
        const payload = bytes.subarray(offset + 8, end - 4);
        const separator = payload.indexOf(0);
        requireThat(separator > 0 && separator <= 79 && payload[separator + 1] === 0, 'Invalid ICC chunk.');
        const profile = zlib.inflateSync(payload.subarray(separator + 2), { maxOutputLength: 16 * 1024 * 1024 });
        checkProfile(profile, profiles);
        // The profile's free-form name is metadata too; replace it deterministically.
        chunk = pngChunk('iCCP', Buffer.concat([Buffer.from('ICC\0\0'), zlib.deflateSync(profile)]));
      }
      output.push(chunk);
    }
    if (type === 'IDAT') hasPixels = true;
    offset = end;
    count++;
    if (type === 'IEND') {
      requireThat(length === 0 && hasPixels, 'Invalid PNG end.');
      ended = true;
      break;
    }
  }
  requireThat(ended, 'Missing PNG end.');
  // Deliberately discard bytes after the image, which may contain private data.
  return Buffer.concat(output);
}

export function pngChunk(type, payload) {
  const result = Buffer.alloc(12 + payload.length);
  result.writeUInt32BE(payload.length);
  result.write(type, 4, 4, 'ascii');
  payload.copy(result, 8);
  result.writeUInt32BE(crc32(result.subarray(4, -4)), result.length - 4);
  return result;
}

export function riffChunk(type, payload) {
  const result = Buffer.alloc(8 + payload.length + (payload.length % 2));
  result.write(type, 0, 4, 'ascii');
  result.writeUInt32LE(payload.length, 4);
  payload.copy(result, 8);
  return result;
}

function webpChunks(bytes, profiles, frame = false) {
  const result = [];
  let offset = 0, hasImage = false;
  while (offset < bytes.length) {
    requireThat(offset + 8 <= bytes.length, 'Truncated WebP chunk.');
    const type = bytes.toString('ascii', offset, offset + 4);
    const size = bytes.readUInt32LE(offset + 4);
    const end = offset + 8 + size;
    requireThat(end + (size % 2) <= bytes.length, 'Invalid WebP chunk size.');
    let data = bytes.subarray(offset + 8, end);
    if (['VP8 ', 'VP8L', 'ALPH'].includes(type)) {
      if (type !== 'ALPH') hasImage = true;
      result.push(riffChunk(type, data));
    } else if (!frame && type === 'VP8X') {
      requireThat(size === 10, 'Invalid WebP extended header.');
      data = Buffer.from(data);
      data[0] &= ~0x0c; // Clear EXIF and XMP flags, preserve alpha/animation/ICC.
      result.push(riffChunk(type, data));
    } else if (!frame && type === 'ICCP') {
      checkProfile(data, profiles);
      result.push(riffChunk(type, data));
    } else if (!frame && type === 'ANIM') {
      requireThat(size === 6, 'Invalid WebP animation header.');
      result.push(riffChunk(type, data));
    } else if (!frame && type === 'ANMF') {
      requireThat(size >= 16, 'Invalid WebP frame.');
      const cleaned = webpChunks(data.subarray(16), profiles, true);
      result.push(riffChunk(type, Buffer.concat([data.subarray(0, 16), cleaned])));
      hasImage = true;
    }
    // All other chunks (including EXIF, XMP, unknown chunks and padding) are discarded.
    offset = end + (size % 2);
  }
  requireThat(hasImage, 'Missing WebP image data.');
  return Buffer.concat(result);
}

export function sanitizeImage(bytes, extension, profiles) {
  if (extension === '.png') return cleanPng(bytes, profiles);
  requireThat(extension === '.webp', 'Only PNG and WebP are supported for automatic cleaning.');
  requireThat(bytes.length >= 12 && bytes.toString('ascii', 0, 4) === 'RIFF' && bytes.toString('ascii', 8, 12) === 'WEBP', 'Invalid WebP signature.');
  const end = bytes.readUInt32LE(4) + 8;
  requireThat(end >= 12 && end <= bytes.length, 'Invalid WebP container size.');
  const chunks = webpChunks(bytes.subarray(12, end), profiles);
  const header = Buffer.from(bytes.subarray(0, 12));
  header.writeUInt32LE(chunks.length + 4, 4);
  return Buffer.concat([header, chunks]);
}

const forbiddenPath = /(?:^|\/)(?:\.git|\.claude|\.codex|\.aws|\.ssh|node_modules|Library|Temp|Logs)(?:\/|$)|^Documentation~\/(?:archive|product|deluxe|smooth-mouth-research|pricing-study[^/]*)(?:\/|$)|^Documentation~\/booth\/(?:source\/|BOOTH\.md$)|\.(?:afdesign|psd|fbx|blend|unitypackage|zip|pem|key|pfx|log)$/i;
const textExtensions = new Set(['.md', '.cs', '.meta', '.asmdef', '.json', '.mjs', '.ps1', '.yml', '.yaml', '.ts', '.vue', '.css', '.txt']);
const textNames = new Set(['.gitignore', '.gitattributes', 'pre-commit', 'pre-push']);
const sensitivePatterns = [
  ['local user path', /[a-z]:[\\/]+(?:Users|Documents and Settings)[\\/]+[^\s"'`<>]+|\/(?:Users|home)\/[^\s"'`<>]+/i],
  ['VRChat identity', /(?:usr|avtr|wrld)_[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}/i],
  ['private key', /-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----/],
  ['access token', /(?:AKIA|ASIA)[A-Z0-9]{16}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-(?:proj-)?[A-Za-z0-9_-]{24,}|xox[baprs]-[A-Za-z0-9-]{15,}|AIza[A-Za-z0-9_-]{30,}|eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}/],
  ['credential assignment', /(?:password|passwd|api[_-]?key|client[_-]?secret|access[_-]?token|private[_-]?key|token|secret|authorization)\s*[=:]\s*["'][^"'\r\n]{8,}["']/i],
  ['webhook or invite', /discord(?:app)?\.com\/api\/webhooks\/|discord\.gg\/[A-Za-z0-9]+/i],
  ['internal document marker', new RegExp(['集計担当' + 'だけに渡す情報', '制作者のサポート' + '・互換性対応方針', 'CONFI' + 'DENTIAL', 'DO NOT' + ' PUBLISH'].join('|'))],
];

export function textIssues(text) {
  const issues = [];
  // JSON-escaped backslashes must be checked too.
  const normalized = text.replace(/\\\\/g, '\\');
  for (const [label, pattern] of sensitivePatterns) {
    if (pattern.test(normalized)) issues.push(label);
  }
  const emails = normalized.match(/[A-Za-z0-9.!#$%&'*+/=?^_`{|}~\[\]-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g) ?? [];
  const serviceEmails = new Set(['noreply', 'support'].map(name => name + '@' + 'github.com'));
  if (emails.some(value => !/@users\.noreply\.github\.com$/i.test(value) && !serviceEmails.has(value.toLowerCase()))) issues.push('email address');
  if (/data:[^,\s]*;base64,/i.test(text)) issues.push('embedded binary data requires separate review');
  return issues;
}

function validRelative(name) {
  return typeof name === 'string' && name.length > 0 && !/[\x00-\x1f\x7f\\:]/.test(name) && !name.startsWith('/') && !name.split('/').some(p => !p || p === '.' || p === '..');
}

export function validatePolicy(policy) {
  requireThat(policy.version === 1 && Array.isArray(policy.files) && Array.isArray(policy.allowedIccProfiles) && policy.reviewedPdfs && typeof policy.reviewedPdfs === 'object', 'Invalid publication policy.');
  requireThat(policy.files.every(validRelative) && new Set(policy.files).size === policy.files.length, 'Invalid or duplicate public path.');
  requireThat(policy.files.includes(POLICY), 'The policy must be registered too.');
  requireThat(policy.allowedIccProfiles.every(h => /^[0-9a-f]{64}$/.test(h)), 'Invalid profile hash.');
  requireThat(Object.entries(policy.reviewedPdfs).every(([p, h]) => policy.files.includes(p) && p.endsWith('.pdf') && /^[0-9a-f]{64}$/.test(h)), 'Invalid reviewed PDF entry.');
  return policy;
}

export function validateSnapshot(entries, policy) {
  validatePolicy(policy);
  const allowed = new Set(policy.files), seen = new Set(), errors = [];
  for (const { name, mode, bytes } of entries) {
    seen.add(name);
    if (!validRelative(name) || forbiddenPath.test(name) || /(?:^|\/)\.env(?:\.|$)/i.test(name)) {
      errors.push(`${name}: private or unsupported path`);
      continue;
    }
    if (!allowed.has(name)) { errors.push(`${name}: file is not registered for publication`); continue; }
    if (!['100644', '100755'].includes(mode)) { errors.push(`${name}: links and submodules are not supported`); continue; }
    const extension = path.posix.extname(name).toLowerCase();
    try {
      if (extension === '.png' || extension === '.webp') {
        requireThat(bytes.equals(sanitizeImage(bytes, extension, policy.allowedIccProfiles)), 'Image has metadata or noncanonical extra data; run strip-images.');
      } else if (extension === '.pdf') {
        requireThat(bytes.subarray(0, 5).toString() === '%PDF-' && policy.reviewedPdfs[name] === sha256(bytes), 'PDF is new or changed; review its contents/metadata before registering its hash.');
      } else {
        requireThat(textExtensions.has(extension) || textNames.has(path.posix.basename(name)), 'Unsupported file format; an explicit inspection method is required.');
        requireThat(!bytes.includes(0), 'Unexpected binary content.');
        const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
        for (const issue of textIssues(text)) errors.push(`${name}: ${issue}`);
      }
    } catch (error) { errors.push(`${name}: ${error.message}`); }
  }
  for (const name of allowed) if (!seen.has(name)) errors.push(`${name}: registered file is missing`);
  return errors;
}

function git(root, ...args) {
  return execFileSync('git', ['-C', root, ...args], { maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
}

function worktreeEntries(root) {
  const names = [...new Set(git(root, 'ls-files', '--cached', '--others', '--exclude-standard', '-z').toString('utf8').split('\0').filter(Boolean))];
  return names.filter(name => fs.existsSync(path.join(root, name))).map(name => {
    const stat = fs.lstatSync(path.join(root, name));
    requireThat(stat.isFile() && !stat.isSymbolicLink(), `${name}: links/directories are not supported`);
    return { name, mode: '100644', bytes: fs.readFileSync(path.join(root, name)) };
  });
}

function gitEntries(root, commit) {
  const listing = commit ? git(root, 'ls-tree', '-r', '-z', commit) : git(root, 'ls-files', '--stage', '-z');
  return listing.toString('utf8').split('\0').filter(Boolean).map(record => {
    const separator = record.indexOf('\t');
    const header = record.slice(0, separator), name = record.slice(separator + 1);
    const [mode, second, third] = header.split(' ');
    requireThat(commit || third === '0', 'Resolve index conflicts before publication checks.');
    const oid = commit ? third : second;
    return { name, mode, bytes: mode === '160000' ? Buffer.alloc(0) : git(root, 'cat-file', 'blob', oid) };
  });
}

function policyFor(entries) {
  const entry = entries.find(e => e.name === POLICY);
  requireThat(entry, 'Missing publication policy.');
  return validatePolicy(JSON.parse(entry.bytes.toString('utf8')));
}

function check(entries) {
  const policy = policyFor(entries);
  const errors = validateSnapshot(entries, policy);
  if (errors.length) throw new Error(`Publication check failed:\n${errors.map(e => `  ${e}`).join('\n')}`);
  return policy;
}

function install(root) {
  const values = {
    'core.hooksPath': '.githooks',
    'filter.public-image.clean': 'node "Tools~/privacy.mjs" clean-image %f',
    'filter.public-image.required': 'true',
  };
  // Do not replace an existing hook or filter configuration silently.
  for (const [key, value] of Object.entries(values)) {
    let current;
    try { current = git(root, 'config', '--get', key).toString().trim(); }
    catch (error) { if (error.status !== 1) throw error; }
    requireThat(current === undefined || current === value, `Existing ${key} differs; inspect it before installing.`);
    if (key === 'core.hooksPath' && current === undefined) {
      const hooks = path.resolve(root, git(root, 'rev-parse', '--git-path', 'hooks').toString().trim());
      for (const name of ['pre-commit', 'pre-push']) {
        requireThat(!fs.existsSync(path.join(hooks, name)), 'An existing Git hook is present; inspect it before changing hooksPath.');
      }
    }
  }
  for (const [key, value] of Object.entries(values)) git(root, 'config', '--local', key, value);
  if (process.platform !== 'win32') {
    for (const name of ['pre-commit', 'pre-push']) fs.chmodSync(path.join(root, '.githooks', name), 0o755);
  }
  console.log('Installed repository-local image filter, pre-commit and pre-push hooks.');
}

function checkPush(root, input) {
  let hold;
  try { hold = git(root, 'config', '--bool', '--get', 'publication.pushHold').toString().trim(); }
  catch (error) { if (error.status !== 1) throw error; }
  requireThat(hold !== 'true', 'Push is on hold until publication decisions and the final audit are complete.');
  const commits = new Set();
  for (const line of input.trim().split('\n').filter(Boolean)) {
    const fields = line.trim().split(/\s+/);
    requireThat(fields.length === 4, 'Invalid pre-push input.');
    const [, localOid, , remoteOid] = fields;
    requireThat(/^[0-9a-f]{40}(?:[0-9a-f]{24})?$/.test(localOid) && /^[0-9a-f]{40}(?:[0-9a-f]{24})?$/.test(remoteOid), 'Invalid push object ID.');
    if (/^0+$/.test(localOid)) continue;
    const args = ['rev-list', '--reverse', localOid];
    if (!/^0+$/.test(remoteOid)) args.push(`^${remoteOid}`);
    // If the remote base is unavailable, fail and ask the developer to fetch first.
    let pending;
    try { pending = git(root, ...args).toString().trim().split('\n').filter(Boolean); }
    catch { throw new Error('Cannot inspect outgoing history; fetch the remote base before retrying.'); }
    for (const oid of pending) commits.add(oid);
  }
  for (const oid of commits) {
    try {
      const identities = git(root, 'show', '-s', '--format=%an <%ae>%n%cn <%ce>%n%B', oid).toString('utf8');
      requireThat(textIssues(identities).length === 0, 'Commit identity or message contains potentially private data.');
      check(gitEntries(root, oid));
    } catch (error) { throw new Error(`Push blocked at commit ${oid.slice(0, 12)}: ${error.message}`); }
  }
  console.log(`Publication check passed: ${commits.size} outgoing commits.`);
}

export function main(args = process.argv.slice(2)) {
  const [command, argument] = args;
  const root = git(process.cwd(), 'rev-parse', '--show-toplevel').toString().trim();
  if (command === 'clean-image') {
    requireThat(argument, 'Expected an image path.');
    const policy = validatePolicy(JSON.parse(fs.readFileSync(path.join(root, POLICY), 'utf8')));
    process.stdout.write(sanitizeImage(fs.readFileSync(0), path.extname(argument).toLowerCase(), policy.allowedIccProfiles));
    return;
  }
  if (command === 'install') { install(root); return; }
  if (command === 'check-push') { checkPush(root, fs.readFileSync(0, 'utf8')); return; }
  if (command === 'check-author') {
    const identity = ['GIT_AUTHOR_IDENT', 'GIT_COMMITTER_IDENT'].map(name => git(root, 'var', name).toString('utf8')).join('\n');
    requireThat(textIssues(identity).length === 0, 'Use a public Git author identity (for example a GitHub noreply email).');
    console.log('Git author identity passed.');
    return;
  }
  if (command === 'check-staged' || command === 'check-commit') {
    requireThat(command !== 'check-commit' || argument, 'Expected a commit reference.');
    const entries = gitEntries(root, command === 'check-commit' ? argument : undefined);
    check(entries);
    console.log(`Publication check passed: ${entries.length} Git files.`);
    return;
  }
  const entries = worktreeEntries(root);
  if (command === 'strip-images') {
    const policy = policyFor(entries);
    let changed = 0;
    // Only explicitly registered images are modified; never sweep arbitrary local directories.
    for (const { name, bytes } of entries) {
      const extension = path.extname(name).toLowerCase();
      if (!policy.files.includes(name) || !['.png', '.webp'].includes(extension)) continue;
      const cleaned = sanitizeImage(bytes, extension, policy.allowedIccProfiles);
      if (!cleaned.equals(bytes)) { fs.writeFileSync(path.join(root, name), cleaned); changed++; }
    }
    console.log(`Removed image metadata: ${changed} files.`);
    return;
  }
  requireThat(command === 'check-worktree' || command === 'export', 'Usage: privacy.mjs install|check-worktree|check-staged|check-commit REF|strip-images|export NEW_DIRECTORY');
  check(entries);
  if (command === 'export') {
    requireThat(argument, 'Expected a new destination directory.');
    const destination = path.resolve(argument);
    requireThat(!fs.existsSync(destination), 'Export destination must not exist.');
    // No .git directory, ignored files, local settings, or history are copied.
    fs.mkdirSync(destination, { recursive: true });
    for (const { name, bytes } of entries) {
      const target = path.join(destination, name);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, bytes);
      if (name.startsWith('.githooks/') && process.platform !== 'win32') fs.chmodSync(target, 0o755);
    }
    console.log(`Exported ${entries.length} checked files without Git history.`);
  } else console.log(`Publication check passed: ${entries.length} working-tree files.`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { main(); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
