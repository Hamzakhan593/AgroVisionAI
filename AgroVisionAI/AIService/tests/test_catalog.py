import unittest

from app.config import CROP_SPECS
from app.disease_info import disease_info


class DiseaseCatalogTests(unittest.TestCase):
    def test_every_model_class_has_catalog_information(self):
        for crop, spec in CROP_SPECS.items():
            for label in spec.classes:
                entry = disease_info(crop, label)
                self.assertTrue(entry["name"])
                self.assertTrue(entry["description"])
                self.assertTrue(entry["treatment"])
                self.assertTrue(entry["prevention"])


if __name__ == "__main__":
    unittest.main()

