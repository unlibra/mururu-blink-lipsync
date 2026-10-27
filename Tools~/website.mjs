// No dependencies. IndexNow's ownership key is intentionally public.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const SITE = 'https://docs.unlibra.com/mururu-blink-lipsync/';
export const INDEXNOW_KEY = 'caf45e8a927b4d2cb43a30b786fc8096';
const STATE = 'publication.json';
const assert = (ok, message) => { if (!ok) throw new Error(message); };
const hash = value => crypto.createHash('sha256').update(value).digest('hex');

// Compare reader-visible content, excluding theme scripts and styles.
export function pageContent(html) {
  const main = html.match(/<main[\s>][\s\S]*?<\/main>/i)?.[0] ?? '';
  const text = [], tags = [];
  for (const [token, skipped] of main.matchAll(/<(script|style)\b[\s\S]*?<\/\1\s*>|<[^>]*>|[^<]+/gi)) {
    if (!skipped) (token.startsWith('<') ? tags : text).push(token);
  }
  return JSON.stringify({
    title: html.match(/<title>([^<]*)<\/title>/i)?.[1] ?? '',
    description: html.match(/<meta name="description" content="([^"]*)"/i)?.[1] ?? '',
    text: text.join(' ').replace(/\s+/g, ' ').trim(),
    links: tags.flatMap(tag => tag.match(/\shref="([^"]*)"/)?.[1] ?? []),
    images: tags.filter(tag => /^<img\s/i.test(tag)).map(img => [
      img.match(/\ssrc="([^"]*)"/)?.[1] ?? '',
      img.match(/\salt="([^"]*)"/)?.[1] ?? '',
    ]),
  });
}

export function ownUrl(value) {
  const url = new URL(value);
  assert(url.href.startsWith(SITE) && url.origin === new URL(SITE).origin && !url.search && !url.hash && !url.username && !url.password, 'URL is outside this manual.');
  return url.href;
}

export function pageUrl(relative) {
  assert(!relative.includes('\\') && !relative.startsWith('/') && !relative.split('/').some(p => p === '..' || p === '.' || !p), 'Invalid page path.');
  return ownUrl(SITE + relative.replace(/(^|\/)index\.html$/, '$1').replace(/\.html$/, ''));
}

export function sitemapUrls(xml) {
  assert(/<urlset(?:\s|>)/.test(xml) && !/<!DOCTYPE|<!ENTITY|<sitemapindex/i.test(xml), 'Expected a page sitemap.');
  const urls = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m => ownUrl(m[1].replaceAll('&amp;', '&')));
  assert(urls.length > 0 && urls.length <= 1000 && new Set(urls).size === urls.length, 'Invalid sitemap URL list.');
  return urls.sort();
}

export function validateState(state) {
  assert(state?.version === 1 && /^[a-f0-9]{40}$/.test(state.revision) && state.pages && typeof state.pages === 'object' && !Array.isArray(state.pages), 'Invalid publication state.');
  const entries = Object.entries(state.pages);
  assert(entries.length > 0 && entries.length <= 1000, 'Invalid page count.');
  for (const [url, digest] of entries) {
    ownUrl(url);
    assert(/^[a-f0-9]{64}$/.test(digest), 'Invalid page digest.');
  }
  return state;
}

export function changedUrls(before, after) {
  validateState(before);
  validateState(after);
  return [...new Set([...Object.keys(before.pages), ...Object.keys(after.pages)])]
    .filter(url => before.pages[url] !== after.pages[url]).sort();
}

async function getText(url, allowMissing = false, fetcher = fetch) {
  const result = await fetcher(url, { signal: AbortSignal.timeout(30000), redirect: 'error', headers: { 'cache-control': 'no-cache' } });
  if (allowMissing && result.status === 404) return null;
  assert(result.status === 200, `HTTP ${result.status}: ${url}`);
  return result.text();
}

function readState(directory) {
  return validateState(JSON.parse(fs.readFileSync(path.join(directory, STATE), 'utf8')));
}

export function stamp(directory, revision) {
  const pages = {};
  function walk(current, relative = '') {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      assert(!entry.isSymbolicLink(), 'Site contains a symbolic link.');
      const name = relative + entry.name;
      if (entry.isDirectory()) walk(path.join(current, entry.name), name + '/');
      else if (entry.isFile() && name.endsWith('.html') && !/(^|\/)404\.html$/.test(name)) {
        pages[pageUrl(name)] = hash(pageContent(fs.readFileSync(path.join(current, entry.name), 'utf8')));
      }
    }
  }
  walk(directory);
  const state = validateState({ version: 1, revision, pages });
  const sitemap = sitemapUrls(fs.readFileSync(path.join(directory, 'sitemap.xml'), 'utf8'));
  assert(JSON.stringify(Object.keys(pages).sort()) === JSON.stringify(sitemap), 'Built pages and sitemap differ.');
  fs.writeFileSync(path.join(directory, STATE), JSON.stringify(state, null, 2) + '\n');
  return state;
}

