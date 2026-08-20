#!/usr/bin/env python3
"""AI ArtMirror GPT Image 2 helper.

Reads the API key from AIARTMIRROR_API_KEY or AI_ARTMIRROR_API_KEY.
Never prints the key.
"""

from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path


DEFAULT_BASE_URL = "https://www.aiartmirror.com/v1"
MODEL = "gpt-image-2"
SIZES = ("auto", "1024x1024", "1024x1536", "1536x1024")
QUALITIES = ("auto", "low", "medium", "high")


class ApiError(RuntimeError):
    def __init__(self, status: int | None, message: str, body: str = "") -> None:
        super().__init__(message)
        self.status = status
        self.body = body


def api_key() -> str:
    key = os.environ.get("AIARTMIRROR_API_KEY") or os.environ.get("AI_ARTMIRROR_API_KEY")
    if not key:
        raise SystemExit("Missing AIARTMIRROR_API_KEY or AI_ARTMIRROR_API_KEY.")
    return key


def base_url() -> str:
    return os.environ.get("AIARTMIRROR_BASE_URL", DEFAULT_BASE_URL).rstrip("/")


def request_json(method: str, path: str, *, payload: dict | None = None, timeout: float = 120.0) -> dict:
    data = None
    headers = {
        "Authorization": f"Bearer {api_key()}",
        "Accept": "application/json",
    }
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(
        f"{base_url()}{path}",
        data=data,
        headers=headers,
        method=method,
    )
    return send(req, timeout)


def request_multipart(path: str, *, fields: dict[str, str], files: dict[str, Path], timeout: float = 120.0) -> dict:
    boundary = f"----aiartmirror-{uuid.uuid4().hex}"
    chunks: list[bytes] = []

    for name, value in fields.items():
        chunks.append(f"--{boundary}\r\n".encode())
        chunks.append(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode())
        chunks.append(str(value).encode("utf-8"))
        chunks.append(b"\r\n")

    for name, file_path in files.items():
        filename = file_path.name
        content_type = mimetypes.guess_type(filename)[0] or "application/octet-stream"
        chunks.append(f"--{boundary}\r\n".encode())
        chunks.append(
            f'Content-Disposition: form-data; name="{name}"; filename="{filename}"\r\n'.encode()
        )
        chunks.append(f"Content-Type: {content_type}\r\n\r\n".encode())
        chunks.append(file_path.read_bytes())
        chunks.append(b"\r\n")

    chunks.append(f"--{boundary}--\r\n".encode())
    body = b"".join(chunks)

    req = urllib.request.Request(
        f"{base_url()}{path}",
        data=body,
        headers={
            "Authorization": f"Bearer {api_key()}",
            "Accept": "application/json",
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "Content-Length": str(len(body)),
        },
        method="POST",
    )
    return send(req, timeout)


def send(req: urllib.request.Request, timeout: float) -> dict:
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            text = resp.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        text = exc.read().decode("utf-8", errors="replace")
        raise ApiError(exc.code, friendly_error(exc.code, text), text) from exc
    except urllib.error.URLError as exc:
        raise ApiError(None, f"Network error: {exc.reason}") from exc

    try:
        return json.loads(text)
    except json.JSONDecodeError as exc:
        raise ApiError(None, f"Invalid JSON response: {exc}", text[:1000]) from exc


def friendly_error(status: int, body: str) -> str:
    try:
        parsed = json.loads(body)
        err = parsed.get("error") or {}
        message = err.get("message") or body
        code = err.get("code") or ""
    except json.JSONDecodeError:
        message = body
        code = ""

    if status == 401:
        return "Authentication failed: invalid AI ArtMirror token."
    if status == 503 and code == "model_not_found":
        return "Model is unavailable. Use exactly gpt-image-2."
    return f"AI ArtMirror API error HTTP {status}: {message}"


def output_paths(output: Path, count: int) -> list[Path]:
    if count <= 1:
        return [output]
    stem = output.stem
    suffix = output.suffix or ".png"
    return [output.with_name(f"{stem}-{idx + 1}{suffix}") for idx in range(count)]


