import pkg from 'file:///C:/Users/sietg/AppData/Roaming/npm/node_modules/openspec-playwright/node_modules/playwright-core/index.js';
import { writeFileSync } from 'node:fs';
const { chromium } = pkg;

const url = 'http://127.0.0.1:5308/?build=mass-nav-real-gpu-' + Date.now();
const logs = [];
const browser = await chromium.launch({
  executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  headless: false,
  args: ['--enable-unsafe-webgpu','--enable-features=Vulkan,WebGPU','--ignore-gpu-blocklist','--enable-gpu','--window-size=1320,900'],
});
const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
const page = await ctx.newPage();
page.on('console', m => logs.push(`[${m.type()}] ${m.text()}`));
page.on('pageerror', e => logs.push(`[pageerror] ${e.message}`));
const cdp = await ctx.newCDPSession(page);

async function shot(name) {
  try {
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false });
    writeFileSync(name, Buffer.from(data, 'base64'));
    logs.push('[shot] ' + name + ' ok');
  } catch (e) { logs.push('[shot-fail] ' + name + ' ' + String(e)); }
}

try {
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.waitForTimeout(55000);   // boot + settle
  await shot('.real-gpu-boot.png');
  await page.waitForTimeout(20000);   // sustained
  await shot('.real-gpu-sustained.png');
} catch (e) {
  logs.push('[fatal] ' + String(e));
} finally {
  console.log(logs.filter(l => !l.includes('MONO_WASM:') && !l.includes('Merged fragment')).join('\n'));
  await browser.close();
}
