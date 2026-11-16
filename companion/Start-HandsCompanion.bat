@echo off
rem One-click launcher for the Hands companion.
rem Drag-and-drop a profile JSON onto this file, or put a profile.json
rem next to it. The window stays open and everything is also written to
rem hands-companion.log. Stop with Ctrl+C or Ctrl+Alt+F12.
setlocal
cd /d "%~dp0"
if not exist "hands-companion.exe" (
  echo hands-companion.exe has not been built yet.
  echo Run Build.bat first - it uses the compiler that ships with Windows.
  pause
  exit /b 1
)
set PROFILE=%~1
if "%PROFILE%"=="" set PROFILE=profile.json
if not exist "%PROFILE%" (
  echo Profile not found: %PROFILE%
  echo Export a profile from the Hands studio, then either drag-and-drop
  echo the JSON file onto this .bat, or save it as profile.json here.
  pause
  exit /b 1
)
echo Starting the Hands companion
echo   profile: %PROFILE%
echo   log:     hands-companion.log
echo.
hands-companion.exe "%PROFILE%"
set RC=%ERRORLEVEL%
echo.
echo Companion exited with code %RC%.
pause
endlocal
