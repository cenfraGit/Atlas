@echo off
rem double-click to open Atlas on itself, or drag any repo folder onto this file
setlocal
set "REPO=%~1"
rem no argument: Atlas opens on itself, with the path fully resolved
if "%REPO%"=="" for %%I in ("%~dp0.") do set "REPO=%%~fI"

if not exist "%REPO%" (
  echo Repo not found: %REPO%
  echo Drag a folder onto run.cmd to open that repo instead.
  pause
  exit /b 1
)

cd /d "%~dp0app"
dotnet run -- "%REPO%"
if errorlevel 1 pause
