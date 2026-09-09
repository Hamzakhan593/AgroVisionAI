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
    processing_time_ms: float
    disclaimer: str
