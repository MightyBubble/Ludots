#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Ludots GitHub Pages 门户站点组装脚本（纯标准库，无第三方依赖）。

输入（全部在仓库内）：
  docs/                     站点手写源（index/gallery/tests/diagrams.html、site-assets/、diagrams/、prd/tdd 等）
  gitbook/                  正式文档写作源（markdown，客户端 marked.js 渲染）
  artifacts/acceptance/     showcase 验收证据（六件套）
  showcase.registry.json    showcase 注册表（主控合并生成，缺失时画廊为空并告警）

输出：
  _site/                    完整静态站点（含 .nojekyll），供 Pages 流水线原样发布
  _site/site-assets/docs-nav.js       由 gitbook/SUMMARY.md 解析生成的文档目录树
  _site/site-assets/graph-op-nav.js   由 graph-node-op-wiki/README.md 解析的节点画廊目录
  _site/site-assets/engine-gallery-nav.js  由 engine-gallery-wiki/README.md 解析的引擎画廊目录
  _site/site-assets/gallery-data.js   由 showcase.registry.json 注入的画廊数据
  _site/site-assets/evidence-data.js  由 artifacts/acceptance/ 实扫生成的证据索引

本地验证：
  python scripts/build-site.py
  python -m http.server -d _site 8000   # 然后访问 http://localhost:8000/
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys

# CI Windows 运行器默认控制台编码（如 cp1252）无法输出中文，强制 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------------------
# 路径约定
# ---------------------------------------------------------------------------

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent

DOCS_DIR = REPO_ROOT / "docs"
GITBOOK_DIR = REPO_ROOT / "gitbook"
SUMMARY_MD = GITBOOK_DIR / "SUMMARY.md"
PRD_README = GITBOOK_DIR / "reference" / "mod-editor-prd" / "README.md"
PRD_TODO_DIR = GITBOOK_DIR / "reference" / "mod-editor-prd" / "todo"
ACCEPTANCE_DIR = REPO_ROOT / "artifacts" / "acceptance"
GRAPH_OP_EVIDENCE_GLOB = "capability_standard_graph_op_*"
ENGINE_EVIDENCE_GLOB = "engine_raylib_*"
EVIDENCE_DIR = REPO_ROOT / "artifacts" / "evidence"
REGISTRY_JSON = REPO_ROOT / "showcase.registry.json"

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".webp"}
GRAPH_OP_MEDIA_NAMES = ("play.mp4", "poster.png")

WARNINGS: list[str] = []


def warn(msg: str) -> None:
    WARNINGS.append(msg)
    print(f"  [warn] {msg}")


# ---------------------------------------------------------------------------
# 1. gitbook/SUMMARY.md → docs-nav.js
# ---------------------------------------------------------------------------

SUMMARY_ITEM = re.compile(r"^(\s*)[-*]\s+\[([^\]]+)\]\(([^)]+)\)\s*$")


def parse_summary(summary_path: Path) -> tuple[list[dict], int]:
    """把 GitBook SUMMARY.md 解析成嵌套目录树。返回 (tree, md_count)。"""
    tree: list[dict] = []
    stack: list[tuple[int, list]] = []  # (indent, children_of_node_at_that_indent)
    md_count = 0

    for raw in summary_path.read_text(encoding="utf-8").splitlines():
        m = SUMMARY_ITEM.match(raw)
        if not m:
            continue
        indent = len(m.group(1))
        title = m.group(2).strip()
        href = m.group(3).strip()

        if re.match(r"^https?://", href, re.I):
            node = {"title": title, "path": href, "type": "external", "children": []}
        elif href.lower().endswith((".html", ".htm")):
            node = {"title": title, "path": href, "type": "html", "children": []}
        else:
            node = {"title": title, "path": href, "type": "md", "children": []}
            md_count += 1

        while stack and stack[-1][0] >= indent:
            stack.pop()
        if stack:
            stack[-1][1].append(node)
        else:
            tree.append(node)
        stack.append((indent, node["children"]))

    return tree, md_count


