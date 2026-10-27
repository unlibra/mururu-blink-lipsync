import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { buildRelease, releaseSnapshot, verifyBuilds, verifyPackage } from './release.mjs';

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'mururu-release-tests-'));
  t.after(() => {
    assert.equal(path.dirname(root), os.tmpdir());
    assert.ok(path.basename(root).startsWith('mururu-release-tests-'));
    fs.rmSync(root, { recursive: true, force: true });
  });
  const git = (...args) => execFileSync('git', ['-C', root, ...args], { stdio: ['ignore', 'pipe', 'pipe'] }).toString().trim();
  const write = (name, bytes) => {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), bytes);
  };
  const commit = () => { git('add', '-A'); git('commit', '-qm', 'Release fixture'); };
  git('init', '--initial-branch=main', '--quiet');
  git('config', 'user.useConfigOnly', 'true');
  git('config', 'user.name', 'Release tests');
  git('config', 'user.email', '1+tests@users.noreply.github.com');
  git('config', 'core.autocrlf', 'false');
  write('.gitignore', 'dist~/\n');
  write('.gitattributes', 'README.md text\n');
  const sources = {
    'Editor/Window/MururuBlinkLipSyncSetupWindow.cs': 'private const string ToolVersion = "1.2.3";\n',
    'Runtime/Record.cs': '// Runtime record\n',
    'README.md': '# Fixture\n',
    'CHANGELOG.md': '# Changelog\n\n## [Unreleased]\n\n## [1.2.3] - 2026-10-28\n\n### Fixed\n\n- Test fix.\n\n## [1.2.2] - 2026-10-27\n\n- Old fix.\n',
    'LICENSE.txt': 'PolyForm Perimeter License 1.0.1\n',
    'NOTICE.txt': 'Required Notice: Copyright (c) 2026 unlibra\n',
  };
  for (const [name, bytes] of Object.entries(sources)) write(name, bytes);
  for (const name of ['Editor', 'Editor/Window', 'Runtime', ...Object.keys(sources)]) {
    const guid = createHash('md5').update(name).digest('hex');
    write(`${name}.meta`, `fileFormatVersion: 2\nguid: ${guid}\n${name.includes('.') ? '' : 'folderAsset: yes\n'}`);
  }
  write('Tests/should-not-ship.cs', 'test only');
  write('Generated/should-not-ship.asset', 'generated only');
  write('Documentation~/should-not-ship.md', 'documentation only');
  commit();
  return { root, git, write, commit };
}

test('native tar independently extracts the Unity layout, original bytes, GUIDs and legal files', t => {
  const { root } = fixture(t);
  const snapshot = releaseSnapshot(root);
  const manifest = buildRelease(root);
  const packagePath = path.join(root, 'dist~', manifest.files[0].name);
  const packageBytes = fs.readFileSync(packagePath);
  assert.equal(packageBytes[9], 255, 'gzip must not encode the host OS');
  assert.deepEqual(packageBytes.subarray(4, 8), Buffer.alloc(4), 'gzip must not encode a host timestamp');
  const extracted = path.join(root, 'dist~/extracted');
  fs.mkdirSync(extracted);
  execFileSync('tar', ['-xzf', packagePath, '-C', extracted]);
  const paths = [];
  for (const guid of fs.readdirSync(extracted)) {
    assert.match(guid, /^[0-9a-f]{32}$/);
    const entry = path.join(extracted, guid);
    const assetPath = fs.readFileSync(path.join(entry, 'pathname'), 'utf8');
    paths.push(assetPath);
    const meta = fs.readFileSync(path.join(entry, 'asset.meta'));
    assert.match(meta.toString(), new RegExp(`^guid: ${guid}$`, 'm'));
    if (assetPath.startsWith('Assets/unlibra/MururuBlinkLipSync/')) {
      const source = assetPath.slice('Assets/unlibra/MururuBlinkLipSync/'.length);
      assert.deepEqual(meta, fs.readFileSync(path.join(root, `${source}.meta`)));
      if (fs.statSync(path.join(root, source)).isFile()) {
        assert.deepEqual(fs.readFileSync(path.join(entry, 'asset')), fs.readFileSync(path.join(root, source)));
      }
    }
  }
  assert.equal(fs.readdirSync(extracted).length, snapshot.assetCount + 2);
  for (const file of ['LICENSE.txt', 'NOTICE.txt']) assert.ok(paths.includes(`Assets/unlibra/MururuBlinkLipSync/${file}`));
  assert.ok(paths.every(name => !/\/(?:Tests|Generated|Documentation~)(?:\/|$)/.test(name)));
  assert.ok(fs.readdirSync(path.join(root, 'dist~')).every(name => !name.endsWith('.zip')));
  const note = fs.readFileSync(path.join(root, 'dist~/release-notes.md'), 'utf8');
  assert.ok(note.includes('Test fix.') && !note.includes('Old fix.'));
  assert.equal(manifest.sourceDirty, false);
});

