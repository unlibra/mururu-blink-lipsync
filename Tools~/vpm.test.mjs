import test from 'node:test';
import assert from 'node:assert/strict';
import { PACKAGE_ID, validateManifest, zipBytes, verifyVpm } from './vpm.mjs';

test('ZIP inventory rejects escaping, ambiguous, oversized and non-binary entries', () => {
  for (const name of ['../escape', '/absolute', 'Editor/../escape', 'a//b', './file', 'C:/file', 'a\\b'])
    assert.throws(() => zipBytes(new Map([[name, Buffer.from('x')]])), /Invalid ZIP entry/);
  assert.throws(() => zipBytes(new Map()), /inventory/);
  assert.throws(() => zipBytes(new Map([['a', 'text']])), /Invalid ZIP entry/);
});
test('ZIP order is canonical and tampering or extra entries fail verification', () => {
  const files = new Map([['package.json', Buffer.from('{}')], ['LICENSE.txt', Buffer.from('license')]]);
  const zip = zipBytes(files);
  assert.deepEqual(zip, zipBytes(new Map([...files].reverse())));
  const changed = Buffer.from(zip); changed[10] ^= 1;
  assert.throws(() => verifyVpm(changed, files), /mismatch/);
  assert.throws(() => verifyVpm(Buffer.concat([zip, Buffer.from('trailing data')]), files), /mismatch/);
});
test('VPM metadata pins identity and URL and rejects unverified migration deletion', () => {
  const version = '1.2.0';
  const good = { name: PACKAGE_ID, version, url: `https://github.com/unlibra/mururu-blink-lipsync/releases/download/v${version}/${PACKAGE_ID}-${version}.zip`, unity: '2022.3', license: 'SEE LICENSE IN LICENSE.txt', author: { name: 'unlibra', email: '63234154+unlibra@users.noreply.github.com' }, vpmDependencies: { 'com.vrchat.avatars': '>=3.10.5 <4.0.0', 'nadena.dev.modular-avatar': '>=1.18.7 <2.0.0', 'nadena.dev.ndmf': '>=1.14.8 <2.0.0' } };
  assert.equal(validateManifest(Buffer.from(JSON.stringify(good)), version).name, PACKAGE_ID);
  for (const change of [{ name: 'com.other.tool' }, { version: '1.1.2' }, { url: 'https://example.com/a.zip' }, { legacyFolders: {} }, { legacyFiles: {} }, { zipSHA256: 'a'.repeat(64) }, { vpmDependencies: {} }])
    assert.throws(() => validateManifest(Buffer.from(JSON.stringify({ ...good, ...change })), version), /Invalid VPM manifest/);
});
