import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { SITE, INDEXNOW_KEY, pageContent, pageUrl, sitemapUrls, changedUrls, stamp, planChanges, submitPlan, waitForRevision } from './website.mjs';

const revision = 'a'.repeat(40);
const state = pages => ({ version: 1, revision, pages });
const digest = '1'.repeat(64);
const html = text => `<title>Manual</title><meta name="description" content="Guide"><main><p>${text}</p></main>`;
const sitemap = urls => `<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${urls.map(url => `<url><loc>${url}</loc></url>`).join('')}</urlset>`;
const jsonResponse = value => new Response(JSON.stringify(value), { status: 200 });

function fixture(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'mururu-site-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(directory).startsWith('mururu-site-'));
    fs.rmSync(directory, { recursive: true, force: true });
  });
  fs.writeFileSync(path.join(directory, 'index.html'), html('Hello'));
  fs.writeFileSync(path.join(directory, '404.html'), html('Missing'));
  fs.writeFileSync(path.join(directory, 'sitemap.xml'), sitemap([SITE]));
  stamp(directory, revision);
  return directory;
}

test('content comparison ignores theme scripts and observes text, links, images and metadata', () => {
  const original = html('Hello');
  assert.equal(pageContent(original), pageContent(original.replace('</main>', '<script>theme()</script><style>p{color:red}</style></main>') + '<script src="theme.123.js"></script>'));
  for (const changed of [html('Changed'), original.replace('Guide', 'Other'), original.replace('</p>', '</p><a href="/new">Link</a>'), original.replace('</p>', '</p><img src="new.webp" alt="New">')]) {
    assert.notEqual(pageContent(original), pageContent(changed));
  }
});

test('URL parsing limits every submission to this project and normalizes clean URLs', () => {
  assert.equal(pageUrl('index.html'), SITE);
  assert.equal(pageUrl('installation.html'), SITE + 'installation');
  assert.equal(pageUrl('nested/index.html'), SITE + 'nested/');
  for (const file of ['../index.html', '/index.html', 'nested/../index.html', 'nested\\index.html']) assert.throws(() => pageUrl(file));
  assert.deepEqual(sitemapUrls(sitemap([SITE])), [SITE]);
  assert.throws(() => sitemapUrls(sitemap(['https://docs.unlibra.com/another-project/'])));
  assert.throws(() => sitemapUrls('<sitemapindex><sitemap></sitemap></sitemapindex>'));
  assert.throws(() => sitemapUrls(sitemap([SITE, SITE])));
});

test('successful publication comparison includes added, changed and deleted pages', () => {
  const previous = state({ [SITE]: digest, [SITE + 'deleted']: digest, [SITE + 'same']: digest });
  const next = state({ [SITE]: '2'.repeat(64), [SITE + 'added']: digest, [SITE + 'same']: digest });
  assert.deepEqual(changedUrls(previous, next), [SITE, SITE + 'added', SITE + 'deleted']);
  assert.throws(() => changedUrls(previous, state({ ['https://example.com/']: digest })));
});

test('build stamp requires an exact match between generated pages and sitemap', t => {
  const directory = fixture(t);
  const value = JSON.parse(fs.readFileSync(path.join(directory, 'publication.json')));
  assert.deepEqual(Object.keys(value.pages), [SITE]);
  fs.writeFileSync(path.join(directory, 'extra.html'), html('Extra'));
  assert.throws(() => stamp(directory, revision), /sitemap differ/);
});

test('first migration compares live pages, detects removed terms and never posts', async t => {
  const directory = fixture(t);
  const requests = [];
  const fetcher = async (url, options) => {
    requests.push(url);
    assert.notEqual(options.method, 'POST');
    if (url.endsWith('publication.json')) return new Response('', { status: 404 });
    if (url.endsWith('sitemap.xml')) return new Response(sitemap([SITE, SITE + 'terms/']));
    if (url === SITE) return new Response(html('Hello'));
    if (url === SITE + 'terms/') return new Response(html('Old terms'));
    throw new Error('Unexpected URL');
  };
  assert.deepEqual(await planChanges(directory, fetcher), { revision, urls: [SITE + 'terms/'] });
  assert.equal(requests.length, 4);
});

test('later deployments compare against live publication state, not the preceding source commit', async t => {
  const directory = fixture(t);
  const live = JSON.parse(fs.readFileSync(path.join(directory, 'publication.json')));
  assert.deepEqual((await planChanges(directory, async () => jsonResponse(live))).urls, []);
  live.pages[SITE] = '2'.repeat(64);
  assert.deepEqual((await planChanges(directory, async () => jsonResponse(live))).urls, [SITE]);
  await assert.rejects(planChanges(directory, async () => new Response('', { status: 503 })), /503/);
});

test('IndexNow submission requires the exact deployed revision and project ownership file', async () => {
  const plan = { revision, urls: [SITE, SITE + 'terms/'] };
  const posted = [];
  const fetcher = async (url, options) => {
    if (url.includes('publication.json')) return jsonResponse(state({ [SITE]: digest }));
    if (url.endsWith('.txt')) return new Response(INDEXNOW_KEY);
    assert.equal(url, 'https://api.indexnow.org/indexnow');
    assert.equal(options.method, 'POST');
    posted.push(JSON.parse(options.body));
    return new Response('', { status: 202 });
  };
  assert.match(await submitPlan(plan, fetcher), /accepted 2/);
  assert.deepEqual(posted[0].urlList, plan.urls);
  assert.equal(posted[0].keyLocation, SITE + INDEXNOW_KEY + '.txt');
  const wrongRevision = async () => jsonResponse({ ...state({ [SITE]: digest }), revision: 'b'.repeat(40) });
  await assert.rejects(submitPlan(plan, wrongRevision), /revision differs/);
  await assert.rejects(submitPlan({ ...plan, urls: ['https://example.com/'] }, fetcher), /outside/);
  assert.equal(posted.length, 1);
});

test('deployment waits for the expected revision and fails if it never becomes live', async () => {
  let requests = 0, pauses = 0;
  const pause = async () => { pauses++; };
  const fetcher = async () => {
    requests++;
    if (requests === 1) return new Response('', { status: 404 });
    return jsonResponse({ ...state({ [SITE]: digest }), revision: requests === 2 ? 'b'.repeat(40) : revision });
  };
  await waitForRevision(revision, fetcher, pause);
  assert.equal(requests, 3);
  assert.equal(pauses, 2);
  requests = 0;
  await assert.rejects(waitForRevision(revision, async () => { requests++; return new Response('', { status: 503 }); }, pause), /503/);
  assert.equal(requests, 12);
});