test('committed blob inputs produce identical packages in LF and CRLF checkouts', t => {
  const { root } = fixture(t);
  const first = buildRelease(root);
  const firstBytes = fs.readFileSync(path.join(root, 'dist~', first.files[0].name));
  const clone = path.join(root, 'dist~/crlf-checkout');
  execFileSync('git', ['clone', '--quiet', '--no-hardlinks', '--config', 'core.autocrlf=true', root, clone]);
  assert.ok(fs.readFileSync(path.join(clone, 'README.md'), 'utf8').includes('\r\n'));
  const second = buildRelease(clone);
  assert.deepEqual(fs.readFileSync(path.join(clone, 'dist~', second.files[0].name)), firstBytes);
});

test('release builds reject dirty or untracked input and malformed version, missing license, duplicate GUID or linked assets', t => {
  const { root, git, write, commit } = fixture(t);
  write('untracked.txt', 'not reviewed');
  assert.throws(() => buildRelease(root), /clean committed checkout/);
  fs.unlinkSync(path.join(root, 'untracked.txt'));
  write('Runtime/Record.cs.meta', fs.readFileSync(path.join(root, 'README.md.meta')));
  commit();
  assert.throws(() => buildRelease(root), /duplicate GUID/);
  git('reset', '--hard', 'HEAD~1');
  git('rm', 'LICENSE.txt'); git('commit', '-qm', 'Missing license fixture');
  assert.throws(() => buildRelease(root), /Missing or linked release source: LICENSE.txt/);
  git('reset', '--hard', 'HEAD~1');
  write('CHANGELOG.md', '## [9.9.9] - 2026-10-28\n\nWrong version.\n'); commit();
  assert.throws(() => buildRelease(root), /matching CHANGELOG/);
  git('reset', '--hard', 'HEAD~1');
  const oid = git('hash-object', '-w', '--stdin');
  git('update-index', '--add', '--cacheinfo', `120000,${oid},Runtime/Record.cs`);
  git('commit', '-qm', 'Linked asset fixture');
  assert.throws(() => releaseSnapshot(root), /linked release source: Runtime\/Record.cs/);
});

test('publication requires matching tag, commit, notes, manifests, content and both platform packages', t => {
  const { root } = fixture(t);
  const manifest = buildRelease(root);
  const first = path.join(root, 'dist~/linux'), second = path.join(root, 'dist~/windows');
  for (const dir of [first, second]) {
    fs.mkdirSync(dir);
    for (const name of [manifest.files[0].name, 'release-manifest.json', 'release-notes.md']) fs.copyFileSync(path.join(root, 'dist~', name), path.join(dir, name));
  }
  assert.ok(verifyBuilds(root, [first, second], 'v1.2.3', manifest.sourceCommit).packagePath.endsWith('.unitypackage'));
  assert.throws(() => verifyBuilds(root, [first, second], 'v9.9.9', manifest.sourceCommit), /Tag does not match/);
  assert.throws(() => verifyBuilds(root, [first, second], 'v1.2.3', '0'.repeat(40)), /commit mismatch/);
  assert.throws(() => verifyBuilds(root, [first], 'v1.2.3', manifest.sourceCommit), /Both Linux and Windows/);
  fs.appendFileSync(path.join(second, 'release-notes.md'), 'Unexpected notes.');
  assert.throws(() => verifyBuilds(root, [first, second], 'v1.2.3', manifest.sourceCommit), /notes mismatch/);
  fs.copyFileSync(path.join(first, 'release-notes.md'), path.join(second, 'release-notes.md'));
  const invalidManifest = { ...manifest, sourceCommit: '0'.repeat(40) };
  fs.writeFileSync(path.join(second, 'release-manifest.json'), JSON.stringify(invalidManifest));
  assert.throws(() => verifyBuilds(root, [first, second], 'v1.2.3', manifest.sourceCommit), /manifest mismatch/);
  fs.copyFileSync(path.join(first, 'release-manifest.json'), path.join(second, 'release-manifest.json'));
  const corrupted = Buffer.from(fs.readFileSync(path.join(second, manifest.files[0].name)));
  corrupted[Math.floor(corrupted.length / 2)] ^= 1;
  assert.throws(() => verifyPackage(corrupted, releaseSnapshot(root).entries));
  fs.writeFileSync(path.join(second, manifest.files[0].name), corrupted);
  assert.throws(() => verifyBuilds(root, [first, second], 'v1.2.3', manifest.sourceCommit));
});
