import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { sanitizeImage, sha256, pngChunk, riffChunk, textIssues, validateSnapshot } from './privacy.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const signature = Buffer.from('89504e470d0a1a0a', 'hex');
const ihdr = Buffer.from('00000001000000010806000000', 'hex');
const png = Buffer.concat([signature, pngChunk('IHDR', ihdr), pngChunk('IDAT', zlib.deflateSync(Buffer.from([0, 255, 0, 0, 255]))), pngChunk('IEND', Buffer.alloc(0))]);
const note = Buffer.from('Author\0' + 'private' + '@' + 'example.invalid');
const taggedPng = Buffer.concat([signature, pngChunk('IHDR', ihdr), pngChunk('tEXt', note), png.subarray(33), Buffer.from('private trailer')]);
const policyPath = 'Tools~/public-files.json';

function webp(chunks) {
  const body = Buffer.concat(chunks);
  const header = Buffer.from('524946460000000057454250', 'hex');
  header.writeUInt32LE(body.length + 4, 4);
  return Buffer.concat([header, body]);
}

function policy(files, extra = {}) {
  return { version: 1, files: [policyPath, ...files], allowedIccProfiles: [], reviewedPdfs: {}, ...extra };
}

test('PNG cleaning removes text, EXIF, unknown ancillary chunks and trailers without recompression', () => {
  const input = Buffer.concat([signature, pngChunk('IHDR', ihdr), pngChunk('iTXt', note), pngChunk('eXIf', note), pngChunk('vpAg', note), png.subarray(33), note]);
  assert.deepEqual(sanitizeImage(input, '.png', []), png);
  assert.deepEqual(sanitizeImage(taggedPng, '.png', []), png);
  assert.deepEqual(sanitizeImage(png, '.png', []), png);
});

test('ICC profile bytes are reviewed by hash and their arbitrary name is removed', () => {
  const profile = Buffer.from('reviewed profile fixture');
  const input = Buffer.concat([signature, pngChunk('IHDR', ihdr), pngChunk('iCCP', Buffer.concat([Buffer.from('personal profile name\0\0'), zlib.deflateSync(profile)])), png.subarray(33)]);
  assert.throws(() => sanitizeImage(input, '.png', []), /Unreviewed ICC/);
  const cleaned = sanitizeImage(input, '.png', [sha256(profile)]);
  assert.equal(cleaned.includes(Buffer.from('personal profile name')), false);
  assert.deepEqual(sanitizeImage(cleaned, '.png', [sha256(profile)]), cleaned);
});

test('corrupt/unsupported image containers fail instead of passing through', () => {
  const corrupt = Buffer.from(png); corrupt[45] ^= 1;
  assert.throws(() => sanitizeImage(corrupt, '.png', []), /checksum/);
  assert.throws(() => sanitizeImage(png.subarray(0, -2), '.png', []), /Truncated|size/);
  assert.throws(() => sanitizeImage(Buffer.from('not an image'), '.png', []), /signature/);
  assert.throws(() => sanitizeImage(png, '.jpg', []), /Only PNG/);
});

test('WebP removes EXIF/XMP/unknown chunks and flags, preserving encoded pixels', () => {
  const header = Buffer.alloc(10); header[0] = 0x0c;
  const pixels = Buffer.from('encoded-image-fixture');
  const input = webp([riffChunk('VP8X', header), riffChunk('VP8 ', pixels), riffChunk('EXIF', note), riffChunk('XMP ', note), riffChunk('JUNK', note)]);
  const cleanHeader = Buffer.alloc(10);
  const expected = webp([riffChunk('VP8X', cleanHeader), riffChunk('VP8 ', pixels)]);
  assert.deepEqual(sanitizeImage(input, '.webp', []), expected);
  assert.deepEqual(sanitizeImage(expected, '.webp', []), expected);
});

test('animated WebP retains animation headers and frame pixels while removing nested metadata', () => {
  const extended = Buffer.alloc(10); extended[0] = 0x06;
  const frame = Buffer.alloc(16); frame.writeUIntLE(100, 12, 3);
  const cleanFrame = riffChunk('ANMF', Buffer.concat([frame, riffChunk('VP8L', Buffer.from('frame pixels'))]));
  const dirtyFrame = riffChunk('ANMF', Buffer.concat([frame, riffChunk('VP8L', Buffer.from('frame pixels')), riffChunk('XMP ', note)]));
  const input = webp([riffChunk('VP8X', extended), riffChunk('ANIM', Buffer.alloc(6)), dirtyFrame, riffChunk('EXIF', note)]);
  extended[0] = 0x02;
  assert.deepEqual(sanitizeImage(input, '.webp', []), webp([riffChunk('VP8X', extended), riffChunk('ANIM', Buffer.alloc(6)), cleanFrame]));
});

