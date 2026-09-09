@echo off
setlocal
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo [ERROR] API environment is missing. Run setup_api.bat first.
    pause
    exit /b 1
)

call ".venv\Scripts\python.exe" verify_setup.py
if errorlevel 1 (
    pause
    exit /b 1
)

echo.
echo Starting AgroVisionAI API at http://127.0.0.1:8000
echo Swagger documentation: http://127.0.0.1:8000/docs
echo Keep this window open while using the website.
echo.
call ".venv\Scripts\python.exe" -m uvicorn app.main:app --host 127.0.0.1 --port 8000

