@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Python launcher was not found. Install Python 3.11 first.
    pause
    exit /b 1
)

if not exist ".venv\Scripts\python.exe" (
    echo Creating Python 3.11 virtual environment...
    py -3.11 -m venv .venv
    if errorlevel 1 goto :failed
)

echo Installing API dependencies...
call ".venv\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 goto :failed
call ".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 goto :failed

echo.
echo API environment is ready.
echo Next: copy the three .keras files into AIService\models.
pause
exit /b 0

:failed
echo.
echo [ERROR] API setup failed. Read AIService\README.md for manual steps.
pause
exit /b 1

