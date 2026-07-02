#!/usr/bin/env node
"use strict";

/**
 * Ludots structured-docs governance validator.
 *
 * Checks (errors fail CI; warnings do not unless --strict-freshness):
 *  -每篇文档对应 type 的 JSON Schema 通过（strict + unevaluatedProperties）。
 *  - domain 存在于 registry/domains.json；registry codePaths 存在。
 *  - 所有代码路径字段（relatedCode / sourceCode / enforcedBy / loader / code /
 *    evidence / testCode / declaredIn / codeRef.path / codeScope.path）真实存在。
 *  - constantRef.symbol / valueRef / 术语 constantRef 命中 generated/constants.json。
 *  - termRef.id 命中某 glossary 文档术语。
 *  - id 全仓唯一；relatedDocs / supersedes / targetDocs / residueDebt 目标存在。
 *  - SSOT 唯一性：同一 (domain, ssotTopic) 仅一处 ssot:true。
 *  - 单向引用：tier=formal 文档不得 relatedDocs 指向 tier=deep 文档。
 *  - 新鲜度：reviewBy / dueBy / expiry 过期 → warning（--strict-freshness 时升级为 error）。
 */

const fs = require("fs");
const path = require("path");
const Ajv = require("ajv/dist/2020");
const addFormats = require("ajv-formats");

const DOCS_ROOT = path.resolve(__dirname, "..", "..");
const REPO_ROOT = path.resolve(DOCS_ROOT, "..");
const SCHEMAS_DIR = path.join(DOCS_ROOT, "schemas");
const CONTENT_DIR = path.join(DOCS_ROOT, "content");
const REGISTRY_FILE = path.join(DOCS_ROOT, "registry", "domains.json");
const CONSTANTS_FILE = path.join(DOCS_ROOT, "generated", "constants.json");

const STRICT_FRESHNESS = process.argv.includes("--strict-freshness");
const TODAY = new Date().toISOString().slice(0, 10);

const findings = [];
const add = (level, rule, source, detail) => findings.push({ level, rule, source, detail });

function readJson(fp) {
  return JSON.parse(fs.readFileSync(fp, "utf8"));
}
function walk(dir, filter, acc = []) {
  if (!fs.existsSync(dir)) return acc;
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    const st = fs.statSync(fp);
    if (st.isDirectory()) walk(fp, filter, acc);
    else if (filter(fp)) acc.push(fp);
  }
  return acc;
}
const repoPathExists = (p) => {
  if (typeof p !== "string" || p.length === 0) return false;
  const clean = p.split("#")[0].split("?")[0];
  return fs.existsSync(path.join(REPO_ROOT, clean));
};

// ---- load schemas ----
const ajv = new Ajv({ strict: true, strictRequired: false, allErrors: true });
addFormats(ajv);
const typeToSchemaId = {};
for (const fp of walk(SCHEMAS_DIR, (f) => f.endsWith(".json"))) {
  const s = readJson(fp);
  if (s.$id) ajv.addSchema(s);
  const tconst = s.properties && s.properties.type && s.properties.type.const;
  if (tconst) typeToSchemaId[tconst] = s.$id;
}

// ---- load registry + constants + (later) glossary terms ----
const registry = readJson(REGISTRY_FILE);
const domainIds = new Set(registry.domains.map((d) => d.id));
for (const d of registry.domains) {
  for (const cp of d.codePaths || []) {
    if (!repoPathExists(cp)) add("error", "registry-codepath-missing", `domains.json:${d.id}`, cp);
  }
}

let constants = { constants: {} };
if (fs.existsSync(CONSTANTS_FILE)) constants = readJson(CONSTANTS_FILE);
else add("warn", "constants-manifest-missing", "generated/constants.json", "缺少常量清单（运行导出器生成）。");
const constantKeys = new Set(Object.keys(constants.constants || {}));

// ---- load all content docs ----
const docFiles = walk(CONTENT_DIR, (f) => f.endsWith(".json"));
const docs = [];
for (const fp of docFiles) {
  const rel = path.relative(REPO_ROOT, fp).replace(/\\/g, "/");
  let data;
  try {
    data = readJson(fp);
  } catch (e) {
    add("error", "invalid-json", rel, e.message);
    continue;
  }
  docs.push({ rel, fp, data });
}

// id maps
const idToDoc = new Map();
for (const d of docs) {
  if (!d.data.id) {
    add("error", "missing-id", d.rel, "文档缺少 id");
    continue;
  }
  if (idToDoc.has(d.data.id)) add("error", "duplicate-id", d.rel, `id 已存在: ${d.data.id}`);
  else idToDoc.set(d.data.id, d);
}

// glossary terms
const termIds = new Set();
for (const d of docs) {
  if (d.data.type === "glossary" && Array.isArray(d.data.terms)) {
    for (const t of d.data.terms) if (t && t.id) termIds.add(t.id);
  }
}

// ---- per-doc validation ----
const CODE_PATH_KEYS = new Set([
  "sourceCode", "enforcedBy", "loader", "code", "evidence", "testCode", "declaredIn", "path"
]);

