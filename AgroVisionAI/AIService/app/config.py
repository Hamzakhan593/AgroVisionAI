from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path


SERVICE_ROOT = Path(__file__).resolve().parent.parent
MODELS_DIR = Path(
    os.getenv("AGROVISION_MODELS_DIR", str(SERVICE_ROOT / "models"))
).expanduser().resolve()
MODEL_SELECTION_FILE = Path(
    os.getenv("AGROVISION_MODEL_SELECTION_FILE", str(SERVICE_ROOT / "active_models.json"))
).expanduser().resolve()

MAX_IMAGE_BYTES = int(os.getenv("AGROVISION_MAX_IMAGE_BYTES", str(5 * 1024 * 1024)))
MIN_IMAGE_SIDE = int(os.getenv("AGROVISION_MIN_IMAGE_SIDE", "32"))
MAX_IMAGE_PIXELS = int(os.getenv("AGROVISION_MAX_IMAGE_PIXELS", "25000000"))


@dataclass(frozen=True)
class CropModelSpec:
    crop: str
    classes: tuple[str, ...]
    filenames: tuple[str, ...]


CROP_SPECS: dict[str, CropModelSpec] = {
    "cotton": CropModelSpec(
        crop="cotton",
        classes=("bacterial_blight", "curl_virus", "healthy"),
        filenames=("cotton_cnn_v3_best.keras",),
    ),
    "wheat": CropModelSpec(
        crop="wheat",
        classes=("brown_rust", "healthy", "yellow_rust"),
        filenames=(
            "wheat_v2_efficientnetv2b0_best.keras",
            "wheat_cnn_v2_best.keras",
            "wheat_cnn_v1_best.keras",
        ),
    ),
    "rice": CropModelSpec(
        crop="rice",
        classes=(
            "bacterial_leaf_blight",
            "brown_spot",
            "healthy",
            "leaf_blast",
            "tungro",
        ),
        filenames=(
            "rice_v2_efficientnetv2b0_best.keras",
            "rice_v2_best.keras",
            "rice_v1_efficientnetv2b0_best.keras",
        ),
    ),
}


def _read_model_selection() -> dict[str, str]:
    if not MODEL_SELECTION_FILE.is_file():
        return {}
    try:
        data = json.loads(MODEL_SELECTION_FILE.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    if not isinstance(data, dict):
        return {}
    result: dict[str, str] = {}
    for crop, filename in data.items():
        if crop in CROP_SPECS and isinstance(filename, str) and filename == Path(filename).name:
            result[crop] = filename
    return result


def selected_model_filename(crop: str) -> str | None:
    return _read_model_selection().get(crop)


def persist_model_selection(crop: str, filename: str) -> None:
    if crop not in CROP_SPECS:
        raise ValueError(f"Unsupported crop: {crop}")
    if not filename or filename != Path(filename).name:
        raise ValueError("Model filename must be a plain file name.")

    MODEL_SELECTION_FILE.parent.mkdir(parents=True, exist_ok=True)
    data = _read_model_selection()
    data[crop] = filename
    temporary = MODEL_SELECTION_FILE.with_suffix(MODEL_SELECTION_FILE.suffix + ".tmp")
    temporary.write_text(json.dumps(data, indent=2, sort_keys=True), encoding="utf-8")
    temporary.replace(MODEL_SELECTION_FILE)


def environment_model_override(crop: str) -> str | None:
    value = os.getenv(f"AGROVISION_{crop.upper()}_MODEL")
    return value.strip() if value and value.strip() else None


def model_path_for(spec: CropModelSpec) -> Path | None:
    """Resolve environment override, admin-selected model, known final filename, or one unambiguous model."""
    override = environment_model_override(spec.crop)
    if override:
        override_path = Path(override).expanduser()
        if not override_path.is_absolute():
            override_path = SERVICE_ROOT / override_path
        return override_path.resolve()

    selected = selected_model_filename(spec.crop)
    if selected:
        selected_path = (MODELS_DIR / selected).resolve()
        try:
            selected_path.relative_to(MODELS_DIR)
        except ValueError:
            selected_path = None
        if selected_path and selected_path.is_file():
            return selected_path

    for filename in spec.filenames:
        candidate = MODELS_DIR / filename
        if candidate.is_file():
            return candidate

    matches = sorted(MODELS_DIR.glob(f"{spec.crop}*.keras")) if MODELS_DIR.exists() else []
    return matches[0].resolve() if len(matches) == 1 else None


def preprocessing_for(crop: str) -> str:
    """Return preprocessing mode: auto, none, zero_one, or minus_one_one."""
    return os.getenv(f"AGROVISION_{crop.upper()}_PREPROCESSING", "auto").strip().lower()
