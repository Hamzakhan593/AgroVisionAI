# AgroVisionAI FastAPI service

This local Python service loads the trained Cotton, Wheat and Rice Keras models and exposes
them to the ASP.NET Core MVC website.

## 1. Add the final models

Place the final files in `AIService\models` using these preferred names:

- `crop_validator_v2_best.keras` and the matching `crop_validator_v2_config.json`
- `cotton_cnn_v3_best.keras`
- `wheat_v2_efficientnetv2b0_best.keras`
- `rice_v2_efficientnetv2b0_best.keras`

Alternative filenames and environment-variable overrides are documented in
`models\README.md`. Trained model binaries are distributed separately from Git. The
repository contains only their configuration and class labels.

## 2. One-time setup on Windows

Double-click `setup_api.bat`. It creates `AIService\.venv` with Python 3.11 and installs the
required packages. Internet is needed only for this one-time package installation.

Manual equivalent:

```powershell
cd AIService
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

## 3. Start the API

Double-click `start_api.bat` and keep its window open. The startup script checks the
three disease models and serves the API at `http://127.0.0.1:8000`. Then check `/health`
to confirm all four model entries are loaded, including the mandatory crop validator.
Configure private settings using the root README before starting the web application.

Useful URLs:

- Health: `http://127.0.0.1:8000/health`
- Swagger test page: `http://127.0.0.1:8000/docs`
- Models: `http://127.0.0.1:8000/api/v1/models`

## 4. Run the website

Start the ASP.NET Core project from Visual Studio after the API says all models are ready.
The website is already configured to call `http://127.0.0.1:8000` through
`AgroVisionApi:BaseUrl` in `appsettings.json`.

## Prediction request

```http
POST /api/v1/predict/cotton
Content-Type: multipart/form-data
file=<leaf image>
```

The response includes the predicted class, confidence, top three probabilities, disease
description, symptoms, treatment and prevention guidance.

## Preprocessing behavior

The API reads the image size directly from each model. In `auto` mode it keeps 0-255 pixels
when the model contains a `Rescaling` layer or an EfficientNet base; otherwise it divides
pixels by 255 for a custom CNN. Override only if the training pipeline used something else:

```bat
set AGROVISION_COTTON_PREPROCESSING=zero_one
set AGROVISION_WHEAT_PREPROCESSING=none
set AGROVISION_RICE_PREPROCESSING=none
```

Allowed values are `auto`, `none`, `zero_one`, and `minus_one_one`.

## Important class-order check

Prediction labels are correct only when `classes` in `app\config.py` have the exact same
order used during training. The included order matches alphabetically named training
folders. If any training script used a manual order, update that one tuple before the final
demo.
