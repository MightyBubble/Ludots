#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer from "puppeteer-core";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const fixtureDir = path.join(repoRoot, "fixtures/ui-web-parity");
const htmlPath = path.join(fixtureDir, "parity_menu.html");
const outPath = path.join(fixtureDir, "chrome-layout.golden.json");
const chromePath = process.env.CHROME_PATH || "/usr/bin/google-chrome";

const viewports = [
  { name: "desktop", width: 1280, height: 720 },
  { name: "tablet", width: 900, height: 700 },
  { name: "phone", width: 390, height: 844 },
];

const browser = await puppeteer.launch({
  executablePath: chromePath,
  headless: "new",
  args: [
    "--no-sandbox",
    "--disable-gpu",
    `--user-data-dir=/tmp/ludots-ui-parity-chrome-${process.pid}`,
  ],
});

try {
  const page = await browser.newPage();
  const fileUrl = "file://" + htmlPath;
  const suites = [];

  for (const viewport of viewports) {
    await page.setViewport({
      width: viewport.width,
      height: viewport.height,
      deviceScaleFactor: 1,
    });
    await page.goto(fileUrl, { waitUntil: "networkidle0" });
    const boxes = await page.evaluate(() => {
      const stage = document.querySelector("#stage");
      if (!stage) {
        throw new Error("#stage missing");
      }
      const stageRect = stage.getBoundingClientRect();
      const nodes = [...document.querySelectorAll("[data-parity-id]")];
      const result = {};
      for (const node of nodes) {
        const id = node.getAttribute("data-parity-id");
        const rect = node.getBoundingClientRect();
        result[id] = {
          x: Number((rect.left - stageRect.left).toFixed(2)),
          y: Number((rect.top - stageRect.top).toFixed(2)),
          width: Number(rect.width.toFixed(2)),
          height: Number(rect.height.toFixed(2)),
        };
      }
      return result;
    });
    suites.push({
      name: viewport.name,
      width: viewport.width,
      height: viewport.height,
      boxes,
    });
  }

  const payload = {
    generatedAt: new Date().toISOString(),
    chrome: await browser.version(),
    fixture: "fixtures/ui-web-parity/parity_menu.html",
    coordinateSpace: "stage-local",
    viewports: suites,
  };
  fs.writeFileSync(outPath, JSON.stringify(payload, null, 2) + "\n", "utf8");
  console.log(`Wrote ${outPath}`);
} finally {
  await browser.close();
}
