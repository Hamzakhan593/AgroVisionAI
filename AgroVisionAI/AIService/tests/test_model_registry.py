import unittest
import threading
from pathlib import Path

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


if __name__ == "__main__":
    unittest.main()
