"""Check Git candidates without printing credential values. Uses Python stdlib only."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys


def git(*args: str) -> bytes:
    return subprocess.check_output(["git", *args])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--staged", action="store_true", help="Read the exact Git index, not working files")
    parser.add_argument("--tracked-only", action="store_true", help="Omit untracked candidates (CI)")
    args = parser.parse_args()
    root = Path(git("rev-parse", "--show-toplevel").decode().strip())
    options = ["ls-files", "-z", "--cached"]
    if not args.staged and not args.tracked_only:
        options += ["--others", "--exclude-standard"]
    paths = sorted(set(p.decode() for p in git(*options).split(b"\0") if p))
    ignored = set(p.decode() for p in git("ls-files", "-z", "-ci", "--exclude-standard").split(b"\0") if p)
    issues: list[str] = []
    private_name = re.compile(
        r"(^|/)(\.env(?:\..*)?|appsettings\.(?:.*\.)?Local\.json|secrets\.json|active_models\.json)$"
        r"|\.(?:keras|h5|hdf5|onnx|pt|pth|ckpt|tflite|pem|key|pfx|p12|mdf|ldf|bak|bacpac|db|sqlite3?)$"
        r"|(^|/)(?:\.venv|venv|node_modules|__pycache__|\.vs|datasets|training-data)/"
        r"|^AgroVisionAI/(?:App_Data/(?:CropImages|Explainability|Exports)|wwwroot/uploads)/",
        re.IGNORECASE,
    )
    signatures = re.compile(
        r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
        r"|g[h]p_[A-Za-z0-9]{30,}|github[_]pat_[A-Za-z0-9_]{30,}"
        r"|AK[I]A[0-9A-Z]{16}",
    )
    assignments = re.compile(
        r'''(?im)(?:AGROVISION_ADMIN_KEY|JWT_KEY|AI_ADMIN_KEY|ADMIN_PASSWORD|SQL_SA_PASSWORD)\s*=\s*["']?([^\s"'\r\n]+)'''
    )
    for name in paths:
        example = name.endswith(".example") or ".example." in name
        if name in ignored:
            issues.append(f"{name}: tracked despite .gitignore; remove from the index")
        if private_name.search(name) and not example and not name.endswith("/.gitkeep"):
            issues.append(f"{name}: private/runtime file or model binary must not be committed")
        if args.staged:
            raw = git("show", ":" + name)
        else:
            file = root / name
            if not file.is_file():
                continue
            if file.stat().st_size > 50 * 1024 * 1024:
                issues.append(f"{name}: exceeds this repository's 50 MiB limit")
                continue
            raw = file.read_bytes()
        if len(raw) > 50 * 1024 * 1024:
            issues.append(f"{name}: exceeds this repository's 50 MiB limit")
            continue
        try:
            body = raw.decode("utf-8-sig")
        except UnicodeDecodeError:
            continue
        if signatures.search(body):
            issues.append(f"{name}: possible private key or access token")
        for match in assignments.finditer(body):
            value = match.group(1)
            if not example and not value.startswith(("$", "%", "<", "[", "ChangeThis", "REPLACE", "your_")):
                issues.append(f"{name}: possible hardcoded credential assignment")
                break
        if Path(name).name.startswith("appsettings") and name.endswith(".json"):
            try:
                config = json.loads(body)
            except json.JSONDecodeError:
                issues.append(f"{name}: invalid JSON")
                continue
            def check_values(obj: object, prefix: str = "") -> None:
                if not isinstance(obj, dict):
                    return
                for key, value in obj.items():
                    full_key = f"{prefix}:{key}".strip(":")
                    if isinstance(value, dict):
                        check_values(value, full_key)
                    elif key.lower() in {"key", "password", "adminkey", "apikey", "clientsecret"} and value:
                        issues.append(f"{name}: {full_key} must be empty in committed configuration")
                    elif isinstance(value, str) and re.search(r"(?i)(?:password|pwd)\s*=\s*[^;\s]+", value):
                        issues.append(f"{name}: connection string contains a password")
            check_values(config)
    if issues:
        print("Repository check FAILED (values redacted):")
        print("\n".join(f"- {issue}" for issue in issues))
        return 1
    print(f"Repository check passed: {len(paths)} files; no blocked paths, large files or recognized secrets.")
    print("This is a focused safeguard, not a full secret scan or a Git history audit.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
