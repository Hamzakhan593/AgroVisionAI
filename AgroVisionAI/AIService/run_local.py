"""Start the Windows/local API with the web app's ignored shared admin key."""
import json
import os
from pathlib import Path

import uvicorn


def main() -> None:
    local_settings = Path(__file__).resolve().parent.parent / "appsettings.Local.json"
    if "AGROVISION_ADMIN_KEY" not in os.environ and local_settings.is_file():
        settings = json.loads(local_settings.read_text(encoding="utf-8-sig"))
        key = settings.get("AgroVisionApi", {}).get("AdminKey", "")
        if key:
            os.environ["AGROVISION_ADMIN_KEY"] = key
    uvicorn.run("app.main:app", host="127.0.0.1", port=8000)


if __name__ == "__main__":
    main()
