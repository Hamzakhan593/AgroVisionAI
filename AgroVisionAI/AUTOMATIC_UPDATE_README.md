# AgroVisionAI automatic crop routing update

This is an OVERLAY, not a complete project. Copy its contents into the existing project root (the folder containing AgroVisionAI.csproj). Do not run it as a new standalone project.

## Install using File Explorer

1. Stop Visual Studio debugging and close the running API with Ctrl+C.
2. Back up the existing project code before replacing files.
3. Extract this ZIP into a separate folder. Open the extracted AgroVisionAI folder.
4. Copy everything INSIDE that folder into your existing AgroVisionAI project root. Choose **Replace the files in the destination**. Do not create AgroVisionAI/AgroVisionAI by mistake.
5. In the existing AIService/models folder, keep the three disease models and add BOTH original training outputs:
   - crop_validator_v2_best.keras
   - crop_validator_v2_config.json
   Copy these from D:\AgroVissionAI-ML-copy\models using File Explorer. Do not rename a V1 model to V2, or construct a new config by hand.
6. Run your existing AIService/start_api.bat. Open http://127.0.0.1:8000/health: crop_validator, cotton, wheat and rice should all have loaded=true. If not, inspect the error field.
7. Build/run the website in Visual Studio. Refresh the browser with Ctrl+F5 so new CSS/JS loads.

No training, environment recreation, package reinstall, or database migration is required for the app update. Existing appsettings, models, .venv, App_Data, uploads, database, migrations and user history are NOT included or overwritten by this archive. Keep the existing project folders; merge files rather than deleting the project first.

## What changed

- Scan: no manual crop selection; native file picker, actual drag/drop, size/type checks, image preview, keyboard access, progress feedback and duplicate-submit protection.
- API: V2 validator is mandatory, reads original class order, image size, preprocessing and confidence threshold. Unsupported/uncertain outputs are rejected. Successful predictions route to the detected crop's disease model. Legacy crop routes still validate and reject mismatch.
- Missing or incompatible validator/config fails closed; no silent disease-only fallback. Health includes all four models.
- .NET: sends only the image and saves the crop returned by the API. Checks that a crop-validation result is present. Unsupported images are not saved as successful diagnoses.
- Result/history: disease confidence is labelled explicitly; stored historical crops are left unchanged. Crop confidence is returned separately by the API, but is not persisted in the existing database schema.
- Home: supported disease lists now match the actual model classes. Unsupported accuracy/speed/severity claims removed; example result labelled illustrative.
- Dashboard: automatic workflow text; fixed broken date expression; accessible result link. Crop cards remain informational, not selection inputs.
- Shared UI: dashboard navigation, active-page links, skip link, focus states, responsive refinements, reduced-motion support, broken footer links removed.
- Account/profile: autocomplete and accessible validation/success announcements. Data handling page replaces the template placeholder.

## Checks performed and limits

Python unit tests cover automatic routing of all three crops, unsupported/uncertain rejection, threshold boundary, legacy mismatch, missing validator, invalid/empty/oversized uploads, saved class order, raw pixel input range, invalid configuration and existing disease catalog/registry behavior. Models are mocked in these tests.

JavaScript syntax and Python compilation checked. This environment has no .NET SDK, SQL Server or your trained model weights: a real .NET build, rendered Razor UI review and end-to-end model inference must be checked on your Windows computer. No accuracy guarantee is implied by these code tests.

Optional developer tests: inside AIService, with httpx installed, run python -m unittest discover -s tests -v. httpx is only needed for these tests, not application startup.

## Local smoke-test checklist

- Sign in/register; open Dashboard, History and Profile on desktop and a narrow/mobile screen.
- Upload a known Cotton, Wheat and Rice leaf. Confirm crop and condition are saved and the history image opens.
- Try other leaves, random objects, blurry images and a blank/corrupt file. Check rejected predictions show a readable error. A classifier may still confidently accept some unsuitable images; record those failures.
- Stop the API and submit a leaf: a helpful error should appear, with no fabricated result.
- Test drag/drop, remove/reselect image, Back navigation, sign-out, and access to a different user's result.
- Confirm /health shows all four models loaded after a restart.

## Important model limitations

The saved 0.40 crop threshold remains provisional; it is not proof of safe deployment. Higher confidence alone does not solve confident mistakes. The V2 result of 112/120 is a small held-out test, not a guarantee for arbitrary field images. Leaf-only suitability is not a separate trained gate: grain heads, flowers and non-leaf images can still be misclassified. Keep human confirmation in the workflow, especially before treatment.

For rollback, stop both applications and restore the backed-up code. No schema rollback is needed.
