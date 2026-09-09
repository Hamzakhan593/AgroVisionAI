from __future__ import annotations

import sys


def main() -> int:
    try:
        from app.model_registry import registry
    except ImportError as exc:
        print(f"[ERROR] Missing Python dependency: {exc}")
        print("Run setup_api.bat first.")
        return 1

    registry.load_available_models()
    statuses = registry.status()
    failed = False
    for item in statuses:
        state = "READY" if item["loaded"] else "NOT READY"
        print(f"[{state}] {item['crop'].title()}: {item['filename'] or 'model file not found'}")
        if item["error"]:
            print(f"        {item['error']}")
        failed = failed or not item["loaded"]

    if failed:
        print("\nCopy all three final .keras files into AIService\\models and try again.")
        return 1

    print("\nAll AgroVisionAI models loaded successfully.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

