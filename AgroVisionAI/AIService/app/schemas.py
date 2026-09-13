from __future__ import annotations

from pydantic import BaseModel, Field


class ModelStatus(BaseModel):
    crop: str
    available: bool
    loaded: bool
    filename: str | None = None
    classes: list[str]
    preprocessing: str | None = None
    error: str | None = None


class HealthResponse(BaseModel):
    status: str
    service: str = "AgroVisionAI Inference API"
    models: list[ModelStatus]


class TopPrediction(BaseModel):
    label: str
    name: str
    confidence: float = Field(ge=0.0, le=1.0)
    confidence_percent: float = Field(ge=0.0, le=100.0)


class PredictionResponse(BaseModel):
    request_id: str
    crop: str
    crop_confidence: float = Field(ge=0.0, le=1.0)
    crop_model_name: str
    crop_model_version: str
    predicted_class: str
    disease: str
    confidence: float = Field(ge=0.0, le=1.0)
    confidence_percent: float = Field(ge=0.0, le=100.0)
    is_healthy: bool
    description: str
    symptoms: str
    treatment: str
    prevention: str
    top_predictions: list[TopPrediction]
    model_name: str
    model_version: str
    processing_time_ms: float = Field(ge=0.0)
    explanation_available: bool = False
    explanation_method: str | None = None
    explanation_layer: str | None = None
    explanation_image_base64: str | None = None
    explanation_image_media_type: str | None = None
    disclaimer: str

class ManagedModelStatus(BaseModel):
    crop: str
    filename: str
    version: str
    available: bool
    active: bool
    loaded: bool
    preprocessing: str | None = None
    classes: list[str]
    selection_locked_by_environment: bool = False


class ActivateModelRequest(BaseModel):
    filename: str = Field(min_length=1, max_length=200)
