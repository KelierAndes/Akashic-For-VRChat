import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';

const repo = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const args = process.argv.slice(2);

function flag(name, fallback) {
  const index = args.indexOf(`--${name}`);
  if (index < 0) return fallback;
  return args[index + 1];
}

const hostExe = flag('host', path.join(repo, 'dist', 'Akashic', 'host', 'Akashic.Host.exe'));
const samples = Number(flag('samples', 20));
const frames = Number(flag('frames', 5));
const width = Number(flag('width', 800));
const height = Number(flag('height', 800));

if (!fs.existsSync(hostExe)) {
  console.error(`sidecar not found: ${hostExe}`);
  process.exit(1);
}

const child = spawn(hostExe, ['--port', '0'], { stdio: ['ignore', 'pipe', 'pipe'] });
child.stdout.setEncoding('utf8');
child.stderr.on('data', () => {});

let handshake = null;
let pending = '';
child.stdout.on('data', chunk => {
  pending += chunk;
  const lines = pending.split('\n');
  pending = lines.pop();
  for (const line of lines) {
    if (!line.startsWith('{')) continue;
    try {
      const parsed = JSON.parse(line);
      if (parsed.kind === 'akashic-host') handshake = parsed;
    } catch {}
  }
});

const deadline = Date.now() + 90000;
while (!handshake && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 200));
if (!handshake) {
  console.error('sidecar printed no handshake line');
  child.kill();
  process.exit(1);
}

const base = handshake.url.replace(/\/$/, '');
const token = handshake.token;

async function api(route, init = {}) {
  const response = await fetch(base + route, {
    ...init,
    headers: { 'X-Akashic-Token': token, ...(init.headers || {}) }
  });
  if (!response.ok) {
    const detail = (await response.text()).slice(0, 160);
    const error = new Error(detail);
    error.status = response.status;
    throw error;
  }
  return response;
}

function header(response, name) {
  return Number(response.headers.get(name) || 0);
}

function percentile(values, p) {
  if (!values.length) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return Number(sorted[index].toFixed(1));
}

function block(values) {
  return { p50: percentile(values, 50), p90: percentile(values, 90), max: percentile(values, 100) };
}

const rows = await (await api('/api/resources')).json();
const flat = [];
for (const row of rows) {
  flat.push(row);
  for (const node of row.children || []) if (node) flat.push(node);
}

const models = flat.filter(row => row.kind === 'unitypackage').sort((a, b) => a.key.localeCompare(b.key));
const stride = models.length > samples ? Math.ceil(models.length / samples) : 1;
const picked = [];
for (let i = 0; i < models.length && picked.length < samples; i += stride) picked.push(models[i]);

console.log(`sidecar ${base}`);
console.log(`library unitypackages=${models.length} sampled=${picked.length} frames=${frames} viewport=${width}x${height}`);
console.log('frames alternate raw / jpeg; frame 0 is the cold frame right after build');

const build = [];
const firstWall = [];
const firstServer = [];
const rawWall = [];
const rawServer = [];
const jpegWall = [];
const jpegServer = [];
const steadyWall = [];
const queue = [];
let noMesh = 0;
let failed = 0;
let rendered = 0;

for (const row of picked) {
  let session = null;
  let buildMs = 0;

  const list = await (await api('/api/preview/list?resourceKey=' + encodeURIComponent(row.key))).json();
  const prefabs = (list || []).filter(asset => asset.kind === 'prefab').slice(0, 6);

  for (const prefab of prefabs) {
    const started = performance.now();
    try {
      const response = await api('/api/preview/build', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
        body: JSON.stringify({ resourceKey: row.key, prefabPath: prefab.path, entry: row.archiveEntry })
      });
      const json = await response.json();
      if (json && json.sessionId) {
        buildMs = performance.now() - started;
        session = json.sessionId;
        break;
      }
    } catch (error) {
      if (error.status !== 422) failed++;
    }
  }

  if (!session) { noMesh++; continue; }

  build.push(buildMs);
  rendered++;

  const frameWalls = [];
  for (let frame = 0; frame < frames; frame++) {
    const raw = frame % 2 === 0;
    const started = performance.now();
    let response;
    try {
      response = await api('/api/preview/render', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
        body: JSON.stringify({
          sessionId: session,
          width, height,
          view: 'orbit',
          mode: 'textured',
          outline: true,
          gizmo: false,
          lightBackground: false,
          yaw: 0.35 + frame * 0.45,
          pitch: 0.25 + frame * 0.06,
          fov: 38,
          raw
        })
      });
      await response.arrayBuffer();
    } catch (error) {
      if (error.status === 422) noMesh++; else failed++;
      break;
    }

    const wall = performance.now() - started;
    const server = header(response, 'x-akashic-render-ms');
    queue.push(header(response, 'x-akashic-queue-ms'));
    frameWalls.push(wall);

    if (frame === 0) {
      firstWall.push(wall);
      firstServer.push(server);
    } else {
      steadyWall.push(wall);
    }

    if (raw) {
      rawWall.push(wall);
      rawServer.push(server);
    } else {
      jpegWall.push(wall);
      jpegServer.push(server);
    }
  }

  console.log(`  ${row.name.slice(0, 34).padEnd(34)} build=${Math.round(buildMs)}` +
    ` frames=${frameWalls.map(value => Math.round(value)).join(',')}`);
}

console.log(JSON.stringify({
  sampled: picked.length,
  rendered,
  skippedNoPrefabOrMesh: noMesh,
  failures: failed,
  viewport: `${width}x${height}`,
  buildMs: block(build),
  firstFrameWallMs: block(firstWall),
  firstFrameServerMs: block(firstServer),
  steadyFrameWallMs: block(steadyWall),
  rawFrameWallMs: block(rawWall),
  rawFrameServerMs: block(rawServer),
  jpegFrameWallMs: block(jpegWall),
  jpegFrameServerMs: block(jpegServer),
  queueMs: block(queue)
}, null, 2));

child.kill();
setTimeout(() => process.exit(0), 300);
