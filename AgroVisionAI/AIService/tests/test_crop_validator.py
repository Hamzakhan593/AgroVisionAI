import json
import threading
import unittest
from pathlib import Path
from unittest.mock import patch, Mock

import numpy as np
from PIL import Image

from app.crop_validator import CropValidator
from app.model_registry import LoadedModel, ModelNotAvailableError, PredictionError


class CropValidatorTests(unittest.TestCase):
    def test_saved_class_order_and_raw_pixel_range(self):
        gate = CropValidator()
        model = Mock()
        model.predict.return_value = np.array([[.9, .03, .04, .03]])
        gate.loaded = LoadedModel(model, Path("validator.keras"), 224, 224, "none", threading.Lock())
        gate.classes = ["rice", "cotton", "wheat", "out_of_scope"]
        gate.threshold = .4
        prediction = gate.predict(Image.new("RGB", (80, 80), (255, 128, 0)))
        self.assertEqual(prediction["label"], "rice")
        batch = model.predict.call_args.args[0]
        self.assertEqual(batch.shape, (1, 224, 224, 3))
        self.assertEqual(batch.dtype, np.float32)
        np.testing.assert_array_equal(batch[0, 0, 0], [255, 128, 0])

    def test_nonfinite_output_is_rejected(self):
        gate = CropValidator()
        model = Mock()
        model.predict.return_value = [[float("nan"), 0, 0, 1]]
        gate.loaded = LoadedModel(model, Path("validator.keras"), 32, 32, "none", threading.Lock())
        with self.assertRaises(PredictionError):
            gate.predict(Image.new("RGB", (32, 32)))

    def test_missing_config_is_actionable(self):
        with patch("pathlib.Path.read_text", side_effect=FileNotFoundError("config missing")):
            gate = CropValidator()
            with self.assertRaisesRegex(ModelNotAvailableError, "original"):
                gate.load()
            self.assertFalse(gate.status()["loaded"])

    def test_bad_class_config_fails_before_tensorflow_import(self):
        with patch("pathlib.Path.read_text", return_value=json.dumps({"class_names": ["cotton"] * 4})):
            gate = CropValidator()
            with self.assertRaises(ModelNotAvailableError):
                gate.load()
            self.assertIn("four unique", gate.error)

    def test_original_training_config_loads_with_fake_model(self):
        config = {"class_names": ["cotton", "wheat", "rice", "out_of_scope"],
                  "model_filename": "crop_validator_v2_best.keras", "image_size": [224, 224],
                  "input_pixel_range": [0, 255], "preprocessing": "built_into_saved_mobilenetv3_model",
                  "routing": {"out_of_scope_class": "out_of_scope", "minimum_confidence": .4}}
        tf = Mock()
        tf.keras.models.load_model.return_value.input_shape = (None, 224, 224, 3)
        tf.keras.models.load_model.return_value.output_shape = (None, 4)
        with patch("pathlib.Path.read_text", return_value=json.dumps(config)), patch.dict("sys.modules", {"tensorflow": tf}):
            gate = CropValidator()
            gate.load()
            self.assertTrue(gate.status()["loaded"])
            self.assertEqual(gate.threshold, .4)
            self.assertEqual(gate.classes, config["class_names"])


if __name__ == "__main__":
    unittest.main()
