@echo off
rem double-click to open Atlas on itself, or drag any repo folder onto this file
setlocal
set "REPO=%~1"
rem no argument: Atlas opens on itself, with the path fully resolved
if "%REPO%"=="" set "REPO=%~dp0."
rem made full before the cd below, so a relative path still means the same
rem folder, and a trailing backslash dropped: quoted, it would escape the quote
for %%I in ("%REPO%") do set "REPO=%%~fI"
if "%REPO:~-1%"=="\" set "REPO=%REPO:~0,-1%"

if not exist "%REPO%" (
  echo Repo not found: %REPO%
  echo Drag a folder onto run.cmd to open that repo instead.
  pause
  exit /b 1
)

cd /d "%~dp0app"
dotnet run -- "%REPO%"
if errorlevel 1 pause
