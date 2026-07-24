#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""showcase.registry.json 注册表校验器。

治理目标：新人从单一门户入口进来时，注册表、launcher 配置与 git 树三者必须一致。
本脚本在 CI（完整 clone）与本地 blobless 稀疏克隆中均可运行（目录/文件存在性检查
基于 git 树对象，blobless 克隆包含全部 tree，无需拉取 blob）。

校验规则：
  ① showcase.registry.json / launcher.config.json 必须是合法 JSON；每条 showcase
    必须含 id/tier/category/title 必填字段，tier ∈ {T1,T2,T3,T4}，
    category ∈ 约定枚举，id 全表唯一。
  ② 每条目的 path（mod 目录）必须存在于 git 树中（git ls-tree HEAD -- <path>，
    只读 tree 对象，不触发 blob 拉取）。
  ③ T1 条目必须具有非空 acceptanceTest（契约核心，缺失为错误）；
    binding / preset / screenshot 为完备性目标，缺失记警告（治理追赶项）。
  ④ launcher.config.json 的每个 binding 必须在注册表中有对应条目（binding 字段）；
    反向（注册表 binding 必须存在于 launcher.config.json）同样检查，
    两个方向均可通过注册表 exemptions 数组豁免。
  ⑤ mods/showcases/ 下每个 csproj（含 csproj 的目录）必须有注册表条目覆盖，
    或列入注册表 exemptions 数组。

exemptions 数组元素格式（写在 showcase.registry.json 顶层）：
  { "kind": "csproj",  "value": "mods/showcases/foo/FooMod/FooMod.csproj", "reason": "..." }
  { "kind": "binding", "value": "some_binding_name",                       "reason": "..." }
kind=csproj 的 value 也可以是含 csproj 的目录路径。

失败时以非零码退出并打印全部错误。
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

# CI Windows 运行器默认控制台编码（如 cp1252）无法输出中文，强制 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


TIERS = {"T1", "T2", "T3", "T4"}
CATEGORIES = {
    "capability", "genre", "panel", "stress",
    "fixture", "demo", "tool", "example",
}
STATUSES = {"active", "experimental", "retired"}
REQUIRED_FIELDS = ("id", "tier", "category", "title")
T1_REQUIRED_FIELDS = ("binding", "preset", "acceptanceTest", "screenshot")
DEFAULT_SCAN_ROOT = "mods/showcases"


def norm(path: str) -> str:
    """归一化仓库相对路径：正斜杠、去尾部斜杠。"""
    return str(path).replace("\\", "/").strip().rstrip("/")


def git(repo: Path, args: list, timeout: int = 30) -> tuple:
    """运行 git 子命令，返回 (是否成功, stdout)。失败/超时不抛异常。"""
    try:
        proc = subprocess.run(
            ["git", "-C", str(repo), *args],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=timeout,
        )
        return proc.returncode == 0, proc.stdout
    except (OSError, subprocess.TimeoutExpired):
        return False, ""


def path_exists_in_tree(repo: Path, rel: str) -> bool:
    """目录/文件是否存在于 HEAD 树中。

    使用 `git ls-tree HEAD -- <path>`：只读取 tree 对象，不会触发 blobless
    稀疏克隆的 blob 按需拉取（blob 拉取会访问网络并可能挂起）。git 完全
    不可用（非仓库）时回退文件系统判断。
    """
    ok, out = git(repo, ["ls-tree", "HEAD", "--", rel])
    if ok:
        return out.strip() != ""
    ok_rev, _ = git(repo, ["rev-parse", "--git-dir"])
    if not ok_rev:
        return (repo / rel).exists()
    return False


def list_csproj_dirs(repo: Path, scan_root: str) -> list:
    """列出 scan_root 下所有含 csproj 的目录（git 树相对路径）。"""
    ok, out = git(repo, ["ls-tree", "-r", "--name-only", "HEAD", "--", scan_root])
    if not ok:
        # 回退：文件系统扫描
        root = repo / scan_root
        if not root.is_dir():
            return []
        return sorted(
            norm(p.parent.relative_to(repo).as_posix())
            for p in root.rglob("*.csproj")
        )
    dirs = {
        norm(line.rsplit("/", 1)[0])
        for line in out.splitlines()
        if line.endswith(".csproj") and "/" in line
    }
    return sorted(dirs)


