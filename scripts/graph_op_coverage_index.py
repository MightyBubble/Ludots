#!/usr/bin/env python3
"""Measure which GasTests methods actually execute which GraphNodeOp.

Coverage attribution must be measured from test source, not inferred from
driver family tables. The gallery generator and the C# guard implement the
same expansion rules independently.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

COVERAGE_REL = "assets/GAS/graph_node_op_coverage.registry.json"
GAS_TESTS_REL = "src/Tests/GasTests"
GALLERY_PREFIX = "GraphOpsNodeGallery"
NON_EXECUTING_GALLERY_CLASSES = {
    "GraphOpsNodeGallerySyncGateTests",
}

UNIVERSAL_GALLERY_METHODS = (
    "EveryExecutableOp_HasVignetteGraphAndUniqueShowcaseId",
    "EveryVignette_TicksOnce_WithChineseCaption",
    "ExistingVignettes_CompileWithFeaturedOp",
    "GeneratedMaps_SpawnEveryVignetteActor",
)

UNIVERSAL_GALLERY_REFS = tuple(
    f"GraphOpsNodeGalleryAcceptanceTests.{name}" for name in UNIVERSAL_GALLERY_METHODS
)

_CLASS_RE = re.compile(r"(?:public\s+)?(?:sealed\s+)?class\s+(\w+)")
_METHOD_RE = re.compile(r"public\s+void\s+([A-Za-z0-9_]+)\s*\(")
_STRING_LIT_RE = re.compile(r'"([A-Za-z][A-Za-z0-9_]*)"')
_ARRAY_RE = re.compile(
    r"(?:readonly\s+)?string\[\s*\]\s+(\w+)\s*=\s*\[(.*?)\];",
    re.DOTALL,
)
_TEST_CASE_RE = re.compile(r'\[TestCase\(\s*"([A-Za-z][A-Za-z0-9_]*)"')
_TEST_CASE_SOURCE_RE = re.compile(r"\[TestCaseSource\(\s*nameof\((\w+)\)\s*\)\]")
_GRAPH_OP_ENUM_RE = re.compile(r"GraphNodeOp\.([A-Za-z][A-Za-z0-9_]*)")
_JSON_OP_RE = re.compile(r'"op"\s*:\s*"([A-Za-z][A-Za-z0-9_]*)"')
_BIND_LIT_RE = re.compile(
    r"(?:BindOp|Play|TickOp|BindAndTick)\(\s*\"([A-Za-z][A-Za-z0-9_]*)\"\s*\)"
)
_BIND_PARAM_RE = re.compile(r"(?:BindOp|Play|TickOp|BindAndTick)\(\s*(\w+)\s*\)")
_FOREACH_OP_RE = re.compile(r"foreach\s*\(\s*string\s+(\w+)\s+in\s+(\w+)")
_ENUM_ALL_RE = re.compile(r"Enum\.GetValues(?:<GraphNodeOp>| \(\s*typeof\s*\(\s*GraphNodeOp\s*\)\s*\))")
_GET_FILES_RE = re.compile(r"GetFiles\s*\(")


def read_text(path: Path) -> str:
    raw = path.read_bytes()
    for enc in ("utf-8", "utf-8-sig", "utf-16", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def load_coverage_ops(repo: Path) -> list[str]:
    coverage = json.loads((repo / COVERAGE_REL).read_text(encoding="utf-8"))
    return [entry["op"] for entry in coverage["entries"]]


def extract_leading_attributes(text: str, method_start: int) -> str:
    i = method_start
    while i > 0 and text[i - 1].isspace():
        i -= 1
    chunks: list[str] = []
    while i > 0 and text[i - 1] == "]":
        depth = 0
        j = i - 1
        found = False
        while j >= 0:
            if text[j] == "]":
                depth += 1
            elif text[j] == "[":
                depth -= 1
                if depth == 0:
                    chunks.append(text[j:i])
                    i = j
                    while i > 0 and text[i - 1].isspace():
                        i -= 1
                    found = True
                    break
            j -= 1
        if not found:
            break
    chunks.reverse()
    return "".join(chunks)


def extract_balanced(text: str, start: int) -> str:
    """Return the brace-balanced span starting at text[start] == '{'."""
    if start >= len(text) or text[start] != "{":
        return ""
    depth = 0
    i = start
    in_str = None
    while i < len(text):
        ch = text[i]
        if in_str is None:
            if text.startswith("//", i):
                nl = text.find("\n", i)
                i = len(text) if nl < 0 else nl + 1
                continue
            if text.startswith("/*", i):
                end = text.find("*/", i + 2)
                i = len(text) if end < 0 else end + 2
                continue
            if text.startswith('"""', i):
                end = text.find('"""', i + 3)
                i = len(text) if end < 0 else end + 3
                continue
            if ch in "\"'":
                in_str = ch
                i += 1
                continue
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return text[start : i + 1]
        else:
            if ch == "\\":
                i += 2
                continue
            if ch == in_str:
                in_str = None
        i += 1
    return text[start:]


def _string_ops(blob: str, op_set: set[str]) -> set[str]:
    return {lit for lit in _STRING_LIT_RE.findall(blob) if lit in op_set}


