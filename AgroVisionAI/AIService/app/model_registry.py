from __future__ import annotations

import base64
import io
import os
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageOps

from .config import (
    CROP_SPECS,
    MODELS_DIR,
    environment_model_override,
    model_path_for,
    persist_model_selection,
    preprocessing_for,
)


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

    def managed_models(self) -> list[dict[str, Any]]:
        """Return all discovered disease-model files and identify the active runtime model."""
        result: list[dict[str, Any]] = []
        for crop, spec in CROP_SPECS.items():
            active_path = model_path_for(spec)
            active_name = active_path.name if active_path else None
            loaded = self._models.get(crop)
            loaded_name = loaded.path.name if loaded else None
            candidates = sorted(MODELS_DIR.glob(f"{crop}*.keras")) if MODELS_DIR.exists() else []
            for path in candidates:
                result.append(
                    {
                        "crop": crop,
                        "filename": path.name,
                        "version": self._version_from_filename(path.name),
                        "available": path.is_file(),
                        "active": path.name == active_name,
                        "loaded": path.name == loaded_name,
                        "preprocessing": loaded.preprocessing if loaded and path.name == loaded_name else None,
                        "classes": list(spec.classes),
                        "selection_locked_by_environment": environment_model_override(crop) is not None,
                    }
                )
        return result

    def activate(self, crop: str, filename: str) -> dict[str, Any]:
        """Validate, load, persist, and hot-swap one disease model for a crop."""
        if crop not in CROP_SPECS:
            raise PredictionError(f"Unsupported crop: {crop}")
        if environment_model_override(crop):
            raise PredictionError(
                f"{crop.title()} model selection is locked by an AGROVISION_{crop.upper()}_MODEL environment override."
            )
        if not filename or filename != Path(filename).name:
            raise PredictionError("Model filename must be a plain file name.")
        if not filename.lower().endswith(".keras") or not filename.lower().startswith(crop.lower()):
            raise PredictionError(f"Select a {crop} .keras model file.")

        path = (MODELS_DIR / filename).resolve()
        try:
            path.relative_to(MODELS_DIR)
        except ValueError as exc:
            raise PredictionError("Invalid model path.") from exc
        if not path.is_file():
            raise ModelNotAvailableError(f"Model file not found: {filename}")

        # Load and validate first. The existing active model remains untouched if this fails.
        loaded = self._load_model(crop, path)
        try:
            persist_model_selection(crop, filename)
        except OSError as exc:
            raise PredictionError("The active-model selection could not be saved.") from exc

        with self._load_lock:
            self._models[crop] = loaded
            self._errors.pop(crop, None)

        for item in self.managed_models():
            if item["crop"] == crop and item["filename"] == filename:
                return item
        raise PredictionError("Model activated but registry status could not be refreshed.")

    @staticmethod
    def _version_from_filename(filename: str) -> str:
        import re
        match = re.search(r"(?:^|_)v(\d+)(?:_|\.|$)", filename, flags=re.IGNORECASE)
        return f"v{match.group(1)}" if match else "unversioned"

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

    def explain(self, crop: str, image: Image.Image, class_label: str) -> dict[str, Any]:
        """Generate a Grad-CAM overlay for the requested disease class.

        Explainability is intentionally separate from ``predict`` so a failed heatmap
        never changes or blocks the actual disease prediction. The caller can safely
        treat this as optional diagnostic output.
        """
        if crop not in CROP_SPECS:
            raise PredictionError(f"Unsupported crop: {crop}")

        spec = CROP_SPECS[crop]
        try:
            class_index = spec.classes.index(class_label)
        except ValueError as exc:
            raise PredictionError(
                f"Cannot explain unknown {crop} class '{class_label}'."
            ) from exc

        loaded = self._get_or_load(crop)
        resized = image.resize((loaded.input_width, loaded.input_height), Image.Resampling.BILINEAR)
        array = np.asarray(resized, dtype=np.float32)
        batch = np.expand_dims(self._preprocess(array, loaded.preprocessing), axis=0)

        try:
            os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
            import tensorflow as tf

            target_layer = self._find_gradcam_layer(loaded.model)
            if target_layer is None:
                raise PredictionError(
                    f"No convolutional feature layer was found in {loaded.path.name}."
                )

            with loaded.lock:
                grad_model = tf.keras.Model(
                    inputs=loaded.model.inputs,
                    outputs=[target_layer.output, loaded.model.output],
                )

                with tf.GradientTape() as tape:
                    convolution_output, model_output = grad_model(batch, training=False)
                    model_output = self._single_tensor_output(model_output)
                    if model_output.shape.rank != 2:
                        raise PredictionError(
                            "Grad-CAM requires a single 2-D classification output."
                        )
                    class_score = model_output[:, class_index]

                gradients = tape.gradient(class_score, convolution_output)

            if gradients is None:
                raise PredictionError("Grad-CAM gradients could not be calculated.")

            convolution_output = convolution_output[0]
            pooled_gradients = tf.reduce_mean(gradients, axis=(0, 1, 2))
            heatmap = tf.reduce_sum(convolution_output * pooled_gradients, axis=-1)
            heatmap = tf.nn.relu(heatmap)
            maximum = tf.reduce_max(heatmap)
            if not bool(tf.math.is_finite(maximum).numpy()) or float(maximum.numpy()) <= 0.0:
                raise PredictionError("Grad-CAM produced an empty activation map.")
            heatmap = heatmap / maximum
            heatmap_array = np.asarray(heatmap.numpy(), dtype=np.float32)

            encoded, media_type = self._render_gradcam_overlay(image, heatmap_array)
            return {
                "method": "Grad-CAM",
                "layer_name": str(getattr(target_layer, "name", "feature_layer")),
                "image_base64": encoded,
                "image_media_type": media_type,
            }
        except PredictionError:
            raise
        except Exception as exc:
            raise PredictionError(
                f"Grad-CAM explanation could not be generated for {crop}."
            ) from exc

    @staticmethod
    def _single_tensor_output(output: Any) -> Any:
        if isinstance(output, dict):
            if len(output) != 1:
                raise PredictionError("Grad-CAM requires one model output.")
            return next(iter(output.values()))
        if isinstance(output, (list, tuple)):
            if len(output) != 1:
                raise PredictionError("Grad-CAM requires one model output.")
            return output[0]
        return output

    @staticmethod
    def _layer_output_rank(layer: Any) -> int | None:
        try:
            shape = getattr(layer.output, "shape", None)
            if shape is None:
                return None
            return len(shape)
        except Exception:
            return None

    @classmethod
    def _find_gradcam_layer(cls, model: Any) -> Any | None:
        """Find a late spatial feature layer that is connected to the classifier.

        Custom CNNs expose Conv2D layers directly. The Rice EfficientNet model is a
        nested Functional layer whose output is still a 4-D feature map, so the
        fallback intentionally supports a top-level nested model as well.
        """
        layers = list(getattr(model, "layers", []))
        convolution_names = {
            "conv2d",
            "separableconv2d",
            "depthwiseconv2d",
            "conv2dtranspose",
        }

        for layer in reversed(layers):
            if (
                layer.__class__.__name__.lower() in convolution_names
                and cls._layer_output_rank(layer) == 4
            ):
                return layer

        for layer in reversed(layers):
            if cls._layer_output_rank(layer) == 4:
                return layer

        return None

    @staticmethod
    def _render_gradcam_overlay(image: Image.Image, heatmap: np.ndarray) -> tuple[str, str]:
        """Blend a compact yellow-to-red attention map over the original image."""
        original = ImageOps.exif_transpose(image).convert("RGB")
        max_side = 900
        if max(original.size) > max_side:
            scale = max_side / max(original.size)
            original = original.resize(
                (max(1, round(original.width * scale)), max(1, round(original.height * scale))),
                Image.Resampling.LANCZOS,
            )

        clipped = np.clip(heatmap, 0.0, 1.0)
        heatmap_image = Image.fromarray((clipped * 255.0).astype(np.uint8), mode="L")
        heatmap_image = heatmap_image.resize(original.size, Image.Resampling.BILINEAR)
        h = np.asarray(heatmap_image, dtype=np.float32) / 255.0

        # Transparent at low activation, yellow at medium activation, red at high activation.
        red = np.full_like(h, 255.0)
        green = 255.0 * (1.0 - h)
        blue = np.zeros_like(h)
        alpha = 165.0 * np.power(h, 0.85)
        rgba = np.stack([red, green, blue, alpha], axis=-1).astype(np.uint8)

        overlay = Image.fromarray(rgba, mode="RGBA")
        blended = Image.alpha_composite(original.convert("RGBA"), overlay).convert("RGB")

        buffer = io.BytesIO()
        blended.save(buffer, format="JPEG", quality=88, optimize=True)
        return base64.b64encode(buffer.getvalue()).decode("ascii"), "image/jpeg"

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
                loaded = self._load_model(crop, path)
                self._models[crop] = loaded
                self._errors.pop(crop, None)
                return loaded
            except Exception as exc:
                error = f"Failed to load {path.name}: {type(exc).__name__}: {exc}"
                self._errors[crop] = error
                raise ModelNotAvailableError(error) from exc

    def _load_model(self, crop: str, path: Path) -> LoadedModel:
        os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
        import tensorflow as tf

        model = tf.keras.models.load_model(path, compile=False)
        input_height, input_width = self._input_size(model)
        mode = self._resolve_preprocessing(crop, model)

        # Basic compatibility validation: model output count must match configured classes.
        output_shape = model.output_shape
        if isinstance(output_shape, list):
            if len(output_shape) != 1:
                raise ValueError("Only one model output is supported.")
            output_shape = output_shape[0]
        if len(output_shape) < 2 or output_shape[-1] != len(CROP_SPECS[crop].classes):
            raise ValueError(
                f"Expected {len(CROP_SPECS[crop].classes)} output classes for {crop}, received {output_shape}."
            )

        return LoadedModel(
            model=model,
            path=path,
            input_height=input_height,
            input_width=input_width,
            preprocessing=mode,
            lock=threading.Lock(),
        )

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

