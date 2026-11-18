@echo off
setlocal

where ffmpeg >nul 2>nul
if errorlevel 1 (
    echo ExtendedScreen needs ffmpeg, which isn't on your PATH yet.
    echo Install it with:  winget install ffmpeg
    echo Then run this again.
    pause
    exit /b 1
)

set "EXE=%~dp0publish\ExtendedScreen.Sender.exe"
if not exist "%EXE%" (
    echo Couldn't find ExtendedScreen.Sender.exe next to this script.
    echo Build it first with:  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
    pause
    exit /b 1
)

echo Starting ExtendedScreen, mirroring your primary monitor...
echo (Close this window, or press Ctrl+C, to stop.)
echo.
"%EXE%" --source desktop --monitor 0 --fps 30 --bitrate 8000

pause