@dataclass
class TestMethodIndex:
    ops: list[str]
    executed: dict[tuple[str, str], set[str]] = field(default_factory=dict)

    @property
    def op_set(self) -> set[str]:
        return set(self.ops)

    def executes(self, class_name: str, method: str, op: str) -> bool:
        return op in self.executed.get((class_name, method), set())

    def has_method(self, class_name: str, method: str) -> bool:
        return (class_name, method) in self.executed

    def gallery_specific_refs(self, op: str) -> list[str]:
        refs: list[str] = []
        for (cls, method), executed in sorted(self.executed.items()):
            if not cls.startswith(GALLERY_PREFIX):
                continue
            if cls in NON_EXECUTING_GALLERY_CLASSES:
                continue
            if method in UNIVERSAL_GALLERY_METHODS:
                continue
            if op in executed:
                refs.append(f"{cls}.{method}")
        return refs

    def is_universal_gallery(self, class_name: str, method: str) -> bool:
        return class_name.startswith(GALLERY_PREFIX) and method in UNIVERSAL_GALLERY_METHODS


def index_gas_tests(repo: Path, ops: list[str] | None = None) -> TestMethodIndex:
    op_list = list(ops) if ops is not None else load_coverage_ops(repo)
    op_set = set(op_list)
    index = TestMethodIndex(ops=op_list)
    tests_root = repo / GAS_TESTS_REL
    for path in tests_root.rglob("*Tests.cs"):
        text = read_text(path)
        class_match = _CLASS_RE.search(text)
        if not class_match:
            continue
        class_name = class_match.group(1)
        arrays = {
            name: {lit for lit in _STRING_LIT_RE.findall(body) if lit in op_set}
            for name, body in _ARRAY_RE.findall(text)
        }
        for match in _METHOD_RE.finditer(text):
            method = match.group(1)
            attrs = extract_leading_attributes(text, match.start())
            after = text[match.end() :]
            brace = after.find("{")
            body = extract_balanced(text, match.end() + brace) if brace >= 0 else ""
            executed = _ops_for_method(method, attrs, body, arrays, op_list, op_set)
            index.executed[(class_name, method)] = executed
    return index


def _iterates_vignette_files(body: str) -> bool:
    if not _GET_FILES_RE.search(body) or "*.json" not in body:
        return False
    lowered = body.lower()
    return "vignette" in lowered


def _ops_for_method(
    method: str,
    attrs: str,
    body: str,
    arrays: dict[str, set[str]],
    op_list: list[str],
    op_set: set[str],
) -> set[str]:
    if _ENUM_ALL_RE.search(body) or _iterates_vignette_files(body):
        return set(op_list)

    executed: set[str] = set()
    executed.update(op for op in _TEST_CASE_RE.findall(attrs) if op in op_set)
    for source in _TEST_CASE_SOURCE_RE.findall(attrs):
        executed.update(arrays.get(source, set()))
    executed.update(op for op in _BIND_LIT_RE.findall(body) if op in op_set)
    executed.update(op for op in _GRAPH_OP_ENUM_RE.findall(body) if op in op_set)
    executed.update(op for op in _JSON_OP_RE.findall(body) if op in op_set)

    local_arrays = {
        name: {lit for lit in _STRING_LIT_RE.findall(blob) if lit in op_set}
        for name, blob in _ARRAY_RE.findall(body)
    }
    foreach_vars = {var: src for var, src in _FOREACH_OP_RE.findall(body)}
    for param in _BIND_PARAM_RE.findall(body):
        if param in local_arrays:
            executed.update(local_arrays[param])
        if param in arrays:
            executed.update(arrays[param])
        if param in foreach_vars:
            src = foreach_vars[param]
            executed.update(local_arrays.get(src, set()))
            executed.update(arrays.get(src, set()))

    for op in op_list:
        if method.startswith(op + "_"):
            executed.add(op)
    return executed


def normalize_ref(token: str) -> str | None:
    token = token.strip()
    if token.startswith("FullyQualifiedName~"):
        token = token.split("~", 1)[1].strip()
    if token.count(".") != 1:
        return None
    cls, method = token.split(".", 1)
    if not cls or not method:
        return None
    return f"{cls}.{method}"


def parse_existing_refs(raw) -> list[str]:
    if raw is None:
        return []
    if isinstance(raw, list):
        refs = []
        for item in raw:
            ref = normalize_ref(str(item))
            if ref and ref not in refs:
                refs.append(ref)
        return refs
    refs = []
    for part in str(raw).split(";"):
        ref = normalize_ref(part)
        if ref and ref not in refs:
            refs.append(ref)
    return refs


def coverage_refs_for_op(op: str, existing, index: TestMethodIndex) -> list[str]:
    refs: list[str] = list(UNIVERSAL_GALLERY_REFS)
    specific = index.gallery_specific_refs(op)
    if not specific:
        raise SystemExit(
            f"Coverage op '{op}' has no op-specific GraphOpsNodeGallery test. "
            "Point it at a test that actually executes this op; do not mark covered by structure."
        )
    for ref in specific:
        if ref not in refs:
            refs.append(ref)
    for token in parse_existing_refs(existing):
        if token in refs:
            continue
        cls, method = token.split(".", 1)
        if index.executes(cls, method, op):
            refs.append(token)
    return refs