function deepCheck(node, rel) {
  if (Array.isArray(node)) {
    for (const x of node) deepCheck(x, rel);
    return;
  }
  if (!node || typeof node !== "object") return;

  if (node.kind === "codeRef" && typeof node.path === "string") {
    if (!repoPathExists(node.path)) add("error", "coderef-missing", rel, `codeRef.path 不存在: ${node.path}`);
  }
  if (node.kind === "constantRef" && typeof node.symbol === "string") {
    if (!constantKeys.has(node.symbol)) add("error", "constant-missing", rel, `constantRef.symbol 不在清单: ${node.symbol}`);
  }
  if (node.kind === "termRef" && typeof node.id === "string") {
    if (!termIds.has(node.id)) add("error", "term-missing", rel, `termRef.id 不在术语表: ${node.id}`);
  }

  for (const [k, v] of Object.entries(node)) {
    if (typeof v === "string") {
      if (CODE_PATH_KEYS.has(k) && !repoPathExists(v)) {
        add("error", "code-path-missing", rel, `${k} 路径不存在: ${v}`);
      }
      if ((k === "valueRef" || k === "constantRef") && !constantKeys.has(v)) {
        add("error", "constant-missing", rel, `${k} 不在清单: ${v}`);
      }
    } else if (v && typeof v === "object") {
      deepCheck(v, rel);
    }
  }
}

const ssotSeen = new Map(); // domain::topic -> rel

for (const d of docs) {
  const { rel, data } = d;
  const type = data.type;

  // schema
  const sid = typeToSchemaId[type];
  if (!sid) {
    add("error", "unknown-type", rel, `未知 type: ${type}`);
  } else {
    const validate = ajv.getSchema(sid);
    if (!validate(data)) {
      for (const e of validate.errors) {
        add("error", "schema", rel, `${e.instancePath || "/"} ${e.message}`);
      }
    }
  }

  // domain
  if (data.domain && !domainIds.has(data.domain)) {
    add("error", "unknown-domain", rel, `domain 不在注册表: ${data.domain}`);
  }

  // relatedCode
  for (const cp of data.relatedCode || []) {
    if (!repoPathExists(cp)) add("error", "relatedcode-missing", rel, `relatedCode 不存在: ${cp}`);
  }

  // deep code/ref/const/term checks across the whole doc
  deepCheck(data, rel);

  // doc-id refs
  for (const s of data.supersedes || []) {
    if (!idToDoc.has(s)) add("error", "supersedes-missing", rel, `supersedes 目标不存在: ${s}`);
  }
  for (const r of data.relatedDocs || []) {
    if (r && r.id && !idToDoc.has(r.id)) add("error", "relateddoc-missing", rel, `relatedDocs 目标不存在: ${r.id}`);
  }
  for (const t of data.targetDocs || []) {
    if (!idToDoc.has(t)) add("error", "targetdoc-missing", rel, `targetDocs 目标不存在: ${t}`);
  }
  for (const t of data.residueDebt || []) {
    if (!idToDoc.has(t)) add("error", "residuedebt-missing", rel, `residueDebt 目标不存在: ${t}`);
  }

  // single-direction: formal must not depend on deep
  if (data.tier === "formal") {
    for (const r of data.relatedDocs || []) {
      const tgt = r && r.id && idToDoc.get(r.id);
      if (tgt && tgt.data.tier === "deep") {
        add("error", "tier-direction", rel, `formal 文档引用了 deep 文档: ${r.id}`);
      }
    }
  }

  // ssot uniqueness
  if (data.ssot === true) {
    const key = `${data.domain}::${data.ssotTopic}`;
    if (ssotSeen.has(key)) add("error", "ssot-duplicate", rel, `(${key}) 已有 SSOT: ${ssotSeen.get(key)}`);
    else ssotSeen.set(key, rel);
  }

  // freshness
  const freshLevel = STRICT_FRESHNESS ? "error" : "warn";
  if (data.reviewBy && data.reviewBy < TODAY) add(freshLevel, "stale-review", rel, `reviewBy 已过期: ${data.reviewBy}`);
  if (data.dueBy && data.dueBy < TODAY) add(freshLevel, "debt-overdue", rel, `dueBy 已过期: ${data.dueBy}`);
  if (data.expiry && data.expiry < TODAY) add(freshLevel, "migration-overdue", rel, `expiry 已过期: ${data.expiry}`);
}

// ---- report ----
const errors = findings.filter((f) => f.level === "error");
const warns = findings.filter((f) => f.level === "warn");

function printGroup(list, label) {
  if (!list.length) return;
  console.log(`\n${label} (${list.length}):`);
  for (const f of list.sort((a, b) => (a.rule + a.source).localeCompare(b.rule + b.source))) {
    console.log(`  [${f.rule}] ${f.source}: ${f.detail}`);
  }
}

console.log(`Ludots docs validator — ${docs.length} docs, ${Object.keys(typeToSchemaId).length} types, ${constantKeys.size} constants, ${termIds.size} terms`);
printGroup(warns, "WARN");
printGroup(errors, "ERROR");

if (errors.length) {
  console.log(`\nFAILED: ${errors.length} error(s), ${warns.length} warning(s).`);
  process.exit(1);
}
console.log(`\nPASSED${warns.length ? ` (with ${warns.length} warning(s))` : ""}.`);
process.exit(0);
