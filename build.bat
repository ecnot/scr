@echo off
rem Builds Scr into the "dist" folder next to this file.
rem Needs the .NET 10 SDK:  winget install Microsoft.DotNet.SDK.10
setlocal
cd /d "%~dp0"

rem Close a running Scr first so its files can be replaced.
if exist dist\Scr.exe dist\Scr.exe --quit

dotnet publish src\Scr.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishReadyToRun=true -p:DebugType=none -o dist
if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)
echo.
echo Done: dist\Scr.exe