// Read the last successful live publication before deploying. The initial
// migration reads live HTML because the shared site has no publication state.
export async function planChanges(directory, fetcher = fetch) {
  const next = readState(directory);
  const previousJson = await getText(SITE + STATE, true, fetcher);
  let previous;
  if (previousJson !== null) previous = validateState(JSON.parse(previousJson));
  else {
    const xml = await getText(SITE + 'sitemap.xml', true, fetcher);
    const pages = {};
    if (xml !== null) for (const url of sitemapUrls(xml)) {
      pages[url] = hash(pageContent(await getText(url, false, fetcher)));
    }
    if (!Object.keys(pages).length) return { revision: next.revision, urls: Object.keys(next.pages).sort() };
    previous = { version: 1, revision: '0'.repeat(40), pages };
  }
  return { revision: next.revision, urls: changedUrls(previous, next) };
}

export async function submitPlan(plan, fetcher = fetch) {
  assert(/^[a-f0-9]{40}$/.test(plan?.revision) && Array.isArray(plan.urls) && plan.urls.length <= 1000, 'Invalid notification plan.');
  for (const url of plan.urls) ownUrl(url);
  const state = validateState(JSON.parse(await getText(SITE + STATE + '?revision=' + plan.revision, false, fetcher)));
  assert(state.revision === plan.revision, 'Published revision differs; do not notify IndexNow.');
  if (!plan.urls.length) return 'No reader-visible page changed.';
  assert((await getText(SITE + INDEXNOW_KEY + '.txt', false, fetcher)).trim() === INDEXNOW_KEY, 'IndexNow ownership file differs.');
  const result = await fetcher('https://api.indexnow.org/indexnow', {
    method: 'POST', signal: AbortSignal.timeout(30000), redirect: 'error',
    headers: { 'content-type': 'application/json; charset=utf-8' },
    body: JSON.stringify({ host: new URL(SITE).host, key: INDEXNOW_KEY, keyLocation: SITE + INDEXNOW_KEY + '.txt', urlList: plan.urls }),
  });
  assert(result.status === 200 || result.status === 202, `IndexNow answered ${result.status}.`);
  return `IndexNow accepted ${plan.urls.length} URLs (${result.status}).`;
}

export async function waitForRevision(revision, fetcher = fetch, pause = () => new Promise(resolve => setTimeout(resolve, 5000))) {
  assert(/^[a-f0-9]{40}$/.test(revision), 'Invalid expected revision.');
  for (let attempt = 0; attempt < 12; attempt++) {
    try {
      const live = validateState(JSON.parse(await getText(SITE + STATE + '?revision=' + revision, false, fetcher)));
      if (live.revision === revision) return;
    } catch (error) {
      if (attempt === 11) throw error;
    }
    if (attempt < 11) await pause();
  }
  throw new Error('The expected revision is not live yet.');
}

async function productionCheck() {
  const sitemap = await getText(SITE + 'sitemap.xml');
  for (const url of sitemapUrls(sitemap)) {
    await getText(url);
    console.log(`OK ${url}`);
  }
  assert((await getText(SITE + INDEXNOW_KEY + '.txt')).trim() === INDEXNOW_KEY, 'IndexNow ownership file differs.');
  const state = validateState(JSON.parse(await getText(SITE + STATE)));
  assert(JSON.stringify(Object.keys(state.pages).sort()) === JSON.stringify(sitemapUrls(sitemap)), 'Published state and sitemap differ.');
}

async function main() {
  const [command, directory, output] = process.argv.slice(2);
  if (command === 'stamp' && directory) {
    const revision = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    console.log(`Stamped ${Object.keys(stamp(directory, revision).pages).length} pages.`);
  } else if (command === 'plan' && directory && output) {
    const plan = await planChanges(directory);
    fs.writeFileSync(output, JSON.stringify(plan, null, 2) + '\n');
    console.log(`Planned ${plan.urls.length} changed URLs; nothing submitted.`);
  } else if (command === 'notify' && directory) {
    console.log(await submitPlan(JSON.parse(fs.readFileSync(directory, 'utf8'))));
  } else if (command === 'wait-live' && directory) await waitForRevision(directory);
  else if (command === 'check') await productionCheck();
  else throw new Error('Usage: website.mjs stamp DIST | plan DIST OUTPUT | wait-live REVISION | notify PLAN | check');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
