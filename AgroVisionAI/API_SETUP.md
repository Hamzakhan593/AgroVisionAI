# AgroVisionAI model API — start here

The website and Python model API are now connected.

For a fresh clone, first follow the root `README.md` to configure the database,
JWT key and optional administrator in the ignored `appsettings.Local.json`.
`start_api.bat` reads the same local model-management key as the web application.

1. Open `AIService\models` and add the three disease `.keras` models plus `crop_validator_v2_best.keras` and its matching JSON configuration.
2. Read `AIService\models\README.md` and confirm each filename and class order.
3. Double-click `AIService\setup_api.bat` once.
4. Double-click `AIService\start_api.bat` whenever you run the project.
5. Confirm `/health` reports all four model entries loaded, including `crop_validator`.
6. Start the ASP.NET Core project in Visual Studio.
7. Upload a leaf image from **Scan Your Crop**. The result, confidence and treatment guidance
   are saved in detection history.

If the website reports that the AI service is unavailable, first open
`http://127.0.0.1:8000/health` and check which model is not loaded.
