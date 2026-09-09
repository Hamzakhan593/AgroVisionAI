from __future__ import annotations

import os
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from .config import CROP_SPECS, CropModelSpec, model_path_for, preprocessing_for


class ModelNotAvailableError(RuntimeError):
    pass


class PredictionError(RuntimeError):
    pass


@dataclass
class LoadedModel:
    model: Any
    path: Path
    input_height: int
    input_width: int
    preprocessing: str
    lock: threading.Lock


class ModelRegistry:
    def __init__(self) -> None:
        self._models: dict[str, LoadedModel] = {}
        self._errors: dict[str, str] = {}
        self._load_lock = threading.Lock()

    def load_available_models(self) -> None:
        for crop in CROP_SPECS:
            path = model_path_for(CROP_SPECS[crop])
            if path and path.is_file():
                try:
                    self._get_or_load(crop)
                except ModelNotAvailableError:
                    pass

    def status(self) -> list[dict[str, Any]]:
        result: list[dict[str, Any]] = []
        for crop, spec in CROP_SPECS.items():
            path = model_path_for(spec)
            loaded = self._models.get(crop)
            result.append(
                {
                    "crop": crop,
                    "available": bool(path and path.is_file()),
                    "loaded": loaded is not None,
                    "filename": path.name if path else None,
                    "classes": list(spec.classes),
                    "preprocessing": loaded.preprocessing if loaded else None,
                    "error": self._errors.get(crop),
                }
            )
        return result

    def predict(self, crop: str, image: Image.Image) -> dict[str, Any]:
        if crop not in CROP_SPECS:
            raise PredictionError(f"Unsupported crop: {crop}")

        loaded = self._get_or_load(crop)
        spec = CROP_SPECS[crop]

        resized = image.resize((loaded.input_width, loaded.input_height), Image.Resampling.BILINEAR)
        array = np.asarray(resized, dtype=np.float32)
        batch = np.expand_dims(self._preprocess(array, loaded.preprocessing), axis=0)

        try:
            with loaded.lock:
                raw_output = loaded.model.predict(batch, verbose=0)
        except Exception as exc:
            raise PredictionError(f"The {crop} model could not process this image.") from exc

        scores = self._extract_scores(raw_output)
        if scores.size != len(spec.classes):
            raise PredictionError(
                f"{crop.title()} model returned {scores.size} classes, but API configuration "
                f"contains {len(spec.classes)}. Check class order in app/config.py."
            )

        probabilities = self._to_probabilities(scores)
        order = np.argsort(probabilities)[::-1]
        best_index = int(order[0])

        return {
            "label": spec.classes[best_index],
            "confidence": float(probabilities[best_index]),
            "top_predictions": [
                {"label": spec.classes[int(index)], "confidence": float(probabilities[int(index)])}
                for index in order[: min(3, len(order))]
            ],
            "model_name": loaded.path.name,
        }

    def _get_or_load(self, crop: str) -> LoadedModel:
        existing = self._models.get(crop)
        if existing is not None:
            return existing

        with self._load_lock:
            existing = self._models.get(crop)
            if existing is not None:
                return existing

            spec = CROP_SPECS[crop]
            path = model_path_for(spec)
            if path is None or not path.is_file():
                expected = ", ".join(spec.filenames)
                error = f"Model file is missing. Put one of these files in AIService/models: {expected}"
                self._errors[crop] = error
                raise ModelNotAvailableError(error)

            try:
                os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
                import tensorflow as tf

                model = tf.keras.models.load_model(path, compile=False)
                input_height, input_width = self._input_size(model)
                mode = self._resolve_preprocessing(crop, model)
                loaded = LoadedModel(
                    model=model,
                    path=path,
                    input_height=input_height,
                    input_width=input_width,
                    preprocessing=mode,
                    lock=threading.Lock(),
                )
                self._models[crop] = loaded
                self._errors.pop(crop, None)
                return loaded
            except Exception as exc:
                error = f"Failed to load {path.name}: {type(exc).__name__}: {exc}"
                self._errors[crop] = error
                raise ModelNotAvailableError(error) from exc

    @staticmethod
    def _input_size(model: Any) -> tuple[int, int]:
        shape = model.input_shape
        if isinstance(shape, list):
            if len(shape) != 1:
                raise ValueError("Only single-input image models are supported.")
            shape = shape[0]

        if len(shape) != 4 or shape[-1] not in (3, None):
            raise ValueError(f"Expected model input shape (batch, height, width, 3), received {shape}.")

        if shape[1] is None or shape[2] is None:
            raise ValueError("Model input height and width must be fixed.")

        return int(shape[1]), int(shape[2])

    @staticmethod
    def _all_layers(model: Any) -> list[Any]:
        found: list[Any] = []
        pending = list(getattr(model, "layers", []))
        visited: set[int] = set()
        while pending:
            layer = pending.pop()
            if id(layer) in visited:
                continue
            visited.add(id(layer))
            found.append(layer)
            pending.extend(getattr(layer, "layers", []))
        return found

    def _resolve_preprocessing(self, crop: str, model: Any) -> str:
        requested = preprocessing_for(crop)
        valid = {"auto", "none", "zero_one", "minus_one_one"}
        if requested not in valid:
            raise ValueError(
                f"Invalid {crop} preprocessing '{requested}'. Choose one of: {', '.join(sorted(valid))}."
            )
        if requested != "auto":
            return requested

        layers = self._all_layers(model)
        if any(layer.__class__.__name__.lower() == "rescaling" for layer in layers):
            return "none"

        searchable = " ".join(
            [str(getattr(model, "name", ""))]
            + [f"{layer.__class__.__name__} {getattr(layer, 'name', '')}" for layer in layers]
        ).lower()
        if "efficientnet" in searchable:
            return "none"

        return "zero_one"

    @staticmethod
    def _preprocess(array: np.ndarray, mode: str) -> np.ndarray:
        if mode == "none":
            return array
        if mode == "zero_one":
            return array / 255.0
        if mode == "minus_one_one":
            return (array / 127.5) - 1.0
        raise ValueError(f"Unknown preprocessing mode: {mode}")

    @staticmethod
    def _extract_scores(raw_output: Any) -> np.ndarray:
        if isinstance(raw_output, dict):
            if len(raw_output) != 1:
                raise PredictionError("Model has multiple named outputs; one output is required.")
            raw_output = next(iter(raw_output.values()))
        if isinstance(raw_output, (list, tuple)):
            if len(raw_output) != 1:
                raise PredictionError("Model has multiple outputs; one output is required.")
            raw_output = raw_output[0]

        scores = np.asarray(raw_output, dtype=np.float64)
        if scores.ndim == 2 and scores.shape[0] == 1:
            scores = scores[0]
        scores = scores.reshape(-1)
        if not np.all(np.isfinite(scores)):
            raise PredictionError("Model returned a non-finite prediction.")
        return scores

    @staticmethod
    def _to_probabilities(scores: np.ndarray) -> np.ndarray:
        total = float(np.sum(scores))
        if np.all(scores >= 0.0) and np.all(scores <= 1.0) and abs(total - 1.0) <= 0.01:
            return scores / total

        shifted = scores - np.max(scores)
        exponentials = np.exp(shifted)
        return exponentials / np.sum(exponentials)


registry = ModelRegistry()

