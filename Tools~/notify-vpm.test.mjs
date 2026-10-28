import test from 'node:test';
import assert from 'node:assert/strict';
import { checkVpm, notifyVpm } from './notify-vpm.mjs';
const fixtureValue = 'fixture-fixtureValue', id = 'com.unlibra.mururu-blink-lipsync';
const index = { id: 'com.unlibra.vpm', url: 'https://docs.unlibra.com/vpm/index.json', packages: {} };
const run = { id: 42, head_branch: 'main', event: 'workflow_dispatch', path: '.github/workflows/sync.yml', status: 'completed', conclusion: 'success' };
const published = { ...index, packages: { [id]: { versions: { '1.2.0': { name: id, version: '1.2.0', zipSHA256: 'a'.repeat(64), url: `https://github.com/unlibra/mururu-blink-lipsync/releases/download/v1.2.0/${id}-1.2.0.zip` } } } } };
const json = value => new Response(JSON.stringify(value));
test('preflight requires an active workflow and the public listing without leaking the dispatch credential', async () => {
  const calls = [];
  await checkVpm(fixtureValue, async (url, options) => {
    calls.push({ url, options });
    return json(url.startsWith('https://api.github.com/') ? { state: 'active', path: '.github/workflows/sync.yml' } : index);
  });
  assert.equal(calls.length, 2);
  assert.equal(calls[0].options.headers.Authorization, `Bearer ${fixtureValue}`);
  assert.equal(calls[1].options.headers.Authorization, undefined);
  assert.equal(calls[0].options.redirect, 'error');
  await assert.rejects(checkVpm('', async () => { throw Error('must not fetch'); }), /required/);
  await assert.rejects(checkVpm(fixtureValue, async () => json({ state: 'disabled_manually' })), /not active/);
});
test('notification targets only the vpm main workflow and waits for its exact run and published version', async () => {
  const calls = []; let reads = 0;
  const url = await notifyVpm('v1.2.0', fixtureValue, { sleep: async () => {}, fetcher: async (url, options) => {
    calls.push({ url, options });
    if (options.method === 'POST') return json({ workflow_run_id: 42 });
    if (url.endsWith('/actions/runs/42')) return json(++reads === 1 ? { ...run, status: 'in_progress' } : run);
    return json(published);
  } });
  assert.equal(url, 'https://github.com/unlibra/vpm/actions/runs/42');
  assert.deepEqual(JSON.parse(calls[0].options.body), { ref: 'main', inputs: { package_id: id, release_tag: 'v1.2.0' } });
  assert.equal(reads, 2);
  assert.equal(calls.at(-1).options.headers.Authorization, undefined);
});
test('failed, unexpected and uncorrelated workflow runs cannot report successful publication', async () => {
  for (const changed of [{ conclusion: 'failure' }, { id: 99 }, { head_branch: 'feature/other' }, { path: '.github/workflows/other.yml' }]) {
    await assert.rejects(notifyVpm('v1.2.0', fixtureValue, { fetcher: async (url, options) => json(options.method === 'POST' ? { workflow_run_id: 42 } : url.includes('api.github.com') ? { ...run, ...changed } : index) }));
  }
  await assert.rejects(notifyVpm('latest', fixtureValue), /stable/);
  await assert.rejects(notifyVpm('v1.2.0', fixtureValue, { fetcher: async () => json({}) }), /run ID/);
});
test('API failures, timeouts and a missing published version fail visibly', async () => {
  await assert.rejects(checkVpm(fixtureValue, async () => new Response('{}', { status: 403 })), /403/);
  let ticks = 0;
  await assert.rejects(notifyVpm('v1.2.0', fixtureValue, { now: () => ticks++ * 100, timeoutMs: 50, fetcher: async () => json({ workflow_run_id: 42 }) }), /Timed out/);
  ticks = 0;
  await assert.rejects(notifyVpm('v1.2.0', fixtureValue, { now: () => ticks++ * 100, timeoutMs: 250, sleep: async () => {}, fetcher: async (url, options) => json(options.method === 'POST' ? { workflow_run_id: 42 } : url.includes('api.github.com') ? run : index) }), /Timed out/);
});
test('superseded runs and Pages propagation are accepted only after the expected immutable version appears', async () => {
  let pages = 0;
  const result = await notifyVpm('v1.2.0', fixtureValue, { sleep: async () => {}, fetcher: async (url, options) => json(options.method === 'POST' ? { workflow_run_id: 42 } : url.includes('api.github.com') ? { ...run, conclusion: 'cancelled' } : ++pages === 1 ? index : published) });
  assert.equal(pages, 2);
  assert.equal(result, 'https://github.com/unlibra/vpm/actions/runs/42');
});