test('text detection covers escaped paths, identities, credentials and emails without leaking values', () => {
  const userPath = ['C:', 'Users', 'someone', 'work'].join('\\');
  assert.ok(textIssues(JSON.stringify({ path: userPath })).includes('local user path'));
  assert.ok(textIssues('/' + ['home', 'someone', 'file'].join('/')).includes('local user path'));
  assert.ok(textIssues('usr_' + '12345678-1234-1234-1234-123456789abc').includes('VRChat identity'));
  assert.ok(textIssues('ghp_' + 'A'.repeat(36)).includes('access token'));
  assert.ok(textIssues('pass' + 'word="a-secret-fixture"').includes('credential assignment'));
  assert.ok(textIssues('someone' + '@' + 'example.invalid').includes('email address'));
  assert.deepEqual(textIssues('41898282+github-actions[bot]' + '@' + 'users.noreply.github.com'), []);
  assert.deepEqual(textIssues('noreply' + '@' + 'github.com'), []);
  assert.ok(textIssues('employee' + '@' + 'github.com').includes('email address'));
});

test('snapshots require explicit paths, reviewed PDFs, canonical images and regular files', () => {
  const p = policy(['README.md', 'image.png', 'terms.pdf']);
  const entries = [
    { name: policyPath, mode: '100644', bytes: Buffer.from(JSON.stringify(p)) },
    { name: 'README.md', mode: '100644', bytes: Buffer.from('public documentation') },
    { name: 'image.png', mode: '100644', bytes: taggedPng },
    { name: 'terms.pdf', mode: '100644', bytes: Buffer.from('%PDF-unreviewed') },
    { name: 'internal.md', mode: '100644', bytes: Buffer.from('private notes') },
    { name: 'link', mode: '120000', bytes: Buffer.from('elsewhere') },
  ];
  const errors = validateSnapshot(entries, p);
  assert.ok(errors.some(e => e.includes('image.png: Image has metadata')));
  assert.ok(errors.some(e => e.includes('terms.pdf: PDF is new or changed')));
  assert.ok(errors.some(e => e.includes('internal.md: file is not registered')));
  const linkPolicy = policy(['link']);
  assert.ok(validateSnapshot([entries[0], entries[5]], linkPolicy).some(e => e.includes('links and submodules')));
});

