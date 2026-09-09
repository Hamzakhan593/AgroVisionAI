# AgroVisionAI model API — start here

The website and Python model API are now connected.

1. Open `AIService\models` and paste the three final `.keras` models there.
2. Read `AIService\models\README.md` and confirm each filename and class order.
3. Double-click `AIService\setup_api.bat` once.
4. Double-click `AIService\start_api.bat` whenever you run the project.
5. Wait until Cotton, Wheat and Rice all show `READY`.
6. Start the ASP.NET Core project in Visual Studio.
7. Upload a leaf image from **Scan Your Crop**. The result, confidence and treatment guidance
   are saved in detection history.

If the website reports that the AI service is unavailable, first open
`http://127.0.0.1:8000/health` and check which model is not loaded.

