"""Mandatory V2 crop gate. Uses the class order and threshold shipped with training."""
from __future__ import annotations

import json
import math
import threading
import numpy as np
from PIL import Image

from .config import MODELS_DIR
from .model_registry import LoadedModel, ModelRegistry, ModelNotAvailableError, PredictionError


class CropValidator:
    def __init__(self):
        self.loaded = None
        self.classes = []
        self.threshold = None
        self.error = None
        self._lock = threading.Lock()

    def load(self):
        if self.loaded is not None:
            return self.loaded
        with self._lock:
            if self.loaded is not None:
                return self.loaded
            try:
                config_path = MODELS_DIR / "crop_validator_v2_config.json"
                config = json.loads(config_path.read_text(encoding="utf-8-sig"))
                classes = config["class_names"]
                if not isinstance(classes, list) or len(classes) != 4 or set(classes) != {"cotton", "wheat", "rice", "out_of_scope"}:
                    raise ValueError("Expected four unique crop class_names in saved order.")
                if config.get("model_filename") != "crop_validator_v2_best.keras":
                    raise ValueError("Config must reference crop_validator_v2_best.keras.")
                if config.get("input_pixel_range") != [0, 255] or config.get("preprocessing") != "built_into_saved_mobilenetv3_model":
                    raise ValueError("Expected V2 built-in preprocessing and [0, 255] input range.")
                routing = config["routing"]
                threshold = float(routing["minimum_confidence"])
                if not math.isfinite(threshold) or not 0 < threshold <= 1:
                    raise ValueError("Invalid minimum_confidence.")
                if routing.get("out_of_scope_class") != "out_of_scope":
                    raise ValueError("Invalid out_of_scope_class.")
                import tensorflow as tf
                path = MODELS_DIR / "crop_validator_v2_best.keras"
                model = tf.keras.models.load_model(path, compile=False)
                height, width = ModelRegistry._input_size(model)
                if config.get("image_size") != [height, width] or model.output_shape[-1] != 4:
                    raise ValueError("Saved model dimensions do not match its config.")
                self.classes = classes
                self.threshold = threshold
                self.loaded = LoadedModel(model, path, height, width, "none", threading.Lock())
                self.error = None
                return self.loaded
            except Exception as exc:
                self.error = f"Crop validator unavailable: {exc}"
                raise ModelNotAvailableError(
                    "Crop validator could not load. Copy crop_validator_v2_best.keras and its original "
                    "crop_validator_v2_config.json into AIService/models, then restart the API. "
                    "Check /health for configuration details."
                ) from exc

    def status(self):
        return {"crop": "crop_validator", "available": all((MODELS_DIR / name).is_file() for name in
                ("crop_validator_v2_best.keras", "crop_validator_v2_config.json")),
                "loaded": self.loaded is not None, "filename": "crop_validator_v2_best.keras",
                "classes": self.classes, "preprocessing": "none", "error": self.error}

    def predict(self, image: Image.Image):
        loaded = self.load()
        array = np.asarray(image.resize((loaded.input_width, loaded.input_height), Image.Resampling.BILINEAR), dtype=np.float32)
        try:
            with loaded.lock:
                output = loaded.model.predict(array[None, ...], verbose=0)
        except Exception as exc:
            raise PredictionError("Crop validation failed. Try another clear leaf photograph.") from exc
        scores = ModelRegistry._extract_scores(output)
        if scores.size != 4:
            raise PredictionError("Crop validator output must contain four classes.")
        probabilities = ModelRegistry._to_probabilities(scores)
        index = int(np.argmax(probabilities))
        return {"label": self.classes[index], "confidence": float(probabilities[index]),
                "threshold": self.threshold, "model_name": loaded.path.name}


validator = CropValidator()