function git(root, ...args) {
  return execFileSync('git', ['-C', root, ...args], { env: { ...process.env, GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config'), GIT_CONFIG_NOSYSTEM: '1' }, stdio: ['ignore', 'pipe', 'pipe'] });
}

test('installed Git filter and hook inspect staged bytes, including partial staging', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'mbls-privacy-test-'));
  try {
    git(root, 'init');
    fs.mkdirSync(path.join(root, 'Tools~')); fs.mkdirSync(path.join(root, '.githooks'));
    fs.copyFileSync(path.join(here, 'privacy.mjs'), path.join(root, 'Tools~/privacy.mjs'));
    const repoRoot = path.dirname(here);
    for (const name of ['.gitattributes', '.githooks/pre-commit', '.githooks/pre-push']) fs.copyFileSync(path.join(repoRoot, name), path.join(root, name));
    for (const name of ['pre-commit', 'pre-push']) fs.chmodSync(path.join(root, '.githooks', name), 0o755);
    const names = ['Tools~/privacy.mjs', '.gitattributes', '.githooks/pre-commit', '.githooks/pre-push', 'README.md', 'image.png'];
    fs.writeFileSync(path.join(root, policyPath), JSON.stringify(policy(names)));
    fs.writeFileSync(path.join(root, 'README.md'), 'public');
    fs.writeFileSync(path.join(root, 'image.png'), taggedPng);
    const run = (args, input) => spawnSync(process.execPath, [path.join(root, 'Tools~/privacy.mjs'), ...args], { cwd: root, input, encoding: 'utf8', env: { ...process.env, GIT_CONFIG_GLOBAL: path.join(root, 'no-global-config'), GIT_CONFIG_NOSYSTEM: '1' } });
    const cli = (...args) => run(args);
    const priorHook = path.join(root, '.git/hooks/pre-push');
    fs.writeFileSync(priorHook, '#!/bin/sh\nexit 0\n');
    assert.notEqual(cli('install').status, 0);
    fs.unlinkSync(priorHook);
    assert.equal(cli('install').status, 0);
    git(root, 'add', '.');
    assert.deepEqual(git(root, 'show', ':image.png'), png);
    assert.deepEqual(fs.readFileSync(path.join(root, 'image.png')), taggedPng, 'clean filter should not modify the artist working copy');
    assert.equal(cli('check-staged').status, 0);
    const privateEmail = 'someone' + '@' + 'example.invalid';
    fs.writeFileSync(path.join(root, 'README.md'), privateEmail);
    git(root, 'add', 'README.md');
    fs.writeFileSync(path.join(root, 'README.md'), 'public again');
    const bad = cli('check-staged');
    assert.notEqual(bad.status, 0);
    assert.match(bad.stderr, /email address/);
    assert.equal(bad.stderr.includes(privateEmail), false);
    assert.throws(() => git(root, '-c', 'user.name=Privacy Test', '-c', 'user.email=noreply@users.noreply.github.com', 'commit', '-m', 'blocked fixture'), /Command failed/);
    git(root, 'add', 'README.md');
    assert.equal(cli('check-staged').status, 0);
    const commit = message => git(root, '-c', 'user.name=Privacy Test', '-c', 'user.email=noreply@users.noreply.github.com', 'commit', '-m', message);
    commit('public fixture');
    const first = git(root, 'rev-parse', 'HEAD').toString().trim();
    const newPush = oid => `refs/heads/main ${oid} refs/heads/main ${'0'.repeat(40)}\n`;
    assert.equal(run(['check-push'], newPush(first)).status, 0);
    git(root, 'config', '--local', 'publication.pushHold', 'true');
    const held = run(['check-push'], newPush(first));
    assert.notEqual(held.status, 0);
    assert.match(held.stderr, /Push is on hold/);
    git(root, 'config', '--local', '--unset', 'publication.pushHold');
    assert.equal(run(['check-push'], newPush(first)).status, 0);
    // Simulate a client bypassing the hook: a later repair must not hide the earlier leak.
    fs.writeFileSync(path.join(root, 'README.md'), privateEmail);
    git(root, 'add', 'README.md');
    git(root, '-c', 'core.hooksPath=disabled-test-hooks', '-c', 'user.name=Privacy Test', '-c', 'user.email=noreply@users.noreply.github.com', 'commit', '-m', 'unsafe fixture');
    fs.writeFileSync(path.join(root, 'README.md'), 'repaired');
    git(root, 'add', 'README.md');
    commit('repair fixture');
    const tip = git(root, 'rev-parse', 'HEAD').toString().trim();
    assert.equal(cli('check-commit', tip).status, 0);
    const rejected = run(['check-push'], newPush(tip));
    assert.notEqual(rejected.status, 0);
    assert.match(rejected.stderr, /Push blocked at commit/);
    assert.equal(rejected.stderr.includes(privateEmail), false);
    assert.notEqual(run(['check-push'], `refs/heads/main ${tip} refs/heads/main ${first}\n`).status, 0);
    const destination = path.join(root, 'snapshot');
    // Exports reject unregistered local files; ignored local artifacts never enter an export.
    fs.writeFileSync(path.join(root, 'private-draft.md'), 'private notes');
    assert.notEqual(cli('export', destination).status, 0);
    assert.equal(fs.existsSync(destination), false);
    fs.unlinkSync(path.join(root, 'private-draft.md'));
    fs.writeFileSync(path.join(root, 'image.png'), png);
    assert.equal(cli('export', destination).status, 0);
    assert.equal(fs.existsSync(path.join(destination, '.git')), false);
    assert.deepEqual(fs.readFileSync(path.join(destination, 'image.png')), png);
    // A missing filter executable is an error, never an unfiltered pass-through.
    git(root, 'config', '--local', 'filter.public-image.clean', 'missing-public-image-cleaner');
    assert.throws(() => git(root, 'add', '--renormalize', 'image.png'));
    // Existing user hook configuration must not be overwritten by setup.
    git(root, 'config', '--local', 'core.hooksPath', 'existing-hooks');
    assert.notEqual(cli('install').status, 0);
    assert.equal(git(root, 'config', '--get', 'core.hooksPath').toString().trim(), 'existing-hooks');
  } finally {
    const resolved = path.resolve(root);
    assert.ok(resolved.startsWith(path.resolve(os.tmpdir()) + path.sep));
    assert.ok(path.basename(resolved).startsWith('mbls-privacy-test-'));
    fs.rmSync(resolved, { recursive: true, force: true });
  }
});
