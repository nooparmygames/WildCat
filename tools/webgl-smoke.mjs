// Run after building unity-sdk/Assets/WebGLSmoke/Smoke.unity for Web and starting the Development host.
// Install Playwright outside the source tree, then pass its module path as the second argument.
// Example: node tools/webgl-smoke.mjs .tmp/webgl-review/player .tmp/webgl-review/node_modules/playwright/index.mjs
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const root = path.resolve(process.argv[2] ?? '.tmp/webgl-review/player');
const playwrightPath = path.resolve(process.argv[3] ?? '.tmp/webgl-review/node_modules/playwright/index.mjs');
const { chromium } = await import(pathToFileURL(playwrightPath).href);
const messages = [];
const failures = [];
const mime = { '.html': 'text/html', '.js': 'application/javascript', '.wasm': 'application/wasm',
  '.data': 'application/octet-stream', '.json': 'application/json', '.css': 'text/css', '.png': 'image/png' };
const server = http.createServer((request, response) => {
  const url = new URL(request.url, 'http://localhost');
  const file = path.resolve(root, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
  if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return; }
  if (!fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404).end(); return; }
  const encoding = file.endsWith('.gz') ? 'gzip' : file.endsWith('.br') ? 'br' : null;
  const uncompressed = encoding ? file.slice(0, -3) : file;
  response.setHeader('Content-Type', mime[path.extname(uncompressed)] ?? 'application/octet-stream');
  if (encoding) response.setHeader('Content-Encoding', encoding);
  fs.createReadStream(file).pipe(response);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let browser;
let timeout;
try {
  browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--enable-unsafe-swiftshader'] });
  const page = await browser.newPage();
  let pass;
  let fail;
  const completed = new Promise((resolve, reject) => { pass = resolve; fail = reject; });
  page.on('console', message => {
    const text = message.text();
    messages.push({ type: message.type(), text });
    if (text.includes('WILDCAT_WEBGL_')) console.log(text);
    if (text.includes('WILDCAT_WEBGL_SMOKE_FAIL')) fail(new Error(text));
    if (text.includes('WILDCAT_WEBGL_SMOKE_PASS')) pass();
  });
  page.on('pageerror', error => { failures.push(error.message); fail(error); });
  timeout = setTimeout(() => fail(new Error('Browser smoke test did not finish within 120 seconds.')), 120000);
  // Attach the rejection handler before navigation, which can fail during loading.
  const outcome = completed.then(() => null, error => error);
  await page.goto(`http://127.0.0.1:${server.address().port}`, { waitUntil: 'domcontentloaded' });
  const error = await outcome;
  if (error) throw error;
  console.log('Browser smoke test passed.');
} catch (error) {
  failures.push(error.message);
  console.error(error.message);
  process.exitCode = 1;
} finally {
  clearTimeout(timeout);
  fs.writeFileSync(path.join(path.dirname(root), 'browser-results.json'), JSON.stringify({ messages, failures }, null, 2));
  await browser?.close();
  server.close();
}