# ---------------------------------------------------------------------------
# 1b. mod-editor-prd/README.md 分篇目录 -> prd-nav.js
# ---------------------------------------------------------------------------

PRD_VOLUME_HEADING = re.compile(r"^### (卷 \d+ · .+?)\s*$")
PRD_TABLE_ROW = re.compile(r"^\|\s*`(?P<file>[^`|]+\.md)`\s*\|(?P<rest>.+)\|$")


def parse_prd_catalog(readme_path: Path) -> dict:
    """解析 PRD 总篇 README 的分篇目录区块为卷/篇树（站点侧栏数据源）。"""
    nav: dict = {"volumes": [], "total": 0, "written": 0}
    if not readme_path.exists():
        warn("mod-editor-prd/README.md 不存在 -> PRD 页目录为空")
        return nav

    lines = readme_path.read_text(encoding="utf-8").splitlines()
    in_section = False
    current_volume = None

    for raw in lines:
        line = raw.rstrip()
        if line.startswith("## "):
            in_section = "分篇目录" in line
            continue
        if not in_section:
            continue

        vol = PRD_VOLUME_HEADING.match(line)
        if vol:
            current_volume = {"title": vol.group(1), "children": []}
            nav["volumes"].append(current_volume)
            continue

        row = PRD_TABLE_ROW.match(line)
        if not row or current_volume is None:
            continue

        rest = row.group("rest").split("|")
        if len(rest) < 4:
            warn(f"PRD 目录行字段数异常，已跳过：{row.group('file')}")
            continue

        title = rest[0].strip()
        priority = rest[2].strip()
        status = rest[3].strip().strip("*").strip()
        written = "已写" in status
        child = {
            "file": row.group("file").strip(),
            "title": title,
            "priority": priority,
            "status": status or "未写",
            "written": written,
        }
        current_volume["children"].append(child)
        nav["total"] += 1
        if written:
            nav["written"] += 1

    if nav["total"] == 0:
        warn("PRD 分篇目录解析结果为 0 篇 -> 检查 README 目录表格式")

    handbook_dir = readme_path.parent
    layer_dirs = [handbook_dir / layer for layer in ("prd", "config", "uxd", "spec-runtime", "spec-editor", "reference")]
    missing = [
        f"{layer.name}/{child['file']}"
        for layer in layer_dirs
        for vol in nav["volumes"]
        for child in vol["children"]
        if not (layer / child["file"]).is_file()
    ]
    if missing:
        raise SystemExit(
            "PRD 分篇目录与磁盘文件不一致（站点导航会 404），先修 README 或补文件：%s"
            % "、".join(missing[:12])
        )
    return nav


# ---------------------------------------------------------------------------
# 1c. wiki README 家族目录 -> 画廊导航 JS（graph-node-op-wiki 与 engine-gallery-wiki 共用）
# ---------------------------------------------------------------------------

GRAPH_OP_WIKI_DIR = GITBOOK_DIR / "reference" / "graph-node-op-wiki"
ENGINE_GALLERY_WIKI_DIR = GITBOOK_DIR / "reference" / "engine-gallery-wiki"
WIKI_FAMILY_HEADING = re.compile(r"^## (.+?)\s*$")
WIKI_OP_ITEM = re.compile(r"^-\s+\[(?P<title>[^\]]+)\]\((?P<file>[^)]+?\.md)\)\s*(?:—|-)\s*(?P<desc>.*)$")


