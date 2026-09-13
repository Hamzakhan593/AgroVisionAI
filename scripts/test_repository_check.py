"""Exercise repository checks using disposable Git indexes, never the user's index."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


CHECKER = Path(__file__).with_name("check_repository.py")


class RepositoryCheckTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.git("init", "-q")

    def git(self, *args):
        subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)

    def write(self, name, body):
        target = self.root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(body, encoding="utf-8")

    def check(self, *args):
        return subprocess.run([sys.executable, str(CHECKER), *args], cwd=self.root,
                              capture_output=True, text=True)

    def test_public_assets_and_empty_examples_pass(self):
        self.write("appsettings.Local.example.json", json.dumps({"Jwt": {"Key": ""}}))
        self.write("AgroVisionAI/App_Data/TreatmentPk/catalog.json", "{}")
        self.write("AgroVisionAI/AIService/models/classes.json", "{}")
        self.write("AgroVisionAI/App_Data/CropImages/.gitkeep", "")
        self.write(".env.example", "JWT_" + "KEY=\n")
        self.assertEqual(self.check().returncode, 0)

    def test_uploads_and_model_weights_fail(self):
        self.write("AgroVisionAI/App_Data/CropImages/private.jpg", "image")
        self.write("models/model.onnx", "weights")
        result = self.check()
        self.assertEqual(result.returncode, 1)
        self.assertIn("private/runtime", result.stdout)

    def test_populated_config_fails_without_printing_value(self):
        value = "test-fixture-credential"
        self.write("appsettings.json", json.dumps({"IdentitySeed": {"Password": value}}))
        result = self.check()
        self.assertEqual(result.returncode, 1)
        self.assertNotIn(value, result.stdout)

    def test_staged_scan_detects_secret_even_after_working_copy_is_fixed(self):
        self.write("appsettings.json", json.dumps({"Jwt": {"Key": "fixture-value"}}))
        self.git("add", "appsettings.json")
        self.write("appsettings.json", json.dumps({"Jwt": {"Key": ""}}))
        self.assertEqual(self.check().returncode, 0)
        self.assertEqual(self.check("--staged").returncode, 1)

    def test_forced_tracked_ignored_file_fails(self):
        self.write(".gitignore", "local-notes.txt\n")
        self.write("local-notes.txt", "private")
        self.git("add", "-f", "local-notes.txt")
        self.assertIn("tracked despite .gitignore", self.check().stdout)


if __name__ == "__main__":
    unittest.main()