def load_json(path: Path, label: str, errors: list):
    if not path.is_file():
        errors.append(f"{label} 不存在: {path}")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        errors.append(f"{label} 不是合法 JSON: {path} ({exc})")
        return None


def non_empty(value) -> bool:
    return value is not None and str(value).strip() != ""


def main() -> int:
    parser = argparse.ArgumentParser(description="showcase 注册表一致性校验器")
    repo_default = Path(__file__).resolve().parent.parent
    parser.add_argument("--repo", default=str(repo_default), help="仓库根目录")
    parser.add_argument("--registry", default=None,
                        help="showcase.registry.json 路径（默认 <repo>/showcase.registry.json）")
    parser.add_argument("--launcher", default=None,
                        help="launcher.config.json 路径（默认 <repo>/launcher.config.json）")
    parser.add_argument("--csproj-scan-root", default=DEFAULT_SCAN_ROOT,
                        help=f"csproj 覆盖检查扫描根目录（默认 {DEFAULT_SCAN_ROOT}）")
    args = parser.parse_args()

    repo = Path(args.repo).resolve()
    registry_path = Path(args.registry) if args.registry else repo / "showcase.registry.json"
    launcher_path = Path(args.launcher) if args.launcher else repo / "launcher.config.json"
    scan_root = norm(args.csproj_scan_root)

    errors: list = []
    warnings: list = []

    registry = load_json(registry_path, "注册表", errors)
    launcher = load_json(launcher_path, "launcher.config.json", errors)
    if registry is None or launcher is None:
        for e in errors:
            print(f"[错误] {e}")
        return 1

    # ---------- 豁免清单 ----------
    exemptions = registry.get("exemptions", [])
    if not isinstance(exemptions, list):
        errors.append("注册表 exemptions 必须是数组")
        exemptions = []
    csproj_exempts = set()
    binding_exempts = set()
    for i, ex in enumerate(exemptions):
        if isinstance(ex, str):  # 兼容简写：纯字符串按 csproj 路径豁免
            csproj_exempts.add(norm(ex))
            continue
        if not isinstance(ex, dict) or "kind" not in ex or "value" not in ex:
            warnings.append(f"exemptions[{i}] 缺少 kind/value，已忽略: {ex!r}")
            continue
        if not ex.get("reason"):
            warnings.append(f"exemptions[{i}] 建议填写 reason: {ex!r}")
        if ex["kind"] == "csproj":
            csproj_exempts.add(norm(ex["value"]))
        elif ex["kind"] == "binding":
            binding_exempts.add(str(ex["value"]))
        else:
            warnings.append(f"exemptions[{i}] 未知 kind={ex['kind']!r}，已忽略")

    # ---------- ① 基础字段 ----------
    showcases = registry.get("showcases")
    if not isinstance(showcases, list):
        errors.append("注册表缺少 showcases 数组")
        showcases = []

    seen_ids = set()
    entries = []  # (index, entry_dict)
    for i, entry in enumerate(showcases):
        if not isinstance(entry, dict):
            errors.append(f"showcases[{i}] 不是对象")
            continue
        label = f"showcases[{i}](id={entry.get('id', '?')})"
        for field in REQUIRED_FIELDS:
            if not non_empty(entry.get(field)):
                errors.append(f"{label} 缺少必填字段或字段为空: {field}")
        tier = entry.get("tier")
        if non_empty(tier) and tier not in TIERS:
            errors.append(f"{label} tier={tier!r} 非法，必须是 {sorted(TIERS)} 之一")
        category = entry.get("category")
        if non_empty(category) and category not in CATEGORIES:
            errors.append(f"{label} category={category!r} 非法，必须是 {sorted(CATEGORIES)} 之一")
        status = entry.get("status")
        if non_empty(status) and status not in STATUSES:
            errors.append(f"{label} status={status!r} 非法，必须是 {sorted(STATUSES)} 之一")
        sid = entry.get("id")
        if non_empty(sid):
            if sid in seen_ids:
                errors.append(f"{label} id 重复: {sid}")
            seen_ids.add(sid)
        entries.append((i, entry))

    # ---------- ② path 存在于 git 树 ----------
    covered_dirs = {}  # 归一化目录 -> entry
    for i, entry in entries:
        label = f"showcases[{i}](id={entry.get('id', '?')})"
        path = entry.get("path")
        if not non_empty(path):
            errors.append(f"{label} 缺少 path 字段（mod 所在目录）")
            continue
        rel = norm(path)
        covered_dirs[rel] = entry
        if not path_exists_in_tree(repo, rel):
            errors.append(f"{label} path 在 git 树中不存在: {rel}")
        project = entry.get("projectPath")
        if non_empty(project) and path_exists_in_tree(repo, rel):
            csproj_rel = f"{rel}/{norm(project)}"
            if not path_exists_in_tree(repo, csproj_rel):
                errors.append(f"{label} projectPath 在 git 树中不存在: {csproj_rel}")

    # ---------- ③ T1 完整性 ----------
    # acceptanceTest 是 T1 的契约核心，缺失即错误；
    # binding/preset/screenshot 是 T1 的完备性目标（五件套），当前大量条目
    # 证据尚未检入或未接线 launcher，缺失只记警告（治理追赶项，见 tests.html）。
    for i, entry in entries:
        if entry.get("tier") != "T1":
            continue
        label = f"showcases[{i}](id={entry.get('id', '?')})"
        for field in T1_REQUIRED_FIELDS:
            if non_empty(entry.get(field)):
                continue
            if field == "acceptanceTest":
                errors.append(f"{label} 为 T1，必须具有非空字段: {field}")
            else:
                warnings.append(f"{label} 为 T1，完备性字段待补: {field}")

    # ---------- ④ binding 双向一致 ----------
    launcher_bindings = {
        b.get("name") for b in launcher.get("bindings", [])
        if isinstance(b, dict) and non_empty(b.get("name"))
    }
    registry_bindings = {
        entry.get("binding") for _, entry in entries
        if non_empty(entry.get("binding"))
    }
    for name in sorted(launcher_bindings - registry_bindings):
        if name in binding_exempts:
            continue
        errors.append(
            f"launcher.config.json binding '{name}' 在注册表中无对应条目"
            f"（需新增条目或加入 exemptions: kind=binding）"
        )
    for name in sorted(registry_bindings - launcher_bindings):
        if name in binding_exempts:
            continue
        errors.append(
            f"注册表 binding '{name}' 在 launcher.config.json 中不存在"
            f"（需修正 binding 名或加入 exemptions: kind=binding）"
        )

    # ---------- ⑤ csproj 覆盖 ----------
    for d in list_csproj_dirs(repo, scan_root):
        if d in covered_dirs:
            continue
        # 豁免可按 csproj 文件路径或目录路径声明
        csproj_hit = d in csproj_exempts or any(
            norm(v) == d or norm(v).startswith(d + "/") and norm(v).endswith(".csproj")
            for v in csproj_exempts
        )
        if csproj_hit:
            continue
        errors.append(
            f"{scan_root} 下目录 '{d}' 含 csproj 但无注册表条目"
            f"（需新增条目或加入 exemptions: kind=csproj）"
        )

    # ---------- 汇总 ----------
    for w in warnings:
        print(f"[警告] {w}")
    for e in errors:
        print(f"[错误] {e}")
    print(
        f"校验汇总: 条目 {len(entries)} 个, launcher binding {len(launcher_bindings)} 个, "
        f"豁免 {len(exemptions)} 条, 错误 {len(errors)} 个, 警告 {len(warnings)} 个"
    )
    if errors:
        print("注册表校验失败。")
        return 1
    print("注册表校验通过。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