def parse_wiki_catalog(readme_path: Path, label: str) -> dict:
    """解析 wiki 总目录为家族树（站点画廊页数据源；条目缺失页面时硬失败防 404）。"""
    nav: dict = {"families": [], "total": 0}
    if not readme_path.exists():
        warn(f"{label}/README.md 不存在 -> 画廊目录为空")
        return nav

    current_family = None
    seen_files: set[str] = set()
    for raw in readme_path.read_text(encoding="utf-8").splitlines():
        fam = WIKI_FAMILY_HEADING.match(raw)
        if fam:
            current_family = {"title": fam.group(1), "ops": []}
            nav["families"].append(current_family)
            continue
        item = WIKI_OP_ITEM.match(raw)
        if not item or current_family is None:
            continue
        current_family["ops"].append({
            "file": item.group("file").strip(),
            "title": item.group("title").strip(),
            "desc": item.group("desc").strip(),
        })
        seen_files.add(item.group("file").strip())
        nav["total"] += 1

    if nav["total"] == 0:
        warn(f"{label}/README.md 未解析到任何条目 -> 检查家族列表格式")

    missing = [f for f in sorted(seen_files) if not (readme_path.parent / f).is_file()]
    if missing:
        raise SystemExit(
            f"{label} 目录链接了不存在的页面（站点会 404）：%s" % "、".join(missing[:12])
        )
    orphans = sorted(
        p.name for p in readme_path.parent.glob("*.md")
        if p.name != "README.md" and p.name not in seen_files
    )
    if orphans:
        warn(f"{label} 存在未被 README 收录的孤儿页面：%s" % "、".join(orphans[:12]))
    return nav


# ---------------------------------------------------------------------------
# 2. showcase.registry.json → gallery-data.js
# ---------------------------------------------------------------------------

REGISTRY_FIELDS = [
    "id", "path", "projectPath", "title", "summary", "tier", "category",
    "tags", "binding", "preset", "docsPath", "readmePath",
    "acceptanceTest", "artifactDir", "screenshot", "video", "status",
]


