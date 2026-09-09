from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


SERVICE_ROOT = Path(__file__).resolve().parent.parent
MODELS_DIR = Path(
    os.getenv("AGROVISION_MODELS_DIR", str(SERVICE_ROOT / "models"))
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


def model_path_for(spec: CropModelSpec) -> Path | None:
    """Resolve an explicit override, a known final filename, or one unambiguous crop model."""
    override = os.getenv(f"AGROVISION_{spec.crop.upper()}_MODEL")
    if override:
        override_path = Path(override).expanduser()
        if not override_path.is_absolute():
            override_path = SERVICE_ROOT / override_path
        return override_path.resolve()

    for filename in spec.filenames:
        candidate = MODELS_DIR / filename
        if candidate.is_file():
            return candidate

    matches = sorted(MODELS_DIR.glob(f"{spec.crop}*.keras")) if MODELS_DIR.exists() else []
    return matches[0].resolve() if len(matches) == 1 else None


def preprocessing_for(crop: str) -> str:
    """Return preprocessing mode: auto, none, zero_one, or minus_one_one."""
    return os.getenv(f"AGROVISION_{crop.upper()}_PREPROCESSING", "auto").strip().lower()
