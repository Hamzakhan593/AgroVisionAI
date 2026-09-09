import io
import unittest
from unittest.mock import patch

from fastapi.testclient import TestClient
from PIL import Image

from app.main import app
from app.model_registry import ModelNotAvailableError, PredictionError


class AutomaticRoutingTests(unittest.TestCase):
    def setUp(self):
        self.client = TestClient(app)  # No lifespan: tests use fake predictions, not weights.
        data = io.BytesIO()
        Image.new("RGB", (64, 64), (20, 100, 30)).save(data, format="PNG")
        self.image = data.getvalue()

    def request(self, label="cotton", confidence=.9, endpoint="/api/v1/predict", data=None):
        crop = {"label": label, "confidence": confidence, "threshold": .4, "model_name": "validator.keras"}
        disease = {"label": "healthy", "confidence": .8, "model_name": "disease.keras",
                   "top_predictions": [{"label": "healthy", "confidence": .8}]}
        with patch("app.main.validator.predict", return_value=crop), patch("app.main.registry.predict", return_value=disease) as routed:
            response = self.client.post(endpoint, data=data, files={"file": ("leaf.png", self.image, "image/png")})
            return response, routed

    def test_auto_routes_all_three_crops_without_crop_field(self):
        for crop in ("cotton", "wheat", "rice"):
            response, routed = self.request(crop)
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.json()["crop"], crop.title())
            self.assertEqual(response.json()["crop_confidence"], .9)
            self.assertEqual(routed.call_args.args[0], crop)

    def test_unsupported_never_reaches_disease(self):
        response, routed = self.request("out_of_scope", .999)
        self.assertEqual(response.status_code, 422)
        routed.assert_not_called()

    def test_uncertain_never_reaches_disease(self):
        response, routed = self.request("cotton", .39)
        self.assertEqual(response.status_code, 422)
        routed.assert_not_called()

    def test_threshold_boundary(self):
        response, _ = self.request("cotton", .4)
        self.assertEqual(response.status_code, 200)

    def test_legacy_path_cannot_bypass_validator(self):
        response, routed = self.request("cotton", endpoint="/api/v1/predict/wheat")
        self.assertEqual(response.status_code, 422)
        routed.assert_not_called()

    def test_legacy_form_cannot_bypass_validator(self):
        response, routed = self.request("cotton", data={"crop": "rice"})
        self.assertEqual(response.status_code, 422)
        routed.assert_not_called()

    def test_missing_validator_fails_closed(self):
        with patch("app.main.validator.predict", side_effect=ModelNotAvailableError("missing")), patch("app.main.registry.predict") as disease:
            response = self.client.post("/api/v1/predict", files={"file": ("leaf.png", self.image)})
            self.assertEqual(response.status_code, 503)
            disease.assert_not_called()

    def test_prediction_failure_fails_closed(self):
        with patch("app.main.validator.predict", side_effect=PredictionError("invalid")), patch("app.main.registry.predict") as disease:
            response = self.client.post("/api/v1/predict", files={"file": ("leaf.png", self.image)})
            self.assertEqual(response.status_code, 500)
            disease.assert_not_called()

    def test_invalid_and_empty_uploads(self):
        with patch("app.main.validator.predict") as gate:
            for data in (b"", b"not an image"):
                response = self.client.post("/api/v1/predict", files={"file": ("leaf.png", data)})
                self.assertEqual(response.status_code, 400)
            gate.assert_not_called()

    def test_oversized_upload(self):
        response = self.client.post("/api/v1/predict", files={"file": ("leaf.png", b"a" * (5 * 1024 * 1024 + 1))})
        self.assertEqual(response.status_code, 413)


if __name__ == "__main__":
    unittest.main()
