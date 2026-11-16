@echo off
rem Builds hands-companion.exe from src\*.cs using the C# compiler that
rem ships with Windows itself (.NET Framework csc.exe). No downloads,
rem no installs. Call with "nopause" from CI to skip the final pause.
setlocal
cd /d "%~dp0"
set CSC=
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not defined CSC (
  echo [ERROR] csc.exe was not found on this PC.
  echo The .NET Framework compiler is part of Windows. If it is missing,
  echo turn on ".NET Framework 4.x" under Windows Features, then retry.
  goto :end
)
echo Building hands-companion.exe
echo   compiler: %CSC%
"%CSC%" /nologo /optimize+ /target:exe /out:hands-companion.exe /reference:System.dll /reference:System.Core.dll src\*.cs
if errorlevel 1 (
  echo [ERROR] Build failed. The compiler messages above say why.
  goto :end
)
echo Build OK: hands-companion.exe
:end
if not "%~1"=="nopause" pause
endlocal
