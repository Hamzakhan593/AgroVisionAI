from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path
from typing import Any


CATALOG_PATH = Path(__file__).with_name("disease_catalog.json")


def display_name(label: str) -> str:
    return label.replace("_", " ").title()


@lru_cache(maxsize=1)
def _catalog() -> dict[str, dict[str, dict[str, Any]]]:
    with CATALOG_PATH.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def disease_info(crop: str, label: str) -> dict[str, Any]:
    entry = _catalog().get(crop, {}).get(label)
    if entry is not None:
        return entry

    return {
        "name": display_name(label),
        "description": "The model detected this crop condition from the uploaded image.",
        "symptoms": "Inspect the crop carefully and compare the visible symptoms in the field.",
        "treatment": "Consult a local agricultural extension officer before applying a treatment.",
        "prevention": "Use healthy planting material and monitor the crop regularly.",
        "is_healthy": label == "healthy",
    }