def save_images(response: dict, output: Path) -> list[Path]:
    data = response.get("data")
    if not isinstance(data, list) or not data:
        raise ApiError(None, "Response did not contain data[].b64_json.", json.dumps(response)[:1000])

    paths = output_paths(output, len(data))
    output.parent.mkdir(parents=True, exist_ok=True)

    for item, path in zip(data, paths, strict=True):
        b64 = item.get("b64_json") if isinstance(item, dict) else None
        if not b64:
            raise ApiError(None, "Response image item is missing b64_json.", json.dumps(item)[:1000])
        if "," in b64 and b64.startswith("data:"):
            b64 = b64.split(",", 1)[1]
        path.write_bytes(base64.b64decode(b64))

    return paths


def cmd_models(args: argparse.Namespace) -> int:
    response = request_json("GET", "/models", timeout=args.timeout)
    print(json.dumps(response, ensure_ascii=False, indent=2))
    return 0


def cmd_generate(args: argparse.Namespace) -> int:
    payload = {
        "model": MODEL,
        "prompt": args.prompt,
        "n": args.n,
        "size": args.size,
        "quality": args.quality,
    }
    started = time.time()
    response = request_json("POST", "/images/generations", payload=payload, timeout=args.timeout)
    paths = save_images(response, Path(args.output))
    print_summary(response, paths, time.time() - started)
    return 0


def cmd_edit(args: argparse.Namespace) -> int:
    image = Path(args.image)
    if not image.is_file():
        raise SystemExit(f"Reference image not found: {image}")
    fields = {
        "model": MODEL,
        "prompt": args.prompt,
        "n": str(args.n),
        "size": args.size,
        "quality": args.quality,
    }
    started = time.time()
    response = request_multipart(
        "/images/edits",
        fields=fields,
        files={"image": image},
        timeout=args.timeout,
    )
    paths = save_images(response, Path(args.output))
    print_summary(response, paths, time.time() - started)
    return 0


def print_summary(response: dict, paths: list[Path], elapsed: float) -> None:
    summary = {
        "ok": True,
        "model": response.get("model"),
        "created": response.get("created"),
        "imageCount": len(paths),
        "elapsedSeconds": round(elapsed, 2),
        "files": [str(path.resolve()) for path in paths],
        "usage": response.get("usage"),
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))


def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="AI ArtMirror GPT Image 2 helper")
    sub = p.add_subparsers(dest="command", required=True)

    models = sub.add_parser("models", help="List available models")
    models.add_argument("--timeout", type=float, default=120.0)
    models.set_defaults(func=cmd_models)

    gen = sub.add_parser("generate", help="Generate image from text")
    gen.add_argument("--prompt", required=True)
    gen.add_argument("--output", required=True)
    gen.add_argument("--n", type=int, default=1)
    gen.add_argument("--size", choices=SIZES, default="1024x1024")
    gen.add_argument("--quality", choices=QUALITIES, default="low")
    gen.add_argument("--timeout", type=float, default=180.0)
    gen.set_defaults(func=cmd_generate)

    edit = sub.add_parser("edit", help="Edit a reference image")
    edit.add_argument("--image", required=True)
    edit.add_argument("--prompt", required=True)
    edit.add_argument("--output", required=True)
    edit.add_argument("--n", type=int, default=1)
    edit.add_argument("--size", choices=SIZES, default="1024x1024")
    edit.add_argument("--quality", choices=QUALITIES, default="low")
    edit.add_argument("--timeout", type=float, default=180.0)
    edit.set_defaults(func=cmd_edit)

    return p


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    if getattr(args, "n", 1) < 1 or getattr(args, "n", 1) > 10:
        raise SystemExit("--n must be between 1 and 10.")
    try:
        return args.func(args)
    except ApiError as exc:
        print(json.dumps({"ok": False, "status": exc.status, "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
