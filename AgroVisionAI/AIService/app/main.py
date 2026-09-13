from __future__ import annotations

import asyncio
import io
import re
import secrets
import os
import time
import uuid
from contextlib import asynccontextmanager

from fastapi import FastAPI, File, Form, Header, HTTPException, UploadFile, status
from PIL import Image, ImageOps, UnidentifiedImageError

from .config import CROP_SPECS, MAX_IMAGE_BYTES, MAX_IMAGE_PIXELS, MIN_IMAGE_SIDE
from .disease_info import disease_info
from .crop_validator import validator
from .model_registry import ModelNotAvailableError, PredictionError, registry
from .schemas import ActivateModelRequest, HealthResponse, ManagedModelStatus, ModelStatus, PredictionResponse, TopPrediction


DISCLAIMER = (
    "AI results are decision support, not a laboratory diagnosis. Confirm important cases "
    "with a qualified local agricultural expert before applying chemicals."
)


@asynccontextmanager
async def lifespan(_: FastAPI):
    await asyncio.to_thread(registry.load_available_models)
    try:
        await asyncio.to_thread(validator.load)
    except ModelNotAvailableError:
        pass  # Keep health diagnostics reachable; prediction remains fail-closed.
    yield


app = FastAPI(
    title="AgroVisionAI Inference API",
    version="1.3.0",
    description="Local crop-disease inference for Cotton, Wheat and Rice models.",
    lifespan=lifespan,
)


def _model_version(model_name: str) -> str:
    """Return a compact model version from a versioned filename (for example v2/v3)."""
    match = re.search(r"(?:^|_)v(\d+)(?:_|\.|$)", model_name, flags=re.IGNORECASE)
    return f"v{match.group(1)}" if match else "unversioned"


@app.get("/", tags=["Service"])
async def root() -> dict[str, str]:
    return {
        "service": "AgroVisionAI Inference API",
        "version": "1.3.0",
        "health": "/health",
        "documentation": "/docs",
    }


@app.get("/health", response_model=HealthResponse, tags=["Service"])
async def health() -> HealthResponse:
    model_statuses = [ModelStatus(**item) for item in [validator.status(), *registry.status()]]
    overall = "ready" if model_statuses and all(item.loaded for item in model_statuses) else "degraded"
    return HealthResponse(status=overall, models=model_statuses)


@app.get("/api/v1/models", response_model=list[ModelStatus], tags=["Service"])
async def models() -> list[ModelStatus]:
    return [ModelStatus(**item) for item in [validator.status(), *registry.status()]]


