// GitHub.com workflow dispatch: the credential is scoped to unlibra/vpm Actions.
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = 'unlibra/vpm';
const api = `https://api.github.com/repos/${repository}`;
const workflow = `${api}/actions/workflows/sync.yml`;
const listingUrl = 'https://docs.unlibra.com/vpm/index.json';
const packageId = 'com.unlibra.mururu-blink-lipsync';
const requireThat = (value, message) => { if (!value) throw Error(message); };

function headers(token) {
  requireThat(typeof token === 'string' && token.trim().length > 0, 'VPM_DISPATCH_TOKEN is required; generate a GitHub App token scoped to unlibra/vpm Actions write.');
  return { Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`, 'X-GitHub-Api-Version': '2026-03-10' };
}
async function request(fetcher, url, options) {
  const response = await fetcher(url, { ...options, redirect: 'error', signal: AbortSignal.timeout(30000) });
  requireThat(response.ok, `VPM automation request failed (${response.status}). Check the vpm workflow and token permissions.`);
  return response.json();
}
export async function checkVpm(token, fetcher = fetch) {
  const result = await request(fetcher, workflow, { headers: headers(token) });
  requireThat(result.state === 'active' && result.path === '.github/workflows/sync.yml', 'VPM sync workflow is not active. Publish and enable the vpm main workflow first.');
  // This anonymous check also verifies that initial Pages setup preceded release publication.
  const index = await request(fetcher, listingUrl, { headers: { Accept: 'application/json' } });
  requireThat(index.id === 'com.unlibra.vpm' && index.url === listingUrl && index.packages && typeof index.packages === 'object', 'VPM listing is not available at the configured URL.');
}
export async function notifyVpm(tag, token, { fetcher = fetch, sleep = ms => new Promise(resolve => setTimeout(resolve, ms)), now = Date.now, timeoutMs = 20 * 60 * 1000 } = {}) {
  requireThat(/^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(tag), 'Expected a stable vMAJOR.MINOR.PATCH tag.');
  const auth = headers(token);
  const started = now();
  const dispatch = await request(fetcher, `${workflow}/dispatches`, {
    method: 'POST', headers: { ...auth, 'Content-Type': 'application/json' },
    body: JSON.stringify({ ref: 'main', inputs: { package_id: packageId, release_tag: tag } })
  });
  requireThat(Number.isSafeInteger(dispatch.workflow_run_id) && dispatch.workflow_run_id > 0, 'VPM workflow dispatch did not return a run ID.');
  const runId = dispatch.workflow_run_id;
  const runUrl = `https://github.com/${repository}/actions/runs/${runId}`;
  while (now() - started < timeoutMs) {
    const run = await request(fetcher, `${api}/actions/runs/${runId}`, { headers: auth });
    requireThat(run.id === runId && run.head_branch === 'main' && run.event === 'workflow_dispatch' && run.path === '.github/workflows/sync.yml', 'Unexpected VPM workflow run.');
    if (run.status === 'completed') {
      requireThat(['success', 'cancelled'].includes(run.conclusion), `VPM listing update failed. Retry the notification job after checking ${runUrl}`);
      const index = await request(fetcher, listingUrl, { headers: { Accept: 'application/json', 'Cache-Control': 'no-cache' } });
      requireThat(index.id === 'com.unlibra.vpm' && index.url === listingUrl, 'Unexpected public VPM listing.');
      const version = index.packages?.[packageId]?.versions?.[tag.slice(1)];
      if (version?.name === packageId && version.version === tag.slice(1) && /^[0-9a-f]{64}$/.test(version.zipSHA256) &&
          version.url === `https://github.com/unlibra/mururu-blink-lipsync/releases/download/${tag}/${packageId}-${tag.slice(1)}.zip`) return runUrl;
      // A newer queued sync may supersede this run. It imports the whole backlog.
    }
    await sleep(10000);
  }
  throw Error(`Timed out waiting for VPM listing publication: ${runUrl}`);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [command, tag] = process.argv.slice(2);
    if (command === 'check' && tag === undefined) { await checkVpm(process.env.VPM_DISPATCH_TOKEN); console.log('VPM automation prerequisites verified.'); }
    else if (command === 'notify' && tag) console.log(`VPM listing published: ${await notifyVpm(tag, process.env.VPM_DISPATCH_TOKEN)}`);
    else throw Error('Usage: node Tools~/notify-vpm.mjs check | notify vVERSION');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