def load_registry(registry_path: Path) -> tuple[list[dict], int | None]:
    """读取注册表并按字段白名单规整。返回 (showcases, schemaVersion)。"""
    if not registry_path.exists():
        warn("showcase.registry.json 不存在（主控尚未合并生成）→ 画廊数据为空")
        return [], None
    try:
        raw = json.loads(registry_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        warn(f"showcase.registry.json JSON 解析失败：{exc} → 画廊数据为空")
        return [], None

    schema_version = raw.get("schemaVersion")
    showcases = raw.get("showcases")
    if not isinstance(showcases, list):
        warn("showcase.registry.json 缺少 showcases 数组 → 画廊数据为空")
        return [], schema_version

    cleaned: list[dict] = []
    seen_ids: set[str] = set()
    for i, item in enumerate(showcases):
        if not isinstance(item, dict):
            warn(f"showcases[{i}] 不是对象，已跳过")
            continue
        entry = {k: item.get(k) for k in REGISTRY_FIELDS}
        if not entry.get("id"):
            warn(f"showcases[{i}] 缺少 id，已跳过")
            continue
        if entry["id"] in seen_ids:
            warn(f"showcase id 重复：{entry['id']}，后者已跳过")
            continue
        seen_ids.add(entry["id"])
        if entry.get("tags") is None:
            entry["tags"] = []
        if entry.get("status") is None:
            entry["status"] = "active"
        cleaned.append(entry)
    return cleaned, schema_version


# ---------------------------------------------------------------------------
# 3. artifacts/acceptance/ → evidence-data.js
# ---------------------------------------------------------------------------

def scan_one_dir(child: Path, name: str) -> dict:
    """扫描单个证据目录（六件套 + 根部散图 + 根部额外 md 报告）。"""
    entry: dict = {"kind": "dir", "name": name}
    entry["hasBattleReport"] = (child / "battle-report.md").is_file()
    entry["hasTrace"] = (child / "trace.jsonl").is_file()
    entry["hasPathMmd"] = (child / "path.mmd").is_file()
    entry["hasChecklist"] = (child / "visible-checklist.md").is_file()

    summary_file = child / "summary.json"
    if summary_file.is_file():
        try:
            entry["summary"] = json.loads(summary_file.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            warn(f"{name}/summary.json 解析失败：{exc}")
            entry["summary"] = None
    else:
        entry["summary"] = None

    # screens/ 子目录 + 目录根部散图都算关键帧
    screens: list[str] = []
    screens_dir = child / "screens"
    if screens_dir.is_dir():
        screens += [
            f"screens/{p.name}"
            for p in sorted(screens_dir.iterdir(), key=lambda x: x.name)
            if p.is_file() and p.suffix.lower() in IMAGE_EXTS
        ]
    screens += [
        p.name
        for p in sorted(child.iterdir(), key=lambda x: x.name)
        if p.is_file() and p.suffix.lower() in IMAGE_EXTS
    ]
    entry["screens"] = screens

    # 非标准命名的根部 md（如 stage_handoff_*.md）作为附加报告展示
    entry["extraReports"] = [
        p.name
        for p in sorted(child.iterdir(), key=lambda x: x.name)
        if p.is_file()
        and p.suffix.lower() == ".md"
        and p.name not in ("battle-report.md", "visible-checklist.md")
    ]
    return entry


def scan_acceptance(acc_dir: Path) -> list[dict]:
    """扫描验收证据目录。目录 → 六件套索引（支持一层嵌套）；散装 .md → 独立报告条目。"""
    entries: list[dict] = []
    if not acc_dir.exists():
        warn("artifacts/acceptance/ 不存在 → 证据数据为空")
        return entries

    for child in sorted(acc_dir.iterdir(), key=lambda p: p.name):
        if child.is_dir():
            sub_dirs = [p for p in sorted(child.iterdir(), key=lambda x: x.name) if p.is_dir()
                        and p.name != "screens"]
            has_own_evidence = any(
                (child / f).exists()
                for f in ("battle-report.md", "summary.json", "trace.jsonl")
            ) or any(p.suffix.lower() in IMAGE_EXTS for p in child.iterdir() if p.is_file())

            if has_own_evidence or not sub_dirs:
                entries.append(scan_one_dir(child, child.name))
            else:
                # 纯容器目录：下沉一层扫描（如 entity-query-tactics-showcase/raylib）
                for sub in sub_dirs:
                    entries.append(scan_one_dir(sub, f"{child.name}/{sub.name}"))
        elif child.suffix.lower() == ".md":
            entries.append({"kind": "report", "name": child.stem, "file": child.name})

    return entries


# ---------------------------------------------------------------------------
# 4. 组装 _site/
# ---------------------------------------------------------------------------

def write_js(path: Path, var_name: str, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = (
        "// 本文件由 scripts/build-site.py 生成，请勿手改；改动请改数据源后重新构建。\n"
        f"window.{var_name} = "
        + json.dumps(payload, ensure_ascii=False, indent=2)
        + ";\n"
    )
    path.write_text(text, encoding="utf-8")


def copy_tree(src: Path, dst: Path, label: str) -> int:
    """整树复制，返回文件数。"""
    if not src.exists():
        warn(f"{label} 源目录不存在：{src.relative_to(REPO_ROOT)}")
        return 0
    count = 0
    for item in src.rglob("*"):
        if item.is_file():
            rel = item.relative_to(src)
            target = dst / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(item, target)
            count += 1
    return count


def copy_graph_op_media(src: Path, dst: Path) -> int:
    """只拷贝画廊录像的 play.mp4 / poster.png，供 Pages 与 wiki 嵌入。"""
    if not src.is_dir():
        warn("artifacts/evidence/ 不存在 → 画廊录像媒体为空")
        return 0

    count = 0
    globs = (GRAPH_OP_EVIDENCE_GLOB, ENGINE_EVIDENCE_GLOB)
    children = sorted(p for g in globs for p in src.glob(g) if p.is_dir())
    for child in children:
        for name in GRAPH_OP_MEDIA_NAMES:
            file = child / name
            if not file.is_file():
                warn(f"画廊录像媒体缺失：{file.relative_to(REPO_ROOT)}")
                continue
            target = dst / child.name / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file, target)
            count += 1
    return count


def build(out_dir: Path) -> int:
    now = datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")

    print("== Ludots 门户站点组装 ==")
    print(f"repo : {REPO_ROOT}")
    print(f"out  : {out_dir}")

    # 硬校验：站点手写源必须存在
    for must in (DOCS_DIR / "index.html", DOCS_DIR / "site-assets" / "site.css"):
        if not must.exists():
            print(f"[error] 缺少必需站点文件：{must.relative_to(REPO_ROOT)}", file=sys.stderr)
            return 1

    # 清空并重建输出目录
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)

    # --- 数据生成（先于拷贝校验数据源） ---
    print("-- 解析 gitbook/SUMMARY.md → docs-nav.js")
    if SUMMARY_MD.exists():
        nav_tree, md_count = parse_summary(SUMMARY_MD)
    else:
        warn("gitbook/SUMMARY.md 不存在 → 文档目录树为空")
        nav_tree, md_count = [], 0
    docs_nav = {
        "generatedAt": now,
        "source": "gitbook/SUMMARY.md",
        "count": md_count,
        "tree": nav_tree,
    }

    todo_docs = []
    if PRD_TODO_DIR.is_dir():
        tf = [f for f in PRD_TODO_DIR.glob("*.md") if f.name != "README.md"]
        readme = PRD_TODO_DIR / "README.md"
        if readme.exists(): tf.append(readme)
        for f in sorted(tf):
            first = ""
            for line in f.read_text(encoding="utf-8").splitlines():
                if line.startswith("# "):
                    first = line[2:].strip()
                    break
            todo_docs.append({"file": "todo/" + f.name, "title": first or f.stem})

    print("-- 解析 mod-editor-prd/README.md 分篇目录 -> prd-nav.js")
    prd_catalog = parse_prd_catalog(PRD_README)
    prd_nav = {
        "generatedAt": now,
        "source": "gitbook/reference/mod-editor-prd/README.md",
        **prd_catalog,
        "todo": todo_docs,
    }

    print("-- 解析 graph-node-op-wiki/README.md 家族目录 -> graph-op-nav.js")
    graph_op_nav = {
        "generatedAt": now,
        "source": "gitbook/reference/graph-node-op-wiki/README.md",
        **parse_wiki_catalog(GRAPH_OP_WIKI_DIR / "README.md", "graph-node-op-wiki"),
    }

    print("-- 解析 engine-gallery-wiki/README.md 家族目录 -> engine-gallery-nav.js")
    engine_gallery_nav = {
        "generatedAt": now,
        "source": "gitbook/reference/engine-gallery-wiki/README.md",
        **parse_wiki_catalog(ENGINE_GALLERY_WIKI_DIR / "README.md", "engine-gallery-wiki"),
    }

    print("-- 读取 showcase.registry.json → gallery-data.js")
    showcases, schema_version = load_registry(REGISTRY_JSON)
    gallery_data = {
        "generatedAt": now,
        "source": "showcase.registry.json",
        "schemaVersion": schema_version,
        "count": len(showcases),
        "showcases": showcases,
    }

    print("-- 扫描 artifacts/acceptance/ → evidence-data.js")
    evidence = scan_acceptance(ACCEPTANCE_DIR)
    evidence_data = {
        "generatedAt": now,
        "source": "artifacts/acceptance/",
        "count": len(evidence),
        "entries": evidence,
    }

    # --- 拷贝静态内容 ---
    print("-- 拷贝 docs/ → _site/")
    n_docs = copy_tree(DOCS_DIR, out_dir, "docs/")

    print("-- 拷贝 gitbook/ → _site/gitbook/")
    n_gitbook = copy_tree(GITBOOK_DIR, out_dir / "gitbook", "gitbook/")

    print("-- 拷贝 artifacts/acceptance/ → _site/artifacts/acceptance/")
    n_acc = copy_tree(ACCEPTANCE_DIR, out_dir / "artifacts" / "acceptance", "artifacts/acceptance/")

    print("-- 拷贝画廊录像 play.mp4/poster.png（graph 节点 + 引擎场景）→ _site/artifacts/evidence/")
    n_graph_media = copy_graph_op_media(EVIDENCE_DIR, out_dir / "artifacts" / "evidence")

    if REGISTRY_JSON.exists():
        shutil.copy2(REGISTRY_JSON, out_dir / "showcase.registry.json")
        print("-- 拷贝 showcase.registry.json → _site/")

    # --- 生成数据 JS（覆盖/补充 site-assets） ---
    write_js(out_dir / "site-assets" / "docs-nav.js", "DOCS_NAV", docs_nav)
    write_js(out_dir / "site-assets" / "prd-nav.js", "PRD_NAV", prd_nav)
    write_js(out_dir / "site-assets" / "graph-op-nav.js", "GRAPH_OP_NAV", graph_op_nav)
    write_js(out_dir / "site-assets" / "engine-gallery-nav.js", "ENGINE_GALLERY_NAV", engine_gallery_nav)
    write_js(out_dir / "site-assets" / "gallery-data.js", "GALLERY_DATA", gallery_data)
    write_js(out_dir / "site-assets" / "evidence-data.js", "EVIDENCE_DATA", evidence_data)

    # --- GitHub Pages 标记 ---
    (out_dir / ".nojekyll").write_text("", encoding="utf-8")

    # --- 结构自验 ---
    print("-- 结构自验")
    required = [
        "index.html", "gallery.html", "tests.html", "diagrams.html",
        "graph-op-wiki.html", "raylib-engine.html", "agent-bridge.html",
        "site-assets/site.css", "site-assets/site.js",
        "site-assets/docs-nav.js", "site-assets/prd-nav.js", "site-assets/graph-op-nav.js",
        "site-assets/engine-gallery-nav.js",
        "site-assets/gallery-data.js", "site-assets/evidence-data.js",
        ".nojekyll",
    ]
    missing = [r for r in required if not (out_dir / r).exists()]
    if missing:
        print(f"[error] _site/ 结构不完整，缺少：{missing}", file=sys.stderr)
        return 1

    svg_count = len(list((out_dir / "diagrams").glob("*.svg"))) if (out_dir / "diagrams").is_dir() else 0

    # 硬编码盘符路径检查（如 D:\ 或 C:/ 泄漏进 HTML/JS；负向后行排除 https:// 等协议）
    bad_pattern = re.compile(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/]")
    leaks = []
    for html in list(out_dir.glob("*.html")) + list((out_dir / "site-assets").glob("*.js")):
        text = html.read_text(encoding="utf-8", errors="ignore")
        for m in bad_pattern.finditer(text):
            # 允许出现在代码示例中的转义写法（如 .\\scripts\\run-mod-launcher.cmd 不含盘符，这里只查盘符开头）
            leaks.append(f"{html.name}: …{text[max(0, m.start()-30):m.end()+20]}…")
    if leaks:
        warn("检测到疑似硬编码盘符路径：\n    " + "\n    ".join(leaks[:10]))

    print()
    print("== 组装完成 ==")
    print(f"  docs/ 文件          : {n_docs}")
    print(f"  gitbook/ 文件       : {n_gitbook}")
    print(f"  acceptance/ 文件    : {n_acc}")
    print(f"  画廊录像媒体文件    : {n_graph_media}")
    print(f"  文档目录树 md 条目  : {md_count}")
    print('  PRD 手册篇目        : {}/{} 已写（{} 卷）'.format(prd_nav['written'], prd_nav['total'], len(prd_nav['volumes'])))
    print('  Graph 节点 Wiki op  : {}（{} 家族）'.format(graph_op_nav['total'], len(graph_op_nav['families'])))
    print('  引擎画廊 Wiki 场景  : {}（{} 家族）'.format(engine_gallery_nav['total'], len(engine_gallery_nav['families'])))
    print(f"  注册 showcase       : {len(showcases)}")
    print(f"  验收证据条目        : {len(evidence)}（目录 {sum(1 for e in evidence if e['kind'] == 'dir')} + 散装报告 {sum(1 for e in evidence if e['kind'] == 'report')}）")
    print(f"  diagrams SVG        : {svg_count}")
    print(f"  告警                : {len(WARNINGS)}")
    print()
    print("本地预览：python -m http.server -d _site 8000  →  http://localhost:8000/")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Ludots 门户站点组装脚本")
    parser.add_argument(
        "--out",
        default=str(REPO_ROOT / "_site"),
        help="输出目录（默认 <repo>/_site）",
    )
    args = parser.parse_args()
    return build(Path(args.out).resolve())


if __name__ == "__main__":
    sys.exit(main())
