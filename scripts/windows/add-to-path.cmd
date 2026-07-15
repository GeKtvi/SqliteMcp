@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0add-to-path.ps1" %*
if errorlevel 1 (
  echo.
  pause
  exit /b 1
)
echo.
pause
