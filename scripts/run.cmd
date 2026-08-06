@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "SRC=%~dp0SafeDiskCleaner-0.4.0-portable-win64.exe"
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
