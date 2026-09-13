import base64
import io
import unittest
import threading
import tempfile
from pathlib import Path
from unittest.mock import patch

import numpy as np
from PIL import Image

from app.model_registry import LoadedModel, ModelRegistry


class FakeModel:
    def predict(self, batch, verbose=0):
        assert batch.shape == (1, 32, 32, 3)
        assert verbose == 0
        return np.array([[0.05, 0.15, 0.80]], dtype=np.float32)


class ModelRegistryTests(unittest.TestCase):
    def test_softmax_converts_logits_to_probabilities(self):
        probabilities = ModelRegistry._to_probabilities(np.array([1.0, 2.0, 3.0]))
        self.assertAlmostEqual(float(probabilities.sum()), 1.0, places=6)
        self.assertEqual(int(np.argmax(probabilities)), 2)

    def test_existing_probabilities_are_preserved(self):
        values = np.array([0.1, 0.2, 0.7])
        probabilities = ModelRegistry._to_probabilities(values)
        np.testing.assert_allclose(probabilities, values)

    def test_zero_one_preprocessing(self):
        image = np.array([0.0, 127.5, 255.0], dtype=np.float32)
        processed = ModelRegistry._preprocess(image, "zero_one")
        np.testing.assert_allclose(processed, [0.0, 0.5, 1.0])

    def test_prediction_maps_output_to_configured_class_order(self):
        registry = ModelRegistry()
        registry._models["cotton"] = LoadedModel(
            model=FakeModel(),
            path=Path("cotton_cnn_v3_best.keras"),
            input_height=32,
            input_width=32,
            preprocessing="zero_one",
            lock=threading.Lock(),
        )

        image = Image.new("RGB", (80, 60), color=(20, 100, 40))
        prediction = registry.predict("cotton", image)

        self.assertEqual(prediction["label"], "healthy")
        self.assertAlmostEqual(prediction["confidence"], 0.8, places=5)
        self.assertEqual(prediction["model_name"], "cotton_cnn_v3_best.keras")


    def test_gradcam_overlay_returns_compact_jpeg(self):
        image = Image.new("RGB", (120, 80), color=(35, 110, 45))
        heatmap = np.zeros((8, 8), dtype=np.float32)
        heatmap[2:6, 3:7] = 1.0

        encoded, media_type = ModelRegistry._render_gradcam_overlay(image, heatmap)
        decoded = base64.b64decode(encoded)

        self.assertEqual(media_type, "image/jpeg")
        self.assertTrue(decoded.startswith(b"\xff\xd8\xff"))
        with Image.open(io.BytesIO(decoded)) as rendered:
            self.assertEqual(rendered.size, image.size)

    def test_gradcam_layer_prefers_late_convolutional_layer(self):
        class TensorLike:
            shape = (None, 14, 14, 128)

        Conv2D = type("Conv2D", (), {})
        Dense = type("Dense", (), {})
        first_conv = Conv2D()
        first_conv.output = TensorLike()
        first_conv.name = "early_conv"
        last_conv = Conv2D()
        last_conv.output = TensorLike()
        last_conv.name = "late_conv"
        dense = Dense()
        dense.output = type("DenseTensor", (), {"shape": (None, 3)})()
        dense.name = "prediction"
        model = type("FakeGradCamModel", (), {"layers": [first_conv, last_conv, dense]})()

        selected = ModelRegistry._find_gradcam_layer(model)

        self.assertIs(selected, last_conv)

    def test_model_version_is_read_from_filename(self):
        self.assertEqual(ModelRegistry._version_from_filename("wheat_cnn_v2_best.keras"), "v2")
        self.assertEqual(ModelRegistry._version_from_filename("cotton_final.keras"), "unversioned")

    def test_managed_models_discovers_active_candidate(self):
        import app.config as config
        import app.model_registry as model_registry_module

        with tempfile.TemporaryDirectory() as directory:
            models_dir = Path(directory)
            (models_dir / "cotton_cnn_v3_best.keras").touch()
            (models_dir / "cotton_cnn_v4_candidate.keras").touch()
            selection_file = models_dir / "active_models.json"
            selection_file.write_text('{"cotton":"cotton_cnn_v4_candidate.keras"}', encoding="utf-8")

            with patch.object(config, "MODELS_DIR", models_dir), \
                 patch.object(config, "MODEL_SELECTION_FILE", selection_file), \
                 patch.object(model_registry_module, "MODELS_DIR", models_dir):
                registry = ModelRegistry()
                items = [item for item in registry.managed_models() if item["crop"] == "cotton"]

            self.assertEqual(len(items), 2)
            active = next(item for item in items if item["active"])
            self.assertEqual(active["filename"], "cotton_cnn_v4_candidate.keras")
            self.assertEqual(active["version"], "v4")


if __name__ == "__main__":
    unittest.main()
