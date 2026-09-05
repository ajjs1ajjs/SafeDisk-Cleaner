@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "SRC="
for %%F in ("%~dp0SafeDiskCleaner-*-portable-win64.exe") do set "SRC=%%~fF"
if not defined SRC (
    echo SafeDiskCleaner portable exe not found next to run.cmd
    exit /b 1
)
set "DST=%LOCALAPPDATA%\SafeDisk\app\SafeDiskCleaner.exe"
set "MARK=%LOCALAPPDATA%\SafeDisk\app\.version"
for %%F in ("%SRC%") do set "SRC_DATE=%%~tF" & set "SRC_SIZE=%%~zF"
if exist "%DST%" if exist "%MARK%" (
    set /p OLD=<"%MARK%"
    if "!OLD!"=="%SRC_DATE%~%SRC_SIZE%" goto :run
)
mkdir "%LOCALAPPDATA%\SafeDisk\app" 2>nul
copy /y "%SRC%" "%DST%" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -Path '%DST%' -ErrorAction SilentlyContinue"
echo %SRC_DATE%~%SRC_SIZE%>"%MARK%"
:run
start "" "%DST%"
endlocal