def _require_model_admin_key(x_agrovision_admin_key: str | None = Header(default=None)) -> None:
    expected = os.getenv("AGROVISION_ADMIN_KEY", "").strip()
    if len(expected) < 16:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Model management is disabled because AGROVISION_ADMIN_KEY is not configured.",
        )
    if not x_agrovision_admin_key or not secrets.compare_digest(x_agrovision_admin_key, expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid model-management key.")


@app.get("/api/v1/admin/models", response_model=list[ManagedModelStatus], tags=["Model Management"])
async def managed_models(x_agrovision_admin_key: str | None = Header(default=None)) -> list[ManagedModelStatus]:
    _require_model_admin_key(x_agrovision_admin_key)
    return [ManagedModelStatus(**item) for item in await asyncio.to_thread(registry.managed_models)]


@app.post(
    "/api/v1/admin/models/{crop}/activate",
    response_model=ManagedModelStatus,
    tags=["Model Management"],
)
async def activate_model(
    crop: str,
    request: ActivateModelRequest,
    x_agrovision_admin_key: str | None = Header(default=None),
) -> ManagedModelStatus:
    _require_model_admin_key(x_agrovision_admin_key)
    normalized_crop = crop.strip().lower()
    try:
        result = await asyncio.to_thread(registry.activate, normalized_crop, request.filename.strip())
        return ManagedModelStatus(**result)
    except ModelNotAvailableError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except PredictionError as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc


@app.post(
    "/api/v1/predict/{crop}",
    response_model=PredictionResponse,
    tags=["Prediction"],
)
async def predict_by_crop(crop: str, file: UploadFile = File(...)) -> PredictionResponse:
    return await _predict(crop, file)


@app.post(
    "/api/v1/predict",
    response_model=PredictionResponse,
    tags=["Prediction"],
)
async def predict(crop: str | None = Form(None), file: UploadFile = File(...)) -> PredictionResponse:
    return await _predict(crop, file)


async def _predict(crop: str | None, file: UploadFile) -> PredictionResponse:
    started = time.perf_counter()
    requested_crop = crop.strip().lower() if crop else None
    if requested_crop and requested_crop not in CROP_SPECS:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Unsupported crop '{crop}'. Choose cotton, wheat, or rice.",
        )

    data = await file.read(MAX_IMAGE_BYTES + 1)
    await file.close()
    if not data:
        raise HTTPException(status_code=400, detail="The uploaded image is empty.")
    if len(data) > MAX_IMAGE_BYTES:
        raise HTTPException(status_code=413, detail="The image must be 5 MB or smaller.")

    try:
        with Image.open(io.BytesIO(data)) as source:
            if source.width * source.height > MAX_IMAGE_PIXELS:
                raise HTTPException(status_code=400, detail="The image dimensions are too large.")
            source.verify()
        with Image.open(io.BytesIO(data)) as source:
            image = ImageOps.exif_transpose(source).convert("RGB")
    except (UnidentifiedImageError, OSError, Image.DecompressionBombError) as exc:
        raise HTTPException(status_code=400, detail="Upload a valid JPG, JPEG, or PNG image.") from exc

    if image.width < MIN_IMAGE_SIDE or image.height < MIN_IMAGE_SIDE:
        raise HTTPException(
            status_code=400,
            detail=f"Image dimensions must be at least {MIN_IMAGE_SIDE} x {MIN_IMAGE_SIDE} pixels.",
        )

    try:
        crop_prediction = await asyncio.to_thread(validator.predict, image)
        normalized_crop = crop_prediction["label"]
        if normalized_crop == "out_of_scope":
            raise HTTPException(status_code=422, detail="This image does not appear to be a supported crop leaf. Upload a clear Cotton, Wheat, or Rice leaf photograph.")
        if crop_prediction["confidence"] < crop_prediction["threshold"]:
            raise HTTPException(status_code=422, detail="The crop could not be identified confidently. Upload one clear, well-lit leaf with a simple background.")
        if normalized_crop not in CROP_SPECS:
            raise PredictionError("Crop validator returned an unknown crop.")
        if requested_crop and requested_crop != normalized_crop:
            raise HTTPException(status_code=422, detail=f"This image appears to be {normalized_crop.title()}, not {requested_crop.title()}. Use automatic detection.")
        prediction = await asyncio.to_thread(registry.predict, normalized_crop, image)
    except ModelNotAvailableError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    except PredictionError as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    info = disease_info(normalized_crop, prediction["label"])
    top_predictions = []
    for item in prediction["top_predictions"]:
        item_info = disease_info(normalized_crop, item["label"])
        top_predictions.append(
            TopPrediction(
                label=item["label"],
                name=item_info["name"],
                confidence=round(item["confidence"], 6),
                confidence_percent=round(item["confidence"] * 100.0, 2),
            )
        )

    explanation = None
    try:
        explanation = await asyncio.to_thread(
            registry.explain, normalized_crop, image, prediction["label"]
        )
    except (PredictionError, ModelNotAvailableError):
        # Explainability is optional: a Grad-CAM failure must never invalidate an
        # otherwise valid disease prediction.
        explanation = None

    confidence = float(prediction["confidence"])
    return PredictionResponse(
        request_id=str(uuid.uuid4()),
        crop=normalized_crop.title(),
        crop_confidence=round(crop_prediction["confidence"], 6),
        crop_model_name=crop_prediction["model_name"],
        crop_model_version=_model_version(crop_prediction["model_name"]),
        predicted_class=prediction["label"],
        disease=info["name"],
        confidence=round(confidence, 6),
        confidence_percent=round(confidence * 100.0, 2),
        is_healthy=bool(info["is_healthy"]),
        description=info["description"],
        symptoms=info["symptoms"],
        treatment=info["treatment"],
        prevention=info["prevention"],
        top_predictions=top_predictions,
        model_name=prediction["model_name"],
        model_version=_model_version(prediction["model_name"]),
        processing_time_ms=round((time.perf_counter() - started) * 1000.0, 2),
        explanation_available=explanation is not None,
        explanation_method=explanation["method"] if explanation else None,
        explanation_layer=explanation["layer_name"] if explanation else None,
        explanation_image_base64=explanation["image_base64"] if explanation else None,
        explanation_image_media_type=explanation["image_media_type"] if explanation else None,
        disclaimer=DISCLAIMER,
    )
